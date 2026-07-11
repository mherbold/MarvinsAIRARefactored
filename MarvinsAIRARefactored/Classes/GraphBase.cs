
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Image = System.Windows.Controls.Image;

namespace MarvinsAIRARefactored.Classes;

public class GraphBase
{
	public int BitmapWidth { get; private set; }
	public int BitmapHeight { get; private set; }

	// Per-row background for one column: the grid line color on the grid rows, transparent (0) everywhere else.
	// FinishUpdates copies it under the plotted pixels so every row is written every frame — no stale pixels
	// survive from the previous wrap-around. Rebuilt lazily on theme / clipping-line changes.
	private uint[]? _columnTemplate = null;
	private bool _columnTemplateDirty = true;

	private int _bitmapStride;
	private int _bitmapHeightMinusOne;
	private int _centerY;      // the zero line's pixel row

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

		_columnTemplateDirty = true;
	}

	// Background for pixels not plotted this column (and not on a grid line): fully transparent normally, or a
	// solid flash color while a protection/clipping condition is active (see SetClearColor).
	private uint _clearColor = 0;

	public void Initialize( Image image )
	{
		BitmapWidth = (int) image.Width;
		BitmapHeight = (int) image.Height;

		_bitmapStride = BitmapWidth * 4;
		_bitmapHeightMinusOne = BitmapHeight - 1;
		_centerY = _bitmapHeightMinusOne / 2;

		_writeableBitmap = new( BitmapWidth, BitmapHeight, 96f, 96f, PixelFormats.Bgra32, null );

		_colorArray = new uint[ BitmapHeight, BitmapWidth ];
		_colorMixArray = new float[ BitmapHeight, 4 ];

		_columnTemplate = new uint[ BitmapHeight ];
		_columnTemplateDirty = true;

		image.Source = _writeableBitmap;
	}

	private void RebuildColumnTemplate()
	{
		Array.Clear( _columnTemplate! );

		// the grid lines span the full bitmap (zero on the center row); the first and last lines (the ±100%
		// rows) are never drawn — clipping is signaled by the clear-color background flash instead
		for ( var i = 1; i <= 7; i++ )
		{
			_columnTemplate![ (int) ( i * _bitmapHeightMinusOne / 8f + 0.5f ) ] = _gridLineColorArray[ i ];
		}

		_columnTemplateDirty = false;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Reset()
	{
		_x = 0;
	}

	// Map a -1..1 value to its pixel row (+1 = the very top row, -1 = the very bottom row). The +0.5f truncation
	// rounds half-up — a hair faster than MathF.Round and indistinguishable at pixel scale.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private int ValueToY( float value )
	{
		// clamp to -1..1, then flip and shift to 0..1 where 0 is the top of the graph and 1 is the bottom
		var y = Math.Clamp( value, -1f, 1f ) * -0.5f + 0.5f;

		return (int) ( y * _bitmapHeightMinusOne + 0.5f );
	}

	/// <summary>Render a value as a solid fill from the zero line to the value.</summary>
	public unsafe void UpdateSolidFill( float value, float r, float g, float b )
	{
		if ( _colorMixArray == null )
		{
			return;
		}

		var delta = ValueToY( value ) - _centerY;

		var sign = Math.Sign( delta );
		var range = Math.Abs( delta );

		fixed ( float* mixArray = _colorMixArray )
		{
			// walk from the centerline toward the value (one 4-float row per step, no bounds checks)
			var mix = mixArray + _centerY * 4;
			var step = sign * 4;

			for ( var i = 1; i <= range; i++ )
			{
				mix[ 0 ] = 1f;
				mix[ 1 ] += r;
				mix[ 2 ] += g;
				mix[ 3 ] += b;

				mix += step;
			}
		}
	}

	/// <summary>Render a value as a connected line — this column is painted only from the previous column's value
	/// to this one, so consecutive calls trace a continuous 1-px-wide waveform instead of a fill from zero. The
	/// space between the centerline and the value is filled with solid black first, occluding any traces already
	/// drawn there this column (so the line reads as a silhouette over the solid-fill traces).</summary>
	public unsafe void UpdateLine( float previousValue, float value, float r, float g, float b )
	{
		if ( _colorMixArray == null )
		{
			return;
		}

		var valueY = ValueToY( value );

		var sign = Math.Sign( valueY - _centerY );
		var range = Math.Abs( valueY - _centerY );

		fixed ( float* mixArray = _colorMixArray )
		{
			// fill from the centerline to the value with solid black (assignment, not additive — this erases
			// whatever was already mixed into those rows, e.g. the solid-fill traces drawn before this call)
			var mix = mixArray + _centerY * 4;
			var step = sign * 4;

			for ( var i = 1; i <= range; i++ )
			{
				mix[ 0 ] = 1f;
				mix[ 1 ] = 0f;
				mix[ 2 ] = 0f;
				mix[ 3 ] = 0f;

				mix += step;
			}

			// draw the vertical segment connecting the two values, inclusive, so steep slopes stay connected
			var previousY = ValueToY( previousValue );

			var minY = Math.Min( previousY, valueY );
			var maxY = Math.Max( previousY, valueY );

			mix = mixArray + minY * 4;

			for ( var iY = minY; iY <= maxY; iY++ )
			{
				mix[ 0 ] = 1f;
				mix[ 1 ] += r;
				mix[ 2 ] += g;
				mix[ 3 ] += b;

				mix += 4;
			}
		}
	}

	/// <summary>Background color for pixels this column that no <see cref="UpdateSolidFill"/>/<see cref="UpdateLine"/>
	/// call touched (grid lines still draw over it): 0 = fully transparent (normal), or a solid flash color —
	/// yellow for curb protection, orange for crash protection, red for clipping. The caller resolves the
	/// priority (clipping trumps crash protection trumps curb protection).</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetClearColor( uint color )
	{
		_clearColor = color;
	}

	// Writes EVERY row of the current column exactly once (plotted pixel wins, then grid template, then the
	// clear color), so no pixel can carry stale color from the previous wrap-around of the scrolling window.
	// One pointer walk down the column — no bounds checks, no 2D-index multiplies in the loop.
	public unsafe void FinishUpdates()
	{
		if ( ( _colorArray != null ) && ( _colorMixArray != null ) && ( _columnTemplate != null ) )
		{
			if ( _columnTemplateDirty )
			{
				RebuildColumnTemplate();
			}

			var width = BitmapWidth;
			var height = BitmapHeight;
			var clearColor = _clearColor;

			fixed ( uint* colorArray = _colorArray )
			fixed ( float* mixArray = _colorMixArray )
			fixed ( uint* template = _columnTemplate )
			{
				var pixel = colorArray + _x;   // top of this column; stepping by width walks down one row
				var mix = mixArray;

				for ( var y = 0; y < height; y++ )
				{
					if ( mix[ 0 ] > 0f )
					{
						var a = (uint) ( MathF.Min( 1f, mix[ 0 ] ) * 255f );
						var r = (uint) ( MathF.Min( 1f, mix[ 1 ] ) * 255f );
						var g = (uint) ( MathF.Min( 1f, mix[ 2 ] ) * 255f );
						var b = (uint) ( MathF.Min( 1f, mix[ 3 ] ) * 255f );

						*pixel = ( a << 24 ) | ( r << 16 ) | ( g << 8 ) | b;
					}
					else
					{
						var templateColor = template[ y ];

						*pixel = templateColor != 0 ? templateColor : clearColor;
					}

					pixel += width;
					mix += 4;
				}
			}

			_x = ( _x + 1 ) % width;

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
