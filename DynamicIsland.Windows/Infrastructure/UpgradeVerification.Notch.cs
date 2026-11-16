using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using CheckBox = System.Windows.Controls.CheckBox;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyNotchAsync(IslandViewModel vm, IslandWindow island,
        SettingsService persistence, LoggingService log, IQProviderRegistry registry,
        Action<bool, string> check, string output)
    {
        var settings = vm.Settings;
        settings.FlowEnabled = false;
        settings.AnimationIntensity = AnimationIntensity.Reduced;
        settings.DefaultPosition = PositionMode.BottomRight;
        settings.SideOffset = 35; settings.TopOffset = 47;
        settings.IslandWidth = 230; settings.IslandHeight = 62;
        check(!new AppSettings().TopNotchMode, "Existing installs retain floating mode by default");
        using var codex = new AsyncDisposeAdapter(new CodexAppServerClient(log: log));
        var editorVm = new SettingsViewModel(settings, persistence, new StartupService(log),
            () => { vm.ApplySettings(); island.ApplySettings(); }, () => { }, () => { },
            new FakeSecrets(), registry, new CodexAccountCoordinator(codex.Client, log));
        var editor = new SettingsWindow(editorVm, vm, island) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        async Task Idle() { await island.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); island.UpdateLayout(); editor.UpdateLayout(); }
        var shell = (NotchBorder)island.FindName("GlassShell");
        void SaveShell(string name)
        {
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 86, 110)), null, new Rect(0, 0, shell.ActualWidth + 40, shell.ActualHeight + 20));
                context.DrawRectangle(new VisualBrush(shell), null, new Rect(20, 0, shell.ActualWidth, shell.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling((shell.ActualWidth + 40) * 2), (int)Math.Ceiling((shell.ActualHeight + 20) * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        try
        {
            island.Show(); editor.Show(); await Idle();
            editor.OpenSearch(nameof(SettingsViewModel.TopNotchMode)); await Idle();
            var toggle = FeatureLogicalDescendants(editor).OfType<CheckBox>().Single(element =>
                BindingOperations.GetBindingExpression(element, ToggleButton.IsCheckedProperty)?.ParentBinding.Path?.Path == nameof(SettingsViewModel.TopNotchMode));
            check(toggle.IsVisible && editorVm.SelectedSectionKey == "position", "Settings search opens Mac notch mode in Position");
            check(System.Windows.Automation.AutomationProperties.GetName(toggle) == "Mac notch mode", "Notch toggle has an accessible name");
            var handle = new WindowInteropHelper(island).Handle;
            Interop.NativeMethods.GetWindowRect(handle, out var floating);
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource(); await Idle();
            check(settings.TopNotchMode && vm.IsTopNotch && shell.IsNotch && shell.NotchProgress == 1, "Settings toggle immediately updates the actual notch shell");
            check(!editorVm.CanAdjustFloatingPosition && settings.DefaultPosition == PositionMode.BottomRight && settings.SideOffset == 35 && settings.TopOffset == 47, "Notch locks its anchor while preserving floating placement preferences");
            var scale = Interop.NativeMethods.GetDpiForWindow(handle) / 96d;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            void CheckAnchor(string label)
            {
                var origin = shell.PointToScreen(new System.Windows.Point());
                check(Math.Abs(origin.Y - screen.Bounds.Top) <= 1 && Math.Abs(origin.X + shell.ActualWidth * scale / 2 - (screen.Bounds.Left + screen.Bounds.Width / 2d)) <= 1, label);
            }
            CheckAnchor("Compact notch touches the physical top edge and stays centered");
            check(shell.Clip.FillContains(new System.Windows.Point(shell.ActualWidth / 2, .1)) && !shell.Clip.FillContains(new System.Windows.Point(1, 9)) && !shell.Clip.FillContains(new System.Windows.Point(1, shell.ActualHeight - 1)), "Notch mask has a full top attachment, concave shoulders and rounded lower corners");
            var preview = (NotchBorder)editor.FindName("PreviewPill");
            check(preview.IsNotch && preview.NotchProgress == 1 && !vm.NativeFrostActive, "Preview shares the notch mask and no rectangular backdrop leaks around it");
            SaveShell("notch-compact");
            vm.IsExpanded = true; island.ApplySettings(); await Idle();
            CheckAnchor("Expanded notch remains attached to the top center");
            check(shell.ActualHeight > settings.IslandHeight && shell.Clip.FillContains(new System.Windows.Point(shell.ActualWidth / 2, shell.ActualHeight - 1)), "Expanded notch reveals its content inside the same mask");
            SaveShell("notch-expanded");
            vm.IsExpanded = false; island.ApplySettings(); await Idle();
            CheckAnchor("Collapse returns to the attached compact notch");
            await persistence.SaveAsync(settings);
            var saved = await persistence.LoadAsync();
            check(saved.TopNotchMode && saved.DefaultPosition == PositionMode.BottomRight && saved.SideOffset == 35 && saved.TopOffset == 47, "Notch preference and previous floating placement survive save and reload");
            editorVm.TopNotchMode = false; await Idle();
            Interop.NativeMethods.GetWindowRect(handle, out var restored);
            check(!shell.IsNotch && shell.NotchProgress == 0 && Math.Abs(restored.Left - floating.Left) <= 1 && Math.Abs(restored.Top - floating.Top) <= 1, "Switching notch off restores the exact floating position and rounded shell");
            settings.AnimationIntensity = AnimationIntensity.Normal; vm.ApplySettings(); island.ApplySettings();
            editorVm.TopNotchMode = true;
            await Task.Delay(100); await Idle();
            check(shell.NotchProgress > 0 && shell.NotchProgress < 1, "Docking morph renders intermediate geometry");
            editorVm.TopNotchMode = false;
            await Task.Delay(480); await Idle();
            check(shell.NotchProgress < .001 && !settings.TopNotchMode, "A reversed docking morph settles cleanly back into floating mode");
            Interop.NativeMethods.GetWindowRect(handle, out var reversed);
            check(Math.Abs(reversed.Left - floating.Left) <= 1 && Math.Abs(reversed.Top - floating.Top) <= 1, "Reversing a docking animation also restores the native window anchor");
            settings.AnimationIntensity = AnimationIntensity.Reduced; vm.ApplySettings(); island.ApplySettings();
            editorVm.TopNotchMode = true; await Idle();
            check(shell.NotchProgress == 1, "Reduced motion applies the notch without animation");
        }
        finally { editor.Close(); }
    }
}
