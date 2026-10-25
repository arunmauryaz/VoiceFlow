using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;

namespace VoiceFlow.Services;

internal sealed class ContinuousDictationSession : IDisposable
{
    private readonly Channel<byte[]> _phrases = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _cancel;
    private readonly Task _consumer;
    private readonly StringBuilder _text = new();
    public string Transcript => _text.ToString();
    public event EventHandler? Faulted;

    public ContinuousDictationSession(ITranscriptionProvider provider, Func<string, string, Task> onPhrase, CancellationToken ct)
    {
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _consumer = ConsumeAsync(provider, onPhrase);
        _ = _consumer.ContinueWith(task => { _ = task.Exception; Faulted?.Invoke(this, EventArgs.Empty); },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public bool TryAdd(byte[] wav)
    {
        if (_phrases.Writer.TryWrite(wav)) return true;
        _phrases.Writer.TryComplete(new InvalidOperationException("Continuous typing cannot keep up. Choose a smaller model."));
        return false;
    }

    private async Task ConsumeAsync(ITranscriptionProvider provider, Func<string, string, Task> onPhrase)
    {
        await foreach (var wav in _phrases.Reader.ReadAllAsync(_cancel.Token))
        {
            using var audio = new MemoryStream(wav, false);
            var result = await provider.TranscribeAsync(audio, "audio/wav", _cancel.Token);
            if (!result.Success)
            {
                if (result.ErrorMessage?.StartsWith("No speech", StringComparison.OrdinalIgnoreCase) == true) continue;
                throw new InvalidOperationException(result.ErrorMessage ?? "Transcription failed.");
            }
            string text = result.Text.Trim();
            if (text.Length == 0) continue;
            string addition = _text.Length == 0 || char.IsWhiteSpace(_text[^1]) ? text : " " + text;
            _text.Append(addition);
            await onPhrase(addition, _text.ToString());
        }
    }

    public async Task<string> CompleteAsync()
    {
        _phrases.Writer.TryComplete();
        await _consumer;
        return Transcript;
    }
    public void Dispose() { _phrases.Writer.TryComplete(); _cancel.Cancel(); _cancel.Dispose(); }
}
