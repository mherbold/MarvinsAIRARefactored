
using System.IO;

namespace MarvinsAIRARefactored.Classes;

// Wire protocols for the rev light strip on the rim of a Logitech Trueforce wheel.
//
// Two families, picked by product id:
//
//   HID++ page 0x807A  - G PRO, RS50, G923 Xbox. A level 0..10 saying how many lights to light; the
//                        wheel's own onboard profile owns the colours, the direction, and the scaling,
//                        so there is no per-light or RGB control here.
//   Legacy "F8 12"     - G923 PlayStation. A 5-bit progressive mask on the gamepad collection, the
//                        same report the Linux hid-lg4ff driver writes.
//
// Both are paced by a single background sender that writes at a fixed cadence and never bursts, so the
// wheel sees a steady trickle rather than a flurry every time the revs cross a segment boundary.

public abstract class LogitechRevLightChannel : IDisposable
{
	// The wheel holds a level on its own for a good while, so it only needs an occasional refresh in
	// case it drops state. Refreshing faster buys nothing.
	private const int KeepAliveMilliseconds = 1000;

	// Floor on the gap between level writes, which sets how smoothly the bar fills. Ten segments sweeping
	// past in around a second needs roughly this often to look continuous rather than steppy.
	//
	// This used to be far slower, back when force feedback still went out over DirectInput: writing the
	// lights at any useful rate made the force cut out. Force now goes out on the Trueforce endpoint, and
	// while that streams the wheel ignores force on this one, so there is nothing left here for the lights
	// to disturb and the cadence can be chosen for how the bar looks.
	private const int ChangeMinimumMilliseconds = 60;

	private const int SenderTickMilliseconds = 10;

	protected readonly Action<string> _log;
	protected readonly object _ioLock = new();

	private Thread? _senderThread;
	private volatile bool _senderStopRequested;

	private volatile int _targetLevel;
	private int _sentLevel = -1;
	private long _lastWriteMilliseconds;

	public bool IsReady { get; private set; }
	public string ResolvedInfo { get; protected set; } = string.Empty;

	public abstract int MaximumLevel { get; }

	protected LogitechRevLightChannel( Action<string> log )
	{
		_log = log;
	}

	// Find and open the wheel's light collection and do any one-time setup it needs. Called once on the
	// background sender thread, never on a thread the app cares about the latency of.
	protected abstract bool OpenAndArm( ushort productId );

	protected abstract void WriteLevel( int level );

	protected abstract void CloseStreams();

	// Start resolving and sending on a background thread. Returns immediately; IsReady flips once the
	// wheel has answered. Safe to call when the wheel is absent - it simply never becomes ready.
	public void Start( ushort productId )
	{
		if ( _senderThread != null )
		{
			return;
		}

		_senderStopRequested = false;

		_senderThread = new Thread( () => SenderLoop( productId ) ) { IsBackground = true, Name = "LogitechRevLights" };

		_senderThread.Start();
	}

	// Set how many lights should be lit, 0..MaximumLevel. Cheap and non-blocking: it only moves the
	// target, and the sender thread decides when that reaches the wheel. Safe to call every frame.
	public void SetLevel( int level )
	{
		_targetLevel = Math.Clamp( level, 0, MaximumLevel );
	}

	private void SenderLoop( ushort productId )
	{
		try
		{
			lock ( _ioLock )
			{
				IsReady = OpenAndArm( productId );
			}
		}
		catch ( Exception exception )
		{
			_log( $"[LogitechRevLights] Could not open the rev lights: {exception.Message}" );

			IsReady = false;
		}

		if ( !IsReady )
		{
			return;
		}

		_log( $"[LogitechRevLights] Rev lights ready: {ResolvedInfo}" );

		while ( !_senderStopRequested )
		{
			Thread.Sleep( SenderTickMilliseconds );

			if ( _senderStopRequested || !IsReady )
			{
				continue;
			}

			var now = NowMilliseconds();
			var targetLevel = _targetLevel;

			var levelChanged = targetLevel != _sentLevel;

			var changeIsDue = levelChanged && ( ( now - _lastWriteMilliseconds ) >= ChangeMinimumMilliseconds );
			var keepAliveIsDue = !levelChanged && ( ( now - _lastWriteMilliseconds ) >= KeepAliveMilliseconds );

			if ( !changeIsDue && !keepAliveIsDue )
			{
				continue;
			}

			lock ( _ioLock )
			{
				if ( !IsReady )
				{
					continue;
				}

				try
				{
					WriteLevel( targetLevel );

					_sentLevel = targetLevel;
					_lastWriteMilliseconds = NowMilliseconds();
				}
				catch ( Exception exception )
				{
					_log( $"[LogitechRevLights] Rev light write failed, giving up on the device: {exception.Message}" );

					IsReady = false;
				}
			}
		}
	}

