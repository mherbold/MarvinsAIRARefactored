# CLAUDE.md

## Project Overview

**MarvinsAIRA Refactored** (v2.0) is a Windows desktop application written in **C# 13 / .NET 9** using **WPF** (Windows Presentation Foundation). It is a sim-racing companion tool for **iRacing** that provides advanced force-feedback processing, steering effects, pedal haptics, hardware integrations, and various overlays.

The project has a second build target called **AdminBoxx**, controlled via the `ADMINBOXX` preprocessor constant. When `ADMINBOXX` is defined, many features are disabled and the app runs as a simpler hardware-controller utility.

---

## Project Rules

- Use the Edit and Write tools to modify files. If an edit cannot be applied directly, explain why and stop.
- Do not use PowerShell, terminal commands, or shell scripts to write or modify file content. The terminal is fine for builds, git, and other non-editing tasks.

---

## Coding Conventions

- **Use `var`** whenever possible in C# code.
- Use **descriptive variable names** (e.g., `var leftTargetPositionTenths` not `var i`).
- All components use a consistent **logging pattern**: `app.Logger.WriteLine( "[ComponentName] MethodName >>>" )` / `"<<< MethodName"`.
- `UpdateInterval` constants (typically multiples of 6 frames at 60 Hz) throttle non-critical UI updates.
- Regex patterns use `[GeneratedRegex]` source generators.
- `MethodImpl(AggressiveInlining)` is applied to hot-path math helpers in `MathZ` and other performance-critical components.
- **Localize** all UI strings — never hardcode labels or units; use `Localization["Key"]`.
- **Custom controls only** — never use raw WPF `TextBox`, `ComboBox`, `Button`, `CheckBox`, `Slider`, `GroupBox`, or `TabItem` when a `Maira*` equivalent exists.
- **Settings ordering** — properties in `Settings.cs` and `ContextSettings.cs` must match UI top-to-bottom / left-to-right order.

---

## Build Configuration

| Property | Value |
|---|---|
| Target Framework | `net9.0-windows10.0.19041.0` |
| C# Version | 13.0 |
| Output Type | `WinExe` |
| Nullable | enabled |
| Implicit Usings | enabled |
| Unsafe Blocks | allowed |
| Platforms | AnyCPU, x64 |
| High DPI Mode | PerMonitorV2 |
| Min OS | Windows 10 2004 (19041) |

The app version is computed at build time from the current UTC date via MSBuild expressions — no manual version bumping is needed.

### Build Configurations
- **Debug** — includes extra logging, writes `SessionInfo.yaml` and `TelemetryData.yaml` to the documents folder, and logs min/max FrameRate/GpuUsage every second.
- **Release** — standard release build.
- **ADMINBOXX** — strips out most features (steering effects, pedals, speech-to-text, cloud updates, etc.) and rebrands the app as "AdminBoxx".

### Post-Build Events
The post-build step copies several asset folders (sounds, recordings, calibration files, STT files, SBT files) to the user's `My Documents\MarvinsAIRA Refactored\` folder using `xcopy`.

---

## NuGet / External Dependencies

| Package | Purpose |
|---|---|
| `IRSDKSharper` (1.1.8) | iRacing SDK wrapper — provides telemetry and session info |
| `SharpDX.DirectInput` (4.2.0) | DirectInput for racing wheel / joystick input and FFB |
| `SharpDX.DirectSound` (4.2.0) | DirectSound for LFE (Low Frequency Effects) audio capture |
| `Accord` / `Accord.Neuro` / `Accord.Statistics` (3.8.0) | Machine-learning / signal-processing utilities |
| `CsvHelper` (33.1.0) | CSV reading/writing (calibration data, recordings) |
| `Newtonsoft.Json` (13.0.4) | JSON serialization (cloud service responses) |
| `OpenMacroBoard.SDK` + `StreamDeckSharp` (6.1.0) | Elgato Stream Deck integration |
| `Microsoft.Web.WebView2` (1.0.4022.49) | Embedded Chromium browser (Speech-to-Text bridge) |
| `Microsoft.Windows.CsWin32` (0.3.287) | Source-generated P/Invoke wrappers |
| `SharpZipLib` (1.4.2) | BZip2 decompression (TradingPaints livery downloads) |
| `System.IO.Ports` (10.0.9) | USB serial communication (AdminBoxx, Wind, SBT) |
| `System.Management` (10.0.9) | WMI queries (device enumeration) |
| `System.Net.Http` (4.3.4) | Legacy HTTP API compatibility support |
| `System.Text.RegularExpressions` (4.3.1) | Regex API compatibility support |

### Local DLL References
| DLL | Purpose |
|---|---|
| `SimagicHPR.dll` | Simagic HPR pedal haptics API |
| `vJoyInterfaceWrap.dll` | vJoy virtual joystick output |
| `vJoyInterface.dll` | Native vJoy runtime dependency |
| `LogitechSteeringWheelEnginesWrapper.dll` | Logitech wheel LED/effects support |
| `fmod.dll` / `fmodL.dll` | FMOD native audio runtime libraries |

---

## Project Structure

> **Important for resolving file paths:**
> The solution root and the main application project share the same name, creating a **nested directory**:
> `[repo root]/MarvinsAIRARefactored/MarvinsAIRARefactored.csproj`
> All application source files (Components, DataContext, Windows, Pages, etc.) live under
> `[repo root]/MarvinsAIRARefactored/` — **not** directly under `[repo root]/`.

