using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Shutdown_Scheduler.Models;

namespace Shutdown_Scheduler_WinUI.Converters;

// Turns a glyph into the Margin that centres its ink; a left/top margin of 2x shifts a centred icon by x.
public sealed class GlyphInkMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var offset = value is string glyph ? AppGlyphs.InkOffsetFor(glyph) * 2 : 0;
        return new Thickness(offset, offset, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
