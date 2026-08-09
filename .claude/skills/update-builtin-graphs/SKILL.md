---
name: update-builtin-graphs
description: >-
  Normalize and ship the maintainer's updated built-in FFB graphs. Use after
  the maintainer re-exports/saves updated .mairagraph files into
  MarvinsAIRARefactored/BuiltInGraphs — it fixes each file's graph name and
  GraphId back to the shipped identity (the update workflow goes through a
  differently-named clone, which leaks the clone's name and a fresh id into the
  file), syncs any changed graph descriptions and node descriptions into the
  localization tables with fresh translations for all languages, and refreshes
  the racing-wheel web documentation's built-in graphs coverage. Invoke on
  "update the built-in graphs", "fix the built-in graph names", "I saved new
  built-in graphs", or after any change to BuiltInGraphs/*.mairagraph.
---

# Update built-in graphs

The maintainer updates a shipped built-in graph by **cloning it in the app,
editing the clone, and saving/exporting the clone over the shipped file** in
`MarvinsAIRARefactored/BuiltInGraphs/`. That workflow leaks three things into
the file that must be fixed before shipping, and the file may carry new
description text that must be translated and documented. This skill does all of
it in one pass.

## Why the identity fix matters (do not skip)

- The launch-time sync (`Settings.EnsureBuiltInFFBGraphsInitialized`) matches
  stored graphs **by name**. A file named `Marvin's native 60 Hz.mairagraph`
  whose `<Name>` says `Marvin's native 60 Hz (clone)` would create a duplicate
  graph AND purge the correctly-named one as "retired". The wizard also looks
  up the flagship graph by the exact string `Marvin's easy detail adjustment`.
- The **GraphId** scopes every user's per-context module values and drives
  import matching. A clone carries a freshly-minted id — shipping it would
  orphan all testers' saved values. The shipped id must stay what it was in the
  last committed version of the file.
- `<IsBuiltIn>` says `false` in clone exports (the loader forces `true` at
  runtime, but keep the file honest).

## Step 1 — Fix the graph identities

Run the bundled script (from anywhere inside the repo):

```powershell
pwsh -NoProfile -File .claude/skills/update-builtin-graphs/scripts/fix-graph-identity.ps1
```

It is idempotent and per-file reports `FIXED` or `ok`: `<Name>` ← file name
stem, `<GraphId>` ← the id in `git show HEAD:<file>` (kept as-is for a file new
to git), `<IsBuiltIn>` ← `true`, plus a duplicate-id check across the set.

Reference — the shipped ids (sanity check against the script's output; if HEAD
itself ever carries a wrong id, restore from this table):

| Graph | GraphId |
|---|---|
| Low latency 360 Hz detail booster & limiter (flagship) | `87582264ef2c4b2c8cf3532d33c91c6a` |
| Low latency 60 Hz detail booster & limiter | `415930263cef4bf8955112282f81e451` |
| 360 Hz slew & total compression | `4707d38812774e2b9144f1c081759e57` |
| Hybrid multi adjustment toolkit | `0c3fd50625f342c393fe013eb43fa355` |

## Step 2 — Sync descriptions into the localization tables

Built-in graphs render their descriptions **translated** via resx keys, with
the text stored in the graph file as the English fallback:

- Graph display NAME → key `FFBGraphName<Name alnum-only>`
  (e.g. `FFBGraphNameLowlatency60Hzdetailboosterlimiter`) — see
  `FFBGraphViewModel.NameLocalizationKey`. The raw English name stays the
  stable identifier (dictionary key, stored selection, migration targets,
  export file name); ONLY the graph-selector display localizes. A renamed or
  brand-new built-in needs its name key added + translated, and the old name's
  key removed.
- Graph description → key `FFBGraphDescription<Name alnum-only>`
  (e.g. `FFBGraphDescriptionMarvinsnative60Hz`) — see
  `FFBGraphViewModel.DescriptionLocalizationKey`.
