using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools.Tools;

/// <summary>
/// Stores a fact as a key-value pair in a JSON file.
/// The parser supplies parsed args; this tool expects "key" and "value" keys.
/// </summary>
public sealed class RememberFactTool : ITool
{
    private readonly string _filePath;

    public string Name => "remember_fact";

    public string Description =>
        "Store a fact for later recall. Arguments: key (the fact name), value (the fact content). " +
        "Example: key=user_name, value=Alice.";

    public RememberFactTool() : this("/shared/projects/local-lizard/memory.json") { }

    public RememberFactTool(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task<string> RunAsync(string args, CancellationToken ct)
    {
        // Parse key=value from the raw args block
        string? key = null;
        string? value = null;
        var lines = args.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("key=", StringComparison.OrdinalIgnoreCase))
                key = trimmed[4..].Trim();
            else if (trimmed.StartsWith("value=", StringComparison.OrdinalIgnoreCase))
                value = trimmed[6..].Trim();
            else if (key is not null && value is null)
                // Continuation of value (multiline)
                value = (value ?? "") + "\n" + trimmed;
        }

        if (string.IsNullOrWhiteSpace(key))
            return "Error: remember_fact requires a key argument. Usage: key=fact_name, value=fact_content.";

        value ??= "";
        return await ExecuteAsync(key, value, ct);
    }

    /// <summary>
    /// Save a fact to the JSON store. Call this from the tool execution pipeline
    /// after the parser has extracted the key/value from the [TOOL_CALL] block.
    /// </summary>
    public async Task<string> ExecuteAsync(string key, string value, CancellationToken ct)
    {
        try
        {
            var dict = await LoadAllAsync(ct);
            dict[key] = value;
            await SaveAllAsync(dict, ct);
            return $"Remembered: {key} = {value}";
        }
        catch (Exception ex)
        {
            return $"Error saving fact: {ex.Message}";
        }
    }

    /// <summary>
    /// Look up a fact by key.
    /// </summary>
    public async Task<string?> LookupAsync(string key, CancellationToken ct)
    {
        var dict = await LoadAllAsync(ct);
        return dict.TryGetValue(key, out var val) ? val : null;
    }

    /// <summary>
    /// Load all facts from the JSON file.
    /// </summary>
    public async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var json = await File.ReadAllTextAsync(_filePath, ct);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get top N facts formatted for system prompt injection.
    /// </summary>
    public async Task<string> GetFormattedFactsAsync(CancellationToken ct, int maxFacts = 50)
    {
        var facts = await LoadAllAsync(ct);
        var top = facts.Take(maxFacts);
        var lines = top.Select(kv => $"- {kv.Key}: {kv.Value}");
        return string.Join("\n", lines);
    }

    private async Task SaveAllAsync(Dictionary<string, string> facts, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, ct);
    }
}
