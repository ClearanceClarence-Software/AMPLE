using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ample.App.ViewModels;

/// <summary>Turns a theme resource key such as "OkTextBrush" into the brush of the current theme.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    /// <summary>The shared instance for XAML.</summary>
    public static BrushKeyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is not { } application)
        {
            return null;
        }

        return application.TryGetResource(key, application.ActualThemeVariant, out object? resource)
            ? resource as IBrush
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
