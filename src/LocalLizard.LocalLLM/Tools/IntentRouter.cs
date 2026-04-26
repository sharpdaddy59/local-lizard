using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// Deterministic intent router for common queries. Uses regex/pattern matching
/// to handle common requests without LLM inference.
///
/// Each intent registers a pattern and a handler. Handlers that return null
/// cause the router to try the next intent. If all intents return null,
/// the query falls through to the LLM.
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
    ///
    /// Unlike the original "match first, return always" approach, this version
    /// runs the handler and only returns if the handler returns a non-null result.
    /// A handler that returns null causes the next intent to be tried.
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
            if (!intent.Matches(lower))
                continue;

            var sw = Stopwatch.StartNew();
            var result = await intent.Handler(lower, toolRegistry, ct);
            sw.Stop();

            if (result is null)
                continue; // Handler declined — try next intent

            Debug.WriteLine($"[Router] {intent.Name} matched in {sw.Elapsed.TotalMilliseconds:F0}ms");
            return result;
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
                return await RunTool(tool, ("q", query), ct);
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
                return await RunTool(tool, ("memory", fact), ct);
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
                    topic = match.Groups["topic2"].Value.Trim().TrimEnd('?', '.', '!', ':', ';', ',');
                if (string.IsNullOrEmpty(topic))
                    return null;
                if (!tools.TryGet("lookup_fact", out var tool))
                    return null;
                return await RunTool(tool, ("query", topic), ct);
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
                return await RunTool(tool, ("command", cmd), ct);
            },
        });
    }

    /// <summary>
    /// Build a JSON arguments dictionary from key-value pairs and run the tool.
    /// Replaces the old colon-split parsing with structured JSON construction.
    /// </summary>
    private static async Task<string> RunTool(ITool tool, (string key, string value) arg, CancellationToken ct)
    {
        var dict = new Dictionary<string, string> { [arg.key] = arg.value };
        var json = JsonSerializer.Serialize(dict);
        using var doc = JsonDocument.Parse(json);
        return await tool.RunAsync(doc.RootElement, ct);
    }

    /// <summary>
    /// Overload with two arguments for tools that need multiple parameters.
    /// </summary>
    private static async Task<string> RunTool(ITool tool, (string key, string value) arg1, (string key, string value) arg2, CancellationToken ct)
    {
        var dict = new Dictionary<string, string>
        {
            [arg1.key] = arg1.value,
            [arg2.key] = arg2.value,
        };
        var json = JsonSerializer.Serialize(dict);
        using var doc = JsonDocument.Parse(json);
        return await tool.RunAsync(doc.RootElement, ct);
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

    /// <summary>
    /// Matches memory-related lookup phrases. Only matches "what is my/your" (possessives),
    /// not "what is the" or bare "what is X". "what is 7 x 23" no longer triggers memory lookup.
    /// </summary>
    [GeneratedRegex(@"(?:what do you know about|tell me about|look up (?:in memory |)(?:the )?)(?<topic>.+)|what (?:is|are|was) (?:my |your )(?<topic2>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LookupPattern();

    [GeneratedRegex(@"run (?:shell |)(?:command |)(?<cmd>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ShellPattern();


}
