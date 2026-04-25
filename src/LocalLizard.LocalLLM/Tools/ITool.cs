namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// A callable tool that the LLM can invoke via [TOOL_CALL] syntax.
/// </summary>
public interface ITool
{
    /// <summary>Unique name used in [TOOL_CALL] blocks.</summary>
    string Name { get; }

    /// <summary>Short description shown in system prompt.</summary>
    string Description { get; }

    /// <summary>
    /// Execute the tool with the given arguments (raw string after name=value parsing).
    /// Returns a string to inject back as [TOOL_RESULT].
    /// </summary>
    Task<string> RunAsync(string args, CancellationToken ct);
}
