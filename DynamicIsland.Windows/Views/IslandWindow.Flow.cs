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

    private void ShowFlow(FlowStatus status)
    {
        var reduced = (DataContext as IslandViewModel)?.IsReducedMotion ?? false;
        FlowDots.ReducedMotion = reduced;
        FlowOverlay.ToolTip = string.IsNullOrEmpty(status.Detail) ? null : status.Detail;
        AutomationPropertiesHelper.SetName(FlowOverlay, status.State switch
        {
            FlowState.Listening => "Sutra Flow listening",
            FlowState.Polishing => "Sutra Flow polishing",
            FlowState.Pasted => $"Sutra Flow pasted {status.Words} words",
            FlowState.Error => "Sutra Flow: " + status.Detail,
            _ => "Sutra Flow",
        });

        if (status.State == FlowState.Idle)
        {
            if (FlowOverlay.Visibility != Visibility.Visible) { FlowDots.SetState(FlowState.Idle); return; }
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(reduced ? 1 : 280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (_, _) =>
            {
                if (_flow?.Status.State != FlowState.Idle) return;
                FlowOverlay.Visibility = Visibility.Collapsed;
                FlowDots.SetState(FlowState.Idle);
            };
            FlowOverlay.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        FlowDots.SetState(status.State);
        if (FlowOverlay.Visibility != Visibility.Visible || FlowOverlay.Opacity < 1)
        {
            FlowOverlay.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(reduced ? 1 : 200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            FlowOverlay.BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    private static class AutomationPropertiesHelper
    {
        public static void SetName(DependencyObject element, string name) =>
            System.Windows.Automation.AutomationProperties.SetName(element, name);
    }
}
