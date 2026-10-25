using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VoiceFlow.Models;

namespace VoiceFlow.Services;

public sealed class LocalModelService
{
    private static readonly HttpClient DefaultClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    public string Root { get; }
    public LocalModelService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceFlow", "Models")) { }
    public LocalModelService(string root, HttpClient? client = null) => (Root, _client) = (root, client ?? DefaultClient);
    public string ModelPath(LocalModelInfo model) => Path.Combine(Root, model.FileName);
    public bool IsInstalled(LocalModelInfo model) => File.Exists(ModelPath(model)) && new FileInfo(ModelPath(model)).Length == model.Size;

    public async Task<bool> VerifyAsync(LocalModelInfo model, CancellationToken ct)
    {
        if (!IsInstalled(model)) return false;
        await using var file = File.OpenRead(ModelPath(model));
        string sha = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        return sha.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task DeleteAsync(LocalModelInfo model, CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct);
        try
        {
            DeleteDecodedCache(model);
            File.Delete(ModelPath(model));
            File.Delete(ModelPath(model) + ".partial");
        }
        finally { _downloadLock.Release(); }
    }

    public async Task DownloadAsync(LocalModelInfo model, IProgress<double>? progress, CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct);
        string partial = ModelPath(model) + ".partial";
        try
        {
            Directory.CreateDirectory(Root);
            DeleteDecodedCache(model);
            using var response = await _client.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[131072];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > model.Size) throw new InvalidDataException("Model download is larger than expected.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress?.Report(total * 100.0 / model.Size);
                }
                ValidateDownload(total, Convert.ToHexString(hash.GetHashAndReset()), model);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(partial, ModelPath(model), true);
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            finally { _downloadLock.Release(); }
        }
    }

    private void DeleteDecodedCache(LocalModelInfo model)
    {
        string cache = ModelPath(model) + ".voiceflow-cache-v1.pt";
        File.Delete(cache);
        if (Directory.Exists(Root))
            foreach (string partial in Directory.EnumerateFiles(Root, Path.GetFileName(cache) + ".*.partial"))
                File.Delete(partial);
    }

    internal static void ValidateDownload(long size, string sha, LocalModelInfo model)
    {
        if (size != model.Size || !sha.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model verification failed. Download it again.");
    }
}
