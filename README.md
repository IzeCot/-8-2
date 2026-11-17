# Dependency Graph Visualizer — вариант №8

Консольное приложение на C# и .NET 8 для построения графа зависимостей NuGet-пакета.

Программа получает сведения о пакетах из NuGet Flat Container API или из локального XML-файла тестового репозитория. Результат выводится в консоль в виде списка зависимостей, ASCII-дерева и диаграммы Mermaid.

## Возможности

- Чтение параметров из XML-файла `config.xml`.
- Два источника данных: NuGet API и тестовый XML-репозиторий.
- Обход графа зависимостей в ширину (BFS) с ограничением глубины.
- Фильтрация пакетов по заданной подстроке.
- Поиск циклических зависимостей.
- Вывод прямых зависимостей анализируемого пакета.
- Формирование ASCII-дерева и диаграммы Mermaid.
- Вычисление порядка загрузки: сначала зависимости, затем пакет, который от них зависит.
- Сохранение диаграммы Mermaid в файл `dependency-graph.mmd`.

## Требования

- .NET SDK 8.0 или новее.
- Подключение к интернету для работы с NuGet API.
- Для тестового режима подключение к интернету не требуется.

## Структура репозитория

Все файлы проекта находятся в корне репозитория:

```text
-8-2/
├── DependencyGraphVisualizer.csproj
├── Program.cs
├── config.xml
├── test-repository.xml
├── README.md
└── .gitignore
```

## Запуск

Откройте терминал в каталоге репозитория и выполните:

```bash
dotnet restore
dotnet build
dotnet run
```

По умолчанию программа загружает настройки из файла `config.xml`. Чтобы указать другой конфигурационный файл, передайте его путь:

```bash
dotnet run -- путь-к-файлу.xml
```

## Настройка программы

Основные параметры задаются в файле `config.xml`:

| Параметр | Назначение |
|---|---|
| `Package` | Имя пакета, зависимости которого нужно проанализировать |
| `Repository` | Адрес NuGet Flat Container API или путь к тестовому XML-файлу |
| `TestMode` | `true` — использовать тестовый XML; `false` — использовать NuGet API |
| `AsciiTree` | Включить или отключить вывод ASCII-дерева |
| `ShowLoadOrder` | Включить или отключить вывод порядка загрузки |
| `MaxDepth` | Максимальная глубина обхода графа; значение должно быть целым числом не меньше 0 |
| `ExcludeSubstring` | Подстрока, по которой отфильтровываются пакеты; пустое значение отключает фильтр |

Пример конфигурации для реального режима:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <Package>Newtonsoft.Json</Package>
  <Repository>https://api.nuget.org/v3-flatcontainer/</Repository>
  <TestMode>false</TestMode>
  <AsciiTree>true</AsciiTree>
  <ShowLoadOrder>true</ShowLoadOrder>
  <MaxDepth>3</MaxDepth>
  <ExcludeSubstring></ExcludeSubstring>
</Configuration>
```

## Тестовый режим

Для запуска на демонстрационных данных измените параметры в `config.xml`:

```xml
<Package>ALPHA</Package>
<Repository>test-repository.xml</Repository>
<TestMode>true</TestMode>
<AsciiTree>true</AsciiTree>
<ShowLoadOrder>true</ShowLoadOrder>
<MaxDepth>10</MaxDepth>
<ExcludeSubstring></ExcludeSubstring>
```

Файл `test-repository.xml` содержит набор условных пакетов. В нём специально создан цикл зависимостей:

```text
ALPHA -> BRAVO -> DELTA -> ALPHA
```

При обработке этого набора программа должна сообщить о циклической зависимости. Тестовый режим позволяет проверить работу алгоритма без обращения к NuGet.

## Результаты работы

Во время выполнения программа выводит:

1. Использованные параметры конфигурации.
2. Имя анализируемого пакета, количество узлов и связей графа.
3. Список прямых зависимостей.
4. Порядок загрузки зависимостей, если `ShowLoadOrder=true`.
5. ASCII-дерево, если `AsciiTree=true`.
6. Диаграмму в формате Mermaid.

Файл `dependency-graph.mmd` сохраняется в каталоге сборки приложения (например, `bin/Debug/net8.0/`). Диаграмму можно открыть в редакторе с поддержкой Mermaid или использовать в Markdown-документах.

## Используемые технологии

- C#
- .NET 8
- XML (`System.Xml.Linq`) для чтения конфигурации и тестовых данных
- HTTP (`HttpClient`) для обращения к NuGet API
- JSON (`System.Text.Json`) для обработки ответа NuGet API
- Mermaid для визуального представления графа
