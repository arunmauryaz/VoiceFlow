using System;
using System.Collections.ObjectModel;
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

    // --- Status Dashboard ---
    [ObservableProperty]
    private string _statusDisplay = "● Ready";

    [ObservableProperty]
    private string _statusColor = "#22C55E"; // Green

    [ObservableProperty]
    private string _currentShortcutText = "Ctrl + Space";

    [ObservableProperty]
    private string _currentMicrophoneText = "Default Microphone";

    [ObservableProperty]
    private string _currentModelText = "gemini-2.5-flash";

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
    private string _testSpeechStatus = "Click 'Start Speaking' to test your microphone and live Gemini transcription.";

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
    private string _selectedModel = "gemini-2.5-flash";

    [ObservableProperty]
    private string _connectionStatusMessage = string.Empty;

    [ObservableProperty]
    private string _connectionStatusColor = "#94A3B8";

    [ObservableProperty]
    private bool _isTestingConnection;

    public ObservableCollection<string> AvailableModels { get; } = new()
    {
        "gemini-2.5-flash",
        "gemini-2.0-flash",
        "gemini-1.5-flash"
    };

    public ObservableCollection<string> AvailableProviders { get; } = new()
    {
        "Google Gemini",
        "Local Whisper (Coming soon)"
    };

    [ObservableProperty]
    private string _selectedProvider = "Google Gemini";

    // --- Microphone ---
    public ObservableCollection<AudioDeviceInfo> AudioDevices { get; } = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioDevice;

    [ObservableProperty]
    private bool _isTestingMic;

    [ObservableProperty]
    private float _micTestLevel;

    // --- Shortcut ---
    [ObservableProperty]
    private HotkeyActivationMode _selectedHotkeyMode = HotkeyActivationMode.HoldToTalk;

    [ObservableProperty]
    private string _hotkeyRegistrationError = string.Empty;

    public ObservableCollection<string> PresetShortcuts { get; } = new()
    {
        "Ctrl + Space",
        "Alt + Space",
        "Ctrl + Shift + Space",
        "F8",
        "F9"
    };

    // --- AI Cleanup ---
    [ObservableProperty]
    private bool _cleanupEnabled;

    [ObservableProperty]
    private TextCleanupMode _cleanupMode = TextCleanupMode.CleanTranscription;

    // --- Behavior & System ---
    [ObservableProperty]
    private bool _autoPaste = true;

    [ObservableProperty]
    private bool _preserveClipboard = true;

    [ObservableProperty]
    private bool _showOverlay = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _runInBackground = true;

    [ObservableProperty]
    private ThemePreference _selectedTheme = ThemePreference.System;

    // --- History (Optional) ---
    [ObservableProperty]
    private bool _enableHistory;

    public ObservableCollection<HistoryItem> RecentHistory { get; } = new();

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

        LoadState();
    }

    private void LoadState()
    {
        var s = _settingsService.Settings;

        HasSavedApiKey = _settingsService.HasApiKey;
        if (HasSavedApiKey)
        {
            ApiKeyInput = "••••••••••••••••••••••••";
        }

        SelectedModel = s.GeminiModel;
        CurrentModelText = s.GeminiModel;

        SelectedHotkeyMode = s.HotkeyMode;
        CurrentShortcutText = KeyFormattingHelper.FormatHotkey(s.Hotkey);

        CleanupEnabled = s.CleanupEnabled;
        CleanupMode = s.CleanupMode;

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

        var match = AudioDevices.FirstOrDefault(d => d.DeviceNumber == _settingsService.Settings.SelectedAudioDeviceIndex)
                    ?? AudioDevices.FirstOrDefault();

        SelectedAudioDevice = match;
        CurrentMicrophoneText = match?.Name ?? "Default Microphone";
    }

    private void OnAppStateChanged(object? sender, AppState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            (StatusDisplay, StatusColor) = state switch
            {
                AppState.Ready => ("● Ready", "#22C55E"),
                AppState.Recording => ("● Recording...", "#EF4444"),
                AppState.Transcribing => ("◌ Transcribing...", "#38BDF8"),
                AppState.Cleaning => ("✦ Refining text...", "#A855F7"),
                AppState.Pasting => ("✓ Pasting...", "#10B981"),
                AppState.Success => ("✓ Done", "#22C55E"),
                AppState.Error => ("✕ Error", "#EF4444"),
                AppState.Paused => ("● Paused", "#94A3B8"),
                _ => ("● Ready", "#22C55E")
            };
        });
    }

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        if (!EnableHistory) return;

        Dispatcher.UIThread.Post(() =>
        {
            RecentHistory.Insert(0, new HistoryItem
            {
                FinalText = text,
                Timestamp = DateTime.Now
            });

            while (RecentHistory.Count > 20)
            {
                RecentHistory.RemoveAt(RecentHistory.Count - 1);
            }
        });
    }

    private void OnAudioLevelChanged(object? sender, float level)
    {
        if (IsTestingMic)
        {
            Dispatcher.UIThread.Post(() => MicTestLevel = level);
        }
        if (IsRecordingTestSpeech)
        {
            Dispatcher.UIThread.Post(() => TestAudioLevel = level);
        }
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput) || ApiKeyInput.Contains('•'))
        {
            ConnectionStatusMessage = "Please enter a valid Gemini API key.";
            ConnectionStatusColor = "#EF4444";
            return;
        }

        _settingsService.SaveApiKey(ApiKeyInput.Trim());
        HasSavedApiKey = true;
        ApiKeyInput = "••••••••••••••••••••••••";
        ConnectionStatusMessage = "API key securely saved.";
        ConnectionStatusColor = "#22C55E";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!_settingsService.HasApiKey && (string.IsNullOrWhiteSpace(ApiKeyInput) || ApiKeyInput.Contains('•')))
        {
            ConnectionStatusMessage = "✕ Please enter an API key first.";
            ConnectionStatusColor = "#EF4444";
            return;
        }

        if (!ApiKeyInput.Contains('•') && !string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            _settingsService.SaveApiKey(ApiKeyInput.Trim());
            HasSavedApiKey = true;
            ApiKeyInput = "••••••••••••••••••••••••";
        }

        IsTestingConnection = true;
        ConnectionStatusMessage = "Testing connection to Gemini API...";
        ConnectionStatusColor = "#38BDF8";

        try
        {
            var (success, message) = await _transcriptionProvider.TestConnectionAsync();
            ConnectionStatusMessage = message;
            ConnectionStatusColor = success ? "#22C55E" : "#EF4444";
        }
        catch (Exception ex)
        {
            ConnectionStatusMessage = $"✕ Connection failed: {ex.Message}";
            ConnectionStatusColor = "#EF4444";
        }
        finally
        {
            IsTestingConnection = false;
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

    [RelayCommand]
    private void SetPresetShortcut(string preset)
    {
        HotkeyConfig config = preset switch
        {
            "Alt + Space" => new HotkeyConfig(KeyModifiers.Alt, Win32Constants.VK_SPACE),
            "Ctrl + Shift + Space" => new HotkeyConfig(KeyModifiers.Control | KeyModifiers.Shift, Win32Constants.VK_SPACE),
            "F8" => new HotkeyConfig(KeyModifiers.None, Win32Constants.VK_F8),
            "F9" => new HotkeyConfig(KeyModifiers.None, Win32Constants.VK_F9),
            _ => new HotkeyConfig(KeyModifiers.Control, Win32Constants.VK_SPACE)
        };

        _settingsService.Settings.Hotkey = config;
        _settingsService.SaveSettings();

        CurrentShortcutText = KeyFormattingHelper.FormatHotkey(config);

        bool success = _hotkeyService.RegisterHotkey(config, SelectedHotkeyMode);
        HotkeyRegistrationError = success ? string.Empty : "✕ Could not register this shortcut. It may be in use by Windows.";
    }

    [RelayCommand]
    private void SaveAllSettings()
    {
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

        if (SelectedAudioDevice != null)
        {
            s.SelectedAudioDeviceIndex = SelectedAudioDevice.DeviceNumber;
            s.SelectedAudioDeviceName = SelectedAudioDevice.Name;
            CurrentMicrophoneText = SelectedAudioDevice.Name;
        }

        _settingsService.SaveSettings();

        _startupService.SetStartupEnabled(StartWithWindows);
        s.StartWithWindows = StartWithWindows;

        _hotkeyService.RegisterHotkey(s.Hotkey, s.HotkeyMode);

        ApplyTheme(SelectedTheme);
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

    [RelayCommand]
    private void ClearHistory()
    {
        RecentHistory.Clear();
    }

    // --- Interactive Voice Testing Playground Commands ---

    [RelayCommand]
    private void StartTestSpeech()
    {
        if (IsRecordingTestSpeech || IsTranscribingTestSpeech) return;

        try
        {
            int dev = SelectedAudioDevice?.DeviceNumber ?? -1;
            _audioRecorder.StartRecording(dev);
            IsRecordingTestSpeech = true;
            HasTestResult = false;
            TestTranscriptionResult = string.Empty;
            TestSpeechLatencyInfo = string.Empty;
            TestSpeechDuration = "00:00";
            TestAudioLevel = 0f;
            TestSpeechStatus = "Listening... Speak clearly into your microphone, then click Finish.";

            _testSpeechTimer?.Stop();
            _testSpeechTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _testSpeechTimer.Tick += (s, e) =>
            {
                var d = _audioRecorder.RecordingDuration;
                TestSpeechDuration = $"{(int)d.TotalMinutes:D2}:{d.Seconds:D2}";
            };
            _testSpeechTimer.Start();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to start test speech recording.", ex);
            TestSpeechStatus = $"Microphone error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task FinishTestSpeechAsync()
    {
        if (!IsRecordingTestSpeech) return;

        _testSpeechTimer?.Stop();
        IsRecordingTestSpeech = false;
        IsTranscribingTestSpeech = true;
        TestSpeechStatus = "Transcribing audio with Gemini API...";

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var audioStream = await _audioRecorder.StopRecordingAsync();
            if (audioStream.Length < 1000)
            {
                TestSpeechStatus = "Recording too short to transcribe. Please speak a sentence and click Finish.";
                IsTranscribingTestSpeech = false;
                return;
            }

            var result = await _transcriptionProvider.TranscribeAsync(audioStream, "audio/wav");
            if (!result.Success)
            {
                TestSpeechStatus = $"Transcription failed: {result.ErrorMessage}";
                BuildDetailedError("Transcription Failed", result.ErrorMessage ?? "Unknown transcription failure", null);
                IsTranscribingTestSpeech = false;
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
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Error in test speech transcription.", ex);
            TestSpeechStatus = $"Error: {ex.Message}";
            BuildDetailedError("Exception during Speech Test", ex.Message, ex);
        }
        finally
        {
            IsTranscribingTestSpeech = false;
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
        IsDebugViewVisible = true; // Automatically expand debug view so user can see and copy it!
    }

    [RelayCommand]
    private void ToggleDebugView()
    {
        IsDebugViewVisible = !IsDebugViewVisible;
        if (IsDebugViewVisible && string.IsNullOrEmpty(DetailedErrorSummary))
        {
            // Populate with current state diagnostics even if no error
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
            await _audioRecorder.StopRecordingAsync();
            TestSpeechStatus = "Recording cancelled.";
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
}
