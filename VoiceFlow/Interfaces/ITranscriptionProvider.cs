using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface ITranscriptionProvider
{
    string ProviderName { get; }
    Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default);
    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default);
}
