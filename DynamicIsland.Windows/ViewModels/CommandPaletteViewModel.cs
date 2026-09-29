using System.Collections.ObjectModel;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows.ViewModels;

public sealed class CommandPaletteSuggestion
{
    public CommandPaletteSuggestion(CommandDefinition command)
    {
        Command = command;
        Name = command.Name;
        Description = command.Description;
        Example = command.Example;
        Glyph = GlyphFor(command.Id);
    }

    public CommandPaletteSuggestion(string expression, decimal result)
    {
        Name = CalculatorParser.Format(result);
        Description = expression;
        Example = "Enter to copy";
        Glyph = "\uE8EF";
        CalculationValue = Name;
    }

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
        PaletteCommand.StartStopwatch or PaletteCommand.PauseStopwatch => "\uE917",
        PaletteCommand.ResumeStopwatch => "\uE768",
        PaletteCommand.ResetStopwatch => "\uE72C",
        PaletteCommand.FocusOn or PaletteCommand.FocusOff => "\uE8A8",
        PaletteCommand.Expand => "\uE70E",
        PaletteCommand.Collapse => "\uE70D",
        PaletteCommand.OpenSettings => "\uE713",
        _ => "\uE812",
    };
}

public sealed class CommandPaletteViewModel : ObservableObject
{
    private readonly CommandRunner _runner;
    private string _query = "";
    private string _statusMessage = "";
    private bool _hasStatus;
    private bool _isStatusError;
    private int _selectedIndex;
    private bool _showingAllCommands;

    public CommandPaletteViewModel(CommandRunner runner)
    {
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
        if (string.IsNullOrWhiteSpace(_query)) return;
        if (CalculatorParser.IsCandidate(_query))
        {
            if (CalculatorParser.TryEvaluate(_query, out var value, out var calculationError))
                ShowStatus($"Result: {CalculatorParser.Format(value)}", isError: false);
            else
                ShowStatus(calculationError ?? "Complete the expression to calculate.", isError: true);
            return;
        }

        var match = CommandCatalog.Match(_query);
        if (match is null && SelectedSuggestion is { } selected)
        {
            // Smart-complete the first word to the selected suggestion, e.g. "tim 10 minutes"
            // becomes "timer 10 minutes" before matching.
            var parts = _query.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var rebuilt = parts.Length <= 1 ? selected.Name : $"{selected.Name} {parts[1]}";
            match = CommandCatalog.Match(rebuilt);
        }
        if (match is null)
        {
            ShowStatus("Unknown command — pick a suggestion or type help.", isError: true);
            return;
        }

        var result = await _runner.ExecuteAsync(match);
        if (result.ShowAllCommands)
        {
            _showingAllCommands = true;
            RefreshSuggestions(forceAll: true);
            ShowStatus(result.Message ?? "", isError: false);
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

    private void RefreshSuggestions(bool forceAll = false)
    {
        Suggestions.Clear();
        var isCalculation = !forceAll && !_showingAllCommands && CalculatorParser.IsCandidate(_query);
        if (isCalculation)
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
                : CommandCatalog.Filter(_query);
            foreach (var command in commands)
                Suggestions.Add(new CommandPaletteSuggestion(command));
        }

        _selectedIndex = 0;
        RaisePropertyChanged(nameof(SelectedIndex));
        RaisePropertyChanged(nameof(SelectedSuggestion));
        if (!isCalculation && !_showingAllCommands && !forceAll &&
            !string.IsNullOrWhiteSpace(_query) && Suggestions.Count == 0)
            ShowStatus("No matching commands. Type help to see all.", isError: false);
        SuggestionsRefreshed?.Invoke(this, EventArgs.Empty);
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
