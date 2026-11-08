@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET 8 SDK is required. Install Visual Studio .NET desktop development workload.
    pause
    exit /b 1
)
set "OUT=%~dp0publish\win-x64"
echo Building one-file Windows x64 app...
dotnet publish "YouTubeMP3.Windows\YouTubeMP3.Windows.csproj" -c Release -r win-x64 --self-contained true -o "%OUT%" /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
if errorlevel 1 (
    echo [ERROR] Publish failed. See build errors above.
    pause
    exit /b 1
)
if not exist "%OUT%\YouTubeMP3.Windows.exe" (
    echo [ERROR] Expected EXE not found in publish directory.
    pause
    exit /b 1
)
echo.
echo Output: "%OUT%\YouTubeMP3.Windows.exe"
echo Verify that the publish folder contains only one file before distributing just the EXE.
explorer "%OUT%"
pause
