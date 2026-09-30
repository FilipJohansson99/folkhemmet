@echo off
rem HOST ONLY: puts Folkhemmet on GitHub (installs Git if needed, GitHub sign-in once).
rem Friends never run this - they use Install-Folkhemmet.bat from the Folkhemmet.zip link.
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Folkhemmet.ps1"
echo.
pause
