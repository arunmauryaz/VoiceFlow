using System.Collections.Concurrent;
using NAudio.Wave;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Services;

namespace VoiceFlow.Tests;

public class ContinuousDictationTests
{
    private sealed class Provider(params string[] words) : ITranscriptionProvider
    {
        public string ProviderName => "Test";
        public int Calls;
        public Task<TranscriptionResult> TranscribeAsync(Stream audio, string mime, CancellationToken ct = default)
            => Task.FromResult(TranscriptionResult.Ok(words[Calls++], TimeSpan.Zero));
        public Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult((true, "Ready"));
    }

    [Fact]
    public void SegmentationFlushesEachPhraseExactlyOnceAndIgnoresSilence()
    {
        var segmenter = new SpeechPhraseSegmenter();
        var phrases = new List<byte[]>();
        segmenter.PhraseReady += (_, wav) => phrases.Add(wav);
        var speech = new byte[16000];
        var silence = new byte[17600];
        segmenter.Append(silence, silence.Length, 0);
        Assert.Empty(phrases);
        segmenter.Append(speech, speech.Length, 0.1f);
        segmenter.Append(silence, silence.Length, 0);
        segmenter.Append(speech, speech.Length, 0.1f);
        segmenter.Flush();
        segmenter.Flush();
        Assert.Equal(2, phrases.Count);
        foreach (var phrase in phrases)
        {
            using var reader = new WaveFileReader(new MemoryStream(phrase));
            Assert.Equal(16000, reader.WaveFormat.SampleRate);
            Assert.True(reader.Length > 0);
        }
    }

    [Fact]
    public async Task PhrasesAreInsertedInOrderWhileStillListeningAndFinalTextIsNotInsertedTwice()
    {
        var provider = new Provider("First phrase.", "Second phrase.");
        var inserted = new List<string>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new ContinuousDictationSession(provider, (addition, complete) =>
        {
            inserted.Add(addition);
            first.TrySetResult();
            return Task.CompletedTask;
        }, CancellationToken.None);
        Assert.True(session.TryAdd(new byte[] { 1 }));
        await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(inserted); // Output arrives before CompleteAsync / recording stop.
        Assert.True(session.TryAdd(new byte[] { 2 }));
        string final = await session.CompleteAsync();
        Assert.Equal("First phrase. Second phrase.", final);
        Assert.Equal(final, string.Concat(inserted));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public void PhraseBufferIsBoundedForLongUnbrokenSpeech()
    {
        var segmenter = new SpeechPhraseSegmenter();
        var phrases = new List<byte[]>();
        segmenter.PhraseReady += (_, wav) => phrases.Add(wav);
        var audio = new byte[1600];
        for (int i = 0; i < 500; i++) segmenter.Append(audio, audio.Length, 0.1f);
        Assert.True(phrases.Count >= 3);
        Assert.All(phrases, phrase => Assert.InRange(phrase.Length, 44, 258000));
    }

    [Fact]
    public void UnicodeEntryReleasesAndRestoresHeldShortcutModifiersAndPreservesUnicode()
    {
        var inputs = WindowsTextInjectionService.BuildUnicodeInputs("Hi नमस्ते", new ushort[] { 0xA2, 0x5B });
        var characters = inputs.Where(i => i.u.ki.dwFlags == 0x0004).Select(i => (char)i.u.ki.wScan);
        Assert.Equal("Hi नमस्ते", new string(characters.ToArray()));
        Assert.Equal((ushort)0xA2, inputs[^2].u.ki.wVk);
        Assert.Equal((ushort)0x5B, inputs[^1].u.ki.wVk);
        Assert.Equal((uint)0, inputs[^1].u.ki.dwFlags);
    }
}
