using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Infrastructure.CommandPalette;
using DynamicIsland.Windows.ViewModels;
using Brushes = System.Windows.Media.Brushes;

namespace DynamicIsland.Windows.Views;

public partial class CommandPaletteWindow
{
    private enum SearchMode { Google, Assistant }
    private SearchMode? _searchMode;
    private IslandViewModel? _searchAssistant;
    private CancellationTokenSource? _searchTurn;
    private bool _searchTurnVisible;
    private bool _searchTurnStarted;
    private bool _searchStopped;

    private void AttachSearch(IslandViewModel? assistant)
    {
        _searchAssistant = assistant;
        RefreshSearchAvailability();
        if (assistant is null) return;
        SearchSurface.DataContext = null;
        SearchWorking.IsVisibleChanged += (_, _) => UpdateSearchPulse();
        assistant.PropertyChanged += SearchAssistantChanged;
        Closed += (_, _) => assistant.PropertyChanged -= SearchAssistantChanged;
    }

    private void RefreshSearchAvailability()
    {
        var settings = _searchAssistant?.Settings;
        OpenGoogleSearch.Visibility = VisibleWhen(settings?.GoogleSearchEnabled == true);
        OpenAiSearch.Visibility = VisibleWhen(settings?.QEnabled == true);
        SearchGoogleMode.Visibility = OpenGoogleSearch.Visibility;
        SearchAiMode.Visibility = OpenAiSearch.Visibility;
    }

    private static Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void GoogleMode_Click(object sender, RoutedEventArgs e) => OpenSearch(SearchMode.Google);
    private void AiMode_Click(object sender, RoutedEventArgs e) => OpenSearch(SearchMode.Assistant);

    private async Task<bool> TryOpenSearchCommandAsync()
    {
        if (_viewModel.SelectedSuggestion is { IsCalculation: true }) return false;
        var match = PaletteSelection.Resolve(_viewModel.Query, _viewModel.SelectedSuggestion?.Command, _viewModel.SelectedSuggestion?.Argument);
        var mode = match?.Command.Id switch
        {
            PaletteCommand.GoogleSearch when _searchAssistant?.Settings.GoogleSearchEnabled == true => SearchMode.Google,
            PaletteCommand.Ask when _searchAssistant?.Settings.QEnabled == true => SearchMode.Assistant,
            _ => (SearchMode?)null
        };
        if (mode is null || match is null) return false;
        OpenSearch(mode.Value, match.Argument);
        if (match.Argument.Length > 0) await SubmitSearchAsync();
        return true;
    }

