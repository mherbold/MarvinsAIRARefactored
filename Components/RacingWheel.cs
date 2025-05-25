
using System.Runtime.CompilerServices;

using ComboBox = System.Windows.Controls.ComboBox;

namespace MarvinsAIRARefactored.Components;

public class RacingWheel
{
	private bool _suspend = true;
	private int _unsuspendCounter = 0;

	public bool Suspend
	{
		get => _suspend;

		set
		{
			if ( value != _suspend )
			{
				_suspend = value;

				var app = App.Instance;

				if ( app != null )
				{
					if ( _suspend )
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting suspend of force feedback" );
					}
					else
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting resumption of force feedback" );
					}

					app.MainWindow.UpdateRacingWheelPowerIcon();
				}
			}

			if ( value )
			{
				_unsuspendCounter = 1500;
			}
		}
	}

	private bool _active = false;
	private int _activeCounter = 0;
	private const int _activeCounterMaxValue = 1000;

	public bool Active
	{
		private get => _active;

		set
		{
			if ( value != _active )
			{
				_active = value;

				var app = App.Instance;

				if ( app != null )
				{
					if ( _active )
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade in of force feedback" );
					}
					else
					{
						app.Logger.WriteLine( "[RacingWheel] Requesting fade out of force feedback" );
					}

					app.MainWindow.UpdateRacingWheelPowerIcon();
				}

				_activeCounter = _activeCounterMaxValue;
			}
		}
	}

	public bool UpdateSteeringWheelTorqueBuffer { private get; set; } = true;

	private Guid? _racingWheelGuid = null;
	private Guid? _nextRacingWheelGuid = null;

	public Guid? NextRacingWheelGuid
	{
		private get => _nextRacingWheelGuid;

		set
		{
			var app = App.Instance;

			app?.Logger.WriteLine( "[RacingWheel] Setting next racing wheel GUID" );

			_nextRacingWheelGuid = value;
		}
	}

	private int _testSignalCounter = 0;

	public bool PlayTestSignal { private get; set; } = false;

	public bool Reset { private get; set; } = false;

	private readonly float[] _steeringWheelTorque360Hz = new float[ Simulator.IRSDK_360HZ_SAMPLES_PER_FRAME + 2 ];

	private const int _maxSteeringWheelTorque360HzIndex = Simulator.IRSDK_360HZ_SAMPLES_PER_FRAME + 1;

	private float _elapsedMilliseconds = 0f;

	public static void SetAlgorithmComboBoxItemsSource( ComboBox comboBox )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[RacingWheel] SetAlgorithmComboBoxItemsSource >>>" );

			comboBox.ItemsSource = new Dictionary<Settings.RacingWheelAlgorithmEnum, string>
			{
				{ Settings.RacingWheelAlgorithmEnum.Native60Hz, DataContext.Instance.Localization[ "Native60Hz" ] },
				{ Settings.RacingWheelAlgorithmEnum.Native360Hz, DataContext.Instance.Localization[ "Native360Hz" ] }
			};

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
					_testSignalCounter = 1000;

					PlayTestSignal = false;
				}

				var testSignalTorque = 0f;

				if ( _testSignalCounter > 0 )
				{
					testSignalTorque = MathF.Cos( _testSignalCounter * MathF.Tau / 12f ) * MathF.Sin( _testSignalCounter * MathF.Tau / 1000f * 2f ) * 0.2f;

					_testSignalCounter--;
				}

				// check if we want to reset the racing wheel device

				if ( Reset )
				{
					_nextRacingWheelGuid = _racingWheelGuid;

					Reset = false;
				}

				// if power button is off, or suspend is requested, or unsuspend counter is still counting down, then suspend the racing wheel force feedback

				if ( !DataContext.Instance.Settings.RacingWheelEnableForceFeedback || Suspend || ( _unsuspendCounter > 0 ) )
				{
					if ( _racingWheelGuid != null )
					{
						app.Logger.WriteLine( "[RacingWheel] Suspending racing wheel force feedback" );

						app.DirectInput.ShutdownForceFeedback();

						_nextRacingWheelGuid = _racingWheelGuid;

						_racingWheelGuid = null;
					}

					Interlocked.Decrement( ref _unsuspendCounter );

					return;
				}

				// if next racing wheel guid is set then re-initialize force feedback

				if ( _nextRacingWheelGuid != null )
				{
					if ( _racingWheelGuid != null )
					{
						app.Logger.WriteLine( "[RacingWheel] Uninitializing racing wheel force feedback" );

						app.DirectInput.ShutdownForceFeedback();

						_racingWheelGuid = null;
					}

					app.Logger.WriteLine( "[RacingWheel] Initializing racing wheel force feedback" );

					_racingWheelGuid = _nextRacingWheelGuid;

					_nextRacingWheelGuid = null;

					app.DirectInput.InitializeForceFeedback( (Guid) _racingWheelGuid );
				}

				// update elapsed milliseconds

				_elapsedMilliseconds += deltaMilliseconds;

				// update steering wheel torque data

				if ( UpdateSteeringWheelTorqueBuffer )
				{
					if ( _active )
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
					else if ( _activeCounter > 0 )
					{
						_steeringWheelTorque360Hz[ 0 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 1 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 2 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 3 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 4 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 5 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 6 ] = _steeringWheelTorque360Hz[ 7 ];
						_steeringWheelTorque360Hz[ 7 ] = _steeringWheelTorque360Hz[ 7 ];
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

				// this part is done only if we have a racing wheel device initialized

				if ( _racingWheelGuid != null )
				{
					// calculate output torque

					var outputTorque = 0f;

					switch ( DataContext.Instance.Settings.RacingWheelAlgorithm )
					{
						case Settings.RacingWheelAlgorithmEnum.Native60Hz:

							outputTorque = steeringWheelTorque60Hz / DataContext.Instance.Settings.RacingWheelMaxForce;
							break;

						case Settings.RacingWheelAlgorithmEnum.Native360Hz:

							outputTorque = steeringWheelTorque360Hz / DataContext.Instance.Settings.RacingWheelMaxForce;
							break;
					}

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

							if ( MathF.Sign( app.Simulator.SteeringWheelVelocity ) != sign )
							{
								outputTorque += app.Simulator.SteeringWheelVelocity * DataContext.Instance.Settings.RacingWheelSoftLockStrength;
							}
						}
					}

					// add test signal torque

					outputTorque += testSignalTorque;

					// apply friction torque

					if ( DataContext.Instance.Settings.RacingWheelFriction > 0f )
					{
						outputTorque += app.Simulator.SteeringWheelVelocity * DataContext.Instance.Settings.RacingWheelFriction;
					}

					// apply fade

					if ( _activeCounter > 0 )
					{
						var fadeScale = (float) _activeCounter / _activeCounterMaxValue;

						if ( _active )
						{
							fadeScale = 1f - fadeScale;
						}

						outputTorque *= fadeScale;

						_activeCounter--;
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
}
