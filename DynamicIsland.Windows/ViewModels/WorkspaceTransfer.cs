using DynamicIsland.Windows.Infrastructure;

namespace DynamicIsland.Windows.ViewModels;

internal sealed class WorkspaceTransfer(string name, string folder) : ObservableObject
{
    private int? _progress;
    private string? _result;
    public string Name { get; } = name;
    public string Folder { get; } = folder;
    public int Percent => _progress ?? 0;
    public bool IsIndeterminate => _progress is null;
    public bool IsBusy => _result is null;
    public bool CanOpenFolder { get; private set; }
    public string Status => _result ?? (_progress is { } percent ? $"{percent}%" : "Transferring…");
    internal CancellationTokenSource Cancellation { get; } = new();

    internal void Report(int? progress)
    {
        if (!IsBusy || _progress == progress) return;
        _progress = progress;
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(Status));
    }

    internal void Finish(string result, bool succeeded = false)
    {
        _result = result;
        CanOpenFolder = succeeded;
        RaisePropertyChanged(nameof(IsBusy));
        RaisePropertyChanged(nameof(CanOpenFolder));
        RaisePropertyChanged(nameof(Status));
        Cancellation.Dispose();
    }
}
