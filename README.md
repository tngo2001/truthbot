# TruFraudBot — Gemini chatbot (PHP)

Console and **Discord** AI chatbot using **Google Gemini** (free tier): two modes — **tb** = normal chat, **fb** = chat that follows your rules (with rule management).

## Step-by-step (run locally)

1. **Install PHP 8.1+** and **Composer** — run `php -v` and `composer -V` to confirm.
2. **Get a Gemini API key** — [Google AI Studio](https://aistudio.google.com/apikey) → Create API key.
3. **Create a `.env` file** in the project folder with:
   ```env
   GEMINI_API_KEY=your-gemini-key
   DISCORD_TOKEN=your-discord-bot-token
   ```
4. **Install dependencies:** `composer install`
5. **Console mode:** Run `php run.php`. If `DISCORD_TOKEN` is not set, you get the local chatbot (type to chat, `/clear`, `/quit`).
6. **Discord mode:** Add `DISCORD_TOKEN` to `.env`, [create a bot and invite it](#discord-get-token-set-up-and-test), then run `php run.php`. In Discord: `tb hello` (normal chat), `fb hello` (chat that follows your rules), `fb addrule Be concise`, `fb listrules`.

---

## Requirements

- **PHP 8.1+** with extensions: `curl`, `json`, `mbstring`, `openssl`
- **Composer** — [getcomposer.org](https://getcomposer.org/)

## Features

- **Two prefixes:** **tb** = normal multi-turn chat; **fb** = chat that follows your rules. **Both are shared per channel**—anyone in the channel can continue the conversation.
- **Rules (fb only):** `fb addrule`, `fb editrule`, `fb removerule`, `fb listrules`. Rules are stored in `rules.txt` and prepended to every `fb` chat.
- **Modes:** Run as **Discord bot** (when `DISCORD_TOKEN` is set) or **console** (local chat).

## Discord commands

| Prefix | Commands | Description |
|--------|----------|-------------|
| **tb** | `tb <message>` | Normal chat with Gemini; **conversation is shared by everyone in the channel** (or thread). |
| **tb** | `tb clear` | Start a new conversation for this channel. |
| **tb** | `tb` | Show help. |
| **fb** | `fb <message>` | Chat with Gemini **following your rules**; **conversation is shared by everyone in the channel**. |
| **fb** | `fb clear` | Clear conversation for this channel. |
| **fb** | `fb addrule <text>` | Add a rule (e.g. `fb addrule Always be polite`). |
| **fb** | `fb editrule <number> <new text>` | Edit rule at that number. |
| **fb** | `fb removerule <number>` | Remove rule at that number. |
| **fb** | `fb listrules` | List all rules with numbers. |
| **fb** | `fb` | Show command list / help. |

You can also @mention the bot and type a message — that uses **tb** (normal chat).

## Environment variables (for deployment)

Set these in your host (Railway, Fly.io, Docker, etc.); **Discord mode requires both**:

| Variable | Required | Description |
|----------|----------|-------------|
| `GEMINI_API_KEY` or `GOOGLE_API_KEY` | Yes (Discord) / optional (console) | Gemini API key from [Google AI Studio](https://aistudio.google.com/apikey) |
| `DISCORD_TOKEN` | Yes for Discord | Bot token from [Discord Developer Portal](https://discord.com/developers/applications) |
| `BOT_PREFIX_FB` | No | fb prefix (default `fb`) |
| `BOT_PREFIX_TB` | No | tb prefix (default `tb`) |
| `RULES_FILE` | No | Path to rules file (default: `rules.txt` in app directory) |

## Discord: get token, set up, and test

`.env` is in `.gitignore` — your keys stay local and are never committed.

### 1. Get your Discord bot token

1. Open **[Discord Developer Portal](https://discord.com/developers/applications)** and log in.
2. Click **New Application**, name it (e.g. "TruFraudBot"), create it.
3. In the left sidebar click **Bot** → **Add Bot**.
4. Under **Privileged Gateway Intents**, turn **on** **MESSAGE CONTENT INTENT** (required so the bot can read messages).
5. Under **Bot**, click **Reset Token** (or **View Token**), copy the token — this is your **DISCORD_TOKEN**.

### 2. Put the token in `.env`**

In your project folder you should have a `.env` file. Add or edit:

```env
DISCORD_TOKEN=your-copied-token-here
GEMINI_API_KEY=your-gemini-key
```

### 3. Invite the bot to your server

1. In the Developer Portal, open your app → **OAuth2** → **URL Generator**.
2. **Scopes:** check **bot**.
3. **Bot Permissions:** check **Send Messages**, **Read Message History**, **View Channels** (or "Administrator" for testing).
4. Copy the **Generated URL**, paste it in your browser, pick a server, click **Authorize**.

### 4. Run and test

From the project folder:

```bash
composer install
php run.php
```

You should see: `TruFraudBot connected as TruFraudBot` (or your bot's name).

**Test in Discord:**

- **Normal chat:** `tb hello` or @mention the bot and type `hello`.
- **Rules chat:** `fb addrule Always reply in one sentence` then `fb What is PHP?`
- **List rules:** `fb listrules`
- **New conversation:** `tb clear`
- **Help:** `tb` or `fb` (just the prefix) to see commands.

**Test console mode (no Discord):**  
Remove or comment out `DISCORD_TOKEN` in `.env`, run `php run.php` — the app runs as a local console chatbot (type to chat, `/clear`, `/quit`).

## Console mode (local)

If `DISCORD_TOKEN` is **not** set, the app runs as a console chatbot:

- **Commands**: `/clear`, `/quit`.
- Set `GEMINI_API_KEY` (or type it when prompted).

```bash
cd trufraudbot
composer install
php run.php
```

## Free tier notes

- **Chat**: Gemini 2.0 Flash has generous free limits for the Developer API.
- **"Quota exceeded" or "limit: 0":** Your API key's free-tier quota is used up or not enabled. Try: (1) **Create a new API key** at [Google AI Studio](https://aistudio.google.com/apikey). (2) **Wait** — free-tier limits reset over time. (3) Check [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits).

## Project layout

- `run.php` — Entry point: Discord mode (if `DISCORD_TOKEN` set) or console mode.
- `src/GeminiService.php` — Gemini client: `chat()` (with history), model fallback.
- `src/DiscordBotService.php` — Discord bot: `tb` and `fb` prefixes, rules commands.
- `src/RulesService.php` — Read/write `rules.txt` for fb rules.
- `rules.txt` — Default rules file (optional; create or use `fb addrule`).

## Tech / packages

- **PHP 8.1+** with Composer
- **team-reflex/discord-php** (Discord bot)
- Gemini API via HTTP (cURL)
