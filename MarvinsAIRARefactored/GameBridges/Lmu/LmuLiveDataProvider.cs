
using System.IO;
using System.IO.MemoryMappedFiles;

namespace MarvinsAIRARefactored.GameBridges.Lmu;

/// <summary>
/// Reads Le Mans Ultimate's built-in "LMU_Data" shared memory map - the game exposes this natively, so no
/// plugin needs to be installed. The map has no version counters; readers just take the latest bytes.
/// </summary>
public class LmuLiveDataProvider : LmuDataProvider
{
	private const string NativeMapName = "LMU_Data";

	private MemoryMappedFile? _memoryMappedFile = null;
	private MemoryMappedViewAccessor? _viewAccessor = null;

	public override bool TryOpen()
	{
		if ( _viewAccessor == null )
		{
			try
			{
				_memoryMappedFile = MemoryMappedFile.OpenExisting( NativeMapName, MemoryMappedFileRights.Read );

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

	public override bool TryReadBuffer( LmuBufferType bufferType, byte[] destination )
	{
		if ( ( bufferType != LmuBufferType.NativeData ) || ( _viewAccessor == null ) )
		{
			return false;
		}

		var length = (int) Math.Min( destination.Length, _viewAccessor.Capacity );

		_viewAccessor.ReadArray( 0, destination, 0, length );

		return true;
	}
}
