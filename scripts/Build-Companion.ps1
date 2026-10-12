param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process BONELAB,BONELAB_Steam_Windows64 -ErrorAction SilentlyContinue) { throw 'Close BONELAB before building/installing the companion.' }
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build src/Pancake.Companion -c Release "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw 'Companion build failed. See docs/SETUP.md for references and runtime requirements.' }
    Write-Host 'Built: src/Pancake.Companion/bin/Release/net6.0/Pancake.Companion.dll'
    Write-Host 'Install that DLL using Pancake Client > Settings > Install companion DLL.'
} finally { Pop-Location }
