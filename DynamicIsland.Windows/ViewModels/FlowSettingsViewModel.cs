using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Input;
using DynamicIsland.Q.Core;
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
        CopyHeldTextCommand = new RelayCommand(() => _flow.CopyHeldText(), () => _flow.HasHeldText);
        RefreshUsageCommand = new RelayCommand(() => RaisePropertyChanged(nameof(WordsDictated)));
        _flow.HeldTextChanged += (_, _) => ((RelayCommand)CopyHeldTextCommand).RaiseCanExecuteChanged();
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

    public IReadOnlyList<string> CleanupOptions { get; } = ["Off", "Basic (on this PC)", "AI (your chosen provider)"];

    private const string SameAsAssistant = "";

    /// <summary>"Same as Sutra Assistant", then every provider that can clean dictation.</summary>
    public IReadOnlyList<QProviderChoice> AiProviderOptions =>
        new[] { new QProviderChoice(SameAsAssistant, "Same as Sutra Assistant") }
            .Concat(_flow.Ai.Providers.Select(p => new QProviderChoice(p.Id,
                p.Id == "ollama" || _flow.Ai.HasKey(p.Id) ? p.DisplayName : p.DisplayName + " (no key)")))
            .ToArray();

    public string AiProvider
    {
        get => _settings.FlowAiProvider ?? "";
        set
        {
            value ??= "";
            if (string.Equals(_settings.FlowAiProvider, value, StringComparison.OrdinalIgnoreCase)) return;
            _settings.FlowAiProvider = value;
            _settings.FlowAiModel = ""; // a model name rarely carries over between providers
            RaiseAi();
            Save();
        }
    }

    /// <summary>Suggested models for the chosen provider, with the one in use first.</summary>
    public IReadOnlyList<string> AiModelOptions =>
        new[] { _flow.Ai.ModelId }.Concat(QProviderPolicy.ModelSuggestions(_flow.Ai.ProviderId, _flow.Ai.ModelId))
            .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The model in use. Typing a name sets it; clearing it goes back to the default.</summary>
    public string AiModel
    {
        get => _flow.Ai.ModelId;
        set
        {
            var model = (value ?? "").Trim();
            if (string.Equals(model, _flow.Ai.ModelId, StringComparison.Ordinal)) return;
            _settings.FlowAiModel = model;
            RaiseAi();
            Save();
        }
    }

    public IReadOnlyList<string> AiEffortOptions => _flow.Ai.EffortOptions;

    /// <summary>Reasoning effort for cleanup. Lower is faster; a level the model doesn't support falls back to auto.</summary>
    public string AiEffort
    {
        get => _flow.Ai.Effort;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, _settings.FlowAiEffort, StringComparison.OrdinalIgnoreCase)) return;
            _settings.FlowAiEffort = value;
            RaisePropertyChanged();
            Save();
        }
    }

    public string AiModelNote => _flow.AiUnavailableReason is { } reason
        ? reason + "."
        : "Small, fast models work best: cleanup has 8 seconds before Flow falls back to Basic.";

    private void RaiseAi()
    {
        foreach (var name in new[] { nameof(AiProvider), nameof(AiProviderOptions), nameof(AiModel), nameof(AiModelOptions), nameof(AiEffortOptions), nameof(AiEffort), nameof(AiModelNote), nameof(CleanupNote) })
            RaisePropertyChanged(name);
    }

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
        FlowCleanupMode.Basic => "Removes um and uh, stutters, and spoken punctuation like \"comma\" or \"new line\". Nothing leaves your PC. It doesn't resolve changes of mind (\"actually no...\"); choose AI for that.",
        _ => _flow.AiUnavailableReason is { } reason
            ? $"{reason}. Until then, Basic cleanup is used."
            : "Also resolves self-corrections (\"Monday, no wait, Tuesday\") and lists. Sends only the text, never audio, to the provider chosen below. Falls back to Basic if it is slow or rewrites too much.",
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
    /// <summary>Copies the last dictation that couldn't be pasted (kept in memory for two minutes).</summary>
    public ICommand CopyHeldTextCommand { get; }
    public ICommand RefreshUsageCommand { get; }
    public string WordsDictated => _settings.FlowWordsDictated == 1 ? "1 word dictated" : $"{_settings.FlowWordsDictated:N0} words dictated";

    public void Refresh()
    {
        foreach (var name in new[] { nameof(Enabled), nameof(Cleanup), nameof(CleanupNote), nameof(Vocabulary), nameof(ModelStatus), nameof(ModelReady), nameof(WordsDictated) })
            RaisePropertyChanged(name);
        RaiseAi(); // keys and the Assistant's provider can change on other settings pages
    }

    private void Save()
    {
        _flow.ApplySettings();
        _ = _settingsService.SaveAsync(_settings);
    }
}
