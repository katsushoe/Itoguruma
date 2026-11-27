@echo off
setlocal
set "INSTALLER_SCRIPT=%~1"
set "PACKAGE_PATH=%~2"
set "APP_LANGUAGE=%~3"
set "INSTALL_DIRECTORY=%~4"
set "PREVIOUS_INSTALL_DIRECTORY=%~5"
if "%INSTALL_DIRECTORY%"=="" set "INSTALL_DIRECTORY=C:\Itoguruma"
if "%PREVIOUS_INSTALL_DIRECTORY%"=="" set "PREVIOUS_INSTALL_DIRECTORY=%INSTALL_DIRECTORY%"
rem A trailing backslash would escape the closing quote of the PowerShell argument.
if "%INSTALL_DIRECTORY:~-1%"=="\" set "INSTALL_DIRECTORY=%INSTALL_DIRECTORY:~0,-1%"
if "%PREVIOUS_INSTALL_DIRECTORY:~-1%"=="\" set "PREVIOUS_INSTALL_DIRECTORY=%PREVIOUS_INSTALL_DIRECTORY:~0,-1%"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%INSTALLER_SCRIPT%" -PackagePath "%PACKAGE_PATH%" -Language "%APP_LANGUAGE%" -InstallDirectory "%INSTALL_DIRECTORY%" -PreviousInstallDirectory "%PREVIOUS_INSTALL_DIRECTORY%" > "%LOCALAPPDATA%\Itoguruma-msi-install.log" 2>&1
exit /b %ERRORLEVEL%