	// Turn the lights off and stop sending. Blocks briefly so the "off" write actually reaches the wheel
	// before the app tears the device down - otherwise the strip stays lit after MAIRA exits.
	public void Stop()
	{
		_senderStopRequested = true;

		var senderThread = _senderThread;

		_senderThread = null;

		try
		{
			senderThread?.Join( 500 );
		}
		catch ( Exception )
		{
		}

		lock ( _ioLock )
		{
			if ( IsReady )
			{
				try
				{
					WriteLevel( 0 );
				}
				catch ( Exception )
				{
				}
			}

			IsReady = false;

			CloseStreams();
		}

		_sentLevel = -1;
		_targetLevel = 0;
	}

	protected static long NowMilliseconds() => Environment.TickCount64;

	public void Dispose()
	{
		Stop();

		GC.SuppressFinalize( this );
	}
}

// G PRO, RS50 and G923 Xbox. Level 0..10 over HID++ feature page 0x807A.
//
// Windows splits the wheel's HID++ interface into one collection per report size (7 byte short, 20 byte
// long, 64 byte very long), a report id is only valid on its own collection, and a request's reply comes
// back on a different collection than the one it was written to, so all of them are opened and routed by
// report id. The G923 Xbox exposes no 7 byte collection at all, so its short-form commands ride the 20
// byte one padded out, which is why the command stream is a separate field rather than just the short one.

public sealed class HidPlusPlusRevLightChannel( Action<string> log ) : LogitechRevLightChannel( log )
{
	private const ushort LogitechVendorId = 0x046D;

	private const byte ReportShort = 0x10;
	private const byte ReportLong = 0x11;

	private const int LengthShort = 7;
	private const int LengthLong = 20;
	private const int LengthVeryLong = 64;

	private const byte DeviceIndexWired = 0xFF;
	private const byte RootFeatureIndex = 0x00;
	private const byte RootGetFeatureFunction = 0x0B;

	private const ushort RevLightsFeaturePage = 0x807A;

	// Low nibble of the function byte. Logitech's own software uses 0x0D and the wheel echoes it back,
	// so keeping it identical means our traffic looks exactly like traffic the firmware already expects.
	private const byte SoftwareId = 0x0D;

	// Spacing for the one-time arming burst. Seven transfers back to back at session start was enough to
	// hitch force feedback, so they are spread out; this runs once, so the delay costs nothing.
	private const int ArmGapMilliseconds = 4;

	private const int ReplyTimeoutMilliseconds = 250;

	public override int MaximumLevel => 10;

	private FileStream? _shortStream;
	private FileStream? _longStream;
	private FileStream? _veryLongStream;

	private FileStream? _commandStream;
	private int _commandLength;
	private byte _commandReportId;

	private int _longLength = LengthLong;

	private byte _revLightsFeatureIndex;

	protected override bool OpenAndArm( ushort productId )
	{
		if ( !OpenAndResolve( productId ) )
		{
			return false;
		}

		Arm();

		return true;
	}

