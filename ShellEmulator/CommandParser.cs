namespace ShellEmulator;

internal static class CommandParser
{
    public static IReadOnlyList<string> Parse(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        bool escaping = false;

        foreach (char c in input)
        {
            if (escaping)
            {
                current.Append(c);
                escaping = false;
                continue;
            }

            if (c == '\\' && quote != '\'')
            {
                escaping = true;
                continue;
            }

            if ((c == '"' || c == '\'') && (quote == '\0' || quote == c))
            {
                quote = quote == '\0' ? c : '\0';
                continue;
            }

            if (char.IsWhiteSpace(c) && quote == '\0')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (escaping) current.Append('\\');
        if (quote != '\0') throw new FormatException("Не закрыта кавычка в команде.");
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
