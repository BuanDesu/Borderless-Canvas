@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch.ps1" -Mode Recover
set "bfw_exit=%errorlevel%"
echo.
echo Recovery exit code: %bfw_exit% (0 complete, 2 pending, 1 error)
pause
exit /b %bfw_exit%
