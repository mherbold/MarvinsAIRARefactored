# TTS-PLAN.md — ElevenLabs Flash TTS Integration for MAIRA

## Overview

This document is the authoritative implementation plan for integrating ElevenLabs Flash TTS into MAIRA. It covers all new files, all modifications to existing files, data models, default voice personalities, caching strategy, settings persistence, UI pages, and audio playback routing. Steps are ordered so that each one builds on the last and the project compiles cleanly at each checkpoint.

---

## Architecture Summary

```
Race event / simulator telemetry
		 │
		 ▼
  Commentary.cs          ← new component — detects events, generates text, assigns voice slot
		 │
		 ▼
  ElevenLabsTts.cs       ← new component — HTTP client, shared priority queue, DPAPI key storage
		 │
	┌────┴─────────────────────────────────────┐
	│  Cache hit?                               │
	│  Yes → load MP3 from disk                 │
	│  No  → POST to ElevenLabs API             │
	│        receive MP3 bytes                  │
	│        write to disk cache                │
	└──────────────┬───────────────────────────┘
				   │
				   ▼
		 AudioManager.cs  ← extended with PlayFromMemoryAsync(byte[])
				   │
				   ▼
		  FMOD output device  (same device the user has already configured)
```

---

## Voice Roster — Five Slots with Default Personalities

All voice IDs below are ElevenLabs stock voices. Users can replace any slot with their own voices (including cloned voices). Defaults are chosen to be clearly distinguishable from each other.

| Slot | Role | Default Voice Name | Default ElevenLabs Voice ID | Stability | Style | SimilarityBoost | SpeakerBoost | Personality Notes |
|------|------|-------------------|----------------------------|-----------|-------|-----------------|--------------|-------------------|
| 0 | Crew Chief | `Adam` | `pNInz6obpgDQGcFmaJgB` | 0.75 | 0.30 | 0.80 | true | Deep, authoritative, calm under pressure — tactical and direct |
| 1 | Spotter | `Sam` | `yoZ06aMxZJJ28mfd3POQ` | 0.90 | 0.15 | 0.75 | true | Clipped, fast, high-clarity — safety-critical, no drama |
| 2 | Sportscaster 1 (Lead) | `Antoni` | `ErXwobaYiN019PkySvjV` | 0.35 | 0.75 | 0.75 | true | Warm, theatrical, excited on big moments — main play-by-play |
| 3 | Sportscaster 2 (Color) | `Josh` | `TxGEqnHWrfWFTfGW9XjX` | 0.55 | 0.50 | 0.75 | true | Analytical, measured, conversational — insight and strategy |
| 4 | Pit Reporter | `Rachel` | `21m00Tcm4TlvDq8ikWAM` | 0.45 | 0.65 | 0.80 | true | Energetic, on-the-ground, slightly breathless — pit lane presence |

**Note:** Voice IDs should be verified against the user's ElevenLabs account at first run. The "Verify Key" button on the settings page will also confirm voice availability. If the user's subscription does not include a default voice, MAIRA should fall back gracefully and show a warning.

---

## Commentary Event Table

Events are detected in `Commentary.cs` by comparing current telemetry/session state to the previous tick. Each event type has a designated primary voice slot, optional handoff voice slot, a cooldown period, and a priority level.

| Event Type | Primary Slot | Handoff Slot | Cooldown (sec) | Priority | In-Race Only |
|------------|-------------|--------------|---------------|----------|--------------|
| Session start (green flag) | Sportscaster 1 | — | 999 | 3 | Yes |
| Overtake / position change | Sportscaster 1 | Sportscaster 2 | 20 | 3 | Yes |
| Close battle (gap < 1.0 s) | Sportscaster 2 | — | 30 | 4 | Yes |
| Fastest lap set | Sportscaster 1 | — | 60 | 3 | Yes |
| Pit stop entry | Pit Reporter | — | 30 | 2 | Yes |
| Pit stop exit | Pit Reporter | — | 30 | 2 | Yes |
| Caution / yellow flag | Sportscaster 1 | — | 60 | 2 | Yes |
| Red flag | Sportscaster 1 | — | 999 | 1 | Yes |
| Session end / checkered flag | Sportscaster 1 | — | 999 | 1 | Yes |
| Driver incident penalty | Sportscaster 2 | — | 45 | 4 | Yes |
| Car left (spotter) | Spotter | — | 3 | 1 | Yes |
| Car right (spotter) | Spotter | — | 3 | 1 | Yes |
| Clear (spotter) | Spotter | — | 3 | 1 | Yes |
| Overlap (spotter) | Spotter | — | 3 | 1 | Yes |
| Fuel warning | Crew Chief | — | 120 | 2 | Yes |
| Tire wear warning | Crew Chief | — | 120 | 2 | Yes |
| Pit window open | Crew Chief | — | 60 | 2 | Yes |
| Damage warning | Crew Chief | — | 90 | 1 | Yes |

