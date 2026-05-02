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

---

## UI Controls — Always Use Custom Maira Variants

**Never use plain WPF controls when a Maira equivalent exists.** Always prefer the custom controls in `Controls/` — they enforce the app's visual style, support localization labels, and integrate with the data context.

| Instead of… | Use… |
|---|---|
| `TextBox` | `controls:MairaTextBox` |
| `ComboBox` | `controls:MairaComboBox` |
| `Button` | `controls:MairaButton` |
| `CheckBox` / toggle | `controls:MairaSwitch` |
| `Slider` | `controls:MairaDualSlider` or `controls:MairaKnob` |
| `GroupBox` | `controls:MairaGroupBox` |
| `TabItem` | `controls:MairaTabItem` |

---

## MairaTextBox

`MairaTextBox` is a labeled text input. It replaces every plain `TextBox` in pages and windows.

**Dependency Properties:**
| Property | Type | Notes |
|---|---|---|
| `Label` | `string` | Displayed above the input field; bind to `Localization[Key]` |
| `Value` | `string` | Two-way bound to the data source |
| `IsNumericOnly` | `bool` | Restricts input to numeric characters |

**Binding pattern:**
```xml
<controls:MairaTextBox Label="{Binding DataContext.Localization[MyLabel], RelativeSource={RelativeSource AncestorType=UserControl}}"
                        Value="{Binding MyProperty, Mode=TwoWay, UpdateSourceTrigger=LostFocus}" />
```

**Key behaviors:**
- Pressing **Enter** commits the binding (`UpdateSource()`) and moves focus to the next field automatically — no extra code needed.
- Use `UpdateSourceTrigger=LostFocus` (not `PropertyChanged`) for numeric fields to allow in-progress editing without triggering side effects.
- Inside a `DataTemplate`, pass the data item via `Tag="{Binding}"` and use the `LostFocus` routed event to sync settings after edits.

---

## MairaComboBox

`MairaComboBox` is a labeled combo box. It replaces every plain `ComboBox`.

**Dependency Properties:**
| Property | Type | Notes |
|---|---|---|
| `Label` | `string` | Displayed above the control |
| `SelectedValue` | `object` | Two-way bound to the enum/value property |
| `ItemsSource` | `IEnumerable` | List of `KeyValuePair<TEnum, string>` items |
| `SelectionChanged` | event | Routed event fired on user selection |

**General XAML pattern (outside DataTemplates):**
```xml
<controls:MairaComboBox Label="{Binding Localization[MyLabel]}"
                         SelectedValue="{Binding Settings.MyEnumProperty, Mode=TwoWay}"
                         ItemsSource="{Binding MyOptionsProperty}" />
```

### MairaComboBox Inside DataTemplates — Initialization Pattern

When a `MairaComboBox` is inside a `DataTemplate` bound to an `ItemsControl` or `ListBox`, you **cannot** set `ItemsSource` directly in XAML (the items are generated and the control is not in the visual tree during normal data binding). Use this pattern instead:

**XAML:**
```xml
<controls:MairaComboBox Label="{Binding DataContext.Localization[CpuPriority], RelativeSource={RelativeSource AncestorType=UserControl}}"
                         SelectedValue="{Binding CpuPriority, Mode=TwoWay}"
                         Loaded="CpuPriorityComboBox_Loaded"
                         Tag="{Binding}"
                         SelectionChanged="Entry_SelectionChanged" />
```
- `Tag="{Binding}"` — passes the data item (the list entry object) to the `Loaded` handler.
- **Do not** set `ItemsSource` in XAML.

**Code-behind `Loaded` handler:**
```csharp
private void CpuPriorityComboBox_Loaded( object sender, RoutedEventArgs e )
{
    if ( sender is MairaComboBox combo && combo.ItemsSource == null )
    {
        combo.ItemsSource = _cpuPriorityOptions;

        if ( combo.Tag is MyEntryClass entry )
        {
            combo.SelectedValue = entry.CpuPriority;
        }
    }
}
```
- **Check `combo.ItemsSource == null`** — this prevents re-initialization when the control is recycled by the virtualizing panel.
- Restore `SelectedValue` from `Tag` after setting `ItemsSource`, because setting `ItemsSource` clears the current selection.

### Localized ComboBox Items

Build the options list in a `UpdateComboBoxOptions()` method (called from `App` when the language changes) using the `Localization` indexer:

