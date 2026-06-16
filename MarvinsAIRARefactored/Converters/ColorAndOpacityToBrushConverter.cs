using System.Globalization;
using System.Windows.Data;

using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MarvinsAIRARefactored.Converters;

// Combines a background color (hex string like "#RRGGBB") and an opacity (0..1 float) into a SolidColorBrush.
// The opacity is applied as the brush alpha channel only, so overlay text/borders painted with other brushes stay fully opaque.
public class ColorAndOpacityToBrushConverter : IMultiValueConverter
{
	public object Convert( object[] values, Type targetType, object parameter, CultureInfo culture )
	{
		var color = Colors.Black;

		if ( ( values.Length > 0 ) && ( values[ 0 ] is string hexColor ) && !string.IsNullOrWhiteSpace( hexColor ) )
		{
			try
			{
				color = (Color) ColorConverter.ConvertFromString( hexColor );
			}
			catch
			{
				color = Colors.Black;
			}
		}

		var opacity = 1f;

		if ( ( values.Length > 1 ) && ( values[ 1 ] is float opacityValue ) )
		{
			opacity = Math.Clamp( opacityValue, 0f, 1f );
		}

		color.A = (byte) Math.Round( opacity * 255f );

		var brush = new SolidColorBrush( color );

		brush.Freeze();

		return brush;
	}

	public object[] ConvertBack( object value, Type[] targetTypes, object parameter, CultureInfo culture )
	{
		throw new NotSupportedException();
	}
}
