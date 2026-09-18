using System;
using System.Threading.Tasks;

namespace VoiceFlow.Interfaces;

public interface ITextInjectionService
{
    IntPtr GetForegroundWindowHandle();
    Task<bool> InjectTextAsync(string text, IntPtr targetHwnd, bool preserveClipboard = true);
}
