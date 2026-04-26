namespace LocalLizard.LocalLLM;

/// <summary>
/// A single message in a chat conversation.
/// Role is a string ("system", "user", "assistant", "tool") compatible
/// with LLamaTemplate.Add(role, content).
/// </summary>
public sealed record ChatMessage(
    string Role,
    string Content)
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";

    public override string ToString()
        => $"{Role}: {Content}";
}
