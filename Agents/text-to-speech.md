# Text-to-Speech (TTS) System

This sub-file covers the ElevenLabs-based text-to-speech pipeline: voice slots, queue/playback, commentary event detection, phrase templates, API key storage, and UI wiring.

## Source Files

| File | Purpose |
|---|---|
| `Components/TextToSpeech.cs` | HTTP queue, cache, ElevenLabs API calls, playback hand-off |
| `Components/Commentary.cs` | iRacing event detection → TTS dispatch |
| `Classes/VoiceSlotSettings.cs` | Per-slot voice parameters; default factory methods |
| `Classes/CommentaryTemplates.cs` | Embedded JSON phrase loader with language fallback |
| `Classes/ElevenLabsKeyStore.cs` | DPAPI-encrypted API key storage |
| `TTS/*.json` | Embedded phrase template files, one per language tag (in `MarvinsAIRARefactored/TTS/`) |
| `Pages/CommentaryPage.xaml/.cs` | Settings UI: key verification, voice/model pickers, per-slot knobs |
| `DataContext/Settings.cs` | All TTS-related settings properties (region `Commentary — *`) |

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
		|  writes to bounded Channel<SpeechRequest>(64)
		v
   ConsumeQueueAsync (background Task, SingleReader)
		|  cache hit? → read MP3 from disk
		|  cache miss? → CallApiAsync → ElevenLabs v1/text-to-speech → write cache
		v
   AudioManager.PlayFromMemoryAsync( mp3Bytes, volume )
```

`TextToSpeech` is owned by `App` (`App.Instance!.TextToSpeech`) and follows the standard Initialize / Dispose lifecycle.

---

## Voice Slots

Five fixed voice slots are always present in `Settings.CommentaryVoiceSlots` (`List<VoiceSlotSettings>`):

| Index | Constant | Default voice | Personality |
|---|---|---|---|
| 0 | `SlotCrewChief` | Adam (`pNInz6obpgDQGcFmaJgB`) | Tactical, authoritative, calm |
| 1 | `SlotSpotter` | Liam (`TX3LPaxmHKxFdv7VOQHJ`) | Clipped, fast, safety-critical |
| 2 | `SlotSportscaster1` | George (`JBFqnCBsd6RMkjVDRZzb`) | Warm, theatrical, lead play-by-play |
| 3 | `SlotSportscaster2` | Daniel (`onwK4e9ZLuTAKqWW03F9`) | Analytical, measured, colour commentary |
| 4 | `SlotPitReporter` | Jessica (`cgSgspJ2msm6clMCkdW9`) | Energetic, on-the-ground, breathless |

`VoiceSlotSettings.CreateDefaults()` returns this list. `Settings.CommentaryVoiceSlots` setter always ensures exactly 5 entries are present, padding with defaults if needed.

### VoiceSlotSettings Properties

| Property | Type | Notes |
|---|---|---|
| `RoleLabel` | `string` | User-editable display name |
| `Enabled` | `bool` | When false, no TTS is generated for this slot |
| `VoiceId` | `string` | ElevenLabs voice ID |
| `VoiceName` | `string` | Cached human-readable name (avoids extra API round-trip) |
| `Stability` | `float` 0–1 | Consistency vs. expressiveness |
| `Style` | `float` 0–1 | Exaggeration level |
| `SimilarityBoost` | `float` 0–1 | How closely to match the original voice |
| `SpeakerBoost` | `bool` | ElevenLabs speaker-boost processing |
| `Volume` | `float` 0–1 | Slot-level volume; multiplied with `Settings.CommentaryMasterVolume` at playback |

---

## TextToSpeech Component

### Enqueue

```csharp
app.TextToSpeech.Enqueue( slotIndex, text, priority );
```

- Returns immediately (fire-and-dispatch, not fire-and-forget — the consumer awaits each request).
- Silently drops if `Settings.CommentaryEnabled` is false or the slot is disabled/has no voice ID.
- The internal channel holds up to **64 requests**; oldest is dropped on overflow (`BoundedChannelFullMode.DropOldest`).
- `priority` is metadata on the request record — the channel is FIFO, not a heap. Use it as a convention hint for future prioritization if needed.

### Cache

Cached MP3 files live in `%DOCUMENTS%\MarvinsAIRA Refactored\TTS\Cache\`.

File name format:
```
{slotIndex}_{voiceId}_{languageId}_{hash}.mp3
```

The hash is a 16-char SHA-256 hex digest of a normalized+lowercased+punctuation-stripped version of the text combined with the voice settings (`stability_style_similarityBoost_speakerBoost`). This means casing variants of the same phrase share one cache entry, but any voice-setting change invalidates it.

Cache writes happen in a fire-and-forget `Task` so they never delay playback.

### API Call (ElevenLabs)

- Endpoint: `POST https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=mp3_44100_128`
- Auth header: `xi-api-key: {apiKey}`
- Body includes `text`, `model_id`, and `voice_settings` (stability, similarity_boost, style, use_speaker_boost).
- HTTP timeout: **10 seconds** (set on the shared static `HttpClient`).
- `Settings.SessionCharactersUsed` is incremented by `text.Length` on every successful (non-cached) API call.

### Key Verification

`VerifyKeyAsync()` probes four endpoints (voices read, models read, text-to-speech POST, user subscription) and returns a `KeyVerificationResult` with per-permission `PermissionStatus` values (`Granted`, `MissingPermission`, `InvalidKey`). `KeyVerificationResult.IsFullyFunctional` is true only when all four are `Granted`.

### Other Public Methods

| Method | Returns | Purpose |
|---|---|---|
| `GetVoicesAsync()` | `Dictionary<string,string>?` | Voice ID → name, sorted by name |
| `GetModelsAsync()` | `Dictionary<string,string>?` | Model ID → display name, filtered to TTS-capable only |
| `GetSubscriptionAsync()` | `SubscriptionInfo?` | Current billing-period character usage and limit |

---

## Commentary Component

`Commentary` is owned by `App` (`App.Instance!.Commentary`) and follows Initialize / no Shutdown pattern (no resources to release).

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

## API Key Storage

`ElevenLabsKeyStore` stores the ElevenLabs API key encrypted with **Windows DPAPI** (`DataProtectionScope.CurrentUser`). The encrypted blob is written to:

```
%DOCUMENTS%\MarvinsAIRA Refactored\ElevenLabs\api-key.dat
```

`Settings.CommentaryElevenLabsApiKey` is `[XmlIgnore]`; its getter calls `ElevenLabsKeyStore.LoadKey()` and its setter calls `ElevenLabsKeyStore.SaveKey()`. The key is **never** written to `Settings.xml`.

---

## Settings Properties Reference

All TTS settings live in `DataContext/Settings.cs` under `#region Commentary — *` blocks:

