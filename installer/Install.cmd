@echo off
setlocal
title TiboMonitor Installer

set "SOURCE=%~dp0app"
set "DEST=%LOCALAPPDATA%\Programs\TiboMonitor"

if not exist "%SOURCE%\TiboMonitor.exe" (
  echo ERROR: app\TiboMonitor.exe was not found.
  echo Extract the complete release ZIP before running Install.cmd.
  pause
  exit /b 1
)

echo Installing TiboMonitor to:
echo %DEST%
echo.

if /I not "%TIBO_INSTALL_NO_LAUNCH%"=="1" taskkill /IM TiboMonitor.exe /F >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"

robocopy "%SOURCE%" "%DEST%" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo ERROR: Failed to copy application files.
  pause
  exit /b 1
)

copy /Y "%~dp0Uninstall.cmd" "%DEST%\Uninstall.cmd" >nul
if /I "%TIBO_INSTALL_NO_LAUNCH%"=="1" goto install_complete
start "" "%DEST%\TiboMonitor.exe"

:install_complete
echo.
echo TiboMonitor was installed successfully.
if /I "%TIBO_INSTALL_NO_LAUNCH%"=="1" goto install_finish
echo TiboMonitor was started successfully.
echo You can find it in the Windows notification area.
:install_finish
if /I "%TIBO_INSTALL_QUIET%"=="1" exit /b 0
pause
exit /b 0
