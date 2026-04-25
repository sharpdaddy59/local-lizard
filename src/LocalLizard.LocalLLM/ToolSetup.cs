using LocalLizard.Common;
using LocalLizard.LocalLLM.Tools;
using LocalLizard.LocalLLM.Tools.Tools;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Factory that wires up the tool system and configures it on an LlmEngine.
/// </summary>
public static class ToolSetup
{
    /// <summary>
    /// Creates the full tool pipeline from config and registers it on the engine.
    /// If tools are disabled in config, does nothing.
    /// </summary>
    public static void ConfigureTools(LlmEngine engine, LizardConfig config)
    {
        if (!config.ToolsEnabled)
            return;

        var memoryTool = new RememberFactTool(config.MemoryFilePath);
        var searchTool = new SearchWebTool(config.BraveSearchApiKey);
        var shellTool = new RunShellTool(config.ShellAllowlistPath);

        var registry = new ToolRegistry(new ITool[]
        {
            new GetTimeTool(),
            memoryTool,
            new LookupFactTool(memoryTool),
            searchTool,
            shellTool,
        });

        // Update system prompt to include tool definitions
        var toolPrompt = registry.ToSystemPrompt();
        engine.SetToolSystemPrompt(toolPrompt);

        var pipeline = new ToolExecutionPipeline(registry);
        engine.ConfigureTools(pipeline);
    }
}
