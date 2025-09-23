
using System.Runtime.CompilerServices;

using SharpDX.DirectInput;

using MarvinsAIRARefactored.Windows;

namespace MarvinsAIRARefactored.Components;

public class DirectInput
{
	private class JoystickInfo
	{
		public required Joystick _joystick;
		public required Guid _instanceGuid;
		public required string _productName;

		public ObjectProperties? _xAxisProperties = null;
		public JoystickState _joystickState = new();
		public JoystickUpdate[]? _joystickUpdates = null;
		public bool _isDefunct = false;
	}

	public const int DI_FFNOMINALMAX = 10000;
	private const int DIEB_NOTRIGGER = -1;

	private static readonly Guid KeyboardGuid = new( "6f1d2b61-d5a0-11cf-bfc7-444553540000" );

	public bool ForceFeedbackInitialized { get => _forceFeedbackInitialized; }
	public Joystick? ForceFeedbackJoystick { get; private set; } = null;
	public float ForceFeedbackWheelPosition { get; private set; } = 0f;
	public float ForceFeedbackWheelVelocity { get; private set; } = 0f;

	public event Action<string, Guid, int, bool>? OnInput = null;

	public Dictionary<Guid, string> ForceFeedbackDeviceList { get; private set; } = [];

	private readonly SharpDX.DirectInput.DirectInput _directInput = new();

	private Keyboard? _keyboard = null;
	private KeyboardState _keyboardState = new();
	private KeyboardUpdate[]? _keyboardUpdates = null;
	private bool _keyboardIsDefunct = false;

	private readonly Dictionary<Guid, JoystickInfo> _joystickInfoDictionary = [];

	private bool _forceFeedbackInitialized = false;
	private Guid _forceFeedbackDeviceInstanceGuid = Guid.Empty;
	private EffectParameters? _forceFeedbackEffectParameters = null;
	private Effect? _forceFeedbackEffect = null;

	private bool _joystickInfoListNeedsToBeUpdated = false;

