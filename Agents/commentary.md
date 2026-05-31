# Commentary System

This sub-file covers the **Commentary** component: iRacing event detection, cooldowns, phrase resolution, JSON phrase templates, the LocalizationEditor tool, and per-event settings toggles.

For ElevenLabs API integration, voice slots, TTS queue/cache, and API key storage see [`Agents/elevenlabs-integration.md`](Agents/elevenlabs-integration.md).

---

## Source Files

| File | Purpose |
|---|---|
| `Components/Commentary.cs` | iRacing event detection → TTS dispatch |
| `Classes/CommentaryTemplates.cs` | Embedded JSON phrase loader with language fallback |
| `TTS/*.json` | Embedded phrase template files, one per language tag (in `MarvinsAIRARefactored/TTS/`) |
| `DataContext/Settings.cs` | All Commentary-related settings properties (region `Commentary — *`) |

---

## Architecture Overview

```
iRacing telemetry tick
		|
   Commentary.Tick()
		|  detects race events
		|  resolves phrases from CommentaryTemplates
		v
   TextToSpeech.Enqueue( slotIndex, text, priority )
```

`Commentary` is owned by `App` (`App.Instance!.Commentary`) and follows the Initialize / no-Shutdown pattern (no resources to release).

---

## Commentary Component

### Initialization

```csharp
app.Commentary.Initialize( language );  // e.g. "en-US"
```

Called at startup and whenever `Settings.CommentaryElevenLabsLanguage` changes. Delegates to `CommentaryTemplates.Initialize()`.

### Tick

Called every worker-thread frame by `App`:

```csharp
app.Commentary.Tick( app );
```

Only runs when `Settings.CommentaryEnabled && _isRacingActive`.

### Event Detection

Each event type has an independent cooldown (stored in `_cooldowns` dictionary keyed by event name) to prevent spam:

| Event | Trigger | Cooldown | Slot | Settings guard |
|---|---|---|---|---|
| `SessionStart` | State → Racing | — | Sportscaster1 | `CommentarySessionStartEnd` |
| `SessionEnd` | State → Checkered/CoolDown | — | Sportscaster1 | `CommentarySessionStartEnd` |
| `Overtake` | `CarIdxPosition[i]` improved vs. previous tick | 15 s | Sportscaster1 (player) / Sportscaster2 (others) | `CommentaryOvertake` |
| `CloseBattle` | Player's `CarIdxF2Time < 1.0 s` | 30 s | Sportscaster2 | `CommentaryCloseBattle` |
| `FastestLap` | New session-best lap time set | 20 s | Sportscaster1 | `CommentaryFastestLap` |
| `PitStopEntry` / `PitStopExit` | `CarIdxOnPitRoad` transition | 10 s per car | CrewChief (player) / PitReporter (others) | `CommentaryPitStop` |
| `Caution` | `SessionFlags` has `Caution`/`CautionWaving` | 60 s | Sportscaster2 | `CommentaryCaution` |
| `RedFlag` | `SessionFlags` has `Red` | 30 s | Sportscaster1 | *(always)* |
| `Incident` | `PlayerCarMyIncidentCount` increases | 20 s | CrewChief | `CommentaryIncident` |
| `CrewFuelWarning` | Estimated laps remaining ≤ 3.0 | 60 s | CrewChief | `CommentaryCrewFuelWarning` |
| `CrewPitWindowOpen` | `PitsOpen` transitions to true | 60 s | CrewChief | `CommentaryCrewPitWindowOpen` |
| `CrewTireWarning` | `(lastLap - bestLap) / bestLap > 1.5 %` | 60 s | CrewChief | `CommentaryCrewTireWarning` |
| `SpotterCarLeft/Right/Overlap` | `sim.CarLeftRight` | *(each tick)* | Spotter | `SpotterCarCalls` |

### Phrase Token Substitution

`ResolvePhrase(eventKey, params (string token, string value)[] substitutions)` picks a random variant from `CommentaryTemplates` and performs string-replace for all `{token}` placeholders. Token names are case-insensitive.

Common tokens: `{driver}`, `{attacker}`, `{defender}`, `{position}`, `{lapTime}`, `{laps}`, `{seconds}`, `{driver1}`, `{driver2}`.

---

## Commentary Templates & JSON Files

### CommentaryTemplates

`CommentaryTemplates.Initialize(language)` loads `TTS/{language}.json` from **embedded assembly resources** (not from disk). Falls back to `en-US` if the requested language file is missing.

`GetRandomPhrase(eventKey)` returns a random element from the phrase array, or `null` if the key is absent.

