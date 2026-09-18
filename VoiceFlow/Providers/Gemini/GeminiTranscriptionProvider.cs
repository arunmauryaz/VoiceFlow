using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Services;

namespace VoiceFlow.Providers.Gemini;

public class GeminiTranscriptionProvider : ITranscriptionProvider
{
    private readonly ISettingsService _settingsService;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string ProviderName => "Google Gemini";

    public GeminiTranscriptionProvider(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default)
    {
        string? apiKey = _settingsService.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranscriptionResult.Fail("Gemini API key is missing. Please enter your API key in Settings.");
        }

        if (audioStream == null || audioStream.Length == 0)
        {
            return TranscriptionResult.Fail("Audio recording is empty.");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            byte[] audioBytes;
            if (audioStream is MemoryStream ms)
            {
                audioBytes = ms.ToArray();
            }
            else
            {
                using var copy = new MemoryStream();
                await audioStream.CopyToAsync(copy, ct);
                audioBytes = copy.ToArray();
            }

            if (audioBytes.Length < 1000)
            {
                return TranscriptionResult.Fail("Recording was too short to transcribe.");
            }

            string base64Audio = Convert.ToBase64String(audioBytes);
            string model = !string.IsNullOrWhiteSpace(_settingsService.Settings.GeminiModel)
                ? _settingsService.Settings.GeminiModel
                : "gemini-2.5-flash";

            string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                text = "You are an expert voice-to-text transcription engine. Transcribe the following spoken audio verbatim. Maintain the speaker's exact wording, numbers, and language. Apply appropriate capitalization and punctuation (periods, commas, question marks). Do NOT add any preamble, quotes, timestamps, markdown code fences, or explanations. Output ONLY the transcription."
                            },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = mimeType,
                                    data = base64Audio
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.0,
                    maxOutputTokens = 2048
                }
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            AppLogger.LogInfo($"Sending audio to Gemini ({model}, {audioBytes.Length} bytes)...");
            using var response = await HttpClient.PostAsync(endpoint, content, ct);
            sw.Stop();

            string responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = ParseErrorMessage(response.StatusCode, responseJson);
                AppLogger.LogWarning($"Gemini API error: {response.StatusCode} - {errorMsg}");
                return TranscriptionResult.Fail(errorMsg);
            }

            string transcript = ExtractTextFromGeminiResponse(responseJson);
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return TranscriptionResult.Fail("No speech detected in audio.");
            }

            AppLogger.LogInfo($"Transcription received in {sw.ElapsedMilliseconds}ms: \"{(transcript.Length > 40 ? transcript[..40] + "..." : transcript)}\"");
            return TranscriptionResult.Ok(transcript.Trim(), sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return TranscriptionResult.Fail("Transcription request timed out. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            AppLogger.LogError("Network error during Gemini transcription.", ex);
            return TranscriptionResult.Fail("Network connection failed. Please check your internet connection.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Unexpected error during transcription.", ex);
            return TranscriptionResult.Fail($"Transcription error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default)
    {
        string? apiKey = _settingsService.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, "Please enter an API key first.");
        }

        string model = !string.IsNullOrWhiteSpace(_settingsService.Settings.GeminiModel)
            ? _settingsService.Settings.GeminiModel
            : "gemini-2.5-flash";

        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var testPayload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = "Respond with 'OK' to test connection." }
                    }
                }
            }
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(testPayload), Encoding.UTF8, "application/json");
            using var response = await HttpClient.PostAsync(endpoint, content, ct);
            sw.Stop();

            string body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"✓ Connected successfully ({sw.ElapsedMilliseconds}ms roundtrip)");
            }
            else
            {
                string msg = ParseErrorMessage(response.StatusCode, body);
                return (false, $"✕ Connection failed: {msg}");
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, $"✕ Network error: Could not reach Gemini API ({ex.Message})");
        }
        catch (Exception ex)
        {
            return (false, $"✕ Connection test error: {ex.Message}");
        }
    }

    private static string ParseErrorMessage(HttpStatusCode statusCode, string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("error", out var errorEl) &&
                errorEl.TryGetProperty("message", out var msgEl))
            {
                string serverMsg = msgEl.GetString() ?? string.Empty;

                if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden || serverMsg.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase))
                {
                    return "Invalid or unauthorized API key.";
                }
                if (statusCode == (HttpStatusCode)429 || serverMsg.Contains("Resource has been exhausted", StringComparison.OrdinalIgnoreCase))
                {
                    return "Gemini API quota/rate limit reached. Please try again later.";
                }
                if (!string.IsNullOrEmpty(serverMsg))
                {
                    return serverMsg;
                }
            }
        }
        catch { }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Invalid request to Gemini API.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Invalid or unauthorized API key.",
            (HttpStatusCode)429 => "Gemini API rate limit reached. Please wait a moment.",
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                => "Gemini service is temporarily unavailable. Please try again.",
            _ => $"HTTP {(int)statusCode} error from Gemini API."
        };
    }

    private static string ExtractTextFromGeminiResponse(string responseJson)
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
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to parse Gemini response JSON.", ex);
        }

        return string.Empty;
    }
}
