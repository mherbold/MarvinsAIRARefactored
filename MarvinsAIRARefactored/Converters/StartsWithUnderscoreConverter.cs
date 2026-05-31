
using System.Windows.Data;
using System.Globalization;

namespace MarvinsAIRARefactored.Converters;

[ValueConversion( typeof( string ), typeof( bool ) )]
public class StartsWithUnderscoreConverter : IValueConverter
{
	public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
	{
		if ( value is string str && !string.IsNullOrEmpty( str ) )
		{
			return str.StartsWith( '_' );
		}

		return false;
	}

	public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
	{
		throw new NotImplementedException( "ConvertBack is not supported for StartsWithUnderscoreConverter" );
	}
}
