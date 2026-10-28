using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyNotificationListenerAsync(LoggingService log, Action<bool, string> check)
    {
        static NotificationInfo Note(uint id) => new("Delivery fixture", $"Message {id}", "Local synthetic notification",
            id, DateTimeOffset.UtcNow, "notification.delivery.fixture");
        static async Task Until(Func<bool> condition)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (!condition())
            {
                if (DateTimeOffset.UtcNow >= deadline) throw new InvalidOperationException("Notification delivery did not recover without UI interaction.");
                await Task.Delay(25);
            }
        }

        var nativeRead = new TaskCompletionSource<IReadOnlyList<NotificationInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeReadCount = 0;
        var reader = new NotificationSnapshotReader(() => { nativeReadCount++; return nativeRead.Task; });
        using (var timedOutWait = new CancellationTokenSource())
        {
            var firstWait = reader.ReadAsync(timedOutWait.Token);
            timedOutWait.Cancel();
            try { await firstWait; check(false, "Notifications: timed-out snapshot wait cancels"); }
            catch (OperationCanceledException) { }
            var retry = reader.ReadAsync(CancellationToken.None);
            check(nativeReadCount == 1 && !retry.IsCompleted,
                "Notifications: timeout retries reuse the pending Windows read instead of overlapping or restarting it");
            nativeRead.SetResult([Note(800)]);
            check((await retry).Single().Id == 800,
                "Notifications: a Windows read slower than the timeout still delivers its result on retry");
        }
        nativeRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var timedOutWait = new CancellationTokenSource())
        {
            var wait = reader.ReadAsync(timedOutWait.Token);
            timedOutWait.Cancel();
            try { await wait; } catch (OperationCanceledException) { }
            nativeRead.SetResult([Note(801)]);
            check((await reader.ReadAsync(CancellationToken.None)).Single().Id == 801 && nativeReadCount == 2,
                "Notifications: a snapshot completed between timeout and retry is delivered without starting another read");
        }
        nativeRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var followingRead = reader.ReadAsync(CancellationToken.None);
        check(nativeReadCount == 3, "Notifications: consuming a snapshot allows the next fresh Windows read");
        nativeRead.SetResult([Note(802)]);
        await followingRead;

        var failedReadCount = 0;
        var failedReader = new NotificationSnapshotReader(() => ++failedReadCount == 1
            ? Task.FromException<IReadOnlyList<NotificationInfo>>(new InvalidOperationException("Synthetic failed read"))
            : Task.FromResult<IReadOnlyList<NotificationInfo>>([Note(803)]));
        try { await failedReader.ReadAsync(CancellationToken.None); } catch (InvalidOperationException) { }
        check((await failedReader.ReadAsync(CancellationToken.None)).Single().Id == 803 && failedReadCount == 2,
            "Notifications: failed Windows operations are released so subsequent polls can recover");

        var dismissSource = new NotificationSourceFixture();
        using (var service = new NotificationListenerService(log, dismissSource))
        {
            var delivered = new List<NotificationInfo>();
            service.BatchReceived += (_, batch) => delivered.AddRange(batch);
            var original = Note(700);
            var copy = original with { Id = 701 };
            dismissSource.Snapshot = [original, copy, Note(702)];
            await service.StartAsync();
            check(delivered.Count == 2, "Dismissal: duplicate source IDs produce one banner and preserve unrelated notifications");
            service.Dismiss([original]);
            check(dismissSource.Dismissed.Select(n => n.Id).Order().SequenceEqual(new uint?[] { 700, 701 }),
                "Dismissal: removes the displayed toast and its duplicate IDs from Windows, leaving unrelated messages");
            dismissSource.RejectDismissal = true;
            service.Dismiss([original]);
            dismissSource.Snapshot = [original with { Body = "Late update" }, original with { Id = 703 }];
            await service.RetryAsync();
            check(delivered.Count == 2, "Dismissal: late reads and recreated copies stay suppressed after Retry even if Windows removal fails");
            dismissSource.Snapshot = [Note(704)];
            dismissSource.Added!();
            await Until(() => delivered.Count == 3);
            check(delivered[^1].Id == 704, "Dismissal: subsequent new messages still arrive");
        }

        var source = new NotificationSourceFixture { RejectSubscription = true };
        using (var service = new NotificationListenerService(log, source))
        {
            var delivered = new List<NotificationInfo>();
            service.BatchReceived += (_, batch) => delivered.AddRange(batch);
            service.Configure(TimeSpan.FromSeconds(1));
            await service.StartAsync();
            check(service.IsActive && service.Status.Message == "Connected (polling)",
                "Notifications: unsupported foreground event still starts connected fallback polling");
            source.Snapshot = [Note(1)];
            await Until(() => delivered.Count == 1);
            source.Snapshot = [.. source.Snapshot, Note(2)];
            await Until(() => delivered.Count == 2);
            check(service.SuccessfulPollCount >= 3 && source.SubscribeCount == 1,
                "Notifications: successive idle polls deliver arrivals without opening the island or repeatedly subscribing");
            check(delivered.Select(n => n.Id).SequenceEqual(new uint?[] { 1, 2 }),
                "Notifications: fallback snapshots deliver new messages once in order");
            source.HasAccess = false;
            await Until(() => service.Status.State == IntegrationState.PermissionRequired);
            source.Snapshot = [.. source.Snapshot, Note(3)]; source.HasAccess = true;
            await Until(() => delivered.Count == 3);
            check(service.IsActive && source.PermissionRequests == 0,
                "Notifications: restored Windows permission resumes polling without a prompt or manual retry");
            service.Stop();
            var stoppedReads = source.ReadCount;
            await Task.Delay(1100);
            check(source.ReadCount == stoppedReads && service.Status.State == IntegrationState.Disabled,
                "Notifications: explicitly disabling the listener stops background checks");
        }

        var stalled = new TaskCompletionSource<IReadOnlyList<NotificationInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        source = new NotificationSourceFixture { Read = _ => stalled.Task };
        using (var service = new NotificationListenerService(log, source, TimeSpan.FromMilliseconds(80)))
        {
            var delivered = new List<NotificationInfo>();
            service.BatchReceived += (_, batch) => delivered.AddRange(batch);
            await service.StartAsync();
            check(service.Status.State == IntegrationState.Error, "Notifications: a stalled Windows read has a bounded timeout");
            source.Read = null; source.Snapshot = [Note(4)];
            await Task.Run(() => source.Added!());
            await Until(() => delivered.Count == 1);
            check(service.IsActive, "Notifications: an event after a timeout can refresh immediately without a stuck polling gate");
            stalled.SetResult([Note(99)]); await Task.Delay(50);
            check(delivered.Count == 1 && delivered[0].Id == 4,
                "Notifications: late results from a timed-out read cannot replay stale arrivals");
        }

        source = new NotificationSourceFixture
        {
            Read = _ => Task.FromException<IReadOnlyList<NotificationInfo>>(new InvalidOperationException("Synthetic temporary Windows read failure"))
        };
        using (var service = new NotificationListenerService(log, source))
        {
            var delivered = 0; service.BatchReceived += (_, batch) => delivered += batch.Count;
            service.Configure(TimeSpan.FromSeconds(1)); await service.StartAsync();
            check(service.Status.State == IntegrationState.Error, "Notifications: transient read failure is reported");
            source.Read = null; source.Snapshot = [Note(5)];
            await Until(() => delivered == 1);
            check(service.IsActive, "Notifications: timer alone recovers a failed read without Retry or pointer activity");
        }

        var oldRead = new TaskCompletionSource<IReadOnlyList<NotificationInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRead = new TaskCompletionSource<IReadOnlyList<NotificationInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        source = new NotificationSourceFixture { Read = _ => oldRead.Task };
        using (var service = new NotificationListenerService(log, source))
        {
            var delivered = new List<NotificationInfo>(); service.BatchReceived += (_, batch) => delivered.AddRange(batch);
            var oldStart = service.StartAsync();
            var oldEvent = source.Added!;
            service.Stop();
            source.Read = _ => newRead.Task;
            var newStart = service.StartAsync();
            await oldStart;
            oldRead.SetResult([Note(98)]);
            oldEvent();
            for (var i = 0; i < 20; i++) source.Added!();
            await Task.Delay(50);
            check(source.ReadCount == 2, "Notifications: stop/restart isolates read ownership and coalesces concurrent events");
            source.Read = null; source.Snapshot = [Note(6)]; newRead.SetResult(source.Snapshot);
            await newStart; await Until(() => source.ReadCount == 3);
            check(delivered.Count == 1 && delivered[0].Id == 6,
                "Notifications: restarted listener ignores old events/results and keeps duplicate suppression");
        }
    }

    private sealed class NotificationSourceFixture : INotificationSource
    {
        public bool HasAccess { get; set; } = true;
        public bool RejectSubscription { get; init; }
        public IReadOnlyList<NotificationInfo> Snapshot { get; set; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<NotificationInfo>>>? Read { get; set; }
        public Action? Added { get; private set; }
        public int ReadCount { get; private set; }
        public int SubscribeCount { get; private set; }
        public int PermissionRequests { get; private set; }
        public List<NotificationInfo> Dismissed { get; } = [];
        public bool RejectDismissal { get; set; }
        public void Dismiss(NotificationInfo notification)
        {
            if (RejectDismissal) throw new InvalidOperationException("Synthetic dismissal failure");
            Dismissed.Add(notification);
        }
        public Task RequestAccessAsync() { PermissionRequests++; return Task.CompletedTask; }
        public Task<IReadOnlyList<NotificationInfo>> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(Snapshot);
        }
        public void Subscribe(Action added)
        {
            SubscribeCount++;
            if (RejectSubscription) throw new System.Runtime.InteropServices.COMException("Synthetic unpackaged event failure", unchecked((int)0x80070490));
            Added = added;
        }
        public void Unsubscribe() => Added = null;
    }
}
