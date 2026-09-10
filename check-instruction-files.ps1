# Asserts the two rules that keep the instruction files usable, so neither is left advisory
# (the pillars' meta-rule: a rule that must hold with zero exceptions belongs in tooling).
#
#   AGENTS.md is rules only, and stays under the playbook's line limit (steps 18 and 96).
#   The decision log carries the phase state, and that section is replaced, not appended.
#   The decision log stays under the playbook's word cap (step 92); the full paragraphs live
#   in the decisions archive beside it.
#   Every D-number and S-number cited anywhere in the repo resolves to a decision paragraph in
#   the decisions archive, since a citation is plain text a reader resolves by searching for
#   its bold heading (D68).
#
# Run it locally the same way CI does:  .\check-instruction-files.ps1
# Exits 0 when all four hold, 1 with one line per failure when they do not.
[CmdletBinding()]
param(
    [string] $AgentsPath = 'AGENTS.md',
    # Both layouts the scaffold produces: a repo with a docs folder, and one without.
    [string] $DecisionLogPath,
    [int] $MaxAgentsLines = 150,
    [int] $MaxPhaseSectionLines = 20,
    [int] $MaxDecisionLogWords = 2000
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

    # A word is a run of non-whitespace, which is what wc -w counts.
    $words = @((Get-Content -Raw -LiteralPath $DecisionLogPath) -split '\s+' | Where-Object { $_ -ne '' }).Count
    if ($words -gt $MaxDecisionLogWords) {
        $failures += "$DecisionLogPath is $words words; the limit is $MaxDecisionLogWords (playbook step 92). The full paragraphs belong in the decisions archive; this file keeps the phase and one paragraph per sprint (D68)."
    }

    # Citations are plain text, never links, so one resolves by searching the decisions archive
    # for its bold heading. Definitions are read from the archive alone, because after the trim
    # every decision paragraph is there and this log's sprint paragraphs open on a range of
    # numbers (`**D1 to D31,`) that they name but do not define. A decision paragraph opens
    # `**D<n>.` or `**S<n>.`, with `**D67 (open).` the one variant; an addendum heading
    # (`**D3 addendum,`) amends a paragraph rather than defining one. A cited number with no
    # such paragraph is a citation to nothing (D68).
    $archivePath = @('docs/DECISIONS_ARCHIVE.md', 'DECISIONS_ARCHIVE.md') |
        ForEach-Object { [System.IO.Path]::Combine($PSScriptRoot, $_) } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if (-not $archivePath) {
        $failures += "No decisions archive found at docs/DECISIONS_ARCHIVE.md or DECISIONS_ARCHIVE.md. Every decision paragraph lives there, so nothing a citation names could be resolved (D68)."
    }
    else {
        $scanRoots = @('src', 'tests', 'docs', 'README.md', 'AGENTS.md') |
            Where-Object { Test-Path -LiteralPath ([System.IO.Path]::Combine($PSScriptRoot, $_)) }
        $tracked = & git -C $PSScriptRoot ls-files -- $scanRoots
        if ($LASTEXITCODE -ne 0) {
            $failures += "git ls-files failed in $PSScriptRoot, so no citation was checked. A check that did not run is not a check that passed."
        }
        else {
            # The archive is both where citations resolve and a file that cites numbers itself,
            # so it is scanned whether or not it is tracked yet.
            $citingFiles = @(@($tracked |
                ForEach-Object { [System.IO.Path]::Combine($PSScriptRoot, $_) }) + $archivePath |
                Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)
            $cited = @(Select-String -LiteralPath $citingFiles -Pattern '\b[DS][0-9]+\b' -AllMatches |
                ForEach-Object { $_.Matches.Value } | Sort-Object -Unique)
            $defined = @(Select-String -LiteralPath $archivePath -Pattern '^\*\*([DS][0-9]+)(?:\s\(open\))?\.' |
                ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
            foreach ($number in @($cited | Where-Object { $defined -notcontains $_ })) {
                $first = Select-String -LiteralPath $citingFiles -Pattern "\b$number\b" -List | Select-Object -First 1
                $failures += "$number is cited (first at $($first.Path):$($first.LineNumber)) and no decision paragraph in $archivePath defines it."
            }
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "instruction-files: $failure" }
    exit 1
}

Write-Host "instruction-files: $AgentsPath is $($agents.Count) lines of rules; the phase state is in $DecisionLogPath, which is $words words of $MaxDecisionLogWords; $($cited.Count) cited decision numbers all resolve to paragraphs among the $($defined.Count) in $archivePath."
