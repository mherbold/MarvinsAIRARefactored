
using System.IO;
using System.IO.MemoryMappedFiles;

using MarvinsAIRARefactored.GameBridges.Lmu;

namespace MarvinsAIRARefactored.GameBridges.Rf2;

/// <summary>
/// Reads the rFactor 2 shared memory buffers published by The Iron Wolf's rF2SharedMemoryMapPlugin
/// ($rFactor2SMMP_Telemetry$ / $rFactor2SMMP_Scoring$). Unlike Le Mans Ultimate, rFactor 2 has no native
/// shared memory map, so the plugin must be installed in the game's Bin64\Plugins folder and enabled in
/// CustomPluginVariables.JSON. This provider implements the LmuDataProvider contract (the plugin buffer
/// indices match LmuBufferType), so LmuCapturePlayer can replay rFactor 2 captures recorded by the
/// LmuCaptureTool without any changes.
/// </summary>
public class Rf2LiveDataProvider : LmuDataProvider
{
	// only the buffers the bridge consumes are opened; each buffer starts with the plugin's version block
	// (mVersionUpdateBegin / mVersionUpdateEnd) that the bridge uses to reject torn reads
	private static readonly (LmuBufferType bufferType, string mapName)[] Buffers =
	[
		( LmuBufferType.Telemetry, rFactor2Constants.MM_TELEMETRY_FILE_NAME ),
		( LmuBufferType.Scoring, rFactor2Constants.MM_SCORING_FILE_NAME )
	];

	private readonly MemoryMappedFile?[] _memoryMappedFiles = new MemoryMappedFile?[ Buffers.Length ];
	private readonly MemoryMappedViewAccessor?[] _viewAccessors = new MemoryMappedViewAccessor?[ Buffers.Length ];

	public override bool TryOpen()
	{
		var allOpen = true;

		for ( var i = 0; i < Buffers.Length; i++ )
		{
			if ( _viewAccessors[ i ] == null )
			{
				try
				{
					_memoryMappedFiles[ i ] = MemoryMappedFile.OpenExisting( Buffers[ i ].mapName, MemoryMappedFileRights.Read );

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
		for ( var i = 0; i < Buffers.Length; i++ )
		{
			_viewAccessors[ i ]?.Dispose();
			_memoryMappedFiles[ i ]?.Dispose();

			_viewAccessors[ i ] = null;
			_memoryMappedFiles[ i ] = null;
		}
	}

	public override bool TryReadBuffer( LmuBufferType bufferType, byte[] destination )
	{
		for ( var i = 0; i < Buffers.Length; i++ )
		{
			if ( Buffers[ i ].bufferType == bufferType )
			{
				var accessor = _viewAccessors[ i ];

				if ( accessor == null )
				{
					return false;
				}

				var length = (int) Math.Min( destination.Length, accessor.Capacity );

				accessor.ReadArray( 0, destination, 0, length );

				return true;
			}
		}

		return false;
	}
}
