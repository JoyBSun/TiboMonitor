$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot 'src\TiboMonitor.App\TiboMonitor.App.csproj'
$outputRoot = Join-Path $projectRoot 'src\TiboMonitor.App\bin\Debug\net8.0-windows\win-x64'

dotnet build $appProject --configuration Debug
Copy-Item -LiteralPath (Join-Path $projectRoot 'config\config.mock.json') -Destination (Join-Path $outputRoot 'config.json') -Force

$env:TIBO_DATA_ROOT = Join-Path $env:TEMP 'TiboMonitor-Mock'
Start-Process -FilePath (Join-Path $outputRoot 'TiboMonitor.exe')

Write-Host "Mock app started. Test data: $env:TIBO_DATA_ROOT"
