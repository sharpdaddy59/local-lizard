using LocalLizard.Common;
using LocalLizard.LocalLLM;
using LocalLizard.Voice;

var config = new LizardConfig();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<LlmEngine>(sp =>
{
    var engine = new LlmEngine(config);
    ToolSetup.ConfigureTools(engine, config);
    return engine;
});
builder.Services.AddSingleton<VoicePipeline>();
builder.Services.AddSingleton<LocalLizard.Web.Services.ChatLoopService>();
builder.Services.AddSingleton<LocalLizard.Web.Services.WakeWordHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LocalLizard.Web.Services.WakeWordHostedService>());

var app = builder.Build();

// Serve static files (wwwroot/)
app.UseDefaultFiles();
app.UseStaticFiles();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "alive", time = DateTime.UtcNow }));

// Streaming chat endpoint (SSE-style via raw body write)
app.MapPost("/api/chat", async (HttpContext ctx, LlmEngine llm, CancellationToken ct) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<ChatRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.Message))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Message is required", ct);
        return;
    }

    if (!llm.IsLoaded)
        llm.LoadModel();

    ctx.Response.ContentType = "text/plain; charset=utf-8";

    await foreach (var token in llm.CompleteWithToolsAsync(body.Message, ct: ct))
    {
        await ctx.Response.WriteAsync(token, ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

// Audio upload → transcription
app.MapPost("/api/transcribe-upload", async (HttpContext ctx, VoicePipeline voice, CancellationToken ct) =>
{
    var form = await ctx.Request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    if (file is null)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("No audio file", ct);
        return;
    }

    // Save uploaded audio to temp file
    var tempPath = Path.Combine(Path.GetTempPath(), $"lizard-stt-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
    using (var stream = File.Create(tempPath))
    {
        await file.CopyToAsync(stream, ct);
    }

    try
    {
        // Convert to WAV if needed (whisper expects WAV)
        var wavPath = tempPath;
        if (!tempPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            wavPath = await ConvertToWavAsync(tempPath, ct);
        }

        var text = await voice.TranscribeAsync(wavPath, ct);
        await ctx.Response.WriteAsJsonAsync(new { text }, ct);
    }
    finally
    {
        try { File.Delete(tempPath); } catch { }
    }
});

// Synthesize text to audio (JSON body)
app.MapPost("/api/synthesize", async (HttpContext ctx, VoicePipeline voice, CancellationToken ct) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<SynthesizeRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.Text))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Text is required", ct);
        return;
    }

    var outputPath = Path.Combine(Path.GetTempPath(), $"lizard-tts-{Guid.NewGuid():N}.wav");
    await voice.SynthesizeAsync(body.Text, outputPath, ct);
    var bytes = await File.ReadAllBytesAsync(outputPath, ct);
    try { File.Delete(outputPath); } catch { }
    ctx.Response.ContentType = "audio/wav";
    await ctx.Response.Body.WriteAsync(bytes, ct);
});

// Full voice chat loop: upload audio → STT → LLM → TTS → audio response
app.MapPost("/api/voice-chat", async (HttpContext ctx, LocalLizard.Web.Services.ChatLoopService chatLoop, CancellationToken ct) =>
{
    var form = await ctx.Request.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    if (file is null)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("No audio file uploaded", ct);
        return;
    }

    using var stream = file.OpenReadStream();
    var result = await chatLoop.VoiceChatAsync(stream, file.FileName, ct);

    // Return JSON with transcription, response text, and base64 audio
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new
    {
        transcription = result.Transcription,
        response = result.Response,
        audio = Convert.ToBase64String(result.Audio),
        latency = new
        {
            sttMs = result.SttLatencyMs,
            llmMs = result.LlmLatencyMs,
            ttsMs = result.TtsLatencyMs,
            totalMs = result.TotalLatencyMs,
        },
    }, ct);
});

// Text chat with conversation history
app.MapPost("/api/text-chat", async (HttpContext ctx, LocalLizard.Web.Services.ChatLoopService chatLoop, CancellationToken ct) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<ChatRequest>(ct);
    if (body is null || string.IsNullOrWhiteSpace(body.Message))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Message is required", ct);
        return;
    }

    var result = await chatLoop.TextChatAsync(body.Message, ct);
    await ctx.Response.WriteAsJsonAsync(result, ct);
});

// Clear conversation history
app.MapPost("/api/clear-history", (LocalLizard.Web.Services.ChatLoopService chatLoop) =>
{
    chatLoop.ClearHistory();
    Results.Ok(new { cleared = true, historyLength = 0 });
});

// Wake word control endpoints
app.MapGet("/api/wakeword/status", (LocalLizard.Web.Services.WakeWordHostedService wakeWord) =>
{
    return Results.Ok(new
    {
        listening = wakeWord.IsListening,
        wakePhrase = wakeWord.WakePhrase,
    });
});

app.MapPost("/api/wakeword/start", (LocalLizard.Web.Services.WakeWordHostedService wakeWord) =>
{
    wakeWord.StartListening();
    return Results.Ok(new { listening = true, wakePhrase = wakeWord.WakePhrase });
});

app.MapPost("/api/wakeword/stop", async (LocalLizard.Web.Services.WakeWordHostedService wakeWord) =>
{
    await wakeWord.StopListeningAsync();
    return Results.Ok(new { listening = false });
});

app.Run();

// Convert audio to WAV using ffmpeg (available on brazos)
static async Task<string> ConvertToWavAsync(string inputPath, CancellationToken ct)
{
    var outputPath = Path.Combine(Path.GetTempPath(), $"lizard-converted-{Guid.NewGuid():N}.wav");
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "ffmpeg",
        Arguments = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"",
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    using var proc = System.Diagnostics.Process.Start(psi)
        ?? throw new InvalidOperationException("ffmpeg not found");
    await proc.WaitForExitAsync(ct);

    if (proc.ExitCode != 0)
        throw new InvalidOperationException($"ffmpeg failed with exit code {proc.ExitCode}");

    return outputPath;
}

record ChatRequest(string Message);
record SynthesizeRequest(string Text);
