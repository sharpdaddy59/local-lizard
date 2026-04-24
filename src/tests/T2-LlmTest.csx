// T2: LLM Component Test — Load Gemma 1B and get a response
// Run: dotnet script or compile as console app

using LocalLizard.Common;
using LocalLizard.LocalLLM;

var config = new LizardConfig
{
    ModelPath = "/home/wily/ai/models/google_gemma-3-1b-it-Q4_K_M.gguf",
    LlmContextSize = 2048,
    LlmGpuLayers = 0,
    MaxTokens = 100,
};

Console.WriteLine("Loading model...");
using var engine = new LlmEngine(config);
engine.LoadModel();
Console.WriteLine($"Model loaded: {engine.IsLoaded}");

Console.WriteLine("Sending prompt: \"What is 2+2? Answer briefly.\"");
var response = new System.Text.StringBuilder();
await foreach (var chunk in engine.CompleteAsync("What is 2+2? Answer briefly."))
{
    Console.Write(chunk);
    response.Append(chunk);
}
Console.WriteLine();
Console.WriteLine($"Response length: {response.Length} chars");
Console.WriteLine("LLM test complete.");
