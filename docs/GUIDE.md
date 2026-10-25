# VoiceFlow guide

[← README](../README.md)

## Hardware and models

VoiceFlow's installer targets Windows x64, with Windows 10 build 19041 as the app
platform floor. Local backends add requirements beyond those of the desktop UI.

| Path | Requirements and starting guidance |
| --- | --- |
| Gemini | Microphone, internet and your API key. Cloud recognition avoids loading local model weights. Consider it first on memory-constrained PCs. |
| Whisper Tiny / Base | Windows 11, x64 CPU supporting AVX, AVX2, FMA and F16C, plus the VC++ 2022 x64 redistributable. An 8 GB system is a practical starting recommendation, not a validated minimum. |
| Whisper Small Q5 | Same native requirements; allow more compute and memory headroom. Consider a 16 GB system, especially while multitasking. |
| Bark Clean | English only, 0.6B model with built-in filler cleanup. Requires an isolated Python/PyTorch runtime and several GB of available memory/disk. A 16 GB+ system is a starting recommendation. |

No dedicated GPU is required. Default detects a compatible GPU and falls back to
CPU if GPU initialization fails. Whisper GPU uses Vulkan on compatible AMD, Intel,
and NVIDIA hardware. Bark GPU needs a compatible NVIDIA device and CUDA driver
12.6+ (the app checks compute capability 7 or newer); AMD users can use Bark CPU.
The app installs backends automatically. A genuine CPU/GPU change replaces its worker.

These are planning guidelines, not measured FPS, real-time factors, or guaranteed
minimum RAM figures. CPU generation, memory available to the process, audio length,
and other running applications affect performance. We have not published a full
hardware or cross-product accuracy benchmark.

| Available model | Approximate weights | Language |
| --- | ---: | --- |
| Whisper Tiny | 78 MB | Multilingual, including English and Hindi |
| Whisper Base | 148 MB | Multilingual, including English and Hindi |
| Whisper Small Q5 | 190 MB | Multilingual, including English and Hindi |
| Underdog Bark 3.0 Clean | 180 MB | English; built-in filler cleanup |
| Gemini cloud | No local weights | Provider/model dependent |

Downloads are pinned and verified. Weight sizes come from the app's model catalog.
Native requirements follow [Whisper.net's documentation](https://github.com/sandrohanea/whisper.net#whispernetruntime).
Bark's model and language description come from its [model card](https://huggingface.co/ConwayResearch/Underdog-Bark-3.0-Clean).

## Storage and privacy

| Location | Contents |
| --- | --- |
| Your installation folder | About 195 MB for the app, .NET, native files and uninstaller |
| `%LOCALAPPDATA%\VoiceFlow\Models` | Selected model weights and Bark's decoded cache |
| `%LOCALAPPDATA%\VoiceFlow` | Optional Whisper GPU backend and isolated Bark CPU/GPU runtimes |
| `%APPDATA%\VoiceFlow` | Settings, local history and rotating logs |

Bark's compressed 180 MB download expands into a much larger in-memory model. Its
optional decoded cache is about 2.5 GB; the Python/PyTorch runtime needs additional
space. GPU runtime storage varies. Thus a small weight download does **not** imply
small total disk or RAM use. First setup/cache creation takes longer than later starts.

**Repair model** refreshes setup and weights and rebuilds the cache. **Delete model**
removes the selected weights and decoded cache, while retaining shared runtimes.
Deleted models stay removed until you choose Download or select them again.

Local recognition does not send audio to Gemini or use Gemini cleanup, and never
silently falls back to the cloud. Gemini mode sends audio to Google's API. Local
History still contains transcripts. API keys use Windows DPAPI encryption.
Temporary recording files are deleted after processing.

Models unload after **15 minutes idle**, unless **Keep model ready** is enabled.
Keeping a model ready prioritizes fast starts and retains memory. Workers use
below-normal process priority and up to four CPU threads. Audio queues are bounded;
long finished-mode recordings use disk-backed temporary files. Workers exit with
the app, and logs rotate at 2 MB.

## Shortcuts and output

Set hold and toggle shortcuts independently in Settings. Click each key box and
press a key, then Save & Activate. Use one to three keys; duplicate, conflicting,
and known reserved Windows combinations are rejected. Escape cancels key capture.
New installs use Ctrl + Space for hold and F10 for toggle; updates preserve existing bindings.

- **When finished:** insert the complete transcript after recording stops.
- **Continuous typing:** insert recognized, non-overlapping phrases at pauses.
  This is phrase-by-phrase output, not speculative token streaming. It skips the
  separate cloud cleanup pass and pauses insertion if you change windows or fields.
- **Home:** Start dictation previews inside VoiceFlow instead of typing into another app.
- **Escape:** immediately hides the notch while recording or processing. The next
  shortcut restores a hidden recording. During dismissed processing it cancels
  the old request and begins a fresh recording; late results cannot overwrite it.

## Troubleshooting

| Symptom | First check |
| --- | --- |
| Local setup takes time | Allow the first weight/runtime download and Bark cache build to finish. Tiny/Base are lighter starting choices. |
| GPU unavailable | Select Default or CPU; check compatible GPU and drivers. Bark GPU needs NVIDIA. |
| Missing/corrupt model | Use Repair model in Settings. |
| Speech is not inserted | Focus an editable field. Continuous insertion pauses after a focus change; copy your full transcript from the app. |
| App appears to stay open | Closing the window leaves the tray app running. Use tray → Exit to quit or upgrade. |
| Large install-folder size | Keep app installation separate from source code and build outputs. Model/runtime data is stored separately. |

For bug reports, include app version, Windows version, CPU/GPU, RAM, selected model
and device, reproduction steps, and the relevant error report. Remove private
transcript content and never include API keys.

## Build and debug

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Double-click
[Run-VoiceFlow.bat](../Run-VoiceFlow.bat) to build current source and launch it. Exit
an existing tray instance first; build errors remain visible.

```powershell
dotnet restore VoiceFlow.sln
dotnet test VoiceFlow.Tests/VoiceFlow.Tests.csproj -c Release
dotnet run --project VoiceFlow/VoiceFlow.csproj
```

For installer and portable packages, install Inno Setup 6 and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

The script runs tests and creates self-contained x64 packages, checksums, and install
notes in `release/`. It removes temporary build folders even after failures; use
`-KeepBuildFiles` only for packaging diagnostics. Keep native libraries and worker
scripts beside the executable. Do not install into the source checkout.

README SVGs are generated with the Python standard library:

```powershell
python docs/design/generate_readme_visuals.py
```

## Comparison sources

The README comparison was checked on **26 October 2026** against:

- [Wispr supported platforms](https://docs.wisprflow.ai/articles/1036674442-supported-devices-and-system-requirements), [cloud processing](https://wisprflow.ai/enterprise-privacy), and [plans](https://docs.wisprflow.ai/articles/9559327591).
- [Handy's official repository](https://github.com/cjpais/Handy).
- [Microsoft's Win + H voice typing guide](https://support.microsoft.com/en-us/accessibility/windows/use-voice-typing-to-talk-instead-of-type-on-your-pc).
- [Microsoft's separate offline Voice Access feature](https://support.microsoft.com/en-us/accessibility/windows/voice-access/voice-access-frequently-asked-questions-faqs).

It compares documented features, not recognition accuracy, latency, battery use,
or privacy compliance. Feature sets and prices can change.