	private bool OpenAndResolve( ushort productId )
	{
		var collections = HidDeviceHelper.Enumerate( LogitechVendorId ).Where( collection => collection.ProductId == productId ).ToList();

		if ( collections.Count == 0 )
		{
			_log( "[LogitechRevLights] The wheel exposes no HID collections." );

			return false;
		}

		// Interface 2 is the Trueforce audio-haptic endpoint on most of these wheels, and interface 1 is
		// on the G923 Xbox. Never open those: they are not HID++, and holding one can collide with
		// whatever else is streaming to them.
		var skipInterface1 = ( productId == 0xC26D ) || ( productId == 0xC26E );

		var candidates = collections
			.Where( collection => !collection.PathContains( "mi_02" ) )
			.Where( collection => !skipInterface1 || !collection.PathContains( "mi_01" ) )
			.ToList();

		foreach ( var group in candidates.GroupBy( collection => collection.GroupStem ) )
		{
			if ( TryGroup( group.ToList() ) )
			{
				return true;
			}
		}

		_log( "[LogitechRevLights] No interface answered a HID++ request for the rev light feature." );

		return false;
	}

	private bool TryGroup( List<HidCollectionInfo> group )
	{
		var openedStreams = new List<FileStream>();

		try
		{
			FileStream? shortStream = null, longStream = null, veryLongStream = null;

			var longLength = LengthLong;

			foreach ( var collection in group )
			{
				if ( ( collection.OutputReportByteLength != LengthShort ) && ( collection.OutputReportByteLength != LengthLong ) && ( collection.OutputReportByteLength != LengthVeryLong ) )
				{
					continue;
				}

				var stream = HidDeviceHelper.Open( collection.DevicePath );

				if ( stream == null )
				{
					continue;
				}

				openedStreams.Add( stream );

				if ( ( collection.OutputReportByteLength == LengthShort ) && ( shortStream == null ) )
				{
					shortStream = stream;
				}
				else if ( ( collection.OutputReportByteLength == LengthLong ) && ( longStream == null ) )
				{
					longStream = stream;

					longLength = collection.OutputReportByteLength;
				}
				else if ( ( collection.OutputReportByteLength == LengthVeryLong ) && ( veryLongStream == null ) )
				{
					veryLongStream = stream;
				}
			}

			// The level itself is a long report, and a report id is only valid on its own collection, so
			// without the 20 byte collection this interface cannot carry the command at all.
			if ( longStream == null )
			{
				CloseAll( openedStreams );

				return false;
			}

			_shortStream = shortStream;
			_longStream = longStream;
			_veryLongStream = veryLongStream;
			_longLength = longLength;

			if ( shortStream != null )
			{
				_commandStream = shortStream;
				_commandLength = LengthShort;
				_commandReportId = ReportShort;
			}
			else
			{
				_commandStream = longStream;
				_commandLength = LengthLong;
				_commandReportId = ReportLong;
			}

			var featureIndex = TryGetFeatureIndex( RevLightsFeaturePage );

			if ( featureIndex == 0 )
			{
				CloseAll( openedStreams );
				ClearStreams();

				return false;
			}

			_revLightsFeatureIndex = featureIndex;

			// A wheel exposing two collections of the same size leaves an opened stream in no slot; drop
			// those now rather than leaking the handle until the app exits.
			foreach ( var stream in openedStreams )
			{
				if ( ( stream != _shortStream ) && ( stream != _longStream ) && ( stream != _veryLongStream ) )
				{
					stream.Dispose();
				}
			}

			ResolvedInfo = $"HID++ feature 0x{_revLightsFeatureIndex:X2}, {MaximumLevel} levels";

			return true;
		}
		catch ( Exception exception )
		{
			_log( $"[LogitechRevLights] Error probing a wheel interface: {exception.Message}" );

			CloseAll( openedStreams );
			ClearStreams();

			return false;
		}
	}

	private static void CloseAll( List<FileStream> streams )
	{
		foreach ( var stream in streams )
		{
			try
			{
				stream.Dispose();
			}
			catch ( Exception )
			{
			}
		}
	}

	private void ClearStreams()
	{
		_shortStream = null;
		_longStream = null;
		_veryLongStream = null;
		_commandStream = null;
	}

