using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceFlow.Services;

public sealed class WhisperRuntimeService
{
    public string Root { get; }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };
    public WhisperRuntimeService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceFlow", "WhisperRuntime")) { }
    public WhisperRuntimeService(string root) => Root = root;
    public async Task EnsureGpuAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string package = Path.Combine(Root, "vulkan.partial");
        try
        {
            string destination = Path.Combine(Root, "runtimes", "vulkan", "win-x64");
            string marker = Path.Combine(destination, "1.9.1.ready");
            if (File.Exists(marker) && File.Exists(Path.Combine(destination, "whisper.dll"))) return;
            Directory.CreateDirectory(Root);
            using var response = await Client.GetAsync("https://api.nuget.org/v3-flatcontainer/whisper.net.runtime.vulkan/1.9.1/whisper.net.runtime.vulkan.1.9.1.nupkg", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(package)) await response.Content.CopyToAsync(output, ct);
            await using (var file = File.OpenRead(package))
            {
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
                if (hash != "25FBEA771D8AAFFC4588A1D6AA1345A7BBC74912A8ADA895D1695BBACFC1135A")
                    throw new InvalidDataException("GPU download verification failed. Try again.");
            }
            Directory.CreateDirectory(destination);
            using var zip = ZipFile.OpenRead(package);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("build/win-x64/", StringComparison.Ordinal) && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                ct.ThrowIfCancellationRequested();
                entry.ExtractToFile(Path.Combine(destination, entry.Name), true);
            }
            await File.WriteAllTextAsync(marker, "1.9.1", ct);
        }
        finally
        {
            try { File.Delete(package); }
            finally { _gate.Release(); }
        }
    }
}