- Node (module) description **override** → key
  `FFBNodeDescription<Name alnum-only><ModuleId alnum-only>`
  (e.g. `FFBNodeDescriptionMarvinsnative60HzSource360`) — see
  `FFBGraphViewModel.NodeDescriptionLocalizationKey`. Only modules whose
  `<Description>` element is **non-empty** get a key; empty ones fall back to
  the per-module-type `FFBModuleDescription<Type>` defaults, which are already
  translated and are NOT this skill's business.

Procedure:

1. Extract from every `.mairagraph`: the graph `<Description>` and each
   module's non-empty `<Description>`, and derive each key (drop every
   non-alphanumeric from the graph name; module ids are already alphanumeric).
2. Diff against the base `Resources.resx`: which keys are **new**, which have
   **changed** English text, which existing `FFBGraphDescription*` /
   `FFBNodeDescription*` keys are now **orphaned** (description removed or
   module deleted).
3. Nothing new/changed/orphaned → skip to Step 3.
4. For new/changed keys: write the English map to a scratchpad JSON
   (`builtingraphdesc.en.json`, keys → English text), then spawn translation
   sub-agents — **always with `model: "opus"` (Opus 5, the maintainer's
   usage-conservation choice — never Fable)** — to produce
   `builtingraphdesc.<code>.json` per culture (~7 languages per agent; give
   them the English JSON path, the exact key set, and the house rules: keep
   iRacing/Hz/LFE/ABS/V8/A/B/percent tokens verbatim, natural sim-racing
   vocabulary, each language's own casing). Enumerate the live culture list
   with `Glob Resources.*.resx` — do not trust a hardcoded count.
5. Validate every JSON (exact same key set as `en`, no empty values), then
   apply with the bundled upserter — prove on base first, then fan out:

   ```powershell
   pwsh -NoProfile -File .claude/skills/update-builtin-graphs/scripts/upsert-resx-keys.ps1 -JsonDir <scratchpad> -Prefix builtingraphdesc -Only en
   pwsh -NoProfile -File .claude/skills/update-builtin-graphs/scripts/upsert-resx-keys.ps1 -JsonDir <scratchpad> -Prefix builtingraphdesc
   ```

   It adds missing keys and **replaces** changed values (idempotent, BOM/EOL
   preserving, XML-validated per write).
6. Remove orphaned keys across all files with the localization skill's
   `transform-resx.ps1 -Operation Remove -Key <key>`.
7. Verify (`grep -c` the keys across `Resources*.resx`) and **build** with VS
   MSBuild Debug x64 per CLAUDE.md (never `dotnet build`). Exit 0 required.

## Step 3 — Refresh the racing-wheel web documentation

Update `Server/Wordpress/Documentation/racing-wheel.txt` (the FFB graph /
built-in graphs coverage): one entry per built-in graph describing, in plain
user-facing language, **what the graph does** (derive it from the actual module
chain in the file plus the graph/node descriptions — read the graph, don't
guess) and **which users it suits** (e.g. flagship = most drivers wanting an
easy detail knob; native 60 Hz = purists who want the classic signal;
Alan Le's = users of the old algorithm families). Keep the graphs in the same
order the app lists them, flagship first.

Conventions (see [[app-documentation-location]]): WordPress-block format —
`<p class="section" id="...">` for the section, `<p class="setting">` for a
graph's name line, plain `<p>` for the description; sentence case; preserve the
prev/next nav footer; defaults come from code, not XAML. **Do not deploy to
WordPress from this skill** — deployment and verification belong to
`/commitpush`, which ships the doc together with the graph/resx changes in the
same commit. While the FFB graph system is still alpha-only (ffb-stack not yet
released), ALSO check whether ALPHA-TESTING.md's built-in graph descriptions
need the same content refresh — testers read that file, not the live docs.

## Report

Summarize: per-file identity fixes (name/id/flag), localization keys
added/replaced/removed (+ translation coverage count), doc sections updated,
and the build result. **No git writes** — the maintainer commits via
`/commitpush` or GitHub Desktop ([[no-git-activity]]).
