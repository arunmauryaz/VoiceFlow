using System;
using System.Threading.Tasks;

namespace VoiceFlow.Interfaces;

public interface ITextInjectionService
{
    IntPtr GetForegroundWindowHandle();
    bool HasFocusedEditableControl(IntPtr targetHwnd);
    string? GetFocusedControlIdentity() => null;
    Task<bool> InjectTextAsync(string text, IntPtr targetHwnd, bool preserveClipboard = true);
    Task<bool> InjectPartialTextAsync(string text, IntPtr targetHwnd, bool preserveClipboard = true)
        => InjectTextAsync(text, targetHwnd, preserveClipboard);
}
