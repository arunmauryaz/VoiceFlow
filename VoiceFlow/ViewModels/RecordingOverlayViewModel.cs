using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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

    private CancellationTokenSource? _autoHideCts;

    public void ShowRecording()
    {
        _autoHideCts?.Cancel();
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
        // Visual equalizer bar heights based on microphone level
        double baseHeight = 6.0;
        double maxHeight = 24.0;
        double span = maxHeight - baseHeight;

        LevelBar1 = Math.Clamp(baseHeight + span * (level * 1.3), baseHeight, maxHeight);
        LevelBar2 = Math.Clamp(baseHeight + span * (level * 2.0), baseHeight, maxHeight);
        LevelBar3 = Math.Clamp(baseHeight + span * (level * 1.6), baseHeight, maxHeight);
        LevelBar4 = Math.Clamp(baseHeight + span * (level * 0.9), baseHeight, maxHeight);
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

    public void Hide()
    {
        _autoHideCts?.Cancel();
        IsVisible = false;
        State = AppState.Ready;
    }
}
