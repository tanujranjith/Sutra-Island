using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Views;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyWorkspaceAsync(IslandWindow island, SettingsService settings, Action<bool, string> check, string output)
    {
        var path = Path.Combine(output, "sample.txt");
        await File.WriteAllTextAsync(path, "Sutra transfer fixture");
        var files = new DataObject(DataFormats.FileDrop, new[] { path });
        var link = new DataObject(DataFormats.UnicodeText, "https://example.com/download");
        check(IslandWindow.SupportsWorkspaceDrop(files) && await island.AddWorkspaceDropAsync(files) == 1, "The tray accepts a dropped file");
        check(await island.AddWorkspaceDropAsync(files) == 0 && await island.AddWorkspaceDropAsync(link) == 1, "The tray deduplicates files and accepts web links");
        var image = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 4, 5, 6, 255 }, 4);
        check(await island.AddWorkspaceDropAsync(new DataObject(DataFormats.Bitmap, image)) == 1 &&
            File.Exists(island.WorkspaceTray[0].Value), "The tray keeps a dropped screenshot as a PNG");
        await island.SaveWorkspaceNoteAsync("Pick up the microphone.");
        await island.SetWorkspacePinAsync(IslandGlance.Stopwatch);
        var loaded = await settings.LoadAsync();
        check(loaded.DropTrayItems.Count == 3 && loaded.CapturedNotes.Single().Text == "Pick up the microphone." &&
            loaded.PinnedGlance == IslandGlance.Stopwatch, "Tray items, notes and the compact pin survive a settings reload");
        await Task.Delay(3200);
        var glance = (System.Windows.Controls.TextBlock)island.FindName("WorkspaceGlanceLabel");
        check(glance.IsVisible && glance.Text.StartsWith("Stopwatch", StringComparison.Ordinal), "The pinned stopwatch appears on the compact island");

        island.ShowWorkspace(WorkspaceTab.Capture);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var panel = island.OwnedWindows.OfType<WorkspaceWindow>().Single();
        var tabs = (System.Windows.Controls.TabControl)panel.FindName("Tabs");
        check(panel.IsVisible && tabs.SelectedIndex == (int)WorkspaceTab.Capture &&
            ((System.Windows.Controls.ListBox)panel.FindName("NotesList")).Items.Count == 1, "Quick Capture opens with saved notes");
        island.ShowWorkspace(WorkspaceTab.Pinned);
        check(tabs.SelectedIndex == (int)WorkspaceTab.Pinned && (IslandGlance)((System.Windows.Controls.ComboBox)panel.FindName("PinChoice")).SelectedValue == IslandGlance.Stopwatch,
            "The Pinned tab reflects the saved compact choice");
        island.ShowWorkspace(WorkspaceTab.Transfers);
        var copiedPath = Path.Combine(output, "sample-copy.txt");
        var copy = island.StartWorkspaceCopyAsync(path, copiedPath);
        check(island.WorkspaceTransfers[0].IsBusy, "A running copy exposes progress");
        panel.Close();
        await copy;
        check(island.WorkspaceTransfers[0].CanOpenFolder && File.ReadAllText(copiedPath) == File.ReadAllText(path),
            "A copy completes after the panel closes and offers its destination folder");
    }
}
