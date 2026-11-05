@echo off
rem AI code detector for Windows. Usage: run.cmd MR-URL, run.cmd web, run.cmd batch mrs.txt
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
exit /b %ERRORLEVEL%
