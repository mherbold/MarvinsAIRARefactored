
namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Plays a single <see cref="FmodSound"/> via an FMOD channel.
/// </summary>
public sealed class FmodChannel
{
	private readonly FmodSound _fmodSound;
	private readonly FMOD.System _fmodSystem;

	private FMOD.Channel _channel;

	public FmodChannel( FmodSound fmodSound, FMOD.System fmodSystem )
	{
		_fmodSound = fmodSound;
		_fmodSystem = fmodSystem;
	}

	public void Play( float volume = 1f, float frequencyRatio = 1f, bool loop = false, uint loopStartMs = 0, uint loopEndMs = 0 )
	{
		// Start paused so we can configure before the first sample plays
		_fmodSystem.playSound( _fmodSound.Sound, default, true, out _channel );

		if ( loop )
		{
			_fmodSound.Sound.setMode( FMOD.MODE.LOOP_NORMAL );

			// Convert milliseconds to PCM samples for the loop region
			_fmodSound.Sound.getDefaults( out float defaultFrequency, out _ );
			uint sampleRate = (uint) defaultFrequency;

			uint startSample = loopStartMs * sampleRate / 1000u;

			uint endSample;
			if ( loopEndMs == 0 )
			{
				_fmodSound.Sound.getLength( out uint totalSamples, FMOD.TIMEUNIT.PCM );
				endSample = totalSamples > 0 ? totalSamples - 1 : 0;
			}
			else
			{
				endSample = loopEndMs * sampleRate / 1000u;
			}

			_channel.setLoopPoints( startSample, FMOD.TIMEUNIT.PCM, endSample, FMOD.TIMEUNIT.PCM );
		}
		else
		{
			_fmodSound.Sound.setMode( FMOD.MODE.LOOP_OFF );
		}

		_channel.setVolume( MathZ.Saturate( volume ) );

		// Apply frequency ratio relative to the sound's natural frequency
		_fmodSound.Sound.getDefaults( out float baseFrequency, out _ );
		_channel.setFrequency( baseFrequency * frequencyRatio );

		_channel.setPaused( false );
	}

	public void Update( float volume, float frequencyRatio = 1f )
	{
		_channel.isPlaying( out bool playing );

		if ( playing )
		{
			_channel.setVolume( MathZ.Saturate( volume ) );

			_fmodSound.Sound.getDefaults( out float baseFrequency, out _ );
			_channel.setFrequency( baseFrequency * frequencyRatio );
		}
	}

	public void Stop()
	{
		_channel.isPlaying( out bool playing );

		if ( playing )
		{
			_channel.stop();
		}
	}

	public bool IsPlaying()
	{
		_channel.isPlaying( out bool playing );

		return playing;
	}
}
