using Avalonia.Headless.XUnit;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Services;
using VoiceFlow.ViewModels;

namespace VoiceFlow.Tests;

public class DictationPipelineTests
{
    private sealed class Recorder : IAudioRecorder
    {
        public bool IsRecording { get; private set; }
        public int Starts;
        public TaskCompletionSource<Stream>? PendingStop;
        public bool HasInputDevices => true;
        public TimeSpan RecordingDuration => TimeSpan.FromSeconds(2);
        public int CurrentDeviceIndex => -1;
        public event EventHandler<float>? AudioLevelChanged;
        public event EventHandler? RecordingStarted;
        public event EventHandler? RecordingStopped;
        public event EventHandler<byte[]>? PhraseReady;
        public void StartRecording(int deviceIndex = -1, bool continuous = false)
        { Starts++; IsRecording = true; RecordingStarted?.Invoke(this, EventArgs.Empty); AudioLevelChanged?.Invoke(this, 0.1f); }
        public Task<Stream> StopRecordingAsync()
        {
            IsRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
            var pending = PendingStop;
            PendingStop = null;
            return pending?.Task ?? Task.FromResult<Stream>(new MemoryStream(new byte[2000]));
        }
        public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => Array.Empty<AudioDeviceInfo>();
        public void Emit() => PhraseReady?.Invoke(this, new byte[10]);
        public void Dispose() { }
    }
    private sealed class Provider : ITranscriptionProvider
    {
        public int Calls;
        public TaskCompletionSource<TranscriptionResult>? Pending;
        public string ProviderName => "Test";
        public Task<TranscriptionResult> TranscribeAsync(Stream audio, string mime, CancellationToken ct = default)
            => Pending?.Task ?? Task.FromResult(TranscriptionResult.Ok(++Calls == 1 ? "First phrase." : "Second phrase.", TimeSpan.Zero));
        public Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult((true, "Ready"));
    }
    private sealed class Target : ITextInjectionService
    {
        public string Focus = "original-field";
        public List<string> Insertions { get; } = new();
        public IntPtr GetForegroundWindowHandle() => (IntPtr)123;
        public string? GetFocusedControlIdentity() => Focus;
        public bool HasFocusedEditableControl(IntPtr window) => true;
        public Task<bool> InjectTextAsync(string text, IntPtr window, bool preserveClipboard = true)
        { Insertions.Add(text); return Task.FromResult(true); }
    }

    [AvaloniaFact]
    public async Task VeryFastShortcutWaitsOnlyForDeviceReleaseThenStartsWithoutAnotherPress()
    {
        var settings = new MockSettingsService();
        settings.Settings.ShowOverlay = true;
        using var recorder = new Recorder();
        using var hotkeys = new WindowsHotkeyService();
        using var overlay = new RecordingOverlayViewModel();
        var provider = new Provider { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var manager = new AppStateManager(settings, recorder, provider, new GeminiTextProcessor(settings),
            hotkeys, new Target(), new MockClipboardService(), overlay);
        Assert.True(manager.StartRecording());
        var released = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.PendingStop = released;
        var oldRequest = manager.StopRecordingAsync();
        manager.DismissOverlay();
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StateChanged += (_, state) => { if (state == AppState.Recording) restarted.TrySetResult(); };
        Assert.True(manager.StartRecording());
        Assert.Equal(1, recorder.Starts); // Cannot reopen a device which has not released yet.
        released.SetResult(new MemoryStream(new byte[2000]));
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, recorder.Starts);
        Assert.True(overlay.IsVisible);
        provider.Pending.SetResult(TranscriptionResult.Ok("Superseded", TimeSpan.Zero));
        await oldRequest;
        Assert.Equal(AppState.Recording, manager.CurrentState);
        await manager.CancelRecordingAsync();
    }

