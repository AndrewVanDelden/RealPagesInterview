# Windows PowerShell 5.1: without ForEach-Object, 2>&1 wraps every stderr line from a native
# command in a NativeCommandError record and the tee file gets a six-line block per line.
# The pipeline ends in Tee-Object, so $? is always true; only $LASTEXITCODE carries dotnet's
# result, and the final exit hands it to the caller (CI step, hook, agent).
$ErrorActionPreference = 'Continue'
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=./coverage/ /p:Threshold=100 /p:ThresholdType=line%2cbranch%2cmethod /p:ThresholdStat=total /p:ExcludeByAttribute=CompilerGeneratedAttribute /p:ExcludeByFile="**/*.g.cs" --logger "console;verbosity=detailed" 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath "test-output.txt"
exit $LASTEXITCODE