**Handoff pattern:** When `HandoffSlot` is set, the primary voice says a handoff line (e.g. *"Let's go down to Sarah in pit lane —"*) and then the handoff voice adds follow-up detail. Both messages are queued sequentially at the same priority level.

---

## Phrase Template System

Commentary is generated from template strings. Templates support `{tokens}` for runtime substitution. Templates are defined as `static readonly` string arrays in `Commentary.cs` — multiple variants per event type add variety (MAIRA picks one randomly per event).

Example:
```csharp
private static readonly string[] OvertakeTemplates =
[
	"[excitedly] {attackerName} makes the move — he's through into P{position}!",
	"[excitedly] What a move by {attackerName}! He dives past {defenderName} for P{position}!",
	"[excited] {attackerName} goes for it and it sticks — P{position} is his!"
];
```

Spotter and crew chief phrases are short fixed strings (no tokens needed for most of them) and are ideal cache candidates.

---

## Phrase Cache Strategy

### Cache Location
```
%DOCUMENTS%\MarvinsAIRA Refactored\ElevenLabs\cache\
```

### Cache File Naming
```
{slotIndex}_{voiceId}_{sha256(normalizedText).Substring(0,16)}.mp3
```
- `slotIndex` is 0–4 (matches the `VoiceSlot` enum)
- `voiceId` is the ElevenLabs voice ID in use at generation time
- Normalized text: trimmed, lowercased, punctuation stripped — ensures casing variants of identical phrases share the cache entry
- Cache is **per-voice-ID**: if user changes a voice, old entries are ignored (not deleted — allows rollback)

### What Gets Pre-Cached
Spotter and crew chief phrases are fully enumerable. The "Pre-generate phrase cache" button calls `ElevenLabsTts.PregenerateSpotterCacheAsync()`, which iterates all fixed phrases for slots 0 and 1 and generates any that are missing.

### Cache Miss Behavior
Live API call → MP3 bytes received → written to cache → played. The cache write is fire-and-forget (does not block playback).

---

## Audio Playback — FMOD Extension

FMOD supports `FMOD_OPENMEMORY` to create a sound from a raw byte buffer in memory. A new method `PlayFromMemoryAsync(byte[] mp3Bytes, float volume)` will be added to `AudioManager`.

```
AudioManager.PlayFromMemoryAsync(byte[] mp3Bytes, float volume)
  → createSound(mp3Bytes, MODE.OPENMEMORY | MODE.CREATESTREAM, exinfo, out sound)
  → playSound(sound, channelGroup, false, out channel)
  → channel.setVolume(volume)
  → register for cleanup when playback ends
```

The method is `async` because it marshals back to the FMOD lock and then monitors channel state for cleanup. The caller (`ElevenLabsTts`) awaits it only to detect errors — it does not block for the full playback duration.

---

## API Key Security — DPAPI

The ElevenLabs API key is **not** stored in `Settings.xml`. It is stored separately using Windows DPAPI (`System.Security.Cryptography.ProtectedData`) with `DataProtectionScope.CurrentUser`.

```
%DOCUMENTS%\MarvinsAIRA Refactored\ElevenLabs\api-key.dat  ← encrypted blob
```

A new `ElevenLabsKeyStore` static helper class (in `Classes/`) handles protect/unprotect. The Settings property `ElevenLabsApiKey` is `[XmlIgnore]` and its getter/setter delegates to `ElevenLabsKeyStore`.

---

## New and Modified Files

### New Files

| File | Purpose |
|------|---------|
| `Components/ElevenLabsTts.cs` | HTTP client, voice slot roster, shared priority queue, cache I/O, FMOD playback hand-off |
| `Components/Commentary.cs` | Telemetry event detection, template expansion, voice slot assignment |
| `Classes/ElevenLabsKeyStore.cs` | DPAPI protect/unprotect for the API key |
| `Classes/VoiceSlotSettings.cs` | Data class for one voice slot (serializable, used inside Settings) |
| `Pages/ElevenLabsTtsPage.xaml` | Settings UI page |
| `Pages/ElevenLabsTtsPage.xaml.cs` | Code-behind for the settings page |

