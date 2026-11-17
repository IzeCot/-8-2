using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace DependencyGraphVisualizer;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length > 1) { Console.Error.WriteLine("Использование: dotnet run -- [путь к config.xml]"); return 1; }
        var configPath = args.Length == 1 ? args[0] : "config.xml";

        try
        {
            var config = AppConfig.Load(configPath);
            PrintConfiguration(config);

            IPackageSource source = config.TestMode
              ? new TestPackageSource(config.Repository)
              : new NuGetPackageSource(config.Repository);

            var analyzer = new GraphAnalyzer(source, config);
            var graph = await analyzer.BuildAsync();

            Console.WriteLine();
            Console.WriteLine($"Пакет: {graph.Root}");
            Console.WriteLine($"Узлов: {graph.Nodes.Count}; связей: {graph.Edges.Count}");
            Console.WriteLine("Прямые зависимости:");
            var directDependencies = graph.Edges
                .Where(e => e.From.Equals(graph.Root, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.To).OrderBy(x => x).ToList();
            if (directDependencies.Count == 0)
                Console.WriteLine("  (не найдены)");
            else
                foreach (var dependency in directDependencies)
                    Console.WriteLine($"  - {dependency}");
            Console.WriteLine();

            if (config.ShowLoadOrder)
            {
                Console.WriteLine("Порядок загрузки (сначала зависимости):");
                foreach (var (name, level) in graph.GetLoadOrder())
                    Console.WriteLine($"{new string(' ', Math.Min(level, 20) * 2)}{name}");
                Console.WriteLine();
            }

            if (config.AsciiTree)
            {
                Console.WriteLine("ASCII-дерево зависимостей:");
                Console.WriteLine(graph.ToAscii());
                Console.WriteLine();
            }

            Console.WriteLine("Диаграмма Mermaid:");
            Console.WriteLine(graph.ToMermaid());

            var output = Path.Combine(AppContext.BaseDirectory, "dependency-graph.mmd");
            await File.WriteAllTextAsync(output, graph.ToMermaid(), Encoding.UTF8);
            Console.WriteLine();
            Console.WriteLine($"Файл диаграммы сохранён: {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            return 1;
        }
    }

    private static void PrintConfiguration(AppConfig c)
    {
        Console.WriteLine("Параметры конфигурации:");
        Console.WriteLine($"package={c.Package}");
        Console.WriteLine($"repository={c.Repository}");
        Console.WriteLine($"testMode={c.TestMode}");
        Console.WriteLine($"asciiTree={c.AsciiTree}");
        Console.WriteLine($"maxDepth={c.MaxDepth}");
        Console.WriteLine($"excludeSubstring={c.ExcludeSubstring}");
        Console.WriteLine($"showLoadOrder={c.ShowLoadOrder}");
        Console.WriteLine(new string('-', 45));
    }
}

internal sealed class AppConfig
{
    public string Package { get; init; } = "";
    public string Repository { get; init; } = "https://api.nuget.org/v3-flatcontainer/";
    public bool TestMode { get; init; }
    public bool AsciiTree { get; init; } = true;
    public int MaxDepth { get; init; } = 3;
    public string ExcludeSubstring { get; init; } = "";
    public bool ShowLoadOrder { get; init; } = true;

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найден конфигурационный файл: {path}");

        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidDataException("В config.xml отсутствует корневой элемент.");
        if (root.Name.LocalName != "Configuration")
            throw new InvalidDataException("Корневой элемент должен называться Configuration.");
        string Get(string name) =>
            (string?)root.Element(name) ?? throw new InvalidDataException($"Отсутствует параметр {name}.");
        bool GetBool(string name)
        {
            if (!bool.TryParse(Get(name).Trim(), out var value))
                throw new InvalidDataException($"Параметр {name} должен быть true или false.");
            return value;
        }
        int GetInt(string name)
        {
            if (!int.TryParse(Get(name).Trim(), out var value) || value < 0 || value > 100)
                throw new InvalidDataException($"Параметр {name} должен быть целым числом от 0 до 100.");
            return value;
        }

        var package = Get("Package").Trim();
        if (string.IsNullOrWhiteSpace(package))
            throw new InvalidDataException("В config.xml необходимо указать Package.");

        var testMode = GetBool("TestMode");
        var repository = Get("Repository").Trim();
        if (repository.Length == 0)
            throw new InvalidDataException("Параметр Repository не может быть пустым.");
        if (testMode && !System.Text.RegularExpressions.Regex.IsMatch(package, "^[A-Z]+$"))
            throw new InvalidDataException("В тестовом режиме Package должен содержать только заглавные латинские буквы.");

