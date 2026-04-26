using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// A callable tool that the LLM can invoke via tool call syntax.
/// </summary>
public interface ITool
{
    /// <summary>Unique name used in tool call blocks.</summary>
    string Name { get; }

    /// <summary>Short description shown in system prompt.</summary>
    string Description { get; }

    /// <summary>
    /// Execute the tool with the given arguments as a JSON element.
    /// Returns a string to inject back as the tool result.
    /// </summary>
    Task<string> RunAsync(JsonElement arguments, CancellationToken ct);
}
