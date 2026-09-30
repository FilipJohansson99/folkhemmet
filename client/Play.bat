@echo off
setlocal
title Folkhemmet
rem Double-click: updates the mods to match the server, then starts Valheim.
rem (A newer Play.bat downloaded last time is swapped in first.)
if exist "%~dp0Play.bat.new" (move /y "%~dp0Play.bat.new" "%~dp0Play.bat" >nul & "%~dp0Play.bat" %* & exit /b)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-Mods.ps1" -Launch %*
if errorlevel 1 (
    echo.
    echo Something went wrong - read the messages above. Nothing was started.
    pause
    exit /b 1
)
timeout /t 5 >nul
