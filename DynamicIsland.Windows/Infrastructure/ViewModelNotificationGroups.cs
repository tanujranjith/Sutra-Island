namespace DynamicIsland.Windows.Infrastructure;

/// <summary>Pure classification of ViewModel PropertyChanged groups — no WPF dependencies, testable without a Window.</summary>
public static class ViewModelNotificationGroups
{
    public static readonly IReadOnlySet<string> StructuralProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "IsCompact", "IsStatsStyle", "IsAppleStyle", "PinExpanded",
        "IslandCornerRadius", "IslandInnerCornerRadius",
        "MediaColumn", "VolumeColumn", "StatusColumn"
    };

    public static readonly IReadOnlySet<string> MediaPresentationProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "MediaTitle", "MediaArtist", "IsPlaying", "PlayPauseGlyph",
        "MediaProgress", "ShowMediaTimes", "MediaElapsedText", "MediaTotalText",
        "MediaTimeRemaining", "MediaTrailingTimeText", "ShowNowPlaying", "ShowExplicitBadge",
        "CanSeek", "SeekBackTooltip", "SeekForwardTooltip",
        "PrimaryActivity", "CompactGlyph", "CompactPrimaryText", "CompactSecondaryText",
        "ShowCompactArt", "ShowCompactMediaRing", "ShowExpandedMediaRing", "ShowCompactRingTrack",
        "Artwork", "HasArtwork", "ShowMedia", "ScrollTitles"
    };

    public static readonly IReadOnlySet<string> QProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "QState", "QCurrentMode", "QStatusText", "QHeaderStatusText", "QResponse", "QResponseDisplay", "QPromptText", "QPromptDisplay", "QHasPrompt", "QShortcuts", "QHasShortcuts",
        "QInlineStatusText", "QShowInlineThinking", "QCanStop", "QCanCopyResponse", "QCanRetry", "QShowResponseActions", "QError", "QSourceText", "QCompactText",
        "IsQActive", "ShowQSurface", "QIsAsk", "QIsSay", "QIsListening", "QNeedsConsent", "QSpeechAvailable", "QSelectedProvider", "ShowCompactMediaContent", "ShowCompactQContent",
        "PrimaryActivity", "CompactGlyph", "CompactPrimaryText", "CompactSecondaryText", "ShowCompactEqualizer"
    };

    public static readonly IReadOnlySet<string> MediaProgressOnlyProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "MediaProgress", "MediaElapsedText", "MediaTimeRemaining", "MediaTrailingTimeText"
    };

    public static readonly IReadOnlySet<string> AudioProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "VolumeText", "VolumeGlyph", "OutputDeviceText", "AudioStatusText",
        "IsMuted", "IsAudioActive", "ShowAudioStatusText", "ShowVolume",
        "PrimaryActivity", "CompactGlyph", "CompactPrimaryText", "CompactSecondaryText",
        "ShowCompactArt", "ShowCompactMediaRing", "ShowCompactRingTrack",
        "UseRealSpectrum", "ShowAnimatedWave", "ShowMusicVisualizer", "ShowCompactEqualizer"
    };

    public static readonly IReadOnlySet<string> BatteryProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "BatteryText", "ChargingText", "BatteryGlyph",
        "ShowBattery", "IsCharging", "IsPowerConnected", "ShowBatteryLevel",
        "ShowCompactChargingIndicator", "ShowCompactSecondary", "ShowBatteryTime", "BatteryTimeText",
        "ShowStatusExtras", "ShowWidgetsPanel", "PrimaryActivity",
        "CompactGlyph", "CompactPrimaryText", "CompactSecondaryText",
        "ShowCompactArt", "ShowCompactMediaRing", "ShowCompactRingTrack"
    };

    public static readonly IReadOnlySet<string> ThemeProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "PrimaryTextBrush", "SecondaryTextBrush", "AccentTextBrush", "PanelBrush",
        "PanelBorderBrush", "AccentBrush", "AccentSoftBrush", "IslandSurfaceBrush", "IslandCardBrush", "IslandDividerBrush",
        "ShellControlBrush", "ShellControlHoverBrush", "ProgressFillBrush", "ProgressTrackBrush", "UiFontFamily"
    };

    public static readonly IReadOnlySet<string> ExpansionProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "IsCompact", "ShowTimerOrb", "ShowQuoteInCompact", "ShowQuoteInExpanded"
    };
}
