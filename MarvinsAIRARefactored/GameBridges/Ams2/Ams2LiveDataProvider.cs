
using System.IO;
using System.IO.MemoryMappedFiles;

namespace MarvinsAIRARefactored.GameBridges.Ams2;

/// <summary>
/// Reads the "$pcars2$" file mapping that Automobilista 2 publishes when its Shared Memory option is set to
/// "Project CARS 2". The game exposes it natively, so nothing needs to be installed. The mapping has no
/// Local\ prefix - it is opened exactly as the game's own example app opens it.
/// </summary>
public class Ams2LiveDataProvider : Ams2DataProvider
{
	private MemoryMappedFile? _memoryMappedFile = null;
	private MemoryMappedViewAccessor? _viewAccessor = null;

	public override bool TryOpen()
	{
		if ( _viewAccessor != null )
		{
			return true;
		}

		try
		{
			_memoryMappedFile = MemoryMappedFile.OpenExisting( Ams2Constants.MapName, MemoryMappedFileRights.Read );

			_viewAccessor = _memoryMappedFile.CreateViewAccessor( 0, Ams2Constants.StructSize, MemoryMappedFileAccess.Read );

			return true;
		}
		catch ( FileNotFoundException )
		{
			Close();

			return false;
		}
		catch ( UnauthorizedAccessException )
		{
			Close();

			return false;
		}
	}

	public override void Close()
	{
		_viewAccessor?.Dispose();
		_memoryMappedFile?.Dispose();

		_viewAccessor = null;
		_memoryMappedFile = null;
	}

	public override bool TryReadBlock( byte[] destination )
	{
		var accessor = _viewAccessor;

		if ( accessor == null )
		{
			return false;
		}

		// the game bumps mSequenceNumber before and after each write (odd = write in progress), so a coherent
		// copy is one taken while the number is even and unchanged across the copy - the same scheme as the
		// game's own example app
		var sequenceBefore = accessor.ReadUInt32( Ams2Constants.SequenceNumberOffset );

		if ( ( sequenceBefore & 1u ) != 0u )
		{
			return false;
		}

		var length = (int) Math.Min( destination.Length, accessor.Capacity );

		accessor.ReadArray( 0, destination, 0, length );

		var sequenceAfter = accessor.ReadUInt32( Ams2Constants.SequenceNumberOffset );

		return sequenceAfter == sequenceBefore;
	}
}
