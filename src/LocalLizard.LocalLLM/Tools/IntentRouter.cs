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
    /// Runs the handler and only returns if the handler returns a non-null result.
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

        // ── weather ──
        // "weather in {place}", "forecast {place}", "what's the weather in {place}"
        Register(new Intent
        {
            Name = "weather",
            Matches = m => WeatherPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = WeatherPattern().Match(m);
                var place = match.Groups["place"].Value.Trim();
                if (string.IsNullOrEmpty(place))
                {
                    // No place specified — fall through to LLM
                    return null;
                }
                if (!tools.TryGet("search_web", out var tool))
                    return null;
                var result = await RunTool(tool, ("q", $"weather in {place}"), ct);
                return $"Here's the weather for {place}: {result}";
            },
        });

        // ── math ──
        // "what is {expression}", "calculate {expression}"
        Register(new Intent
        {
            Name = "math",
            Matches = m => MathPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = MathPattern().Match(m);
                var expr = match.Groups["expr"].Value.Trim();
                if (string.IsNullOrEmpty(expr))
                    return null;

                // Simplify expression for bc: replace common tokens
                expr = expr
                    .Replace('x', '*')
                    .Replace('×', '*')
                    .Replace('÷', '/')
                    .Replace("plus", "+")
                    .Replace("minus", "-")
                    .Replace("times", "*")
                    .Replace("divided by", "/")
                    .TrimEnd('?', '.', '!');

                // Sanitize: only allow digits, operators, parens, spaces, decimal
                if (!IsSafeMathExpression(expr))
                    return null; // fall through to LLM if expression looks unsafe

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/bc",
                        Arguments = "",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var proc = new Process { StartInfo = psi };
                    proc.Start();
                    await proc.StandardInput.WriteLineAsync(expr);
                    proc.StandardInput.Close();
                    var output = await proc.StandardOutput.ReadToEndAsync(ct);
                    var result = output.Trim();

                    if (decimal.TryParse(result, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var dec))
                    {
                        return $"{expr} = {dec:G}";
                    }
                    return $"{expr} = {result}";
                }
                catch
                {
                    return null; // fall through to LLM
                }
            },
        });

        // ── web search (broad) ──
        // "what is {topic}", "what's a {topic}", "how to {topic}", "what does {topic} mean"
        // Registered AFTER weather and math so they get first crack at "what is X" queries
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

        // ── lookup patterns (split into separate intents) ──

        // "what do you know about {topic}"
        Register(new Intent
        {
            Name = "lookup_fact",
            Matches = m => LookupGeneralPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = LookupGeneralPattern().Match(m);
                var topic = match.Groups["topic"].Value.Trim().TrimEnd('?', '.', '!', ':', ';', ',');
                if (string.IsNullOrEmpty(topic))
                    return null;
                if (!tools.TryGet("lookup_fact", out var tool))
                    return null;
                return await RunTool(tool, ("query", topic), ct);
            },
        });

        // "tell me about {topic}"
        Register(new Intent
        {
            Name = "lookup_fact",
            Matches = m => LookupTellPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = LookupTellPattern().Match(m);
                var topic = match.Groups["topic"].Value.Trim().TrimEnd('?', '.', '!', ':', ';', ',');
                if (string.IsNullOrEmpty(topic))
                    return null;
                if (!tools.TryGet("lookup_fact", out var tool))
                    return null;
                return await RunTool(tool, ("query", topic), ct);
            },
        });

        // "look up [in memory] [the] {topic}" (no overlap with search_web — "look up" removed from SearchPattern)
        Register(new Intent
        {
            Name = "lookup_fact",
            Matches = m => LookupMemoryPattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = LookupMemoryPattern().Match(m);
                var topic = match.Groups["topic"].Value.Trim().TrimEnd('?', '.', '!', ':', ';', ',');
                if (string.IsNullOrEmpty(topic))
                    return null;
                if (!tools.TryGet("lookup_fact", out var tool))
                    return null;
                return await RunTool(tool, ("query", topic), ct);
            },
        });

        // "what is/are/was my/your {topic}" (possessive only — no bare "what is X")
        Register(new Intent
        {
            Name = "lookup_fact",
            Matches = m => LookupPossessivePattern().IsMatch(m),
            Handler = async (m, tools, ct) =>
            {
                var match = LookupPossessivePattern().Match(m);
                var topic = match.Groups["topic"].Value.Trim().TrimEnd('?', '.', '!', ':', ';', ',');
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

    // ── Math safety ──

    /// <summary>
    /// Only allow digits, basic operators, parens, spaces, and decimal point.
    /// Prevents shell injection through bc.
    /// </summary>
    private static bool IsSafeMathExpression(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return false;
        foreach (var c in expr)
        {
            if (!char.IsDigit(c) && c != '+' && c != '-' && c != '*' && c != '/'
                && c != '(' && c != ')' && c != ' ' && c != '.' && c != '%')
                return false;
        }
        return true;
    }

    // ── Tool execution helpers ──

    /// <summary>
    /// Build a JSON arguments dictionary from key-value pairs and run the tool.
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

    /// <summary>
    /// "weather in {place}", "forecast {place}", "what's the weather in {place}"
    /// Captures the place name. If no place specified, handler returns null (falls through).
    /// </summary>
    [GeneratedRegex(@"(?:weather|forecast)\s+(?:in |for |at |)(?<place>.+)|what(?:'s| is) the weather (?:in |for |at |)(?<place>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WeatherPattern();

    /// <summary>
    /// "what is {expression}", "calculate {expression}"
    /// Must NOT match possessive patterns ("what is my X") — those go to lookup_fact.
    /// The 'what is' branch requires the expression to start with a digit or opening paren.
    /// </summary>
    [GeneratedRegex(@"(?:what(?:'s| is)\s+(?=\d|\()(?<expr>.+))|(?:calculate\s+(?<expr2>.+))|(?:calc\s+(?<expr3>.+))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MathPattern();

    /// <summary>
    /// Broad web search queries that don't fit weather, math, or memory lookup.
    /// - "search for {q}", "find {q}", "google {q}"
    /// - "what's a {q}" (general knowledge)
    /// - "how to {q}" (procedural)
    /// - "what does {q} mean"
    /// NOTE: "look up X" is NOT here — those are memory operations.
    /// </summary>
    [GeneratedRegex(@"(?:search (?:for |)(?<q>.+))|(?:find (?<q>.+))|(?:google (?<q>.+))|what(?:'s| is) a (?:n |)(?<q>.+)|how (?:do |does |can |would |to |)(?<q>.+)|what does (?<q>.+) mean", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SearchPattern();

    [GeneratedRegex(@"remember (?:that |)(?<fact>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RememberPattern();

    /// <summary>
    /// "what do you know about {topic}"
    /// </summary>
    [GeneratedRegex(@"what do you know about (?<topic>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LookupGeneralPattern();

    /// <summary>
    /// "tell me about {topic}"
    /// </summary>
    [GeneratedRegex(@"tell me about (?<topic>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LookupTellPattern();

    /// <summary>
    /// "look up [in memory] [the] {topic}"
    /// </summary>
    [GeneratedRegex(@"look up (?:in memory |)(?:the |)(?<topic>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LookupMemoryPattern();

    /// <summary>
    /// "what is/are/was my/your {topic}" (possessive only).
    /// Does NOT match bare "what is X" — those go to math (if numeric) or LLM.
    /// </summary>
    [GeneratedRegex(@"what (?:is|are|was) (?:my |your )(?<topic>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LookupPossessivePattern();

    [GeneratedRegex(@"run (?:shell |)(?:command |)(?<cmd>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ShellPattern();
}
