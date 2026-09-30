@echo off
rem Double-click to update mods and start the Valheim server. Stop the server with Ctrl+C.
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-server.ps1" %*
echo.
pause
