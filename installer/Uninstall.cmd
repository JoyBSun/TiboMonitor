@echo off
setlocal
title TiboMonitor Uninstaller

set "DEST=%LOCALAPPDATA%\Programs\TiboMonitor"

echo This will remove TiboMonitor and its local UserData.
choice /C YN /M "Continue"
if errorlevel 2 exit /b 0

taskkill /IM TiboMonitor.exe /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v TiboMonitor /f >nul 2>&1

cd /d "%TEMP%"
start "" /b powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '%DEST%' -Recurse -Force -ErrorAction SilentlyContinue"

echo TiboMonitor uninstall has been scheduled.
exit /b 0
