namespace DynamicIsland.Windows.ViewModels;

public sealed partial class SettingsViewModel
{
    public bool RememberIslandScreenshots
    {
        get => _settings.RememberIslandScreenshots;
        set => Set(v => _settings.RememberIslandScreenshots = v, value);
    }

    public int IslandScreenshotHistoryLimit
    {
        get => _settings.IslandScreenshotHistoryLimit;
        set => Set(v => _settings.IslandScreenshotHistoryLimit = v, Math.Clamp(value, 1, 30));
    }
}
