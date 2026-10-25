using System.Collections.Generic;
using System.Linq;

namespace VoiceFlow.Models;

public sealed record LocalModelInfo(string Id, string Name, string Description, string FileName, long Size, string Sha256, string Url, bool IsBark = false)
{
    public override string ToString() => Name;
}

public static class LocalModels
{
    private const string WhisperRoot = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/";
    public static IReadOnlyList<LocalModelInfo> All { get; } = new[]
    {
        new LocalModelInfo("whisper-tiny", "Whisper Tiny · 78 MB", "Fastest option for a modest CPU. Multilingual, including English and Hindi; lower accuracy than Base or Small.", "ggml-tiny.bin", 77691713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", WhisperRoot + "ggml-tiny.bin"),
        new LocalModelInfo("whisper-base", "Whisper Base · 148 MB", "Balanced CPU option. Multilingual, including English and Hindi. No Python or GPU required.", "ggml-base.bin", 147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", WhisperRoot + "ggml-base.bin"),
        new LocalModelInfo("whisper-small", "Whisper Small Q5 · 190 MB", "Quantized multilingual model. Better accuracy, with more CPU time and memory than Base.", "ggml-small-q5_1.bin", 190085487, "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb", WhisperRoot + "ggml-small-q5_1.bin"),
        new LocalModelInfo("bark-clean", "Underdog Bark 3.0 Clean · 180 MB", "English only, with built-in filler removal. CPU inference uses a separate Python/PyTorch runtime (additional download and several GB of free disk/RAM).", "model.bark", 179458613, "b67ca90d94b79a36af4616f1cb3f241884925de7aa9f3e673a8d5c6c4cfd3436", "https://huggingface.co/ConwayResearch/Underdog-Bark-3.0-Clean/resolve/6d34ed8639b2b9a33e764acf06748519bbb950cf/model.bark", true)
    };

    public static LocalModelInfo Get(string id) => All.FirstOrDefault(m => m.Id == id)
        ?? throw new System.ArgumentException("Unknown local model. Select a model in Settings.");
}
