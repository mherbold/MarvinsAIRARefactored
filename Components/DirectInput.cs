
using System.Runtime.CompilerServices;

using SharpDX.DirectInput;

using MarvinsAIRARefactored.Controls;

namespace MarvinsAIRARefactored.Components;

public class DirectInput
{
	private class JoystickInfo
	{
		public required Joystick Joystick;
		public required Guid InstanceGuid;
		public required string ProductName;

		public ObjectProperties? XAxisProperties = null;
		public JoystickState JoystickState = new();
		public JoystickUpdate[]? JoystickUpdates = null;
		public bool Defunct = false;
		public int NextRetryCounter = 0;
	}

	public const int DI_FFNOMINALMAX = 10000;
	private const int DIEB_NOTRIGGER = -1;

	private static readonly Guid KeyboardGuid = new( "6f1d2b61-d5a0-11cf-bfc7-444553540000" );

	public bool ForceFeedbackInitialized { get => _forceFeedbackInitialized; }
	public float ForceFeedbackWheelPosition { get; private set; } = 0f;
	public float ForceFeedbackWheelVelocity { get; private set; } = 0f;

	public event Action<string, Guid, int, bool>? OnInput = null;

	private readonly Dictionary<Guid, string> _forceFeedbackDeviceList = [];

	private readonly SharpDX.DirectInput.DirectInput _directInput = new();

	private Keyboard? _keyboard = null;
	private KeyboardState _keyboardState = new();
	private KeyboardUpdate[]? _keyboardUpdates = null;
	private bool _keyboardDefunct = false;
	private int _keyboardNextRetryCounter = 0;

	private readonly Dictionary<Guid, JoystickInfo> _joystickInfoList = [];

