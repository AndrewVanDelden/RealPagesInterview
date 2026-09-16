# Runs the agent once on a JSONL file with every output on, builds the visual report from the files
# the run wrote, and opens it. The report is one self-contained HTML page, report.html, in the run
# folder beside out.json, eval.txt, eval.json, diag.json, review_queue.json and run.log.
#
#   .\run-report.ps1 -InputPath C:\path\to\cases.jsonl -RunDir runs\my-run
#   .\run-report.ps1 -InputPath holdout_12.jsonl -RunDir runs\smoke -Composer template
#
# -Composer openai (the default) writes messages with the model and grades them with the judge, both
# of which are paid calls; -Composer template runs offline with no judge and costs nothing. The first
# use creates .venv at the repository root and installs tools\run_report\requirements.txt into it.
# Exit code: the agent's own (0 success, 1 usage error, 2 partial failure), or the report's 1 when the
# page could not be built. A usage error stops before the report, since the run wrote no scorecard.

param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$RunDir,
    [ValidateSet('openai', 'template')][string]$Composer = 'openai'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$runFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RunDir)
$python = Join-Path $repositoryRoot '.venv\Scripts\python.exe'

if (-not (Test-Path $python)) {
    Write-Host 'Creating .venv for the report (first use only).'
    python -m venv (Join-Path $repositoryRoot '.venv')
    & $python -m pip install --quiet -r (Join-Path $repositoryRoot 'tools\run_report\requirements.txt')
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

$agentArguments = @(
    '--input', $InputPath,
    '--output', (Join-Path $runFolder 'out.json'),
    '--composer', $Composer,
    '--eval-report', (Join-Path $runFolder 'eval.txt'),
    '--eval-json', (Join-Path $runFolder 'eval.json'),
    '--diagnostics', (Join-Path $runFolder 'diag.json'),
    '--review-queue', (Join-Path $runFolder 'review_queue.json'),
    '--log-file', (Join-Path $runFolder 'run.log')
)
if ($Composer -eq 'openai') { $agentArguments += '--judge' }

dotnet run --project (Join-Path $repositoryRoot 'src\Agent.Cli') -- @agentArguments
$runExitCode = $LASTEXITCODE
if ($runExitCode -eq 1) { exit 1 }

Push-Location (Join-Path $repositoryRoot 'tools\run_report')
try {
    & $python -m run_report --run-dir $runFolder
    $reportExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($reportExitCode -ne 0) { exit $reportExitCode }

Invoke-Item (Join-Path $runFolder 'report.html')
exit $runExitCode
