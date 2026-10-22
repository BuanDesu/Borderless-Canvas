@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch.ps1" -Mode Probe
set "bfw_exit=%errorlevel%"
if not "%bfw_exit%"=="0" pause
exit /b %bfw_exit%
