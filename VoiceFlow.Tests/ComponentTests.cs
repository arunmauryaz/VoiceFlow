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
}
