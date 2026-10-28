using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using ComboBox = System.Windows.Controls.ComboBox;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyBrandingAndMiniPlaybackAsync(IslandViewModel vm, IslandWindow window, Action<bool,string> check, string output)
    {
        var app = System.Windows.Application.Current;
        check(app.FindResource("SutraIslandLogo") is BitmapSource { PixelWidth: 256 }
            && app.FindResource("SutraAssistantLogo") is BitmapSource { PixelWidth: 256 },
            "Both supplied logos load from the published app as shared decoded image resources");
        check(((Border)window.FindName("AssistantBrandLogo")).Child is SutraMascotView,
            "Assistant header uses the shared animated mascot without a duplicate static eye layer underneath");
        using (var trayIcon = (System.Drawing.Icon)typeof(TrayService).GetMethod("CreateIcon", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!)
            check(trayIcon.Width == 32 && trayIcon.Height == 32, "Branded tray icon loads its 32-pixel embedded Windows icon");

        var originalWidth = vm.Settings.IslandWidth; var originalHeight = vm.Settings.IslandHeight;
        var originalEqualizer = vm.Settings.ShowEqualizerInCompact; var originalMode = vm.Settings.IslandVisualMode;
        var originalMotion = vm.Settings.AnimationIntensity; var originalMedia = vm.Media; var originalAudio = vm.Audio;
        var originalExpanded = vm.IsExpanded;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var mediaChanged = typeof(IslandViewModel).GetMethod("OnMediaChanged", flags)!;
        var audioChanged = typeof(IslandViewModel).GetMethod("OnAudioChanged", flags)!;
        var shell = (FrameworkElement)window.FindName("GlassShell");
        async Task Layout()
        {
            await app.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        }
        try
        {
            vm.ClearQ(); vm.Settings.IslandWidth = 136; vm.Settings.IslandHeight = 38;
            vm.Settings.ShowEqualizerInCompact = true; vm.Settings.AnimationIntensity = AnimationIntensity.Reduced;
            vm.Settings.IslandVisualMode = IslandVisualMode.Apple; vm.IsExpanded = false; vm.ApplySettings();
            window.Show(); await Layout();
            var providerSelector = (ComboBox)window.FindName("QProviderSelector"); providerSelector.ApplyTemplate();
            var providerLabel = (TextBlock)providerSelector.Template.FindName("SelectedLabel", providerSelector);
            check(providerLabel.Text == vm.QSelectedProviderLabel && !string.IsNullOrWhiteSpace(providerLabel.Text), "Provider selector displays its saved provider identity");
            var selectedProvider = vm.Settings.QSelectedProvider;
            providerSelector.SelectedIndex = -1; await Layout();
            check(vm.Settings.QSelectedProvider == selectedProvider && providerLabel.Text == vm.QSelectedProviderLabel, "Transient empty dropdown selection cannot erase the saved provider label");
            providerSelector.GetBindingExpression(ComboBox.SelectedValueProperty)!.UpdateTarget();
            providerSelector.SetCurrentValue(FrameworkElement.TagProperty, "Codex · Subscription"); await Layout();
            check(providerLabel.Text == "Codex · Subscription", "Codex subscription identity is visible in the closed provider selector");
            providerSelector.GetBindingExpression(FrameworkElement.TagProperty)!.UpdateTarget();
            var openedSettings = 0;
            EventHandler onSettings = (_, _) => openedSettings++;
            window.OpenSettingsRequested += onSettings;
            try
            {
                shell.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonUpEvent });
                check(openedSettings == 0, "Right-clicking the island does not open Settings");
                typeof(IslandWindow).GetMethod("SettingsButton_Click", flags)!.Invoke(window, [shell, new RoutedEventArgs()]);
                check(openedSettings == 1, "The explicit Settings button remains available after removing the right-click shortcut");
            }
            finally { window.OpenSettingsRequested -= onSettings; }
            var paused = new MediaInfo { Title = "Mini pill playback regression", Artist = "Fixture artist", SourceAppName = "Fixture", PlaybackState = MediaPlaybackState.Paused };
            mediaChanged.Invoke(vm, [null, paused]);
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available }]);
            await Layout();
            check(Math.Abs(shell.Width - 136) < .01 && !vm.ShowCompactEqualizer, "Paused mini pill keeps the configured 136-pixel width");
            await CaptureSutraAsync(window, output, "mini-pill-paused");
            mediaChanged.Invoke(vm, [null, paused with { PlaybackState = MediaPlaybackState.Playing }]);
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available, ActiveAudioOutput = true }]);
            await Layout();
            check(vm.ShowCompactEqualizer && Math.Abs(shell.Width - 136) < .01,
                "Starting playback displays the equalizer without widening the mini pill or revealing extra title space");
            await CaptureSutraAsync(window, output, "mini-pill-playing");
            var lane = (CompactEqualizerView)window.FindName("CompactEqualizerLane");
            var compact = (FrameworkElement)window.FindName("CompactContent");
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available }]); await Layout();
            vm.Settings.AnimationIntensity = AnimationIntensity.Normal; vm.ApplySettings(); await Layout();
            var generationField = typeof(IslandWindow).GetField("_pillAnimationGeneration", flags)!;
            var generation = (int)generationField.GetValue(window)!;
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available, ActiveAudioOutput = true }]); await Layout();
            check((int)generationField.GetValue(window)! == generation && compact.Opacity == 1 && shell.Opacity == 1,
                "Equalizer entry does not restart the whole pill morph or fade compact content");
            check(lane.HasAnimatedProperties && lane.Active, "Normal-motion equalizer entry animates only its own lane");
            await WaitForSutraAsync(() => Math.Abs(lane.Width - 32.5) < .01 && lane.Opacity > .999); await Layout();
            check(Math.Abs(shell.Width - 136) < .01, "Animated equalizer entry preserves mini-pill dimensions");
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available }]); await Layout();
            check((int)generationField.GetValue(window)! == generation && compact.Opacity == 1,
                "Equalizer exit keeps artwork and compact status content fully visible");
            // Reverse before the outgoing lane finishes; the latest state must win.
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available, ActiveAudioOutput = true }]); await Layout();
            await WaitForSutraAsync(() => Math.Abs(lane.Width - 32.5) < .01 && lane.Opacity > .999);
            check(lane.Active && (int)generationField.GetValue(window)! == generation, "Rapid equalizer reversal settles visible without replaying an old collapse");
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available }]); await Layout();
            await WaitForSutraAsync(() => lane.Width < .01 && lane.Opacity < .001);
            check(!lane.Active && compact.Opacity == 1, "Equalizer exit releases its lane without a whole-pill flash");
            vm.Settings.AnimationIntensity = AnimationIntensity.Reduced; vm.ApplySettings(); await Layout();
            audioChanged.Invoke(vm, [null, new AudioState { Availability = AudioAvailability.Available, ActiveAudioOutput = true }]); await Layout();
            check(lane.Width == 32.5 && lane.Opacity == 1 && !lane.HasAnimatedProperties, "Reduced motion shows the equalizer immediately without transition clocks");
            vm.Settings.IslandWidth = 280; vm.ApplySettings(); await Layout();
            check(Math.Abs(shell.Width - 280) < .01 && vm.ShowCompactEqualizer, "Larger compact layouts retain their configured width with the equalizer visible");
            vm.Settings.IslandVisualMode = IslandVisualMode.Stats; vm.Settings.IslandWidth = 136; vm.ApplySettings(); await Layout();
            check(Math.Abs(shell.Width - 136) < .01, "Stats compact layout also preserves the configured width during playback");
        }
        finally
        {
            vm.Settings.IslandWidth = originalWidth; vm.Settings.IslandHeight = originalHeight;
            vm.Settings.ShowEqualizerInCompact = originalEqualizer; vm.Settings.IslandVisualMode = originalMode;
            vm.Settings.AnimationIntensity = originalMotion;
            mediaChanged.Invoke(vm, [null, originalMedia]); audioChanged.Invoke(vm, [null, originalAudio]);
            vm.IsExpanded = originalExpanded; vm.ApplySettings();
        }
    }
}
