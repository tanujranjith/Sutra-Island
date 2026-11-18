using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services.Flow;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

public partial class WelcomeWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IslandViewModel _island;
    private readonly FlowController? _flow;
    private readonly LocalSpeechOutput _voicePreview = new();
    private CancellationTokenSource? _previewCancellation;
    private int _step;
    public bool Completed { get; private set; }
    private static readonly (string Eyebrow, string Title, string Description, string Hint)[] Steps =
    [
        ("WELCOME TO SUTRA", "Your desktop,\nmore in tune.", "A little space that keeps your music, tools, voice and assistant within reach. Let’s make it feel like yours.", "This is a live view of your actual island, not a demo."),
        ("01 / MEET YOUR ISLAND", "Go ahead.\nMake it yours.", "Your island is already running above. Try expanding it or opening the assistant using the buttons below, then interact with the real island.", "The preview follows your real island as it changes."),
        ("02 / SUTRA FLOW", "Say it.\nSee it written.", "Try dictation here. The dots in your island respond to your microphone, then your words appear in the practice box.", "Flow transcribes on your device. Your practice text stays here."),
        ("03 / YOUR VOICE", "Find a voice\nyou actually like.", "Choose how Sutra sounds and preview it. Voice synthesis runs locally, and you can change your choice later.", "Your voice choice is saved when you finish setup."),
        ("READY WHEN YOU ARE", "That was just\nthe beginning.", "Your island is ready. Explore the assistant, music controls, timers and more—or return to this guide anytime in Assistant settings.", "Your desktop. Your pace. Your island.")
    ];

    public WelcomeWindow(AppSettings settings, IslandViewModel island, IslandWindow islandWindow, FlowController? flow)
    {
        InitializeComponent();
        _settings = settings; _island = island; _flow = flow;
        VoiceChoice.ItemsSource = LocalSpeechOutput.Voices;
        VoiceChoice.SelectedItem = LocalSpeechOutput.Voices.Contains(settings.VoiceName) ? settings.VoiceName : LocalSpeechOutput.Voices[0];
        WakeChoice.IsChecked = settings.WakeListeningEnabled;
        var pushKey = settings.FlowPushKey switch { "RightAlt" => "Right Alt", "RightShift" => "Right Shift", _ => "Right Ctrl" };
        FlowInstructions.Text = $"Click the box, hold {pushKey}, speak a sentence, then release.";
        FlowFeedback.Text = settings.FlowEnabled ? "Ready when you are." : "Sutra Flow is off. Enable it in Settings to try dictation.";
        PracticeText.IsReadOnly = !settings.FlowEnabled;
        LiveIslandPreview.Fill = new VisualBrush(islandWindow.ExpandedPreviewVisual) { Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
        PreviewFallback.Visibility = Visibility.Collapsed;
        _island.PropertyChanged += Island_PropertyChanged;
        if (_flow is not null) _flow.StatusChanged += Flow_StatusChanged;
        Closed += (_, _) =>
        {
            _island.PropertyChanged -= Island_PropertyChanged;
            if (_flow is not null) _flow.StatusChanged -= Flow_StatusChanged;
            _previewCancellation?.Cancel();
            _voicePreview.Dispose();
            LiveIslandPreview.Fill = null;
        };
        ShowStep(); UpdateIslandStatus();
    }

    private void ShowStep()
    {
        var item = Steps[_step];
        Eyebrow.Text = item.Eyebrow; Headline.Text = item.Title; Description.Text = item.Description; Hint.Text = item.Hint;
        ProgressLabel.Text = $"{_step + 1:00} / {Steps.Length:00}";
        IslandPanel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        FlowPanel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        VoicePanel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = _step == Steps.Length - 1 ? "Finish setup  →" : "Continue  →";
        FooterHint.Text = _step == 0 ? "A short tour · skip or return anytime" : "Your island remains available while you explore";
        var entries = new[] { NavWelcome, NavIsland, NavFlow, NavVoice, NavReady };
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i].Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(i == _step ? "#F7F4FF" : "#888093")!;
            entries[i].FontWeight = i == _step ? FontWeights.SemiBold : FontWeights.Normal;
        }
        if (_step != 3) { _previewCancellation?.Cancel(); _voicePreview.StopSpeaking(); }
    }

    private void Island_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(IslandViewModel.IsExpanded) or nameof(IslandViewModel.IsQActive)) UpdateIslandStatus(); }
    private void UpdateIslandStatus()
    {
        ToggleIslandButton.Content = _island.IsExpanded ? "Collapse the island" : "Expand the island";
        LiveStatus.Text = _island.IsQActive ? "Assistant is open in your island" : _island.IsExpanded ? "Your island is expanded" : "Your island is live";
    }
    private void ToggleIsland_Click(object sender, RoutedEventArgs e)
    {
        _island.ToggleExpandedCommand.Execute(null);
        IslandActionStatus.Text = "That changed your actual island. Try interacting with it above.";
    }
    private async void OpenAssistant_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.QEnabled) { IslandActionStatus.Text = "Enable Sutra Assistant in Settings to open it."; return; }
        try { await _island.StartChatAsync(); _island.IsExpanded = true; IslandActionStatus.Text = "Assistant is open in your island. Connect a provider in Settings when you are ready to ask."; }
        catch (Exception ex) { IslandActionStatus.Text = "Couldn’t open Assistant: " + ex.Message; }
    }
    private void Flow_StatusChanged(object? sender, FlowStatus status)
    {
        FlowFeedback.Text = status.State switch
        {
            FlowState.Listening => "Listening… keep holding the key.",
            FlowState.Released or FlowState.Polishing => "Turning your words into text…",
            FlowState.Pasted => "Nice—your words arrived in the practice box.",
            FlowState.Error => status.Detail,
            _ => _settings.FlowEnabled ? "Ready for another try." : "Enable Sutra Flow in Settings to try dictation."
        };
    }
    private async void PreviewVoice_Click(object sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel();
        _voicePreview.StopSpeaking();
        using var preview = new CancellationTokenSource();
        _previewCancellation = preview;
        PreviewVoiceButton.IsEnabled = false;
        VoiceFeedback.Text = "Preparing your local voice…";
        try
        {
            await _voicePreview.SpeakAsync("Hello, I’m Sutra. It’s nice to meet you.", VoiceChoice.SelectedItem as string ?? LocalSpeechOutput.Voices[0], preview.Token);
            if (!preview.IsCancellationRequested) VoiceFeedback.Text = "That’s your local voice. Try another, or continue.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { VoiceFeedback.Text = "Voice preview unavailable: " + ex.Message; }
        finally
        {
            if (ReferenceEquals(_previewCancellation, preview)) _previewCancellation = null;
            PreviewVoiceButton.IsEnabled = true;
        }
    }
    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) { _step--; ShowStep(); } }
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < Steps.Length - 1) { _step++; ShowStep(); return; }
        _settings.VoiceName = VoiceChoice.SelectedItem as string ?? LocalSpeechOutput.Voices[0];
        _settings.WakeListeningEnabled = WakeChoice.IsChecked == true;
        Completed = true;
        Close();
    }
}
