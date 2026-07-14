using System.Globalization;

// ============================================================================================================
// MAIRA Prediction Lab
//
// Offline test bench for the Prediction FFB module: replays a MAIRA 360 Hz recording through candidate
// prediction algorithms and measures how far each one ACTUALLY shifts the torque waveform left in time
// (latency reduction), and at what cost. This is the tool that produced the 2026-07 Prediction module
// redesign (frame-anchored NLMS filter bank + Strength re-expansion) — keep it around to score any future
// prediction idea against the same yardstick before touching the app.
//
// Usage:
//   dotnet run -c Release                          (newest v3 recording in Documents\MarvinsAIRA Refactored\Recordings)
//   dotnet run -c Release -- "path\to\recording.csv"
//
// Ground truth: a predictor with horizon k should output, at tick t, an estimate of y[t+k]. Metrics per run:
//   rmse    - RMSE(out[t], y[t+k]) / RMSE(y[t], y[t+k])  — error vs the ideal shifted waveform, relative to
//             doing nothing; < 1 means the output matches the future BETTER than the unshifted input does
//   rmseLF  - same, but both signals band-limited to 15 Hz first (the perceptual body of FFB)
//   shift   - argmin over s of RMSE(outLF[t], yLF[t+s]) — the shift actually achieved, in ticks and ms
//             (validated: a perfect oracle scores exactly k ticks, persistence scores 0)
//   hfGain  - HF (>30 Hz) energy of output / HF energy of input — road-texture/noise amplification
//   |d|p95  - 95th percentile of the applied correction (Nm) — sanity check for the Correction limit knob
//
// Findings as of 2026-07-13 (Mazda MX-5 Cup @ Summit Point, 84 s):
//   * The old RLS model [1, y, wheelVelocity] achieved < 1 ms of real shift — no history/derivative features
//     means least-squares collapses to persistence. Wheel velocity only correlates r ≈ 0.3 with future torque
//     change, so it is not a useful leading signal.
//   * Frame anchoring is the big structural win: iRacing delivers all six 360 Hz ST samples at once, so a
//     predictor can anchor at the frame's NEWEST sample and only extrapolate the remaining depth.
//   * MMSE-optimal prediction (Wiener/NLMS) is amplitude-shy — accurate but shrunk toward the mean, so it
//     shows little visible shift. Re-expanding the learned correction (the module's Strength knob at
//     150–200%) restores full-amplitude lead; offline Wiener + 2x expansion hit 16 of 16.7 ms at K6.
//   * Only ~40–55% of torque change is linearly predictable: a clean K12 (33 ms) shift is NOT achievable,
//     and K12 nets LESS true shift than K6 once converged. K6 is the recommendation.
//   * Dead ends, so nobody retries them: RLS-with-forgetting blows up (covariance windup — section P);
//     training NLMS on all six correlated anchors per frame hurts (misadjustment — section O); a fixed
//     analytic unity-gain phase-lead FIR is causally infeasible (section K).
//   * Telemetry channel audit (section L, added 2026-07-13): an adaptive BIAS feature alone improves rmse
//     0.832 -> 0.763 for free (the torque taps have no intercept). Beyond the bias, the only channels with
//     real value are the steering-geometry ones — SteeringWheelAngle (rmse -0.077, shift +0.8 ticks) and
//     d/dt YawNorth = yaw rate (rmse -0.082, shift +0.4); together ~ rmse -0.099. Physically: angle + yaw
//     rate ~ front slip angle, which drives future self-aligning torque beyond what torque history shows.
//     Everything else is noise-level or harmful; slowly-varying channels (Speed, Gear, RPM) only ever acted
//     as intercept smugglers before the bias control was added. Yaw rate is NOT in FFBTickContext yet;
//     SteeringWheelAngle and the bias are available to the module today.
//   * The bias + steering-angle features shipped in the module on 2026-07-13 (section K reflects them):
//     every configuration improved on every metric at once — K6@150% rmse 0.82 -> 0.71 / shift 9.9 -> 10.6 ms
//     / hfGain 1.44 -> 1.29, and K12 went from pointless (5.4 ms) to viable (9.5 ms @150%, 12 ms @200%).
// ============================================================================================================

const int TickRate = 360;
const float Dt = 1f / TickRate;
const int Warmup = 3600; // 10 s — lets adaptive algorithms settle before scoring

// ---------------------------------------------------------------- recording discovery + load

var path = args.Length > 0 ? args[ 0 ] : FindNewestRecording();

if ( ( path == null ) || !File.Exists( path ) )
{
	Console.WriteLine( "No recording found — pass a path to a MAIRA Recording v3 .csv, or record one in the app first." );

	return 1;
}

Console.WriteLine( $"Recording: {path}" );

// load EVERY column of the recording (bools become 0/1) so the telemetry channel audit (section L) can test
// any channel MAIRA records without loader changes
var columnNames = Array.Empty<string>();
var columnData = Array.Empty<List<float>>();

using ( var reader = new StreamReader( path ) )
{
	// the loader is header-driven, so any version with the columns we key on works: v3 recordings simply
	// lack the YawRate column (added in v4) and it silently drops out of the audit's candidate list
	var formatLine = reader.ReadLine();

	if ( ( formatLine != "MAIRA Recording v3" ) && ( formatLine != "MAIRA Recording v4" ) )
	{
		Console.WriteLine( $"Unsupported recording format (expected 'MAIRA Recording v3' or 'v4', got '{formatLine}')." );

		return 1;
	}

	reader.ReadLine(); // description

	columnNames = ( reader.ReadLine() ?? string.Empty ).Split( ',' );
	columnData = [ .. columnNames.Select( _ => new List<float>() ) ];

	string? line;

	while ( ( line = reader.ReadLine() ) != null )
	{
		var parts = line.Split( ',' );

		if ( parts.Length != columnNames.Length )
		{
			continue;
		}

		for ( var i = 0; i < parts.Length; i++ )
		{
			columnData[ i ].Add( ParseCell( parts[ i ] ) );
		}
	}
}

var columns = new Dictionary<string, float[]>( StringComparer.Ordinal );

for ( var i = 0; i < columnNames.Length; i++ )
{
	columns[ columnNames[ i ] ] = [ .. columnData[ i ] ];
}

var Y = columns[ "InputTorque360Hz" ];     // the signal being predicted (Nm)
var VDI = columns[ "WheelVelocity" ];      // DirectInput wheel velocity (local hardware)
var VSIM = columns[ "SteeringWheelVelocity" ]; // sim steering wheel velocity (rad/s)
var n = Y.Length;

if ( n < Warmup * 2 )
{
	Console.WriteLine( $"Recording too short ({n / (float) TickRate:F1} s) — need at least {Warmup * 2 / TickRate} s." );

	return 1;
}

Console.WriteLine( $"Loaded {n} ticks ({n / (float) TickRate:F1} s), torque range [{Y.Min():F2}, {Y.Max():F2}] Nm" );

var evalRows = new List<string>();

// ============================================================================================================
// A. baselines — persistence (score floor) and a perfect oracle (validates the shift metric: must read k)
// ============================================================================================================

Banner( "A. baselines" );

foreach ( var k in new[] { 6, 12 } )
{
	Evaluate( "persistence (do nothing)", k, Y );
}

foreach ( var k in new[] { 6, 12 } )
{
	var output = new float[ n ];

	for ( var t = 0; t < n; t++ )
	{
		output[ t ] = Y[ Math.Min( n - 1, t + k ) ];
	}

	Evaluate( "ORACLE (perfect prediction)", k, output );
}

