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
