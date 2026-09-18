using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Services;

namespace VoiceFlow.Providers.Gemini;

public class GeminiTextProcessor : IAITextProcessor
{
    private readonly ISettingsService _settingsService;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public GeminiTextProcessor(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> ProcessTextAsync(string rawText, TextCleanupMode mode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawText) || mode == TextCleanupMode.Off)
        {
            return rawText;
        }

        string? apiKey = _settingsService.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return rawText;
        }

        string systemInstruction = mode switch
        {
            TextCleanupMode.SmartFormatting =>
                "You are an intelligent text formatting engine. Convert the following spoken dictation into cleanly organized, professional text. Apply appropriate paragraph breaks or concise bullet points if the speaker is listing items. Remove filler words ('um', 'uh', 'you know'), correct punctuation, and ensure clean structure. Preserve the speaker's meaning and vocabulary. Output ONLY the formatted text with no explanations.",
            _ =>
                "You are an intelligent text cleanup engine for speech transcription. Refine the following spoken transcript to improve readability while strictly preserving the speaker's original meaning, tone, and exact choice of words. Remove filler words (such as 'um', 'uh', 'you know', 'like', 'sort of'), remove accidental stuttering or repetitions, fix sentence boundaries, capitalization, and punctuation. Do NOT rewrite aggressively or invent facts. Output ONLY the cleaned text with no explanations."
        };

        string model = !string.IsNullOrWhiteSpace(_settingsService.Settings.GeminiModel)
            ? _settingsService.Settings.GeminiModel
            : "gemini-3.5-flash";

        // Dedicated speech-to-text models like gemini-3.5-transcribe only accept audio.
        // For text cleanup and formatting prompts, route to gemini-3.5-flash.
        if (model.Contains("transcribe", StringComparison.OrdinalIgnoreCase))
        {
            model = "gemini-3.5-flash";
        }

        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = $"{systemInstruction}\n\nTranscript:\n{rawText}" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 2048
            }
        };

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await HttpClient.PostAsync(endpoint, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.LogWarning($"Text cleanup failed with status {response.StatusCode}, using raw text.");
                return rawText;
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);
            string cleaned = ExtractText(responseJson);

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned.Trim();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Exception during text cleanup, falling back to raw transcript: {ex.Message}");
        }

        return rawText;
    }

    private static string ExtractText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textEl))
                        {
                            sb.Append(textEl.GetString());
                        }
                    }
                    return sb.ToString();
                }
            }
        }
        catch { }

        return string.Empty;
    }
}