// ============================================================================================================
// B. the pre-2026-07 module: RLS over [1, y, wheelVelocity]. Verdict: ~0.3 ticks of shift — useless.
//    Kept as the historical baseline every new idea should embarrass.
// ============================================================================================================

Banner( "B. old module (RLS on [1, y, v])" );

foreach ( var k in new[] { 6, 12 } )
{
	Evaluate( "old RLS [1,y,vSim]", k, RunOldRls( k, VSIM ) );
	Evaluate( "old RLS [1,y,vDI]", k, RunOldRls( k, VDI ) );
}

float[] RunOldRls( int k, float[] vel )
{
	var predictor = new OldRls3( k );

	var output = new float[ n ];

	for ( var t = 0; t < n; t++ )
	{
		output[ t ] = predictor.Step( Y[ t ], vel[ t ] );
	}

	return output;
}

// ============================================================================================================
// C. Taylor-1 slope extrapolation (per-tick, no frame anchor): y + k*dt*slope with a low-passed derivative.
//    Verdict: 2-4 ticks of shift but boosts HF noise; strictly dominated by the frame-anchored variants.
// ============================================================================================================

Banner( "C. Taylor-1 slope extrapolation (per-tick)" );

foreach ( var k in new[] { 6, 12 } )
{
	foreach ( var fc in new[] { 5f, 10f, 20f } )
	{
		var output = new float[ n ];

		var a = 1f - MathF.Exp( -MathF.Tau * fc * Dt );

		var slope = 0f;

		for ( var t = 0; t < n; t++ )
		{
			if ( t > 0 )
			{
				slope += a * ( ( Y[ t ] - Y[ t - 1 ] ) * TickRate - slope );
			}

			output[ t ] = Y[ t ] + slope * k * Dt;
		}

		Evaluate( $"taylor1 slopeLPF {fc}Hz", k, output );
	}
}

// ============================================================================================================
// D. Savitzky-Golay polynomial extrapolation, per-tick vs frame-anchored. Verdict: the SAME algorithm gains
//    ~2 ticks of shift AND much better accuracy from frame anchoring alone — the clearest demonstration of
//    why the module anchors at the frame's newest ST sample. Order 2 shifts more but amplifies noise hard.
// ============================================================================================================

Banner( "D. SG extrapolation — per-tick vs frame-anchored" );

foreach ( var k in new[] { 6, 12 } )
{
	foreach ( var N in new[] { 8, 12, 18, 24 } )
	{
		foreach ( var order in new[] { 1, 2 } )
		{
			var perTick = new float[ n ];

			for ( var t = 0; t < n; t++ )
			{
				perTick[ t ] = t < N ? Y[ t ] : SgExtrapolate( Y, t, N, order, k );
			}

			Evaluate( $"SG per-tick N={N} order={order}", k, perTick );

			var frameAnchored = new float[ n ];

			for ( var t = 0; t < n; t++ )
			{
				var e = FrameNewest( t );
				var targetIndex = t + k;

				if ( ( e >= n ) || ( e < N ) )
				{
					frameAnchored[ t ] = Y[ t ];
				}
				else if ( targetIndex <= e )
				{
					frameAnchored[ t ] = Y[ targetIndex ]; // known in-frame data
				}
				else
				{
					frameAnchored[ t ] = SgExtrapolate( Y, e, N, order, targetIndex - e );
				}
			}

			Evaluate( $"SG FRAME N={N} order={order}", k, frameAnchored );
		}
	}
}

// ============================================================================================================
// E. chain ordering — the "LPF before or after prediction?" question. Chain outputs are scored against the
//    RAW input so the LPF's own lag counts. Verdict: an LPF ahead of the predictor eats the shift; predict
//    on raw first, smooth after.
// ============================================================================================================

Banner( "E. LPF chain ordering" );

foreach ( var k in new[] { 6, 12 } )
{
	{
		var filtered = OnePoleLpf( Y, 30f );

		var output = new float[ n ];

		var a = 1f - MathF.Exp( -MathF.Tau * 10f * Dt );

		var slope = 0f;

		for ( var t = 0; t < n; t++ )
		{
			if ( t > 0 )
			{
				slope += a * ( ( filtered[ t ] - filtered[ t - 1 ] ) * TickRate - slope );
			}

			output[ t ] = filtered[ t ] + slope * k * Dt;
		}

		Evaluate( "chain LPF30 -> taylor1", k, output );
	}

	{
		var output = new float[ n ];

		for ( var t = 0; t < n; t++ )
		{
			var e = FrameNewest( t );
			var targetIndex = t + k;

			if ( ( e >= n ) || ( e < 18 ) )
			{
				output[ t ] = Y[ t ];
			}
			else
			{
				output[ t ] = targetIndex <= e ? Y[ targetIndex ] : SgExtrapolate( Y, e, 18, 1, targetIndex - e );
			}
		}

		output = OnePoleLpf( output, 30f );

		Evaluate( "chain SG FRAME18 o1 -> LPF30", k, output );
	}
}

// ============================================================================================================
// F. WIENER ceiling — the MMSE-optimal linear FIR per depth, solved from the recording's own autocorrelation
//    (trained on the first half, scored on the second). This is the best ANY linear predictor can do on this
//    signal. Verdict: superb accuracy but little visible shift at g=1 (MMSE shrinkage!); re-expanding the
//    correction (g = the module's Strength knob) restores near-full shift — the module's core design.
// ============================================================================================================

Banner( "F. Wiener ceiling + expansion (g = Strength)" );

{
	const int L = 48;

	var half = n / 2;

	var coefficients = ComputeWienerBank( L, 18, Warmup, half );

	foreach ( var k in new[] { 6, 12 } )
	{
		foreach ( var frameAware in new[] { false, true } )
		{
			var output = new float[ n ];

			for ( var t = 0; t < n; t++ )
			{
				var e = frameAware ? FrameNewest( t ) : t;

				if ( e >= n )
				{
					e = n - 1;
				}

				var d = t + k - e;

				if ( ( e < L ) || ( d < 1 ) || ( d > 18 ) )
				{
					output[ t ] = Y[ t ];

					continue;
				}

				var a = coefficients[ d ];

				var s = 0.0;

				for ( var j = 0; j < L; j++ )
				{
					s += a[ j ] * Y[ e - j ];
				}

				output[ t ] = (float) s;
			}

			EvaluateRange( $"WIENER48 frame={( frameAware ? 1 : 0 )} g=1", k, output, half + 100, n - k - 30 );
		}

		foreach ( var g in new[] { 1.5f, 2f, 2.5f } )
		{
			var output = new float[ n ];

			for ( var t = 0; t < n; t++ )
			{
				var e = Math.Min( n - 1, FrameNewest( t ) );

				var d = t + k - e;

				if ( ( e < L ) || ( d < 1 ) || ( d > 18 ) )
				{
					output[ t ] = Y[ t ];

					continue;
				}

				var a = coefficients[ d ];

				var s = 0.0;

				for ( var j = 0; j < L; j++ )
				{
					s += a[ j ] * Y[ e - j ];
				}

				output[ t ] = Y[ t ] + g * ( (float) s - Y[ t ] );
			}

			EvaluateRange( $"WIENER48 frame=1 g={g}", k, output, half + 100, n - k - 30 );
		}
	}
}

// ============================================================================================================
// G. DEAD END: analytic phase-lead FIR — a fixed, signal-independent filter designed in the frequency domain
//    to lead the body band at unity gain and pass the texture band through. Verdict: rmseLF 2.4-4.7x — a
//    causal 32-tap filter cannot deliver unity-gain lead across the body band. Kept so nobody re-opens this.
// ============================================================================================================

