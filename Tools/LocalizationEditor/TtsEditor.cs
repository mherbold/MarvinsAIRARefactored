using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalizationEditor;

/// <summary>
/// All CRUD operations for the TTS JSON phrase files under <c>TTS/*.json</c>.
/// Every read and write uses explicit UTF-8 — no encoding corruption.
/// </summary>
internal static class TtsEditor
{
	// -------------------------------------------------------------------------
	// Read / write helpers
	// -------------------------------------------------------------------------

	private static JObject Load(string lang)
	{
		var path = FileSetConfig.TtsFilePath(lang);

		if (!File.Exists(path))
			throw new FileNotFoundException($"TTS file not found: {path}");

		var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
		return JObject.Parse(json);
	}

	private static void Save(string lang, JObject obj)
	{
		var path = FileSetConfig.TtsFilePath(lang);
		var json = obj.ToString(Formatting.Indented);
		File.WriteAllText(path, json + Environment.NewLine, System.Text.Encoding.UTF8);
	}

	// -------------------------------------------------------------------------
	// Commands
	// -------------------------------------------------------------------------

	/// <summary>Lists all keys present in en-US and shows which languages are missing each one.</summary>
	public static void ListKeys()
	{
		var base_ = Load("en-US");
		var langs = FileSetConfig.TtsLanguages.Where(l => l != "en-US").ToList();

		Console.WriteLine($"TTS keys in en-US ({base_.Count}):\n");

		foreach (var key in base_.Properties().Select(p => p.Name).OrderBy(k => k))
		{
			var missing = langs.Where(l =>
			{
				try { var o = Load(l); return !o.ContainsKey(key); }
				catch { return true; }
			}).ToList();

			var status = missing.Count == 0 ? "OK" : $"MISSING in: {string.Join(", ", missing)}";
			Console.WriteLine($"  {key,-40} {status}");
		}
	}

