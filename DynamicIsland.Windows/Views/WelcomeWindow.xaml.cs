using System.Windows;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services.Q;

namespace DynamicIsland.Windows.Views;

public partial class WelcomeWindow : Window
{
    private readonly AppSettings _settings;
    private int _step;
    private static readonly (string Eyebrow, string Title, string Description, string Demo, string Hint)[] Steps =
    [
        ("WELCOME", "A little more room for everything.", "Meet your island: a calm place for the things you need throughout the day. Let’s make it yours.", "●  Sutra Island", "A small space, ready when you are"),
        ("YOUR ISLAND", "Everything at a glance.", "Hover over the island to expand it. See your music, battery, time, timers and live activity without losing your place.", "♫  Now playing", "Try hovering over your island after setup"),
        ("SUTRA ASSISTANT", "A little help, right here.", "Open Sutra Assistant to ask a question or get help with what’s on screen. Configure a provider in Settings before your first request.", "✦  Ask Sutra", "Choose a provider in Settings → Sutra Assistant"),
        ("SUTRA FLOW", "Your words, ready to go.", "Hold Right Ctrl, speak, and release to dictate into your current text field. Flow uses an on-device speech model.", "● ● ● ● ●", "Hold Right Ctrl  →  speak  →  release"),
        ("MAKE IT YOURS", "Choose how Sutra sounds.", "Replies use a local voice model. Pick a voice and choose whether the assistant should listen for its wake phrase.", "◖  Voice ready  ◗", "Voice and wake settings can be changed later"),
        ("READY TO GO", "The island is yours.", "Explore Settings to customize your layout, shortcuts, notifications and more. You can change these choices whenever you like.", "✦  All set", "Your island is ready")
    ];

    public WelcomeWindow(AppSettings settings)
    {
        InitializeComponent(); _settings = settings;
        VoiceChoice.ItemsSource = LocalSpeechOutput.Voices;
        VoiceChoice.SelectedItem = LocalSpeechOutput.Voices.Contains(settings.VoiceName) ? settings.VoiceName : LocalSpeechOutput.Voices[0];
        WakeChoice.IsChecked = settings.WakeListeningEnabled;
        ShowStep();
    }
    private void ShowStep()
    {
        var item = Steps[_step]; Eyebrow.Text = item.Eyebrow; Headline.Text = item.Title; Description.Text = item.Description;
        Demo.Text = item.Demo; Hint.Text = item.Hint; ProgressLabel.Text = $"{_step + 1:00} / {Steps.Length:00}";
        OptionsPanel.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = _step == Steps.Length - 1 ? "Start exploring" : "Continue";
    }
    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) { _step--; ShowStep(); } }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < Steps.Length - 1) { _step++; ShowStep(); return; }
        _settings.VoiceName = VoiceChoice.SelectedItem as string ?? LocalSpeechOutput.Voices[0];
        _settings.WakeListeningEnabled = WakeChoice.IsChecked == true;
        DialogResult = true;
    }
}
