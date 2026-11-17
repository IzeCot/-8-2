$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
Write-Host "=== Our analyzer: NuGet HTTP API and .nuspec only ==="
$ours = (& dotnet run --configuration Release --no-build --project DependencyGraphVisualizer.csproj -- tests/real-nuget.xml 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw "NuGet HTTP test failed: $ours" }
if (!$ours.Contains("Пакет: Newtonsoft.Json") -or !$ours.Contains("Диаграмма Mermaid:"))
    { throw "Unexpected analyzer result: $ours" }
Write-Host $ours

# Only for independent validation: the analyzer itself never uses a package manager.
$folder = Join-Path $env:TEMP ("NuGetCompare_" + [Guid]::NewGuid().ToString("N"))
try {
    & dotnet new console --framework net8.0 -o $folder --force | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet new failed" }
    & dotnet add $folder package Newtonsoft.Json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet add package failed" }
    $reference = (& dotnet list $folder package --include-transitive 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or !$reference.Contains("Newtonsoft.Json"))
        { throw "NuGet reference output unexpected: $reference" }
    Write-Host "=== Official NuGet dependency list (independent comparison) ==="
    Write-Host $reference
    Write-Host "Compare package version, target framework and transitive nodes; dotnet list does not render edges."
}
finally { if (Test-Path $folder) { Remove-Item -Recurse -Force $folder } }
exit 0
