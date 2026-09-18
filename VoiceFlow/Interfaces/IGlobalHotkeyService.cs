using System;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface IGlobalHotkeyService : IDisposable
{
    bool IsRegistered { get; }
    bool IsPaused { get; set; }
    HotkeyConfig CurrentConfig { get; }
    HotkeyActivationMode CurrentMode { get; }

    event EventHandler? HotkeyPressed;
    event EventHandler? HotkeyReleased;

    bool RegisterHotkey(HotkeyConfig config, HotkeyActivationMode mode);
    void UnregisterHotkey();
}
