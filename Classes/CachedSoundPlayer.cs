
using SharpDX.XAudio2;

namespace MarvinsAIRARefactored.Classes;

public class CachedSoundPlayer : IDisposable
{
	private readonly CachedSound _sound;
	private readonly XAudio2 _xaudio2;
	private SourceVoice? _sourceVoice;

	public CachedSoundPlayer( CachedSound sound, XAudio2 xaudio2 )
	{
		_sound = sound;
		_xaudio2 = xaudio2;
	}

	public void Play( float volume = 1.0f, bool loop = false )
	{
		if ( ( _sourceVoice == null ) || ( _sourceVoice.VoiceDetails.InputSampleRate != _sound.WaveFormat.SampleRate ) )
		{
			_sourceVoice?.DestroyVoice();

			_sourceVoice = new SourceVoice( _xaudio2, _sound.WaveFormat );
		}
		else
		{
			if ( _sourceVoice.State.BuffersQueued > 0 )
			{
				_sourceVoice.Stop();
				_sourceVoice.FlushSourceBuffers();
			}
		}

		_sourceVoice.SetVolume( Math.Clamp( volume, 0.0f, 1.0f ) );

		var buffer = new AudioBuffer
		{
			Stream = _sound.AudioBuffer.Stream,
			AudioBytes = _sound.AudioBuffer.AudioBytes,
			Flags = _sound.AudioBuffer.Flags,
			LoopCount = loop ? AudioBuffer.LoopInfinite : 0
		};

		buffer.LoopCount = loop ? AudioBuffer.LoopInfinite : 0;

		_sourceVoice.SubmitSourceBuffer( buffer, _sound.DecodedPacketsInfo );
		_sourceVoice.Start();
	}

	public void Stop()
	{
		_sourceVoice?.Stop();
	}

	public void Dispose()
	{
		_sourceVoice?.Dispose();
		_sourceVoice = null;
	}
}
