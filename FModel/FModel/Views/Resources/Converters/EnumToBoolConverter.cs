using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace FModel.Views.Resources.Converters;

/// <summary>
/// Converts an enum value to bool for RadioButton IsChecked bindings.
/// ConverterParameter = the enum member name to match.
/// </summary>
[ValueConversion(typeof(Enum), typeof(bool))]
public class EnumToBoolConverter : MarkupExtension, IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return Enum.Parse(targetType, parameter!.ToString()!);
        return Binding.DoNothing;
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;
}
