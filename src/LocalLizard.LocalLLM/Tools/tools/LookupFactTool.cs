using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools.Tools;

/// <summary>
/// Looks up stored facts by natural language query.
/// Single argument: query (what to look up).
/// Searches across all stored fact keys and values for matches.
/// </summary>
public sealed class LookupFactTool : ITool
{
    private readonly RememberFactTool _memory;

    public string Name => "lookup_fact";

    public string Description =>
        "Look up a fact from memory. Single argument: query (what to look for, in natural language). " +
        "Example: query=What is my name";

    public LookupFactTool(RememberFactTool memory)
    {
        _memory = memory;
    }

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken ct)
    {
        // Extract the single "query" argument
        if (arguments.TryGetProperty("query", out var queryEl))
        {
            var query = queryEl.GetString();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var result = await _memory.LookupAsync(query, ct);
                return result is not null
                    ? result
                    : $"I don't remember anything about '{query}'.";
            }
        }
        return "Error: lookup_fact requires a query argument. Example: query=What is my name";
    }
}
