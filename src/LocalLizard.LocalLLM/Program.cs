using LocalLizard.Common;
using LocalLizard.LocalLLM;

var config = new LizardConfig();

// Allow overriding model path via first CLI arg
if (args.Length > 0)
    config.ModelPath = args[0];

Console.WriteLine($"Loading model: {config.ModelPath}");

using var llm = new LlmService(config);
await llm.LoadAsync();

Console.WriteLine("Model loaded. Type a message (or 'quit' to exit):\n");

var chatHistory = new List<(string Role, string Content)>();

while (true)
{
    Console.Write("You> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

    var response = await llm.CompleteAsync(input, chatHistory: chatHistory, ct: default);
    Console.WriteLine($"Bot> {response}\n");

    chatHistory.Add(("user", input));
    chatHistory.Add(("assistant", response));
}

Console.WriteLine("Goodbye!");