```csharp
private List<KeyValuePair<ProcessPriorityClass, string>> _cpuPriorityOptions = [];

public void UpdateComboBoxOptions()
{
    var localization = DataContext.DataContext.Instance.Localization;

    var dict = new Dictionary<ProcessPriorityClass, string>
    {
        { ProcessPriorityClass.Normal,      localization[ "CpuPriorityNormal" ] },
        { ProcessPriorityClass.AboveNormal, localization[ "CpuPriorityAboveNormal" ] },
        { ProcessPriorityClass.High,        localization[ "CpuPriorityHigh" ] },
    };

    _cpuPriorityOptions = dict.ToList();

    RefreshLists(); // re-bind ItemsControls so Loaded fires again with new strings
}
```

- The options list is `List<KeyValuePair<TEnum, string>>` — the enum value is the key, the localized string is the value.
- Call `RefreshLists()` (or equivalent) after rebuilding options so the `Loaded` event fires again and picks up the new strings.
- `UpdateComboBoxOptions()` must be called by `App` whenever the language is changed.

---

## MairaButton

`MairaButton` is a circular icon button with two size variants.

**Dependency Properties:**
| Property | Type | Default | Notes |
|---|---|---|---|
| `Label` | `string` | `""` | Optional text label |
| `LabelOnRight` | `bool` | `false` | Places label to the right of the button ring |
| `Icon` | `ImageSource` | — | The icon displayed inside the ring |
| `BlinkIcon` | `ImageSource` | — | Alternate icon used for blinking state |
| `DefaultFrame` | `ImageSource` | `ring-large-default.png` | Overridable ring frame |
| `MappedFrame` | `ImageSource` | `ring-large-mapped.png` | Ring when a button mapping is active |
| `PressedFrame` | `ImageSource` | `ring-large-pressed.png` | Ring when pressed |
| `IconWidth` | `double` | `48.0` | WPF units |
| `IconHeight` | `double` | `48.0` | WPF units |
| `IsPressed` | `bool` | `false` | Programmatic pressed state |
| `IsSmall` | `bool` | `false` | Use small ring assets instead of large |
| `Disabled` | `bool` | `false` | Disables the button |
| `Click` | event | — | `RoutedEventHandler`; sender is the `MairaButton` itself |

**Sizing rules:**
- **Omit `IsSmall`** (or set `IsSmall="False"`) for standalone action buttons (e.g., "Add Application").
- **Set `IsSmall="True"`** for inline buttons that sit alongside text inputs in a row (e.g., Browse, Pick Process, Remove).

**Icon resource path format:**
```xml
Icon="/MarvinsAIRARefactored;component/Artwork/Buttons/my-icon.png"
```

**DataTemplate pattern — passing the data item through Click:**
```xml
<controls:MairaButton Icon="/MarvinsAIRARefactored;component/Artwork/Buttons/browse.png"
                       IsSmall="True"
                       Tag="{Binding}"
                       Click="BrowseButton_Click"
                       VerticalAlignment="Bottom" />
```
```csharp
private void BrowseButton_Click( object sender, RoutedEventArgs e )
{
    if ( sender is MairaButton button && button.Tag is MyEntryClass entry )
    {
        // use entry ...
    }
}
```
- The `Click` event sender is the `MairaButton` itself, not the inner button element.
- `Tag="{Binding}"` is the standard way to pass the current data item to the handler.

**Standalone "Add" button pattern:**
```xml
<controls:MairaButton Icon="/MarvinsAIRARefactored;component/Artwork/Buttons/plus-large.png"
                       Click="AddItem_Click"
                       HorizontalAlignment="Left"
                       Margin="0,16,0,0" />
```

---

## Artwork / Icon PNGs

### Creation Conventions

All button icon PNGs follow a strict layout so they align correctly inside the ring frames:

| Property | Value |
|---|---|
| Canvas size | 96 × 96 px |
| Background | Transparent |
| Stroke color | White (`Color.White`) |
| Pen width | 4.0 – 4.5 px |
| Content footprint | ~40 × 40 px, centered at (48, 48) — roughly x: 28–68, y: 28–68 |
| Smoothing | `SmoothingMode.AntiAlias` |
| Line caps/joins | `LineCap.Round`, `LineJoin.Round` |
| Save format | PNG 32-bit with alpha |

Icons are generated via **GDI+ PowerShell scripts** using `System.Drawing`. See existing scripts in the session history for reference patterns (folder icon, crosshair/target icon, etc.).

### Registering a New Icon in the Project

After creating a PNG, add it to `MarvinsAIRARefactored.csproj` in **alphabetical order** within the existing `<ItemGroup>` of button artwork resources:

