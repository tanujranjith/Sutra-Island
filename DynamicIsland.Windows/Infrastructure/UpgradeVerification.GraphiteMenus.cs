using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Windows.Services;
using DynamicIsland.Windows.ViewModels;
using DynamicIsland.Windows.Views;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace DynamicIsland.Windows.Infrastructure;

internal static partial class UpgradeVerification
{
    private sealed class DerivedMenuFixture : ContextMenu { }
    private static async Task VerifyGraphiteMenusAsync(IslandViewModel vm, IslandWindow window, Action<bool,string> check, string output, bool assistant)
    {
        async Task Layout() => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
            var drawing=new DrawingVisual(); using(var context=drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));
            bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        var deck=(GraphiteIslandView)window.FindName("GraphiteDeck");
        var launcher=(Button)deck.FindName("GraphiteAssistantButton");
        check(launcher.Content is Border { Background:ImageBrush { ImageSource:BitmapSource { PixelWidth:256 } } },"Assistant launcher uses the supplied mascot image rather than a font glyph");
        var button=assistant ? (Button)window.FindName("QMenuButton") : VisualDescendants(deck).OfType<Button>().Single(item=>Equals(item.ToolTip,"More island controls"));
        var menu=button.ContextMenu!; var keep=vm.KeepExpanded;
        if(!assistant) vm.KeepExpanded=false;
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(); menu.ApplyTemplate();
            check(menu.IsOpen && menu.Template.FindName("GraphiteMenuChrome",menu) is Border { CornerRadius.TopLeft:13, Background:SolidColorBrush { Color.R:29, Color.G:31, Color.B:37 } },
                assistant?"Assistant options uses the graphite menu surface":"Island options uses the shared graphite menu surface");
            if(!assistant)
            {
                var timer=(DispatcherTimer)typeof(IslandWindow).GetField("_collapseTimer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                var interval=timer.Interval; timer.Interval=TimeSpan.FromMilliseconds(100);timer.Start();await Task.Delay(250);await Layout();timer.Stop();timer.Interval=interval;
                check(vm.IsExpanded && menu.IsOpen && vm.InteractionProtected,"An unpinned island stays expanded while its options menu is open");
            }
            else
            {
                var screen=menu.Items.OfType<MenuItem>().First();var initial=vm.QUseScreenNext;
                var peer=new MenuItemAutomationPeer(screen);var toggle=(IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!;toggle.Toggle();await Layout();
                check(vm.QUseScreenNext != initial,"Graphite checkable menu item still invokes its bound screen-access toggle");
                menu.IsOpen=true;screen.IsChecked=initial; await Layout();
                Capture(menu,"graphite-assistant-options");
                screen.Focus();screen.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(menu)!,Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent });await Layout();
                check(!menu.IsOpen,"Escape dismisses the graphite assistant menu");
            }
            if(!assistant) Capture(menu,"graphite-island-options");
        }
        finally {menu.IsOpen=false;vm.KeepExpanded=keep;}
        if(assistant)
        {
            var submenu=new DerivedMenuFixture { PlacementTarget=button };var parent=new MenuItem {Header="More controls"};var leaf=new MenuItem {Header="Checked action",IsCheckable=true,IsChecked=true};parent.Items.Add(leaf);submenu.Items.Add(parent);
            try {submenu.IsOpen=true;await Layout();parent.IsSubmenuOpen=true;await Layout();check(submenu.Template.FindName("GraphiteMenuChrome",submenu) is Border,$"Derived context menu uses graphite chrome (open={submenu.IsOpen}, loaded={submenu.IsLoaded}, style={submenu.Style?.TargetType.Name})"); check(parent.Template.FindName("PART_Popup",parent) is Popup {IsOpen:true} && leaf.IsVisible,$"Graphite submenu retains its popup and checked item (parentOpen={parent.IsSubmenuOpen}, leafVisible={leaf.IsVisible})");}
            finally{submenu.IsOpen=false;}
            using var tray=new System.Windows.Forms.ContextMenuStrip { Renderer=new GraphiteTrayMenuRenderer(),BackColor=GraphiteTrayMenuRenderer.Surface,ForeColor=GraphiteTrayMenuRenderer.Text,ShowImageMargin=false,ShowCheckMargin=true,Padding=new System.Windows.Forms.Padding(5) };
            tray.Items.Add("Sutra Assistant");tray.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Focus mode") {Checked=true});tray.Items.Add(new System.Windows.Forms.ToolStripSeparator());tray.Items.Add("Settings");
            tray.Size=tray.GetPreferredSize(System.Drawing.Size.Empty);using var bitmap=new System.Drawing.Bitmap(tray.Width,tray.Height);tray.DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,tray.Size));bitmap.Save(Path.Combine(output,"graphite-tray-menu.png"));
            check(tray.Renderer is GraphiteTrayMenuRenderer,"System tray uses the graphite renderer rather than the Windows default renderer");
        }
    }
}
