# AGENTS.md — MarvinsAIRA Refactored

## Project Overview

**MarvinsAIRA Refactored** (v2.0) is a Windows desktop application written in **C# 13 / .NET 9** using **WPF** (Windows Presentation Foundation). It is a sim-racing companion tool for **iRacing** that provides advanced force-feedback processing, steering effects, pedal haptics, hardware integrations, and various overlays.

The project has a second build target called **AdminBoxx**, controlled via the `ADMINBOXX` preprocessor constant. When `ADMINBOXX` is defined, many features are disabled and the app runs as a simpler hardware-controller utility.

- **GitHub repo:** https://github.com/mherbold/MarvinsAIRARefactored
- **Community:** https://discord.gg/Y7JN3BAz72
- **Progress board:** https://trello.com/b/o7vbR74U/maira-refactored-20

---

## Sub-File Index

For topic-specific details, load the relevant sub-file. Each sub-file lists the exact source files it covers.

| Sub-file | Topics covered |
|---|---|
| [`docs/agents/force-feedback.md`](docs/agents/force-feedback.md) | FFB algorithms (`RacingWheel`), steering effects, DirectInput, LFE bass shaker, multimedia timer, RLS velocity predictor, `MathZ` hot-path helpers |
| [`docs/agents/hardware-io.md`](docs/agents/hardware-io.md) | AdminBoxx, Wind simulator, SeatBeltTensioner, vJoy, Stream Deck, hot-plug detection, button mappings, Logitech LEDs |
| [`docs/agents/simulator-iracing.md`](docs/agents/simulator-iracing.md) | iRacing SDK bridge (`Simulator`), 60/360 Hz telemetry, memory-mapped IPC (`Telemetry`), driver tracking, timing markers, debug viewers |
| [`docs/agents/audio-sounds.md`](docs/agents/audio-sounds.md) | Audio device management, sound effect playback, CachedSound/Player, XAudio2, LFE DirectSound capture |
| [`docs/agents/speech-to-text.md`](docs/agents/speech-to-text.md) | Chrome/Edge Web Speech API bridge, STT asset files, ChatQueue, transcript overlay window |
| [`docs/agents/text-to-speech.md`](docs/agents/text-to-speech.md) | ElevenLabs TTS pipeline, voice slots, Commentary event detection, phrase templates, API key storage |
| [`docs/agents/settings-context.md`](docs/agents/settings-context.md) | Per-context settings system, `ContextSwitches`, `ContextSettings`, `Settings.cs` ordering rules, adding new per-context settings |
| [`docs/agents/ui-wpf-controls.md`](docs/agents/ui-wpf-controls.md) | All `Maira*` controls, XAML patterns, artwork/icon PNGs, dialog templates, async window loading, XAML BOM warning |
| [`docs/agents/localization.md`](docs/agents/localization.md) | `.resx` resource files, `Localization` indexer, adding strings, adding languages, localized ComboBox items |

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
| `IRSDKSharper` (1.1.6) | iRacing SDK wrapper — provides telemetry and session info |
| `SharpDX.DirectInput` (4.2.0) | DirectInput for racing wheel / joystick input and FFB |
| `SharpDX.DirectSound` (4.2.0) | DirectSound for LFE (Low Frequency Effects) audio capture |
| `SharpDX.XAudio2` (4.2.0) | XAudio2 for sound playback |
| `Accord` / `Accord.Neuro` / `Accord.Statistics` (3.8.0) | Machine-learning / signal-processing utilities |
| `CsvHelper` (33.1.0) | CSV reading/writing (calibration data, recordings) |
| `Newtonsoft.Json` (13.0.4) | JSON serialization (cloud service responses) |
| `OpenMacroBoard.SDK` + `StreamDeckSharp` (6.1.0) | Elgato Stream Deck integration |
| `Microsoft.Web.WebView2` (1.0.3800.47) | Embedded Chromium browser (Speech-to-Text bridge) |
| `Microsoft.Windows.CsWin32` (0.3.269) | Source-generated P/Invoke wrappers |
| `PInvoke.User32` / `PInvoke.SetupApi` (0.7.124) | Windows API P/Invoke helpers |
| `SharpZipLib` (1.4.2) | BZip2 decompression (TradingPaints livery downloads) |
| `System.IO.Ports` (10.0.5) | USB serial communication (AdminBoxx, Wind, SBT) |
| `System.Management` (10.0.5) | WMI queries (device enumeration) |
| `YamlDotNet` | YAML parsing (iRacing session info) |

