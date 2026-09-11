# Mutation testing: restores the pinned Stryker.NET local tool, then mutates each source
# project against its own test project and reports how many changed lines a test noticed.
# Agent is mutated with Agent.Tests (stryker-config.json), Agent.Cli with Agent.Cli.Tests
# (stryker-config.cli.json). Stryker mutates one project under test per run and is run from
# the test project's directory, so there is one run per pair.
# Stryker's own MSBuild discovery prefers a Visual Studio install when one exists, and Visual
# Studio 2022's MSBuild resolves the .NET 9 SDK, which cannot build net10.0, so every project
# analysis fails. The MSBuild of the SDK that dotnet itself resolves here is passed instead.
# Reports land in StrykerOutput/<project>/; the console text is teed to mutation-output.txt.
# Windows PowerShell 5.1: ForEach-Object turns each stderr line into plain text (see test.ps1),
# and $LASTEXITCODE, not $?, carries each native command's result through the tee.
$ErrorActionPreference = 'Continue'
$outputFile = Join-Path $PSScriptRoot "mutation-output.txt"
if (Test-Path $outputFile) { Remove-Item $outputFile }

Push-Location $PSScriptRoot
dotnet tool restore 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $outputFile -Append
$restoreExitCode = $LASTEXITCODE
$sdkVersion = (dotnet --version).Trim()
Pop-Location
if ($restoreExitCode -ne 0) { exit $restoreExitCode }

$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$msbuildPath = Join-Path $dotnetRoot "sdk\$sdkVersion\MSBuild.dll"
"Using MSBuild at $msbuildPath" | Tee-Object -FilePath $outputFile -Append

$runs = @(
    @{ Name = "Agent"; TestProjectDirectory = "tests\Agent.Tests"; ConfigFile = "stryker-config.json" },
    @{ Name = "Agent.Cli"; TestProjectDirectory = "tests\Agent.Cli.Tests"; ConfigFile = "stryker-config.cli.json" }
)
$worstExitCode = 0
foreach ($run in $runs) {
    $configPath = Join-Path $PSScriptRoot $run.ConfigFile
    $reportPath = Join-Path $PSScriptRoot "StrykerOutput\$($run.Name)"
    $started = Get-Date
    Push-Location (Join-Path $PSScriptRoot $run.TestProjectDirectory)
    dotnet stryker --config-file $configPath --msbuild-path $msbuildPath --output $reportPath 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $outputFile -Append
    $exitCode = $LASTEXITCODE
    Pop-Location
    $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
    "Stryker run for $($run.Name) exited $exitCode after $minutes minutes" | Tee-Object -FilePath $outputFile -Append
    if ($exitCode -ne 0) { $worstExitCode = $exitCode }
}
exit $worstExitCode
