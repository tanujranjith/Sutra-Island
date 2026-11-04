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
        ImportVocabularyCommand = new RelayCommand(ImportVocabulary);
        TestDuckingCommand = new RelayCommand(TestDucking);
        ResetCommand = new RelayCommand(ResetFlow);
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

    // ---- General -------------------------------------------------------------------------

    private static readonly (string Label, string Value)[] PushKeys =
        [("Right Ctrl", "RightCtrl"), ("Right Alt", "RightAlt"), ("Right Shift", "RightShift")];
    public IReadOnlyList<string> PushKeyOptions { get; } = PushKeys.Select(k => k.Label).ToArray();
    public string PushKey
    {
        get => PushKeys.FirstOrDefault(k => k.Value == _settings.FlowPushKey).Label ?? "Right Ctrl";
        set
        {
            var key = PushKeys.FirstOrDefault(k => k.Label == value).Value;
            if (key is null || key == _settings.FlowPushKey) return;
            _settings.FlowPushKey = key;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(PushKeyNote));
            Save();
        }
    }
    public string PushKeyNote => $"Hold {PushKey} and speak. A quick tap or a quick shortcut ({PushKey}+C) still works as normal."
        + (_settings.FlowPushKey == "RightAlt" ? " Right Alt is reserved for Flow, so AltGr characters won't type while it's selected." : "");

    private const string DefaultMic = "Windows default";
    public IReadOnlyList<string> MicrophoneOptions => [DefaultMic, .. FlowRecorder.Microphones()];
    public string Microphone
    {
        get => string.IsNullOrWhiteSpace(_settings.FlowMicrophone) ? DefaultMic : _settings.FlowMicrophone;
        set
        {
            var name = value == DefaultMic || value is null ? "" : value;
            if (name == _settings.FlowMicrophone) return;
            _settings.FlowMicrophone = name;
            RaisePropertyChanged();
            Save();
        }
    }

    private static readonly (string Label, string Code)[] Languages =
    [
        ("English (fastest)", "en"), ("Auto-detect (99 languages)", "auto"), ("Spanish", "es"), ("Tamil", "ta"), ("Hindi", "hi"),
        ("French", "fr"), ("German", "de"), ("Portuguese", "pt"), ("Chinese", "zh"), ("Japanese", "ja"), ("Korean", "ko"), ("Arabic", "ar"),
    ];
    public IReadOnlyList<string> LanguageOptions { get; } = Languages.Select(l => l.Label).ToArray();
    public string Language
    {
        get => Languages.FirstOrDefault(l => l.Code == _settings.FlowLanguage).Label ?? "English (fastest)";
        set
        {
            var code = Languages.FirstOrDefault(l => l.Label == value).Code;
            if (code is null || code == _settings.FlowLanguage) return;
            _settings.FlowLanguage = code;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ModelStatus));
            RaisePropertyChanged(nameof(ModelReady));
            ((RelayCommand)DownloadModelCommand).RaiseCanExecuteChanged();
            Save(); // starts the multilingual model download when needed
        }
    }

    // ---- Sound & extras --------------------------------------------------------------------

    public bool Sounds { get => _settings.FlowSounds; set => Set(value, _settings.FlowSounds, v => _settings.FlowSounds = v); }
    public bool SmartFormatting { get => _settings.FlowSmartFormatting; set => Set(value, _settings.FlowSmartFormatting, v => _settings.FlowSmartFormatting = v); }
    public bool CreatorMode { get => _settings.FlowCreatorMode; set => Set(value, _settings.FlowCreatorMode, v => _settings.FlowCreatorMode = v); }
    public bool CodeVariables { get => _settings.FlowCodeVariables; set => Set(value, _settings.FlowCodeVariables, v => _settings.FlowCodeVariables = v); }
    public bool FileTagging { get => _settings.FlowFileTagging; set => Set(value, _settings.FlowFileTagging, v => _settings.FlowFileTagging = v); }

    private void Set(bool value, bool current, Action<bool> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (value == current) return;
        apply(value);
        RaisePropertyChanged(name);
        Save();
    }

    // ---- Data ------------------------------------------------------------------------------

    public ICommand ImportVocabularyCommand { get; }
    public ICommand ResetCommand { get; }

    /// <summary>Adds terms from a .txt or .csv file to the vocabulary (duplicates skipped).</summary>
    private void ImportVocabulary()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import vocabulary", Filter = "Word lists (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var terms = FlowCodeText.ParseVocabulary(System.IO.File.ReadAllText(dialog.FileName));
            var before = _settings.FlowVocabulary.Count;
            Vocabulary = string.Join(", ", _settings.FlowVocabulary.Concat(terms));
            var added = _settings.FlowVocabulary.Count - before;
            System.Windows.MessageBox.Show($"Added {added} new term{(added == 1 ? "" : "s")} to your vocabulary.", "Sutra Flow", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Couldn't read that file: " + ex.Message, "Sutra Flow", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Puts every Sutra Flow setting back to its default. Usage and the downloaded model are kept.</summary>
    private void ResetFlow()
    {
        if (System.Windows.MessageBox.Show("Reset every Sutra Flow setting to its default? Your vocabulary is cleared; your word count and the downloaded speech model are kept.",
                "Reset Sutra Flow", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK) return;
        var defaults = new AppSettings();
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.Name.StartsWith("Flow", StringComparison.Ordinal) || property.Name == nameof(AppSettings.FlowWordsDictated) || !property.CanWrite) continue;
            property.SetValue(_settings, property.GetValue(defaults));
        }
        Save();
        Refresh();
    }

    /// <summary>Keep uhs, ohs and drawn-out sounds.</summary>
    public bool Expressive
    {
        get => _settings.FlowExpressive;
        set
        {
            if (_settings.FlowExpressive == value) return;
            _settings.FlowExpressive = value;
            RaisePropertyChanged();
            Save();
        }
    }

    /// <summary>Lower other apps' audio while listening.</summary>
    public bool DuckAudio
    {
        get => _settings.FlowDuckAudio;
        set
        {
            if (_settings.FlowDuckAudio == value) return;
            _settings.FlowDuckAudio = value;
            RaisePropertyChanged();
            Save();
        }
    }

    /// <summary>How loud other apps stay while listening, 0 (mute) to 100, on the same feel as the Windows volume slider.</summary>
    public double DuckPercent
    {
        get => _settings.FlowDuckPercent;
        set
        {
            var percent = (int)Math.Round(Math.Clamp(value, 0, 100));
            if (percent == _settings.FlowDuckPercent) return;
            _settings.FlowDuckPercent = percent;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(DuckLevel));
            Save();
        }
    }

    public string DuckLevel => _settings.FlowDuckPercent <= 0 ? "Mute" : $"{_settings.FlowDuckPercent}%";

    private string _duckTestResult = "";
    /// <summary>What the Test button did, so a setup where nothing gets lowered is visible.</summary>
    public string DuckTestResult { get => _duckTestResult; private set { _duckTestResult = value; RaisePropertyChanged(); } }
    public ICommand TestDuckingCommand { get; private set; } = null!;

    private async void TestDucking()
    {
        DuckTestResult = "Testing… play some music first.";
        var lowered = await _flow.TestDuckingAsync();
        DuckTestResult = lowered switch
        {
            null => "Finish the dictation first.",
            { Count: 0 } => "Nothing was playing to lower. Start music, then test again.",
            _ => $"Lowered {string.Join(", ", lowered)} for 2.5 seconds.",
        };
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
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(300).ToList();
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
        foreach (var name in new[] { nameof(Enabled), nameof(PushKey), nameof(PushKeyNote), nameof(MicrophoneOptions), nameof(Microphone), nameof(Language),
                     nameof(Sounds), nameof(SmartFormatting), nameof(CreatorMode), nameof(CodeVariables), nameof(FileTagging), nameof(DuckAudio), nameof(DuckPercent), nameof(DuckLevel), nameof(Expressive), nameof(Cleanup), nameof(CleanupNote), nameof(Vocabulary), nameof(ModelStatus), nameof(ModelReady), nameof(WordsDictated) })
            RaisePropertyChanged(name);
        RaiseAi(); // keys and the Assistant's provider can change on other settings pages
    }

    private void Save()
    {
        _flow.ApplySettings();
        _ = _settingsService.SaveAsync(_settings);
    }
}
