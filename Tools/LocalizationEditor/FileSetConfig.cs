using System.IO;
using System.Xml.Linq;

namespace LocalizationEditor;

/// <summary>
/// Locates the repository root and provides canonical paths + csproj helpers
/// for both the TTS JSON file set and the resx file set.
/// </summary>
internal static class FileSetConfig
{
	// -------------------------------------------------------------------------
	// Root discovery
	// -------------------------------------------------------------------------

	/// <summary>
	/// Walks up from the current directory to find the repo root
	/// (identified by containing MarvinsAIRARefactored.csproj).
	/// </summary>
	public static string RepoRoot { get; } = FindRepoRoot();

	private static string FindRepoRoot()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

		while (dir != null)
		{
			if (dir.GetFiles("MarvinsAIRARefactored.csproj").Length > 0)
				return dir.FullName;

			dir = dir.Parent;
		}

		throw new InvalidOperationException(
			"Cannot locate MarvinsAIRARefactored.csproj. " +
			"Run LocalizationEditor from inside the repository directory.");
	}

	// -------------------------------------------------------------------------
	// TTS paths
	// -------------------------------------------------------------------------

	public static string TtsDir => Path.Combine(RepoRoot, "TTS");

	public static string TtsFilePath(string lang) =>
		Path.Combine(TtsDir, $"{lang}.json");

	/// <summary>All TTS language tags, sorted, derived from files on disk.</summary>
	public static IReadOnlyList<string> TtsLanguages =>
		Directory.GetFiles(TtsDir, "*.json")
				 .Select(f => Path.GetFileNameWithoutExtension(f))
				 .OrderBy(l => l)
				 .ToList();

	// -------------------------------------------------------------------------
	// Resx paths
	// -------------------------------------------------------------------------

	public static string ResxDir => Path.Combine(RepoRoot, "Resources");

	/// <summary>Path to the English base file.</summary>
	public static string ResxBasePath => Path.Combine(ResxDir, "Resources.resx");

	public static string ResxFilePath(string lang) =>
		Path.Combine(ResxDir, $"Resources.{lang}.resx");

	/// <summary>All resx language tags (i.e. files that are NOT the base file), sorted.</summary>
	public static IReadOnlyList<string> ResxLanguages =>
		Directory.GetFiles(ResxDir, "Resources.*.resx")
				 .Select(f => Path.GetFileNameWithoutExtension(f))   // "Resources.de-DE"
				 .Select(n => n["Resources.".Length..])               // "de-DE"
				 .OrderBy(l => l)
				 .ToList();

	// -------------------------------------------------------------------------
	// csproj helpers
	// -------------------------------------------------------------------------

	public static string CsprojPath => Path.Combine(RepoRoot, "MarvinsAIRARefactored.csproj");

	/// <summary>
	/// Inserts a new &lt;EmbeddedResource&gt; entry into the csproj immediately after the last
	/// existing entry of the same file set (TTS or resx), keeping the block sorted.
	/// Does nothing if the entry already exists.
	/// </summary>
	public static void EnsureCsprojEntry(string relativePath, bool withCultureFalse)
	{
		var content = File.ReadAllText(CsprojPath, System.Text.Encoding.UTF8);

		// Normalise to forward slashes for comparison
		var normalised = relativePath.Replace('/', '\\');

		if (content.Contains(normalised))
			return;   // already present

		string newEntry = withCultureFalse
			? $"    <EmbeddedResource Include=\"{normalised}\"><WithCulture>false</WithCulture></EmbeddedResource>"
			: $"    <EmbeddedResource Include=\"{normalised}\" />";

		// Find the last existing entry in the same block by looking for the same path prefix
		string prefix = normalised.Contains("TTS") ? "TTS\\" : "Resources\\Resources.";

		// Insert after the last line that contains the prefix
		var lines = content.Split('\n').ToList();
		int lastIdx = -1;

		for (int i = 0; i < lines.Count; i++)
		{
			if (lines[i].Contains(prefix) && lines[i].Contains("EmbeddedResource"))
				lastIdx = i;
		}

		if (lastIdx < 0)
		{
			Console.WriteLine($"[WARN] Could not locate insertion point in csproj for '{relativePath}'. Add manually.");
			return;
		}

		lines.Insert(lastIdx + 1, newEntry);
		File.WriteAllText(CsprojPath, string.Join('\n', lines), System.Text.Encoding.UTF8);
		Console.WriteLine($"  csproj: added entry for {relativePath}");
	}
}
