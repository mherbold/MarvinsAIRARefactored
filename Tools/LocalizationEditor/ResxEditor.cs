using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LocalizationEditor;

/// <summary>
/// All CRUD operations for the resx localization files under <c>Resources/Resources*.resx</c>.
///
/// Resx format used here: only string <c>&lt;data&gt;</c> entries are touched.
/// The large XML header block (schema comments, resheader elements) is preserved verbatim.
/// All reads and writes use explicit UTF-8 without BOM.
/// </summary>
internal static class ResxEditor
{
	// -------------------------------------------------------------------------
	// Read / write helpers
	// -------------------------------------------------------------------------

	private static XDocument Load(string? lang)
	{
		var path = lang is null ? FileSetConfig.ResxBasePath : FileSetConfig.ResxFilePath(lang);

		if (!File.Exists(path))
			throw new FileNotFoundException($"Resx file not found: {path}");

		return XDocument.Load(path, LoadOptions.PreserveWhitespace);
	}

	private static void Save(string? lang, XDocument doc)
	{
		var path = lang is null ? FileSetConfig.ResxBasePath : FileSetConfig.ResxFilePath(lang);

		var settings = new XmlWriterSettings
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Indent = true,
			IndentChars = "  ",
			NewLineChars = "\r\n",
			NewLineHandling = NewLineHandling.Replace,
		};

