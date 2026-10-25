using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VoiceFlow.Models;
using VoiceFlow.Services;
using VoiceFlow.ViewModels;

namespace VoiceFlow.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();
    private async void CopyEntry(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Button { Tag: HistoryItem item }) return;
        try { await vm.CopyHistoryItemCommand.ExecuteAsync(item); }
        catch (Exception ex) { AppLogger.LogError("Could not copy history entry", ex); }
    }
    private void DeleteEntry(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button { Tag: HistoryItem item })
            vm.DeleteHistoryItemCommand.Execute(item);
    }
}
