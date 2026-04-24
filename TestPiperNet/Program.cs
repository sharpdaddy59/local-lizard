using System;
using System.IO;
using System.Threading.Tasks;
using LocalLizard.Common;
using LocalLizard.Voice;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== LocalLizard T6: Piper C# Integration Test ===\n");
        
        // Create configuration
        var config = new LizardConfig();
        
        Console.WriteLine("Configuration:");
        Console.WriteLine($"  Piper Executable: {config.PiperPath}");
        Console.WriteLine($"  Piper Model: {config.PiperModel}");
        Console.WriteLine();
        
        try
        {
            // Test 1: Direct PiperTTSService
            Console.WriteLine("Test 1: Testing PiperTTSService directly...");
            await TestPiperTTSServiceDirectly(config);
            Console.WriteLine();
            
            // Test 2: VoicePipeline integration
            Console.WriteLine("Test 2: Testing VoicePipeline integration...");
            await TestVoicePipelineIntegration(config);
            Console.WriteLine();
            
            // Test 3: Model information
            Console.WriteLine("Test 3: Getting model information...");
            await TestModelInformation(config);
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed with exception: {ex}");
            Environment.Exit(1);
        }
        
        Console.WriteLine("\n=== All tests completed ===");
    }
    
    static async Task TestPiperTTSServiceDirectly(LizardConfig config)
    {
        using var piperService = new PiperTTSService(config);
        
        // Check if Piper is installed
        Console.WriteLine("  Checking Piper installation...");
        var isInstalled = await piperService.ValidateInstallationAsync();
        Console.WriteLine($"  Piper installation valid: {isInstalled}");
        
        if (!isInstalled)
        {
            Console.WriteLine("  WARNING: Piper is not installed or not accessible.");
            Console.WriteLine("  Skipping synthesis tests.");
            return;
        }
        
        // Test file-based synthesis
        Console.WriteLine("  Testing file-based synthesis...");
        var testText = "Hello, this is a test of the Piper text to speech system.";
        var outputFile = "./test-piper-output.wav";
        
        try
        {
            var result = await piperService.SynthesizeToFileAsync(testText, outputFile);
            Console.WriteLine($"  Synthesis completed: {result}");
            
            if (File.Exists(outputFile))
            {
                var fileInfo = new FileInfo(outputFile);
                Console.WriteLine($"  Output file size: {fileInfo.Length} bytes");
                
                // Clean up
                File.Delete(outputFile);
                Console.WriteLine("  Test file cleaned up.");
            }
        }
        catch (PiperException ex)
        {
            Console.WriteLine($"  Piper synthesis failed: {ex.Message}");
        }
        
        // Test memory-based synthesis
        Console.WriteLine("  Testing memory-based synthesis...");
        try
        {
            var audioData = await piperService.SynthesizeToMemoryAsync("Short test.");
            Console.WriteLine($"  Memory synthesis completed: {audioData.Length} bytes");
        }
        catch (PiperException ex)
        {
            Console.WriteLine($"  Memory synthesis failed: {ex.Message}");
        }
    }
    
    static async Task TestVoicePipelineIntegration(LizardConfig config)
    {
        using var pipeline = new VoicePipeline(config);
        
        // Check Piper installation via pipeline
        Console.WriteLine("  Checking Piper installation via pipeline...");
        var isInstalled = await pipeline.ValidatePiperInstallationAsync();
        Console.WriteLine($"  Piper installation valid: {isInstalled}");
        
        if (!isInstalled)
        {
            Console.WriteLine("  WARNING: Piper is not installed or not accessible.");
            Console.WriteLine("  Skipping pipeline synthesis tests.");
            return;
        }
        
        // Test pipeline synthesis
        Console.WriteLine("  Testing pipeline synthesis...");
        var testText = "This is a test using the VoicePipeline.";
        var outputFile = "./test-pipeline-output.wav";
        
        try
        {
            var result = await pipeline.SynthesizeAsync(testText, outputFile);
            Console.WriteLine($"  Pipeline synthesis completed: {result}");
            
            if (File.Exists(outputFile))
            {
                var fileInfo = new FileInfo(outputFile);
                Console.WriteLine($"  Output file size: {fileInfo.Length} bytes");
                
                // Clean up
                File.Delete(outputFile);
                Console.WriteLine("  Test file cleaned up.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Pipeline synthesis failed: {ex.Message}");
        }
    }
    
    static async Task TestModelInformation(LizardConfig config)
    {
        using var piperService = new PiperTTSService(config);
        
        try
        {
            var modelInfo = await piperService.GetModelInfoAsync();
            Console.WriteLine($"  Model Path: {modelInfo.Path}");
            Console.WriteLine($"  Model Size: {modelInfo.SizeFormatted}");
            Console.WriteLine($"  Last Modified: {modelInfo.LastModified}");
            Console.WriteLine($"  Executable Path: {modelInfo.ExecutablePath}");
            Console.WriteLine($"  Executable Exists: {modelInfo.ExecutableExists}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to get model information: {ex.Message}");
        }
    }
}