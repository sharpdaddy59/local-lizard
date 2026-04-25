using LocalLizard.Common;
using LocalLizard.LocalLLM;
using LocalLizard.Telegram;
using LocalLizard.Voice;
using Telegram.Bot;

var config = new LizardConfig();

var token = Environment.GetEnvironmentVariable("LIZARD_TELEGRAM_BOT_TOKEN")
    ?? throw new InvalidOperationException("Set LIZARD_TELEGRAM_BOT_TOKEN env var");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<LlmEngine>(sp =>
{
    var engine = new LlmEngine(config);
    ToolSetup.ConfigureTools(engine, config);
    return engine;
});
builder.Services.AddSingleton<VoicePipeline>();
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(token));
builder.Services.AddSingleton<BotService>();

var app = builder.Build();

// Minimal health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "alive", bot = "local-lizard-telegram" }));

// Start the bot in the background
var bot = app.Services.GetRequiredService<BotService>();
var botTask = Task.Run(() => bot.StartAsync(app.Lifetime.ApplicationStopping));

Console.WriteLine("[LocalLizard.Telegram] Starting bot...");

app.Run();
