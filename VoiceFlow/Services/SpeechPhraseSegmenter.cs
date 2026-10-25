using System;
using System.IO;
using NAudio.Wave;

namespace VoiceFlow.Services;

// Non-overlapping phrases: each audio sample is transcribed at most once.
// 16 kHz, signed 16-bit mono. Buffered audio is bounded to eight seconds.
internal sealed class SpeechPhraseSegmenter
{
    private readonly MemoryStream _audio = new();
    private int _silenceSamples, _speechSamples;
    public event EventHandler<byte[]>? PhraseReady;

    public void Append(byte[] data, int count, float peak)
    {
        bool speech = peak >= 0.012f;
        if (!speech && _audio.Length == 0) return;
        _audio.Write(data, 0, count);
        if (speech) { _speechSamples += count / 2; _silenceSamples = 0; }
        else _silenceSamples += count / 2;
        if (_silenceSamples >= 8800 || _audio.Length >= 256000) Flush();
    }

    public void Flush()
    {
        if (_speechSamples >= 1600)
        {
            using var wav = new MemoryStream();
            using (var nonClosing = new Helpers.IgnoreDisposeStream(wav))
            using (var writer = new WaveFileWriter(nonClosing, new WaveFormat(16000, 16, 1)))
                writer.Write(_audio.GetBuffer(), 0, (int)_audio.Length);
            PhraseReady?.Invoke(this, wav.ToArray());
        }
        _audio.SetLength(0);
        _speechSamples = _silenceSamples = 0;
    }
}
