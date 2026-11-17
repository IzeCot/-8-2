$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet build DependencyGraphVisualizer.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Ошибка сборки" }

function CheckCase([string]$file, [string]$expected, [bool]$mustFail = $false) {
    Write-Host "CHECK $file"
    $result = (& dotnet run --no-build --configuration Release --project DependencyGraphVisualizer.csproj -- $file 2>&1 | Out-String)
    $code = $LASTEXITCODE
    if ($mustFail -and $code -eq 0) { throw "Ожидалась ошибка $file, получен успешный код. $result" }
    if (!$mustFail -and $code -ne 0) { throw "Ожидалось успешное выполнение $file, получен код $code. $result" }
    if (!$result.Contains($expected)) { throw "Нет ожидаемого текста '$expected' в $file. $result" }
    Write-Host "PASS $file"
}

CheckCase "tests/alpha.xml" "Обнаружена циклическая зависимость"
CheckCase "tests/bravo.xml" "Диаграмма Mermaid:"
CheckCase "tests/charlie-filter.xml" "CHARLIE"
CheckCase "tests/depth-zero.xml" "Узлов: 1; связей: 0"
CheckCase "tests/invalid-boolean.xml" "TestMode" $true
CheckCase "tests/invalid-depth.xml" "MaxDepth" $true
CheckCase "tests/invalid-repository.xml" "Тестовый репозиторий не найден" $true
CheckCase "tests/invalid-package.xml" "заглавные латинские" $true
Write-Host "ALL TESTS PASSED"
exit 0
