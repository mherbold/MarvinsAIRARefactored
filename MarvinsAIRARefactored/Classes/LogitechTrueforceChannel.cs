
using System.Diagnostics;
using System.IO;

using MarvinsAIRARefactored.Components;

namespace MarvinsAIRARefactored.Classes;

// Streams steering force to a Logitech Trueforce wheel over its Trueforce endpoint instead of through
// DirectInput.
//
// Why this exists: the rev lights and DirectInput force share an endpoint on the wheel, and writing the
// lights while force is flowing makes the force cut out. The exact cause was never pinned down, and the
// way around it does not need one: while this endpoint is streaming, the wheel takes its motor torque
// from here and ignores force sent to the shared endpoint, so light writes have nothing left to disturb.
//
// The endpoint is an audio-haptic channel: each 64 byte packet carries a torque target plus a rolling
// window of 13 haptic samples layered on top of it. MAIRA has no audio layer to add, so every packet
// carries the torque target with a window of centre samples, which sum to nothing. That shape is also
// what makes the wheel prefer this endpoint over its DirectInput force path, which is the point.
//
// The wheel accepts a packet rate anywhere in roughly 250 to 1000 per second, chosen by the sender.
// There is no pacing here: WriteForce is called once per playout tick on the playout timer thread, so
// the packet rate is the playout rate (360 Hz), exactly one packet per torque value and inside the
// accepted band. While the playout timer is suspended no packets flow and the wheel simply holds the
// last (already faded to zero) torque target. If the sample window is ever used for LFE or high
// frequency effects, revisit the rate: sample bandwidth is four per packet, so it scales with it.

public sealed class LogitechTrueforceChannel : IDisposable
{
	private const ushort LogitechVendorId = 0x046D;

	// The Trueforce interface identifies itself with this vendor defined usage, which is how it is told
	// apart from the wheel's other 64 byte collections.
	private const ushort TrueforceUsagePage = 0xFFFD;
	private const ushort TrueforceUsage = 0xFD01;

	private const int PacketLength = 64;
	private const int WindowSlots = 13;
	private const int WindowByteOffset = 12;

	private const ushort CentreSample = 0x8000;

	private const int InitPacketGapMicroseconds = 2000;
	private const int InitPasses = 2;

	private readonly Action<string> _log;
	private readonly object _ioLock = new();

	private FileStream? _stream;

	private byte _sequence;

	private readonly byte[] _packetBuffer = new byte[ PacketLength ];

	private volatile bool _isStreaming;

	public bool IsStreaming => _isStreaming;
	public string ResolvedInfo { get; private set; } = string.Empty;

	public LogitechTrueforceChannel( Action<string> log )
	{
		_log = log;
	}

	// Open the wheel's Trueforce interface and run the handshake. Slow (the handshake alone is 136
	// packets spaced 2 ms apart, so about a third of a second), so call it off the UI thread. Once this
	// returns true the playout drives the stream by calling WriteForce every tick.
	public bool Start( ushort productId )
	{
		lock ( _ioLock )
		{
			if ( _isStreaming )
			{
				return true;
			}

			var trueforceInterface = HidDeviceHelper.Enumerate( LogitechVendorId )
				.Where( collection => collection.ProductId == productId )
				.Where( collection => collection.OutputReportByteLength == PacketLength )
				.FirstOrDefault( collection => ( collection.UsagePage == TrueforceUsagePage ) && ( collection.Usage == TrueforceUsage ) );

			if ( trueforceInterface == null )
			{
				_log( "[LogitechTrueforce] The wheel has no Trueforce interface. Is it in PC mode?" );

				return false;
			}

			_stream = HidDeviceHelper.Open( trueforceInterface.DevicePath );

			if ( _stream == null )
			{
				_log( "[LogitechTrueforce] Could not open the Trueforce interface. Another program may already own it." );

				return false;
			}

			try
			{
				RunHandshake();
			}
			catch ( Exception exception )
			{
				_log( $"[LogitechTrueforce] Handshake failed: {exception.Message}" );

				CloseStream();

				return false;
			}

			_isStreaming = true;

			ResolvedInfo = $"Trueforce endpoint, one packet per playout tick ({PlayoutTimer.TickRateHz:F0} Hz)";

			_log( $"[LogitechTrueforce] Streaming started: {ResolvedInfo}" );

			return true;
		}
	}

