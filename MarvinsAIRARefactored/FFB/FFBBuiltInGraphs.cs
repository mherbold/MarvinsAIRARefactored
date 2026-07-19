
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// The built-in graphs shipped inside the app. Each one is a regular .mairagraph export file maintained in the
/// project's BuiltInGraphs folder and embedded into the assembly. At every launch
/// <c>Settings.EnsureBuiltInFFBGraphsInitialized</c> syncs the stored graph dictionary against these files via
/// each file's content hash — so updating a file in the folder updates every user's settings on their next launch,
/// while their per-context knob values (keyed by the stable module ids inside the file) carry over. Built-in
/// graphs cannot be renamed, deleted, or structurally edited in the app; users clone them into a custom graph
/// to make structural changes.
/// </summary>
public static class FFBBuiltInGraphs
{
	/// <summary>The flagship built-in FFB graph — the first-run wizard selects it and tunes its first Gain module.</summary>
	public const string FlagshipGraphName = "Marvin's easy detail adjustment";

	public sealed record BuiltInGraph( string GraphType, FFBGraph Graph, string Hash );

	private static List<BuiltInGraph>? _cache = null;

	/// <summary>All built-in graphs, loaded once per app run. The cached FFBGraph instances are masters — hand
	/// out clones (see <see cref="CreateGraph"/>), never the instances themselves.</summary>
	public static IReadOnlyList<BuiltInGraph> All => _cache ??= Load();

	/// <summary>Fresh deep copy of the named built-in graph (with <c>IsBuiltIn</c> set), or null when no shipped
	/// file matches. Callers store the clone and may mutate its setting values freely.</summary>
	public static FFBGraph? CreateGraph( string graphType, string name )
	{
		foreach ( var builtInGraph in All )
		{
			if ( ( builtInGraph.GraphType == graphType ) && ( builtInGraph.Graph.Name == name ) )
			{
				return builtInGraph.Graph.Clone();
			}
		}

		return null;
	}

	private static List<BuiltInGraph> Load()
	{
		var app = App.Instance!;

		app.Logger.WriteLine( "[FFBBuiltInGraphs] Load >>>" );

		var builtInGraphs = new List<BuiltInGraph>();

		var assembly = Assembly.GetExecutingAssembly();

		foreach ( var resourceName in assembly.GetManifestResourceNames() )
		{
			if ( !resourceName.EndsWith( FFBGraphPort.FileExtension, StringComparison.OrdinalIgnoreCase ) )
			{
				continue;
			}

			try
			{
				using var resourceStream = assembly.GetManifestResourceStream( resourceName )!;
				using var memoryStream = new MemoryStream();

				resourceStream.CopyTo( memoryStream );

				// the hash is the change detector for the launch-time settings sync — any edit to the shipped
				// file (values, wiring, node positions) produces a new hash and the stored graph is refreshed
				var hash = Convert.ToHexString( SHA256.HashData( memoryStream.ToArray() ) );

				memoryStream.Position = 0;

				var exportFile = Serializer.Load<FFBGraphExportFile>( memoryStream );

				if ( exportFile.GraphType != FFBGraphExportFile.FFBGraphType )
				{
					app.Logger.WriteLine( $"[FFBBuiltInGraphs] Skipping {resourceName} - unsupported graph type '{exportFile.GraphType}'" );

					continue;
				}

				if ( string.IsNullOrWhiteSpace( exportFile.Graph.Name ) )
				{
					app.Logger.WriteLine( $"[FFBBuiltInGraphs] Skipping {resourceName} - the graph has no name" );

					continue;
				}

				var unknownModule = exportFile.Graph.Modules.FirstOrDefault( module => FFBModuleRegistry.TryGet( module.ModuleType ) == null );

				if ( unknownModule != null )
				{
					app.Logger.WriteLine( $"[FFBBuiltInGraphs] Skipping {resourceName} - unknown module type '{unknownModule.ModuleType}'" );

					continue;
				}

				exportFile.Graph.IsBuiltIn = true;

				builtInGraphs.Add( new BuiltInGraph( exportFile.GraphType, exportFile.Graph, hash ) );

				app.Logger.WriteLine( $"[FFBBuiltInGraphs] Loaded '{exportFile.Graph.Name}' ({exportFile.GraphType}, {exportFile.Graph.Modules.Count} modules, hash {hash[ ..8 ]})" );
			}
			catch ( Exception exception )
			{
				app.Logger.WriteLine( $"[FFBBuiltInGraphs] Failed to load {resourceName}: {exception.Message}" );
			}
		}

		app.Logger.WriteLine( "[FFBBuiltInGraphs] <<< Load" );

		return builtInGraphs;
	}
}
