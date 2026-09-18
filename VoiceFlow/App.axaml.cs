using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Services;
using VoiceFlow.ViewModels;
using VoiceFlow.Views;

namespace VoiceFlow;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private ITrayService? _trayService;
    private RecordingOverlayWindow? _overlayWindow;

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

            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Singletons
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<ITranscriptionProvider, GeminiTranscriptionProvider>();
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

        _trayService?.Dispose();
        _overlayWindow?.Close();
        _serviceProvider?.Dispose();
    }
}