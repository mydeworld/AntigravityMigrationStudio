@echo off
cd /d "%~dp0"
set DOTNET_CLI_HOME=%~dp0.dotnet_home
echo Terminating any running instances of Migration Studio...
taskkill /f /im MigrationPgSqlApp.exe >nul 2>&1
echo Rebuilding and Publishing Antigravity Migration Studio...
dotnet publish MigrationPgSqlApp\MigrationPgSqlApp.csproj -c Release -o Publish
pause
