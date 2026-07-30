# CLAUDE.md

## Project Overview

**MAIRA** (v2.1, "Marvin's Awesome iRacing App" — the repo keeps its historical `MarvinsAIRARefactored` name) is a Windows desktop application written in **C# 13 / .NET 9** using **WPF** (Windows Presentation Foundation). It is a sim-racing companion tool for **iRacing** that provides advanced force-feedback processing, steering effects, pedal haptics, hardware integrations, and various overlays. A **game bridge** feeds telemetry from other simulators (Le Mans Ultimate, rFactor 2, the Assetto Corsa family, RaceRoom) into the same iRacing-native pipeline.

The project has a second build target called **AdminBoxx**, controlled via the `ADMINBOXX` preprocessor constant. When `ADMINBOXX` is defined, many features are disabled and the app runs as a simpler hardware-controller utility.

---

## Project Rules

### AI model preferences
- **Main agent: Fable 5. Sub-agents: Opus 5** — when spawning sub-agents (Agent tool, workflows), pass `model: "opus"` rather than letting them inherit the main-agent model.

### Editing files
- **Default to the Edit and Write tools** for normal, one-off code changes — anything that requires reasoning about the code. If an edit can't be applied directly, explain why and stop.
- **For bulk mechanical edits, write a script — do NOT loop the Edit tool.** A "bulk mechanical edit" is the *same deterministic transformation* applied across many files or many occurrences: renaming a key in every `.resx`, prefixing a string across all localizations, project-wide find-and-replace, etc. When the same change touches ~3+ files (or many repeats of one pattern), reading and editing each file individually burns a large amount of usage and invites copy-by-copy mistakes. Write one script, prove it on a single file, then run it once across the rest.

### Scripting rules (for bulk edits)
- **Use `pwsh` (PowerShell 7+), never `powershell.exe` (Windows PowerShell 5.1).** `powershell.exe` corrupts UTF-8 and destroys non-ASCII text. `pwsh` defaults to UTF-8 and round-trips it correctly.
- **Prove the transform before fanning out:** run it on ONE file, show the resulting diff, and confirm correctness (no missing/extra spaces, no doubling, encoding intact) before touching the rest.
- If the transform is wrong, **fix the script and re-run it** — never patch the result file-by-file with the Edit tool. Make scripts **idempotent** so a second run is a no-op rather than double-applying.
- Verify with a single grep/search across the affected files afterward, then build to confirm it still compiles. The terminal remains fine for builds, git, and other non-editing tasks.

### Localization
- For localization work specifically (editing `Resources.*.resx` / `Localization.cs` — adding, renaming, or translating UI strings), use the **`localization` skill** in `.claude/skills/localization/`. It carries the confirmed file set, BOM convention, and a reusable `transform-resx.ps1` for the per-file encoding details.

---

## Coding Conventions

- **Use `var`** whenever possible in C# code.
- Use **descriptive variable names** (e.g., `var leftTargetPositionTenths` not `var i`).
- All components use a consistent **logging pattern**: `app.Logger.WriteLine( "[ComponentName] MethodName >>>" )` / `"<<< MethodName"`.
- `UpdateInterval` constants (typically multiples of 6 frames at 60 Hz) throttle non-critical UI updates.
- Regex patterns use `[GeneratedRegex]` source generators.
- `MethodImpl(AggressiveInlining)` is applied to hot-path math helpers in `MathZ` and other performance-critical components.
- **Localize** all UI strings — never hardcode labels or units; use `Localization["Key"]`.
- **Sentence case for UI text** — settings labels, buttons, group-box/tab headers, combo-box options, and status/toast text use **sentence case** (capitalize only the first word + proper nouns), not Title Case. Keep capitalized: brand/product names (iRacing, GitHub, Discord, Simagic, Windows, Trading Paints, Stream Deck, AdminBoxx, MAIRA, Typhoon Wind, G Tensioner, Xtreme Scoring Race Control, Grip-O-Meter) and acronyms/units/symbols (FFB, ABS, CPU, LFE, UI, API, Hz, Nm, RPM, Ay, Vy, P1000…). This applies to the English base `Resources.resx`; translations follow each language's own casing rules (e.g. German keeps nouns capitalized) and are produced by the machine-translation pipeline.
- **Custom controls only** — never use raw WPF `TextBox`, `ComboBox`, `Button`, `CheckBox`, `Slider`, `GroupBox`, or `TabItem` when a `Maira*` equivalent exists.
- **Settings ordering** — properties in `Settings.cs` and `ContextSettings.cs` must match UI top-to-bottom / left-to-right order.
- **`MairaKnob` step sizes are two separate things** — `ClickStepSize` is the +/- button increment (only a fallback for mappable knobs; their click step comes from the `MappableActionCatalog` `DefaultStepSize` / `Settings.KnobStepSizes`), and `DragStepSize` is the drag increment (always read from XAML). Set each deliberately — never just mirror one into the other. `DragStepSize` is much finer because it is applied per pixel of drag (drag value change = `pixelsMoved × DragStepSize`), so it is typically ~100× smaller than the click step; e.g. `RacingWheelAutoTarget` uses `ClickStepSize="0.5"` / `DragStepSize="0.001"`.

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

