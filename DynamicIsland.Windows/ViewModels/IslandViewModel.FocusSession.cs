namespace DynamicIsland.Windows.ViewModels;

public sealed partial class IslandViewModel
{
    private readonly System.Windows.Threading.DispatcherTimer _focusSessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    public bool FocusSessionActive => Settings.FocusSessionEndsAt is not null;
    public string FocusSessionRemaining
    {
        get
        {
            var remaining = Settings.FocusSessionEndsAt is { } end ? end - DateTimeOffset.UtcNow : TimeSpan.Zero;
            var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
    private void InitializeFocusSession()
    {
        _focusSessionTimer.Tick += (_, _) =>
        {
            if (Settings.FocusSessionEndsAt <= DateTimeOffset.UtcNow) EndFocusSession();
            else RaisePropertyChanged(nameof(FocusSessionRemaining));
        };
        if (Settings.FocusSessionEndsAt is not { } end) return;
        if (end <= DateTimeOffset.UtcNow) EndFocusSession();
        else { Settings.FocusModeEnabled = true; _focusSessionTimer.Start(); }
    }
    public void StartFocusSession(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromHours(24)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!FocusSessionActive) Settings.FocusSessionRestoreEnabled = Settings.FocusModeEnabled;
        Settings.FocusSessionEndsAt = DateTimeOffset.UtcNow + duration;
        SetFocusMode(true); _focusSessionTimer.Start();
        _ = PersistSettingsAsync();
        RaiseMany(nameof(FocusSessionActive), nameof(FocusSessionRemaining));
    }
    public void EndFocusSession(bool restore = true)
    {
        if (!FocusSessionActive) return;
        var prior = Settings.FocusSessionRestoreEnabled;
        Settings.FocusSessionEndsAt = null; _focusSessionTimer.Stop();
        if (restore) SetFocusMode(prior);
        _ = PersistSettingsAsync();
        RaiseMany(nameof(FocusSessionActive), nameof(FocusSessionRemaining));
    }
}