Banner( "G. DEAD END: analytic phase-lead FIR" );

foreach ( var k in new[] { 6, 12 } )
{
	foreach ( var (f1, f2) in new[] { ( 6f, 20f ), ( 10f, 30f ) } )
	{
		var bank = DesignLeadFirBank( k, f1, f2, f0: 5f, wHf: 0.02f );

		var half = n / 2;

		var output = new float[ n ];

		for ( var t = 0; t < n; t++ )
		{
			var e = Math.Min( n - 1, FrameNewest( t ) );

			var d = t + k - e;

			if ( ( e < 32 ) || ( d < 1 ) || ( d > k ) )
			{
				output[ t ] = Y[ t ];

				continue;
			}

			var h = bank[ d ];

			var s = 0f;

			for ( var j = 0; j < 32; j++ )
			{
				s += h[ j ] * Y[ e - j ];
			}

			output[ t ] = s;
		}

		EvaluateRange( $"LEADFIR f1={f1} f2={f2}", k, output, half + 100, n - k - 30 );
	}
}

// ============================================================================================================
// H. online NLMS filter bank — the shippable version of the Wiener result: one L-tap filter per depth,
//    anchored at the frame's newest sample, ONE update per depth per frame, delta re-expanded by g.
//    Verdict: the winner. Converges within seconds and tracks the car continuously.
// ============================================================================================================

Banner( "H. online NLMS bank (the winning family)" );

foreach ( var k in new[] { 6, 12 } )
{
	foreach ( var (mu, g) in new[] { ( 0.25f, 1.5f ), ( 0.25f, 2f ), ( 0.5f, 1.5f ), ( 0.5f, 2f ) } )
	{
		var output = RunNlmsBank( k, 24, mu, g );

		EvaluateRange( $"NLMS L=24 mu={mu} g={g}", k, output, n / 2, n - k - 30 );

		// early window (10-25 s in) — shows convergence speed from a cold start
		EvaluateRange( $"NLMS-early L=24 mu={mu} g={g}", k, output, Warmup, 9000 );
	}
}

float[] RunNlmsBank( int k, int tapCount, float mu, float g )
{
	const float eps = 1e-3f;

	var dMin = Math.Max( 1, k - 5 );

	var w = new float[ k + 1 ][];

	for ( var d = dMin; d <= k; d++ )
	{
		w[ d ] = new float[ tapCount ];
		w[ d ][ 0 ] = 1f;
	}

	var output = new float[ n ];

	for ( var t = 0; t < n; t++ )
	{
		var e = Math.Min( n - 1, FrameNewest( t ) );

		var d = t + k - e;

		if ( e < tapCount + 40 )
		{
			output[ t ] = Y[ t ];

			continue;
		}

		if ( d < 1 )
		{
			output[ t ] = Y[ t + k ]; // target inside the already-known frame: exact

			continue;
		}

		var wd = w[ d ];

		// one NLMS update per depth per frame: the prediction made from anchor (e - d) just met its truth
		{
			var anchor = e - d;

			float dot = 0f, norm = eps;

			for ( var j = 0; j < tapCount; j++ )
			{
				var x = Y[ anchor - j ];

				dot += wd[ j ] * x;
				norm += x * x;
			}

			var scale = mu * ( Y[ anchor + d ] - dot ) / norm;

			for ( var j = 0; j < tapCount; j++ )
			{
				wd[ j ] += scale * Y[ anchor - j ];
			}
		}

		var pred = 0f;

		for ( var j = 0; j < tapCount; j++ )
		{
			pred += wd[ j ] * Y[ e - j ];
		}

		output[ t ] = Y[ t ] + g * ( pred - Y[ t ] );
	}

	return output;
}

// ============================================================================================================
// I. DEAD END: NLMS trained on all six anchors per frame. Verdict: consecutive anchors are highly correlated,
//    so 6x the updates just multiplies misadjustment — WORSE than one update per frame at every setting.
// ============================================================================================================

Banner( "I. DEAD END: NLMS with 6 updates/frame" );

foreach ( var k in new[] { 6, 12 } )
{
	const int L = 24;
	const float eps = 1e-3f;
	const float mu = 0.5f;
	const float g = 2f;

	var dMin = Math.Max( 1, k - 5 );

	var w = new float[ k + 1 ][];

	for ( var d2 = dMin; d2 <= k; d2++ )
	{
		w[ d2 ] = new float[ L ];
		w[ d2 ][ 0 ] = 1f;
	}

	var output = new float[ n ];
	var lastTrainedAnchor = new int[ k + 1 ];

	for ( var t = 0; t < n; t++ )
	{
		var e = Math.Min( n - 1, FrameNewest( t ) );

		var d = t + k - e;

		if ( e < L + 40 )
		{
			output[ t ] = Y[ t ];

			if ( ( d >= dMin ) && ( d <= k ) )
			{
				lastTrainedAnchor[ d ] = e - d;
			}

			continue;
		}

		if ( d < 1 )
		{
			output[ t ] = Y[ t + k ];

			continue;
		}

		var wd = w[ d ];

		for ( var anchor = Math.Max( L, lastTrainedAnchor[ d ] + 1 ); anchor <= e - d; anchor++ )
		{
			float dot = 0f, norm = eps;

			for ( var j = 0; j < L; j++ )
			{
				var x = Y[ anchor - j ];

				dot += wd[ j ] * x;
				norm += x * x;
			}

			var scale = mu * ( Y[ anchor + d ] - dot ) / norm;

			for ( var j = 0; j < L; j++ )
			{
				wd[ j ] += scale * Y[ anchor - j ];
			}
		}

		lastTrainedAnchor[ d ] = e - d;

		var pred = 0f;

		for ( var j = 0; j < L; j++ )
		{
			pred += wd[ j ] * Y[ e - j ];
		}

		output[ t ] = Y[ t ] + g * ( pred - Y[ t ] );
	}

	EvaluateRange( $"NLMS6x mu={mu} g={g}", k, output, n / 2, n - k - 30 );
}

// ============================================================================================================
// J. DEAD END: RLS with forgetting over structured features [y, slopeFast, slopeSlow, curv, 1].
//    Verdict: covariance windup — the estimate explodes to ~1e8 Nm. Plain NLMS is unconditionally stable;
//    do not "upgrade" the module back to RLS without directional forgetting and a lot of care.
// ============================================================================================================

Banner( "J. DEAD END: RLS-with-forgetting instability demo" );

