using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;
using VoiceFlow.Native;

namespace VoiceFlow.Services;

public class WindowsTextInjectionService : ITextInjectionService
{
    private readonly IClipboardService _clipboardService;

    public WindowsTextInjectionService(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public IntPtr GetForegroundWindowHandle()
    {
        return Win32PInvoke.GetForegroundWindow();
    }

    public string? GetFocusedControlIdentity()
    {
        try
        {
            var automation = new Interop.UIAutomationClient.CUIAutomationClass();
            var focused = automation.GetFocusedElement();
            try { return focused == null ? null : string.Join(".", focused.GetRuntimeId()); }
            finally
            {
                if (focused != null) Marshal.ReleaseComObject(focused);
                Marshal.ReleaseComObject(automation);
            }
        }
        catch { return null; }
    }

    public bool HasFocusedEditableControl(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero)
            return false;

        // 1. Fast check: Desktop or Taskbar/Shell is NOT an editable target
        var sb = new System.Text.StringBuilder(256);
        Win32PInvoke.GetClassName(targetHwnd, sb, 256);
        string className = sb.ToString();

        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "DV2ControlHost")
        {
            return false;
        }

        // 2. Win32 GUI Thread Caret check & standard Edit/RichEdit control
        uint threadId = Win32PInvoke.GetWindowThreadProcessId(targetHwnd, out _);
        if (threadId != 0)
        {
            var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (Win32PInvoke.GetGUIThreadInfo(threadId, ref gui))
            {
                if (gui.hwndCaret != IntPtr.Zero)
                    return true;

                if ((gui.flags & Win32Constants.GUI_CARETBLINKING) != 0)
                    return true;

                IntPtr focusedChild = gui.hwndFocus != IntPtr.Zero ? gui.hwndFocus : targetHwnd;
                var childSb = new System.Text.StringBuilder(256);
                Win32PInvoke.GetClassName(focusedChild, childSb, 256);
                string childClass = childSb.ToString().ToLowerInvariant();

                if (childClass.Contains("edit") ||
                    childClass.Contains("rich") ||
                    childClass.Contains("scintilla") ||
                    childClass.Contains("textbox") ||
                    childClass.Contains("terminal") ||
                    childClass.Contains("console"))
                {
                    return true;
                }
            }
        }

        // 3. UI Automation Check (Modern apps: Chromium, Electron, WPF, Avalonia, UWP, Office)
        try
        {
            var uia = new Interop.UIAutomationClient.CUIAutomationClass();
            var focused = uia.GetFocusedElement();
            if (focused != null)
            {
                int controlType = focused.CurrentControlType;
                // 50004 = UIA_EditControlTypeId, 50030 = UIA_DocumentControlTypeId, 50003 = UIA_ComboBoxControlTypeId
                if (controlType is 50004 or 50030 or 50003)
                {
                    return true;
                }

                var valPattern = focused.GetCurrentPattern(10002);
                if (valPattern is Interop.UIAutomationClient.IUIAutomationValuePattern vp)
                {
                    if (vp.CurrentIsReadOnly == 0)
                        return true;
                }

                var textEditPattern = focused.GetCurrentPattern(10024);
                if (textEditPattern != null)
                    return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"UI Automation check exception: {ex.Message}");
        }

        return false;
    }

    public async Task<bool> InjectTextAsync(string text, IntPtr targetHwnd, bool preserveClipboard = true)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        if (targetHwnd == IntPtr.Zero || Win32PInvoke.GetForegroundWindow() != targetHwnd) return false;

        try
        {
            string? originalClipboard = null;
            if (preserveClipboard)
            {
                originalClipboard = await _clipboardService.GetTextAsync();
            }

            await _clipboardService.SetTextAsync(text);

            if (targetHwnd != IntPtr.Zero)
            {
                if (Win32PInvoke.GetForegroundWindow() != targetHwnd)
                {
                    if (originalClipboard != null) await _clipboardService.SetTextAsync(originalClipboard);
                    return false;
                }
            }

            SendCtrlV();

            if (preserveClipboard && originalClipboard != null)
            {
                // Wait briefly for target application to finish consuming paste message
                await Task.Delay(250);
                await _clipboardService.SetTextAsync(originalClipboard);
            }

            AppLogger.LogInfo("Text successfully injected into target window.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to inject text into active application.", ex);
            return false;
        }
    }

    public Task<bool> InjectPartialTextAsync(string text, IntPtr targetHwnd, bool preserveClipboard = true)
    {
        if (string.IsNullOrEmpty(text) || targetHwnd == IntPtr.Zero || Win32PInvoke.GetForegroundWindow() != targetHwnd)
            return Task.FromResult(false);
        // Unicode entry avoids clipboard churn and works while a hold shortcut is down.
        var held = new System.Collections.Generic.List<ushort>();
        foreach (ushort key in new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
            if ((Win32PInvoke.GetAsyncKeyState(key) & 0x8000) != 0) held.Add(key);
        var inputs = BuildUnicodeInputs(text, held);
        uint sent = Win32PInvoke.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return Task.FromResult(sent == inputs.Length);
    }

    internal static INPUT[] BuildUnicodeInputs(string text, System.Collections.Generic.IReadOnlyList<ushort> held)
    {
        var inputs = new System.Collections.Generic.List<INPUT>();
        // A temporary Ctrl press prevents a released Win/Alt key from opening system menus.
        if (held.Count > 0) inputs.Add(CreateKeyInput(0xA2, false));
        foreach (ushort key in held) inputs.Add(CreateKeyInput(key, true));
        if (held.Count > 0) inputs.Add(CreateKeyInput(0xA2, true));
        foreach (char character in text)
        {
            inputs.Add(new INPUT { type = Win32Constants.INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = 0x0004 } } });
            inputs.Add(new INPUT { type = Win32Constants.INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = character, dwFlags = 0x0004 | Win32Constants.KEYEVENTF_KEYUP } } });
        }
        foreach (ushort key in held) inputs.Add(CreateKeyInput(key, false));
        return inputs.ToArray();
    }

    private static void RestoreFocusToWindow(IntPtr hWnd)
    {
        IntPtr currentForeground = Win32PInvoke.GetForegroundWindow();
        if (currentForeground == hWnd) return;

        uint targetThread = Win32PInvoke.GetWindowThreadProcessId(hWnd, out _);
        uint appThread = Win32PInvoke.GetCurrentThreadId();

        if (targetThread != appThread && targetThread != 0)
        {
            Win32PInvoke.AttachThreadInput(appThread, targetThread, true);
            Win32PInvoke.SetForegroundWindow(hWnd);
            Win32PInvoke.AttachThreadInput(appThread, targetThread, false);
        }
        else
        {
            Win32PInvoke.SetForegroundWindow(hWnd);
        }
    }

    private static void SendCtrlV()
    {
        INPUT[] inputs = new INPUT[4];

        // 1. Ctrl Down
        inputs[0] = CreateKeyInput(Win32Constants.VK_CONTROL, false);

        // 2. V Down
        inputs[1] = CreateKeyInput(Win32Constants.VK_KEY_V, false);

        // 3. V Up
        inputs[2] = CreateKeyInput(Win32Constants.VK_KEY_V, true);

        // 4. Ctrl Up
        inputs[3] = CreateKeyInput(Win32Constants.VK_CONTROL, true);

        uint sent = Win32PInvoke.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            AppLogger.LogWarning($"SendInput sent {sent} of {inputs.Length} inputs.");
        }
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = Win32Constants.INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? Win32Constants.KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }
}
