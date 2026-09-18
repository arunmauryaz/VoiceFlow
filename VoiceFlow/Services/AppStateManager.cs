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

    private AppState _currentState = AppState.Ready;
    private IntPtr _targetHwnd = IntPtr.Zero;
    private DispatcherTimer? _recordingTimer;
    private bool _isDisposed;

    public AppState CurrentState
    {
        get => _currentState;
        private set
        {
            _currentState = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public string? LastTranscription { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public event EventHandler<AppState>? StateChanged;
    public event EventHandler<string>? TranscriptionCompleted;
    public event EventHandler<string>? ErrorOccurred;

    public AppStateManager(
        ISettingsService settingsService,
        IAudioRecorder audioRecorder,
        ITranscriptionProvider transcriptionProvider,
        IAITextProcessor aiTextProcessor,
        IGlobalHotkeyService hotkeyService,
        ITextInjectionService textInjectionService,
        IClipboardService clipboardService,
        RecordingOverlayViewModel overlayViewModel)
    {
        _settingsService = settingsService;
        _audioRecorder = audioRecorder;
        _transcriptionProvider = transcriptionProvider;
        _aiTextProcessor = aiTextProcessor;
        _hotkeyService = hotkeyService;
        _textInjectionService = textInjectionService;
        _clipboardService = clipboardService;
        _overlayViewModel = overlayViewModel;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _hotkeyService.HotkeyReleased += OnHotkeyReleased;
        _audioRecorder.AudioLevelChanged += OnAudioLevelChanged;

        _recordingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _recordingTimer.Tick += OnRecordingTimerTick;
    }

    public void Initialize()
    {
        var settings = _settingsService.Settings;
        _hotkeyService.RegisterHotkey(settings.Hotkey, settings.HotkeyMode);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentState != AppState.Ready) return;

            try
            {
                _targetHwnd = _textInjectionService.GetForegroundWindowHandle();
                int deviceIndex = _settingsService.Settings.SelectedAudioDeviceIndex;

                _audioRecorder.StartRecording(deviceIndex);
                CurrentState = AppState.Recording;

                if (_settingsService.Settings.ShowOverlay)
                {
                    _overlayViewModel.ShowRecording();
                }

                _recordingTimer?.Start();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to begin voice recording on hotkey press.", ex);
                HandleError($"Microphone error: {ex.Message}");
            }
        });
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (CurrentState != AppState.Recording) return;

            _recordingTimer?.Stop();

            if (_settingsService.Settings.ShowOverlay)
            {
                _overlayViewModel.ShowTranscribing();
            }

            CurrentState = AppState.Transcribing;

            try
            {
                using var audioStream = await _audioRecorder.StopRecordingAsync();

                if (audioStream.Length < 1000)
                {
                    _overlayViewModel.Hide();
                    CurrentState = AppState.Ready;
                    return;
                }

                var result = await _transcriptionProvider.TranscribeAsync(audioStream, "audio/wav");

                if (!result.Success)
                {
                    HandleError(result.ErrorMessage ?? "Transcription failed.");
                    return;
                }

                string text = result.Text;

                if (_settingsService.Settings.CleanupEnabled &&
                    _settingsService.Settings.CleanupMode != TextCleanupMode.Off)
                {
                    CurrentState = AppState.Cleaning;
                    if (_settingsService.Settings.ShowOverlay)
                    {
                        _overlayViewModel.ShowCleaning();
                    }

                    text = await _aiTextProcessor.ProcessTextAsync(text, _settingsService.Settings.CleanupMode);
                }

                LastTranscription = text;
                TranscriptionCompleted?.Invoke(this, text);

                if (_settingsService.Settings.AutoPaste)
                {
                    CurrentState = AppState.Pasting;
                    await _textInjectionService.InjectTextAsync(
                        text,
                        _targetHwnd,
                        _settingsService.Settings.PreserveClipboard);
                }
                else
                {
                    await _clipboardService.SetTextAsync(text);
                }

                CurrentState = AppState.Success;
                if (_settingsService.Settings.ShowOverlay)
                {
                    _overlayViewModel.ShowSuccess("Done");
                }

                await Task.Delay(800);
                CurrentState = AppState.Ready;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error in recording/transcription pipeline.", ex);
                HandleError($"Error: {ex.Message}");
            }
        });
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        if (CurrentState == AppState.Recording && _settingsService.Settings.ShowOverlay)
        {
            Dispatcher.UIThread.Post(() => _overlayViewModel.UpdateAudioLevel(level));
        }
    }

    private void OnRecordingTimerTick(object? sender, EventArgs e)
    {
        if (CurrentState == AppState.Recording && _settingsService.Settings.ShowOverlay)
        {
            _overlayViewModel.UpdateDuration(_audioRecorder.RecordingDuration);
        }
    }

    private void HandleError(string message)
    {
        LastErrorMessage = message;
        CurrentState = AppState.Error;
        ErrorOccurred?.Invoke(this, message);

        if (_settingsService.Settings.ShowOverlay)
        {
            _overlayViewModel.ShowError(message);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            Dispatcher.UIThread.Post(() =>
            {
                if (CurrentState == AppState.Error)
                {
                    CurrentState = AppState.Ready;
                }
            });
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _hotkeyService.HotkeyReleased -= OnHotkeyReleased;
        _audioRecorder.AudioLevelChanged -= OnAudioLevelChanged;

        if (_recordingTimer != null)
        {
            _recordingTimer.Stop();
            _recordingTimer.Tick -= OnRecordingTimerTick;
            _recordingTimer = null;
        }
    }
}