        if (testMode && !Uri.TryCreate(repository, UriKind.Absolute, out _))
            repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, repository));

        if (testMode && !File.Exists(repository))
            throw new FileNotFoundException($"Тестовый репозиторий не найден: {repository}");
        if (!testMode && (!Uri.TryCreate(repository, UriKind.Absolute, out var uri) ||
                          (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            throw new InvalidDataException("Repository должен быть HTTP(S)-адресом NuGet flat-container API.");

        return new AppConfig
        {
            Package = package,
            Repository = repository,
            TestMode = testMode,
            AsciiTree = GetBool("AsciiTree"),
            MaxDepth = GetInt("MaxDepth"),
            ExcludeSubstring = Get("ExcludeSubstring").Trim(),
            ShowLoadOrder = GetBool("ShowLoadOrder")
        };
    }
}

internal sealed record PackageDependency(string Id, string VersionRange);
internal sealed record PackageInfo(string Id, string Version, IReadOnlyList<PackageDependency> Dependencies);

internal interface IPackageSource
{
    Task<PackageInfo> GetPackageAsync(string id, string? requestedVersion = null);
}

internal sealed class TestPackageSource : IPackageSource
{
    private readonly Dictionary<string, PackageInfo> _packages;

    public TestPackageSource(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найден тестовый репозиторий: {path}");

        var doc = XDocument.Load(path);
        if (doc.Root?.Name != "TestRepository")
            throw new InvalidDataException("Ожидался корневой элемент TestRepository.");
        _packages = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in doc.Root.Elements("Package"))
        {
            var id = (string?)element.Attribute("Id") ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Z]+$"))
                throw new InvalidDataException($"Некорректное имя тестового пакета: {id}");
            var dependencies = new List<PackageDependency>();
            foreach (var d in element.Elements("Dependency"))
            {
                var depId = (string?)d.Attribute("Id") ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(depId, "^[A-Z]+$"))
                    throw new InvalidDataException($"Некорректная тестовая зависимость: {depId}");
                dependencies.Add(new PackageDependency(depId, (string?)d.Attribute("Version") ?? "*"));
            }
            if (!_packages.TryAdd(id, new PackageInfo(id, (string?)element.Attribute("Version") ?? "1.0.0", dependencies)))
                throw new InvalidDataException($"Повторяющийся тестовый пакет: {id}");
        }
        foreach (var p in _packages.Values)
            foreach (var d in p.Dependencies)
                if (!_packages.ContainsKey(d.Id))
                    throw new InvalidDataException($"У пакета {p.Id} отсутствует зависимость {d.Id} в тестовом репозитории.");
    }

    public Task<PackageInfo> GetPackageAsync(string id, string? requestedVersion = null)
    {
        if (!_packages.TryGetValue(id.ToLowerInvariant(), out var package))
            throw new KeyNotFoundException($"Пакет '{id}' отсутствует в тестовом репозитории.");
        return Task.FromResult(package);
    }
}

internal sealed class NuGetPackageSource : IPackageSource
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly string _baseUrl;

    public NuGetPackageSource(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/') + "/";
        // Только HTTP + стандартные JsonDocument/XDocument: ни NuGet CLI, ни библиотеки NuGet не используются.
    }

    public async Task<PackageInfo> GetPackageAsync(string id, string? requestedVersion = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9_.-]+$"))
            throw new InvalidDataException($"Недопустимое имя NuGet-пакета: {id}");
        var normalizedId = id.ToLowerInvariant();
        var versionsUrl = $"{_baseUrl}{normalizedId}/index.json";
        using var versionsResponse = await _http.GetAsync(versionsUrl);
        versionsResponse.EnsureSuccessStatusCode();
        using var versionsJson = JsonDocument.Parse(await versionsResponse.Content.ReadAsStringAsync());
        var versions = versionsJson.RootElement.GetProperty("versions")
            .EnumerateArray().Select(x => x.GetString()!)
            .Where(v => !v.Contains('-') && !v.Contains('+'))
            .ToList();

        var selected = versions
            .Where(v => requestedVersion == null || VersionRangeMatcher.Matches(v, requestedVersion))
            .OrderBy(v => v, NuGetVersionComparer.Instance)
            .LastOrDefault();

        if (selected == null)
            throw new InvalidDataException($"Не найдена стабильная версия пакета {id}, подходящая под '{requestedVersion ?? "latest"}'.");

        var nupkgUrl = $"{_baseUrl}{normalizedId}/{selected}/{normalizedId}.nuspec";
        using var nuspecResponse = await _http.GetAsync(nupkgUrl);
        nuspecResponse.EnsureSuccessStatusCode();
        var xml = XDocument.Parse(await nuspecResponse.Content.ReadAsStringAsync());
        XNamespace ns = xml.Root!.GetDefaultNamespace();
        var metadata = xml.Descendants(ns + "metadata").FirstOrDefault()
                       ?? throw new InvalidDataException($"Некорректный nuspec для {id}.");

        var dependenciesElement = metadata.Element(ns + "dependencies");
        var groups = dependenciesElement?.Elements(ns + "group").ToList() ?? new();
        IEnumerable<XElement> dependencyElements;

        if (groups.Count == 0)
        {
            dependencyElements = dependenciesElement?.Elements(ns + "dependency") ?? Enumerable.Empty<XElement>();
        }
        else
        {
            var chosenGroup = groups.FirstOrDefault(g =>
                ((string?)g.Attribute("targetFramework") ?? "").Contains("net8.0", StringComparison.OrdinalIgnoreCase))
                ?? groups.FirstOrDefault(g =>
                    ((string?)g.Attribute("targetFramework") ?? "").Contains("netstandard2.0", StringComparison.OrdinalIgnoreCase))
                ?? groups.FirstOrDefault(g => string.IsNullOrWhiteSpace((string?)g.Attribute("targetFramework")))
                ?? groups[0];
            dependencyElements = chosenGroup.Elements(ns + "dependency");
        }

        var dependencies = dependencyElements.Select(d => new PackageDependency(
            (string?)d.Attribute("id") ?? "",
            (string?)d.Attribute("version") ?? "*"))
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .ToList();

        return new PackageInfo(id, selected, dependencies);
    }
}

