@echo off
setlocal
title VoiceFlow - Build and Run
pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>&1
if errorlevel 1 goto missing_sdk

echo Building the latest VoiceFlow source in Debug mode...
echo If VoiceFlow is already running, exit it from the system tray first.
echo.
dotnet build "VoiceFlow\VoiceFlow.csproj" --configuration Debug --nologo
if errorlevel 1 goto build_failed

echo.
echo Starting VoiceFlow. Keep this window open while debugging.
echo Application log: "%APPDATA%\VoiceFlow\voiceflow.log"
echo Closing the app window may leave it running in the system tray.
echo Use the tray Exit command before rebuilding.
echo.
dotnet run --project "VoiceFlow\VoiceFlow.csproj" --configuration Debug --no-build --no-restore -- %*
set "run_exit=%errorlevel%"
if not "%run_exit%"=="0" (
    echo.
    echo VoiceFlow exited with code %run_exit%. Check the application log above.
    pause
)
popd
exit /b %run_exit%

:missing_sdk
echo ERROR: The .NET SDK was not found. Install the .NET 8 SDK and try again.
echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
goto failed

:build_failed
echo.
echo ERROR: Build failed. An older build will not be launched.
echo Review the errors above. First run needs internet access for NuGet packages.
echo Also check that the previous VoiceFlow instance has exited from the tray.

:failed
pause
popd
exit /b 1
