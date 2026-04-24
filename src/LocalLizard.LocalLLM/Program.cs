using LocalLizard.Common;
using LocalLizard.LocalLLM;

var config = new LizardConfig();
if (args.Length > 0) config.ModelPath = args[0];

Console.Error.WriteLine($"Loading model: {config.ModelPath}");
Console.Error.WriteLine($"mmproj: {config.MmprojPath}");

using var engine = new LlmEngine(config);
engine.LoadModel();

Console.Error.WriteLine($"Vision: {engine.CanDoVision}");
Console.Error.WriteLine("--- TEXT RESPONSE ---");

// Text-only test
var response = new System.Text.StringBuilder();
await foreach (var token in engine.CompleteAsync("What is 2+2?"))
{
    response.Append(token);
    Console.Out.Write(token);
    Console.Out.Flush();
}
Console.Out.WriteLine();
Console.Error.WriteLine($"--- TEXT END ({response.Length} chars) ---");

// Vision test (if available)
if (engine.CanDoVision)
{
    Console.Error.WriteLine("--- VISION TEST ---");
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
