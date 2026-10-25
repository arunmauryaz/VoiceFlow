using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace VoiceFlow.Controls;

// A short, real microphone history flowing left to right. No idle animation thread.
public sealed class SpeechWaveform : Control
{
    public static readonly StyledProperty<float> LevelProperty =
        AvaloniaProperty.Register<SpeechWaveform, float>(nameof(Level));
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SpeechWaveform, bool>(nameof(IsActive));
    public float Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    private readonly double[] _history = new double[24];
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly Pen _pen = new(Brushes.White, 2, lineCap: PenLineCap.Round);
    private double _phase, _lastTime;
    private bool _attached;

    public SpeechWaveform()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnFrame;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateAnimation();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _timer.Stop();
        _clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) UpdateAnimation();
    }
    private void UpdateAnimation()
    {
        if (_attached && IsActive)
        {
            Array.Clear(_history);
            _phase = _lastTime = 0;
            _clock.Restart();
            _timer.Start();
        }
        else { _timer.Stop(); _clock.Stop(); }
        InvalidateVisual();
    }
    private void OnFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        _phase += Math.Min(now - _lastTime, .2) / .065;
        _lastTime = now;
        double energy = Math.Clamp(Math.Sqrt(Math.Max(0, Level)) * 2.8, 0, 1);
        while (_phase >= 1)
        {
            Array.Copy(_history, 0, _history, 1, _history.Length - 1);
            _history[0] = energy;
            _phase--;
        }
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double incoming = Math.Clamp(Math.Sqrt(Math.Max(0, Level)) * 2.8, 0, 1);
        double step = Bounds.Width / _history.Length;
        for (int i = 0; i < _history.Length; i++)
        {
            double previous = i == 0 ? incoming : _history[i - 1];
            double energy = _history[i] + (previous - _history[i]) * _phase;
            double halfHeight = 1.5 + energy * Math.Max(0, Bounds.Height / 2 - 3);
            double x = (i + .5) * step;
            using (context.PushOpacity(.35 + .65 * (1 - (double)i / _history.Length)))
                context.DrawLine(_pen, new Point(x, Bounds.Height / 2 - halfHeight), new Point(x, Bounds.Height / 2 + halfHeight));
        }
    }
}
