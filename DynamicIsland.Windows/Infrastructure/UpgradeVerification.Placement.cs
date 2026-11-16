using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyPlacementAsync(AppSettings settings, SettingsService store, IslandViewModel vm,
        IslandWindow window, Action<bool, string> check, string output)
    {
        settings.TopOffset = 52;
        settings.SideOffset = 0;
        settings.IslandWidth = 188;
        settings.IslandHeight = 38;
        typeof(IslandViewModel).GetMethod("OnMediaChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, [null, new MediaInfo { Title = "Midnight Drive", Artist = "Island Studio", PlaybackState = MediaPlaybackState.Playing }]);
        var handle = new WindowInteropHelper(window).Handle;
        var scale = Math.Max(96u, Interop.NativeMethods.GetDpiForWindow(handle)) / 96d;
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var shell = (FrameworkElement)window.FindName("GlassShell");
        var content = (FrameworkElement)window.FindName("CompactContent");
        async Task Settle(string name)
        {
            vm.ApplySettings(); window.ApplySettings();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(shell.ActualWidth * 2), (int)Math.Ceiling(shell.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
                context.DrawRectangle(new VisualBrush(shell) { Stretch = Stretch.Fill }, null, new Rect(0, 0, shell.ActualWidth, shell.ActualHeight));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        double VisibleTop()
        {
            Interop.NativeMethods.GetWindowRect(handle, out var rect);
            return (rect.Top - screen.Bounds.Top) / scale + 8;
        }
        await Settle("original-compact");
        check(shell.ActualWidth == 188 && shell.ActualHeight == 38 && content.Visibility == Visibility.Visible && content.Opacity > .99,
            "Original compact layout and configured dimensions are restored");
        check(Math.Abs(VisibleTop() - 52) <= 1, "Positive top offset measures from the actual screen top");
        foreach (var mode in new[] { PositionMode.TopLeft, PositionMode.TopCenter, PositionMode.TopRight })
        {
            settings.DefaultPosition = mode;
            settings.TopOffset = 0;
            await Settle("zero-" + mode);
            check(Math.Abs(VisibleTop()) <= 1, mode + ": zero reaches the physical screen edge above the taskbar");
        }
        settings.DefaultPosition = PositionMode.TopCenter;
        settings.TopOffset = 23;
        vm.IsExpanded = true;
        await Settle("expanded");
        check(shell.ActualHeight > 250 && Math.Abs(VisibleTop() - 23) <= 1, "Expansion retains the selected absolute top offset");
        vm.IsExpanded = false;
        await Settle("collapsed");
        check(shell.ActualHeight == 38 && content.Opacity > .99 && Math.Abs(VisibleTop() - 23) <= 1,
            "Collapse restores the original compact layout without changing its offset");
        await store.SaveAsync(settings);
        var saved = await store.LoadAsync();
        check(saved.TopOffset == 23 && saved.IslandHeight == 38 && saved.IslandWidth == 188,
            "Chosen offset and original dimensions persist through a settings round trip");
    }
}
