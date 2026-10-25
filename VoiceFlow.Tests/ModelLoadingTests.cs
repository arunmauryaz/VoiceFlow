using System.Security.Cryptography;
using VoiceFlow.Models;
using VoiceFlow.Providers.Local;
using VoiceFlow.Services;

namespace VoiceFlow.Tests;

public class ModelLoadingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(32, 4)]
    public void WorkerThreadBudgetLeavesCapacityForDesktop(int processors, int expected)
        => Assert.Equal(expected, LocalWorkerResources.ThreadsFor(processors));

    [Fact]
    public async Task DeletingModelAlsoRemovesDecodedCacheAndInterruptedWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "voiceflow-cache-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var model = LocalModels.All.First(m => m.IsBark);
            var service = new LocalModelService(root);
            string file = service.ModelPath(model);
            await File.WriteAllTextAsync(file, "model");
            await File.WriteAllTextAsync(file + ".voiceflow-cache-v1.pt", "cache");
            await File.WriteAllTextAsync(file + ".voiceflow-cache-v1.pt.123.partial", "partial");
            await File.WriteAllTextAsync(Path.Combine(root, "other.bin"), "keep");
            await service.DeleteAsync(model, CancellationToken.None);
            Assert.Equal(new[] { "other.bin" }, Directory.GetFiles(root).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task EquivalentDevicesAndUnrelatedSettingsReuseResidentModel()
    {
        string root = Path.Combine(Path.GetTempPath(), "voiceflow-loading-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            byte[] bytes = [1, 2, 3, 4];
            var model = new LocalModelInfo("test", "Test", "Test", "test.bin", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)), "https://example.invalid/test");
            var models = new LocalModelService(root);
            await File.WriteAllBytesAsync(models.ModelPath(model), bytes);
            var settings = new MockSettingsService();
            using var bark = new BarkRuntimeService(Path.Combine(root, "runtime"), root);
            int workerChecks = 0;
            using var provider = new LocalTranscriptionProvider(settings, models, bark,
                (preference, _) => preference == "GPU" ? throw new InvalidOperationException("GPU unavailable") : "CPU",
                (_, _, _) => { workerChecks++; return Task.CompletedTask; });
            var statuses = new List<string>();
            provider.PreparationChanged += (_, status) => statuses.Add(status.Text);
            await provider.PrepareAsync(model, "Default", false, CancellationToken.None);
            Assert.Contains("Loading model...", statuses);
            statuses.Clear();
            settings.Settings.LocalLanguage = "hi";
            settings.Settings.KeepModelLoaded = true;
            await provider.PrepareAsync(model, "CPU", false, CancellationToken.None);
            Assert.Equal(new[] { "Ready · CPU" }, statuses);
            statuses.Clear();
            // An unavailable device must leave the existing CPU worker reusable.
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.PrepareAsync(model, "GPU", false, CancellationToken.None));
            statuses.Clear();
            await provider.PrepareAsync(model, "Default", false, CancellationToken.None);
            Assert.Equal(new[] { "Ready · CPU" }, statuses);
            Assert.Equal(3, workerChecks);
            // Replacing model weights invalidates the resident identity.
            File.SetLastWriteTimeUtc(models.ModelPath(model), DateTime.UtcNow.AddMinutes(1));
            statuses.Clear();
            await provider.PrepareAsync(model, "CPU", false, CancellationToken.None);
            Assert.Contains("Checking model...", statuses);
            Assert.Contains("Loading model...", statuses);
        }
        finally { Directory.Delete(root, true); }
    }
}
