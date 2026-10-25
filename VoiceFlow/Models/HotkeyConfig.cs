using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using VoiceFlow.Native;

namespace VoiceFlow.Models;

public class HotkeyConfig
{
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.Control;
    public uint VirtualKey { get; set; } = Win32Constants.VK_SPACE;

    public uint[]? Keys { get; set; }

    public static uint Normalize(uint key) => key switch
    {
        0xA2 or 0xA3 => 0x11,
        0xA0 or 0xA1 => 0x10,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => key
    };

    [JsonIgnore]
    public uint[] EffectiveKeys
    {
        get
        {
            if (Keys != null) return Keys.Select(Normalize).Where(k => k != 0).Distinct().ToArray();
            var keys = new List<uint>();
            if (Modifiers.HasFlag(KeyModifiers.Control)) keys.Add(0x11);
            if (Modifiers.HasFlag(KeyModifiers.Alt)) keys.Add(0x12);
            if (Modifiers.HasFlag(KeyModifiers.Shift)) keys.Add(0x10);
            if (Modifiers.HasFlag(KeyModifiers.Windows)) keys.Add(0x5B);
            if (VirtualKey != 0) keys.Add(Normalize(VirtualKey));
            return keys.Distinct().ToArray();
        }
    }

    public static HotkeyConfig FromKeys(uint[] keys) => new(KeyModifiers.None, 0) { Keys = keys.Select(Normalize).ToArray() };
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
