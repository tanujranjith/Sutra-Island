using System.Windows;
using System.Windows.Media.Animation;
using DynamicIsland.Windows.Services.Flow;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

/// <summary>Sutra Flow presentation: the island keeps its size and its content morphs into dots.</summary>
public partial class IslandWindow
{
    private FlowController? _flow;

    public void AttachFlow(FlowController flow)
    {
        _flow = flow;
        FlowDots.Attach(flow);
        flow.StatusChanged += (_, status) => ShowFlow(status);
    }

    /// <summary>Voice mode for Sutra Assistant: the composer gives way to the dots while a voice turn runs.</summary>
    public void AttachAssistantVoiceDots(Func<float> level)
    {
        QVoiceDots.LevelSource = level;
        CompactVoice.LevelSource = level;
        if (DataContext is not IslandViewModel vm) return;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IslandViewModel.VoiceState) or null) Dispatcher.BeginInvoke(() => ShowAssistantVoice(vm.VoiceState));
        };
    }

    private void ShowAssistantVoice(DynamicIsland.Q.Core.AssistantVoiceState state)
    {
        var dots = state switch
        {
            DynamicIsland.Q.Core.AssistantVoiceState.Listening => FlowState.Listening,
            DynamicIsland.Q.Core.AssistantVoiceState.Thinking => FlowState.Polishing,
            DynamicIsland.Q.Core.AssistantVoiceState.Speaking => FlowState.Released,
            _ => FlowState.Idle,
        };
        QVoiceDots.ReducedMotion = (DataContext as IslandViewModel)?.IsReducedMotion ?? false;
        QVoiceDots.SetState(dots);
        var voice = dots != FlowState.Idle;
        QVoiceDots.Visibility = voice ? Visibility.Visible : Visibility.Collapsed;
        QComposer.Visibility = voice ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowFlow(FlowStatus status)
    {
        var reduced = (DataContext as IslandViewModel)?.IsReducedMotion ?? false;
        FlowDots.ReducedMotion = reduced;
        FlowOverlay.ToolTip = string.IsNullOrEmpty(status.Detail) ? null : status.Detail;
        FlowCreatorLabel.Visibility = _flow?.CreatorMode == true && status.State is FlowState.Listening or FlowState.Released or FlowState.Polishing
            ? Visibility.Visible : Visibility.Collapsed;
        AutomationPropertiesHelper.SetName(FlowOverlay, status.State switch
        {
            FlowState.Listening => "Sutra Flow listening",
            FlowState.Polishing => "Sutra Flow polishing",
            FlowState.Pasted => $"Sutra Flow pasted {status.Words} words",
            FlowState.Error => "Sutra Flow: " + status.Detail,
            _ => "Sutra Flow",
        });

        // Motion matches the island's own content transitions (see the AirPods and privacy intros):
        // content fades in over ~180 ms while it springs from 0.82 to 1 with a light BackEase,
        // and leaves with a short CubicEase-in fade.
        if (status.State == FlowState.Idle)
        {
            if (FlowOverlay.Visibility != Visibility.Visible) { FlowDots.SetState(FlowState.Idle); return; }
            var exit = TimeSpan.FromMilliseconds(reduced ? 1 : 220);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fadeOut = new DoubleAnimation(0, exit) { EasingFunction = easeIn };
            fadeOut.Completed += (_, _) =>
            {
                if (_flow?.Status.State != FlowState.Idle) return;
                FlowOverlay.Visibility = Visibility.Collapsed;
                FlowDots.SetState(FlowState.Idle);
            };
            FlowDotsScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, exit) { EasingFunction = easeIn });
            FlowDotsScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, exit) { EasingFunction = easeIn });
            FlowOverlay.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        FlowDots.SetState(status.State);
        if (FlowOverlay.Visibility != Visibility.Visible || FlowOverlay.Opacity < 1)
        {
            FlowOverlay.Visibility = Visibility.Visible;
            if (reduced)
            {
                FlowOverlay.BeginAnimation(OpacityProperty, null);
                FlowDotsScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                FlowDotsScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                FlowOverlay.Opacity = 1; FlowDotsScale.ScaleX = FlowDotsScale.ScaleY = 1;
                return;
            }
            var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 };
            FlowOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            FlowDotsScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = spring });
            FlowDotsScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = spring });
        }
    }

    private static class AutomationPropertiesHelper
    {
        public static void SetName(DependencyObject element, string name) =>
            System.Windows.Automation.AutomationProperties.SetName(element, name);
    }
}
