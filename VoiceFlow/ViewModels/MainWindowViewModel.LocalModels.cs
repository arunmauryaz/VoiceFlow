using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceFlow.Models;
using VoiceFlow.Providers.Local;
using VoiceFlow.Services;

namespace VoiceFlow.ViewModels;

public partial class MainWindowViewModel
{
    private readonly LocalModelService _localModels;
    private readonly BarkRuntimeService _barkRuntime;
    private readonly LocalTranscriptionProvider _localProvider;
    private CancellationTokenSource? _localOperation;
    private bool _localInitializationStarted;
    private int _localOperationVersion;
    public IReadOnlyList<LocalModelInfo> AvailableLocalModels => LocalModels.All;
    public IReadOnlyList<string> LocalLanguages { get; } = new[] { "Auto detect", "English", "Hindi" };
    public IReadOnlyList<string> ComputeDevices { get; } = new[] { "Default", "CPU", "GPU" };
    public bool IsLocalProvider => SelectedProvider == "Local models (offline)";
    public bool IsCloudProvider => !IsLocalProvider;
    public bool NeedsCloudApiKey => IsCloudProvider && !HasSavedApiKey;
    public bool IsBarkModel => SelectedLocalModel.IsBark;
    public bool IsWhisperModel => !IsBarkModel;
    public bool IsLocalModelInstalled => _localModels.IsInstalled(SelectedLocalModel);
    public bool ShowModelDownload => !IsLocalModelInstalled && !IsLocalBusy;
    public bool CanChangeTranscriptionSettings => !IsListening && !IsHeroTranscribing;
    public bool CanManageLocalModel => !IsLocalBusy && CanChangeTranscriptionSettings;

    [ObservableProperty] private LocalModelInfo _selectedLocalModel = LocalModels.Get("whisper-base");
    [ObservableProperty] private string _selectedLocalLanguage = "Auto detect";
    [ObservableProperty] private string _selectedComputeDevice = "Default";
    [ObservableProperty] private string _localModelStatus = "Not downloaded";
    [ObservableProperty] private bool _isLocalBusy;
    [ObservableProperty] private double _localDownloadProgress;
    [ObservableProperty] private bool _isLocalProgressIndeterminate;

    partial void OnIsListeningChanged(bool value) => NotifyLocalActions();
    partial void OnIsHeroTranscribingChanged(bool value) => NotifyLocalActions();
    partial void OnIsLocalBusyChanged(bool value) => NotifyLocalActions();
    private void NotifyLocalActions()
    {
        OnPropertyChanged(nameof(IsLocalModelInstalled));
        OnPropertyChanged(nameof(ShowModelDownload));
        OnPropertyChanged(nameof(CanChangeTranscriptionSettings));
        OnPropertyChanged(nameof(CanManageLocalModel));
        OnPropertyChanged(nameof(CanStartDictation));
        OnPropertyChanged(nameof(ShowHomeReady));
        OnPropertyChanged(nameof(SidebarStatus));
        OnPropertyChanged(nameof(SidebarStatusColor));
    }

    public async Task InitializeLocalModelsAsync()
    {
        _localInitializationStarted = true;
        if (IsLocalProvider && _settingsService.Settings.AutoPrepareLocalModel)
            await PrepareSelectedLocalModelAsync(false);
    }

