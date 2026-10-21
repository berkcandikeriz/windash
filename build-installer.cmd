@echo off
rem WinDash MSI paketini uretir (artifacts klasorune)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*
pause
