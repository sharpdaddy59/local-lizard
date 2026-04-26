using System.Text.Json;
using LocalLizard.LocalLLM.Tools.Tools;

namespace LocalLizard.Tests;

public class GetTimeToolTests
{
    [Fact]
    public async Task RunAsync_ReturnsFormattedTime()
    {
        var tool = new GetTimeTool();
        var emptyArgs = JsonDocument.Parse("{}").RootElement;
        var result = await tool.RunAsync(emptyArgs, CancellationToken.None);

        // Should look like "Friday, April 24, 2026 at 10:03 PM"
        Assert.Contains(",", result);
        Assert.Contains("at", result);
        Assert.Matches(@"\d{4}", result); // has a year
    }

    [Fact]
    public void Name_IsGetTime()
    {
        var tool = new GetTimeTool();
        Assert.Equal("get_time", tool.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var tool = new GetTimeTool();
        Assert.NotEmpty(tool.Description);
    }
}
