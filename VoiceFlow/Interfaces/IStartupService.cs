namespace VoiceFlow.Interfaces;

public interface IStartupService
{
    bool IsStartupEnabled();
    bool SetStartupEnabled(bool enable);
}
