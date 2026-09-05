# Regenerates .agents/rules/project.md (Antigravity workspace rule) from AGENTS.md.
# Run after any edit to AGENTS.md. AGENTS.md stays the single source of truth.
# Frontmatter uses the template embedded in Antigravity's language server:
#   trigger: always_on  (only always_on rules are injected unconditionally)
$src = Get-Content -Raw -Encoding utf8 AGENTS.md
$hdr = @"
---
trigger: always_on
glob:
description: Project instructions for this repository. GENERATED COPY of AGENTS.md; edit AGENTS.md and run .\sync-agent-rules.ps1
---

"@
New-Item -ItemType Directory -Force .agents/rules | Out-Null
# Plain UTF-8 without BOM: Windows PowerShell 5.1 -Encoding utf8 emits a BOM, which breaks frontmatter parsing.
$text = ($hdr + $src) -replace "`r`n", "`n"   # LF only, matching Antigravity's own rule files
[System.IO.File]::WriteAllText((Join-Path (Get-Location) ".agents/rules/project.md"), $text, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Regenerated .agents/rules/project.md from AGENTS.md ($((Get-Item .agents/rules/project.md).Length) bytes)"
