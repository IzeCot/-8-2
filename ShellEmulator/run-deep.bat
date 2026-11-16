@echo off
cd /d "%~dp0"
dotnet run --project ShellEmulator.csproj -- --vfs "vfs\deep.csv" --script "scripts\test-deep.sh"
pause
