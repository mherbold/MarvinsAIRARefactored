using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MarvinsAIRARefactored.Converters;

// Turns the AppUIScale factor into the logical thickness that renders as exactly one device pixel under the
// app's LayoutTransform (thickness = 1 / scale). Without this, a 1-logical-pixel hairline scaled below 100%
// becomes a sub-pixel band that can antialias into invisibility depending on its subpixel phase (e.g. the
// scroll offset). Pair with RenderOptions.EdgeMode="Aliased" so the single-pixel line stays crisp.
[ValueConversion( typeof( float ), typeof( double ) )]
public class InverseScaleThicknessConverter : IValueConverter
{
	public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
	{
		var scale = value switch
		{
			float floatValue => floatValue,
			double doubleValue => (float) doubleValue,
			_ => 1f
		};

		return 1.0 / Math.Clamp( scale, 0.25f, 4f );
	}

	public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
	{
		return DependencyProperty.UnsetValue;
	}
}
