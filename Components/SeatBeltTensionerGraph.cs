using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Classes;

using Image = System.Windows.Controls.Image;

namespace MarvinsAIRARefactored.Components;

public class SeatBeltTensionerGraph : GraphBase
{
	private float _minR;
	private float _minG;
	private float _minB;

	private float _maxR;
	private float _maxG;
	private float _maxB;

	public void Initialize( Image image, float minR, float minG, float minB, float maxR, float maxG, float maxB )
	{
		Initialize( image );

		_minR = minR;
		_minG = minG;
		_minB = minB;

		_maxR = maxR;
		_maxG = maxG;
		_maxB = maxB;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Advance( float value )
	{
		Update( value, _minR, _minG, _minB, _maxR, _maxG, _maxB );
		FinishUpdates();
	}
}
