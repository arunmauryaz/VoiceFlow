using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceFlow.Helpers;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Native;
using VoiceFlow.Services;

namespace VoiceFlow.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioRecorder _audioRecorder;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly IAITextProcessor _aiTextProcessor;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IClipboardService _clipboardService;
    private readonly IStartupService _startupService;
    private readonly AppStateManager _appStateManager;
    private DispatcherTimer? _testSpeechTimer;

    // --- Navigation ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutSelected))]
    private AppSection _currentSection = AppSection.Home;

    public bool IsHomeSelected => CurrentSection == AppSection.Home;
    public bool IsHistorySelected => CurrentSection == AppSection.History;
    public bool IsSettingsSelected => CurrentSection == AppSection.Settings;
    public bool IsAboutSelected => CurrentSection == AppSection.About;

    // --- Status Dashboard ---
    [ObservableProperty]
    private string _statusDisplay = "● Ready";

    [ObservableProperty]
    private string _statusColor = "#10B981"; // Green

    [ObservableProperty]
    private string _currentShortcutText = "Ctrl + Space";

    [ObservableProperty]
    private string _shortcutModifierKeyText = "Ctrl";

    [ObservableProperty]
    private string _shortcutMainKeyText = "Space";

    [ObservableProperty]
    private string _shortcutModeCaption = "(Hold to talk)";

    [ObservableProperty]
    private string _currentMicrophoneText = "Default Microphone";

    [ObservableProperty]
    private string _currentModelText = "gemini-3.5-transcribe";

    // --- Home View Properties ---
    [ObservableProperty]
    private string _homeHeading = "Ready to dictate";

    [ObservableProperty]
    private string _homeSubtitle = "VoiceFlow is active and listening for your hotkey anywhere on Windows.";

    [ObservableProperty]
    private string _dictationInstructionTitle = "Hold Ctrl + Space to dictate";

    [ObservableProperty]
    private string _dictationInstructionSubtitle = "Release shortcut when you are finished speaking";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private bool _isHeroTranscribing;

    [ObservableProperty]
    private string _homeTimerText = "00:00";

    [ObservableProperty]
    private double _homeWaveBar1 = 8.0;

    [ObservableProperty]
    private double _homeWaveBar2 = 18.0;

    [ObservableProperty]
    private double _homeWaveBar3 = 28.0;

    [ObservableProperty]
    private double _homeWaveBar4 = 18.0;

    [ObservableProperty]
    private double _homeWaveBar5 = 8.0;

    [ObservableProperty]
    private string _lastTranscriptionText = "Ready to dictate anywhere. Hold your shortcut key to begin.";

    [ObservableProperty]
    private string _lastTranscriptionTime = "Ready";

    [ObservableProperty]
    private bool _hasLastTranscription = true;

    // --- Interactive Voice Testing Playground ---
    [ObservableProperty]
    private bool _isRecordingTestSpeech;

    [ObservableProperty]
    private bool _isTranscribingTestSpeech;

    [ObservableProperty]
    private string _testSpeechDuration = "00:00";

    [ObservableProperty]
    private float _testAudioLevel;

    [ObservableProperty]
    private string _testTranscriptionResult = string.Empty;

    [ObservableProperty]
    private string _testSpeechStatus = "Click 'Start Speaking' or 'Test Microphone' to test live Gemini transcription.";

    [ObservableProperty]
    private string _testSpeechLatencyInfo = string.Empty;

    [ObservableProperty]
    private bool _hasTestResult;

    // --- Debug & Diagnostics View ---
    [ObservableProperty]
    private bool _isDebugViewVisible;

    [ObservableProperty]
    private bool _hasDebugError;

    [ObservableProperty]
    private string _detailedErrorSummary = string.Empty;

    // --- API Setup ---
    [ObservableProperty]
    private string _apiKeyInput = string.Empty;

    [ObservableProperty]
    private bool _hasSavedApiKey;

    [ObservableProperty]
    private bool _isApiKeyRevealed;

    [ObservableProperty]
    private char _apiKeyPasswordChar = '•';

    [ObservableProperty]
    private string _revealButtonIcon = "👁️";

    [ObservableProperty]
    private string _testConnectionButtonText = "Test Connection";

    [ObservableProperty]
    private string _savedKeyPreviewText = string.Empty;

    [ObservableProperty]
    private string _selectedModel = "gemini-3.5-transcribe";

    partial void OnSelectedModelChanged(string value)
    {
        CurrentModelText = value;
        if (_settingsService?.Settings != null && !string.IsNullOrWhiteSpace(value))
        {
            _settingsService.Settings.GeminiModel = value;
            AutoPersistSettings();
        }
    }

    [ObservableProperty]
    private string _connectionStatusMessage = string.Empty;

    [ObservableProperty]
    private string _connectionStatusColor = "#A1A1AA";

    [ObservableProperty]
    private bool _isTestingConnection;

    public ObservableCollection<string> AvailableModels { get; } = new()
    {
        "gemini-3.5-transcribe",
        "gemini-3.8-flash",
        "gemini-3.7-flash",
        "gemini-3.5-flash"
    };

    public ObservableCollection<string> AvailableProviders { get; } = new()
    {
        "Google Gemini (Recommended)",
        "Local Whisper (Coming soon)"
    };

    [ObservableProperty]
    private string _selectedProvider = "Google Gemini (Recommended)";

    // --- Microphone ---
    public ObservableCollection<AudioDeviceInfo> AudioDevices { get; } = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioDevice;

    partial void OnSelectedAudioDeviceChanged(AudioDeviceInfo? value)
    {
        if (value != null)
        {
            CurrentMicrophoneText = value.Name;
            AutoPersistSettings();
        }
    }

    [ObservableProperty]
    private bool _isTestingMic;

    [ObservableProperty]
    private float _micTestLevel;

    // --- Shortcut ---
    [ObservableProperty]
    private HotkeyActivationMode _selectedHotkeyMode = HotkeyActivationMode.HoldToTalk;

    partial void OnSelectedHotkeyModeChanged(HotkeyActivationMode value)
    {
        OnPropertyChanged(nameof(IsHoldToTalk));
        OnPropertyChanged(nameof(IsToggleToTalk));
        UpdateShortcutKeyDisplay(_settingsService?.Settings?.Hotkey ?? HotkeyConfig.Default);
        AutoPersistSettings(updateHotkey: true);
    }

    public bool IsHoldToTalk
    {
        get => SelectedHotkeyMode == HotkeyActivationMode.HoldToTalk;
        set
        {
            if (value && SelectedHotkeyMode != HotkeyActivationMode.HoldToTalk)
            {
                SelectedHotkeyMode = HotkeyActivationMode.HoldToTalk;
            }
        }
    }

    public bool IsToggleToTalk
    {
        get => SelectedHotkeyMode == HotkeyActivationMode.Toggle;
        set
        {
            if (value && SelectedHotkeyMode != HotkeyActivationMode.Toggle)
            {
                SelectedHotkeyMode = HotkeyActivationMode.Toggle;
            }
        }
    }

    [ObservableProperty]
    private string _hotkeyRegistrationError = string.Empty;

    // --- 3-Box Shortcut Slot Builder ---
    // Each slot records one key independently. The 3 slots are combined into a HotkeyConfig.
    // Slot 1 = leftmost, Slot 3 = rightmost. Empty slots are skipped.
    [ObservableProperty]
    private string _slot1Text = string.Empty;
    partial void OnSlot1TextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSlot1Key));
        OnPropertyChanged(nameof(HasAnySlotKey));
    }

    [ObservableProperty]
    private string _slot2Text = string.Empty;
    partial void OnSlot2TextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSlot2Key));
        OnPropertyChanged(nameof(HasAnySlotKey));
    }

    [ObservableProperty]
    private string _slot3Text = string.Empty;
    partial void OnSlot3TextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSlot3Key));
        OnPropertyChanged(nameof(HasAnySlotKey));
    }

    [ObservableProperty]
    private bool _isShortcutSavedAndActive = true;

    // -1 = none recording, 0/1/2 = slot index being recorded
    [ObservableProperty]
    private int _activeRecordingSlot = -1;

    partial void OnActiveRecordingSlotChanged(int value)
    {
        OnPropertyChanged(nameof(IsRecordingSlot1));
        OnPropertyChanged(nameof(IsRecordingSlot2));
        OnPropertyChanged(nameof(IsRecordingSlot3));
        OnPropertyChanged(nameof(IsRecordingAnySlot));
    }

    private uint _slot1Vk;
    private uint _slot2Vk;
    private uint _slot3Vk;

    public bool IsRecordingSlot1 => ActiveRecordingSlot == 0;
    public bool IsRecordingSlot2 => ActiveRecordingSlot == 1;
    public bool IsRecordingSlot3 => ActiveRecordingSlot == 2;
    public bool IsRecordingAnySlot => ActiveRecordingSlot >= 0;

    public bool HasSlot1Key => !string.IsNullOrEmpty(Slot1Text);
    public bool HasSlot2Key => !string.IsNullOrEmpty(Slot2Text);
    public bool HasSlot3Key => !string.IsNullOrEmpty(Slot3Text);
    public bool HasAnySlotKey => !string.IsNullOrEmpty(Slot1Text) || !string.IsNullOrEmpty(Slot2Text) || !string.IsNullOrEmpty(Slot3Text);

    // Keep IsRecordingShortcut for backwards-compat on HomeView bindings (always false now)
    [ObservableProperty]
    private bool _isRecordingShortcut;

    [ObservableProperty]
    private string _shortcutStatusFeedback = string.Empty;

    [ObservableProperty]
    private string _shortcutStatusColor = "#10B981";

    public ObservableCollection<string> PresetFunctionKeys { get; } = new()
    {
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
    };

    public ObservableCollection<string> PresetCombos { get; } = new()
    {
        "Ctrl + Space",
        "Alt + Space",
        "Win + Space",
        "Ctrl + Shift + Space",
        "Ctrl + Alt + Space",
        "Alt + Shift + Space",
        "Ctrl + F8",
        "Ctrl + F9",
        "Win + Alt + V"
    };

    public ObservableCollection<string> PresetShortcuts { get; } = new()
    {
        "Ctrl + Space",
        "Alt + Space",
        "Win + Space",
        "Ctrl + Shift + Space",
        "Ctrl + Alt + Space",
        "Alt + Shift + Space",
        "Ctrl + F8",
        "Ctrl + F9",
        "Win + Alt + V",
        "F1",
        "F2",
        "F3",
        "F4",
        "F5",
        "F6",
        "F7",
        "F8",
        "F9",
        "F10",
        "F11",
        "F12"
    };

    // --- AI Cleanup ---
    [ObservableProperty]
    private bool _cleanupEnabled;

    partial void OnCleanupEnabledChanged(bool value) => AutoPersistSettings();

    [ObservableProperty]
    private TextCleanupMode _cleanupMode = TextCleanupMode.CleanTranscription;

    partial void OnCleanupModeChanged(TextCleanupMode value) => AutoPersistSettings();

    public ObservableCollection<string> AvailableCleanupModes { get; } = new()
    {
        "Clean transcription (Light)",
        "Smart formatting"
    };

    [ObservableProperty]
    private string _selectedCleanupModeString = "Clean transcription (Light)";

    partial void OnSelectedCleanupModeStringChanged(string value)
    {
        CleanupMode = value == "Smart formatting" ? TextCleanupMode.SmartFormatting : TextCleanupMode.CleanTranscription;
        AutoPersistSettings();
    }

    // --- Behavior & System ---
    [ObservableProperty]
    private bool _autoPaste = true;

    partial void OnAutoPasteChanged(bool value) => AutoPersistSettings();

    [ObservableProperty]
    private bool _preserveClipboard = true;

    partial void OnPreserveClipboardChanged(bool value) => AutoPersistSettings();

    [ObservableProperty]
    private bool _showOverlay = true;

    partial void OnShowOverlayChanged(bool value) => AutoPersistSettings();

    [ObservableProperty]
    private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value)
    {
        _startupService.SetStartupEnabled(value);
        AutoPersistSettings();
    }

    [ObservableProperty]
    private bool _runInBackground = true;

    partial void OnRunInBackgroundChanged(bool value) => AutoPersistSettings();

    [ObservableProperty]
    private ThemePreference _selectedTheme = ThemePreference.System;

    partial void OnSelectedThemeChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        ApplyTheme(value);
        AutoPersistSettings();
    }

    public bool IsSystemTheme
    {
        get => SelectedTheme == ThemePreference.System;
        set
        {
            if (value && SelectedTheme != ThemePreference.System)
            {
                SelectedTheme = ThemePreference.System;
            }
        }
    }

    public bool IsLightTheme
    {
        get => SelectedTheme == ThemePreference.Light;
        set
        {
            if (value && SelectedTheme != ThemePreference.Light)
            {
                SelectedTheme = ThemePreference.Light;
            }
        }
    }

    public bool IsDarkTheme
    {
        get => SelectedTheme == ThemePreference.Dark;
        set
        {
            if (value && SelectedTheme != ThemePreference.Dark)
            {
                SelectedTheme = ThemePreference.Dark;
            }
        }
    }

    // --- History ---
    [ObservableProperty]
    private bool _enableHistory = true;

    partial void OnEnableHistoryChanged(bool value) => AutoPersistSettings();

    public ObservableCollection<HistoryItem> RecentHistory { get; } = new();

    public bool HasHistory => RecentHistory.Count > 0;

    public MainWindowViewModel(
        ISettingsService settingsService,
        IAudioRecorder audioRecorder,
        ITranscriptionProvider transcriptionProvider,
        IAITextProcessor aiTextProcessor,
        IGlobalHotkeyService hotkeyService,
        IClipboardService clipboardService,
        IStartupService startupService,
        AppStateManager appStateManager)
    {
        _settingsService = settingsService;
        _audioRecorder = audioRecorder;
        _transcriptionProvider = transcriptionProvider;
        _aiTextProcessor = aiTextProcessor;
        _hotkeyService = hotkeyService;
        _clipboardService = clipboardService;
        _startupService = startupService;
        _appStateManager = appStateManager;

        _appStateManager.StateChanged += OnAppStateChanged;
        _appStateManager.TranscriptionCompleted += OnTranscriptionCompleted;
        _audioRecorder.AudioLevelChanged += OnAudioLevelChanged;

        _hotkeyService.ShortcutRecorded += OnShortcutRecorded;
        _hotkeyService.ShortcutRecordingPreview += OnShortcutRecordingPreview;
        _hotkeyService.ShortcutRecordingCanceled += OnShortcutRecordingCanceled;
        _hotkeyService.SingleKeyCaptured += OnSingleKeyCaptured;

        LoadState();
    }

    private void LoadState()
    {
        var s = _settingsService.Settings;

        string? savedKey = _settingsService.LoadApiKey();
        HasSavedApiKey = !string.IsNullOrEmpty(savedKey);
        ApiKeyInput = savedKey ?? string.Empty;
        if (HasSavedApiKey && savedKey!.Length > 8)
        {
            SavedKeyPreviewText = $"{savedKey[..4]}...{savedKey[^4..]}";
            ConnectionStatusMessage = $"✓ Connected ({SavedKeyPreviewText})";
            ConnectionStatusColor = "#10B981";
        }
        else if (HasSavedApiKey)
        {
            SavedKeyPreviewText = "Configured";
            ConnectionStatusMessage = "✓ Connected";
            ConnectionStatusColor = "#10B981";
        }
        else
        {
            // First-run clean install: prompt user to enter their API key
            CurrentSection = AppSection.Settings;
            ConnectionStatusMessage = "Welcome to VoiceFlow! Please enter your Google Gemini API key below to activate voice typing.";
            ConnectionStatusColor = "#F59E0B";
        }

        string model = !string.IsNullOrWhiteSpace(s.GeminiModel) ? s.GeminiModel : "gemini-3.5-transcribe";
        if (!AvailableModels.Contains(model))
        {
            AvailableModels.Insert(0, model);
        }
        SelectedModel = model;
        CurrentModelText = model;

        SelectedHotkeyMode = s.HotkeyMode;
        CurrentShortcutText = KeyFormattingHelper.FormatHotkey(s.Hotkey);
        if (!PresetShortcuts.Contains(CurrentShortcutText))
        {
            PresetShortcuts.Insert(0, CurrentShortcutText);
        }
        UpdateShortcutKeyDisplay(s.Hotkey);

        CleanupEnabled = s.CleanupEnabled;
        CleanupMode = s.CleanupMode;
        SelectedCleanupModeString = s.CleanupMode == TextCleanupMode.SmartFormatting ? "Smart formatting" : "Clean transcription (Light)";

        AutoPaste = s.AutoPaste;
        PreserveClipboard = s.PreserveClipboard;
        ShowOverlay = s.ShowOverlay;

        RunInBackground = s.RunInBackground;
        StartWithWindows = _startupService.IsStartupEnabled();
        SelectedTheme = s.Theme;
        EnableHistory = s.EnableHistory;

        RefreshAudioDevices();
    }

    public void RefreshAudioDevices()
    {
        AudioDevices.Clear();
        var devices = _audioRecorder.GetInputDevices();
        foreach (var dev in devices)
        {
            AudioDevices.Add(dev);
        }

        if (!_audioRecorder.HasInputDevices)
        {
            SelectedAudioDevice = AudioDevices.FirstOrDefault();
            CurrentMicrophoneText = "No microphone connected";
            return;
        }

        var match = AudioDevices.FirstOrDefault(d => d.DeviceNumber == _settingsService.Settings.SelectedAudioDeviceIndex)
                    ?? AudioDevices.FirstOrDefault();

        SelectedAudioDevice = match;
        CurrentMicrophoneText = match?.Name ?? "Default Microphone";
    }

    private void UpdateShortcutKeyDisplay(HotkeyConfig config)
    {
        string formatted = KeyFormattingHelper.FormatHotkey(config);
        var parts = formatted.Split(" + ");
        if (parts.Length > 1)
        {
            ShortcutModifierKeyText = string.Join(" + ", parts.Take(parts.Length - 1));
            ShortcutMainKeyText = parts.Last();
        }
        else
        {
            ShortcutModifierKeyText = string.Empty;
            ShortcutMainKeyText = parts[0];
        }
        ShortcutModeCaption = SelectedHotkeyMode == HotkeyActivationMode.HoldToTalk ? "(Hold to talk)" : "(Toggle)";
        string action = SelectedHotkeyMode == HotkeyActivationMode.HoldToTalk ? "Hold" : "Press";
        DictationInstructionTitle = $"{action} {formatted} to dictate";
        DictationInstructionSubtitle = SelectedHotkeyMode == HotkeyActivationMode.HoldToTalk
            ? "Release shortcut when you are finished speaking"
            : "Press again when you are finished speaking";

        // Sync 3-box slot display from config
        SyncSlotsFromConfig(config);
    }

    /// <summary>
    /// Populates the 3 slot text boxes to reflect the currently active HotkeyConfig.
    /// Modifiers go in slots 1–(n-1), the main VK goes in the last slot.
    /// </summary>
    private void SyncSlotsFromConfig(HotkeyConfig config)
    {
        // Build the ordered list of keys just like FormatHotkey does
        var slotVks = new System.Collections.Generic.List<uint>();
        if (config.Modifiers.HasFlag(KeyModifiers.Control)) slotVks.Add(Win32Constants.VK_CONTROL);
        if (config.Modifiers.HasFlag(KeyModifiers.Alt)) slotVks.Add(Win32Constants.VK_MENU);
        if (config.Modifiers.HasFlag(KeyModifiers.Shift)) slotVks.Add(Win32Constants.VK_SHIFT);
        if (config.Modifiers.HasFlag(KeyModifiers.Windows)) slotVks.Add(Win32Constants.VK_LWIN);
        if (config.VirtualKey != 0) slotVks.Add(config.VirtualKey);

        _slot1Vk = slotVks.Count > 0 ? slotVks[0] : 0;
        _slot2Vk = slotVks.Count > 1 ? slotVks[1] : 0;
        _slot3Vk = slotVks.Count > 2 ? slotVks[2] : 0;

        Slot1Text = _slot1Vk != 0 ? KeyFormattingHelper.FormatVirtualKey(_slot1Vk) : string.Empty;
        Slot2Text = _slot2Vk != 0 ? KeyFormattingHelper.FormatVirtualKey(_slot2Vk) : string.Empty;
        Slot3Text = _slot3Vk != 0 ? KeyFormattingHelper.FormatVirtualKey(_slot3Vk) : string.Empty;
    }


    private void OnAppStateChanged(object? sender, AppState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            (StatusDisplay, StatusColor) = state switch
            {
                AppState.Ready => ("● Ready", "#10B981"),
                AppState.Recording => ("● Listening...", "#E11D48"),
                AppState.Transcribing => ("◌ Transcribing...", "#38BDF8"),
                AppState.Cleaning => ("✦ Refining text...", "#A855F7"),
                AppState.Pasting => ("✓ Pasting...", "#10B981"),
                AppState.Success => ("✓ Done", "#10B981"),
                AppState.Error => ("✕ Error", "#E11D48"),
                AppState.Paused => ("● Paused", "#A1A1AA"),
                _ => ("● Ready", "#10B981")
            };

            switch (state)
            {
                case AppState.Recording:
                    IsListening = true;
                    IsHeroTranscribing = false;
                    HomeHeading = "Listening...";
                    HomeSubtitle = "Speak naturally. Release hotkey when done.";
                    break;
                case AppState.Transcribing:
                case AppState.Cleaning:
                    IsListening = false;
                    IsHeroTranscribing = true;
                    HomeHeading = "Transcribing...";
                    HomeSubtitle = "Transcribing in real-time via Gemini Transcribe...";
                    break;
                case AppState.Ready:
                case AppState.Success:
                    IsListening = false;
                    IsHeroTranscribing = false;
                    HomeHeading = "Ready to dictate";
                    HomeSubtitle = "VoiceFlow is active and listening for your hotkey anywhere on Windows.";
                    break;
                case AppState.Error:
                    IsListening = false;
                    IsHeroTranscribing = false;
                    HomeHeading = "Transcription error";
                    HomeSubtitle = "Check microphone connection or Gemini API key in Settings.";
                    break;
            }
        });
    }

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LastTranscriptionText = text;
            LastTranscriptionTime = "Just now";
            HasLastTranscription = true;

            if (EnableHistory)
            {
                RecentHistory.Insert(0, new HistoryItem
                {
                    FinalText = text,
                    Timestamp = DateTime.Now
                });

                while (RecentHistory.Count > 50)
                {
                    RecentHistory.RemoveAt(RecentHistory.Count - 1);
                }
                OnPropertyChanged(nameof(HasHistory));
            }
        });
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        if (IsTestingMic)
        {
            Dispatcher.UIThread.Post(() => MicTestLevel = level);
        }
        if (IsRecordingTestSpeech || IsListening)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TestAudioLevel = level;
                UpdateHomeWaveBars(level);
            });
        }
    }

    private void UpdateHomeWaveBars(float level)
    {
        double baseH = 8.0;
        double maxH = 38.0;
        double span = maxH - baseH;
        HomeWaveBar1 = Math.Clamp(baseH + span * (level * 1.0), baseH, maxH);
        HomeWaveBar2 = Math.Clamp(baseH + span * (level * 2.2), baseH, maxH);
        HomeWaveBar3 = Math.Clamp(baseH + span * (level * 3.0), baseH, maxH);
        HomeWaveBar4 = Math.Clamp(baseH + span * (level * 2.0), baseH, maxH);
        HomeWaveBar5 = Math.Clamp(baseH + span * (level * 1.1), baseH, maxH);
    }

    // --- Navigation Commands ---
    [RelayCommand]
    private void NavigateTo(string section)
    {
        if (Enum.TryParse<AppSection>(section, true, out var target))
        {
            CurrentSection = target;
        }
    }

    // --- Home View Commands ---
    [RelayCommand]
    private async Task ToggleHomeRecordingAsync()
    {
        if (IsRecordingTestSpeech)
        {
            await FinishTestSpeechAsync();
        }
        else if (!IsListening && !IsHeroTranscribing)
        {
            StartTestSpeech();
        }
    }

    [RelayCommand]
    private async Task CopyLastTranscriptionAsync()
    {
        if (!string.IsNullOrEmpty(LastTranscriptionText))
        {
            await _clipboardService.SetTextAsync(LastTranscriptionText);
            ConnectionStatusMessage = "✓ Copied to clipboard!";
        }
    }

    // --- History View Commands ---
    [RelayCommand]
    private async Task CopyHistoryItemAsync(HistoryItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.FinalText))
        {
            await _clipboardService.SetTextAsync(item.FinalText);
        }
    }

    [RelayCommand]
    private void DeleteHistoryItem(HistoryItem? item)
    {
        if (item != null)
        {
            RecentHistory.Remove(item);
            OnPropertyChanged(nameof(HasHistory));
            if (LastTranscriptionText == item.FinalText)
            {
                var next = RecentHistory.FirstOrDefault();
                if (next != null)
                {
                    LastTranscriptionText = next.FinalText;
                    LastTranscriptionTime = next.FormattedTime;
                }
                else
                {
                    HasLastTranscription = false;
                    LastTranscriptionText = string.Empty;
                }
            }
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        RecentHistory.Clear();
        OnPropertyChanged(nameof(HasHistory));
    }

    // --- Settings Commands & Auto-Persist ---
    [RelayCommand]
    private void SaveApiKey()
    {
        string key = ApiKeyInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            ConnectionStatusMessage = "Please paste or type your Gemini API key.";
            ConnectionStatusColor = "#E11D48";
            return;
        }

        _settingsService.SaveApiKey(key);
        HasSavedApiKey = true;
        SavedKeyPreviewText = key.Length > 8 ? $"{key[..4]}...{key[^4..]}" : "Configured";
        ConnectionStatusMessage = $"✓ Connected ({SavedKeyPreviewText})";
        ConnectionStatusColor = "#10B981";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        string key = ApiKeyInput?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(key))
        {
            _settingsService.SaveApiKey(key);
            HasSavedApiKey = true;
            SavedKeyPreviewText = key.Length > 8 ? $"{key[..4]}...{key[^4..]}" : "Configured";
        }

        if (!_settingsService.HasApiKey)
        {
            ConnectionStatusMessage = "✕ Please enter an API key first.";
            ConnectionStatusColor = "#E11D48";
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedModel))
        {
            _settingsService.Settings.GeminiModel = SelectedModel;
            CurrentModelText = SelectedModel;
        }

        IsTestingConnection = true;
        TestConnectionButtonText = "Testing...";
        ConnectionStatusMessage = "Connecting to Google Gemini API...";
        ConnectionStatusColor = "#38BDF8";

        try
        {
            var (success, message) = await _transcriptionProvider.TestConnectionAsync();
            ConnectionStatusMessage = success ? "✓ Connected" : message;
            ConnectionStatusColor = success ? "#10B981" : "#E11D48";
        }
        catch (Exception ex)
        {
            ConnectionStatusMessage = $"✕ Connection failed: {ex.Message}";
            ConnectionStatusColor = "#E11D48";
        }
        finally
        {
            IsTestingConnection = false;
            TestConnectionButtonText = "Test Connection";
        }
    }

    [RelayCommand]
    private void ToggleRevealApiKey()
    {
        IsApiKeyRevealed = !IsApiKeyRevealed;
        ApiKeyPasswordChar = IsApiKeyRevealed ? '\0' : '•';
        RevealButtonIcon = IsApiKeyRevealed ? "🔒" : "👁️";
    }

    [RelayCommand]
    private async Task PasteApiKeyFromClipboardAsync()
    {
        string? text = await _clipboardService.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            ApiKeyInput = text.Trim();
            ConnectionStatusMessage = "Key pasted! Click 'Save API Key' or 'Test Connection'.";
            ConnectionStatusColor = "#38BDF8";
        }
        else
        {
            ConnectionStatusMessage = "Clipboard is empty or does not contain text.";
            ConnectionStatusColor = "#E11D48";
        }
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        ApiKeyInput = string.Empty;
        _settingsService.SaveApiKey(string.Empty);
        HasSavedApiKey = false;
        SavedKeyPreviewText = string.Empty;
        ConnectionStatusMessage = "API key cleared. Enter a new key to connect.";
        ConnectionStatusColor = "#F59E0B";
    }

    [RelayCommand]
    private void OpenAiStudio()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/apikey",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to open AI Studio in browser.", ex);
        }
    }

    [RelayCommand]
    private void ToggleMicrophoneTest()
    {
        if (IsTestingMic)
        {
            _audioRecorder.StopRecordingAsync();
            IsTestingMic = false;
            MicTestLevel = 0f;
        }
        else
        {
            try
            {
                int dev = SelectedAudioDevice?.DeviceNumber ?? -1;
                _audioRecorder.StartRecording(dev);
                IsTestingMic = true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to test microphone.", ex);
            }
        }
    }

    // ── 3-Box Slot Commands ──────────────────────────────────────────────────

    private void StartRecordingSlot(int slotIndex)
    {
        _hotkeyService.StopCapturingSingleKey();
        ActiveRecordingSlot = slotIndex;
        ShortcutStatusFeedback = $"Press any key for Box {slotIndex + 1} (Esc to cancel)...";
        ShortcutStatusColor = "#3B82F6";
        _hotkeyService.StartCapturingSingleKey();
    }

    [RelayCommand]
    private void RecordSlot1() => StartRecordingSlot(0);

    [RelayCommand]
    private void RecordSlot2() => StartRecordingSlot(1);

    [RelayCommand]
    private void RecordSlot3() => StartRecordingSlot(2);

    [RelayCommand]
    private void RecordSlot(object? parameter)
    {
        if (parameter != null && int.TryParse(parameter.ToString(), out int slot))
        {
            StartRecordingSlot(slot);
        }
    }

    private void ClearSlotInternal(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: _slot1Vk = 0; Slot1Text = string.Empty; break;
            case 1: _slot2Vk = 0; Slot2Text = string.Empty; break;
            case 2: _slot3Vk = 0; Slot3Text = string.Empty; break;
        }
        IsShortcutSavedAndActive = false;
        if (HasAnySlotKey)
        {
            ShortcutStatusFeedback = "Key removed. Click 'Save & Activate' to apply changes.";
            ShortcutStatusColor = "#3B82F6";
        }
        else
        {
            ShortcutStatusFeedback = "All boxes cleared. Add at least 1 key.";
            ShortcutStatusColor = "#F59E0B";
        }
    }

    [RelayCommand]
    private void ClearSlot1() => ClearSlotInternal(0);

    [RelayCommand]
    private void ClearSlot2() => ClearSlotInternal(1);

    [RelayCommand]
    private void ClearSlot3() => ClearSlotInternal(2);

    [RelayCommand]
    private void ClearSlot(object? parameter)
    {
        if (parameter != null && int.TryParse(parameter.ToString(), out int slot))
        {
            ClearSlotInternal(slot);
        }
    }

    [RelayCommand]
    private void SaveAndActivateShortcut()
    {
        ApplySlotsAsHotkey();
    }

    [RelayCommand]
    private void ResetDefaultShortcut()
    {
        _hotkeyService.StopCapturingSingleKey();
        _hotkeyService.StopRecordingShortcut();
        ActiveRecordingSlot = -1;
        IsRecordingShortcut = false;
        var def = HotkeyConfig.Default;
        _settingsService.Settings.Hotkey = def;
        AutoPersistSettings();
        CurrentShortcutText = KeyFormattingHelper.FormatHotkey(def);
        UpdateShortcutKeyDisplay(def);
        bool success = _hotkeyService.RegisterHotkey(def, SelectedHotkeyMode);
        IsShortcutSavedAndActive = success;
        ShortcutStatusFeedback = success ? "✓ Reset to default (Ctrl + Space) and activated!" : "✕ Could not register default shortcut.";
        ShortcutStatusColor = success ? "#10B981" : "#E11D48";
        HotkeyRegistrationError = success ? string.Empty : "✕ Default shortcut is currently unavailable.";
    }

    [RelayCommand]
    private void SelectPreset(object? presetParam)
    {
        string? preset = presetParam?.ToString();
        if (string.IsNullOrWhiteSpace(preset)) return;

        _hotkeyService.StopCapturingSingleKey();
        _hotkeyService.StopRecordingShortcut();
        ActiveRecordingSlot = -1;

        var config = KeyFormattingHelper.ParseHotkey(preset);
        if (config == null) return;

        bool success = _hotkeyService.RegisterHotkey(config, SelectedHotkeyMode);
        if (success)
        {
            _settingsService.Settings.Hotkey = config;
            AutoPersistSettings();
            CurrentShortcutText = KeyFormattingHelper.FormatHotkey(config);
            UpdateShortcutKeyDisplay(config);
            IsShortcutSavedAndActive = true;
            ShortcutStatusFeedback = $"✓ Preset [{preset}] activated and ready to use!";
            ShortcutStatusColor = "#10B981";
            HotkeyRegistrationError = string.Empty;
        }
        else
        {
            ShortcutStatusFeedback = $"✕ Windows rejected [{preset}]. Choose another preset.";
            ShortcutStatusColor = "#E11D48";
        }
    }

    [RelayCommand]
    private void SetPresetShortcut(string preset) => SelectPreset(preset);

    [RelayCommand]
    private void SelectPresetF8() => SelectPreset("F8");

    [RelayCommand]
    private void SelectPresetCtrlSpace() => SelectPreset("Ctrl + Space");

    [RelayCommand]
    private void SelectPresetAltSpace() => SelectPreset("Alt + Space");

    [RelayCommand]
    private void SelectPresetF6() => SelectPreset("F6");

    [RelayCommand]
    private void SelectPresetCtrlF8() => SelectPreset("Ctrl + F8");

    [RelayCommand]
    private void SelectPresetCtrlShiftSpace() => SelectPreset("Ctrl + Shift + Space");

    [RelayCommand]
    private void StartRecordingShortcut() { }

    [RelayCommand]
    private void CancelRecordingShortcut()
    {
        _hotkeyService.StopCapturingSingleKey();
        ActiveRecordingSlot = -1;
        ShortcutStatusFeedback = "Recording cancelled.";
        ShortcutStatusColor = "#A1A1AA";
    }

    private void OnSingleKeyCaptured(object? sender, uint vk)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            int slot = ActiveRecordingSlot;
            ActiveRecordingSlot = -1;

            if (slot < 0) return;

            string keyText = KeyFormattingHelper.FormatVirtualKey(vk);
            switch (slot)
            {
                case 0: _slot1Vk = vk; Slot1Text = keyText; break;
                case 1: _slot2Vk = vk; Slot2Text = keyText; break;
                case 2: _slot3Vk = vk; Slot3Text = keyText; break;
            }

            IsShortcutSavedAndActive = false;
            ShortcutStatusFeedback = $"Box {slot + 1} set to [{keyText}]. Click 'Save & Activate' to apply.";
            ShortcutStatusColor = "#3B82F6";
        });
    }

    /// <summary>
    /// Builds a HotkeyConfig from the 3 slot VKs, registers it, and saves it.
    /// Modifiers (Ctrl/Alt/Shift/Win) → Modifiers flags. Last non-modifier → VirtualKey.
    /// If all are modifiers, last one becomes VirtualKey.
    /// </summary>
    private void ApplySlotsAsHotkey()
    {
        var vks = new[] { _slot1Vk, _slot2Vk, _slot3Vk }.Where(v => v != 0).ToArray();
        if (vks.Length == 0)
        {
            ShortcutStatusFeedback = "All boxes are empty. Add at least one key.";
            ShortcutStatusColor = "#F59E0B";
            IsShortcutSavedAndActive = false;
            return;
        }

        HotkeyConfig config = BuildHotkeyConfigFromVks(vks);

        if (IsReservedWindowsShortcut(config))
        {
            ShortcutStatusFeedback = "✕ This combination is reserved by Windows. Choose another.";
            ShortcutStatusColor = "#E11D48";
            IsShortcutSavedAndActive = false;
            return;
        }

        bool success = _hotkeyService.RegisterHotkey(config, SelectedHotkeyMode);
        if (success)
        {
            _settingsService.Settings.Hotkey = config;
            AutoPersistSettings();
            CurrentShortcutText = KeyFormattingHelper.FormatHotkey(config);
            // Update HomeView keycaps
            string formatted = CurrentShortcutText;
            var parts = formatted.Split(" + ");
            ShortcutModifierKeyText = parts.Length > 1 ? string.Join(" + ", parts.Take(parts.Length - 1)) : string.Empty;
            ShortcutMainKeyText = parts.Last();
            string action = SelectedHotkeyMode == HotkeyActivationMode.HoldToTalk ? "Hold" : "Press";
            DictationInstructionTitle = $"{action} {formatted} to dictate";
            ShortcutStatusFeedback = $"✓ Shortcut [{formatted}] is now active and ready to use!";
            ShortcutStatusColor = "#10B981";
            HotkeyRegistrationError = string.Empty;
            IsShortcutSavedAndActive = true;
        }
        else
        {
            _hotkeyService.RegisterHotkey(_settingsService.Settings.Hotkey, SelectedHotkeyMode);
            ShortcutStatusFeedback = "✕ Windows rejected this shortcut. It may be in use by another app.";
            ShortcutStatusColor = "#E11D48";
            IsShortcutSavedAndActive = false;
        }
    }

    private static HotkeyConfig BuildHotkeyConfigFromVks(uint[] vks)
    {
        static bool IsModVk(uint v) =>
            v is Win32Constants.VK_CONTROL or 0xA2 or 0xA3
              or Win32Constants.VK_MENU or 0xA4 or 0xA5
              or Win32Constants.VK_SHIFT or 0xA0 or 0xA1
              or Win32Constants.VK_LWIN or Win32Constants.VK_RWIN;

        var modVks = vks.Where(IsModVk).ToArray();
        var nonModVks = vks.Where(v => !IsModVk(v)).ToArray();

        uint mainVk;
        uint[] modifierVks;

        if (nonModVks.Length > 0)
        {
            mainVk = nonModVks.Last();
            modifierVks = modVks;
        }
        else
        {
            // All modifier keys — last one is "the key", the rest are modifiers
            mainVk = modVks.Last();
            modifierVks = modVks.Take(modVks.Length - 1).ToArray();
        }

        KeyModifiers mods = KeyModifiers.None;
        foreach (uint v in modifierVks)
        {
            if (v is Win32Constants.VK_CONTROL or 0xA2 or 0xA3) mods |= KeyModifiers.Control;
            else if (v is Win32Constants.VK_MENU or 0xA4 or 0xA5) mods |= KeyModifiers.Alt;
            else if (v is Win32Constants.VK_SHIFT or 0xA0 or 0xA1) mods |= KeyModifiers.Shift;
            else if (v is Win32Constants.VK_LWIN or Win32Constants.VK_RWIN) mods |= KeyModifiers.Windows;
        }

        return new HotkeyConfig(mods, mainVk);
    }

    private void OnShortcutRecorded(object? sender, HotkeyConfig recorded)
    {
        // Legacy handler — not triggered by slot recorder (kept for compatibility)
    }

    private void OnShortcutRecordingPreview(object? sender, string preview) { }

    private void OnShortcutRecordingCanceled(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ActiveRecordingSlot = -1;
            OnPropertyChanged(nameof(IsRecordingAnySlot));
            IsRecordingShortcut = false;
            ShortcutStatusFeedback = "Recording cancelled.";
            ShortcutStatusColor = "#A1A1AA";
        });
    }

    private static bool IsReservedWindowsShortcut(HotkeyConfig config)
    {
        if (config.Modifiers == KeyModifiers.Windows && config.VirtualKey == 0x4C) return true; // Win + L
        if (config.Modifiers == KeyModifiers.Windows && config.VirtualKey == 0x44) return true; // Win + D
        if (config.Modifiers == KeyModifiers.Alt && config.VirtualKey == Win32Constants.VK_TAB) return true; // Alt + Tab
        if (config.Modifiers == KeyModifiers.Alt && config.VirtualKey == Win32Constants.VK_F4) return true; // Alt + F4
        return false;
    }

    [RelayCommand]
    private void SaveAllSettings()
    {
        AutoPersistSettings(updateHotkey: false);
    }

    private bool _isPersistingSettings = false;

    private void AutoPersistSettings(bool updateHotkey = false)
    {
        if (_isPersistingSettings) return;
        if (_settingsService?.Settings == null) return;

        try
        {
            _isPersistingSettings = true;

            var s = _settingsService.Settings;
            s.GeminiModel = SelectedModel;
            s.HotkeyMode = SelectedHotkeyMode;
            s.CleanupEnabled = CleanupEnabled;
            s.CleanupMode = CleanupMode;
            s.AutoPaste = AutoPaste;
            s.PreserveClipboard = PreserveClipboard;
            s.ShowOverlay = ShowOverlay;
            s.RunInBackground = RunInBackground;
            s.Theme = SelectedTheme;
            s.EnableHistory = EnableHistory;
            s.StartWithWindows = StartWithWindows;

            if (SelectedAudioDevice != null)
            {
                s.SelectedAudioDeviceIndex = SelectedAudioDevice.DeviceNumber;
                s.SelectedAudioDeviceName = SelectedAudioDevice.Name;
                CurrentMicrophoneText = SelectedAudioDevice.Name;
            }

            _settingsService.SaveSettings();

            if (updateHotkey)
            {
                _hotkeyService?.RegisterHotkey(s.Hotkey, s.HotkeyMode);
            }
        }
        finally
        {
            _isPersistingSettings = false;
        }
    }

    public static void ApplyTheme(ThemePreference theme)
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    // --- Interactive Voice Testing Playground Commands ---
    [RelayCommand]
    private void StartTestSpeech()
    {
        if (IsRecordingTestSpeech || IsTranscribingTestSpeech) return;

        if (!_audioRecorder.HasInputDevices)
        {
            TestSpeechStatus = "No microphone connected. Please plug in a microphone.";
            return;
        }

        try
        {
            int dev = SelectedAudioDevice?.DeviceNumber ?? -1;
            _audioRecorder.StartRecording(dev);
            IsRecordingTestSpeech = true;
            IsListening = true;
            HasTestResult = false;
            TestTranscriptionResult = string.Empty;
            TestSpeechLatencyInfo = string.Empty;
            TestSpeechDuration = "00:00";
            HomeTimerText = "00:00";
            TestAudioLevel = 0f;
            HomeHeading = "Listening...";
            HomeSubtitle = "Speak naturally. Release hotkey or click finish when done.";
            TestSpeechStatus = "Listening... Speak clearly into your microphone, then click Finish.";

            _testSpeechTimer?.Stop();
            _testSpeechTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _testSpeechTimer.Tick += (s, e) =>
            {
                var d = _audioRecorder.RecordingDuration;
                TestSpeechDuration = $"{(int)d.TotalMinutes:D2}:{d.Seconds:D2}";
                HomeTimerText = TestSpeechDuration;
            };
            _testSpeechTimer.Start();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to start test speech recording.", ex);
            TestSpeechStatus = (ex is InvalidOperationException ioe && ioe.Message.Contains("No microphone", StringComparison.OrdinalIgnoreCase))
                ? "No microphone connected. Please plug in a microphone."
                : $"Microphone error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task FinishTestSpeechAsync()
    {
        if (!IsRecordingTestSpeech) return;

        _testSpeechTimer?.Stop();
        IsRecordingTestSpeech = false;
        IsListening = false;
        IsTranscribingTestSpeech = true;
        IsHeroTranscribing = true;
        HomeHeading = "Transcribing...";
        HomeSubtitle = "Transcribing in real-time via Gemini Transcribe...";
        TestSpeechStatus = "Transcribing audio with Gemini API...";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var audioStream = await _audioRecorder.StopRecordingAsync();
            if (audioStream.Length < 1000)
            {
                TestSpeechStatus = "Recording too short to transcribe. Please speak a sentence and click Finish.";
                HomeHeading = "Ready to dictate";
                HomeSubtitle = "Recording was too short. Speak clearly into your microphone.";
                IsTranscribingTestSpeech = false;
                IsHeroTranscribing = false;
                return;
            }

            var result = await _transcriptionProvider.TranscribeAsync(audioStream, "audio/wav");
            if (!result.Success)
            {
                TestSpeechStatus = $"Transcription failed: {result.ErrorMessage}";
                HomeHeading = "Transcription error";
                HomeSubtitle = result.ErrorMessage ?? "Unknown transcription failure";
                BuildDetailedError("Transcription Failed", result.ErrorMessage ?? "Unknown transcription failure", null);
                IsTranscribingTestSpeech = false;
                IsHeroTranscribing = false;
                return;
            }

            string text = result.Text;

            if (CleanupEnabled && CleanupMode != TextCleanupMode.Off)
            {
                TestSpeechStatus = "Refining transcription with AI cleanup...";
                text = await _aiTextProcessor.ProcessTextAsync(text, CleanupMode);
            }

            sw.Stop();
            TestTranscriptionResult = text;
            HasTestResult = true;
            HasDebugError = false;
            TestSpeechLatencyInfo = $"✓ Transcribed in {sw.ElapsedMilliseconds}ms ({SelectedModel})";
            TestSpeechStatus = "Transcription complete!";

            LastTranscriptionText = text;
            LastTranscriptionTime = "Just now";
            HasLastTranscription = true;
            HomeHeading = "Ready to dictate";
            HomeSubtitle = "VoiceFlow is active and listening for your hotkey anywhere on Windows.";

            if (EnableHistory)
            {
                RecentHistory.Insert(0, new HistoryItem
                {
                    FinalText = text,
                    Timestamp = DateTime.Now
                });
                while (RecentHistory.Count > 50)
                {
                    RecentHistory.RemoveAt(RecentHistory.Count - 1);
                }
                OnPropertyChanged(nameof(HasHistory));
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Error in test speech transcription.", ex);
            TestSpeechStatus = $"Error: {ex.Message}";
            HomeHeading = "Transcription error";
            HomeSubtitle = ex.Message;
            BuildDetailedError("Exception during Speech Test", ex.Message, ex);
        }
        finally
        {
            IsTranscribingTestSpeech = false;
            IsHeroTranscribing = false;
        }
    }

    private void BuildDetailedError(string category, string message, Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== VOICEFLOW ERROR REPORT ===");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Category: {category}");
        sb.AppendLine($"Error Message: {message}");
        sb.AppendLine();
        sb.AppendLine($"--- ENVIRONMENT & CONFIGURATION ---");
        sb.AppendLine($"Microphone: {SelectedAudioDevice?.Name ?? "Default"} (Index: {SelectedAudioDevice?.DeviceNumber})");
        sb.AppendLine($"Target Model: {SelectedModel}");
        sb.AppendLine($"Has API Key: {_settingsService.HasApiKey}");
        sb.AppendLine($"AI Cleanup Enabled: {CleanupEnabled} (Mode: {CleanupMode})");
        sb.AppendLine();

        if (ex != null)
        {
            sb.AppendLine($"--- EXCEPTION DETAILS ---");
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            sb.AppendLine();
            sb.AppendLine($"--- STACK TRACE ---");
            sb.AppendLine(ex.StackTrace ?? "No stack trace available.");
            sb.AppendLine();
        }

        sb.AppendLine($"--- RECENT LOGS (Last 25 events) ---");
        var recentLogs = AppLogger.GetRecentLogs(25);
        if (recentLogs.Count > 0)
        {
            foreach (var log in recentLogs)
            {
                sb.AppendLine(log);
            }
        }
        else
        {
            sb.AppendLine("No recent logs recorded.");
        }

        DetailedErrorSummary = sb.ToString();
        HasDebugError = true;
        IsDebugViewVisible = true;
    }

    [RelayCommand]
    private void ToggleDebugView()
    {
        IsDebugViewVisible = !IsDebugViewVisible;
        if (IsDebugViewVisible && string.IsNullOrEmpty(DetailedErrorSummary))
        {
            BuildDetailedError("Current State Diagnostics", "User opened debug viewer.", null);
            HasDebugError = false;
        }
    }

    [RelayCommand]
    private async Task CopyDetailedErrorAsync()
    {
        if (!string.IsNullOrEmpty(DetailedErrorSummary))
        {
            await _clipboardService.SetTextAsync(DetailedErrorSummary);
            TestSpeechStatus = "✓ Copied complete debug report to clipboard!";
        }
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            string path = AppLogger.LogFilePath;
            if (System.IO.File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to open log file.", ex);
        }
    }

    [RelayCommand]
    private async Task CancelTestSpeechAsync()
    {
        if (IsRecordingTestSpeech)
        {
            _testSpeechTimer?.Stop();
            IsRecordingTestSpeech = false;
            IsListening = false;
            await _audioRecorder.StopRecordingAsync();
            TestSpeechStatus = "Recording cancelled.";
            HomeHeading = "Ready to dictate";
            HomeSubtitle = "VoiceFlow is active and listening for your hotkey anywhere on Windows.";
        }
    }

    [RelayCommand]
    private async Task CopyTestTranscriptionAsync()
    {
        if (!string.IsNullOrEmpty(TestTranscriptionResult))
        {
            await _clipboardService.SetTextAsync(TestTranscriptionResult);
            TestSpeechStatus = "✓ Copied transcription to clipboard!";
        }
    }

    // --- About View Commands ---
    [RelayCommand]
    private void OpenDocumentation()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://ai.google.dev/gemini-api/docs",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to open documentation link.", ex);
        }
    }

    [RelayCommand]
    private void OpenPrivacyPolicy()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to open privacy policy link.", ex);
        }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to open GitHub link.", ex);
        }
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        ConnectionStatusMessage = "VoiceFlow is up to date (v1.0.0).";
        ConnectionStatusColor = "#10B981";
    }
}
