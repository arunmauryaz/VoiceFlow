using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;

namespace VoiceFlow.Services;

public class TrayService : ITrayService
{
    private readonly ISettingsService _settingsService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly Action _openSettingsAction;
    private readonly Action _exitAction;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusMenuItem;
    private NativeMenuItem? _pauseMenuItem;
    private bool _isDisposed;

    public TrayService(
        ISettingsService settingsService,
        IGlobalHotkeyService hotkeyService,
        Action openSettingsAction,
        Action exitAction)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _openSettingsAction = openSettingsAction;
        _exitAction = exitAction;
    }

    public void Initialize()
    {
        try
        {
            var menu = new NativeMenu();

            var titleItem = new NativeMenuItem("VoiceFlow") { IsEnabled = false };
            menu.Add(titleItem);

            _statusMenuItem = new NativeMenuItem("● Ready") { IsEnabled = false };
            menu.Add(_statusMenuItem);

            menu.Add(new NativeMenuItemSeparator());

            var openSettingsItem = new NativeMenuItem("Open Settings");
            openSettingsItem.Click += (s, e) => _openSettingsAction();
            menu.Add(openSettingsItem);

            _pauseMenuItem = new NativeMenuItem("Pause Hotkey");
            _pauseMenuItem.Click += (s, e) => TogglePause();
            menu.Add(_pauseMenuItem);

            menu.Add(new NativeMenuItemSeparator());

            var quitItem = new NativeMenuItem("Quit VoiceFlow");
            quitItem.Click += (s, e) => _exitAction();
            menu.Add(quitItem);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "VoiceFlow - Voice Typing",
                Menu = menu,
                IsVisible = true
            };

            // Set app icon if available
            try
            {
                var assets = AssetLoader.Open(new Uri("avares://VoiceFlow/Assets/app-icon.ico"));
                _trayIcon.Icon = new WindowIcon(assets);
            }
            catch
            {
                // Fallback to default if asset not yet bundled
            }

            _trayIcon.Clicked += (s, e) => _openSettingsAction();

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(Application.Current!, trayIcons);

            AppLogger.LogInfo("Tray service initialized successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to initialize system tray icon.", ex);
        }
    }

    public void UpdateStatus(string statusText)
    {
        if (_statusMenuItem != null)
        {
            _statusMenuItem.Header = statusText;
        }

        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = $"VoiceFlow: {statusText}";
        }
    }

    private void TogglePause()
    {
        _hotkeyService.IsPaused = !_hotkeyService.IsPaused;
        if (_pauseMenuItem != null)
        {
            _pauseMenuItem.Header = _hotkeyService.IsPaused ? "Resume Hotkey" : "Pause Hotkey";
        }
        UpdateStatus(_hotkeyService.IsPaused ? "● Paused" : "● Ready");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
