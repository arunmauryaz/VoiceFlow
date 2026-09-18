using System;

namespace VoiceFlow.Models;

public enum AppSection
{
    Home,
    History,
    Settings,
    About
}

public enum AppState
{
    Ready,
    Recording,
    Transcribing,
    Cleaning,
    Pasting,
    Success,
    Error,
    Paused
}

public enum HotkeyActivationMode
{
    HoldToTalk,
    Toggle
}

public enum TextCleanupMode
{
    Off,
    CleanTranscription,
    SmartFormatting
}

public enum ThemePreference
{
    System,
    Dark,
    Light
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}
