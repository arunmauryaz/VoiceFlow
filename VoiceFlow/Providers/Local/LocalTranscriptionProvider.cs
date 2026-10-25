using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Services;

namespace VoiceFlow.Providers.Local;

public sealed record LocalPreparationStatus(string ModelId, string Text, double? Progress = null, bool Busy = true);

public sealed class LocalTranscriptionProvider : ITranscriptionProvider, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly LocalModelService _models;
    private readonly BarkRuntimeService _bark;
    private readonly WhisperRuntimeService _whisperRuntime;
    private readonly LocalWorkerClient _whisper = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _readySignature;
    private readonly Func<string, bool, string> _resolveDevice = ComputeDeviceService.Resolve;
    private readonly Func<LocalModelInfo, string, CancellationToken, Task>? _startWorker;
    internal LocalTranscriptionProvider(ISettingsService settings, LocalModelService models, BarkRuntimeService bark,
        Func<string, bool, string> resolveDevice, Func<LocalModelInfo, string, CancellationToken, Task> startWorker)
        : this(settings, models, bark) => (_resolveDevice, _startWorker) = (resolveDevice, startWorker);
    private readonly Timer _idleTimer;
    private long _lastUsed = Environment.TickCount64;
    private bool _disposed;
    public string ActiveDevice { get; private set; } = "CPU";
    public event EventHandler<LocalPreparationStatus>? PreparationChanged;
    public string ProviderName => "Local models";
    public bool IsPreparing { get; private set; }
    public LocalTranscriptionProvider(ISettingsService settings, LocalModelService models, BarkRuntimeService bark, WhisperRuntimeService? whisperRuntime = null)
    {
        (_settings, _models, _bark, _whisperRuntime) = (settings, models, bark, whisperRuntime ?? new WhisperRuntimeService());
        _idleTimer = new Timer(state => { _ = ReleaseIdleModelAsync(); }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    private async Task ReleaseIdleModelAsync()
    {
        if (_disposed || _settings.Settings.KeepModelLoaded || Environment.TickCount64 - Interlocked.Read(ref _lastUsed) < 900000 || !await _gate.WaitAsync(0)) return;
        try
        {
            if (_readySignature == null || _disposed) return;
            StopWorkers();
            Report(LocalModels.Get(_settings.Settings.LocalModelId), "Ready on demand", busy: false);
        }
        catch (Exception ex) { AppLogger.LogWarning("Idle model release: " + ex.Message); }
        finally { _gate.Release(); }
    }

    private void Report(LocalModelInfo model, string text, double? percent = null, bool busy = true)
    {
        IsPreparing = busy;
        PreparationChanged?.Invoke(this, new(model.Id, text, percent, busy));
    }
    private string Signature(LocalModelInfo model, string device) => model.Id + "|" + device + "|" + File.GetLastWriteTimeUtc(_models.ModelPath(model)).Ticks;
    private string? _fallbackSignature;
    private readonly System.Collections.Generic.Dictionary<string, string> _verifiedModels = new();

    public async Task PrepareAsync(LocalModelInfo model, string preference, bool repair, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await PrepareCoreAsync(model, preference, repair, ct); }
        catch (OperationCanceledException) { Report(model, "Setup paused", busy: false); throw; }
        catch (Exception ex) { AppLogger.LogError("Local model preparation failed", ex); Report(model, ex.Message, busy: false); throw; }
        finally { _gate.Release(); }
    }

    private async Task PrepareCoreAsync(LocalModelInfo model, string preference, bool repair, CancellationToken ct)
    {
        Interlocked.Exchange(ref _lastUsed, Environment.TickCount64);
        // Resolve before stopping anything: an unsupported selection must not discard a working model.
        string device = _resolveDevice(preference, model.IsBark);
        string requestedSignature = Signature(model, device);
        if (preference == "Default" && _fallbackSignature == requestedSignature) device = "CPU";
        if (!repair && _readySignature == Signature(model, device) && _models.IsInstalled(model))
        {
            // EnsureReady also detects a worker which has crashed since startup.
            await StartWorkerAsync(model, ActiveDevice, ct);
            Report(model, "Ready · " + ActiveDevice, busy: false);
            return;
        }
        StopWorkers();
        Report(model, "Checking model...");
        string fileSignature = Signature(model, "file");
        bool verified = _verifiedModels.TryGetValue(model.Id, out var known) && known == fileSignature && _models.IsInstalled(model);
        if (repair || (!verified && !await _models.VerifyAsync(model, ct)))
        {
            Report(model, "Downloading...");
            // Synchronous progress callback avoids delayed Ready -> Downloading regressions.
            await _models.DownloadAsync(model, new InlineProgress<double>(p => Report(model, $"Downloading · {p:0}%", p)), ct);
        }
        _verifiedModels[model.Id] = Signature(model, "file");
        if (model.IsBark)
        {
            try { await _bark.InstallAsync(device, repair, new InlineProgress<string>(s => Report(model, s)), ct); }
            catch (Exception ex) when (preference == "Default" && device == "GPU" && ex is not OperationCanceledException)
            {
                AppLogger.LogWarning("GPU setup unavailable; using CPU. " + ex.Message);
                device = "CPU";
                await _bark.InstallAsync(device, repair, new InlineProgress<string>(s => Report(model, s)), ct);
            }
        }
        else if (device == "GPU")
        {
            Report(model, "Setting up GPU...");
            try { await _whisperRuntime.EnsureGpuAsync(ct); }
            catch (Exception ex) when (preference == "Default" && ex is not OperationCanceledException)
            { AppLogger.LogWarning("GPU setup unavailable; using CPU. " + ex.Message); device = "CPU"; }
        }
        Report(model, "Loading model...");
        try { await StartWorkerAsync(model, device, ct); }
        catch (Exception ex) when (preference == "Default" && device == "GPU" && ex is not OperationCanceledException)
        {
            AppLogger.LogWarning("GPU initialization unavailable; using CPU. " + ex.Message);
            device = "CPU";
            if (model.IsBark) await _bark.InstallAsync(device, repair, new InlineProgress<string>(s => Report(model, s)), ct);
            await StartWorkerAsync(model, device, ct);
        }
        ActiveDevice = device;
        _readySignature = Signature(model, device);
        _fallbackSignature = preference == "Default" && requestedSignature != _readySignature ? requestedSignature : null;
        Report(model, "Ready · " + device, busy: false);
    }

    private async Task StartWorkerAsync(LocalModelInfo model, string device, CancellationToken ct)
    {
        if (_startWorker != null) { await _startWorker(model, device, ct); return; }
        if (model.IsBark)
        {
            await _bark.EnsureReadyAsync(_models.ModelPath(model), device, ct);
            return;
        }
        string executable = Path.Combine(AppContext.BaseDirectory, "VoiceFlow.exe");
        var info = File.Exists(executable)
            ? LocalWorkerClient.StartInfo(executable, "--whisper-worker", _models.ModelPath(model), device, _whisperRuntime.Root)
            : LocalWorkerClient.StartInfo("dotnet", typeof(Program).Assembly.Location, "--whisper-worker", _models.ModelPath(model), device, _whisperRuntime.Root);
        await _whisper.EnsureReadyAsync(model.Id + "|" + device, info, ct);
    }

    public async Task DeleteModelAsync(LocalModelInfo model, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            StopWorkers();
            _verifiedModels.Remove(model.Id);
            await _models.DeleteAsync(model, ct);
            Report(model, "Not downloaded", busy: false);
        }
        finally { _gate.Release(); }
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(45));
        var model = LocalModels.Get(_settings.Settings.LocalModelId);
        string preference = _settings.Settings.LocalComputeDevice;
        string language = _settings.Settings.LocalLanguage;
        if (!_settings.Settings.AutoPrepareLocalModel && !_models.IsInstalled(model))
            return TranscriptionResult.Fail("Model removed. Choose Download in Settings.");
        await _gate.WaitAsync(timeout.Token);
        try
        {
            var sw = Stopwatch.StartNew();
            await PrepareCoreAsync(model, preference, false, timeout.Token);
            string text = model.IsBark
                ? await _bark.TranscribeReadyAsync(audioStream, timeout.Token)
                : await _whisper.TranscribeAsync(audioStream, language, timeout.Token);
            var result = string.IsNullOrWhiteSpace(text) ? TranscriptionResult.Fail("No speech detected.") : TranscriptionResult.Ok(text, sw.Elapsed);
            result.ProcessedLocally = true;
            return result;
        }
        catch (OperationCanceledException) { StopWorkers(); Report(model, "Setup paused", busy: false); return TranscriptionResult.Fail("Transcription cancelled. Try again."); }
        catch (Exception ex)
        {
            StopWorkers();
            AppLogger.LogError("Local transcription failed", ex);
            Report(model, ex.Message, busy: false);
            return TranscriptionResult.Fail(ex.Message);
        }
        finally { Interlocked.Exchange(ref _lastUsed, Environment.TickCount64); _gate.Release(); }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await PrepareAsync(LocalModels.Get(_settings.Settings.LocalModelId), _settings.Settings.LocalComputeDevice, false, ct);
            return (true, "Ready · " + ActiveDevice);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
    public void StopWorkers() { _readySignature = null; _whisper.Stop(); _bark.StopWorker(); }
    public void Dispose() { if (_disposed) return; _disposed = true; _idleTimer.Dispose(); _whisper.Dispose(); _bark.StopWorker(); }
}

internal sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