`GetAvailableLanguages()` enumerates embedded resource names to produce the list used by the language combo box.

### JSON Format

Each `TTS/{lang}.json` is a flat dictionary of event key → array of phrase strings:

```json
{
  "Overtake": [
	"[excitedly] {attacker} makes the move — he's through into P{position}!",
	"..."
  ],
  "SpotterCarLeft": [
	"Car left.",
	"Car to your left."
  ]
}
```

ElevenLabs **emotion tags** (e.g. `[excitedly]`, `[urgently]`, `[seriously]`) are valid inside phrase strings and are passed verbatim to the API. Not all models support them.

### Adding a New Event Key

**Always use the LocalizationEditor tool — never edit TTS JSON files directly via PowerShell or a text editor.**

1. Open `Tools/LocalizationEditor/Program.cs` and add a new `BuildMyNewKey()` method with phrase translations for all 25 languages. Add a matching `case` in `AddTtsKey()`.
2. Run: `dotnet run --project Tools/LocalizationEditor -- tts add-key MyNewKey`
3. Run: `dotnet run --project Tools/LocalizationEditor -- tts validate` to confirm no issues.
4. Add the detection logic in `Commentary.cs` (or another component that calls `app.TextToSpeech.Enqueue`).
5. If the event is user-togglable, add a `bool` property to `Settings.cs` in the `Commentary — Per-event commentary toggles` region and wire it in the detection code.
6. Rebuild the main project (JSON files are embedded resources — changes only take effect after a rebuild).

### Editing Existing Phrases

```
# Replace all phrases for one key in one language
dotnet run --project Tools/LocalizationEditor -- tts set-phrases SpotterClear de-DE "Frei." "Du bist frei." "Alles klar."

# Replace across all languages (use * as lang)
dotnet run --project Tools/LocalizationEditor -- tts set-phrases SpotterClear * "Clear." "You're clear."
```

### Adding a New Language

1. Add a new `case` entry for the language in each `Build*` method in `Program.cs`.
2. Run: `dotnet run --project Tools/LocalizationEditor -- tts add-lang {lang-tag}`
   - This creates `TTS/{lang-tag}.json` and adds the csproj entry automatically.
3. The combo box is populated automatically via `CommentaryTemplates.GetAvailableLanguages()`.

### Other Useful Commands

```
dotnet run --project Tools/LocalizationEditor -- tts list-keys       # all keys + missing languages
dotnet run --project Tools/LocalizationEditor -- tts show-key SpotterClear  # side-by-side per lang
dotnet run --project Tools/LocalizationEditor -- tts validate        # full consistency check
dotnet run --project Tools/LocalizationEditor -- tts rename-key OldKey NewKey
dotnet run --project Tools/LocalizationEditor -- tts remove-key ObsoleteKey
```

---

## Per-Event Settings Toggles

All Commentary settings live in `DataContext/Settings.cs` under `#region Commentary — *` blocks:

| Property | Type | Default | Notes |
|---|---|---|---|
| `CommentaryEnabled` | `bool` | `false` | Master on/off switch |
| `CommentaryElevenLabsLanguage` | `string` | `"en-US"` | Changing this calls `Commentary.Initialize(value)` immediately |
| `SpotterEnabled` | `bool` | `true` | Enables spotter calls |
| `CrewChiefEnabled` | `bool` | `true` | Enables crew chief calls |
| `CommentaryOvertake` | `bool` | `true` | Per-event toggle |
| `CommentaryCloseBattle` | `bool` | `true` | Per-event toggle |
| `CommentaryFastestLap` | `bool` | `true` | Per-event toggle |
| `CommentaryPitStop` | `bool` | `true` | Per-event toggle |
| `CommentaryCaution` | `bool` | `true` | Per-event toggle |
| `CommentarySessionStartEnd` | `bool` | `true` | Per-event toggle |
| `CommentaryIncident` | `bool` | `true` | Per-event toggle |
| `CommentaryCrewFuelWarning` | `bool` | `true` | Per-event toggle |
| `CommentaryCrewTireWarning` | `bool` | `true` | Per-event toggle |
| `CommentaryCrewDamageWarning` | `bool` | `true` | Per-event toggle |
| `CommentaryCrewPitWindowOpen` | `bool` | `true` | Per-event toggle |
| `SpotterCarCalls` | `bool` | `true` | Per-event toggle |

---

## ADMINBOXX Build Note

All Commentary features (`Commentary`, `CommentaryPage`) are compiled out when the `ADMINBOXX` preprocessor constant is defined.
