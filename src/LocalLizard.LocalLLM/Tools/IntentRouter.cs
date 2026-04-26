using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// Deterministic intent router for common queries. Uses regex/pattern matching
/// to handle common requests without LLM inference.
/// Falls through to the LLM if no pattern matches.
/// </summary>
public sealed partial class IntentRouter
{
    private sealed class Intent
    {
        public required string Name { get; init; }
        /// <summary>Returns response text if matched, null to skip to next intent.</summary>
        public required Func<string, ToolRegistry, CancellationToken, Task<string?>> Handler { get; init; }
        public required Func<string, bool> Matches { get; init; }
    }

    private readonly List<Intent> _intents = [];

    /// <summary>
    /// Create an intent router and register built-in patterns.
    /// </summary>
    public IntentRouter()
    {
        RegisterBuiltinIntents();
    }

    /// <summary>
    /// Try to handle a user query without LLM inference.
    /// Returns the response text if matched, null if no intent matched (fall through to LLM).
    /// </summary>
    public async Task<string?> TryRouteAsync(
        string userMessage,
        ToolRegistry toolRegistry,
        CancellationToken ct)
    {
        var trimmed = userMessage.Trim();
        var lower = trimmed.ToLowerInvariant();

        foreach (var intent in _intents)
        {
            if (intent.Matches(lower))
            {
                var sw = Stopwatch.StartNew();
                var result = await intent.Handler(lower, toolRegistry, ct);
                sw.Stop();
                if (result is not null)
                {
                    Debug.WriteLine($"[Router] {intent.Name} matched in {sw.Elapsed.TotalMilliseconds:F0}ms");
                    return result;
                }
                // Handler returned null — skip to next intent
            }
        }

        return null;
    }

    private void RegisterBuiltinIntents()
    {
        // ── get_time (direct, no tool needed) ──
        Register(new Intent
        {
            Name = "get_time",
            Matches = m => TimePattern().IsMatch(m),
            Handler = (_, _, _) =>
            {
                var now = DateTimeOffset.Now;
                var local = now.ToString("dddd, MMMM d, yyyy 'at' h:mm tt");
                return Task.FromResult<string?>($"The current time is {local}.");
            },
        });

        // ── search_web ──
        Register(new Intent
        {
            Name = "search_web",
            Matches = m => SearchPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = SearchPattern().Match(m);
                var query = match.Groups["q"].Value.Trim();
                if (string.IsNullOrEmpty(query))
                    return null;
                if (!tools.TryGet("search_web", out var tool))
                    return null;
                return await formatToolResult(tool, $"q:{query}", ct);
            },
        });

        // ── remember ──
        Register(new Intent
        {
            Name = "remember_fact",
            Matches = m => RememberPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = RememberPattern().Match(m);
                var fact = match.Groups["fact"].Value.Trim();
                if (string.IsNullOrEmpty(fact))
                    return null;
                if (!tools.TryGet("remember_fact", out var tool))
                    return null;
                return await formatToolResult(tool, $"memory:{fact}", ct);
            },
        });

        // ── lookup ──
        Register(new Intent
        {
            Name = "lookup_fact",
            Matches = m => LookupPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = LookupPattern().Match(m);
                var topic = match.Groups["topic"].Value.Trim().TrimEnd('?', '.', '!', ':', ';', ',');
                if (string.IsNullOrEmpty(topic))
                    return null;
                if (!tools.TryGet("lookup_fact", out var tool))
                    return null;
                return await formatToolResult(tool, $"query:{topic}", ct);
            },
        });

        // ── run_shell (admin only — emits tool call for safety, doesn't auto-execute) ──
        Register(new Intent
        {
            Name = "run_shell",
            Matches = m => ShellPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = ShellPattern().Match(m);
                var cmd = match.Groups["cmd"].Value.Trim();
                if (string.IsNullOrEmpty(cmd))
                    return null;
                if (!tools.TryGet("run_shell", out var tool))
                    return null;
                return await formatToolResult(tool, $"command:{cmd}", ct);
            },
        });
    }

    /// <summary>
    /// Build a JSON arguments object from a simple key:value string and execute the tool.
    /// </summary>
    private static async Task<string> formatToolResult(ITool tool, string keyValue, CancellationToken ct)
    {
        var colonIdx = keyValue.IndexOf(':');
        if (colonIdx > 0)
        {
            var key = keyValue[..colonIdx].Trim();
            var value = keyValue[(colonIdx + 1)..].Trim();
            var json = JsonSerializer.Serialize(new Dictionary<string, string> { [key] = value });
            using var doc = JsonDocument.Parse(json);
            return await tool.RunAsync(doc.RootElement, ct);
        }
        using var emptyDoc = JsonDocument.Parse("{}");
        return await tool.RunAsync(emptyDoc.RootElement, ct);
    }

    private void Register(Intent intent)
    {
        _intents.Add(intent);
    }

    // ── Compiled regex patterns ──

    [GeneratedRegex(@"^(what(('s| is) the| time is it)|current time|time now)\s*\??$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"(?:search (?:for |)(?<q>.+))|(?:look up (?<q>.+))|(?:find (?<q>.+))|(?:google (?<q>.+))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SearchPattern();

    [GeneratedRegex(@"remember (?:that |)(?<fact>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RememberPattern();

    [GeneratedRegex(@"(?:what do you know about|tell me about|look up (?:in memory |)(?:the )?|what (?:is|are|was) (?:my |the |)(?<topic>.+))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LookupPattern();

    [GeneratedRegex(@"run (?:shell |)(?:command |)(?<cmd>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ShellPattern();
}
