using System.Text.Json;
using LocalLizard.LocalLLM.Tools;

namespace LocalLizard.Tests;

public class ToolCallParserTests
{
    // ---- Parse ----

    [Fact]
    public void Parse_ReturnsEmpty_WhenNoToolCall()
    {
        var result = ToolCallParser.Parse("Hello, how are you?");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_SingleToolCall_NoArgs()
    {
        var output = """
            I'll check that for you.<tool_call>{"name": "get_time", "arguments": {}}<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("get_time", calls[0].Name);
        // arguments should be an empty JSON object
        Assert.Equal(0, calls[0].Arguments.EnumerateObject().Count());
    }

    [Fact]
    public void Parse_SingleToolCall_WithStringArg()
    {
        var output = """
            <tool_call>{"name": "search_web", "arguments": {"q": "weather in Dallas"}}<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("search_web", calls[0].Name);
        Assert.True(calls[0].Arguments.TryGetProperty("q", out var q));
        Assert.Equal("weather in Dallas", q.GetString());
    }

    [Fact]
    public void Parse_MultipleToolCalls()
    {
        var output = """
            <tool_call>{"name": "get_time", "arguments": {}}<tool_call><tool_call>{"name": "search_web", "arguments": {"q": "weather"}}<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Equal(2, calls.Count);
        Assert.Equal("get_time", calls[0].Name);
        Assert.Equal("search_web", calls[1].Name);
        Assert.Equal("weather", calls[1].Arguments.GetProperty("q").GetString());
    }

    [Fact]
    public void Parse_MixedWithText()
    {
        var output = """
            Let me look that up for you.
            <tool_call>{"name": "search_web", "arguments": {"q": "current weather Dallas"}}<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("search_web", calls[0].Name);
        Assert.Equal("current weather Dallas", calls[0].Arguments.GetProperty("q").GetString());
    }

    [Fact]
    public void Parse_MultipleArgs()
    {
        var output = """
            <tool_call>{"name": "remember_fact", "arguments": {"key": "color", "value": "blue"}}<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("remember_fact", calls[0].Name);
        Assert.Equal("color", calls[0].Arguments.GetProperty("key").GetString());
        Assert.Equal("blue", calls[0].Arguments.GetProperty("value").GetString());
    }

    [Fact]
    public void Parse_ToolCallInsideText()
    {
        var output = """
            The weather is nice.
            <tool_call>{"name": "search_web", "arguments": {"q": "temperature Dallas"}}<tool_call>
            Let me check...
            <tool_call>{"name": "get_time", "arguments": {}}<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public void Parse_HandlesEmptyArgs()
    {
        var output = """<tool_call>{"name": "get_time", "arguments": {}}<tool_call>""";
        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("get_time", calls[0].Name);
        Assert.Equal(0, calls[0].Arguments.EnumerateObject().Count());
    }

    [Fact]
    public void Parse_SkipsMalformedJson()
    {
        var output = """
            <tool_call>not valid json<tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Empty(calls);
    }

    [Fact]
    public void Parse_WithProperCloseTag_SlashClose()
    {
        var output = """
            <tool_call>{"name": "get_time", "arguments": {}}</tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("get_time", calls[0].Name);
    }

    [Fact]
    public void Parse_MultipleWithInterleavedText_SlashClose()
    {
        // This is the real scenario that was failing: model uses </tool_call> as close,
        // and the tool call blocks have user-directed text between them.
        var output = """
            <tool_call>{"name": "run_shell", "arguments": {"command":"echo hi"}}</tool_call>
            What's the weather?
            <tool_call>{"name": "search_web", "arguments": {"q": "weather"}}</tool_call>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Equal(2, calls.Count);
        Assert.Equal("run_shell", calls[0].Name);
        Assert.Equal("search_web", calls[1].Name);
    }

    [Fact]
    public void StripToolCalls_RemovesSlashCloseVariant()
    {
        var output = """
            Before<tool_call>{"name": "get_time", "arguments": {}}</tool_call>After
            """;

        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<tool_call>", stripped);
        Assert.DoesNotContain("</tool_call>", stripped);
        Assert.Contains("Before", stripped);
        Assert.Contains("After", stripped);
    }

    [Fact]
    public void StripToolCalls_InterleavedText_SlashClose()
    {
        var output = """
            <tool_call>{"name": "run_shell", "arguments": {"command":"echo hi"}}</tool_call>
            Between text
            <tool_call>{"name": "get_time", "arguments": {}}</tool_call>
            After
            """;

        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<tool_call>", stripped);
        Assert.Contains("Between text", stripped);
        Assert.Contains("After", stripped);
    }

    // ---- HasToolCall ----

    [Fact]
    public void HasToolCall_ReturnsTrue_WhenBlockPresent()
    {
        Assert.True(ToolCallParser.HasToolCall("""<tool_call>{"name": "get_time", "arguments": {}}<tool_call>"""));
    }

    [Fact]
    public void HasToolCall_ReturnsFalse_WhenNoBlock()
    {
        Assert.False(ToolCallParser.HasToolCall("Just a normal message"));
    }

    [Fact]
    public void HasToolCall_ReturnsFalse_WithDifferentTags()
    {
        Assert.False(ToolCallParser.HasToolCall("some <other_tag> text"));
    }

    // ---- StripToolCalls ----

    [Fact]
    public void StripToolCalls_RemovesToolCallBlocks()
    {
        var output = """
            Before<tool_call>{"name": "get_time", "arguments": {}}<tool_call>After
            """;

        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<tool_call>", stripped);
        Assert.Contains("Before", stripped);
        Assert.Contains("After", stripped);
    }

    [Fact]
    public void StripToolCalls_ReturnsOriginal_WhenNoBlocks()
    {
        var text = "Just a normal response.";
        Assert.Equal(text, ToolCallParser.StripToolCalls(text));
    }

    [Fact]
    public void StripToolCalls_PreservesTextBetweenCalls()
    {
        var output = """
            First<tool_call>{"name": "get_time", "arguments": {}}<tool_call>Second<tool_call>{"name": "search_web", "arguments": {"q": "test"}}<tool_call>Third
            """;

        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<tool_call>", stripped);
        Assert.Contains("First", stripped);
        Assert.Contains("Second", stripped);
        Assert.Contains("Third", stripped);
    }

    // ---- TruncateResult ----

    [Fact]
    public void TruncateResult_ReturnsOriginal_WhenUnderLimit()
    {
        var text = "Short result.";
        Assert.Equal(text, ToolCallParser.TruncateResult(text, 100));
    }

    [Fact]
    public void TruncateResult_TruncatesAtParagraphBoundary()
    {
        var text = new string('A', 100) + "\n\n" + new string('B', 2000) + "\n\n" + new string('C', 2000);
        var result = ToolCallParser.TruncateResult(text, 500);
        Assert.Contains("[...truncated]", result);
        Assert.True(result.Length < text.Length);
    }

    [Fact]
    public void TruncateResult_KeepsFullParagraphs()
    {
        var para1 = new string('A', 100);
        var para2 = new string('B', 200);
        var text = para1 + "\n\n" + para2;
        var result = ToolCallParser.TruncateResult(text, 500);
        Assert.DoesNotContain("[...truncated]", result);
        Assert.Equal(text, result);
    }
}
