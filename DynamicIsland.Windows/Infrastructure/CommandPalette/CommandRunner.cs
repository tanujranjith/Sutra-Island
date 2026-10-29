using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

/// <summary>Window-level actions the runner needs; implemented by App so the runner stays free
/// of window-lifetime concerns.</summary>
public interface ICommandPaletteHost
{
    void ShowSettings();
    void ShowScreenshotTool() { }
    void ShowTimerPanel();
    void ShowAlarmPanel();
    void ShowStopwatchPanel();
    void OpenQ(string? question, QMode mode, bool compare);
    void OpenChat(string? question, bool useScreen = false);
    void ShowSettingsSearch(string query);
    void RecenterIsland();
    Task OpenClipboardAsync();
    void ShowNotificationHistory();
    Task SetNotificationsAsync(bool enabled);
    Task OpenCommandPanelAsync(string command) => Task.CompletedTask;
}

public sealed record CommandResult(bool Close, string? Message = null, bool IsError = false, bool ShowAllCommands = false, PalettePanel Panel = PalettePanel.None);

/// <summary>Executes a parsed palette command against the island, media, timer, and Q services.
/// Errors return an explanatory message and keep the palette open.</summary>
public sealed class CommandRunner(
    IslandViewModel island,
    MediaSessionService media,
    IPaletteAudioControl audio,
    TimerAlarmService timers,
    TimerAlarmViewModel stopwatchViewModel,
    ICommandPaletteHost host,
    IPaletteSeekControl? seekControl = null,
    IPaletteClipboard? clipboard = null,
    IPaletteOutputControl? outputs = null)
{
    public IPaletteAudioControl Audio => audio;
    public IPaletteSeekControl Seek => seekControl ?? media;
    public IslandViewModel Island => island;
    public TimerAlarmViewModel Stopwatch => stopwatchViewModel;
    public TimerAlarmService Timers => timers;
    public MediaSessionService MediaService => media;
    public IPaletteClipboard? Clipboard => clipboard;
    public IPaletteOutputControl? Outputs => outputs ?? audio as IPaletteOutputControl;
    public Task SetMirroringAsync(bool enabled) => host.SetNotificationsAsync(enabled);
    public Task OpenAssistantPanelAsync(string command) => host.OpenCommandPanelAsync(command);
    public async Task<CommandResult> ExecuteAsync(CommandMatch match)
    {
        try
        {
            return await ExecuteCoreAsync(match).ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Fail($"Command failed: {ex.Message}");
        }
    }

    private async Task<CommandResult> ExecuteCoreAsync(CommandMatch match)
    {
        var panel = PalettePanelPolicy.For(match);
        if (panel != PalettePanel.None) return new CommandResult(Close: false, Panel: panel);
        var argument = match.Argument;
        switch (match.Command.Id)
        {
            case PaletteCommand.Screenshot:
                host.ShowScreenshotTool();
                return Ok();
            case PaletteCommand.Pause:
            {
                var state = island.Media;
                if (!state.HasSession) return Fail("No media is playing.");
                if (!state.CanPlayPause) return Fail("This source doesn't support play/pause.");
                if (state.PlaybackState == MediaPlaybackState.Paused) return Ok();
                await media.PauseAsync().ConfigureAwait(true);
                return Ok();
            }
            case PaletteCommand.Play:
            {
                var state = island.Media;
                if (!state.HasSession) return Fail("No media is playing.");
                if (!state.CanPlayPause) return Fail("This source doesn't support play/pause.");
                if (state.PlaybackState == MediaPlaybackState.Playing) return Ok();
                await media.PlayAsync().ConfigureAwait(true);
                return Ok();
            }
            case PaletteCommand.Next:
            {
                var state = island.Media;
                if (!state.HasSession) return Fail("No media is playing.");
                if (!state.CanNext) return Fail("This source doesn't support next track.");
                await media.NextAsync().ConfigureAwait(true);
                return Ok();
            }
            case PaletteCommand.Previous:
            {
                var state = island.Media;
                if (!state.HasSession) return Fail("No media is playing.");
                if (!state.CanPrevious) return Fail("This source doesn't support previous track.");
                await media.PreviousAsync().ConfigureAwait(true);
                return Ok();
            }
            case PaletteCommand.Seek:
            {
                var state = Seek.Current;
                if (!state.HasSession) return Fail("No media is playing.");
                if (!state.CanSeek || state.Duration <= TimeSpan.Zero)
                    return Fail("This source doesn't support seeking.");
                if (!CommandParsers.TryParseSeek(argument, out var offset, out var seekError))
                    return Fail(seekError!);
                await Seek.SeekByAsync(offset).ConfigureAwait(true);
                return Ok();
            }
            case PaletteCommand.Mute:
                if (audio.Current.Availability != AudioAvailability.Available)
                    return Fail("System audio is unavailable.");
                audio.SetMuted(true);
                return Ok();
            case PaletteCommand.Unmute:
                if (audio.Current.Availability != AudioAvailability.Available)
                    return Fail("System audio is unavailable.");
                audio.SetMuted(false);
                return Ok();

            case PaletteCommand.Volume:
                if (audio.Current.Availability != AudioAvailability.Available) return Fail("System audio is unavailable.");
                if (!double.TryParse(argument, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var level) || !double.IsFinite(level)) return Fail("Use volume 50, volume +10, or volume -10.");
                var relative = argument.StartsWith('+') || argument.StartsWith('-');
                if (!relative && (level < 0 || level > 100)) return Fail("Volume must be between 0 and 100.");
                if (relative) audio.AdjustMasterVolume((int)Math.Clamp(Math.Round(level), -100, 100)); else audio.SetMasterVolume((int)Math.Round(level));
                return Ok();
            case PaletteCommand.OpenMedia:
                if (!island.Media.HasSession) return Fail("No media app is active.");
                media.LaunchSource(); return Ok();
            case PaletteCommand.Recenter: host.RecenterIsland(); return Ok();
            case PaletteCommand.Clipboard: await host.OpenClipboardAsync(); return Ok();
            case PaletteCommand.NotificationHistory: host.ShowNotificationHistory(); return Ok();
            case PaletteCommand.NotificationsOn: await host.SetNotificationsAsync(true); return Ok();
            case PaletteCommand.NotificationsOff: await host.SetNotificationsAsync(false); return Ok();
            case PaletteCommand.RetryNotifications: island.RetryNotificationsCommand.Execute(null); return Ok();
            case PaletteCommand.DismissBanner: island.DismissVisibleBanner(); return Ok();
            case PaletteCommand.Chat:
                if (!TryStartQ(out var chatError)) return Fail(chatError!);
                host.OpenChat(argument); return Ok();
            case PaletteCommand.VoiceInput:
                if (!TryStartQ(out var voiceError)) return Fail(voiceError!);
                if (!island.QSpeechAvailable) return Fail("Install an English Windows speech recognizer and allow microphone access. Typed chat remains available.");
                host.OpenChat(null); _ = island.StartVoiceAsync(); return Ok();
            case PaletteCommand.StopAssistant: island.StopAssistant(); return Ok();
            case PaletteCommand.ReadReply:
                if (string.IsNullOrWhiteSpace(island.QResponse)) return Fail("There is no reply to read yet.");
                _ = island.ReadReplyAsync(); return Ok();
            case PaletteCommand.WakeOn: await island.SetWakeEnabledAsync(true); return Ok();
            case PaletteCommand.WakeOff: await island.SetWakeEnabledAsync(false); return Ok();
            case PaletteCommand.AskScreen:
                if (!TryStartQ(out var screenError)) return Fail(screenError!);
                host.OpenChat(argument, useScreen: true); return Ok();
            case PaletteCommand.SearchSettings: host.ShowSettingsSearch(argument); return Ok();
            case PaletteCommand.Ask:
                if (!TryStartQ(out var askError)) return Fail(askError!);
                host.OpenChat(string.IsNullOrWhiteSpace(argument) ? null : argument);
                return Ok();
            case PaletteCommand.Say:
                if (!TryStartQ(out var sayError)) return Fail(sayError!);
                host.OpenQ(null, QMode.Say, compare: false);
                return Ok();
            case PaletteCommand.Compare:
                if (!TryStartQ(out var compareError)) return Fail(compareError!);
                if (string.IsNullOrWhiteSpace(argument))
                    return Fail("Add what to compare, e.g. compare train or drive.");
                host.OpenQ(argument, QMode.Ask, compare: true);
                return Ok();

            case PaletteCommand.Timer:
                if (string.IsNullOrWhiteSpace(argument))
                {
                    host.ShowTimerPanel();
                    return Ok();
                }
                if (!CommandParsers.TryParseTimerArgument(argument, out var duration, out var label))
                    return Fail("Couldn't read that duration — try e.g. timer 10 minutes for laundry.");
                if (duration.TotalSeconds < 1 || duration.TotalHours > 24)
                    return Fail("Timer must be between 1 second and 24 hours.");
                timers.StartTimer(duration, label);
                return Ok();
            case PaletteCommand.PauseTimer:
            {
                if (ResolveTimer(argument, out var pauseTarget, out var pauseError) is null)
                    return Fail(pauseError!);
                if (pauseTarget.Phase != TimerPhase.Running)
                    return Fail(string.IsNullOrWhiteSpace(argument)
                        ? "No timer is running."
                        : $"Timer '{pauseTarget.Label}' isn't running.");
                timers.PauseTimer(pauseTarget.Id);
                return Ok();
            }
            case PaletteCommand.ResumeTimer:
            {
                if (ResolveTimer(argument, out var resumeTarget, out var resumeError) is null)
                    return Fail(resumeError!);
                if (resumeTarget.Phase != TimerPhase.Paused)
                    return Fail(string.IsNullOrWhiteSpace(argument)
                        ? "No timer is paused."
                        : $"Timer '{resumeTarget.Label}' isn't paused.");
                timers.ResumeTimer(resumeTarget.Id);
                return Ok();
            }
            case PaletteCommand.CancelTimer:
            {
                if (ResolveTimer(argument, out var cancelTarget, out var cancelError) is null)
                    return Fail(cancelError!);
                timers.CancelTimer(cancelTarget.Id);
                return Ok();
            }
            case PaletteCommand.ShowTimers:
                host.ShowTimerPanel();
                return Ok();

            case PaletteCommand.Alarm:
                if (string.IsNullOrWhiteSpace(argument))
                {
                    host.ShowAlarmPanel();
                    return Ok();
                }
                if (!CommandParsers.TryParseAlarmTime(argument, out var alarmHour, out var alarmMinute,
                        out var alarmUse24, out var alarmRepeat, out var alarmError))
                    return Fail(alarmError!);
                timers.SetAlarm(alarmHour, alarmMinute, alarmUse24, label: null, repeat: alarmRepeat);
                return Ok();
            case PaletteCommand.CancelAlarm:
            {
                if (string.IsNullOrWhiteSpace(argument))
                    return Fail("Add a time, e.g. cancel alarm 7:30 AM.");
                if (!CommandParsers.TryParseAlarmTime(argument, out var cancelHour, out var cancelMinute,
                        out _, out _, out var cancelAlarmError))
                    return Fail(cancelAlarmError!);
                var alarm = timers.State.Alarms
                    .OrderBy(a => a.Phase == AlarmPhase.Ringing || a.Phase == AlarmPhase.Snoozed ? 0 : 1)
                    .ThenBy(a => a.Phase == AlarmPhase.Scheduled ? 0 : 1)
                    .FirstOrDefault(a => a.Hour == cancelHour && a.Minute == cancelMinute);
                if (alarm is null)
                    return Fail($"No alarm set for {cancelHour:D2}:{cancelMinute:D2}.");
                timers.DeleteAlarm(alarm.Id);
                return Ok();
            }
            case PaletteCommand.ShowAlarms:
                host.ShowAlarmPanel();
                return Ok();

            case PaletteCommand.StartStopwatch:
                if (stopwatchViewModel.IsStopwatchRunning) return Ok();
                stopwatchViewModel.StopwatchPrimaryCommand.Execute(null);
                host.ShowStopwatchPanel();
                return Ok();
            case PaletteCommand.PauseStopwatch:
                if (!stopwatchViewModel.IsStopwatchRunning)
                    return Fail("Stopwatch isn't running.");
                stopwatchViewModel.StopwatchPrimaryCommand.Execute(null);
                host.ShowStopwatchPanel();
                return Ok();
            case PaletteCommand.ResumeStopwatch:
                if (stopwatchViewModel.IsStopwatchRunning) return Ok();
                if (stopwatchViewModel.StopwatchPrimaryText == "Start")
                    return Fail("Stopwatch hasn't started yet.");
                stopwatchViewModel.StopwatchPrimaryCommand.Execute(null);
                host.ShowStopwatchPanel();
                return Ok();
            case PaletteCommand.ResetStopwatch:
                if (!stopwatchViewModel.IsStopwatchRunning &&
                    stopwatchViewModel.StopwatchPrimaryText == "Start")
                    return Ok();
                stopwatchViewModel.StopwatchResetCommand.Execute(null);
                host.ShowStopwatchPanel();
                return Ok();

            case PaletteCommand.FocusOn:
                island.SetFocusMode(true);
                return Ok();
            case PaletteCommand.FocusOff:
                island.SetFocusMode(false);
                return Ok();
            case PaletteCommand.Expand:
                island.IsExpanded = true;
                return Ok();
            case PaletteCommand.Collapse:
                island.IsExpanded = false;
                return Ok();
            case PaletteCommand.OpenSettings:
                if (string.IsNullOrWhiteSpace(argument)) host.ShowSettings(); else host.ShowSettingsSearch(argument);
                return Ok();

            case PaletteCommand.Convert:
                return UnitConversionParser.TryConvert(argument, out var converted, out var conversionError)
                    ? new CommandResult(Close: false, Message: $"Result: {converted}")
                    : Fail(conversionError!);

            case PaletteCommand.Help:
                return new CommandResult(Close: false,
                    Message: "All commands — ↑↓ to browse, Enter to run.", ShowAllCommands: true);

            default:
                return Fail("Unknown command — type help to see examples.");
        }
    }

    public string? Availability(PaletteCommand command) => command switch
    {
        PaletteCommand.Seek when !Seek.Current.HasSession => "No active media",
        PaletteCommand.Seek when !Seek.Current.CanSeek || Seek.Current.Duration <= TimeSpan.Zero => "This source does not support seeking",
        PaletteCommand.Play or PaletteCommand.Pause or PaletteCommand.Next or PaletteCommand.Previous or PaletteCommand.OpenMedia when !island.Media.HasSession => "No active media",
        PaletteCommand.Volume or PaletteCommand.Mute or PaletteCommand.Unmute when audio.Current.Availability != AudioAvailability.Available => "System audio unavailable",
        PaletteCommand.VoiceInput when !island.QSpeechAvailable => "Windows speech language or microphone required",
        PaletteCommand.ReadReply when string.IsNullOrWhiteSpace(island.QResponse) => "No reply yet",
        PaletteCommand.Chat or PaletteCommand.Ask or PaletteCommand.AskScreen or PaletteCommand.VoiceInput when !island.Settings.QEnabled => "Assistant disabled",
        _ => null
    };
    private bool TryStartQ(out string? error)
    {
        if (!island.Settings.QEnabled)
        {
            error = "Sutra Assistant is disabled in Settings.";
            return false;
        }
        error = null;
        return true;
    }

    private TimerState? ResolveTimer(string name, out TimerState timer, out string? error)
    {
        timer = null!;
        error = null;
        var state = timers.State;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (state.Timers.Count == 0)
            {
                error = "No timer found.";
                return null;
            }
            timer = state.Timer;
            return timer;
        }
        var exact = state.Timers
            .Where(t => string.Equals(t.Label, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1) { timer = exact[0]; return timer; }
        if (exact.Count > 1)
        {
            error = $"Multiple timers are named '{name}'.";
            return null;
        }
        var partial = state.Timers
            .Where(t => t.Label.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (partial.Count == 1) { timer = partial[0]; return timer; }
        if (partial.Count > 1)
        {
            error = $"Multiple timers match '{name}' — use the exact label.";
            return null;
        }
        error = $"No timer named '{name}'.";
        return null;
    }

    private static CommandResult Ok() => new(Close: true);
    private static CommandResult Fail(string message) => new(Close: false, Message: message, IsError: true);
}
