using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.Services.Flow;
using Clipboard = System.Windows.Clipboard;
using TextBox = System.Windows.Controls.TextBox;

namespace DynamicIsland.Windows.Infrastructure;

// Temporary isolated-runner probe. It never runs in normal app startup.
internal static class FlowPasteVerification
{
    public static async Task RunAsync()
    {
        var first = new TextBox { Margin = new Thickness(8) };
        var second = new TextBox { Margin = new Thickness(8) };
        var stack = new StackPanel(); stack.Children.Add(first); stack.Children.Add(second);
        var window = new Window { Title = "Sutra isolated paste probe", Width = 500, Height = 200, Content = stack, Topmost = true, ShowInTaskbar = false };
        try
        {
            window.Show(); window.Activate(); first.Focus(); Keyboard.Focus(first);
            await Task.Delay(500);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!first.IsKeyboardFocused) throw new InvalidOperationException("The isolated CI desktop could not focus the test field.");
            Clipboard.SetText("Sutra original clipboard fixture");
            var injector = new FlowTextInjector(new LoggingService(), window.Dispatcher);
            var target = injector.CaptureTarget() ?? throw new InvalidOperationException("No focused target was available in the isolated CI desktop.");
            injector.PinTarget(target);
            var result = await injector.PasteAsync("Sutra pasted fixture", target, CancellationToken.None);
            var coldOutcome = result.Outcome;
            if (result.Outcome == FlowPasteOutcome.TargetChanged)
            {
                if (first.Text.Length != 0 || Clipboard.GetText() != "Sutra original clipboard fixture")
                    throw new InvalidOperationException("Cold unverified target changed the test field or clipboard.");
                // A missing first accessibility ID must fail closed. Exercise a fresh,
                // warmed attempt rather than treating a later ID as the original target.
                target = injector.CaptureTarget() ?? throw new InvalidOperationException("No target after accessibility warm-up.");
                injector.PinTarget(target);
                result = await injector.PasteAsync("Sutra pasted fixture", target, CancellationToken.None);
            }
            if (result.Outcome != FlowPasteOutcome.Pasted)
            {
                var current = injector.CaptureTarget();
                var pressId = target.ElementAtPress is null ? "null" : await target.ElementAtPress ?? "null";
                var releaseId = target.ElementAtRelease is null ? "null" : await target.ElementAtRelease ?? "null";
                var nowId = current?.ElementAtPress is null ? "null" : await current.ElementAtPress ?? "null";
                throw new InvalidOperationException($"Paste outcome={result.Outcome}; press={target.AtPress}; release={target.AtRelease}; now={current?.AtPress}; element IDs press={pressId}, release={releaseId}, now={nowId}; window active={window.IsActive}; first focused={first.IsKeyboardFocused}");
            }
            if (first.Text != "Sutra pasted fixture") throw new InvalidOperationException("Pasted text did not reach the original test field.");
            if (Clipboard.GetText() != "Sutra original clipboard fixture") throw new InvalidOperationException("The original clipboard text was not restored.");

            first.Focus(); Keyboard.Focus(first); await Task.Delay(150);
            var changed = injector.CaptureTarget() ?? throw new InvalidOperationException("Could not capture focus for the changed-target test.");
            injector.PinTarget(changed); second.Focus(); Keyboard.Focus(second); await Task.Delay(150);
            var rejected = await injector.PasteAsync("must not paste", changed, CancellationToken.None);
            if (rejected.Outcome != FlowPasteOutcome.TargetChanged || second.Text.Length != 0)
                throw new InvalidOperationException("Changed-target paste was not rejected.");
            Directory.CreateDirectory(AppDataPaths.Root);
            File.WriteAllText(Path.Combine(AppDataPaths.Root, "flow-paste-probe.txt"), $"PASS cold outcome={coldOutcome}; warmed paste, clipboard text restore, and changed-target rejection");
        }
        finally { window.Close(); }
    }
}
