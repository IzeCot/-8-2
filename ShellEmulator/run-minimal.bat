@echo off
cd /d "%~dp0"
dotnet run --project ShellEmulator.csproj -- --vfs "vfs\minimal.csv" --script "scripts\test-minimal.sh"
pause
