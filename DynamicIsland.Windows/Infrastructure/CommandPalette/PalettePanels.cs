using System.Globalization;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public enum PalettePanel { None, Volume, Timer, Alarm, Seek, Stopwatch, Focus, Clipboard, Notifications, Convert, AudioOutput }

public interface IPaletteClipboard
{
    IntegrationStatus Status { get; }
    Task<IReadOnlyList<ClipboardEntry>> ReadPaletteAsync(int max);
    bool CopyText(string text);
    bool CopyEntry(ClipboardEntry entry);
    void ReleasePaletteEntries();
}

public interface IPaletteOutputControl
{
    Task<IReadOnlyList<AudioOutputDevice>> GetPaletteOutputsAsync();
    Task SwitchPaletteOutputAsync(string id);
}

public interface IPaletteSeekControl
{
    MediaInfo Current { get; }
    event EventHandler<MediaInfo>? Changed;
    Task SeekFractionAsync(double fraction);
    Task SeekByAsync(TimeSpan offset);
}

public interface IPaletteAudioControl
{
    AudioState Current { get; }
    event EventHandler<AudioState>? Changed;
    void SetMasterVolume(int percent);
    void AdjustMasterVolume(int delta);
    void SetMuted(bool muted);
    void ToggleMuted();
}

public interface IPaletteVerifiedAudioControl
{
    Task<AudioState> ApplyVerifiedAudioAsync(int? absolute, int? delta, bool? muted, CancellationToken token);
}

public static class PalettePanelPolicy
{
    public static PalettePanel For(CommandMatch match) => !string.IsNullOrWhiteSpace(match.Argument)
        ? PalettePanel.None : match.Command.Id switch
        {
            PaletteCommand.Volume => PalettePanel.Volume,
            PaletteCommand.Seek => PalettePanel.Seek,
            PaletteCommand.Timer => PalettePanel.Timer,
            PaletteCommand.Alarm => PalettePanel.Alarm,
            PaletteCommand.Stopwatch => PalettePanel.Stopwatch,
            PaletteCommand.Focus => PalettePanel.Focus,
            PaletteCommand.Clipboard => PalettePanel.Clipboard,
            PaletteCommand.NotificationHistory => PalettePanel.Notifications,
            PaletteCommand.Convert => PalettePanel.Convert,
            PaletteCommand.AudioOutput => PalettePanel.AudioOutput,
            _ => PalettePanel.None
        };

    public static bool ShowSetting(SettingSearchEntry setting, string query, IEnumerable<PaletteCommand> commands)
    {
        var explicitSettings = query.TrimStart().StartsWith("settings", StringComparison.OrdinalIgnoreCase)
            || query.TrimStart().StartsWith("find setting", StringComparison.OrdinalIgnoreCase);
        return explicitSettings || setting.Property != "ShowVolume" || !commands.Contains(PaletteCommand.Volume);
    }

    public static bool TryTimerArgument(string minutesText, string label, out string argument)
    {
        argument = "";
        if (!decimal.TryParse(minutesText.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out var minutes)
            && !decimal.TryParse(minutesText.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out minutes)) return false;
        if (minutes < 1m / 60 || minutes > 1440) return false;
        var seconds = decimal.ToInt32(decimal.Round(minutes * 60, 0, MidpointRounding.AwayFromZero));
        argument = seconds.ToString(CultureInfo.InvariantCulture) + " seconds";
        if (!string.IsNullOrWhiteSpace(label)) argument += " for " + label.Trim();
        return true;
    }
}
