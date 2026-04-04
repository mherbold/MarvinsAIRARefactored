# AGENT.md — MarvinsAIRA Refactored

## Project Overview

**MarvinsAIRA Refactored** (v2.0) is a Windows desktop application written in **C# 13 / .NET 9** using **WPF** (Windows Presentation Foundation). It is a sim-racing companion tool for **iRacing** that provides advanced force-feedback processing, steering effects, pedal haptics, hardware integrations, and various overlays.

The project has a second build target called **AdminBoxx**, controlled via the `ADMINBOXX` preprocessor constant. When `ADMINBOXX` is defined, many features are disabled and the app runs as a simpler hardware-controller utility.

- **GitHub repo:** https://github.com/mherbold/MarvinsAIRARefactored
- **Community:** https://discord.gg/Y7JN3BAz72
- **Progress board:** https://trello.com/b/o7vbR74U/maira-refactored-20

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
The post-build step copies several asset folders (languages, sounds, recordings, calibration files, STT files, SBT files) to the user's `My Documents\MarvinsAIRA Refactored\` folder using `xcopy`.

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
│   ├── MultimediaTimer.cs          # High-resolution multimedia timer (≈17ms tick)
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
├── Translate/resx/                 # Localization resource files (.resx)
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

### iRacing Integration (`Simulator`)
- Uses **IRSDKSharper** library.
- Fires events: `OnConnected`, `OnDisconnected`, `OnSessionInfo` (session YAML), `OnTelemetryData` (60 Hz).
- `OnTelemetryData` runs at **60 Hz** and provides sub-frame 360 Hz arrays (`_ST` suffix = sub-tick arrays with 6 samples per frame).
- Telemetry datum handles (`IRacingSdkDatum`) are cached on first call and reused every frame for performance.
- The `SamplesPerFrame360Hz = 6` constant represents the ratio between the 360 Hz sub-tick data and the 60 Hz frame rate.

### Context System (Per-Car/Track/Wheelbase Settings)
Settings can be overridden per context. A `Context` is a key composed of:
- Wheelbase GUID
- Car name
- Track name
- Track configuration name
- Wet/Dry status

`ContextSwitches` flags determine which axes are active. `ContextSettings` holds the per-context overrides. `Settings.UpdateSettings()` is called whenever the context might have changed (connection, weather change, etc.).

