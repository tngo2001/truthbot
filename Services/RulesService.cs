namespace Truthbot.Services;

/// <summary>
/// Read/write rules from rules.txt (one rule per line). Used as context for fb (rules) chat.
/// </summary>
public class RulesService
{
    private readonly string _path;
    private static readonly object _lock = new();

    public RulesService(string? path = null)
    {
        // Use current working directory (project root when you "dotnet run") so rules.txt next to .csproj works
        _path = path ?? Path.Combine(Directory.GetCurrentDirectory(), "rules.txt");
    }

    public string RulesFilePath => _path;

    /// <summary>Full file content.</summary>
    public string Read()
    {
        if (!File.Exists(_path))
            return "";
        lock (_lock)
        {
            try
            {
                return File.ReadAllText(_path).Trim();
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>Lines (non-empty).</summary>
    public IReadOnlyList<string> GetLines()
    {
        var content = Read();
        if (string.IsNullOrWhiteSpace(content))
            return [];
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>Append one rule (one line).</summary>
    public void Add(string rule)
    {
        rule = rule.Trim();
        if (string.IsNullOrEmpty(rule))
            return;
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        lock (_lock)
        {
            File.AppendAllText(_path, rule + Environment.NewLine);
        }
    }

    /// <summary>Remove rule by 1-based index. Returns true if removed.</summary>
    public bool Remove(int oneBasedIndex)
    {
        var lines = GetLines().ToList();
        if (oneBasedIndex < 1 || oneBasedIndex > lines.Count)
            return false;
        lines.RemoveAt(oneBasedIndex - 1);
        lock (_lock)
        {
            File.WriteAllText(_path, lines.Count > 0 ? string.Join(Environment.NewLine, lines) + Environment.NewLine : "");
        }
        return true;
    }

    /// <summary>Replace rule at 1-based index with new text. Returns true if updated.</summary>
    public bool Edit(int oneBasedIndex, string newText)
    {
        newText = newText.Trim();
        var lines = GetLines().ToList();
        if (oneBasedIndex < 1 || oneBasedIndex > lines.Count || string.IsNullOrEmpty(newText))
            return false;
        lines[oneBasedIndex - 1] = newText;
        lock (_lock)
        {
            File.WriteAllText(_path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        return true;
    }
}
