
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
	public void Update( float value, float minR, float minG, float minB, float maxR, float maxG, float maxB )
	{
		if ( _colorMixArray != null )
		{
			var y = Math.Clamp( value, -1f, 1f );

			var absY = Math.Abs( y );

			var r = MathZ.Lerp( minR, maxR, absY );
			var g = MathZ.Lerp( minG, maxG, absY );
			var b = MathZ.Lerp( minB, maxB, absY );

			y = y * -0.5f + 0.5f;

			var iY1 = _bitmapHeightMinusOne / 2;
			var iY2 = (int) Math.Round( y * ( BitmapHeight - GutterSize * 2 ) ) + GutterSize;

			var delta = iY2 - iY1;

			var sign = Math.Sign( delta );
			var range = Math.Abs( delta );

			var iY = iY1;

			for ( var i = 1; i <= range; i++ )
			{
				var multiplier = MathF.Pow( (float) i / range, 4f );

				_colorMixArray[ iY, 0 ] = 1f;
				_colorMixArray[ iY, 1 ] += r * multiplier;
				_colorMixArray[ iY, 2 ] += g * multiplier;
				_colorMixArray[ iY, 3 ] += b * multiplier;

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
