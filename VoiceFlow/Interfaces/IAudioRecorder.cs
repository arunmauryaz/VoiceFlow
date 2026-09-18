using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    TimeSpan RecordingDuration { get; }
    int CurrentDeviceIndex { get; }

    event EventHandler<float>? AudioLevelChanged;
    event EventHandler? RecordingStarted;
    event EventHandler? RecordingStopped;

    void StartRecording(int deviceIndex = -1);
    Task<Stream> StopRecordingAsync();
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
}
