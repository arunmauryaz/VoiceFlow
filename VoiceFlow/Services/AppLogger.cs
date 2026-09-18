using System;
using System.Diagnostics;
using System.IO;

namespace VoiceFlow.Services;

public static class AppLogger
{
    private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceFlow");
    private static readonly string LogFile = Path.Combine(LogDir, "voiceflow.log");
    private static readonly object LockObj = new();

    static AppLogger()
    {
        try
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);
        }
        catch { }
    }

    public static void LogInfo(string message) => Write("INFO", message);
    public static void LogWarning(string message) => Write("WARN", message);
    public static void LogError(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} | Exception: {ex.Message}" : message;
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level}] {Sanitize(message)}";

        Debug.WriteLine(line);

        try
        {
            lock (LockObj)
            {
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Do not crash if logging fails
        }
    }

    private static string Sanitize(string message)
    {
        // Safety: ensure no raw key or auth string leaks
        if (message.Contains("key=", StringComparison.OrdinalIgnoreCase))
        {
            message = System.Text.RegularExpressions.Regex.Replace(
                message,
                @"key=[A-Za-z0-9_\-]+",
                "key=REDACTED",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return message;
    }
}
