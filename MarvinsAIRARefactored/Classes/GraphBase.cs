
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Image = System.Windows.Controls.Image;

namespace MarvinsAIRARefactored.Classes;

public class GraphBase
{
	private const int GutterSize = 10;

	public int BitmapWidth { get; private set; }
	public int BitmapHeight { get; private set; }

	/// <summary>When false, the first and last grid lines (the ±100% clipping lines) are not drawn. The FFB graph
	/// preview turns them off unless the Output module is selected — clipping only means something there.</summary>
	public bool DrawClippingLines { get; set; } = true;

	private int _bitmapStride;
	private int _bitmapHeightMinusOne;

	private WriteableBitmap? _writeableBitmap = null;

	private int _x = 0;

	private uint[,]? _colorArray = null;
	private float[,]? _colorMixArray = null;

	private readonly uint[] _gridLineColorArray = [
		0xFF884444,
		0xFF444444,
		0xFF666688,
		0xFF444444,
		0xFF000000,
		0xFF444444,
		0xFF666688,
		0xFF444444,
		0xFF884444
	];

	private static readonly uint[] _gridLineColorsDark = [
		0xFF884444,
		0xFF444444,
		0xFF666688,
		0xFF444444,
		0xFF000000,
		0xFF444444,
		0xFF666688,
		0xFF444444,
		0xFF884444
	];

	private static readonly uint[] _gridLineColorsLight = [
		0xFFAA4444,
		0xFFAAAAAA,
		0xFF7777AA,
		0xFFAAAAAA,
		0xFF333333,
		0xFFAAAAAA,
		0xFF7777AA,
		0xFFAAAAAA,
		0xFFAA4444
	];

	public void UpdateThemeColors( bool lightTheme )
	{
		var source = lightTheme ? _gridLineColorsLight : _gridLineColorsDark;

		Array.Copy( source, _gridLineColorArray, source.Length );
	}

	private uint _topGutterBackgroundColor = 0;
	private uint _topGutterForegroundColor = 0;

	private uint _bottomGutterBackgroundColor = 0;
	private uint _bottomGutterForegroundColor = 0;

