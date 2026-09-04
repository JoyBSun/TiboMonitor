$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\TiboMonitor.App\TiboMonitor.App.csproj'
$destination = Join-Path $projectRoot 'dist'

dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $destination
Write-Host "Publish complete: $destination\TiboMonitor.exe"