### How to Build

**Do not use `dotnet build`** — this project uses COM references (`ResolveComReference`) that require the full Visual Studio MSBuild, not the .NET SDK's MSBuild. `dotnet build` will always fail with MSB4803.

Use the PowerShell tool with VS MSBuild directly:

```powershell
$msbuild = (Get-ChildItem "C:\Program Files\Microsoft Visual Studio" -Recurse -Filter "MSBuild.exe" | Where-Object { $_.FullName -like "*amd64*" } | Select-Object -First 1).FullName
$sln = "C:\Users\marvi\OneDrive\Documents\GitHub\MarvinsAIRARefactored\MarvinsAIRARefactored.sln"
& $msbuild $sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m /nologo /v:minimal 2>&1 | Select-Object -Last 30
```

A clean build exits with code 0. The post-build `xcopy` steps use the `/D` flag and will report "0 File(s) copied" when the destination is already up to date — this is normal, not an error.

### Post-Build Events
The post-build step copies several asset folders (sounds, recordings, calibration files, STT files, SBT files) to the user's `My Documents\MAIRA\` folder using `xcopy`.

---

## NuGet / External Dependencies

| Package | Purpose |
|---|---|
| `IRSDKSharper` (1.3.0) | iRacing SDK wrapper — provides telemetry and session info |
| `SharpDX.DirectInput` (4.2.0) | DirectInput for racing wheel / joystick input and FFB |
| `Accord` / `Accord.Neuro` / `Accord.Statistics` (3.8.0) | Machine-learning / signal-processing utilities |
| `CsvHelper` (33.1.0) | CSV reading/writing (calibration data, recordings) |
| `Newtonsoft.Json` (13.0.4) | JSON serialization (cloud service responses) |
| `OpenMacroBoard.SDK` + `StreamDeckSharp` (6.1.0) | Elgato Stream Deck integration |
| `Microsoft.Web.WebView2` (1.0.4078.44) | Embedded Chromium browser (Speech-to-Text bridge) |
| `Microsoft.Windows.CsWin32` (0.3.287) | Source-generated P/Invoke wrappers (referenced by the `MarvinsAIRARefactored.Win32` companion project) |
| `SharpZipLib` (1.4.2) | BZip2 decompression (TradingPaints livery downloads) |
| `System.IO.Ports` (10.0.10) | USB serial communication (AdminBoxx, TyphoonWind, SBT) |
| `System.Management` (10.0.10) | WMI queries (device enumeration) |
| `System.Net.Http` (4.3.4) | Legacy HTTP API compatibility support |
| `System.Text.RegularExpressions` (4.3.1) | Regex API compatibility support |

### Local DLL References
| DLL | Purpose |
|---|---|
| `SimagicHPR.dll` | Simagic HPR pedal haptics API |
| `vJoyInterfaceWrap.dll` | vJoy virtual joystick output |
| `vJoyInterface.dll` | Native vJoy runtime dependency |
| `fmod.dll` / `fmodL.dll` | FMOD native audio runtime libraries |

