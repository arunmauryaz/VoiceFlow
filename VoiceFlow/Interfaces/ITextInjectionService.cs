using System;
using System.Threading.Tasks;

namespace VoiceFlow.Interfaces;

public interface ITextInjectionService
{
    IntPtr GetForegroundWindowHandle();
    bool HasFocusedEditableControl(IntPtr targetHwnd);
    Task<bool> InjectTextAsync(string text, IntPtr targetHwnd, bool preserveClipboard = true);
}
