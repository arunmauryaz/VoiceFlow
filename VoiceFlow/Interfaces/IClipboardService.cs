using System.Threading.Tasks;

namespace VoiceFlow.Interfaces;

public interface IClipboardService
{
    Task<string?> GetTextAsync();
    Task SetTextAsync(string text);
}
