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
    public string LocalModelId { get; set; } = "whisper-base";
    public string LocalLanguage { get; set; } = "auto";
    public string LocalComputeDevice { get; set; } = "Default";
    public bool AutoPrepareLocalModel { get; set; } = true;
    public string GeminiModel { get; set; } = "gemini-3.5-transcribe";

    // AI Text Cleanup
    public bool CleanupEnabled { get; set; } = false;
    public TextCleanupMode CleanupMode { get; set; } = TextCleanupMode.CleanTranscription;

    // Shortcut
    public HotkeyConfig Hotkey { get; set; } = HotkeyConfig.Default;
    public HotkeyActivationMode HotkeyMode { get; set; } = HotkeyActivationMode.HoldToTalk;

    public HotkeyConfig? HoldHotkey { get; set; }
    public HotkeyConfig? ToggleHotkey { get; set; }

    public void EnsureShortcuts()
    {
        HoldHotkey ??= HotkeyMode == HotkeyActivationMode.HoldToTalk ? Hotkey : HotkeyConfig.Default;
        ToggleHotkey ??= HotkeyMode == HotkeyActivationMode.Toggle ? Hotkey : new HotkeyConfig(KeyModifiers.None, 0x79);
        // Preserve the existing binding when it happens to match the new default.
        if (System.Linq.Enumerable.SequenceEqual(System.Linq.Enumerable.OrderBy(HoldHotkey.EffectiveKeys, k => k), System.Linq.Enumerable.OrderBy(ToggleHotkey.EffectiveKeys, k => k)))
        {
            if (HotkeyMode == HotkeyActivationMode.Toggle) HoldHotkey = HotkeyConfig.FromKeys(new uint[] { 0x11, 0x10, 0x5B });
            else ToggleHotkey = HotkeyConfig.FromKeys(new uint[] { 0x78 });
        }
    }

    // Behavior
    public bool AutoPaste { get; set; } = true;
    public bool PreserveClipboard { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public DictationOutputMode OutputMode { get; set; } = DictationOutputMode.InsertWhenFinished;
    public bool KeepModelLoaded { get; set; } = false;

    // History (Default ON)
    public bool EnableHistory { get; set; } = true;
}
