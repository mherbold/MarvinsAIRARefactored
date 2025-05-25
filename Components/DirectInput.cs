
using System.Runtime.CompilerServices;

using Image = System.Windows.Controls.Image;

using SharpDX.DirectInput;

namespace MarvinsAIRARefactored.Components;

public class DirectInput
{
	public const int DI_FFNOMINALMAX = 10000;
	private const int DIEB_NOTRIGGER = -1;

	public float ForceFeedbackWheelPosition { get; private set; } = 0f;
	public float ForceFeedbackWheelVelocity { get; private set; } = 0f;

	private readonly Dictionary<Guid, string> _completeDeviceInstanceList = [];
	private readonly Dictionary<Guid, string> _forceFeedbackDeviceInstanceList = [];

	private readonly SharpDX.DirectInput.DirectInput _directInput = new();

	private bool _forceFeedbackInitialized = false;
	private bool _forceFeedbackDeviceIsBeingPolled = false;
	private Joystick? _forceFeedbackJoystick = null;
	private ObjectProperties? _forceFeedbackXAxisProperties = null;
	private EffectParameters? _forceFeedbackEffectParameters = null;
	private Effect? _forceFeedbackEffect = null;
	private JoystickState? _forceFeedbackJoystickState = null;

	private readonly Graph _outputTorqueGraph;
	private readonly Statistics _outputTorqueStatistics = new( 500 );

	public DirectInput( Image outputTorqueGraphImage )
	{
		var app = App.Instance;

		app?.Logger.WriteLine( $"[DirectInput] Constructor >>>" );

		_outputTorqueGraph = new Graph( outputTorqueGraphImage );

		app?.Logger.WriteLine( $"[DirectInput] <<< Constructor" );
	}

