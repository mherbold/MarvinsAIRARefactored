---
name: localization
description: >-
  Add, rename, translate, or edit UI strings in MarvinsAIRA's localization
  resource files. Use whenever the task touches Resources*.resx or
  Localization.cs — e.g. adding a new UI string/label/tooltip/caption,
  adding a Localization["Key"] lookup, renaming or re-keying a localization
  string, translating a string across all 28 languages, brand-prefixing or
  changing a caption everywhere, or otherwise bulk-editing the resx string
  tables. Performs these as one idempotent script run, never a per-file Edit loop.
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

- **28 `.resx` files** in `MarvinsAIRARefactored/Resources/`:
  - **Base / English:** `Resources.resx` (`ThisLanguage` = "English (United States)").
  - **27 culture files:** `ca-ES, cs-CZ, cy-GB, da-DK, de-DE, es-ES, es-MX,
    fi-FI, fr-CA, fr-FR, he-IL, hu-HU, hy-AM, it-IT, ja-JP, nb-NO, nl-NL, pl-PL,
    pt-BR, pt-PT, ro-RO, ru-RU, sv-SE, th-TH, tr-TR, uk-UA, zh-Hans`.
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
- **BOM convention is MIXED — preserve per file, never normalize.**
  18 files carry a UTF-8 BOM (`ca-ES, cs-CZ, cy-GB, da-DK, fi-FI, fr-CA, he-IL,
  hu-HU, ja-JP, nb-NO, nl-NL, pl-PL, ro-RO, sv-SE, th-TH, tr-TR, uk-UA, zh-Hans`),
  the other 10 (including the base `Resources.resx`) do not. The script detects
  and re-applies each file's own BOM on write.
- **Embedded whitespace:** some values contain literal TABs (e.g. `uk-UA`). The
  script edits the `<data>`/`<value>` block surgically, so such content is
  preserved — do not "clean it up" as a side effect.
- **`_UC` keys:** ~110 keys have an uppercase twin, e.g. `Wind` → "Typhoon Wind"
  and `Wind_UC` → "TYPHOON WIND". They are independent keys with their own
  (uppercased) value, used where the UI wants all-caps. When you add or change a
  string that has a `_UC` twin, update **both**.

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
3. If the string has an all-caps `_UC` twin in the UI, add `Wind_Tooltip_UC` too
   with the uppercased value.
4. Reference it in code/XAML as `Localization["Wind_Tooltip"]`.

### Rename / re-key a string

Change the `name=` attribute consistently across all files in one run:

```powershell
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Rename -Key OldName -NewKey NewName -WhatIf
pwsh -NoProfile -File ./transform-resx.ps1 -Operation Rename -Key OldName -NewKey NewName
```

Then update every `Localization["OldName"]` reference in code/XAML (use `Grep`),
and rename the `_UC` twin (`OldName_UC` → `NewName_UC`) if one exists.

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
