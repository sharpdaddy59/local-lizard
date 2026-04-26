using LocalLizard.Common;
using LocalLizard.LocalLLM;
using LocalLizard.LocalLLM.Tools;
using LocalLizard.LocalLLM.Tools.Tools;

namespace LocalLizard.Tests.Integration;

/// <summary>
/// Shared model fixture: loads the Qwen GGUF once and configures the
/// tool system for any test class that needs it.
/// </summary>
public sealed class ModelFixture : IDisposable
{
    public LlmEngine Engine { get; }
    public ToolRegistry Registry { get; }
    public ToolExecutionPipeline Pipeline { get; }

    public ModelFixture()
    {
        var config = new LizardConfig
        {
            // Use env vars if set, otherwise default paths
            ModelPath = Environment.GetEnvironmentVariable("LIZARD_MODEL_PATH")
                ?? "/home/wily/ai/models/qwen-2.5-3b-instruct-q4_k_m.gguf",
            MmprojPath = Environment.GetEnvironmentVariable("LIZARD_MMPROJ_PATH")
                ?? "/home/wily/ai/models/mmproj-gemma4-E2B-BF16.gguf",
            MaxTokens = 1024,
            LlmTemperature = 0.5f,
            // Disable features we don't need in tests
            VisionEnabled = false,
            ToolsEnabled = true,
        };

        Engine = new LlmEngine(config);
        Engine.LoadModel();

        // Set up memory tool pair (LookupFactTool needs a RememberFactTool)
        var memory = new RememberFactTool();
        var tools = new ITool[]
        {
            new GetTimeTool(),
            new SearchWebTool(config.BraveSearchApiKey),
            memory,
            new LookupFactTool(memory),
            new RunShellTool(),
        };

        Registry = new ToolRegistry(tools);
        Pipeline = new ToolExecutionPipeline(Registry);

        Engine.ConfigureTools(Pipeline);
        Engine.SetToolSystemPrompt(Registry.ToSystemPrompt());
    }

    public void Dispose()
    {
        Engine?.Dispose();
    }
}
