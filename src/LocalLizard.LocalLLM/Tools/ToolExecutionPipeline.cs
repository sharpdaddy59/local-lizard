using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// Orchestrates tool call parsing and execution.
/// After LLM generates output, call ProcessOutput() to detect and run tools.
/// </summary>
public sealed class ToolExecutionPipeline
{
    private readonly ToolRegistry _registry;

    /// <summary>The underlying tool registry.</summary>
    public ToolRegistry Tools => _registry;

    public ToolExecutionPipeline(ToolRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Process LLM output: check for tool calls, execute them, return results.
    /// Returns null if no tool calls found.
    /// </summary>
    public async Task<ToolExecutionResult?> ProcessOutputAsync(string llmOutput, CancellationToken ct)
    {
        if (!ToolCallParser.HasToolCall(llmOutput))
            return null;

        var calls = ToolCallParser.Parse(llmOutput);
        if (calls.Count == 0)
            return null;

        var results = new List<ToolResult>();

        foreach (var (name, arguments) in calls)
        {
            if (!_registry.TryGet(name, out var tool))
            {
                results.Add(new ToolResult(name, "error", $"Unknown tool: {name}"));
                continue;
            }

            try
            {
                var output = await tool.RunAsync(arguments, ct);
                var truncated = System.Text.Encoding.UTF8.GetByteCount(output) > 2000;
                var displayOutput = ToolCallParser.TruncateResult(output);
                results.Add(new ToolResult(name, "ok", displayOutput, truncated));
            }
            catch (Exception ex)
            {
                results.Add(new ToolResult(name, "error", ex.Message));
            }
        }

        return new ToolExecutionResult(
            CleanOutput: ToolCallParser.StripToolCalls(llmOutput),
            Results: results
        );
    }

    public sealed record ToolResult(string Name, string Status, string Output, bool Truncated = false);

    public sealed record ToolExecutionResult(string CleanOutput, List<ToolResult> Results);
}
