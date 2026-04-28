
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
		0xFFFFFFFF,
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
		0xFFFFFFFF,
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

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Update( float value, float r, float g, float b )
	{
		if ( _colorMixArray != null )
		{
			// clamp y value to -1..1 range, where -1 is the bottom of the graph, 0 is the middle and 1 is the top
			var y = Math.Clamp( value, -1f, 1f );

			// invert y value and shift it to 0..1 range, where 0 is the top of the graph and 1 is the bottom
			y = y * -0.5f + 0.5f;

			// calculate the y position of the value on the graph
			var iY1 = _bitmapHeightMinusOne / 2;
			var iY2 = (int) Math.Round( y * ( BitmapHeight - GutterSize * 2 ) ) + GutterSize;

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

			for ( var y = 1; y  < GutterSize - 1; y++ )
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
				var y = gridSize * i + GutterSize;

				if ( ( _colorArray[ y, _x ] == 0 ) || ( ( i & 3 ) == 0 )  )
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