	/// <summary>Prints all phrase variants for <paramref name="key"/> side-by-side across every language.</summary>
	public static void ShowKey(string key)
	{
		Console.WriteLine($"TTS key: {key}\n");

		foreach (var lang in FileSetConfig.TtsLanguages)
		{
			try
			{
				var obj = Load(lang);
				if (obj[key] is JArray arr)
				{
					Console.WriteLine($"  [{lang}]");
					foreach (var phrase in arr)
						Console.WriteLine($"    \"{phrase}\"");
				}
				else
				{
					Console.WriteLine($"  [{lang}]  <missing>");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"  [{lang}]  ERROR: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Adds <paramref name="key"/> to every language file.
	/// <paramref name="phrasesByLang"/> maps lang tag → phrase array.
	/// Any language not present in the dictionary receives the en-US phrases as a fallback.
	/// </summary>
	public static void AddKey(string key, Dictionary<string, string[]> phrasesByLang)
	{
		if (!phrasesByLang.ContainsKey("en-US"))
			throw new ArgumentException("phrasesByLang must include an 'en-US' entry.");

		var fallback = phrasesByLang["en-US"];

		foreach (var lang in FileSetConfig.TtsLanguages)
		{
			var obj = Load(lang);

			if (obj.ContainsKey(key))
			{
				Console.WriteLine($"  [{lang}]  key '{key}' already exists — skipping.");
				continue;
			}

			var phrases = phrasesByLang.TryGetValue(lang, out var p) ? p : fallback;
			obj[key] = new JArray(phrases.Cast<object>().ToArray());
			Save(lang, obj);
			Console.WriteLine($"  [{lang}]  added ({phrases.Length} phrase(s))");
		}
	}

	/// <summary>Removes <paramref name="key"/> from every language file.</summary>
	public static void RemoveKey(string key)
	{
		foreach (var lang in FileSetConfig.TtsLanguages)
		{
			var obj = Load(lang);

			if (!obj.ContainsKey(key))
			{
				Console.WriteLine($"  [{lang}]  key '{key}' not found — skipping.");
				continue;
			}

			obj.Remove(key);
			Save(lang, obj);
			Console.WriteLine($"  [{lang}]  removed");
		}
	}

	/// <summary>Renames <paramref name="oldKey"/> to <paramref name="newKey"/> in every language file, preserving position.</summary>
	public static void RenameKey(string oldKey, string newKey)
	{
		foreach (var lang in FileSetConfig.TtsLanguages)
		{
			var obj = Load(lang);

			if (!obj.ContainsKey(oldKey))
			{
				Console.WriteLine($"  [{lang}]  key '{oldKey}' not found — skipping.");
				continue;
			}

			if (obj.ContainsKey(newKey))
			{
				Console.WriteLine($"  [{lang}]  target key '{newKey}' already exists — skipping.");
				continue;
			}

			// Rebuild the object preserving property order
			var rebuilt = new JObject();
			foreach (var prop in obj.Properties())
				rebuilt[prop.Name == oldKey ? newKey : prop.Name] = prop.Value;

			Save(lang, rebuilt);
			Console.WriteLine($"  [{lang}]  renamed");
		}
	}

	/// <summary>
	/// Replaces all phrase variants for <paramref name="key"/> in a single language file.
	/// Pass <c>"en-US"</c> to update just the base; pass <c>"*"</c> to update all.
	/// </summary>
	public static void SetPhrases(string key, string lang, string[] phrases)
	{
		var targets = lang == "*" ? FileSetConfig.TtsLanguages : [lang];

		foreach (var l in targets)
		{
			var obj = Load(l);
			obj[key] = new JArray(phrases.Cast<object>().ToArray());
			Save(l, obj);
			Console.WriteLine($"  [{l}]  set {phrases.Length} phrase(s) for '{key}'");
		}
	}

	/// <summary>
	/// Validates all language files against en-US:
	/// reports missing keys, extra keys, and empty phrase arrays.
	/// </summary>
	public static void Validate()
	{
		var baseObj = Load("en-US");
		var baseKeys = baseObj.Properties().Select(p => p.Name).ToHashSet();
		var langs = FileSetConfig.TtsLanguages.Where(l => l != "en-US").ToList();
		var issues = 0;

		foreach (var lang in langs)
		{
			JObject obj;
			try { obj = Load(lang); }
			catch (Exception ex) { Console.WriteLine($"  [{lang}]  ERROR loading: {ex.Message}"); issues++; continue; }

			var langKeys = obj.Properties().Select(p => p.Name).ToHashSet();

			foreach (var key in baseKeys.Except(langKeys))
			{
				Console.WriteLine($"  [{lang}]  MISSING key: {key}");
				issues++;
			}

			foreach (var key in langKeys.Except(baseKeys))
			{
				Console.WriteLine($"  [{lang}]  EXTRA key (not in en-US): {key}");
				issues++;
			}

			foreach (var prop in obj.Properties())
			{
				if (prop.Value is JArray arr && arr.Count == 0)
				{
					Console.WriteLine($"  [{lang}]  EMPTY array for key: {prop.Name}");
					issues++;
				}
			}
		}

		// Also check base for empty arrays
		foreach (var prop in baseObj.Properties())
		{
			if (prop.Value is JArray arr && arr.Count == 0)
			{
				Console.WriteLine($"  [en-US]  EMPTY array for key: {prop.Name}");
				issues++;
			}
		}

		Console.WriteLine(issues == 0 ? "\nAll TTS files are valid." : $"\n{issues} issue(s) found.");
	}

	/// <summary>
	/// Adds any key present in en-US but missing from other language files,
	/// using the provided <paramref name="translations"/> map, falling back to en-US phrases.
	/// </summary>
	public static void SyncKeys(Dictionary<string, Dictionary<string, string[]>> translations)
	{
		var baseObj = Load("en-US");
		var langs = FileSetConfig.TtsLanguages.Where(l => l != "en-US").ToList();

		foreach (var lang in langs)
		{
			var obj = Load(lang);
			var changed = false;

			foreach (var prop in baseObj.Properties())
			{
				if (obj.ContainsKey(prop.Name))
					continue;

				string[] phrases;

				if (translations.TryGetValue(prop.Name, out var byLang) &&
					byLang.TryGetValue(lang, out var translated))
					phrases = translated;
				else
					phrases = ((JArray) prop.Value).Select(t => t.ToString()).ToArray();

				obj[prop.Name] = new JArray(phrases.Cast<object>().ToArray());
				Console.WriteLine($"  [{lang}]  synced key: {prop.Name}");
				changed = true;
			}

			if (changed)
				Save(lang, obj);
		}
	}

	/// <summary>
	/// Creates a new language file pre-populated with all en-US keys.
	/// <paramref name="phrasesByKey"/> provides translations; missing keys fall back to en-US.
	/// Also ensures the csproj entry exists.
	/// </summary>
	public static void AddLanguage(string lang, Dictionary<string, string[]> phrasesByKey)
	{
		var path = FileSetConfig.TtsFilePath(lang);

		if (File.Exists(path))
		{
			Console.WriteLine($"TTS file for '{lang}' already exists: {path}");
			return;
		}

		var baseObj = Load("en-US");
		var newObj = new JObject();

		foreach (var prop in baseObj.Properties())
		{
			var fallback = ((JArray) prop.Value).Select(t => t.ToString()).ToArray();
			newObj[prop.Name] = new JArray(
				(phrasesByKey.TryGetValue(prop.Name, out var t) ? t : fallback)
				.Cast<object>().ToArray());
		}

		Directory.CreateDirectory(FileSetConfig.TtsDir);
		File.WriteAllText(path, newObj.ToString(Formatting.Indented) + Environment.NewLine, System.Text.Encoding.UTF8);
		Console.WriteLine($"  Created: {path}");

		FileSetConfig.EnsureCsprojEntry($"TTS\\{lang}.json", withCultureFalse: false);
	}
}
