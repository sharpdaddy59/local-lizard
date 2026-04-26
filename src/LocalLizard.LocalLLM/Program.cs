using System.Diagnostics;
using LocalLizard.Common;
using LocalLizard.LocalLLM;

var config = new LizardConfig();
if (args.Length > 0) config.ModelPath = args[0];

Console.Error.WriteLine($"Loading model: {config.ModelPath}");
// LLamaTemplate handles prompt format from GGUF metadata (removed PromptFormat)

using var engine = new LlmEngine(config);
var loadSw = Stopwatch.StartNew();
engine.LoadModel();
loadSw.Stop();
Console.Error.WriteLine($"Model load: {loadSw.Elapsed.TotalSeconds:F1}s");

Console.Error.WriteLine($"Vision: {engine.CanDoVision}");

// Text-only test
Console.Error.WriteLine("--- TEXT ---");
var response = new System.Text.StringBuilder();
var sw1 = Stopwatch.StartNew();
await foreach (var token in engine.CompleteAsync("What is 2+2?"))
{
    response.Append(token);
    Console.Out.Write(token);
    Console.Out.Flush();
}
Console.Out.WriteLine();
sw1.Stop();
Console.Error.WriteLine($"--- TEXT END ({response.Length} chars, {sw1.Elapsed.TotalSeconds:F1}s) ---");

// Tool test
Console.Error.WriteLine("--- TOOLS ---");
ToolSetup.ConfigureTools(engine, config);
var toolResponse = new System.Text.StringBuilder();
var sw2 = Stopwatch.StartNew();
await foreach (var token in engine.CompleteWithToolsAsync("What time is it?"))
{
    toolResponse.Append(token);
    Console.Out.Write(token);
    Console.Out.Flush();
}
Console.Out.WriteLine();
sw2.Stop();
Console.Error.WriteLine($"--- TOOLS END ({toolResponse.Length} chars, {sw2.Elapsed.TotalSeconds:F1}s) ---");

// Vision test (if available)
if (engine.CanDoVision)
{
    Console.Error.WriteLine("--- VISION ---");
    var snapPath = args.Length > 1 ? args[1] : "/tmp/vision-test-1777061126.jpg";
    if (File.Exists(snapPath))
    {
        var imageBytes = await File.ReadAllBytesAsync(snapPath);
        Console.Error.WriteLine($"Image: {snapPath} ({imageBytes.Length} bytes)");
        try
        {
            var visionResponse = new System.Text.StringBuilder();
            await foreach (var token in engine.CompleteAsync("<media> Describe what you see in this image.", imageBuffer: imageBytes))
            {
                visionResponse.Append(token);
                Console.Out.Write(token);
                Console.Out.Flush();
            }
            Console.Out.WriteLine();
            Console.Error.WriteLine($"--- VISION END ({visionResponse.Length} chars) ---");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Vision error: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

Console.Error.WriteLine("Done.");
