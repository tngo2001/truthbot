using Microsoft.Extensions.Configuration;
using Truthbot.Services;

// Load .env from project directory (optional; for local runs)
LoadEnvFromFile(Path.Combine(AppContext.BaseDirectory, ".env"));
LoadEnvFromFile(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

static void LoadEnvFromFile(string path)
{
    if (!File.Exists(path)) return;
    foreach (var line in File.ReadAllLines(path))
    {
        var s = line.Trim();
        if (s.Length == 0 || s[0] == '#') continue;
        var i = s.IndexOf('=');
        if (i <= 0) continue;
        var key = s[0..i].Trim();
        var value = s[(i + 1)..].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\\\"", "\"");
        Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
    }
}

// Config: .env / user-secrets > env (for deploy use env only)
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var geminiApiKey = config["GEMINI_API_KEY"]
    ?? config["GOOGLE_API_KEY"]
    ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
    ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
    ?? "";

var discordToken = config["DISCORD_TOKEN"]
    ?? Environment.GetEnvironmentVariable("DISCORD_TOKEN")
    ?? "";

// Discord mode: run as bot (requires both keys from env for deploy)
if (!string.IsNullOrWhiteSpace(discordToken))
{
    if (string.IsNullOrWhiteSpace(geminiApiKey))
    {
        Console.WriteLine("Discord mode requires GEMINI_API_KEY (or GOOGLE_API_KEY). Set it in your environment.");
        return 1;
    }
    var fbPrefix = config["BOT_PREFIX_FB"] ?? Environment.GetEnvironmentVariable("BOT_PREFIX_FB") ?? "fb";
    var tbPrefix = config["BOT_PREFIX_TB"] ?? Environment.GetEnvironmentVariable("BOT_PREFIX_TB") ?? "tb";
    var rulesPath = config["RULES_FILE"] ?? Environment.GetEnvironmentVariable("RULES_FILE"); // null = default rules.txt
    var discordBot = new DiscordBotService(
        geminiApiKey,
        discordToken.Trim(),
        fbPrefix: fbPrefix,
        tbPrefix: tbPrefix,
        rules: string.IsNullOrEmpty(rulesPath) ? new RulesService() : new RulesService(rulesPath));
    await discordBot.RunAsync();
    return 0;
}

// Console mode: local chat (no image generation)
if (string.IsNullOrWhiteSpace(geminiApiKey))
{
    Console.WriteLine("Enter your Gemini API key (from https://aistudio.google.com/apikey):");
    geminiApiKey = Console.ReadLine()?.Trim() ?? "";
}
if (string.IsNullOrWhiteSpace(geminiApiKey))
{
    Console.WriteLine("No API key provided. Set GEMINI_API_KEY or GOOGLE_API_KEY.");
    return 1;
}

var gemini = new GeminiService(geminiApiKey);

Console.WriteLine();
Console.WriteLine("TruFraudBot — Gemini chatbot");
Console.WriteLine("Commands: /clear  → new conversation  |  /quit  → exit");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input)) continue;

    if (input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Bye.");
        break;
    }

    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        gemini.ClearHistory();
        Console.WriteLine("Conversation cleared.");
        continue;
    }

    Console.Write("Gemini: ");
    try
    {
        var reply = await gemini.ChatAsync(input);
        Console.WriteLine(reply);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }

    Console.WriteLine();
}

return 0;