### Modified Files

| File | What Changes |
|------|-------------|
| `Components/AudioManager.cs` | Add `PlayFromMemoryAsync(byte[] mp3Bytes, float volume)` |
| `DataContext/Settings.cs` | Add ElevenLabs settings properties (see properties section below) |
| `App.xaml.cs` | Add `ElevenLabsTts` and `Commentary` as component properties; initialize and dispose them |
| `Windows/MainWindow.cs` | Add `AppPage.ElevenLabsTts` to the enum; add `_elevenLabsTtsPage` static field; wire into `RefreshWindow()` |
| `Controls/MairaAppMenuPopup.xaml.cs` | Add menu item, localization cases, help topic, and default page switch arm for `AppPage.ElevenLabsTts` |

---

## Settings Properties to Add to `DataContext/Settings.cs`

All follow the existing pattern: private backing field, public property with `if (value != _field)` guard and `OnPropertyChanged()` call.

### Global ElevenLabs Settings

```csharp
// #region ElevenLabs TTS

bool   ElevenLabsEnabled                  = false
string ElevenLabsApiKey                   = ""          // [XmlIgnore] — stored via DPAPI
string ElevenLabsModelId                  = "eleven_flash_v2_5"
float  ElevenLabsMasterVolume             = 0.85f
bool   ElevenLabsCommentaryEnabled        = true
bool   ElevenLabsSpotterEnabled           = true
bool   ElevenLabsCrewChiefEnabled         = true
int    ElevenLabsSessionCharactersUsed    = 0           // incremented at runtime, reset on app start
```

### Per-Voice-Slot Settings

Because each of the 5 slots needs the same set of properties, they are stored as a `List<VoiceSlotSettings>` property (XML-serializable) rather than 5×N flat properties. This keeps `Settings.cs` manageable.

```csharp
// #region ElevenLabs TTS — Voice Slots

List<VoiceSlotSettings> ElevenLabsVoiceSlots = [
	new() { /* Crew Chief defaults */ },
	new() { /* Spotter defaults */ },
	new() { /* Sportscaster 1 defaults */ },
	new() { /* Sportscaster 2 defaults */ },
	new() { /* Pit Reporter defaults */ }
]
```

### Per-Event-Type Toggles

```csharp
// #region ElevenLabs TTS — Commentary events

bool ElevenLabsCommentaryOvertake            = true
bool ElevenLabsCommentaryCloseBattle         = true
bool ElevenLabsCommentaryFastestLap          = true
bool ElevenLabsCommentaryPitStop             = true
bool ElevenLabsCommentaryCaution             = true
bool ElevenLabsCommentarySessionStartEnd     = true
bool ElevenLabsCommentaryIncident            = true
bool ElevenLabsCommentaryCrewFuelWarning     = true
bool ElevenLabsCommentaryCrewTireWarning     = true
bool ElevenLabsCommentaryCrewDamageWarning   = true
bool ElevenLabsCommentaryCrewPitWindowOpen   = true
```

---

## `VoiceSlotSettings` Class

Lives in `Classes/VoiceSlotSettings.cs`. XML-serializable. Constructed with defaults for each slot role.

```csharp
public class VoiceSlotSettings
{
	public string  RoleLabel        { get; set; }   // "Crew Chief", "Spotter", etc. (user-editable display name)
	public bool    Enabled          { get; set; }   // per-slot on/off
	public string  VoiceId         { get; set; }   // ElevenLabs voice ID
	public string  VoiceName       { get; set; }   // display name (populated after /v1/voices call)
	public float   Stability        { get; set; }
	public float   Style            { get; set; }
	public float   SimilarityBoost  { get; set; }
	public bool    SpeakerBoost     { get; set; }
	public float   Volume           { get; set; }   // per-slot volume multiplier (0–1)
}
```

Default values for each slot are applied in `Settings.cs` when `ElevenLabsVoiceSlots` is first initialized (and after a settings reset).

---

## `ElevenLabsTts` Component

