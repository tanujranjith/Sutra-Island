using System.Windows;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace DynamicIsland.Windows.Infrastructure;

internal static class GraphiteMenuSupport
{
    private static bool _initialized;
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized=true;
        // WPF's built-in text-edit menus use private derived control types. Implicit type
        // styles do not reach those types, so apply the shared styles before first rendering.
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler(Menu_Opened),handledEventsToo:true);
    }
    private static void Menu_Opened(object sender,RoutedEventArgs e)
    {
        if(sender is not ContextMenu menu) return;
        Apply(menu,typeof(ContextMenu));
        ApplyItems(menu.Items);
    }
    private static void Apply(FrameworkElement element,Type type)
    {
        if(System.Windows.Application.Current.TryFindResource(type) is Style style && !ReferenceEquals(element.Style,style))
            element.SetCurrentValue(FrameworkElement.StyleProperty,style);
    }
    private static void ApplyItems(System.Windows.Controls.ItemCollection items)
    {
        foreach(var item in items)
        {
            if(item is MenuItem menuItem) { Apply(menuItem,typeof(MenuItem));ApplyItems(menuItem.Items); }
            else if(item is Separator separator) Apply(separator,typeof(Separator));
        }
    }
}
