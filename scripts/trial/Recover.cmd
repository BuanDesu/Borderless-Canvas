@echo off
cd /d "%~dp0"
"%~dp0BorderlessFlatWindow.Recovery.exe"
echo.
echo Recovery exit code: %errorlevel% (0 complete, 2 pending, 1 error)
pause
