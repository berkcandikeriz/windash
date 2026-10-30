@echo off
rem WinDash MSI paketini uretir ve tum ciktiyi build.log dosyasina yazar (pencere beklemeden kapanir)
cd /d "%~dp0"
echo [%date% %time%] Derleme basladi> "%~dp0build.log"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %* >> "%~dp0build.log" 2>&1
echo EXITCODE=%ERRORLEVEL%>> "%~dp0build.log"
echo [%date% %time%] Bitti>> "%~dp0build.log"
