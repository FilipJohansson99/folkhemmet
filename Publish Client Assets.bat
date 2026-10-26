@echo off
setlocal
title Folkhemmet - publish client assets
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-server.ps1" -PublishClientOnly -NonInteractive
if errorlevel 1 (
    echo.
    echo Publishing failed - read the messages above. Players will not receive an unpublished update.
    pause
    exit /b 1
)
echo.
echo Published. Players get the update when they next launch Folkhemmet.
pause
