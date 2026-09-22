
using System.Diagnostics;
using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using MarvinsAIRARefactored.FFB;

using static MarvinsAIRARefactored.Windows.MainWindow;

namespace MarvinsAIRARefactored.Components;

// Accessibility owns the vJoy steering passthrough - MAIRA reads the real wheelbase's steering axis and passes
// it through to the vJoy device's X axis, and the game (iRacing, or a game bridge game) uses the vJoy X axis
// as its steering input instead of the real wheel. The passthrough is shared by the game bridge page (where it
// lets MAIRA take over the force feedback of games that cannot turn theirs off) and the accessibility page,
// where it can also REMAP the steering to help drivers with limited arm movement: separate left / right
// ranges and curves, a center offset and deadzone, tremor smoothing and speed-sensitive steering.
//
// While the remap is active the game's steering angle no longer matches the physical wheel, so the racing
// wheel's FFB graph is fed the PHYSICAL wheel state from here instead (soft lock at the remapped physical
// limits, damping / friction from the real wheel motion), and the output torque is scaled per turn direction
// plus an optional centering spring.
//
// Angles in this component are counterclockwise-positive (turned LEFT = positive) like iRacing's steering
// telemetry, while the DirectInput / vJoy axis positions are the opposite (left = -1) - see CLAUDE.md.
public class Accessibility
{
	private const int UpdateInterval = 3;

	private const float SteeringSweepPeriodSeconds = 2f;

	// the playout thread samples the wheel at 360 Hz while it runs - if it has not sampled for this long (no sim
	// connected, so the playout timer is suspended) the ~60 Hz app tick takes over the passthrough
	private const double PlayoutSampleTimeoutSeconds = 0.05;

	// tremor smoothing (one euro filter - heavy smoothing while the wheel is held still, little lag in a turn)
	private const float TremorMaximumCutoffHz = 20f;
	private const float TremorMinimumCutoffRatio = 0.02f;
	private const float TremorBeta = 0.05f;
	private const float TremorDerivativeCutoffHz = 1f;

	// low-pass on the physical wheel velocity handed to the FFB graph (the raw difference of 360 Hz samples of a
	// 16-bit axis is noisy)
	private const float PhysicalVelocityCutoffHz = 30f;

	// the left / right FFB strengths blend across this band around center (fraction of the steering range) so
	// there is no torque step when the wheel crosses straight ahead
	private const float StrengthBlendHalfWidth = 0.05f;

	// how long the centering help takes to fade in / out, and how much velocity damping rides along with it
	private const float CenteringHelpFadeSeconds = 1f;
	private const float CenteringHelpDamping = 0.1f;

	private enum SteeringMode
	{
		Off,            // passthrough not running
		Wheel,          // the real wheel drives the vJoy axis (remapped or straight through)
		Calibration,    // the calibration buttons / sweep drive the vJoy axis, the real wheel is ignored
		Paused          // the steering effects calibration robot drives the vJoy axis
	}

	private volatile SteeringMode _steeringMode = SteeringMode.Off;

	private bool _passthroughActive = false;
	private int _passthroughShutdownCountdown = 0;

	private volatile bool _remapActive = false;

	private long _lastPlayoutSampleTimestamp = 0;
	private long _lastFallbackSampleTimestamp = 0;

	private readonly Lock _processLock = new();

	// the steering sweep continuously oscillates the vJoy axis (one full left-right-left cycle every two seconds) -
	// games like AC ignore controller input while their window is not in the foreground, so the user arms the sweep
	// in MAIRA, clicks over to the game, and the game then sees the axis moving
	public bool SteeringSweepActive { get; set; } = false;

	private float _steeringSweepPhase = 0f;

	// tremor filter state
	private bool _tremorFilterPrimed = false;
	private float _tremorFilteredAngle = 0f;
	private float _tremorFilteredDerivative = 0f;
	private float _tremorPreviousAngle = 0f;

	// physical wheel state (relative to the center offset) published for the FFB - written on the sampling thread,
	// read on the telemetry and playout threads (single floats, so never torn)
	private bool _physicalStatePrimed = false;
	private float _physicalPreviousAngleDegrees = 0f;
	private volatile float _physicalAngleDegrees = 0f;
	private volatile float _physicalVelocityDegreesPerSecond = 0f;
	private volatile float _physicalLeftRangeDegrees = 90f;
	private volatile float _physicalRightRangeDegrees = 90f;

