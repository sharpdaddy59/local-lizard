using System.Collections.Frozen;
using System.Text.Json;

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
    /// Uses Qwen 2.5's native JSON Schema tool declaration format.
    /// </summary>
    public string ToSystemPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following functions. Use them if required:");

        foreach (var tool in _tools.Values)
        {
            var schema = BuildToolSchema(tool);
            var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            sb.AppendLine(json);
        }

        sb.AppendLine();
        sb.AppendLine("To call a function, output: <tool_call>{\"name\": \"function_name\", \"arguments\": {}}</tool_call>");
        sb.AppendLine("For example: <tool_call>{\"name\": \"get_time\", \"arguments\": {}}</tool_call>");
        sb.AppendLine("For multiple arguments: <tool_call>{\"name\": \"search_web\", \"arguments\": {\"q\": \"weather in Dallas\"}}</tool_call>");
        return sb.ToString();
    }

    /// <summary>
    /// Build a JSON Schema-style tool declaration for a given tool.
    /// </summary>
    private static object BuildToolSchema(ITool tool)
    {
        var paramNames = ExtractArgumentNames(tool.Description);
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in paramNames)
        {
            properties[param.Name] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = param.Description
            };
            if (param.Required)
                required.Add(param.Name);
        }

        return new Dictionary<string, object>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required
                }
            }
        };
    }

    /// <summary>
    /// Extract argument/parameter names and descriptions from a tool's description text.
    /// </summary>
    private static List<(string Name, string Description, bool Required)> ExtractArgumentNames(string description)
    {
        var parameters = new List<(string Name, string Description, bool Required)>();
        var lines = description.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Match patterns like:
            // "Argument: q (search query)" or "Arguments: key, value" or
            // "Argument: key (the fact name)"
            if (trimmed.Contains("Argument:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("Arguments:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx >= 0)
                {
                    var afterColon = trimmed[(colonIdx + 1)..].Trim();
                    foreach (var part in afterColon.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // Extract name (before '=' or '(' or space)
                        var parenIdx = part.IndexOf('(');
                        var name = parenIdx >= 0
                            ? part[..parenIdx].Trim()
                            : part.Split('=', ' ')[0].Trim().TrimEnd('.');

                        // Extract description from parenthesized text
                        var desc = "";
                        if (parenIdx >= 0)
                        {
                            var closeParen = part.IndexOf(')', parenIdx);
                            if (closeParen >= 0)
                                desc = part[(parenIdx + 1)..closeParen].Trim();
                        }

                        if (!string.IsNullOrEmpty(name) && parameters.All(p => p.Name != name))
                            parameters.Add((name, desc, true));
                    }
                }
            }
        }
        return parameters;
    }
}
