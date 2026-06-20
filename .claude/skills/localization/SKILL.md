---
name: localization
description: >-
  Add, rename, translate, or edit UI strings in MarvinsAIRA's localization
  resource files. Use whenever the task touches Resources*.resx or
  Localization.cs — e.g. adding a new UI string/label/tooltip/caption,
  adding a Localization["Key"] lookup, renaming or re-keying a localization
  string, translating a string across all languages, adding a brand-new language
  (culture .resx), brand-prefixing or changing a caption everywhere, or otherwise
  bulk-editing the resx string tables. Performs these as one idempotent script
  run, never a per-file Edit loop.
---

# Localization (Resources*.resx)

Editing this app's UI strings means changing the **same key across many language
files at once**. That is a bulk deterministic transform — do it with the bundled
script in a single run. **Never loop the Edit tool file-by-file**: with 28 files
it burns large amounts of usage and invites copy-by-copy mistakes (a missing
space, a doubled prefix, a mangled non-Latin character).

The bundled helper is **[`transform-resx.ps1`](transform-resx.ps1)** in this
skill folder. Use it (or a small bespoke adaptation of its helper functions) for
every localization edit.

## Where strings live (confirmed against the repo)

- **`.resx` files** in `MarvinsAIRARefactored/Resources/` — one base + many cultures:
  - **Base / English:** `Resources.resx` (`ThisLanguage` = "English (United States)").
  - **Culture files:** `Resources.<code>.resx` (e.g. `ar-SA, de-DE, he-IL, ja-JP,
    ru-RU, zh-Hans, …`). **The set keeps growing — don't trust a hardcoded count.**
    Enumerate it live: `Glob Resources*.resx` (it was 34 files / ~590 keys as of the
    `ar-SA` addition).
- **Lookup convention:** UI code reads strings via `Localization["Key"]`
  (`DataContext/Localization.cs`). At runtime the chosen language's table is
  consulted first; missing/empty keys fall back to the base `Resources.resx`,
  and a totally unknown key renders as `!Key!`. The getter **`.Trim()`s** the
  value, so leading/trailing whitespace in a value is harmless at runtime but
  should still not be introduced. There is **no separate C# string table** — the
  base strings live only in `Resources.resx`.
- **Element shape** (real localized strings, 2-space indent, `xml:space="preserve"`):
  ```xml
  <data name="Key" xml:space="preserve">
    <value>the string</value>
  </data>
  ```
  (The `Name1`/`Color1`/`Bitmap1` entries near the top of every file are resx
  schema boilerplate — not localization strings. Ignore them.)
- **BOM convention is MIXED — preserve per file, never normalize.** Roughly half
  the culture files carry a UTF-8 BOM (most non-Latin/RTL ones — e.g. `he-IL`,
  `ja-JP`, `zh-Hans`, `ar-SA`), the rest (including the base `Resources.resx`) do
  not. `transform-resx.ps1` detects and re-applies each file's own BOM on write.
  For a **new** culture file, write WITH a BOM if the script is non-Latin/RTL
  (matches the `he-IL`/CJK precedent); `new-language.ps1` defaults to BOM.
- **Embedded whitespace:** some values contain literal TABs (e.g. `uk-UA`). The
  script edits the `<data>`/`<value>` block surgically, so such content is
  preserved — do not "clean it up" as a side effect.
- **All-caps display — no `_UC` keys.** The resx holds a single normal-case
  string per concept; the UI uppercases at runtime via the `Localization.Upper`
  accessor (`DataContext/Localization.cs`), which cases with the *active language's*
  culture (Turkish dotted-I, German eszett, no-op for case-less scripts). Bind in
  XAML as `{Binding Localization.Upper[Key]}` and read in code as
  `Localization.Upper["Key"]`. **Do not reintroduce `_UC` twin keys** — they were
  retired (the old uppercase copies had drifted from their base translations).
  `GlobalAmplitude`/`GlobalFrequency` exist as their own keys because their text
  ("Global …") genuinely differs from the plain `Amplitude`/`Frequency` keys.

## Hard rules

1. **`pwsh` (PowerShell 7+) only — never `powershell.exe`.** Windows PowerShell
   5.1 corrupts UTF-8 and destroys the non-Latin translations (Armenian, Thai,
   Hebrew, Cyrillic, CJK, Welsh). The script refuses to run on <7.
2. **Preserve each file's BOM** (the script does this; don't re-encode by hand).
3. **Idempotent:** every transform must be a no-op on a second run (the script's
   `Prepend`/`Add`/`Remove` already guard for this).
4. **Prove on one, then fan out** (next section).

## Workflow

Run from the skill folder (`.claude/skills/localization/`). Paths in examples are
relative to it. See the header of [`transform-resx.ps1`](transform-resx.ps1) for
the full parameter list.

### Step 1 — Prove the transform on ONE file (dry run)

Always start with `-WhatIf` against a single file and read the diff. Check: no
missing/extra spaces, no doubling, the right key, non-Latin text intact.

```powershell
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Set -Key Wind `
     -Value 'Typhoon Wind' `
     -Path ../../../MarvinsAIRARefactored/Resources/Resources.de-DE.resx -WhatIf
```

### Step 2 — Fan out for real

Drop `-WhatIf` and `-Path` to apply across all 28 files (or keep `-Path`/
`-BaseOnly` to scope it).

```powershell
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Set -Key Wind -Value 'Typhoon Wind'
```

If the result is wrong, **fix the script/arguments and re-run** — never patch
individual result files with the Edit tool.

### Step 3 — Verify, then build

- Confirm the change landed everywhere and nothing doubled:
  ```powershell
  pwsh -NoProfile -File ./transform-resx.ps1 -Operation Get -Key Wind
  ```
  (or `Grep` the key across `Resources*.resx`).
