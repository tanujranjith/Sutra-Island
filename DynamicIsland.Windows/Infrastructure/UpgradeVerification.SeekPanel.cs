using System.Windows;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifySeekPanelAsync(CommandPaletteViewModel vm, CommandPaletteWindow window,
        PaletteSeekFixture media, Action<bool, string> check, string output)
    {
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        vm.Query = "seek"; window.Show(); await vm.ExecuteAsync(); await Layout();
        var slider = (System.Windows.Controls.Slider)window.FindName("SeekSlider");
        check(vm.IsSeekPanel && window.ActualWidth == 440 && slider.Value == 74 && slider.Maximum == 240 && media.Requests == 0,
            "Bare seek opens a compact slider at the current track position without seeking on open");
        check(vm.SeekTrackTitle == "Midnight City" && vm.SeekTrackSubtitle == "M83" && vm.SeekElapsedText == "1:14" && vm.SeekDurationText == "4:00",
            "Seek card displays the track, artist, elapsed time and duration");
        slider.Value = 120; await Layout();
        check(media.Current.Position.TotalSeconds == 120 && vm.SeekElapsedText == "2:00", "Seek slider sends an absolute playback position");
        await Task.Run(() => media.Emit(media.Current with { Position = TimeSpan.FromSeconds(121) })); await Layout();
        check(slider.Value == 121, "Playback progress updates the open seek card through the dispatcher");
        var requests = media.Requests;
        vm.BeginSeekDrag(); slider.Value = 180;
        media.Emit(media.Current with { Position = TimeSpan.FromSeconds(122) }); await Layout();
        check(slider.Value == 180 && media.Requests == requests, "Scrubbing previews the chosen time without being overwritten by playback polling");
        vm.EndSeekDrag(); await Layout();
        check(media.Requests == requests + 1 && media.Current.Position.TotalSeconds == 180, "Releasing the scrubber commits a single seek");
        ((System.Windows.Controls.Button)window.FindName("SeekForwardButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        check(media.Current.Position.TotalSeconds == 190, "Seek card skip-forward button uses the current position");
        slider.Value = 10000;
        check(media.Current.Position.TotalSeconds == 240, "Seek slider clamps requests to the track duration");

        media.HoldNextSeek = true; requests = media.Requests;
        slider.Value = 120; slider.Value = 130; slider.Value = 140;
        media.ReleaseSeek(); await WaitForSutraAsync(() => media.Current.Position.TotalSeconds == 140); await Layout();
        check(media.Requests == requests + 2, "While a seek is in flight, rapid adjustments coalesce to the latest target");
        await CaptureSutraAsync(window, output, "command-seek");
        media.Emit(media.Current with { CanSeek = false }); await Layout();
        check(!slider.IsEnabled && vm.ShowSeekStatus && !((System.Windows.Controls.Button)window.FindName("SeekForwardButton")).IsEnabled,
            "Unsupported media disables the seek controls with an explanation");
        media.Emit(MediaInfo.Empty); await Layout();
        check(!slider.IsEnabled && vm.SeekTrackTitle == "Nothing playing" && vm.SeekElapsedText == "0:00", "Closing the media app clears the seek display safely");
        media.Emit(PaletteSeekFixture.Track with { Title = "Long track", Duration = TimeSpan.FromMinutes(90), Position = TimeSpan.FromMinutes(65) }); await Layout();
        check(vm.SeekTrackTitle == "Long track" && vm.SeekElapsedText == "1:05:00" && vm.SeekDurationText == "1:30:00", "Changing tracks refreshes the title, duration and hour-long timestamps");
        media.Emit(PaletteSeekFixture.Track); await Layout();
        media.HoldNextSeek = true; requests = media.Requests;
        slider.Value = 150; slider.Value = 160; window.Hide(); media.ReleaseSeek();
        await WaitForSutraAsync(() => media.Current.Position.TotalSeconds == 150); await Layout();
        check(media.Subscribers == 0 && media.Requests == requests + 1 && !vm.HasPanel, "Closing seek detaches progress updates and drops queued stale requests");
        vm.Query = "seek forward 10 seconds"; await vm.ExecuteAsync();
        check(media.LastOffset == TimeSpan.FromSeconds(10) && !vm.HasPanel, "Existing seek commands with arguments continue to execute directly");
        vm.Query = "forward 30 seconds"; await vm.ExecuteAsync();
        check(media.LastOffset == TimeSpan.FromSeconds(30) && !vm.HasPanel, "Forward 30 seconds seeks forward directly");
        vm.Query = "back 30 seconds"; await vm.ExecuteAsync();
        check(media.LastOffset == TimeSpan.FromSeconds(-30) && !vm.HasPanel, "Back 30 seconds seeks backward directly");
        window.PrepareForShow();
    }
    private sealed class PaletteSeekFixture : IPaletteSeekControl
    {
        public static MediaInfo Track => new() { Title = "Midnight City", Artist = "M83", SourceAppId = "fixture.music", SourceAppName = "Music", PlaybackState = MediaPlaybackState.Playing, CanSeek = true, Duration = TimeSpan.FromMinutes(4), Position = TimeSpan.FromSeconds(74) };
        private EventHandler<MediaInfo>? _changed;
        private TaskCompletionSource? _release;
        public bool HoldNextSeek { get; set; }
        public int Requests { get; private set; }
        public int Subscribers { get; private set; }
        public TimeSpan LastOffset { get; private set; }
        public MediaInfo Current { get; private set; } = Track;
        public event EventHandler<MediaInfo>? Changed { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public void Emit(MediaInfo info) { Current = info; _changed?.Invoke(this, info); }
        public void ReleaseSeek() { _release?.TrySetResult(); }
        public async Task SeekFractionAsync(double fraction)
        {
            Requests++;
            if (HoldNextSeek) { HoldNextSeek = false; _release = new(TaskCreationOptions.RunContinuationsAsynchronously); await _release.Task; }
            Emit(Current with { Position = TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * Current.Duration.TotalSeconds) });
        }
        public Task SeekByAsync(TimeSpan offset)
        { LastOffset = offset; Emit(Current with { Position = SeekMath.ClampSeek(Current.Position, offset, Current.Duration) }); return Task.CompletedTask; }
    }
}
