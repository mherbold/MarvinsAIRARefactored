
using Simagic;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

public class Pedals
{
	public enum Effect
	{
		None,
		GearChange,
		ABSEngaged,
		RPM,
		UndersteerEffect,
		OversteerEffect,
		SeatOfPantsEffect,
		WheelLock,
		WheelSpin,
		ClutchSlip
	};

	private const float DeltaSeconds = 1f / 20f;
	private const float TestDuration = 3f;
	private const int UpdateInterval = 3;

	public HPR.PedalsDevice PedalsDevice { get; private set; }

	private int _updateCounter = UpdateInterval + 3;

	private bool _testing = false;
	private bool _testJustStarted = false;
	private int _testPedalIndex = 0;
	private int _testEffectIndex = 0;
	private float _testTimer = 0f;

	private readonly HPR _hpr = new();

	private int _gearLastFrame = 0;

	private float _gearChangeFrequency = 0f;
	private float _gearChangeAmplitude = 0f;
	private float _gearChangeTimer = 0f;

	private readonly float[] _frequency = new float[ 3 ];
	private readonly float[] _amplitude = new float[ 3 ];
	private readonly float[] _cycles = new float[ 3 ];

	public float ClutchFrequency { get => _frequency[ (int) HPR.Channel.Clutch ]; }
	public float ClutchAmplitude { get => _amplitude[ (int) HPR.Channel.Clutch ]; }

	public float BrakeFrequency { get => _frequency[ (int) HPR.Channel.Brake ]; }
	public float BrakeAmplitude { get => _amplitude[ (int) HPR.Channel.Brake ]; }

	public float ThrottleFrequency { get => _frequency[ (int) HPR.Channel.Throttle ]; }
	public float ThrottleAmplitude { get => _amplitude[ (int) HPR.Channel.Throttle ]; }

	public void Initialize()
	{
		var app = App.Instance!;

		app.Graph.SetLayerColors( Graph.LayerIndex.ClutchPedalHaptics, 0f, 0f, 0.5f, 0f, 0f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.BrakePedalHaptics, 0.5f, 0f, 0f, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.ThrottlePedalHaptics, 0f, 0.5f, 0f, 0f, 1f, 0f );
	}

	public void Refresh()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[Pedals] Refresh >>>" );

		PedalsDevice = _hpr.Initialize( DataContext.DataContext.Instance.Settings.PedalsEnabled, msg => app.Logger.WriteLine( $"[Simagic] {msg}" ) );

		app.Logger.WriteLine( $"[Pedals] Simagic HPR API reports: {PedalsDevice}" );

		app.MainWindow.UpdatePedalsDevice();