### Responsibilities
- Holds one `HttpClient` (static/singleton, reused across requests)
- Manages the shared priority queue (`PriorityQueue<TtsRequest, int>`)
- Runs a single background `Task` that drains the queue one item at a time
- Checks the disk cache before making API calls
- Calls `AudioManager.PlayFromMemoryAsync` with the resulting MP3 bytes
- Tracks `ElevenLabsSessionCharactersUsed` in Settings
- Exposes `SpeakAsync(VoiceSlot slot, string text, int priority)` (called by `Commentary`)
- Exposes `PregenerateSpotterCacheAsync(CancellationToken ct)` (called from settings page button)
- Exposes `FetchVoicesAsync()` → `List<ElevenLabsVoice>` (called from settings page voice dropdowns)
- Exposes `VerifyApiKeyAsync()` → `(bool valid, string subscriptionTier, int remainingCharacters)` (called from settings page Verify button)

### Queue Item (`TtsRequest`)
```csharp
private sealed record TtsRequest(
	VoiceSlot   Slot,
	string      Text,
	int         Priority,
	string      CacheKey
);
```

### Priority Values (lower = higher priority)
```
Spotter        = 0
Crew Chief     = 1
Pit Reporter   = 2
Sportscaster 1 = 3
Sportscaster 2 = 4
```

### API Endpoint
```
POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id}
Headers:
  xi-api-key: {apiKey}
  Content-Type: application/json
  Accept: audio/mpeg
Body:
{
  "text": "...",
  "model_id": "eleven_flash_v2_5",
  "voice_settings": {
	"stability": 0.35,
	"similarity_boost": 0.75,
	"style": 0.6,
	"use_speaker_boost": true
  }
}
```

Response is raw MP3 bytes. No streaming is required — the full MP3 is small (a 5-second sentence is ~20 KB) and arrives in ~300 ms for Flash.

---

## `Commentary` Component

### Responsibilities
- Called once per MAIRA timer tick from `App.xaml.cs` (same pattern as other components)
- Compares current `Simulator` / `Telemetry` state to previous-tick snapshots to detect events
- Maintains per-event-type cooldown timers (simple `DateTime` timestamps)
- Picks a random template from the event's template array
- Substitutes `{tokens}` from live data
- Calls `ElevenLabsTts.SpeakAsync(slot, text, priority)`
- Does **not** speak if `ElevenLabsEnabled == false` or if the relevant per-event toggle is off

### State Tracking Fields
```csharp
private int   _previousPlayerPosition;
private float _previousGapToCarAhead;
private bool  _previousYellowFlag;
private bool  _previousIsInPits;
private float _previousBestLapTime;
private Dictionary<CommentaryEventType, DateTime> _lastSpokenTimes = new();
```

### Spotter Integration Note
The spotter calls (`CarLeft`, `CarRight`, `Clear`, `Overlap`) are derived from the existing `iRSDKSharper` data that MAIRA already reads. Commentary intercepts these values and voices them through the Spotter slot instead of (or in addition to) whatever iRacing's built-in spotter would say. A user setting controls whether to suppress iRacing's own spotter when MAIRA's is active.

---

## Settings UI Page — `ElevenLabsTtsPage.xaml`

The page follows the same XAML structure as `SpeechToTextPage.xaml` and `SoundsPage.xaml` — `MairaGroupBox` sections with `MairaSwitch`, `MairaKnob`, `MairaComboBox`, and `MairaButton` controls bound to `Settings.*` properties via the existing DataContext.

### Page Sections

#### Account
- API Key — `PasswordBox` (not bound to Settings directly; code-behind reads/writes via `ElevenLabsKeyStore`)
- `[Verify Key]` button — calls `ElevenLabsTts.VerifyApiKeyAsync()`, shows result inline (green checkmark + tier name, or red error)
- Model selector — `MairaComboBox` bound to `Settings.ElevenLabsModelId`, items: `eleven_flash_v2_5` / `eleven_turbo_v2_5`
- Master TTS volume — `MairaKnob` bound to `Settings.ElevenLabsMasterVolume`
- Session character usage — read-only `TextBlock` bound to `Settings.ElevenLabsSessionCharactersUsed` (formatted as "~{N} characters (~${cost:F3})")

