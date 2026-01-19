@echo off
REM Build script for RBN VFD Spot Display
REM Requires .NET 8.0 SDK

echo Building RBN VFD Spot Display...
echo.

cd /d "%~dp0"

echo Restoring packages...
dotnet restore

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Package restore failed!
    pause
    exit /b 1
)

echo.
echo Building Release configuration...
dotnet build -c Release

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo Build successful!
echo.
echo Executable location:
echo   RbnVfdDisplay\bin\Release\net8.0-windows\RbnVfdDisplay.exe
echo ========================================
echo.

REM Optionally publish as self-contained
set /p PUBLISH="Publish as self-contained executable? (y/n): "
if /i "%PUBLISH%"=="y" (
    echo.
    echo Publishing self-contained executable...
    dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
    
    if %errorlevel% equ 0 (
        echo.
        echo Self-contained executable published to: ./publish/RbnVfdDisplay.exe
    ) else (
        echo.
        echo WARNING: Self-contained publish failed!
    )
)

echo.
pause
