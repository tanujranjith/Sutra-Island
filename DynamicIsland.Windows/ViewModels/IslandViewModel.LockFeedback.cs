using System.Windows.Threading;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class IslandViewModel
{
    private readonly DispatcherTimer _lockFeedbackTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTimeOffset _lockFeedbackUntil;
    private string _lockFeedbackText = "";
    public bool IsLockBannerActive => DateTimeOffset.UtcNow < _lockFeedbackUntil && !HasUrgentAlert;
    public void ShowKeyboardLock(string key, bool enabled) => OnUi(() =>
    {
        if (key == "Caps Lock" ? !Settings.CapsLockNotifications : !Settings.NumLockNotifications) return;
        _lockFeedbackText = $"{key} {(enabled ? "On" : "Off")}";
        _lockFeedbackUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        _ordinaryBannerPaused = true;
        _lockFeedbackTimer.Stop();
        _lockFeedbackTimer.Tick -= LockFeedbackExpired;
        _lockFeedbackTimer.Tick += LockFeedbackExpired;
        _lockFeedbackTimer.Start();
        _bannerSeq++;
        RefreshLockFeedback();
    });
    private void LockFeedbackExpired(object? sender, EventArgs e)
    { _lockFeedbackTimer.Stop(); _lockFeedbackUntil = default; RefreshLockFeedback(); PumpNotifications(); }
    private void RefreshLockFeedback() => RaiseMany(nameof(IsLockBannerActive), nameof(ShowNotification), nameof(ShowBanner),
        nameof(BannerApp), nameof(BannerTitle), nameof(BannerBody), nameof(BannerSeq));
}
