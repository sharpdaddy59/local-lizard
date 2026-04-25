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
            I'll check that for you.<|tool_call>call:get_time{}<tool_call|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("get_time", calls[0].Name);
        Assert.Empty(calls[0].Args);
    }

    [Fact]
    public void Parse_SingleToolCall_WithStringArg()
    {
        var output = """
            <|tool_call>call:search_web{query:<|"|>weather in Dallas<|"|>}<tool_call|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("search_web", calls[0].Name);
        Assert.Equal("weather in Dallas", calls[0].Args["query"]);
    }

    [Fact]
    public void Parse_MultipleToolCalls()
    {
        var output = """
            <|tool_call>call:get_time{}<tool_call|><|tool_call>call:search_web{query:<|"|>weather<|"|>}<tool_call|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Equal(2, calls.Count);
        Assert.Equal("get_time", calls[0].Name);
        Assert.Equal("search_web", calls[1].Name);
        Assert.Equal("weather", calls[1].Args["query"]);
    }

    [Fact]
    public void Parse_MixedWithText()
    {
        var output = """
            Let me look that up for you.
            <|tool_call>call:search_web{query:<|"|>current weather Dallas<|"|>}<tool_call|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("search_web", calls[0].Name);
        Assert.Equal("current weather Dallas", calls[0].Args["query"]);
    }

    [Fact]
    public void Parse_MultipleArgs()
    {
        var output = """
            <|tool_call>call:remember_fact{key:<|"|>color<|"|>,value:<|"|>blue<|"|>}<tool_call|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("remember_fact", calls[0].Name);
        Assert.Equal("color", calls[0].Args["key"]);
        Assert.Equal("blue", calls[0].Args["value"]);
    }

    [Fact]
    public void Parse_IgnoresToolResponseBlocks()
    {
        // Tool response blocks should not be parsed as tool calls
        var output = """
            <|tool_response>response:search_web{value:some result}<tool_response|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Empty(calls);
    }

    [Fact]
    public void Parse_ToolCallInsideText()
    {
        var output = """
            The weather is nice.
            <|tool_call>call:search_web{query:<|"|>temperature Dallas<|"|>}<tool_call|>
            Let me check...
            <|tool_call>call:get_time{}<tool_call|>
            """;

        var calls = ToolCallParser.Parse(output);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public void Parse_HandlesEmptyArgs()
    {
        var output = """<|tool_call>call:get_time{}<tool_call|>""";
        var calls = ToolCallParser.Parse(output);
        Assert.Single(calls);
        Assert.Equal("get_time", calls[0].Name);
        Assert.Empty(calls[0].Args);
    }

    // ---- HasToolCall ----

    [Fact]
    public void HasToolCall_ReturnsTrue_WhenBlockPresent()
    {
        Assert.True(ToolCallParser.HasToolCall("<|tool_call>call:get_time{}<tool_call|>"));
    }

    [Fact]
    public void HasToolCall_ReturnsFalse_WhenNoBlock()
    {
        Assert.False(ToolCallParser.HasToolCall("Just a normal message"));
    }

    [Fact]
    public void HasToolCall_ReturnsFalse_WithToolResponseOnly()
    {
        Assert.False(ToolCallParser.HasToolCall("<|tool_response>response:thing{}<tool_response|>"));
    }

    // ---- StripToolCalls ----

    [Fact]
    public void StripToolCalls_RemovesToolCallBlocks()
    {
        var output = """
            Before<|tool_call>call:get_time{}<tool_call|>After
            """;

        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<|tool_call>", stripped);
        Assert.Contains("Before", stripped);
        Assert.Contains("After", stripped);
    }

    [Fact]
    public void StripToolCalls_RemovesToolResponseBlocks()
    {
        var output = """
            <|tool_response>response:search_web{value:some result}<tool_response|>
            """;

        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<|tool_response>", stripped);
        Assert.DoesNotContain("</tool_response>", stripped);
    }

    [Fact]
    public void StripToolCalls_RemovesTurnMarkers()
    {
        var output = """<|turn>user\nHello<turn|>\n<|turn>model\nHi""";
        var stripped = ToolCallParser.StripToolCalls(output);
        Assert.DoesNotContain("<|turn", stripped);
        Assert.DoesNotContain("<turn|>", stripped);
    }

    [Fact]
    public void StripToolCalls_ReturnsOriginal_WhenNoBlocks()
    {
        var text = "Just a normal response.";
        Assert.Equal(text, ToolCallParser.StripToolCalls(text));
    }

    // ---- FormatResult ----

    [Fact]
    public void FormatResult_CreatesValidBlock()
    {
        var result = ToolCallParser.FormatResult("get_time", "ok", "2026-04-24 22:00");

        Assert.StartsWith("<|tool_response>response:get_time{", result);
        Assert.EndsWith("<tool_response|>", result);
        Assert.Contains("2026-04-24 22:00", result);
    }

    [Fact]
    public void FormatResult_IncludesTruncated_WhenFlagged()
    {
        var result = ToolCallParser.FormatResult("search_web", "ok", "lots of data", truncated: true);
        Assert.Contains("[...truncated]", result);
    }

    [Fact]
    public void FormatResult_IncludesStatus()
    {
        var result = ToolCallParser.FormatResult("search_web", "ok", "data");
        Assert.Contains("value:", result);
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
