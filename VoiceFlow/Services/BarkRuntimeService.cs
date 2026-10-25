using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceFlow.Services;

public sealed class BarkRuntimeService : IDisposable
{
    private readonly string _root, _assets;
    private readonly Func<string, string[], CancellationToken, Task> _runCommand;
    private readonly SemaphoreSlim _setupGate = new(1, 1);
    private readonly LocalWorkerClient _worker = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
    private string DeviceRoot(string device) => device == "GPU" ? _root + "-gpu" : _root;
    private string PythonFor(string device)
    {
        string root = DeviceRoot(device);
        string venv = Path.Combine(root, "Scripts", "python.exe");
        return File.Exists(venv) ? venv : Path.Combine(root, "python.exe");
    }
    private string Marker(string device) => Path.Combine(DeviceRoot(device), "voiceflow-runtime-v3.ready");
    public string PythonPath => PythonFor("CPU");
    public bool IsInstalled => IsInstalledFor("CPU");
    public bool IsInstalledFor(string device) => File.Exists(PythonFor(device)) && File.Exists(Marker(device));
    public BarkRuntimeService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceFlow", "BarkRuntime"), Path.Combine(AppContext.BaseDirectory, "LocalRuntime")) { }
    public BarkRuntimeService(string root, string assets) : this(root, assets, RunAsync) { }
    internal BarkRuntimeService(string root, string assets, Func<string, string[], CancellationToken, Task> runCommand)
        => (_root, _assets, _runCommand) = (root, assets, runCommand);

    public Task InstallAsync(IProgress<string>? progress, CancellationToken ct) => InstallAsync("CPU", false, progress, ct);
    public async Task InstallAsync(string device, bool repair, IProgress<string>? progress, CancellationToken ct)
    {
        await _setupGate.WaitAsync(ct);
        try
        {
            if (!repair && IsInstalledFor(device)) return;
            StopWorker();
            string root = DeviceRoot(device);
            Directory.CreateDirectory(root);
            File.Delete(Marker(device));
            if (!File.Exists(PythonFor(device)) || (File.Exists(Path.Combine(root, "python.exe")) && !File.Exists(Path.Combine(root, "embedded.ready"))))
            {
                progress?.Report("Setting up...");
                bool created = false;
                try
                {
                    await _runCommand("py", new[] { "-3.11", "-m", "venv", root }, ct);
                    created = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
                if (!created)
                {
                    try
                    {
                        await _runCommand("python", new[] { "-c", "import sys; assert sys.version_info[:2] == (3, 11)" }, ct);
                        await _runCommand("python", new[] { "-m", "venv", root }, ct);
                        created = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { }
                }
                if (!created) await InstallPrivatePythonAsync(root, ct);
            }
            string python = PythonFor(device);
            await _runCommand(python, new[] { "-m", "pip", "install", "--disable-pip-version-check", "--upgrade", "pip==25.3" }, ct);
            progress?.Report("Setting up " + device + "...");
            string suffix = device == "GPU" ? "cu126" : "cpu";
            await _runCommand(python, new[] { "-m", "pip", "install", "--disable-pip-version-check", "torch==2.10.0+" + suffix, "--extra-index-url", "https://download.pytorch.org/whl/" + suffix }, ct);
            await _runCommand(python, new[] { "-m", "pip", "install", "--disable-pip-version-check", "-r", Path.Combine(_assets, "requirements.txt") }, ct);
            // Exercise the audio feature extractor, not just imports, before marking setup complete.
            await _runCommand(python, new[] { "-c", "import sys; sys.path.insert(0, sys.argv[1]); import numpy as np; from transformers import ParakeetForTDT; from bark_loader import _processor; _processor()([np.zeros(16000, dtype=np.float32)], sampling_rate=16000, return_tensors='pt', padding=True)", _assets }, ct);
            await File.WriteAllTextAsync(Marker(device), "3", ct);
        }
        finally { _setupGate.Release(); }
    }

    private static ProcessStartInfo StartInfo(string executable, params string[] args)
    {
        var info = LocalWorkerClient.StartInfo(executable, args);
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["TRANSFORMERS_OFFLINE"] = "1";
        info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        return info;
    }

    private static async Task RunAsync(string executable, string[] args, CancellationToken ct)
    {
        using var process = Process.Start(StartInfo(executable, args)) ?? throw new InvalidOperationException("Setup could not start. Try again.");
        ChildProcessLifetime.Attach(process);
        using var cancel = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await output;
        string details = await error;
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            AppLogger.LogError("Local setup failed: " + details);
            throw new InvalidOperationException("Setup failed. Check your connection and try Repair model.");
        }
    }

    private static async Task DownloadCheckedAsync(string url, string destination, string sha, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (var output = File.Create(destination)) await response.Content.CopyToAsync(output, ct);
        await using var file = File.OpenRead(destination);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Setup download verification failed. Try again.");
    }

    private static async Task InstallPrivatePythonAsync(string root, CancellationToken ct)
    {
        string zip = Path.Combine(root, "python.partial");
        string bootstrap = Path.Combine(root, "get-pip.py");
        try
        {
            await DownloadCheckedAsync("https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip", zip,
                "009D6BF7E3B2DDCA3D784FA09F90FE54336D5B60F0E0F305C37F400BF83CFD3B", ct);
            ZipFile.ExtractToDirectory(zip, root, true);
            string pth = Path.Combine(root, "python311._pth");
            string contents = await File.ReadAllTextAsync(pth, ct);
            await File.WriteAllTextAsync(pth, contents.Replace("#import site", "import site"), ct);
            await DownloadCheckedAsync("https://raw.githubusercontent.com/pypa/get-pip/37ae3c4daaf63a0c203176251fb99c0e238562b5/public/get-pip.py", bootstrap,
                "F4448A01E3AB2914C65F4CA59D4CACBA6B2DBDE19A483FBAC46587F8BBAFF424", ct);
            await RunAsync(Path.Combine(root, "python.exe"), new[] { bootstrap, "pip==25.3", "--disable-pip-version-check" }, ct);
            await File.WriteAllTextAsync(Path.Combine(root, "embedded.ready"), "3.11.9", ct);
        }
        finally { File.Delete(zip); File.Delete(bootstrap); }
    }

    public async Task EnsureReadyAsync(string modelPath, string device, CancellationToken ct)
    {
        var info = StartInfo(PythonFor(device), "-u", Path.Combine(_assets, "bark_worker.py"), modelPath, device);
        await _worker.EnsureReadyAsync(modelPath + "|" + device, info, ct);
    }
    public async Task<string> TranscribeAsync(string modelPath, Stream audio, CancellationToken ct)
    {
        await InstallAsync(null, ct);
        await EnsureReadyAsync(modelPath, "CPU", ct);
        return await _worker.TranscribeAsync(audio, "en", ct);
    }
    public Task<string> TranscribeReadyAsync(Stream audio, CancellationToken ct) => _worker.TranscribeAsync(audio, "en", ct);
    public void StopWorker() => _worker.Stop();
    public void Dispose() => _worker.Dispose();
}
