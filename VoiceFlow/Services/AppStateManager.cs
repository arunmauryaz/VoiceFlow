using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.ViewModels;

namespace VoiceFlow.Services;

public class AppStateManager : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioRecorder _audioRecorder;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IAITextProcessor _aiTextProcessor;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ITextInjectionService _textInjectionService;
    private readonly IClipboardService _clipboardService;
    private readonly RecordingOverlayViewModel _overlayViewModel;
    private readonly DispatcherTimer _recordingTimer;
    private CancellationTokenSource? _sessionCancel;
    private ContinuousDictationSession? _continuous;
    private IntPtr _targetHwnd;
    private string? _targetControl;
    private bool _previewOnly, _typingPaused, _continuousMode, _autoPaste, _preserveClipboard, _showOverlay;
    private float _latestLevel;
    private bool _isDisposed;
    private int _sessionVersion;
    private Task<Stream>? _pendingAudioStop;
    private AppState _currentState = AppState.Ready;

    public AppState CurrentState
    {
        get => _currentState;
        private set
        {
            _currentState = value;
            if (value != AppState.Recording) _hotkeyService.ResetActivation();
            StateChanged?.Invoke(this, value);
        }
    }
    public string? LastTranscription { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public event EventHandler<AppState>? StateChanged;
    public event EventHandler<string>? TranscriptionCompleted;
    public event EventHandler<string>? PartialTranscription;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler<string>? ErrorOccurred;

    public AppStateManager(ISettingsService settingsService, IAudioRecorder audioRecorder,
        ITranscriptionProvider transcriptionProvider, IAITextProcessor aiTextProcessor,
        IGlobalHotkeyService hotkeyService, ITextInjectionService textInjectionService,
        IClipboardService clipboardService, RecordingOverlayViewModel overlayViewModel)
    {
        (_settingsService, _audioRecorder, _transcriptionProvider, _aiTextProcessor, _hotkeyService,
            _textInjectionService, _clipboardService, _overlayViewModel) =
            (settingsService, audioRecorder, transcriptionProvider, aiTextProcessor, hotkeyService,
            textInjectionService, clipboardService, overlayViewModel);
        _hotkeyService.EscapePressed += OnEscapePressed;
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.HotkeyReleased += OnHotkeyReleased;
        _audioRecorder.AudioLevelChanged += OnAudioLevelChanged;
        _audioRecorder.PhraseReady += OnPhraseReady;
        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _recordingTimer.Tick += OnRecordingTimerTick;
    }

    public void Initialize()
    {
        var settings = _settingsService.Settings;
        settings.EnsureShortcuts();
        _hotkeyService.RegisterHotkeys(settings.HoldHotkey!, settings.ToggleHotkey!);
    }
    private void OnHotkeyPressed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => StartRecording());
    private void OnHotkeyReleased(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () => await StopRecordingAsync());

    private void OnEscapePressed(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_isDisposed) DismissOverlay(); }, DispatcherPriority.Send);

    public void DismissOverlay()
    {
        // Keep later completion/error callbacks from reopening this session's notch.
        _showOverlay = false;
        _hotkeyService.ResetActivation();
        _overlayViewModel.DismissPopup();
    }

    public bool StartRecording(bool previewOnly = false)
    {
        bool restarting = false;
        if (!_isDisposed && !_showOverlay && !_overlayViewModel.IsVisible && _settingsService.Settings.ShowOverlay)
        {
            if (CurrentState == AppState.Recording)
            {
                _showOverlay = true;
                _overlayViewModel.ShowRecording();
                _overlayViewModel.UpdateDuration(_audioRecorder.RecordingDuration);
                return true;
            }
            if (CurrentState is AppState.Transcribing or AppState.Cleaning or AppState.Error && !_audioRecorder.IsRecording)
            {
                if (_pendingAudioStop is { IsCompleted: false } pending)
                {
                    _ = RestartAfterAudioStoppedAsync(pending, _sessionVersion);
                    return true;
                }
                // Let a fresh shortcut start immediately, even if an old request ignores cancellation.
                _sessionCancel?.Cancel();
                _continuous = null; // The finishing session owns and disposes its consumer.
                _currentState = AppState.Ready; // Preserve the freshly activated shortcut latch.
                restarting = true;
            }
        }
        if (_isDisposed || CurrentState != AppState.Ready || _audioRecorder.IsRecording)
        { if (CurrentState != AppState.Recording) _hotkeyService.ResetActivation(); return false; }
        _showOverlay = _settingsService.Settings.ShowOverlay;
        if (_transcriptionProvider.IsPreparing && !restarting)
        {
            _hotkeyService.ResetActivation();
            if (_showOverlay) _overlayViewModel.ShowError("Model is preparing...");
            return false;
        }
        if (!_audioRecorder.HasInputDevices) { HandleError("No microphone connected"); return false; }
        try
        {
            _sessionCancel?.Dispose();
            _sessionCancel = new CancellationTokenSource();
            int version = ++_sessionVersion;
            var settings = _settingsService.Settings;
            _previewOnly = previewOnly;
            _continuousMode = settings.OutputMode == DictationOutputMode.ContinuousTyping;
            _autoPaste = settings.AutoPaste;
            _preserveClipboard = settings.PreserveClipboard;
            _showOverlay = settings.ShowOverlay;
            _typingPaused = false;
            _latestLevel = 0;
            _targetHwnd = _textInjectionService.GetForegroundWindowHandle();
            _targetControl = _textInjectionService.GetFocusedControlIdentity();
            _continuous?.Dispose();
            _continuous = _continuousMode
                ? new ContinuousDictationSession(_transcriptionProvider, (addition, text) => HandlePhraseAsync(addition, text, version), _sessionCancel.Token) : null;
            if (_continuous != null) _continuous.Faulted += OnContinuousFaulted;
            _audioRecorder.StartRecording(settings.SelectedAudioDeviceIndex, _continuousMode);
            CurrentState = AppState.Recording;
            if (_showOverlay) _overlayViewModel.ShowRecording();
            _recordingTimer.Start();
            return true;
        }
        catch (Exception ex) { AppLogger.LogError("Could not start recording", ex); HandleError("Microphone unavailable. Check your input device."); return false; }
    }

    private async Task RestartAfterAudioStoppedAsync(Task<Stream> pending, int version)
    {
        try { await pending; }
        catch { return; } // The original stop request reports microphone errors.
        if (!_isDisposed && version == _sessionVersion && !_showOverlay) StartRecording();
    }

    private void OnContinuousFaulted(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(async () => { if (!_isDisposed && ReferenceEquals(sender, _continuous)) await StopRecordingAsync(); });
    private void OnPhraseReady(object? sender, byte[] wav)
    {
        if (_continuous != null && !_continuous.TryAdd(wav))
            Dispatcher.UIThread.Post(async () => { if (!_isDisposed) await StopRecordingAsync(); });
    }

    private async Task HandlePhraseAsync(string addition, string transcript, int version)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_isDisposed || version != _sessionVersion || _sessionCancel?.IsCancellationRequested != false) return;
            PartialTranscription?.Invoke(this, transcript);
            if (_previewOnly || !_autoPaste || _typingPaused) return;
            bool focused = _textInjectionService.GetForegroundWindowHandle() == _targetHwnd
                && _targetControl != null && _textInjectionService.GetFocusedControlIdentity() == _targetControl
                && _textInjectionService.HasFocusedEditableControl(_targetHwnd);
            if (!focused || !await _textInjectionService.InjectPartialTextAsync(addition, _targetHwnd, _preserveClipboard))
            {
                _typingPaused = true;
                _overlayViewModel.StatusText = "Typing paused";
            }
        });
    }

    public async Task StopRecordingAsync()
    {
        if (_isDisposed || CurrentState != AppState.Recording) return;
        int version = _sessionVersion;
        var sessionCancel = _sessionCancel!;
        var continuous = _continuous;
        bool IsCurrent() => !_isDisposed && version == _sessionVersion;
        _recordingTimer.Stop();
        CurrentState = AppState.Transcribing;
        if (_showOverlay) _overlayViewModel.ShowTranscribing();
        try
        {
            _pendingAudioStop = _audioRecorder.StopRecordingAsync();
            using var audio = await _pendingAudioStop;
            if (!IsCurrent()) return;
            string text;
            if (continuous != null) text = await continuous.CompleteAsync();
            else
            {
                if (audio.Length < 1000) { FinishReady(); return; }
                var result = await _transcriptionProvider.TranscribeAsync(audio, "audio/wav", sessionCancel.Token);
                if (!IsCurrent()) return;
                if (!result.Success) { HandleError(result.ErrorMessage ?? "Transcription failed."); return; }
                text = result.Text;
                if (!result.ProcessedLocally && _settingsService.Settings.CleanupEnabled && _settingsService.Settings.CleanupMode != TextCleanupMode.Off)
                {
                    CurrentState = AppState.Cleaning;
                    if (_showOverlay) _overlayViewModel.ShowCleaning();
                    text = await _aiTextProcessor.ProcessTextAsync(text, _settingsService.Settings.CleanupMode, sessionCancel.Token);
                }
            }
            if (!IsCurrent() || sessionCancel.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text)) { FinishReady(); return; }
            LastTranscription = text;
            TranscriptionCompleted?.Invoke(this, text);
            if (_previewOnly) { if (_showOverlay) _overlayViewModel.ShowSuccess("Done"); }
            else if (_continuousMode && _autoPaste && !_typingPaused)
            { if (_showOverlay) _overlayViewModel.ShowSuccess("Inserted"); }
            else if (!_continuousMode && _autoPaste && _textInjectionService.HasFocusedEditableControl(_targetHwnd))
            {
                CurrentState = AppState.Pasting;
                if (!await _textInjectionService.InjectTextAsync(text, _targetHwnd, _preserveClipboard))
                    await ShowCopyFallbackAsync(text);
                else if (_showOverlay) _overlayViewModel.ShowSuccess("Inserted");
            }
            else await ShowCopyFallbackAsync(text);
            if (IsCurrent()) CurrentState = AppState.Ready;
        }
        catch (OperationCanceledException) { if (IsCurrent()) FinishReady(); }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            AppLogger.LogError("Recording/transcription failed", ex);
            if (!string.IsNullOrEmpty(continuous?.Transcript))
            {
                LastTranscription = continuous.Transcript;
                TranscriptionCompleted?.Invoke(this, LastTranscription);
            }
            HandleError(ex.Message);
        }
        finally
        {
            continuous?.Dispose();
            if (ReferenceEquals(_continuous, continuous)) _continuous = null;
        }
    }

    private async Task ShowCopyFallbackAsync(string text)
    {
        await _clipboardService.SetTextAsync(text);
        if (_showOverlay) _overlayViewModel.ShowTranscriptionPopup(text);
    }
    public async Task CancelRecordingAsync()
    {
        if (CurrentState != AppState.Recording) return;
        _recordingTimer.Stop();
        _sessionCancel?.Cancel();
        CurrentState = AppState.Transcribing;
        int version = _sessionVersion;
        var continuous = _continuous;
        _pendingAudioStop = _audioRecorder.StopRecordingAsync();
        using var discarded = await _pendingAudioStop;
        continuous?.Dispose();
        if (ReferenceEquals(_continuous, continuous)) _continuous = null;
        if (version == _sessionVersion) FinishReady();
    }
    private void FinishReady() { if (_isDisposed) return; _overlayViewModel.Hide(); CurrentState = AppState.Ready; }
    private void OnAudioLevelChanged(object? sender, float level) => Volatile.Write(ref _latestLevel, level);
    private void OnRecordingTimerTick(object? sender, EventArgs e)
    {
        if (CurrentState != AppState.Recording || _isDisposed) return;
        DurationChanged?.Invoke(this, _audioRecorder.RecordingDuration);
        if (_showOverlay)
        {
            _overlayViewModel.UpdateAudioLevel(Volatile.Read(ref _latestLevel));
            _overlayViewModel.UpdateDuration(_audioRecorder.RecordingDuration);
        }
    }
    private void HandleError(string message)
    {
        if (_isDisposed) return;
        _recordingTimer.Stop();
        _sessionCancel?.Cancel();
        LastErrorMessage = message;
        CurrentState = AppState.Error;
        ErrorOccurred?.Invoke(this, message);
        if (_showOverlay) _overlayViewModel.ShowError(message);
        DispatcherTimer.RunOnce(() => { if (!_isDisposed && CurrentState == AppState.Error) CurrentState = AppState.Ready; }, TimeSpan.FromSeconds(3));
    }
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _sessionCancel?.Cancel();
        _continuous?.Dispose();
        _hotkeyService.EscapePressed -= OnEscapePressed;
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyService.HotkeyReleased -= OnHotkeyReleased;
        _audioRecorder.AudioLevelChanged -= OnAudioLevelChanged;
        _audioRecorder.PhraseReady -= OnPhraseReady;
        _recordingTimer.Stop();
        _recordingTimer.Tick -= OnRecordingTimerTick;
        _sessionCancel?.Dispose();
    }
}
