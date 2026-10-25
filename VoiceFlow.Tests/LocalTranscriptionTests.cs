using System.Net;
using System.Security.Cryptography;
using VoiceFlow.Interfaces;
using VoiceFlow.Models;
using VoiceFlow.Providers;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Providers.Local;
using VoiceFlow.Services;

namespace VoiceFlow.Tests;

public class LocalTranscriptionTests
{
    private sealed class Provider : ITranscriptionProvider
    {
        public int Calls;
        public bool Fail;
        public string ProviderName => "fake";
        public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default)
        { Calls++; return Task.FromResult(Fail ? TranscriptionResult.Fail("Missing model") : TranscriptionResult.Ok("Hello", TimeSpan.Zero)); }
        public Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default) => Task.FromResult((true, "Ready"));
    }

    [Fact]
    public async Task LocalRoutingNeverFallsBackToCloudOnFailure()
    {
        var settings = new MockSettingsService();
        settings.Settings.ApiProvider = "Local";
        var cloud = new Provider();
        var local = new Provider { Fail = true };
        var router = new TranscriptionRouter(settings, cloud, local);
        Assert.False((await router.TranscribeAsync(Stream.Null, "audio/wav")).Success);
        Assert.Equal(0, cloud.Calls);
        Assert.Equal(1, local.Calls);
        settings.Settings.ApiProvider = "Gemini";
        Assert.True((await router.TranscribeAsync(Stream.Null, "audio/wav")).Success);
        Assert.Equal(1, cloud.Calls);
    }

    [Fact]
    public async Task CloudCleanupIsBypassedInLocalModeEvenWithApiKey()
    {
        var settings = new MockSettingsService { StoredApiKey = "must-not-be-sent" };
        settings.Settings.ApiProvider = "Local";
        var processor = new GeminiTextProcessor(settings);
        Assert.Equal("um, private text", await processor.ProcessTextAsync("um, private text", TextCleanupMode.SmartFormatting));
    }

    private sealed class DownloadHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadIsVerifiedBeforeReplacingExistingFile(bool corrupt)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var model = new LocalModelInfo("test", "test", "test", "test.bin", 4,
            corrupt ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes)), "https://example.invalid/model");
        string root = Path.Combine(Path.GetTempPath(), "voiceflow-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var client = new HttpClient(new DownloadHandler(bytes));
            var service = new LocalModelService(root, client);
            await File.WriteAllTextAsync(service.ModelPath(model), "old");
            if (corrupt)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(model, null, CancellationToken.None));
                Assert.Equal("old", await File.ReadAllTextAsync(service.ModelPath(model)));
            }
            else
            {
                await service.DownloadAsync(model, null, CancellationToken.None);
                Assert.True(service.IsInstalled(model));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(service.ModelPath(model)));
            }
            Assert.False(File.Exists(service.ModelPath(model) + ".partial"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExplicitlyDeletedModelIsNotSilentlyDownloadedAgain()
    {
        string root = Path.Combine(Path.GetTempPath(), "voiceflow-absent-" + Guid.NewGuid());
        var settings = new MockSettingsService();
        settings.Settings.AutoPrepareLocalModel = false;
        using var bark = new BarkRuntimeService(Path.Combine(root, "runtime"), root);
        using var provider = new LocalTranscriptionProvider(settings, new LocalModelService(root), bark);
        var result = await provider.TranscribeAsync(Stream.Null, "audio/wav");
        Assert.False(result.Success);
        Assert.Contains("Download", result.ErrorMessage);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("Default", false, "CPU")]
    [InlineData("Default", true, "GPU")]
    [InlineData("CPU", false, "CPU")]
    [InlineData("CPU", true, "CPU")]
    [InlineData("GPU", true, "GPU")]
    public void DevicePreferenceResolvesWithoutChangingUserSelection(string preference, bool available, string expected)
        => Assert.Equal(expected, ComputeDeviceService.Resolve(preference, false, available));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitGpuRequestExplainsIncompatibleHardware(bool bark)
        => Assert.Throws<InvalidOperationException>(() => ComputeDeviceService.Resolve("GPU", bark, false));

    [Fact]
    public async Task DeleteRemovesOnlyTheSelectedModelAndItsPartialDownload()
    {
        string root = Path.Combine(Path.GetTempPath(), "voiceflow-delete-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var service = new LocalModelService(root);
            var selected = LocalModels.Get("whisper-tiny");
            string other = service.ModelPath(LocalModels.Get("whisper-base"));
            await File.WriteAllTextAsync(service.ModelPath(selected), "model");
            await File.WriteAllTextAsync(service.ModelPath(selected) + ".partial", "partial");
            await File.WriteAllTextAsync(other, "other-model");
            await service.DeleteAsync(selected, CancellationToken.None);
            Assert.False(File.Exists(service.ModelPath(selected)));
            Assert.False(File.Exists(service.ModelPath(selected) + ".partial"));
            Assert.Equal("other-model", await File.ReadAllTextAsync(other));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExistingFileWithWrongHashIsDetectedForAutomaticRepair()
    {
        string root = Path.Combine(Path.GetTempPath(), "voiceflow-hash-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var model = new LocalModelInfo("test", "Test", "", "test.bin", 4, new string('0', 64), "https://example.invalid");
            var service = new LocalModelService(root);
            await File.WriteAllBytesAsync(service.ModelPath(model), new byte[] { 1, 2, 3, 4 });
            Assert.True(service.IsInstalled(model));
            Assert.False(await service.VerifyAsync(model, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TemporaryAudioCleanupToleratesABriefWorkerFileLock()
    {
        string path = Path.Combine(Path.GetTempPath(), "voiceflow-lock-" + Guid.NewGuid() + ".wav");
        var audio = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var cleanup = LocalWorkerClient.DeleteTemporaryAudioAsync(path);
            await Task.Delay(70);
            await audio.DisposeAsync();
            await cleanup;
            Assert.False(File.Exists(path));
        }
        finally { await audio.DisposeAsync(); File.Delete(path); }
    }

    [Fact]
    public void CatalogContainsOnlyPinnedVerifiedModels()
    {
        Assert.Equal(4, LocalModels.All.Count);
        foreach (var model in LocalModels.All)
        {
            Assert.Equal(64, model.Sha256.Length);
            Assert.StartsWith("https://huggingface.co/", model.Url);
            Assert.DoesNotContain("/main/", model.Url);
            Assert.True(model.Size > 1_000_000);
        }
        Assert.True(LocalModels.Get("bark-clean").IsBark);
        Assert.Throws<ArgumentException>(() => LocalModels.Get("../unknown"));
    }
}
