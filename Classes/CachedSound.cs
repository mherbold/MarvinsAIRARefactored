
using System.IO;

using SharpDX;
using SharpDX.Multimedia;
using SharpDX.XAudio2;

namespace MarvinsAIRARefactored.Classes;

public class CachedSound : IDisposable
{
	public WaveFormat WaveFormat { get; }
	public AudioBuffer AudioBuffer { get; }
	public uint[]? DecodedPacketsInfo { get; } = null;

	private readonly DataStream _stream;

	public CachedSound( string path )
	{
		using var soundStream = new SoundStream( File.OpenRead( path ) );

		WaveFormat = soundStream.Format;
		_stream = soundStream.ToDataStream();

		AudioBuffer = new AudioBuffer
		{
			Stream = _stream,
			AudioBytes = (int) soundStream.Length,
			Flags = BufferFlags.EndOfStream
		};

		if ( soundStream.DecodedPacketsInfo != null )
		{
			DecodedPacketsInfo = Array.ConvertAll( soundStream.DecodedPacketsInfo, x => (uint) x );
		}
	}

	public void Dispose()
	{
		_stream?.Dispose();
	}
}