### Local DLL References
| DLL | Purpose |
|---|---|
| `SimagicHPR.dll` | Simagic HPR pedal haptics API |
| `vJoyInterfaceWrap.dll` | vJoy virtual joystick output |
| `LogitechSteeringWheelEnginesWrapper.dll` | Logitech wheel LED/effects support |

---

## Project Structure

```
MarvinsAIRARefactored/
├── App.xaml / App.xaml.cs          # Application entry point & global singleton
├── AssemblyInfo.cs
├── GlobalSuppressions.cs
│
├── Components/                     # Core services (all initialized & owned by App)
│   ├── AdminBoxx.cs                # USB LED button box hardware
│   ├── AudioManager.cs             # Audio device management
│   ├── ChatQueue.cs                # Chat / messaging queue
│   ├── CloudService.cs             # Update checks, analytics (herboldracing.com)
│   ├── Debug.cs                    # Debug message display
│   ├── DirectInput.cs              # DirectInput device polling + FFB output
│   ├── Drivers.cs                  # iRacing driver/car tracking
│   ├── Graph.cs                    # Telemetry graph rendering
│   ├── HidHotPlugMonitor.cs        # USB hot-plug detection
│   ├── LFE.cs                      # Low Frequency Effects (bass shaker) via DirectSound
│   ├── Logger.cs                   # File + in-app logging
│   ├── MultimediaTimer.cs          # High-resolution multimedia timer (~17ms tick)
│   ├── Pedals.cs                   # Simagic HPR pedal haptics
│   ├── RacingWheel.cs              # FFB algorithms and wheel output
│   ├── RecordingManager.cs         # Telemetry recording/playback
│   ├── SeatBeltTensioner.cs        # USB seat belt tensioner hardware
│   ├── SettingsFile.cs             # XML settings persistence
│   ├── Simulator.cs                # iRacing SDK bridge (IRSDKSharper wrapper)
│   ├── Sounds.cs                   # Sound effect playback
│   ├── SpeechToText.cs             # Chrome-based Web Speech API bridge
│   ├── SteeringEffects.cs          # Understeer/oversteer/SeatOfPants effects
│   ├── StreamDeck.cs               # Elgato Stream Deck integration
│   ├── Telemetry.cs                # Memory-mapped file IPC (exports data to SimHub etc.)
│   ├── TimingMarkers.cs            # Lap timing markers
│   ├── TopLevelWindow.cs           # Always-on-top window helper
│   ├── TradingPaints.cs            # TradingPaints.com livery downloader
│   ├── VirtualJoystick.cs          # vJoy virtual joystick output
│   └── Wind.cs                     # USB twin-fan wind simulator hardware
│
├── DataContext/                    # MVVM data layer
│   ├── DataContext.cs              # Global singleton root (INotifyPropertyChanged)
│   ├── Settings.cs                 # All user settings (serialized to XML)
│   ├── ContextSettings.cs          # Per-context override values for settings
│   ├── ContextSwitches.cs          # Flags controlling which axes create contexts
│   └── Context.cs                  # Context key (wheelbase, car, track, wet/dry)
│
├── Windows/                        # WPF Windows
│   ├── MainWindow.xaml/.cs         # Primary application shell
│   ├── GripOMeterWindow.xaml/.cs   # Grip-O-Meter overlay
│   ├── GapMonitorWindow.xaml/.cs   # Gap monitor overlay
│   ├── SpeechToTextWindow.xaml/.cs # Speech-to-text floating window
│   ├── HelpWindow.xaml/.cs
│   ├── ErrorWindow.xaml/.cs
│   ├── NewVersionAvailableWindow.xaml/.cs
│   ├── RunInstallerWindow.xaml/.cs
│   ├── UpdateButtonMappingsWindow.xaml/.cs
│   ├── UpdateContextSwitchesWindow.xaml/.cs
│   └── CursorCountdownOverlay.xaml/.cs
│
├── Pages/                          # WPF UserControl pages (hosted in MainWindow)
│   ├── RacingWheelPage.xaml/.cs
│   ├── SteeringEffectsPage.xaml/.cs
│   ├── PedalsPage.xaml/.cs
│   ├── WindPage.xaml/.cs
│   ├── SeatBeltTensionerPage.xaml/.cs
│   ├── OverlaysPage.xaml/.cs
│   ├── SoundsPage.xaml/.cs
│   ├── SpeechToTextPage.xaml/.cs
│   ├── TradingPaintsPage.xaml/.cs
│   ├── GraphPage.xaml/.cs
│   ├── SimulatorPage.xaml/.cs
│   ├── AdminBoxxPage.xaml/.cs
│   ├── AppSettingsPage.xaml/.cs
│   ├── HelpPage.xaml/.cs
│   ├── ContributePage.xaml/.cs
│   ├── DonatePage.xaml/.cs
│   └── DebugPage.xaml/.cs
│
├── Controls/                       # Custom WPF controls (all prefixed "Maira")
│   ├── MairaButton, MairaSwitch, MairaKnob, MairaComboBox
│   ├── MairaTextBox, MairaDualSlider, MairaStatusBar
│   ├── MairaAppMenuButton, MairaAppMenuPopup
│   ├── MairaButtonMapping, MairaMappableButton
│   └── MairaTabItem, MairaGroupBox
│
├── Classes/                        # Utility / helper classes
│   ├── MathZ.cs                    # Math helpers (Lerp, Smoothstep, unit conversions, etc.)
│   ├── RlsWheelVelocityPredictor.cs # RLS adaptive filter for wheel velocity prediction
│   ├── Serializer.cs               # XML serialization helpers
│   ├── SerializableDictionary.cs   # XML-serializable dictionary
│   ├── Recording.cs / RecordingData.cs # Telemetry recording structures
│   ├── GraphBase.cs                # Base class for graph rendering
│   ├── Color.cs                    # RGBA color struct
│   ├── ButtonMappings.cs           # Input button mapping logic
│   ├── UsbSerialPortHelper.cs      # USB serial port abstraction
│   ├── CpuAffinityHelper.cs        # CPU affinity / priority management
│   ├── CachedSound.cs / CachedSoundPlayer.cs # Pre-loaded audio samples
│   ├── ChromeLauncher.cs / ChromeSTTBridge.cs # Chrome/Edge Speech-to-Text bridge
│   ├── LogitechGSDK.cs             # Logitech G-SDK wheel LEDs
│   ├── TradingPaintsXML.cs         # TradingPaints XML data model
│   ├── HelpService.cs              # Context-sensitive help
│   ├── Misc.cs                     # General utilities (version, mutex, DPI, etc.)
│   └── TextBoxBehaviors.cs         # WPF text box helper behaviors
│
├── Viewers/                        # iRacing telemetry data viewers
│   ├── SessionInfoViewer.cs
│   ├── TelemetryDataViewer.cs
│   └── HeaderDataViewer.cs
│
├── Converters/                     # WPF value converters
│   ├── BooleanToVisibilityCollapsedConverter.cs
│   ├── StartsWithUnderscoreConverter.cs
│   └── HelpIconVisibilityConverter.cs
│
├── PInvoke/                        # Custom P/Invoke declarations
│   ├── User32.cs
│   ├── WinMM.cs
│   ├── DWMAPI.cs
│   └── UXTheme.cs
│
├── Artwork/                        # Embedded image resources (PNG, ICO)
├── Fonts/                          # Embedded fonts (Aptos Narrow)
├── Resources/                      # Localization resource files (.resx) — embedded into the assembly at build time
├── InnoSetup/                      # Installer assets (sounds, calibration, STT, SBT)
├── Arduino/Wind/Wind.ino           # Arduino sketch for the twin/quad-fan wind simulator
├── Notes/SessionInfo.yaml          # Example iRacing simulator session info dump for debugging and development
├── Notes/TelemetryData.yaml        # Example iRacing simulator telemetry data dump for debugging and development
└── AdminBoxx/code.py               # Embedded Python script for AdminBoxx firmware
```