    private void OpenSearch(SearchMode mode, string? query = null)
    {
        if (IsDismissing || _searchTurn is not null) return;
        if (_searchMode is null)
        {
            var command = CommandCatalog.Match(_viewModel.Query);
            SearchBox.Text = query ?? (command?.Command.Id is PaletteCommand.Ask or PaletteCommand.GoogleSearch
                ? command.Argument : _viewModel.Query);
            PaletteSurface.Visibility = Visibility.Collapsed;
            SearchSurface.Visibility = Visibility.Visible;
        }
        var leavingAssistant = _searchMode == SearchMode.Assistant && mode == SearchMode.Google;
        _searchMode = mode;
        SearchSurface.DataContext = mode == SearchMode.Assistant ? _searchAssistant : null;
        if (leavingAssistant)
        {
            _searchTurnVisible = false;
            _searchAssistant?.CloseSearchChat();
        }
        SearchConsent.Visibility = Visibility.Collapsed;
        SetSearchError("");
        RefreshSearch();
        FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchMode is null) return;
        SetSearchError("");
        UpdateSearchSubmit();
    }

    private void UpdateSearchSubmit()
    {
        var google = _searchMode == SearchMode.Google;
        var hasQuery = !string.IsNullOrWhiteSpace(SearchBox.Text);
        AllowSearch.IsEnabled = hasQuery;
        SearchPlaceholder.Visibility = VisibleWhen(!hasQuery);
        SubmitSearchLabel.Text = google ? (hasQuery ? "Show Google results" : "Open Google") : _searchTurnVisible ? "Send follow-up" : "Ask Sutra";
        SubmitSearch.Visibility = VisibleWhen(_searchTurn is null && SearchConsent.Visibility != Visibility.Visible &&
            (hasQuery || (google && _searchAssistant?.Settings.GoogleSearchOpenHomepageWhenEmpty == true)));
        SubmitSearch.IsEnabled = _searchTurn is null;
    }

    private async void SubmitSearch_Click(object sender, RoutedEventArgs e) => await SubmitSearchAsync();

    private async void SearchAllow_Click(object sender, RoutedEventArgs e)
    {
        _searchAssistant?.AcceptQDisclosure();
        SearchConsent.Visibility = Visibility.Collapsed;
        await SubmitSearchAsync();
    }

    private async Task SubmitSearchAsync()
    {
        if (_searchAssistant is not { } assistant || _searchTurn is not null || IsDismissing) return;
        var query = SearchBox.Text.Trim();
        SetSearchError("");
        if (_searchMode == SearchMode.Google)
        {
            if (query.Length == 0 && !assistant.Settings.GoogleSearchOpenHomepageWhenEmpty) return;
            _commandHistory.Remember(query.Length == 0 ? "google" : $"google {query}");
            await _viewModel.OpenGoogleResultsAsync(query);
            if (_searchMode == SearchMode.Google && _viewModel.IsStatusError) SetSearchError(_viewModel.StatusMessage);
            return;
        }
        if (_searchMode != SearchMode.Assistant || query.Length == 0) return;
        if (assistant.QNeedsConsent)
        {
            SearchConsent.Visibility = Visibility.Visible;
            UpdateSearchSubmit();
            return;
        }
        if (!assistant.Settings.QEnabled || assistant.QCanStop)
        {
            SetSearchError(assistant.Settings.QEnabled ? "Sutra is already working on a request." : "Sutra Assistant is disabled in Settings.");
            return;
        }

        _commandHistory.Remember($"ask {query}");
        using var turn = new CancellationTokenSource();
        _searchTurn = turn;
        _searchTurnVisible = false;
        _searchTurnStarted = false;
        _searchStopped = false;
        SearchBox.Clear();
        SearchAnswerScroll.ScrollToTop();
        RefreshSearch();
        var failed = false;
        try
        {
            await assistant.StartSearchChatAsync(query, turn.Token);
        }
        catch (OperationCanceledException) when (turn.IsCancellationRequested)
        {
            if (ReferenceEquals(_searchTurn, turn)) _searchStopped = true;
        }
        catch (Exception)
        {
            failed = true;
        }
        finally
        {
            if (ReferenceEquals(_searchTurn, turn))
            {
                _searchTurn = null;
                _searchTurnVisible = true;
                if ((_searchStopped || failed || assistant.QState == QRunState.Error) && SearchBox.Text.Length == 0)
                    SearchBox.Text = query;
                if (failed) SetSearchError("Sutra couldn’t send this question. Try again.");
                RefreshSearch();
                if (IsActive && !IsDismissing && !IsKeyboardFocusWithin) FocusSearchBox();
            }
        }
    }

    private void StopSearch_Click(object sender, RoutedEventArgs e)
    {
        _searchStopped = true;
        _searchTurn?.Cancel();
    }

    private void SearchAssistantChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_searchMode != SearchMode.Assistant ||
            e.PropertyName is not (nameof(IslandViewModel.QState) or nameof(IslandViewModel.QResponse) or nameof(IslandViewModel.QIsSearching))) return;
        var assistant = _searchAssistant!;
        if (_searchTurn is not null && assistant.QState is QRunState.Thinking or QRunState.Streaming)
            _searchTurnStarted = true;
        if (_searchTurnStarted && (assistant.QResponse.Length > 0 || assistant.QHasActionFeedback ||
            assistant.QState is QRunState.Complete or QRunState.Error or QRunState.Cancelled))
            _searchTurnVisible = true;
        RefreshSearch();
    }

    private void RefreshSearch()
    {
        var assistant = _searchAssistant!;
        var google = _searchMode == SearchMode.Google;
        var working = _searchTurn is not null && !_searchTurnVisible;
        var answer = !google && _searchTurnVisible;
        SearchCard.Visibility = VisibleWhen(!working);
        SearchWorking.Visibility = VisibleWhen(working);
        SearchWorkingStatus.Text = _searchTurnStarted && assistant.QIsSearching ? "Searching…" : "Thinking…";
        SearchAnswer.Visibility = VisibleWhen(answer);
        StopSearchReply.Visibility = VisibleWhen(answer && _searchTurn is not null);
        SearchReply.Visibility = VisibleWhen(answer && _searchTurnStarted && (assistant.QResponse.Length > 0 || assistant.QSearchUsed));
        SearchTurnStatus.Text = _searchStopped || assistant.QState == QRunState.Cancelled ? "Answer stopped." : assistant.QState == QRunState.Error
            ? (string.IsNullOrWhiteSpace(assistant.QError) ? "Sutra couldn’t answer. Try again." : assistant.QError)
            : !assistant.QHasActionFeedback && assistant.QResponse.Length == 0 && _searchTurn is null &&
              SearchError.Visibility != Visibility.Visible ? "Sutra returned an empty answer." : "";
        SearchTurnStatus.Visibility = VisibleWhen(answer && SearchTurnStatus.Text.Length > 0);
        SearchComposer.Visibility = VisibleWhen(_searchTurn is null);
        SearchModeIcon.Text = google ? "\uE721" : "\uE945";
        SearchPlaceholder.Text = google ? "Search Google" : answer ? "Ask a follow-up" : "Ask Sutra a question";
        AutomationProperties.SetName(SearchBox, SearchPlaceholder.Text);
        SearchGoogleMode.FontWeight = google ? FontWeights.Bold : FontWeights.Normal;
        SearchAiMode.FontWeight = google ? FontWeights.Normal : FontWeights.Bold;
        SearchGoogleMode.Foreground = google ? Brushes.White : Brushes.Silver;
        SearchAiMode.Foreground = google ? Brushes.Silver : Brushes.White;
        UpdateSearchSubmit();
        ResizeSearch(working ? 180 : answer ? 560 : 520);
    }

    private void UpdateSearchPulse()
    {
        var animate = SearchWorking.IsVisible && _searchAssistant?.IsReducedMotion == false;
        for (var i = 0; i < SearchPulse.Children.Count; i++)
        {
            var dot = SearchPulse.Children[i];
            dot.BeginAnimation(OpacityProperty, animate ? new DoubleAnimation(0.28, 1, TimeSpan.FromMilliseconds(440))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(120 * i)
            } : null);
        }
    }

    private void ResizeSearch(double target)
    {
        if (_paletteWorkArea is { } area) target = Math.Min(target, Math.Max(1, area.Width - 16));
        if (Math.Abs((double)GetAnimationBaseValue(WidthProperty) - target) < 0.5) return;
        var from = Width;
        BeginAnimation(WidthProperty, null);
        Width = target;
        if (_presentationMotionEnabled)
            BeginAnimation(WidthProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void SetSearchError(string message)
    {
        SearchError.Text = message;
        SearchError.Visibility = VisibleWhen(message.Length > 0);
    }

    private void ResetSearch()
    {
        if (_searchMode is null) return;
        _searchMode = null;
        var turn = _searchTurn;
        _searchTurn = null;
        turn?.Cancel();
        SearchSurface.DataContext = null;
        _searchAssistant?.CloseSearchChat();
        _searchTurnVisible = false;
        _searchTurnStarted = false;
        _searchStopped = false;
        SearchBox.Clear();
        SearchSurface.Visibility = Visibility.Collapsed;
        PaletteSurface.Visibility = Visibility.Visible;
        BeginAnimation(WidthProperty, null);
        Width = 640;
    }
}