---

## Architecture & Key Patterns

### Global Singleton (`App`)
`App` (in `App.xaml.cs`) is the central singleton accessed via `App.Instance!`. It owns and initializes every component. All components call `App.Instance!` to access sibling services. This is the primary way components communicate.

---

## Where Things Live

All paths are relative to the nested project folder `MarvinsAIRARefactored/` (see Project Structure above).

### Top-level
| File | Purpose |
|---|---|
| `App.xaml.cs` | Application entry point and the `App.Instance!` singleton; owns/initializes every component |
| `GlobalSuppressions.cs` | Assembly-wide analyzer suppressions |
| `MarvinsAIRARefactored.csproj` | Project file — dependencies, build configs, post-build `xcopy` steps |

### `Components/` — runtime services (owned by `App`, reached via `App.Instance!`)
| Area | Files |
|---|---|
| Force feedback / wheel | `RacingWheel.cs`, `SteeringEffects.cs`, `DirectInput.cs`, `Drivers.cs` |
| Pedals & haptics | `Pedals.cs`, `SeatBeltTensioner.cs`, `SeatBeltTensionerGraph.cs` |
| iRacing data | `Telemetry.cs`, `Simulator.cs` |
| Audio | `AudioManager.cs`, `Sounds.cs`, `LFE.cs` |
| Speech / commentary | `SpeechToText.cs`, `TextToSpeech.cs`, `Commentary.cs`, `ChatQueue.cs` |
| Hardware integrations | `Wind.cs`, `AdminBoxx.cs`, `StreamDeck.cs`, `VirtualJoystick.cs`, `HidHotPlugMonitor.cs` |
| Cloud / external | `CloudService.cs`, `TradingPaints.cs` |
| App / utility | `AppManager.cs`, `Logger.cs`, `Debug.cs`, `Graph.cs`, `RecordingManager.cs`, `TimingMarkers.cs`, `SettingsFile.cs`, `MultimediaTimer.cs`, `TopLevelWindow.cs` |

### `DataContext/` — settings, binding, localization
| File | Purpose |
|---|---|
| `Settings.cs` | Global persisted settings (property order must match UI order) |
| `ContextSettings.cs` | Per-context (car/track) settings |
| `Context.cs`, `ContextSwitches.cs` | Context selection and switching logic |
| `DataContext.cs` | Root WPF binding object |
| `Localization.cs` | UI string table — source for `Localization["Key"]` lookups |

### `Pages/` — feature UI (each page pairs with its component)
| Page | Backing component(s) |
|---|---|
| `SteeringEffectsPage` | `SteeringEffects.cs`, `RacingWheel.cs` |
| `SeatBeltTensionerPage` | `SeatBeltTensioner.cs` |
| `WindPage` | `Wind.cs` |
| `SoundsPage` | `Sounds.cs`, `AudioManager.cs` |
| `SpeechToTextPage` | `SpeechToText.cs` |
| `CommentaryPage` | `Commentary.cs`, `TextToSpeech.cs`, `ElevenLabs.cs` |
| `TradingPaintsPage` | `TradingPaints.cs` |
| `AppManagerPage` | `AppManager.cs` |
| `GraphPage` | `Graph.cs`, `GraphBase.cs` |
| `SimulatorPage` | `Simulator.cs`, `Telemetry.cs` |
| `OverlaysPage` | overlay windows in `Windows/` |
| `AdminBoxxPage` | `AdminBoxx.cs` |
| `DebugPage` | `Debug.cs` |
| `ContributePage`, `DonatePage`, `HelpPage` | `CloudService.cs`, `HelpService.cs` |

### `Windows/` — top-level windows & overlays
`MainWindow` is the shell. Overlays: `GapMonitorWindow`, `GripOMeterWindow`, `CursorCountdownOverlay`. Dialogs/wizards: `WizardWindow`, `ErrorWindow`, `HelpWindow`, `PickProcessWindow`, `SpeechToTextWindow`, `NewVersionAvailableWindow`, `RunInstallerWindow`, `UpdateButtonMappingsWindow`, `UpdateContextSwitchesWindow`.

### `Controls/` — custom `Maira*` controls (use these, never raw WPF equivalents)
`MairaButton`, `MairaComboBox`, `MairaTextBox`, `MairaSwitch`, `MairaKnob`, `MairaDualSlider`, `MairaExpander`, `MairaGroupBox`, `MairaStatusBar`, `MairaButtonMapping`, `MairaMappableButton`, `MairaAppMenuButton`, `MairaAppMenuPopup`.

### `Classes/` — helpers & data types
Math/signal: `MathZ.cs`, `RlsWheelVelocityPredictor.cs`. Audio: `CachedSound.cs`, `CachedSoundPlayer.cs`. Recording: `Recording.cs`, `RecordingData.cs`. Serialization: `Serializer.cs`, `SerializableDictionary.cs`. Commentary/voice: `CommentaryTemplates.cs`, `UserCommentaryPhrases.cs`, `VoiceSlotSettings.cs`, `ElevenLabs.cs`, `ElevenLabsKeyStore.cs`. Hardware/util: `UsbSerialPortHelper.cs`, `CpuAffinityHelper.cs`, `ButtonMappings.cs`, `HelpService.cs`, `TextBoxBehaviors.cs`, `Color.cs`, `Misc.cs`.

### `Themes/`
`DarkTheme.xaml`, `LightTheme.xaml`, `Generic.xaml`, plus per-control theme resources.
