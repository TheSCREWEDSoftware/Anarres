@echo off
rem Builds Anarres as one file that runs on any 64-bit Windows PC, with nothing else to install, and zips it
setlocal
cd /d "%~dp0"
set RUNTIME=win-x64
set OUT=publish\%RUNTIME%

echo Building a standalone Anarres for %RUNTIME%; the first time downloads the .NET runtime, about 70 MB...
dotnet publish src\Anarres.App -c Release -r %RUNTIME% --self-contained -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%" -v q --nologo
if errorlevel 1 (
    echo.
    echo The build failed; the errors are above.
    pause
    exit /b 1
)

dotnet publish src\Anarres.Cli -c Release -r %RUNTIME% --self-contained -p:PublishSingleFile=true ^
    -o "%OUT%" -v q --nologo
if errorlevel 1 (
    echo.
    echo The command-line tool didn't build; Anarres.exe itself is fine.
)

set ZIP=publish\Anarres-%RUNTIME%.zip
powershell -NoProfile -Command "Compress-Archive -Force -Path '%OUT%\*.exe' -DestinationPath '%ZIP%'"
echo.
echo Done. Send %ZIP% to anyone who wants to try Anarres: they unzip it and open Anarres.exe.
echo The files themselves are in %OUT%.
explorer publish
pause
