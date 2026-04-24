using LocalLizard.Common;
using LocalLizard.Voice;

Console.WriteLine("=== T3 Retest: Whisper STT with resampling ===\n");

var config = new LizardConfig
{
    PiperPath = "/home/wily/dev/piper/piper",
    PiperModel = "/home/wily/dev/piper/en_US-hfc_female-medium.onnx",
    WhisperModelPath = "/home/wily/dev/whisper.cpp/models/ggml-base.en.bin",
};

// Generate test audio with Piper
Console.WriteLine("Generating test audio with Piper...");
var tts = new PiperTTSService(config);
var testAudio = "/tmp/lizard-stt-test-16k.wav";
await tts.SynthesizeToFileAsync("The quick brown fox jumps over the lazy dog.", testAudio);
Console.WriteLine($"Audio generated: {testAudio}");

// Transcribe
Console.WriteLine("\nTranscribing with Whisper (ffmpeg resampling to 16KHz)...");
var stt = new WhisperSTTService(config);
var transcription = await stt.TranscribeAsync(testAudio);
Console.WriteLine($"Transcription: \"{transcription}\"");

tts.Dispose();
stt.Dispose();

Console.WriteLine("\nT3 PASSED!");
