using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Providers.Local;

namespace VoiceFlow.Providers;

public sealed class TranscriptionRouter : ITranscriptionProvider
{
    private readonly ISettingsService _settings;
    private readonly ITranscriptionProvider _cloud, _local;
    public TranscriptionRouter(ISettingsService settings, GeminiTranscriptionProvider cloud, LocalTranscriptionProvider local)
        : this(settings, (ITranscriptionProvider)cloud, local) { }
    internal TranscriptionRouter(ISettingsService settings, ITranscriptionProvider cloud, ITranscriptionProvider local)
        => (_settings, _cloud, _local) = (settings, cloud, local);
    private ITranscriptionProvider Current => _settings.Settings.ApiProvider == "Local" ? _local : _cloud;
    public string ProviderName => Current.ProviderName;
    public bool IsPreparing => Current.IsPreparing;
    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default)
        => Current.TranscribeAsync(audioStream, mimeType, ct);
    public Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default) => Current.TestConnectionAsync(ct);
}
