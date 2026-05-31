using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Classes;

using Image = System.Windows.Controls.Image;

namespace MarvinsAIRARefactored.Components;

public class SeatBeltTensionerGraph : GraphBase
{
	private float r;
	private float g;
	private float b;

	public void Initialize( Image image, float r, float g, float b )
	{
		Initialize( image );

		this.r = r;
		this.g = g;
		this.b = b;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Advance( float value )
	{
		Update( value, r, g, b );
		FinishUpdates();
	}
}
