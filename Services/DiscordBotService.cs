using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

namespace Truthbot.Services;

/// <summary>
/// Runs TruthBot as a Discord bot: chat and image generation, one conversation per user.
/// </summary>
public class DiscordBotService
{
    private readonly string _geminiApiKey;
    private readonly string _discordToken;
    private readonly string _prefix;
    private readonly DiscordSocketClient _client;
    private readonly Dictionary<ulong, GeminiService> _userSessions = new();
    private readonly string _imageTempDir;
    private const int MaxReplyLength = 1900; // under Discord 2000 limit

    public DiscordBotService(string geminiApiKey, string discordToken, string prefix = "tb")
    {
        _geminiApiKey = geminiApiKey;
        _discordToken = discordToken;
        _prefix = prefix.Trim();
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.DirectMessages
                | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        });
        _imageTempDir = Path.Combine(Path.GetTempPath(), "TruthBot_Images");
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;

        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine($"[Discord] {log.Severity}: {log.Message}");
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        Console.WriteLine($"TruthBot connected as {_client.CurrentUser?.Username}");
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage raw)
    {
        if (raw is not SocketUserMessage message || message.Author.IsBot)
            return;

        bool isDm = message.Channel is IPrivateChannel;
        string content = message.Content.Trim();
        bool hasPrefix = _prefix.Length > 0 && content.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
        bool mentioned = message.Author is SocketGuildUser guildUser &&
            message.MentionedUsers.Any(u => u.Id == _client.CurrentUser?.Id);

        if (!isDm && !hasPrefix && !mentioned)
            return;

        string input = content;
        if (hasPrefix)
            input = content[_prefix.Length..].TrimStart();
        if (mentioned)
            input = s_mentionRegex.Replace(content, "").Trim();

        if (string.IsNullOrWhiteSpace(input) && hasPrefix)
        {
            await SendHelpAsync(message);
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
            return;

        ulong userId = message.Author.Id;
        if (!_userSessions.TryGetValue(userId, out var gemini))
        {
            gemini = new GeminiService(_geminiApiKey);
            _userSessions[userId] = gemini;
        }

        try
        {
            // !image <prompt>
            if (HasCommand(input, "image", out string? imagePrompt))
            {
                if (string.IsNullOrWhiteSpace(imagePrompt))
                {
                    await message.ReplyAsync($"Usage: `{_prefix}image <description>`  e.g.  `{_prefix}image a cute cat on a skateboard`");
                    return;
                }
                await message.Channel.TriggerTypingAsync();
                var path = await gemini.GenerateImageAsync(imagePrompt!, _imageTempDir);
                if (path != null && File.Exists(path))
                {
                    await message.Channel.SendFileAsync(path, text: $"Generated: *{Escape(imagePrompt!)}*", messageReference: new MessageReference(message.Id));
                    try { File.Delete(path); } catch { /* ignore */ }
                }
                else
                    await message.ReplyAsync("Image generation failed or isn’t available on this key. Try chat instead.");
                return;
            }

            // !clear
            if (HasCommand(input, "clear", out _))
            {
                gemini.ClearHistory();
                await message.ReplyAsync("Conversation cleared.");
                return;
            }

            // Chat
            await message.Channel.TriggerTypingAsync();
            string reply = await gemini.ChatAsync(input);
            if (reply.Length > MaxReplyLength)
                reply = reply[..(MaxReplyLength - 3)] + "...";
            await message.ReplyAsync(reply);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Error: {ex.Message}");
            await message.ReplyAsync($"Error: {ex.Message}");
        }
    }

    private static bool HasCommand(string input, string command, out string? rest)
    {
        rest = null;
        var c = command.Trim().ToLowerInvariant();
        if (!input.StartsWith(c, StringComparison.OrdinalIgnoreCase))
            return false;
        rest = input.Length == c.Length ? "" : input[c.Length..].Trim();
        return true;
    }

    private static string Escape(string s) => s.Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`");

    private async Task SendHelpAsync(SocketUserMessage message)
    {
        await message.ReplyAsync(
            "**TruthBot** — Chat with Gemini, generate images.\n" +
            $"• `{_prefix}image <prompt>` — generate an image\n" +
            $"• `{_prefix}clear` — new conversation\n" +
            "• Or just type to chat (or @mention in servers).");
    }

    private static readonly Regex s_mentionRegex = new(@"<@!?\d+>", RegexOptions.Compiled);
}
