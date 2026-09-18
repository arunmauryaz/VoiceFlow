using VoiceFlow.Native;

namespace VoiceFlow.Models;

public class HotkeyConfig
{
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.Control;
    public uint VirtualKey { get; set; } = Win32Constants.VK_SPACE;

    public HotkeyConfig() { }

    public HotkeyConfig(KeyModifiers modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public static HotkeyConfig Default => new(KeyModifiers.Control, Win32Constants.VK_SPACE);

    public override string ToString()
    {
        return Helpers.KeyFormattingHelper.FormatHotkey(this);
    }
}
