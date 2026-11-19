using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DynamicIsland.Windows.Views;

public partial class WorkspaceWindow : Window
{
    private sealed record PinOption(string Label, IslandGlance Value);
    private readonly IslandWindow _island;
    private Point _dragStart;
    private TrayItem? _dragItem;
    private bool _ready;

    internal WorkspaceWindow(IslandWindow island)
    {
        _island = island;
        InitializeComponent();
        TrayList.ItemsSource = island.WorkspaceTray;
        NotesList.ItemsSource = island.WorkspaceNotes;
        TransferList.ItemsSource = island.WorkspaceTransfers;
        PinChoice.ItemsSource = new PinOption[]
        {
            new("Off", IslandGlance.None), new("Active timer", IslandGlance.Timer),
            new("Stopwatch", IslandGlance.Stopwatch), new("CPU usage", IslandGlance.Cpu),
            new("Memory usage", IslandGlance.Memory), new("Battery", IslandGlance.Battery),
            new("Clock", IslandGlance.Clock)
        };
        PinChoice.SelectedValue = island.WorkspacePin;
        _ready = true;
    }

    internal void ShowTab(WorkspaceTab tab)
    {
        Tabs.SelectedIndex = (int)tab;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (tab == WorkspaceTab.Capture) NoteInput.Focus();
    }

    private void Tray_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IslandWindow.SupportsWorkspaceDrop(e.Data) && (e.AllowedEffects & DragDropEffects.Copy) != 0
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Tray_Drop(object sender, DragEventArgs e)
    {
        Tray_DragOver(sender, e);
        if (e.Effects == DragDropEffects.None) return;
        try { ShowAdded(await _island.AddWorkspaceDropAsync(e.Data)); }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.GetDataObject() is { } data) ShowAdded(await _island.AddWorkspaceDropAsync(data, Clipboard.GetImage()));
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }

    private void ShowAdded(int count) => Feedback.Text = count == 0 ? "No new files, images or web links found." :
        count == 1 ? "Item added." : $"{count} items added.";

    private static DataObject? DragData(TrayItem item)
    {
        if (item.Kind == TrayItemKind.Link) return new DataObject(DataFormats.UnicodeText, item.Value);
        if (!File.Exists(item.Value) && !Directory.Exists(item.Value)) return null;
        return new DataObject(DataFormats.FileDrop, new[] { item.Value });
    }

    private void TrayItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(TrayList);
        _dragItem = e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(TrayList, source) is ListBoxItem { Content: TrayItem item } ? item : null;
    }

    private void TrayItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is not { } item) return;
        var delta = e.GetPosition(TrayList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragItem = null;
        if (DragData(item) is not { } data) { Feedback.Text = "The original file or folder is unavailable."; return; }
        try { DragDrop.DoDragDrop(TrayList, data, DragDropEffects.Copy); }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }

    private void CopyTrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (TrayList.SelectedItem is not TrayItem item) { Feedback.Text = "Select an item first."; return; }
        try
        {
            if (DragData(item) is not { } data) { Feedback.Text = "The original file or folder is unavailable."; return; }
            Clipboard.SetDataObject(data, copy: true);
            Feedback.Text = "Copied to clipboard.";
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }

    private async void RemoveTrayItem_Click(object sender, RoutedEventArgs e)
    {
        if (TrayList.SelectedItem is not TrayItem item) return;
        _island.WorkspaceTray.Remove(item);
        await _island.SaveWorkspaceTrayAsync();
        Feedback.Text = "Removed from the tray.";
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!WorkspaceTransferService.TryDownloadUri(DownloadUrl.Text, out var url))
        {
            Feedback.Text = "Enter a direct HTTP or HTTPS download link.";
            return;
        }
        var chooser = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save download", FileName = WorkspaceTransferService.SuggestedFileName(url),
            ValidateNames = true, OverwritePrompt = false
        };
        if (chooser.ShowDialog(this) != true) return;
        if (File.Exists(chooser.FileName)) { Feedback.Text = "Choose a new filename to keep the existing file."; return; }
        Feedback.Text = "";
        await _island.StartWorkspaceDownloadAsync(url, chooser.FileName);
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var chooser = new Microsoft.Win32.OpenFileDialog { Title = "Choose files to copy", Multiselect = true };
        if (chooser.ShowDialog(this) == true) QueueCopies(chooser.FileNames);
    }

    private void CopyTrayFile_Click(object sender, RoutedEventArgs e)
    {
        if (TrayList.SelectedItem is TrayItem { Kind: TrayItemKind.File or TrayItemKind.Screenshot } item && File.Exists(item.Value))
            QueueCopies([item.Value]);
        else Feedback.Text = "Select an available file or screenshot.";
    }

    private async void QueueCopies(string[] files)
    {
        var chooser = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to copy the files" };
        if (chooser.ShowDialog(this) != true) return;
        Tabs.SelectedIndex = (int)WorkspaceTab.Transfers;
        Feedback.Text = "";
        foreach (var file in files)
        {
            var target = Path.Combine(chooser.FolderName, Path.GetFileName(file));
            await _island.StartWorkspaceCopyAsync(file, target);
        }
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkspaceTransfer { IsBusy: true } transfer }) transfer.Cancellation.Cancel();
    }

    private void OpenTransferFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkspaceTransfer { CanOpenFolder: true } transfer }) return;
        try { Process.Start(new ProcessStartInfo(transfer.Folder) { UseShellExecute = true }); }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }

    private async void PinChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || PinChoice.SelectedValue is not IslandGlance value) return;
        await _island.SetWorkspacePinAsync(value);
        Feedback.Text = value == IslandGlance.None ? "Compact pin cleared." : "Pinned to the compact island.";
    }

    private async void NoteInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        await SaveNoteAsync();
    }

    private async void SaveNote_Click(object sender, RoutedEventArgs e) => await SaveNoteAsync();

    private async Task SaveNoteAsync()
    {
        var text = NoteInput.Text;
        if (string.IsNullOrWhiteSpace(text)) { Feedback.Text = "Write a note first."; return; }
        NoteInput.Clear();
        await _island.SaveWorkspaceNoteAsync(text);
        Feedback.Text = "Note saved.";
        NoteInput.Focus();
    }

    private void CopyNote_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not CapturedNote note) return;
        try { Clipboard.SetText(note.Text); Feedback.Text = "Note copied."; }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }

    private async void RemoveNote_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not CapturedNote note) return;
        _island.WorkspaceNotes.Remove(note);
        await _island.SaveWorkspaceNotesAsync();
        Feedback.Text = "Note removed.";
    }
}
