@echo off
setlocal
title TexTrack ERP Server
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-TexTrack.ps1"
if errorlevel 1 (
    echo.
    echo TexTrack could not start. Read the error shown above.
    pause
)
endlocal
