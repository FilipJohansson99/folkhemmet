@echo off
setlocal
title Folkhemmet - publish edited audio
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0custom-mod\tools\Import-EditedAudio.ps1" -Publish
if errorlevel 1 (
    echo.
    echo Import or publication failed. Read the message above.
    pause
    exit /b 1
)
pause
