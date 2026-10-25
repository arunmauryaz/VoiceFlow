using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace VoiceFlow.Services;

internal static class WhisperWorker
{
    public static async Task RunAsync(string[] args)
    {
        try
        {
            bool gpu = args[2] == "GPU";
            RuntimeOptions.RuntimeLibraryOrder = new() { gpu ? RuntimeLibrary.Vulkan : RuntimeLibrary.Cpu };
            if (gpu) RuntimeOptions.LibraryPath = Path.Combine(args[3], "worker.exe");
            using var factory = WhisperFactory.FromPath(args[1]);
            Console.WriteLine(JsonSerializer.Serialize(new { ready = true }));
            string? line;
            while ((line = await Console.In.ReadLineAsync()) != null)
            {
                try
                {
                    using var request = JsonDocument.Parse(line);
                    using var processor = factory.CreateBuilder().WithLanguage(request.RootElement.GetProperty("language").GetString() ?? "auto").Build();
                    var text = new StringBuilder();
                    await using (var audio = File.OpenRead(request.RootElement.GetProperty("audio").GetString()!))
                    {
                        await foreach (var segment in processor.ProcessAsync(audio)) text.Append(segment.Text);
                    }
                    // Close the WAV before acknowledging completion so the parent can remove it.
                    Console.WriteLine(JsonSerializer.Serialize(new { text = text.ToString().Trim() }));
                }
                catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message })); }
            }
        }
        catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message })); }
    }
}
