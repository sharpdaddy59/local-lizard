namespace LocalLizard.LocalLLM.Tools.Tools;

public sealed class GetTimeTool : ITool
{
    public string Name => "get_time";
    public string Description => "Get the current date and time. No arguments needed.";

    public Task<string> RunAsync(string args, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var result = now.ToString("dddd, MMMM d, yyyy 'at' h:mm tt");
        return Task.FromResult(result);
    }
}
