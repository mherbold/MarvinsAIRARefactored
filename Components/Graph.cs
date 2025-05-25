
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Image = System.Windows.Controls.Image;

namespace MarvinsAIRARefactored.Components;

public class Graph
{
	private readonly int _bitmapWidth;
	private readonly int _bitmapHeight;
	private readonly int _bitmapStride;

	private readonly WriteableBitmap _writeableBitmap;

	private readonly uint[,] _colorData;

	public int _x = 0;

	public Graph( Image image )
	{
		_bitmapWidth = (int) image.Width;
		_bitmapHeight = (int) image.Height;
		_bitmapStride = _bitmapWidth * 4;

		_writeableBitmap = new( _bitmapWidth, _bitmapHeight, 96f, 96f, PixelFormats.Bgra32, null );

		_colorData = new uint[ _bitmapHeight, _bitmapWidth ];

		image.Source = _writeableBitmap;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void DrawPoint( float y, uint red, uint green, uint blue )
	{
		y = y * -0.5f + 0.5f;

		var iy = (int) MathF.Floor( Math.Clamp( y * _bitmapHeight, 0, _bitmapHeight - 1 ) );

		_colorData[ iy, _x ] = 0xFF000000 | ( red << 16 ) | ( green << 8 ) | blue;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void DrawGradientLine( float y, uint red, uint green, uint blue )
	{
		y = y * -0.5f + 0.5f;

		var iy1 = (int) MathF.Floor( 0.5f * _bitmapHeight );
		var iy2 = (int) MathF.Floor( Math.Clamp( y * _bitmapHeight, 0, _bitmapHeight - 1 ) );

		if ( iy1 <= iy2 )
		{
			var range = iy2 - iy1 + 1f;

			for ( var i = 1; i <= range; i++ )
			{
				var multiplier = MathF.Pow( i / range, 2.2f );

				var color = 0xFF000000 | ( ( (uint) ( red * multiplier ) ) << 16 ) | ( ( (uint) ( green * multiplier ) ) << 8 ) | ( (uint) ( blue * multiplier ) );

				_colorData[ iy1 + i - 1, _x ] = color;
			}
		}
		else
		{
			var range = iy1 - iy2 + 1f;

			for ( var i = 1; i <= range; i++ )
			{
				var multiplier = MathF.Pow( i / range, 2.2f );

				var color = 0xFF000000 | ( ( (uint) ( red * multiplier ) ) << 16 ) | ( ( (uint) ( green * multiplier ) ) << 8 ) | ( (uint) ( blue * multiplier ) );

				_colorData[ iy1 - i + 1, _x ] = color;
			}
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void DrawSolidLine( float y1, float y2, uint alpha, uint red, uint green, uint blue )
	{
		y1 = y1 * -0.5f + 0.5f;
		y2 = y2 * -0.5f + 0.5f;

		var iy1 = (int) MathF.Floor( Math.Clamp( y2 * _bitmapHeight, 0, _bitmapHeight - 1 ) );
		var iy2 = (int) MathF.Floor( Math.Clamp( y1 * _bitmapHeight, 0, _bitmapHeight - 1 ) );

		var color = ( alpha << 24 ) | ( red << 16 ) | ( green << 8 ) | blue;

		for ( var y = iy1; y <= iy2; y++ )
		{
			_colorData[ y, _x ] = color;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Advance( bool drawEdge = false, uint red = 0, uint green = 0, uint blue = 0 )
	{
		DrawPoint( 0f, 128, 128, 128 );

		if ( drawEdge )
		{
			DrawPoint( -1f, red, green, blue );
			DrawPoint( +1f, red, green, blue );
		}

		_x = ( _x + 1 ) % _bitmapWidth;

		DrawSolidLine( -2f, 2f, 0, 0, 0, 0 );
	}

	public void UpdateImage()
	{
		var leftX = _x;
		var leftWidth = _bitmapWidth - leftX;

		var rightX = 0;
		var rightWidth = _x - rightX;

		if ( leftWidth > 0 )
		{
			var int32Rect = new Int32Rect( leftX, 0, leftWidth, _bitmapHeight );

			_writeableBitmap.WritePixels( int32Rect, _colorData, _bitmapStride, 0, 0 );
		}

		if ( rightWidth > 0 )
		{
			var int32Rect = new Int32Rect( rightX, 0, rightWidth, _bitmapHeight );

			_writeableBitmap.WritePixels( int32Rect, _colorData, _bitmapStride, leftWidth, 0 );
		}
	}
}
