using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Models;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
namespace DynamicIsland.Windows.Views;
public partial class CommandToolsView : UserControl
{
    public CommandToolsView() => InitializeComponent();
    private CommandPaletteViewModel Vm => (CommandPaletteViewModel)DataContext;
    public bool HasOpenDropdown => Descendants(this).OfType<ComboBox>().Any(c => c.IsDropDownOpen);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    { for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); yield return child; foreach(var next in Descendants(child)) yield return next; } }
    public void FocusPanel()
    {
        Control control = Vm.IsStopwatchPanel ? StopwatchPrimary : Vm.IsClipboardPanel ? ClipboardSearchBox : Vm.IsNotificationsPanel ? NotificationSearchBox : Vm.IsConvertPanel ? ConversionValueBox : Vm.IsAudioOutputPanel ? OutputSearchBox : FocusMinutesBox;
        if (!control.IsVisible) return;
        control.Focus(); Keyboard.Focus(control); if(control is TextBox text) text.SelectAll();
    }
    private void Back_Click(object sender,RoutedEventArgs e)=>Vm.ClosePanel();
    private void Close_Click(object sender,RoutedEventArgs e)=>Vm.RequestPaletteClose();
    private void FocusPreset_Click(object sender,RoutedEventArgs e) { if(sender is FrameworkElement {Tag:string value}) Vm.FocusMinutes=value; }
    private void StartFocus_Click(object sender,RoutedEventArgs e)=>Vm.StartPanelFocus();
    private void EndFocus_Click(object sender,RoutedEventArgs e)=>Vm.Island.EndFocusSession();
    private async void RefreshClips_Click(object sender,RoutedEventArgs e)=>await Vm.RefreshClipsAsync();
    private void ClipboardSettings_Click(object sender,RoutedEventArgs e)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:clipboard"){UseShellExecute=true});
    private void ClipboardList_Click(object sender,MouseButtonEventArgs e) { if(e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ClipboardList,source) is ListBoxItem {DataContext:PaletteClip clip}) Vm.CopyClip(clip); }
    private async void Mirroring_Click(object sender,RoutedEventArgs e)=>await Vm.ToggleMirroringAsync();
    private void DismissBanner_Click(object sender,RoutedEventArgs e)=>Vm.DismissPanelBanner();
    private void ClearHistory_Click(object sender,RoutedEventArgs e)=>Vm.ClearPanelHistory();
    private void RemoveNotification_Click(object sender,RoutedEventArgs e) { if(sender is FrameworkElement {DataContext:NotificationHistoryItem item}) Vm.RemoveHistoryItem(item); e.Handled=true; }
    private void SwapConversion_Click(object sender,RoutedEventArgs e)=>Vm.SwapConversion();
    private void CopyConversion_Click(object sender,RoutedEventArgs e)=>Vm.CopyConversion();
    private async void RefreshOutputs_Click(object sender,RoutedEventArgs e)=>await Vm.RefreshOutputsAsync();
    private async void OutputList_Click(object sender,MouseButtonEventArgs e) { if(e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(OutputList,source) is ListBoxItem {DataContext:AudioOutputDevice device}) await Vm.SelectOutputAsync(device); }
    private void Tools_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key is not (Key.Up or Key.Down)) return;
        ListBox? list = ClipboardSearchBox.IsKeyboardFocusWithin ? ClipboardList : OutputSearchBox.IsKeyboardFocusWithin ? OutputList : null;
        if(list is null || list.Items.Count==0) return;
        list.SelectedIndex=Math.Clamp(list.SelectedIndex+(e.Key==Key.Down?1:-1),0,list.Items.Count-1); list.ScrollIntoView(list.SelectedItem); e.Handled=true;
    }
}
