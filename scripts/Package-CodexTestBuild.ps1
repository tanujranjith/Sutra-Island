[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishedExe,
    [Parameter(Mandatory = $true)][string]$CodexExe,
    [Parameter(Mandatory = $true)][string]$CodeModeHostExe,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'),
    [string]$AppVersion,
    [string]$CodexVersion = '0.151.0',
    [string]$ExpectedCodexSha256 = 'cf68265897197ac5f3bff6a10c168eec159842b353129726da5e3ed6b91ef0f4',
    [string]$ExpectedCodeModeHostSha256 = '4ea17cf938023f2d0c292b6dbcd4d51e7fbdf72f3885cf341017a380a87e77dc'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    $projectPath = Join-Path $PSScriptRoot '..\DynamicIsland.Windows\DynamicIsland.Windows.csproj'
    [xml]$project = Get-Content -LiteralPath $projectPath
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw "App version was not supplied and could not be read from $projectPath."
    }
    $AppVersion = $versionNode.InnerText.Trim()
}

if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "App version '$AppVersion' must use MAJOR.MINOR.PATCH format."
}

$publishedExePath = (Resolve-Path -LiteralPath $PublishedExe).Path
$codexExePath = (Resolve-Path -LiteralPath $CodexExe).Path
$codeModeHostPath = (Resolve-Path -LiteralPath $CodeModeHostExe).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagePath = Join-Path $outputPath "DynamicIsland-Codex-Test-v$AppVersion-win-x64"
$zipPath = "$stagePath.zip"

function Assert-Sha256([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected.ToLowerInvariant()) {
        throw "SHA-256 verification failed for $([System.IO.Path]::GetFileName($Path))."
    }
}

Assert-Sha256 $codexExePath $ExpectedCodexSha256
Assert-Sha256 $codeModeHostPath $ExpectedCodeModeHostSha256

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
# Validate the resolved staging targets before replacing an earlier package.
$outputPrefix = $outputPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($target in @($stagePath, $zipPath)) {
    if (-not [System.IO.Path]::GetFullPath($target).StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package staging target must remain inside the output directory.'
    }
}
if (Test-Path -LiteralPath $stagePath) { Remove-Item -LiteralPath $stagePath -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$codexStage = New-Item -ItemType Directory -Force -Path (Join-Path $stagePath 'codex')

Copy-Item -LiteralPath $publishedExePath -Destination (Join-Path $stagePath 'DynamicIsland.exe')
Copy-Item -LiteralPath $codexExePath -Destination (Join-Path $codexStage.FullName 'codex.exe')
Copy-Item -LiteralPath $codeModeHostPath -Destination (Join-Path $codexStage.FullName 'codex-code-mode-host.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\CODEX_TEST_BUILD.md') -Destination $stagePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\THIRD_PARTY_NOTICES.md') -Destination $stagePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Q.md') -Destination $stagePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\README.md') -Destination $stagePath
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\RELEASE_NOTES.md') -Destination $stagePath

$runtimeFiles = @('codex.exe', 'codex-code-mode-host.exe') | ForEach-Object {
    $file = Join-Path $codexStage.FullName $_
    [ordered]@{ name = $_; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ version = $CodexVersion; files = $runtimeFiles } |
    ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $codexStage.FullName 'codex-runtime.json') -Encoding utf8

Compress-Archive -LiteralPath $stagePath -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"
