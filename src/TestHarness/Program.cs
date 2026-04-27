using LocalLizard.Common;
using LocalLizard.Voice;
using LocalLizard.Voice.Capture;

namespace LocalLizard.TestHarness;

/// <summary>
/// Quick smoke test for Phase 0 hardware integration on brazos.
/// Exercises: AlsaCapture, BrightnessGate, CaptureAndTranscribeAsync.
/// Run: dotnet run --project src/TestHarness
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        var config = new LizardConfig();
        var test = args.Contains("--full") ? "full" : args.FirstOrDefault(a => !a.StartsWith("-")) ?? "menu";

        Console.WriteLine("=== LocalLizard Phase 0 Test Harness ===");
        Console.WriteLine($"Config: AlsaDevice={config.AlsaDevice}, CameraDevice={config.CameraDevice}");
        Console.WriteLine($"        BrightnessThreshold={config.CameraBrightnessThreshold}, CheckInterval={config.CameraCheckIntervalSec}s");
        Console.WriteLine();

        switch (test)
        {
            case "full":
                await RunAllTests(config);
                break;
            case "menu":
                await RunMenu(config);
                break;
        }
    }

    static async Task RunMenu(LizardConfig config)
    {
        while (true)
        {
            Console.WriteLine("─── Test Menu ───");
            Console.WriteLine("  1. ALSA mic capture (3 seconds)");
            Console.WriteLine("  2. Brightness gate check");
            Console.WriteLine("  3. Capture + transcribe (speak something)");
            Console.WriteLine("  4. Listening loop (continuous, Ctrl+C to stop)");
            Console.WriteLine("  5. List ALSA capture devices");
            Console.WriteLine("  6. Run all tests");
            Console.WriteLine("  q. Quit");
            Console.Write("\nChoice: ");

            var choice = Console.ReadLine()?.Trim();
            Console.WriteLine();

            try
            {
                switch (choice)
                {
                    case "1":
                        await TestCapture(config);
                        break;
                    case "2":
                        await TestBrightnessGate(config);
                        break;
                    case "3":
                        await TestCaptureAndTranscribe(config);
                        break;
                    case "4":
                        await TestListeningLoop(config);
                        break;
                    case "5":
                        await TestListDevices();
                        break;
                    case "6":
                        await RunAllTests(config);
                        break;
                    case "q":
                        return;
                    default:
                        Console.WriteLine("Invalid choice.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            }

            Console.WriteLine();
        }
    }

    static async Task RunAllTests(LizardConfig config)
    {
        Console.WriteLine("═══ Test 1: List ALSA Devices ═══");
        await TestListDevices();

        Console.WriteLine("\n═══ Test 2: Mic Capture (3s) ═══");
        await TestCapture(config);

        Console.WriteLine("\n═══ Test 3: Brightness Gate ═══");
        await TestBrightnessGate(config);

        Console.WriteLine("\n═══ Test 4: Capture + Transcribe ═══");
        await TestCaptureAndTranscribe(config);

        Console.WriteLine("\n═══ All tests complete ═══");
    }

    static async Task TestListDevices()
    {
        var devices = await AlsaCapture.ListDevicesAsync();
        if (devices.Count == 0)
        {
            Console.WriteLine("  No capture devices found (arecord not available?)");
            return;
        }

        foreach (var d in devices)
            Console.WriteLine($"  {d}");
    }

    static async Task TestCapture(LizardConfig config)
    {
        using var capture = new AlsaCapture(config.AlsaDevice);
        Console.WriteLine($"  Device: {capture.DeviceName}");
        Console.WriteLine($"  Sample rate: {AlsaCapture.SampleRate}Hz, Channels: {AlsaCapture.Channels}");
        Console.WriteLine("  Capturing 3 seconds of audio...");
        Console.WriteLine("  (Speak or make noise near the mic)");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = await capture.ReadAsync(3000);
        sw.Stop();

        Console.WriteLine($"  Captured: {data.Length} bytes in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Expected: {AlsaCapture.BytesPerSecond * 3} bytes");
        Console.WriteLine($"  Match: {(data.Length == AlsaCapture.BytesPerSecond * 3 ? "✅ exact" : "⚠️  mismatch")}");

        // Quick RMS analysis
        double sumSquares = 0;
        for (var i = 0; i < data.Length; i += 2)
        {
            var sample = (short)(data[i] | (data[i + 1] << 8));
            sumSquares += sample * (double)sample;
        }
        var rms = Math.Sqrt(sumSquares / (data.Length / 2)) / 32767.0;
        Console.WriteLine($"  RMS energy: {rms:F4} {(rms > 0.01 ? "✅ audio detected" : "⚠️  silence — mic working?")}");

        // Save to file for manual inspection
        var outPath = "/tmp/locallizard-test-capture.raw";
        await File.WriteAllBytesAsync(outPath, data);
        Console.WriteLine($"  Saved raw PCM to: {outPath}");
        Console.WriteLine($"  Play with: aplay -f S16_LE -r 16000 -c 1 {outPath}");
    }

    static async Task TestBrightnessGate(LizardConfig config)
    {
        using var gate = new BrightnessGate(config.CameraDevice, config.CameraBrightnessThreshold);

        Console.WriteLine("  Checking brightness (camera cover should be OPEN)...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var isOpen = await gate.IsOpenAsync();
        sw.Stop();

        Console.WriteLine($"  Result: {(isOpen ? "OPEN ✅" : "CLOSED ❌")} (took {sw.ElapsedMilliseconds}ms)");

        Console.WriteLine("\n  Now CLOSE the camera cover and press Enter...");
        Console.ReadLine();

        Console.WriteLine("  Checking brightness (cover should be CLOSED)...");
        sw.Restart();
        isOpen = await gate.IsOpenAsync();
        sw.Stop();

        Console.WriteLine($"  Result: {(!isOpen ? "CLOSED ✅" : "OPEN ❌")} (took {sw.ElapsedMilliseconds}ms)");
        Console.WriteLine($"  Threshold: {config.CameraBrightnessThreshold}");
    }

    static async Task TestCaptureAndTranscribe(LizardConfig config)
    {
        using var pipeline = new VoicePipeline(config);

        Console.WriteLine("  Speak something (will capture until silence detected, max 5s)...");
        Console.WriteLine("  Listening...");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var text = await pipeline.CaptureAndTranscribeAsync();
        sw.Stop();

        Console.WriteLine($"  Transcribed ({sw.ElapsedMilliseconds}ms): \"{text}\"");
        Console.WriteLine($"  {(string.IsNullOrWhiteSpace(text) ? "⚠️  No transcription" : "✅ Got text")}");
    }

    static async Task TestListeningLoop(LizardConfig config)
    {
        using var pipeline = new VoicePipeline(config);
        using var cts = new CancellationTokenSource();

        Console.WriteLine("  Starting listening loop. Camera cover = on/off.");
        Console.WriteLine("  Speak to test. Press Ctrl+C to stop.");
        Console.CancelKeyPress += (_, _) => cts.Cancel();

        var turnCount = 0;
        await pipeline.StartListeningLoopAsync(async text =>
        {
            turnCount++;
            Console.WriteLine($"  [Turn {turnCount}] Heard: \"{text}\"");
            await Task.CompletedTask;
            return true; // Continue listening
        }, cts.Token);

        Console.WriteLine($"  Stopped after {turnCount} turns.");
    }
}
