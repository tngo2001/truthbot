using Google.GenAI;
using Google.GenAI.Types;

namespace Truthbot.Services;

/// <summary>
/// Handles chat with Gemini and image generation using the free Gemini API.
/// </summary>
public class GeminiService
{
    private readonly Client _client;
    private readonly List<Content> _chatHistory = [];
    // Current generateContent models: gemini-2.5-flash, gemini-2.0-flash, gemini-2.0-flash-lite
    private const string ChatModel = "gemini-2.5-flash";
    // Free-tier image: Imagen 3 (100/day) or try gemini-2.5-flash-image if available
    private const string ImageModel = "imagen-3.0-generate-002";

    public GeminiService(string apiKey)
    {
        _client = new Client(apiKey: apiKey);
    }

    /// <summary>
    /// Send a message and get a text response, keeping conversation history.
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

        GenerateContentResponse response;
        try
        {
            response = await _client.Models.GenerateContentAsync(
                model: ChatModel,
                contents: _chatHistory,
                config: config);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("quota", StringComparison.OrdinalIgnoreCase) || msg.Contains("429"))
                return "Gemini API quota exceeded. Create or check your key at https://aistudio.google.com/apikey and see https://ai.google.dev/gemini-api/docs/rate-limits — try again in a minute or use a new key.";
            if (msg.Contains("API key", StringComparison.OrdinalIgnoreCase) || msg.Contains("401"))
                return "Invalid or missing Gemini API key. Set GEMINI_API_KEY in .env or get a key at https://aistudio.google.com/apikey";
            return "Gemini API error: " + (msg.Length > 200 ? msg[..200] + "…" : msg);
        }

        var text = response.Candidates?[0].Content?.Parts?[0].Text?.Trim() ?? "(No response.)";
        _chatHistory.Add(new Content
        {
            Role = "model",
            Parts = [new Part { Text = text }]
        });
        return text;
    }

    /// <summary>
    /// Generate an image from a text prompt. Saves to outputFolder and returns the file path.
    /// Uses Imagen on free tier (rate limits apply).
    /// </summary>
    public async Task<string?> GenerateImageAsync(string prompt, string outputFolder, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = new GenerateImagesConfig
            {
                NumberOfImages = 1,
                AspectRatio = "1:1",
                OutputMimeType = "image/png",
            };

            var response = await _client.Models.GenerateImagesAsync(
                model: ImageModel,
                prompt: prompt,
                config: config);

            var generated = response.GeneratedImages?.FirstOrDefault();
            var image = generated?.Image;
            if (image?.ImageBytes == null || image.ImageBytes.Length == 0) return null;

            Directory.CreateDirectory(outputFolder);
            var fileName = $"gemini_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
            var path = Path.Combine(outputFolder, fileName);
            var bytes = image.ImageBytes;

            await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clear conversation history so the next message starts a new context.
    /// </summary>
    public void ClearHistory() => _chatHistory.Clear();
}
