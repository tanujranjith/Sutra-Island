using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using Button = System.Windows.Controls.Button;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyNotificationClicksAsync(AppSettings settings, IslandViewModel vm,
        IslandWindow window, Action<bool, string> check)
    {
        var dismiss = (Button)window.FindName("NotifDismissButton");
        var host = (NotificationMorphHost)window.FindName("NotifBanner");
        void Invoke(string name, params object?[] args) => typeof(IslandViewModel)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);
        settings.AnimationIntensity = AnimationIntensity.Reduced;
        settings.ShowNotifications = true;
        vm.InteractionProtected = false;
        vm.ApplySettings(); window.ApplySettings();
        for (uint attempt = 0; attempt < 3; attempt++)
        {
            Keyboard.ClearFocus();
            vm.InteractionProtected = false;
            var note = new NotificationInfo("Click fixture", $"Dismiss this notification {attempt}", "Focus must not cancel the click",
                8000 + attempt, DateTimeOffset.UtcNow, "click.fixture");
            Invoke("OnNotificationBatch", null, new NotificationInfo[] { note }); window.UpdateLayout();
            check(vm.ShowNotification && host.Visibility == Visibility.Visible, $"Click {attempt + 1}: banner is visible before focus");
            // WPF focuses/activates the window before dispatching the mouse press. Route
            // those events through the real control, rather than calling its command.
            dismiss.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, dismiss)
                { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            check(vm.InteractionProtected && vm.ShowNotification && host.Visibility == Visibility.Visible,
                $"Click {attempt + 1}: focusing X keeps the existing banner clickable");
            dismiss.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(!vm.ShowNotification && host.Visibility == Visibility.Collapsed,
                $"Click {attempt + 1}: the real preview mouse route dismisses the banner");
            vm.InteractionProtected = false;
            await Task.Delay(1100);
            Invoke("PumpNotifications");
            check(!vm.ShowNotification && host.Visibility == Visibility.Collapsed,
                $"Click {attempt + 1}: releasing focus and the next timer tick cannot revive the banner");
            Invoke("OnNotificationBatch", null, new NotificationInfo[] { note });
            check(!vm.ShowNotification, $"Click {attempt + 1}: rereading the same toast stays dismissed");
        }
        Keyboard.ClearFocus();
        vm.InteractionProtected = false;
        var first = new NotificationInfo("Click fixture", "Keyboard dismissal", "First", 8100, DateTimeOffset.UtcNow, "click.fixture");
        var next = first with { Id = 8101, Title = "New notification during click", Body = "Second" };
        Invoke("OnNotificationBatch", null, new NotificationInfo[] { first });
        dismiss.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, dismiss)
            { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        Invoke("OnNotificationBatch", null, new NotificationInfo[] { next });
        check(vm.NotificationTitle == first.Title && vm.ShowNotification, "Click: a new arrival queues while the displayed banner has focus");
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!vm.ShowNotification, "Click: keyboard/button activation dismisses the focused notification");
        vm.InteractionProtected = false; Invoke("PumpNotifications");
        check(vm.ShowNotification && vm.NotificationTitle == next.Title, "Click: a genuinely new queued notification appears after dismissal");
        vm.TimerEditorOpen = true;
        check(!vm.ShowNotification, "Click: opening the timer editor still suppresses ordinary banners");
        vm.TimerEditorOpen = false;
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.ClearNotificationHistoryCommand.Execute(null);
    }
}
