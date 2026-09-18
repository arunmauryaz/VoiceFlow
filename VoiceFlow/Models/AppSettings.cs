namespace VoiceFlow.Models;

public class AppSettings
{
    // General
    public bool StartWithWindows { get; set; } = false;
    public bool RunInBackground { get; set; } = true;
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    // Audio
    public int SelectedAudioDeviceIndex { get; set; } = -1;
    public string SelectedAudioDeviceName { get; set; } = "Default Microphone";

    // Transcription API
    public string ApiProvider { get; set; } = "Gemini";
    public string GeminiModel { get; set; } = "gemini-3.5-transcribe";

    // AI Text Cleanup
    public bool CleanupEnabled { get; set; } = false;
    public TextCleanupMode CleanupMode { get; set; } = TextCleanupMode.CleanTranscription;

    // Shortcut
    public HotkeyConfig Hotkey { get; set; } = HotkeyConfig.Default;
    public HotkeyActivationMode HotkeyMode { get; set; } = HotkeyActivationMode.HoldToTalk;

    // Behavior
    public bool AutoPaste { get; set; } = true;
    public bool PreserveClipboard { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;

    // History (Default ON)
    public bool EnableHistory { get; set; } = true;
}