	public void Initialize( Image image )
	{
		BitmapWidth = (int) image.Width;
		BitmapHeight = (int) image.Height;

		_bitmapStride = BitmapWidth * 4;
		_bitmapHeightMinusOne = BitmapHeight - 1;

		_writeableBitmap = new( BitmapWidth, BitmapHeight, 96f, 96f, PixelFormats.Bgra32, null );

		_colorArray = new uint[ BitmapHeight, BitmapWidth ];
		_colorMixArray = new float[ BitmapHeight, 4 ];

		image.Source = _writeableBitmap;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Reset()
	{
		_x = 0;
	}

	// Map a -1..1 value to its pixel row (top = +1, bottom = -1), inside the gutters.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private int ValueToY( float value )
	{
		// clamp y value to -1..1 range, where -1 is the bottom of the graph, 0 is the middle and 1 is the top
		var y = Math.Clamp( value, -1f, 1f );

		// invert y value and shift it to 0..1 range, where 0 is the top of the graph and 1 is the bottom
		y = y * -0.5f + 0.5f;

		return (int) Math.Round( y * ( BitmapHeight - GutterSize * 2 ) ) + GutterSize;
	}

	/// <summary>Render a value as a solid fill from the zero line to the value.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void UpdateSolidFill( float value, float r, float g, float b )
	{
		if ( _colorMixArray != null )
		{
			// calculate the y position of the value on the graph
			var iY1 = _bitmapHeightMinusOne / 2;
			var iY2 = ValueToY( value );

			var delta = iY2 - iY1;

			var sign = Math.Sign( delta );
			var range = Math.Abs( delta );

			var iY = iY1;

			for ( var i = 1; i <= range; i++ )
			{
				_colorMixArray[ iY, 0 ] = 1f;
				_colorMixArray[ iY, 1 ] += r;
				_colorMixArray[ iY, 2 ] += g;
				_colorMixArray[ iY, 3 ] += b;

				iY += sign;
			}
		}
	}

	/// <summary>Render a value as a connected line — this column is painted only from the previous column's value
	/// to this one, so consecutive calls trace a continuous 1-px-wide waveform instead of a fill from zero. The
	/// space between the centerline and the value is filled with solid black first, occluding any traces already
	/// drawn there this column (so the line reads as a silhouette over the solid-fill traces).</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void UpdateLine( float previousValue, float value, float r, float g, float b )
	{
		if ( _colorMixArray != null )
		{
			// fill from the centerline to the value with solid black (assignment, not additive — this erases
			// whatever was already mixed into those rows, e.g. the solid-fill traces drawn before this call)
			var centerY = _bitmapHeightMinusOne / 2;
			var valueY = ValueToY( value );

			var sign = Math.Sign( valueY - centerY );
			var range = Math.Abs( valueY - centerY );

			var fillY = centerY;

			for ( var i = 1; i <= range; i++ )
			{
				_colorMixArray[ fillY, 0 ] = 1f;
				_colorMixArray[ fillY, 1 ] = 0f;
				_colorMixArray[ fillY, 2 ] = 0f;
				_colorMixArray[ fillY, 3 ] = 0f;

				fillY += sign;
			}

			// draw the vertical segment connecting the two values, inclusive, so steep slopes stay connected
			var previousY = ValueToY( previousValue );

			var minY = Math.Min( previousY, valueY );
			var maxY = Math.Max( previousY, valueY );

			for ( var iY = minY; iY <= maxY; iY++ )
			{
				_colorMixArray[ iY, 0 ] = 1f;
				_colorMixArray[ iY, 1 ] += r;
				_colorMixArray[ iY, 2 ] += g;
				_colorMixArray[ iY, 3 ] += b;
			}
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetGutterColors( uint topForeground, uint topBackground, uint bottomForeground, uint bottomBackground )
	{
		_topGutterForegroundColor = topForeground;
		_topGutterBackgroundColor = topBackground;

		_bottomGutterForegroundColor = bottomForeground;
		_bottomGutterBackgroundColor = bottomBackground;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void FinishUpdates()
	{
		if ( ( _colorArray != null ) && ( _colorMixArray != null ) )
		{
			var oddEven = ( _x % 20 ) < 10;

			var topGutterColor = oddEven ? _topGutterForegroundColor : _topGutterBackgroundColor;

			for ( var y = 1; y < GutterSize - 1; y++ )
			{
				_colorArray[ y, _x ] = topGutterColor;
			}

			for ( var y = GutterSize; y < BitmapHeight - GutterSize; y++ )
			{
				var a = (uint) ( MathF.Min( 1f, _colorMixArray[ y, 0 ] ) * 255f );
				var r = (uint) ( MathF.Min( 1f, _colorMixArray[ y, 1 ] ) * 255f );
				var g = (uint) ( MathF.Min( 1f, _colorMixArray[ y, 2 ] ) * 255f );
				var b = (uint) ( MathF.Min( 1f, _colorMixArray[ y, 3 ] ) * 255f );

				_colorArray[ y, _x ] = ( a << 24 ) | ( r << 16 ) | ( g << 8 ) | b;
			}

			var bottomGutterColor = oddEven ? _bottomGutterForegroundColor : _bottomGutterBackgroundColor;

			for ( var y = BitmapHeight - GutterSize + 1; y < BitmapHeight - 1; y++ )
			{
				_colorArray[ y, _x ] = bottomGutterColor;
			}

			var gridSize = ( _bitmapHeightMinusOne - GutterSize * 2 ) / 8;

			for ( var i = 0; i <= 8; i++ )
			{
				if ( !DrawClippingLines && ( ( i == 0 ) || ( i == 8 ) ) )
				{
					continue;
				}

				var y = gridSize * i + GutterSize;

				if ( ( _colorArray[ y, _x ] == 0 ) || ( ( i & 3 ) == 0 ) )
				{
					_colorArray[ y, _x ] = _gridLineColorArray[ i ];
				}
			}

			_x = ( _x + 1 ) % BitmapWidth;

			Array.Clear( _colorMixArray );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void WritePixels()
	{
		if ( _writeableBitmap != null )
		{
			var x = _x;

			var leftX = x;
			var leftWidth = BitmapWidth - leftX;

			var rightX = 0;
			var rightWidth = x - rightX;

			if ( leftWidth > 0 )
			{
				var int32Rect = new Int32Rect( leftX, 0, leftWidth, BitmapHeight );

				_writeableBitmap.WritePixels( int32Rect, _colorArray, _bitmapStride, 0, 0 );
			}

			if ( rightWidth > 0 )
			{
				var int32Rect = new Int32Rect( rightX, 0, rightWidth, BitmapHeight );

				_writeableBitmap.WritePixels( int32Rect, _colorArray, _bitmapStride, leftWidth, 0 );
			}
		}
	}
}
