
using System.Runtime.CompilerServices;

using ComboBox = System.Windows.Controls.ComboBox;

namespace MarvinsAIRARefactored.Components;

public class RacingWheel
{
	private const int _maxSteeringWheelTorque360HzIndex = Simulator.IRSDK_360HZ_SAMPLES_PER_FRAME + 1;
	private const int _unsuspendCounterStartValue = 500;
	private const int _fadeCounterStartValue = 1000;
	private const int _testSignalCounterStartValue = 1000;

	private Guid? _currentRacingWheelGuid = null;

	private bool _isSuspended = true;
	private bool _usingSteeringWheelTorqueData = false;

	public Guid? NextRacingWheelGuid { private get; set; } = null;
	public bool SuspendForceFeedback { get; set; } = true; // true if simulator is disconnected or if FFB is enabled in the simulator
	public bool ResetForceFeedback { private get; set; } = false; // set to true manually (via reset button)
	public bool UseSteeringWheelTorqueData { private get; set; } = false; // false if simulator is disconnected or if driver is not on track
	public bool UpdateSteeringWheelTorqueBuffer { private get; set; } = false; // true when simulator has new torque data to be copied
	public bool ActivateCrashProtection { private get; set; } = false;
	public bool ActivateCurbProtection { private get; set; } = false;
	public bool PlayTestSignal { private get; set; } = false; // set to true manually (via test button)
	public bool ClearPeakTorque { private get; set; } = false; // set to clear peak torque
	public bool AutoSetMaxForce { private get; set; } = false; // set to auto-set the max force setting

	private int _unsuspendCounter = 0;
	private int _fadeCounter = 0;
	private int _testSignalCounter = 0;

	private readonly float[] _steeringWheelTorque360Hz = new float[ Simulator.IRSDK_360HZ_SAMPLES_PER_FRAME + 2 ];

	private float _lastSteeringWheelTorque360Hz = 0f;
	private float _runningSteeringWheelTorque360Hz = 0f;

	private float _lastUnfadedOutputTorque = 0f;
	private float _peakTorque = 0f;
	private float _autoTorque = 0f;

	private float _elapsedMilliseconds = 0f;

	public static void SetComboBoxItemsSource( ComboBox comboBox )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[RacingWheel] SetAlgorithmComboBoxItemsSource >>>" );

			var selectedItem = comboBox.SelectedItem as KeyValuePair<Settings.RacingWheelAlgorithmEnum, string>?;

			var dictionary = new Dictionary<Settings.RacingWheelAlgorithmEnum, string>
			{
				{ Settings.RacingWheelAlgorithmEnum.Native60Hz, DataContext.Instance.Localization[ "Native60Hz" ] },
				{ Settings.RacingWheelAlgorithmEnum.Native360Hz, DataContext.Instance.Localization[ "Native360Hz" ] },
				{ Settings.RacingWheelAlgorithmEnum.DetailBooster, DataContext.Instance.Localization[ "DetailBooster" ] },
				{ Settings.RacingWheelAlgorithmEnum.DeltaLimiter, DataContext.Instance.Localization[ "DeltaLimiter" ] },
				{ Settings.RacingWheelAlgorithmEnum.DetailBoosterOn60Hz, DataContext.Instance.Localization[ "DetailBoosterOn60Hz" ] },
				{ Settings.RacingWheelAlgorithmEnum.DeltaLimiterOn60Hz, DataContext.Instance.Localization[ "DeltaLimiterOn60Hz" ] },
				{ Settings.RacingWheelAlgorithmEnum.ZeAlanLeTwist, DataContext.Instance.Localization[ "ZeAlanLeTwist" ] }
			};

			comboBox.ItemsSource = dictionary;

			if ( selectedItem.HasValue )
			{
				comboBox.SelectedItem = dictionary.FirstOrDefault( keyValuePair => keyValuePair.Key.Equals( selectedItem.Value.Key ) ); ;
			}

