using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceFlow.ViewModels;

public partial class MainWindowViewModel
{
    public string AppVersion => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
    public string AppVersionLabel => "VoiceFlow for Windows v" + AppVersion;
    public string AboutVersionLabel => "Version " + AppVersion + " · Windows · MIT license";
    [ObservableProperty] private bool _isContinuousTyping;
    [ObservableProperty] private bool _keepModelLoaded;
    public bool InsertWhenFinished
    {
        get => !IsContinuousTyping;
        set { if (value) IsContinuousTyping = false; }
    }
    public string SidebarStatus => IsLocalBusy && !IsListening && !IsHeroTranscribing ? "Preparing model..." : StatusDisplay;
    public string SidebarStatusColor => IsLocalBusy && !IsListening && !IsHeroTranscribing ? "#8D96A5" : StatusColor;
    partial void OnStatusDisplayChanged(string value) => OnPropertyChanged(nameof(SidebarStatus));
    partial void OnStatusColorChanged(string value) => OnPropertyChanged(nameof(SidebarStatusColor));
    public string OutputModeCaption => IsContinuousTyping ? "Continuous typing" : "Insert when finished";
    public bool ShowHomeReady => !IsListening && !IsHeroTranscribing;
    public bool CanStartDictation => CanChangeTranscriptionSettings && !IsLocalBusy;
    public string HomeEngineStatus => IsLocalProvider ? LocalModelStatus : "Cloud transcription";
    partial void OnIsContinuousTypingChanged(bool value)
    {
        OnPropertyChanged(nameof(InsertWhenFinished));
        OnPropertyChanged(nameof(OutputModeCaption));
        AutoPersistSettings();
    }
    partial void OnKeepModelLoadedChanged(bool value) => AutoPersistSettings();
    private void OnPartialTranscription(object? sender, string text)
    {
        LastTranscriptionText = text;
        HasLastTranscription = true;
        LastTranscriptionTime = "Live";
    }
    private void OnDurationChanged(object? sender, TimeSpan duration)
        => HomeTimerText = $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}";
    private void OnDictationError(object? sender, string message)
        => BuildDetailedError("Transcription Failed", message, null);
    public void Dispose()
    {
        CancelLocalSetup();
        _appStateManager.StateChanged -= OnAppStateChanged;
        _appStateManager.TranscriptionCompleted -= OnTranscriptionCompleted;
        _appStateManager.PartialTranscription -= OnPartialTranscription;
        _appStateManager.DurationChanged -= OnDurationChanged;
        _appStateManager.ErrorOccurred -= OnDictationError;
        _audioRecorder.AudioLevelChanged -= OnAudioLevelChanged;
        _localProvider.PreparationChanged -= OnLocalPreparationChanged;
        _localProvider.Dispose();
        _hotkeyService.ShortcutRecorded -= OnShortcutRecorded;
        _hotkeyService.ShortcutRecordingPreview -= OnShortcutRecordingPreview;
        _hotkeyService.ShortcutRecordingCanceled -= OnShortcutRecordingCanceled;
        _hotkeyService.SingleKeyCaptured -= OnSingleKeyCaptured;
    }
}
