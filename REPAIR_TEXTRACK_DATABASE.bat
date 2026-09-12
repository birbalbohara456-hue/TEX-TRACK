@echo off
setlocal
title TexTrack Database Login Repair
echo This repair preserves the existing TexTrack database and synchronizes its application login.
echo You will need the PostgreSQL administrator password chosen during PostgreSQL installation.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Setup-PostgreSql.ps1"
if errorlevel 1 (
    echo.
    echo TexTrack database repair failed. Read the error shown above.
)
echo.
pause
endlocal
