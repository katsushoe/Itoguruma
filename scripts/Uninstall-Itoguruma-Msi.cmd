@echo off
setlocal
set "UNINSTALLER_SCRIPT=%~1"
set "INSTALL_DIRECTORY=%~2"
if "%INSTALL_DIRECTORY%"=="" set "INSTALL_DIRECTORY=C:\Itoguruma"
rem A trailing backslash would escape the closing quote of the PowerShell argument.
if "%INSTALL_DIRECTORY:~-1%"=="\" set "INSTALL_DIRECTORY=%INSTALL_DIRECTORY:~0,-1%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALLER_SCRIPT%" -InstallDirectory "%INSTALL_DIRECTORY%" > "%TEMP%\Itoguruma-msi-uninstall.log" 2>&1
exit /b %ERRORLEVEL%
