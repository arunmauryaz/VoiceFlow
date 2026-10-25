using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Providers.Local;
using VoiceFlow.Providers;
using VoiceFlow.Services;
using VoiceFlow.ViewModels;
using VoiceFlow.Views;

namespace VoiceFlow;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ITrayService? _trayService;
    private RecordingOverlayWindow? _overlayWindow;
    private System.Threading.RegisteredWaitHandle? _activationWait;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            MainWindowViewModel.ApplyTheme(settingsService.Settings.Theme);

            // Create overlay window
            var overlayVm = _serviceProvider.GetRequiredService<RecordingOverlayViewModel>();
            _overlayWindow = new RecordingOverlayWindow(overlayVm);

            // Create main window
            var mainVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow(mainVm, settingsService);
            desktop.MainWindow = mainWindow;
            if (Program.ActivationRequested != null)
                _activationWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(Program.ActivationRequested,
                    (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(mainWindow.ShowAndActivate), null, -1, false);

            // Initialize hotkey and orchestrator
            var appStateManager = _serviceProvider.GetRequiredService<AppStateManager>();
            appStateManager.Initialize();

            // Initialize Tray Service
            var hotkeyService = _serviceProvider.GetRequiredService<IGlobalHotkeyService>();
            _trayService = new TrayService(
                settingsService,
                hotkeyService,
                openSettingsAction: () =>
                {
                    mainWindow.ShowAndActivate();
                },
                exitAction: () =>
                {
                    mainWindow.ExitApplication();
                    desktop.Shutdown();
                });
            _trayService.Initialize();

            // Check launch arguments
            bool startMinimized = desktop.Args != null &&
                                  desktop.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

            if (startMinimized && settingsService.Settings.RunInBackground)
            {
                // Start hidden in background/tray
                mainWindow.Hide();
            }
            else
            {
                mainWindow.Show();
            }

            if (desktop.Args != null && desktop.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            {
                mainVm.CurrentSection = AppSection.Settings;
            }

            if (desktop.Args != null && desktop.Args.Any(a => a.Equals("--test-popup", StringComparison.OrdinalIgnoreCase)))
            {
                overlayVm.ShowTranscriptionPopup("The user dictates while on desktop. VoiceFlow detects no active text field and displays this floating card with quick copy.");
            }

            desktop.Exit += (_, _) => mainVm.CancelLocalSetup();
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => await mainVm.InitializeLocalModelsAsync());
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Singletons
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<LocalModelService>();
        services.AddSingleton<BarkRuntimeService>();
        services.AddSingleton<WhisperRuntimeService>();
        services.AddSingleton<GeminiTranscriptionProvider>();
        services.AddSingleton<LocalTranscriptionProvider>();
        services.AddSingleton<ITranscriptionProvider, TranscriptionRouter>();
        services.AddSingleton<IAITextProcessor, GeminiTextProcessor>();
        services.AddSingleton<IGlobalHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<ITextInjectionService, WindowsTextInjectionService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();

        // Orchestrator and ViewModels
        services.AddSingleton<RecordingOverlayViewModel>();
        services.AddSingleton<AppStateManager>();
        services.AddSingleton<MainWindowViewModel>();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        AppLogger.LogInfo("VoiceFlow exiting cleanly.");
        _activationWait?.Unregister(null);

        _trayService?.Dispose();
        _overlayWindow?.Close();
        _serviceProvider?.Dispose();
    }
}
