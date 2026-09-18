using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface IAITextProcessor
{
    Task<string> ProcessTextAsync(string rawText, TextCleanupMode mode, CancellationToken ct = default);
}
