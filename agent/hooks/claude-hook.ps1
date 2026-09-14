# Claude Code adapter. The generated .claude/settings.json routes PreToolUse (Edit, Write, Bash)
# and PostToolUse (Edit, Write) here. It reads Claude Code's hook JSON from stdin, hands the
# relevant field to the tool-neutral check beside it, and answers in Claude Code's contract:
# exit 2 with the reason on stderr blocks the call on PreToolUse and returns the reason as
# feedback on PostToolUse. Any failure of the adapter itself exits 0: a broken hook must never
# take the tools down, and the pre-commit hook and CI are the floor for every rule wired here.
#
#   Edit, Write (PreToolUse)   the new text carries no em dash          check-em-dash.ps1
#   Bash (PreToolUse)          git commit does not run on main          check-branch-not-main.ps1
#   Edit, Write (PostToolUse)  AGENTS.md and the decision log in shape  check-instruction-files.ps1
$ErrorActionPreference = 'Continue'

function Get-Field([object] $Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Deny-Call([string] $Reason) {
    [Console]::Error.WriteLine($Reason)
    exit 2
}

try {
    $reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
    $raw = $reader.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $hook = $raw | ConvertFrom-Json
    $eventName = [string](Get-Field $hook 'hook_event_name')
    $toolName = [string](Get-Field $hook 'tool_name')
    $toolInput = Get-Field $hook 'tool_input'
    $cwd = [string](Get-Field $hook 'cwd')
    $projectRoot = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { $cwd }
    $here = $PSScriptRoot

    if ($eventName -eq 'PreToolUse' -and ($toolName -eq 'Edit' -or $toolName -eq 'Write')) {
        $text = if ($toolName -eq 'Edit') { [string](Get-Field $toolInput 'new_string') } else { [string](Get-Field $toolInput 'content') }
        $label = [string](Get-Field $toolInput 'file_path')
        $output = & (Join-Path $here 'check-em-dash.ps1') -Text $text -Label $label
        if ($LASTEXITCODE -ne 0) { Deny-Call (($output | Out-String).Trim()) }
        exit 0
    }

    if ($eventName -eq 'PreToolUse' -and $toolName -eq 'Bash') {
        $command = [string](Get-Field $toolInput 'command')
        if ($command -match '\bgit\b[^|;&\r\n]*\bcommit\b') {
            $output = & (Join-Path $here 'check-branch-not-main.ps1') -RepoPath $cwd
            if ($LASTEXITCODE -ne 0) { Deny-Call (($output | Out-String).Trim()) }
        }
        exit 0
    }

    if ($eventName -eq 'PostToolUse' -and ($toolName -eq 'Edit' -or $toolName -eq 'Write')) {
        $path = [string](Get-Field $toolInput 'file_path')
        if ($path -match '(^|[\\/])(AGENTS\.md|DECISION_LOG\.md|DECISIONS_ARCHIVE\.md)$') {
            $check = Join-Path $projectRoot 'check-instruction-files.ps1'
            if (Test-Path -LiteralPath $check) {
                # The check prints its findings with Write-Host, which reaches the console, not
                # the pipeline, so the exit code is the signal and the lines are re-read here.
                $lines = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $check 2>&1
                if ($LASTEXITCODE -ne 0) { Deny-Call (($lines | Out-String).Trim()) }
            }
        }
        exit 0
    }

    exit 0
}
catch {
    [Console]::Error.WriteLine("claude-hook: adapter error, allowing the call: $($_.Exception.Message)")
    exit 0
}
