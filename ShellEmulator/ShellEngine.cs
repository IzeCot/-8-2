namespace ShellEmulator;

internal sealed record CommandResult(string Output, bool Success = true, bool Exit = false);

internal sealed class ShellEngine
{
    private readonly VirtualFileSystem vfs;
    private readonly List<string> history = new();
    public string CurrentDirectory { get; private set; } = "/";
    public IReadOnlyList<string> History => history;

    public ShellEngine(VirtualFileSystem fileSystem) => vfs = fileSystem;

    public CommandResult Execute(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new CommandResult("");
        IReadOnlyList<string> tokens;
        try { tokens = CommandParser.Parse(input); }
        catch (FormatException ex) { return new CommandResult("Ошибка: " + ex.Message, false); }

        if (tokens.Count == 0) return new CommandResult("");
        history.Add(input);
        string command = tokens[0];
        string[] args = tokens.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "exit" => args.Length == 0 ? new CommandResult("Завершение работы эмулятора.", true, true) : Error("Использование: exit"),
                "ls" => Ls(args),
                "cd" => Cd(args),
                "tail" => Tail(args),
                "uniq" => Uniq(args),
                "tac" => Tac(args),
                "chown" => Chown(args),
                "help" => Help(args),
                "pwd" => args.Length == 0 ? new CommandResult(CurrentDirectory) : Error("Использование: pwd"),
                "whoami" => args.Length == 0 ? new CommandResult(Environment.UserName) : Error("Использование: whoami"),
                _ => Error($"Неизвестная команда: {command}")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
        {
            return Error(ex.Message);
        }
    }

    public IReadOnlyList<(string Command, CommandResult Result)> RunScript(string scriptPath)
    {
        if (!File.Exists(scriptPath)) throw new FileNotFoundException("Стартовый скрипт не найден.", scriptPath);
        var results = new List<(string, CommandResult)>();
        int lineNo = 0;
        foreach (string raw in File.ReadLines(scriptPath))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var result = Execute(line);
            results.Add((line, result));
            if (!result.Success || result.Exit)
            {
                if (!result.Success)
                    results.Add(("", new CommandResult($"Скрипт остановлен: ошибка в строке {lineNo}.", false)));
                break;
            }
        }
        return results;
    }

    private CommandResult Ls(string[] args)
    {
        if (args.Length > 1) return Error("Использование: ls [путь]");
        string path = vfs.Resolve(CurrentDirectory, args.Length == 0 ? "." : args[0]);
        if (!vfs.TryGet(path, out var entry)) return Error($"Путь не найден: {path}");
        if (entry.Type == "file") return new CommandResult(entry.Path.Split('/').Last());
        var lines = vfs.Children(path).Select(e => (e.Type == "dir" ? "d " : "- ") + e.Path.Split('/').Last() + $"  ({e.Owner})");
        return new CommandResult(string.Join(Environment.NewLine, lines));
    }

    private CommandResult Cd(string[] args)
    {
        if (args.Length > 1) return Error("Использование: cd [путь]");
        string path = vfs.Resolve(CurrentDirectory, args.Length == 0 ? "/" : args[0]);
        if (!vfs.TryGet(path, out var entry) || entry.Type != "dir") return Error($"Каталог не найден: {path}");
        CurrentDirectory = path;
        return new CommandResult("");
    }

    private CommandResult Tail(string[] args)
    {
        int count = 10;
        string? file = null;
        if (args.Length == 1) file = args[0];
        else if (args.Length == 3 && args[0] == "-n" && int.TryParse(args[1], out count) && count >= 0) file = args[2];
        else return Error("Использование: tail [-n количество] файл");
        var lines = ReadLines(file!);
        return new CommandResult(string.Join(Environment.NewLine, lines.TakeLast(count)));
    }

    private CommandResult Uniq(string[] args)
    {
        bool showCount = args.Length == 2 && args[0] == "-c";
        if ((!showCount && args.Length != 1) || (showCount && args.Length != 2))
            return Error("Использование: uniq [-c] файл");
        var lines = ReadLines(args[^1]);
        var output = new List<string>();
        for (int i = 0; i < lines.Count;)
        {
            int j = i + 1;
            while (j < lines.Count && lines[j] == lines[i]) j++;
            output.Add(showCount ? $"{j - i,4} {lines[i]}" : lines[i]);
            i = j;
        }
        return new CommandResult(string.Join(Environment.NewLine, output));
    }

    private CommandResult Tac(string[] args)
    {
        if (args.Length != 1) return Error("Использование: tac файл");
        var lines = ReadLines(args[0]);
        lines.Reverse();
        return new CommandResult(string.Join(Environment.NewLine, lines));
    }

    private CommandResult Chown(string[] args)
    {
        if (args.Length != 2) return Error("Использование: chown владелец путь");
        string path = vfs.Resolve(CurrentDirectory, args[1]);
        vfs.ChangeOwner(path, args[0]);
        return new CommandResult($"Владелец {path} изменён на {args[0]}.");
    }

    private List<string> ReadLines(string path)
    {
        string resolved = vfs.Resolve(CurrentDirectory, path);
        if (!vfs.TryGet(resolved, out var entry)) throw new InvalidOperationException($"Файл не найден: {resolved}");
        if (entry.Type != "file") throw new InvalidOperationException($"Это не файл: {resolved}");
        return entry.Content.Replace("\r\n", "\n").Split('\n').ToList() is var lines && lines.Count > 0 && lines[^1] == ""
            ? lines.Take(lines.Count - 1).ToList() : lines;
    }

    private static CommandResult Help(string[] args)
    {
        if (args.Length != 0) return Error("Использование: help");
        return new CommandResult("Команды: ls [путь], cd [путь], tail [-n N] файл, uniq [-c] файл, tac файл, chown владелец путь, pwd, whoami, help, exit");
    }

    private static CommandResult Error(string message) => new("Ошибка: " + message, false);
}