	// HID++ root getFeature: ask the wheel which feature index the rev light page landed on. The index
	// differs per wheel and per firmware, so it always has to be asked for rather than assumed.
	private byte TryGetFeatureIndex( ushort featurePage )
	{
		var request = new byte[ LengthShort ];

		request[ 0 ] = _commandReportId;
		request[ 1 ] = DeviceIndexWired;
		request[ 2 ] = RootFeatureIndex;
		request[ 3 ] = RootGetFeatureFunction;
		request[ 4 ] = (byte) ( featurePage >> 8 );
		request[ 5 ] = (byte) ( featurePage & 0xFF );

		try
		{
			WriteCommand( request );
		}
		catch ( Exception exception )
		{
			_log( $"[LogitechRevLights] HID++ request failed: {exception.Message}" );

			return 0;
		}

		// The reply comes back on a different collection than the request went out on, and in practice it is
		// the largest one (confirmed on a G PRO, which answers on its 64 byte collection). Reading that one
		// first means the usual case costs no timeouts; the others are still tried as a fallback.
		var replyStreams = new[] { _veryLongStream, _longStream, _shortStream };

		foreach ( var stream in replyStreams )
		{
			var featureIndex = ReadFeatureIndexReply( stream );

			if ( featureIndex != 0 )
			{
				return featureIndex;
			}
		}

		return 0;
	}

	private byte ReadFeatureIndexReply( FileStream? stream )
	{
		if ( stream == null )
		{
			return 0;
		}

		for ( var attempt = 0; attempt < 4; attempt++ )
		{
			var response = new byte[ LengthVeryLong ];

			var bytesRead = HidDeviceHelper.ReadWithTimeout( stream, response, ReplyTimeoutMilliseconds );

			if ( bytesRead < 5 )
			{
				return 0;
			}

			if ( ( response[ 1 ] != DeviceIndexWired ) || ( response[ 2 ] != RootFeatureIndex ) )
			{
				continue;
			}

			// 0xFF in the function slot is the HID++ error reply: this wheel does not have the page.
			if ( response[ 3 ] == 0xFF )
			{
				return 0;
			}

			var featureIndex = response[ 4 ];

			if ( ( featureIndex != 0 ) && ( featureIndex < 0x80 ) )
			{
				return featureIndex;
			}
		}

		return 0;
	}

	private byte FunctionByte( int function ) => (byte) ( ( function << 4 ) | SoftwareId );

	// The one-time sequence that hands control of the strip to us. Straight from what Logitech's own
	// software sends; without it the wheel ignores the level writes.
	private void Arm()
	{
		WriteCommand( [ _commandReportId, DeviceIndexWired, _revLightsFeatureIndex, FunctionByte( 0 ), 0x00, 0x00, 0x00 ] );
		Thread.Sleep( ArmGapMilliseconds );
		WriteCommand( [ _commandReportId, DeviceIndexWired, _revLightsFeatureIndex, FunctionByte( 1 ), 0x00, 0x00, 0x00 ] );
		Thread.Sleep( ArmGapMilliseconds );
		WriteCommand( [ _commandReportId, DeviceIndexWired, _revLightsFeatureIndex, FunctionByte( 2 ), 0x00, 0x00, 0x00 ] );
		Thread.Sleep( ArmGapMilliseconds );
		WriteCommand( [ _commandReportId, DeviceIndexWired, _revLightsFeatureIndex, FunctionByte( 3 ), 0x02, 0x00, 0x00 ] );
		Thread.Sleep( ArmGapMilliseconds );
		WriteCommand( [ _commandReportId, DeviceIndexWired, _revLightsFeatureIndex, FunctionByte( 0 ), 0x00, 0x00, 0x00 ] );
		Thread.Sleep( ArmGapMilliseconds );

		WriteLevel( 0 );
	}

