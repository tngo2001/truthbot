using Google.GenAI;
using Google.GenAI.Types;

namespace Truthbot.Services;

/// <summary>
/// Handles chat with Gemini and image generation using the free Gemini API.
/// Chat and image use primary model with fallbacks on quota/error.
/// </summary>
public class GeminiService
{
    private readonly Client _client;
    private readonly List<Content> _chatHistory = [];

    private static readonly string[] ChatModels = ["gemini-2.5-flash", "gemini-2.0-flash", "gemini-2.0-flash-lite"];
    private static readonly string[] ImageModels = ["imagen-3.0-generate-002", "gemini-2.5-flash-image"];

    public GeminiService(string apiKey)
    {
        _client = new Client(apiKey: apiKey);
    }

    private static bool IsQuotaOrNotFound(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("429", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("404", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Send a message and get a text response, keeping conversation history.
    /// Tries each chat model in order; on quota/404 tries next, otherwise returns error message.
    /// </summary>
    public async Task<string> ChatAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        _chatHistory.Add(new Content
        {
            Role = "user",
            Parts = [new Part { Text = userMessage }]
        });

        var config = new GenerateContentConfig
        {
            Temperature = 0.7f,
            MaxOutputTokens = 8192,
        };

        foreach (var model in ChatModels)
        {
            try
            {
                var response = await _client.Models.GenerateContentAsync(
                    model,
                    _chatHistory,
                    config);

                var text = response.Candidates?[0].Content?.Parts?[0].Text?.Trim() ?? "(No response.)";
                _chatHistory.Add(new Content
                {
                    Role = "model",
                    Parts = [new Part { Text = text }]
                });
                return text;
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("API key", StringComparison.OrdinalIgnoreCase) || msg.Contains("401"))
                    return "Invalid or missing Gemini API key. Set GEMINI_API_KEY in .env or get a key at https://aistudio.google.com/apikey";
                if (!IsQuotaOrNotFound(ex))
                    return "Gemini API error: " + (msg.Length > 200 ? msg[..200] + "…" : msg);
            }
        }

        return "All free-tier models are out of quota for today. Resets at midnight Pacific. See https://ai.google.dev/gemini-api/docs/rate-limits";
    }

    /// <summary>
    /// Generate an image from a text prompt. Saves to outputFolder and returns the file path.
    /// Tries each image model in order; on failure tries next. Returns null if all fail.
    /// </summary>
    public async Task<string?> GenerateImageAsync(string prompt, string outputFolder, CancellationToken cancellationToken = default)
    {
        var config = new GenerateImagesConfig
        {
            NumberOfImages = 1,
            AspectRatio = "1:1",
            OutputMimeType = "image/png",
        };

        foreach (var model in ImageModels)
        {
            try
            {
                var response = await _client.Models.GenerateImagesAsync(
                    model,
                    prompt,
                    config);

                var generated = response.GeneratedImages?.FirstOrDefault();
                var image = generated?.Image;
                if (image?.ImageBytes == null || image.ImageBytes.Length == 0) continue;

                Directory.CreateDirectory(outputFolder);
                var fileName = $"gemini_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(outputFolder, fileName);
                var bytes = image.ImageBytes;

                await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
                return Path.GetFullPath(path);
            }
            catch
            {
                // try next model
            }
        }

        return null;
    }

    /// <summary>
    /// Clear conversation history so the next message starts a new context.
    /// </summary>
    public void ClearHistory() => _chatHistory.Clear();
}
