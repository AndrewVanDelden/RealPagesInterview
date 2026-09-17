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
# A trailing separator is trimmed: tab completion adds one, and before a closing quote on the command
# line it escapes the quote, so python would receive the folder name with a quote on the end.
$runFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RunDir).TrimEnd('\', '/')
$python = Join-Path $repositoryRoot '.venv\Scripts\python.exe'

# Checked before the run, so a paid run never ends on a report that cannot be built: the venv is
# created when it is missing, and the requirements are installed whenever matplotlib cannot be
# imported, so an install that failed once is retried rather than skipped from then on.
if (-not (Test-Path $python)) {
    Write-Host 'Creating .venv for the report.'
    python -m venv (Join-Path $repositoryRoot '.venv')
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
# find_spec answers by exit code and writes nothing to stderr, which Windows PowerShell 5.1 would
# otherwise turn into a terminating error under $ErrorActionPreference = 'Stop'.
& $python -c "import importlib.util, sys; sys.exit(0 if importlib.util.find_spec('matplotlib') else 1)"
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Installing the report requirements into .venv.'
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