foreach ( var k in new[] { 6 } )
{
	const int F = 5;

	var dMin = Math.Max( 1, k - 5 );

	var feat = new float[ n ][];

	{
		var aFast = 1f - MathF.Exp( -MathF.Tau * 40f * Dt );
		var aSlow = 1f - MathF.Exp( -MathF.Tau * 10f * Dt );

		float slopeFast = 0f, slopeSlow = 0f, curv = 0f, prevSlow = 0f;

		for ( var t = 0; t < n; t++ )
		{
			if ( t > 0 )
			{
				var diff = ( Y[ t ] - Y[ t - 1 ] ) * TickRate;

				slopeFast += aFast * ( diff - slopeFast );
				slopeSlow += aSlow * ( diff - slopeSlow );
				curv += aSlow * ( ( slopeSlow - prevSlow ) * TickRate - curv );

				prevSlow = slopeSlow;
			}

			feat[ t ] = [ Y[ t ], slopeFast * Dt, slopeSlow * Dt, curv * Dt * Dt, 1f ];
		}
	}

	var rls = new RlsN[ k + 1 ];

	for ( var d = dMin; d <= k; d++ )
	{
		rls[ d ] = new RlsN( F );
	}

	var output = new float[ n ];
	var lastTrainedAnchor = new int[ k + 1 ];

	for ( var t = 0; t < n; t++ )
	{
		var e = Math.Min( n - 1, FrameNewest( t ) );

		var d = t + k - e;

		if ( e < 60 )
		{
			output[ t ] = Y[ t ];

			if ( ( d >= dMin ) && ( d <= k ) )
			{
				lastTrainedAnchor[ d ] = e - d;
			}

			continue;
		}

		if ( d < 1 )
		{
			output[ t ] = Y[ t + k ];

			continue;
		}

		for ( var anchor = Math.Max( 30, lastTrainedAnchor[ d ] + 1 ); anchor <= e - d; anchor++ )
		{
			rls[ d ].Update( feat[ anchor ], Y[ anchor + d ] );
		}

		lastTrainedAnchor[ d ] = e - d;

		output[ t ] = Y[ t ] + ( rls[ d ].Predict( feat[ e ] ) - Y[ t ] );
	}

	EvaluateRange( "RLSfeat (watch it explode)", k, output, n / 2, n - k - 30 );
}

// ============================================================================================================
// K. THE SHIPPED MODULE — verbatim port of MarvinsAIRARefactored.FFB.Modules.PredictionModule (ring buffer,
//    depth mapping, known-frame branch, Strength semantics, Correction limit). KEEP IN SYNC with the app:
//    if you change the module, mirror it here and re-run so the scores stay honest.
// ============================================================================================================

Banner( "K. shipped PredictionModule (keep in sync with the app!)" );

{
	// the module scales the angle by the live MaxForce setting; the recording doesn't store it, so use a
	// representative 20 Nm — it only sets the angle feature's scale, which NLMS fine-tunes anyway
	const float maxForce = 20f;

	var wheelPosition = columns[ "WheelPosition" ];

	foreach ( var k in new[] { 2, 6, 12 } )
	{
		foreach ( var strength in new[] { 1f, 1.5f, 2f } )
		{
			var module = new ShippedPredictionModule( k, strength, correctionLimit: 5f );

			var output = new float[ n ];

			var frame = new float[ 6 ];

			for ( var t = 0; t < n; t++ )
			{
				var i = t % 6;

				if ( i == 0 )
				{
					for ( var j = 0; j < 6; j++ )
					{
						frame[ j ] = Y[ Math.Min( n - 1, t + j ) ];
					}
				}

				output[ t ] = module.Process( frame, i, Y[ t ], wheelPosition[ t ], maxForce );
			}

			EvaluateRange( $"SHIPPED k={k} strength={strength}", k, output, n / 2, n - k - 30 );
		}
	}
}

// ============================================================================================================
// L. telemetry channel audit — could ANY recorded channel (or its derivative) help predict torque beyond
//    what torque history already provides? Three tests per channel, from cheap intuition to hard proof:
//      leadCorr  - peak |corr| between the channel and the FUTURE 15 Hz torque change (raw predictive hint)
//      residCorr - peak |corr| between the channel and the torque-only NLMS predictor's RESIDUAL — the only
//                  variance a new input could still explain; residCorr^2 is the ceiling on its contribution
//      aug rmse/shift - the end-to-end proof: the channel added to the NLMS bank as 6 frame-spaced taps,
//                  scored against an identical bank fed a zero channel (same tap count = fair comparison)
//    Channels are 60 Hz frame-constant in the recording, so lags are scanned in whole frames (0/6/12/18).
//    Both the baseline and every candidate include an adaptive BIAS feature: without it, any slowly-varying
//    channel (speed, gear position, ...) "improves" the score by ~0.11 rmse purely by smuggling in an
//    intercept for the slowly-varying torque offset — with zero actual predictive correlation. The bias
//    baseline absorbs that effect so the deltas below measure genuine information only.
// ============================================================================================================

Banner( "L. telemetry channel audit (k=6, strength 150%)" );

{
	const int auditK = 6;
	const float auditMu = 0.25f;
	const float auditG = 1.5f;

	var from = n / 2;
	var to = n - auditK - 30;

	// the torque-only predictor's residual: what torque history cannot explain (pure prediction, g=1)
	var torqueOnly = RunNlmsBank( auditK, 24, auditMu, 1f );

	var residual = new float[ n ];

	for ( var t = 0; t < to; t++ )
	{
		residual[ t ] = Y[ t + auditK ] - torqueOnly[ t ];
	}

	// future LF torque change (the intuition column): what an ideal shift would add at each tick
	var yLf15 = OnePoleLpf( Y, 15f );

	var futureChange = new float[ n ];

	for ( var t = 0; t < to; t++ )
	{
		futureChange[ t ] = yLf15[ t + auditK ] - yLf15[ t ];
	}

	// candidates: every recorded channel except the torque columns and constants/monotonics, plus each
	// channel's 60 Hz frame-difference (e.g. d/dt YawNorth = yaw rate, d/dt SteeringWheelAngle = wheel speed)
	var skip = new HashSet<string>( StringComparer.Ordinal )
	{
		"InputTorque60Hz", "InputTorque360Hz",           // that IS the signal
		"ShiftRPM", "NumForwardGears", "SteeringWheelAngleMax", // car constants
		"TrackPosition",                                 // monotonic (and its derivative is just Speed)
		"YawNorth"                                       // wrapping heading — only its derivative (yaw rate) makes sense
	};

	var candidates = new List<( string Name, float[] Series )>();

	foreach ( var name in columnNames )
	{
		var series = columns[ name ];

		if ( !skip.Contains( name ) )
		{
			candidates.Add( ( name, series ) );
		}

		if ( ( name == "InputTorque60Hz" ) || ( name == "InputTorque360Hz" ) || ( name == "ShiftRPM" ) || ( name == "NumForwardGears" ) || ( name == "SteeringWheelAngleMax" ) || ( name == "TrackPosition" ) )
		{
			continue;
		}

		// frame-difference derivative (per second); YawNorth wraps at ±pi
		var derivative = new float[ n ];

		for ( var t = 6; t < n; t++ )
		{
			var d = series[ t ] - series[ t - 6 ];

			if ( name == "YawNorth" )
			{
				while ( d > MathF.PI ) { d -= MathF.Tau; }
				while ( d < -MathF.PI ) { d += MathF.Tau; }
			}

			derivative[ t ] = d * 60f;
		}

		candidates.Add( ( $"d/dt {name}", derivative ) );
	}

	// baselines: torque-only, then torque + adaptive bias. The bias alone captures the slowly-varying torque
	// offset — every channel is scored against the WITH-bias baseline so a slow channel can't take credit
	// for merely smuggling in an intercept.
	var zeroChannel = new float[ n ];

	var torqueOnlyScore = Measure( auditK, RunAugmentedNlmsBank( auditK, 24, auditMu, auditG, includeBias: false, zeroChannel ), from, to );
	var baseline = Measure( auditK, RunAugmentedNlmsBank( auditK, 24, auditMu, auditG, includeBias: true, zeroChannel ), from, to );

	evalRows.Add( $"{"baseline: torque only",-28} | leadCorr   -- | residCorr   -- | aug rmse {torqueOnlyScore.Rmse,6:F3} ({Signed( torqueOnlyScore.Rmse - baseline.Rmse, 3 )}) | aug shift {torqueOnlyScore.Shift,4:F1} ticks ({Signed( torqueOnlyScore.Shift - baseline.Shift, 1 )})" );
	evalRows.Add( $"{"baseline: torque + bias",-28} | leadCorr   -- | residCorr   -- | aug rmse {baseline.Rmse,6:F3} (+0.000) | aug shift {baseline.Shift,4:F1} ticks (+0.0)" );

	var auditResults = new List<( string Name, double LeadCorr, double ResidCorr, double RmseDelta, double ShiftDelta, string Row )>();

	foreach ( var (name, series) in candidates )
	{
		var leadCorr = MaxAbsLaggedCorr( series, futureChange, from, to );
		var residCorr = MaxAbsLaggedCorr( series, residual, from, to );

		var augmented = Measure( auditK, RunAugmentedNlmsBank( auditK, 24, auditMu, auditG, includeBias: true, series ), from, to );

		var rmseDelta = augmented.Rmse - baseline.Rmse;
		var shiftDelta = augmented.Shift - baseline.Shift;

		var row = $"{name,-28} | leadCorr {leadCorr,4:F2} | residCorr {residCorr,4:F2} | aug rmse {augmented.Rmse,6:F3} ({Signed( rmseDelta, 3 )}) | aug shift {augmented.Shift,4:F1} ticks ({Signed( shiftDelta, 1 )})";

		auditResults.Add( ( name, leadCorr, residCorr, rmseDelta, shiftDelta, row ) );
	}

	// most helpful first: biggest rmse improvement (most negative delta)
	foreach ( var result in auditResults.OrderBy( r => r.RmseDelta ) )
	{
		evalRows.Add( result.Row );
	}

	// kitchen sink: the three best channels together (does anything stack?)
	var topThree = auditResults.OrderBy( r => r.RmseDelta ).Take( 3 ).ToArray();

	var combined = Measure( auditK, RunAugmentedNlmsBank( auditK, 24, auditMu, auditG, includeBias: true, topThree.Select( r => candidates.First( c => c.Name == r.Name ).Series ).ToArray() ), from, to );

	evalRows.Add( $"top-3 combined ({string.Join( " + ", topThree.Select( r => r.Name ) )}) | aug rmse {combined.Rmse,6:F3} ({Signed( combined.Rmse - baseline.Rmse, 3 )}) | aug shift {combined.Shift,4:F1} ticks ({Signed( combined.Shift - baseline.Shift, 1 )})" );
}

