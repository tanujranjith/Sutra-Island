using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

/// <summary>
/// Compact voice mode host: the compact island widens into the Sutra Assistant voice orb (and grows taller
/// for an answer) instead of expanding into the chat panel. The orb itself lives in <see cref="CompactVoiceView"/>.
/// </summary>
public partial class IslandWindow
{
    private bool _compactVoiceMorphing;
    private (double W, double H) _compactVoiceMorphTarget;
    private int _compactVoiceMorphGeneration;
    private bool _lastCompactVoiceWide, _lastCompactVoiceTall, _lastCompactVoiceVisible;

    private void InitializeCompactVoice()
    {
        CompactVoice.OpenInChatRequested += (_, _) => _viewModel.OpenCompactVoiceInChat();
        CompactVoice.MascotClicked += (_, _) => _viewModel.StopCompactVoiceSpeech();
        CompactVoice.FixRequested += (_, _) =>
        {
            var target = _viewModel.CompactVoiceFixTarget;
            _viewModel.DismissCompactVoice();
            if (target is null) { OpenQSettingsRequested?.Invoke(this, EventArgs.Empty); return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { _log.Debug($"Could not open {target}: {ex.Message}"); }
        };
        _viewModel.PropertyChanged += OnCompactVoicePropertyChanged;
        CompactVoice.BaseHeight = CompactVoiceBaseHeight();
    }

    private bool IsCompactVoiceMorphTo(double width, double height)
        => _compactVoiceMorphing && Math.Abs(_compactVoiceMorphTarget.W - width) < 0.5 && Math.Abs(_compactVoiceMorphTarget.H - height) < 0.5;

    /// <summary>The orb's single-line height: the compact island's, with a floor for the mascot.</summary>
    private double CompactVoiceBaseHeight()
        => CompactVoicePolicy.Size(Math.Clamp(_viewModel.Settings.IslandWidth, 72d, 360d), Math.Clamp(_viewModel.Settings.IslandHeight, 38d, 90d), tall: false).Height;

    private void OnCompactVoicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Handled synchronously: the shell must start its exit morph before the view model releases the
        // voice session, whose layout pass would otherwise snap the island back in one frame.
        if (e.PropertyName is nameof(IslandViewModel.CompactVoiceWide) or nameof(IslandViewModel.CompactVoiceTall) or nameof(IslandViewModel.CompactVoiceVisible) or null)
            SyncCompactVoiceShell();
    }

    private void SyncCompactVoiceShell()
    {
        var visible = _viewModel.CompactVoiceVisible;
        var wide = _viewModel.CompactVoiceWide;
        var tall = _viewModel.CompactVoiceTall;
        var reduced = _viewModel.IsReducedMotion;
        CompactVoice.BaseHeight = CompactVoiceBaseHeight();
        if (visible != _lastCompactVoiceVisible)
        {
            _lastCompactVoiceVisible = visible;
            if (visible) { _collapseTimer.Stop(); _idleTimer.Stop(); SetDimmed(false); }
        }
        // The lavender rim fades in with the wake and out with the exit.
        if (wide && visible)
        {
            CompactVoiceRim.Visibility = Visibility.Visible;
            CompactVoiceRim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(reduced ? 150 : 280)));
        }
        else if (CompactVoiceRim.Visibility == Visibility.Visible)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(reduced ? 150 : CompactVoiceTimings.ExitMascot.TotalMilliseconds));
            fade.Completed += (_, _) => { if (!_viewModel.CompactVoiceWide) CompactVoiceRim.Visibility = Visibility.Collapsed; };
            CompactVoiceRim.BeginAnimation(OpacityProperty, fade);
        }

        if (wide == _lastCompactVoiceWide && tall == _lastCompactVoiceTall) return;
        var widening = wide && !_lastCompactVoiceWide;
        var growing = tall && !_lastCompactVoiceTall;
        _lastCompactVoiceWide = wide; _lastCompactVoiceTall = tall;
        if (_timerPanelOpen || _viewModel.IsExpanded) return;
        var m = Metrics();
        // Keep the island anchored the same way an expansion does (edge and corner positions).
        _position.PositionInitial(this, _viewModel.Settings, m.cW, m.cH);
        UpdateTimerOrbLayout(m.cW, m.cH);
        UpdatePrivacyIndicatorLayout(m.cW, m.cH, animate: false);
        var duration = widening ? IslandMorphDuration(expanded: true)
            : growing || tall ? CompactVoiceTimings.Grow
            : CompactVoiceTimings.ExitMascot;
        AnimateCompactVoiceShell(m.cW, m.cH, animate: !reduced && _viewModel.Settings.AnimationIntensity != Models.AnimationIntensity.Reduced, duration, widening || growing);
    }

    /// <summary>Morph the compact shell between the island and the orb with the island's own easing.</summary>
    private void AnimateCompactVoiceShell(double targetW, double targetH, bool animate, TimeSpan duration, bool opening)
    {
        var generation = ++_compactVoiceMorphGeneration;
        var currentW = Math.Max(1d, GlassShell.ActualWidth);
        var currentH = Math.Max(1d, GlassShell.ActualHeight);
        GlassShell.BeginAnimation(WidthProperty, null);
        GlassShell.BeginAnimation(HeightProperty, null);
        _compactVoiceMorphTarget = (targetW, targetH);
        if (!animate || (Math.Abs(currentW - targetW) < 0.5 && Math.Abs(currentH - targetH) < 0.5))
        {
            _compactVoiceMorphing = false;
            GlassShell.Width = targetW; GlassShell.Height = targetH;
            return;
        }
        _compactVoiceMorphing = true;
        GlassShell.Width = currentW; GlassShell.Height = currentH;
        SetSurfaceInMotion(GlassShell, true);
        UseFastPillShadow();
        _frostBackdrop.SetSuspended(true);
        var ease = IslandMorphEase(expanded: opening);
        var width = new DoubleAnimation(currentW, targetW, duration) { EasingFunction = ease };
        var height = new DoubleAnimation(currentH, targetH, duration) { EasingFunction = ease };
        height.Completed += (_, _) =>
        {
            if (generation != _compactVoiceMorphGeneration || !_compactVoiceMorphing) return;
            _compactVoiceMorphing = false;
            GlassShell.BeginAnimation(WidthProperty, null);
            GlassShell.BeginAnimation(HeightProperty, null);
            GlassShell.Width = targetW; GlassShell.Height = targetH;
            SetSurfaceInMotion(GlassShell, false);
            RestorePillShadow();
            _frostBackdrop.SetSuspended(false);
        };
        GlassShell.BeginAnimation(WidthProperty, width);
        GlassShell.BeginAnimation(HeightProperty, height);
    }
}