			app.Logger.WriteLine( "[RacingWheel] <<< SetAlgorithmComboBoxItemsSource" );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Update( float deltaMilliseconds )
	{
		var app = App.Instance;

		if ( app != null )
		{
			try
			{
				// test signal generator

				if ( PlayTestSignal )
				{
					_testSignalCounter = _testSignalCounterStartValue;

					app.Logger.WriteLine( "[RacingWheel] Sending test signal" );

					PlayTestSignal = false;
				}

				var testSignalTorque = 0f;

				if ( _testSignalCounter > 0 )
				{
					testSignalTorque = MathF.Cos( _testSignalCounter * MathF.Tau / 12f ) * MathF.Sin( _testSignalCounter * MathF.Tau / _testSignalCounterStartValue * 2f ) * 0.2f;

					_testSignalCounter--;
				}

				// check if we want to suspend or unsuspend force feedback

				if ( SuspendForceFeedback != _isSuspended )
				{
					_isSuspended = SuspendForceFeedback;

					if ( _isSuspended )
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting suspend of force feedback" );

						_unsuspendCounter = _unsuspendCounterStartValue;
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

					if ( _usingSteeringWheelTorqueData )
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade in of steering wheel torque data" );
					}
					else
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade out of steering wheel torque data" );
					}

					app.MainWindow.UpdateRacingWheelPowerButton();

					if ( DataContext.Instance.Settings.RacingWheelFadeEnabled )
					{
						_fadeCounter = _fadeCounterStartValue;
					}
				}

				// check if we want to reset the racing wheel device

				if ( ResetForceFeedback )
				{
					ResetForceFeedback = false;
					NextRacingWheelGuid = _currentRacingWheelGuid;

					app.Logger.WriteLine( "[RacingWheel] Requesting reset of force feedback device" );
				}

				// if power button is off, or suspend is requested, or unsuspend counter is still counting down, then suspend the racing wheel force feedback

				if ( !DataContext.Instance.Settings.RacingWheelEnableForceFeedback || _isSuspended || ( _unsuspendCounter > 0 ) )
				{
					if ( _currentRacingWheelGuid != null )
					{
						app.Logger.WriteLine( "[RacingWheel] Suspending racing wheel force feedback" );

						app.DirectInput.ShutdownForceFeedback();

						NextRacingWheelGuid = _currentRacingWheelGuid;

						_currentRacingWheelGuid = null;
					}

					_unsuspendCounter--;

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

					app.Logger.WriteLine( "[RacingWheel] Initializing racing wheel force feedback" );

					_currentRacingWheelGuid = NextRacingWheelGuid;

					NextRacingWheelGuid = null;

					app.DirectInput.InitializeForceFeedback( (Guid) _currentRacingWheelGuid );
				}

				// check if we want to auto set max force

				if ( AutoSetMaxForce )
				{
					AutoSetMaxForce = false;
					ClearPeakTorque = true;

					DataContext.Instance.Settings.RacingWheelMaxForce = _autoTorque;

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
				var steeringWheelTorque360Hz = Misc.InterpolateHermite( m0, m1, m2, m3, t );

				// update peak torque

				if ( ClearPeakTorque )
				{
					_peakTorque = 0f;

					ClearPeakTorque = false;
				}

				if ( app.Simulator.IsOnTrack && ( app.Simulator.PlayerTrackSurface == IRSDKSharper.IRacingSdkEnum.TrkLoc.OnTrack ) )
				{
					_peakTorque = MathF.Max( _peakTorque, Misc.Lerp( _peakTorque, MathF.Abs( steeringWheelTorque360Hz ), 0.01f ) );
				}

				// update auto torque

				_autoTorque = _peakTorque * ( 1f + DataContext.Instance.Settings.RacingWheelAutoMargin );

				// this part is done only if we have a racing wheel device initialized

				if ( _currentRacingWheelGuid != null )
				{
					// calculate output torque

					var outputTorque = 0f;

					switch ( DataContext.Instance.Settings.RacingWheelAlgorithm )
					{
						case Settings.RacingWheelAlgorithmEnum.Native60Hz:
						{
							outputTorque = steeringWheelTorque60Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}

						case Settings.RacingWheelAlgorithmEnum.Native360Hz:
						{
							outputTorque = steeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}

						case Settings.RacingWheelAlgorithmEnum.DetailBooster:
						{
							_runningSteeringWheelTorque360Hz = Misc.Lerp( _runningSteeringWheelTorque360Hz + ( steeringWheelTorque360Hz - _lastSteeringWheelTorque360Hz ) * ( 1f + DataContext.Instance.Settings.RacingWheelDetailBoost ), steeringWheelTorque360Hz, DataContext.Instance.Settings.RacingWheelBias );

							outputTorque = _runningSteeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}

						case Settings.RacingWheelAlgorithmEnum.DeltaLimiter:
						{
							var deltaLimit = DataContext.Instance.Settings.RacingWheelDeltaLimit / 500f;

							var limitedDeltaSteeringWheelTorque360Hz = Math.Clamp( steeringWheelTorque360Hz - _lastSteeringWheelTorque360Hz, -deltaLimit, deltaLimit );

							_runningSteeringWheelTorque360Hz = Misc.Lerp( _runningSteeringWheelTorque360Hz + limitedDeltaSteeringWheelTorque360Hz, steeringWheelTorque360Hz, DataContext.Instance.Settings.RacingWheelBias );

							outputTorque = _runningSteeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}

						case Settings.RacingWheelAlgorithmEnum.DetailBoosterOn60Hz:
						{
							_runningSteeringWheelTorque360Hz = Misc.Lerp( _runningSteeringWheelTorque360Hz + ( steeringWheelTorque360Hz - _lastSteeringWheelTorque360Hz ) * ( 1f + DataContext.Instance.Settings.RacingWheelDetailBoost ), steeringWheelTorque60Hz, DataContext.Instance.Settings.RacingWheelBias );

							outputTorque = _runningSteeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}

						case Settings.RacingWheelAlgorithmEnum.DeltaLimiterOn60Hz:
						{
							var deltaLimit = DataContext.Instance.Settings.RacingWheelDeltaLimit / 500f;

							var limitedDeltaSteeringWheelTorque360Hz = Math.Clamp( steeringWheelTorque360Hz - _lastSteeringWheelTorque360Hz, -deltaLimit, deltaLimit );

							_runningSteeringWheelTorque360Hz = Misc.Lerp( _runningSteeringWheelTorque360Hz + limitedDeltaSteeringWheelTorque360Hz, steeringWheelTorque360Hz, DataContext.Instance.Settings.RacingWheelBias );

							outputTorque = _runningSteeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}

						case Settings.RacingWheelAlgorithmEnum.ZeAlanLeTwist:
						{
							var deltaLimit = DataContext.Instance.Settings.RacingWheelDeltaLimit / 500f;

							var delta = steeringWheelTorque360Hz - _lastSteeringWheelTorque360Hz;

							var compressibleDelta = MathF.Max( 0f, MathF.Abs( delta ) - deltaLimit );

							var deltaScale = MathF.Max( 0f, 1f - ( compressibleDelta * DataContext.Instance.Settings.RacingWheelCompressionRate ) );

							var compressedDeltaSteeringWheelTorque360Hz = ( compressibleDelta > 0f ) ? delta : delta + MathF.Sign( delta ) * compressibleDelta * deltaScale;

							_runningSteeringWheelTorque360Hz = Misc.Lerp( _runningSteeringWheelTorque360Hz + compressedDeltaSteeringWheelTorque360Hz, steeringWheelTorque360Hz, DataContext.Instance.Settings.RacingWheelBias );

							outputTorque = _runningSteeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;

							break;
						}
					}

					// save last 360Hz steering wheel torque

					_lastSteeringWheelTorque360Hz = steeringWheelTorque360Hz;

					// reduce forces when parked

					if ( DataContext.Instance.Settings.RacingWheelParkedStrength < 1f )
					{
						outputTorque *= Misc.Lerp( DataContext.Instance.Settings.RacingWheelParkedStrength, 1f, app.Simulator.Velocity / 2.2352f ); // = 5 MPH
					}

					// add soft lock

					if ( DataContext.Instance.Settings.RacingWheelSoftLockStrength > 0f )
					{
						var deltaToMax = app.Simulator.SteeringWheelAngleMax - MathF.Abs( app.Simulator.SteeringWheelAngle );

						if ( deltaToMax < 0f )
						{
							var sign = MathF.Sign( app.Simulator.SteeringWheelAngle );

							outputTorque += sign * deltaToMax * 2f * DataContext.Instance.Settings.RacingWheelSoftLockStrength;

							if ( MathF.Sign( app.DirectInput.ForceFeedbackWheelVelocity ) != sign )
							{
								outputTorque += app.DirectInput.ForceFeedbackWheelVelocity * DataContext.Instance.Settings.RacingWheelSoftLockStrength;
							}
						}
					}

					// add test signal torque

					outputTorque += testSignalTorque;

					// apply friction torque

					if ( DataContext.Instance.Settings.RacingWheelFriction > 0f )
					{
						outputTorque += app.DirectInput.ForceFeedbackWheelVelocity * DataContext.Instance.Settings.RacingWheelFriction;
					}

					// apply fade

					if ( _fadeCounter > 0 )
					{
						var fadeScale = (float) _fadeCounter / _fadeCounterStartValue;

						if ( _usingSteeringWheelTorqueData )
						{
							outputTorque *= 1f - fadeScale;
						}
						else
						{
							outputTorque = _lastUnfadedOutputTorque * fadeScale;
						}

						_fadeCounter--;
					}
					else
					{
						_lastUnfadedOutputTorque = outputTorque;
					}

					// update force feedback torque

					app.DirectInput.UpdateForceFeedbackEffect( outputTorque );
				}
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[RacingWheel] Exception caught: {exception.Message.Trim()}" );

				_unsuspendCounter = 500;
			}
		}
	}

	public void Tick( App app )
	{
		app.MainWindow.RacingWheel_PeakForce_Label.Content = $"{_peakTorque:F2}{DataContext.Instance.Localization[ "TorqueUnits" ]}";
		app.MainWindow.RacingWheel_AutoForce_Label.Content = $"{_autoTorque:F2}{DataContext.Instance.Localization[ "TorqueUnits" ]}";
	}
}
