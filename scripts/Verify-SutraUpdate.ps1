param([switch]$SkipBuild, [string]$Executable, [switch]$CommandPanelsOnly, [switch]$ComposerOnly, [switch]$MascotOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetPath = Join-Path $projectRoot '.dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet).Source }
Push-Location $projectRoot
$priorPreview = $env:ISLAND_PREVIEW
$priorData = $env:ISLAND_PREVIEW_DATA
$priorExtraction = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    if (!$SkipBuild) {
        & $dotnetPath test 'DynamicIsland.Windows.Tests/DynamicIsland.Windows.Tests.csproj' -c Release -p:NuGetAudit=false --results-directory 'artifacts/test-results' --logger 'trx;LogFileName=sutra-tests.trx'
        if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
        & $dotnetPath build 'DynamicIsland.Windows/DynamicIsland.Windows.csproj' -c Release -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw 'WPF build failed.' }
    }
    if (!$Executable) { $Executable = Join-Path $projectRoot 'DynamicIsland.Windows/bin/Release/net10.0-windows10.0.19041.0/win-x64/DynamicIsland.Windows.exe' }
    $Executable = [IO.Path]::GetFullPath($Executable)
    $env:ISLAND_PREVIEW = '1'
    $env:ISLAND_PREVIEW_DATA = Join-Path $projectRoot ('artifacts/sutra-verification-' + [guid]::NewGuid().ToString('N'))
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $projectRoot 'artifacts/bundle-cache'
    $verificationArgument = if ($MascotOnly) { '--verify-mascot-bulb' } elseif ($ComposerOnly) { '--verify-composer-keys' } elseif ($CommandPanelsOnly) { '--verify-command-panels' } else { '--verify-sutra-update' }
    $process = Start-Process -FilePath $Executable -ArgumentList $verificationArgument -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($watch.Elapsed.TotalMinutes -gt 3) { Stop-Process -Id $process.Id; throw 'Native verification timed out after three minutes.' }
    }
    $reportPath = Join-Path $env:ISLAND_PREVIEW_DATA 'captures/sutra-checks.txt'
    if ($process.ExitCode -ne 0) {
        Get-Content (Join-Path $env:ISLAND_PREVIEW_DATA 'verification-error.txt') -ErrorAction SilentlyContinue
        throw "Native verification failed. Report: $reportPath"
    }
    if ($CommandPanelsOnly -and !(Select-String -LiteralPath $reportPath -Pattern '^PASS Command panels have no WPF binding errors$' -Quiet)) {
        throw "Native verification exited before its final completion check. Report: $reportPath"
    }
    if ($ComposerOnly -and !(Select-String -LiteralPath $reportPath -Pattern '^PASS Composer keyboard verification completed without WPF binding errors$' -Quiet)) {
        throw "Composer verification exited before completion. Report: $reportPath"
    }
    if ($MascotOnly -and !(Select-String -LiteralPath $reportPath -Pattern '^PASS Mascot corner bulb verification completed without WPF binding errors$' -Quiet)) {
        throw "Mascot bulb verification exited before completion. Report: $reportPath"
    }
    Get-Content $reportPath | Where-Object { $_ -match 'METRIC|FAIL' }
    Write-Output "Native checks passed. Report: $reportPath"
} finally {
    $env:ISLAND_PREVIEW = $priorPreview
    $env:ISLAND_PREVIEW_DATA = $priorData
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $priorExtraction
    Pop-Location
}