	// Send one force packet, -1 to +1, same sign convention as the DirectInput magnitude it replaces.
	// Called once per tick on the playout timer thread. Never blocks: if another thread is tearing the
	// stream down right now, the packet is simply skipped - the next tick sends a fresh one anyway.
	public void WriteForce( float force )
	{
		if ( !_isStreaming )
		{
			return;
		}

		if ( !Monitor.TryEnter( _ioLock ) )
		{
			return;
		}

		try
		{
			if ( !_isStreaming || ( _stream == null ) )
			{
				return;
			}

			var clampedForce = Math.Clamp( force, -1f, 1f );

			var torqueTarget = (ushort) Math.Clamp( CentreSample + ( clampedForce * short.MaxValue ), ushort.MinValue, ushort.MaxValue );

			BuildForcePacket( _packetBuffer, _sequence++, torqueTarget );

			try
			{
				_stream.Write( _packetBuffer, 0, PacketLength );
				_stream.Flush();
			}
			catch ( Exception exception )
			{
				_log( $"[LogitechTrueforce] Stream write failed, stopping: {exception.Message}" );

				_isStreaming = false;
			}
		}
		finally
		{
			Monitor.Exit( _ioLock );
		}
	}

	private void RunHandshake()
	{
		var packet = new byte[ PacketLength ];

		for ( var pass = 0; pass < InitPasses; pass++ )
		{
			for ( var index = 0; index < LogitechTrueforceInitData.PacketCount; index++ )
			{
				Buffer.BlockCopy( LogitechTrueforceInitData.Packets[ index ], 0, packet, 0, PacketLength );

				packet[ LogitechTrueforceInitData.SequenceByteOffset ] = (byte) ( ( index + 1 ) & 0xFF );

				_stream!.Write( packet, 0, PacketLength );
				_stream.Flush();

				SleepMicroseconds( InitPacketGapMicroseconds );
			}
		}

		_sequence = (byte) ( ( LogitechTrueforceInitData.PacketCount + 1 ) & 0xFF );
	}

	// Byte 4 marks a sample packet and byte 5 is the running sequence number. Bytes 6 to 9 are the torque
	// target, written twice as little endian 16 bit offset binary. Byte 10 says how many window slots are
	// new and byte 11 is the window length. Bytes 12 onward are the 13 window slots, each also a doubled
	// 16 bit value; holding them all at centre makes the overlay silent so only the torque target is felt.
	private static void BuildForcePacket( byte[] packet, byte sequence, ushort force )
	{
		Array.Clear( packet, 0, PacketLength );

		packet[ 0 ] = 0x01;
		packet[ 4 ] = 0x01;
		packet[ 5 ] = sequence;

		packet[ 6 ] = (byte) ( force & 0xFF );
		packet[ 7 ] = (byte) ( force >> 8 );
		packet[ 8 ] = (byte) ( force & 0xFF );
		packet[ 9 ] = (byte) ( force >> 8 );

		packet[ 10 ] = 0x04;
		packet[ 11 ] = 0x0D;

		for ( var slot = 0; slot < WindowSlots; slot++ )
		{
			var offset = WindowByteOffset + ( slot * 4 );

			packet[ offset + 0 ] = (byte) ( CentreSample & 0xFF );
			packet[ offset + 1 ] = (byte) ( CentreSample >> 8 );
			packet[ offset + 2 ] = (byte) ( CentreSample & 0xFF );
			packet[ offset + 3 ] = (byte) ( CentreSample >> 8 );
		}
	}

	// Hand the wheel back: zero the force, tell it to stop, and release the interface. Without the stop
	// command the wheel can hold the last torque target after MAIRA has gone. Taking _ioLock (which
	// WriteForce only ever TryEnters) means no playout packet can interleave with the shutdown sequence.
	public void Stop()
	{
		lock ( _ioLock )
		{
			if ( !_isStreaming && ( _stream == null ) )
			{
				return;
			}

			_isStreaming = false;

			if ( _stream != null )
			{
				try
				{
					BuildForcePacket( _packetBuffer, _sequence++, CentreSample );

					_stream.Write( _packetBuffer, 0, PacketLength );

					var stopCommand = new byte[ PacketLength ];

					Buffer.BlockCopy( LogitechTrueforceInitData.Packets[ LogitechTrueforceInitData.StopCommandIndex ], 0, stopCommand, 0, PacketLength );

					stopCommand[ LogitechTrueforceInitData.SequenceByteOffset ] = _sequence++;

					_stream.Write( stopCommand, 0, PacketLength );
					_stream.Flush();
				}
				catch ( Exception )
				{
				}
			}

			CloseStream();
		}

		_log( "[LogitechTrueforce] Streaming stopped." );
	}

	private void CloseStream()
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

	// Thread.Sleep cannot express the sub-millisecond gap the handshake wants, so short waits spin. This
	// only runs during the handshake, never in the steady state.
	private static void SleepMicroseconds( int microseconds )
	{
		var targetTicks = Stopwatch.GetTimestamp() + ( ( Stopwatch.Frequency * microseconds ) / 1_000_000 );

		while ( Stopwatch.GetTimestamp() < targetTicks )
		{
			Thread.SpinWait( 40 );
		}
	}

	public void Dispose()
	{
		Stop();

		GC.SuppressFinalize( this );
	}
}