```xml
<Resource Include="Artwork\Buttons\my-new-icon.png" />
```

Failing to register the resource causes a runtime `IOException` when the XAML tries to load the pack URI.

---

## List Entries with Colored Left Bar

The standard layout for editable list entries (e.g., `AppLauncherPage`) uses a 3-column `Grid` with a narrow colored `Border` in the first column:

```xml
<Grid Margin="0,0,0,20">
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="4" />
    <ColumnDefinition Width="12" />
    <ColumnDefinition Width="*" />
  </Grid.ColumnDefinitions>

  <!-- Colored left bar -->
  <Border Grid.Column="0"
          Background="#e04040"
          CornerRadius="2"
          Opacity="0.75" />

  <!-- Entry content -->
  <Grid Grid.Column="2">
    <!-- controls go here -->
  </Grid>
</Grid>
```

**Color conventions:**
| List type | Color | Hex |
|---|---|---|
| Terminate / stop / danger | Red | `#e04040` |
| Start / launch / positive | Green | `#44b060` |

Always use `CornerRadius="2"` and `Opacity="0.75"` on the bar `Border`.

---

## Dialog Windows — Template

All modal dialogs follow this window template:

```xml
<Window x:Class="MarvinsAIRARefactored.Windows.MyDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="My Dialog"
        Width="600"
        Height="480"
        ResizeMode="NoResize"
        WindowStyle="SingleBorderWindow"
        WindowStartupLocation="CenterOwner"
        Icon="/Artwork/AppIcon/maira-universal.ico">
```

Opening from a `UserControl`:
```csharp
var dialog = new MyDialog { Owner = Window.GetWindow( this ) };
dialog.ShowDialog();
```

---

## Async Window Loading Pattern

When a dialog needs to enumerate data on open (e.g., running processes, device lists), use this pattern to keep the window responsive:

1. Show a "Searching…" status overlay in the same grid row as the results list.
2. In the constructor, hook `Loaded` with an `async` lambda.
3. Use `Task.Run` to enumerate on a background thread.
4. After `await`, collapse the overlay and populate the list.

```csharp
public MyDialog()
{
    InitializeComponent();

    Loaded += async ( _, _ ) =>
    {
        SearchBox.Focus();
        await LoadDataAsync();
    };
}

private async Task LoadDataAsync()
{
    var items = await Task.Run( () =>
    {
        // enumerate here — runs on thread pool
        return GetItems();
    } );

    _allItems = items;

    SearchingText.Visibility = Visibility.Collapsed; // hide overlay

    ApplyFilter(); // populate results
}
```

**XAML overlay (same Grid row as results):**
```xml
<TextBlock x:Name="SearchingText"
           Text="Searching..."
           HorizontalAlignment="Center"
           VerticalAlignment="Center"
           IsHitTestVisible="False" />
```

---

## Search/Filter Text Box with Watermark Placeholder

The standard pattern for a filter box with a placeholder hint:

```xml
<Grid>
  <TextBox x:Name="SearchBox"
           TextChanged="SearchBox_TextChanged" />
  <TextBlock IsHitTestVisible="False"
             FontStyle="Italic"
             Foreground="#80ffffff">
    <TextBlock.Style>
      <Style TargetType="TextBlock">
        <Setter Property="Visibility" Value="Collapsed" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding Text, ElementName=SearchBox}" Value="">
            <Setter Property="Visibility" Value="Visible" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </TextBlock.Style>
    Filter by name...
  </TextBlock>
</Grid>
```

---

## KeyEventArgs Disambiguation

The project references both `System.Windows.Forms` and `System.Windows.Input`. Any code-behind file that handles keyboard events will get a **CS0104 ambiguous reference** error unless you add a `using` alias at the top of the file:

```csharp
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
```

Add this alias to every `.xaml.cs` file that handles `KeyDown`, `KeyUp`, or `PreviewKey*` events.

---

## XAML Files — Write Without BOM

When writing or overwriting `.xaml` files via PowerShell, **never use `Set-Content -Encoding UTF8`** — it writes a UTF-8 BOM which breaks the XAML code-generator and causes `CS0103` errors for all `x:Name` fields.

Always use:
```powershell
[System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
```

This writes UTF-8 without BOM, which is required for XAML files.

---

## Excluding the Current App from Process Lists

When enumerating running processes and the list should not include the current application, use `Environment.ProcessPath` (.NET 6+):

```csharp
.Where( p => !string.Equals( p.Path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase ) )
```

This returns the full path of the currently executing process and handles the comparison case-insensitively.