    [AvaloniaFact]
    public async Task NextShortcutImmediatelyRestoresDismissedListeningWithoutRestartingMicrophone()
    {
        var settings = new MockSettingsService();
        settings.Settings.ShowOverlay = true;
        using var recorder = new Recorder();
        using var hotkeys = new WindowsHotkeyService();
        using var overlay = new RecordingOverlayViewModel();
        using var manager = new AppStateManager(settings, recorder, new Provider(), new GeminiTextProcessor(settings),
            hotkeys, new Target(), new MockClipboardService(), overlay);
        Assert.True(manager.StartRecording());
        manager.DismissOverlay();
        Assert.True(manager.StartRecording());
        Assert.True(overlay.IsVisible);
        Assert.Equal(AppState.Recording, manager.CurrentState);
        Assert.Equal("00:02", overlay.DurationText);
        Assert.Equal(1, recorder.Starts);
        await manager.CancelRecordingAsync();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NextShortcutStartsDuringDismissedProcessingAndLateResultCannotChangeNewRecording(bool fail)
    {
        var settings = new MockSettingsService();
        settings.Settings.ShowOverlay = true;
        using var recorder = new Recorder();
        using var hotkeys = new WindowsHotkeyService();
        using var overlay = new RecordingOverlayViewModel();
        var provider = new Provider { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var target = new Target();
        using var manager = new AppStateManager(settings, recorder, provider, new GeminiTextProcessor(settings),
            hotkeys, target, new MockClipboardService(), overlay);
        Assert.True(manager.StartRecording());
        var oldRequest = manager.StopRecordingAsync();
        manager.DismissOverlay();
        Assert.True(manager.StartRecording());
        Assert.True(overlay.IsVisible);
        Assert.Equal(2, recorder.Starts);
        provider.Pending.SetResult(fail ? TranscriptionResult.Fail("Old failure") : TranscriptionResult.Ok("Old text", TimeSpan.Zero));
        await oldRequest;
        Assert.Equal(AppState.Recording, manager.CurrentState);
        Assert.True(recorder.IsRecording);
        Assert.True(overlay.IsVisible);
        Assert.Null(manager.LastTranscription);
        Assert.Empty(target.Insertions);
        await manager.CancelRecordingAsync();
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DismissedNotchStaysHiddenThroughCompletionOrError(bool whileRecording, bool fail)
    {
        var settings = new MockSettingsService();
        settings.Settings.ShowOverlay = true;
        using var recorder = new Recorder();
        using var hotkeys = new WindowsHotkeyService();
        using var overlay = new RecordingOverlayViewModel();
        var provider = new Provider { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var manager = new AppStateManager(settings, recorder, provider, new GeminiTextProcessor(settings),
            hotkeys, new Target(), new MockClipboardService(), overlay);
        Assert.True(manager.StartRecording(previewOnly: true));
        Assert.True(overlay.IsVisible);
        if (whileRecording)
        {
            manager.DismissOverlay();
            Assert.False(overlay.IsVisible);
            Assert.True(recorder.IsRecording);
        }
        var finish = manager.StopRecordingAsync();
        Assert.Equal(AppState.Transcribing, manager.CurrentState);
        if (!whileRecording) manager.DismissOverlay();
        Assert.False(overlay.IsVisible);
        Assert.True(overlay.HideImmediately);
        provider.Pending.SetResult(fail ? TranscriptionResult.Fail("Test failure") : TranscriptionResult.Ok("Recognized text", TimeSpan.Zero));
        await finish;
        Assert.False(overlay.IsVisible);
        if (!fail)
        {
            Assert.Equal("Recognized text", manager.LastTranscription);
            Assert.True(manager.StartRecording(previewOnly: true));
            Assert.True(overlay.IsVisible);
            await manager.CancelRecordingAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuousOutputRespectsOriginalFieldAndNeverPastesFullTextTwice(bool changeFocus)
    {
        var settings = new MockSettingsService();
        settings.Settings.OutputMode = DictationOutputMode.ContinuousTyping;
        settings.Settings.ShowOverlay = false;
        using var recorder = new Recorder();
        using var hotkeys = new WindowsHotkeyService();
        using var overlay = new RecordingOverlayViewModel();
        var provider = new Provider();
        var target = new Target();
        var clipboard = new MockClipboardService();
        using var manager = new AppStateManager(settings, recorder, provider, new GeminiTextProcessor(settings), hotkeys, target, clipboard, overlay);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int partials = 0, completions = 0;
        manager.PartialTranscription += (_, _) => { if (++partials == 1) first.SetResult(); else second.SetResult(); };
        manager.TranscriptionCompleted += (_, _) => completions++;
        Assert.True(manager.StartRecording());
        recorder.Emit();
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(target.Insertions);
        Assert.Equal(AppState.Recording, manager.CurrentState);
        if (changeFocus) target.Focus = "another-field";
        recorder.Emit();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await manager.StopRecordingAsync();
        Assert.Equal(changeFocus ? 1 : 2, target.Insertions.Count);
        Assert.Equal("First phrase. Second phrase.", manager.LastTranscription);
        Assert.Equal(1, completions);
        if (changeFocus) Assert.Equal(manager.LastTranscription, await clipboard.GetTextAsync());
        Assert.Equal(AppState.Ready, manager.CurrentState);
    }

    [AvaloniaFact]
    public async Task FinishedModeWaitsUntilRecordingStops()
    {
        var settings = new MockSettingsService();
        settings.Settings.ShowOverlay = false;
        using var recorder = new Recorder();
        using var hotkeys = new WindowsHotkeyService();
        using var overlay = new RecordingOverlayViewModel();
        var provider = new Provider();
        var target = new Target();
        using var manager = new AppStateManager(settings, recorder, provider, new GeminiTextProcessor(settings), hotkeys, target, new MockClipboardService(), overlay);
        Assert.True(manager.StartRecording());
        Assert.False(manager.StartRecording());
        Assert.Equal(0, provider.Calls);
        Assert.Empty(target.Insertions);
        await manager.StopRecordingAsync();
        Assert.Equal(1, provider.Calls);
        Assert.Single(target.Insertions);
    }
}
