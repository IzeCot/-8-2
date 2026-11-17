# NuGet Dependency Graph Visualizer — проект по пяти этапам

Консольное приложение (CLI) на C# / .NET 8, которое строит направленный граф зависимостей пакета из XML тестового репозитория либо из HTTP-метаданных NuGet. **Инструмент не вызывает NuGet CLI и не использует библиотеки NuGet для получения зависимостей**: запросы выполняются `HttpClient`, версии читаются из JSON, зависимости — из XML .nuspec.

## Запуск на Windows

Требуется .NET 8 SDK. Откройте **cmd** в корне репозитория:

```bat
dotnet build DependencyGraphVisualizer.csproj
dotnet run --project DependencyGraphVisualizer.csproj
```

По умолчанию запускается полностью автономная демонстрация с пакетом `ALPHA` и файлом `test-repository.xml`. Отдельную конфигурацию можно указать после `--`:

```bat
dotnet run --project DependencyGraphVisualizer.csproj -- tests/alpha.xml
dotnet run --project DependencyGraphVisualizer.csproj -- tests/bravo.xml
dotnet run --project DependencyGraphVisualizer.csproj -- tests/charlie-filter.xml
dotnet run --project DependencyGraphVisualizer.csproj -- tests/real-nuget.xml
```

Последняя команда использует реальный API NuGet и требует доступа к Интернету.

## XML-конфигурация

Пример содержимого `config.xml`:

```xml
<Configuration>
  <Package>ALPHA</Package>
  <Repository>test-repository.xml</Repository>
  <TestMode>true</TestMode>
  <AsciiTree>true</AsciiTree>
  <ShowLoadOrder>true</ShowLoadOrder>
  <MaxDepth>3</MaxDepth>
  <ExcludeSubstring></ExcludeSubstring>
</Configuration>
```

| Параметр | Значение |
|---|---|
| Package | Идентификатор анализируемого пакета; в тестовом режиме только `A-Z` |
| Repository | Путь к XML тестовых данных относительно конфигурации или base URL NuGet Flat Container API |
| TestMode | `true` / `false` |
| AsciiTree | Печатать или нет текстовое дерево |
| ShowLoadOrder | Печатать зависимости перед зависимым пакетом |
| MaxDepth | Уровни от корневого пакета, от 0 до 100; 0 — только корень |
| ExcludeSubstring | Не добавлять в граф пакеты с этой подстрокой (без учёта регистра) |

Неверное имя, отсутствующий XML-параметр, некорректный `true/false`, отрицательная глубина, неверный URL и отсутствующий тестовый файл приводят к ошибке и ненулевому коду завершения.

## Реальный NuGet без менеджера пакетов

В `tests/real-nuget.xml` указан сервис `https://api.nuget.org/v3-flatcontainer/`. Программа обращается к двум документированным HTTP-ресурсам:

- `GET {base}/{package-id}/index.json` — список версий пакета;
- `GET {base}/{package-id}/{version}/{package-id}.nuspec` — XML-манифест с прямыми зависимостями.

Выбирается последняя стабильная версия пакета, подходящая под указанный диапазон; для фреймворка приоритет отдаётся группе `net8.0`, затем `netstandard2.0`, затем группе без фреймворка или первой имеющейся. Сети не требуется для режима `TestMode=true`.

**Ограничения по сравнению с полным NuGet Restore:** здесь реализован учебный упрощённый алгоритм выбора версий (не полноценный SemVer/NuGet Resolver), не вычисляется глобальное согласование конфликтующих версий и не моделируются все правила совместимости целевых платформ, ограничения совместимости `include/exclude`, asset selection и кэш NuGet. При разных версиях или платформах граф может отличаться от `dotnet restore`. При повторном появлении одного пакета BFS выбирает версию на первом достижении узла (ближайшая зависимость).

Документация API: [Package Content / Flat Container](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource) и [.nuspec](https://learn.microsoft.com/en-us/nuget/reference/nuspec).

## Алгоритм и визуализация

- Транзитивные зависимости строятся **BFS с рекурсивным переходом между уровнями**; повторно посещаемые пакеты не раскрываются. Циклы обнаруживаются и отмечаются.
- Глубина считается от корня (`0` — только исходный пакет).
- `ShowLoadOrder` печатает обход в порядке «сначала зависимости, затем зависимый пакет». В **циклическом** графе строгий топологический порядок невозможен; программа сообщает, что показывает приближённый порядок.
- `AsciiTree=true` дополнительно печатает дерево; оно отмечает циклы и повторные узлы.
- Программа всегда печатает `flowchart TD` в формате Mermaid; также сохраняет файл `dependency-graph.mmd` в каталог исполняемой сборки `bin/Debug/net8.0/` или `bin/Release/net8.0/`.

## Сценарии, включая три пакета

| Файл | Проверяемое |
|---|---|
| `tests/alpha.xml` | Пакет ALPHA, разветвления и цикл ALPHA → BRAVO → DELTA → ALPHA |
| `tests/bravo.xml` | Пакет BRAVO, другая точка обхода |
| `tests/charlie-filter.xml` | Пакет CHARLIE, фильтр ECHO |
| `tests/depth-zero.xml` | Глубина 0: один узел без рёбер |
| `tests/invalid-boolean.xml` | Ошибка булевого параметра |
| `tests/invalid-depth.xml` | Ошибка глубины |
| `tests/invalid-repository.xml` | Отсутствующий тестовый репозиторий |
| `tests/invalid-package.xml` | Неправильное тестовое имя пакета |
| `tests/real-nuget.xml` | Работа по реальному HTTP API NuGet |

Запуск полного **автоматизированного набора офлайн-проверок**:

```powershell
powershell -ExecutionPolicy Bypass -File tests/run-tests.ps1
```

Те же проверки выполняются в GitHub Actions `.github/workflows/build.yml`.

## Сравнение с настоящим NuGet (этапы 4 и 5)

Менеджер пакетов используется **только для независимого сравнения**, а не для сбора зависимостей приложением.

1. Откройте `tests/real-nuget.xml`, запустите анализ и сохраните прямые/транзитивные зависимости, порядок обхода и Mermaid.
2. В **отдельном временном** проекте .NET укажите тот же пакет и соответствующую версию:
   ```bat
   dotnet new console -n NugetCompare
   cd NugetCompare
   dotnet add package Newtonsoft.Json
   dotnet list package --include-transitive
   ```
3. Сравните фактически выведенные пакеты, а не предполагайте совпадение заранее. Если есть расхождения, проверьте **версию**, **TFM**, **транзитивность**, **совместимость диапазонов** и **правила разрешения конфликтов**. Штатный `dotnet list package` показывает список, но не граф с ребрами и не Mermaid; исходный алгоритм визуализирует структуру зависимостей, а не порядок сетевых загрузок при restore.

**Важно:** сравнение с реальным NuGet для конкретного запуска требует доступа к сети и отдельной проверки; успешный офлайн-тест его не заменяет.

## Этапы реализации

1. CLI, чтение XML и валидация параметров.
2. Получение прямых зависимостей пакетов NuGet через HTTP + JSON + .nuspec.
3. Рекурсивный BFS, транзитивные зависимости, фильтр, глубина, циклы и тестовый XML-репозиторий.
4. Порядок обхода зависимостей, предупреждение о циклах и руководство по сравнению с NuGet.
5. Mermaid, ASCII-дерево, демонстрации для ALPHA, BRAVO и CHARLIE, отрицательные тесты и CI.

Исходники находятся в `Program.cs`, зависимости и примеры — в `test-repository.xml` и `tests/`. Файлы `bin/` и `obj/` не загружаются в Git.
