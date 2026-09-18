using System;
using System.IO;
using System.Threading.Tasks;
using VoiceFlow.Helpers;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Native;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Services;
using VoiceFlow.ViewModels;
using Xunit;

namespace VoiceFlow.Tests;

public class MockSettingsService : ISettingsService
{
    public AppSettings Settings { get; set; } = new();
    public string? StoredApiKey { get; set; }

    public bool HasApiKey => !string.IsNullOrEmpty(StoredApiKey);

    public event EventHandler? SettingsChanged;

    public string? LoadApiKey() => StoredApiKey;
    public void LoadSettings() { }
    public void SaveApiKey(string apiKey)
    {
        StoredApiKey = apiKey;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SaveSettings()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class ComponentTests
{
    [Fact]
    public void RecordingOverlayViewModel_StateTransitions_WorkCorrectly()
    {
        var vm = new RecordingOverlayViewModel();

        Assert.Equal(AppState.Ready, vm.State);
        Assert.False(vm.IsVisible);

        // Recording
        vm.ShowRecording();
        Assert.Equal(AppState.Recording, vm.State);
        Assert.True(vm.IsVisible);
        Assert.Equal("Listening...", vm.StatusText);

        // Duration format
        vm.UpdateDuration(TimeSpan.FromSeconds(65));
        Assert.Equal("01:05", vm.DurationText);

        // Audio Level
        vm.UpdateAudioLevel(0.2f);
        Assert.True(vm.LevelBar2 > vm.LevelBar1);

        // Transcribing
        vm.ShowTranscribing();
        Assert.Equal(AppState.Transcribing, vm.State);
        Assert.Equal("Transcribing...", vm.StatusText);

        // Cleaning
        vm.ShowCleaning();
        Assert.Equal(AppState.Cleaning, vm.State);
        Assert.Equal("Refining text...", vm.StatusText);

        // Success
        vm.ShowSuccess("Done");
        Assert.Equal(AppState.Success, vm.State);
        Assert.Equal("Done", vm.StatusText);

        // Error
        vm.ShowError("Microphone not detected");
        Assert.Equal(AppState.Error, vm.State);
        Assert.Equal("Microphone not detected", vm.StatusText);
    }

    [Fact]
    public async Task GeminiProvider_NoApiKey_ReturnsFailure()
    {
        var mockSettings = new MockSettingsService { StoredApiKey = null };
        var provider = new GeminiTranscriptionProvider(mockSettings);

        using var ms = new MemoryStream(new byte[2000]);
        var result = await provider.TranscribeAsync(ms, "audio/wav");

        Assert.False(result.Success);
        Assert.Contains("missing", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeminiProvider_EmptyStream_ReturnsFailure()
    {
        var mockSettings = new MockSettingsService { StoredApiKey = "test_key" };
        var provider = new GeminiTranscriptionProvider(mockSettings);

        using var ms = new MemoryStream();
        var result = await provider.TranscribeAsync(ms, "audio/wav");

        Assert.False(result.Success);
        Assert.Contains("empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeminiTextProcessor_ModeOff_ReturnsOriginalText()
    {
        var mockSettings = new MockSettingsService { StoredApiKey = "test_key" };
        var processor = new GeminiTextProcessor(mockSettings);

        string original = "um hello this is a test";
        string result = await processor.ProcessTextAsync(original, TextCleanupMode.Off);

        Assert.Equal(original, result);
    }

    [Fact]
    public async Task GeminiTextProcessor_EmptyText_ReturnsEmpty()
    {
        var mockSettings = new MockSettingsService { StoredApiKey = "test_key" };
        var processor = new GeminiTextProcessor(mockSettings);

        string result = await processor.ProcessTextAsync("", TextCleanupMode.CleanTranscription);
        Assert.Equal("", result);
    }

    [Fact]
    public void KeyFormattingHelper_CustomKeys_FormatsAccurately()
    {
        var ctrlV = new HotkeyConfig(KeyModifiers.Control, Win32Constants.VK_KEY_V);
        Assert.Equal("Ctrl + V", KeyFormattingHelper.FormatHotkey(ctrlV));

        var winAltSpace = new HotkeyConfig(KeyModifiers.Windows | KeyModifiers.Alt, Win32Constants.VK_SPACE);
        Assert.Equal("Alt + Win + Space", KeyFormattingHelper.FormatHotkey(winAltSpace));
    }

    [Fact]
    public void TestSpeech_InitialState_IsReady()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(
            settings,
            audioRecorder,
            provider,
            processor,
            hotkey,
            clipboard,
            startup,
            stateManager);

        Assert.False(vm.IsRecordingTestSpeech);
        Assert.False(vm.IsTranscribingTestSpeech);
        Assert.False(vm.HasTestResult);
        Assert.Equal(string.Empty, vm.TestTranscriptionResult);
        Assert.Contains("Start Speaking", vm.TestSpeechStatus);
    }

    [Fact]
    public void IgnoreDisposeStream_PreventsUnderlyingStreamFromClosing()
    {
        var memStream = new MemoryStream();
        using (var nonClosing = new VoiceFlow.Helpers.IgnoreDisposeStream(memStream))
        using (var writer = new NAudio.Wave.WaveFileWriter(nonClosing, new NAudio.Wave.WaveFormat(16000, 16, 1)))
        {
            byte[] dummyData = new byte[100];
            writer.Write(dummyData, 0, dummyData.Length);
            writer.Flush();
        }

        // Must still be open, seekable and readable without ObjectDisposedException
        Assert.True(memStream.CanRead);
        Assert.True(memStream.CanSeek);
        memStream.Position = 0;
        Assert.True(memStream.Length > 0);

        byte[] readBack = memStream.ToArray();
        Assert.True(readBack.Length > 100); // 100 bytes data + 44 bytes WAV RIFF header
    }

    [Fact]
    public void DebugView_Toggle_PopulatesDiagnosticsReport()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(
            settings,
            audioRecorder,
            provider,
            processor,
            hotkey,
            clipboard,
            startup,
            stateManager);

        Assert.False(vm.IsDebugViewVisible);
        vm.ToggleDebugViewCommand.Execute(null);

        Assert.True(vm.IsDebugViewVisible);
        Assert.False(string.IsNullOrEmpty(vm.DetailedErrorSummary));
        Assert.Contains("VOICEFLOW ERROR REPORT", vm.DetailedErrorSummary);
    }

    [Fact]
    public void MainWindowViewModel_DefaultModel_IsGemini35Transcribe()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(
            settings,
            audioRecorder,
            provider,
            processor,
            hotkey,
            clipboard,
            startup,
            stateManager);

        Assert.Equal("gemini-3.5-transcribe", vm.SelectedModel);
        Assert.Equal("gemini-3.5-transcribe", vm.CurrentModelText);
        Assert.Contains("gemini-3.5-transcribe", vm.AvailableModels);
    }

    [Fact]
    public void MainWindowViewModel_ModelSwitch_SyncsSettings()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(
            settings,
            audioRecorder,
            provider,
            processor,
            hotkey,
            clipboard,
            startup,
            stateManager);

        vm.SelectedModel = "gemini-3.8-flash";
        Assert.Equal("gemini-3.8-flash", vm.CurrentModelText);
        Assert.Equal("gemini-3.8-flash", settings.Settings.GeminiModel);
    }

    [Fact]
    public void Gemini35TranscribeResponse_ExtractsAudioTranscriptionText()
    {
        string json = @"
        {
          ""candidates"": [
            {
              ""content"": {
                ""parts"": [
                  {
                    ""audioTranscription"": {
                      ""text"": ""Hello world, testing voice typing with Gemini 3.5 transcribe.""
                    }
                  }
                ],
                ""role"": ""model""
              },
              ""finishReason"": ""STOP""
            }
          ]
        }";

        string text = GeminiTranscriptionProvider.ExtractTextFromGeminiResponse(json);
        Assert.Equal("Hello world, testing voice typing with Gemini 3.5 transcribe.", text);
    }

    [Fact]
    public void GeminiSilenceResponse_ReturnsEmpty()
    {
        string json = @"
        {
          ""candidates"": [
            {
              ""content"": {},
              ""finishReason"": ""STOP""
            }
          ]
        }";

        string text = GeminiTranscriptionProvider.ExtractTextFromGeminiResponse(json);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void GeminiTextResponse_ExtractsDirectText()
    {
        string json = @"
        {
          ""candidates"": [
            {
              ""content"": {
                ""parts"": [
                  {
                    ""text"": ""Direct speech transcript.""
                  }
                ]
              }
            }
          ]
        }";

        string text = GeminiTranscriptionProvider.ExtractTextFromGeminiResponse(json);
        Assert.Equal("Direct speech transcript.", text);
    }

    [Fact]
    public void Navigation_SwitchSections_UpdatesSelectionFlags()
    {
        var settings = new MockSettingsService { StoredApiKey = "test_key" };
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(settings, audioRecorder, provider, processor, hotkey, clipboard, startup, stateManager);

        Assert.Equal(AppSection.Home, vm.CurrentSection);
        Assert.True(vm.IsHomeSelected);
        Assert.False(vm.IsHistorySelected);
        Assert.False(vm.IsSettingsSelected);
        Assert.False(vm.IsAboutSelected);

        vm.NavigateToCommand.Execute("History");
        Assert.Equal(AppSection.History, vm.CurrentSection);
        Assert.False(vm.IsHomeSelected);
        Assert.True(vm.IsHistorySelected);

        vm.NavigateToCommand.Execute("Settings");
        Assert.Equal(AppSection.Settings, vm.CurrentSection);
        Assert.True(vm.IsSettingsSelected);

        vm.NavigateToCommand.Execute("About");
        Assert.Equal(AppSection.About, vm.CurrentSection);
        Assert.True(vm.IsAboutSelected);
    }

    [Fact]
    public void Settings_TwoWayRadioAndToggles_SyncsCorrectly()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(settings, audioRecorder, provider, processor, hotkey, clipboard, startup, stateManager);

        Assert.True(vm.IsHoldToTalk);
        Assert.False(vm.IsToggleToTalk);

        vm.IsToggleToTalk = true;
        Assert.True(vm.IsToggleToTalk);
        Assert.False(vm.IsHoldToTalk);
        Assert.Equal(HotkeyActivationMode.Toggle, settings.Settings.HotkeyMode);

        vm.SelectedCleanupModeString = "Smart formatting";
        Assert.Equal(TextCleanupMode.SmartFormatting, settings.Settings.CleanupMode);

        vm.AutoPaste = false;
        Assert.False(settings.Settings.AutoPaste);

        vm.PreserveClipboard = false;
        Assert.False(settings.Settings.PreserveClipboard);
    }

    [Fact]
    public void History_AddAndClear_ManagesItemsProperly()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new WindowsClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel();
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(settings, audioRecorder, provider, processor, hotkey, clipboard, startup, stateManager);

        Assert.False(vm.HasHistory);

        var item = new HistoryItem
        {
            FinalText = "This is a transcribed test sentence.",
            Timestamp = DateTime.Now
        };
        vm.RecentHistory.Add(item);

        Assert.True(vm.HasHistory);
        Assert.Equal("6 words", item.WordCount);
        Assert.Equal("TODAY", item.RelativeDate);

        vm.DeleteHistoryItemCommand.Execute(item);
        Assert.False(vm.HasHistory);
    }

    [Fact]
    public void KeyFormattingHelper_ExtendedKeys_FormatsProperly()
    {
        Assert.Equal("Enter", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_RETURN));
        Assert.Equal("Tab", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_TAB));
        Assert.Equal("Backspace", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_BACK));
        Assert.Equal("Caps Lock", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_CAPITAL));
        Assert.Equal("Page Up", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_PRIOR));
        Assert.Equal("Page Down", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_NEXT));
        Assert.Equal("Home", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_HOME));
        Assert.Equal("End", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_END));
        Assert.Equal("Left", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_LEFT));
        Assert.Equal("Up", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_UP));
        Assert.Equal("Right", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_RIGHT));
        Assert.Equal("Down", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_DOWN));
        Assert.Equal("PrtScn", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_SNAPSHOT));
        Assert.Equal("Delete", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_DELETE));
        Assert.Equal(";", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_OEM_1));
        Assert.Equal("=", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_OEM_PLUS));
        Assert.Equal("`", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_OEM_3));
        Assert.Equal("[", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_OEM_4));
        Assert.Equal("]", KeyFormattingHelper.FormatVirtualKey(Win32Constants.VK_OEM_6));
        Assert.Equal("Num 5", KeyFormattingHelper.FormatVirtualKey(0x65));

        var combo = new HotkeyConfig(KeyModifiers.Control | KeyModifiers.Shift, 0x4B); // Ctrl + Shift + K
        Assert.Equal("Ctrl + Shift + K", KeyFormattingHelper.FormatHotkey(combo));

        var winCombo = new HotkeyConfig(KeyModifiers.Windows | KeyModifiers.Alt, Win32Constants.VK_SPACE);
        Assert.Equal("Alt + Win + Space", KeyFormattingHelper.FormatHotkey(winCombo));
    }

    [Fact]
    public async Task RecordingOverlayViewModel_PopupMode_DisplaysTextAndCopies()
    {
        var mockClipboard = new MockClipboardService();
        var vm = new RecordingOverlayViewModel(mockClipboard);

        Assert.False(vm.IsPopupMode);

        vm.ShowTranscriptionPopup("Dictated thought without text box focus");

        Assert.True(vm.IsPopupMode);
        Assert.True(vm.IsVisible);
        Assert.Equal("Dictated thought without text box focus", vm.TranscriptionText);
        Assert.Equal("6 words", vm.WordCountText);
        Assert.False(vm.IsCopied);
        Assert.Equal("Copy", vm.CopyButtonText);

        await vm.CopyTranscriptionCommand.ExecuteAsync(null);

        Assert.True(vm.IsCopied);
        Assert.Equal("Copied! ✓", vm.CopyButtonText);
        Assert.Equal("Dictated thought without text box focus", mockClipboard.CurrentText);

        vm.DismissPopup();
        Assert.False(vm.IsPopupMode);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void WindowsTextInjectionService_ZeroHwnd_ReturnsFalse()
    {
        var mockClipboard = new MockClipboardService();
        var service = new WindowsTextInjectionService(mockClipboard);

        bool result = service.HasFocusedEditableControl(IntPtr.Zero);
        Assert.False(result);
    }

    [Fact]
    public void MainWindowViewModel_ShortcutRecording_StartsCancelsAndResets()
    {
        var settings = new MockSettingsService();
        var audioRecorder = new AudioRecorder();
        var provider = new GeminiTranscriptionProvider(settings);
        var processor = new GeminiTextProcessor(settings);
        var hotkey = new WindowsHotkeyService();
        var clipboard = new MockClipboardService();
        var startup = new WindowsStartupService();
        var overlay = new RecordingOverlayViewModel(clipboard);
        var textInjection = new WindowsTextInjectionService(clipboard);
        var stateManager = new AppStateManager(settings, audioRecorder, provider, processor, hotkey, textInjection, clipboard, overlay);

        var vm = new MainWindowViewModel(settings, audioRecorder, provider, processor, hotkey, clipboard, startup, stateManager);

        Assert.False(vm.IsRecordingShortcut);

        // Start Recording
        vm.StartRecordingShortcutCommand.Execute(null);
        Assert.True(vm.IsRecordingShortcut);

        // Cancel Recording
        vm.CancelRecordingShortcutCommand.Execute(null);
        Assert.False(vm.IsRecordingShortcut);

        // Reset to Default
        vm.ResetDefaultShortcutCommand.Execute(null);
        Assert.False(vm.IsRecordingShortcut);
        Assert.Equal("Ctrl + Space", vm.CurrentShortcutText);
        Assert.Equal("Ctrl", vm.ShortcutModifierKeyText);
        Assert.Equal("Space", vm.ShortcutMainKeyText);
    }
}

public class MockClipboardService : IClipboardService
{
    public string? CurrentText { get; set; }
    public Task<string?> GetTextAsync() => Task.FromResult(CurrentText);
    public Task SetTextAsync(string text)
    {
        CurrentText = text;
        return Task.CompletedTask;
    }
}

