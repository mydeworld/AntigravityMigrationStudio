@echo off
cd /d "%~dp0"
echo Terminating any running instances of Migration Studio...
taskkill /f /im MigrationPgSqlApp.exe >nul 2>&1

if exist "%~dp0Publish\MigrationPgSqlApp.exe" (
    echo Starting pre-compiled Antigravity Migration Studio...
    cd /d "%~dp0Publish"
    start "" "MigrationPgSqlApp.exe"
) else (
    echo Publish folder not found. Starting from source using dotnet run...
    set DOTNET_CLI_HOME=%~dp0.dotnet_home
    dotnet run --project "%~dp0MigrationPgSqlApp\MigrationPgSqlApp.csproj"
)
