# Asserts the two rules that keep the instruction files usable, so neither is left advisory
# (the pillars' meta-rule: a rule that must hold with zero exceptions belongs in tooling).
#
#   AGENTS.md is rules only, and stays under the playbook's line limit (steps 18 and 96).
#   The decision log carries the phase state, and that section is replaced, not appended.
#
# Run it locally the same way CI does:  .\check-instruction-files.ps1
# Exits 0 when both hold, 1 with one line per failure when they do not.
[CmdletBinding()]
param(
    [string] $AgentsPath = 'AGENTS.md',
    # Both layouts the scaffold produces: a repo with a docs folder, and one without.
    [string] $DecisionLogPath,
    [int] $MaxAgentsLines = 150,
    [int] $MaxPhaseSectionLines = 20
)

$ErrorActionPreference = 'Stop'
$failures = @()

# Anchored to the script's own directory, not the caller's, so this runs the same whether
# invoked as .\check-instruction-files.ps1 from the repo root or by absolute path from
# anywhere else (an editor run button, a wrapper, a template stamped into another repo).
$AgentsPath = [System.IO.Path]::Combine($PSScriptRoot, $AgentsPath)

if (-not $DecisionLogPath) {
    $DecisionLogPath = @('docs/DECISION_LOG.md', 'DECISION_LOG.md') |
        ForEach-Object { [System.IO.Path]::Combine($PSScriptRoot, $_) } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
else {
    $DecisionLogPath = [System.IO.Path]::Combine($PSScriptRoot, $DecisionLogPath)
}

if (-not (Test-Path -LiteralPath $AgentsPath)) {
    $failures += "$AgentsPath not found. Run this from the repository root."
}
else {
    $agents = Get-Content -LiteralPath $AgentsPath
    if ($agents | Where-Object { $_ -match '^##\s+Current phase' }) {
        $failures += "$AgentsPath carries a Current phase section. Phase state belongs at the top of the decision log."
    }
    if ($agents.Count -gt $MaxAgentsLines) {
        $failures += "$AgentsPath is $($agents.Count) lines; the limit is $MaxAgentsLines (playbook steps 18 and 96)."
    }
}

if (-not $DecisionLogPath) {
    $failures += "No decision log found at docs/DECISION_LOG.md or DECISION_LOG.md. Phase First reads its Current phase section."
}
elseif (-not (Test-Path -LiteralPath $DecisionLogPath)) {
    $failures += "$DecisionLogPath not found."
}
else {
    $log = Get-Content -LiteralPath $DecisionLogPath
    $headings = 0..($log.Count - 1) | Where-Object { $log[$_] -match '^##\s' }
    $phaseStart = $headings | Where-Object { $log[$_] -match '^##\s+Current phase' } | Select-Object -First 1

    if ($null -eq $phaseStart) {
        $failures += "$DecisionLogPath has no Current phase section. Phase First reads it there."
    }
    else {
        $next = $headings | Where-Object { $_ -gt $phaseStart } | Select-Object -First 1
        $end = if ($null -ne $next) { $next - 1 } else { $log.Count - 1 }
        $length = $end - $phaseStart + 1
        if ($length -gt $MaxPhaseSectionLines) {
            $failures += "The Current phase section in $DecisionLogPath is $length lines; the limit is $MaxPhaseSectionLines. Replace it at the end of a sprint, never append: passed phases and their proofs belong in the paragraphs below."
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "instruction-files: $failure" }
    exit 1
}

Write-Host "instruction-files: $AgentsPath is $($agents.Count) lines of rules; the phase state is in $DecisionLogPath."
