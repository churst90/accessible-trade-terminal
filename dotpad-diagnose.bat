@echo off
REM Runs dotpad-diagnose.ps1 in this directory and writes the full output to
REM dotpad-diagnose.log alongside the script. No admin needed.

cd /d "%~dp0"

set "LOGFILE=%~dp0dotpad-diagnose.log"

echo Running Dot Pad device diagnostics…
echo Output will be written to: %LOGFILE%
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dotpad-diagnose.ps1" > "%LOGFILE%" 2>&1

if errorlevel 1 (
    echo.
    echo PowerShell exited with an error. Check %LOGFILE% for details.
) else (
    echo.
    echo Done. Paste the contents of dotpad-diagnose.log back to Claude.
)

echo.
pause
