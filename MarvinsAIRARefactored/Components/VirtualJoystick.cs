
using MarvinsAIRARefactored.Classes;

using vJoyInterfaceWrap;

namespace MarvinsAIRARefactored.Components;

public class VirtualJoystick
{
	public uint JoystickId { get; set; } = 1;
	public float Steering { get; set; } = 0f;
	public float Brake { get; set; } = 0f;
	public float Throttle { get; set; } = 0f;
	public bool ShiftUp { get; set; } = false;
	public bool ShiftDown { get; set; } = false;
	public bool ActiveResetSave { get; set; } = false;
	public bool ActiveResetRun { get; set; } = false;

	private long _minimumX = 0;
	private long _maximumX = 0;

	private long _minimumY = 0;
	private long _maximumY = 0;

	private long _minimumZ = 0;
	private long _maximumZ = 0;

	private readonly vJoy _vJoy = new();

	private vJoy.JoystickState _joystickState;

	private bool _initialized = false;
	private bool _faulted = false;

	private int _consecutiveUpdateFailures = 0;

	// the accessibility steering passthrough writes the X axis straight to the device from the 360 Hz playout
	// thread (UpdateSteeringImmediately) while Tick pushes the whole state from the UI thread - this lock keeps
	// the two (and initialize / shutdown) from ever talking to the driver at the same time
	private readonly Lock _deviceLock = new();

	public bool Initialized { get => _initialized; }
	public bool Faulted { get => _faulted; }

	public void Initialize()
	{
		lock ( _deviceLock )
		{
			InitializeDevice();
		}
	}

	private void InitializeDevice()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[VirtualJoystick] Initialize >>>" );

		if ( !_vJoy.vJoyEnabled() )
		{
			app.Logger.WriteLine( "[VirtualJoystick] Driver is not enabled" );

			_faulted = true;
		}
		else
		{
			app.Logger.WriteLine( $"[VirtualJoystick] Vendor is {_vJoy.GetvJoyManufacturerString()}" );
			app.Logger.WriteLine( $"[VirtualJoystick] Vendor is {_vJoy.GetvJoyProductString()}" );
			app.Logger.WriteLine( $"[VirtualJoystick] Vendor is {_vJoy.GetvJoySerialNumberString()}" );

			UInt32 dllVer = 0, drvVer = 0;

			if ( !_vJoy.DriverMatch( ref dllVer, ref drvVer ) )
			{
				app.Logger.WriteLine( $"[VirtualJoystick] DLL version ({dllVer}) does not match driver version ({drvVer})" );
			}
			else
			{
				app.Logger.WriteLine( "[VirtualJoystick] DLL version is correct" );
			}

			var vjdStatus = _vJoy.GetVJDStatus( JoystickId );

			if ( ( vjdStatus != VjdStat.VJD_STAT_OWN ) && ( vjdStatus != VjdStat.VJD_STAT_FREE ) )
			{
				app.Logger.WriteLine( $"[VirtualJoystick] Joystick {JoystickId} is not owned or free" );

				_faulted = true;
			}
			else
			{
				if ( !_vJoy.AcquireVJD( JoystickId ) )
				{
					app.Logger.WriteLine( $"[VirtualJoystick] Joystick {JoystickId} could not be acquired" );

					_faulted = true;
				}
				else
				{
					_vJoy.ResetVJD( JoystickId );

					var xAxisExists = _vJoy.GetVJDAxisExist( JoystickId, HID_USAGES.HID_USAGE_X );
					var yAxisExists = _vJoy.GetVJDAxisExist( JoystickId, HID_USAGES.HID_USAGE_Y );
					var zAxisExists = _vJoy.GetVJDAxisExist( JoystickId, HID_USAGES.HID_USAGE_Z );

					_vJoy.GetVJDAxisMin( JoystickId, HID_USAGES.HID_USAGE_X, ref _minimumX );
					_vJoy.GetVJDAxisMax( JoystickId, HID_USAGES.HID_USAGE_X, ref _maximumX );

					_vJoy.GetVJDAxisMin( JoystickId, HID_USAGES.HID_USAGE_Y, ref _minimumY );
					_vJoy.GetVJDAxisMax( JoystickId, HID_USAGES.HID_USAGE_Y, ref _maximumY );

					_vJoy.GetVJDAxisMin( JoystickId, HID_USAGES.HID_USAGE_Z, ref _minimumZ );
					_vJoy.GetVJDAxisMax( JoystickId, HID_USAGES.HID_USAGE_Z, ref _maximumZ );

					app.Logger.WriteLine( $"[VirtualJoystick] Axes - X: {xAxisExists} ({_minimumX}..{_maximumX}), Y: {yAxisExists} ({_minimumY}..{_maximumY}), Z: {zAxisExists} ({_minimumZ}..{_maximumZ})" );

					// without an X axis the steering can never move - flag it as a fault instead of silently
					// writing a constant zero forever (the same goes for a degenerate axis range)
					if ( !xAxisExists || ( _minimumX == _maximumX ) )
					{
						app.Logger.WriteLine( $"[VirtualJoystick] Joystick {JoystickId} has no usable X axis - reconfigure the vJoy device with an X axis!" );

						_vJoy.RelinquishVJD( JoystickId );

						_faulted = true;
					}
					else
					{
						_initialized = true;
					}
				}
			}
		}

