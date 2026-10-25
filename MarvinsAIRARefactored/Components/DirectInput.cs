
using System.Diagnostics;
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

	// physical steering axis of the FFB wheel, normalized to -1..+1 — the game bridges use this to reconstruct
	// the real wheel angle when a game clamps its reported steering value at full lock
	public float ForceFeedbackWheelPosition { get; private set; } = 0f;

	public event Action<string, Guid, int, bool>? OnInput = null;

	public Dictionary<Guid, string> ForceFeedbackDeviceList { get; private set; } = [];

	private readonly SharpDX.DirectInput.DirectInput _directInput = new();

	private Keyboard? _keyboard = null;
	private KeyboardState _keyboardState = new();
	private KeyboardUpdate[]? _keyboardUpdates = null;
	private bool _keyboardIsDefunct = false;

	private readonly Dictionary<Guid, JoystickInfo> _joystickInfoDictionary = [];

	// Product GUID per device instance, kept so callers can recover the USB vendor and product ids of a
	// selected device (see TryGetUsbIds). Separate from _joystickInfoDictionary because it is also wanted
	// for the force feedback device before any joystick has been polled.
	private readonly Dictionary<Guid, Guid> _productGuidDictionary = [];

	// volatile - read by the effect-write failure check to tell a concurrent app-exit shutdown apart from a lost device
	private volatile bool _forceFeedbackInitialized = false;
	private Guid _forceFeedbackDeviceInstanceGuid = Guid.Empty;

	// force feedback recovery - a failed initialization or a lost device (another app took or reset the wheel,
	// driver reset, unplug) is retried by RacingWheel after ForceFeedbackRetryDelayMilliseconds, which backs off
	// exponentially while the failures keep coming so a device that stays unavailable can't spin re-init attempts
	private const float ForceFeedbackRetryBaseDelayMS = 1000f;
	private const float ForceFeedbackRetryMaximumDelayMS = 15000f;

	// a device that ran this long before being lost counts as healthy, so its loss starts the backoff over
	private const double ForceFeedbackHealthyRunTimeMS = 10000.0;

	public float ForceFeedbackRetryDelayMilliseconds { get; private set; } = ForceFeedbackRetryBaseDelayMS;

	private int _forceFeedbackConsecutiveFailureCount = 0;
	private long _forceFeedbackInitializedTimestamp = 0;
	private bool _forceFeedbackRecoveryPending = false;
	private bool _forceFeedbackLossReported = false;
	private EffectParameters? _forceFeedbackEffectParameters = null;
	private Effect? _forceFeedbackEffect = null;

	// last magnitude actually written to the device (int.MinValue = force the next write through) plus a
	// periodic-refresh clock — see UpdateForceFeedbackEffect
	private int _lastForceFeedbackMagnitude = int.MinValue;
	private long _lastForceFeedbackWriteTimestamp = 0;

	// unchanged magnitudes are still rewritten this often as insurance against wheelbase drivers with
	// inactivity watchdogs (the started infinite-duration effect holds by spec, but 4 writes/second is free)
	private static readonly long _forceFeedbackRefreshTimestampTicks = Stopwatch.Frequency / 4;

	private bool _joystickInfoListNeedsToBeUpdated = false;

	// set when the hot-plug monitor saw the steering device itself arrive or leave - a replug keeps the same
	// instance guid, so the next rescan has to re-open force feedback explicitly (see EnumerateDevices)
	private volatile bool _steeringDeviceWasReplugged = false;

	// the steering device's joystick info, resolved on each poll - the accessibility passthrough samples this one
	// device from the 360 Hz playout thread (TrySampleSteeringWheelPosition), so its Poll/GetCurrentState calls
	// are serialized against PollDevices by locking the JoystickInfo itself, and the sample goes into a private
	// state object so the shared _joystickState (read by IsButtonDown) is never written from two threads
	private volatile JoystickInfo? _steeringJoystickInfo = null;

	private JoystickState _steeringSampleJoystickState = new();

	private readonly bool[] _streamDeckButtons = new bool[ 128 ];

	private int _pollMutex = 0;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[DirectInput] Initialize >>>" );

		EnumerateDevices();

		app.HidHotPlugMonitor.DeviceListMightHaveChanged += OnDeviceListMightHaveChanged;

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

			_lastForceFeedbackMagnitude = int.MinValue;

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

				success = true;
			}
		}
		catch ( Exception exception )
		{
			ForceFeedbackErrorMessage = exception.Message.Trim();

			app.Logger.WriteLine( $"[DirectInput] Exception caught: {ForceFeedbackErrorMessage}" );

			// release whatever got created before the failure - a joystick left acquired here would hold the wheel
			// exclusively with no effect on it, and would block our own retry
			ReleaseForceFeedbackObjects();

			_forceFeedbackDeviceInstanceGuid = Guid.Empty;

			// keep the device selected and let RacingWheel retry after a backoff (it used to be blanked here, which
			// left force feedback dead until the device selection changed or MAIRA was restarted)
			_forceFeedbackConsecutiveFailureCount++;
			_forceFeedbackRecoveryPending = true;

			ForceFeedbackRetryDelayMilliseconds = GetForceFeedbackRetryDelayMS( _forceFeedbackConsecutiveFailureCount );

			app.Logger.WriteLine( $"[DirectInput] Force feedback initialization failed ({_forceFeedbackConsecutiveFailureCount} in a row) - retrying in {ForceFeedbackRetryDelayMilliseconds / 1000f:F0} s" );
		}

		if ( success )
		{
			_forceFeedbackInitializedTimestamp = Stopwatch.GetTimestamp();
			_forceFeedbackLossReported = false;

			if ( _forceFeedbackRecoveryPending )
			{
				_forceFeedbackRecoveryPending = false;

				app.Logger.WriteLine( "[DirectInput] Force feedback recovered - the device is initialized again" );
			}
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

			ReleaseForceFeedbackObjects();

			app.Logger.WriteLine( "[DirectInput] <<< ShutdownForceFeedback" );
		}
	}

	/// <summary>
	/// Clears the retry backoff so the next force feedback initialization attempt happens right away - called on an
	/// explicit reset (the Reset button / mapped reset, or a simulator connecting).
	/// </summary>
	public void ResetForceFeedbackRetryBackoff()
	{
		_forceFeedbackConsecutiveFailureCount = 0;

		ForceFeedbackRetryDelayMilliseconds = ForceFeedbackRetryBaseDelayMS;
	}

	private static float GetForceFeedbackRetryDelayMS( int consecutiveFailureCount )
	{
		var exponent = Math.Clamp( consecutiveFailureCount - 1, 0, 16 );

		return MathF.Min( ForceFeedbackRetryBaseDelayMS * ( 1 << exponent ), ForceFeedbackRetryMaximumDelayMS );
	}

	private void ReleaseForceFeedbackObjects()
	{
		var app = App.Instance!;

		_forceFeedbackEffectParameters = null;

		_lastForceFeedbackMagnitude = int.MinValue;

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

			try
			{
				_forceFeedbackEffect.Dispose();
			}
			catch ( Exception )
			{
			}

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

			try
			{
				ForceFeedbackJoystick.Dispose();
			}
			catch ( Exception )
			{
			}

			ForceFeedbackJoystick = null;
		}
	}

	public void PollDevices()
	{
		if ( Interlocked.Exchange( ref _pollMutex, 1 ) != 0 )
		{
			return;
		}

		var app = App.Instance!;

		// track the wheel position on the SELECTED steering device from settings, not on the initialized
		// force feedback device - force feedback is only initialized once a simulator connects (RacingWheel
		// runs on the playout timer, which is suspended until then), but the wheel position is needed
		// earlier than that (the game bridge's vJoy steering passthrough runs while the game is still in
		// its menus, before any telemetry flows)
		var steeringDeviceInstanceGuid = ( _forceFeedbackDeviceInstanceGuid != Guid.Empty ) ? _forceFeedbackDeviceInstanceGuid : DataContext.DataContext.Instance.Settings.RacingWheelSteeringDeviceGuid;

		if ( _joystickInfoListNeedsToBeUpdated )
		{
			_joystickInfoListNeedsToBeUpdated = false;

			_steeringJoystickInfo = null;

			EnumerateDevices();
		}

		JoystickInfo? steeringJoystickInfo = null;

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
					lock ( joystickInfo )
					{
						joystickInfo._joystick.Poll();
						joystickInfo._joystick.GetCurrentState( ref joystickInfo._joystickState );

						joystickInfo._joystickUpdates = joystickInfo._joystick.GetBufferedData();
					}

					if ( joystickInfo._instanceGuid == steeringDeviceInstanceGuid )
					{
						if ( joystickInfo._xAxisProperties != null )
						{
							ForceFeedbackWheelPosition = NormalizeXAxis( joystickInfo, joystickInfo._joystickState.X );

							steeringJoystickInfo = joystickInfo;
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

		_steeringJoystickInfo = steeringJoystickInfo;

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
	private static float NormalizeXAxis( JoystickInfo joystickInfo, int xAxisValue )
	{
		var xAxisRange = joystickInfo._xAxisProperties!.Range;

		return (float) 2f * ( xAxisValue - xAxisRange.Minimum ) / ( xAxisRange.Maximum - xAxisRange.Minimum ) - 1f;
	}

	/// <summary>
	/// Reads the steering device's X axis right now (normalized -1..+1, left = -1, the same scale as
	/// ForceFeedbackWheelPosition) - called from the 360 Hz playout thread by the accessibility passthrough so
	/// the steering reaches vJoy without waiting for the ~60 Hz device poll. Returns false until PollDevices has
	/// resolved the steering device (or while the device list is being rebuilt).
	/// </summary>
	public bool TrySampleSteeringWheelPosition( out float position )
	{
		position = 0f;

		var joystickInfo = _steeringJoystickInfo;

		if ( ( joystickInfo == null ) || joystickInfo._isDefunct || ( joystickInfo._xAxisProperties == null ) )
		{
			return false;
		}

		try
		{
			lock ( joystickInfo )
			{
				joystickInfo._joystick.Poll();
				joystickInfo._joystick.GetCurrentState( ref _steeringSampleJoystickState );

				position = NormalizeXAxis( joystickInfo, _steeringSampleJoystickState.X );
			}

			return true;
		}
		catch ( Exception )
		{
			// the device is going away (unplugged, or unacquired by a device list rebuild) - PollDevices marks it
			// defunct on its next pass, and the caller falls back to the last polled position until then
			return false;
		}
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

	/// <summary>
	/// Writes the constant force magnitude to the wheel. Returns false when the write failed because the device was
	/// lost (another app took or reset it, a driver reset, an unplug) - the caller then shuts force feedback down and
	/// re-initializes it after ForceFeedbackRetryDelayMilliseconds. Returns true otherwise, including when nothing
	/// was written (no device, or an unchanged magnitude).
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool UpdateForceFeedbackEffect( float magnitude )
	{
		// local snapshots — ShutdownForceFeedback (app exit, suspend, device switch) nulls these fields from
		// another thread, so the null check below must test the same references we then dereference; testing
		// the fields directly is a race the 360 Hz playout thread eventually loses
		var forceFeedbackEffectParameters = _forceFeedbackEffectParameters;
		var forceFeedbackEffect = _forceFeedbackEffect;

		if ( forceFeedbackEffectParameters != null )
		{
			var clampedMagnitude = (int) Math.Clamp( magnitude * DI_FFNOMINALMAX, -DI_FFNOMINALMAX, DI_FFNOMINALMAX );

			// skip the native SetParameters marshal when the quantized magnitude hasn't changed — the started
			// infinite-duration constant force keeps applying; a periodic refresh still goes through (see fields)

			var timestamp = Stopwatch.GetTimestamp();

			if ( ( clampedMagnitude == _lastForceFeedbackMagnitude ) && ( ( timestamp - _lastForceFeedbackWriteTimestamp ) < _forceFeedbackRefreshTimestampTicks ) )
			{
				return true;
			}

			( (ConstantForce) forceFeedbackEffectParameters.Parameters ).Magnitude = clampedMagnitude;

			try
			{
				forceFeedbackEffect?.SetParameters( forceFeedbackEffectParameters, EffectParameterFlags.TypeSpecificParameters );
			}
			catch ( Exception exception )
			{
				// a concurrent shutdown (app exit) can stop/dispose the effect between the snapshot and this call -
				// the device is going away on purpose, so that failed final write is harmless
				if ( !_forceFeedbackInitialized || !ReferenceEquals( forceFeedbackEffect, _forceFeedbackEffect ) )
				{
					return true;
				}

				// anything else means the effect is no longer reaching the wheel - swallowing it here left force
				// feedback silently dead until a manual reset, so report it and let the caller re-initialize
				ReportForceFeedbackLoss( exception );

				return false;
			}

			_lastForceFeedbackMagnitude = clampedMagnitude;
			_lastForceFeedbackWriteTimestamp = timestamp;
		}

		return true;
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private void ReportForceFeedbackLoss( Exception exception )
	{
		ForceFeedbackErrorMessage = exception.Message.Trim();

		// log once per initialized device - the caller shuts force feedback down right away, so this is a guard
		if ( _forceFeedbackLossReported )
		{
			return;
		}

		_forceFeedbackLossReported = true;

		// a device that ran healthy for a while before being lost starts the backoff over; one that is lost again
		// right after being re-initialized keeps backing off
		if ( Stopwatch.GetElapsedTime( _forceFeedbackInitializedTimestamp ).TotalMilliseconds >= ForceFeedbackHealthyRunTimeMS )
		{
			_forceFeedbackConsecutiveFailureCount = 0;
		}

		_forceFeedbackConsecutiveFailureCount++;
		_forceFeedbackRecoveryPending = true;

		ForceFeedbackRetryDelayMilliseconds = GetForceFeedbackRetryDelayMS( _forceFeedbackConsecutiveFailureCount );

		var resultCode = ( exception is SharpDX.SharpDXException sharpDXException ) ? sharpDXException.ResultCode : new SharpDX.Result( exception.HResult );

		var reason = IsDeviceLossResult( resultCode ) ? "Force feedback device lost" : "Unexpected force feedback error";

		App.Instance!.Logger.WriteLine( $"[DirectInput] {reason} while updating the effect (0x{resultCode.Code:X8}: {ForceFeedbackErrorMessage}) - re-initializing in {ForceFeedbackRetryDelayMilliseconds / 1000f:F0} s ({_forceFeedbackConsecutiveFailureCount} in a row)" );
	}

	// the HRESULTs DirectInput returns when another application took the device (exclusive acquire), the device or
	// its driver was reset, or it was unplugged - the effect needs a fresh acquire and download to work again
	private static bool IsDeviceLossResult( SharpDX.Result resultCode )
	{
		return ( resultCode == ResultCode.InputLost )
			|| ( resultCode == ResultCode.NotAcquired )
			|| ( resultCode == ResultCode.NotExclusiveAcquired )
			|| ( resultCode == ResultCode.OtherApplicationHasPriority )
			|| ( resultCode == ResultCode.NotDownloaded )
			|| ( resultCode == ResultCode.Unplugged );
	}

	// True once EnumerateDevices has seen this device instance. Lets a caller tell "the device list has not
	// been built yet" apart from "it has, and this is not a device I care about", so it knows whether a
	// failed TryGetUsbIds is worth retrying.
	public bool IsDeviceEnumerated( Guid deviceInstanceGuid ) => _productGuidDictionary.ContainsKey( deviceInstanceGuid );

	// DirectInput packs a HID device's USB vendor and product ids into the product GUID: the first DWORD
	// is the two ids, and the trailing bytes spell "PIDVID". Devices that are not HID (and so carry no
	// ids) use a different GUID shape, hence the signature check before trusting the numbers.
	public bool TryGetUsbIds( Guid deviceInstanceGuid, out ushort vendorId, out ushort productId )
	{
		vendorId = 0;
		productId = 0;

		if ( !_productGuidDictionary.TryGetValue( deviceInstanceGuid, out var productGuid ) )
		{
			return false;
		}

		var productGuidBytes = productGuid.ToByteArray();

		ReadOnlySpan<byte> hidSignature = [ (byte) 'P', (byte) 'I', (byte) 'D', (byte) 'V', (byte) 'I', (byte) 'D' ];

		if ( !productGuidBytes.AsSpan( 10, 6 ).SequenceEqual( hidSignature ) )
		{
			return false;
		}

		vendorId = (ushort) ( productGuidBytes[ 0 ] | ( productGuidBytes[ 1 ] << 8 ) );
		productId = (ushort) ( productGuidBytes[ 2 ] | ( productGuidBytes[ 3 ] << 8 ) );

		return true;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void OnDeviceListMightHaveChanged( object? sender, DeviceListChangedEventArgs e )
	{
		if ( e.SteeringDeviceChanged )
		{
			_steeringDeviceWasReplugged = true;
		}

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

		// taken up front so a replug reported while this rescan runs is kept for the follow-up rescan
		var steeringDeviceWasReplugged = _steeringDeviceWasReplugged;

		_steeringDeviceWasReplugged = false;

		foreach ( var joystickInfo in _joystickInfoDictionary )
		{
			joystickInfo.Value._joystick.Unacquire();
		}

		_joystickInfoDictionary.Clear();
		_productGuidDictionary.Clear();
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

				_productGuidDictionary[ deviceInstance.InstanceGuid ] = deviceInstance.ProductGuid;

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
		else if ( steeringDeviceWasReplugged && ForceFeedbackDeviceList.ContainsKey( settings.RacingWheelSteeringDeviceGuid ) )
		{
			// the steering device is back under the same instance guid, and the force feedback device we hold
			// still points at the handle that died with the unplug - open it again (only once the device is
			// present; an unplug alone has nothing to re-open yet, the arrival brings us back here)
			app.Logger.WriteLine( "[DirectInput] Steering device was replugged, requesting force feedback re-initialization" );

			app.RacingWheel.NextRacingWheelGuid = settings.RacingWheelSteeringDeviceGuid;
		}

		app.MainWindow.Dispatcher.Invoke( () =>
		{
			MainWindow._racingWheelPage.UpdateSteeringDeviceOptions();

			foreach ( var window in System.Windows.Application.Current.Windows.OfType<MarvinsAIRARefactored.Windows.WizardWindow>() )
			{
				window.UpdateSteeringDeviceOptions();
			}
		} );

		app.Logger.WriteLine( "[DirectInput] <<< EnumerateDevices" );
	}
}
