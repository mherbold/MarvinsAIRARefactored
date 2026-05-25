# Audio & Sounds

## Related Source Files
- `Components/AudioManager.cs` — Audio device enumeration and management
- `Components/Sounds.cs` — Sound effect playback
- `Components/LFE.cs` — Low Frequency Effects (bass shaker) via DirectSound
- `Classes/CachedSound.cs` — Pre-loaded in-memory audio sample
- `Classes/CachedSoundPlayer.cs` — Plays a `CachedSound` via XAudio2
- `Pages/SoundsPage.xaml/.cs` — Sounds settings UI

---

## Audio Device Management (`AudioManager`)

`AudioManager` enumerates available audio output devices using Windows APIs and exposes the list to the settings UI. Components that need a specific audio device (Sounds, LFE) query `AudioManager` for a device by the user-configured name.

- Device list is refreshed on hot-plug events (signalled by `HidHotPlugMonitor`).
- The selected device names are persisted in `Settings.xml`.

---

## Sound Effects (`Sounds`)

`Sounds` plays short WAV sound effects for in-app events (e.g., context switch confirmation, warnings, notifications).

- Sound files are stored in `My Documents\MarvinsAIRA Refactored\Sounds\` and are copied there by the post-build `xcopy` step.
- Each sound is loaded at startup into a `CachedSound` (fully in memory) to avoid disk I/O during playback.
- Playback uses XAudio2 via `SharpDX.XAudio2` through `CachedSoundPlayer`.
- `Sounds` picks the correct audio output device from `AudioManager` based on user settings.

---

## Cached Sound Playback

`CachedSound` reads a WAV file once at startup and stores the decoded PCM data in a `byte[]`.

`CachedSoundPlayer` creates an XAudio2 source voice for each play request. Each play is fire-and-complete (no looping). Multiple overlapping plays of the same `CachedSound` are supported.

---

## Low Frequency Effects (`LFE`)

`LFE` captures audio from a configured DirectSound capture device (e.g., a "virtual cable" fed by the game) and converts it into tactile vibration output for bass shakers or buttkickers.

- Runs on a dedicated high-priority **LFE Worker Thread** to minimize latency.
- Uses `SharpDX.DirectSound` for audio capture (`DirectSoundCapture`).
- Post-processing applies a low-pass filter and configurable gain.
- Output is forwarded to a DirectInput FFB effect or a separate audio playback device, depending on configuration.
- For full FFB integration details see `docs/agents/force-feedback.md`.
