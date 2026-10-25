using System;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface IGlobalHotkeyService : IDisposable
{
    bool IsRegistered { get; }
    bool IsPaused { get; set; }
    bool IsRecordingShortcut { get; }
    bool IsCapturingSingleKey { get; }
    HotkeyConfig CurrentConfig { get; }
    HotkeyActivationMode CurrentMode { get; }

    event EventHandler? HotkeyPressed;
    event EventHandler? HotkeyReleased;
    event EventHandler<HotkeyConfig>? ShortcutRecorded;
    event EventHandler<string>? ShortcutRecordingPreview;
    event EventHandler? ShortcutRecordingCanceled;
    event EventHandler<uint>? SingleKeyCaptured;

    bool RegisterHotkey(HotkeyConfig config, HotkeyActivationMode mode);
    bool RegisterHotkeys(HotkeyConfig hold, HotkeyConfig toggle);
    void ResetActivation();
    void UnregisterHotkey();
    void StartRecordingShortcut();
    void StopRecordingShortcut();
    void StartCapturingSingleKey();
    void StopCapturingSingleKey();
}
