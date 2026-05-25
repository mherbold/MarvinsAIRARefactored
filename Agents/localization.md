# Localization

## Related Source Files
- `Resources/Resources.resx` — English base strings (default language)
- `Resources/Resources.<lang>.resx` — Per-language translations
- `DataContext/DataContext.cs` — Exposes `Localization` instance as a bindable property
- `MarvinsAIRARefactored.csproj` — Explicit `<EmbeddedResource>` entries for every `.resx` file

---

## Architecture

Resource strings are stored in `.resx` files under `Resources/`. All `.resx` files are **compiled and embedded directly into the main assembly** at build time — they are never distributed as separate files and are never copied to the user's documents folder.

The `Localization` component (in `DataContext/`) loads the appropriate compiled `.resources` stream from the assembly via `Assembly.GetManifestResourceStream` and exposes all strings through an indexer:

```csharp
DataContext.DataContext.Instance.Localization["KeyName"]
```

In XAML, bind using:
```xml
{Binding Localization[KeyName]}
```

---

## Resource File Naming

| File | Purpose |
|---|---|
| `Resources/Resources.resx` | English base strings — the authoritative source |
| `Resources/Resources.<lang>.resx` | Per-language translations (e.g. `Resources.de-DE.resx`) |

All culture-specific files are embedded with `<WithCulture>false</WithCulture>` in the `.csproj` so they stay in the **main assembly** rather than satellite assemblies.

The SDK's default auto-inclusion of `.resx` files is **disabled** (`<EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>`), so every `.resx` file must be listed explicitly in the project file.

---

## Adding or Updating Strings

**Always use the LocalizationEditor tool for bulk operations — never edit resx files directly via PowerShell.**

### Adding a new key

1. Open `Tools/LocalizationEditor/Program.cs` and add a `case` in `AddResxKey()` with translated values for all languages.
2. Run: `dotnet run --project Tools/LocalizationEditor -- resx add-key MyNewKey`
3. Run: `dotnet run --project Tools/LocalizationEditor -- resx validate` to confirm no issues.
4. Reference in XAML via `{Binding Localization[MyNewKey]}` — never hard-code strings in XAML.
5. Reference in C# via `DataContext.DataContext.Instance.Localization["MyNewKey"]`.

### Other useful commands

```
dotnet run --project Tools/LocalizationEditor -- resx list-keys              # all keys + missing languages
dotnet run --project Tools/LocalizationEditor -- resx show-key Commentary    # value per language
dotnet run --project Tools/LocalizationEditor -- resx validate               # full consistency check
dotnet run --project Tools/LocalizationEditor -- resx set-value MyKey de-DE "Neuer Wert"
dotnet run --project Tools/LocalizationEditor -- resx rename-key OldKey NewKey
dotnet run --project Tools/LocalizationEditor -- resx remove-key ObsoleteKey
```

### Manual single-file edits

For a one-off change to a single language file it is still acceptable to use `replace_string_in_file` directly — but follow the Unicode safety rules in the section below.

**Always use localization keys** for unit strings and value formatters in `Settings.cs` too. Do **not** hard-code unit strings in C#:

```csharp
// Correct
SomeValueString = $"{value} {DataContext.Instance.Localization["DegreesPerSecond"]}";

// Wrong
SomeValueString = $"{value} °/s";
```

Common existing unit keys: `"Degrees"`, `"DegreesPerSecond"`, `"Percent"`, `"Hz"`, `"GForceUnits"`, `"MPSUnits"`, `"OFF"`. Check the existing `.resx` files before introducing new keys.

---

## Adding a New Language

1. Create `Resources/Resources.<lang>.resx` (e.g., `Resources.ko-KR.resx`) with a `ThisLanguage` key containing the language's native name.
2. Add an explicit `<EmbeddedResource>` entry in `MarvinsAIRARefactored.csproj`:
   ```xml
   <EmbeddedResource Include="Resources\Resources.ko-KR.resx">
	 <WithCulture>false</WithCulture>
   </EmbeddedResource>
   ```
3. `Localization.Initialize()` automatically discovers and registers the new language on startup by scanning the assembly's manifest resource names — no other code changes are needed.

---

## ComboBox Options Must Be Localized Too

When building option lists for `MairaComboBox`, always construct them from the `Localization` indexer, never from hard-coded strings. Rebuild the list and refresh the UI whenever the language changes (see `docs/agents/ui-wpf-controls.md` — "Localized ComboBox Items").

---

## Unicode Safety — File Read/Write and String Replacement

Several XAML and resource files in this project contain **non-ASCII Unicode characters** (e.g., `Català`, `Français`, `Čestina`, `Русский`, `简体中文`, Thai, Armenian, etc.). Careless file operations will silently corrupt these characters.

### Rules

1. **Never use PowerShell `Get-Content` / `Set-Content` for XAML/resx files without specifying encoding.**
   PowerShell defaults to the system code page (typically Windows-1252 on Western systems), which cannot represent characters outside its range and will corrupt them silently.

2. **Always use `[System.IO.File]::ReadAllText` / `[System.IO.File]::WriteAllText` with explicit UTF-8 encoding** for any PowerShell read-modify-write operation:
   ```powershell
   $content = [System.IO.File]::ReadAllText($path)           # defaults to UTF-8 with BOM detection
   $content = $content.Replace("old", "new")
   [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
   ```
   The `[System.Text.UTF8Encoding]::new($false)` argument writes UTF-8 **without BOM**, which is required for XAML files (see the XAML Files — Write Without BOM section above).

3. **Never use `replace_string_in_file` or PowerShell `-replace` for bulk multi-occurrence substitutions across Unicode-containing files.**
   Bulk replacements (e.g., replacing all occurrences of a hardcoded color across a file) must be done with `[System.IO.File]::ReadAllText` → `.Replace()` → `[System.IO.File]::WriteAllText` so encoding is preserved end-to-end.

4. **Always verify after any bulk replacement** that non-ASCII characters in the file are intact:
   ```powershell
   $content = [System.IO.File]::ReadAllText($path)
   $lines = $content -split "`n"
   # spot-check lines known to contain non-ASCII
   $lines | Where-Object { $_ -match '[^\x00-\x7F]' } | Select-Object -First 10
   ```

5. **If corruption is detected** (U+FFFD `?` replacement character, or bytes like `0xEF 0xBF 0xBD`), restore the file from Git and redo the replacement using the safe `[System.IO.File]` approach above.