	// centering help fade (playout thread only)
	private float _centeringHelpBlend = 0f;

	// last values for the page's live readout
	private volatile float _lastInputAngleDegrees = 0f;
	private volatile float _lastLockFraction = 0f;
	private volatile float _lastSpeedFactor = 1f;

	private int _updateCounter = UpdateInterval;

	/// <summary>True while the steering remap is running - the racing wheel then uses the physical wheel state from here.</summary>
	public bool RemapActive => _remapActive;

	/// <summary>True while the vJoy passthrough (or calibration mode) owns the vJoy steering axis.</summary>
	public bool PassthroughActive => _passthroughActive;

	/// <summary>
	/// The steering position the game actually sees (vJoy axis scale, -1..+1, left = -1) - the vJoy axis while the
	/// passthrough runs, otherwise the physical wheel. The game bridges calibrate the game's reported steering
	/// angle against this.
	/// </summary>
	public float GameSteeringPosition
	{
		get
		{
			var app = App.Instance!;

			if ( _passthroughActive && app.VirtualJoystick.Initialized )
			{
				return app.VirtualJoystick.Steering;
			}

			return app.DirectInput.ForceFeedbackWheelPosition;
		}
	}

	public float LastInputAngleDegrees => _lastInputAngleDegrees;
	public float LastLockFraction => _lastLockFraction;
	public float LastSpeedFactor => _lastSpeedFactor;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Accessibility] Initialize >>>" );

		_accessibilityPage.Update();

		app.Logger.WriteLine( "[Accessibility] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Accessibility] Shutdown >>>" );

		_steeringMode = SteeringMode.Off;
		_remapActive = false;

		app.Logger.WriteLine( "[Accessibility] <<< Shutdown" );
	}

	#region Remap

	/// <summary>The remap settings for one sample (angles in degrees).</summary>
	public readonly struct RemapParameters
	{
		public readonly float WheelbaseHalfRangeDegrees;
		public readonly float CarHalfLockDegrees;
		public readonly float CenterOffsetDegrees;
		public readonly float DeadzoneDegrees;
		public readonly float LeftRangeDegrees;
		public readonly float RightRangeDegrees;
		public readonly float LeftPower;
		public readonly float RightPower;
		public readonly float SpeedFactor;

		public RemapParameters( DataContext.Settings settings, float carHalfLockDegrees, float speedFactor )
		{
			WheelbaseHalfRangeDegrees = settings.AccessibilityWheelbaseRange * 0.5f;
			CarHalfLockDegrees = ( carHalfLockDegrees > 0f ) ? carHalfLockDegrees : WheelbaseHalfRangeDegrees;
			CenterOffsetDegrees = settings.AccessibilityCenterOffset;
			DeadzoneDegrees = settings.AccessibilityCenterDeadzone;
			LeftRangeDegrees = settings.AccessibilityLeftRange;
			RightRangeDegrees = settings.AccessibilityRightRange;
			LeftPower = MathZ.CurveToPower( settings.AccessibilityLeftCurve );
			RightPower = MathZ.CurveToPower( settings.AccessibilityRightCurve );
			SpeedFactor = speedFactor;
		}
	}

	/// <summary>
	/// The remap itself - takes the physical wheel angle (degrees, left positive) and returns the steering as a
	/// fraction of the car's steering lock (-1..+1, left positive): center offset, then deadzone, then the side's
	/// range scaled to full lock, then the side's power curve, then the speed-sensitive reduction.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float RemapToLockFraction( float angleDegrees, in RemapParameters remapParameters )
	{
		var centeredAngleDegrees = angleDegrees - remapParameters.CenterOffsetDegrees;

		var isTurnedLeft = centeredAngleDegrees >= 0f;

		var sideRangeDegrees = isTurnedLeft ? remapParameters.LeftRangeDegrees : remapParameters.RightRangeDegrees;

		// the deadzone can never swallow the whole range
		var deadzoneDegrees = MathF.Min( remapParameters.DeadzoneDegrees, sideRangeDegrees * 0.9f );

		var normalizedAngle = MathZ.Saturate( ( MathF.Abs( centeredAngleDegrees ) - deadzoneDegrees ) / ( sideRangeDegrees - deadzoneDegrees ) );

		var curvedAngle = MathF.Pow( normalizedAngle, isTurnedLeft ? remapParameters.LeftPower : remapParameters.RightPower );

		var lockFraction = curvedAngle * remapParameters.SpeedFactor;

		return isTurnedLeft ? lockFraction : -lockFraction;
	}

	/// <summary>The speed-sensitive steering factor (1 = full sensitivity) at the given speed in meters per second.</summary>
	public static float GetSpeedFactor( DataContext.Settings settings, float speedMetersPerSecond )
	{
		var speedBlend = MathZ.Saturate( speedMetersPerSecond / settings.AccessibilityFullEffectSpeed );

		return MathZ.Lerp( 1f, settings.AccessibilityHighSpeedSensitivity, speedBlend );
	}

	/// <summary>The car's half steering lock in degrees (0 when not known - no car loaded).</summary>
	public static float GetCarHalfLockDegrees()
	{
		return App.Instance!.Simulator.SteeringWheelAngleMax * 0.5f * MathZ.RadiansToDegrees;
	}

	// processes one sample of the real wheel (DirectInput axis scale, left = -1) and returns the vJoy axis position
	// to send to the game - callers hold _processLock
	private float ProcessWheelSample( float wheelPosition, float deltaSeconds )
	{
		var app = App.Instance!;

		var settings = DataContext.DataContext.Instance.Settings;

		var wheelbaseHalfRangeDegrees = settings.AccessibilityWheelbaseRange * 0.5f;

		var angleDegrees = -wheelPosition * wheelbaseHalfRangeDegrees;

		// physical wheel state for the FFB (relative to the center offset, never smoothed - the FFB acts on the
		// real wheel)

		var centeredAngleDegrees = angleDegrees - settings.AccessibilityCenterOffset;

		if ( !_physicalStatePrimed )
		{
			_physicalStatePrimed = true;
			_physicalPreviousAngleDegrees = centeredAngleDegrees;
			_physicalVelocityDegreesPerSecond = 0f;
		}

		var instantVelocityDegreesPerSecond = ( centeredAngleDegrees - _physicalPreviousAngleDegrees ) / deltaSeconds;

		_physicalPreviousAngleDegrees = centeredAngleDegrees;

		_physicalVelocityDegreesPerSecond += LowPassAlpha( PhysicalVelocityCutoffHz, deltaSeconds ) * ( instantVelocityDegreesPerSecond - _physicalVelocityDegreesPerSecond );
		_physicalAngleDegrees = centeredAngleDegrees;
		_physicalLeftRangeDegrees = settings.AccessibilityLeftRange;
		_physicalRightRangeDegrees = settings.AccessibilityRightRange;

		var carHalfLockDegrees = GetCarHalfLockDegrees();

		var speedFactor = GetSpeedFactor( settings, app.Simulator.Speed );

		_lastSpeedFactor = speedFactor;

		if ( !settings.AccessibilityRemapEnabled )
		{
			// master switch off - the real wheel goes straight through, untouched

			_tremorFilterPrimed = false;

			_lastInputAngleDegrees = angleDegrees;
			_lastLockFraction = angleDegrees / ( ( carHalfLockDegrees > 0f ) ? carHalfLockDegrees : wheelbaseHalfRangeDegrees );

			return wheelPosition;
		}

		var smoothedAngleDegrees = ApplyTremorSmoothing( angleDegrees, settings.AccessibilityTremorSmoothing, deltaSeconds );

		var remapParameters = new RemapParameters( settings, carHalfLockDegrees, speedFactor );

		var lockFraction = RemapToLockFraction( smoothedAngleDegrees, in remapParameters );

		_lastInputAngleDegrees = smoothedAngleDegrees;
		_lastLockFraction = lockFraction;

		// the game was calibrated with the vJoy axis spanning the wheelbase's rotation range, so a steering angle
		// of the car's lock sits at (car half lock / wheelbase half range) on the axis

		var steeringAngleDegrees = lockFraction * remapParameters.CarHalfLockDegrees;

		return Math.Clamp( -steeringAngleDegrees / wheelbaseHalfRangeDegrees, -1f, 1f );
	}

	private float ApplyTremorSmoothing( float angleDegrees, float tremorSmoothing, float deltaSeconds )
	{
		if ( tremorSmoothing <= 0f )
		{
			_tremorFilterPrimed = false;

			return angleDegrees;
		}

		if ( !_tremorFilterPrimed )
		{
			_tremorFilterPrimed = true;
			_tremorFilteredAngle = angleDegrees;
			_tremorFilteredDerivative = 0f;
			_tremorPreviousAngle = angleDegrees;

			return angleDegrees;
		}

		// one euro filter - the cutoff rises with the (heavily smoothed) turning speed, so a held wheel is smoothed
		// hard while a deliberate turn passes through with little lag; the 1 Hz derivative filter averages a
		// back-and-forth tremor out, so the tremor itself does not open the filter up

		var derivative = ( angleDegrees - _tremorPreviousAngle ) / deltaSeconds;

		_tremorPreviousAngle = angleDegrees;

		_tremorFilteredDerivative += LowPassAlpha( TremorDerivativeCutoffHz, deltaSeconds ) * ( derivative - _tremorFilteredDerivative );

		var minimumCutoffHz = TremorMaximumCutoffHz * MathF.Pow( TremorMinimumCutoffRatio, tremorSmoothing );

		var cutoffHz = minimumCutoffHz + TremorBeta * MathF.Abs( _tremorFilteredDerivative );

		_tremorFilteredAngle += LowPassAlpha( cutoffHz, deltaSeconds ) * ( angleDegrees - _tremorFilteredAngle );

		return _tremorFilteredAngle;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static float LowPassAlpha( float cutoffHz, float deltaSeconds )
	{
		var timeConstant = 1f / ( MathF.Tau * cutoffHz );

		return deltaSeconds / ( deltaSeconds + timeConstant );
	}

	#endregion

	#region Force feedback

	/// <summary>
	/// The physical wheel state in the units of iRacing's steering telemetry (radians, left positive), for the
	/// racing wheel's FFB graph while the remap is active - the angle is relative to the center offset, and the
	/// "max" angle is the lock-to-lock span of the side the wheel is turned to, so the soft lock lands at the
	/// remapped physical limit of each side.
	/// </summary>
	public void GetPhysicalSteeringState( out float angleRadians, out float angleMaxRadians, out float velocityRadiansPerSecond )
	{
		var angleDegrees = _physicalAngleDegrees;

		var sideRangeDegrees = ( angleDegrees >= 0f ) ? _physicalLeftRangeDegrees : _physicalRightRangeDegrees;

		angleRadians = angleDegrees * MathZ.DegreesToRadians;
		angleMaxRadians = sideRangeDegrees * 2f * MathZ.DegreesToRadians;
		velocityRadiansPerSecond = _physicalVelocityDegreesPerSecond * MathZ.DegreesToRadians;
	}

	// physical wheel position normalized to its side's range (-1..+1 at the remapped limits, left positive)
	private float GetPhysicalNormalizedPosition( out float sideRangeDegrees )
	{
		var angleDegrees = _physicalAngleDegrees;

		sideRangeDegrees = MathF.Max( 1f, ( angleDegrees >= 0f ) ? _physicalLeftRangeDegrees : _physicalRightRangeDegrees );

		return angleDegrees / sideRangeDegrees;
	}

	/// <summary>
	/// The multiplier for the FFB output torque - the left strength while the wheel is turned left, the right
	/// strength while it is turned right, blended across center. 1 while the remap is not active.
	/// </summary>
	public float GetForceFeedbackStrengthScale()
	{
		if ( !_remapActive )
		{
			return 1f;
		}

		var settings = DataContext.DataContext.Instance.Settings;

		var normalizedPosition = GetPhysicalNormalizedPosition( out _ );

		var leftBlend = MathZ.Smoothstep( -StrengthBlendHalfWidth, StrengthBlendHalfWidth, normalizedPosition );

		return MathZ.Lerp( settings.AccessibilityRightFFBStrength, settings.AccessibilityLeftFFBStrength, leftBlend );
	}

	/// <summary>
	/// The centering help torque to add to the FFB output (normalized output units, left positive) - a spring
	/// toward the (offset) center that reaches its full strength at each side's remapped limit, with a little
	/// damping. Fades in and out over a second. Called from the 360 Hz playout thread.
	/// </summary>
	public float GetCenteringHelpTorque( float deltaMilliseconds )
	{
		var app = App.Instance!;

		var settings = DataContext.DataContext.Instance.Settings;

		var strength = settings.AccessibilityCenteringHelp;

		var targetBlend = ( _remapActive && ( strength > 0f ) && app.Simulator.IsOnTrack ) ? 1f : 0f;

		var blendStep = deltaMilliseconds / ( CenteringHelpFadeSeconds * 1000f );

		_centeringHelpBlend = ( targetBlend > _centeringHelpBlend ) ? MathF.Min( targetBlend, _centeringHelpBlend + blendStep ) : MathF.Max( targetBlend, _centeringHelpBlend - blendStep );

		if ( _centeringHelpBlend <= 0f )
		{
			return 0f;
		}

		var normalizedPosition = GetPhysicalNormalizedPosition( out var sideRangeDegrees );

		var normalizedVelocity = _physicalVelocityDegreesPerSecond / sideRangeDegrees;

		// a restoring force - left positive position / velocity needs a rightward (negative) torque
		var torque = -( Math.Clamp( normalizedPosition, -1f, 1f ) + normalizedVelocity * CenteringHelpDamping );

		return Math.Clamp( torque, -1.5f, 1.5f ) * strength * _centeringHelpBlend;
	}

	#endregion

	#region Calibration mode

	/// <summary>The vJoy axis position for a wheel angle in degrees (left positive) on the configured wheelbase.</summary>
	public static float GetAxisPositionForAngle( float angleDegrees )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		return Math.Clamp( -angleDegrees / ( settings.AccessibilityWheelbaseRange * 0.5f ), -1f, 1f );
	}

	/// <summary>Moves the vJoy steering axis to a fixed position (calibration mode buttons) - cancels a running sweep.</summary>
	public void SetCalibrationSteering( float axisPosition )
	{
		var app = App.Instance!;

		SteeringSweepActive = false;

		app.VirtualJoystick.Steering = axisPosition;

		UpdatePages( app );
	}

	public void ToggleSteeringSweep()
	{
		var app = App.Instance!;

		SteeringSweepActive = !SteeringSweepActive;

		if ( !SteeringSweepActive )
		{
			app.VirtualJoystick.Steering = 0f;
		}

		UpdatePages( app );
	}

	private void UpdatePages( App app )
	{
		_gameBridgePage.UpdateSweepButton( app );
		_accessibilityPage.UpdateSweepButton( app );
	}

	#endregion

	#region Passthrough

	// called from the playout timer worker thread at 360 Hz (before the game bridge pump and the racing wheel
	// update) - samples the real wheel and sends it (remapped) straight to vJoy, so the steering reaches the game
	// within one playout tick instead of waiting for the ~60 Hz app tick
	public void UpdatePlayout()
	{
		if ( _steeringMode != SteeringMode.Wheel )
		{
			return;
		}

		var app = App.Instance!;

		if ( !app.DirectInput.TrySampleSteeringWheelPosition( out var wheelPosition ) )
		{
			return;
		}

		float axisPosition;

		lock ( _processLock )
		{
			axisPosition = ProcessWheelSample( wheelPosition, FFBTickContext.TickDeltaMilliseconds / 1000f );
		}

		app.VirtualJoystick.UpdateSteeringImmediately( axisPosition );

		Volatile.Write( ref _lastPlayoutSampleTimestamp, Stopwatch.GetTimestamp() );
	}

	public void Tick( App app )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		UpdatePassthrough( app, settings );

		_updateCounter--;

		if ( _updateCounter <= 0 )
		{
			_updateCounter = UpdateInterval;

			if ( MairaAppMenuPopup.CurrentAppPage == AppPage.Accessibility )
			{
				_accessibilityPage.UpdateLiveState( app );
			}
		}
	}

	// This tick runs BEFORE VirtualJoystick.Tick in the app worker loop, so a position written here goes out to
	// vJoy in the same frame.
	private void UpdatePassthrough( App app, DataContext.Settings settings )
	{
		if ( settings.GameBridgeSendSteeringToVJoy || settings.GameBridgeSteeringTestEnabled )
		{
			if ( !app.VirtualJoystick.Initialized && !app.VirtualJoystick.Faulted )
			{
				app.VirtualJoystick.Initialize();
			}

			if ( settings.GameBridgeSteeringTestEnabled )
			{
				// calibration mode - the vJoy axis is driven by the calibration buttons instead of the real wheel, so
				// the user can move ONLY the vJoy axis while calibrating / binding steering in the game, and the game
				// cannot mistake the real wheel for the moving axis

				_steeringMode = SteeringMode.Calibration;

				if ( SteeringSweepActive )
				{
					_steeringSweepPhase += MathF.Tau / ( SteeringSweepPeriodSeconds * App.TimerTicksPerSecond );

					app.VirtualJoystick.Steering = MathF.Sin( _steeringSweepPhase );
				}
				else
				{
					_steeringSweepPhase = 0f;
				}
			}
			else if ( app.SteeringEffects.IsCalibrating )
			{
				// the steering effects calibration robot drives the vJoy axis - stay out of its way

				SteeringSweepActive = false;

				_steeringMode = SteeringMode.Paused;
			}
			else
			{
				SteeringSweepActive = false;

				_steeringMode = SteeringMode.Wheel;

				// fall back to the ~60 Hz device poll while the playout timer is not sampling (no sim connected)

				var playoutSampleElapsedSeconds = Stopwatch.GetElapsedTime( Volatile.Read( ref _lastPlayoutSampleTimestamp ) ).TotalSeconds;

				if ( playoutSampleElapsedSeconds > PlayoutSampleTimeoutSeconds )
				{
					var timestamp = Stopwatch.GetTimestamp();

					var deltaSeconds = ( _lastFallbackSampleTimestamp == 0 ) ? ( 1f / App.TimerTicksPerSecond ) : (float) Stopwatch.GetElapsedTime( _lastFallbackSampleTimestamp, timestamp ).TotalSeconds;

					_lastFallbackSampleTimestamp = timestamp;

					deltaSeconds = Math.Clamp( deltaSeconds, 0.001f, 0.1f );

					lock ( _processLock )
					{
						app.VirtualJoystick.Steering = ProcessWheelSample( app.DirectInput.ForceFeedbackWheelPosition, deltaSeconds );
					}
				}
				else
				{
					_lastFallbackSampleTimestamp = 0;
				}
			}

			if ( !_passthroughActive && app.VirtualJoystick.Initialized )
			{
				app.Logger.WriteLine( "[Accessibility] vJoy steering passthrough started" );
			}

			_passthroughActive = app.VirtualJoystick.Initialized;
			_passthroughShutdownCountdown = 2;
		}
		else
		{
			_steeringMode = SteeringMode.Off;

			if ( _passthroughActive )
			{
				// on the way out, center the axis first and give VirtualJoystick.Tick one frame to actually push the
				// zero to the device before it is released

				app.VirtualJoystick.Steering = 0f;

				_passthroughShutdownCountdown--;

				if ( _passthroughShutdownCountdown <= 0 )
				{
					_passthroughActive = false;

					SteeringSweepActive = false;

					app.Logger.WriteLine( "[Accessibility] vJoy steering passthrough stopped" );

					if ( app.VirtualJoystick.Initialized )
					{
						app.VirtualJoystick.Shutdown();
					}
				}
			}
		}

		if ( _steeringMode != SteeringMode.Wheel )
		{
			lock ( _processLock )
			{
				_tremorFilterPrimed = false;
				_physicalStatePrimed = false;
			}
		}

		_remapActive = ( _steeringMode == SteeringMode.Wheel ) && _passthroughActive && settings.AccessibilityRemapEnabled;
	}

	#endregion
}
