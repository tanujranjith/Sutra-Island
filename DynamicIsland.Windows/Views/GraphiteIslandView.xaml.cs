using System.Windows;
using System.Windows.Input;
using DynamicIsland.Windows.ViewModels;

namespace DynamicIsland.Windows.Views;

public partial class GraphiteIslandView : System.Windows.Controls.UserControl
{
    public event RoutedEventHandler? SettingsRequested, TimerRequested, AssistantRequested, RecenterRequested, OutputRequested, MenuOpened, MenuClosed;
    public GraphiteIslandView() => InitializeComponent();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(sender, e);
    private void Screenshot_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as IslandWindow)?.RequestScreenshot();
    private void Timer_Click(object sender, RoutedEventArgs e) => TimerRequested?.Invoke(sender, e);
    private void Assistant_Click(object sender, RoutedEventArgs e) => AssistantRequested?.Invoke(sender, e);
    private void Recenter_Click(object sender, RoutedEventArgs e) => RecenterRequested?.Invoke(sender, e);
    private void Output_Click(object sender, RoutedEventArgs e) => OutputRequested?.Invoke(sender, e);
    private void Artwork_Click(object sender, MouseButtonEventArgs e) { if (DataContext is IslandViewModel vm) vm.OpenMediaAppCommand.Execute(null); }
    private void MoreMenu_Opened(object sender, RoutedEventArgs e) => MenuOpened?.Invoke(sender,e);
    private void MoreMenu_Closed(object sender, RoutedEventArgs e) => MenuClosed?.Invoke(sender,e);
    private void More_Click(object sender, RoutedEventArgs e)
    { if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button) { menu.DataContext = DataContext; menu.PlacementTarget = button; menu.IsOpen = true; } }
    private void Seek_Click(object sender, MouseButtonEventArgs e)
    { if (sender is FrameworkElement { ActualWidth: > 0 } track && DataContext is IslandViewModel { CanSeek: true } vm) vm.SeekCommand.Execute(Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1)); }
}