		app.Logger.WriteLine( "[Pedals] <<< Refresh" );
	}

	public void StartTest( int pedalIndex, int effectIndex )
	{
		_testPedalIndex = pedalIndex;
		_testEffectIndex = effectIndex;
		_testTimer = 0f;
		_testJustStarted = true;
		_testing = true;
	}

	public void UpdateGraph()
	{
		var app = App.Instance!;

		for ( var i = 0; i < 3; i++ )
		{
			_cycles[ i ] += _frequency[ i ] * MathF.Tau / 500f;

			var amplitude = MathF.Sin( _cycles[ i ] ) * _amplitude[ i ];

			app.Graph.UpdateLayer( Graph.LayerIndex.ClutchPedalHaptics + i, amplitude, amplitude );
		}
	}

	private void Update( App app )
	{
		// update gear change timer

		if ( _gearChangeTimer > 0f )
		{
			_gearChangeTimer -= DeltaSeconds;
		}

		// set gear last frame

		if ( !app.Simulator.WasOnTrack && app.Simulator.IsOnTrack )
		{
			_gearLastFrame = app.Simulator.Gear;
		}

		// update test timer

		if ( _testing )
		{
			_testTimer += DeltaSeconds;

			if ( _testTimer >= TestDuration )
			{
				_testing = false;
			}
		}

		// if we aren't on track then just shut off all HPRs (unless we are testing)

		if ( !_testing )
		{
			if ( !app.Simulator.IsOnTrack || ( app.Simulator.SimMode != "full" ) )
			{
				_hpr.VibratePedal( HPR.Channel.Clutch, HPR.State.Off, 0, 0 );
				_hpr.VibratePedal( HPR.Channel.Brake, HPR.State.Off, 0, 0 );
				_hpr.VibratePedal( HPR.Channel.Throttle, HPR.State.Off, 0, 0 );

				for ( var pedalIndex = 0; pedalIndex < 3; pedalIndex++ )
				{
					_frequency[ pedalIndex ] = 0f;
					_amplitude[ pedalIndex ] = 0f;
					_cycles[ pedalIndex ] = 0f;
				}

				return;
			}
		}

		// shortcut to settings

		var settings = DataContext.DataContext.Instance.Settings;

		if ( _testJustStarted || ( app.Simulator.Gear != _gearLastFrame ) )
		{
			if ( _testJustStarted || ( app.Simulator.Gear != 0 ) )
			{
				_gearChangeFrequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, settings.PedalsShiftIntoGearFrequency );
				_gearChangeAmplitude = settings.PedalsShiftIntoGearAmplitude;
				_gearChangeTimer = settings.PedalsShiftIntoGearDuration;
			}
			else
			{
				_gearChangeFrequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, settings.PedalsShiftIntoNeutralFrequency );
				_gearChangeAmplitude = settings.PedalsShiftIntoNeutralAmplitude;
				_gearChangeTimer = settings.PedalsShiftIntoNeutralDuration;
			}
		}

		_gearLastFrame = app.Simulator.Gear;

		// generate and apply effects

		(Effect, float)[,] effectConfiguration =
		{
			{
				( settings.PedalsClutchEffect1, settings.PedalsClutchStrength1 ),
				( settings.PedalsClutchEffect2, settings.PedalsClutchStrength2 ),
				( settings.PedalsClutchEffect3, settings.PedalsClutchStrength3 )
			},
			{
				( settings.PedalsBrakeEffect1, settings.PedalsBrakeStrength1 ),
				( settings.PedalsBrakeEffect2, settings.PedalsBrakeStrength2 ),
				( settings.PedalsBrakeEffect3, settings.PedalsBrakeStrength3 )
			},
			{
				( settings.PedalsThrottleEffect1, settings.PedalsThrottleStrength1 ),
				( settings.PedalsThrottleEffect2, settings.PedalsThrottleStrength2 ),
				( settings.PedalsThrottleEffect3, settings.PedalsThrottleStrength3 )
			}
		};

		for ( var pedalIndex = 0; pedalIndex < 3; pedalIndex++ )
		{
			if ( _testing && ( _testPedalIndex != pedalIndex ) )
			{
				continue;
			}

			var effectActive = false;
			var frequency = 0f;
			var amplitude = 0f;

			for ( var effectIndex = 0; effectIndex < 3; effectIndex++ )
			{
				if ( _testing && ( _testEffectIndex != effectIndex ) )
				{
					continue;
				}

				(effectActive, frequency, amplitude) = DoEffect( app, effectConfiguration[ pedalIndex, effectIndex ].Item1, effectConfiguration[ pedalIndex, effectIndex ].Item2 );

				if ( effectActive )
				{
					break;
				}
			}

			if ( effectActive )
			{
				_hpr.VibratePedal( (HPR.Channel) pedalIndex, HPR.State.On, frequency, amplitude * 100f );

				_frequency[ pedalIndex ] = (int) ( Math.Clamp( frequency, 1f, 50f ) );
				_amplitude[ pedalIndex ] = amplitude;
			}
			else
			{
				_hpr.VibratePedal( (HPR.Channel) pedalIndex, HPR.State.Off, 0f, 0f );

				app.Graph.UpdateLayer( Graph.LayerIndex.ClutchPedalHaptics + pedalIndex, 0f, 0f );

				_frequency[ pedalIndex ] = 0f;
				_amplitude[ pedalIndex ] = 0f;
				_cycles[ pedalIndex ] = 0f;
			}
		}

		// update test just started

		_testJustStarted = false;
	}

	private (bool, float, float) DoEffect( App app, Effect effect, float amplitude )
	{
		return effect switch
		{
			Effect.GearChange => DoGearChangeEffect( app, amplitude ),
			Effect.ABSEngaged => DoABSEngagedEffect( app, amplitude ),
			Effect.RPM => DoRPMEffect( app, amplitude ),
			Effect.UndersteerEffect => DoUndersteerEffect( app, amplitude ),
			Effect.OversteerEffect => DoOversteerEffect( app, amplitude ),
			Effect.SeatOfPantsEffect => DoSeatOfPantsEffect( app, amplitude ),
			Effect.WheelLock => DoWheelLockEffect( app, amplitude ),
			Effect.WheelSpin => DoWheelSpinEffect( app, amplitude ),
			Effect.ClutchSlip => DoClutchSlipEffect( app, amplitude ),
			_ => (false, 0f, 0f),
		};
	}

	private (bool, float, float) DoGearChangeEffect( App app, float amplitude )
	{
		if ( _gearChangeTimer > 0f )
		{
			return (true, _gearChangeFrequency, _gearChangeAmplitude * amplitude);
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoABSEngagedEffect( App app, float amplitude )
	{
		if ( _testing || app.Simulator.BrakeABSactive )
		{
			var settings = DataContext.DataContext.Instance.Settings;

			var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( settings.PedalsABSEngagedFrequency, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

			amplitude *= settings.PedalsABSEngagedAmplitude;

			if ( !_testing && settings.PedalsABSEngagedFadeWithBrakeEnabled )
			{
				amplitude *= app.Simulator.Brake;
			}

			amplitude = MathZ.Saturate( amplitude );
			amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );

			return (true, frequency, amplitude);
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoRPMEffect( App app, float amplitude )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( _testing || settings.PedalsRPMVibrateInTopGearEnabled || ( app.Simulator.Gear < app.Simulator.NumForwardGears ) )
		{
			var rpm = app.Simulator.RPM;

			if ( _testing )
			{
				rpm = _testTimer / TestDuration * app.Simulator.ShiftLightsShiftRPM;
			}

			var startingRPM = app.Simulator.ShiftLightsShiftRPM * settings.PedalsStartingRPM;
			var rpmRange = app.Simulator.ShiftLightsShiftRPM - startingRPM;

			if ( rpm >= startingRPM )
			{
				rpm = MathZ.Saturate( ( rpm - startingRPM ) / rpmRange );

				var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( rpm, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

				if ( !_testing && settings.PedalsRPMFadeWithThrottleEnabled )
				{
					amplitude *= app.Simulator.Throttle;
				}

				amplitude = MathZ.Saturate( amplitude );
				amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );
				amplitude *= MathF.Pow( frequency / 50f, MathZ.CurveToPower( settings.PedalsNoiseDamper ) );

				return (true, frequency, amplitude);
			}
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoUndersteerEffect( App app, float amplitude )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var factor = app.SteeringEffects.UndersteerEffect;

		if ( _testing || ( settings.SteeringEffectsUndersteerEnabled && ( factor > 0f ) ) )
		{
			if ( _testing )
			{
				factor = _testTimer / TestDuration;
			}

			factor = MathF.Pow( factor, MathZ.CurveToPower( settings.SteeringEffectsUndersteerPedalVibrationCurve ) );

			var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( factor, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

			amplitude = MathZ.Saturate( amplitude );
			amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );
			amplitude *= MathF.Pow( frequency / 50f, MathZ.CurveToPower( settings.PedalsNoiseDamper ) );

			return (true, frequency, amplitude);
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoOversteerEffect( App app, float amplitude )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var factor = app.SteeringEffects.OversteerEffect;

		if ( _testing || ( settings.SteeringEffectsOversteerEnabled && ( factor > 0f ) ) )
		{
			if ( _testing )
			{
				factor = _testTimer / TestDuration;
			}

			factor = MathF.Pow( factor, MathZ.CurveToPower( settings.SteeringEffectsOversteerPedalVibrationCurve ) );

			var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( factor, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

			amplitude = MathZ.Saturate( amplitude );
			amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );
			amplitude *= MathF.Pow( frequency / 50f, MathZ.CurveToPower( settings.PedalsNoiseDamper ) );

			return (true, frequency, amplitude);
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoSeatOfPantsEffect( App app, float amplitude )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		var factor = MathF.Abs( app.SteeringEffects.SeatOfPantsEffect );

		if ( _testing || ( settings.SteeringEffectsSeatOfPantsEnabled && ( factor > 0f ) ) )
		{
			if ( _testing )
			{
				factor = _testTimer / TestDuration;
			}

			factor = MathF.Pow( factor, MathZ.CurveToPower( settings.SteeringEffectsSeatOfPantsPedalVibrationCurve ) );

			var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( factor, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

			amplitude = MathZ.Saturate( amplitude );
			amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );
			amplitude *= MathF.Pow( frequency / 50f, MathZ.CurveToPower( settings.PedalsNoiseDamper ) );

			return (true, frequency, amplitude);
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoWheelLockEffect( App app, float amplitude )
	{
		if ( _testing || ( ( app.Simulator.CurrentRpmSpeedRatio > 0f ) && ( app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] > 0f ) ) )
		{
			var settings = DataContext.DataContext.Instance.Settings;

			var difference = app.Simulator.CurrentRpmSpeedRatio - app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ];
			var differencePct = ( difference / app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] ) - ( 1f - settings.PedalsWheelLockSensitivity );

			if ( _testing || ( differencePct > 0f ) )
			{
				var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( settings.PedalsWheelLockFrequency, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

				if ( !_testing )
				{
					amplitude = MathZ.Lerp( 0f, amplitude, differencePct / 0.03f );

					if ( settings.PedalsWheelLockFadeWithBrakeEnabled )
					{
						amplitude *= app.Simulator.Brake;
					}
				}

				amplitude = MathZ.Saturate( amplitude );
				amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );

				return (true, frequency, amplitude);
			}
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoWheelSpinEffect( App app, float amplitude )
	{
		if ( _testing || ( ( app.Simulator.CurrentRpmSpeedRatio > 0f ) && ( app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] > 0f ) ) )
		{
			var settings = DataContext.DataContext.Instance.Settings;

			var difference = app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] - app.Simulator.CurrentRpmSpeedRatio;
			var differencePct = ( difference / app.Simulator.RPMSpeedRatios[ app.Simulator.Gear ] ) - ( 1f - settings.PedalsWheelSpinSensitivity );

			if ( _testing || ( differencePct > 0f ) )
			{
				var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( settings.PedalsWheelSpinFrequency, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

				if ( !_testing )
				{
					amplitude = MathZ.Lerp( 0f, amplitude, differencePct / 0.03f );

					if ( settings.PedalsWheelSpinFadeWithThrottleEnabled )
					{
						amplitude *= app.Simulator.Throttle;
					}
				}

				amplitude = MathZ.Saturate( amplitude );
				amplitude = MathZ.Lerp( settings.PedalsMinimumAmplitude, settings.PedalsMaximumAmplitude, MathF.Pow( amplitude, MathZ.CurveToPower( settings.PedalsAmplitudeCurve ) ) );

				return (true, frequency, amplitude);
			}
		}

		return (false, 0f, 0f);
	}

	private (bool, float, float) DoClutchSlipEffect( App app, float amplitude )
	{
		var settings = DataContext.DataContext.Instance.Settings;

		if ( _testing || ( app.Simulator.Clutch >= settings.PedalsClutchSlipStart ) && ( app.Simulator.Clutch < settings.PedalsClutchSlipEnd ) )
		{
			var frequency = MathZ.Lerp( settings.PedalsMinimumFrequency, settings.PedalsMaximumFrequency, MathF.Pow( settings.PedalsClutchSlipFrequency, MathZ.CurveToPower( settings.PedalsFrequencyCurve ) ) );

			amplitude = MathZ.Saturate( amplitude );
			amplitude = MathF.Min( settings.PedalsMaximumAmplitude, MathF.Max( settings.PedalsMinimumAmplitude, amplitude ) );

			return (true, frequency, amplitude);
		}

		return (false, 0f, 0f);
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter == 0 )
		{
			_updateCounter = UpdateInterval;

			Update( app );
		}
	}
}
