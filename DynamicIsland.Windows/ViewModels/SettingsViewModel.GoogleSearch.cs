using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class SettingsViewModel
{
    public bool GoogleSearchEnabled
    {
        get => _settings.GoogleSearchEnabled;
        set => Set(v => _settings.GoogleSearchEnabled = v, value);
    }

    public bool GoogleSearchOpenHomepageWhenEmpty
    {
        get => _settings.GoogleSearchOpenHomepageWhenEmpty;
        set => Set(v => _settings.GoogleSearchOpenHomepageWhenEmpty = v, value);
    }

    public bool GoogleSearchSafeSearch
    {
        get => _settings.GoogleSearchSafeSearch;
        set => Set(v => _settings.GoogleSearchSafeSearch = v, value);
    }

    public string GoogleSearchLanguage
    {
        get => GoogleSearch.NormalizeLanguage(_settings.GoogleSearchLanguage);
        set => Set(v => _settings.GoogleSearchLanguage = GoogleSearch.NormalizeLanguage(v), value);
    }

    public IReadOnlyList<GoogleSearchLanguageOption> GoogleSearchLanguageOptions => GoogleSearch.LanguageOptions;
}
