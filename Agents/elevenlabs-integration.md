# ElevenLabs Integration

This sub-file covers the **ElevenLabs** TTS integration: the `TextToSpeech` component (HTTP queue, cache, API calls, playback), voice slots (`VoiceSlotSettings`), API key storage (`ElevenLabsKeyStore`), the `CommentaryPage` settings UI, and ElevenLabs-specific settings properties.

For iRacing event detection, phrase templates, and per-event toggles see [`Agents/commentary.md`](Agents/commentary.md).

---

## Source Files

| File | Purpose |
|---|---|
| `Components/TextToSpeech.cs` | HTTP queue, cache, ElevenLabs API calls, playback hand-off |
| `Classes/VoiceSlotSettings.cs` | Per-slot voice parameters; default factory methods |
| `Classes/ElevenLabsKeyStore.cs` | DPAPI-encrypted API key storage |
| `Pages/CommentaryPage.xaml/.cs` | Settings UI: key verification, voice/model pickers, per-slot knobs |
| `DataContext/Settings.cs` | ElevenLabs settings properties (region `Commentary — *`) |

---

## Architecture Overview

```
Commentary.Tick()
		|  resolves phrase
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

## API Key Storage

`ElevenLabsKeyStore` stores the ElevenLabs API key encrypted with **Windows DPAPI** (`DataProtectionScope.CurrentUser`). The encrypted blob is written to:

```
%DOCUMENTS%\MarvinsAIRA Refactored\ElevenLabs\api-key.dat
```

`Settings.CommentaryElevenLabsApiKey` is `[XmlIgnore]`; its getter calls `ElevenLabsKeyStore.LoadKey()` and its setter calls `ElevenLabsKeyStore.SaveKey()`. The key is **never** written to `Settings.xml`.

---

## Settings Properties Reference

ElevenLabs-specific settings live in `DataContext/Settings.cs` under `#region Commentary — *` blocks:

| Property | Type | Default | Notes |
|---|---|---|---|
| `CommentaryElevenLabsApiKey` | `string` | *(DPAPI)* | `[XmlIgnore]`; routed through `ElevenLabsKeyStore` |
| `CommentaryElevenLabsModelId` | `string` | `"eleven_flash_v2_5"` | ElevenLabs model ID |
| `CommentaryMasterVolume` | `float` | `0.85` | Multiplied with each slot's `Volume` at playback |
| `SessionCharactersUsed` | `int` | 0 | `[XmlIgnore]`; runtime counter of API chars used this session |
| `CommentaryVoiceSlots` | `List<VoiceSlotSettings>` | 5 defaults | Setter ensures exactly 5 entries |

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
- **ADMINBOXX build** — all TTS features (`TextToSpeech`, `CommentaryPage`) are compiled out when the `ADMINBOXX` preprocessor constant is defined.
