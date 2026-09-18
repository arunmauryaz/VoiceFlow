using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;

namespace VoiceFlow.Services;

public class AudioRecorder : IAudioRecorder
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _recordingBuffer;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    public bool IsRecording { get; private set; }
    public TimeSpan RecordingDuration => _stopwatch.Elapsed;
    public int CurrentDeviceIndex { get; private set; } = -1;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingStopped;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        int count = WaveInEvent.DeviceCount;

        devices.Add(new AudioDeviceInfo(-1, "Default System Microphone", true));

        for (int i = 0; i < count; i++)
        {
            try
            {
                var caps = WaveInEvent.GetCapabilities(i);
                devices.Add(new AudioDeviceInfo(i, caps.ProductName, false));
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to query audio device {i}: {ex.Message}");
            }
        }

        return devices;
    }

    public void StartRecording(int deviceIndex = -1)
    {
        lock (_lock)
        {
            if (IsRecording)
            {
                AppLogger.LogWarning("StartRecording called while already recording.");
                return;
            }

            CurrentDeviceIndex = deviceIndex;
            _recordingBuffer = new MemoryStream();

            try
            {
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = deviceIndex >= 0 ? deviceIndex : 0,
                    // 16kHz, 16-bit Mono is optimal for speech recognition
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 50
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
                _stopwatch.Restart();
                IsRecording = true;

                AppLogger.LogInfo($"Audio recording started (Device: {deviceIndex}).");
                RecordingStarted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to start audio recording.", ex);
                Cleanup();
                throw;
            }
        }
    }

    public Task<Stream> StopRecordingAsync()
    {
        var tcs = new TaskCompletionSource<Stream>();

        lock (_lock)
        {
            if (!IsRecording || _waveIn == null)
            {
                tcs.SetResult(new MemoryStream());
                return tcs.Task;
            }

            IsRecording = false;
            _stopwatch.Stop();

            EventHandler<StoppedEventArgs>? handler = null;
            handler = (s, e) =>
            {
                if (_waveIn != null)
                {
                    _waveIn.RecordingStopped -= handler;
                }

                try
                {
                    var wavStream = CreateWavStream();
                    AppLogger.LogInfo($"Audio recording stopped. Duration: {_stopwatch.Elapsed.TotalSeconds:F1}s, Size: {wavStream.Length} bytes.");
                    tcs.TrySetResult(wavStream);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to finalize WAV audio stream.", ex);
                    tcs.TrySetException(ex);
                }
                finally
                {
                    Cleanup();
                    RecordingStopped?.Invoke(this, EventArgs.Empty);
                }
            };

            _waveIn.RecordingStopped += handler;

            try
            {
                _waveIn.StopRecording();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Exception during StopRecording.", ex);
                tcs.TrySetException(ex);
                Cleanup();
            }
        }

        return tcs.Task;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!IsRecording || _recordingBuffer == null) return;

        lock (_lock)
        {
            _recordingBuffer.Write(e.Buffer, 0, e.BytesRecorded);
        }

        // Calculate peak sample level for visual level meter
        float maxSample = 0f;
        for (int index = 0; index < e.BytesRecorded; index += 2)
        {
            short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);
            float sample32 = Math.Abs(sample / 32768f);
            if (sample32 > maxSample)
            {
                maxSample = sample32;
            }
        }

        // Raise audio level event for UI responsiveness (0.0 to 1.0)
        AudioLevelChanged?.Invoke(this, maxSample);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            AppLogger.LogError("Audio recording stopped with error.", e.Exception);
        }
    }

    private MemoryStream CreateWavStream()
    {
        var rawBytes = _recordingBuffer?.ToArray() ?? Array.Empty<byte>();
        var outputStream = new MemoryStream();

        var waveFormat = new WaveFormat(16000, 16, 1);
        using (var nonClosing = new VoiceFlow.Helpers.IgnoreDisposeStream(outputStream))
        using (var writer = new WaveFileWriter(nonClosing, waveFormat))
        {
            writer.Write(rawBytes, 0, rawBytes.Length);
            writer.Flush();
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private void Cleanup()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _recordingBuffer?.Dispose();
        _recordingBuffer = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        lock (_lock)
        {
            IsRecording = false;
            Cleanup();
        }
    }
}
