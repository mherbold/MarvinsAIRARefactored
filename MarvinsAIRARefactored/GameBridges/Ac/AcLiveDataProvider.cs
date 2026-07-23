
using System.IO;
using System.IO.MemoryMappedFiles;

namespace MarvinsAIRARefactored.GameBridges.Ac;

/// <summary>
/// Reads the three Kunos shared memory pages. The default map names are AC's classic acpmf_* set (also used
/// by ACC and AC Rally); Assetto Corsa EVO publishes the same three pages under acevo_pmf_* names, so the EVO
/// bridge passes those in instead. The games expose these natively, so nothing needs to be installed.
/// </summary>
public class AcLiveDataProvider( string physicsMapName = AcConstants.PhysicsMapName, string graphicsMapName = AcConstants.GraphicsMapName, string staticMapName = AcConstants.StaticMapName ) : AcDataProvider
{
	private readonly MemoryMappedFile?[] _memoryMappedFiles = new MemoryMappedFile?[ 3 ];
	private readonly MemoryMappedViewAccessor?[] _viewAccessors = new MemoryMappedViewAccessor?[ 3 ];

	private string MapName( AcBufferType bufferType )
	{
		return bufferType switch
		{
			AcBufferType.Physics => physicsMapName,
			AcBufferType.Graphics => graphicsMapName,
			_ => staticMapName
		};
	}

	public override bool TryOpen()
	{
		var allOpen = true;

		for ( var i = 0; i < _viewAccessors.Length; i++ )
		{
			if ( _viewAccessors[ i ] == null )
			{
				try
				{
					_memoryMappedFiles[ i ] = MemoryMappedFile.OpenExisting( MapName( (AcBufferType) i ), MemoryMappedFileRights.Read );

					_viewAccessors[ i ] = _memoryMappedFiles[ i ]!.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read );
				}
				catch ( FileNotFoundException )
				{
					allOpen = false;
				}
			}
		}

		return allOpen;
	}

	public override void Close()
	{
		for ( var i = 0; i < _viewAccessors.Length; i++ )
		{
			_viewAccessors[ i ]?.Dispose();
			_memoryMappedFiles[ i ]?.Dispose();

			_viewAccessors[ i ] = null;
			_memoryMappedFiles[ i ] = null;
		}
	}

	public override bool TryReadBuffer( AcBufferType bufferType, byte[] destination )
	{
		var accessor = _viewAccessors[ (int) bufferType ];

		if ( accessor == null )
		{
			return false;
		}

		var length = (int) Math.Min( destination.Length, accessor.Capacity );

		accessor.ReadArray( 0, destination, 0, length );

		return true;
	}
}
