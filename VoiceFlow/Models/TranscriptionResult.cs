using System;

namespace VoiceFlow.Models;

public class TranscriptionResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }

    public static TranscriptionResult Ok(string text, TimeSpan duration) => new()
    {
        Success = true,
        Text = text,
        Duration = duration
    };

    public static TranscriptionResult Fail(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
