
using System.Windows;
using System.Windows.Data;
using System.Globalization;

namespace MarvinsAIRARefactored.Converters;

[ValueConversion( typeof( bool ), typeof( Visibility ) )]
public class BooleanToVisibilityCollapsedConverter : IValueConverter
{
	public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
	{
		if ( value is bool b && b )
		{
			return Visibility.Visible;
		}

		return Visibility.Collapsed;
	}

	public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
	{
		if ( value is Visibility v )
		{
			return v == Visibility.Visible;
		}

		return DependencyProperty.UnsetValue;
	}
}

[ValueConversion( typeof( bool ), typeof( Visibility ) )]
public class BooleanToVisibilityHiddenConverter : IValueConverter
{
	public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
	{
		if ( value is bool b && b )
		{
			return Visibility.Visible;
		}

		return Visibility.Hidden;
	}

	public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
	{
		if ( value is Visibility v )
		{
			return v == Visibility.Visible;
		}

		return DependencyProperty.UnsetValue;
	}
}
