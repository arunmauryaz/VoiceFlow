using System;

namespace VoiceFlow.Models;

public class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string RawText { get; set; } = string.Empty;
    public string FinalText { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }

    public string FormattedTime => Timestamp.ToString("hh:mm tt");
    public string WordCount => $"{FinalText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} words";
    public string RelativeDate => Timestamp.Date == DateTime.Today
        ? "TODAY"
        : (Timestamp.Date == DateTime.Today.AddDays(-1) ? "YESTERDAY" : Timestamp.ToString("MMMM dd, yyyy").ToUpperInvariant());
}