For full details on the design and how to add new per-context settings, see the **[Per-Context Settings System](#per-context-settings-system)** section below.

### Settings Persistence
Settings are serialized to XML at `My Documents\MarvinsAIRA Refactored\Settings.xml` via `SettingsFile`. Serialization is queued (not immediate) to avoid excessive I/O. The `Serializer` class handles XML serialization using `XmlSerializer`.

### IPC / Telemetry Export
`Telemetry.cs` creates a **memory-mapped file** named `Local\MAIRARefactoredTelemetry` using a fixed-layout `unsafe struct` (`DataBufferStruct`). External tools (e.g., SimHub) can read this to display MAIRA output values.

### Force Feedback Algorithms (`RacingWheel`)
Several FFB processing algorithms are implemented:
- `Native60Hz` — raw iRacing 60 Hz torque
- `Native360Hz` — raw iRacing 360 Hz sub-tick torque
- `DetailBooster` — enhances small detail forces
- `DeltaLimiter` — limits rate-of-change
- `SlewAndTotalCompression` — dual compression stages
- `MultiAdjustmentToolkit` — fully configurable multi-source blending

The `RlsWheelVelocityPredictor` class implements an **RLS (Recursive Least Squares)** adaptive filter to predict future wheel velocity for latency compensation.

### Steering Effects (`SteeringEffects`)
Computes understeer, oversteer, and "seat of pants" effects from iRacing telemetry. Requires a per-car calibration file (CSV) stored in `My Documents\MarvinsAIRA Refactored\Calibration\`. A built-in calibration routine drives the car at low speed through steering angles to build the calibration curve.

### Hardware Integrations
All custom hardware communicates via **USB serial port** abstracted by `UsbSerialPortHelper`:
- **AdminBoxx** — 8×4 RGB LED button box (Adafruit ItsyBitsy M4, VID `239A`, PID `80F2`)
- **Wind** — dual-fan wind simulator (identified by product name `"MAIRA WIND"`)
- **SeatBeltTensioner (SBT)** — seat belt tensioner (identified by `"MAIRA SBT"`)

**Simagic HPR** pedal haptics use a dedicated DLL (`SimagicHPR.dll`).

**Elgato Stream Deck** is supported via `OpenMacroBoard.SDK` + `StreamDeckSharp`, integrated into the DirectInput button mapping system with a fake device GUID.

**vJoy** virtual joystick output uses `vJoyInterfaceWrap.dll`.

### Localization
Resource strings are stored in `.resx` files under `Translate\resx\`. The `Localization` component loads these and exposes them as an indexer: `DataContext.Instance.Localization["KeyName"]`. The post-build event copies `.resx` files to the user's documents folder so translations can be updated independently.

---

## Coding Conventions

- **Use `var`** whenever possible in C# code.
- Use **descriptive variable names** (e.g., `var leftTargetPositionTenths` not `var i` for variables).
- All components use a consistent **logging pattern**: `app.Logger.WriteLine( "[ComponentName] MethodName >>>" )` / `"<<< MethodName"`.
- Components follow an **Initialize / Shutdown** lifecycle called by `App`.
- `UpdateInterval` constants (typically multiples of 6 frames at 60 Hz) are used throughout to throttle non-critical UI updates.
- P/Invoke wrappers live in the `PInvoke/` folder; prefer `CsWin32` generated wrappers where available.
- Regex patterns use `[GeneratedRegex]` source generators.
- `MethodImpl(AggressiveInlining)` is applied to hot-path math helpers in `MathZ` and other performance-critical components.

---

## Documents Folder Layout (Runtime)

At runtime the app reads/writes to `My Documents\MarvinsAIRA Refactored\`:

```
Settings.xml              # Serialized user settings
Languages/                # Localization .resx files
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

---

## Per-Context Settings System

Per-context settings allow individual settings to be overridden based on the active racing context (wheelbase, car, track, track configuration, wet/dry). This section documents the full design and the exact steps required to add a new per-context setting.

### Key Types

| Type | File | Purpose |
|---|---|---|
| `Context` | `DataContext\Context.cs` | Immutable key: wheelbase GUID + car + track + track config + wet/dry |
| `ContextSwitches` | `DataContext\ContextSwitches.cs` | 5 booleans controlling which axes create a unique context for a given setting |
| `ContextSettings` | `DataContext\ContextSettings.cs` | Flat class of auto-properties — one per context-capable setting — holding the current context's override value |
| `Settings` | `DataContext\Settings.cs` | All persistent settings; also hosts the companion `ContextSwitches` property and the `ContextSettingsDictionary` |

### How It Works

1. **`ContextSwitches`** has 5 boolean flags in constructor order:
   ```csharp
   new ContextSwitches( wheelbaseGuid, carName, trackName, trackConfigurationName, wetDry )
   ```
   Each flag controls whether that axis is included when building the `Context` key for that setting. The common default for "per-car only" is:
   ```csharp
   new( false, true, false, false, false )
   ```

2. **Companion property convention in `Settings.cs`**: Every context-capable property `Foo` must have a companion property named exactly `FooContextSwitches` of type `ContextSwitches`:
   ```csharp
   public float Foo { get; set; } = 1.0f;
   public ContextSwitches FooContextSwitches { get; set; } = new( false, true, false, false, false );
   ```

3. **`ContextSettings.cs`** contains a matching auto-property with the **same name and same default value** as the setting in `Settings.cs`:
   ```csharp
   public float Foo { get; set; } = 1.0f;
   ```
   This class holds the **active context's** current value and is read by components during processing.

4. **`Settings.UpdateSettings()`** uses **reflection** to scan `Settings` for all `*ContextSwitches` properties, builds the appropriate `Context` key for each one, looks up (or creates) the matching entry in `ContextSettingsDictionary`, and copies the stored context value into the corresponding `ContextSettings` property — or writes the current setting value back if no override exists yet. This is called whenever the context may have changed.

5. **UI binding**: In XAML pages, each context-capable control gets a `ContextSwitches` attribute bound to the companion property:
   ```xml
   <controls:MairaKnob Value="{Binding Settings.Foo, Mode=TwoWay}"
                        ContextSwitches="{Binding Settings.FooContextSwitches}" />

   <controls:MairaSwitch IsOn="{Binding Settings.Bar, Mode=TwoWay}"
                          ContextSwitches="{Binding Settings.BarContextSwitches}" />
   ```
   The binding does **not** need `Mode=TwoWay` because `ContextSwitches` is a reference type and the window modifies its properties in place.

6. **Right-click to configure**: `MairaKnob` and `MairaSwitch` detect a right-click on their label and, if `ContextSwitches != null`, open `UpdateContextSwitchesWindow` to let the user configure which axes create a context for that setting.

### Adding a New Per-Context Setting — Checklist

Follow these steps every time a setting needs per-context support:

**1. `DataContext\Settings.cs`** — add the companion property immediately after the setting property:
```csharp
public float MySetting { get; set; } = 5.0f;
public ContextSwitches MySettingContextSwitches { get; set; } = new( false, true, false, false, false );
```

**2. `DataContext\ContextSettings.cs`** — add a matching auto-property with the same name and default:
```csharp
public float MySetting { get; set; } = 5.0f;
```

**3. XAML page** — add `ContextSwitches` binding to the control:
```xml
<controls:MairaKnob Value="{Binding Settings.MySetting, Mode=TwoWay}"
                     ContextSwitches="{Binding Settings.MySettingContextSwitches}" />
```

**Important rules:**
- The companion property name **must** be exactly `[SettingName]ContextSwitches` — `UpdateSettings()` finds it by reflection using this naming convention.
- The `ContextSettings` property name **must** exactly match the setting name in `Settings.cs`.
- Do **not** add `Mode=TwoWay` to the `ContextSwitches` XAML binding.
- Always use `new( false, true, false, false, false )` (per-car-only) as the default unless there is a specific reason to use a different default.
- Group the companion property directly below its paired setting in `Settings.cs` and in the same `#region` block in `ContextSettings.cs`.
