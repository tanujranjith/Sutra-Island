using System.Windows;
using System.Windows.Input;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyCommandPanelsAsync(CommandPaletteViewModel vm, CommandPaletteWindow window,
        PaletteAudioFixture audio, TimerAlarmService timers, Action<bool, string> check, string output)
    {
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        void Click(string name) => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        vm.Query = "vo";
        check(vm.Suggestions.Count(x => x.Command?.Id == PaletteCommand.Volume) == 1 && vm.Suggestions.All(x => x.Argument != "ShowVolume"), "Volume search shows one audio action without the duplicate visibility setting");
        check(vm.Suggestions.First(x => x.Command?.Id == PaletteCommand.Volume).Glyph == "\uE767", "Volume command has a speaker icon instead of a generic triangle");
        vm.Query = "find setting volume";
        check(vm.Suggestions.Any(x => x.Argument == "ShowVolume" && x.Name == "Show volume in island"), "Explicit settings search retains the separately named volume visibility preference");

        vm.Query = "volume"; window.Show(); await vm.ExecuteAsync(); await Layout();
        var slider = (System.Windows.Controls.Slider)window.FindName("VolumeSlider");
        check(vm.IsVolumePanel && ((FrameworkElement)window.FindName("VolumePanel")).IsVisible && slider.Value == 42,
            "Bare volume Enter opens its custom panel at the actual volume level");
        check(window.ActualWidth <= 460 && window.ActualHeight < 340 && slider.ActualWidth >= 300,
            "Volume controls fit a compact card while retaining a usable slider target");
        check(!((FrameworkElement)window.FindName("SuggestionList")).IsVisible && !((FrameworkElement)window.FindName("QueryBox")).IsVisible,
            "Command panel replaces the search results instead of displaying an argument error");
        slider.Value = 58;
        check(audio.Current.MasterVolumePercent == 58 && vm.VolumeValueText == "58%", "Volume slider writes live audio level and updates the percentage");
        await Task.Run(() => audio.Emit(audio.Current with { MasterVolumePercent = 63 })); await Layout();
        check(slider.Value == 63, "Hardware volume changes synchronize into the open panel through the dispatcher");
        Click("VolumePreset25"); check(audio.Current.MasterVolumePercent == 25, "Volume preset button changes the level");
        Click("VolumeMuteButton"); check(audio.Current.SystemMuted && vm.VolumeMuteText == "Unmute", "Volume mute action reflects the current mute state");
        await Layout(); await CaptureSutraAsync(window, output, "command-volume");
        audio.Emit(audio.Current with { Availability = AudioAvailability.Unsupported }); await Layout();
        check(!slider.IsEnabled && !((System.Windows.Controls.Button)window.FindName("VolumeMuteButton")).IsEnabled && vm.VolumeStateText.Contains("unavailable"), "Unavailable audio disables controls with an actionable status");
        audio.Emit(audio.Current with { Availability = AudioAvailability.Available, SystemMuted = false });
        window.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        check(!vm.HasPanel && vm.Query == "volume" && window.IsVisible && audio.Subscribers == 0, "Escape returns to the same query and detaches live audio updates");
        await Layout();
        check(window.ActualWidth == 640, "Returning to search restores the full command palette width");
        await vm.ExecuteAsync(); window.Hide();
        check(!vm.HasPanel && audio.Subscribers == 0, "Dismissing the palette releases panel subscriptions");
        vm.Query = "volume 50"; await vm.ExecuteAsync();
        check(!vm.HasPanel && audio.Current.MasterVolumePercent == 50, "Volume with an explicit level still executes directly");
        vm.Query = "volume +10"; await vm.ExecuteAsync();
        check(audio.Current.MasterVolumePercent == 60, "Signed volume commands retain relative adjustment behavior");

        vm.Query = "timer"; window.Show(); await vm.ExecuteAsync(); await Layout();
        var count = timers.State.Timers.Count;
        vm.TimerMinutes = "0"; await vm.SubmitPanelAsync();
        check(vm.HasPanel && vm.IsStatusError && timers.State.Timers.Count == count, "Timer form rejects invalid duration without creating a timer");
        ((System.Windows.Controls.TextBox)window.FindName("TimerMinutesBox")).Text = "25";
        ((System.Windows.Controls.TextBox)window.FindName("TimerLabelBox")).Text = "Tea";
        await Layout(); await CaptureSutraAsync(window, output, "command-timer");
        await vm.SubmitPanelAsync();
        var timer = timers.State.Timers.Last();
        check(!window.IsVisible && timer.TotalSeconds == 1500 && timer.Label == "Tea", "Timer form creates the selected duration and label then dismisses the palette");
        vm.Query = "timer cancel Tea"; window.Show(); await vm.ExecuteAsync();
        check(!timers.State.Timers.Any(t => t.Id == timer.Id) && !vm.IsStatusError,
            "Timer cancel with a label selects the cancel action and removes that timer");
        timers.StartTimer(TimeSpan.FromMinutes(2), "Quick");
        var quickTimer = timers.State.Timer;
        vm.Query = "timer cancel"; window.Show(); await vm.ExecuteAsync();
        check(!timers.State.Timers.Any(t => t.Id == quickTimer.Id) && !vm.IsStatusError,
            "Timer cancel without a label cancels the selected timer");

        vm.Query = "alarm"; window.Show(); await vm.ExecuteAsync(); await Layout();
        count = timers.State.Alarms.Count;
        vm.AlarmTime = "25:99"; await vm.SubmitPanelAsync();
        check(vm.HasPanel && vm.IsStatusError && timers.State.Alarms.Count == count, "Alarm form rejects invalid times without creating an alarm");
        ((System.Windows.Controls.TextBox)window.FindName("AlarmTimeBox")).Text = "7:30 AM";
        vm.AlarmRepeat = "Weekdays";
        await Layout(); await CaptureSutraAsync(window, output, "command-alarm");
        await vm.SubmitPanelAsync();
        var alarm = timers.State.Alarms.Last();
        check(!window.IsVisible && alarm.Hour == 7 && alarm.Minute == 30 && alarm.Repeat == AlarmRepeat.Weekdays,
            "Alarm form creates the selected time and recurrence then dismisses the palette");
        timers.DeleteAlarm(alarm.Id);
        window.PrepareForShow();
        check(!vm.HasPanel && vm.Query == "", "Reopening the palette starts with clean search rather than a stale command panel");
    }
    private sealed class PaletteAudioFixture : IPaletteAudioControl
    {
        private EventHandler<AudioState>? _changed;
        public int Subscribers { get; private set; }
        public event EventHandler<AudioState>? Changed { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public AudioState Current { get; private set; } = new() { MasterVolumePercent = 42, OutputDeviceName = "Speakers · System output" };
        public void Emit(AudioState state) { Current = state; _changed?.Invoke(this, state); }
        public void SetMasterVolume(int percent) => Emit(Current with { MasterVolumePercent = Math.Clamp(percent, 0, 100) });
        public void AdjustMasterVolume(int delta) => SetMasterVolume(Current.MasterVolumePercent + delta);
        public void SetMuted(bool muted) => Emit(Current with { SystemMuted = muted });
        public void ToggleMuted() => SetMuted(!Current.SystemMuted);
    }
}
