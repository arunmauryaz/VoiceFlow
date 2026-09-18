using System;
using VoiceFlow.Models;

namespace VoiceFlow.Interfaces;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void SaveSettings();
    void LoadSettings();
    void SaveApiKey(string apiKey);
    string? LoadApiKey();
    bool HasApiKey { get; }
    event EventHandler? SettingsChanged;
}
