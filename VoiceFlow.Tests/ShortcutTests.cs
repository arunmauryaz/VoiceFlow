using System.Text.Json;
using VoiceFlow.Helpers;
using VoiceFlow.Models;
using VoiceFlow.Services;

namespace VoiceFlow.Tests;

public class ShortcutTests
{
    private static ShortcutStateMachine Create(uint[]? hold = null)
    {
        var state = new ShortcutStateMachine();
        state.Configure(HotkeyConfig.FromKeys(hold ?? new uint[] { 0x10, 0x11, 0x5B }),
            HotkeyConfig.FromKeys(new uint[] { 0x79 }));
        return state;
    }

    [Theory]
    [InlineData(0x79)]
    [InlineData(0x77)]
    public void DismissalRearmsToggleAndHoldForTheNextFreshPress(int shortcut)
    {
        var state = Create(new uint[] { 0x77 });
        Assert.Equal(1, state.Process((uint)shortcut, true).Action);
        state.Process((uint)shortcut, false);
        state.ResetActivation(); // Escape dismisses and rearms the active shortcut.
        Assert.Equal(1, state.Process((uint)shortcut, true).Action);
        state.Process((uint)shortcut, false);
        if (shortcut == 0x79) Assert.Equal(-1, state.Process((uint)shortcut, true).Action);
    }

    [Theory]
    [InlineData(0x10, 0x11, 0x5B)]
    [InlineData(0x5B, 0x10, 0x11)]
    [InlineData(0x11, 0x5B, 0x10)]
    public void ModifierChordWorksInAnyOrderAndReleasesAnyMember(int a, int b, int c)
    {
        var state = Create();
        Assert.Equal(0, state.Process((uint)a, true).Action);
        Assert.Equal(0, state.Process((uint)b, true).Action);
        Assert.Equal((1, true), state.Process((uint)c, true));
        Assert.Equal(0, state.Process((uint)c, true).Action);
        Assert.Equal(-1, state.Process((uint)a, false).Action);
        Assert.Equal(0, state.Process((uint)b, false).Action);
    }

    [Fact]
    public void ToggleOnlyChangesOnFreshPressAndSuppressesFunctionKey()
    {
        var state = Create();
        Assert.Equal((1, true), state.Process(0x79, true));
        for (int i = 0; i < 20; i++) Assert.Equal((0, true), state.Process(0x79, true));
        Assert.Equal((0, true), state.Process(0x79, false));
        Assert.Equal((-1, true), state.Process(0x79, true));
    }

    [Fact]
    public void HoldDoesNotStopAnExistingToggleRecording()
    {
        var state = Create(new uint[] { 0x77 });
        state.Process(0x79, true);
        state.Process(0x79, false);
        Assert.Equal(0, state.Process(0x77, true).Action);
        Assert.Equal(0, state.Process(0x77, false).Action);
        Assert.Equal(-1, state.Process(0x79, true).Action);
        state.Process(0x79, false);
        Assert.Equal(1, state.Process(0x77, true).Action);
        Assert.Equal(-1, state.Process(0x77, false).Action);
    }

    [Fact]
    public void MultipleNonModifierKeysAreRetainedAndMatched()
    {
        var state = Create(new uint[] { 0x41, 0x42, 0x43 });
        state.Process(0x43, true);
        Assert.Equal(0, state.Process(0x41, true).Action);
        Assert.Equal(1, state.Process(0x42, true).Action);
        Assert.Equal(-1, state.Process(0x43, false).Action);
        Assert.Equal("A + B + C", HotkeyConfig.FromKeys(new uint[] { 0x41, 0x42, 0x43 }).ToString());
    }

    [Fact]
    public void LeftAndRightModifiersShareOneLogicalKey()
    {
        var state = Create(new uint[] { 0x11 });
        Assert.Equal(1, state.Process(0xA2, true).Action);
        Assert.Equal(0, state.Process(0xA3, true).Action);
        Assert.Equal(0, state.Process(0xA2, false).Action);
        Assert.Equal(-1, state.Process(0xA3, false).Action);
    }

    [Fact]
    public void ExtraModifiersDoNotTriggerSingleFunctionKey()
    {
        var state = Create();
        state.Process(0xA2, true);
        Assert.Equal((0, false), state.Process(0x79, true));
    }

    [Fact]
    public void ResetAfterRecordingErrorLetsNextTogglePressStartAgain()
    {
        var state = Create();
        state.Process(0x79, true);
        state.ResetActivation();
        Assert.Equal(0, state.Process(0x79, true).Action);
        state.Process(0x79, false);
        Assert.Equal(1, state.Process(0x79, true).Action);
    }

    [Fact]
    public void PausedKeysDoNotActivateOrLeaveStuckModifiers()
    {
        var state = Create();
        Assert.Equal((0, false), state.Process(0x79, true, false));
        state.Process(0x79, false, false);
        state.Process(0xA2, true, false);
        state.Process(0xA2, false, false);
        Assert.Equal(1, state.Process(0x79, true).Action);
    }

    [Theory]
    [InlineData(HotkeyActivationMode.HoldToTalk)]
    [InlineData(HotkeyActivationMode.Toggle)]
    public void LegacySettingsPreserveExistingShortcut(HotkeyActivationMode mode)
    {
        var settings = new AppSettings { Hotkey = new HotkeyConfig(KeyModifiers.Alt, 0x77), HotkeyMode = mode };
        settings.EnsureShortcuts();
        var migrated = mode == HotkeyActivationMode.HoldToTalk ? settings.HoldHotkey : settings.ToggleHotkey;
        Assert.Equal("Alt + F8", migrated!.ToString());
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.HoldHotkey!.EffectiveKeys, roundTrip.HoldHotkey!.EffectiveKeys);
        Assert.Equal(settings.ToggleHotkey!.EffectiveKeys, roundTrip.ToggleHotkey!.EffectiveKeys);
    }

    [Fact]
    public void ArbitraryKeysSurviveSettingsRoundTrip()
    {
        var settings = new AppSettings
        {
            HoldHotkey = HotkeyConfig.FromKeys(new uint[] { 0xA0, 0xA3, 0x5C }),
            ToggleHotkey = HotkeyConfig.FromKeys(new uint[] { 0x79 })
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Shift + Ctrl + Win", KeyFormattingHelper.FormatHotkey(restored.HoldHotkey!));
        Assert.Equal("F10", restored.ToggleHotkey!.ToString());
    }
}
