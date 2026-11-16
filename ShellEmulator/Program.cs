using System.Windows.Forms;

namespace ShellEmulator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = StartupOptions.Parse(args);
        Application.Run(new ShellForm(options));
    }
}

internal sealed record StartupOptions(string? VfsPath, string? ScriptPath)
{
    public static StartupOptions Parse(string[] args)
    {
        string? vfs = null;
        string? script = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vfs" when i + 1 < args.Length:
                    vfs = args[++i];
                    break;
                case "--script" when i + 1 < args.Length:
                    script = args[++i];
                    break;
                case "--help":
                    MessageBox.Show("Параметры запуска:\n--vfs <путь к CSV>\n--script <путь к стартовому скрипту>",
                        "Shell Emulator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default:
                    throw new ArgumentException($"Неизвестный или неполный параметр: {args[i]}");
            }
        }

        return new StartupOptions(vfs, script);
    }
}
