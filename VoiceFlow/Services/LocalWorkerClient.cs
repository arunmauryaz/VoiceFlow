using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceFlow.Services;

public sealed class LocalWorkerClient : IDisposable
{
    private Process? _process;
    private string? _identity;
    private readonly StringBuilder _diagnostics = new();
    public static ProcessStartInfo StartInfo(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        // Prevent native math libraries from taking every core during imports/model loading.
        string threads = LocalWorkerResources.ThreadCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (string variable in new[] { "OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS", "NUMEXPR_NUM_THREADS" })
            info.Environment[variable] = threads;
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    // The owning provider serializes all preparation, inference, and deletion.
    public async Task EnsureReadyAsync(string identity, ProcessStartInfo start, CancellationToken ct)
    {
        if (_identity == identity && _process is { HasExited: false }) return;
        Stop();
        _diagnostics.Clear();
        try
        {
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the local model.");
            ChildProcessLifetime.Attach(_process);
            try { _process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { AppLogger.LogWarning("Worker priority: " + ex.Message); }
            _process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                lock (_diagnostics)
                {
                    if (_diagnostics.Length > 2000) _diagnostics.Clear();
                    _diagnostics.AppendLine(e.Data);
                }
            };
            _process.BeginErrorReadLine();
            using var ready = await ReadAsync(ct);
            if (!ready.RootElement.TryGetProperty("ready", out _)) throw new InvalidDataException("Model did not finish loading.");
            _identity = identity;
        }
        catch { Stop(); throw; }
    }

    public async Task<string> TranscribeAsync(Stream audio, string language, CancellationToken ct)
    {
        string wav = Path.Combine(Path.GetTempPath(), "voiceflow-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await using (var file = File.Create(wav))
            {
                if (audio.CanSeek) audio.Position = 0;
                await audio.CopyToAsync(file, ct);
            }
            if (_process == null || _process.HasExited) throw new InvalidOperationException("Model stopped. Try again.");
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { audio = wav, language }));
            await _process.StandardInput.FlushAsync(ct);
            using var response = await ReadAsync(ct);
            return response.RootElement.GetProperty("text").GetString() ?? "";
        }
        catch { Stop(); throw; }
        finally { await DeleteTemporaryAudioAsync(wav); }
    }

    internal static async Task DeleteTemporaryAudioAsync(string wav)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { File.Delete(wav); return; }
            catch (IOException) { await Task.Delay(50); }
        }
        // Cleanup must not turn a completed transcription into a failed transcription.
        AppLogger.LogWarning("Temporary audio cleanup was delayed by another process.");
    }

    private async Task<JsonDocument> ReadAsync(CancellationToken ct)
    {
        while (await _process!.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith('{')) continue; // Native backends can print startup diagnostics.
            var result = JsonDocument.Parse(line);
            if (result.RootElement.TryGetProperty("error", out var error))
            {
                string message = error.GetString() ?? "Local model error";
                result.Dispose();
                throw new InvalidOperationException(message);
            }
            return result;
        }
        string details;
        lock (_diagnostics) details = _diagnostics.ToString();
        AppLogger.LogError("Local model worker exited. " + details);
        throw new InvalidOperationException("Model could not start. Try Repair model or select CPU.");
    }

    public void Stop()
    {
        if (_process == null) return;
        try { if (!_process.HasExited) { _process.Kill(true); _process.WaitForExit(3000); } }
        catch (InvalidOperationException) { }
        finally { _process.Dispose(); _process = null; _identity = null; }
    }
    public void Dispose() => Stop();
}
