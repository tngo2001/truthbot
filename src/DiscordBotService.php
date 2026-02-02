<?php

namespace TruFraudBot;

use Discord\Builders\MessageBuilder;
use Discord\Discord;
use Discord\Parts\Channel\Message;
use Discord\WebSockets\Event;
use Discord\WebSockets\Intents;
use React\Promise\PromiseInterface;

/**
 * Runs TruFraudBot as a Discord bot: tb = normal chat, fb = chat with rules + rule management.
 */
final class DiscordBotService
{
    private const MAX_REPLY_LENGTH = 1900;
    private const FB_PREFIX_DEFAULT = 'fb';
    private const TB_PREFIX_DEFAULT = 'tb';

    private string $geminiApiKey;
    private string $fbPrefix;
    private string $tbPrefix;
    private RulesService $rules;
    /** @var array<string, GeminiService> channel id => session (tb) */
    private array $channelSessions = [];
    /** @var array<string, GeminiService> channel id => session (fb) */
    private array $fbChannelSessions = [];

    public function __construct(
        string $geminiApiKey,
        ?string $fbPrefix = null,
        ?string $tbPrefix = null,
        ?RulesService $rules = null
    ) {
        $this->geminiApiKey = $geminiApiKey;
        $this->fbPrefix = trim($fbPrefix ?? self::FB_PREFIX_DEFAULT);
        $this->tbPrefix = trim($tbPrefix ?? self::TB_PREFIX_DEFAULT);
        $this->rules = $rules ?? new RulesService();
    }

    public function register(Discord $discord): void
    {
        $discord->on(Event::MESSAGE_CREATE, function (Message $message, Discord $discord) {
            $this->onMessage($message, $discord);
        });
    }

    private function onMessage(Message $message, Discord $discord): void
    {
        $author = $message->author;
        if ($author->bot ?? false) {
            return;
        }

        $isDm = $message->channel->is_private ?? true;
        $content = trim($message->content ?? '');
        $pf = $this->fbPrefix;
        $pt = $this->tbPrefix;
        $hasFb = strlen($pf) > 0 && stripos($content, $pf) === 0
            && (strlen($content) === strlen($pf) || ($this->isPrefixDelimiter($content, strlen($pf))));
        $hasTb = strlen($pt) > 0 && stripos($content, $pt) === 0
            && (strlen($content) === strlen($pt) || ($this->isPrefixDelimiter($content, strlen($pt))));
        $botId = $discord->user->id ?? null;
        $mentioned = false;
        if ($botId && $message->mentions) {
            foreach ($message->mentions as $u) {
                if ((string) $u->id === (string) $botId) {
                    $mentioned = true;
                    break;
                }
            }
        }

        if (!$isDm && !$hasFb && !$hasTb && !$mentioned) {
            return;
        }

        $useFb = false;
        if ($hasFb && !$hasTb) {
            $input = (strlen($content) > strlen($pf))
                ? trim(trim(substr($content, strlen($pf)), ','))
                : '';
            $useFb = true;
        } elseif ($hasTb || $mentioned) {
            if ($mentioned) {
                $input = trim(preg_replace('/<@!?\d+>/', '', $content));
            } else {
                $input = trim(trim(substr($content, strlen($pt)), ','));
            }
        } else {
            return;
        }

        if ($input === '') {
            $this->sendHelp($message, $useFb);
            return;
        }

        try {
            if ($useFb) {
                $this->handleFbInput($message, $input);
            } else {
                $this->handleTbInput($message, $input);
            }
        } catch (\Throwable $ex) {
            echo "[Discord] Error: " . $ex->getMessage() . "\n";
            $message->reply('Error: ' . $ex->getMessage());
        }
    }

    /** @return string|null */
    private function getReferencedMessageContent(Message $message): ?string
    {
        $ref = $message->referenced_message ?? null;
        if ($ref === null) {
            return null;
        }
        $content = $ref->content ?? '';
        return trim($content);
    }

