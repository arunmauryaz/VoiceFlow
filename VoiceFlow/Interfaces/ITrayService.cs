using System;

namespace VoiceFlow.Interfaces;

public interface ITrayService : IDisposable
{
    void Initialize();
    void UpdateStatus(string statusText);
}