#### Voice Roster (five expandable sections, one per slot)
Each section is a `MairaGroupBox` with the slot's role label as header. Contents:
- Enable/disable toggle — bound to `VoiceSlots[i].Enabled`
- Role label text box — bound to `VoiceSlots[i].RoleLabel`
- Voice dropdown — populated by calling `ElevenLabsTts.FetchVoicesAsync()` (cached for the session); bound to `VoiceSlots[i].VoiceId`; displays `VoiceName` in the list
- `[Preview]` button — plays ElevenLabs' static preview clip URL (no API cost); uses `MediaPlayer`
- `[Test]` button — calls `ElevenLabsTts.SpeakAsync` with a role-appropriate hardcoded test phrase
- Stability slider — `MairaKnob` bound to `VoiceSlots[i].Stability`, labeled "Consistency ↔ Expressiveness"
- Style slider — `MairaKnob` bound to `VoiceSlots[i].Style`, labeled "Neutral ↔ Dramatic"
- Similarity Boost slider — `MairaKnob` bound to `VoiceSlots[i].SimilarityBoost`
- Speaker Boost toggle — `MairaSwitch` bound to `VoiceSlots[i].SpeakerBoost`
- Per-slot volume — `MairaKnob` bound to `VoiceSlots[i].Volume`

#### Commentary
- Master commentary enable — `MairaSwitch` bound to `Settings.ElevenLabsCommentaryEnabled`
- Per-event checkboxes — one `MairaSwitch` per row in the event table above

#### Spotter & Crew Chief
- Spotter enable — `MairaSwitch` bound to `Settings.ElevenLabsSpotterEnabled`
- Crew Chief enable — `MairaSwitch` bound to `Settings.ElevenLabsCrewChiefEnabled`
- `[Pre-generate phrase cache]` button — calls `ElevenLabsTts.PregenerateSpotterCacheAsync()` with a `CancellationToken`; shows a progress indicator and character count while running
- `[Clear phrase cache]` button — deletes `%DOCUMENTS%\MarvinsAIRA Refactored\ElevenLabs\cache\` and recreates it empty

---

## Navigation Wiring Checklist

Adding the new page to MAIRA's navigation requires touching exactly four places:

1. **`Windows/MainWindow.cs`** — `AppPage` enum: add `ElevenLabsTts` (insert after `SpeechToText`)
2. **`Windows/MainWindow.cs`** — add `public static readonly ElevenLabsTtsPage _elevenLabsTtsPage = new();`
3. **`Windows/MainWindow.cs`** — `RefreshWindow()`: no special refresh method needed initially; add one later if voice dropdowns need repopulation after a key change
4. **`Controls/MairaAppMenuPopup.xaml.cs`** — four locations:
   - `Initialize()`: add `AppMenuItem` for `AppPage.ElevenLabsTts` after the `SpeechToText` entry
   - `Initialize()` default page switch: add arm `AppPage.ElevenLabsTts => _elevenLabsTtsPage`
   - `RelocalizeAppMenuItems()`: add `case AppPage.ElevenLabsTts: menuItem.DisplayName = localization["ElevenLabsTts"]; break;`
   - `UpdateSelectedAppPageText()`: add `case AppPage.ElevenLabsTts: SelectedAppPageText = localization["ElevenLabsTts_UC"]; break;`
   - `GetHelpTopicForAppPage()`: add `case AppPage.ElevenLabsTts: return "advanced/elevenlabs-tts/";`

---

## App.xaml.cs Changes

### New component properties (alongside existing ones)
```csharp
public ElevenLabsTts ElevenLabsTts { get; private set; } = null!;
public Commentary    Commentary    { get; private set; } = null!;
```

### Initialization (in the existing startup sequence, after `AudioManager` is initialized)
```csharp
ElevenLabsTts = new ElevenLabsTts();
ElevenLabsTts.Initialize();

Commentary = new Commentary();
Commentary.Initialize();
```

### Dispose (in the existing shutdown sequence)
```csharp
Commentary.Dispose();
ElevenLabsTts.Dispose();
```

### Timer tick
```csharp
Commentary.Tick();   // add after other component Tick() calls
```

---

## Implementation Steps

### Step 1 — `Classes/VoiceSlotSettings.cs`
Create the serializable `VoiceSlotSettings` data class with all fields and five static factory methods (`CreateCrewChief()`, `CreateSpotter()`, `CreateSportscaster1()`, `CreateSportscaster2()`, `CreatePitReporter()`) that return instances pre-loaded with the default values from the voice roster table.

### Step 2 — `Classes/ElevenLabsKeyStore.cs`
Create the DPAPI helper. Two methods: `SaveKey(string apiKey)` and `LoadKey() → string`. Uses `ProtectedData.Protect` / `ProtectedData.Unprotect` with `DataProtectionScope.CurrentUser`. Key file path: `Path.Combine(App.DocumentsFolder, "ElevenLabs", "api-key.dat")`.

### Step 3 — `DataContext/Settings.cs` — add ElevenLabs properties
Add all settings properties defined in the Settings Properties section above. Insert before the final `#endregion` of the file. The `ElevenLabsVoiceSlots` property initializer calls the five factory methods from `VoiceSlotSettings`. The `ElevenLabsApiKey` property is `[XmlIgnore]` and delegates to `ElevenLabsKeyStore`.

