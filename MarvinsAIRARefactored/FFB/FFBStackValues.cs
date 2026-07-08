
using MarvinsAIRARefactored.Classes;

namespace MarvinsAIRARefactored.FFB;

/// <summary>
/// Per-context snapshot of FFB stack module setting values, keyed by the composite <c>"{moduleId}/{settingKey}"</c>.
/// Module IDs are unique app-wide, so a single flat dictionary spans every stack's modules with no stack-name
/// prefix — and a stack rename never has to rewrite these keys. Stored inside <see cref="DataContext.ContextSettings"/>,
/// so persistence, backup, and overlay-style defaulting all come for free via the existing serializer.
/// </summary>
public class FFBStackValues : SerializableDictionary<string, float>
{
	/// <summary>Compose the dictionary key for a module setting.</summary>
	public static string ComposeKey( string moduleId, string settingKey )
	{
		return $"{moduleId}/{settingKey}";
	}
}
