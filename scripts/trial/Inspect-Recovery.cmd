@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch.ps1" -Mode Inspect
set "bfw_exit=%errorlevel%"
echo.
pause
exit /b %bfw_exit%
