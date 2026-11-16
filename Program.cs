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
        var configPath = args.Length > 0 ? args[0] : "config.xml";

        try
        {
            var config = AppConfig.Load(configPath);
            PrintConfiguration(config);

            var source = config.TestMode
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
        string Get(string name, string fallback = "") =>
            (string?)root.Element(name) ?? fallback;
        bool GetBool(string name, bool fallback) =>
            bool.TryParse(Get(name), out var value) ? value : fallback;
        int GetInt(string name, int fallback)
        {
            if (!int.TryParse(Get(name), out var value) || value < 0)
                throw new InvalidDataException($"Параметр {name} должен быть целым числом >= 0.");
            return value;
        }

        var package = Get("Package").Trim();
        if (string.IsNullOrWhiteSpace(package))
            throw new InvalidDataException("В config.xml необходимо указать Package.");

        var testMode = GetBool("TestMode", false);
        var repository = Get("Repository",
            testMode ? "test-repository.xml" : "https://api.nuget.org/v3-flatcontainer/").Trim();

        if (testMode && !Uri.TryCreate(repository, UriKind.Absolute, out _))
            repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, repository));

        if (!testMode && (!Uri.TryCreate(repository, UriKind.Absolute, out var uri) ||
                          (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            throw new InvalidDataException("Repository должен быть HTTP(S)-адресом NuGet API.");

        return new AppConfig
        {
            Package = package,
            Repository = repository,
            TestMode = testMode,
            AsciiTree = GetBool("AsciiTree", true),
            MaxDepth = GetInt("MaxDepth", 3),
            ExcludeSubstring = Get("ExcludeSubstring").Trim(),
            ShowLoadOrder = GetBool("ShowLoadOrder", true)
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
        _packages = doc.Root!.Elements("Package").ToDictionary(
            e => ((string?)e.Attribute("Id") ?? "").ToLowerInvariant(),
            e => new PackageInfo(
                (string?)e.Attribute("Id") ?? throw new InvalidDataException("У пакета отсутствует Id."),
                (string?)e.Attribute("Version") ?? "1.0.0",
                e.Elements("Dependency")
                    .Select(d => new PackageDependency(
                        (string?)d.Attribute("Id") ?? "",
                        (string?)d.Attribute("Version") ?? "*"))
                    .ToList()));
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
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;

    public NuGetPackageSource(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/') + "/";
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DependencyGraphVisualizer", "1.0"));
    }

    public async Task<PackageInfo> GetPackageAsync(string id, string? requestedVersion = null)
    {
        var normalizedId = id.ToLowerInvariant();
        var versionsUrl = $"{_baseUrl}{normalizedId}/index.json";
        using var versionsResponse = await _http.GetAsync(versionsUrl);
        versionsResponse.EnsureSuccessStatusCode();
        using var versionsJson = JsonDocument.Parse(await versionsResponse.Content.ReadAsStringAsync());
        var versions = versionsJson.RootElement.GetProperty("versions")
            .EnumerateArray().Select(x => x.GetString()!)
            .Where(v => !v.Contains('-'))
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

    public IReadOnlyList<(string Name, int Level)> GetLoadOrder()
    {
        var result = new List<(string, int)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string node, int level)
        {
            if (visited.Contains(node) || !visiting.Add(node)) return;
            foreach (var dependency in Edges.Where(e => e.From.Equals(node, StringComparison.OrdinalIgnoreCase)).Select(e => e.To))
                Visit(dependency, level + 1);
            visiting.Remove(node);
            if (visited.Add(node)) result.Add((node, level));
        }
        Visit(Root, 0);
        return result;
    }

    private static string Safe(string value) => "N_" + new string(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static string Escape(string value) => value.Replace("\"", "'").Replace("\n", " ");
}

internal sealed class GraphAnalyzer
{
    private readonly IPackageSource _source;
    private readonly AppConfig _config;
    private readonly DependencyGraph _graph;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

    public GraphAnalyzer(IPackageSource source, AppConfig config)
    {
        _source = source;
        _config = config;
        _graph = new DependencyGraph(config.Package);
    }

    public async Task<DependencyGraph> BuildAsync()
    {
        var queue = new Queue<(string Id, string? Version, int Depth)>();
        queue.Enqueue((_config.Package, null, 0));
        await ProcessQueueRecursivelyAsync(queue);
        ReportCycles();
        return _graph;
    }

    // Рекурсивная обработка очереди сохраняет порядок BFS: сначала весь текущий уровень,
    // затем зависимости следующего уровня.
    private async Task ProcessQueueRecursivelyAsync(Queue<(string Id, string? Version, int Depth)> queue)
    {
        if (queue.Count == 0) return;

        var (packageId, requestedVersion, depth) = queue.Dequeue();

        if (depth <= _config.MaxDepth &&
            (string.IsNullOrEmpty(_config.ExcludeSubstring) ||
             !packageId.Contains(_config.ExcludeSubstring, StringComparison.OrdinalIgnoreCase)))
        {
            var key = packageId.ToLowerInvariant();
            if (_expanded.Add(key))
            {
                try
                {
                    var package = await _source.GetPackageAsync(packageId, requestedVersion);
                    _graph.Nodes[package.Id] = package.Version;

                    foreach (var dependency in package.Dependencies)
                    {
                        if (!string.IsNullOrEmpty(_config.ExcludeSubstring) &&
                            dependency.Id.Contains(_config.ExcludeSubstring, StringComparison.OrdinalIgnoreCase))
                            continue;

                        _graph.Edges.Add((package.Id, dependency.Id));
                        if (depth < _config.MaxDepth)
                            queue.Enqueue((dependency.Id, dependency.VersionRange, depth + 1));
                        else
                            _graph.Nodes.TryAdd(dependency.Id, "не загружена (лимит глубины)");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Не удалось обработать {packageId}: {ex.Message}");
                }
            }
        }

        await ProcessQueueRecursivelyAsync(queue);
    }

    private void ReportCycles()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Visit(string node)
        {
            if (active.Contains(node))
            {
                if (reported.Add(node))
                    Console.WriteLine($"Обнаружена циклическая зависимость, возвращающаяся к пакету: {node}");
                return true;
            }
            if (!visited.Add(node)) return false;

            active.Add(node);
            var hasCycle = false;
            foreach (var next in _graph.Edges
                         .Where(e => e.From.Equals(node, StringComparison.OrdinalIgnoreCase))
                         .Select(e => e.To))
                hasCycle |= Visit(next);
            active.Remove(node);
            return hasCycle;
        }

        Visit(_config.Package);
    }
}
