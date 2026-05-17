using System.IO.MemoryMappedFiles;
using System.Threading;

namespace MarvinsAIRARefactored.Classes;

// Writes MAIRA's force + RPM into a shared memory file that TF4ALL
// (TrueForce-For-All) reads, so TF4ALL can render the force through the
// Logitech Trueforce ep3 endpoint and drive the rim rev LEDs over HID++.
//
// Why: on the Logitech G PRO the rev LEDs (HID++ page 0x807A) and PID
// force feedback (HID++ page 0x8123) share one command processor on the
// wheel; when MAIRA streams PID at ~320 Hz, any LED write stalls FFB
// ~1.5 s (a device-level mutual exclusion, not fixable by software
// scheduling). With the "Pass FFB signal through TF4ALL" option on,
// MAIRA stops sending PID to the wheel and publishes its force here
// instead. No PID on the HID++ pipe => LEDs and FFB no longer fight.
//
// Wire format MUST stay byte-identical to TF4ALL's MairaIpcSource
// (TrueForce-For-All repo, TrueforceForAll.Core/MairaIpcSource.cs):
// fixed 64-byte map, little-endian, single writer, monotonic sequence
// written before and after the payload so the reader can detect a torn
// read.

public sealed class TF4ALLPassthrough : IDisposable
{
	private const string MapName = "Local\\TF4ALL_MAIRA_FFB_v1";
	private const int    MapSize = 64;
	private const int    Magic   = 0x4D414952; // 'MAIR'
	private const int    Version = 1;

	// MAIRA publishes ONLY the FFB force. TF4ALL owns the rim LEDs from
	// its own SimHub telemetry (it is not MAIRA's job to source shift-light
	// data). Offsets are kept fixed for wire compatibility with TF4ALL's
	// MairaIpcSource; the rpm/shift slots simply go unused/zero.
	private const int OffMagic   = 0;
	private const int OffVersion = 4;
	private const int OffSeqA    = 8;
	private const int OffFfbNorm = 16;
	private const int OffWriteMs = 32;
	private const int OffSeqB    = 40;

	private MemoryMappedFile? _mmf;
	private MemoryMappedViewAccessor? _view;
	private long _seq;
	private bool _opened;

	private bool EnsureOpen()
	{
		if ( _opened )
		{
			return true;
		}

		try
		{
			_mmf = MemoryMappedFile.CreateOrOpen( MapName, MapSize, MemoryMappedFileAccess.ReadWrite );
			_view = _mmf.CreateViewAccessor( 0, MapSize, MemoryMappedFileAccess.ReadWrite );
			_view.Write( OffMagic, Magic );
			_view.Write( OffVersion, Version );
			_opened = true;

			App.Instance?.Logger.WriteLine( "[TF4ALL] FFB passthrough shared memory created." );

			return true;
		}
		catch ( Exception exception )
		{
			App.Instance?.Logger.WriteLine( $"[TF4ALL] Could not create passthrough shared memory: {exception.Message}" );

			return false;
		}
	}

	// ffbNormalized is MAIRA's outputTorque (already torque / MaxForce,
	// roughly -1..1); TF4ALL maps it to the wheel scale and applies its
	// own FFB scale/sign. FFB only , nothing else.
	public void Write( float ffbNormalized )
	{
		if ( !EnsureOpen() || _view == null )
		{
			return;
		}

		long seq = ++_seq;

		_view.Write( OffSeqA, seq );
		Thread.MemoryBarrier();
		_view.Write( OffFfbNorm, ffbNormalized );
		_view.Write( OffWriteMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() );
		Thread.MemoryBarrier();
		_view.Write( OffSeqB, seq );
	}

	public void Dispose()
	{
		try { _view?.Dispose(); } catch { }
		try { _mmf?.Dispose(); } catch { }

		_view = null;
		_mmf = null;
		_opened = false;
	}
}
