using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Input;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Flow;

namespace DynamicIsland.Windows.ViewModels;

/// <summary>Settings → Sutra Flow.</summary>
public sealed class FlowSettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly FlowController _flow;

    public FlowSettingsViewModel(AppSettings settings, SettingsService settingsService, FlowController flow)
    {
        _settings = settings;
        _settingsService = settingsService;
        _flow = flow;
        DownloadModelCommand = new RelayCommand(() => _ = _flow.Engine.EnsureModelAsync(), () => !_flow.Engine.IsModelInstalled && !_flow.Engine.IsDownloading);
        _flow.Engine.ModelStatusChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RaisePropertyChanged(nameof(ModelStatus));
            RaisePropertyChanged(nameof(ModelReady));
            ((RelayCommand)DownloadModelCommand).RaiseCanExecuteChanged();
        });
    }

    public bool Enabled
    {
        get => _settings.FlowEnabled;
        set
        {
            if (_settings.FlowEnabled == value) return;
            _settings.FlowEnabled = value;
            RaisePropertyChanged();
            Save();
        }
    }

    public IReadOnlyList<string> CleanupOptions { get; } = ["Off", "Basic (on this PC)", "AI (Sutra Assistant provider)"];

    public string Cleanup
    {
        get => CleanupOptions[(int)_settings.FlowCleanup];
        set
        {
            var index = Math.Max(0, CleanupOptions.ToList().IndexOf(value));
            if ((int)_settings.FlowCleanup == index) return;
            _settings.FlowCleanup = (FlowCleanupMode)index;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CleanupNote));
            Save();
        }
    }

    public string CleanupNote => _settings.FlowCleanup switch
    {
        FlowCleanupMode.Off => "Pastes exactly what Whisper heard.",
        FlowCleanupMode.Basic => "Removes um and uh, stutters, and spoken punctuation like \"comma\" or \"new line\". Nothing leaves your PC.",
        _ => _flow.AiUnavailableReason is { } reason
            ? $"{reason}. Until then, Basic cleanup is used."
            : "Also resolves self-corrections (\"Monday, no wait, Tuesday\") and lists. Sends only the text, never audio, to your selected provider. Falls back to Basic if it is slow or rewrites too much.",
    };

    public string Vocabulary
    {
        get => string.Join(", ", _settings.FlowVocabulary);
        set
        {
            var words = (value ?? "").Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
            if (words.SequenceEqual(_settings.FlowVocabulary)) return;
            _settings.FlowVocabulary = words;
            RaisePropertyChanged();
            Save();
        }
    }

    public string ModelStatus => _flow.Engine.ModelStatusText;
    public bool ModelReady => _flow.Engine.IsModelInstalled;
    public ICommand DownloadModelCommand { get; }
    public string WordsDictated => _settings.FlowWordsDictated == 1 ? "1 word dictated" : $"{_settings.FlowWordsDictated:N0} words dictated";

    public void Refresh()
    {
        foreach (var name in new[] { nameof(Enabled), nameof(Cleanup), nameof(CleanupNote), nameof(Vocabulary), nameof(ModelStatus), nameof(ModelReady), nameof(WordsDictated) })
            RaisePropertyChanged(name);
    }

    private void Save()
    {
        _flow.ApplySettings();
        _ = _settingsService.SaveAsync(_settings);
    }
}
