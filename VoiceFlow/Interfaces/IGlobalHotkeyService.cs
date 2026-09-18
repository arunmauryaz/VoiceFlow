using System;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface IGlobalHotkeyService : IDisposable
{
    bool IsRegistered { get; }
    bool IsPaused { get; set; }
    bool IsRecordingShortcut { get; }
    HotkeyConfig CurrentConfig { get; }
    HotkeyActivationMode CurrentMode { get; }

    event EventHandler? HotkeyPressed;
    event EventHandler? HotkeyReleased;
    event EventHandler<HotkeyConfig>? ShortcutRecorded;
    event EventHandler<string>? ShortcutRecordingPreview;
    event EventHandler? ShortcutRecordingCanceled;

    bool RegisterHotkey(HotkeyConfig config, HotkeyActivationMode mode);
    void UnregisterHotkey();
    void StartRecordingShortcut();
    void StopRecordingShortcut();
}