		using var writer = XmlWriter.Create(path, settings);
		doc.Save(writer);
	}

	private static XElement? FindDataElement(XDocument doc, string key) =>
		doc.Root?
		   .Elements("data")
		   .FirstOrDefault(e => (string?) e.Attribute("name") == key);

	private static string? GetValue(XDocument doc, string key) =>
		FindDataElement(doc, key)?.Element("value")?.Value;

	private static IEnumerable<string> AllKeys(XDocument doc) =>
		doc.Root?
		   .Elements("data")
		   .Select(e => (string?) e.Attribute("name") ?? string.Empty)
		   .Where(k => k.Length > 0)
		   ?? Enumerable.Empty<string>();

	// -------------------------------------------------------------------------
	// Commands
	// -------------------------------------------------------------------------

	/// <summary>Lists all keys in the base file and flags any missing from language files.</summary>
	public static void ListKeys()
	{
		var baseDoc = Load(null);
		var baseKeys = AllKeys(baseDoc).OrderBy(k => k).ToList();
		var langs = FileSetConfig.ResxLanguages;

		Console.WriteLine($"Resx keys in base file ({baseKeys.Count}):\n");

		foreach (var key in baseKeys)
		{
			var missing = langs.Where(l =>
			{
				try { return GetValue(Load(l), key) is null; }
				catch { return true; }
			}).ToList();

			var status = missing.Count == 0 ? "OK" : $"MISSING in: {string.Join(", ", missing)}";
			Console.WriteLine($"  {key,-45} {status}");
		}
	}

	/// <summary>Prints the value for <paramref name="key"/> across every language file.</summary>
	public static void ShowKey(string key)
	{
		Console.WriteLine($"Resx key: {key}\n");

		// base first
		try
		{
			var val = GetValue(Load(null), key);
			Console.WriteLine($"  [en-US / base]  {(val ?? "<missing>")}");
		}
		catch (Exception ex) { Console.WriteLine($"  [base]  ERROR: {ex.Message}"); }

		foreach (var lang in FileSetConfig.ResxLanguages)
		{
			try
			{
				var val = GetValue(Load(lang), key);
				Console.WriteLine($"  [{lang,-12}]  {(val ?? "<missing>")}");
			}
			catch (Exception ex) { Console.WriteLine($"  [{lang}]  ERROR: {ex.Message}"); }
		}
	}

	/// <summary>
	/// Adds <paramref name="key"/> to every file (base + all language files).
	/// <paramref name="valuesByLang"/> maps lang tag → translated string.
	/// Use <c>null</c> as the key for the base file value.
	/// Any language not in the dictionary falls back to the base (en-US) value.
	/// </summary>
	public static void AddKey(string key, string baseValue, Dictionary<string, string> valuesByLang)
	{
		// Base file first
		SetValueInFile(null, key, baseValue);

		var fallback = baseValue;

		foreach (var lang in FileSetConfig.ResxLanguages)
		{
			var value = valuesByLang.TryGetValue(lang, out var v) ? v : fallback;
			SetValueInFile(lang, key, value);
		}
	}

	/// <summary>Removes <paramref name="key"/> from every file (base + all language files).</summary>
	public static void RemoveKey(string key)
	{
		RemoveFromFile(null, key);
		foreach (var lang in FileSetConfig.ResxLanguages)
			RemoveFromFile(lang, key);
	}

	/// <summary>Renames <paramref name="oldKey"/> to <paramref name="newKey"/> in every file.</summary>
	public static void RenameKey(string oldKey, string newKey)
	{
		RenameInFile(null, oldKey, newKey);
		foreach (var lang in FileSetConfig.ResxLanguages)
			RenameInFile(lang, oldKey, newKey);
	}

	/// <summary>Sets the value for <paramref name="key"/> in one language file (or base if <paramref name="lang"/> is null).</summary>
	public static void SetValue(string key, string? lang, string value)
	{
		SetValueInFile(lang, key, value);
		Console.WriteLine($"  [{lang ?? "base"}]  set value for '{key}'");
	}

	/// <summary>
	/// Validates all language files against the base file:
	/// reports missing keys, extra keys, and empty values.
	/// </summary>
	public static void Validate()
	{
		var baseDoc = Load(null);
		var baseKeys = AllKeys(baseDoc).ToHashSet();
		var issues = 0;

		foreach (var lang in FileSetConfig.ResxLanguages)
		{
			XDocument doc;
			try { doc = Load(lang); }
			catch (Exception ex) { Console.WriteLine($"  [{lang}]  ERROR loading: {ex.Message}"); issues++; continue; }

			var langKeys = AllKeys(doc).ToHashSet();

			foreach (var key in baseKeys.Except(langKeys))
			{
				Console.WriteLine($"  [{lang}]  MISSING key: {key}");
				issues++;
			}

			foreach (var key in langKeys.Except(baseKeys))
			{
				Console.WriteLine($"  [{lang}]  EXTRA key (not in base): {key}");
				issues++;
			}

			foreach (var key in langKeys)
			{
				var val = GetValue(doc, key);
				if (string.IsNullOrWhiteSpace(val))
				{
					Console.WriteLine($"  [{lang}]  EMPTY value for key: {key}");
					issues++;
				}
			}
		}

		Console.WriteLine(issues == 0 ? "\nAll resx files are valid." : $"\n{issues} issue(s) found.");
	}

	/// <summary>
	/// Adds any key present in the base file but missing from language files,
	/// using the provided <paramref name="translations"/> map (key → lang → value),
	/// falling back to the base value for any untranslated entry.
	/// </summary>
	public static void SyncKeys(Dictionary<string, Dictionary<string, string>> translations)
	{
		var baseDoc = Load(null);
		var baseKeys = AllKeys(baseDoc).ToList();

		foreach (var lang in FileSetConfig.ResxLanguages)
		{
			var doc = Load(lang);
			var langKeys = AllKeys(doc).ToHashSet();
			var changed = false;

			foreach (var key in baseKeys)
			{
				if (langKeys.Contains(key))
					continue;

				string value;
				if (translations.TryGetValue(key, out var byLang) &&
					byLang.TryGetValue(lang, out var translated))
					value = translated;
				else
					value = GetValue(baseDoc, key) ?? string.Empty;

				SetDataElement(doc, key, value);
				Console.WriteLine($"  [{lang}]  synced key: {key}");
				changed = true;
			}

			if (changed)
				Save(lang, doc);
		}
	}

	/// <summary>
	/// Creates a new language resx file pre-populated with all base keys.
	/// <paramref name="valuesByKey"/> provides translations; missing keys fall back to the base value.
	/// Also ensures the csproj entry exists.
	/// </summary>
	public static void AddLanguage(string lang, Dictionary<string, string> valuesByKey)
	{
		var path = FileSetConfig.ResxFilePath(lang);

		if (File.Exists(path))
		{
			Console.WriteLine($"Resx file for '{lang}' already exists: {path}");
			return;
		}

		// Clone base file as the template (preserves header / schema block)
		var doc = Load(null);

		// Replace all data element values
		if (doc.Root is not null)
		{
			foreach (var dataEl in doc.Root.Elements("data").ToList())
			{
				var key = (string?) dataEl.Attribute("name");
				if (key is null) continue;

				var valueEl = dataEl.Element("value");
				if (valueEl is null) continue;

				if (valuesByKey.TryGetValue(key, out var translated))
					valueEl.Value = translated;
				// else leave base value as-is
			}
		}

		Save(lang, doc);   // saves to the new lang path
		Console.WriteLine($"  Created: {path}");

		FileSetConfig.EnsureCsprojEntry(
			$"Resources\\Resources.{lang}.resx",
			withCultureFalse: true);
	}

	// -------------------------------------------------------------------------
	// Private file-level helpers
	// -------------------------------------------------------------------------

	private static void SetValueInFile(string? lang, string key, string value)
	{
		var doc = Load(lang);
		SetDataElement(doc, key, value);
		Save(lang, doc);
		Console.WriteLine($"  [{lang ?? "base"}]  set '{key}'");
	}

	private static void SetDataElement(XDocument doc, string key, string value)
	{
		var existing = FindDataElement(doc, key);

		if (existing is not null)
		{
			var valueEl = existing.Element("value");
			if (valueEl is not null) valueEl.Value = value;
			else existing.Add(new XElement("value", value));
			return;
		}

		// Append a new data element before </root>
		var newData = new XElement("data",
			new XAttribute("name", key),
			new XAttribute(XNamespace.Xml + "space", "preserve"),
			new XElement("value", value));

		doc.Root?.Add(newData);
	}

	private static void RemoveFromFile(string? lang, string key)
	{
		var doc = Load(lang);
		var el = FindDataElement(doc, key);

		if (el is null)
		{
			Console.WriteLine($"  [{lang ?? "base"}]  key '{key}' not found — skipping.");
			return;
		}

		el.Remove();
		Save(lang, doc);
		Console.WriteLine($"  [{lang ?? "base"}]  removed '{key}'");
	}

	private static void RenameInFile(string? lang, string oldKey, string newKey)
	{
		var doc = Load(lang);
		var el = FindDataElement(doc, oldKey);

		if (el is null)
		{
			Console.WriteLine($"  [{lang ?? "base"}]  key '{oldKey}' not found — skipping.");
			return;
		}

		el.SetAttributeValue("name", newKey);
		Save(lang, doc);
		Console.WriteLine($"  [{lang ?? "base"}]  renamed '{oldKey}' → '{newKey}'");
	}
}
