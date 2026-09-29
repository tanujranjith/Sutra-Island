namespace DynamicIsland.Windows.Views;

public partial class TimerListPanel : System.Windows.Controls.UserControl
{
    public TimerListPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
        UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        var narrow = ActualWidth > 0 && ActualWidth < 680;
        TimerLayout.ColumnDefinitions[0].Width = narrow
            ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star)
            : new System.Windows.GridLength(320);
        TimerLayout.ColumnDefinitions[1].Width = narrow
            ? new System.Windows.GridLength(0)
            : new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        System.Windows.Controls.Grid.SetColumn(TimerRingPanel, 0);
        System.Windows.Controls.Grid.SetRow(TimerRingPanel, 2);
        System.Windows.Controls.Grid.SetColumn(OptionsPanel, narrow ? 0 : 1);
        System.Windows.Controls.Grid.SetRow(OptionsPanel, narrow ? 3 : 2);
        TimerRingPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        OptionsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
    }
}
