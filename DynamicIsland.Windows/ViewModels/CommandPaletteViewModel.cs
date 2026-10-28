using System.Collections.ObjectModel;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows.ViewModels;

public sealed class CommandPaletteSuggestion
{
    public CommandPaletteSuggestion(CommandDefinition command, bool favorite = false, string? availability = null)
    {
        Command = command;
        Name = command.Name;
        Description = $"{command.Category} · {availability ?? command.Description}";
        IsFavorite = favorite;
        Example = command.Example;
        Glyph = GlyphFor(command.Id);
    }

    public CommandPaletteSuggestion(string expression, decimal result)
        : this(expression, CalculatorParser.Format(result)) { }

    public CommandPaletteSuggestion(string expression, string result)
    {
        Name = result;
        Description = expression;
        Example = "Enter to copy";
        Glyph = "\uE8EF";
        CalculationValue = Name;
    }

    public CommandPaletteSuggestion(SettingSearchEntry setting) : this(CommandCatalog.ById(PaletteCommand.SearchSettings))
    { Name = setting.Name; Description = "Settings · " + setting.Description; Example = "Open setting"; Argument = setting.Property; }
    public string? Argument { get; }
    public bool IsFavorite { get; }
    public bool CanFavorite => Command is not null && Argument is null;
    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";
    public CommandDefinition? Command { get; }
    public string Name { get; }
    public string Description { get; }
    public string Example { get; }
    public string Glyph { get; }
    public string? CalculationValue { get; }
    public bool IsCalculation => CalculationValue is not null;

    // Segoe Fluent Icons codepoints — keeps the list scannable like Spotlight/Raycast.
    // Written as \u escapes so private-use glyphs survive any non-UTF8 tooling.
    private static string GlyphFor(PaletteCommand id) => id switch
    {
        PaletteCommand.Pause => "\uE769",
        PaletteCommand.Play => "\uE768",
        PaletteCommand.Next => "\uE893",
        PaletteCommand.Previous => "\uE892",
        PaletteCommand.Seek => "\uE774",
        PaletteCommand.Mute => "\uE74F",
        PaletteCommand.Unmute => "\uE767",
        PaletteCommand.Ask => "\uE945",
        PaletteCommand.Say => "\uE720",
        PaletteCommand.Compare => "\uE8A1",
        PaletteCommand.Timer or PaletteCommand.ShowTimers => "\uE916",
        PaletteCommand.PauseTimer => "\uE769",
        PaletteCommand.ResumeTimer => "\uE768",
        PaletteCommand.CancelTimer or PaletteCommand.CancelAlarm => "\uE711",
        PaletteCommand.Alarm or PaletteCommand.ShowAlarms => "\uE707",
        PaletteCommand.Stopwatch or PaletteCommand.StartStopwatch or PaletteCommand.PauseStopwatch => "\uE917",
        PaletteCommand.ResumeStopwatch => "\uE768",
        PaletteCommand.ResetStopwatch => "\uE72C",
        PaletteCommand.Focus or PaletteCommand.FocusOn or PaletteCommand.FocusOff => "\uE8A8",
        PaletteCommand.Expand => "\uE70E",
        PaletteCommand.Collapse => "\uE70D",
        PaletteCommand.OpenSettings => "\uE713",
        PaletteCommand.Convert => "\uE8AB",
        PaletteCommand.Volume or PaletteCommand.AudioOutput => "\uE767",
        PaletteCommand.VoiceInput => "\uE720",
        PaletteCommand.Chat => "\uE8F2",
        PaletteCommand.AskScreen => "\uE7F4",
        PaletteCommand.SearchSettings => "\uE713",
        PaletteCommand.OpenMedia => "\uE8D6",
        PaletteCommand.Recenter => "\uE81D",
        PaletteCommand.Clipboard => "\uE77F",
        PaletteCommand.NotificationHistory or PaletteCommand.NotificationsOn or PaletteCommand.NotificationsOff => "\uE7F4",
        PaletteCommand.RetryNotifications => "\uE72C",
        PaletteCommand.DismissBanner => "\uE711",
        PaletteCommand.StopAssistant => "\uE71A",
        PaletteCommand.ReadReply => "\uE767",
        PaletteCommand.WakeOn or PaletteCommand.WakeOff => "\uE720",
        PaletteCommand.Help => "\uE897",
        _ => "\uE713",
    };
}

