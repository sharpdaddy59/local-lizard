using System;
using System.Threading.Tasks;
using LocalLizard.Common;
using LocalLizard.Voice;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing Whisper.net integration for LocalLizard T5");
        Console.WriteLine("==================================================");
        
        // Create configuration
        var config = new LizardConfig();
        
        // Update paths for testing
        config.WhisperModelPath = "./test-models/ggml-base.bin";
        
        Console.WriteLine($"Whisper model path: {config.WhisperModelPath}");
        Console.WriteLine($"Whisper language: {config.WhisperLanguage}");
        Console.WriteLine($"Whisper threads: {config.WhisperThreads}");
        Console.WriteLine($"Whisper use GPU: {config.WhisperUseGpu}");
        
        try
        {
            // Test 1: Create WhisperSTTService
            Console.WriteLine("\nTest 1: Creating WhisperSTTService...");
            using var whisperService = new WhisperSTTService(config);
            Console.WriteLine("✓ WhisperSTTService created successfully");
            
            // Test 2: Get model info
            Console.WriteLine("\nTest 2: Getting model info...");
            var modelInfo = await whisperService.GetModelInfoAsync();
            if (modelInfo != null)
            {
                Console.WriteLine($"✓ Model path: {modelInfo.Path}");
                Console.WriteLine($"✓ Model size: {modelInfo.SizeFormatted}");
                Console.WriteLine($"✓ Last modified: {modelInfo.LastModified}");
            }
            else
            {
                Console.WriteLine("⚠ Model not found, will download on first use");
            }
            
            // Test 3: Test with VoicePipeline
            Console.WriteLine("\nTest 3: Testing VoicePipeline integration...");
            using var pipeline = new VoicePipeline(config);
            
            // Check if we're using Whisper.net
            var pipelineModelInfo = await pipeline.GetWhisperModelInfoAsync();
            if (pipelineModelInfo != null)
            {
                Console.WriteLine($"✓ VoicePipeline is using Whisper.net");
                Console.WriteLine($"✓ Model: {pipelineModelInfo.Path}");
            }
            else
            {
                Console.WriteLine("⚠ VoicePipeline fell back to process wrapper");
            }
            
            Console.WriteLine("\n✅ All tests completed successfully!");
            Console.WriteLine("\nNext steps:");
            Console.WriteLine("1. Download a Whisper model (will auto-download on first use)");
            Console.WriteLine("2. Test with actual audio file:");
            Console.WriteLine("   await pipeline.TranscribeAsync(\"test.wav\");");
            Console.WriteLine("3. Test with timestamps:");
            Console.WriteLine("   var segments = await pipeline.TranscribeWithTimestampsAsync(\"test.wav\");");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"\nInner exception: {ex.InnerException.Message}");
            }
        }
    }
}