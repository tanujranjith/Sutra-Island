using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using DynamicIsland.Q.Core;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Q;
using DynamicIsland.Windows.Services.Flow;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyFeatureSettingsAsync(IslandViewModel vm, IslandWindow island,
        SettingsService persistence, LoggingService log, IQProviderRegistry registry,
        Action<bool, string> check, string output)
    {
        if (!AppDataPaths.IsPreview) throw new InvalidOperationException("Settings verification requires preview isolation.");
        var defaults = new AppSettings();
        check(defaults.GoogleSearchEnabled && defaults.GoogleSearchOpenHomepageWhenEmpty
            && !defaults.GoogleSearchSafeSearch && defaults.GoogleSearchLanguage == "auto",
            "Google search defaults preserve browser redirects and automatic results language");
        check(defaults.WakeConfidenceThreshold == .86 && defaults.WakeCooldownSeconds == 4
            && !defaults.WakeRequireTraining && defaults.WakeVoiceProfile is null,
            "Wake customization starts with strict confidence, cooldown, and no enrolled voice");
        check(defaults.RememberIslandScreenshots && defaults.IslandScreenshotHistoryLimit == 10,
            "Island screenshots default to bounded session history");
        check(defaults.FlowEnabled && defaults.FlowPushKey == "RightCtrl" && defaults.FlowLanguage == "en"
            && defaults.FlowCleanup == FlowCleanupMode.Basic && defaults.FlowSounds && defaults.FlowSmartFormatting
            && defaults.FlowDuckAudio && defaults.FlowDuckPercent == 10 && defaults.FlowCodeVariables
            && defaults.FlowFileTagging && !defaults.FlowAutoDictionary && !defaults.FlowCreatorMode
            && !defaults.FlowExpressive && defaults.FlowVocabulary.Count == 0,
            "Restored Sutra Flow defaults retain local push-to-talk dictation, basic cleanup, and audio ducking");
        check(defaults.CompactVoiceMode == CompactVoicePolicy.DefaultEnabled,
            "Compact voice settings preserve the restored voice experience default");
        Dictionary<string, string> FlowSnapshot(AppSettings settings) => typeof(AppSettings).GetProperties()
            .Where(property => property.Name.StartsWith("Flow", StringComparison.Ordinal))
            .ToDictionary(property => property.Name, property => System.Text.Json.JsonSerializer.Serialize(property.GetValue(settings)));
        await persistence.SaveAsync(defaults);
        var restoredDefaults = await persistence.LoadAsync();
        check(FlowSnapshot(defaults).All(pair => FlowSnapshot(restoredDefaults).GetValueOrDefault(pair.Key) == pair.Value)
            && restoredDefaults.CompactVoiceMode == defaults.CompactVoiceMode,
            "Every Flow default and compact voice preference survives settings save and reload");
        // Some two-way Flow selectors normalize their value when first bound.
        // Keep the controller disabled even if a selector invokes its normal save
        // path; this must not download a model or enable push-to-talk in a test.
        vm.Settings.FlowEnabled = false;

        using var codex = new AsyncDisposeAdapter(new CodexAppServerClient(log: log));
        var editorVm = new SettingsViewModel(vm.Settings, persistence, new StartupService(log),
            vm.ApplySettings, () => { }, () => { }, new FakeSecrets(), registry,
            new CodexAccountCoordinator(codex.Client, log));
        editorVm.AttachWakeTraining(vm);
        // Construct the settings surface only: never Start/ApplySettings the controller
        // or execute download, recording, paste, or ducking commands in this fixture.
        using var flow = new FlowController(vm.Settings, persistence, island.Dispatcher, log, registry, new FakeSecrets());
        var flowVm = new FlowSettingsViewModel(vm.Settings, persistence, flow);
        var editor = new SettingsWindow(editorVm, vm, island)
        {
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false,
            Title = "Feature settings verification " + Guid.NewGuid().ToString("N")
        };
        async Task Layout()
        {
            await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, DispatcherPriority.ApplicationIdle);
        }
        T Bound<T>(DependencyProperty property, string path) where T : FrameworkElement =>
            FeatureLogicalDescendants(editor).OfType<T>().Single(element =>
                BindingOperations.GetBindingExpression(element, property)?.ParentBinding.Path?.Path == path);
        var flowPage = (FrameworkElement)editor.FindName("FlowPage");
        flowPage.DataContext = flowVm;
        Dictionary<string, string>? customizedFlow = null;
        try
        {
            island.Show(); editor.Show(); editor.OpenQSettings(); await Layout();
            check(editorVm.SelectedSectionKey == "q" && ((FrameworkElement)editor.FindName("QPage")).IsVisible,
                "Assistant settings expose the wake phrase training section");
            var compactVoice = Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.CompactVoiceMode));
            check(compactVoice.IsChecked == vm.Settings.CompactVoiceMode && compactVoice.DataContext == editorVm
                && System.Windows.Automation.AutomationProperties.GetName(compactVoice) == "Compact voice mode",
                "Assistant settings retain the bound and named Compact voice mode checkbox");
            editorVm.CompactVoiceMode = !defaults.CompactVoiceMode;
            await Layout();
            check(compactVoice.IsChecked == !defaults.CompactVoiceMode,
                "Compact voice mode responds immediately to its saved preference");
            check(editorVm.TrainWakeCommand.CanExecute(null) && !editorVm.CancelWakeTrainingCommand.CanExecute(null)
                && !editorVm.ResetWakeTrainingCommand.CanExecute(null) && !editorVm.IsWakeTraining,
                "Training is available while cancel and reset are disabled before enrollment");
            var trainingButtons = FeatureLogicalDescendants(editor).OfType<Button>()
                .Where(button => button.Command == editorVm.TrainWakeCommand
                    || button.Command == editorVm.CancelWakeTrainingCommand || button.Command == editorVm.ResetWakeTrainingCommand).ToArray();
            check(trainingButtons.Length == 3 && trainingButtons.Single(button => button.Command == editorVm.TrainWakeCommand).IsEnabled
                && trainingButtons.Where(button => button.Command != editorVm.TrainWakeCommand).All(button => !button.IsEnabled),
                "Rendered training buttons reflect their command availability without using the microphone");
            check(editorVm.WakeTrainingStatus.Contains("No voice training yet")
                && FeatureLogicalDescendants(editor).OfType<TextBlock>().Any(text => text.Text == editorVm.WakeTrainingStatus),
                "The training section clearly explains that no voice has been enrolled");
            editor.OpenSearch(nameof(SettingsViewModel.TrainWakeCommand)); await Layout();
            check(editorVm.SelectedSectionKey == "q" && trainingButtons.Single(button => button.Command == editorVm.TrainWakeCommand).IsVisible,
                "Settings search opens the training action in the assistant section");
            var confidence = Bound<Slider>(RangeBase.ValueProperty, nameof(SettingsViewModel.WakeConfidenceThreshold));
            var cooldown = Bound<Slider>(RangeBase.ValueProperty, nameof(SettingsViewModel.WakeCooldownSeconds));
            check(confidence.Minimum == .80 && confidence.Maximum == .97 && cooldown.Minimum == 1 && cooldown.Maximum == 15,
                "Wake confidence and cooldown sliders expose their supported adjustment ranges");
            check(FeatureLogicalDescendants(editor).OfType<TextBlock>().Any(text => text.Text.Contains("you only repeat examples", StringComparison.Ordinal)),
                "Training explains that matching examples are retained while an outlier is repeated");
            var previousProfile = vm.Settings.WakeVoiceProfile;
            try
            {
                // Synthetic feature vectors only: this checks persistence and UI
                // compatibility without recording or enrolling anyone's voice.
                var syntheticFeatures = Enumerable.Range(0, 192).Select(i => Math.Sin((i % 12) * .7 + (i / 12) * .12)).ToArray();
                var syntheticProfile = WakeDetectionPolicy.CreateProfile(
                    Enumerable.Range(0, 5).Select(_ => syntheticFeatures.ToArray()).ToArray(), [1, 1, 1, 1, 1]);
                vm.Settings.WakeVoiceProfile = syntheticProfile;
                await persistence.SaveAsync(vm.Settings);
                var savedTraining = (await persistence.LoadAsync()).WakeVoiceProfile;
                check(savedTraining is { Version: 2, IsValid: true, SampleCount: 5 }
                    && System.Text.Json.JsonSerializer.Serialize(savedTraining) == System.Text.Json.JsonSerializer.Serialize(syntheticProfile),
                    "New training profiles survive Settings normalization and save/reload without changing their samples or limits");
                editorVm.RefreshRuntimePreferences(); await Layout();
                check(editorVm.WakeTrainingStatus.Contains("Trained with 5 examples")
                    && FeatureLogicalDescendants(editor).OfType<TextBlock>().Any(text => text.Text == editorVm.WakeTrainingStatus)
                    && editorVm.TrainWakeCommand.CanExecute(null) && editorVm.ResetWakeTrainingCommand.CanExecute(null)
                    && !editorVm.CancelWakeTrainingCommand.CanExecute(null),
                    "Settings recognizes new saved training and enables retraining and reset without microphone input");
                syntheticProfile.Version = 1;
                await persistence.SaveAsync(vm.Settings);
                check((await persistence.LoadAsync()).WakeVoiceProfile is { Version: 1, IsValid: true },
                    "Existing Version 1 voice training remains valid through Settings normalization");
            }
            finally
            {
                vm.Settings.WakeVoiceProfile = previousProfile;
                await persistence.SaveAsync(vm.Settings);
                editorVm.RefreshRuntimePreferences(); await Layout();
            }
            await CaptureSutraAsync(editor, output, "feature-settings-wake");

            var flowTab = (ToggleButton)editor.FindName("FlowTab");
            check(flowTab.Content?.ToString() == "Sutra Flow" && flowTab.CommandParameter?.ToString() == "flow"
                && flowTab.Visibility == Visibility.Visible,
                "Settings navigation retains the restored Sutra Flow entry");
            flowTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Layout();
            check(flowPage.IsVisible && flowTab.IsChecked == true && ReferenceEquals(flowPage.DataContext, flowVm),
                "The Sutra Flow navigation entry opens its own bound settings page");
            var pushKey = Bound<ComboBox>(Selector.SelectedItemProperty, nameof(FlowSettingsViewModel.PushKey));
            var flowLanguage = Bound<ComboBox>(Selector.SelectedItemProperty, nameof(FlowSettingsViewModel.Language));
            var cleanup = Bound<ComboBox>(Selector.SelectedItemProperty, nameof(FlowSettingsViewModel.Cleanup));
            check(pushKey.Items.Count == 3 && pushKey.SelectedItem?.ToString() == "Right Ctrl"
                && flowLanguage.Items.Count >= 10 && flowLanguage.SelectedItem?.ToString() == "English (fastest)"
                && cleanup.Items.Count == 3 && cleanup.SelectedItem?.ToString() == "Basic (on this PC)",
                "Flow controls retain push-to-talk key, recognition language, and cleanup choices");
            check(Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(FlowSettingsViewModel.Enabled)).IsChecked == false
                && Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(FlowSettingsViewModel.DuckAudio)).IsChecked == true
                && Bound<Slider>(RangeBase.ValueProperty, nameof(FlowSettingsViewModel.DuckPercent)).Value == 10,
                "Flow controls honor an explicit disabled preference while retaining default audio ducking");
            var flowCommands = FeatureLogicalDescendants(flowPage).OfType<Button>().Select(button => button.Command).ToArray();
            check(flowCommands.Contains(flowVm.DownloadModelCommand) && flowCommands.Contains(flowVm.CopyHeldTextCommand)
                && flowCommands.Contains(flowVm.ImportVocabularyCommand) && flowCommands.Contains(flowVm.ResetCommand),
                "Flow retains model download, undelivered dictation, vocabulary import, and reset actions");
            check(flow.Status.State == FlowState.Idle && !flow.Engine.IsDownloading,
                "Restored Flow settings render without starting dictation, opening the microphone, or downloading a model");
            FeatureLogicalDescendants(flowPage).OfType<TextBlock>().First().BringIntoView(); await Layout();
            await CaptureSutraAsync(editor, output, "feature-settings-flow-restored");

            // Change the model directly and refresh bindings. Flow preference setters
            // normally reconfigure/download the engine; this fixture stays offline.
            vm.Settings.FlowEnabled = false;
            vm.Settings.FlowPushKey = "RightShift";
            vm.Settings.FlowLanguage = "ta";
            vm.Settings.FlowCleanup = FlowCleanupMode.Off;
            vm.Settings.FlowSounds = false;
            vm.Settings.FlowSmartFormatting = false;
            vm.Settings.FlowDuckAudio = false;
            vm.Settings.FlowDuckPercent = 27;
            vm.Settings.FlowCreatorMode = true;
            vm.Settings.FlowAutoDictionary = true;
            vm.Settings.FlowCodeVariables = false;
            vm.Settings.FlowFileTagging = false;
            vm.Settings.FlowExpressive = true;
            vm.Settings.FlowVocabulary = ["Sutra", "Fixture"];
            vm.Settings.FlowWordsDictated = 42;
            customizedFlow = FlowSnapshot(vm.Settings);
            flowVm.Refresh(); await Layout();
            check(pushKey.SelectedItem?.ToString() == "Right Shift" && flowLanguage.SelectedItem?.ToString() == "Tamil"
                && cleanup.SelectedItem?.ToString() == "Off"
                && Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(FlowSettingsViewModel.Enabled)).IsChecked == false
                && Bound<Slider>(RangeBase.ValueProperty, nameof(FlowSettingsViewModel.DuckPercent)).Value == 27,
                "Restored Flow controls continue to display customized key, language, enable, cleanup, and ducking preferences");

            editorVm.GoogleSearchEnabled = false;
            editorVm.GoogleSearchOpenHomepageWhenEmpty = false;
            editorVm.GoogleSearchSafeSearch = true;
            editorVm.GoogleSearchLanguage = "hi";
            editorVm.WakeConfidenceThreshold = .91;
            editorVm.WakeCooldownSeconds = 9;
            editorVm.WakeRequireTraining = true;
            editorVm.RememberIslandScreenshots = false;
            editorVm.IslandScreenshotHistoryLimit = 7;
            await Layout();
            check(confidence.Value == .91 && cooldown.Value == 9
                && Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.WakeRequireTraining)).IsChecked == true,
                "Wake controls synchronize immediately with customized preference values");

            editor.OpenSearch(nameof(SettingsViewModel.GoogleSearchLanguage)); await Layout();
            check(editorVm.SelectedSectionKey == "advanced", "Google language can be opened directly from settings search");
            var language = Bound<ComboBox>(Selector.SelectedValueProperty, nameof(SettingsViewModel.GoogleSearchLanguage));
            check(language.Items.Count >= 10 && language.SelectedValue?.ToString() == "hi"
                && language.DisplayMemberPath == "Name" && language.SelectedValuePath == "Code",
                "Google results language displays named choices and retains the chosen language code");
            check(Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.GoogleSearchEnabled)).IsChecked == false
                && Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.GoogleSearchOpenHomepageWhenEmpty)).IsChecked == false
                && Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.GoogleSearchSafeSearch)).IsChecked == true,
                "Google enable, empty-query, and SafeSearch controls reflect their individual preferences");
            await CaptureSutraAsync(editor, output, "feature-settings-google");

            editor.OpenSearch(nameof(SettingsViewModel.RememberIslandScreenshots)); await Layout();
            var historyLimit = Bound<Slider>(RangeBase.ValueProperty, nameof(SettingsViewModel.IslandScreenshotHistoryLimit));
            check(editorVm.SelectedSectionKey == "advanced" && historyLimit.Minimum == 1 && historyLimit.Maximum == 30
                && historyLimit.Value == 7 && Bound<CheckBox>(ToggleButton.IsCheckedProperty, nameof(SettingsViewModel.RememberIslandScreenshots)).IsChecked == false,
                "Screenshot session history has a searchable toggle and adjustable capture limit");
            await CaptureSutraAsync(editor, output, "feature-settings-screenshots");

            // Persist the isolated fixture directly; never touch Windows startup registration.
            await persistence.SaveAsync(vm.Settings);
            var reloaded = await persistence.LoadAsync();
            check(!reloaded.GoogleSearchEnabled && !reloaded.GoogleSearchOpenHomepageWhenEmpty
                && reloaded.GoogleSearchSafeSearch && reloaded.GoogleSearchLanguage == "hi",
                "All four Google customization options survive settings save and reload");
            check(reloaded.WakeConfidenceThreshold == .91 && reloaded.WakeCooldownSeconds == 9 && reloaded.WakeRequireTraining,
                "Wake confidence, cooldown, and training requirement survive settings save and reload");
            check(!reloaded.RememberIslandScreenshots && reloaded.IslandScreenshotHistoryLimit == 7,
                "Screenshot history preferences survive settings save and reload");
            var reloadedFlow = FlowSnapshot(reloaded);
            check(customizedFlow.All(pair => reloadedFlow.GetValueOrDefault(pair.Key) == pair.Value)
                && !reloaded.FlowEnabled && reloaded.FlowPushKey == "RightShift" && reloaded.FlowLanguage == "ta"
                && reloaded.FlowWordsDictated == 42 && reloaded.FlowVocabulary.SequenceEqual(new[] { "Sutra", "Fixture" }),
                "Every customized Flow property, vocabulary, and usage count survives settings save and reload");
            check(reloaded.CompactVoiceMode == !defaults.CompactVoiceMode && !flow.Engine.IsDownloading
                && flow.Status.State == FlowState.Idle,
                "Compact voice customization persists while restored Flow verification remains idle and offline");
            check(reloaded.WakeVoiceProfile is null && !editorVm.IsWakeTraining,
                "Settings verification never enrolls a voice or records microphone audio");
        }
        finally { editor.Close(); }
    }

    private static IEnumerable<DependencyObject> FeatureLogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in FeatureLogicalDescendants(child)) yield return nested;
        }
    }
}