	private bool _forceFeedbackInitialized = false;
	private Guid _forceFeedbackDeviceInstanceGuid = Guid.Empty;
	private Joystick? _forceFeedbackJoystick = null;
	private EffectParameters? _forceFeedbackEffectParameters = null;
	private Effect? _forceFeedbackEffect = null;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] Initialize >>>" );

		EnumerateDevices();

		app.Logger.WriteLine( "[DirectInput] <<< Initialize" );
	}

	public void Shutdown()
	{
		ShutdownForceFeedback();

		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] Shutdown >>>" );

		foreach ( var keyValuePair in _joystickInfoList )
		{
			var joystickInfo = keyValuePair.Value;

			joystickInfo.Joystick.Dispose();
		}

		_keyboard?.Dispose();

		_keyboard = null;
		_keyboardUpdates = null;

		app.Logger.WriteLine( "[DirectInput] <<< Shutdown" );
	}

	public bool InitializeForceFeedback( Guid deviceGuid )
	{
		var success = false;

		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] InitializeForceFeedback >>>" );

		try
		{
			app.Logger.WriteLine( "[DirectInput] Creating the force feedback joystick" );

			_forceFeedbackDeviceInstanceGuid = deviceGuid;

			_forceFeedbackJoystick = new Joystick( _directInput, _forceFeedbackDeviceInstanceGuid );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[DirectInput] Exception caught: {exception.Message.Trim()}" );

			app.RacingWheel.NextRacingWheelGuid = Guid.Empty;
		}

		if ( _forceFeedbackJoystick != null )
		{
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

					break;
				}
			}

			if ( _forceFeedbackEffect == null )
			{
				app.Logger.WriteLine( "[DirectInput] Warning - constant force effect was not created (not supported?)" );
			}

			_forceFeedbackInitialized = true;

			app.MainWindow.UpdateRacingWheelForceFeedbackButtons();
		}

		app.Logger.WriteLine( "[DirectInput] <<< InitializeForceFeedback" );

		return success;
	}

	public void ShutdownForceFeedback()
	{
		var app = App.Instance!;

		if ( _forceFeedbackInitialized )
		{
			app.Logger.WriteLine( "[DirectInput] ShutdownForceFeedback >>>" );

			_forceFeedbackInitialized = false;

			app.MainWindow.UpdateRacingWheelForceFeedbackButtons();

			ForceFeedbackWheelPosition = 0f;
			ForceFeedbackWheelVelocity = 0f;

			_forceFeedbackEffectParameters = null;

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

	public void SetMairaComboBoxItemsSource( MairaComboBox mairaComboBox )
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] SetMairaComboBoxItemsSource >>>" );

		var dictionary = new Dictionary<Guid, string>();

		if ( _forceFeedbackDeviceList.Count == 0 )
		{
			dictionary.Add( Guid.Empty, DataContext.DataContext.Instance.Localization[ "NoFFBDevicesFound" ] );
		}

		_forceFeedbackDeviceList.ToList().ForEach( keyValuePair => dictionary[ keyValuePair.Key ] = keyValuePair.Value );

		mairaComboBox.ItemsSource = dictionary.OrderBy( keyValuePair => keyValuePair.Value );
		mairaComboBox.SelectedValue = DataContext.DataContext.Instance.Settings.RacingWheelSteeringDeviceGuid;

		app.Logger.WriteLine( "[DirectInput] <<< SetMairaComboBoxItemsSource" );
	}

	public void PollDevices( float deltaSeconds )
	{
		var app = App.Instance!;

		if ( _keyboard != null )
		{
			try
			{
				if ( _keyboardDefunct )
				{
					_keyboardNextRetryCounter--;

					if ( _keyboardNextRetryCounter == 0 )
					{
						_keyboard.SetCooperativeLevel( app.MainWindow.WindowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background );
						_keyboard.Acquire();

						_keyboardDefunct = false;
					}
				}
				else
				{
					_keyboard.Poll();
					_keyboard.GetCurrentState( ref _keyboardState );

					_keyboardUpdates = _keyboard.GetBufferedData();
				}
			}
			catch ( Exception )
			{
				_keyboardDefunct = true;
				_keyboardNextRetryCounter = 120;
				_keyboardUpdates = null;
			}
		}

		foreach ( var keyValuePair in _joystickInfoList )
		{
			var joystickInfo = keyValuePair.Value;

			try
			{
				if ( joystickInfo.Defunct )
				{
					joystickInfo.NextRetryCounter--;

					if ( joystickInfo.NextRetryCounter == 0 )
					{
						joystickInfo.Joystick.SetCooperativeLevel( app.MainWindow.WindowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background );
						joystickInfo.Joystick.Acquire();

						joystickInfo.Defunct = false;
					}
				}
				else
				{
					joystickInfo.Joystick.Poll();
					joystickInfo.Joystick.GetCurrentState( ref joystickInfo.JoystickState );

					joystickInfo.JoystickUpdates = joystickInfo.Joystick.GetBufferedData();

					if ( joystickInfo.InstanceGuid == _forceFeedbackDeviceInstanceGuid )
					{
						if ( joystickInfo.XAxisProperties != null )
						{
							var lastForceFeedbackWheelPosition = ForceFeedbackWheelPosition;

							ForceFeedbackWheelPosition = (float) 2f * ( joystickInfo.JoystickState.X - joystickInfo.XAxisProperties.Range.Minimum ) / ( joystickInfo.XAxisProperties.Range.Maximum - joystickInfo.XAxisProperties.Range.Minimum ) - 1f;
							ForceFeedbackWheelVelocity = ( ForceFeedbackWheelPosition - lastForceFeedbackWheelPosition ) / deltaSeconds;
						}
					}
				}
			}
			catch ( Exception )
			{
				joystickInfo.Defunct = true;
				joystickInfo.NextRetryCounter = 120;
				joystickInfo.JoystickUpdates = null;
			}
		}

		if ( _keyboardUpdates != null )
		{
			var keyboardText = DataContext.DataContext.Instance.Localization[ "Keyboard" ];

			foreach ( var keyboardUpdate in _keyboardUpdates )
			{
				OnInput?.Invoke( keyboardText, KeyboardGuid, keyboardUpdate.RawOffset, keyboardUpdate.IsPressed );
			}
		}

		foreach ( var keyValuePair in _joystickInfoList )
		{
			var joystickInfo = keyValuePair.Value;

			if ( joystickInfo.JoystickUpdates != null )
			{
				foreach ( var joystickUpdate in joystickInfo.JoystickUpdates )
				{
					if ( ( joystickUpdate.Offset >= JoystickOffset.Buttons0 ) && ( joystickUpdate.Offset <= JoystickOffset.Buttons127 ) )
					{
						OnInput?.Invoke( joystickInfo.ProductName, joystickInfo.InstanceGuid, joystickUpdate.Offset - JoystickOffset.Buttons0, joystickUpdate.Value != 0 );
					}
				}
			}
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool IsButtonDown( Guid deviceInstanceGuid, int buttonNumber )
	{
		if ( deviceInstanceGuid == KeyboardGuid )
		{
			if ( _keyboardState.IsPressed( (Key) buttonNumber ) )
			{
				return true;
			}
		}
		else if ( _joystickInfoList.TryGetValue( deviceInstanceGuid, out var joystickInfo ) )
		{
			if ( joystickInfo.JoystickState.Buttons[ buttonNumber ] )
			{
				return true;
			}
		}

		return false;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void UpdateForceFeedbackEffect( float magnitude )
	{
		if ( _forceFeedbackEffectParameters != null )
		{
			( (ConstantForce) _forceFeedbackEffectParameters.Parameters ).Magnitude = (int) Math.Clamp( magnitude * DI_FFNOMINALMAX, -DI_FFNOMINALMAX, DI_FFNOMINALMAX );

			_forceFeedbackEffect?.SetParameters( _forceFeedbackEffectParameters, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start );
		}
	}

	private void EnumerateDevices()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] EnumerateDevices >>>" );

		_joystickInfoList.Clear();
		_forceFeedbackDeviceList.Clear();

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

				var description = $"{deviceInstance.ProductName} [{deviceInstance.InstanceGuid}]";

				if ( deviceInstance.ForceFeedbackDriverGuid != Guid.Empty )
				{
					_forceFeedbackDeviceList.Add( deviceInstance.InstanceGuid, description );
				}

				if ( deviceInstance.Type == DeviceType.Keyboard )
				{
					app.Logger.WriteLine( "[DirectInput] Creating the keyboard" );

					var keyboard = new Keyboard( _directInput );

					keyboard.Properties.BufferSize = 128;

					app.Logger.WriteLine( "[DirectInput] Setting the cooperative level to non-exclusive and background mode" );

					keyboard.SetCooperativeLevel( app.MainWindow.WindowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background );

					app.Logger.WriteLine( "[DirectInput] Acquiring the keyboard" );

					keyboard.Acquire();

					_keyboard = keyboard;
				}
				else
				{
					app.Logger.WriteLine( "[DirectInput] Creating the joystick" );

					var joystick = new Joystick( _directInput, deviceInstance.InstanceGuid );

					joystick.Properties.BufferSize = 128;

					app.Logger.WriteLine( "[DirectInput] Setting the cooperative level to non-exclusive and background mode" );

					joystick.SetCooperativeLevel( app.MainWindow.WindowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background );

					app.Logger.WriteLine( "[DirectInput] Acquiring the joystick" );

					joystick.Acquire();

					app.Logger.WriteLine( "[DirectInput] Getting the X-Axis properties" );

					var objectList = joystick.GetObjects( DeviceObjectTypeFlags.AbsoluteAxis );

					ObjectProperties? xAxisProperties = null;

					foreach ( var obj in objectList )
					{
						if ( ( obj.UsagePage == 0x01 ) && ( obj.Usage == 0x30 ) )
						{
							xAxisProperties = joystick.GetObjectPropertiesById( obj.ObjectId );
						}
					}

					var joystickInfo = new JoystickInfo()
					{
						Joystick = joystick,
						ProductName = joystick.Information.ProductName,
						InstanceGuid = deviceInstance.InstanceGuid,
						XAxisProperties = xAxisProperties
					};

					_joystickInfoList.Add( deviceInstance.InstanceGuid, joystickInfo );
				}

				app.Logger.WriteLine( $"[DirectInput] ---" );
			}
		}

		if ( DataContext.DataContext.Instance.Settings.RacingWheelSteeringDeviceGuid == Guid.Empty )
		{
			DataContext.DataContext.Instance.Settings.RacingWheelSteeringDeviceGuid = _forceFeedbackDeviceList.FirstOrDefault().Key;
		}

		app.Logger.WriteLine( "[DirectInput] <<< EnumerateDevices" );
	}
}