    partial void OnSelectedProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsLocalProvider));
        OnPropertyChanged(nameof(IsCloudProvider));
        OnPropertyChanged(nameof(NeedsCloudApiKey));
        if (_settingsService == null) return;
        _settingsService.Settings.ApiProvider = IsLocalProvider ? "Local" : "Gemini";
        CurrentModelText = IsLocalProvider ? SelectedLocalModel.Name : SelectedModel;
        AutoPersistSettings();
        RefreshLocalStatus();
        if (_localInitializationStarted)
        {
            if (IsLocalProvider) _ = PrepareSelectedLocalModelAsync(false);
            else { _localOperation?.Cancel(); }
        }
    }

    partial void OnSelectedLocalModelChanged(LocalModelInfo value)
    {
        if (value == null) return;
        OnPropertyChanged(nameof(IsBarkModel));
        OnPropertyChanged(nameof(IsWhisperModel));
        if (_settingsService == null) return;
        _settingsService.Settings.LocalModelId = value.Id;
        if (IsLocalProvider) CurrentModelText = value.Name;
        if (_localInitializationStarted) _settingsService.Settings.AutoPrepareLocalModel = true;
        AutoPersistSettings();
        RefreshLocalStatus();
        if (_localInitializationStarted && IsLocalProvider) _ = PrepareSelectedLocalModelAsync(false);
    }

    partial void OnSelectedComputeDeviceChanged(string value)
    {
        if (_settingsService == null) return;
        _settingsService.Settings.LocalComputeDevice = value;
        AutoPersistSettings();
        if (_localInitializationStarted && IsLocalProvider && _settingsService.Settings.AutoPrepareLocalModel)
            _ = PrepareSelectedLocalModelAsync(false);
    }

    partial void OnSelectedLocalLanguageChanged(string value)
    {
        if (_settingsService == null) return;
        _settingsService.Settings.LocalLanguage = value == "English" ? "en" : value == "Hindi" ? "hi" : "auto";
        AutoPersistSettings();
    }
    partial void OnHasSavedApiKeyChanged(bool value) => OnPropertyChanged(nameof(NeedsCloudApiKey));

    private void RefreshLocalStatus()
    {
        if (_localModels == null || IsLocalBusy) return;
        LocalModelStatus = IsLocalModelInstalled ? "Preparing..." : "Not downloaded";
        NotifyLocalActions();
    }

    private void OnLocalPreparationChanged(object? sender, LocalPreparationStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsLocalProvider || status.ModelId != SelectedLocalModel.Id) return;
            LocalModelStatus = status.Text;
            OnPropertyChanged(nameof(HomeEngineStatus));
            IsLocalBusy = status.Busy;
            IsLocalProgressIndeterminate = !status.Progress.HasValue;
            LocalDownloadProgress = status.Progress ?? 0;
            if (IsHeroTranscribing) HomeSubtitle = status.Text;
            NotifyLocalActions();
        });
    }

    private async Task PrepareSelectedLocalModelAsync(bool repair)
    {
        _localOperation?.Cancel();
        int version = ++_localOperationVersion;
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(45));
        _localOperation = operation;
        IsLocalBusy = true;
        IsLocalProgressIndeterminate = true;
        LocalModelStatus = "Preparing...";
        try
        {
            await _localProvider.PrepareAsync(SelectedLocalModel, SelectedComputeDevice, repair, operation.Token);
        }
        catch (OperationCanceledException) { if (version == _localOperationVersion) LocalModelStatus = "Setup paused"; }
        catch (Exception ex) { if (version == _localOperationVersion) LocalModelStatus = ex.Message; }
        finally
        {
            if (version == _localOperationVersion)
            {
                IsLocalBusy = false;
                _localOperation = null;
                NotifyLocalActions();
            }
        }
    }

    [RelayCommand]
    private async Task DownloadLocalModelAsync()
    {
        _settingsService.Settings.AutoPrepareLocalModel = true;
        AutoPersistSettings();
        await PrepareSelectedLocalModelAsync(false);
    }
    [RelayCommand]
    private Task RepairLocalModelAsync()
    {
        _settingsService.Settings.AutoPrepareLocalModel = true;
        AutoPersistSettings();
        return PrepareSelectedLocalModelAsync(true);
    }

    [RelayCommand]
    private async Task DeleteLocalModelAsync()
    {
        if (!CanManageLocalModel) return;
        _localOperation?.Cancel();
        _settingsService.Settings.AutoPrepareLocalModel = false;
        AutoPersistSettings();
        IsLocalBusy = true;
        try { await _localProvider.DeleteModelAsync(SelectedLocalModel, CancellationToken.None); LocalModelStatus = "Not downloaded"; }
        catch (Exception ex) { LocalModelStatus = ex.Message; }
        finally { IsLocalBusy = false; NotifyLocalActions(); }
    }
    [RelayCommand] private void CancelLocalOperation() => _localOperation?.Cancel();
    public void CancelLocalSetup() => _localOperation?.Cancel();
}
