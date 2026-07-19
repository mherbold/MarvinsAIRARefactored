
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Per-context snapshot of FFB graph module setting values, keyed by the composite
/// <c>"{graphId}/{moduleId}/{settingKey}"</c>. The graph id prefix is required because the canonical source and
/// Output module ids (see <see cref="FFBGraph.CanonicalSourceId"/>) are shared by every graph — without it a
/// setting stored for one graph's 360 Hz source (its Enabled switch, say) would leak into every other graph.
/// Graph ids and module ids are stable across renames, so a rename never has to rewrite these keys. Stored
/// inside <see cref="DataContext.ContextSettings"/>, so persistence, backup, and overlay-style defaulting all
/// come for free via the existing serializer.
/// </summary>
public class FFBGraphValues : SerializableDictionary<string, float>
{
	/// <summary>Compose the dictionary key for a module setting of a specific graph.</summary>
	public static string ComposeKey( string graphId, string moduleId, string settingKey )
	{
		return $"{graphId}/{moduleId}/{settingKey}";
	}
}
