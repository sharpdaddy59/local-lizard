using System.Text.RegularExpressions;

namespace LocalLizard.LocalLLM.Tools;

/// <summary>
/// Parses Gemma 4's native tool call blocks from model output.
///
/// Tool call format (model output):
///   <![CDATA[<|tool_call>call:tool_name{key1:val1,key2:val2}<tool_call|>]]>
///
/// Strings are wrapped with <![CDATA[<|"|>text<|"|>]]> delimiters.
/// Arguments with no value: <![CDATA[<|tool_call>call:get_time{}<tool_call|>]]>
///
/// The model stops generation on EOG token 50 (<![CDATA[<|tool_response>]]>).
/// We inject results using the same format as response blocks.
///
/// Result injection format:
///   <![CDATA[<|tool_response>response:tool_name{key:val}<tool_response|>]]>
/// </summary>
public sealed class ToolCallParser
{
    // Matches <|tool_call>call:name{...}<tool_call|> blocks
    // Tool name: word characters after "call:"
    // Arguments: everything between { } — but we need a more careful match
    // since { } can nest and <|"|> delimiters contain { } too
    private static readonly Regex ToolCallRegex = new(
        @"<\|tool_call\>call:(\w+)\{(.*?)\}<tool_call(?:\|>)?",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// A single parsed tool invocation.
    /// </summary>
    public sealed record ToolCall(string Name, Dictionary<string, string> Args);

    /// <summary>
    /// Extract all tool calls from LLM output. Returns empty list if none found.
    /// </summary>
    public static List<ToolCall> Parse(string output)
    {
        var results = new List<ToolCall>();

        var matches = ToolCallRegex.Matches(output);
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var args = ParseArgs(match.Groups[2].Value);
            results.Add(new ToolCall(name, args));
        }

        return results;
    }

    /// <summary>
    /// Check if the output contains any tool call at all (fast path).
    /// </summary>
    public static bool HasToolCall(string output)
        => output.Contains("<|tool_call>", StringComparison.Ordinal);

    /// <summary>
    /// Strip all tool call and tool response blocks from output.
    /// Also strips <![CDATA[<|turn>]]>, <![CDATA[<turn|>]]>, and
    /// <![CDATA[<|channel>]]> / <![CDATA[<channel|>]]> markers.
    /// </summary>
    public static string StripToolCalls(string output)
    {
        var result = ToolCallRegex.Replace(output, "");
        result = ToolResponseBlockRegex.Replace(result, "");
        result = TurnMarkerRegex.Replace(result, "");
        result = ChannelBlockRegex.Replace(result, "");
        return result.Trim();
    }

    /// <summary>
    /// Format a tool result block for injection into the conversation.
    /// Uses Gemma 4's native response format:
    ///   <![CDATA[<|tool_response>response:tool_name{key:val}<tool_response|>]]>
    /// </summary>
    public static string FormatResult(string toolName, string status, string result, bool truncated = false)
    {
        var value = truncated ? result + "\n\n[...truncated]" : result;
        // Escape <|"|> sequences in result (escape them so they don't confuse parser)
        var escaped = EscapeForResponse(value);
        return $"<|tool_response>response:{toolName}{{value:{EscapeForResponse(escaped)}}}<tool_response|>";
    }

    /// <summary>
    /// Truncate tool result to ~2KB at paragraph boundaries.
    /// </summary>
    public static string TruncateResult(string text, int maxBytes = 2048)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;

        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        var truncated = false;

        foreach (var para in paragraphs)
        {
            var candidate = sb.Length == 0 ? para : sb.ToString() + "\n\n" + para;
            if (System.Text.Encoding.UTF8.GetByteCount(candidate) > maxBytes)
            {
                truncated = true;
                break;
            }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(para);
        }

        return truncated ? sb.ToString() + "\n\n[...truncated]" : sb.ToString();
    }

    // ---- Private helpers ----

    private static readonly Regex ToolResponseBlockRegex = new(
        @"<\|tool_response\>.*?<tool_response\|>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TurnMarkerRegex = new(
        @"<\|?/?turn\|?>",
        RegexOptions.Compiled);

    private static readonly Regex ChannelBlockRegex = new(
        @"<\|channel\>.*?<channel\|>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Parse key:value arguments from the { } block.
    /// Format: key1:val1,key2:val2
    /// String values are wrapped in <![CDATA[<|"|>...<|"|>]]> delimiters.
    /// This uses a simple state machine rather than regex to handle
    /// the <![CDATA[<|"|>]]> delimiters containing colons and commas.
    /// </summary>
    private static Dictionary<string, string> ParseArgs(string argsBlock)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(argsBlock))
            return args;

        // Simple state-machine parser for key:value pairs
        // States: 0=key, 1=value, 2=in_quoted_string
        int i = 0;
        while (i < argsBlock.Length)
        {
            // Skip whitespace
            while (i < argsBlock.Length && char.IsWhiteSpace(argsBlock[i])) i++;
            if (i >= argsBlock.Length) break;

            // Read key
            var keyStart = i;
            while (i < argsBlock.Length && argsBlock[i] != ':' && argsBlock[i] != ',') i++;
            var key = argsBlock[keyStart..i].Trim();
            if (string.IsNullOrEmpty(key)) break;

            // Skip ':'
            if (i < argsBlock.Length && argsBlock[i] == ':') i++;

            // Skip whitespace before value
            while (i < argsBlock.Length && char.IsWhiteSpace(argsBlock[i])) i++;

            // Read value
            string value;
            if (i < argsBlock.Length && argsBlock[i] == '<' && argsBlock.Substring(i).StartsWith("<|\"|>"))
            {
                // Quoted string: <|"|>text<|"|>
                i += 5; // skip <|"|>
                var valStart = i;
                var depth = 0;
                while (i < argsBlock.Length)
                {
                    if (argsBlock[i] == '{') depth++;
                    else if (argsBlock[i] == '}') { depth--; }
                    else if (depth == 0 && argsBlock.Substring(i).StartsWith("<|\"|>"))
                    {
                        break;
                    }
                    i++;
                }
                value = argsBlock[valStart..i].Trim();
                i += 5; // skip closing <|"|>
            }
            else
            {
                // Unquoted value (boolean, number, or nested {})
                var valStart = i;
                var depth = 0;
                while (i < argsBlock.Length)
                {
                    if (argsBlock[i] == '{') depth++;
                    else if (argsBlock[i] == '}') depth--;
                    else if (depth == 0 && (argsBlock[i] == ',')) break;
                    i++;
                }
                value = argsBlock[valStart..i].Trim();
            }

            // Handle escaped sequences in value
            value = UnescapeForResponse(value);

            if (!string.IsNullOrEmpty(key))
                args[key] = value;

            // Skip ','
            if (i < argsBlock.Length && argsBlock[i] == ',') i++;
        }

        return args;
    }

    /// <summary>
    /// Escape value text for embedding in a response block.
    /// </summary>
    private static string EscapeForResponse(string text)
        => text.Replace("<|tool_response|>", "<|tool_response\\|>");

    /// <summary>
    /// Unescape value text extracted from a response block.
    /// </summary>
    private static string UnescapeForResponse(string text)
        => text.Replace("<|tool_response\\|>", "<|tool_response|>");
}
