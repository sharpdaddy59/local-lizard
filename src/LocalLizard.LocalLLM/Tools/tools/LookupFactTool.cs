namespace LocalLizard.LocalLLM.Tools.Tools;

/// <summary>
/// Looks up a stored fact by key from the memory JSON file.
/// Pairs with RememberFactTool.
/// </summary>
public sealed class LookupFactTool : ITool
{
    private readonly RememberFactTool _memory;

    public string Name => "lookup_fact";

    public string Description =>
        "Look up a previously stored fact. Argument: key (the fact name to retrieve). " +
        "Example: key=user_name";

    public LookupFactTool(RememberFactTool memory)
    {
        _memory = memory;
    }

    public async Task<string> RunAsync(string args, CancellationToken ct)
    {
        // Parse the "key" from args
        // The args parameter is the raw block text. We parse it here for simplicity.
        var lines = args.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("key=", StringComparison.OrdinalIgnoreCase))
            {
                var key = trimmed[4..].Trim();
                var value = await _memory.LookupAsync(key, ct);
                return value is not null
                    ? $"{key}: {value}"
                    : $"No fact found for '{key}'.";
            }
        }
        return "Error: lookup_fact requires a key argument. Usage: key=fact_name";
    }
}