public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly CommandRunner _runner;
    private readonly Models.AppSettings _settings;
    private readonly Func<Task>? _save;
    private bool _executing;
    private string _query = "";
    private string _statusMessage = "";
    private bool _hasStatus;
    private bool _isStatusError;
    private int _selectedIndex;
    private bool _showingAllCommands;

    public CommandPaletteViewModel(CommandRunner runner, Models.AppSettings? settings = null, Func<Task>? save = null)
    {
        _settings = settings ?? new Models.AppSettings();
        _save = save;
        _runner = runner;
        RefreshSuggestions();
    }

    public ObservableCollection<CommandPaletteSuggestion> Suggestions { get; } = [];

    public event EventHandler? CloseRequested;
    public event EventHandler? SuggestionsRefreshed;

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? "")) return;
            ClosePanel();
            _showingAllCommands = false;
            ClearStatus();
            RefreshSuggestions();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var count = Suggestions.Count;
            var clamped = count == 0 ? 0 : Math.Clamp(value, 0, count - 1);
            if (!SetProperty(ref _selectedIndex, clamped)) return;
            RaisePropertyChanged(nameof(SelectedSuggestion));
        }
    }

    public CommandPaletteSuggestion? SelectedSuggestion =>
        _selectedIndex >= 0 && _selectedIndex < Suggestions.Count ? Suggestions[_selectedIndex] : null;

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool HasStatus { get => _hasStatus; private set => SetProperty(ref _hasStatus, value); }
    public bool IsStatusError { get => _isStatusError; private set => SetProperty(ref _isStatusError, value); }

    public void MoveSelection(int delta)
    {
        if (Suggestions.Count == 0) return;
        var next = _selectedIndex + delta;
        if (next < 0) next = Suggestions.Count - 1;
        else if (next >= Suggestions.Count) next = 0;
        SelectedIndex = next;
    }

    public async Task ExecuteAsync()
    {
        if (_executing) return;
        if (HasPanel) { await SubmitPanelAsync(); return; }
        if (AlgebraParser.IsCandidate(_query))
        {
            if (AlgebraParser.TrySolve(_query, out var solution, out var algebraError))
                ShowStatus($"Result: {solution}", isError: false);
            else
                ShowStatus(algebraError ?? "Complete the equation.", isError: true);
            return;
        }
        if (!_query.Trim().Equals("convert", StringComparison.OrdinalIgnoreCase) && !CalculatorParser.IsCandidate(_query) && UnitConversionParser.IsCandidate(_query))
        {
            if (UnitConversionParser.TryConvert(_query, out var converted, out var conversionError))
                ShowStatus($"Result: {converted}", isError: false);
            else
                ShowStatus(conversionError ?? "Complete the conversion.", isError: true);
            return;
        }
        if (CalculatorParser.IsCandidate(_query))
        {
            if (CalculatorParser.TryEvaluate(_query, out var value, out var calculationError))
                ShowStatus($"Result: {CalculatorParser.Format(value)}", isError: false);
            else
                ShowStatus(calculationError ?? "Complete the expression to calculate.", isError: true);
            return;
        }

        var match = PaletteSelection.Resolve(_query, SelectedSuggestion?.Command, SelectedSuggestion?.Argument);
        if (match is null)
        {
            ShowStatus("Unknown command — pick a suggestion or type help.", isError: true);
            return;
        }

        await ExecuteMatchAsync(match);
    }

    private async Task ExecuteMatchAsync(CommandMatch match)
    {
        if (_executing) return;
        _executing = true;
        try
        {
            var result = await _runner.ExecuteAsync(match);
            if (!result.IsError && !result.ShowAllCommands && match.Command.Id != PaletteCommand.SearchSettings)
            {
                var id = match.Command.Id.ToString();
                _settings.PaletteRecents.Remove(id); _settings.PaletteRecents.Insert(0, id);
                if (_settings.PaletteRecents.Count > 10) _settings.PaletteRecents.RemoveRange(10, _settings.PaletteRecents.Count - 10);
                if (_save is not null) await _save();
            }
            if (result.ShowAllCommands)
            {
                _showingAllCommands = true;
                RefreshSuggestions(forceAll: true);
                ShowStatus(result.Message ?? "", isError: false);
                return;
            }
            if (result.Panel != PalettePanel.None)
            {
                OpenPanel(result.Panel);
                return;
            }
            if (result.Close)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (!string.IsNullOrEmpty(result.Message))
                ShowStatus(result.Message!, result.IsError);
        }
        finally { _executing = false; }
    }

    private void RefreshSuggestions(bool forceAll = false)
    {
        Suggestions.Clear();
        var isAlgebra = !forceAll && !_showingAllCommands && AlgebraParser.IsCandidate(_query);
        var isConversion = !isAlgebra && !forceAll && !_showingAllCommands && !_query.Trim().Equals("convert", StringComparison.OrdinalIgnoreCase) && !CalculatorParser.IsCandidate(_query) && UnitConversionParser.IsCandidate(_query);
        var isCalculation = !forceAll && !_showingAllCommands && CalculatorParser.IsCandidate(_query);
        if (isAlgebra)
        {
            if (AlgebraParser.TrySolve(_query, out var result, out var error))
                Suggestions.Add(new CommandPaletteSuggestion(_query.Trim(), result));
            else
                ShowStatus(error ?? "Complete the equation.", isError: true);
        }
        else if (isConversion)
        {
            if (UnitConversionParser.TryConvert(_query, out var result, out var error))
                Suggestions.Add(new CommandPaletteSuggestion(_query.Trim(), result));
            else
                ShowStatus(error ?? "Complete the conversion.", isError: true);
        }
        else if (isCalculation)
        {
            if (CalculatorParser.TryEvaluate(_query, out var result, out var error))
                Suggestions.Add(new CommandPaletteSuggestion(_query.Trim(), result));
            else
                ShowStatus(error ?? "Complete the expression to calculate.", isError: true);
        }
        else
        {
            var commands = forceAll || _showingAllCommands
                ? CommandCatalog.All
                : CommandCatalog.Filter(_query, _settings.PaletteFavorites, _settings.PaletteRecents);
            foreach (var command in commands)
                Suggestions.Add(new CommandPaletteSuggestion(command, _settings.PaletteFavorites.Contains(command.Id.ToString()), _runner.Availability(command.Id)));
        }

        if (!isAlgebra && !isConversion && !isCalculation && !forceAll && !_showingAllCommands)
        {
            var search = _query.Trim();
            if (search.StartsWith("find setting ", StringComparison.OrdinalIgnoreCase)) search = search[13..];
            else if (search.StartsWith("settings search ", StringComparison.OrdinalIgnoreCase)) search = search[16..];
            var commandIds = Suggestions.Where(x => x.Command is not null).Select(x => x.Command!.Id).ToArray();
            foreach (var setting in SettingsSearchIndex.Search(search))
                if (PalettePanelPolicy.ShowSetting(setting, _query, commandIds)) Suggestions.Add(new CommandPaletteSuggestion(setting));
        }
        _selectedIndex = 0;
        RaisePropertyChanged(nameof(SelectedIndex));
        RaisePropertyChanged(nameof(SelectedSuggestion));
        if (!isAlgebra && !isConversion && !isCalculation && !_showingAllCommands && !forceAll &&
            !string.IsNullOrWhiteSpace(_query) && Suggestions.Count == 0)
            ShowStatus("No matching commands. Type help to see all.", isError: false);
        SuggestionsRefreshed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    { ClosePanel(); _query = ""; _showingAllCommands = false; ClearStatus(); RaisePropertyChanged(nameof(Query)); RefreshSuggestions(); }
    public async Task ToggleFavoriteAsync(CommandPaletteSuggestion? suggestion = null)
    {
        var target = suggestion ?? SelectedSuggestion;
        if (target?.Command is null || target.Argument is not null) return;
        var id = target.Command.Id.ToString();
        if (!_settings.PaletteFavorites.Remove(id)) _settings.PaletteFavorites.Add(id);
        var selectedId = target.Command.Id;
        RefreshSuggestions();
        SelectedIndex = Suggestions.ToList().FindIndex(x => x.Command?.Id == selectedId);
        if (_save is not null) await _save();
    }
    public void ReportCalculationCopied() => ShowStatus("Result copied to clipboard.", isError: false);
    public void ReportCalculationCopyFailed() => ShowStatus("Could not copy the result.", isError: true);

    private void ShowStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
        HasStatus = !string.IsNullOrEmpty(message);
    }

    private void ClearStatus()
    {
        StatusMessage = "";
        IsStatusError = false;
        HasStatus = false;
    }
}
