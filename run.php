<?php

declare(strict_types=1);

/**
 * TruFraudBot — Entry point.
 * Discord mode when DISCORD_TOKEN is set; otherwise console mode.
 */

// Load .env from project directory
$envPaths = [
    __DIR__ . DIRECTORY_SEPARATOR . '.env',
    getcwd() . DIRECTORY_SEPARATOR . '.env',
];
foreach ($envPaths as $path) {
    if (is_file($path)) {
        $lines = file($path, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES);
        foreach ($lines as $line) {
            $line = trim($line);
            if ($line === '' || $line[0] === '#') {
                continue;
            }
            $i = strpos($line, '=');
            if ($i === false || $i === 0) {
                continue;
            }
            $key = trim(substr($line, 0, $i));
            $value = trim(substr($line, $i + 1));
            if (strlen($value) >= 2 && $value[0] === '"' && $value[strlen($value) - 1] === '"') {
                $value = str_replace('\\"', '"', substr($value, 1, -1));
            }
            putenv("$key=$value");
            $_ENV[$key] = $value;
        }
        break;
    }
}

function env(string $key, string $default = ''): string
{
    $v = getenv($key);
    if ($v !== false && $v !== '') {
        return $v;
    }
    return $_ENV[$key] ?? $default;
}

$geminiApiKey = env('GEMINI_API_KEY') ?: env('GOOGLE_API_KEY') ?: '';
$discordToken = trim(env('DISCORD_TOKEN') ?: '');

// Discord mode
if ($discordToken !== '') {
    if ($geminiApiKey === '') {
        echo "Discord mode requires GEMINI_API_KEY (or GOOGLE_API_KEY). Set it in your environment.\n";
        exit(1);
    }
    require __DIR__ . '/vendor/autoload.php';

    $fbPrefix = env('BOT_PREFIX_FB', 'fb');
    $tbPrefix = env('BOT_PREFIX_TB', 'tb');
    $rulesPath = env('RULES_FILE') ?: null;
    $rules = $rulesPath !== '' ? new \TruFraudBot\RulesService($rulesPath) : new \TruFraudBot\RulesService();

    $intents = \Discord\WebSockets\Intents::GUILDS
        | \Discord\WebSockets\Intents::GUILD_MESSAGES
        | \Discord\WebSockets\Intents::DIRECT_MESSAGES;
    if (defined('Discord\WebSockets\Intents::MESSAGE_CONTENT')) {
        $intents |= \Discord\WebSockets\Intents::MESSAGE_CONTENT;
    } else {
        $intents |= 0x8000; // MESSAGE_CONTENT = 32768
    }

    $discord = new \Discord\Discord([
        'token' => $discordToken,
        'intents' => $intents,
    ]);

    $bot = new \TruFraudBot\DiscordBotService($geminiApiKey, $fbPrefix, $tbPrefix, $rules);
    $discord->on('init', function (\Discord\Discord $d) use ($bot) {
        $bot->register($d);
    });
    $discord->on('ready', function (\Discord\Discord $d) {
        echo 'TruFraudBot connected as ' . ($d->user->username ?? 'unknown') . "\n";
    });

    $discord->run();
    exit(0);
}

// Console mode
if ($geminiApiKey === '') {
    echo "Enter your Gemini API key (from https://aistudio.google.com/apikey):\n";
    $geminiApiKey = trim(fgets(STDIN) ?: '');
}
if ($geminiApiKey === '') {
    echo "No API key provided. Set GEMINI_API_KEY or GOOGLE_API_KEY.\n";
    exit(1);
}

require __DIR__ . '/vendor/autoload.php';

$gemini = new \TruFraudBot\GeminiService($geminiApiKey);

echo "\nTruFraudBot — Gemini chatbot\n";
echo "Commands: /clear  → new conversation  |  /quit  → exit\n\n";

while (true) {
    echo "You: ";
    $input = trim(fgets(STDIN) ?: '');
    if ($input === '') {
        continue;
    }
    if (strtolower($input) === '/quit') {
        echo "Bye.\n";
        break;
    }
    if (strtolower($input) === '/clear') {
        $gemini->clearHistory();
        echo "Conversation cleared.\n";
        continue;
    }
    echo "Gemini: ";
    try {
        $reply = $gemini->chat($input);
        echo $reply . "\n";
    } catch (\Throwable $ex) {
        echo "Error: " . $ex->getMessage() . "\n";
    }
    echo "\n";
}

exit(0);
