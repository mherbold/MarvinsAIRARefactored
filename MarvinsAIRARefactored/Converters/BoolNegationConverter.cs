
using System.Globalization;
using System.Windows.Data;

namespace MarvinsAIRARefactored.Converters;

[ValueConversion( typeof( bool ), typeof( bool ) )]
public class BoolNegationConverter : IValueConverter
{
	public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		=> value is bool b && !b;

	public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		=> value is bool b && !b;
}

/// <summary>
/// Multi-binding converter: returns true (Disabled) when any of the bound bool values is false.
/// </summary>
public class BoolAllTrueNegationConverter : IMultiValueConverter
{
	public object Convert( object[] values, Type targetType, object parameter, CultureInfo culture )
		=> values.Any( v => v is not true );

	public object[] ConvertBack( object value, Type[] targetTypes, object parameter, CultureInfo culture )
		=> throw new NotSupportedException();
}
