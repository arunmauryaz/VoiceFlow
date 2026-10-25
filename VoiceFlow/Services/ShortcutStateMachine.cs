using System.Collections.Generic;
using System.Linq;
using VoiceFlow.Models;

namespace VoiceFlow.Services;

// Pure keyboard state logic, shared by the native hook and regression tests.
internal sealed class ShortcutStateMachine
{
    private readonly HashSet<uint> _down = new();
    private readonly HashSet<uint> _physicalDown = new();
    private readonly HashSet<uint> _suppressed = new();
    private uint[] _hold = System.Array.Empty<uint>();
    private uint[] _toggle = System.Array.Empty<uint>();
    private bool _holdLatched, _toggleLatched;
    private HotkeyActivationMode? _active;

    public void Configure(HotkeyConfig? hold, HotkeyConfig? toggle)
    {
        _hold = hold?.EffectiveKeys ?? System.Array.Empty<uint>();
        _toggle = toggle?.EffectiveKeys ?? System.Array.Empty<uint>();
        _physicalDown.Clear();
        _down.Clear();
        _suppressed.Clear();
        _holdLatched = _toggleLatched = false;
        ResetActivation();
    }

    public void ResetActivation() => _active = null;
    public void BeginCapture()
    {
        _physicalDown.Clear();
        _down.Clear();
        _holdLatched = _toggleLatched = false;
        ResetActivation();
    }

    private bool Matches(uint[] keys) => keys.Length > 0 && keys.All(_down.Contains)
        && !_down.Any(k => (k is 0x10 or 0x11 or 0x12 or 0x5B) && !keys.Contains(k));

    // Action: 1 starts recording, -1 stops it, 0 has no recording effect.
    public (int Action, bool Suppress) Process(uint physicalKey, bool down, bool enabled = true)
    {
        uint key = HotkeyConfig.Normalize(physicalKey);
        if (down && !_physicalDown.Add(physicalKey)) return (0, _suppressed.Contains(key));
        if (down) _down.Add(key);
        else
        {
            _physicalDown.Remove(physicalKey);
            if (!_physicalDown.Any(k => HotkeyConfig.Normalize(k) == key)) _down.Remove(key);
        }
        bool suppress = !down && _suppressed.Remove(key);
        int action = 0;
        if (!down && !_down.Contains(key) && _holdLatched && _hold.Contains(key))
        {
            _holdLatched = false;
            if (_active == HotkeyActivationMode.HoldToTalk) { _active = null; action = -1; }
        }
        if (!down && !_down.Contains(key) && _toggle.Contains(key)) _toggleLatched = false;
        if (enabled && down && !_holdLatched && _hold.Contains(key) && Matches(_hold))
        {
            _holdLatched = true;
            suppress = true;
            if (_active == null) { _active = HotkeyActivationMode.HoldToTalk; action = 1; }
        }
        if (enabled && down && !_toggleLatched && _toggle.Contains(key) && Matches(_toggle))
        {
            _toggleLatched = true;
            suppress = true;
            if (_active == HotkeyActivationMode.Toggle) { _active = null; action = -1; }
            else if (_active == null) { _active = HotkeyActivationMode.Toggle; action = 1; }
        }
        if (down && suppress) _suppressed.Add(key);
        return (action, suppress);
    }
}