	// Short function 2 then long function 6 with the level in byte 9. Byte 7 is the scale, ie how many
	// lights the strip has.
	protected override void WriteLevel( int level )
	{
		var clampedLevel = (byte) Math.Clamp( level, 0, MaximumLevel );

		WriteCommand( [ _commandReportId, DeviceIndexWired, _revLightsFeatureIndex, FunctionByte( 2 ), 0x00, 0x00, 0x00 ] );

		var levelReport = new byte[ _longLength ];

		levelReport[ 0 ] = ReportLong;
		levelReport[ 1 ] = DeviceIndexWired;
		levelReport[ 2 ] = _revLightsFeatureIndex;
		levelReport[ 3 ] = FunctionByte( 6 );
		levelReport[ 5 ] = 0x01;
		levelReport[ 7 ] = (byte) MaximumLevel;
		levelReport[ 9 ] = clampedLevel;

		_longStream?.Write( levelReport, 0, levelReport.Length );
		_longStream?.Flush();
	}

	// A HID write has to be exactly the collection's output report length, so short-form payloads are
	// padded out when they are riding the long collection.
	private void WriteCommand( byte[] payload )
	{
		if ( _commandStream == null )
		{
			return;
		}

		payload[ 0 ] = _commandReportId;

		var report = payload;

		if ( _commandLength > payload.Length )
		{
			report = new byte[ _commandLength ];

			Array.Copy( payload, report, payload.Length );
		}

		_commandStream.Write( report, 0, report.Length );
		_commandStream.Flush();
	}

	protected override void CloseStreams()
	{
		foreach ( var stream in new[] { _shortStream, _longStream, _veryLongStream } )
		{
			try
			{
				stream?.Dispose();
			}
			catch ( Exception )
			{
			}
		}

		ClearStreams();
	}
}

// G923 PlayStation. The legacy Logitech "set rev lights" report, five lights as a progressive bitmask.
//
// This one rides the wheel's gamepad collection rather than the HID++ command path, so it does not
// compete with force feedback the way the HID++ page does.

public sealed class LegacyRevLightChannel( Action<string> log ) : LogitechRevLightChannel( log )
{
	private const ushort LogitechVendorId = 0x046D;

	private const byte CommandPrefix = 0xF8;
	private const byte CommandSetRevLights = 0x12;

	private const int MinimumReportLength = 8;

	private const ushort UsagePageGenericDesktop = 0x01;

	private static readonly ushort[] JoystickUsages = [ 0x04, 0x05, 0x08 ];

	public override int MaximumLevel => 5;

	private FileStream? _stream;
	private int _reportLength = MinimumReportLength;

	protected override bool OpenAndArm( ushort productId )
	{
		var candidates = HidDeviceHelper.Enumerate( LogitechVendorId )
			.Where( collection => collection.ProductId == productId )
			.Where( collection => !collection.PathContains( "mi_02" ) )
			.Where( collection => collection.OutputReportByteLength > 0 )
			.ToList();

		// Prefer the joystick collection the command actually belongs on. An input-only collection would
		// fail every write, which is why a writable output report is required above.
		var chosen = candidates.FirstOrDefault( collection => ( collection.UsagePage == UsagePageGenericDesktop ) && JoystickUsages.Contains( collection.Usage ) ) ?? candidates.FirstOrDefault();

		if ( chosen == null )
		{
			_log( "[LogitechRevLights] The wheel exposes no writable gamepad collection." );

			return false;
		}

		_stream = HidDeviceHelper.Open( chosen.DevicePath );

		if ( _stream == null )
		{
			_log( "[LogitechRevLights] Could not open the wheel's gamepad collection." );

			return false;
		}

		_reportLength = Math.Max( MinimumReportLength, chosen.OutputReportByteLength );

		ResolvedInfo = $"legacy rev light report, {MaximumLevel} levels";

		return true;
	}

	protected override void WriteLevel( int level )
	{
		var clampedLevel = Math.Clamp( level, 0, MaximumLevel );

		var mask = (byte) ( ( 1 << clampedLevel ) - 1 );

		var report = new byte[ _reportLength ];

		report[ 1 ] = CommandPrefix;
		report[ 2 ] = CommandSetRevLights;
		report[ 3 ] = mask;
		report[ 7 ] = 0x01;

		_stream?.Write( report, 0, report.Length );
		_stream?.Flush();
	}

	protected override void CloseStreams()
	{
		try
		{
			_stream?.Dispose();
		}
		catch ( Exception )
		{
		}

		_stream = null;
	}
}
