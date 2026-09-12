@echo off
setlocal
title TexTrack One-Time Database Setup
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-TexTrack-Secrets.ps1"
if errorlevel 1 (
    echo.
    echo TexTrack setup failed. Read the error shown above.
)
echo.
pause
endlocal
