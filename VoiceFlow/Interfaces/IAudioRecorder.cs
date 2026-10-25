using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    bool HasInputDevices { get; }
    TimeSpan RecordingDuration { get; }
    int CurrentDeviceIndex { get; }

    event EventHandler<float>? AudioLevelChanged;
    event EventHandler? RecordingStarted;
    event EventHandler? RecordingStopped;
    event EventHandler<byte[]>? PhraseReady;

    void StartRecording(int deviceIndex = -1, bool continuous = false);
    Task<Stream> StopRecordingAsync();
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
}
