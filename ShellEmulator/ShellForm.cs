using System.Drawing;

namespace ShellEmulator;

internal sealed class ShellForm : Form
{
    private readonly RichTextBox output = new();
    private readonly TextBox input = new();
    private readonly ShellEngine engine;
    private readonly StartupOptions options;

    public ShellForm(StartupOptions startupOptions)
    {
        options = startupOptions;
        Text = $"Эмулятор - [{Environment.UserName}@{Environment.MachineName}]";
        Width = 900;
        Height = 620;
        MinimumSize = new Size(650, 420);
        BackColor = Color.FromArgb(20, 22, 27);

        output.Dock = DockStyle.Fill;
        output.ReadOnly = true;
        output.BackColor = Color.FromArgb(20, 22, 27);
        output.ForeColor = Color.FromArgb(220, 225, 230);
        output.Font = new Font("Consolas", 11);
        output.BorderStyle = BorderStyle.None;
        output.WordWrap = false;

        input.Dock = DockStyle.Bottom;
        input.Font = new Font("Consolas", 11);
        input.BackColor = Color.FromArgb(35, 38, 45);
        input.ForeColor = Color.White;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Margin = new Padding(10);
        input.KeyDown += Input_KeyDown;

        Controls.Add(output);
        Controls.Add(input);

        var fs = VirtualFileSystem.CreateDefault();
        if (!string.IsNullOrWhiteSpace(options.VfsPath))
        {
            try { fs.LoadCsv(options.VfsPath); }
            catch (Exception ex) { AppendLine("Ошибка загрузки VFS: " + ex.Message); }
        }
        engine = new ShellEngine(fs);

        AppendLine("Shell Emulator — вариант 8");
        AppendLine($"Пользователь: {Environment.UserName}");
        AppendLine($"Компьютер: {Environment.MachineName}");
        AppendLine($"Путь к VFS: {options.VfsPath ?? "(встроенная демонстрационная VFS)"}");
        AppendLine($"Стартовый скрипт: {options.ScriptPath ?? "(не задан)"}");
        AppendLine("Введите help для списка команд.");
        AppendLine("");

        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(options.ScriptPath))
            {
                try
                {
                    foreach (var (command, result) in engine.RunScript(options.ScriptPath))
                    {
                        if (command.Length > 0) AppendLine(Prompt() + command);
                        if (result.Output.Length > 0) AppendLine(result.Output);
                        if (!result.Success) break;
                        if (result.Exit) break;
                    }
                }
                catch (Exception ex) { AppendLine("Ошибка стартового скрипта: " + ex.Message); }
            }
            input.Focus();
        };
    }

    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        string command = input.Text;
        input.Clear();
        AppendLine(Prompt() + command);
        var result = engine.Execute(command);
        if (result.Output.Length > 0) AppendLine(result.Output);
        if (result.Exit) Close();
        input.Focus();
    }

    private string Prompt() => $"student@vfs:{engine.CurrentDirectory}$ ";

    private void AppendLine(string text)
    {
        output.AppendText(text + Environment.NewLine);
        output.SelectionStart = output.TextLength;
        output.ScrollToCaret();
    }
}
