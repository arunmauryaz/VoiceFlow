using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;

namespace VoiceFlow.ViewModels;

public partial class RecordingOverlayViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private AppState _state = AppState.Ready;
    [ObservableProperty] private string _statusText = "Listening...";
    [ObservableProperty] private string _durationText = "00:00";
    [ObservableProperty] private float _audioLevel;
    [ObservableProperty] private double _levelBar1;
    [ObservableProperty] private double _levelBar2;
    [ObservableProperty] private double _levelBar3;
    [ObservableProperty] private double _levelBar4;
    [ObservableProperty] private double _levelBar5;
    [ObservableProperty] private bool _isPopupMode;
    [ObservableProperty] private string _transcriptionText = string.Empty;
    [ObservableProperty] private string _wordCountText = string.Empty;
    [ObservableProperty] private bool _isCopied;
    [ObservableProperty] private string _copyButtonText = "Copy";
    public bool HideImmediately { get; private set; }
    private readonly IClipboardService? _clipboardService;
    private System.Threading.CancellationTokenSource? _hideCancel;
    public RecordingOverlayViewModel() { }
    public RecordingOverlayViewModel(IClipboardService clipboardService) => _clipboardService = clipboardService;
    private void CancelHide() { _hideCancel?.Cancel(); _hideCancel?.Dispose(); _hideCancel = null; }
    private void HideAfter(TimeSpan delay)
    {
        CancelHide();
        _hideCancel = new System.Threading.CancellationTokenSource();
        _ = HideLaterAsync(delay, _hideCancel.Token);
    }
    private async System.Threading.Tasks.Task HideLaterAsync(TimeSpan delay, System.Threading.CancellationToken token)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(delay, token);
            Dispatcher.UIThread.Post(() => { if (!token.IsCancellationRequested) Hide(); });
        }
        catch (OperationCanceledException) { }
    }
    public void ShowRecording()
    {
        CancelHide(); IsPopupMode = false; State = AppState.Recording;
        StatusText = "Listening..."; DurationText = "00:00"; AudioLevel = 0;
        UpdateBars(0); IsVisible = true;
    }
    public void UpdateDuration(TimeSpan duration) => DurationText = $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}";
    public void UpdateAudioLevel(float level)
    {
        float target = Math.Clamp(level, 0, 1);
        AudioLevel += (target - AudioLevel) * (target > AudioLevel ? 0.7f : 0.25f);
        UpdateBars(AudioLevel);
    }
    private void UpdateBars(float level)
    {
        double energy = Math.Clamp(Math.Sqrt(Math.Max(0, level)) * 2.8, 0, 1);
        LevelBar1 = 4 + 12 * energy;
        LevelBar2 = 5 + 17 * energy;
        LevelBar3 = 6 + 20 * energy;
        LevelBar4 = 5 + 15 * energy;
        LevelBar5 = 4 + 10 * energy;
    }
    public void ShowTranscribing() { CancelHide(); State = AppState.Transcribing; StatusText = "Transcribing..."; UpdateBars(0); }
    public void ShowCleaning() { CancelHide(); State = AppState.Cleaning; StatusText = "Refining text..."; }
    public void ShowSuccess(string message = "Done") { IsPopupMode = false; State = AppState.Success; StatusText = message; IsVisible = true; HideAfter(TimeSpan.FromMilliseconds(900)); }
    public void ShowError(string error)
    {
        IsPopupMode = false; State = AppState.Error; StatusText = error.Length > 45 ? error[..42] + "..." : error;
        IsVisible = true; HideAfter(TimeSpan.FromSeconds(3));
    }
    public void ShowNoMicrophone() => ShowError("No microphone connected");
    public void ShowTranscriptionPopup(string text)
    {
        State = AppState.Success; TranscriptionText = text;
        int count = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        WordCountText = $"{count} {(count == 1 ? "word" : "words")}";
        IsCopied = false; CopyButtonText = "Copy"; IsPopupMode = true; IsVisible = true;
        HideAfter(TimeSpan.FromSeconds(15));
    }
    [RelayCommand]
    public async System.Threading.Tasks.Task CopyTranscriptionAsync()
    {
        if (_clipboardService != null && TranscriptionText.Length > 0) await _clipboardService.SetTextAsync(TranscriptionText);
        IsCopied = true; CopyButtonText = "Copied";
    }
    [RelayCommand] public void DismissPopup() => HideCore(immediately: true);
    public void Hide() => HideCore(immediately: false);
    private void HideCore(bool immediately)
    {
        HideImmediately = immediately;
        CancelHide(); IsVisible = false; IsPopupMode = false; State = AppState.Ready;
    }
    public void Dispose() => CancelHide();
}