	private int _pollMutex = 0;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] Initialize >>>" );

		EnumerateDevices();

		app.HidHotplugMonitor.DeviceListMightHaveChanged += OnDeviceListMightHaveChanged;

		app.Logger.WriteLine( "[DirectInput] <<< Initialize" );
	}

	public void Shutdown()
	{
		ShutdownForceFeedback();

		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] Shutdown >>>" );

		foreach ( var keyValuePair in _joystickInfoDictionary )
		{
			var joystickInfo = keyValuePair.Value;

			joystickInfo._joystick.Dispose();
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

			ForceFeedbackJoystick = new Joystick( _directInput, _forceFeedbackDeviceInstanceGuid );
		}
		catch ( Exception exception )
		{
			app.Logger.WriteLine( $"[DirectInput] Exception caught: {exception.Message.Trim()}" );

			app.RacingWheel.NextRacingWheelGuid = Guid.Empty;

			_forceFeedbackDeviceInstanceGuid = Guid.Empty;
		}

		if ( ForceFeedbackJoystick != null )
		{
			app.Logger.WriteLine( "[DirectInput] Setting the cooperative level to exclusive and background mode" );

			ForceFeedbackJoystick.SetCooperativeLevel( app.MainWindow.WindowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background );

			app.Logger.WriteLine( "[DirectInput] Acquiring the joystick" );

			ForceFeedbackJoystick.Acquire();

			foreach ( var effectInfo in ForceFeedbackJoystick.GetEffects() )
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

					_forceFeedbackEffect = new Effect( ForceFeedbackJoystick, effectInfo.Guid, _forceFeedbackEffectParameters );

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

			if ( ForceFeedbackJoystick != null )
			{
				app.Logger.WriteLine( "[DirectInput] Unacquiring and disposing of the force feedback joystick" );

				try
				{
					ForceFeedbackJoystick.Unacquire();
				}
				catch ( Exception )
				{
				}

				ForceFeedbackJoystick.Dispose();

				ForceFeedbackJoystick = null;
			}

			app.Logger.WriteLine( "[DirectInput] <<< ShutdownForceFeedback" );
		}
	}

	public void PollDevices( float deltaSeconds )
	{
		if ( Interlocked.Exchange( ref _pollMutex, 1 ) != 0 )
		{
			return;
		}

		var app = App.Instance!;

		if ( _joystickInfoListNeedsToBeUpdated )
		{
			_joystickInfoListNeedsToBeUpdated = false;

			EnumerateDevices();
		}

		if ( _keyboard != null )
		{
			try
			{
				if ( !_keyboardIsDefunct )
				{
					_keyboard.Poll();
					_keyboard.GetCurrentState( ref _keyboardState );

					_keyboardUpdates = _keyboard.GetBufferedData();
				}
			}
			catch ( Exception )
			{
				_keyboardIsDefunct = true;
				_keyboardUpdates = null;
			}
		}

		foreach ( var keyValuePair in _joystickInfoDictionary )
		{
			var joystickInfo = keyValuePair.Value;

			try
			{
				if ( !joystickInfo._isDefunct )
				{
					joystickInfo._joystick.Poll();
					joystickInfo._joystick.GetCurrentState( ref joystickInfo._joystickState );

					joystickInfo._joystickUpdates = joystickInfo._joystick.GetBufferedData();

					if ( joystickInfo._instanceGuid == _forceFeedbackDeviceInstanceGuid )
					{
						if ( joystickInfo._xAxisProperties != null )
						{
							var lastForceFeedbackWheelPosition = ForceFeedbackWheelPosition;

							ForceFeedbackWheelPosition = (float) 2f * ( joystickInfo._joystickState.X - joystickInfo._xAxisProperties.Range.Minimum ) / ( joystickInfo._xAxisProperties.Range.Maximum - joystickInfo._xAxisProperties.Range.Minimum ) - 1f;
							ForceFeedbackWheelVelocity = ( ForceFeedbackWheelPosition - lastForceFeedbackWheelPosition ) / deltaSeconds;
						}
					}
				}
			}
			catch ( Exception )
			{
				joystickInfo._isDefunct = true;
				joystickInfo._joystickUpdates = null;
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

		foreach ( var keyValuePair in _joystickInfoDictionary )
		{
			var joystickInfo = keyValuePair.Value;

			if ( joystickInfo._joystickUpdates != null )
			{
				foreach ( var joystickUpdate in joystickInfo._joystickUpdates )
				{
					if ( ( joystickUpdate.Offset >= JoystickOffset.Buttons0 ) && ( joystickUpdate.Offset <= JoystickOffset.Buttons127 ) )
					{
						OnInput?.Invoke( joystickInfo._productName, joystickInfo._instanceGuid, joystickUpdate.Offset - JoystickOffset.Buttons0, joystickUpdate.Value != 0 );
					}
				}
			}
		}

		_pollMutex = 0;
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
		else if ( _joystickInfoDictionary.TryGetValue( deviceInstanceGuid, out var joystickInfo ) )
		{
			if ( joystickInfo._joystickState.Buttons[ buttonNumber ] )
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

	private void OnDeviceListMightHaveChanged( object? sender, EventArgs e )
	{
		_joystickInfoListNeedsToBeUpdated = true;
	}

	private void EnumerateDevices()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] EnumerateDevices >>>" );

		foreach ( var joystickInfo in _joystickInfoDictionary )
		{
			joystickInfo.Value._joystick.Unacquire();
		}

		_joystickInfoDictionary.Clear();
		ForceFeedbackDeviceList.Clear();

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
					ForceFeedbackDeviceList.Add( deviceInstance.InstanceGuid, description );
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
						_joystick = joystick,
						_productName = joystick.Information.ProductName,
						_instanceGuid = deviceInstance.InstanceGuid,
						_xAxisProperties = xAxisProperties
					};

					_joystickInfoDictionary.Add( deviceInstance.InstanceGuid, joystickInfo );
				}

				app.Logger.WriteLine( $"[DirectInput] ---" );
			}
		}

		var settings = DataContext.DataContext.Instance.Settings;

		if ( settings.RacingWheelSteeringDeviceGuid == Guid.Empty )
		{
			settings.RacingWheelSteeringDeviceGuid = ForceFeedbackDeviceList.FirstOrDefault().Key;
		}
		else if ( _forceFeedbackDeviceInstanceGuid != settings.RacingWheelSteeringDeviceGuid )
		{
			app.RacingWheel.NextRacingWheelGuid = settings.RacingWheelSteeringDeviceGuid;
		}

		MainWindow._racingWheelPage.UpdateSteeringDeviceOptions();

		app.Logger.WriteLine( "[DirectInput] <<< EnumerateDevices" );
	}
}
