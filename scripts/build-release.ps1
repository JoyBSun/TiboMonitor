param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.1.2'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $projectRoot "release\v$Version"
$packageRoot = Join-Path $releaseRoot 'package'
$appRoot = Join-Path $packageRoot 'app'
$zipPath = Join-Path $releaseRoot "TiboMonitor-win-x64-v$Version.zip"
$hashPath = "$zipPath.sha256"

$expectedReleasePrefix = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'release')) + [System.IO.Path]::DirectorySeparatorChar
$resolvedReleaseRoot = [System.IO.Path]::GetFullPath($releaseRoot)
if (-not $resolvedReleaseRoot.StartsWith($expectedReleasePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected release path: $resolvedReleaseRoot"
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $appRoot -Force | Out-Null

dotnet restore (Join-Path $projectRoot 'TiboMonitor.sln')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $projectRoot 'TiboMonitor.sln') --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project (Join-Path $projectRoot 'tests\TiboMonitor.Tests\TiboMonitor.Tests.csproj') --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $projectRoot 'src\TiboMonitor.App\TiboMonitor.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $appRoot `
    --no-restore `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\Install.cmd') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\Uninstall.cmd') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\README.txt') -Destination $packageRoot

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

$verifyRoot = Join-Path $releaseRoot 'verify'
Expand-Archive -LiteralPath $zipPath -DestinationPath $verifyRoot
$requiredFiles = @(
    'Install.cmd',
    'Uninstall.cmd',
    'README.txt',
    'app\TiboMonitor.exe',
    'app\config.json',
    'app\D3DCompiler_47_cor3.dll',
    'app\PenImc_cor3.dll',
    'app\PresentationNative_cor3.dll',
    'app\vcruntime140_cor3.dll',
    'app\wpfgfx_cor3.dll'
)
$missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $verifyRoot $_)) })
$forbiddenFiles = @(Get-ChildItem -LiteralPath $verifyRoot -Recurse -Force | Where-Object {
    $_.FullName -match '\\(UserData|bin|obj|Source|\.git)(\\|$)'
})
if ($missingFiles.Count -gt 0 -or $forbiddenFiles.Count -gt 0) {
    throw "Release validation failed. Missing=$($missingFiles -join ',') Forbidden=$($forbiddenFiles.FullName -join ',')"
}
Remove-Item -LiteralPath $verifyRoot -Recurse -Force

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$hashLine = "$hash  $([System.IO.Path]::GetFileName($zipPath))"
[System.IO.File]::WriteAllText($hashPath, $hashLine + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Remove-Item -LiteralPath $packageRoot -Recurse -Force

Write-Host "Release package: $zipPath"
Write-Host "SHA256: $hash"
