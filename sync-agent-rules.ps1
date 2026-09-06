# Regenerates .agents/rules/project.md (Antigravity workspace rule) from AGENTS.md.
# Run after any edit to AGENTS.md. AGENTS.md stays the single source of truth.
# Frontmatter uses the template embedded in Antigravity's language server:
#   trigger: always_on  (only always_on rules are injected unconditionally)
#
# All paths are anchored to this script's directory (the repo root), not the caller's
# current directory, and any error aborts before anything is written.
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$srcPath  = Join-Path $repoRoot 'AGENTS.md'
$outDir   = Join-Path (Join-Path $repoRoot '.agents') 'rules'   # nested: a literal backslash is not a separator on pwsh for Linux/macOS
$outPath  = Join-Path $outDir 'project.md'

if (-not (Test-Path -LiteralPath $srcPath)) { throw "Source not found: $srcPath" }
$src = Get-Content -Raw -Encoding utf8 -LiteralPath $srcPath
if ([string]::IsNullOrWhiteSpace($src)) { throw "Source is empty: $srcPath" }

$hdr = @"
---
trigger: always_on
glob:
description: Project instructions for this repository. GENERATED COPY of AGENTS.md; edit AGENTS.md and run .\sync-agent-rules.ps1
---

"@

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
# LF only and UTF-8 without BOM, matching Antigravity's own rule files. Windows PowerShell 5.1's
# -Encoding utf8 emits a BOM, which breaks frontmatter parsing.
$text = ($hdr + $src) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($outPath, $text, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Regenerated $outPath from $srcPath ($((Get-Item -LiteralPath $outPath).Length) bytes)"
