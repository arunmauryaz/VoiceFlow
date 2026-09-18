using System;
using System.Diagnostics;
using Microsoft.Win32;
using VoiceFlow.Interfaces;

namespace VoiceFlow.Services;

public class WindowsStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyName = "VoiceFlow";

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key == null) return false;
            object? value = key.GetValue(AppKeyName);
            return value != null;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check Windows startup registry.", ex);
            return false;
        }
    }

    public bool SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppKeyName, $"\"{exePath}\"");
                    AppLogger.LogInfo("Windows startup enabled.");
                    return true;
                }
                return false;
            }
            else
            {
                key.DeleteValue(AppKeyName, false);
                AppLogger.LogInfo("Windows startup disabled.");
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to modify Windows startup registry (enable={enable}).", ex);
            return false;
        }
    }
}
