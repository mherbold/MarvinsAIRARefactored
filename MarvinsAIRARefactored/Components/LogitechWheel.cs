
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.Components;

// Rev lights on the rim of a Logitech Trueforce wheel, driven from iRacing's shift light data.
//
// These wheels light their strip from the game over USB, and iRacing does not do it, so the rim stays
// dark no matter how the car is revving. This component fills that in.
//
// It also moves force feedback off DirectInput and onto the wheel's Trueforce endpoint while the lights
// are running, which is not an optional extra. The lights and DirectInput force share an endpoint, and
// writing the lights while force is flowing makes the force cut out. Slowing the light writes does not
// avoid it, and on a G PRO there is nowhere else to put them: the only other collection is input only.
// Streaming the Trueforce endpoint is what settles it, because the wheel then takes its motor torque from
// there and ignores force sent to the shared endpoint. Because the two are inseparable, the HID++ lights
// are only ever run while the Trueforce stream is genuinely up - if that stream cannot start or dies,
// force falls back to DirectInput and the lights stay off rather than cutting it out. The PlayStation
// G923 is the exception: its legacy lights ride the gamepad collection and do not compete with force,
// so that wheel keeps the DirectInput path and no Trueforce stream is opened.
//
// Scope is deliberately narrow. It looks only at the wheel already chosen as the steering device, does
// nothing at all unless that wheel is one of the Logitech models below, and never opens or writes to any
// other device. On any other wheelbase this component resolves nothing and costs one dictionary lookup
// every tenth of a second.

public class LogitechWheel
{
	// The rev level is recomputed this often, in app ticks. Faster than most of the app's periodic work
	// because the bar is watched directly and lag in it reads as the lights being broken. Recomputing is
	// a handful of float operations, so the cost is nothing next to how much crisper the bar looks.
	private const int UpdateInterval = 2;

	private const ushort LogitechVendorId = 0x046D;

	// When there is no first-light RPM in the session data, light the strip across the top of the rev
	// range rather than from idle.
	private const float FallbackFirstLightFraction = 0.75f;

	// How long the flash stays lit, and dark, once past the blink RPM. Matched to the rate iRacing blinks
	// its own shift lights, so the rim and the screen agree instead of beating against each other.
	private const int FlashHalfPeriodMilliseconds = 185;

	// How far past a segment boundary the revs must go before the bar actually moves, in segments. Without
	// it, telemetry jitter at a steady RPM sits right on a boundary and the top segment flickers. Kept
	// small: every bit of it is lag against the sim's own display.
	private const float LevelHysteresis = 0.15f;

	private int _hysteresisLevel = -1;

	private enum RevLightProtocol
	{
		HidPlusPlus,
		Legacy
	}

	// The Logitech wheels with a Trueforce motor, and which rev light protocol each one speaks. The
	// PlayStation G923 is the odd one out: it uses the older report rather than the HID++ feature page.
	//
	// Hardware status: the G PRO (Xbox) is verified end to end on a real wheel. The RS50 and the Xbox G923
	// drive the same HID++ feature over the same collections, so they are expected to work but are not
	// confirmed. The PlayStation G923's legacy path is written from a known-good reference and has not been
	// run at all. Anyone with one of these please report back.
	private static readonly Dictionary<ushort, (RevLightProtocol Protocol, string Name)> _supportedWheels = new()
	{
		{ 0xC272, ( RevLightProtocol.HidPlusPlus, "Logitech G PRO Racing Wheel (Xbox)" ) },
		{ 0xC268, ( RevLightProtocol.HidPlusPlus, "Logitech G PRO Racing Wheel (PlayStation)" ) },
		{ 0xC276, ( RevLightProtocol.HidPlusPlus, "Logitech RS50" ) },
		{ 0xC26D, ( RevLightProtocol.HidPlusPlus, "Logitech G923 (Xbox)" ) },
		{ 0xC26E, ( RevLightProtocol.HidPlusPlus, "Logitech G923 (Xbox)" ) },
		{ 0xC266, ( RevLightProtocol.Legacy, "Logitech G923 (PlayStation)" ) }
	};

	// True when the selected steering device is a wheel this component knows how to light. The racing
	// wheel page uses this to show or hide the rev lights section.
	public bool WheelIsSupported { get; private set; } = false;
	public string WheelName { get; private set; } = string.Empty;

