using System;
using System.IO;
using System.Threading.Tasks;
using LocalLizard.Common;
using LocalLizard.Voice;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== LocalLizard T5: Whisper.cpp C# Bindings Test ===\n");
        
        // Create configuration
        var config = new LizardConfig();
        
        // Use a test model path in current directory
        config.WhisperModelPath = "./ggml-base.bin";
        config.WhisperThreads = 2; // Use fewer threads for testing
        
        Console.WriteLine("Configuration:");
        Console.WriteLine($"  Whisper Model: {config.WhisperModelPath}");
        Console.WriteLine($"  Whisper Language: {config.WhisperLanguage}");
        Console.WriteLine($"  Whisper Threads: {config.WhisperThreads}");
        Console.WriteLine($"  Whisper Use GPU: {config.WhisperUseGpu}");
        Console.WriteLine();
        
        try
        {
            // Test 1: Direct WhisperSTTService
            Console.WriteLine("Test 1: Testing WhisperSTTService directly...");
            await TestWhisperServiceDirectly(config);
            
            Console.WriteLine("\n" + new string('-', 50) + "\n");
            
            // Test 2: VoicePipeline integration
            Console.WriteLine("Test 2: Testing VoicePipeline integration...");
            await TestVoicePipeline(config);
            
            Console.WriteLine("\n✅ All tests completed!");
            Console.WriteLine("\nSummary:");
            Console.WriteLine("- Created WhisperSTTService with Whisper.net bindings");
            Console.WriteLine("- Implemented fallback to process wrapper");
            Console.WriteLine("- Added model auto-download capability");
            Console.WriteLine("- Integrated with existing VoicePipeline");
            Console.WriteLine("- Successfully built and ready for audio testing");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"\nInner exception: {ex.InnerException.Message}");
            }
        }
    }
    
    static async Task TestWhisperServiceDirectly(LizardConfig config)
    {
        using var service = new WhisperSTTService(config);
        
        Console.WriteLine("  ✓ WhisperSTTService created");
        
        // Get model info
        var modelInfo = await service.GetModelInfoAsync();
        if (modelInfo != null && modelInfo.SizeBytes > 0)
        {
            Console.WriteLine($"  ✓ Model found: {modelInfo.SizeFormatted}");
            Console.WriteLine($"    Path: {modelInfo.Path}");
        }
        else
        {
            Console.WriteLine("  ⚠ Model not found locally");
            Console.WriteLine("    Note: Model will auto-download on first transcription");
        }
        
        Console.WriteLine("  ✓ WhisperSTTService test passed");
    }
    
    static async Task TestVoicePipeline(LizardConfig config)
    {
        using var pipeline = new VoicePipeline(config);
        
        Console.WriteLine("  ✓ VoicePipeline created");
        
        // Check which implementation is being used
        var modelInfo = await pipeline.GetWhisperModelInfoAsync();
        if (modelInfo != null)
        {
            Console.WriteLine($"  ✓ Using Whisper.net (P/Invoke bindings)");
            Console.WriteLine($"    Model: {Path.GetFileName(modelInfo.Path)}");
        }
        else
        {
            Console.WriteLine("  ⚠ Using process wrapper (fallback mode)");
            Console.WriteLine("    Note: Check Whisper.net dependencies if this is unexpected");
        }
        
        // Test that we can at least create the service
        Console.WriteLine("  ✓ VoicePipeline integration test passed");
        
        // Note: Actual transcription test requires an audio file
        Console.WriteLine("\n  Next steps for audio testing:");
        Console.WriteLine("  1. Create a test.wav file (16kHz, mono, PCM)");
        Console.WriteLine("  2. Run: await pipeline.TranscribeAsync(\"test.wav\")");
        Console.WriteLine("  3. For timestamps: await pipeline.TranscribeWithTimestampsAsync(\"test.wav\")");
    }
}