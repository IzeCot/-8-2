using System.Text;

namespace ShellEmulator;

internal sealed class VfsEntry
{
    public required string Path { get; init; }
    public required string Type { get; init; }
    public string Content { get; set; } = "";
    public string Owner { get; set; } = "root";
}

internal sealed class VirtualFileSystem
{
    private readonly Dictionary<string, VfsEntry> entries = new(StringComparer.Ordinal);
    public string Name { get; private set; } = "memory-vfs";
    public IReadOnlyCollection<VfsEntry> Entries => entries.Values;

    public void LoadCsv(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("CSV-файл VFS не найден.", path);
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) throw new FormatException("CSV-файл VFS пуст.");

        var header = ParseCsvLine(lines[0]).Select(x => x.Trim().ToLowerInvariant()).ToArray();
        int pathCol = Array.IndexOf(header, "path");
        int typeCol = Array.IndexOf(header, "type");
        int contentCol = Array.IndexOf(header, "content");
        int ownerCol = Array.IndexOf(header, "owner");
        if (pathCol < 0 || typeCol < 0 || contentCol < 0 || ownerCol < 0)
            throw new FormatException("CSV должен содержать столбцы path,type,content,owner.");

        var loaded = new Dictionary<string, VfsEntry>(StringComparer.Ordinal)
        {
            ["/"] = new VfsEntry { Path = "/", Type = "dir" }
        };
        for (int lineNo = 1; lineNo < lines.Length; lineNo++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineNo])) continue;
            var cells = ParseCsvLine(lines[lineNo]);
            int max = Math.Max(Math.Max(pathCol, typeCol), Math.Max(contentCol, ownerCol));
            if (cells.Count <= max) throw new FormatException($"Некорректная CSV-строка {lineNo + 1}.");

            string entryPath = Normalize(cells[pathCol]);
            string type = cells[typeCol].Trim().ToLowerInvariant();
            if (entryPath == "/" || !entryPath.StartsWith('/') || (type != "file" && type != "dir"))
                throw new FormatException($"Недопустимый путь или тип в строке {lineNo + 1}.");
            string content = cells[contentCol].Replace("\\n", "\n");
            if (!loaded.TryAdd(entryPath, new VfsEntry {
                Path = entryPath, Type = type, Content = content, Owner = cells[ownerCol]
            })) throw new FormatException($"Повторяющийся путь: {entryPath}");
        }

        foreach (var entry in loaded.Values.Where(e => e.Path != "/"))
        {
            var parent = Parent(entry.Path);
            if (!loaded.TryGetValue(parent, out var p) || p.Type != "dir")
                throw new FormatException($"Не найдена папка-родитель {parent} для {entry.Path}.");
        }

        entries.Clear();
        foreach (var pair in loaded) entries[pair.Key] = pair.Value;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public static VirtualFileSystem CreateDefault()
    {
        var vfs = new VirtualFileSystem();
        vfs.entries["/"] = new VfsEntry { Path = "/", Type = "dir" };
        vfs.entries["/home"] = new VfsEntry { Path = "/home", Type = "dir", Owner = "student" };
        vfs.entries["/home/student"] = new VfsEntry { Path = "/home/student", Type = "dir", Owner = "student" };
        vfs.entries["/home/student/readme.txt"] = new VfsEntry {
            Path = "/home/student/readme.txt", Type = "file",
            Content = "Welcome to the virtual shell.\nThis is a sample file.\nThis is a sample file.\nHave a nice day.\n", Owner = "student"
        };
        vfs.entries["/home/student/numbers.txt"] = new VfsEntry {
            Path = "/home/student/numbers.txt", Type = "file", Content = "one\ntwo\nthree\n", Owner = "student"
        };
        return vfs;
    }

    public string Resolve(string cwd, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return cwd;
        string combined = path.StartsWith('/') ? path : (cwd == "/" ? "/" + path : cwd + "/" + path);
        var parts = new List<string>();
        foreach (var part in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return "/" + string.Join("/", parts);
    }

    public bool TryGet(string path, out VfsEntry entry) => entries.TryGetValue(path, out entry!);
    public IEnumerable<VfsEntry> Children(string path) =>
        entries.Values.Where(e => Parent(e.Path) == path && e.Path != path)
            .OrderBy(e => e.Type == "dir" ? 0 : 1).ThenBy(e => e.Path);

    public void ChangeOwner(string path, string owner)
    {
        if (!entries.TryGetValue(path, out var entry))
            throw new InvalidOperationException($"Файл или каталог не найден: {path}");
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Имя владельца не может быть пустым.");
        entry.Owner = owner;
    }

    public static string Parent(string path)
    {
        if (path == "/") return "/";
        int index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    private static string Normalize(string path)
    {
        string normalized = path.Replace('\\\\', '/').TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        if (quoted) throw new FormatException("Незакрытая кавычка в CSV.");
        values.Add(value.ToString());
        return values;
    }
}
