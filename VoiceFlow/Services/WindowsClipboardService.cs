using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;
using VoiceFlow.Native;

namespace VoiceFlow.Services;

public class WindowsClipboardService : IClipboardService
{
    private const int MaxRetries = 5;
    private const int RetryDelayMs = 25;

    public Task<string?> GetTextAsync()
    {
        return Task.Run(() =>
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                if (Win32PInvoke.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        IntPtr handle = Win32PInvoke.GetClipboardData(Win32Constants.CF_UNICODETEXT);
                        if (handle != IntPtr.Zero)
                        {
                            IntPtr pointer = Win32PInvoke.GlobalLock(handle);
                            if (pointer != IntPtr.Zero)
                            {
                                try
                                {
                                    return Marshal.PtrToStringUni(pointer);
                                }
                                finally
                                {
                                    Win32PInvoke.GlobalUnlock(handle);
                                }
                            }
                        }
                        return null;
                    }
                    finally
                    {
                        Win32PInvoke.CloseClipboard();
                    }
                }
                System.Threading.Thread.Sleep(RetryDelayMs);
            }
            return null;
        });
    }

    public Task SetTextAsync(string text)
    {
        return Task.Run(() =>
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                if (Win32PInvoke.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        Win32PInvoke.EmptyClipboard();

                        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                        IntPtr hMem = Win32PInvoke.GlobalAlloc(Win32PInvoke.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                        if (hMem != IntPtr.Zero)
                        {
                            IntPtr ptr = Win32PInvoke.GlobalLock(hMem);
                            if (ptr != IntPtr.Zero)
                            {
                                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                                Win32PInvoke.GlobalUnlock(hMem);
                                Win32PInvoke.SetClipboardData(Win32Constants.CF_UNICODETEXT, hMem);
                                return;
                            }
                        }
                    }
                    finally
                    {
                        Win32PInvoke.CloseClipboard();
                    }
                }
                System.Threading.Thread.Sleep(RetryDelayMs);
            }
            AppLogger.LogWarning("Failed to open clipboard for writing after retries.");
        });
    }
}
