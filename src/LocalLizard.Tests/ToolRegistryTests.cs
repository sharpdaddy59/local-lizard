using LocalLizard.LocalLLM.Tools;

namespace LocalLizard.Tests;

public class ToolRegistryTests
{
    private static ITool MakeTool(string name, string desc)
    {
        var mock = new Moq.Mock<ITool>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.Description).Returns(desc);
        return mock.Object;
    }

    [Fact]
    public void Registry_ContainsAllRegisteredTools()
    {
        var tools = new ITool[]
        {
            MakeTool("get_time", "Returns current time"),
            MakeTool("search_web", "Searches the web"),
        };
        var registry = new ToolRegistry(tools);

        Assert.Equal(2, registry.ToolNames.Count);
        Assert.Contains("get_time", registry.ToolNames);
        Assert.Contains("search_web", registry.ToolNames);
    }

    [Fact]
    public void TryGet_ReturnsTrue_ForKnownTool()
    {
        var registry = new ToolRegistry(new[] { MakeTool("get_time", "...") });
        Assert.True(registry.TryGet("get_time", out var tool));
        Assert.Equal("get_time", tool!.Name);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForUnknownTool()
    {
        var registry = new ToolRegistry(new[] { MakeTool("get_time", "...") });
        Assert.False(registry.TryGet("nonexistent", out _));
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var registry = new ToolRegistry(new[] { MakeTool("get_time", "...") });
        Assert.True(registry.TryGet("GET_TIME", out _));
        Assert.True(registry.TryGet("Get_Time", out _));
    }

    [Fact]
    public void Get_ThrowsForUnknownTool()
    {
        var registry = new ToolRegistry(new[] { MakeTool("get_time", "...") });
        Assert.Throws<KeyNotFoundException>(() => registry.Get("nonexistent"));
    }

    [Fact]
    public void ToSystemPrompt_IncludesAllTools()
    {
        var tools = new ITool[]
        {
            MakeTool("get_time", "Returns current date and time"),
            MakeTool("search_web", "Search the internet"),
        };
        var registry = new ToolRegistry(tools);
        var prompt = registry.ToSystemPrompt();

        Assert.Contains("get_time", prompt);
        Assert.Contains("Returns current date and time", prompt);
        Assert.Contains("search_web", prompt);
        Assert.Contains("Search the internet", prompt);
        Assert.Contains("<tool_call>", prompt);
    }

    [Fact]
    public void ToSystemPrompt_UsesJsonSchemaFormat()
    {
        var registry = new ToolRegistry(new[] { MakeTool("test", "A test tool") });
        var prompt = registry.ToSystemPrompt();

        // Should contain JSON Schema structure
        // JsonSerializer outputs compact JSON like {"type":"function",...}
        Assert.Contains("\"type\":\"function\"", prompt);
        Assert.Contains("\"name\":\"test\"", prompt);
        Assert.Contains("\"parameters\"", prompt);
        Assert.Contains("\"type\":\"object\"", prompt);
    }

    [Fact]
    public void ToSystemPrompt_IncludesFormatExample()
    {
        var registry = new ToolRegistry(new[] { MakeTool("test", "A test tool") });
        var prompt = registry.ToSystemPrompt();

        Assert.Contains("get_time", prompt);
        Assert.Contains("<tool_call>", prompt);
    }

    [Fact]
    public void EmptyRegistry_ProducesMinimalPrompt()
    {
        var registry = new ToolRegistry(Array.Empty<ITool>());
        Assert.Empty(registry.ToolNames);
        var prompt = registry.ToSystemPrompt();
        // Should still have the base description
        Assert.Contains("functions", prompt);
    }
}
