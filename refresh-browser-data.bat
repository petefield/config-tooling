@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

set "TOOLING_PROJECT=src\config-tooling\config-tooling.csproj"
set "SOURCE_CONFIGS=configs"
set "TOOLING_OUTPUT=src\config-tooling\root"
set "BROWSER_DATA=src\config-browser\wwwroot\data"

echo Generating config output...
dotnet run --project "%TOOLING_PROJECT%" -- "%SOURCE_CONFIGS%" "%TOOLING_OUTPUT%"
if errorlevel 1 goto :fail

echo Refreshing browser data folder...
if exist "%BROWSER_DATA%" rd /s /q "%BROWSER_DATA%"
mkdir "%BROWSER_DATA%"
if errorlevel 1 goto :fail

xcopy "%TOOLING_OUTPUT%\*" "%BROWSER_DATA%\" /e /i /y >nul
if errorlevel 1 goto :fail

echo Browser data refreshed in "%BROWSER_DATA%".
popd >nul
exit /b 0

:fail
set "EXIT_CODE=%errorlevel%"
echo Failed to refresh browser data.
popd >nul
exit /b %EXIT_CODE%
