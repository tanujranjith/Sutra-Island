using DynamicIsland.Windows.Infrastructure;

namespace DynamicIsland.Windows.ViewModels;

public sealed partial class SettingsViewModel
{
    private IslandViewModel? _wakeTrainingHost;
    private CancellationTokenSource? _wakeTrainingCancellation;
    private string _wakeTrainingStatus = "";
    public double WakeConfidenceThreshold
    {
        get => _settings.WakeConfidenceThreshold;
        set => Set(v => _settings.WakeConfidenceThreshold = Math.Clamp(double.IsFinite(v) ? v : .86, .80, .97), value);
    }
    public double WakeCooldownSeconds
    {
        get => _settings.WakeCooldownSeconds;
        set => Set(v => _settings.WakeCooldownSeconds = Math.Clamp(double.IsFinite(v) ? v : 4, 1, 15), value);
    }
    public bool WakeRequireTraining
    {
        get => _settings.WakeRequireTraining;
        set => Set(v => _settings.WakeRequireTraining = v, value);
    }
    public bool IsWakeTraining => _wakeTrainingCancellation is not null;
    public string WakeTrainingStatus => string.IsNullOrEmpty(_wakeTrainingStatus)
        ? _settings.WakeVoiceProfile?.IsValid == true
            ? $"Trained with {_settings.WakeVoiceProfile.SampleCount} examples. You can retrain if your microphone or surroundings change."
            : "No voice training yet. Record five clear examples of “Hey Sutra” to reduce accidental activations."
        : _wakeTrainingStatus;
    public RelayCommand TrainWakeCommand { get; private set; } = null!;
    public RelayCommand CancelWakeTrainingCommand { get; private set; } = null!;
    public RelayCommand ResetWakeTrainingCommand { get; private set; } = null!;

    private void InitializeWakeTrainingCommands()
    {
        TrainWakeCommand = new RelayCommand(() => _ = TrainWakePhraseAsync(), () => !IsWakeTraining && _wakeTrainingHost is not null);
        CancelWakeTrainingCommand = new RelayCommand(CancelWakeTraining, () => IsWakeTraining);
        ResetWakeTrainingCommand = new RelayCommand(() => _ = ResetWakeTrainingAsync(),
            () => !IsWakeTraining && _settings.WakeVoiceProfile is not null);
    }
    public void AttachWakeTraining(IslandViewModel host)
    {
        _wakeTrainingHost = host;
        RefreshWakeTraining();
    }
    public void CancelWakeTraining() => _wakeTrainingCancellation?.Cancel();
    private void RefreshWakeTraining()
    {
        RaisePropertyChanged(nameof(IsWakeTraining));
        RaisePropertyChanged(nameof(WakeTrainingStatus));
        TrainWakeCommand.RaiseCanExecuteChanged();
        CancelWakeTrainingCommand.RaiseCanExecuteChanged();
        ResetWakeTrainingCommand.RaiseCanExecuteChanged();
    }
    private async Task TrainWakePhraseAsync()
    {
        if (IsWakeTraining || _wakeTrainingHost is null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _wakeTrainingCancellation = cancellation;
        _wakeTrainingStatus = "Preparing microphone…";
        RefreshWakeTraining();
        var progress = new Progress<string>(message =>
        {
            if (!ReferenceEquals(_wakeTrainingCancellation, cancellation) || cancellation.IsCancellationRequested) return;
            _wakeTrainingStatus = message;
            RaisePropertyChanged(nameof(WakeTrainingStatus));
        });
        try
        {
            await _wakeTrainingHost.TrainWakeAsync(progress, cancellation.Token);
            _wakeTrainingStatus = "Training complete. Your five examples are saved on this PC. Say “Hey Sutra” on its own, wait for the dots, then ask your question. Wake listening follows the switch above.";
        }
        catch (OperationCanceledException)
        { _wakeTrainingStatus = "Training canceled. Your previous voice training is unchanged."; }
        catch (Exception ex)
        { _wakeTrainingStatus = "Training could not finish: " + ex.Message; }
        finally
        {
            _wakeTrainingCancellation = null;
            RefreshWakeTraining();
        }
    }
    private async Task ResetWakeTrainingAsync()
    {
        if (IsWakeTraining) return;
        _settings.WakeVoiceProfile = null;
        _wakeTrainingStatus = "Voice training removed. Stricter default wake checks remain active.";
        await SaveAsync();
        RefreshWakeTraining();
    }
}
