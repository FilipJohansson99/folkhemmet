@echo off
rem Removes the modpack from your Valheim folder (back to vanilla). Play.bat installs it again.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Mods.ps1" -Uninstall %*
echo.
pause
