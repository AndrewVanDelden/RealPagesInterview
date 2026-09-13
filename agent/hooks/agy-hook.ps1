# Antigravity adapter. The generated .agents/hooks.json routes PreToolUse for the file-writing
# tools and run_command here. Antigravity's contract differs from Claude Code's in every field:
# the tool is toolCall.name, its arguments toolCall.args, and the verdict is JSON on stdout,
# {"decision":"deny","reason":"..."}; anything else allows. Current public IDE builds do not
# run hooks (verified 2026-08: the agy CLI only), so this file is early feedback for the CLI,
# and the pre-commit hook and CI remain the floor. Any failure of the adapter itself prints
# allow and exits 0: on Windows a hook that exited 1 took every tool down (reported 2026-09-04).
#
# The argument names (CodeContent, ReplacementContent, ReplacementChunks, TargetFile,
# CommandLine, Cwd) are the tool schemas as published; the hook payload has not been observed
# on this machine. A missing field allows, it never blocks.
$ErrorActionPreference = 'Continue'

function Get-Field([object] $Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Allow-Call {
    Write-Output '{"decision":"allow"}'
    exit 0
}

function Deny-Call([string] $Reason) {
    $escaped = $Reason.Replace('\', '\\').Replace('"', '\"').Replace("`r", '').Replace("`n", ' ')
    Write-Output ('{"decision":"deny","reason":"' + $escaped + '"}')
    exit 0
}

try {
    $reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
    $raw = $reader.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { Allow-Call }

    $hook = $raw | ConvertFrom-Json
    $call = Get-Field $hook 'toolCall'
    if ($null -eq $call) { Allow-Call }
    $toolName = [string](Get-Field $call 'name')
    $toolArgs = Get-Field $call 'args'
    $here = $PSScriptRoot

    $texts = @()
    switch ($toolName) {
        'write_to_file'              { $texts += [string](Get-Field $toolArgs 'CodeContent') }
        'replace_file_content'       { $texts += [string](Get-Field $toolArgs 'ReplacementContent') }
        'multi_replace_file_content' { foreach ($chunk in @(Get-Field $toolArgs 'ReplacementChunks')) { $texts += [string](Get-Field $chunk 'ReplacementContent') } }
    }
    if ($texts.Count -gt 0) {
        $label = [string](Get-Field $toolArgs 'TargetFile')
        foreach ($text in $texts) {
            $output = & (Join-Path $here 'check-em-dash.ps1') -Text $text -Label $label
            if ($LASTEXITCODE -ne 0) { Deny-Call (($output | Out-String).Trim()) }
        }
        Allow-Call
    }

    if ($toolName -eq 'run_command') {
        $command = [string](Get-Field $toolArgs 'CommandLine')
        if ($command -match '\bgit\b[^|;&\r\n]*\bcommit\b') {
            $cwd = [string](Get-Field $toolArgs 'Cwd')
            if (-not $cwd) { $cwd = [string](@(Get-Field $hook 'workspacePaths'))[0] }
            if ($cwd) {
                $output = & (Join-Path $here 'check-branch-not-main.ps1') -RepoPath $cwd
                if ($LASTEXITCODE -ne 0) { Deny-Call (($output | Out-String).Trim()) }
            }
        }
        Allow-Call
    }

    Allow-Call
}
catch {
    Allow-Call
}