// ---------------------------------------------------------------- results

Console.WriteLine();

foreach ( var row in evalRows )
{
	Console.WriteLine( row );
}

return 0;

// ============================================================================================================
// helpers
// ============================================================================================================

static string? FindNewestRecording()
{
	var folder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "MarvinsAIRA Refactored", "Recordings" );

	if ( !Directory.Exists( folder ) )
	{
		return null;
	}

	return Directory.EnumerateFiles( folder, "*.csv" ).OrderByDescending( File.GetLastWriteTimeUtc ).FirstOrDefault();
}

static string Signed( double value, int decimals ) => ( value >= 0 ? "+" : "" ) + value.ToString( "F" + decimals, CultureInfo.CurrentCulture );

static float ParseCell( string cell )
{
	if ( float.TryParse( cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) )
	{
		return value;
	}

	return cell == "True" ? 1f : 0f;
}

// peak |Pearson r| between the (causally lagged) series and the target, over whole-frame lags — the recording's
// telemetry channels are 60 Hz frame-constant, so finer lag steps would just re-test the same values
double MaxAbsLaggedCorr( float[] series, float[] target, int from, int to )
{
	var best = 0.0;

	foreach ( var lag in new[] { 0, 6, 12, 18 } )
	{
		best = Math.Max( best, Math.Abs( Pearson( series, target, from, to, lag ) ) );
	}

	return best;
}

static double Pearson( float[] a, float[] b, int from, int to, int aLag )
{
	var sumA = 0.0;
	var sumB = 0.0;
	var count = 0;

	for ( var t = from; t < to; t++ )
	{
		sumA += a[ t - aLag ];
		sumB += b[ t ];

		count++;
	}

	var meanA = sumA / count;
	var meanB = sumB / count;

	var num = 0.0;
	var varA = 0.0;
	var varB = 0.0;

	for ( var t = from; t < to; t++ )
	{
		var da = a[ t - aLag ] - meanA;
		var db = b[ t ] - meanB;

		num += da * db;
		varA += da * da;
		varB += db * db;
	}

	return num / Math.Sqrt( varA * varB + 1e-30 );
}

// the NLMS bank with auxiliary telemetry channels appended to the feature vector: 24 torque lags plus 6
// frame-spaced taps per channel (values at e, e-6, ..., e-30 — finer spacing is pointless on 60 Hz-constant
// channels). Channels are standardized to the torque's scale so the shared NLMS normalization treats every
// tap comparably. includeBias adds one constant feature (an adaptive intercept) — IMPORTANT for the audit:
// any slowly-varying channel (speed, gear, ...) can smuggle in an intercept and look predictive without
// carrying real information, so channels must be compared against a baseline that already has the bias.
// Pass an all-zero channel to get the fair same-tap-count baseline.
float[] RunAugmentedNlmsBank( int k, int tapCount, float mu, float g, bool includeBias, params float[][] auxChannels )
{
	const float eps = 1e-3f;
	const int auxTaps = 6;

	var yStd = StdDev( Y, Warmup, n / 2 );

	var scaled = new float[ auxChannels.Length ][];

	for ( var c = 0; c < auxChannels.Length; c++ )
	{
		var (mean, std) = MeanStd( auxChannels[ c ], Warmup, n / 2 );

		var s = new float[ n ];

		if ( std > 1e-9 )
		{
			var factor = (float) ( yStd / std );

			for ( var t = 0; t < n; t++ )
			{
				s[ t ] = ( auxChannels[ c ][ t ] - (float) mean ) * factor;
			}
		}

		scaled[ c ] = s;
	}

	var biasFeature = includeBias ? (float) yStd : 0f;

	var featureCount = tapCount + auxChannels.Length * auxTaps + ( includeBias ? 1 : 0 );

	var dMin = Math.Max( 1, k - 5 );

	var w = new float[ k + 1 ][];

	for ( var d = dMin; d <= k; d++ )
	{
		w[ d ] = new float[ featureCount ];
		w[ d ][ 0 ] = 1f;
	}

	var x = new float[ featureCount ];

	void BuildFeatures( int anchor )
	{
		for ( var j = 0; j < tapCount; j++ )
		{
			x[ j ] = Y[ anchor - j ];
		}

		var index = tapCount;

		for ( var c = 0; c < scaled.Length; c++ )
		{
			for ( var j = 0; j < auxTaps; j++ )
			{
				x[ index++ ] = scaled[ c ][ anchor - j * 6 ];
			}
		}

		if ( includeBias )
		{
			x[ index ] = biasFeature;
		}
	}

	var output = new float[ n ];

	for ( var t = 0; t < n; t++ )
	{
		var e = Math.Min( n - 1, FrameNewest( t ) );

		var d = t + k - e;

		if ( e < tapCount + auxTaps * 6 + 40 )
		{
			output[ t ] = Y[ t ];

			continue;
		}

		if ( d < 1 )
		{
			output[ t ] = Y[ t + k ];

			continue;
		}

		var wd = w[ d ];

		// one NLMS update per depth per frame, from the anchor whose truth just arrived (see RunNlmsBank)
		BuildFeatures( e - d );

		var dot = 0f;
		var norm = eps;

		for ( var j = 0; j < featureCount; j++ )
		{
			dot += wd[ j ] * x[ j ];
			norm += x[ j ] * x[ j ];
		}

		var scale = mu * ( Y[ e ] - dot ) / norm;

		for ( var j = 0; j < featureCount; j++ )
		{
			wd[ j ] += scale * x[ j ];
		}

		BuildFeatures( e );

		var pred = 0f;

		for ( var j = 0; j < featureCount; j++ )
		{
			pred += wd[ j ] * x[ j ];
		}

		output[ t ] = Y[ t ] + g * ( pred - Y[ t ] );
	}

	return output;
}

