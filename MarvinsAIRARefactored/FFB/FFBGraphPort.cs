
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Serializable envelope for a graph exported to a shareable file. The type tag identifies FFB graph files
/// (the retired "Vibration" tag marked the old standalone vibration graphs, whose generator modules live
/// inside the FFB graph now — those legacy files are rejected on import), and the format version lets a
/// future release evolve the file format while older releases reject newer files with a clear message
/// instead of silently mangling them.
/// </summary>
public class FFBGraphExportFile
{
	public const int CurrentFormatVersion = 1;

	public const string FFBGraphType = "FFB";
	public const string VibrationGraphType = "Vibration";   // legacy standalone vibration graph files

	public int FormatVersion { get; set; } = CurrentFormatVersion;
	public string GraphType { get; set; } = string.Empty;
	public FFBGraph Graph { get; set; } = new();
}

/// <summary>
/// Export/import of single FFB graphs as shareable files (see <see cref="FileExtension"/>), backing the
/// export/import buttons next to the graph selector on the racing wheel page.
/// </summary>
public static class FFBGraphPort
{
	public const string FileExtension = ".mairagraph";

	/// <summary>Import-time validation failure carrying an already-localized, user-presentable message —
	/// shown by the page as-is, unlike unexpected exceptions which get the generic failure message.</summary>
	public class ImportException( string message ) : Exception( message );

	/// <summary>Write the graph to <paramref name="filePath"/>. The graph is deep-copied, and a built-in
	/// exports as an ordinary user graph so it arrives editable on the other side.</summary>
	public static void Export( FFBGraph graph, string filePath )
	{
		var exportFile = new FFBGraphExportFile
		{
			GraphType = FFBGraphExportFile.FFBGraphType,
			Graph = graph.Clone()
		};

		exportFile.Graph.IsBuiltIn = false;

		Serializer.Save( filePath, exportFile );
	}

	/// <summary>
	/// Read and validate a graph file. Throws <see cref="ImportException"/> (localized message) when the file
	/// is not a MAIRA FFB graph file (including the legacy standalone vibration graph files), or was written by
	/// a newer app version (a newer format version or any module type this build's registry does not know). The
	/// returned graph still needs the Settings-side treatment (unique name, fresh module ids) before it joins
	/// the graph dictionary.
	/// </summary>
	public static FFBGraph Import( string filePath )
	{
		var localization = DataContext.DataContext.Instance.Localization;

		var exportFile = Serializer.Load<FFBGraphExportFile>( filePath );

		if ( exportFile.GraphType != FFBGraphExportFile.FFBGraphType )
		{
			throw new ImportException( localization[ "ImportGraphInvalidFile" ] );
		}

		if ( exportFile.FormatVersion > FFBGraphExportFile.CurrentFormatVersion )
		{
			throw new ImportException( localization[ "ImportGraphNewerVersion" ] );
		}

		foreach ( var module in exportFile.Graph.Modules )
		{
			if ( FFBModuleRegistry.TryGet( module.ModuleType ) == null )
			{
				// an unknown module type means the file came from a newer app version — rejecting beats the
				// engine's silent fallback (which would quietly turn the unknown module into a 360 Hz source)
				throw new ImportException( localization[ "ImportGraphNewerVersion" ] );
			}
		}

		return exportFile.Graph;
	}
}
