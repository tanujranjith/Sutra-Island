using System.Diagnostics;
using DynamicIsland.Windows.Infrastructure.CommandPalette;

namespace DynamicIsland.Windows;

public partial class App
{
    void ICommandPaletteHost.OpenDefaultBrowser(Uri destination)
    {
        if (_isShuttingDown) return;
        _ = LaunchDefaultBrowserAsync(destination);
    }

    private async Task LaunchDefaultBrowserAsync(Uri destination)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await GoogleSearch.LaunchAsync(destination).ConfigureAwait(false);
            _log?.Info($"Default browser handoff completed: elapsedMs={watch.Elapsed.TotalMilliseconds:0}.");
        }
        catch (Exception ex)
        {
            _log?.Error($"Default browser handoff failed after {watch.Elapsed.TotalMilliseconds:0} ms", ex);
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (_isShuttingDown) return;
                _tray?.ShowNotification("Google search couldn't open",
                    "Check your Windows default browser. Click this notification to try again.",
                    () => ((ICommandPaletteHost)this).OpenDefaultBrowser(destination));
            });
        }
    }
}
