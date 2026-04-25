using System.Text;
using LocalLizard.LocalLLM.Tools;
using LocalLizard.LocalLLM;
using Xunit;

namespace LocalLizard.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the tool-calling pipeline with a real GGUF model.
/// Loads Gemma 4 2B Q4_K_M once per test class via IClassFixture.
/// </summary>
[Collection("ModelLoading")]
public sealed class LlmEngineToolTests : IClassFixture<ModelFixture>, IDisposable
{
    private const int MaxExpectedTokens = 4096;
    private const int DefaultTimeoutMs = 90_000; // ~90s per inference

    private readonly ModelFixture _fixture;
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromMilliseconds(DefaultTimeoutMs));

    public LlmEngineToolTests(ModelFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose() => _cts.Dispose();

    // ---- Happy path tests ----

    [Fact]
    public async Task GetTime_ReturnsCurrentTime()
    {
        var reply = await CompleteAsync("What time is it?");

        Assert.NotNull(reply);
        Assert.NotEmpty(reply);
        // Should contain a time-like string — year, month, day, or time
        Assert.True(
            reply.Contains("202") || reply.Contains(":") || reply.Contains("AM") || reply.Contains("PM") ||
            reply.Contains("morning") || reply.Contains("afternoon") || reply.Contains("evening") ||
            reply.Contains("o'clock") || reply.Contains("now"),
            $"Expected time-related output, got: {reply.Truncate(100)}"
        );
    }

    [Fact]
    public async Task SearchWeb_WithQuery_ReturnsResult()
    {
        var reply = await CompleteAsync("Search the web for weather in Dallas");

        // The tool might fail (no actual web API), but the pipeline should
        // have executed it and returned something (result or error message)
        Assert.NotNull(reply);
        Assert.NotEmpty(reply);
    }

    [Fact]
    public async Task RememberAndLookup_FactRoundTrip()
    {
        // This tests multi-step tool usage
        var reply1 = await CompleteAsync("Remember that my favorite color is blue");
        Assert.NotNull(reply1);
        Assert.NotEmpty(reply1);

        // The model should now remember "favorite color = blue"
        var reply2 = await CompleteAsync("What is my favorite color?");
        Assert.NotNull(reply2);
        Assert.NotEmpty(reply2);
        Assert.Contains("blue", reply2, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellCommand_ReturnsOutput()
    {
        var reply = await CompleteAsync("Run the shell command: echo hello world");

        Assert.NotNull(reply);
        Assert.NotEmpty(reply);
        Assert.Contains("hello", reply, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Edge case tests ----

    [Fact]
    public async Task UnknownTool_FallsBackToNormalResponse()
    {
        var reply = await CompleteAsync("Call the nonexistent_tool function");

        // Should either say it can't, or produce a normal response
        Assert.NotNull(reply);
        Assert.NotEmpty(reply);
        Assert.DoesNotContain("error", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoToolNeeded_Passthrough()
    {
        var reply = await CompleteAsync("Hello! How are you today?");

        Assert.NotNull(reply);
        Assert.NotEmpty(reply);
        Assert.DoesNotContain("<|tool_call>", reply);
    }

    [Fact]
    public async Task ToolCallStrip_ProducesCleanOutput()
    {
        var reply = await CompleteAsync("Say hello and then tell me what time it is");

        Assert.NotNull(reply);
        Assert.NotEmpty(reply);
        // The final output should have tool markers stripped
        Assert.DoesNotContain("<|tool_call>", reply);
        Assert.DoesNotContain("<|tool_response>", reply);
    }

    [Fact]
    public async Task MultipleTurns_MaintainCoherence()
    {
        // First turn — simple chat
        var reply1 = await CompleteAsync("Hi, my name is TestUser");
        Assert.NotNull(reply1);
        Assert.NotEmpty(reply1);

        // Second turn — should use tool and remember the name
        var reply2 = await CompleteAsync("What's my name and what time is it?");

        Assert.NotNull(reply2);
        Assert.NotEmpty(reply2);
        // Soft check: model may not respond perfectly, but shouldn't
        // contain raw tool markers (pipeline properly stripped them)
        Assert.DoesNotContain("<|tool_call>", reply2);
        Assert.DoesNotContain("<|tool_response>", reply2);
    }

    // ---- Helpers ----

    /// <summary>
    /// Run a message through the full tool loop (CompleteWithToolsAsync).
    /// </summary>
    private async Task<string> CompleteAsync(string userMessage)
    {
        var result = new StringBuilder();
        await foreach (var token in _fixture.Engine.CompleteWithToolsAsync(userMessage, ct: _cts.Token))
        {
            result.Append(token);
        }
        return result.ToString().Trim();
    }
}

/// <summary>
/// Extension helpers for readable test output.
/// </summary>
file static class StringExtensions
{
    public static string Truncate(this string s, int maxChars)
        => s.Length <= maxChars ? s : s[..maxChars] + "...";
}