- Build to confirm the resx still compiles, using the **VS-MSBuild** procedure in
  `CLAUDE.md` (Debug/x64) — **not** `dotnet build` (it fails on the COM refs).

## Common tasks

### Add a new language (a whole new culture .resx)

`transform-resx.ps1` is the wrong tool here — it edits one key across existing
files. Adding a language means generating one new file with **every** key
translated. Use **[`new-language.ps1`](new-language.ps1)**, which clones the base
`Resources.resx` structure exactly and swaps in your translations.

**What you do NOT need to touch** (learned adding `ar-SA`):
- No C# change to register the language. `Localization.Initialize()`
  (`DataContext/Localization.cs`) scans the embedded compiled resources
  (`Resources.<code>.resources`) and adds any whose table has a **`ThisLanguage`**
  key to the in-app language list.
- **RTL is automatic** — `Localization.IsRightToLeft` reads
  `CultureInfo.GetCultureInfo(code).TextInfo.IsRightToLeft`, no hardcoded list.

**The one wiring step that IS required:** add the file to
`MarvinsAIRARefactored.csproj` as an `<EmbeddedResource>` (alphabetical order):
```xml
<EmbeddedResource Include="Resources\Resources.ar-SA.resx"><WithCulture>false</WithCulture></EmbeddedResource>
```
`EnableDefaultEmbeddedResourceItems` is **false**, so until you list it the file
is invisible to the build and the language won't appear.

**Steps:**
1. Dump the base keys+English values to build your translation map from:
   ```powershell
   pwsh -NoProfile -Command '[xml]$x = Get-Content -Raw -Encoding UTF8 ../../../MarvinsAIRARefactored/Resources/Resources.resx; $x.root.data | ForEach-Object { "{0}`t{1}" -f $_.name, $_.value }'
   ```
2. Author a JSON map `{ "Key": "translated value", … }`. Conventions:
   - **`ThisLanguage`** = `"Native (Country) - English (Country)"`
     (e.g. `"العربية (المملكة العربية السعودية) - Arabic (Saudi Arabia)"`).
   - **No uppercase variants:** translate each key once in normal case. All-caps UI
     is produced at runtime by `Localization.Upper` — never add an uppercased copy.
   - **Omit keys you want left as the English passthrough** — runtime falls back to
     base for any key not in your JSON. Leave out: SI/technical symbols
     (` Nm`, ` Hz`, ` g`, ` m/s`, `%`, `°`, `RPM`, `POV`, `ms`), brand names
     (`AppTitle`, `AdminBoxx`, `Trading Paints`, `Buy Me a Coffee`), and the
     `✓`/`✗` permission glyphs (`KeyPermissionGranted`/`Missing`). Translate
     word-based units (`sec`, `KPH`, `MPH`).
3. Generate and **read the report**:
   ```powershell
   pwsh -NoProfile -File ./new-language.ps1 -LanguageCode ar-SA -Json ./ar.json
   ```
   Confirm "left as English" lists ONLY your intended keep-list, and "JSON typos"
   is 0 (any typo'd key means a translation silently went nowhere).
4. Register in the csproj (above), then **build** (VS-MSBuild Debug/x64 per
   `CLAUDE.md`) and confirm exit 0. Optionally spot-check the file renders by
   reading a few `<value>` lines (the terminal shows `?`/`�` for non-Latin even
   when the bytes are correct — use the Read tool, not a grep dump, to eyeball it).

A native speaker spot-check of sim-racing terminology is the ideal final polish
but does not block shipping (untranslated/odd keys still fall back gracefully).

### Add a new UI string

1. Add the key to the base file first, then translate into every culture file.
   ```powershell
   # English base only:
   pwsh -NoProfile -File ./transform-resx.ps1 -Operation Add -Key Wind_Tooltip `
        -Value 'Adjusts wind fan strength' -BaseOnly
   ```
2. Add the (translated) value to each culture file. If you don't yet have
   translations, you can seed every file with the English value via `Add` (no
   `-BaseOnly`) and translate later — runtime falls back to base for any key left
   empty, so an untranslated key is safe but should be filled in.
3. Reference it in code/XAML as `Localization["Wind_Tooltip"]`. If it needs to
   render all-caps, use `Localization.Upper["Wind_Tooltip"]` /
   `{Binding Localization.Upper[Wind_Tooltip]}` — never add an uppercased twin key.

### Rename / re-key a string

Change the `name=` attribute consistently across all files in one run:

```powershell
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Rename -Key OldName -NewKey NewName -WhatIf
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Rename -Key OldName -NewKey NewName
```

Then update every `Localization["OldName"]` reference in code/XAML (use `Grep`),
including any `Localization.Upper["OldName"]` all-caps lookups.

### Transform an existing value everywhere

Brand-prefix or caption-change the same key across all languages. `Prepend` is
idempotent (skips any file whose value already starts with the prefix):

```powershell
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Prepend -Key AppTitle -Prefix 'MAIRA ' -WhatIf
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Prepend -Key AppTitle -Prefix 'MAIRA '
```

For a wholesale value replacement use `Operation Set`; to delete a retired key
use `Operation Remove` (also idempotent).

## When the script doesn't fit

These flags cover the routine jobs. An unusual one-off (e.g. a conditional edit,
a regex that depends on the current value, splitting one key into two) is better
served by a **small bespoke adaptation of the helper functions** in
`transform-resx.ps1` than by forcing the job through fixed flags — copy the
read/transform/write skeleton (it already handles BOM detection and the pwsh
guard) and write the specific transform inline. Still: prove on one, fan out,
verify, build.
