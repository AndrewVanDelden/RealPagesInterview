# The floor under every rule the agent hooks enforce: git runs it whatever tool wrote the
# change, and CI runs it on the pushed range. .githooks/pre-commit is the shim git calls;
# sync-agent-config.ps1 points core.hooksPath at that folder.
#
#   .\agent\hooks\pre-commit.ps1                       staged changes (the git hook)
#   .\agent\hooks\pre-commit.ps1 -Range origin/dev...HEAD   a commit range (CI)
#
# Checks: not on main (staged mode only; CI runs on main by design), no em dash on any added
# line, and the instruction files keep their shape (check-instruction-files.ps1 at the root).
# Exits 1 with one line per failure, 0 when all hold. Complexity: one pass over the diff,
# O(added lines); the instruction-file check is O(tracked docs) and runs once.
[CmdletBinding()]
param([string] $Range)
$ErrorActionPreference = 'Continue'
# Windows PowerShell 5.1 decodes native output with the console code page; git emits UTF-8,
# and an em dash read through the wrong code page is three other characters.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$here = $PSScriptRoot
$root = ([string](& git rev-parse --show-toplevel)).Trim()
$failures = @()

if (-not $Range) {
    $output = & (Join-Path $here 'check-branch-not-main.ps1') -RepoPath $root
    if ($LASTEXITCODE -ne 0) { $failures += @($output) }
}

$diffArgs = @('-C', $root, 'diff', '-U0', '--diff-filter=ACMR', '--no-color')
if ($Range) { $diffArgs += $Range } else { $diffArgs += '--cached' }
$diff = & git @diffArgs
$file = ''
$lineNumber = 0
foreach ($line in @($diff)) {
    if ($line.StartsWith('+++ b/')) { $file = $line.Substring(6); continue }
    if ($line.StartsWith('@@')) {
        # @@ -a,b +c,d @@: the added side starts at line c.
        if ($line -match '\+(\d+)') { $lineNumber = [int]$Matches[1] }
        continue
    }
    if ($line.StartsWith('+') -and -not $line.StartsWith('+++')) {
        $output = & (Join-Path $here 'check-em-dash.ps1') -Text $line.Substring(1) -Label $file -FirstLine $lineNumber
        if ($LASTEXITCODE -ne 0) { $failures += @($output) }
        $lineNumber++
    }
}

$instructionCheck = Join-Path $root 'check-instruction-files.ps1'
if (Test-Path -LiteralPath $instructionCheck) {
    & $instructionCheck
    if ($LASTEXITCODE -ne 0) { $failures += 'check-instruction-files.ps1 failed; its lines are above.' }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "pre-commit: $failure" }
    Write-Host 'pre-commit: refused. The same checks run in the agent hooks and in CI.'
    exit 1
}
exit 0