| Property | Type | Default | Notes |
|---|---|---|---|
| `CommentaryEnabled` | `bool` | `false` | Master on/off switch |
| `CommentaryElevenLabsApiKey` | `string` | *(DPAPI)* | `[XmlIgnore]`; routed through `ElevenLabsKeyStore` |
| `CommentaryElevenLabsModelId` | `string` | `"eleven_flash_v2_5"` | ElevenLabs model ID |
| `CommentaryMasterVolume` | `float` | `0.85` | Multiplied with each slot's `Volume` at playback |
| `CommentaryElevenLabsLanguage` | `string` | `"en-US"` | Changing this calls `Commentary.Initialize(value)` immediately |
| `SessionCharactersUsed` | `int` | 0 | `[XmlIgnore]`; runtime counter of API chars used this session |
| `CommentaryVoiceSlots` | `List<VoiceSlotSettings>` | 5 defaults | Setter ensures exactly 5 entries |
| `SpotterEnabled` | `bool` | `true` | Enables spotter calls (future — currently guarded in Commentary) |
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

## CommentaryPage (UI)

`Pages/CommentaryPage.xaml.cs` — activated via `OnPageActivated()` from `MainWindow`.

On activation it:
1. Populates the language ComboBox from `CommentaryTemplates.GetAvailableLanguages()`.
2. Loads the API key into the password field (read from DPAPI; never shown in plain text in settings XML).
3. If Commentary is enabled, calls `VerifyAndPopulateAsync()` which runs `VerifyKeyAsync()`, then `GetVoicesAsync()` and `GetModelsAsync()` to populate the voice and model pickers.

The page also re-runs `VerifyAndPopulateAsync()` when `Settings.CommentaryEnabled` flips to `true` via `PropertyChanged`.

---

## Common Pitfalls

- **Do not call `TextToSpeech.Enqueue` from the UI thread in a tight loop** — the channel is bounded at 64 and drops oldest entries (`BoundedChannelFullMode.DropOldest`).
- **Cache key includes voice settings** — changing Stability/Style/SimilarityBoost/SpeakerBoost on a slot invalidates its cache entries for existing phrases. This is intentional.
- **`SessionCharactersUsed` is runtime-only** (`[XmlIgnore]`) — it resets to 0 every time the app starts. It counts API characters since launch, not lifetime usage. Use `GetSubscriptionAsync()` for the ElevenLabs billing-period total.
- **CommentaryTemplates loads from embedded resources** — editing the JSON files in `TTS/` only takes effect after a rebuild. The files are not read from the documents folder at runtime.
- **ADMINBOXX build** — all TTS features (`TextToSpeech`, `Commentary`, `CommentaryPage`) are compiled out when the `ADMINBOXX` preprocessor constant is defined.
