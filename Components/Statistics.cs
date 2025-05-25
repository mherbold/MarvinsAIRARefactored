
namespace MarvinsAIRARefactored.Components;

public class Statistics( int size )
{
	public float AverageValue { get; private set; } = 0f;
	public float MinimumValue { get; private set; } = 0f;
	public float MaximumValue { get; private set; } = 0f;
	public float Variance { get; private set; } = 0f;
	public float StandardDeviation { get; private set; } = 0f;

	private readonly float[] _array = new float[ size ];

	private int _index = 0;

	private float _total = 0f;

	public void Update( float newValue )
	{
		var oldValue = _array[ _index ];

		_array[ _index ] = newValue;

		_index = ( _index + 1 ) % _array.Length;

		_total -= oldValue;
		_total += newValue;

		AverageValue = _total / _array.Length;

		var minimumValue = float.MaxValue;
		var maximumValue = float.MinValue;

		var variance = 0f;

		for ( var i = 0; i < _array.Length; i++ )
		{
			var value = _array[ i ];

			minimumValue = MathF.Min( minimumValue, value );
			maximumValue = MathF.Max( maximumValue, value );

			variance += MathF.Pow( ( value - AverageValue ), 2f );
		}

		MinimumValue = minimumValue;
		MaximumValue = maximumValue;

		Variance = variance / _array.Length;

		StandardDeviation = MathF.Sqrt( Variance );
	}
}
