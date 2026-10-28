using System.Windows;
using System.Windows.Input;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private static async Task VerifyComposerKeysAsync(IslandViewModel vm, IslandWindow window, Action<bool,string> check)
    {
        await vm.StartChatAsync(newConversation: true);
        window.ApplySettings(); window.UpdateLayout();
        var box = (TextBox)window.FindName("QPromptBox");
        KeyEventArgs Enter() => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.Text = "Keyboard submission fixture";
        var send = Enter(); box.RaiseEvent(send);
        check(send.Handled && box.Text.Length == 0, "Enter submits through the preview route before the multiline editor can insert a newline");
        box.Text = "Draft kept while responding";
        var busy = Enter(); box.RaiseEvent(busy);
        check(busy.Handled && box.Text == "Draft kept while responding", "Enter while responding preserves the draft and does not insert a newline or send twice");
        await WaitForSutraAsync(() => !vm.QCanStop);
        check(vm.QPromptText == "Keyboard submission fixture", "Keyboard submission reaches the assistant with the complete prompt");
        check(!IslandWindow.IsAssistantSubmitKey(Key.Enter, ModifierKeys.Shift)
            && !IslandWindow.IsAssistantSubmitKey(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift), "Shift+Enter selects newline behavior even when another modifier is held");
        box.Text = "first replace last"; box.Select(6,7);
        await window.HandleAssistantEnterAsync(ModifierKeys.Shift);
        check(box.Text == "first " + Environment.NewLine + " last" && box.CaretIndex == 6 + Environment.NewLine.Length && box.SelectionLength == 0,
            "Shift+Enter inserts one real newline at the selection and leaves the caret after it");
        var draft = box.Text;
        await window.HandleAssistantEnterAsync(ModifierKeys.None, isRepeat:true);
        check(box.Text == draft, "Held Enter repeats cannot resubmit a draft");
        box.Clear(); vm.ClearQ(); window.ApplySettings();
    }
}