---

## Localized Strings in the UI

**Always use localization keys** for any text that appears in the UI, including unit strings in value formatters in `Settings.cs`. Do **not** hardcode unit strings or labels directly in C# code.

**Correct:**
```csharp
SomeValueString = $"{value} {DataContext.Instance.Localization[ "DegreesPerSecond" ]}";
```

**Wrong:**
```csharp
SomeValueString = $"{value} °/s";
```

Check the existing `.resx` localization files for available keys before introducing new ones. Common unit keys include `"Degrees"`, `"DegreesPerSecond"`, `"Percent"`, `"Hz"`, `"GForceUnits"`, `"MPSUnits"`, `"OFF"`, etc.

---

## Settings.cs — Property Ordering

Settings in `DataContext/Settings.cs` must appear in the **same order as the controls appear in the UI page**. Read columns left-to-right, top-to-bottom.

**Example:** if the SoP section has Column 0 = Mode, Column 4 = Amplitude then Curve, the properties in Settings.cs must appear as: Mode, Amplitude, Curve.

---

## ContextSettings.cs — What Belongs Here and Ordering

`DataContext/ContextSettings.cs` is a flat list of per-context saveable settings. The rules are:

1. **Only settings that have a `ContextSwitches` companion property in `Settings.cs` should appear in `ContextSettings.cs`.** Having a `ContextSwitches` property means the setting can be saved/restored as part of a context preset.
2. The order of properties in `ContextSettings.cs` must match the order they appear in `Settings.cs` (and therefore the UI order).
3. When you add a new setting with a `ContextSwitches` companion property in `Settings.cs`, you **must** also add a matching property to `ContextSettings.cs` in the correct position.

See the **Per-Context Settings System** section above for the full checklist and reflection-based mechanism.

---

## Unicode Safety — File Read/Write and String Replacement

Several XAML and resource files in this project contain **non-ASCII Unicode characters** (e.g., `Català`, `Français`, `Čestina`, `Русский`, `简体中文`, Thai, Armenian, etc.). Careless file operations will silently corrupt these characters.

### Rules

1. **Never use PowerShell `Get-Content` / `Set-Content` for XAML/resx files without specifying encoding.**
   PowerShell defaults to the system code page (typically Windows-1252 on Western systems), which cannot represent characters outside its range and will corrupt them silently.

2. **Always use `[System.IO.File]::ReadAllText` / `[System.IO.File]::WriteAllText` with explicit UTF-8 encoding** for any PowerShell read-modify-write operation:
   ```powershell
   $content = [System.IO.File]::ReadAllText($path)           # defaults to UTF-8 with BOM detection
   $content = $content.Replace("old", "new")
   [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
   ```
   The `[System.Text.UTF8Encoding]::new($false)` argument writes UTF-8 **without BOM**, which is required for XAML files (see the XAML Files — Write Without BOM section above).

3. **Never use `replace_string_in_file` or PowerShell `-replace` for bulk multi-occurrence substitutions across Unicode-containing files.**
   Bulk replacements (e.g., replacing all occurrences of a hardcoded color across a file) must be done with `[System.IO.File]::ReadAllText` → `.Replace()` → `[System.IO.File]::WriteAllText` so encoding is preserved end-to-end.

4. **Always verify after any bulk replacement** that non-ASCII characters in the file are intact:
   ```powershell
   $content = [System.IO.File]::ReadAllText($path)
   $lines = $content -split "`n"
   # spot-check lines known to contain non-ASCII
   $lines | Where-Object { $_ -match '[^\x00-\x7F]' } | Select-Object -First 10
   ```

5. **If corruption is detected** (U+FFFD `?` replacement character, or bytes like `0xEF 0xBF 0xBD`), restore the file from Git and redo the replacement using the safe `[System.IO.File]` approach above.

---

## Localization

Resource strings are stored in `.resx` files under `Translate\resx\`.
The `Localization` component loads these and exposes them as an indexer: `DataContext.Instance.Localization["KeyName"]`.
The post-build event copies `.resx` files to the user's documents folder so translations can be updated independently.

All user-facing strings displayed in the UI **must** have proper localization support:

- Add every new string as a named entry in `Translate\resx\Resources.resx` (the English base resource file).
- Reference the string in XAML via `{Binding Localization[KeyName]}` — never use hard-coded string literals in XAML.
- Reference the string in C# via `DataContext.DataContext.Instance.Localization["KeyName"]`.
- **Always** add translations for every new string to all other language `.resx` files.
