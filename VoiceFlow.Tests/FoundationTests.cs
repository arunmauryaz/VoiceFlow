using System;
using System.IO;
using VoiceFlow.Helpers;
using VoiceFlow.Models;
using VoiceFlow.Native;
using VoiceFlow.Services;
using Xunit;

namespace VoiceFlow.Tests;

public class FoundationTests
{
    [Fact]
    public void SecureStorage_EncryptAndDecrypt_ReturnsOriginalString()
    {
        string original = "AIzaSyTestApiKey_1234567890!@#$%^&*()";
        string encrypted = SecureStorageHelper.EncryptString(original);

        Assert.False(string.IsNullOrEmpty(encrypted));
        Assert.NotEqual(original, encrypted);

        string? decrypted = SecureStorageHelper.DecryptString(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void SecureStorage_DecryptInvalidData_ReturnsNull()
    {
        string? result = SecureStorageHelper.DecryptString("InvalidBase64Garbage==");
        Assert.Null(result);
    }

    [Fact]
    public void KeyFormatting_FormatsStandardCombinationsCorrectly()
    {
        var ctrlSpace = new HotkeyConfig(KeyModifiers.Control, Win32Constants.VK_SPACE);
        Assert.Equal("Ctrl + Space", KeyFormattingHelper.FormatHotkey(ctrlSpace));

        var altSpace = new HotkeyConfig(KeyModifiers.Alt, Win32Constants.VK_SPACE);
        Assert.Equal("Alt + Space", KeyFormattingHelper.FormatHotkey(altSpace));

        var ctrlShiftSpace = new HotkeyConfig(KeyModifiers.Control | KeyModifiers.Shift, Win32Constants.VK_SPACE);
        Assert.Equal("Ctrl + Shift + Space", KeyFormattingHelper.FormatHotkey(ctrlShiftSpace));

        var f8 = new HotkeyConfig(KeyModifiers.None, Win32Constants.VK_F8);
        Assert.Equal("F8", KeyFormattingHelper.FormatHotkey(f8));
    }

    [Theory]
    [InlineData("F1", KeyModifiers.None, Win32Constants.VK_F1)]
    [InlineData("F2", KeyModifiers.None, Win32Constants.VK_F2)]
    [InlineData("F8", KeyModifiers.None, Win32Constants.VK_F8)]
    [InlineData("F12", KeyModifiers.None, Win32Constants.VK_F12)]
    [InlineData("Ctrl + Space", KeyModifiers.Control, Win32Constants.VK_SPACE)]
    [InlineData("Alt + Space", KeyModifiers.Alt, Win32Constants.VK_SPACE)]
    [InlineData("Win + Space", KeyModifiers.Windows, Win32Constants.VK_SPACE)]
    [InlineData("Ctrl + Shift + Space", KeyModifiers.Control | KeyModifiers.Shift, Win32Constants.VK_SPACE)]
    [InlineData("Ctrl + Alt + Space", KeyModifiers.Control | KeyModifiers.Alt, Win32Constants.VK_SPACE)]
    [InlineData("Ctrl + F8", KeyModifiers.Control, Win32Constants.VK_F8)]
    [InlineData("Win + Alt + V", KeyModifiers.Windows | KeyModifiers.Alt, 0x56)]
    public void KeyFormatting_ParsesFunctionKeysAndCombinationsCorrectly(string input, KeyModifiers expectedMods, uint expectedVk)
    {
        var parsed = KeyFormattingHelper.ParseHotkey(input);
        Assert.NotNull(parsed);
        Assert.Equal(expectedMods, parsed.Modifiers);
        Assert.Equal(expectedVk, parsed.VirtualKey);
    }

    [Fact]
    public void AppSettings_DefaultValues_MatchRequirements()
    {
        var settings = new AppSettings();
        Assert.True(settings.RunInBackground);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(HotkeyActivationMode.HoldToTalk, settings.HotkeyMode);
        Assert.True(settings.AutoPaste);
        Assert.True(settings.PreserveClipboard);
        Assert.True(settings.ShowOverlay);
        Assert.False(settings.CleanupEnabled);
        Assert.Equal("gemini-3.5-transcribe", settings.GeminiModel);
    }
}