---

## Architecture & Key Patterns

### Global Singleton (`App`)
`App` (in `App.xaml.cs`) is the central singleton accessed via `App.Instance!`. It owns and initializes every component. All components call `App.Instance!` to access sibling services. This is the primary way components communicate.

### Threading Model
| Thread | Purpose |
|---|---|
| **UI Thread** (WPF Dispatcher) | All WPF rendering and UI updates |
| **MAIRA App Worker Thread** | Processes work triggered by iRacing telemetry tick |
| **MAIRA Multimedia Timer Worker Thread** | High-priority ~17ms timer for FFB output |
| **IRSDKSharper threads** | Telemetry (60 Hz) and session info callbacks from iRacing SDK |
| **LFE Worker Thread** | High-priority audio capture for bass shaker |
| **TradingPaints async Task** | Background livery download |
| **SpeechToText** | Async Chrome bridge |

The `App._autoResetEvent` is used to signal the worker thread from the iRacing telemetry callback (`OnTelemetryData`). Use `app.TriggerWorkerThread()` to signal it.

The `MultimediaTimer` uses Windows `timeSetEvent` (via `WinMM.cs`) at 17ms period to drive high-priority FFB output on its own thread.

### Component Lifecycle
All components follow an **Initialize / Shutdown** lifecycle called by `App` at startup and shutdown. Components access siblings via `App.Instance!` — there is no dependency injection container.

