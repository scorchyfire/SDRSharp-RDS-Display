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
echo Copying to SDRSharp...
copy /y "%~dp0..\Release\net7.0-windows\SDRSharp.RdsDisplay.dll" "Z:\SDR Software-Other\sdrsharp-x86\Plugins\SDRSharp.RdsDisplay.dll"
if errorlevel 1 (
    echo.
    echo COPY FAILED. Is SDRSharp still running?
    pause
    exit /b 1
)

echo.
echo Done! Plugin deployed successfully.
pause
