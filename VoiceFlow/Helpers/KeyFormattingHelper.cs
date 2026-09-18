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

    public static string FormatModifiers(KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public static string FormatVirtualKey(uint vk)
    {
        return vk switch
        {
            Win32Constants.VK_SPACE => "Space",
            Win32Constants.VK_ESCAPE => "Esc",
            Win32Constants.VK_RETURN => "Enter",
            Win32Constants.VK_TAB => "Tab",
            Win32Constants.VK_BACK => "Backspace",
            Win32Constants.VK_CAPITAL => "Caps Lock",
            Win32Constants.VK_PRIOR => "Page Up",
            Win32Constants.VK_NEXT => "Page Down",
            Win32Constants.VK_END => "End",
            Win32Constants.VK_HOME => "Home",
            Win32Constants.VK_LEFT => "Left",
            Win32Constants.VK_UP => "Up",
            Win32Constants.VK_RIGHT => "Right",
            Win32Constants.VK_DOWN => "Down",
            Win32Constants.VK_SNAPSHOT => "PrtScn",
            Win32Constants.VK_INSERT => "Insert",
            Win32Constants.VK_DELETE => "Delete",
            Win32Constants.VK_OEM_1 => ";",
            Win32Constants.VK_OEM_PLUS => "=",
            Win32Constants.VK_OEM_COMMA => ",",
            Win32Constants.VK_OEM_MINUS => "-",
            Win32Constants.VK_OEM_PERIOD => ".",
            Win32Constants.VK_OEM_2 => "/",
            Win32Constants.VK_OEM_3 => "`",
            Win32Constants.VK_OEM_4 => "[",
            Win32Constants.VK_OEM_5 => "\\",
            Win32Constants.VK_OEM_6 => "]",
            Win32Constants.VK_OEM_7 => "'",
            >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            _ => $"Key(0x{vk:X2})"
        };
    }

    public static HotkeyConfig? ParseHotkey(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var tokens = text.Split(new[] { "+", " " }, System.StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        KeyModifiers mods = KeyModifiers.None;
        uint vk = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].Trim();
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= KeyModifiers.Control;
                    break;
                case "alt":
                case "menu":
                    mods |= KeyModifiers.Alt;
                    break;
                case "shift":
                    mods |= KeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                case "meta":
                    mods |= KeyModifiers.Windows;
                    break;
                default:
                    vk = ParseVirtualKey(token);
                    break;
            }
        }

        if (vk == 0) return null;
        return new HotkeyConfig(mods, vk);
    }

    public static uint ParseVirtualKey(string key)
    {
        string upper = key.Trim().ToUpperInvariant();
        return upper switch
        {
            "SPACE" => Win32Constants.VK_SPACE,
            "ESC" or "ESCAPE" => Win32Constants.VK_ESCAPE,
            "ENTER" or "RETURN" => Win32Constants.VK_RETURN,
            "TAB" => Win32Constants.VK_TAB,
            "BACKSPACE" or "BACK" => Win32Constants.VK_BACK,
            "CAPS" or "CAPSLOCK" or "CAPS LOCK" => Win32Constants.VK_CAPITAL,
            "PAGEUP" or "PAGE UP" or "PGUP" => Win32Constants.VK_PRIOR,
            "PAGEDOWN" or "PAGE DOWN" or "PGDN" => Win32Constants.VK_NEXT,
            "HOME" => Win32Constants.VK_HOME,
            "END" => Win32Constants.VK_END,
            "LEFT" => Win32Constants.VK_LEFT,
            "UP" => Win32Constants.VK_UP,
            "RIGHT" => Win32Constants.VK_RIGHT,
            "DOWN" => Win32Constants.VK_DOWN,
            "PRTSCN" or "PRINTSCREEN" or "SNAPSHOT" => Win32Constants.VK_SNAPSHOT,
            "INSERT" or "INS" => Win32Constants.VK_INSERT,
            "DELETE" or "DEL" => Win32Constants.VK_DELETE,
            "PAUSE" => Win32Constants.VK_PAUSE,
            ";" => Win32Constants.VK_OEM_1,
            "=" or "+" => Win32Constants.VK_OEM_PLUS,
            "," or "<" => Win32Constants.VK_OEM_COMMA,
            "-" or "_" => Win32Constants.VK_OEM_MINUS,
            "." or ">" => Win32Constants.VK_OEM_PERIOD,
            "/" or "?" => Win32Constants.VK_OEM_2,
            "`" or "~" => Win32Constants.VK_OEM_3,
            "[" or "{" => Win32Constants.VK_OEM_4,
            "\\" or "|" => Win32Constants.VK_OEM_5,
            "]" or "}" => Win32Constants.VK_OEM_6,
            "'" or "\"" => Win32Constants.VK_OEM_7,
            "F1" => Win32Constants.VK_F1,
            "F2" => Win32Constants.VK_F2,
            "F3" => Win32Constants.VK_F3,
            "F4" => Win32Constants.VK_F4,
            "F5" => Win32Constants.VK_F5,
            "F6" => Win32Constants.VK_F6,
            "F7" => Win32Constants.VK_F7,
            "F8" => Win32Constants.VK_F8,
            "F9" => Win32Constants.VK_F9,
            "F10" => Win32Constants.VK_F10,
            "F11" => Win32Constants.VK_F11,
            "F12" => Win32Constants.VK_F12,
            _ when upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z' => (uint)upper[0],
            _ when upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9' => (uint)upper[0],
            _ => 0
        };
    }
}
