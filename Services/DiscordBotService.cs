using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;

namespace Truthbot.Services;

/// <summary>
/// Runs TruFraudBot as a Discord bot: tb = normal chat, fb = chat with rules + rule management.
/// </summary>
public class DiscordBotService
{
    private const string FbPrefixDefault = "fb";
    private const string TbPrefixDefault = "tb";
    private readonly string _geminiApiKey;
    private readonly string _discordToken;
    private readonly string _fbPrefix;
    private readonly string _tbPrefix;
    private readonly RulesService _rules;
    private readonly DiscordSocketClient _client;
    private readonly Dictionary<ulong, GeminiService> _channelSessions = new(); // tb: one convo per channel
    private readonly Dictionary<ulong, GeminiService> _fbChannelSessions = new(); // fb: one convo per channel (with rules)
    private const int MaxReplyLength = 1900;

    public DiscordBotService(string geminiApiKey, string discordToken, string? fbPrefix = null, string? tbPrefix = null, RulesService? rules = null)
    {
        _geminiApiKey = geminiApiKey;
        _discordToken = discordToken;
        _fbPrefix = (fbPrefix ?? FbPrefixDefault).Trim();
        _tbPrefix = (tbPrefix ?? TbPrefixDefault).Trim();
        _rules = rules ?? new RulesService();
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.DirectMessages
                | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        });
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
        Console.WriteLine($"TruFraudBot connected as {_client.CurrentUser?.Username}");
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage raw)
    {
        if (raw is not SocketUserMessage message || message.Author.IsBot)
            return;

        bool isDm = message.Channel is IPrivateChannel;
        string content = message.Content.Trim();
        bool hasFb = _fbPrefix.Length > 0 && content.StartsWith(_fbPrefix, StringComparison.OrdinalIgnoreCase) &&
            (content.Length == _fbPrefix.Length || char.IsWhiteSpace(content[_fbPrefix.Length]) || content[_fbPrefix.Length] == ',');
        bool hasTb = _tbPrefix.Length > 0 && content.StartsWith(_tbPrefix, StringComparison.OrdinalIgnoreCase) &&
            (content.Length == _tbPrefix.Length || char.IsWhiteSpace(content[_tbPrefix.Length]) || content[_tbPrefix.Length] == ',');
        bool mentioned = message.Author is SocketGuildUser &&
            message.MentionedUsers.Any(u => u.Id == _client.CurrentUser?.Id);

        if (!isDm && !hasFb && !hasTb && !mentioned)
            return;

        string input;
        bool useFb;
        if (hasFb && !hasTb)
        {
            input = content.Length > _fbPrefix.Length ? content[_fbPrefix.Length..].TrimStart().TrimStart(',') : "";
            useFb = true;
        }
        else if (hasTb || mentioned)
        {
            if (mentioned)
                input = s_mentionRegex.Replace(content, "").Trim();
            else
                input = content.Length > _tbPrefix.Length ? content[_tbPrefix.Length..].TrimStart().TrimStart(',') : "";
            useFb = false;
        }
        else
            return;

        if (string.IsNullOrWhiteSpace(input))
        {
            await SendHelpAsync(message, useFb);
            return;
        }

        try
        {
            if (useFb)
                await HandleFbInputAsync(message, input);
            else
                await HandleTbInputAsync(message, input);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discord] Error: {ex.Message}");
            await message.ReplyAsync($"Error: {ex.Message}");
        }
    }

    private async Task HandleFbInputAsync(SocketUserMessage message, string input)
    {
        // commandlist / help (same as typing just "fb")
        if (input.Equals("commandlist", StringComparison.OrdinalIgnoreCase) || input.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await message.ReplyAsync(GetFbHelpContent());
            return;
        }

        // clear
        if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            if (_fbChannelSessions.TryGetValue(message.Channel.Id, out var fbGemini))
            {
                fbGemini.ClearHistory();
                _fbChannelSessions.Remove(message.Channel.Id);
            }
            await message.ReplyAsync("Conversation cleared for this channel.");
            return;
        }

        // addrule
        if (input.StartsWith("addrule ", StringComparison.OrdinalIgnoreCase))
        {
            var ruleText = input["addrule ".Length..].Trim();
            if (string.IsNullOrEmpty(ruleText))
            {
                await message.ReplyAsync($"Usage: `{_fbPrefix} addrule <your rule text>`");
                return;
            }
            _rules.Add(ruleText);
            await message.ReplyAsync("Added rule.");
            return;
        }

        // removerule
        if (input.StartsWith("removerule ", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = input["removerule ".Length..].Trim();
            if (!int.TryParse(numStr, out int num) || num < 1 || !_rules.Remove(num))
            {
                await message.ReplyAsync($"Usage: `{_fbPrefix} removerule <number>` (use listrules to see numbers).");
                return;
            }
            await message.ReplyAsync($"Removed rule {num}.");
            return;
        }

        // editrule
        if (input.StartsWith("editrule ", StringComparison.OrdinalIgnoreCase))
        {
            var after = input["editrule ".Length..].Trim();
            if (string.IsNullOrEmpty(after))
            {
                await message.ReplyAsync($"Usage: `{_fbPrefix} editrule <number> <new text>`  e.g. `{_fbPrefix} editrule 1 Always be concise`");
                return;
            }
            var match = Regex.Match(after, @"^(\d+)\s+(.+)$", RegexOptions.Singleline);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int num) || !_rules.Edit(num, match.Groups[2].Value.Trim()))
            {
                await message.ReplyAsync($"Usage: `{_fbPrefix} editrule <number> <new text>` (use listrules to see numbers).");
                return;
            }
            await message.ReplyAsync($"Updated rule {num}.");
            return;
        }

        // listrules
        if (input.Equals("listrules", StringComparison.OrdinalIgnoreCase))
        {
            var lines = _rules.GetLines();
            if (lines.Count == 0)
            {
                await message.ReplyAsync($"No rules yet. Use `{_fbPrefix} addrule <text>` to add one.");
                return;
            }
            var out_ = "**Rules:**\n" + string.Join("\n", lines.Select((line, i) => $"{i + 1}. {line}"));
            if (out_.Length > MaxReplyLength + 3)
                out_ = out_[..(MaxReplyLength - 1)] + "…";
            await message.ReplyAsync(out_);
            return;
        }

        // fb chat (with rules) — shared per channel like tb
        await message.Channel.TriggerTypingAsync();
        ulong channelId = message.Channel.Id;
        if (!_fbChannelSessions.TryGetValue(channelId, out var gemini))
        {
            gemini = new GeminiService(_geminiApiKey);
            _fbChannelSessions[channelId] = gemini;
        }
        var rulesText = _rules.Read();
        var prompt = !gemini.HasHistory && !string.IsNullOrEmpty(rulesText)
            ? "Follow these rules:\n" + rulesText + "\n\nUser message:\n" + input
            : input;
        var reply = await gemini.ChatAsync(prompt);
        if (reply.Length > MaxReplyLength)
            reply = reply[..(MaxReplyLength - 3)] + "...";
        await message.ReplyAsync(reply);
    }

    private async Task HandleTbInputAsync(SocketUserMessage message, string input)
    {
        ulong channelId = message.Channel.Id;
        if (!_channelSessions.TryGetValue(channelId, out var gemini))
        {
            gemini = new GeminiService(_geminiApiKey);
            _channelSessions[channelId] = gemini;
        }

        // commandlist / help
        if (input.Equals("commandlist", StringComparison.OrdinalIgnoreCase) || input.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await SendHelpAsync(message, useFb: false);
            return;
        }

        // clear
        if (HasCommand(input, "clear", out _))
        {
            gemini.ClearHistory();
            await message.ReplyAsync("Conversation cleared for this channel.");
            return;
        }

        // tb chat (normal, no rules) — shared by everyone in this channel/thread
        await message.Channel.TriggerTypingAsync();
        var reply = await gemini.ChatAsync(input);
        if (reply.Length > MaxReplyLength)
            reply = reply[..(MaxReplyLength - 3)] + "...";
        await message.ReplyAsync(reply);
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

    private string GetFbHelpContent()
    {
        var p = _fbPrefix;
        return "**Commands (fb — rules mode)**\n" +
            $"• `{p}` (only) — this list\n" +
            $"• `{p} <message>` — chat with Gemini (follows rules)\n  e.g. `{p} What is PHP?`\n" +
            $"• `{p} addrule <text>` — add a rule\n  e.g. `{p} addrule Always be polite`\n" +
            $"• `{p} editrule <number> <new text>` — edit rule at number\n  e.g. `{p} editrule 1 Always be concise`\n" +
            $"• `{p} removerule <number>` — remove rule at number\n  e.g. `{p} removerule 2`\n" +
            $"• `{p} listrules` — list all rules with numbers\n" +
            $"• `{p} clear` — clear conversation for this channel";
    }

    private async Task SendHelpAsync(SocketUserMessage message, bool useFb)
    {
        if (useFb)
            await message.ReplyAsync(GetFbHelpContent());
        else
            await message.ReplyAsync(
                "**TruFraudBot** — `tb` = normal chat (shared in this channel), `fb` = chat that follows your rules.\n" +
                $"• `{_tbPrefix} <message>` — chat normally (or @mention); everyone in this channel shares the convo\n" +
                $"• `{_tbPrefix} clear` — new conversation for this channel\n" +
                $"• `{_fbPrefix}` — rules-mode command list");
    }

    private static readonly Regex s_mentionRegex = new(@"<@!?\d+>", RegexOptions.Compiled);
}
