# Dependency Graph Visualizer — вариант №8

Консольное приложение .NET 8 для анализа прямых и транзитивных зависимостей NuGet-пакетов. Приложение получает сведения напрямую из NuGet HTTP API, не использует менеджеры пакетов или сторонние библиотеки для чтения зависимостей.

## Возможности

- Настройки из `config.xml` (XML).
- Реальный режим NuGet и тестовый XML-репозиторий.
- Рекурсивный обход графа в глубину (DFS) с ограничением глубины.
- Отсечение пакетов по подстроке.
- Обнаружение циклических зависимостей.
- Вывод Mermaid-диаграммы и ASCII-дерева.
- Вычисление порядка загрузки зависимостей.
- Сохранение Mermaid-описания в `dependency-graph.mmd`.

## Требования

- .NET SDK 8.0 или новее.
- Доступ к интернету для реального режима NuGet.

## Запуск

Из каталога проекта:

```bash
dotnet run
```

Можно передать путь к другому конфигурационному файлу:

```bash
dotnet run -- config.xml
```

## Настройка

Измените значения в `config.xml`:

| Параметр | Назначение |
|---|---|
| `Package` | Имя анализируемого пакета |
| `Repository` | NuGet API URL или путь к тестовому XML |
| `TestMode` | `true` — тестовый XML, `false` — NuGet |
| `AsciiTree` | Вывод ASCII-дерева |
| `ShowLoadOrder` | Вывод порядка загрузки |
| `MaxDepth` | Максимальная глубина обхода |
| `ExcludeSubstring` | Подстрока для исключения пакетов |

## Тестовый режим

Для тестового сценария укажите в `config.xml`:

```xml
<Package>ALPHA</Package>
<Repository>test-repository.xml</Repository>
<TestMode>true</TestMode>
<MaxDepth>10</MaxDepth>
```

В тестовом репозитории намеренно есть цикл `ALPHA -> BRAVO -> DELTA -> ALPHA`. Программа должна обнаружить цикл и не зациклиться.

## Mermaid

Программа печатает описание графа в синтаксисе Mermaid и сохраняет его в `dependency-graph.mmd`. Содержимое файла можно вставить в Markdown-документ с блоком `mermaid`.

## Структура

```text
DependencyGraphVisualizer/
├── DependencyGraphVisualizer.csproj
├── Program.cs
├── config.xml
├── test-repository.xml
└── README.md
```
