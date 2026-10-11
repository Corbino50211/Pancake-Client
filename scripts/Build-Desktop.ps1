$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet run --project tests/Pancake.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    dotnet publish src/Pancake.Desktop -c Release -r win-x64 --self-contained true -o artifacts/PancakeClient-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    Write-Host 'Ready: artifacts/PancakeClient-win-x64/PancakeClient.exe'
} finally { Pop-Location }
