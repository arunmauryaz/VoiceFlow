namespace VoiceFlow.Models;

public record AudioDeviceInfo(int DeviceNumber, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} (Default)" : Name;
}