	// True once force is going out on the Trueforce endpoint. RacingWheel reads this to decide where to
	// send its torque, so it must only be true when the stream is genuinely up.
	public bool TrueforceIsStreaming => _trueforceChannel?.IsStreaming ?? false;

	private LogitechRevLightChannel? _revLightChannel = null;
	private LogitechTrueforceChannel? _trueforceChannel = null;

	private Guid _resolvedDeviceGuid = Guid.Empty;
	private ushort _resolvedProductId = 0;

	private int _updateCounter = UpdateInterval;

	private volatile bool _deviceListChanged = false;
	private volatile bool _trueforceStartInProgress = false;

	// How long to wait before trying the Trueforce endpoint again after a failed start or a dead stream.
	// The attempt enumerates HID devices and logs, and if the endpoint is owned by another program (G HUB,
	// most likely) it is not coming free in the next few milliseconds, so retrying every tick buys nothing.
	private const long TrueforceRetryDelayMilliseconds = 5000;

	private long _nextTrueforceAttemptMilliseconds = 0;

	public void Initialize()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LogitechWheel] Initialize >>>" );

		// Unplugging the wheel, replugging it, or another program taking and releasing it all leave the
		// steering device selection untouched, so a device change is the only cue to open the wheel again.
		app.HidHotPlugMonitor.DeviceListMightHaveChanged += ( _, __ ) => _deviceListChanged = true;

		app.Logger.WriteLine( "[LogitechWheel] <<< Initialize" );
	}

	public void Shutdown()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[LogitechWheel] Shutdown >>>" );

		StopChannels( waitForCompletion: true );

		app.Logger.WriteLine( "[LogitechWheel] <<< Shutdown" );
	}

	// Called once per playout tick from the force feedback playout thread, not from Tick - the channel
	// writes the packet to the wheel synchronously, so the packet rate is the playout rate (360 Hz).
	// Sign is flipped because MAIRA's graph output torque is counterclockwise-positive while the
	// Trueforce torque target is clockwise-positive (measured on a G PRO: a positive target turns the
	// wheel right).
	public void SetOutputTorque( float outputTorque )
	{
		_trueforceChannel?.WriteForce( -outputTorque );
	}

	public void Tick( App app )
	{
		_updateCounter--;

		if ( _updateCounter > 0 )
		{
			return;
		}

		_updateCounter = UpdateInterval;

		var settings = DataContext.DataContext.Instance.Settings;

		var selectedDeviceGuid = settings.RacingWheelSteeringDeviceGuid;

		if ( _deviceListChanged || ( selectedDeviceGuid != _resolvedDeviceGuid ) )
		{
			// At startup the first ticks can run before DirectInput has enumerated anything. Waiting until
			// the device is actually known avoids caching a "no rev lights here" answer we cannot yet know,
			// which would hide the section for the whole session.
			if ( ( selectedDeviceGuid == Guid.Empty ) || app.DirectInput.IsDeviceEnumerated( selectedDeviceGuid ) )
			{
				_deviceListChanged = false;

				UpdateSelectedWheel( app, selectedDeviceGuid );
			}
		}

		// The wheel is handed back whenever the simulator is not running, mirroring what the racing wheel
		// does with the DirectInput device: the strip goes dark, the Trueforce stop command parks the motor,
		// and every handle closes, so other games are free to use the Trueforce endpoint while MAIRA sits
		// idle. The channels reopen (fresh handshake and all) on the next connect.
		if ( !WheelIsSupported || !settings.RacingWheelRevLightsEnabled || !app.Simulator.IsConnected )
		{
			StopChannels( waitForCompletion: false );

			return;
		}

		// A Trueforce stream that died mid-session (an unplug, a USB write error) means force has already
		// fallen back to DirectInput - the lights must come down with it, because writing them over the
		// shared endpoint is exactly what cuts that force out. The retry delay decides when the pair is
		// tried again.
		if ( ( _trueforceChannel != null ) && !_trueforceChannel.IsStreaming )
		{
			StopChannels( waitForCompletion: false );

			_nextTrueforceAttemptMilliseconds = Environment.TickCount64 + TrueforceRetryDelayMilliseconds;
		}

		StartChannels( app );

		if ( ( _revLightChannel == null ) || !_revLightChannel.IsReady )
		{
			return;
		}

		// Off track, in the pits before the engine is running, or between sessions there is nothing
		// meaningful to show, so the strip goes dark rather than freezing on its last value.
		var level = 0;
		var shouldFlash = false;

		if ( app.Simulator.IsConnected && app.Simulator.IsOnTrack )
		{
			level = CalculateLevel( app.Simulator, _revLightChannel.MaximumLevel, ref _hysteresisLevel );

			shouldFlash = ( app.Simulator.ShiftLightsBlinkRPM > 0f ) && ( app.Simulator.RPM >= app.Simulator.ShiftLightsBlinkRPM );
		}
		else
		{
			_hysteresisLevel = -1;
		}

		// Once the bar is full it says the same thing whether the driver is 10 RPM over or buried against
		// the limiter, so past the blink RPM it flashes instead. Nothing else on the strip moves, which is
		// what makes it readable out of the corner of an eye.
		if ( shouldFlash && settings.RacingWheelRevLightsFlashAtShiftPoint )
		{
			var flashIsLit = ( ( Environment.TickCount64 / FlashHalfPeriodMilliseconds ) % 2 ) == 0;

			level = flashIsLit ? _revLightChannel.MaximumLevel : 0;
		}

		_revLightChannel.SetLevel( level );
	}

	private void UpdateSelectedWheel( App app, Guid deviceInstanceGuid )
	{
		app.Logger.WriteLine( "[LogitechWheel] UpdateSelectedWheel >>>" );

		StopChannels( waitForCompletion: false );

		// A different wheel (or a replug) deserves an immediate attempt, whatever the previous one did.
		_nextTrueforceAttemptMilliseconds = 0;

		_resolvedDeviceGuid = deviceInstanceGuid;
		_resolvedProductId = 0;

		WheelIsSupported = false;
		WheelName = string.Empty;

		if ( app.DirectInput.TryGetUsbIds( deviceInstanceGuid, out var vendorId, out var productId ) && ( vendorId == LogitechVendorId ) && _supportedWheels.TryGetValue( productId, out var wheel ) )
		{
			_resolvedProductId = productId;

			WheelIsSupported = true;
			WheelName = wheel.Name;

			app.Logger.WriteLine( $"[LogitechWheel] Steering device is a {wheel.Name}, rev lights are available." );

			// Someone who owns one of these wants the lights, so turn them on the first time we see one
			// rather than leaving the strip dark until they find the switch. Once only: after this the
			// switch is theirs, and turning it off stays off.
			var settings = DataContext.DataContext.Instance.Settings;

			if ( !settings.RacingWheelRevLightsDefaultApplied )
			{
				settings.RacingWheelRevLightsDefaultApplied = true;
				settings.RacingWheelRevLightsEnabled = true;

				app.Logger.WriteLine( "[LogitechWheel] First time seeing a wheel with rev lights, switching them on." );
			}
		}
		else
		{
			app.Logger.WriteLine( "[LogitechWheel] Steering device has no rev lights we can drive." );
		}

		app.Dispatcher.BeginInvoke( () => Windows.MainWindow._racingWheelPage.UpdateRevLightsSection() );

		app.Logger.WriteLine( "[LogitechWheel] <<< UpdateSelectedWheel" );
	}

	private void StartChannels( App app )
	{
		if ( !_supportedWheels.TryGetValue( _resolvedProductId, out var wheel ) )
		{
			return;
		}

		if ( wheel.Protocol == RevLightProtocol.HidPlusPlus )
		{
			// The HID++ lights and DirectInput force share an endpoint, so the lights may only ever run
			// while force is streaming on the Trueforce endpoint. The Trueforce channel comes up first,
			// and the lights follow only once it is genuinely streaming - never the other way around.
			StartTrueforceChannel( app );

			if ( TrueforceIsStreaming && ( _revLightChannel == null ) )
			{
				app.Logger.WriteLine( $"[LogitechWheel] Opening the rev lights on the {wheel.Name}" );

				_revLightChannel = new HidPlusPlusRevLightChannel( app.Logger.WriteLine );

				_revLightChannel.Start( _resolvedProductId );
			}
		}
		else if ( _revLightChannel == null )
		{
			// The legacy lights ride the wheel's gamepad collection and do not compete with DirectInput
			// force, so this wheel keeps the proven DirectInput path and no Trueforce stream is opened.
			app.Logger.WriteLine( $"[LogitechWheel] Opening the rev lights on the {wheel.Name}" );

			_revLightChannel = new LegacyRevLightChannel( app.Logger.WriteLine );

			_revLightChannel.Start( _resolvedProductId );
		}
	}

	private void StartTrueforceChannel( App app )
	{
		if ( ( _trueforceChannel != null ) || _trueforceStartInProgress )
		{
			return;
		}

		if ( Environment.TickCount64 < _nextTrueforceAttemptMilliseconds )
		{
			return;
		}

		_trueforceStartInProgress = true;

		var productId = _resolvedProductId;

		// The Trueforce handshake is 136 packets spaced 2 ms apart, so starting it blocks for about a third
		// of a second. Tick runs on the dispatcher, so it goes on a worker instead of freezing the UI.
		Task.Run( () =>
		{
			var channel = new LogitechTrueforceChannel( app.Logger.WriteLine );

			if ( channel.Start( productId ) )
			{
				_trueforceChannel = channel;
			}
			else
			{
				channel.Dispose();

				_nextTrueforceAttemptMilliseconds = Environment.TickCount64 + TrueforceRetryDelayMilliseconds;
			}

			_trueforceStartInProgress = false;
		} );
	}

	// Tearing the channels down turns the strip off, parks the motor, and joins their threads. Tick runs on
	// the dispatcher, so it must not wait for that; app shutdown must, or the wheel is left lit and holding
	// its last torque target.
	private void StopChannels( bool waitForCompletion )
	{
		var revLightChannel = _revLightChannel;
		var trueforceChannel = _trueforceChannel;

		_revLightChannel = null;
		_trueforceChannel = null;

		if ( ( revLightChannel == null ) && ( trueforceChannel == null ) )
		{
			return;
		}

		if ( waitForCompletion )
		{
			revLightChannel?.Dispose();
			trueforceChannel?.Dispose();
		}
		else
		{
			Task.Run( () =>
			{
				revLightChannel?.Dispose();
				trueforceChannel?.Dispose();
			} );
		}
	}

	// How full the bar should be, 0 to 1, from the per-car shift light RPMs the sim publishes for exactly
	// this purpose. These are what external shift lights are meant to run on.
	//
	// Not from the sim's ShiftIndicatorPct telemetry: that value is pinned at 1.0 even at idle, so whatever
	// it once meant it no longer reports a live fill. Checked on a Global MX-5 at 875 RPM.
	private static bool TryCalculateFraction( Simulator simulator, out float fraction )
	{
		fraction = 0f;

		var lastRPM = simulator.ShiftLightsLastRPM;

		if ( lastRPM <= 0f )
		{
			return false;
		}

		var firstRPM = simulator.ShiftLightsFirstRPM;

		if ( ( firstRPM <= 0f ) || ( firstRPM >= lastRPM ) )
		{
			firstRPM = lastRPM * FallbackFirstLightFraction;
		}

		fraction = Math.Clamp( ( simulator.RPM - firstRPM ) / ( lastRPM - firstRPM ), 0f, 1f );

		return true;
	}

	private static int CalculateLevel( Simulator simulator, int maximumLevel, ref int hysteresisLevel )
	{
		if ( !TryCalculateFraction( simulator, out var fraction ) )
		{
			hysteresisLevel = -1;

			return 0;
		}

		var scaled = fraction * maximumLevel;

		// Rounding UP, so a segment lights the moment the revs enter it rather than halfway through. That is
		// what the sim's own shift lights do, and rounding to nearest instead puts the rim a segment and a
		// half behind the screen at the bottom of the range.
		var level = (int) MathF.Ceiling( scaled );

		// A segment N covers scaled in (N-1, N]. Moving up needs the revs to clear the top of the current
		// segment by the margin, moving down needs them to fall below its bottom by the same, so a steady
		// throttle sitting on a boundary holds instead of chattering.
		if ( hysteresisLevel >= 0 )
		{
			// Zero is a hard floor, so the margin below segment one would sit at a negative value the revs
			// can never reach and the bar would never go dark again. Clamping the threshold keeps the last
			// segment able to switch off.
			var dropThreshold = MathF.Max( 0f, hysteresisLevel - 1f - LevelHysteresis );

			if ( ( level > hysteresisLevel ) && ( scaled < hysteresisLevel + LevelHysteresis ) )
			{
				level = hysteresisLevel;
			}
			else if ( ( level < hysteresisLevel ) && ( scaled > dropThreshold ) )
			{
				level = hysteresisLevel;
			}
		}

		hysteresisLevel = level;

		return level;
	}
}
