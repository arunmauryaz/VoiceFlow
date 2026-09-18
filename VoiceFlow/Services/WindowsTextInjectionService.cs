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
                RestoreFocusToWindow(targetHwnd);
                await Task.Delay(60);
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
