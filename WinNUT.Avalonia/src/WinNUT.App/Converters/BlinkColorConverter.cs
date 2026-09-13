using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WinNUT.App.Converters;

/// <summary>Alternates red/black, matching Shutdown_Gui.vb's flashing countdown text.</summary>
public sealed class BlinkColorConverter : IValueConverter
{
    public static readonly BlinkColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Colors.Red : Colors.Black;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
