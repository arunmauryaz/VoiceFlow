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
    }
}
