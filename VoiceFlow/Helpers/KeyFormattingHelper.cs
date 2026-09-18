using System.Collections.Generic;
using System.Text;
using VoiceFlow.Models;
using VoiceFlow.Native;

namespace VoiceFlow.Helpers;

public static class KeyFormattingHelper
{
    public static string FormatHotkey(HotkeyConfig config)
    {
        var parts = new List<string>();

        if (config.Modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (config.Modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (config.Modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (config.Modifiers.HasFlag(KeyModifiers.Windows))
            parts.Add("Win");

        parts.Add(FormatVirtualKey(config.VirtualKey));

        return string.Join(" + ", parts);
    }

    public static string FormatVirtualKey(uint vk)
    {
        return vk switch
        {
            Win32Constants.VK_SPACE => "Space",
            Win32Constants.VK_ESCAPE => "Esc",
            Win32Constants.VK_F1 => "F1",
            Win32Constants.VK_F2 => "F2",
            Win32Constants.VK_F3 => "F3",
            Win32Constants.VK_F4 => "F4",
            Win32Constants.VK_F5 => "F5",
            Win32Constants.VK_F6 => "F6",
            Win32Constants.VK_F7 => "F7",
            Win32Constants.VK_F8 => "F8",
            Win32Constants.VK_F9 => "F9",
            Win32Constants.VK_F10 => "F10",
            Win32Constants.VK_F11 => "F11",
            Win32Constants.VK_F12 => "F12",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            _ => $"Key(0x{vk:X2})"
        };
    }
}
