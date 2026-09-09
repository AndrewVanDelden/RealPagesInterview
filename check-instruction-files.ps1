# Asserts the two rules that keep the instruction files usable, so neither is left advisory
# (the pillars' meta-rule: a rule that must hold with zero exceptions belongs in tooling).
#
#   AGENTS.md is rules only, and stays under the playbook's line limit (steps 18 and 96).
#   docs/DECISION_LOG.md carries the phase state, and that section is replaced, not appended.
#
# Run it locally the same way CI does:  .\check-instruction-files.ps1
# Exits 0 when both hold, 1 with one line per failure when they do not.
[CmdletBinding()]
param(
    [string] $AgentsPath = 'AGENTS.md',
    [string] $DecisionLogPath = 'docs/DECISION_LOG.md',
    [int] $MaxAgentsLines = 150,
    [int] $MaxPhaseSectionLines = 20
)

$ErrorActionPreference = 'Stop'
$failures = @()

$agents = Get-Content -LiteralPath $AgentsPath
if ($agents | Where-Object { $_ -match '^##\s+Current phase' }) {
    $failures += "$AgentsPath carries a Current phase section. Phase state belongs at the top of $DecisionLogPath."
}
if ($agents.Count -gt $MaxAgentsLines) {
    $failures += "$AgentsPath is $($agents.Count) lines; the limit is $MaxAgentsLines (playbook steps 18 and 96)."
}

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

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "instruction-files: $failure" }
    exit 1
}

Write-Host "instruction-files: AGENTS.md is $($agents.Count) lines of rules; the phase state is in $DecisionLogPath."
