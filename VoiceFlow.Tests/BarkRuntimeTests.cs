using VoiceFlow.Services;

namespace VoiceFlow.Tests;

public class BarkRuntimeTests
{
    [Theory]
    [InlineData("CPU")]
    [InlineData("GPU")]
    public async Task FreshInstallationCreatesMissingDirectoryAndOnlyMarksReadyAfterValidation(string device)
    {
        string parent = Path.Combine(Path.GetTempPath(), "voiceflow-fresh-" + Guid.NewGuid());
        string root = Path.Combine(parent, "BarkRuntime");
        string expected = device == "GPU" ? root + "-gpu" : root;
        string marker = Path.Combine(expected, "voiceflow-runtime-v3.ready");
        int commands = 0;
        try
        {
            Assert.False(Directory.Exists(parent));
            using var runtime = new BarkRuntimeService(root, parent, async (executable, args, ct) =>
            {
                Assert.True(Directory.Exists(expected));
                Assert.False(File.Exists(marker));
                commands++;
                if (executable == "py")
                {
                    Directory.CreateDirectory(Path.Combine(expected, "Scripts"));
                    await File.WriteAllTextAsync(Path.Combine(expected, "Scripts", "python.exe"), "test-runtime", ct);
                }
            });
            await runtime.InstallAsync(device, false, null, CancellationToken.None);
            Assert.True(runtime.IsInstalledFor(device));
            Assert.Equal("3", await File.ReadAllTextAsync(marker));
            Assert.Equal(5, commands); // Python, pip, backend, dependencies, audio validation.
            await runtime.InstallAsync(device, false, null, CancellationToken.None);
            Assert.Equal(5, commands); // A ready runtime is reused.
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task CancelledFreshInstallationCanRetryWithoutCreatingFalseReadyMarker()
    {
        string parent = Path.Combine(Path.GetTempPath(), "voiceflow-retry-" + Guid.NewGuid());
        string root = Path.Combine(parent, "BarkRuntime");
        bool cancel = true;
        try
        {
            using var runtime = new BarkRuntimeService(root, parent, async (executable, args, ct) =>
            {
                if (cancel) throw new OperationCanceledException();
                if (executable == "py")
                {
                    Directory.CreateDirectory(Path.Combine(root, "Scripts"));
                    await File.WriteAllTextAsync(Path.Combine(root, "Scripts", "python.exe"), "test-runtime", ct);
                }
            });
            await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.InstallAsync(null, CancellationToken.None));
            Assert.False(runtime.IsInstalled);
            cancel = false;
            await runtime.InstallAsync(null, CancellationToken.None);
            Assert.True(runtime.IsInstalled);
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }
}
