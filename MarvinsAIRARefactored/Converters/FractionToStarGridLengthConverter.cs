using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MarvinsAIRARefactored.Converters;

// Turns a 0..1 split fraction into a star GridLength so two grid columns can share a settings-driven ratio;
// pass ConverterParameter=Complement to get the other side's share (1 - fraction).
[ValueConversion( typeof( double ), typeof( GridLength ) )]
public class FractionToStarGridLengthConverter : IValueConverter
{
	public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
	{
		var fraction = (double) value;

		if ( parameter is string text && ( text == "Complement" ) )
		{
			fraction = 1.0 - fraction;
		}

		return new GridLength( fraction, GridUnitType.Star );
	}

	public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
	{
		return DependencyProperty.UnsetValue;
	}
}
