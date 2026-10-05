@echo off
rem ValCraft installer (see install.ps1). Close Valheim first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause
