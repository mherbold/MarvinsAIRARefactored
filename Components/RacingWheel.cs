
using System.Runtime.CompilerServices;

using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class RacingWheel
{
	public enum Algorithm
	{
		Native60Hz,
		Native360Hz,
		DetailBooster,
		DeltaLimiter,
		DetailBoosterOn60Hz,
		DeltaLimiterOn60Hz,
		SlewAndTotalCompression,
		MultiAdjustmentToolkit
	};

	public enum VibrationPattern
	{
		None,
		SineWave,
		SquareWave,
		TriangleWave,
		SawtoothWaveIn,
		SawtoothWaveOut
	};

	public enum ConstantForceDirection
	{
		None,
		DecreaseForce,
		IncreaseForce,
	};

	private const int UpdateInterval = 6;
	private const int MaxSteeringWheelTorque360HzIndex = Simulator.SamplesPerFrame360Hz + 1;

	private const float UnsuspendTimeMS = 1000f;
	private const float FadeInTimeMS = 2000f;
	private const float FadeOutTimeMS = 500f;
	private const float TestSignalTimeMS = 2000f;
	private const float CrashProtectionRecoveryTime = 1000f;

	private Guid? _currentRacingWheelGuid = null;

	private bool _isSuspended = true;
	private bool _usingSteeringWheelTorqueData = false;

	public Guid? NextRacingWheelGuid { private get; set; } = null;
	public bool SuspendForceFeedback { get; set; } = true; // true if simulator is disconnected or if FFB is enabled in the simulator
	public bool ResetForceFeedback { private get; set; } = false; // set to true manually (via reset button)
	public bool UseSteeringWheelTorqueData { private get; set; } = false; // false if simulator is disconnected or if driver is not on track
	public bool UpdateSteeringWheelTorqueBuffer { private get; set; } = false; // true when simulator has new torque data to be copied
	public bool ActivateCrashProtection { private get; set; } = false; // set to true to activate crash protection
	public bool ActivateCurbProtection { private get; set; } = false; // set to true to activate curb protection
	public bool PlayTestSignal { private get; set; } = false; // set to true manually (via test button)
	public bool ClearPeakTorque { private get; set; } = false; // set to clear peak torque
	public bool AutoSetMaxForce { private get; set; } = false; // set to auto-set the max force setting
	public bool UpdateAlgorithmPreview { private get; set; } = true; // set to update the algorithm preview

	private float _unsuspendTimerMS = 0f;
	private float _fadeTimerMS = 0f;
	private float _testSignalTimerMS = 0f;
	private float _crashProtectionTimerMS = 0f;
	private float _curbProtectionTimerMS = 0f;
	private float _understeerEffectTimerMS = 0f;
	private float _oversteerEffectTimerMS = 0f;

	private readonly float[] _steeringWheelTorque360Hz = new float[ Simulator.SamplesPerFrame360Hz + 2 ];

	private float _algorithmPropertyA = 0f; // last
	private float _algorithmPropertyB = 0f; // running
	private float _algorithmPropertyC = 0f;

	private float _outputTorque = 0f;
	private float _peakTorque = 0f;
	private float _autoTorque = 0f;

	private float _lastUnfadedOutputTorque = 0f;

	private float _elapsedMilliseconds = 0f;

	private readonly GraphBase _algorithmPreviewGraphBase = new();

	private bool _logiPlayLedsNotWorking = false;

	private int _updateCounter = UpdateInterval + 4;

	public float AutoTorque { get => _autoTorque; }
	public float OutputTorque { get => _outputTorque; }
	public bool CrashProtectionIsActive { get => _crashProtectionTimerMS > 0f; }
	public bool CurbProtectionIsActive { get => _curbProtectionTimerMS > 0f; }
	public bool FadingIsActive { get => _fadeTimerMS > 0f; }

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheel] Initialize >>>" );

		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque60Hz, 1f, 0f, 0f, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque, 1f, 0f, 1f, 1f, 0f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputLFE, 0.1f, 0.5f, 1f, 1f, 1f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.OutputTorque, 0f, 1f, 1f, 0f, 1f, 1f );

		_algorithmPreviewGraphBase.Initialize( MainWindow._racingWheelPage.AlgorithmPreview_Image );

		app.Logger.WriteLine( "[RacingWheel] <<< Initialize" );
	}

	public float GetCurrentAutoTorque()
	{
		return _autoTorque;
	}

	public static void SendChatMessage( string key, string? value = null )
	{
		var app = App.Instance!;

		if ( DataContext.DataContext.Instance.Settings.RacingWheelSendChatMessages && ( app.Simulator.UserName != string.Empty ) )
		{
			var playerName = app.Simulator.UserName;

			playerName = playerName.Replace( " ", "." );

			app.ChatQueue.SendMessage( $"/{playerName} [MAIRA] {DataContext.DataContext.Instance.Localization[ key ]}", value );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private float ProcessAlgorithm( float steeringWheelTorque60Hz, float steeringWheelTorque500Hz, float curbProtectionLerpFactor )
	{
		// shortcut to settings

		var settings = DataContext.DataContext.Instance.Settings;

		// apply algorithm

		var outputTorque = 0f;

		switch ( settings.RacingWheelAlgorithm )
		{
			case Algorithm.Native60Hz:
			{
				outputTorque = steeringWheelTorque60Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.Native360Hz:
			{
				outputTorque = steeringWheelTorque500Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DetailBooster:
			{
				var detailBoost = MathZ.Lerp( 1f + settings.RacingWheelDetailBoost, 1f, curbProtectionLerpFactor );

				_algorithmPropertyB = MathZ.Lerp( _algorithmPropertyB + ( steeringWheelTorque500Hz - _algorithmPropertyA ) * detailBoost, steeringWheelTorque500Hz, settings.RacingWheelDetailBoostBias );
				_algorithmPropertyA = steeringWheelTorque500Hz;

				outputTorque = _algorithmPropertyB / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DeltaLimiter:
			{
				var deltaLimit = MathZ.Lerp( settings.RacingWheelDeltaLimit / 500f, 0f, curbProtectionLerpFactor );

				var limitedDeltaSteeringWheelTorque500Hz = Math.Clamp( steeringWheelTorque500Hz - _algorithmPropertyA, -deltaLimit, deltaLimit );

				_algorithmPropertyB = MathZ.Lerp( _algorithmPropertyB + limitedDeltaSteeringWheelTorque500Hz, steeringWheelTorque500Hz, settings.RacingWheelDeltaLimiterBias );
				_algorithmPropertyA = steeringWheelTorque500Hz;

				outputTorque = _algorithmPropertyB / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DetailBoosterOn60Hz:
			{
				var detailBoost = MathZ.Lerp( 1f + settings.RacingWheelDetailBoost, 1f, curbProtectionLerpFactor );

				_algorithmPropertyB = MathZ.Lerp( _algorithmPropertyB + ( steeringWheelTorque500Hz - _algorithmPropertyA ) * detailBoost, steeringWheelTorque60Hz, settings.RacingWheelDetailBoostBias );
				_algorithmPropertyA = steeringWheelTorque500Hz;

				outputTorque = _algorithmPropertyB / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DeltaLimiterOn60Hz:
			{
				var deltaLimit = MathZ.Lerp( settings.RacingWheelDeltaLimit / 500f, 0f, curbProtectionLerpFactor );

				var limitedDeltaSteeringWheelTorque500Hz = Math.Clamp( steeringWheelTorque500Hz - _algorithmPropertyA, -deltaLimit, deltaLimit );

				_algorithmPropertyB = MathZ.Lerp( _algorithmPropertyB + limitedDeltaSteeringWheelTorque500Hz, steeringWheelTorque60Hz, settings.RacingWheelDeltaLimiterBias );
				_algorithmPropertyA = steeringWheelTorque500Hz;

				outputTorque = _algorithmPropertyB / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.SlewAndTotalCompression:
			{
				var normalizedRunningTorque = _algorithmPropertyB / settings.RacingWheelMaxForce;

				var normalizedDelta = ( steeringWheelTorque500Hz - _algorithmPropertyB ) / settings.RacingWheelMaxForce;
				var normalizedDeltaAbs = MathF.Abs( normalizedDelta );

				var deltaLimit = settings.RacingWheelSlewCompressionThreshold / 500f;

				float oneMinusSlewCompressionRate;

				if ( MathF.Sign( normalizedDelta ) == MathF.Sign( steeringWheelTorque500Hz ) )
				{
					oneMinusSlewCompressionRate = 1f - settings.RacingWheelSlewCompressionRate;
				}
				else
				{
					oneMinusSlewCompressionRate = MathF.Max( 0.75f, 1f - settings.RacingWheelSlewCompressionRate * 0.75f );
				}

				if ( normalizedDeltaAbs > deltaLimit )
				{
					normalizedRunningTorque += ( deltaLimit + ( ( normalizedDeltaAbs - deltaLimit ) * oneMinusSlewCompressionRate ) ) * MathF.Sign( normalizedDelta );
				}
				else
				{
					normalizedRunningTorque += normalizedDelta;
				}

				var compressionThreshold = settings.RacingWheelTotalCompressionThreshold;
				var compressionWidth = settings.RacingWheelTotalCompressionThreshold;

				var oneMinusTotalCompressionRate = 1f - settings.RacingWheelTotalCompressionRate;

				if ( settings.RacingWheelTotalCompressionRate != 0f )
				{
					normalizedRunningTorque = MathZ.Compression( normalizedRunningTorque, settings.RacingWheelTotalCompressionRate, compressionThreshold, compressionWidth, false );
				}

				if ( settings.RacingWheelEnableSoftLimiter )
				{
					normalizedRunningTorque = MathZ.SoftLimiter( normalizedRunningTorque, 1f );
				}

				_algorithmPropertyB = normalizedRunningTorque * settings.RacingWheelMaxForce;
				_algorithmPropertyA = steeringWheelTorque500Hz;

				outputTorque = normalizedRunningTorque;

				break;
			}

			case Algorithm.MultiAdjustmentToolkit:
			{
				var steadyBias = 0.08f;

				var normalizedLastCompressedTorque = _algorithmPropertyA / settings.RacingWheelMaxForce;
				var normalizedPriorRunningTorque = _algorithmPropertyB / settings.RacingWheelMaxForce;
				var normalizedPriorSteadyTorque = _algorithmPropertyC / settings.RacingWheelMaxForce;

				var normalizedTorque500Hz = steeringWheelTorque500Hz / settings.RacingWheelMaxForce;
				var normalizedTorque60Hz = steeringWheelTorque60Hz / settings.RacingWheelMaxForce;

				var compressionAmount = settings.RacingWheelMultiTorqueCompression;
				var slewAmount = settings.RacingWheelMultiSlewRateReduction;
				var detailGain = MathZ.Lerp( 1f + settings.RacingWheelMultiDetailGain, 1f, curbProtectionLerpFactor );
				var smoothingAmount = settings.RacingWheelMultiOutputSmoothing;

				var normalizedCompressedTorque = normalizedTorque500Hz;

				if ( compressionAmount != 0f )
				{
					var compressionRate = MathF.Min( 2f * compressionAmount, 0.75f );
					var compressionThreshold = 1f - 0.75f * compressionAmount;
					var compressionWidth = MathF.Min( compressionAmount, 0.5f );

					normalizedCompressedTorque = MathZ.Compression( normalizedTorque500Hz, compressionRate, compressionThreshold, compressionWidth, false );
				}

				if ( slewAmount != 0f )
				{
					float slewRateMultiplier;

					if ( ( MathF.Abs( normalizedCompressedTorque ) > MathF.Abs( normalizedLastCompressedTorque ) ) || ( MathF.Sign( normalizedCompressedTorque ) != MathF.Sign( normalizedLastCompressedTorque ) ) )
					{
						slewRateMultiplier = 1f;
					}
					else
					{
						slewRateMultiplier = 0.8f;
					}

					var slewRate = MathF.Min( MathF.Pow( slewAmount, 0.35f ), 0.9f ) * slewRateMultiplier;
					var slewThreshold = 0.01f - 0.0095f * slewAmount;
					var slewWidth = MathF.Min( MathF.Pow( slewAmount, 0.005f ), 0.0025f );

					var delta = normalizedCompressedTorque - normalizedLastCompressedTorque;
					var compressedDelta = MathZ.Compression( delta, slewRate, slewThreshold, slewWidth, false );

					normalizedCompressedTorque = normalizedLastCompressedTorque + compressedDelta;
				}

				var normalizedRunningSteadyTorque = MathZ.Lerp( normalizedPriorSteadyTorque, ( normalizedCompressedTorque + normalizedTorque60Hz ) / 2f, steadyBias );
				var normalizedRunningTorque = normalizedCompressedTorque;

				if ( detailGain != 1f )
				{
					if ( ( MathF.Abs( normalizedCompressedTorque - normalizedRunningSteadyTorque ) > MathF.Abs( normalizedLastCompressedTorque - normalizedRunningSteadyTorque ) ) || ( MathF.Sign( normalizedCompressedTorque - normalizedRunningSteadyTorque ) != MathF.Sign( normalizedLastCompressedTorque - normalizedPriorSteadyTorque ) ) || ( normalizedCompressedTorque == normalizedRunningSteadyTorque ) || ( normalizedLastCompressedTorque == normalizedPriorSteadyTorque ) )
					{
						if ( normalizedCompressedTorque > normalizedRunningSteadyTorque )
						{
							normalizedRunningTorque = MathF.Max( normalizedRunningSteadyTorque + ( normalizedCompressedTorque - normalizedRunningSteadyTorque ) * detailGain, normalizedRunningSteadyTorque );
						}
						else
						{
							normalizedRunningTorque = MathF.Min( normalizedRunningSteadyTorque - ( normalizedRunningSteadyTorque - normalizedCompressedTorque ) * detailGain, normalizedRunningSteadyTorque );
						}
					}
					else
					{
						if ( normalizedCompressedTorque > normalizedRunningSteadyTorque )
						{
							normalizedRunningTorque = MathF.Max( normalizedRunningSteadyTorque + ( normalizedCompressedTorque - normalizedRunningSteadyTorque ) / ( normalizedLastCompressedTorque - normalizedRunningSteadyTorque ) * ( normalizedPriorRunningTorque - normalizedRunningSteadyTorque ), normalizedRunningSteadyTorque );
						}
						else
						{
							normalizedRunningTorque = MathF.Min( normalizedRunningSteadyTorque - ( normalizedRunningSteadyTorque - normalizedCompressedTorque ) / ( normalizedRunningSteadyTorque - normalizedLastCompressedTorque ) * ( normalizedRunningSteadyTorque - normalizedPriorRunningTorque ), normalizedRunningSteadyTorque );
						}
					}
				}

				if ( smoothingAmount != 0f )
				{
					var smoothingRate = 0.8f * MathF.Pow( smoothingAmount, 0.3f );

					normalizedRunningTorque = MathZ.Lerp( normalizedRunningTorque, normalizedPriorRunningTorque + normalizedRunningSteadyTorque - normalizedPriorSteadyTorque, smoothingRate );
				}

				if ( settings.RacingWheelEnableMultiSoftLimiter )
				{
					var limitedTorque = MathZ.SoftLimiter( normalizedRunningTorque, 1f );

					normalizedCompressedTorque = normalizedCompressedTorque * limitedTorque / normalizedRunningTorque;
					normalizedRunningTorque = limitedTorque;
				}

				_algorithmPropertyA = normalizedCompressedTorque * settings.RacingWheelMaxForce;
				_algorithmPropertyB = normalizedRunningTorque * settings.RacingWheelMaxForce;
				_algorithmPropertyC = normalizedRunningSteadyTorque * settings.RacingWheelMaxForce;

				outputTorque = normalizedRunningTorque;

				break;
			}
		}

		// apply output curve

		if ( settings.RacingWheelOutputCurve != 0f )
		{
			var power = MathZ.CurveToPower( settings.RacingWheelOutputCurve );

			outputTorque = MathF.Sign( outputTorque ) * MathF.Pow( MathF.Abs( outputTorque ), power );
		}

		// apply output maximum

		if ( settings.RacingWheelOutputMaximum < 1f )
		{
			outputTorque = MathF.Min( outputTorque, settings.RacingWheelOutputMaximum );
		}

		// apply output minimum

		if ( settings.RacingWheelOutputMinimum > 0f )
		{
			if ( outputTorque >= 0f )
			{
				if ( outputTorque < settings.RacingWheelOutputMinimum )
				{
					outputTorque = settings.RacingWheelOutputMinimum;
				}
			}
			else
			{
				if ( outputTorque > -settings.RacingWheelOutputMinimum )
				{
					outputTorque = -settings.RacingWheelOutputMinimum;
				}
			}
		}

		// return calculated output torque

		return outputTorque;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Update( float deltaMilliseconds )
	{
		var app = App.Instance!;

		try
		{
			// easy reference to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// initialize generated vibration torque

			var vibrationTorque = 0f;

			// test signal generator

			if ( PlayTestSignal )
			{
				_testSignalTimerMS = TestSignalTimeMS;

				app.Logger.WriteLine( "[RacingWheel] Sending test signal" );

				PlayTestSignal = false;
			}

			if ( _testSignalTimerMS > 0f )
			{
				_testSignalTimerMS -= deltaMilliseconds;

				vibrationTorque += MathF.Cos( _testSignalTimerMS * MathF.Tau / 20f ) * MathF.Sin( _testSignalTimerMS * MathF.Tau / TestSignalTimeMS * 2f ) * 0.2f;
			}

			// understeer vibration effect

			if ( app.SteeringEffects.UndersteerEffect > 0f )
			{
				var isUndersteering = ( app.SteeringEffects.UndersteerEffect == 1f );

				var frequency = isUndersteering ? settings.SteeringEffectsUndersteerWheelVibrationMaximumFrequency : settings.SteeringEffectsUndersteerWheelVibrationMinimumFrequency;

				frequency = MathF.Max( 0.01f, frequency );

				var timeInSeconds = _understeerEffectTimerMS * 0.001f;

				var understeerEffectTorque = 0f;

				switch ( settings.SteeringEffectsUndersteerWheelVibrationPattern )
				{
					case VibrationPattern.SineWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						understeerEffectTorque = sine;
						break;
					}

					case VibrationPattern.SquareWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						understeerEffectTorque = ( sine >= 0f ) ? 1f : -1f;
						break;
					}

					case VibrationPattern.TriangleWave:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						understeerEffectTorque = 4f * MathF.Abs( phase - 0.5f ) - 1f;
						break;
					}

					case VibrationPattern.SawtoothWaveIn:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						understeerEffectTorque = ( phase - 1f ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}

					case VibrationPattern.SawtoothWaveOut:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						understeerEffectTorque = ( 1f - phase ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}
				}

				_understeerEffectTimerMS += deltaMilliseconds;

				var periodMS = 1000f / frequency;

				if ( _understeerEffectTimerMS >= periodMS )
				{
					_understeerEffectTimerMS -= periodMS * MathF.Floor( _understeerEffectTimerMS / periodMS );
				}

				vibrationTorque += understeerEffectTorque * settings.SteeringEffectsUndersteerWheelVibrationStrength * MathF.Pow( app.SteeringEffects.UndersteerEffect, MathZ.CurveToPower( settings.SteeringEffectsUndersteerWheelVibrationCurve ) );
			}

			// oversteer vibration effect

			if ( app.SteeringEffects.OversteerEffect > 0f )
			{
				var isOversteering = ( app.SteeringEffects.OversteerEffect == 1f );

				var frequency = isOversteering ? settings.SteeringEffectsOversteerWheelVibrationMaximumFrequency : settings.SteeringEffectsOversteerWheelVibrationMinimumFrequency;

				frequency = MathF.Max( 0.01f, frequency );

				var timeInSeconds = _oversteerEffectTimerMS * 0.001f;

				var oversteerEffectTorque = 0f;

				switch ( settings.SteeringEffectsOversteerWheelVibrationPattern )
				{
					case VibrationPattern.SineWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						oversteerEffectTorque = sine;
						break;
					}

					case VibrationPattern.SquareWave:
					{
						var sine = MathF.Sin( timeInSeconds * MathF.Tau * frequency );
						oversteerEffectTorque = ( sine >= 0f ) ? 1f : -1f;
						break;
					}

					case VibrationPattern.TriangleWave:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						oversteerEffectTorque = 4f * MathF.Abs( phase - 0.5f ) - 1f;
						break;
					}

					case VibrationPattern.SawtoothWaveIn:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						oversteerEffectTorque = ( phase - 1f ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}

					case VibrationPattern.SawtoothWaveOut:
					{
						var phase = ( timeInSeconds * frequency ) % 1f;
						oversteerEffectTorque = ( 1f - phase ) * -MathF.Sign( app.Simulator.SteeringWheelAngle );
						break;
					}
				}

				_oversteerEffectTimerMS += deltaMilliseconds;

				var periodMS = 1000f / frequency;

				if ( _oversteerEffectTimerMS >= periodMS )
				{
					_oversteerEffectTimerMS -= periodMS * MathF.Floor( _oversteerEffectTimerMS / periodMS );
				}

				vibrationTorque += oversteerEffectTorque * settings.SteeringEffectsOversteerWheelVibrationStrength * MathF.Pow( app.SteeringEffects.OversteerEffect, MathZ.CurveToPower( settings.SteeringEffectsOversteerWheelVibrationCurve ) );
			}

			// check if we want to suspend or unsuspend force feedback

			if ( SuspendForceFeedback != _isSuspended )
			{
				_isSuspended = SuspendForceFeedback;

				if ( _isSuspended )
				{
					app.Logger.WriteLine( "[RacingWheel] Requesting suspend of force feedback" );

					_unsuspendTimerMS = UnsuspendTimeMS;
				}
				else
				{
					app.Logger.WriteLine( "[RacingWheel] Requesting resumption of force feedback" );
				}

				app.MainWindow.UpdateRacingWheelPowerButton();
			}

			// check if we want to fade in or out the steering wheel torque data

			if ( UseSteeringWheelTorqueData != _usingSteeringWheelTorqueData )
			{
				_usingSteeringWheelTorqueData = UseSteeringWheelTorqueData;

				app.MainWindow.UpdateRacingWheelPowerButton();

				if ( settings.RacingWheelFadeEnabled )
				{
					if ( _usingSteeringWheelTorqueData )
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade in of steering wheel torque data" );

						_fadeTimerMS = FadeInTimeMS;
					}
					else
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade out of steering wheel torque data" );

						_fadeTimerMS = FadeOutTimeMS;
					}
				}
			}

			// check if we want to reset the racing wheel device

			if ( ResetForceFeedback )
			{
				ResetForceFeedback = false;

				if ( NextRacingWheelGuid == null )
				{
					NextRacingWheelGuid = _currentRacingWheelGuid;

					app.Logger.WriteLine( "[RacingWheel] Requesting reset of force feedback device" );
				}
			}

			// if power button is off, or suspend is requested, or unsuspend counter is still counting down, then suspend the racing wheel force feedback

			if ( !settings.RacingWheelEnableForceFeedback || _isSuspended || ( _unsuspendTimerMS > 0f ) )
			{
				if ( _currentRacingWheelGuid != null )
				{
					app.Logger.WriteLine( "[RacingWheel] Suspending racing wheel force feedback" );

					app.DirectInput.ShutdownForceFeedback();

					NextRacingWheelGuid = _currentRacingWheelGuid;

					_currentRacingWheelGuid = null;
				}

				_unsuspendTimerMS -= deltaMilliseconds;

				return;
			}

			// if next racing wheel guid is set then re-initialize force feedback

			if ( NextRacingWheelGuid != null )
			{
				if ( _currentRacingWheelGuid != null )
				{
					app.Logger.WriteLine( "[RacingWheel] Uninitializing racing wheel force feedback" );

					app.DirectInput.ShutdownForceFeedback();

					_currentRacingWheelGuid = null;
				}

				if ( NextRacingWheelGuid != Guid.Empty )
				{
					app.Logger.WriteLine( "[RacingWheel] Initializing racing wheel force feedback" );

					_currentRacingWheelGuid = NextRacingWheelGuid;

					NextRacingWheelGuid = null;

					app.DirectInput.InitializeForceFeedback( (Guid) _currentRacingWheelGuid );
				}
			}

			// check if we want to auto set max force

			if ( AutoSetMaxForce )
			{
				AutoSetMaxForce = false;
				ClearPeakTorque = true;

				settings.RacingWheelMaxForce = _autoTorque;

				app.Logger.WriteLine( $"[RacingWheel] Max force auto set to {_autoTorque}" );
			}

			// update elapsed milliseconds

			_elapsedMilliseconds += deltaMilliseconds;

			// update steering wheel torque data

			if ( UpdateSteeringWheelTorqueBuffer )
			{
				if ( _usingSteeringWheelTorqueData )
				{
					_steeringWheelTorque360Hz[ 0 ] = _steeringWheelTorque360Hz[ 7 ];
					_steeringWheelTorque360Hz[ 1 ] = app.Simulator.SteeringWheelTorque_ST[ 0 ];
					_steeringWheelTorque360Hz[ 2 ] = app.Simulator.SteeringWheelTorque_ST[ 1 ];
					_steeringWheelTorque360Hz[ 3 ] = app.Simulator.SteeringWheelTorque_ST[ 2 ];
					_steeringWheelTorque360Hz[ 4 ] = app.Simulator.SteeringWheelTorque_ST[ 3 ];
					_steeringWheelTorque360Hz[ 5 ] = app.Simulator.SteeringWheelTorque_ST[ 4 ];
					_steeringWheelTorque360Hz[ 6 ] = app.Simulator.SteeringWheelTorque_ST[ 5 ];
					_steeringWheelTorque360Hz[ 7 ] = app.Simulator.SteeringWheelTorque_ST[ 5 ];
				}
				else
				{
					_steeringWheelTorque360Hz[ 0 ] = 0f;
					_steeringWheelTorque360Hz[ 1 ] = 0f;
					_steeringWheelTorque360Hz[ 2 ] = 0f;
					_steeringWheelTorque360Hz[ 3 ] = 0f;
					_steeringWheelTorque360Hz[ 4 ] = 0f;
					_steeringWheelTorque360Hz[ 5 ] = 0f;
					_steeringWheelTorque360Hz[ 6 ] = 0f;
					_steeringWheelTorque360Hz[ 7 ] = 0f;
				}

				_elapsedMilliseconds = 0f;

				UpdateSteeringWheelTorqueBuffer = false;
			}

			// get next 60Hz and 360Hz steering wheel torque samples

			var steeringWheelTorque360HzIndex = 1f + ( _elapsedMilliseconds * 360f / 1000f );

			var i1 = Math.Min( MaxSteeringWheelTorque360HzIndex, (int) MathF.Truncate( steeringWheelTorque360HzIndex ) );
			var i2 = Math.Min( MaxSteeringWheelTorque360HzIndex, i1 + 1 );
			var i3 = Math.Min( MaxSteeringWheelTorque360HzIndex, i2 + 1 );
			var i0 = Math.Max( 0, i1 - 1 );

			var t = MathF.Min( 1f, steeringWheelTorque360HzIndex - i1 );

			var m0 = _steeringWheelTorque360Hz[ i0 ];
			var m1 = _steeringWheelTorque360Hz[ i1 ];
			var m2 = _steeringWheelTorque360Hz[ i2 ];
			var m3 = _steeringWheelTorque360Hz[ i3 ];

			var steeringWheelTorque60Hz = _steeringWheelTorque360Hz[ 6 ];
			var steeringWheelTorque500Hz = MathZ.InterpolateHermite( m0, m1, m2, m3, t );

			// update peak torque

			if ( ClearPeakTorque )
			{
				_peakTorque = 0f;

				ClearPeakTorque = false;
			}

			if ( app.Simulator.IsOnTrack && ( app.Simulator.PlayerTrackSurface == IRSDKSharper.IRacingSdkEnum.TrkLoc.OnTrack ) )
			{
				_peakTorque = MathF.Max( _peakTorque, MathZ.Lerp( _peakTorque, MathF.Abs( steeringWheelTorque500Hz ), 0.01f ) );
			}

			// update auto torque

			_autoTorque = _peakTorque * ( 1f + settings.RacingWheelAutoMargin );

			// update crash protection

			if ( ActivateCrashProtection )
			{
				if ( _crashProtectionTimerMS <= 0f )
				{
					if ( settings.RacingWheelCrashProtectionMessagesEnabled )
					{
						SendChatMessage( "CrashProtectionActivated" );
					}
				}

				_crashProtectionTimerMS = settings.RacingWheelCrashProtectionDuration * 1000f + CrashProtectionRecoveryTime;

				ActivateCrashProtection = false;
			}

			var crashProtectionScale = 1f;

			if ( _crashProtectionTimerMS > 0f )
			{
				crashProtectionScale = 1f - settings.RacingWheelCrashProtectionForceReduction * ( ( _crashProtectionTimerMS <= CrashProtectionRecoveryTime ) ? ( _crashProtectionTimerMS / CrashProtectionRecoveryTime ) : 1f );

				_crashProtectionTimerMS -= deltaMilliseconds;
			}

			// update curb protection

			if ( ActivateCurbProtection )
			{
				if ( _curbProtectionTimerMS <= 0f )
				{
					if ( settings.RacingWheelCurbProtectionMessagesEnabled )
					{
						SendChatMessage( "CurbProtectionActivated" );
					}
				}

				_curbProtectionTimerMS = settings.RacingWheelCurbProtectionDuration * 1000f;

				ActivateCurbProtection = false;
			}

			var curbProtectionLerpFactor = 0f;

			if ( _curbProtectionTimerMS > 0f )
			{
				curbProtectionLerpFactor = settings.RacingWheelCurbProtectionForceReduction;

				_curbProtectionTimerMS -= deltaMilliseconds;
			}

			// grab the next LFE magnitude

			var inputLFEMagnitude = app.LFE.CurrentMagnitude;

			// process the algorithm

			var outputTorque = ProcessAlgorithm( steeringWheelTorque60Hz, steeringWheelTorque500Hz, curbProtectionLerpFactor );

			// understeer constant force effect

			if ( app.SteeringEffects.UndersteerEffect > 0f )
			{
				switch ( settings.SteeringEffectsUndersteerWheelConstantForceDirection )
				{
					case ConstantForceDirection.DecreaseForce:
					{
						outputTorque = MathZ.Lerp( outputTorque, 0f, app.SteeringEffects.UndersteerEffect * settings.SteeringEffectsUndersteerWheelConstantForceStrength );
						break;
					}

					case ConstantForceDirection.IncreaseForce:
					{
						outputTorque += app.SteeringEffects.UndersteerEffect * settings.SteeringEffectsUndersteerWheelConstantForceStrength * MathF.Sign( app.Simulator.VelocityY );
						break;
					}
				}
			}

			// oversteer constant force effect

			if ( app.SteeringEffects.OversteerEffect > 0f )
			{
				switch ( settings.SteeringEffectsOversteerWheelConstantForceDirection )
				{
					case ConstantForceDirection.DecreaseForce:
					{
						outputTorque = MathZ.Lerp( outputTorque, 0f, app.SteeringEffects.OversteerEffect * settings.SteeringEffectsOversteerWheelConstantForceStrength );
						break;
					}

					case ConstantForceDirection.IncreaseForce:
					{
						outputTorque += app.SteeringEffects.OversteerEffect * settings.SteeringEffectsOversteerWheelConstantForceStrength * MathF.Sign( app.Simulator.VelocityY );
						break;
					}
				}
			}

			// apply crash protection

			outputTorque *= crashProtectionScale;

			// calculate parked factor (0-5 MPH)

			var parkedFactor = MathZ.Saturate( 1f - ( app.Simulator.Velocity / 2.2352f ) );

			// reduce forces when parked

			if ( settings.RacingWheelParkedStrength < 1f )
			{
				outputTorque *= MathZ.Lerp( 1f, settings.RacingWheelParkedStrength, parkedFactor );
			}

			// add wheel LFE

			if ( settings.RacingWheelLFEStrength > 0f )
			{
				outputTorque += inputLFEMagnitude * settings.RacingWheelLFEStrength;
			}

			// add soft lock

			if ( settings.RacingWheelSoftLockStrength > 0f )
			{
				var deltaToMax = ( app.Simulator.SteeringWheelAngleMax * 0.5f ) - MathF.Abs( app.Simulator.SteeringWheelAngle );

				if ( deltaToMax < 0f )
				{
					var sign = MathF.Sign( app.Simulator.SteeringWheelAngle );

					outputTorque += sign * deltaToMax * 2f * settings.RacingWheelSoftLockStrength;

					if ( MathF.Sign( app.DirectInput.ForceFeedbackWheelVelocity ) != sign )
					{
						outputTorque += app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelSoftLockStrength;
					}
				}
			}

			// apply normal friction torque

			if ( settings.RacingWheelFriction > 0f )
			{
				outputTorque += MathZ.Lerp( app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelFriction, 0f, parkedFactor );
			}

			// apply parked friction torque

			if ( settings.RacingWheelParkedFriction > 0f )
			{
				outputTorque += MathZ.Lerp( 0f, app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelParkedFriction, parkedFactor );
			}

			// center wheel while racing and parked

			if ( app.Simulator.IsOnTrack )
			{
				var racingCenteringForce = 0f;

				if ( settings.RacingWheelCenterWheelWhileRacing )
				{
					var centeringForce = ( Math.Clamp( app.DirectInput.ForceFeedbackWheelPosition, -0.25f, 0.25f ) + app.DirectInput.ForceFeedbackWheelVelocity * 0.1f ) * settings.RacingWheelWheelCenteringStrength;

					racingCenteringForce = Math.Clamp( centeringForce, -1f, 1f );
				}

				var parkedCenteringForce = 0f;

				if ( settings.RacingWheelCenterWheelWhileParked )
				{
					var centeringForce = ( Math.Clamp( app.DirectInput.ForceFeedbackWheelPosition, -0.25f, 0.25f ) + app.DirectInput.ForceFeedbackWheelVelocity * 0.1f ) * settings.RacingWheelParkedWheelCenteringStrength;

					parkedCenteringForce = Math.Clamp( centeringForce, -1f, 1f );
				}

				outputTorque += MathZ.Lerp( racingCenteringForce, parkedCenteringForce, parkedFactor );
			}

			// apply fade

			var fadeScale = 0f;

			if ( _fadeTimerMS > 0f )
			{
				if ( _usingSteeringWheelTorqueData )
				{
					fadeScale = _fadeTimerMS / FadeInTimeMS;

					outputTorque *= 1f - fadeScale;
				}
				else
				{
					fadeScale = _fadeTimerMS / FadeOutTimeMS;

					outputTorque = _lastUnfadedOutputTorque * fadeScale;
				}

				_fadeTimerMS -= deltaMilliseconds;
			}
			else
			{
				_lastUnfadedOutputTorque = outputTorque;
			}

			// center wheel when not in car (also affected by fade)

			if ( settings.RacingWheelCenterWheelWhenNotInCar )
			{
				if ( !app.Simulator.IsOnTrack )
				{
					var centeringForce = Math.Clamp( app.DirectInput.ForceFeedbackWheelPosition, -0.25f, 0.25f ) + 0.1f * app.DirectInput.ForceFeedbackWheelVelocity;

					centeringForce = Math.Clamp( centeringForce, -1f, 1f );

					outputTorque += centeringForce * ( 1f - fadeScale );
				}
			}

			// add vibration torque

			outputTorque += vibrationTorque;

			_outputTorque = outputTorque;

			// update force feedback torque

			app.DirectInput.UpdateForceFeedbackEffect( outputTorque );

			// update graph

			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque60Hz, steeringWheelTorque60Hz, steeringWheelTorque60Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque, steeringWheelTorque500Hz, steeringWheelTorque500Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputLFE, inputLFEMagnitude, inputLFEMagnitude );
			app.Graph.UpdateLayer( Graph.LayerIndex.OutputTorque, outputTorque, outputTorque );

			// update recording data

			app.RecordingManager.AddRecordingData( steeringWheelTorque60Hz, steeringWheelTorque500Hz );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[RacingWheel] Exception caught: {exception.Message.Trim()}" );

			_unsuspendTimerMS = UnsuspendTimeMS;
		}
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter == 0 )
		{
			_updateCounter = UpdateInterval;

			// shortcut to settings

			var settings = DataContext.DataContext.Instance.Settings;

			// update auto force label

			MainWindow._racingWheelPage.AutoForce_TextBlock.Text = $"{_autoTorque:F1}{DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";

			// update logitech rpm lights

			if ( settings.RacingWheelEnableLogitechRPMLights )
			{
				if ( !_logiPlayLedsNotWorking && app.Simulator.IsConnected && ( app.DirectInput.ForceFeedbackJoystick != null ) )
				{
					try
					{
						if ( !LogitechGSDK.LogiPlayLedsDInput( app.DirectInput.ForceFeedbackJoystick.NativePointer, app.Simulator.RPM, app.Simulator.ShiftLightsFirstRPM, app.Simulator.ShiftLightsShiftRPM ) )
						{
							_logiPlayLedsNotWorking = true;
						}
					}
					catch ( Exception )
					{
						_logiPlayLedsNotWorking = true;
					}

					if ( _logiPlayLedsNotWorking )
					{
						app.Logger.WriteLine( "[RacingWheel] The Logitech G SDK doesn't seem to be working, so we are temporarily disabling Logitech RPM lights support." );
					}
				}
			}

			// update algorithm preview

			if ( UpdateAlgorithmPreview )
			{
				UpdateAlgorithmPreview = false;

				_algorithmPreviewGraphBase.Reset();

				var recording = app.RecordingManager.Recording;

				_algorithmPropertyA = 0f;
				_algorithmPropertyB = 0f;
				_algorithmPropertyC = 0f;

				for ( var x = 0; x < _algorithmPreviewGraphBase.BitmapWidth; x++ )
				{
					if ( recording != null )
					{
						var inputTorque60Hz = recording.Data![ x ].InputTorque60Hz;
						var inputTorque500Hz = recording.Data![ x ].InputTorque500Hz;

						var outputTorque = ProcessAlgorithm( inputTorque60Hz, inputTorque500Hz, 0f );

						_algorithmPreviewGraphBase.Update( inputTorque500Hz / settings.RacingWheelMaxForce, 0.5f, 0f, 0f, 1f, 0.25f, 0.25f );
						_algorithmPreviewGraphBase.Update( outputTorque, 0f, 0.5f, 0.5f, 0.25f, 1f, 1f );
					}

					_algorithmPreviewGraphBase.FinishUpdates();
				}

				_algorithmPreviewGraphBase.WritePixels();
			}
		}
	}
}
