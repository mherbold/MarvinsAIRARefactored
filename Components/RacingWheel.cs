
using MarvinsAIRARefactored.Classes;
using MarvinsAIRARefactored.Controls;
using System.Runtime.CompilerServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

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
		ZeAlanLeTwist,
        TimeBias360hz
    };

	private const int _maxSteeringWheelTorque360HzIndex = Simulator.SamplesPerFrame360Hz + 1;

	private const float _unsuspendTimeMS = 1000f;
	private const float _fadeInTimeMS = 2000f;
	private const float _fadeOutTimeMS = 500f;
	private const float _testSignalTimeMS = 2000f;
	private const float _crashProtectionRecoveryTime = 1000f;

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

	private readonly float[] _steeringWheelTorque360Hz = new float[ Simulator.SamplesPerFrame360Hz + 2 ];

	private float _lastSteeringWheelTorque500Hz = 0f;
	private float _runningSteeringWheelTorque500Hz = 0f;

	private float _lastUnfadedOutputTorque = 0f;
	private float _peakTorque = 0f;
	private float _autoTorque = 0f;

	private float _elapsedMilliseconds = 0f;

	private GraphBase _algorithmPreviewGraphBase = new();

    private float _lastTorque = -1;
  
    public float Matts_OutputValue { get; private set; } = 0f;
    public void Initialize()
	{ 
		var app = App.Instance!;

		app.Logger.WriteLine( "[RacingWheel] Initialize >>>" );

		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque60Hz, 1f, 0f, 0f, 1f, 0f, 0f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputTorque, 1f, 0f, 1f, 1f, 0f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.InputLFE, 0.1f, 0.5f, 1f, 1f, 1f, 1f );
		app.Graph.SetLayerColors( Graph.LayerIndex.OutputTorque, 0f, 1f, 1f, 0f, 1f, 1f );

		_algorithmPreviewGraphBase.Initialize( app.MainWindow.Algorithm_Image );

		app.Logger.WriteLine( "[RacingWheel] <<< Initialize" );
	}

	public static void SetMairaComboBoxItemsSource(MairaComboBox mairaComboBox)
	{
		var app = App.Instance!;

		app.Logger.WriteLine("[RacingWheel] SetMairaComboBoxItemsSource >>>");

		var dictionary = new Dictionary<Algorithm, string>
			{
				{ Algorithm.Native60Hz, DataContext.DataContext.Instance.Localization[ "Native60Hz" ] },
				{ Algorithm.Native360Hz, DataContext.DataContext.Instance.Localization[ "Native360Hz" ] },
				{ Algorithm.DetailBooster, DataContext.DataContext.Instance.Localization[ "DetailBooster" ] },
				{ Algorithm.DeltaLimiter, DataContext.DataContext.Instance.Localization[ "DeltaLimiter" ] },
				{ Algorithm.DetailBoosterOn60Hz, DataContext.DataContext.Instance.Localization[ "DetailBoosterOn60Hz" ] },
				{ Algorithm.DeltaLimiterOn60Hz, DataContext.DataContext.Instance.Localization[ "DeltaLimiterOn60Hz" ] },
				{ Algorithm.ZeAlanLeTwist, DataContext.DataContext.Instance.Localization[ "ZeAlanLeTwist" ] },
				{Algorithm.TimeBias360hz, DataContext.DataContext.Instance.Localization[ "TimeBias360hz" ] }, 
			};

		mairaComboBox.ItemsSource = dictionary;
		mairaComboBox.SelectedValue = DataContext.DataContext.Instance.Settings.RacingWheelAlgorithm;

		app.Logger.WriteLine( "[RacingWheel] <<< SetMairaComboBoxItemsSource" );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float ProcessAlgorithm( ref float runningSteeringWheelTorque500Hz, float lastSteeringWheelTorque500Hz, float steeringWheelTorque60Hz, float steeringWheelTorque500Hz, float curbProtectionLerpFactor )
	{
		var settings = DataContext.DataContext.Instance.Settings;

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
				var detailBoost = Misc.Lerp( 1f + settings.RacingWheelDetailBoost, 1f, curbProtectionLerpFactor );

				runningSteeringWheelTorque500Hz = Misc.Lerp( runningSteeringWheelTorque500Hz + ( steeringWheelTorque500Hz - lastSteeringWheelTorque500Hz ) * detailBoost, steeringWheelTorque500Hz, settings.RacingWheelDetailBoostBias );

				outputTorque = runningSteeringWheelTorque500Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DeltaLimiter:
			{
				var deltaLimit = Misc.Lerp( settings.RacingWheelDeltaLimit / 500f, 0f, curbProtectionLerpFactor );

				var limitedDeltaSteeringWheelTorque500Hz = Math.Clamp( steeringWheelTorque500Hz - lastSteeringWheelTorque500Hz, -deltaLimit, deltaLimit );

				runningSteeringWheelTorque500Hz = Misc.Lerp( runningSteeringWheelTorque500Hz + limitedDeltaSteeringWheelTorque500Hz, steeringWheelTorque500Hz, settings.RacingWheelDeltaLimiterBias );

				outputTorque = runningSteeringWheelTorque500Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DetailBoosterOn60Hz:
			{
				var detailBoost = Misc.Lerp( 1f + settings.RacingWheelDetailBoost, 1f, curbProtectionLerpFactor );

				runningSteeringWheelTorque500Hz = Misc.Lerp( runningSteeringWheelTorque500Hz + ( steeringWheelTorque500Hz - lastSteeringWheelTorque500Hz ) * detailBoost, steeringWheelTorque60Hz, settings.RacingWheelDetailBoostBias );

				outputTorque = runningSteeringWheelTorque500Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.DeltaLimiterOn60Hz:
			{
				var deltaLimit = Misc.Lerp( settings.RacingWheelDeltaLimit / 500f, 0f, curbProtectionLerpFactor );

				var limitedDeltaSteeringWheelTorque500Hz = Math.Clamp( steeringWheelTorque500Hz - lastSteeringWheelTorque500Hz, -deltaLimit, deltaLimit );

				runningSteeringWheelTorque500Hz = Misc.Lerp( runningSteeringWheelTorque500Hz + limitedDeltaSteeringWheelTorque500Hz, steeringWheelTorque60Hz, settings.RacingWheelDeltaLimiterBias );

				outputTorque = runningSteeringWheelTorque500Hz / settings.RacingWheelMaxForce;

				break;
			}

			case Algorithm.ZeAlanLeTwist:
			{
				var normalizedRunningTorque = runningSteeringWheelTorque500Hz / settings.RacingWheelMaxForce;
				var normalizedRunningTorqueAbs = MathF.Abs( normalizedRunningTorque );

				var normalizedDelta = ( steeringWheelTorque500Hz - runningSteeringWheelTorque500Hz ) / settings.RacingWheelMaxForce;
				var normalizedDeltaAbs = MathF.Abs( normalizedDelta );

				var deltaLimit = settings.RacingWheelSlewCompressionThreshold / 500f;

				if ( ( ( MathF.Sign( normalizedDelta ) != MathF.Sign( steeringWheelTorque500Hz ) ) && ( normalizedDeltaAbs > deltaLimit ) ) )
				{
					var oneMinusSlewCompressionRate = 1f - settings.RacingWheelSlewCompressionRate;
					var deltaRate = oneMinusSlewCompressionRate * MathF.Max( 0.75f, 0.25f + ( oneMinusSlewCompressionRate * 0.75f ) );

					normalizedRunningTorque += ( deltaLimit + ( ( normalizedDeltaAbs - deltaLimit ) * deltaRate ) ) * MathF.Sign( normalizedDelta );
				}
				else
				{
					normalizedRunningTorque += normalizedDelta;
				}

				var compressionThreshold = settings.RacingWheelTotalCompressionThreshold;
				var compressionWidth = compressionThreshold;
				var halfCompressionWidth = compressionWidth * 0.5f;

				var oneMinusTotalCompressionRate = 1f - settings.RacingWheelTotalCompressionRate;

				if ( ( normalizedRunningTorqueAbs > ( compressionThreshold - halfCompressionWidth ) ) && ( normalizedRunningTorqueAbs < ( compressionThreshold + halfCompressionWidth ) ) )
				{
					normalizedRunningTorqueAbs -= halfCompressionWidth * ( normalizedRunningTorqueAbs - compressionThreshold + halfCompressionWidth - ( compressionWidth / MathF.PI * MathF.Sin( MathF.PI * ( normalizedRunningTorqueAbs - compressionThreshold + halfCompressionWidth ) / compressionWidth ) ) );
				}
				else if ( normalizedRunningTorqueAbs >= ( compressionThreshold + halfCompressionWidth ) )
				{
					normalizedRunningTorqueAbs = compressionThreshold + ( normalizedRunningTorqueAbs - compressionThreshold ) * oneMinusTotalCompressionRate;
				}

				normalizedRunningTorque = normalizedRunningTorqueAbs * MathF.Sign( normalizedRunningTorque );

				runningSteeringWheelTorque500Hz = normalizedRunningTorque * settings.RacingWheelMaxForce;

				outputTorque = normalizedRunningTorque;

				break;
			}

			case Algorithm.TimeBias360hz:

                outputTorque = steeringWheelTorque60Hz / settings.RacingWheelMaxForce;

				if (_lastTorque == -1)//if flag
                {
                    _lastTorque = outputTorque;
                }

                float deltaTorque = Math.Abs(outputTorque - _lastTorque);
                float bias = Math.Clamp(deltaTorque / settings.RacingWheel_Matts_DeltaForce, settings.RacingWheel_Matts_MinValue, 1);
                outputTorque = outputTorque * bias + (_lastTorque * (1 - bias));

                _lastTorque = outputTorque;
                break;
		}

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

			// test signal generator

			if ( PlayTestSignal )
			{
				_testSignalTimerMS = _testSignalTimeMS;

				app.Logger.WriteLine( "[RacingWheel] Sending test signal" );

				PlayTestSignal = false;
			}

			var testSignalTorque = 0f;

			if ( _testSignalTimerMS > 0f )
			{
				testSignalTorque = MathF.Cos( _testSignalTimerMS * MathF.Tau / 20f ) * MathF.Sin( _testSignalTimerMS * MathF.Tau / _testSignalTimeMS * 2f ) * 0.2f;

				_testSignalTimerMS -= deltaMilliseconds;
			}

			// check if we want to suspend or unsuspend force feedback

			if ( SuspendForceFeedback != _isSuspended )
			{
				_isSuspended = SuspendForceFeedback;

				if ( _isSuspended )
				{
					app.Logger.WriteLine( "[RacingWheel] Requesting suspend of force feedback" );

					_unsuspendTimerMS = _unsuspendTimeMS;
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

						_fadeTimerMS = _fadeInTimeMS;
					}
					else
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade out of steering wheel torque data" );

						_fadeTimerMS = _fadeOutTimeMS;
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

			var i1 = Math.Min( _maxSteeringWheelTorque360HzIndex, (int) MathF.Truncate( steeringWheelTorque360HzIndex ) );
			var i2 = Math.Min( _maxSteeringWheelTorque360HzIndex, i1 + 1 );
			var i3 = Math.Min( _maxSteeringWheelTorque360HzIndex, i2 + 1 );
			var i0 = Math.Max( 0, i1 - 1 );

			var t = MathF.Min( 1f, steeringWheelTorque360HzIndex - i1 );

			var m0 = _steeringWheelTorque360Hz[ i0 ];
			var m1 = _steeringWheelTorque360Hz[ i1 ];
			var m2 = _steeringWheelTorque360Hz[ i2 ];
			var m3 = _steeringWheelTorque360Hz[ i3 ];

			var steeringWheelTorque60Hz = _steeringWheelTorque360Hz[ 6 ];
			var steeringWheelTorque500Hz = Misc.InterpolateHermite( m0, m1, m2, m3, t );

			// update peak torque

			if ( ClearPeakTorque )
			{
				_peakTorque = 0f;

				ClearPeakTorque = false;
			}

			if ( app.Simulator.IsOnTrack && ( app.Simulator.PlayerTrackSurface == IRSDKSharper.IRacingSdkEnum.TrkLoc.OnTrack ) )
			{
				_peakTorque = MathF.Max( _peakTorque, Misc.Lerp( _peakTorque, MathF.Abs( steeringWheelTorque500Hz ), 0.01f ) );
			}

			// update auto torque

			_autoTorque = _peakTorque * ( 1f + settings.RacingWheelAutoMargin );

			// update crash protection

			if ( ActivateCrashProtection )
			{
				_crashProtectionTimerMS = settings.RacingWheelCrashProtectionDuration * 1000f + _crashProtectionRecoveryTime;

				ActivateCrashProtection = false;
			}

			app.Debug.Label_3 = $"_crashProtectionTimerMS = {_crashProtectionTimerMS:F0}";

			var crashProtectionScale = 1f;

			if ( _crashProtectionTimerMS > 0f )
			{
				crashProtectionScale = 1f - settings.RacingWheelCrashProtectionForceReduction * ( ( _crashProtectionTimerMS <= _crashProtectionRecoveryTime ) ? ( _crashProtectionTimerMS / _crashProtectionRecoveryTime ) : 1f );

				_crashProtectionTimerMS -= deltaMilliseconds;
			}

			app.Debug.Label_4 = $"crashProtectionScale = {crashProtectionScale * 100f:F0}";

			// update curb protection

			if ( ActivateCurbProtection )
			{
				_curbProtectionTimerMS = settings.RacingWheelCurbProtectionDuration * 1000f;

				ActivateCurbProtection = false;
			}

			app.Debug.Label_9 = $"_curbProtectionTimerMS = {_curbProtectionTimerMS:F0}";

			var curbProtectionLerpFactor = 0f;

			if ( _curbProtectionTimerMS > 0f )
			{
				curbProtectionLerpFactor = settings.RacingWheelCurbProtectionForceReduction;

				_curbProtectionTimerMS -= deltaMilliseconds;
			}

			app.Debug.Label_10 = $"curbProtectionLerpFactor = {curbProtectionLerpFactor * 100f:F0}%";

			// grab the next LFE magnitude

			var inputLFEMagnitude = app.LFE.CurrentMagnitude;

			// process the algorithm

			var outputTorque = ProcessAlgorithm( ref _runningSteeringWheelTorque500Hz, _lastSteeringWheelTorque500Hz, steeringWheelTorque60Hz, steeringWheelTorque500Hz, curbProtectionLerpFactor );

			// save last 500Hz steering wheel torque

			_lastSteeringWheelTorque500Hz = steeringWheelTorque500Hz;

			// apply output maximum

			if ( settings.RacingWheelOutputMaximum < 1f )
			{
				outputTorque *= settings.RacingWheelOutputMaximum;
			}

			// apply output curve

			if ( settings.RacingWheelOutputCurve != 0f )
			{
				var power = Misc.CurveToPower( settings.RacingWheelOutputCurve );

				outputTorque = MathF.Sign( outputTorque ) * MathF.Pow( MathF.Abs( outputTorque ), power );
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

			// apply crash protection

			outputTorque *= crashProtectionScale;

			// reduce forces when parked

			if ( settings.RacingWheelParkedStrength < 1f )
			{
				outputTorque *= Misc.Lerp( settings.RacingWheelParkedStrength, 1f, app.Simulator.Velocity / 2.2352f ); // = 5 MPH
			}

			// add wheel LFE

			if ( settings.RacingWheelLFEStrength > 0f )
			{
				outputTorque += inputLFEMagnitude * settings.RacingWheelLFEStrength;
			}

			// add soft lock

			if ( settings.RacingWheelSoftLockStrength > 0f )
			{
				var deltaToMax = app.Simulator.SteeringWheelAngleMax - MathF.Abs( app.Simulator.SteeringWheelAngle );

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

			// apply friction torque

			if ( settings.RacingWheelFriction > 0f )
			{
				outputTorque += app.DirectInput.ForceFeedbackWheelVelocity * settings.RacingWheelFriction;
			}

			// apply fade

			app.Debug.Label_5 = $"_fadeTimerMS = {_fadeTimerMS:F0}";
			app.Debug.Label_6 = $"_lastUnfadedOutputTorque = {_lastUnfadedOutputTorque:F2}";

			if ( _fadeTimerMS > 0f )
			{
				if ( _usingSteeringWheelTorqueData )
				{
					var fadeScale = _fadeTimerMS / _fadeInTimeMS;

					app.Debug.Label_7 = $"fadeScale = {fadeScale * 100f:F2}% (fading in)";

					outputTorque *= 1f - fadeScale;
				}
				else
				{
					var fadeScale = _fadeTimerMS / _fadeOutTimeMS;

					app.Debug.Label_7 = $"fadeScale = {fadeScale * 100f:F0}% (fading out)";

					outputTorque = _lastUnfadedOutputTorque * fadeScale;
				}

				_fadeTimerMS -= deltaMilliseconds;
			}
			else
			{
				app.Debug.Label_7 = $"fadeScale = OFF";

				_lastUnfadedOutputTorque = outputTorque;
			}

			// add test signal torque

			outputTorque += testSignalTorque;

			// update force feedback torque

			app.DirectInput.UpdateForceFeedbackEffect( outputTorque );

			// update graph

			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque60Hz, steeringWheelTorque60Hz, steeringWheelTorque60Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputTorque, steeringWheelTorque500Hz, steeringWheelTorque500Hz / settings.RacingWheelMaxForce );
			app.Graph.UpdateLayer( Graph.LayerIndex.InputLFE, inputLFEMagnitude, inputLFEMagnitude );
			app.Graph.UpdateLayer( Graph.LayerIndex.OutputTorque, outputTorque, outputTorque );

			// update alan le dump

			app.Debug.AddFFBSample( deltaMilliseconds, steeringWheelTorque60Hz, steeringWheelTorque500Hz, inputLFEMagnitude, outputTorque );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[RacingWheel] Exception caught: {exception.Message.Trim()}" );

			_unsuspendTimerMS = _unsuspendTimeMS;
		}
	}

	public void Tick( App app )
	{
		app.MainWindow.RacingWheel_PeakForce_Label.Content = $"{_peakTorque:F2}{DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";
		app.MainWindow.RacingWheel_AutoForce_Label.Content = $"{_autoTorque:F2}{DataContext.DataContext.Instance.Localization[ "TorqueUnits" ]}";

		if ( UpdateAlgorithmPreview )
		{
			UpdateAlgorithmPreview = false;

			var settings = DataContext.DataContext.Instance.Settings;

			_algorithmPreviewGraphBase.Reset();

			var torqueArray = new float[ _algorithmPreviewGraphBase.BitmapWidth + 10 ];

			for ( var x = 0; x < _algorithmPreviewGraphBase.BitmapWidth; x++ )
			{
				var percent = (float) x / _algorithmPreviewGraphBase.BitmapWidth;

				var time = 10f * percent;

				var normalizedInputTorque = MathF.Sin( time * 0.25f * MathF.Tau ) * 0.35f + MathF.Cos( time * 30f * MathF.Tau ) * MathF.Sin( time * MathF.Tau ) * 0.5f * MathF.Cos( percent * MathF.Tau );

				torqueArray[ x ] = 35f * normalizedInputTorque;
			}

			var runningTorque = 0f;
			var lastTorque500Hz = 0f;

			for ( var x = 0; x < _algorithmPreviewGraphBase.BitmapWidth; x++ )
			{
				var inputTorque60Hz = torqueArray[ (int) ( MathF.Ceiling( x / 8.333f ) * 8.333f ) ];
				var inputTorque500Hz = torqueArray[ x ];

				var outputTorque = ProcessAlgorithm( ref runningTorque, lastTorque500Hz, inputTorque60Hz, inputTorque500Hz, 0f );

				lastTorque500Hz = inputTorque500Hz;

				_algorithmPreviewGraphBase.Update( inputTorque500Hz / settings.RacingWheelMaxForce, 0.5f, 0f, 0f, 1f, 0.25f, 0.25f );
				_algorithmPreviewGraphBase.Update( outputTorque, 0f, 0.5f, 0.5f, 0.25f, 1f, 1f );

				_algorithmPreviewGraphBase.FinishUpdates();
			}

			_algorithmPreviewGraphBase.WritePixels();
		}
	}
}
