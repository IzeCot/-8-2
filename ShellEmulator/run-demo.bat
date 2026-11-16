@echo off
cd /d "%~dp0"
dotnet run --project ShellEmulator.csproj -- --vfs "vfs\sample.csv" --script "scripts\demo.sh"
pause
