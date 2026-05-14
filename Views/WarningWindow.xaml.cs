using System.Windows;
using Shutdown_Scheduler.Models;
using Shutdown_Scheduler.Services;

namespace Shutdown_Scheduler.Views;

public partial class WarningWindow : Window
{
    public WarningWindow(ActiveSchedule schedule, LocalizationService loc)
    {
        InitializeComponent();
        DataContext = new
        {
            TitleText = loc.T("WarningTitle"),
            MessageText = string.Format(loc.T("WarningText"), loc.ActionLabel(schedule.Request.Action), FormatRemaining(schedule.TargetTime - DateTime.Now)),
            PostponeText = loc.T("Postpone"),
            CancelText = loc.T("CancelButton")
        };
    }

    public bool ShouldPostpone { get; private set; }
    public bool ShouldCancel { get; private set; }

    private void Postpone_Click(object sender, RoutedEventArgs e)
    {
        ShouldPostpone = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ShouldCancel = true;
        Close();
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}