	public void Initialize()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectInput] Initialize >>>" );

			EnumerateAttachedDevices();

			app.Logger.WriteLine( "[DirectInput] <<< Initialize" );
		}
	}

	public void InitializeForceFeedback( Guid deviceGuid )
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectInput] InitializeForceFeedback >>>" );

			app.Logger.WriteLine( "[DirectInput] Creating the force feedback joystick" );

			_forceFeedbackJoystick = new Joystick( _directInput, deviceGuid );

			app.Logger.WriteLine( "[DirectInput] Getting the X-Axis properties" );

			var objectList = _forceFeedbackJoystick.GetObjects( DeviceObjectTypeFlags.AbsoluteAxis );

			foreach ( var obj in objectList )
			{
				if ( ( obj.UsagePage == 0x01 ) && ( obj.Usage == 0x30 ) )
				{
					_forceFeedbackXAxisProperties = _forceFeedbackJoystick.GetObjectPropertiesById( obj.ObjectId );
				}
			}

			if ( _forceFeedbackXAxisProperties == null )
			{
				app.Logger.WriteLine( "[DirectInput] Warning - We were not able to find the X-Axis properties!" );
			}

			app.Logger.WriteLine( "[DirectInput] Setting the cooperative level to exclusive and background mode" );

			_forceFeedbackJoystick.SetCooperativeLevel( app.MainWindow.WindowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background );

			app.Logger.WriteLine( "[DirectInput] Acquiring the joystick" );

			_forceFeedbackJoystick.Acquire();

			foreach ( var effectInfo in _forceFeedbackJoystick.GetEffects() )
			{
				if ( ( effectInfo.Type & EffectType.Hardware ) == EffectType.ConstantForce )
				{
					_forceFeedbackEffectParameters = new EffectParameters
					{
						Flags = EffectFlags.ObjectOffsets | EffectFlags.Cartesian,
						Duration = 500000,
						Gain = DI_FFNOMINALMAX,
						SamplePeriod = 0,
						StartDelay = 0,
						TriggerButton = DIEB_NOTRIGGER,
						TriggerRepeatInterval = int.MaxValue,
						Axes = [ 0 ],
						Directions = [ 0 ],
						Envelope = new Envelope(),
						Parameters = new ConstantForce { Magnitude = 0 }
					};

					app.Logger.WriteLine( "[DirectInput] Creating the constant force effect" );

					_forceFeedbackEffect = new Effect( _forceFeedbackJoystick, effectInfo.Guid, _forceFeedbackEffectParameters );

					_forceFeedbackEffect.Download();
				}
			}

			if ( _forceFeedbackEffect == null )
			{
				app.Logger.WriteLine( "[DirectInput] Warning - constant force effect was not created (not supported?)" );
			}

			_forceFeedbackInitialized = true;

			app.Logger.WriteLine( "[DirectInput] <<< InitializeForceFeedback" );
		}
	}

	public void ShutdownForceFeedback()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectInput] ShutdownForceFeedback >>>" );

			_forceFeedbackInitialized = false;

			while ( _forceFeedbackDeviceIsBeingPolled )
			{
				Thread.Sleep( 0 );
			}

			_forceFeedbackXAxisProperties = null;
			_forceFeedbackEffectParameters = null;
			_forceFeedbackJoystickState = null;

			if ( _forceFeedbackEffect != null )
			{
				app.Logger.WriteLine( "[DirectInput] Stopping and diposing of the force feedback effect" );

				try
				{
					_forceFeedbackEffect.Stop();
				}
				catch ( Exception )
				{
				}

				_forceFeedbackEffect.Dispose();

				_forceFeedbackEffect = null;
			}

			if ( _forceFeedbackJoystick != null )
			{
				app.Logger.WriteLine( "[DirectInput] Unacquiring and disposing of the force feedback joystick" );

				try
				{
					_forceFeedbackJoystick.Unacquire();
				}
				catch ( Exception )
				{
				}

				_forceFeedbackJoystick.Dispose();

				_forceFeedbackJoystick = null;
			}

			app.Logger.WriteLine( "[DirectInput] <<< ShutdownForceFeedback" );
		}
	}

	public void PollForceFeedbackDevice( float deltaSeconds )
	{
		_forceFeedbackDeviceIsBeingPolled = true;

		if ( ( _forceFeedbackJoystick != null ) && ( _forceFeedbackXAxisProperties != null ) && _forceFeedbackInitialized )
		{
			_forceFeedbackJoystick.Poll();

			_forceFeedbackJoystickState = _forceFeedbackJoystick.GetCurrentState();

			var lastForceFeedbackWheelPosition = ForceFeedbackWheelPosition;

			ForceFeedbackWheelPosition = (float) 2f * ( _forceFeedbackJoystickState.X - _forceFeedbackXAxisProperties.Range.Minimum ) / ( _forceFeedbackXAxisProperties.Range.Maximum - _forceFeedbackXAxisProperties.Range.Minimum ) - 1f;
			ForceFeedbackWheelVelocity = ( ForceFeedbackWheelPosition - lastForceFeedbackWheelPosition ) / deltaSeconds;

			var app = App.Instance;

			if ( app != null )
			{
				if ( app.MainWindow.DebugTabItemIsVisible )
				{
					app.Debug.Update( ForceFeedbackWheelVelocity );
				}
			}
		}

		_forceFeedbackDeviceIsBeingPolled = false;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void UpdateForceFeedbackEffect( float magnitude )
	{
		var app = App.Instance;

		if ( app != null )
		{
			if ( app.MainWindow.GraphsTabItemIsVisible )
			{
				_outputTorqueStatistics.Update( magnitude );

				var red = (uint) 0;
				var green = (uint) 0;
				var blue = (uint) 0;

				if ( MathF.Abs( magnitude ) <= 1f )
				{
					green = 255;
					blue = 255;
				}
				else
				{
					red = 255;
				}

				_outputTorqueGraph.DrawGradientLine( magnitude, red, green, blue );
				_outputTorqueGraph.Advance();
			}

			if ( _forceFeedbackEffectParameters != null )
			{
				( (ConstantForce) _forceFeedbackEffectParameters.Parameters ).Magnitude = (int) Math.Clamp( magnitude * DI_FFNOMINALMAX, -DI_FFNOMINALMAX, DI_FFNOMINALMAX );

				_forceFeedbackEffect?.SetParameters( _forceFeedbackEffectParameters, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start );
			}
		}
	}

	private void EnumerateAttachedDevices()
	{
		var app = App.Instance;

		if ( app != null )
		{
			app.Logger.WriteLine( "[DirectInput] EnumerateAttachedDevices >>>" );

			_completeDeviceInstanceList.Clear();

			var deviceInstanceList = _directInput.GetDevices( DeviceClass.All, DeviceEnumerationFlags.AttachedOnly );

			foreach ( var deviceInstance in deviceInstanceList )
			{
				if ( ( deviceInstance.Type != DeviceType.Device ) && ( deviceInstance.Type != DeviceType.Mouse ) )
				{
					app.Logger.WriteLine( $"[DirectInput] Type: {deviceInstance.Type}" );
					app.Logger.WriteLine( $"[DirectInput] Subtype: {deviceInstance.Subtype}" );
					app.Logger.WriteLine( $"[DirectInput] Product name: {deviceInstance.ProductName}" );
					app.Logger.WriteLine( $"[DirectInput] Product GUID: {deviceInstance.ProductGuid}" );
					app.Logger.WriteLine( $"[DirectInput] Instance name: {deviceInstance.InstanceName}" );
					app.Logger.WriteLine( $"[DirectInput] Instance GUID: {deviceInstance.InstanceGuid}" );
					app.Logger.WriteLine( $"[DirectInput] Force feedback driver GUID: {deviceInstance.ForceFeedbackDriverGuid}" );
					app.Logger.WriteLine( $"[DirectInput] ---" );

					var description = $"{deviceInstance.ProductName} [{deviceInstance.InstanceGuid}]";

					_completeDeviceInstanceList.Add( deviceInstance.InstanceGuid, description );

					if ( deviceInstance.ForceFeedbackDriverGuid != Guid.Empty )
					{
						_forceFeedbackDeviceInstanceList.Add( deviceInstance.InstanceGuid, description );
					}
				}
			}

			if ( _forceFeedbackDeviceInstanceList.Count == 0 )
			{
				_forceFeedbackDeviceInstanceList.Add( Guid.Empty, DataContext.Instance.Localization[ "NoAttachedFFBDevicesFound" ] );
			}

			app.MainWindow.RacingWheelDevice_ComboBox.ItemsSource = _forceFeedbackDeviceInstanceList.OrderBy( keyValuePair => keyValuePair.Value );

			if ( DataContext.Instance.Settings.RacingWheelDeviceGuid == Guid.Empty )
			{
				DataContext.Instance.Settings.RacingWheelDeviceGuid = _forceFeedbackDeviceInstanceList.FirstOrDefault().Key;
			}

			app.Logger.WriteLine( "[DirectInput] <<< EnumerateAttachedDevices" );
		}
	}

	public void Tick( App app )
	{
		if ( app.MainWindow.GraphsTabItemIsVisible )
		{
			_outputTorqueGraph.UpdateImage();

			app.MainWindow.OutputTorque_MinMaxAvg.Content = $"{_outputTorqueStatistics.MinimumValue,5:F2} {_outputTorqueStatistics.MaximumValue,5:F2} {_outputTorqueStatistics.AverageValue,5:F2}";
			app.MainWindow.OutputTorque_VarStdDev.Content = $"{_outputTorqueStatistics.Variance,5:F2} {_outputTorqueStatistics.StandardDeviation,5:F2}";
		}
	}
}
