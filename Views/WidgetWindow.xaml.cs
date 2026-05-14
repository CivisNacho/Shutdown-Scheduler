using System.Windows;
using System.Windows.Controls;

namespace Shutdown_Scheduler.Views;

public partial class WidgetWindow : Window
{
    public WidgetWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    public void SetText(string text) => WidgetText.Text = text;

    private void ApplyResponsiveLayout()
    {
        var compact = ActualHeight <= 84 || ActualWidth <= 240;

        if (compact)
        {
            OuterShell.Padding = new Thickness(6);
            InnerCard.Padding = new Thickness(8, 5, 8, 5);
            HeaderText.Visibility = Visibility.Collapsed;
            WidgetText.FontSize = 14;
            WidgetText.TextWrapping = TextWrapping.NoWrap;
            WidgetText.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        else
        {
            OuterShell.Padding = new Thickness(10);
            InnerCard.Padding = new Thickness(12, 8, 12, 8);
            HeaderText.Visibility = Visibility.Visible;
            WidgetText.FontSize = 18;
            WidgetText.TextWrapping = TextWrapping.NoWrap;
            WidgetText.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }
}
