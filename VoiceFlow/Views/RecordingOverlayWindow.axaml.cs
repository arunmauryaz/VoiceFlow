using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VoiceFlow.Native;
using VoiceFlow.ViewModels;

namespace VoiceFlow.Views;

public partial class RecordingOverlayWindow : Window
{
    public RecordingOverlayWindow()
    {
        InitializeComponent();
    }

    public RecordingOverlayWindow(RecordingOverlayViewModel vm) : this()
    {
        DataContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordingOverlayViewModel.IsVisible))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is RecordingOverlayViewModel vm)
                {
                    if (vm.IsVisible)
                    {
                        UpdatePosition();
                        Show();
                    }
                    else
                    {
                        Hide();
                    }
                }
            });
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyNonActivatingStyle();
        UpdatePosition();
    }

    private void ApplyNonActivatingStyle()
    {
        var handle = TryGetPlatformHandle()?.Handle;
        if (handle != null && handle != IntPtr.Zero)
        {
            IntPtr currentExStyle = Win32PInvoke.GetWindowLongPtr(handle.Value, Win32Constants.GWL_EXSTYLE);
            long newExStyle = currentExStyle.ToInt64() |
                              Win32Constants.WS_EX_NOACTIVATE |
                              Win32Constants.WS_EX_TOOLWINDOW |
                              Win32Constants.WS_EX_TOPMOST;
            Win32PInvoke.SetWindowLongPtr(handle.Value, Win32Constants.GWL_EXSTYLE, new IntPtr(newExStyle));
        }
    }

    private void UpdatePosition()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen == null) return;

        double width = Bounds.Width > 0 ? Bounds.Width : 220;
        double height = Bounds.Height > 0 ? Bounds.Height : 50;

        int x = (int)(screen.WorkingArea.X + (screen.WorkingArea.Width - width) / 2);
        int y = (int)(screen.WorkingArea.Y + screen.WorkingArea.Height - height - 70);

        Position = new PixelPoint(x, y);
    }
}