---

## Project Structure

> **Important for resolving file paths:**
> The solution root and the main application project share the same name, creating a **nested directory**:
> `[repo root]/MarvinsAIRARefactored/MarvinsAIRARefactored.csproj`
> All application source files (Components, DataContext, Windows, Pages, etc.) live under
> `[repo root]/MarvinsAIRARefactored/` — **not** directly under `[repo root]/`.

### Other solution projects
- **`MarvinsAIRARefactored.Win32/`** — Win32 interop helper library.
- **`MarvinsAIRARefactored.PredictionLab/`** — offline test bench for the Prediction FFB module. Replays a MAIRA 360 Hz recording through candidate prediction algorithms and scores the achieved waveform shift / error / noise amplification (its header comment documents the metrics and findings, including the dead ends). Score any new prediction idea here before touching the app module, and keep its `ShippedPredictionModule` port in sync with `FFB/Modules/DspModules.cs`. Plain console app (no COM references), so `dotnet run -c Release` works from its folder.

---

## Architecture & Key Patterns

### Steering / FFB sign conventions (get these right!)
- **iRacing steering telemetry is counterclockwise-positive:** `SteeringWheelAngle` (and `SteeringWheelVelocity`) are **positive when the wheel is turned LEFT**, negative when turned right. The derived `FFBTickContext.WheelPosition` / `WheelVelocity` (angle/velocity normalized to the car's half-lock) inherit this same convention.
- **FFB graph output torque is also counterclockwise-positive:** a positive module output pushes the wheel LEFT.
- **Consequence:** any resisting/restoring force built from these values (friction, damping, centering, soft lock) must **negate** the telemetry-derived term — e.g. the wheel-centering source returns `-position`, and the friction/velocity sources return `-velocity`. Getting this wrong flips a stable centering spring into a runaway force toward the locks (this bug actually shipped once).
- **The old DirectInput axis convention was the opposite** (positive = turned right), so any formula ported from pre-graph fixed-function code that used `DirectInput.ForceFeedbackWheelPosition/Velocity` needs its sign flipped when fed the sim-telemetry-derived values.

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
| Pedals & haptics | `Pedals.cs`, `GTensioner.cs`, `GTensionerGraph.cs` |
| iRacing data | `Telemetry.cs`, `Simulator.cs` |
| Game bridge | `GameBridge.cs` (adapters for other sims live in `GameBridges/` — LMU, rFactor 2, AC / ACC / AC EVO / AC Rally, RaceRoom) |
| Audio | `AudioManager.cs`, `Sounds.cs`, `LFE.cs` |
| Speech / commentary | `SpeechToText.cs`, `TextToSpeech.cs`, `Commentary.cs`, `ChatQueue.cs` |
| Hardware integrations | `TyphoonWind.cs`, `AdminBoxx.cs`, `StreamDeck.cs`, `VirtualJoystick.cs`, `HidHotPlugMonitor.cs`, `LogitechWheel.cs` |
| Cloud / external | `CloudService.cs`, `TradingPaints.cs` |
| App / utility | `AppManager.cs`, `Logger.cs`, `Debug.cs`, `Graph.cs`, `RecordingManager.cs`, `TimingMarkers.cs`, `SettingsFile.cs`, `PlayoutTimer.cs`, `TopLevelWindow.cs` |

### `DataContext/` — settings, binding, localization
| File | Purpose |
|---|---|
| `Settings.cs` | Global persisted settings (property order must match UI order) |
| `ContextSettings.cs` | Per-context (car/track) settings |
| `Context.cs`, `ContextSwitches.cs` | Context selection and switching logic |
| `DataContext.cs` | Root WPF binding object |
| `OverlayLayoutSettings.cs` | Per-context overlay window layout settings |
| `Localization.cs` | UI string table — source for `Localization["Key"]` lookups |

### `Pages/` — feature UI (each page pairs with its component)
| Page | Backing component(s) |
|---|---|
| `RacingWheelPage` | `RacingWheel.cs`, `DirectInput.cs` |
| `SteeringEffectsPage` | `SteeringEffects.cs`, `RacingWheel.cs` |
| `PedalsPage` | `Pedals.cs` |
| `GTensionerPage` | `GTensioner.cs` |
| `GameBridgePage` | `GameBridge.cs`, `GameBridges/` |
| `ControllerProfilesPage` | `ControllerProfile.cs` (Classes) |
| `TyphoonWindPage` | `TyphoonWind.cs` |
| `SoundsPage` | `Sounds.cs`, `AudioManager.cs` |
| `SpeechToTextPage` | `SpeechToText.cs` |
| `CommentaryPage` | `Commentary.cs`, `TextToSpeech.cs`, `ElevenLabs.cs` |
| `TradingPaintsPage` | `TradingPaints.cs` |
| `AppManagerPage` | `AppManager.cs` |
| `GraphPage` | `Graph.cs`, `GraphBase.cs` |
| `SimulatorPage` | `Simulator.cs`, `Telemetry.cs` |
| `OverlaysPage` | overlay windows in `Windows/` |
| `AdminBoxxPage` | `AdminBoxx.cs` |
| `AppSettingsPage` | `Settings.cs`, `SettingsFile.cs` |
| `DebugPage` | `Debug.cs` |
| `ContributePage`, `DonatePage`, `HelpPage` | `CloudService.cs`, `HelpService.cs` |

### `Windows/` — top-level windows & overlays
`MainWindow` is the shell; `StartupWindow` shows during launch. Overlays: `GapMonitorWindow`, `DeltaMonitorWindow`, `GripOMeterWindow`, `CursorCountdownOverlay`, plus `OverlaySettingsWindow` for configuring them. Dialogs/wizards: `WizardWindow`, `ErrorWindow`, `HelpWindow`, `PickProcessWindow`, `SpeechToTextWindow`, `NewVersionAvailableWindow`, `RunInstallerWindow`, `UpdateButtonMappingsWindow`, `UpdateContextSwitchesWindow`, `NewControllerProfileWindow`, `RenameControllerProfileWindow`, `DeleteControllerProfileWindow`.

### `Controls/` — custom `Maira*` controls (use these, never raw WPF equivalents)
`MairaButton`, `MairaComboBox`, `MairaTextBox`, `MairaSwitch`, `MairaMappableSwitch`, `MairaKnob`, `MairaDualSlider`, `MairaTriangleBalance`, `MairaProgressBar`, `MairaExpander`, `MairaGroupBox`, `MairaTabItem`, `MairaStatusBar`, `MairaButtonMapping`, `MairaMappableButton`, `MairaAppMenuButton`, `MairaAppMenuPopup`.

### `Classes/` — helpers & data types
Math/signal: `MathZ.cs`, `RlsWheelVelocityPredictor.cs`. Audio: `CachedSound.cs`, `CachedSoundPlayer.cs`. Recording: `Recording.cs`, `RecordingData.cs`. Serialization: `Serializer.cs`, `SerializableDictionary.cs`. Commentary/voice: `CommentaryTemplates.cs`, `UserCommentaryPhrases.cs`, `VoiceSlotSettings.cs`, `ElevenLabs.cs`, `ElevenLabsKeyStore.cs`. Graph: `GraphBase.cs`. Input/mapping: `ButtonMappings.cs`, `MappableActionCatalog.cs`, `ControllerProfile.cs`. Windows/overlays: `WindowScaler.cs`, `OverlayWindowMover.cs`, `OverlayWindowScaler.cs`. App manager: `AppManagerEntry.cs`, `AppManagerStartEntryViewModel.cs`. Logitech wheel (rev lights / TrueForce): `HidDeviceHelper.cs`, `LogitechRevLightChannel.cs`, `LogitechTrueforceChannel.cs`, `LogitechTrueforceInitData.cs`. Hardware/util: `UsbSerialPortHelper.cs`, `CpuAffinityHelper.cs`, `HelpService.cs`, `TextBoxBehaviors.cs`, `TradingPaintsXML.cs`, `Color.cs`, `Misc.cs`.

### `Themes/`
`DarkTheme.xaml`, `LightTheme.xaml`, `Generic.xaml`, plus per-control theme resources.