### Key Cross-Cutting Summaries
For full details on any of these topics, see the sub-files listed in the **Sub-File Index** above.

- **iRacing SDK** — 60 Hz telemetry + 360 Hz sub-tick arrays, datum handle caching; see [`docs/agents/simulator-iracing.md`](docs/agents/simulator-iracing.md).
- **Per-context settings** — reflection-based context switching per car/track/wheelbase; see [`docs/agents/settings-context.md`](docs/agents/settings-context.md).
- **FFB pipeline** — multimedia timer -> `RacingWheel` -> `DirectInput`; see [`docs/agents/force-feedback.md`](docs/agents/force-feedback.md).
- **Custom hardware** — USB serial via `UsbSerialPortHelper`; see [`docs/agents/hardware-io.md`](docs/agents/hardware-io.md).
- **IPC export** — memory-mapped file `Local\MAIRARefactoredTelemetry`; see [`docs/agents/simulator-iracing.md`](docs/agents/simulator-iracing.md).

---

## Coding Conventions

- **Use `var`** whenever possible in C# code.
- Use **descriptive variable names** (e.g., `var leftTargetPositionTenths` not `var i`).
- All components use a consistent **logging pattern**: `app.Logger.WriteLine( "[ComponentName] MethodName >>>" )` / `"<<< MethodName"`.
- `UpdateInterval` constants (typically multiples of 6 frames at 60 Hz) throttle non-critical UI updates.
- P/Invoke wrappers live in `PInvoke/`; prefer `CsWin32` generated wrappers where available.
- Regex patterns use `[GeneratedRegex]` source generators.
- `MethodImpl(AggressiveInlining)` is applied to hot-path math helpers in `MathZ` and other performance-critical components.

---

## Documents Folder Layout (Runtime)

At runtime the app reads/writes to `My Documents\MarvinsAIRA Refactored\`:

```
Settings.xml              # Serialized user settings
Sounds/                   # Sound effect .wav files
Recordings/               # Telemetry recording .csv files
Calibration/              # Steering calibration .csv files
STT/                      # Speech-to-text HTML/assets
SBT/                      # Seat belt tensioner assets
SessionInfo.yaml          # (DEBUG only) last iRacing session YAML
TelemetryData.yaml        # (DEBUG only) last telemetry property dump
```

---

## Mutual Exclusion

Two named mutexes prevent multiple instances and conflict with the classic MAIRA version:
- `MarvinsAIRARefactoredMutex` — prevents duplicate Refactored instances
- `MarvinsAIRA Mutex` — prevents running alongside classic MAIRA