static ( double Mean, double Std ) MeanStd( float[] series, int from, int to )
{
	var sum = 0.0;

	for ( var t = from; t < to; t++ )
	{
		sum += series[ t ];
	}

	var mean = sum / ( to - from );

	var variance = 0.0;

	for ( var t = from; t < to; t++ )
	{
		var d = series[ t ] - mean;

		variance += d * d;
	}

	return ( mean, Math.Sqrt( variance / ( to - from ) ) );
}

double StdDev( float[] series, int from, int to ) => MeanStd( series, from, to ).Std;

// index of the newest already-known sample in tick t's 60 Hz frame (the live burst has the whole ST frame)
static int FrameNewest( int t ) => t - ( t % 6 ) + 5;

static float[] OnePoleLpf( float[] x, float cutoffHz, int passes = 2 )
{
	var a = 1f - MathF.Exp( -MathF.Tau * cutoffHz * Dt );

	var output = (float[]) x.Clone();

	for ( var p = 0; p < passes; p++ )
	{
		var s = output[ 0 ];

		for ( var i = 0; i < output.Length; i++ )
		{
			s += a * ( output[ i ] - s );

			output[ i ] = s;
		}
	}

	return output;
}

static double Rmse( float[] a, float[] b, int from, int to, int bShift = 0 )
{
	var sum = 0.0;
	var count = 0;

	for ( var t = from; t < to; t++ )
	{
		var d = a[ t ] - b[ t + bShift ];

		sum += d * (double) d;

		count++;
	}

	return Math.Sqrt( sum / count );
}

// least-squares polynomial fit over the last N samples ending at index e (x = -(N-1)..0), evaluated at x = +d
static float SgExtrapolate( float[] src, int e, int N, int order, float d )
{
	double s0 = N, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
	double b0 = 0, b1 = 0, b2 = 0;

	for ( var j = 0; j < N; j++ )
	{
		double x = -j;

		var v = src[ e - j ];

		s1 += x; s2 += x * x; s3 += x * x * x; s4 += x * x * x * x;
		b0 += v; b1 += x * v; b2 += x * x * v;
	}

	if ( order == 1 )
	{
		var det = s0 * s2 - s1 * s1;

		var a = ( b0 * s2 - b1 * s1 ) / det;
		var b = ( s0 * b1 - s1 * b0 ) / det;

		return (float) ( a + b * d );
	}
	else
	{
		double m00 = s0, m01 = s1, m02 = s2, m11 = s2, m12 = s3, m22 = s4;

		var det = m00 * ( m11 * m22 - m12 * m12 ) - m01 * ( m01 * m22 - m12 * m02 ) + m02 * ( m01 * m12 - m11 * m02 );

		var a = ( b0 * ( m11 * m22 - m12 * m12 ) - m01 * ( b1 * m22 - m12 * b2 ) + m02 * ( b1 * m12 - m11 * b2 ) ) / det;
		var b = ( m00 * ( b1 * m22 - m12 * b2 ) - b0 * ( m01 * m22 - m12 * m02 ) + m02 * ( m01 * b2 - b1 * m02 ) ) / det;
		var c = ( m00 * ( m11 * b2 - b1 * m12 ) - m01 * ( m01 * b2 - b1 * m02 ) + b0 * ( m01 * m12 - m11 * m02 ) ) / det;

		return (float) ( a + b * d + c * d * d );
	}
}

// solve A x = b in place via Gaussian elimination with partial pivoting (A is destroyed)
static double[] SolveLinear( double[,] a, double[] b )
{
	var size = b.Length;

	for ( var col = 0; col < size; col++ )
	{
		var pivot = col;

		for ( var r = col + 1; r < size; r++ )
		{
			if ( Math.Abs( a[ r, col ] ) > Math.Abs( a[ pivot, col ] ) )
			{
				pivot = r;
			}
		}

		if ( pivot != col )
		{
			for ( var c = 0; c < size; c++ )
			{
				( a[ col, c ], a[ pivot, c ] ) = ( a[ pivot, c ], a[ col, c ] );
			}

			( b[ col ], b[ pivot ] ) = ( b[ pivot ], b[ col ] );
		}

		for ( var r = col + 1; r < size; r++ )
		{
			var f = a[ r, col ] / a[ col, col ];

			for ( var c = col; c < size; c++ )
			{
				a[ r, c ] -= f * a[ col, c ];
			}

			b[ r ] -= f * b[ col ];
		}
	}

	var x = new double[ size ];

	for ( var r = size - 1; r >= 0; r-- )
	{
		var s = b[ r ];

		for ( var c = r + 1; c < size; c++ )
		{
			s -= a[ r, c ] * x[ c ];
		}

		x[ r ] = s / a[ r, r ];
	}

	return x;
}

// per-depth MMSE-optimal FIR coefficients from the signal's autocorrelation over [trainFrom, trainTo)
double[][] ComputeWienerBank( int L, int maxDepth, int trainFrom, int trainTo )
{
	var maxLag = L + maxDepth;

	var ac = new double[ maxLag + 1 ];

	for ( var lag = 0; lag <= maxLag; lag++ )
	{
		var s = 0.0;

		for ( var t = trainFrom; t < trainTo - lag; t++ )
		{
			s += Y[ t ] * (double) Y[ t + lag ];
		}

		ac[ lag ] = s / ( trainTo - lag - trainFrom );
	}

	var bank = new double[ maxDepth + 1 ][];

	for ( var d = 1; d <= maxDepth; d++ )
	{
		var a = new double[ L, L ];
		var b = new double[ L ];

		for ( var r = 0; r < L; r++ )
		{
			for ( var c = 0; c < L; c++ )
			{
				a[ r, c ] = ac[ Math.Abs( r - c ) ];
			}

			a[ r, r ] += 1e-6 * ac[ 0 ]; // ridge for stability

			b[ r ] = ac[ r + d ];
		}

		bank[ d ] = SolveLinear( a, b );
	}

	return bank;
}

