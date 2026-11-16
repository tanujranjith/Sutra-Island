using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyPrivacyAsync(IslandViewModel vm, IslandWindow window,
        PrivacySensorService sensor, Action<PrivacySensorState> update, Action<bool, string> check, string output)
    {
        vm.Settings.IslandWidth = 188; vm.Settings.IslandHeight = 38;
        vm.Settings.AnimationIntensity = AnimationIntensity.Reduced;
        vm.Settings.ShowPrivacyIndicators = true;
        vm.ApplySettings(); window.Show(); window.ApplySettings();
        sensor.ConfigurePolling(TimeSpan.FromSeconds(1)); sensor.Start();
        async Task Idle() { await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        var dot = (FrameworkElement)window.FindName("PrivacyIndicatorDot");
        check(!vm.ShowPrivacyInUse && dot.Visibility == Visibility.Collapsed, "Inactive sensors do not show an indicator just because an app is open");
        update(new PrivacySensorState(["Camera"], []));
        await WaitForSutraAsync(() => vm.ShowCameraInUse); await Idle();
        check(dot.IsVisible && vm.PrivacyCameraApps.SequenceEqual(["Camera"]), "Camera streaming displays the compact dot and its actual app attribution");
        check(((SolidColorBrush)vm.PrivacyIndicatorBrush).Color == System.Windows.Media.Color.FromRgb(48, 209, 88), "Camera activity uses the green privacy color");
        var p = dot.TranslatePoint(new System.Windows.Point(), window);
        check(p.X >= 0 && p.Y >= 0 && p.X + dot.ActualWidth <= window.ActualWidth && p.Y + dot.ActualHeight <= window.ActualHeight,
            "The green dot fits inside the compact window at the user's 188 by 38 size");
        check(vm.PrivacyDetailText.Contains("Camera"), "The privacy detail identifies which app uses the camera");
        await CaptureSutraAsync(window, output, "camera-dot-compact");
        vm.Settings.TopNotchMode = true; vm.ApplySettings(); window.ApplySettings(); await Idle();
        check(dot.IsVisible, "The green camera dot remains visible in top notch mode");
        vm.IsExpanded = true; window.ApplySettings(); await Idle();
        check(dot.Visibility == Visibility.Collapsed && ((FrameworkElement)((GraphiteIslandView)window.FindName("GraphiteDeck")).FindName("GraphitePrivacyActivity")).IsVisible,
            "Expansion presents the camera activity inside the expanded island");
        vm.IsExpanded = false; window.ApplySettings(); await Idle();
        check(dot.IsVisible, "Collapsing restores the compact camera dot");
        update(new PrivacySensorState([], ["Microphone app"]));
        await WaitForSutraAsync(() => !vm.ShowCameraInUse && vm.ShowMicInUse); await Idle();
        check(((SolidColorBrush)vm.PrivacyIndicatorBrush).Color == System.Windows.Media.Color.FromRgb(255, 159, 10), "Microphone-only activity uses the orange privacy color");
        update(new PrivacySensorState(["Camera"], ["Microphone app"]));
        await WaitForSutraAsync(() => vm.ShowCameraInUse && vm.ShowMicInUse); await Idle();
        check(vm.PrivacyCameraApps.Count == 1 && vm.PrivacyMicApps.Count == 1 && ((SolidColorBrush)vm.PrivacyIndicatorBrush).Color.G == 209,
            "Concurrent camera and microphone activity retains both sources and prioritizes green");
        vm.Settings.ShowPrivacyIndicators = false; vm.ApplySettings(); await Idle();
        check(!vm.ShowPrivacyInUse && dot.Visibility == Visibility.Collapsed, "The existing privacy-indicator setting still disables the dot");
        vm.Settings.ShowPrivacyIndicators = true; vm.ApplySettings(); await Idle();
        check(vm.ShowCameraInUse && dot.IsVisible, "Re-enabling privacy indicators restores the active camera dot immediately");
        update(PrivacySensorState.None);
        await WaitForSutraAsync(() => !vm.ShowPrivacyInUse); await Idle();
        check(dot.Visibility == Visibility.Collapsed, "Stopping sensor use removes the privacy dot");
    }
}
