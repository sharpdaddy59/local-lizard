using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools.Tools;

/// <summary>
/// Stores a fact from natural language text. Accepts a single "memory" argument
/// (e.g., "my name is Wily", "user_name = Alice") and either extracts key=value
/// or stores the full sentence as a fact.
/// </summary>
public sealed class RememberFactTool : ITool
{
    private readonly string _filePath;

    public string Name => "remember_fact";

    public string Description =>
        "Remember a fact from natural language. Single argument: memory (what to remember). " +
        "Example: memory=My name is Wily";

    public RememberFactTool() : this("/shared/projects/local-lizard/memory.json") { }

    public RememberFactTool(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken ct)
    {
        // Extract the single "memory" argument
        string? memory = null;
        if (arguments.TryGetProperty("memory", out var memEl))
            memory = memEl.GetString();

        if (string.IsNullOrWhiteSpace(memory))
            return "Error: remember_fact requires a memory argument. Example: memory=My name is Wily";

        return await ExecuteAsync(memory, ct);
    }

    /// <summary>
    /// Save a fact. If it looks like key=value, parse it. Otherwise generate a key
    /// from the first few words and store the full text as value.
    /// </summary>
    public async Task<string> ExecuteAsync(string memory, CancellationToken ct)
    {
        try
        {
            var dict = await LoadAllAsync(ct);
            var (key, value) = ParseMemory(memory);
            dict[key] = value;
            await SaveAllAsync(dict, ct);
            return $"Remembered: {value}";
        }
        catch (Exception ex)
        {
            return $"Error saving fact: {ex.Message}";
        }
    }

    /// <summary>
    /// Look up a fact by key.
    /// </summary>
    public async Task<string?> LookupAsync(string query, CancellationToken ct)
    {
        var dict = await LoadAllAsync(ct);

        // Direct key match first (try both original and underscore-normalized)
        if (dict.TryGetValue(query, out var val))
            return val;
        var queryWithUnderscores = query.Replace(' ', '_');
        if (queryWithUnderscores != query && dict.TryGetValue(queryWithUnderscores, out val))
            return val;

        // Search through keys and values for matches
        // Normalize both query and stored data (underscores ↔ spaces) for fuzzy matching
        var results = new List<string>();
        var q = query.ToLowerInvariant();
        var qNormalized = q.Replace(' ', '_');
        foreach (var (k, v) in dict)
        {
            var kLower = k.ToLowerInvariant();
            var vLower = v.ToLowerInvariant();
            if (kLower.Contains(q) || kLower.Contains(qNormalized) ||
                vLower.Contains(q) || vLower.Contains(qNormalized))
                results.Add($"{k}: {v}");
        }

        return results.Count > 0
            ? string.Join("\n", results)
            : null;
    }

    /// <summary>
    /// Parse a natural language memory string into key/value.
    /// Supports: "key=value", "key is value", "my key is value", or auto-generates key.
    /// </summary>
    private static (string key, string value) ParseMemory(string memory)
    {
        memory = memory.Trim();

        // Pattern 1: key=value
        var eqIdx = memory.IndexOf('=');
        if (eqIdx > 0)
        {
            var k = memory[..eqIdx].Trim().ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("my_", "", StringComparison.OrdinalIgnoreCase);
            var v = memory[(eqIdx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                return (k, v);
        }

        // Pattern 2: "my <key> is <value>" or "<key> is <value>"
        // e.g., "my name is Wily" → key="name", value="Wily"
        // e.g., "favorite color is blue" → key="favorite_color", value="blue"
        var isIdx = memory.IndexOf(" is ", StringComparison.OrdinalIgnoreCase);
        if (isIdx > 0)
        {
            var before = memory[..isIdx].Trim();
            var after = memory[(isIdx + 4)..].Trim();

            // Strip "my " prefix
            if (before.StartsWith("my ", StringComparison.OrdinalIgnoreCase))
                before = before[3..].Trim();

            if (!string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(after))
            {
                var key = before.Replace(" ", "_").ToLowerInvariant();
                return (key, after);
            }
        }

        // Pattern 3: Auto-generate key from first 3 words
        var words = memory.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var autoKey = words.Length >= 3
            ? string.Join("_", words.Take(3)).ToLowerInvariant()
            : words.Length >= 1
                ? words[0].ToLowerInvariant()
                : "fact";

        return (autoKey, memory);
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
