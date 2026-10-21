using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Shutdown_Scheduler.Services;

// Reads CalmGlass theme resources from code, for chrome and elements that are built in code (title bar, dialogs).
public static class ThemeResources
{
    public static Color ResolveColor(string resourceKey, Color fallback)
        => TryResolveBrush(Application.Current.Resources, resourceKey, out var brush) ? brush.Color : fallback;

    public static Brush ResolveBrush(string resourceKey, Brush fallback)
        => TryResolveBrush(Application.Current.Resources, resourceKey, out var brush) ? brush : fallback;

    public static void ApplyDialogStyle(ContentDialog dialog)
    {
        dialog.Background = ResolveBrush("PopupBackgroundBrush", new SolidColorBrush(Color.FromArgb(255, 24, 25, 28)));
        dialog.BorderBrush = ResolveBrush("PopupBorderBrush", new SolidColorBrush(Color.FromArgb(255, 53, 57, 64)));
        dialog.Foreground = ResolveBrush("ControlForegroundBrush", new SolidColorBrush(Colors.White));
        dialog.BorderThickness = new Thickness(1);

        // A DefaultButton is restyled with AccentButtonStyle (system blue), whose colors can't be overridden
        // from app resources. Use the palette styles instead; Esc still closes and Enter activates the focused button.
        var resources = Application.Current.Resources;
        dialog.DefaultButton = ContentDialogButton.None;
        dialog.PrimaryButtonStyle = (Style)resources["GlassPrimaryButtonStyle"];
        dialog.SecondaryButtonStyle = (Style)resources["GlassSecondaryButtonStyle"];
        dialog.CloseButtonStyle = (Style)resources["GlassSecondaryButtonStyle"];
    }

    private static bool TryResolveBrush(ResourceDictionary dictionary, string resourceKey, out SolidColorBrush brush)
    {
        if (dictionary.TryGetValue(resourceKey, out var resource) &&
            resource is SolidColorBrush directBrush)
        {
            brush = directBrush;
            return true;
        }

        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (TryResolveBrush(mergedDictionary, resourceKey, out brush))
            {
                return true;
            }
        }

        brush = null!;
        return false;
    }
}
