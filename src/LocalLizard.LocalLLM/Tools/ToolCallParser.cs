using System.Text;
using System.Text.Json;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// Parses Qwen 2.5's native tool call blocks from model output.
///
/// Tool call format (model output):
///   <![CDATA[<tool_call>{"name": "get_time", "arguments": {}}<tool_call>]]>
///
/// Multiple tool calls can appear in a single output.
/// The parser returns structured (name, arguments) tuples.
///
/// <see cref="TruncateResult"/> is preserved as a utility method used by tools.
/// </summary>
public sealed class ToolCallParser
{
    private const string OpenTag = "<tool_call>";
    private const string CloseTag = "</tool_call>";
    private const int OpenTagLen = 11;

    /// <summary>
    /// Extract all tool calls from LLM output. Returns empty list if none found.
    /// Accepts both <c>&lt;/tool_call&gt;</c> (proper XML) and <c>&lt;tool_call&gt;</c>
    /// (Qwen sometimes reuses the open tag) as close markers. The JSON content is
    /// bounded by the open tag and whichever close marker comes first.
    /// </summary>
    public static List<(string Name, JsonElement Arguments)> Parse(string output)
    {
        var results = new List<(string Name, JsonElement Arguments)>();
        if (string.IsNullOrEmpty(output))
            return results;

        var searchStart = 0;
        while (true)
        {
            var openIdx = output.IndexOf(OpenTag, searchStart, StringComparison.Ordinal);
            if (openIdx < 0)
                break;

            var jsonStart = openIdx + OpenTagLen;

            // Find the close marker: prefer </tool_call>, fallback to next <tool_call>
            var closeSlash = output.IndexOf(CloseTag, jsonStart, StringComparison.Ordinal);
            var closeSame = output.IndexOf(OpenTag, jsonStart, StringComparison.Ordinal);

            int closeIdx;
            if (closeSlash >= 0 && (closeSame < 0 || closeSlash < closeSame))
                closeIdx = closeSlash;
            else if (closeSame >= 0)
                closeIdx = closeSame;
            else
                break; // No close marker at all

            var jsonText = output[jsonStart..closeIdx].Trim();
            if (string.IsNullOrEmpty(jsonText))
            {
                searchStart = closeIdx + (closeIdx == closeSlash ? CloseTag.Length : OpenTagLen);
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                if (root.TryGetProperty("name", out var nameEl) &&
                    root.TryGetProperty("arguments", out var argsEl))
                {
                    var name = nameEl.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        results.Add((name, argsEl.Clone()));
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed tool call blocks
            }

            searchStart = closeIdx + (closeIdx == closeSlash ? CloseTag.Length : OpenTagLen);
        }

        return results;
    }

    /// <summary>
    /// Check if the output contains any tool call at all (fast path).
    /// </summary>
    public static bool HasToolCall(string output)
        => output.Contains(OpenTag, StringComparison.Ordinal);

    /// <summary>
    /// Truncate tool result to ~2KB at paragraph boundaries.
    /// </summary>
    public static string TruncateResult(string text, int maxBytes = 2048)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;

        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        var truncated = false;

        foreach (var para in paragraphs)
        {
            var candidate = sb.Length == 0 ? para : sb.ToString() + "\n\n" + para;
            if (Encoding.UTF8.GetByteCount(candidate) > maxBytes)
            {
                truncated = true;
                break;
            }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(para);
        }

        return truncated ? sb.ToString() + "\n\n[...truncated]" : sb.ToString();
    }

    /// <summary>
    /// Strip all <tool_call>...</tool_call> blocks from output.
    /// Returns any remaining text (text before/after/between tool calls).
    /// Accepts both <c>&lt;/tool_call&gt;</c> and <c>&lt;tool_call&gt;</c> as close marker.
    /// </summary>
    public static string StripToolCalls(string output)
    {
        if (string.IsNullOrEmpty(output))
            return output;

        var sb = new StringBuilder();
        var searchStart = 0;

        while (true)
        {
            var openIdx = output.IndexOf(OpenTag, searchStart, StringComparison.Ordinal);
            if (openIdx < 0)
            {
                // No more tool calls — append remaining text
                sb.Append(output.AsSpan(searchStart));
                break;
            }

            // Append text before this tool call
            if (openIdx > searchStart)
                sb.Append(output.AsSpan(searchStart, openIdx - searchStart));

            var jsonStart = openIdx + OpenTagLen;

            // Find the close marker: prefer </tool_call>, fallback to next <tool_call>
            var closeSlash = output.IndexOf(CloseTag, jsonStart, StringComparison.Ordinal);
            var closeSame = output.IndexOf(OpenTag, jsonStart, StringComparison.Ordinal);

            int closeIdx;
            int closeLen;
            if (closeSlash >= 0 && (closeSame < 0 || closeSlash < closeSame))
            {
                closeIdx = closeSlash;
                closeLen = CloseTag.Length;
            }
            else if (closeSame >= 0)
            {
                closeIdx = closeSame;
                closeLen = OpenTagLen;
            }
            else
            {
                // Malformed — no closing tag, skip ahead
                searchStart = jsonStart;
                continue;
            }

            searchStart = closeIdx + closeLen;
        }

        return sb.ToString().Trim();
    }
}
