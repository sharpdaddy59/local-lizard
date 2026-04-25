using System.Collections.Frozen;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// Registry of all available tools, keyed by name.
/// Thread-safe after construction.
/// </summary>
public sealed class ToolRegistry
{
    private readonly FrozenDictionary<string, ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToFrozenDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All registered tool names.</summary>
    public IReadOnlyCollection<string> ToolNames => _tools.Keys;

    /// <summary>
    /// Try to get a tool by name.
    /// </summary>
    public bool TryGet(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ITool? tool)
        => _tools.TryGetValue(name, out tool);

    /// <summary>
    /// Get a tool by name. Throws if not found.
    /// </summary>
    public ITool Get(string name) => _tools[name];

    /// <summary>
    /// Generate the system prompt section describing available tools.
    /// </summary>
    /// <summary>
    /// Generate the system prompt section describing available tools.
    /// Uses Gemma 4's native &lt;|tool&gt; declaration format.
    /// </summary>
    public string ToSystemPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following functions.");

        foreach (var tool in _tools.Values)
        {
            var escapedDesc = tool.Description
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");

            // Build parameters block from description hints.
            // The description typically looks like:
            // "Search the web for current information. Argument: q (search query). Example: q=weather in Dallas Texas"
            // We parse argument names out of it.
            var paramNames = ExtractArgumentNames(tool.Description);
            var paramsBlock = BuildParamsBlock(paramNames);

            sb.Append($"<|tool>declaration:{tool.Name}{{description:<|\"|>{escapedDesc}<|\"|>");
            if (!string.IsNullOrEmpty(paramsBlock))
                sb.Append($",parameters:{{{paramsBlock}}}");
            sb.AppendLine("}<tool|>");
        }

        sb.AppendLine();
        sb.AppendLine("To call a function, output: <|tool_call>call:function_name{args}<tool_call|>");
        sb.AppendLine("For example: <|tool_call>call:get_time{}<tool_call|>");
        sb.AppendLine("For multiple arguments: <|tool_call>call:search_web{query:<|\"|>weather<|\"|>}<tool_call|>");
        return sb.ToString();
    }

    /// <summary>
    /// Extract argument/parameter names from a tool's description text.
    /// Looks for patterns like "Argument: argName" or "key" in common formats.
    /// </summary>
    private static string[] ExtractArgumentNames(string description)
    {
        var names = new List<string>();
        var lines = description.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Match "Argument: name" or "Arguments: name1, name2"
            if (trimmed.StartsWith("Argument", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Arguments", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx >= 0)
                {
                    var afterColon = trimmed[(colonIdx + 1)..].Trim();
                    foreach (var part in afterColon.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // Get just the name (before '=' or '(' or space)
                        var name = part.Split('=', '(', ' ')[0].Trim().TrimEnd('.');
                        if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                            names.Add(name);
                    }
                }
            }
        }
        return names.ToArray();
    }

    /// <summary>
    /// Build the parameters block portion of a tool declaration.
    /// Returns a string like "key:{type:str},value:{type:str}" or empty string if no params.
    /// </summary>
    private static string BuildParamsBlock(string[] paramNames)
    {
        if (paramNames.Length == 0)
            return string.Empty;

        var parts = new List<string>();
        foreach (var name in paramNames)
        {
            parts.Add($"{name}:{{type:<|\"|>str<|\"|>}}");
        }
        return string.Join(",", parts);
    }
}
