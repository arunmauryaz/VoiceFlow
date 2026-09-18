using System;
using System.IO;
using System.Text.Json;
using VoiceFlow.Helpers;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;

namespace VoiceFlow.Services;

public class SettingsService : ISettingsService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceFlow");
    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
    private static readonly string CredentialsFilePath = Path.Combine(AppDataFolder, "credentials.dat");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private AppSettings _settings = new();
    private string? _cachedApiKey;

    public AppSettings Settings => _settings;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_cachedApiKey ?? LoadApiKey());

    public event EventHandler? SettingsChanged;

    public SettingsService()
    {
        EnsureDirectoryExists();
        LoadSettings();
        _cachedApiKey = LoadApiKey();
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (loaded != null)
                {
                    _settings = loaded;
                    // Auto-migrate legacy/deprecated Gemini models to gemini-3.5-transcribe
                    if (string.IsNullOrWhiteSpace(_settings.GeminiModel) ||
                        _settings.GeminiModel.StartsWith("gemini-2.") ||
                        _settings.GeminiModel.StartsWith("gemini-1."))
                    {
                        _settings.GeminiModel = "gemini-3.5-transcribe";
                        SaveSettings();
                    }
                }
            }
            else
            {
                _settings = new AppSettings();
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load settings file, using defaults.", ex);
            _settings = new AppSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            EnsureDirectoryExists();
            string json = JsonSerializer.Serialize(_settings, JsonOpts);
            File.WriteAllText(SettingsFilePath, json);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            AppLogger.LogInfo("Settings saved successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save settings.", ex);
        }
    }

    public void SaveApiKey(string apiKey)
    {
        try
        {
            EnsureDirectoryExists();
            _cachedApiKey = apiKey?.Trim();
            if (string.IsNullOrEmpty(_cachedApiKey))
            {
                if (File.Exists(CredentialsFilePath))
                    File.Delete(CredentialsFilePath);
            }
            else
            {
                SecureStorageHelper.SaveSecureFile(CredentialsFilePath, _cachedApiKey);
            }
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            AppLogger.LogInfo("API key updated securely.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to securely save API key.", ex);
        }
    }

    public string? LoadApiKey()
    {
        try
        {
            if (_cachedApiKey != null)
                return _cachedApiKey;

            _cachedApiKey = SecureStorageHelper.LoadSecureFile(CredentialsFilePath);
            return _cachedApiKey;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load secure API key.", ex);
            return null;
        }
    }
}
