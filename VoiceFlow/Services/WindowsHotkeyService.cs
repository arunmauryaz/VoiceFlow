using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Native;

namespace VoiceFlow.Services;

public class WindowsHotkeyService : IGlobalHotkeyService
{
    private IntPtr _hookId = IntPtr.Zero;
    private Win32PInvoke.LowLevelKeyboardProc? _proc;
    private HotkeyConfig _config = HotkeyConfig.Default;
    private HotkeyActivationMode _mode = HotkeyActivationMode.HoldToTalk;

    private readonly ShortcutStateMachine _state = new();
    private readonly System.Collections.Generic.HashSet<uint> _captureSuppressed = new();
    private bool _paused;
    private bool _isRecordingShortcut;
    private bool _isCapturingSingleKey;
    private uint _lastPressedModifierVk;
    private KeyModifiers _recordedModifiers;
    private bool _isDisposed;

    public bool IsRegistered => _hookId != IntPtr.Zero;
    public bool IsPaused
    {
        get => _paused;
        set { if (value && !_paused) HotkeyReleased?.Invoke(this, EventArgs.Empty); _paused = value; _state.ResetActivation(); }
    }
    public void ResetActivation() => _state.ResetActivation();

    public bool RegisterHotkeys(HotkeyConfig hold, HotkeyConfig toggle)
    {
        bool registered = RegisterHotkey(hold, HotkeyActivationMode.HoldToTalk);
        _state.Configure(hold, toggle);
        return registered;
    }
    public bool IsRecordingShortcut => _isRecordingShortcut;
    public bool IsCapturingSingleKey => _isCapturingSingleKey;
    public HotkeyConfig CurrentConfig => _config;
    public HotkeyActivationMode CurrentMode => _mode;

    public event EventHandler? EscapePressed;
    public event EventHandler? HotkeyPressed;
    public event EventHandler? HotkeyReleased;
    public event EventHandler<HotkeyConfig>? ShortcutRecorded;
    public event EventHandler<string>? ShortcutRecordingPreview;
    public event EventHandler? ShortcutRecordingCanceled;
    public event EventHandler<uint>? SingleKeyCaptured;

    public WindowsHotkeyService()
    {
        _proc = HookCallback;
    }

    public void StartRecordingShortcut()
    {
        _isRecordingShortcut = true;
        _isCapturingSingleKey = false;
        _lastPressedModifierVk = 0;
        _recordedModifiers = KeyModifiers.None;
        if (_hookId == IntPtr.Zero)
        {
            RegisterHotkey(_config, _mode);
        }
    }

    public void StopRecordingShortcut()
    {
        _isRecordingShortcut = false;
        _lastPressedModifierVk = 0;
        _recordedModifiers = KeyModifiers.None;
    }

    /// <summary>
    /// Start capturing a single keypress for a shortcut slot box.
    /// ANY key (including Ctrl, Win, Alt, Shift, F8, Space, etc.) is captured immediately on press.
    /// </summary>
    public void StartCapturingSingleKey()
    {
        _state.BeginCapture();
        _isCapturingSingleKey = true;
        _isRecordingShortcut = false;
        _lastPressedModifierVk = 0;
        _recordedModifiers = KeyModifiers.None;
        if (_hookId == IntPtr.Zero)
        {
            RegisterHotkey(_config, _mode);
        }
    }

    public void StopCapturingSingleKey()
    {
        _isCapturingSingleKey = false;
    }

