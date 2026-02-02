# TruthBot — Gemini chatbot (C# / .NET)

Console and **Discord** AI chatbot using **Google Gemini** (free tier): text chat and image generation.

## Step-by-step (run locally)

1. **Install .NET 8** — see [Requirements](#requirements) below; run `dotnet --version` to confirm.
2. **Get a Gemini API key** — [Google AI Studio](https://aistudio.google.com/apikey) → Create API key.
3. **Create a `.env` file** in the project folder with:
   ```env
   GEMINI_API_KEY=your-gemini-key
   BOT_PREFIX=tb
   ```
4. **Console mode:** Run `dotnet run`. If `DISCORD_TOKEN` is not set, you get the local chatbot (type to chat, `/image`, `/clear`, `/quit`).
5. **Discord mode:** Add `DISCORD_TOKEN=your-discord-bot-token` to `.env`, [create a bot and invite it](#discord-get-token-set-up-and-test), then run `dotnet run`. In Discord: `tb hello`, `tbimage a red apple`, `tbclear`.

**Deploy 24/7 for free:** See [deploy/README-Oracle.md](deploy/README-Oracle.md) for Oracle Cloud Free Tier.

---

## Requirements

- **.NET 8 SDK** — required to build and run. If `dotnet` is not found:
  - **macOS (Homebrew):** `brew install dotnet@8` then run `brew link --overwrite dotnet@8` (or add to PATH: `echo 'export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"' >> ~/.zshrc` and restart the terminal).
  - **macOS (installer):** [Download .NET 8 SDK for macOS](https://dotnet.microsoft.com/download/dotnet/8.0) (pick ARM64 for Apple Silicon, x64 for Intel), install. If `dotnet` still isn’t found, add it to PATH: `echo 'export PATH="/usr/local/share/dotnet:$PATH"' >> ~/.zshrc` then run `source ~/.zshrc` or open a new terminal.
  - **Windows:** [Download .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and run the installer.
- After installing, run `dotnet --version` to confirm (e.g. `8.0.x`).

## Features

- **Chat**: Multi-turn conversation with Gemini 2.0 Flash; context kept per user/session.
- **Image generation**: Generate images from a prompt (console: `/image`; Discord: `{prefix}image`, e.g. `tbimage`).
- **Modes**: Run as **Discord bot** (when `DISCORD_TOKEN` is set) or **console** (local chat).

## Environment variables (for deployment)

Set these in your host (Railway, Fly.io, Docker, etc.); **Discord mode requires both**:

| Variable | Required | Description |
|----------|----------|-------------|
| `GEMINI_API_KEY` or `GOOGLE_API_KEY` | Yes (Discord) / optional (console) | Gemini API key from [Google AI Studio](https://aistudio.google.com/apikey) |
| `DISCORD_TOKEN` | Yes for Discord | Bot token from [Discord Developer Portal](https://discord.com/developers/applications) |
| `BOT_PREFIX` | No | Command prefix (default `tb`) |

- **Discord**: Set `GEMINI_API_KEY` and `DISCORD_TOKEN`; the app will not prompt, so env is required.
- **Console**: If `GEMINI_API_KEY` is not set, the app will prompt you to type it (local only).

## Discord: get token, set up, and test

`.env` is in `.gitignore` — your keys stay local and are never committed.

### 1. Get your Discord bot token

1. Open **[Discord Developer Portal](https://discord.com/developers/applications)** and log in.
2. Click **New Application**, name it (e.g. “TruthBot”), create it.
3. In the left sidebar click **Bot** → **Add Bot**.
4. Under **Privileged Gateway Intents**, turn **on** **MESSAGE CONTENT INTENT** (required so the bot can read messages).
5. Under **Bot**, click **Reset Token** (or **View Token**), copy the token — this is your **DISCORD_TOKEN**.  
   - If you lose it, use **Reset Token** again (old token stops working).

### 2. Put the token in `.env`

In your project folder you should have a `.env` file. Add or edit this line (use your real token):

```env
DISCORD_TOKEN=your-copied-token-here
```

Keep your Gemini key there too:

```env
GEMINI_API_KEY=your-gemini-key
BOT_PREFIX=tb
```

### 3. Invite the bot to your server

1. In the Developer Portal, open your app → **OAuth2** → **URL Generator**.
2. **Scopes:** check **bot**.
3. **Bot Permissions:** check **Send Messages**, **Attach Files**, **Read Message History**, **View Channels** (or “Administrator” for testing).
4. Copy the **Generated URL**, paste it in your browser, pick a server, click **Authorize**.

### 4. Run and test

From the project folder:

```bash
dotnet run
```

You should see something like: `TruthBot connected as TruthBot` (or your bot’s name). The bot is now online in Discord.

**Test in Discord:**

- **Chat:** In a channel, type `tb hello` or @mention the bot and type `hello`. It should reply with Gemini.
- **Image:** `tbimage a red apple` — it should generate and post an image.
- **New conversation:** `tbclear`.
- **Help:** `tb` (just the prefix) to see commands.

**Test console mode (no Discord):**  
Remove or comment out `DISCORD_TOKEN` in `.env`, run `dotnet run` — the app runs as a local console chatbot (type to chat, `/image`, `/clear`, `/quit`).

## Console mode (local)

If `DISCORD_TOKEN` is **not** set, the app runs as a console chatbot:

- **Commands**: `/image <prompt>`, `/clear`, `/quit`.
- Set `GEMINI_API_KEY` (or type it when prompted).

```bash
cd truthbot
dotnet restore
dotnet run
```

## Local config (optional)

- **User secrets**: `dotnet user-secrets set "GEMINI_API_KEY" "your-key"` (and/or `DISCORD_TOKEN`).
- **Env**: `export GEMINI_API_KEY=...` and `export DISCORD_TOKEN=...`.

Config order: user-secrets → environment variables → prompt (console only).

## Free tier notes

- **Chat**: Gemini 2.0 Flash has generous free limits for the Developer API.
- **Images**: The app uses an image model available on the free tier (e.g. Imagen). If you see “Image generation failed or is not available”, your key or region might not have image generation enabled; you can still use chat. Rate limits (e.g. requests per day) apply; see [Gemini API rate limits](https://ai.google.dev/gemini-api/docs/rate-limits).

**“Quota exceeded” or “limit: 0”:** Your API key’s free-tier quota is used up or not enabled. Try: (1) **Create a new API key** at [Google AI Studio](https://aistudio.google.com/apikey) and put it in `.env` as `GEMINI_API_KEY`. (2) **Wait** — free-tier limits reset over time (per minute / per day). (3) Check [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits) and [usage](https://ai.dev/rate-limit).

## Project layout

- `Program.cs` — Entry point: Discord mode (if `DISCORD_TOKEN` set) or console mode.
- `Services/GeminiService.cs` — Gemini client: `ChatAsync()` (with history) and `GenerateImageAsync()`.
- `Services/DiscordBotService.cs` — Discord bot: prefix commands (e.g. `tbimage`, `tbclear`) and chat per user.

## Tech / packages

- Packages: `Google.GenAI`, `Discord.Net` (see the `.csproj` in this repo)
