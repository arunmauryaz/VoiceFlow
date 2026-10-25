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
    private Stream? _recordingBuffer;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();
    private bool _isDisposed;
    private SpeechPhraseSegmenter? _segmenter;

    public bool IsRecording { get; private set; }
    public bool HasInputDevices
    {
        get
        {
            try
            {
                return WaveInEvent.DeviceCount > 0;
            }
            catch
            {
                return false;
            }
        }
    }
    public TimeSpan RecordingDuration => _stopwatch.Elapsed;
    public int CurrentDeviceIndex { get; private set; } = -1;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingStopped;
    public event EventHandler<byte[]>? PhraseReady;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        int count = 0;
        try
        {
            count = WaveInEvent.DeviceCount;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to query audio device count: {ex.Message}");
        }

        if (count == 0)
        {
            devices.Add(new AudioDeviceInfo(-1, "No microphone detected", false));
            return devices;
        }

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

    public void StartRecording(int deviceIndex = -1, bool continuous = false)
    {
        lock (_lock)
        {
            if (IsRecording)
            {
                AppLogger.LogWarning("StartRecording called while already recording.");
                return;
            }

            if (!HasInputDevices)
            {
                AppLogger.LogWarning("Cannot start recording: No microphone connected.");
                throw new InvalidOperationException("No microphone connected");
            }

            CurrentDeviceIndex = deviceIndex;
            _recordingBuffer = continuous ? null : CreateTemporaryStream();
            _segmenter = continuous ? new SpeechPhraseSegmenter() : null;
            if (_segmenter != null) _segmenter.PhraseReady += (_, phrase) => PhraseReady?.Invoke(this, phrase);

            try
            {
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = deviceIndex >= 0 && deviceIndex < WaveInEvent.DeviceCount ? deviceIndex : -1,
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
        var tcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);

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

                Stream? completedAudio = null;
                Exception? failure = null;
                try
                {
                    _segmenter?.Flush();
                    completedAudio = _segmenter == null ? CreateWavStream() : new MemoryStream();
                    AppLogger.LogInfo($"Audio recording stopped. Duration: {_stopwatch.Elapsed.TotalSeconds:F1}s, Size: {completedAudio.Length} bytes.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to finalize WAV audio stream.", ex);
                    failure = ex;
                }
                finally
                {
                    // Release the old device before a waiting shortcut can start a new one.
                    try
                    {
                        Cleanup();
                        RecordingStopped?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        failure ??= ex;
                        AppLogger.LogError("Failed to release the recording device.", ex);
                    }
                }
                if (failure != null)
                {
                    completedAudio?.Dispose();
                    tcs.TrySetException(failure);
                }
                else tcs.TrySetResult(completedAudio!);
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
        if (!IsRecording) return;

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

        lock (_lock)
        {
            if (!IsRecording) return;
            _recordingBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
            _segmenter?.Append(e.Buffer, e.BytesRecorded, maxSample);
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

    private static FileStream CreateTemporaryStream()
        => new(Path.Combine(Path.GetTempPath(), "voiceflow-recording-" + Guid.NewGuid().ToString("N") + ".wav"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);

    private Stream CreateWavStream()
    {
        var output = CreateTemporaryStream();
        try
        {
            using (var nonClosing = new Helpers.IgnoreDisposeStream(output))
            using (var writer = new WaveFileWriter(nonClosing, new WaveFormat(16000, 16, 1)))
            {
                if (_recordingBuffer != null)
                {
                    _recordingBuffer.Position = 0;
                    _recordingBuffer.CopyTo(writer);
                }
            }
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private void Cleanup()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _segmenter = null;
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
