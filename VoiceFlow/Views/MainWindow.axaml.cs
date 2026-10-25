using System;
using System.ComponentModel;
using Avalonia.Controls;
using VoiceFlow.Interfaces;
using VoiceFlow.ViewModels;

namespace VoiceFlow.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService? _settingsService;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel vm, ISettingsService settingsService) : this()
    {
        DataContext = vm;
        _settingsService = settingsService;
        Deactivated += (_, _) =>
        {
            if (vm.IsRecordingAnySlot) vm.CancelRecordingShortcutCommand.Execute(null);
        };
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isExplicitExit && _settingsService != null && _settingsService.Settings.RunInBackground)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}