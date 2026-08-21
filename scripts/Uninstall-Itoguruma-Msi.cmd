@echo off
setlocal
set "UNINSTALLER_SCRIPT=%~1"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALLER_SCRIPT%" > "%TEMP%\Itoguruma-msi-uninstall.log" 2>&1
exit /b %ERRORLEVEL%
