using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;

namespace VoiceFlow.ViewModels;

public partial class RecordingOverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private AppState _state = AppState.Ready;

    [ObservableProperty]
    private string _statusText = "Listening...";

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private float _audioLevel;

    [ObservableProperty]
    private double _levelBar1;

    [ObservableProperty]
    private double _levelBar2;

    [ObservableProperty]
    private double _levelBar3;

    [ObservableProperty]
    private double _levelBar4;

    [ObservableProperty]
    private double _levelBar5;

    // --- Floating Notch / Transcription Popup Mode (Wispr Flow style) ---
    [ObservableProperty]
    private bool _isPopupMode;

    [ObservableProperty]
    private string _transcriptionText = string.Empty;

    [ObservableProperty]
    private string _wordCountText = string.Empty;

    [ObservableProperty]
    private bool _isCopied;

    [ObservableProperty]
    private string _copyButtonText = "Copy";

    private readonly IClipboardService? _clipboardService;
    private CancellationTokenSource? _autoHideCts;

    public RecordingOverlayViewModel()
    {
    }

    public RecordingOverlayViewModel(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public void ShowRecording()
    {
        _autoHideCts?.Cancel();
        IsPopupMode = false;
        State = AppState.Recording;
        StatusText = "Listening...";
        DurationText = "00:00";
        AudioLevel = 0f;
        UpdateBars(0f);
        IsVisible = true;
    }

    public void UpdateDuration(TimeSpan duration)
    {
        DurationText = $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}";
    }

    public void UpdateAudioLevel(float level)
    {
        AudioLevel = level;
        UpdateBars(level);
    }

    private void UpdateBars(float level)
    {
        // Visual 5-bar equalizer matching Flow HUD specifications
        double baseHeight = 6.0;
        double maxHeight = 22.0;
        double span = maxHeight - baseHeight;

        LevelBar1 = Math.Clamp(baseHeight + span * (level * 1.0), baseHeight, maxHeight);
        LevelBar2 = Math.Clamp(baseHeight + span * (level * 2.0), baseHeight, maxHeight);
        LevelBar3 = Math.Clamp(baseHeight + span * (level * 2.5), baseHeight, maxHeight);
        LevelBar4 = Math.Clamp(baseHeight + span * (level * 1.8), baseHeight, maxHeight);
        LevelBar5 = Math.Clamp(baseHeight + span * (level * 0.9), baseHeight, maxHeight);
    }

    public void ShowTranscribing()
    {
        _autoHideCts?.Cancel();
        State = AppState.Transcribing;
        StatusText = "Transcribing...";
        AudioLevel = 0f;
        UpdateBars(0f);
    }

    public void ShowCleaning()
    {
        _autoHideCts?.Cancel();
        State = AppState.Cleaning;
        StatusText = "Refining text...";
    }

    public void ShowSuccess(string message = "Done")
    {
        _autoHideCts?.Cancel();
        _autoHideCts = new CancellationTokenSource();
        var token = _autoHideCts.Token;

        State = AppState.Success;
        StatusText = message;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(900, token);
                if (!token.IsCancellationRequested)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsVisible = false;
                        State = AppState.Ready;
                    });
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void ShowError(string error)
    {
        _autoHideCts?.Cancel();
        _autoHideCts = new CancellationTokenSource();
        var token = _autoHideCts.Token;

        State = AppState.Error;
        StatusText = error.Length > 35 ? error[..32] + "..." : error;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, token);
                if (!token.IsCancellationRequested)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsVisible = false;
                        State = AppState.Ready;
                    });
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void ShowTranscriptionPopup(string text)
    {
        _autoHideCts?.Cancel();
        _autoHideCts = new CancellationTokenSource();
        var token = _autoHideCts.Token;

        State = AppState.Success;
        TranscriptionText = text;
        int wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        WordCountText = $"{wordCount} {(wordCount == 1 ? "word" : "words")}";
        IsCopied = false;
        CopyButtonText = "Copy";
        IsPopupMode = true;
        IsVisible = true;

        // Keep popup card visible for 15 seconds or until user interacts/dismisses
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(15000, token);
                if (!token.IsCancellationRequested)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (IsPopupMode)
                        {
                            Hide();
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task CopyTranscriptionAsync()
    {
        if (_clipboardService != null && !string.IsNullOrEmpty(TranscriptionText))
        {
            await _clipboardService.SetTextAsync(TranscriptionText);
        }

        IsCopied = true;
        CopyButtonText = "Copied! ✓";

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                CopyButtonText = "Copy";
            });
        });
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void DismissPopup()
    {
        Hide();
    }

    public void Hide()
    {
        _autoHideCts?.Cancel();
        IsVisible = false;
        IsPopupMode = false;
        State = AppState.Ready;
    }
}
