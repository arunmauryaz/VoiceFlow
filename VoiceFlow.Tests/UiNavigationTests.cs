using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VoiceFlow.Models;
using VoiceFlow.Providers.Gemini;
using VoiceFlow.Services;
using VoiceFlow.ViewModels;
using VoiceFlow.Views;

[assembly: AvaloniaTestApplication(typeof(VoiceFlow.Tests.UiTestApplication))]

namespace VoiceFlow.Tests;

public static class UiTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class UiNavigationTests
{
    [AvaloniaFact]
    public async Task PopulatedHistoryRendersAndAllSidebarPagesCanBeOpened()
    {
        var settings = new MockSettingsService();
        using var audio = new AudioRecorder();
        using var hotkey = new WindowsHotkeyService();
        var clipboard = new MockClipboardService();
        using var overlay = new RecordingOverlayViewModel(clipboard);
        var provider = new GeminiTranscriptionProvider(settings);
        var cleanup = new GeminiTextProcessor(settings);
        using var manager = new AppStateManager(settings, audio, provider, cleanup, hotkey,
            new WindowsTextInjectionService(clipboard), clipboard, overlay);
        using var vm = new MainWindowViewModel(settings, audio, provider, cleanup, hotkey, clipboard,
            new WindowsStartupService(), manager);
        const string transcript = "A real transcript in the History template.";
        vm.RecentHistory.Add(new HistoryItem { FinalText = transcript });
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            foreach (string page in new[] { "History", "Settings", "About", "Home", "History" })
            {
                vm.NavigateToCommand.Execute(page);
                window.Measure(new Size(1000, 800));
                window.Arrange(new Rect(0, 0, 1000, 800));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Enum.Parse<AppSection>(page), vm.CurrentSection);
            }
            var history = window.GetVisualDescendants().OfType<HistoryView>().Single();
            Assert.Contains(history.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == transcript);
            var copy = history.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Copy");
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Assert.Equal(transcript, await clipboard.GetTextAsync());
        }
        finally { window.Close(); }
    }
}
