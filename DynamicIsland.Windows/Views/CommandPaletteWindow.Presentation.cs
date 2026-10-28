using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
namespace DynamicIsland.Windows.Views;

public partial class CommandPaletteWindow
{
    private readonly DispatcherTimer _presentationMaintenance = new(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMinutes(1) };
    private bool _maintenanceStarted, _presentationClosed, _presentationMotionEnabled;
    private int _presentationGeneration;
    private Rect? _paletteWorkArea;
    public bool IsDismissing { get; private set; }
    internal int HiddenWarmCount { get; private set; }
    internal double LastHiddenWarmMilliseconds { get; private set; }
    internal double PresentationProgress => PaletteMorph.RevealProgress;

    public void StartPresentationMaintenance()
    {
        if (_maintenanceStarted) return;
        _maintenanceStarted = true;
        _presentationMaintenance.Tick += OnPresentationMaintenance;
        SystemEvents.PowerModeChanged += OnPresentationPowerChanged;
        _presentationMaintenance.Start();
    }
    private void StopPresentationMaintenance()
    {
        _presentationClosed = true;
        _presentationMaintenance.Stop();
        _presentationMaintenance.Tick -= OnPresentationMaintenance;
        if (_maintenanceStarted) SystemEvents.PowerModeChanged -= OnPresentationPowerChanged;
    }
    private void OnPresentationMaintenance(object? sender, EventArgs e) => WarmHiddenPresentation();
    private void OnPresentationPowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !_presentationClosed)
            Dispatcher.BeginInvoke(WarmHiddenPresentation, DispatcherPriority.ContextIdle);
    }
    internal void WarmHiddenPresentation()
    {
        if (_presentationClosed || IsVisible || IsPriming) return;
        var watch = Stopwatch.StartNew();
        try
        {
            _viewModel.Reset(); Width = 640;
            WarmLayout(); HiddenWarmCount++;
            LastHiddenWarmMilliseconds = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex) { Trace.TraceWarning($"Palette idle preparation failed: {ex.Message}"); }
    }
    public void PresentFromIsland(Rect anchor, Rect workArea, bool reducedMotion)
    {
        var generation = ++_presentationGeneration;
        IsDismissing = false; _presentationMotionEnabled = !reducedMotion;
        _paletteWorkArea = workArea;
        PaletteMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
        PaletteMorph.RevealProgress = reducedMotion ? 1 : 0;
        Left = Math.Max(workArea.Left + 8, Math.Min(anchor.Left + anchor.Width / 2 - Width / 2, workArea.Right - Width - 8));
        // The host has a 14-DIP inset; its neck reaches four DIPs into the island.
        Top = Math.Max(workArea.Top, anchor.Bottom - 2);
        Show(); UpdateLayout(); KeepOnScreen();
        if (reducedMotion) return;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) =>
        {
            if (generation != _presentationGeneration || IsDismissing || !IsVisible) return;
            PaletteMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
            PaletteMorph.RevealProgress = 1;
        };
        PaletteMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, animation);
    }
    public void Dismiss()
    {
        if (IsPriming || !IsVisible || IsDismissing) return;
        if (!_presentationMotionEnabled) { Hide(); return; }
        var generation = ++_presentationGeneration;
        IsDismissing = true;
        var animation = new DoubleAnimation(PaletteMorph.RevealProgress, 0, TimeSpan.FromMilliseconds(170)) { FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) => { if (generation == _presentationGeneration && IsDismissing) Hide(); };
        PaletteMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, animation);
    }
    private void ResetPresentationMotion()
    {
        ++_presentationGeneration; IsDismissing = false;
        PaletteMorph.BeginAnimation(NotificationMorphHost.RevealProgressProperty, null);
        PaletteMorph.RevealProgress = 1;
    }
}
