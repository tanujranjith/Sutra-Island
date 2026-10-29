namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

public sealed record SettingSearchEntry(string Property, string Name, string Description);
public static class SettingsSearchIndex
{
    public static readonly SettingSearchEntry[] All =
    [
        new("ShowClock", "Clock", "Show the local time in the island"),
        new("ShowDate", "Date", "Show the date in the expanded island"),
        new("ShowCompactDate", "Compact date", "Show the date beneath the compact clock"),
        new("CapsLockNotifications", "Caps Lock notifications", "Show lock-key feedback"),
        new("NumLockNotifications", "Num Lock notifications", "Show lock-key feedback"),
        new("WakeListeningEnabled", "Hey Sutra wake listening", "Enable local microphone wake detection"),
        new("VoiceReadAloud", "Read voice replies aloud", "Speak replies to voice questions"),
        new("VoiceName", "Assistant voice", "Choose an installed Windows voice"),
        new("QSelectedProvider", "Assistant provider", "Choose an AI service or local model"),
        new("QSelectedModel", "Assistant model", "Choose the model for chat"),
        new("QIncludeScreenImage", "Screen images", "Include an image when screen help is requested"),
        new("QEnabled", "Enable Sutra Assistant", "Chat and screen help"),
        new("ShowNotifications", "Windows notifications", "Mirror Windows notification banners"),
        new("NotificationPollSeconds", "Notification recovery interval", "Seconds between recovery snapshots"),
        new("MediaPollMilliseconds", "Media polling rate", "Playback update interval in milliseconds"),
        new("ClockPollSeconds", "Clock polling rate", "Clock, date, and world clock refresh interval in seconds"),
        new("WindowPollSeconds", "Window and screen polling rate", "Fullscreen, display, and window health check interval in seconds"),
        new("ConnectivityPollSeconds", "Connectivity polling rate", "Wi-Fi status check interval in seconds"),
        new("AudioPollMilliseconds", "Audio polling rate", "Audio session update interval in milliseconds"),
        new("SystemMonitorPollMilliseconds", "System monitor polling rate", "CPU, memory, and network update interval in milliseconds"),
        new("TimerPollMilliseconds", "Timer and alarm polling rate", "Timer progress and alarm check interval in milliseconds"),
        new("BatteryPollSeconds", "Battery polling rate", "Battery status refresh interval in seconds"),
        new("PrivacyPollSeconds", "Privacy indicator polling rate", "Camera and microphone status interval in seconds"),
        new("ThemePollSeconds", "Theme polling rate", "Windows theme change check interval in seconds"),
        new("AirPodsPollSeconds", "AirPods polling rate", "Paired device connection check interval in seconds"),
        new("CalendarPollMinutes", "Calendar polling rate", "Meeting refresh interval in minutes"),
        new("StocksPollMinutes", "Stocks polling rate", "Quote refresh interval in minutes"),
        new("WeatherPollMinutes", "Weather polling rate", "Forecast refresh interval in minutes"),
        new("ShowClipboard", "Clipboard history", "Enable Windows clipboard integration"),
        new("ShowWeather", "Weather", "Show current conditions"),
        new("ShowNextMeeting", "Calendar meetings", "Show upcoming meetings"),
        new("ShowPrivacyIndicators", "Privacy indicators", "Show microphone and camera activity"),
        new("ShowMedia", "Media", "Show the active music and video session"),
        new("ShowAlbumArtInCompact", "Compact album artwork", "Show album art in the compact island"),
        new("ShowVolume", "Show volume in island", "Show or hide the island's system audio controls"),
        new("ShowBattery", "Battery", "Show battery level and charging"),
        new("ShowSystemMonitor", "System monitor", "Show RAM and network activity"),
        new("RealAudioSpectrum", "Audio spectrum", "Use real loopback audio for the equalizer"),
        new("Theme", "Theme", "Choose system, light, dark, or custom colors"),
        new("AnimationIntensity", "Animation intensity", "Normal, expressive, or reduced motion"),
        new("IslandWidth", "Island width", "Compact island size in DIPs"),
        new("IslandHeight", "Island height", "Compact island size in DIPs"),
        new("DefaultPosition", "Island position", "Screen placement and anchoring"),
        new("CommandPaletteHotkey", "Command palette shortcut", "Change Ctrl + Alt + K"),
        new("ExpandOnHover", "Expand on hover", "Open the island when the pointer enters"),
        new("LaunchOnStartup", "Launch on startup", "Start with Windows"),
        new("AlwaysOnTop", "Always on top", "Keep the island above other windows"),
        new("DebugLogging", "Debug logging", "Record integration and performance diagnostics"),
        new("Use24HourClock", "24-hour clock", "Choose the time format"),
        new("ShowSeconds", "Clock seconds", "Show seconds in the clock")
,
        new("IsDropDownOpen", "Island content", "Choose what appears in the island."),
        new("ShowTimerAlarm", "Timer & alarm", "Open this island preference"),
        new("ShowRamInCompact", "RAM in compact view", "Keep memory usage visible while the island is collapsed."),
        new("ShowEqualizerInCompact", "Equalizer in compact view", "Show animated audio bars while the island is collapsed and audio is playing."),
        new("IslandVisualMode", "Island style", "Apple is clean and minimal; Stats keeps more live information visible."),
        new("IslandSize", "Island size", "Open this island preference"),
        new("IslandCornerRadius", "Corner radius", "Open this island preference"),
        new("InterfaceScale", "Interface scale", "Open this island preference"),
        new("FontFamilyName", "Font", "Open this island preference"),
        new("AdaptiveAccent", "Adaptive accent", "Pull the accent colour from current album art."),
        new("UseCustomColors", "Use custom colours", "Open this island preference"),
        new("AccentColorHex", "Accent colour", "Open this island preference"),
        new("TextColorHex", "Text colour", "Open this island preference"),
        new("AlbumArtSize", "Compact size", "Open this island preference"),
        new("ExpandedAlbumArtSize", "Expanded size", "Open this island preference"),
        new("AlbumCornerRadius", "Corner radius", "Open this island preference"),
        new("SideOffset", "Side offset", "Open this island preference"),
        new("TopOffset", "Top / bottom offset", "Open this island preference"),
        new("PreferredMonitor", "Preferred monitor", "Open this island preference"),
        new("FollowActiveScreen", "Follow active screen", "Open this island preference"),
        new("LockPosition", "Lock position", "Open this island preference"),
        new("WeatherLocation", "Location", "City name or location used by the weather service."),
        new("WeatherFahrenheit", "Use Fahrenheit", "Open this island preference"),
        new("ShowMusicVisualizer", "Music visualizer", "Show animated bars that react to playback in the expanded island."),
        new("ShowConnectivity", "Wi-Fi & Bluetooth", "Open this island preference"),
        new("ShowStocks", "Stocks and crypto", "Open this island preference"),
        new("StockSymbols", "Symbols", "Open this island preference"),
        new("ShowWorldClocks", "World clocks", "Open this island preference"),
        new("WorldClockZones", "Time zones", "Open this island preference"),
        new("ShowCountdown", "Countdown", "Open this island preference"),
        new("CountdownLabel", "Countdown label", "Open this island preference"),
        new("CountdownDate", "Countdown date", "Open this island preference"),
        new("NotificationHistoryEnabled", "Notification history", "Open this island preference"),
        new("NotificationFilterMode", "Notification filter", "Open this island preference"),
        new("NotificationAppFilter", "App list", "Open this island preference"),
        new("QCtrlAltQ", "Sutra Assistant activation shortcuts", "Enable any combination. Each shortcut runs the same Sutra Assistant action."),
        new("QShiftA", "Sutra Assistant activation shortcuts", "Enable any combination. Each shortcut runs the same Sutra Assistant action."),
        new("QVarSequence", "Sutra Assistant activation shortcuts", "Enable any combination. Each shortcut runs the same Sutra Assistant action."),
        new("QShiftComma", "Sutra Assistant activation shortcuts", "Enable any combination. Each shortcut runs the same Sutra Assistant action."),
        new("QShiftPeriod", "Sutra Assistant activation shortcuts", "Enable any combination. Each shortcut runs the same Sutra Assistant action."),
        new("QAutoExpandIsland", "Auto-expand island for Sutra Assistant", "Expand the island automatically when the Sutra Assistant shortcut or Sutra Assistant action is used."),
        new("QAutoCloseAfterResponse", "Auto-close Sutra Assistant after response", "Close the Sutra Assistant session automatically after a response finishes."),
        new("QAutoCloseDelaySeconds", "Sutra Assistant auto-close delay", "How long to keep the completed response visible before closing Sutra Assistant."),
        new("QHotkeyShortcut", "Hotkey quick action", "Optionally run a Sutra Assistant quick shortcut immediately after the selected Sutra Assistant shortcut opens and captures the screen."),
        new("QReasoningEffort", "Reasoning effort", "Auto uses the provider default; explicit values are sent when supported by the selected model."),
        new("QCaptureMode", "Capture source", "Open this island preference"),
        new("QOllamaBaseUrl", "Ollama base URL", "Open this island preference"),
        new("QAskSystemPrompt", "Ask instructions", "Optional instructions added after Sutra Assistant\u2019s built-in Ask behavior."),
        new("QSaySystemPrompt", "Say instructions", "Optional instructions added after Sutra Assistant\u2019s built-in Say behavior."),
        new("Name", "Listen for \u201cHey Sutra\u201d", "Opt in to local wake detection. Say the wake phrase, wait for Listening, then ask your question. Requires installed English Windows speech and microphone access."),
        new("Prompt", "Listen for \u201cHey Sutra\u201d", "Opt in to local wake detection. Say the wake phrase, wait for Listening, then ask your question. Requires installed English Windows speech and microphone access."),
        new("QTimeoutSeconds", "Timeout seconds", "Open this island preference"),
        new("QMaxResponseTokens", "Maximum response tokens", "Includes hidden reasoning tokens. Default: 8,192."),
        new("ClickThroughWhenCompact", "Click-through when compact", "Open this island preference"),
        new("AlwaysExpanded", "Always expanded", "Open this island preference"),
        new("AutoGrowPill", "Auto-grow to fit content", "Open this island preference"),
        new("AutoHideFullscreen", "Hide in fullscreen", "Temporarily hide over fullscreen apps and games."),
        new("SelectedMediaApp", "Preferred media app", "Open this island preference"),
        new("ShowMediaProgressRing", "Media progress ring", "Open this island preference"),
        new("ShowSongTimeRemaining", "Song time remaining", "Open this island preference"),
        new("ScrollLongTitles", "Scroll long titles", "Open this island preference"),
        new("ClickArtOpensApp", "Click artwork to open app", "Open this island preference"),
        new("ShowBatteryTime", "Battery time remaining", "Open this island preference"),
        new("LowBatteryWarning", "Low-battery warning", "Open this island preference"),
        new("LowBatteryThreshold", "Low-battery threshold", "Open this island preference"),
        new("VolumeWarningEnabled", "Volume warning", "Open this island preference"),
        new("VolumeWarningThreshold", "Volume threshold", "Open this island preference"),
        new("SelectedPreset", "Import or export settings", "Open this island preference"),
        new("NewPresetName", "Import or export settings", "Open this island preference"),
        new("DebugOverlay", "Debug overlay", "Open this island preference"),
        new("ShowIslandInScreenshots", "Show Island in screenshots", "Turn on only when you want the Island included in screenshots; off excludes it where Windows supports capture affinity."),
        new("ShowInAltTab", "Show island in Alt-Tab", "Open this island preference"),
    ];

    // Settings entries are immutable and reused for every palette query. Keep the combined
    // search corpus once so typing does not rebuild a string for every entry and word.
    private static readonly Dictionary<SettingSearchEntry, string> SearchTextByEntry = BuildSearchTextByEntry();

    private static Dictionary<SettingSearchEntry, string> BuildSearchTextByEntry()
    {
        var searchTextByEntry = new Dictionary<SettingSearchEntry, string>(ReferenceEqualityComparer.Instance);
        foreach (var entry in All)
            searchTextByEntry[entry] = $"{entry.Name} {entry.Description} {entry.Property}";
        return searchTextByEntry;
    }

    private static string SearchTextFor(SettingSearchEntry entry) =>
        SearchTextByEntry.TryGetValue(entry, out var cached)
            ? cached
            : $"{entry.Name} {entry.Description} {entry.Property}";

    public static IEnumerable<SettingSearchEntry> Search(string query)
    {
        var words = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return All.Where(entry =>
        {
            if (words.Length == 0) return false;

            var searchText = SearchTextFor(entry);
            foreach (var word in words)
                if (!searchText.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }).Take(8);
    }
}
