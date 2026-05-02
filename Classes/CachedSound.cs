
namespace MarvinsAIRARefactored.Classes;

/// <summary>
/// Holds a loaded FMOD sound and the file path it was created from.
/// </summary>
public sealed class FmodSound : IDisposable
{
	/// <summary>The absolute path the sound was loaded from, kept so it can be reloaded after a device change.</summary>
	public string FilePath { get; }

	public FMOD.Sound Sound { get; private set; }

	public FmodSound( string filePath, FMOD.System fmodSystem )
	{
		FilePath = filePath;

		var result = fmodSystem.createSound( filePath, FMOD.MODE.DEFAULT, out var sound );

		if ( result != FMOD.RESULT.OK )
		{
			throw new InvalidOperationException( $"FMOD createSound failed ({result}): {filePath}" );
		}

		Sound = sound;
	}

	public void Dispose()
	{
		Sound.release();
	}
}