### Step 4 — `Components/AudioManager.cs` — `PlayFromMemoryAsync`
Add the new method. It must acquire `_lock`, create an FMOD sound with `MODE.OPENMEMORY | MODE.CREATESTREAM`, play it on a free channel, set volume, and schedule a cleanup callback (poll `channel.isPlaying` in a background task loop or via FMOD channel callback) to release the sound after playback ends.

### Step 5 — `Components/ElevenLabsTts.cs`
Create the full component: `HttpClient`, cache helpers, queue drain loop (`Channel<TtsRequest>` + background `Task`), `SpeakAsync`, `PregenerateSpotterCacheAsync`, `FetchVoicesAsync`, `VerifyApiKeyAsync`, `Initialize`, `Dispose`. Depends on Steps 2, 3, and 4.

### Step 6 — `Components/Commentary.cs`
Create the component with all phrase templates, state tracking fields, cooldown map, `Initialize`, `Dispose`, `Tick`. Wire into simulator telemetry reads. Depends on Step 5.

### Step 7 — `App.xaml.cs`
Add the two new component properties and wire them into startup, shutdown, and timer tick. Depends on Steps 5 and 6.

### Step 8 — `Pages/ElevenLabsTtsPage.xaml` + `.xaml.cs`
Create the full settings page. Code-behind handles: Verify Key button async call, Pre-generate cache button with progress and cancellation, Clear cache button, voice dropdown population (calls `FetchVoicesAsync` on page load), API key `PasswordBox` read/write via `ElevenLabsKeyStore`.

### Step 9 — Navigation wiring
Apply all four changes to `MainWindow.cs` and all five changes to `MairaAppMenuPopup.xaml.cs` per the Navigation Wiring Checklist above. Add `"ElevenLabsTts"` and `"ElevenLabsTts_UC"` localization keys (English default values: `"ElevenLabs TTS"` and `"ELEVENLABS TTS"`).

### Step 10 — Build and smoke test
Run the build. Navigate to the new page. Enter a valid API key, click Verify, select a voice, click Test for each slot. Verify audio plays through the configured output device. Verify cache file appears on disk.

### Step 11 — Spotter cache pre-generation test
Click Pre-generate phrase cache. Verify progress indicator runs, MP3 files appear in the cache folder, and character count updates. Click Clear cache, verify files are removed.

### Step 12 — Live commentary test
Connect iRacing session. Trigger an overtake event (or use the Debug page to fire a synthetic event). Verify the correct voice slot speaks and the queue handles rapid events without overlap.

---

## Open Questions / Future Work

- **Synthetic debug events**: Adding a Debug-page button to manually fire each commentary event type would greatly speed up testing. Can be added alongside Step 12 or deferred.
- **iRacing built-in spotter suppression**: iRacing's own spotter audio can be disabled via the iRacing app.ini. MAIRA could offer a toggle that writes that setting when MAIRA's spotter is enabled. Deferred — needs investigation of iRacing's app.ini support.
- **Localization of templates**: Commentary template strings are currently English-only. Full localization is out of scope for the initial implementation but template arrays could eventually be moved to resource files.
- **Sentence chunking for long strings**: ElevenLabs Flash has no practical length limit for a single sentence, but extremely long strings (> ~300 characters) may benefit from being split at natural sentence boundaries to reduce first-audio latency. Not needed for the spotter/crew-chief/short-commentary use case but worth revisiting for longer color commentary.
- **Cloned voice onboarding UI**: A future feature could include a wizard for uploading voice samples to ElevenLabs directly from within MAIRA.
- **ElevenLabs Conversational AI**: For a more dynamic sportscasting experience, ElevenLabs also offers a real-time conversational AI API. This is a significantly more complex feature and is explicitly out of scope for this implementation.
