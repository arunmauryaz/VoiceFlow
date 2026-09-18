using System;

namespace VoiceFlow.Models;

public class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string RawText { get; set; } = string.Empty;
    public string FinalText { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
}