    /**
     * @return PromiseInterface<void>
     */
    private function sendLongMessage(Message $message, string $text): PromiseInterface
    {
        if ($text === '') {
            return \React\Promise\resolve(null);
        }
        $chunks = $this->splitChunks($text, self::MAX_REPLY_LENGTH);
        $p = $message->reply($chunks[0]);
        for ($i = 1; $i < count($chunks); $i++) {
            $idx = $i;
            $p = $p->then(function () use ($message, $chunks, $idx) {
                return $message->channel->sendMessage(
                    MessageBuilder::new()->setContent($chunks[$idx])
                );
            });
        }
        return $p;
    }

    /** @return list<string> */
    private function splitChunks(string $text, int $maxLen): array
    {
        $list = [];
        $start = 0;
        $len = strlen($text);
        while ($start < $len) {
            $take = min($maxLen, $len - $start);
            $end = $start + $take;
            if ($end < $len) {
                $lastSpace = strrpos(substr($text, $start, $take), ' ');
                if ($lastSpace !== false) {
                    $end = $start + $lastSpace + 1;
                }
            }
            $list[] = rtrim(substr($text, $start, $end - $start));
            $start = $end;
        }
        return $list;
    }

    private function handleFbInput(Message $message, string $input): void
    {
        $p = $this->fbPrefix;

        if (strcasecmp($input, 'commandlist') === 0 || strcasecmp($input, 'help') === 0) {
            $message->reply($this->getFbHelpContent());
            return;
        }
        if (strcasecmp($input, 'clear') === 0) {
            $channelId = $message->channel_id;
            if (isset($this->fbChannelSessions[$channelId])) {
                unset($this->fbChannelSessions[$channelId]);
            }
            $message->reply('Conversation cleared for this channel.');
            return;
        }
        if (stripos($input, 'addrule ') === 0) {
            $ruleText = trim(substr($input, 8));
            if ($ruleText === '') {
                $message->reply("Usage: `{$p} addrule <your rule text>`");
                return;
            }
            $this->rules->add($ruleText);
            $message->reply('Added rule.');
            return;
        }
        if (stripos($input, 'removerule ') === 0) {
            $numStr = trim(substr($input, 11));
            $num = is_numeric($numStr) ? (int) $numStr : 0;
            if ($num < 1 || !$this->rules->remove($num)) {
                $message->reply("Usage: `{$p} removerule <number>` (use listrules to see numbers).");
                return;
            }
            $message->reply("Removed rule {$num}.");
            return;
        }
        if (stripos($input, 'editrule ') === 0) {
            $after = trim(substr($input, 8));
            if ($after === '') {
                $message->reply("Usage: `{$p} editrule <number> <new text>`  e.g. `{$p} editrule 1 Always be concise`");
                return;
            }
            if (!preg_match('/^(\d+)\s+(.+)$/s', $after, $m) || !$this->rules->edit((int) $m[1], trim($m[2]))) {
                $message->reply("Usage: `{$p} editrule <number> <new text>` (use listrules to see numbers).");
                return;
            }
            $message->reply('Updated rule ' . $m[1] . '.');
            return;
        }
        if (strcasecmp($input, 'listrules') === 0) {
            $lines = $this->rules->getLines();
            if ($lines === []) {
                $message->reply("No rules yet. Use `{$p} addrule <text>` to add one.");
                return;
            }
            $out = "**Rules:**\n" . implode("\n", array_map(function ($line, $i) {
                return ($i + 1) . '. ' . $line;
            }, $lines, array_keys($lines)));
            if (strlen($out) > self::MAX_REPLY_LENGTH + 3) {
                $out = substr($out, 0, self::MAX_REPLY_LENGTH - 1) . '…';
            }
            $message->reply($out);
            return;
        }

        // fb chat with rules
        $message->channel->broadcastTyping()->then(function () use ($message, $input) {
            $channelId = $message->channel_id;
            if (!isset($this->fbChannelSessions[$channelId])) {
                $this->fbChannelSessions[$channelId] = new GeminiService($this->geminiApiKey);
            }
            $gemini = $this->fbChannelSessions[$channelId];
            $rulesText = $this->rules->read();
            $prompt = !$gemini->hasHistory() && $rulesText !== ''
                ? "Follow these rules:\n" . $rulesText . "\n\nUser message:\n" . $input
                : $input;
            $refContent = $this->getReferencedMessageContent($message);
            if ($refContent !== null && $refContent !== '') {
                $prompt = "The user is replying to this specific message:\n\"\"\"\n{$refContent}\n\"\"\"\n\nTheir reply: {$prompt}";
            }
            $reply = $gemini->chat($prompt);
            return $this->sendLongMessage($message, $reply);
        })->otherwise(function (\Throwable $e) use ($message) {
            echo "[Discord] " . $e->getMessage() . "\n";
            $message->reply('Error: ' . $e->getMessage());
        });
    }

