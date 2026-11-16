using System.Diagnostics;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Flow;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyResponsivenessAsync(IslandViewModel vm, Action<bool, string> check, string output)
    {
        await VerifyBackgroundPersistenceAsync(vm, check, output);
        var log = new LoggingService();
        using var media = new MediaSessionService(log);
        using var timers = new TimerAlarmService(log);
        using var timerVm = new TimerAlarmViewModel(timers, false);
        var audio = new PaletteAudioFixture();
        var runner = new CommandRunner(vm, media, audio, timers, timerVm, new SutraPaletteHost(vm, null!));
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var palette = new CommandPaletteViewModel(runner, new Models.AppSettings(), () => save.Task);
        palette.Query = "volume";
        var watch = Stopwatch.StartNew();
        var execution = palette.ExecuteAsync();
        try
        {
            check(palette.IsVolumePanel && !execution.IsCompleted,
                "Volume panel appears while recent-command persistence is deliberately blocked");
            // Closing while saving must not resurrect a dismissed panel.
            palette.Reset();
        }
        finally { save.TrySetResult(); }
        await execution;
        check(!palette.HasPanel, "Completing a delayed save does not reopen a dismissed panel");

        const int rate = 16000, frameSize = 480;
        var speech = Enumerable.Range(0, frameSize).Select(i => (float)(.3 * Math.Sin(i * .2))).ToArray();
        var silence = new float[frameSize];
        var segmenter = new VoiceSegmenter(rate, TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(7));
        for (var i = 0; i < 20; i++) segmenter.Push(speech);
        var before = GC.GetAllocatedBytesForCurrentThread();
        // Capacity already covers these frames; appending must not allocate a copy per frame.
        for (var i = 0; i < 4; i++) segmenter.Push(speech);
        var appendBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(appendBytes == 0, $"Active speech frames append without temporary arrays (bytes={appendBytes})");
        float[]? result = null;
        for (var i = 0; i < 15; i++) result = segmenter.Push(silence);
        check(result is not null && result.Length == 39 * frameSize &&
            result.Take(24 * frameSize).Select((sample, i) => sample == speech[i % frameSize]).All(x => x) &&
            result.Skip(24 * frameSize).All(x => x == 0),
            "Speech segmentation preserves every sample, leading audio, and 450ms silence boundary");
        File.WriteAllText(Path.Combine(output, "responsiveness-metrics.txt"),
            $"Blocked-save panel check completed; active-frame temporary allocations: {appendBytes} bytes.\n");
    }

    private static async Task VerifyBackgroundPersistenceAsync(IslandViewModel vm, Action<bool, string> check, string output)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var log = new LoggingService();
        var persistence = new SettingsService(log);
        var preferences = new Models.AppSettings();
        await persistence.SaveAsync(preferences); // exclude first serializer JIT from the blocked-I/O check
        var writes = (OrderedBackgroundWork)typeof(SettingsService).GetField("_writes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = writes.Enqueue(() => release.Task);
        Task first = Task.CompletedTask, last = Task.CompletedTask;
        var watch = Stopwatch.StartNew();
        try
        {
            preferences.FontFamilyName = "snapshot-one";
            first = persistence.SaveAsync(preferences);
            preferences.FontFamilyName = "snapshot-two";
            last = persistence.SaveAsync(preferences);
            preferences.FontFamilyName = "not-saved";
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Input);
            check(!first.IsCompleted && !last.IsCompleted && watch.ElapsedMilliseconds < 250,
                $"Queued settings writes preserve input responsiveness while disk work is blocked ({watch.Elapsed.TotalMilliseconds:0.0}ms)");
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, last, blocker);
        check((await persistence.LoadAsync()).FontFamilyName == "snapshot-two",
            "Settings persist the last requested immutable snapshot rather than later unsaved mutations");

        var logWrites = (OrderedBackgroundWork)typeof(LoggingService).GetField("_writes", flags)!.GetValue(log)!;
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var logRelease = release;
        _ = logWrites.Enqueue(() => logRelease.Task);
        var marker = "performance-log-" + Guid.NewGuid().ToString("N");
        try
        {
            for (var i = 0; i < 40; i++) log.Info(marker + "-" + i.ToString("D2"));
            check(!log.FlushAsync().IsCompleted, "Logging queues every message without waiting for blocked disk work");
        }
        finally { release.TrySetResult(); }
        await log.FlushAsync();
        var lines = File.ReadAllLines(log.LogPath).Where(line => line.Contains(marker)).ToArray();
        check(lines.Length == 40 && lines.Select((line, i) => line.EndsWith(marker + "-" + i.ToString("D2"))).All(x => x),
            "Log flush retains all queued messages in event order");

        var timerPath = Path.Combine(output, "queued-timers.json");
        using (var timers = new TimerAlarmService(log, timerPath))
        {
            await timers.FlushAsync();
            var timerWrites = (OrderedBackgroundWork)typeof(TimerAlarmService).GetField("_writes", flags)!.GetValue(timers)!;
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var timerRelease = release;
            _ = timerWrites.Enqueue(() => timerRelease.Task);
            try
            {
                timers.StartTimer(TimeSpan.FromMinutes(3), "queued-timer");
                check(timers.State.Timers.Any(t => t.Label == "queued-timer") && !timers.FlushAsync().IsCompleted,
                    "Timer controls update immediately while persistence is blocked");
            }
            finally { release.TrySetResult(); }
        }
        check(TimerEngine.Deserialize(File.ReadAllText(timerPath)).Timers.Any(t => t.Label == "queued-timer"),
            "Timer disposal flushes the final state before returning");

        var invalidTimerPath = Path.Combine(output, "timer-target-is-directory");
        Directory.CreateDirectory(invalidTimerPath);
        using (var failingTimers = new TimerAlarmService(log, invalidTimerPath))
        {
            await failingTimers.FlushAsync();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(failingTimers.StorageWarning.Contains("could not be saved"),
                "A background timer write failure still reports its storage warning on the UI dispatcher");
        }

        // Compare binding fanout for identical streaming state, changing only response text.
        var session = (DynamicIsland.Q.Core.QSessionController)typeof(IslandViewModel).GetField("_qSession", flags)!.GetValue(vm)!;
        var sourceSnapshot = typeof(DynamicIsland.Q.Core.QSessionController).GetField("_snapshot", flags)!;
        var changed = typeof(IslandViewModel).GetMethod("OnQChanged", flags)!;
        var fullRefresh = typeof(IslandViewModel).GetMethod("RaiseQProperties", flags)!;
        var counts = new Dictionary<string, int>();
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        { if (e.PropertyName is { } name) counts[name] = counts.GetValueOrDefault(name) + 1; };
        var snapshot = session.Snapshot with { State = DynamicIsland.Q.Core.QRunState.Streaming, Response = "start" };
        sourceSnapshot.SetValue(session, snapshot); changed.Invoke(vm, [snapshot]);
        vm.PropertyChanged += handler;
        try
        {
            watch.Restart();
            for (var i = 0; i < 100; i++)
            {
                snapshot = snapshot with { Response = snapshot.Response + "x" };
                sourceSnapshot.SetValue(session, snapshot); changed.Invoke(vm, [snapshot]);
            }
            var narrowMs = watch.Elapsed.TotalMilliseconds;
            var narrowCount = counts.Values.Sum();
            check(counts.GetValueOrDefault(nameof(vm.QResponse)) == 100 && counts.GetValueOrDefault(nameof(vm.QSelectedModel)) == 0 && vm.QResponse == snapshot.Response,
                "Every streamed text update is retained without refreshing unchanged provider/model bindings");
            counts.Clear(); watch.Restart();
            for (var i = 0; i < 100; i++) fullRefresh.Invoke(vm, null);
            var broadMs = watch.Elapsed.TotalMilliseconds;
            var broadCount = counts.Values.Sum();
            check(narrowCount < broadCount, $"Text-only streaming reduces binding notifications ({narrowCount} vs {broadCount} for 100 chunks)");
            File.WriteAllText(Path.Combine(output, "streaming-metrics.txt"), $"100 synthetic UI updates: narrow={narrowMs:0.00}ms/{narrowCount} notifications; full={broadMs:0.00}ms/{broadCount} notifications. Not a provider latency or FPS measurement.");
            snapshot = snapshot with { State = DynamicIsland.Q.Core.QRunState.Complete };
            sourceSnapshot.SetValue(session, snapshot); counts.Clear(); changed.Invoke(vm, [snapshot]);
            check(counts.ContainsKey(nameof(vm.QState)) && vm.QCanCopyResponse && !vm.QCanStop,
                "Stream completion still refreshes full state and response actions");
        }
        finally { vm.PropertyChanged -= handler; session.Clear(); }
    }
}
