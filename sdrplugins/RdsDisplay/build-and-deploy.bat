@echo off
echo Building RDS Display plugin...
dotnet build "%~dp0SDRSharp.RdsDisplay.csproj" --configuration Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Check errors above.
    pause
    exit /b 1
)

echo.
echo Copying to SDRSharp Plugins folder...
copy /y "%~dp0..\Release\net9.0-windows\SDRSharp.RdsDisplay.dll" "%~dp0..\bin\Plugins\SDRSharp.RdsDisplay.dll"
if errorlevel 1 (
    echo.
    echo COPY FAILED. Is SDRSharp still running?
    pause
    exit /b 1
)

copy /y "%~dp0..\Release\net9.0-windows\pi_codes.json" "%~dp0..\bin\Plugins\pi_codes.json"

echo.
echo Done! Plugin deployed to sdrplugins\bin\Plugins successfully.
pause