// frequency-domain weighted LS design of a causal "lead the body band, pass the texture band" FIR per depth —
// kept only as the section-G dead-end demonstration
static float[][] DesignLeadFirBank( int k, float f1, float f2, float f0, float wHf )
{
	const int L = 32;
	const int M = 720;

	var bank = new float[ k + 1 ][];

	var cr = new double[ L ];
	var ci = new double[ L ];

	for ( var d = 1; d <= k; d++ )
	{
		var a = new double[ L, L ];
		var b = new double[ L ];

		for ( var m = 0; m < M; m++ )
		{
			var f = 180.0 * ( m + 0.5 ) / M;

			var w = 1.0 / ( 1.0 + ( f / f0 ) * ( f / f0 ) ) + wHf;

			var omega = 2.0 * Math.PI * f * Dt;

			double phase;

			if ( f <= f1 )
			{
				phase = omega * d;
			}
			else if ( f >= f2 )
			{
				phase = -omega * ( k - d );
			}
			else
			{
				var s = ( f - f1 ) / ( f2 - f1 );

				s = s * s * ( 3 - 2 * s );

				phase = ( 1 - s ) * ( omega * d ) + s * ( -omega * ( k - d ) );
			}

			var tr = Math.Cos( phase );
			var ti = Math.Sin( phase );

			for ( var j = 0; j < L; j++ )
			{
				cr[ j ] = Math.Cos( omega * j );
				ci[ j ] = -Math.Sin( omega * j );
			}

			for ( var r = 0; r < L; r++ )
			{
				b[ r ] += w * ( cr[ r ] * tr + ci[ r ] * ti );

				for ( var c = r; c < L; c++ )
				{
					a[ r, c ] += w * ( cr[ r ] * cr[ c ] + ci[ r ] * ci[ c ] );
				}
			}
		}

		for ( var r = 0; r < L; r++ )
		{
			a[ r, r ] += 1e-4 * M;

			for ( var c = 0; c < r; c++ )
			{
				a[ r, c ] = a[ c, r ];
			}
		}

		var solved = SolveLinear( a, b );

		bank[ d ] = [ .. solved.Select( v => (float) v ) ];
	}

	return bank;
}

void Banner( string title )
{
	evalRows.Add( string.Empty );
	evalRows.Add( $"---- {title} " + new string( '-', Math.Max( 4, 110 - title.Length ) ) );
}

void Evaluate( string name, int k, float[] output ) => EvaluateRange( name, k, output, Warmup, n - k - 30 );

void EvaluateRange( string name, int k, float[] output, int from, int to )
{
	var score = Measure( k, output, from, to );

	evalRows.Add( $"{name,-46} k={k,2} | rmse {score.Rmse,6:F3} | rmseLF {score.RmseLf,6:F3} | shift {score.Shift,5:F1} ticks ({score.Shift * 1000f / TickRate,5:F1} ms) | hfGain {score.HfGain,6:F2} | |d|p95 {score.P95,6:F2} Nm" );
}

// the raw metrics behind EvaluateRange, for sections that need to compare scores numerically
( double Rmse, double RmseLf, double Shift, double HfGain, float P95 ) Measure( int k, float[] output, int from, int to )
{
	var rmsePredicted = Rmse( output, Y, from, to, k );
	var rmsePersistence = Rmse( Y, Y, from, to, k );

	var yLf = OnePoleLpf( Y, 15f );
	var outputLf = OnePoleLpf( output, 15f );

	var rmsePredictedLf = Rmse( outputLf, yLf, from, to, k );
	var rmsePersistenceLf = Rmse( yLf, yLf, from, to, k );

	// achieved shift: argmin over s of RMSE(outLF[t], yLF[t+s]), with parabolic sub-tick refinement
	var bestShift = 0;
	var bestError = double.MaxValue;

	var errorByShift = new double[ 49 ];

	for ( var s = -24; s <= 24; s++ )
	{
		var e = Rmse( outputLf, yLf, from, to, s );

		errorByShift[ s + 24 ] = e;

		if ( e < bestError )
		{
			bestError = e;
			bestShift = s;
		}
	}

	var shift = (double) bestShift;

	if ( ( bestShift > -24 ) && ( bestShift < 24 ) )
	{
		var e0 = errorByShift[ bestShift + 23 ];
		var e1 = errorByShift[ bestShift + 24 ];
		var e2 = errorByShift[ bestShift + 25 ];

		var denom = e0 - 2 * e1 + e2;

		if ( Math.Abs( denom ) > 1e-12 )
		{
			shift = bestShift + 0.5 * ( e0 - e2 ) / denom;
		}
	}

	// HF (>30 Hz) energy amplification
	var y30 = OnePoleLpf( Y, 30f );
	var output30 = OnePoleLpf( output, 30f );

	var hfIn = 0.0;
	var hfOut = 0.0;

	for ( var t = from; t < to; t++ )
	{
		var hi = Y[ t ] - y30[ t ];
		var ho = output[ t ] - output30[ t ];

		hfIn += hi * (double) hi;
		hfOut += ho * (double) ho;
	}

	var hfGain = Math.Sqrt( hfOut / hfIn );

	// correction magnitude p95
	var deltas = new List<float>( to - from );

	for ( var t = from; t < to; t++ )
	{
		deltas.Add( MathF.Abs( output[ t ] - Y[ t ] ) );
	}

	deltas.Sort();

	var p95 = deltas[ (int) ( deltas.Count * 0.95 ) ];

	return ( rmsePredicted / rmsePersistence, rmsePredictedLf / rmsePersistenceLf, shift, hfGain, p95 );
}

// ============================================================================================================
// The module that shipped — VERBATIM port of MarvinsAIRARefactored.FFB.Modules.PredictionModule.
// If the app module changes, mirror the change here and re-run the lab.
// ============================================================================================================

sealed class ShippedPredictionModule
{
	private const int TapCount = 24;
	private const int AngleTapCount = 6;
	private const int FeatureCount = TapCount + AngleTapCount + 1; // + 1 adaptive bias feature
	private const float StepSize = 0.25f;
	private const float Epsilon = 1e-3f;
	private const int HistorySize = 64;
	private const int HistoryMask = HistorySize - 1;
	private const int SamplesPerFrame = 6;

	private readonly float[] _history = new float[ HistorySize ];
	private readonly float[] _angleHistory = new float[ HistorySize ];
	private long _sampleCount;

	private readonly float[][] _weights;
	private readonly float[] _features = new float[ FeatureCount ];
	private readonly float[] _predicted = new float[ SamplesPerFrame ];
	private bool _predictionsValid;

	private readonly int _horizonTicks;
	private readonly float _strength;
	private readonly float _correctionLimit;

	public ShippedPredictionModule( int horizonTicks, float strength, float correctionLimit )
	{
		_horizonTicks = horizonTicks;
		_strength = strength;
		_correctionLimit = correctionLimit;

		_weights = new float[ SamplesPerFrame ][];

		for ( var i = 0; i < _weights.Length; i++ )
		{
			_weights[ i ] = new float[ FeatureCount ];

			_weights[ i ][ 0 ] = 1f;
		}
	}

	public float Process( float[] torqueFrame, int sampleIndex, float inputA, float wheelPosition, float maxForce )
	{
		var horizonTicks = _horizonTicks;

		if ( horizonTicks <= 0 )
		{
			return inputA;
		}

		if ( sampleIndex == 0 )
		{
			IngestFrameAndPredict( torqueFrame, horizonTicks, wheelPosition, maxForce );
		}

		var currentTorque = torqueFrame[ sampleIndex ];

		var strength = _strength;

		var targetIndex = sampleIndex + horizonTicks;

		float delta;

		if ( targetIndex < SamplesPerFrame )
		{
			delta = ( torqueFrame[ targetIndex ] - currentTorque ) * MathF.Min( strength, 1f );
		}
		else if ( _predictionsValid )
		{
			var depthIndex = targetIndex - SamplesPerFrame - Math.Max( 0, horizonTicks - SamplesPerFrame );

			delta = ( _predicted[ depthIndex ] - currentTorque ) * strength;
		}
		else
		{
			delta = 0f;
		}

		return inputA + Math.Clamp( delta, -_correctionLimit, _correctionLimit );
	}

