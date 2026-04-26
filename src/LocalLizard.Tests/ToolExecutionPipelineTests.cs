using System.Text.Json;
using LocalLizard.LocalLLM.Tools;
using Moq;

namespace LocalLizard.Tests;

public class ToolExecutionPipelineTests
{
    [Fact]
    public async Task ProcessOutput_ReturnsNull_WhenNoToolCall()
    {
        var registry = new ToolRegistry(Array.Empty<ITool>());
        var pipeline = new ToolExecutionPipeline(registry);

        var result = await pipeline.ProcessOutputAsync("Just a normal response.", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessOutput_ReturnsNull_WhenToolCallHasNoName()
    {
        var registry = new ToolRegistry(Array.Empty<ITool>());
        var pipeline = new ToolExecutionPipeline(registry);

        var result = await pipeline.ProcessOutputAsync("""<tool_call>{"arguments": {}}<tool_call>""", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessOutput_ReturnsResult_WhenToolCallHasUnknownName()
    {
        var registry = new ToolRegistry(Array.Empty<ITool>());
        var pipeline = new ToolExecutionPipeline(registry);

        var result = await pipeline.ProcessOutputAsync("""<tool_call>{"name": "bad", "arguments": {}}<tool_call>""", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.Equal("bad", result.Results[0].Name);
        Assert.Equal("error", result.Results[0].Status);
        Assert.Contains("Unknown tool", result.Results[0].Output);
    }

    [Fact]
    public async Task ProcessOutput_ExecutesKnownTool()
    {
        var mockTool = new Mock<ITool>();
        mockTool.Setup(t => t.Name).Returns("get_time");
        mockTool.Setup(t => t.RunAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Friday, April 24, 2026 at 10:03 PM");

        var registry = new ToolRegistry(new[] { mockTool.Object });
        var pipeline = new ToolExecutionPipeline(registry);

        var output = """
            Let me check the time.<tool_call>{"name": "get_time", "arguments": {}}<tool_call>
            """;

        var result = await pipeline.ProcessOutputAsync(output, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.Equal("get_time", result.Results[0].Name);
        Assert.Equal("ok", result.Results[0].Status);
        Assert.Contains("Friday", result.Results[0].Output);
    }

    [Fact]
    public async Task ProcessOutput_ReportsError_ForUnknownTool()
    {
        var registry = new ToolRegistry(new ITool[0]);
        var pipeline = new ToolExecutionPipeline(registry);

        var output = """
            <tool_call>{"name": "nonexistent", "arguments": {}}<tool_call>
            """;

        var result = await pipeline.ProcessOutputAsync(output, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.Equal("nonexistent", result.Results[0].Name);
        Assert.Equal("error", result.Results[0].Status);
    }

    [Fact]
    public async Task ProcessOutput_ReportsError_WhenToolThrows()
    {
        var mockTool = new Mock<ITool>();
        mockTool.Setup(t => t.Name).Returns("failing_tool");
        mockTool.Setup(t => t.RunAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Something broke"));

        var registry = new ToolRegistry(new[] { mockTool.Object });
        var pipeline = new ToolExecutionPipeline(registry);

        var output = """
            <tool_call>{"name": "failing_tool", "arguments": {}}<tool_call>
            """;

        var result = await pipeline.ProcessOutputAsync(output, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.Equal("error", result.Results[0].Status);
        Assert.Contains("Something broke", result.Results[0].Output);
    }

    [Fact]
    public async Task ProcessOutput_StripsCallsFromCleanOutput()
    {
        var mockTool = new Mock<ITool>();
        mockTool.Setup(t => t.Name).Returns("get_time");
        mockTool.Setup(t => t.RunAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("some time");

        var registry = new ToolRegistry(new[] { mockTool.Object });
        var pipeline = new ToolExecutionPipeline(registry);

        var output = """
            Let me check.<tool_call>{"name": "get_time", "arguments": {}}<tool_call>Here is what I found.
            """;

        var result = await pipeline.ProcessOutputAsync(output, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Let me check.Here is what I found.", result.CleanOutput);
    }

    [Fact]
    public async Task ProcessOutput_ExecutesMultipleTools()
    {
        var mockTime = new Mock<ITool>();
        mockTime.Setup(t => t.Name).Returns("get_time");
        mockTime.Setup(t => t.RunAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Friday");

        var mockSearch = new Mock<ITool>();
        mockSearch.Setup(t => t.Name).Returns("search_web");
        mockSearch.Setup(t => t.RunAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("Sunny, 72°F");

        var registry = new ToolRegistry(new ITool[] { mockTime.Object, mockSearch.Object });
        var pipeline = new ToolExecutionPipeline(registry);

        var output = """
            <tool_call>{"name": "get_time", "arguments": {}}<tool_call><tool_call>{"name": "search_web", "arguments": {"q": "weather"}}<tool_call>
            """;

        var result = await pipeline.ProcessOutputAsync(output, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, result.Results.Count);
        Assert.Equal("ok", result.Results[0].Status);
        Assert.Equal("ok", result.Results[1].Status);
    }
}
