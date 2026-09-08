param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe',
    [ValidateSet('EditMode', 'PlayMode', 'All')][string]$Mode = 'All',
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$resultsRoot = Join-Path $projectRoot 'TestResults'
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity executable not found: $UnityPath" }
if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw 'Close Unity before batch tests, or use Window > General > Test Runner in the open Editor.'
}
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
$modes = if ($Mode -eq 'All') { @('EditMode', 'PlayMode') } else { @($Mode) }
foreach ($testMode in $modes) {
    $runId = [Guid]::NewGuid().ToString('N')
    $xmlPath = Join-Path $resultsRoot "$testMode-$runId.xml"
    $logPath = Join-Path $resultsRoot "$testMode-$runId.log"
    $assembly = "IterationRoom.${testMode}Tests"
    # No -quit: the test runner exits after writing results. These tests do not render or bake.
    $arguments = "-batchmode -nographics -projectPath `"$projectRoot`" -runTests -testPlatform $testMode -assemblyNames $assembly -testResults `"$xmlPath`" -logFile `"$logPath`""
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle # Keep the native handle so ExitCode remains readable after exit.
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            throw "Unity timed out during $testMode. Check license and compilation logs: $logPath"
        }
        if (-not (Test-Path -LiteralPath $xmlPath)) {
            throw "Unity produced no $testMode test results (exit $($process.ExitCode)). Log: $logPath"
        }
        [xml]$report = Get-Content -LiteralPath $xmlPath -Raw
        $run = $report.'test-run'
        if ($process.ExitCode -ne 0 -or [int]$run.total -eq 0 -or
            [int]$run.skipped -gt 0 -or [int]$run.failed -gt 0 -or
            [int]$run.passed -ne [int]$run.total -or $run.result -ne 'Passed') {
            throw "$testMode failed, skipped, or discovered no tests. Results: $xmlPath; log: $logPath"
        }
        Write-Output "$testMode passed: $($run.passed) tests. Results: $xmlPath"
    }
    finally { $process.Dispose() }
}
