
using System.IO;
using System.IO.MemoryMappedFiles;

namespace MarvinsAIRARefactored.GameBridges.R3e;

/// <summary>
/// Reads RaceRoom Racing Experience's built-in "$R3E" shared memory map - the game exposes this natively, so
/// nothing needs to be installed. The map holds a single r3e_shared struct with no version counters; readers
/// just take the latest bytes (the player's game_simulation_time serves as the update detector).
/// </summary>
public class R3eLiveDataProvider : R3eDataProvider
{
	private MemoryMappedFile? _memoryMappedFile = null;
	private MemoryMappedViewAccessor? _viewAccessor = null;

	public override bool TryOpen()
	{
		if ( _viewAccessor == null )
		{
			try
			{
				_memoryMappedFile = MemoryMappedFile.OpenExisting( R3eConstants.SharedMemoryMapName, MemoryMappedFileRights.Read );

				_viewAccessor = _memoryMappedFile.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );
			}
			catch ( FileNotFoundException )
			{
			}
		}

		return _viewAccessor != null;
	}

	public override void Close()
	{
		_viewAccessor?.Dispose();
		_memoryMappedFile?.Dispose();

		_viewAccessor = null;
		_memoryMappedFile = null;
	}

	public override bool TryReadBuffer( R3eBufferType bufferType, byte[] destination )
	{
		if ( ( bufferType != R3eBufferType.Shared ) || ( _viewAccessor == null ) )
		{
			return false;
		}

		var length = (int) Math.Min( destination.Length, _viewAccessor.Capacity );

		_viewAccessor.ReadArray( 0, destination, 0, length );

		return true;
	}
}
