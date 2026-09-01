@echo off
setlocal enabledelayedexpansion
title Antigravity Migration Studio Launcher
cd /d "%~dp0"

echo =========================================================
echo   Starting Antigravity Migration Studio...
echo =========================================================

:: 1. Terminate any running instances
echo [1/3] Terminating any existing running instances...
taskkill /f /im MigrationPgSqlApp.exe >nul 2>&1

:: 2. Check candidate executable paths in order of preference
set "EXE_PATH="
set "WORK_DIR="

if exist "%~dp0Publish\MigrationPgSqlApp.exe" (
    set "EXE_PATH=%~dp0Publish\MigrationPgSqlApp.exe"
    set "WORK_DIR=%~dp0Publish"
) else if exist "%~dp0MigrationPgSqlApp\bin\Release\net8.0-windows\MigrationPgSqlApp.exe" (
    set "EXE_PATH=%~dp0MigrationPgSqlApp\bin\Release\net8.0-windows\MigrationPgSqlApp.exe"
    set "WORK_DIR=%~dp0MigrationPgSqlApp\bin\Release\net8.0-windows"
) else if exist "%~dp0MigrationPgSqlApp\bin\Debug\net8.0-windows\MigrationPgSqlApp.exe" (
    set "EXE_PATH=%~dp0MigrationPgSqlApp\bin\Debug\net8.0-windows\MigrationPgSqlApp.exe"
    set "WORK_DIR=%~dp0MigrationPgSqlApp\bin\Debug\net8.0-windows"
)

:: 3. Launch compiled EXE if found
if defined EXE_PATH (
    echo [2/3] Found compiled application: !EXE_PATH!
    echo [3/3] Launching Migration Studio...
    start "Migration Studio" /d "!WORK_DIR!" "!EXE_PATH!"
    echo Success! Application started.
    timeout /t 2 >nul
    exit /b 0
)

:: 4. If no compiled EXE is found, auto-build using dotnet
echo [2/3] Pre-compiled executable not found.
echo       Attempting to auto-build and launch using dotnet CLI...
echo.

where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] 'dotnet' command is not found in system PATH.
    echo Please install .NET 8.0 SDK or run 'build.bat' / compile in Visual Studio first!
    echo.
    pause
    exit /b 1
)

:: Auto publish to Publish folder so future runs are instant
echo [3/3] Building Release package to Publish directory...
set "DOTNET_CLI_HOME=%~dp0.dotnet_home"
dotnet publish "%~dp0MigrationPgSqlApp\MigrationPgSqlApp.csproj" -c Release -o "%~dp0Publish"

if exist "%~dp0Publish\MigrationPgSqlApp.exe" (
    echo.
    echo Build succeeded! Launching Antigravity Migration Studio...
    start "Migration Studio" /d "%~dp0Publish" "%~dp0Publish\MigrationPgSqlApp.exe"
    timeout /t 2 >nul
    exit /b 0
) else (
    echo.
    echo [ERROR] Build failed or MigrationPgSqlApp.exe not generated.
    echo Please check error messages above.
    echo.
    pause
    exit /b 1
)

