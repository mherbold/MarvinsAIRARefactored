
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MarvinsAIRARefactored.Classes;

public sealed class HelpIconVisibilityConverter : IMultiValueConverter
{
	public object? Convert( object[] values, Type targetType, object? parameter, CultureInfo culture )
	{
		var topic = values.Length > 0 ? values[ 0 ] as string : null;

		return ( !string.IsNullOrWhiteSpace( topic ) ) ? Visibility.Visible : Visibility.Collapsed;
	}

	public object[] ConvertBack( object value, Type[] targetTypes, object? parameter, CultureInfo culture ) => throw new NotSupportedException();
}
