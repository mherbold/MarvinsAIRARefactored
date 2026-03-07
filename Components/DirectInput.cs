
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
		public bool[] _povButtons = new bool[ PovVirtualButtonCount ];
		public bool _isDefunct = false;
	}

	public const int DI_FFNOMINALMAX = 10000;
	private const int DIEB_NOTRIGGER = -1;

	public const int PovButtonBase = 1000;
	public const int MaxPovHats = 4;
	public const int ButtonsPerPovHat = 4;
	public const int PovVirtualButtonCount = MaxPovHats * ButtonsPerPovHat;

	private static readonly Guid KeyboardGuid = new( "6f1d2b61-d5a0-11cf-bfc7-444553540000" );

	public string ForceFeedbackErrorMessage { get; private set; } = string.Empty;
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

	private readonly bool[] _streamDeckButtons = new bool[ 128 ];

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

		if ( app.TopLevelWindow.WindowHandle == IntPtr.Zero )
		{
			throw new Exception( "Top level window handle has not been created." );
		}

		try
		{
			app.Logger.WriteLine( "[DirectInput] Creating the force feedback joystick" );

			_forceFeedbackDeviceInstanceGuid = deviceGuid;

			ForceFeedbackJoystick = new Joystick( _directInput, _forceFeedbackDeviceInstanceGuid );

			if ( ForceFeedbackJoystick != null )
			{
				app.Logger.WriteLine( "[DirectInput] Setting the cooperative level to exclusive and background mode" );

				ForceFeedbackJoystick.SetCooperativeLevel( app.TopLevelWindow.WindowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background );

				app.Logger.WriteLine( "[DirectInput] Acquiring the joystick" );

				ForceFeedbackJoystick.Acquire();

				foreach ( var effectInfo in ForceFeedbackJoystick.GetEffects() )
				{
					if ( ( effectInfo.Type & EffectType.Hardware ) == EffectType.ConstantForce )
					{
						_forceFeedbackEffectParameters = new EffectParameters
						{
							Flags = EffectFlags.ObjectOffsets | EffectFlags.Cartesian,
							Duration = -1,
							Gain = DI_FFNOMINALMAX,
							SamplePeriod = 0,
							StartDelay = 0,
							TriggerButton = DIEB_NOTRIGGER,
							TriggerRepeatInterval = 0,
							Axes = [ 0 ],
							Directions = [ 1 ],
							Envelope = new Envelope(),
							Parameters = new ConstantForce { Magnitude = 0 }
						};

						app.Logger.WriteLine( "[DirectInput] Creating the constant force effect" );

						_forceFeedbackEffect = new Effect( ForceFeedbackJoystick, effectInfo.Guid, _forceFeedbackEffectParameters );

						_forceFeedbackEffect.Download();
						_forceFeedbackEffect.Start();

						break;
					}
				}

				if ( _forceFeedbackEffect == null )
				{
					app.Logger.WriteLine( "[DirectInput] Warning - constant force effect was not created (not supported?)" );
				}

				_forceFeedbackInitialized = true;
			}
		}
		catch ( Exception exception )
		{
			ForceFeedbackErrorMessage = exception.Message.Trim();

			app.Logger.WriteLine( $"[DirectInput] Exception caught: {ForceFeedbackErrorMessage}" );

			app.RacingWheel.NextRacingWheelGuid = Guid.Empty;

			_forceFeedbackDeviceInstanceGuid = Guid.Empty;
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
					else if ( TryGetPovHatIndex( joystickUpdate.Offset, out var hatIndex ) )
					{
						ProcessPovUpdate( joystickInfo, hatIndex, joystickUpdate.Value );
					}
				}
			}
		}

		_pollMutex = 0;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool TryGetPovHatIndex( JoystickOffset offset, out int hatIndex )
	{
		hatIndex = offset switch
		{
			JoystickOffset.PointOfViewControllers0 => 0,
			JoystickOffset.PointOfViewControllers1 => 1,
			JoystickOffset.PointOfViewControllers2 => 2,
			JoystickOffset.PointOfViewControllers3 => 3,
			_ => -1
		};

		return hatIndex >= 0;
	}

	private void ProcessPovUpdate( JoystickInfo joystickInfo, int hatIndex, int povValue )
	{
		var isUp = false;
		var isRight = false;
		var isDown = false;
		var isLeft = false;

		if ( povValue >= 0 )
		{
			var angle = povValue % 36000;

			if ( ( angle >= 33750 ) || ( angle < 2250 ) )
			{
				isUp = true;
			}
			else if ( angle < 6750 )
			{
				isUp = true;
				isRight = true;
			}
			else if ( angle < 11250 )
			{
				isRight = true;
			}
			else if ( angle < 15750 )
			{
				isRight = true;
				isDown = true;
			}
			else if ( angle < 20250 )
			{
				isDown = true;
			}
			else if ( angle < 24750 )
			{
				isDown = true;
				isLeft = true;
			}
			else if ( angle < 29250 )
			{
				isLeft = true;
			}
			else
			{
				isLeft = true;
				isUp = true;
			}
		}

		UpdatePovButton( joystickInfo, hatIndex, 0, isUp );    // Up
		UpdatePovButton( joystickInfo, hatIndex, 1, isRight ); // Right
		UpdatePovButton( joystickInfo, hatIndex, 2, isDown );  // Down
		UpdatePovButton( joystickInfo, hatIndex, 3, isLeft );  // Left
	}

	private void UpdatePovButton( JoystickInfo joystickInfo, int hatIndex, int directionIndex, bool newIsPressed )
	{
		var localIndex = ( hatIndex * ButtonsPerPovHat ) + directionIndex;

		if ( (uint) localIndex >= (uint) joystickInfo._povButtons.Length )
		{
			return;
		}

		var oldIsPressed = joystickInfo._povButtons[ localIndex ];

		if ( oldIsPressed == newIsPressed )
		{
			return;
		}

		joystickInfo._povButtons[ localIndex ] = newIsPressed;

		var virtualButtonNumber = PovButtonBase + localIndex;

		OnInput?.Invoke( joystickInfo._productName, joystickInfo._instanceGuid, virtualButtonNumber, newIsPressed );
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
		else if ( deviceInstanceGuid == StreamDeck.DeviceGuid )
		{
			if ( (uint) buttonNumber < (uint) _streamDeckButtons.Length )
			{
				if ( _streamDeckButtons[ buttonNumber ] )
				{
					return true;
				}
			}
		}
		else if ( _joystickInfoDictionary.TryGetValue( deviceInstanceGuid, out var joystickInfo ) )
		{
			if ( ( buttonNumber >= PovButtonBase ) && ( buttonNumber < ( PovButtonBase + PovVirtualButtonCount ) ) )
			{
				var povIndex = buttonNumber - PovButtonBase;

				if ( joystickInfo._povButtons[ povIndex ] )
				{
					return true;
				}
			}
			else
			{
				if ( (uint) buttonNumber < (uint) joystickInfo._joystickState.Buttons.Length )
				{
					if ( joystickInfo._joystickState.Buttons[ buttonNumber ] )
					{
						return true;
					}
				}
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

			_forceFeedbackEffect?.SetParameters( _forceFeedbackEffectParameters, EffectParameterFlags.TypeSpecificParameters );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void OnDeviceListMightHaveChanged( object? sender, EventArgs e )
	{
		_joystickInfoListNeedsToBeUpdated = true;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void InjectStreamDeckInput( string deviceProductName, int buttonNumber, bool isPressed )
	{
		if ( (uint) buttonNumber < (uint) _streamDeckButtons.Length )
		{
			_streamDeckButtons[ buttonNumber ] = isPressed;

			OnInput?.Invoke( deviceProductName, StreamDeck.DeviceGuid, buttonNumber, isPressed );
		}
	}

	private void EnumerateDevices()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] EnumerateDevices >>>" );

		if ( app.TopLevelWindow.WindowHandle == IntPtr.Zero )
		{
			throw new Exception( "Top level window handle has not been created." );
		}

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

					keyboard.SetCooperativeLevel( app.TopLevelWindow.WindowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background );

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

					joystick.SetCooperativeLevel( app.TopLevelWindow.WindowHandle, CooperativeLevel.NonExclusive | CooperativeLevel.Background );

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
