# Regenerates every per-tool agent file from the tool-neutral source under agent/, then runs
# sync-agent-rules.ps1, so one command refreshes everything an agent loads from this repo.
#
#   agent/skills/<name>/  ->  .claude/skills/<name>/ and .agents/skills/<name>/   copied unchanged;
#                             both tools read the Agent Skills spec, only the folder differs
#   agent/agents/*.md     ->  .claude/agents/*.md                                  Claude Code only;
#                             Antigravity documents no subagent file
#   agent/hooks/*.ps1     ->  .claude/settings.json (hooks block) and .agents/hooks.json
#                             each tool's wiring around the same scripts
#   .githooks/pre-commit  ->  core.hooksPath                                       the floor: git
#                             runs it whatever tool wrote the change
#
# Never edit a generated copy; edit agent/ and rerun. .claude/settings.json is generated whole,
# so personal Claude Code settings go in .claude/settings.local.json, which this script never
# touches. Writes UTF-8 without BOM and LF, like sync-agent-rules.ps1.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Lf([string] $Path, [string] $Text) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [System.IO.File]::WriteAllText($Path, ($Text -replace "`r`n", "`n"), $utf8)
}

function Copy-Tree([string] $From, [string] $To) {
    if (-not (Test-Path -LiteralPath $From)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $From -Recurse -File) {
        $relative = $file.FullName.Substring($From.Length).TrimStart('\', '/')
        Write-Lf (Join-Path $To $relative) (Get-Content -Raw -Encoding utf8 -LiteralPath $file.FullName)
        Write-Host "sync $relative -> $To"
    }
}

Copy-Tree (Join-Path $root 'agent\skills') (Join-Path $root '.claude\skills')
Copy-Tree (Join-Path $root 'agent\skills') (Join-Path $root '.agents\skills')
Copy-Tree (Join-Path $root 'agent\agents') (Join-Path $root '.claude\agents')

# Claude Code runs the command through bash (Git Bash on Windows), which expands the project
# root; powershell.exe then runs the adapter with the hook JSON on stdin.
$claudeHook = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"${CLAUDE_PROJECT_DIR}/agent/hooks/claude-hook.ps1\"'
$claudeSettings = @"
{
  "`$schema": "https://json.schemastore.org/claude-code-settings.json",
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Edit|Write|Bash",
        "hooks": [ { "type": "command", "command": "$claudeHook", "timeout": 30 } ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "hooks": [ { "type": "command", "command": "$claudeHook", "timeout": 60 } ]
      }
    ]
  }
}
"@
Write-Lf (Join-Path $root '.claude\settings.json') $claudeSettings
Write-Host 'sync .claude/settings.json'

$agyHook = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File agent/hooks/agy-hook.ps1'
$agyHooks = @"
{
  "agent-config": {
    "PreToolUse": [
      {
        "matcher": "write_to_file|replace_file_content|multi_replace_file_content|run_command",
        "hooks": [ { "type": "command", "command": "$agyHook", "timeout": 30 } ]
      }
    ]
  }
}
"@
Write-Lf (Join-Path $root '.agents\hooks.json') $agyHooks
Write-Host 'sync .agents/hooks.json'

& git -C $root config core.hooksPath .githooks
Write-Host 'sync core.hooksPath = .githooks'

& (Join-Path $root 'sync-agent-rules.ps1')