		app.Logger.WriteLine( $"[VirtualJoystick] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( $"[VirtualJoystick] Shutdown >>>" );

		lock ( _deviceLock )
		{
			if ( _initialized )
			{
				_vJoy.RelinquishVJD( JoystickId );

				_initialized = false;
			}
		}

		app.Logger.WriteLine( $"[VirtualJoystick] <<< Shutdown" );
	}

	/// <summary>
	/// Sets the steering and writes just the X axis to the device right away, instead of waiting for the next
	/// ~60 Hz Tick - used by the accessibility passthrough from the 360 Hz playout thread. Tick keeps pushing
	/// the same (latest) Steering value along with the rest of the state.
	/// </summary>
	public void UpdateSteeringImmediately( float steering )
	{
		lock ( _deviceLock )
		{
			Steering = steering;

			if ( _initialized )
			{
				var axisX = (int) MathF.Round( MathZ.Lerp( _minimumX, _maximumX, steering * 0.5f + 0.5f ) );

				// a failure here is left for Tick to report and recover (it re-acquires on a failed update)
				_vJoy.SetAxis( axisX, JoystickId, HID_USAGES.HID_USAGE_X );
			}
		}
	}

	public void Tick( App app )
	{
		lock ( _deviceLock )
		{
			UpdateDevice( app );
		}
	}

	private void UpdateDevice( App app )
	{
		if ( _initialized )
		{
			_joystickState.bDevice = (byte) JoystickId;

			_joystickState.AxisX = (int) MathF.Round( MathZ.Lerp( _minimumX, _maximumX, Steering * 0.5f + 0.5f ) );
			_joystickState.AxisY = (int) MathF.Round( MathZ.Lerp( _minimumY, _maximumY, Brake ) );
			_joystickState.AxisZ = (int) MathF.Round( MathZ.Lerp( _minimumZ, _maximumZ, Throttle ) );

			var shiftUp = ShiftUp ? (uint) 0x00000001 : 0;
			var shiftDown = ShiftDown ? (uint) 0x00000002 : 0;
			var activeResetSave = ActiveResetSave ? (uint) 0x00000004 : 0;
			var activeResetRun = ActiveResetRun ? (uint) 0x00000008 : 0;

			ShiftUp = false;
			ShiftDown = false;
			ActiveResetSave = false;
			ActiveResetRun = false;

			_joystickState.Buttons = shiftUp | shiftDown | activeResetSave | activeResetRun;

			if ( !_vJoy.UpdateVJD( JoystickId, ref _joystickState ) )
			{
				// log the first failure and then once every ~5 seconds - if the re-acquire below keeps
				// succeeding this would otherwise loop forever in silence with the axes frozen
				_consecutiveUpdateFailures++;

				if ( ( _consecutiveUpdateFailures == 1 ) || ( _consecutiveUpdateFailures % 300 == 0 ) )
				{
					app.Logger.WriteLine( $"[VirtualJoystick] UpdateVJD failed on joystick {JoystickId} ({_consecutiveUpdateFailures}x)" );
				}

				if ( !_vJoy.AcquireVJD( JoystickId ) )
				{
					app.Logger.WriteLine( $"[VirtualJoystick] Joystick {JoystickId} could not be re-acquired" );

					_initialized = false;
				}
			}
			else
			{
				_consecutiveUpdateFailures = 0;
			}
		}
	}
}