internal static class VersionRangeMatcher
{
    public static bool Matches(string versionText, string rangeText)
    {
        if (string.IsNullOrWhiteSpace(rangeText) || rangeText == "*") return true;
        var version = NuGetVersionComparer.Parse(versionText);
        rangeText = rangeText.Trim();

        if (!(rangeText.StartsWith("[") || rangeText.StartsWith("(")))
        {
            var minimum = NuGetVersionComparer.Parse(rangeText.Trim('[', ']', '(', ')'));
            return version.CompareTo(minimum) >= 0;
        }

        var inclusiveMin = rangeText.StartsWith("[");
        var inclusiveMax = rangeText.EndsWith("]");
        var parts = rangeText.Trim('[', ']', '(', ')').Split(',');
        if (parts.Length == 1)
        {
            var exact = NuGetVersionComparer.Parse(parts[0]);
            return version.CompareTo(exact) == 0;
        }

        if (!string.IsNullOrWhiteSpace(parts[0]))
        {
            var min = NuGetVersionComparer.Parse(parts[0]);
            var cmp = version.CompareTo(min);
            if (cmp < 0 || (cmp == 0 && !inclusiveMin)) return false;
        }
        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            var max = NuGetVersionComparer.Parse(parts[1]);
            var cmp = version.CompareTo(max);
            if (cmp > 0 || (cmp == 0 && !inclusiveMax)) return false;
        }
        return true;
    }
}

internal sealed class NuGetVersionComparer : IComparer<string>
{
    public static readonly NuGetVersionComparer Instance = new();
    public int Compare(string? x, string? y) => Parse(x ?? "0").CompareTo(Parse(y ?? "0"));

    public static Version Parse(string value)
    {
        var clean = value.Split('-')[0];
        var parts = clean.Split('.');
        while (parts.Length < 3)
        {
            clean += ".0";
            parts = clean.Split('.');
        }
        return Version.TryParse(clean, out var result) ? result : new Version(0, 0, 0);
    }
}

internal sealed class DependencyGraph
{
    public string Root { get; }
    public Dictionary<string, string> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<(string From, string To)> Edges { get; } = new();

    public DependencyGraph(string root) => Root = root;

    public string ToMermaid()
    {
        var sb = new StringBuilder("flowchart TD\n");
        foreach (var (id, version) in Nodes.OrderBy(x => x.Key))
            sb.Append("  ").Append(Safe(id)).Append("[\"").Append(Escape(id))
                .Append(" ").Append(Escape(version)).AppendLine("\"]");
        foreach (var (from, to) in Edges.OrderBy(e => e.From).ThenBy(e => e.To))
            sb.Append("  ").Append(Safe(from)).Append(" --> ").Append(Safe(to)).AppendLine();
        return sb.ToString();
    }

