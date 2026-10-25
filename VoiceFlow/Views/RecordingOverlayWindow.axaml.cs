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
    private bool _closed;
    private RecordingOverlayViewModel? _vm;
    public RecordingOverlayWindow()
    {
        InitializeComponent();
    }

    public RecordingOverlayWindow(RecordingOverlayViewModel vm) : this()
    {
        DataContext = vm;
        _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            Dispatcher.UIThread.Post(UpdatePosition);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (e.PropertyName == nameof(RecordingOverlayViewModel.IsVisible))
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (!_closed && DataContext is RecordingOverlayViewModel vm)
                {
                    if (vm.IsVisible)
                    {
                        UpdatePosition();
                        var transitions = NotchCard.Transitions;
                        NotchCard.Transitions = null;
                        NotchCard.Opacity = 0;
                        Show();
                        NotchCard.Transitions = transitions;
                        Dispatcher.UIThread.Post(() => { if (!_closed) NotchCard.Opacity = 1; }, DispatcherPriority.Loaded);
                        Dispatcher.UIThread.Post(UpdatePosition, DispatcherPriority.Loaded);
                    }
                    else
                    {
                        NotchCard.Opacity = 0;
                        await System.Threading.Tasks.Task.Delay(170);
                        if (!_closed && !vm.IsVisible) Hide();
                    }
                }
            });
        }
        else if (e.PropertyName == nameof(RecordingOverlayViewModel.IsPopupMode))
        {
            Dispatcher.UIThread.Post(UpdatePosition, DispatcherPriority.Loaded);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        if (_vm != null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
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
        if (_closed) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen == null) return;

        double width = (Bounds.Width > 0 ? Bounds.Width : 240) * screen.Scaling;
        double height = (Bounds.Height > 0 ? Bounds.Height : 50) * screen.Scaling;

        int x = (int)(screen.WorkingArea.X + (screen.WorkingArea.Width - width) / 2);
        int y = (int)(screen.WorkingArea.Y + screen.WorkingArea.Height - height - 45 * screen.Scaling);

        Position = new PixelPoint(x, y);
    }
}
