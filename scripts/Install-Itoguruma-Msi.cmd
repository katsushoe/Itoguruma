@echo off
setlocal
set "INSTALLER_SCRIPT=%~1"
set "PACKAGE_PATH=%~2"
set "APP_LANGUAGE=%~3"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%INSTALLER_SCRIPT%" -PackagePath "%PACKAGE_PATH%" -Language "%APP_LANGUAGE%" > "%TEMP%\Itoguruma-msi-install.log" 2>&1
exit /b %ERRORLEVEL%
