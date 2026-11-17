param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'))
$ErrorActionPreference = 'Stop'
$archive = Join-Path $OutputDirectory 'kokoro-int8.tar.bz2'
$model = Join-Path $OutputDirectory 'kokoro-int8-multi-lang-v1_0'
$expected = '72C347230107EF8A31098EC4D468A605244BEEF5F1ED00DB97EFA13518800653'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    Invoke-WebRequest 'https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Local voice model archive failed its SHA-256 check.' }
tar -xf $archive -C $OutputDirectory
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $model 'model.int8.onnx'))) { throw 'Could not extract the local voice model.' }
Write-Output "Voice model ready: $model"
