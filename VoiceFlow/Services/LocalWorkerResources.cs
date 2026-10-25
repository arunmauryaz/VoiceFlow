using System;

namespace VoiceFlow.Services;

internal static class LocalWorkerResources
{
    // Leave CPU capacity for the foreground application, including on small machines.
    public static int ThreadCount => ThreadsFor(Environment.ProcessorCount);
    internal static int ThreadsFor(int processors) => Math.Clamp(processors / 2, 1, 4);
}