	private void IngestFrameAndPredict( float[] torqueFrame, int horizonTicks, float wheelPosition, float maxForce )
	{
		var angleTorqueScaled = wheelPosition * maxForce;

		for ( var i = 0; i < SamplesPerFrame; i++ )
		{
			var slot = (int) ( _sampleCount & HistoryMask );

			_history[ slot ] = torqueFrame[ i ];
			_angleHistory[ slot ] = angleTorqueScaled;

			_sampleCount++;
		}

		if ( _sampleCount < horizonTicks + TapCount + ( AngleTapCount * SamplesPerFrame ) )
		{
			return;
		}

		var newest = _sampleCount - 1;

		var newestTorque = _history[ (int) ( newest & HistoryMask ) ];

		var bias = maxForce;

		var minDepth = Math.Max( 1, horizonTicks - ( SamplesPerFrame - 1 ) );

		for ( var depth = minDepth; depth <= horizonTicks; depth++ )
		{
			var weights = _weights[ depth - minDepth ];

			BuildFeatures( newest - depth, bias );

			var dot = 0f;
			var norm = Epsilon;

			for ( var j = 0; j < FeatureCount; j++ )
			{
				var feature = _features[ j ];

				dot += weights[ j ] * feature;
				norm += feature * feature;
			}

			var scale = StepSize * ( newestTorque - dot ) / norm;

			for ( var j = 0; j < FeatureCount; j++ )
			{
				weights[ j ] += scale * _features[ j ];
			}

			BuildFeatures( newest, bias );

			var prediction = 0f;

			for ( var j = 0; j < FeatureCount; j++ )
			{
				prediction += weights[ j ] * _features[ j ];
			}

			_predicted[ depth - minDepth ] = prediction;
		}

		_predictionsValid = true;
	}

	private void BuildFeatures( long anchor, float bias )
	{
		for ( var j = 0; j < TapCount; j++ )
		{
			_features[ j ] = _history[ (int) ( ( anchor - j ) & HistoryMask ) ];
		}

		for ( var j = 0; j < AngleTapCount; j++ )
		{
			_features[ TapCount + j ] = _angleHistory[ (int) ( ( anchor - ( j * SamplesPerFrame ) ) & HistoryMask ) ];
		}

		_features[ TapCount + AngleTapCount ] = bias;
	}
}

// ============================================================================================================
// Small-N RLS with forgetting — kept ONLY for the section-J instability demonstration.
// ============================================================================================================

sealed class RlsN
{
	private readonly int _featureCount;
	private readonly float[] _w;
	private readonly float[,] _p;
	private readonly float[] _px;

	private const float Lambda = 0.999f;

	public RlsN( int featureCount )
	{
		_featureCount = featureCount;

		_w = new float[ featureCount ];
		_w[ 0 ] = 1f;

		_p = new float[ featureCount, featureCount ];

		for ( var i = 0; i < featureCount; i++ )
		{
			_p[ i, i ] = 100f;
		}

		_px = new float[ featureCount ];
	}

	public float Predict( float[] x )
	{
		var s = 0f;

		for ( var i = 0; i < _featureCount; i++ )
		{
			s += _w[ i ] * x[ i ];
		}

		return s;
	}

	public void Update( float[] x, float yTrue )
	{
		for ( var i = 0; i < _featureCount; i++ )
		{
			var s = 0f;

			for ( var j = 0; j < _featureCount; j++ )
			{
				s += _p[ i, j ] * x[ j ];
			}

			_px[ i ] = s;
		}

		var denom = Lambda;

		for ( var i = 0; i < _featureCount; i++ )
		{
			denom += x[ i ] * _px[ i ];
		}

		if ( denom < 1e-9f )
		{
			return;
		}

		var err = yTrue - Predict( x );
		var inv = 1f / denom;

		for ( var i = 0; i < _featureCount; i++ )
		{
			_w[ i ] += _px[ i ] * inv * err;
		}

		for ( var i = 0; i < _featureCount; i++ )
		{
			for ( var j = 0; j < _featureCount; j++ )
			{
				_p[ i, j ] = ( _p[ i, j ] - _px[ i ] * inv * _px[ j ] ) / Lambda;
			}
		}
	}
}

// ============================================================================================================
// The PRE-2026-07 Prediction module's RLS — verbatim port of the deleted RlsWheelVelocityPredictor.
// Kept as the historical baseline (section B).
// ============================================================================================================

sealed class OldRls3
{
	private float _w0, _w1 = 1f, _w2;
	private float _p00 = 1000f, _p01, _p02, _p10, _p11 = 1000f, _p12, _p20, _p21, _p22 = 1000f;

	private readonly int _horizon;
	private readonly float _lambda;

	private readonly (float X0, float X1, float X2)[] _pending;
	private int _pendingHead, _pendingCount;

	public OldRls3( int horizon, float forgettingFactor = 0.9995f )
	{
		_horizon = Math.Max( 1, horizon );
		_lambda = Math.Clamp( forgettingFactor, 0.95f, 0.999999f );
		_pending = new (float, float, float)[ Math.Max( 8, _horizon + 2 ) ];
	}

	public float Step( float yNow, float wheelVelocityNow )
	{
		const float x0 = 1f;

		var x1 = yNow;
		var x2 = wheelVelocityNow;

		var yHat = _w0 * x0 + _w1 * x1 + _w2 * x2;

		var writeIndex = ( _pendingHead + _pendingCount ) % _pending.Length;

		if ( _pendingCount == _pending.Length )
		{
			_pendingHead = ( _pendingHead + 1 ) % _pending.Length;

			_pendingCount--;
		}

		_pending[ writeIndex ] = ( x0, x1, x2 );

		_pendingCount++;

		if ( _pendingCount > _horizon )
		{
			var item = _pending[ _pendingHead ];

			_pendingHead = ( _pendingHead + 1 ) % _pending.Length;

			_pendingCount--;

			Update( item.X0, item.X1, item.X2, yNow );
		}

		return yHat;
	}

	private void Update( float x0, float x1, float x2, float yTrue )
	{
		var px0 = _p00 * x0 + _p01 * x1 + _p02 * x2;
		var px1 = _p10 * x0 + _p11 * x1 + _p12 * x2;
		var px2 = _p20 * x0 + _p21 * x1 + _p22 * x2;

		var denom = _lambda + ( x0 * px0 + x1 * px1 + x2 * px2 );

		if ( denom < 1e-9f )
		{
			return;
		}

		var invDenom = 1f / denom;

		var k0 = px0 * invDenom;
		var k1 = px1 * invDenom;
		var k2 = px2 * invDenom;

		var yHat = _w0 * x0 + _w1 * x1 + _w2 * x2;
		var err = yTrue - yHat;

		_w0 += k0 * err;
		_w1 += k1 * err;
		_w2 += k2 * err;

		var xtp0 = x0 * _p00 + x1 * _p10 + x2 * _p20;
		var xtp1 = x0 * _p01 + x1 * _p11 + x2 * _p21;
		var xtp2 = x0 * _p02 + x1 * _p12 + x2 * _p22;

		_p00 = ( _p00 - k0 * xtp0 ) / _lambda; _p01 = ( _p01 - k0 * xtp1 ) / _lambda; _p02 = ( _p02 - k0 * xtp2 ) / _lambda;
		_p10 = ( _p10 - k1 * xtp0 ) / _lambda; _p11 = ( _p11 - k1 * xtp1 ) / _lambda; _p12 = ( _p12 - k1 * xtp2 ) / _lambda;
		_p20 = ( _p20 - k2 * xtp0 ) / _lambda; _p21 = ( _p21 - k2 * xtp1 ) / _lambda; _p22 = ( _p22 - k2 * xtp2 ) / _lambda;
	}
}
