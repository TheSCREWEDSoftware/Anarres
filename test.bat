@echo off
rem Runs the core tests and the standards check on unpushed commits, then sums them up
rem ANARRES_TEST_DBC can point at a folder of clean 3.3.5a .dbc files to round-trip every real file too
setlocal
cd /d "%~dp0"

set failed=

echo === Core tests ===
dotnet run --project tests\Anarres.Core.Tests -c Release
if errorlevel 1 set failed=%failed% core
echo.

echo === Coding standards of unpushed commits ===
dotnet run --project tools\Anarres.Standards -v q -- --unpushed
if errorlevel 1 set failed=%failed% standards
echo.

if defined failed (
    echo FAILED:%failed%
    pause
    exit /b 1
)
echo Everything passed.
pause
