
namespace MarvinsAIRARefactored.Classes;

public sealed class RlsWheelVelocityPredictor
{
	// Model: yHat = w0 + w1 * y + w2 * v

	private float _w0;
	private float _w1 = 1f; // start near persistence
	private float _w2;

	// 3x3 covariance matrix P (RLS)

	private float _p00 = 1000f, _p01, _p02;
	private float _p10, _p11 = 1000f, _p12;
	private float _p20, _p21, _p22 = 1000f;

	private readonly int _horizon;
	private readonly float _lambda;

	// pending feature vectors (x) waiting for the future truth y

	private readonly Pending[] _pending;
	private int _pendingHead;
	private int _pendingCount;

	private struct Pending
	{
		public float X0; // 1
		public float X1; // y
		public float X2; // v

		public float Predicted; // for debugging
	}

	public RlsWheelVelocityPredictor( int horizon, float forgettingFactor = 0.9995f, int maxPending = 8 )
	{
		_horizon = Math.Max( 1, horizon );
		_lambda = Math.Clamp( forgettingFactor, 0.95f, 0.999999f );
		_pending = new Pending[ Math.Max( maxPending, _horizon + 2 ) ];
	}

	public void Reset()
	{
		_w0 = 0f;
		_w1 = 1f;
		_w2 = 0f;

		_p00 = 1000f; _p01 = 0f; _p02 = 0f;
		_p10 = 0f; _p11 = 1000f; _p12 = 0f;
		_p20 = 0f; _p21 = 0f; _p22 = 1000f;

		_pendingHead = 0;
		_pendingCount = 0;
	}

	public float Step( float yNow, float wheelVelocityNow )
	{
		// predict using current features

		const float x0 = 1f;

		var x1 = yNow;
		var x2 = wheelVelocityNow;

		var yHat = _w0 * x0 + _w1 * x1 + _w2 * x2;

		// enqueue this feature vector for training when future truth arrives

		Enqueue( x0, x1, x2, yHat );

		// if we have a queued item that is now "mature" (horizon elapsed), update using yNow as truth

		if ( _pendingCount > _horizon )
		{
			var item = DequeueOldest();

			UpdateRls( item.X0, item.X1, item.X2, yNow );
		}

		return yHat;
	}

	private void Enqueue( float x0, float x1, float x2, float predicted )
	{
		var writeIndex = ( _pendingHead + _pendingCount ) % _pending.Length;

		if ( _pendingCount == _pending.Length )
		{
			// overwrite oldest if we ever overflow (shouldn't happen with maxPending >= horizon + 2)

			_pendingHead = ( _pendingHead + 1 ) % _pending.Length;

			_pendingCount--;
		}

		_pending[ writeIndex ] = new Pending { X0 = x0, X1 = x1, X2 = x2, Predicted = predicted };

		_pendingCount++;
	}

	private Pending DequeueOldest()
	{
		var item = _pending[ _pendingHead ];

		_pendingHead = ( _pendingHead + 1 ) % _pending.Length;

		_pendingCount--;

		return item;
	}

	private void UpdateRls( float x0, float x1, float x2, float yTrue )
	{
		// compute P*x

		var px0 = _p00 * x0 + _p01 * x1 + _p02 * x2;
		var px1 = _p10 * x0 + _p11 * x1 + _p12 * x2;
		var px2 = _p20 * x0 + _p21 * x1 + _p22 * x2;

		// denom = lambda + x^T * P * x

		var denom = _lambda + ( x0 * px0 + x1 * px1 + x2 * px2 );

		if ( denom < 1e-9f )
		{
			return;
		}

		var invDenom = 1f / denom;

		// Kalman gain K = (P*x)/denom

		var k0 = px0 * invDenom;
		var k1 = px1 * invDenom;
		var k2 = px2 * invDenom;

		// prediction error

		var yHat = _w0 * x0 + _w1 * x1 + _w2 * x2;
		var err = yTrue - yHat;

		// update weights

		_w0 += k0 * err;
		_w1 += k1 * err;
		_w2 += k2 * err;

		// update covariance: P = ( P - K * ( x^T * P ) ) / lambda, where x^T * P is row vector: [ x0 x1 x2 ] * P

		var xtp0 = x0 * _p00 + x1 * _p10 + x2 * _p20;
		var xtp1 = x0 * _p01 + x1 * _p11 + x2 * _p21;
		var xtp2 = x0 * _p02 + x1 * _p12 + x2 * _p22;

		_p00 = ( _p00 - k0 * xtp0 ) / _lambda;
		_p01 = ( _p01 - k0 * xtp1 ) / _lambda;
		_p02 = ( _p02 - k0 * xtp2 ) / _lambda;

		_p10 = ( _p10 - k1 * xtp0 ) / _lambda;
		_p11 = ( _p11 - k1 * xtp1 ) / _lambda;
		_p12 = ( _p12 - k1 * xtp2 ) / _lambda;

		_p20 = ( _p20 - k2 * xtp0 ) / _lambda;
		_p21 = ( _p21 - k2 * xtp1 ) / _lambda;
		_p22 = ( _p22 - k2 * xtp2 ) / _lambda;
	}
}
