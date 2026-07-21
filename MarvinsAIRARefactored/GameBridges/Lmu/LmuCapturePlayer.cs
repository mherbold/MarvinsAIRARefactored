
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MarvinsAIRARefactored.GameBridges.Lmu;

/// <summary>
/// Plays back a capture file recorded by the LmuCaptureTool as if it were the live shared memory, so the
/// bridge can be developed and tested without the game running.
/// </summary>
public class LmuCapturePlayer : LmuDataProvider
{
	private const string FileMagic = "LMUCAP01";

	private readonly string _captureFilePath;

	private BinaryReader? _reader = null;
	private string[] _bufferNames = [];
	private byte[]?[] _latestBuffers = [];
	private bool _endOfFile = false;

	private double _nextFrameSeconds = double.NaN;
	private int _nextFrameBufferIndex = -1;

	public double PlaybackSeconds { get; private set; } = 0.0;

	public bool EndOfFile => _endOfFile;

	public LmuCapturePlayer( string captureFilePath )
	{
		_captureFilePath = captureFilePath;
	}

	public override bool TryOpen()
	{
		if ( _reader != null )
		{
			return true;
		}

		var fileStream = new FileStream( _captureFilePath, FileMode.Open, FileAccess.Read, FileShare.Read );
		var gzipStream = new GZipStream( fileStream, CompressionMode.Decompress );

		_reader = new BinaryReader( gzipStream );

		var magic = Encoding.ASCII.GetString( _reader.ReadBytes( FileMagic.Length ) );

		if ( magic != FileMagic )
		{
			Close();

			throw new InvalidDataException( $"{_captureFilePath} is not an LMU capture file." );
		}

		var bufferCount = _reader.ReadInt32();

		_bufferNames = new string[ bufferCount ];

		for ( var i = 0; i < bufferCount; i++ )
		{
			_bufferNames[ i ] = _reader.ReadString();
		}

		_latestBuffers = new byte[ bufferCount ][];

		ReadNextFrameHeader();

		return true;
	}

	public override void Close()
	{
		_reader?.Dispose();

		_reader = null;
	}

	/// <summary>
	/// Applies all captured frames up to the given playback time, in order.
	/// </summary>
	/// <param name="toSeconds">The playback time, in seconds from the start of the capture.</param>
	public void Advance( double toSeconds )
	{
		if ( _reader == null )
		{
			throw new InvalidOperationException( "The capture player has not been opened." );
		}

		while ( !_endOfFile && ( _nextFrameSeconds <= toSeconds ) )
		{
			var length = _reader.ReadInt32();

			var buffer = _reader.ReadBytes( length );

			if ( ( _nextFrameBufferIndex >= 0 ) && ( _nextFrameBufferIndex < _latestBuffers.Length ) )
			{
				_latestBuffers[ _nextFrameBufferIndex ] = buffer;
			}

			ReadNextFrameHeader();
		}

		PlaybackSeconds = toSeconds;
	}

	public override bool TryReadBuffer( LmuBufferType bufferType, byte[] destination )
	{
		var index = (int) bufferType;

		if ( ( index >= _latestBuffers.Length ) || ( _latestBuffers[ index ] == null ) )
		{
			return false;
		}

		var buffer = _latestBuffers[ index ]!;

		Buffer.BlockCopy( buffer, 0, destination, 0, Math.Min( destination.Length, buffer.Length ) );

		return true;
	}

	private void ReadNextFrameHeader()
	{
		try
		{
			_nextFrameBufferIndex = _reader!.ReadInt32();

			if ( _nextFrameBufferIndex < 0 )
			{
				_endOfFile = true;

				return;
			}

			_nextFrameSeconds = _reader.ReadDouble();
		}
		catch ( EndOfStreamException )
		{
			// the capture was stopped without a clean end marker (e.g. the tool was killed) - treat it as the end
			_endOfFile = true;
		}
	}
}