    public string ToAscii()
    {
        var sb = new StringBuilder();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id, string prefix, bool last, HashSet<string> path)
        {
            sb.Append(prefix).Append(last ? "└── " : "├── ").Append(id);
            if (path.Contains(id)) { sb.AppendLine(" [цикл]"); return; }
            if (visited.Contains(id)) { sb.AppendLine(" [уже показан]"); return; }
            sb.AppendLine();
            visited.Add(id);
            var nextPath = new HashSet<string>(path, StringComparer.OrdinalIgnoreCase) { id };
            var children = Edges.Where(e => e.From.Equals(id, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.To).OrderBy(x => x).ToList();
            for (var i = 0; i < children.Count; i++)
                Visit(children[i], prefix + (last ? "    " : "│   "), i == children.Count - 1, nextPath);
        }
        sb.AppendLine(Root);
        var rootChildren = Edges.Where(e => e.From.Equals(Root, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.To).OrderBy(x => x).ToList();
        for (var i = 0; i < rootChildren.Count; i++)
            Visit(rootChildren[i], "", i == rootChildren.Count - 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Root });
        return sb.ToString();
    }

    // Детерминированный порядок "зависимости перед пакетом", не порядок сетевых загрузок NuGet.
    // Для циклов топологического порядка не существует: отмечаем их явно.
    public IReadOnlyList<(string Name, int Level)> GetLoadOrder()
    {
        var order = new List<(string Name, int Level)>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool cycleFound = false;
        void Visit(string node, int level)
        {
            if (done.Contains(node)) return;
            if (!active.Add(node)) { cycleFound = true; return; }
            foreach (var dependency in Edges
                         .Where(e => e.From.Equals(node, StringComparison.OrdinalIgnoreCase))
                         .Select(e => e.To).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Visit(dependency, level + 1);
            active.Remove(node);
            if (done.Add(node)) order.Add((node, level));
        }
        Visit(Root, 0);
        if (cycleFound)
            Console.WriteLine("Внимание: в циклическом графе нет строгого порядка загрузки; показан порядок обхода.");
        return order;
    }

    private static string Safe(string value) => "N_" + new string(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static string Escape(string value) => value.Replace("\"", "'").Replace("\n", " ");
}


internal sealed class GraphAnalyzer
{
    private readonly IPackageSource _source;
    private readonly AppConfig _config;
    private readonly DependencyGraph _graph;
    private readonly HashSet<string> _expanded =
        new(StringComparer.OrdinalIgnoreCase);

    public GraphAnalyzer(IPackageSource source, AppConfig config)
    {
        _source = source;
        _config = config;
        _graph = new DependencyGraph(config.Package);
    }

    public async Task<DependencyGraph> BuildAsync()
    {
        if (!string.IsNullOrEmpty(_config.ExcludeSubstring) &&
            _config.Package.Contains(_config.ExcludeSubstring, StringComparison.OrdinalIgnoreCase))
            Console.Error.WriteLine("Предупреждение: корневой пакет не фильтруется.");

        var queue = new Queue<(string Id, string? Version, int Depth)>();
        queue.Enqueue((_config.Package, null, 0));
        await ProcessLevelsRecursivelyAsync(queue);
        ReportCycles();
        return _graph;
    }

    // BFS с рекурсией по уровням. Рекурсия не углубляется на каждый пакет.
    private async Task ProcessLevelsRecursivelyAsync(Queue<(string Id, string? Version, int Depth)> queue)
    {
        if (queue.Count == 0) return;

        var levelCount = queue.Count;
        for (int i = 0; i < levelCount; i++)
        {
            var (packageId, version, depth) = queue.Dequeue();
            if (depth > _config.MaxDepth || !_expanded.Add(packageId)) continue;

            var package = await _source.GetPackageAsync(packageId, version);
            _graph.Nodes[package.Id] = package.Version;
            if (depth == _config.MaxDepth) continue;

            foreach (var dep in package.Dependencies)
            {
                if (!string.IsNullOrEmpty(_config.ExcludeSubstring) &&
                    dep.Id.Contains(_config.ExcludeSubstring, StringComparison.OrdinalIgnoreCase))
                    continue;
                _graph.Edges.Add((package.Id, dep.Id));
                _graph.Nodes.TryAdd(dep.Id, "ещё не загружена");
                if (!_expanded.Contains(dep.Id))
                    queue.Enqueue((dep.Id, dep.VersionRange, depth + 1));
            }
        }
        await ProcessLevelsRecursivelyAsync(queue);
    }

    private void ReportCycles()
    {
        var visited = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var active = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var reported = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        void Visit(string node)
        {
            if (active.Contains(node))
            {
                if (reported.Add(node))
                {
                    Console.WriteLine(
                        $"Обнаружена циклическая зависимость: {node}");
                }

                return;
            }

            if (!visited.Add(node))
                return;

            active.Add(node);

            foreach (var next in _graph.Edges
                         .Where(e => e.From.Equals(
                             node, StringComparison.OrdinalIgnoreCase))
                         .Select(e => e.To))
            {
                Visit(next);
            }

            active.Remove(node);
        }

        Visit(_config.Package);
    }
}

