
using SharpDX.DirectSound;
using SharpDX.XAudio2;

namespace MarvinsAIRARefactored.Classes;

public class CachedSoundPlayer( CachedSound sound, XAudio2 xaudio2 ) : IDisposable
{
	private readonly CachedSound _sound = sound;
	private readonly XAudio2 _xaudio2 = xaudio2;
	private SourceVoice? _sourceVoice;

	public void Play( float volume = 1f, float frequencyRatio = 1f, bool loop = false )
	{
		if ( ( _sourceVoice == null ) || ( _sourceVoice.VoiceDetails.InputSampleRate != _sound.WaveFormat.SampleRate ) )
		{
			_sourceVoice?.DestroyVoice();

			_sourceVoice = new SourceVoice( _xaudio2, _sound.WaveFormat, VoiceFlags.None, 4f );
		}
		else
		{
			if ( _sourceVoice.State.BuffersQueued > 0 )
			{
				_sourceVoice.Stop();
				_sourceVoice.FlushSourceBuffers();
			}
		}

		var buffer = new AudioBuffer
		{
			Stream = _sound.AudioBuffer.Stream,
			AudioBytes = _sound.AudioBuffer.AudioBytes,
			Flags = _sound.AudioBuffer.Flags,
			LoopCount = loop ? AudioBuffer.LoopInfinite : 0
		};

		buffer.LoopCount = loop ? AudioBuffer.LoopInfinite : 0;

		_sourceVoice.SetFrequencyRatio( frequencyRatio );
		_sourceVoice.SetVolume( Math.Clamp( volume, 0f, 1f ) );
		_sourceVoice.SubmitSourceBuffer( buffer, _sound.DecodedPacketsInfo );
		_sourceVoice.Start();
	}

	public void Update( float volume, float frequencyRatio = 1f )
	{
		if ( _sourceVoice != null )
		{
			_sourceVoice.SetFrequencyRatio( frequencyRatio );
			_sourceVoice.SetVolume( Math.Clamp( volume, 0f, 1f ) );
		}
	}

	public void Stop()
	{
		_sourceVoice?.Stop();
	}

	public bool IsPlaying()
	{
		return _sourceVoice?.State.BuffersQueued > 0;
	}

	public void Dispose()
	{
		_sourceVoice?.Dispose();
		_sourceVoice = null;
	}
}