    public bool RegisterHotkey(HotkeyConfig config, HotkeyActivationMode mode)
    {
        UnregisterHotkey();

        _config = config;
        _mode = mode;
        _state.Configure(mode == HotkeyActivationMode.HoldToTalk ? config : null, mode == HotkeyActivationMode.Toggle ? config : null);

        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr moduleHandle = Win32PInvoke.GetModuleHandle(curModule?.ModuleName);

            _hookId = Win32PInvoke.SetWindowsHookEx(
                Win32Constants.WH_KEYBOARD_LL,
                _proc!,
                moduleHandle,
                0);

            if (_hookId == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                AppLogger.LogError($"Failed to register low-level keyboard hook. Error code: {errorCode}");
                return false;
            }

            AppLogger.LogInfo($"Registered global hotkey [{_config}] in {_mode} mode.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Exception while installing keyboard hook.", ex);
            return false;
        }
    }

    public void UnregisterHotkey()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32PInvoke.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _state.ResetActivation();
            AppLogger.LogInfo("Unregistered global keyboard hook.");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbStruct.vkCode;

            bool isKeyDown = msg == Win32Constants.WM_KEYDOWN || msg == Win32Constants.WM_SYSKEYDOWN;
            bool isKeyUp = msg == Win32Constants.WM_KEYUP || msg == Win32Constants.WM_SYSKEYUP;

            // Ignore injected input (including VoiceFlow's own text insertion).
            if ((kbStruct.flags & 0x10) != 0)
                return Win32PInvoke.CallNextHookEx(_hookId, nCode, wParam, lParam);
            // Dismiss overlays globally, including while shortcuts are paused or being edited.
            // Continue normal Escape handling so the focused application still receives it.
            if (isKeyDown && vk == Win32Constants.VK_ESCAPE)
                EscapePressed?.Invoke(this, EventArgs.Empty);
            if (_captureSuppressed.Contains(vk))
            {
                if (isKeyUp) _captureSuppressed.Remove(vk);
                return (IntPtr)1;
            }

            // --- Slot-based single key capture (for the 3-box shortcut builder) ---
            if (_isCapturingSingleKey)
            {
                if (isKeyDown)
                {
                    _captureSuppressed.Add(vk);
                    if (vk == Win32Constants.VK_ESCAPE)
                    {
                        StopCapturingSingleKey();
                        ShortcutRecordingCanceled?.Invoke(this, EventArgs.Empty);
                        return (IntPtr)1;
                    }

                    // Capture any key – modifier keys (Ctrl, Alt, Shift, Win) or regular keys
                    StopCapturingSingleKey();
                    SingleKeyCaptured?.Invoke(this, vk);
                    return (IntPtr)1;
                }
                return (IntPtr)1; // swallow key-up events too while capturing
            }

            if (_isRecordingShortcut)
            {
                if (isKeyDown)
                {
                    if (vk == Win32Constants.VK_ESCAPE)
                    {
                        StopRecordingShortcut();
                        ShortcutRecordingCanceled?.Invoke(this, EventArgs.Empty);
                        return (IntPtr)1;
                    }

                    KeyModifiers currentMods = GetCurrentModifiers();

                    if (IsModifierKey(vk))
                    {
                        _lastPressedModifierVk = vk;
                        _recordedModifiers = currentMods;

                        string preview = Helpers.KeyFormattingHelper.FormatModifiers(currentMods);
                        if (!string.IsNullOrEmpty(preview)) preview += " + ...";
                        ShortcutRecordingPreview?.Invoke(this, preview);
                        return (IntPtr)1;
                    }

                    // Complete key combination captured (e.g. F6, Ctrl+Space, Alt+F8)!
                    var recorded = new HotkeyConfig(currentMods, vk);
                    StopRecordingShortcut();
                    ShortcutRecorded?.Invoke(this, recorded);
                    return (IntPtr)1;
                }
                else if (isKeyUp)
                {
                    // Check if a multi-modifier combo was pressed (e.g. user pressed Ctrl, then Alt, then released)
                    int modCount = 0;
                    if (_recordedModifiers.HasFlag(KeyModifiers.Control)) modCount++;
                    if (_recordedModifiers.HasFlag(KeyModifiers.Alt)) modCount++;
                    if (_recordedModifiers.HasFlag(KeyModifiers.Shift)) modCount++;
                    if (_recordedModifiers.HasFlag(KeyModifiers.Windows)) modCount++;

                    if (modCount >= 2 && _lastPressedModifierVk != 0)
                    {
                        KeyModifiers remainingMods = _recordedModifiers;
                        if (_lastPressedModifierVk is Win32Constants.VK_CONTROL or 0xA2 or 0xA3)
                            remainingMods &= ~KeyModifiers.Control;
                        else if (_lastPressedModifierVk is Win32Constants.VK_MENU or 0xA4 or 0xA5)
                            remainingMods &= ~KeyModifiers.Alt;
                        else if (_lastPressedModifierVk is Win32Constants.VK_SHIFT or 0xA0 or 0xA1)
                            remainingMods &= ~KeyModifiers.Shift;
                        else if (_lastPressedModifierVk is Win32Constants.VK_LWIN or Win32Constants.VK_RWIN)
                            remainingMods &= ~KeyModifiers.Windows;

                        var recorded = new HotkeyConfig(remainingMods, _lastPressedModifierVk);
                        StopRecordingShortcut();
                        ShortcutRecorded?.Invoke(this, recorded);
                    }
                    return (IntPtr)1;
                }
            }

            if (isKeyDown || isKeyUp)
            {
                var result = _state.Process(vk, isKeyDown, !IsPaused);
                if (!IsPaused)
                {
                    if (result.Action == 1) HotkeyPressed?.Invoke(this, EventArgs.Empty);
                    if (result.Action == -1) HotkeyReleased?.Invoke(this, EventArgs.Empty);
                }
                if (result.Suppress) return (IntPtr)1;
            }
        }

        return Win32PInvoke.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsKeyDown(int vKey) => (Win32PInvoke.GetAsyncKeyState(vKey) & 0x8000) != 0;
    private static bool IsCtrlDown() => IsKeyDown(Win32Constants.VK_CONTROL) || IsKeyDown(Win32Constants.VK_LCONTROL) || IsKeyDown(Win32Constants.VK_RCONTROL);
    private static bool IsAltDown() => IsKeyDown(Win32Constants.VK_MENU) || IsKeyDown(Win32Constants.VK_LMENU) || IsKeyDown(Win32Constants.VK_RMENU);
    private static bool IsShiftDown() => IsKeyDown(Win32Constants.VK_SHIFT) || IsKeyDown(Win32Constants.VK_LSHIFT) || IsKeyDown(Win32Constants.VK_RSHIFT);
    private static bool IsWinDown() => IsKeyDown(Win32Constants.VK_LWIN) || IsKeyDown(Win32Constants.VK_RWIN);

    private static KeyModifiers GetCurrentModifiers()
    {
        KeyModifiers mods = KeyModifiers.None;
        if (IsCtrlDown()) mods |= KeyModifiers.Control;
        if (IsAltDown()) mods |= KeyModifiers.Alt;
        if (IsShiftDown()) mods |= KeyModifiers.Shift;
        if (IsWinDown()) mods |= KeyModifiers.Windows;
        return mods;
    }

    private static bool IsModifierKey(uint vk)
    {
        return vk is Win32Constants.VK_CONTROL or Win32Constants.VK_LCONTROL or Win32Constants.VK_RCONTROL
            or Win32Constants.VK_MENU or Win32Constants.VK_LMENU or Win32Constants.VK_RMENU
            or Win32Constants.VK_SHIFT or Win32Constants.VK_LSHIFT or Win32Constants.VK_RSHIFT
            or Win32Constants.VK_LWIN or Win32Constants.VK_RWIN;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        UnregisterHotkey();
        _proc = null;
    }
}
