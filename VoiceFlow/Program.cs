using Avalonia;
using System;

namespace VoiceFlow;

sealed class Program
{
    internal static System.Threading.EventWaitHandle? ActivationRequested;
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "--whisper-worker")
        {
            Services.WhisperWorker.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        using var instance = new System.Threading.Mutex(true, "Local\\VoiceFlow.Desktop", out bool firstInstance);
        if (!firstInstance)
        {
            try { using var activate = System.Threading.EventWaitHandle.OpenExisting("Local\\VoiceFlow.Activate"); activate.Set(); }
            catch (System.Threading.WaitHandleCannotBeOpenedException) { }
            return;
        }
        using var activation = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\VoiceFlow.Activate");
        ActivationRequested = activation;
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Services.AppLogger.LogError("FATAL UNHANDLED EXCEPTION in AppDomain", ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Services.AppLogger.LogError("FATAL UnobservedTaskException", e.Exception);
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