    private function handleTbInput(Message $message, string $input): void
    {
        $channelId = $message->channel_id;
        if (!isset($this->channelSessions[$channelId])) {
            $this->channelSessions[$channelId] = new GeminiService($this->geminiApiKey);
        }
        $gemini = $this->channelSessions[$channelId];

        if (strcasecmp($input, 'commandlist') === 0 || strcasecmp($input, 'help') === 0) {
            $this->sendHelp($message, false);
            return;
        }
        if (strcasecmp(trim($input), 'clear') === 0) {
            $gemini->clearHistory();
            $message->reply('Conversation cleared for this channel.');
            return;
        }

        $message->channel->broadcastTyping()->then(function () use ($message, $input, $gemini) {
            $prompt = $input;
            $refContent = $this->getReferencedMessageContent($message);
            if ($refContent !== null && $refContent !== '') {
                $prompt = "The user is replying to this specific message:\n\"\"\"\n{$refContent}\n\"\"\"\n\nTheir reply: {$input}";
            }
            $reply = $gemini->chat($prompt);
            return $this->sendLongMessage($message, $reply);
        })->otherwise(function (\Throwable $e) use ($message) {
            echo "[Discord] " . $e->getMessage() . "\n";
            $message->reply('Error: ' . $e->getMessage());
        });
    }

    private function getFbHelpContent(): string
    {
        $p = $this->fbPrefix;
        return "**Commands (fb — rules mode)**\n" .
            "• `{$p}` (only) — this list\n" .
            "• `{$p} <message>` — chat with Gemini (follows rules)\n  e.g. `{$p} What is PHP?`\n" .
            "• `{$p} addrule <text>` — add a rule\n  e.g. `{$p} addrule Always be polite`\n" .
            "• `{$p} editrule <number> <new text>` — edit rule at number\n  e.g. `{$p} editrule 1 Always be concise`\n" .
            "• `{$p} removerule <number>` — remove rule at number\n  e.g. `{$p} removerule 2`\n" .
            "• `{$p} listrules` — list all rules with numbers\n" .
            "• `{$p} clear` — clear conversation for this channel";
    }

    private function isPrefixDelimiter(string $content, int $prefixLen): bool
    {
        if (strlen($content) <= $prefixLen) {
            return false;
        }
        $ch = $content[$prefixLen];
        return ctype_space($ch) || $ch === ',';
    }

    private function sendHelp(Message $message, bool $useFb): void
    {
        if ($useFb) {
            $message->reply($this->getFbHelpContent());
        } else {
            $tb = $this->tbPrefix;
            $fb = $this->fbPrefix;
            $message->reply(
                "**TruFraudBot** — `tb` = normal chat (shared in this channel), `fb` = chat that follows your rules.\n" .
                "• `{$tb} <message>` — chat normally (or @mention); everyone in this channel shares the convo\n" .
                "• `{$tb} clear` — new conversation for this channel\n" .
                "• `{$fb}` — rules-mode command list"
            );
        }
    }
}
