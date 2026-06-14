# This is the MAIRA Refactored (2.0) GitHub repo!

**Marvin's Awesome iRacing App (MAIRA) Refactored** is a Windows desktop companion app for the [iRacing](https://www.iracing.com/) simulator. It is written in **C# 13 / .NET 9** using **WPF**, and provides advanced force-feedback processing, steering effects, pedal haptics, seat-belt tensioner support, wind/LFE effects, AI commentary and speech-to-text, hardware integrations (Stream Deck, AdminBoxx, vJoy, etc.), Trading Paints livery downloading, and a variety of overlays.

The project also has a second build target, **AdminBoxx** (selected via the `ADMINBOXX` preprocessor constant), which strips out most sim-racing features and runs as a simpler hardware-controller utility.

Check out my progress on my Trello board here - https://trello.com/b/o7vbR74U/maira-refactored-20

For any questions or suggestions, please reach out to me on my Discord server - https://discord.gg/Y7JN3BAz72

Feel free to contribute to the project by forking the repo and submitting pull requests. Your contributions are greatly appreciated!

### Architecture Overview (ChatGPT Description)

This document captures the architectural description from the initial ChatGPT analysis of the MAIRA Refactored codebase.

---

## 1. High-Level Architecture

At a very high level, the solution looks like this:

### 1.1 Application Shell

- **`App` (`App.xaml.cs`)**
  - WPF `Application` subclass.
  - Acts as a **global singleton** and owns almost all “services” / components.
  - Exposes them via `App.Instance`.

- **`MainWindow` and other WPF windows**
  - `MainWindow` is the primary shell.
  - Additional windows: Help, Error, Grip-O-Meter, STT, update dialogs, etc.

### 1.2 Core Components (“Services”) — `Components/`

Examples (not exhaustive):

- **Force feedback / wheel** — `RacingWheel`, `SteeringEffects`, `DirectInput`, `Drivers`
- **Pedals & haptics** — `Pedals`, `SeatBeltTensioner`, `SeatBeltTensionerGraph`
- **iRacing data** — `Simulator` (iRacing telemetry, **IRSDKSharper**), `Telemetry` (memory-mapped IPC), `TimingMarkers`
- **Audio** — `AudioManager`, `Sounds`, `LFE`
- **Speech / commentary** — `SpeechToText`, `TextToSpeech`, `Commentary`, `ChatQueue`
- **Hardware integrations** — `Wind`, `AdminBoxx`, `StreamDeck`, `VirtualJoystick`, `HidHotPlugMonitor`
- **Cloud / external** — `CloudService`, `TradingPaints`
- **App / utility** — `AppManager`, `Logger`, `Debug`, `Graph`, `RecordingManager`, `MultimediaTimer`, `SettingsFile`, `TopLevelWindow`

### 1.3 Data / MVVM-ish Layer — `DataContext/`

- `DataContext` (global singleton root)
- `Settings` (view-model-ish plus persistence wiring)
- `Context`
- `ContextSettings`
- `ContextSwitches`

### 1.4 UI Layer

- **Windows/**
  - WPF windows such as `MainWindow`, `HelpWindow`, `ErrorWindow`, `GripOMeterWindow`, `SpeechToTextWindow`, etc.

- **Pages/** (WPF `UserControl` pages hosted in `MainWindow`)
  - `RacingWheelPage`
  - `SteeringEffectsPage`
  - `PedalsPage`
  - `SeatBeltTensionerPage`
  - `WindPage`
  - `SoundsPage`
  - `SpeechToTextPage`
  - `CommentaryPage`
  - `TradingPaintsPage`
  - `AppManagerPage`
  - `GraphPage`
  - `SimulatorPage`
  - `OverlaysPage`
  - `AppSettingsPage`
  - `AdminBoxxPage`
  - `DebugPage`
  - `ContributePage`
  - `DonatePage`
  - `HelpPage`

- **Controls/** (reusable `Maira*` visual components — used in place of raw WPF equivalents)
  - `MairaButton`
  - `MairaMappableButton`
  - `MairaComboBox`
  - `MairaTextBox`
  - `MairaKnob`
  - `MairaSwitch`
  - `MairaDualSlider`
  - `MairaProgressBar`
  - `MairaExpander`
  - `MairaGroupBox`
  - `MairaTabItem`
  - `MairaStatusBar`
  - `MairaButtonMapping`
  - `MairaAppMenuButton`
  - `MairaAppMenuPopup`

- **Viewers/**
  - `TelemetryDataViewer`
  - `HeaderDataViewer`
  - `SessionInfoViewer`  
  These are custom drawing controls that render telemetry/session/header data.

### 1.5 Support / Utilities

- **Classes/** — helpers such as:
  - Math / signal: `MathZ`, `RlsWheelVelocityPredictor`
  - Audio: `CachedSound`, `CachedSoundPlayer`
  - Recording: `Recording`, `RecordingData`
  - Serialization: `Serializer`, `SerializableDictionary`
  - Commentary / voice: `CommentaryTemplates`, `UserCommentaryPhrases`, `VoiceSlotSettings`, `ElevenLabs`, `ElevenLabsKeyStore`
  - Graph / UI: `GraphBase`, `ButtonMappings`, `TextBoxBehaviors`, `Color`
  - App manager: `AppManagerEntry`, `AppManagerStartEntryViewModel`
  - Hardware / util: `UsbSerialPortHelper`, `CpuAffinityHelper`, `HelpService`, `TradingPaintsXML`, `Misc`

- **DataContext/Localization.cs** + **Resources/** — UI string table and localized resources.
- **Converters/** — WPF value converters.
- **Viewers/** — custom telemetry/session drawing controls.
- **Themes/** — `DarkTheme`, `LightTheme`, `Generic`, and per-control resources.
- **MarvinsAIRARefactored.Win32/** — companion project for source-generated P/Invoke (CsWin32) bindings.
- **Arduino/**, **AdminBoxx/**, **InnoSetup/**, **Wordpress/** — external tooling, firmware, the installer script, and supporting web/site assets.

> **Mental model:**  
> - `App` is the global **service locator**.  
> - `Components` are the **engine room**.  
> - `DataContext` is the global **view-model & settings**.  
> - `Windows/Pages/Controls` form the WPF **UI layer** on top.

---

## 2. Class Hierarchies (The Interesting Bits)

The project uses **composition** heavily with fairly shallow inheritance. The main hierarchies:

### 2.1 WPF Base Types

- `App : Application`

**Windows** (all inherit from `Window`):

- `MainWindow` — the primary shell
- `StartupWindow`
- `WizardWindow`
- `ErrorWindow`
- `HelpWindow`
- `PickProcessWindow`
- `SpeechToTextWindow`
- `NewVersionAvailableWindow`
- `RunInstallerWindow`
- `UpdateButtonMappingsWindow`
- `UpdateContextSwitchesWindow`
- Overlays: `GripOMeterWindow`, `GapMonitorWindow`, `CursorCountdownOverlay`

**Pages** (all inherit from `UserControl`):

- `RacingWheelPage`
- `SteeringEffectsPage`
- `PedalsPage`
- `SeatBeltTensionerPage`
- `WindPage`
- `SoundsPage`
- `SpeechToTextPage`
- `CommentaryPage`
- `TradingPaintsPage`
- `AppManagerPage`
- `GraphPage`
- `SimulatorPage`
- `OverlaysPage`
- `AppSettingsPage`
- `AdminBoxxPage`
- `DebugPage`
- `ContributePage`
- `DonatePage`
- `HelpPage`

**Core custom controls:**

- `MairaButton : UserControl`
- `MairaMappableButton : MairaButton`
- `MairaComboBox : UserControl`
- `MairaTextBox : UserControl`
- `MairaKnob : UserControl`
- `MairaSwitch : UserControl`
- `MairaDualSlider : UserControl`
- `MairaButtonMapping : UserControl`
- `MairaAppMenuPopup : UserControl`

### 2.2 Viewers

- `TelemetryDataViewer : Control`
- `HeaderDataViewer : Control`
- `SessionInfoViewer : Control`

These are low-level drawing controls:

- Override `OnRender`.
- Maintain their own visual state.
- Render textual and graphical representations of telemetry / session data.

### 2.3 Data / MVVM

- `DataContext : INotifyPropertyChanged`
  - Singleton, exposed as `DataContext.Instance`.
- `Settings : INotifyPropertyChanged`
- `ContextSwitches : INotifyPropertyChanged`
- `Context : IComparable<Context?>`
- `ContextSettings` — options regarding how contexts behave.

### 2.4 Components / Services

Most service classes do **not** use deep inheritance; they are plain classes. Notable base types:

- `RecordingManager : IDisposable`
- `Logger` (plain class; no interface)
- `Graph : GraphBase`
- `GraphBase`
  - Core bitmap/drawing-buffer logic.
- `Telemetry`
  - Holds `DataBufferStruct` with `[StructLayout(LayoutKind.Sequential, Pack = 4)]` for MMF.

### 2.5 Converters / Helpers

- `HelpIconVisibilityConverter : IMultiValueConverter`
- A variety of utility helper classes.

**Summary of inheritance topics:**

- WPF base types (`Window`, `UserControl`, `Control`) dominate.
- Domain-specific bases: `GraphBase`, `MairaButton`.
- Data classes implement `INotifyPropertyChanged`.
- Most functionality is assembled by **composition plus global singletons**.

---

## 3. Component Relationships

This section describes “who talks to whom” at the component level.

### 3.1 `App` as Hub / Service Locator

`App` constructs and owns almost all components:

```text
App
 ├─ Logger
 ├─ TopLevelWindow
 ├─ CloudService
 ├─ SettingsFile
 ├─ Graph          (GraphBase)
 ├─ Pedals
 ├─ AdminBoxx
 ├─ Debug
 ├─ RacingWheel
 ├─ ChatQueue
 ├─ AudioManager
 ├─ Sounds
 ├─ DirectInput
 ├─ StreamDeck
 ├─ LFE
 ├─ MultimediaTimer
 ├─ Simulator
 ├─ RecordingManager
 ├─ SteeringEffects
 ├─ VirtualJoystick
 ├─ Drivers
 ├─ TimingMarkers
 ├─ Telemetry
 ├─ TextToSpeech
 ├─ Commentary
 ├─ SpeechToText
 ├─ Wind
 ├─ SeatBeltTensioner
 ├─ HidHotPlugMonitor
 ├─ TradingPaints
 └─ AppManager
```

Other classes get at these via `App.Instance!`.

### 3.2 `DataContext` and Settings

- `DataContext.Instance` is created in its constructor and stored statically.
- `DataContext` owns:
  - `Localization` (via `Components.Localization`).
  - `_settings : Settings`.

**`Settings` responsibilities:**

- Raise `PropertyChanged` for WPF bindings.
- Coordinate with `App.Instance.SettingsFile` for persistence:
  - Mark `SettingsFile.QueueForSerialization = true` on changes.
- Sometimes trigger UI or other side effects.

**`ContextSwitches` responsibilities:**

- Reference `DataContext.Instance`.
- Toggle `SettingsFile.QueueForSerialization` when switches change.

**Usage graph:**

```text
Pages / Controls
   └─ bind to → DataContext.Instance
        ├─ Settings
        └─ ContextSwitches, Context, ContextSettings

Settings
   └─ serializes via → SettingsFile (component)
SettingsFile
   └─ owned by → App
```

### 3.3 Hardware & I/O Components

**Simulator**

- Uses `IRSDKSharper.IRacingSdk`.
- Produces live properties: lap data, tire data, RPM, flags, etc.
- Updates other components or is polled by them.
  - Example: `RacingWheel` uses RPM, speed, slip, surface data.

**DirectInput**

- Uses `SharpDX.DirectInput`.
- Manages physical joystick/FFB devices.
- Sends FFB data using DI constants (e.g., `DI_FFNOMINALMAX`).

**RacingWheel**

- Uses telemetry from `Simulator`.
- Implements FFB algorithms:
  - `Native60Hz`
  - `Native360Hz`
  - `DetailBooster`
  - and others.
- Outputs FFB via `DirectInput`.
- Contributes to shared telemetry (`Telemetry`).

**Other hardware-facing components:**

- `Pedals`
- `LFE`
- `VirtualJoystick`
- `StreamDeck`
- `Wind`
- `AdminBoxx`

Each:

- Wraps a hardware device or subsystem.
- Interacts with `Simulator` and `Settings`.

**Telemetry Component**

- Uses `MemoryMappedFile` and a fixed `DataBufferStruct` layout.
- Collects values from components like `RacingWheel`, `Pedals`, etc.
- Exposes them to external consumers (e.g., overlay tools).

### 3.4 UI and Controls

**Windows / MainWindow**

- Host pages in a frame or via content controls.
- Often set `DataContext` to `DataContext.Instance`.
- `MainWindow` exposes static references to certain pages:
  - e.g. `MainWindow._graphPage`.
- Components like `Graph` call into those references:
  - `Graph.Initialize(MainWindow._graphPage.Image);`.

**Pages**

- In XAML, typically:

  ```xml
  <UserControl.DataContext>
      <Binding Source="{x:Static datacontext:DataContext.Instance}" />
  </UserControl.DataContext>
  ```

- Bind to `Settings` and other `DataContext` properties.
- Code-behind handles user interactions and calls:
  - `App.Instance.RacingWheel`
  - `App.Instance.Sounds`
  - etc.

**Custom Controls**

- `MairaButton`:
  - Visual button with a `Click` event and bitmap-based styling.
- `MairaMappableButton`:
  - Extends `MairaButton` with button-mapping behavior (using `ButtonMappings` helper).
- `MairaComboBox`, `MairaKnob`, `MairaSwitch`:
  - Visual representations bound to `Settings`.
  - Update settings on user interaction.
- Many of these controls:
  - Directly use `App.Instance` and `DataContext.Instance`.

**Viewers**

- `TelemetryDataViewer`, `HeaderDataViewer`, `SessionInfoViewer`:
  - Custom drawing.
  - Read IRSDK / telemetry values.
  - Render textual and graphic info.

---

## 4. How MVVM, UI, Data Layer, and Hardware Interact

This area describes data & control flow in runtime.

### 4.1 Configuration Flow (MVVM-ish Side)

1. **Startup**
   - `App` is created.
   - It constructs almost all components.
   - `DataContext` is constructed, sets `DataContext.Instance = this`.
   - `DataContext` creates:
     - `Localization`.
     - `Settings`.
   - `Settings`:
     - Loads defaults.
     - Loads persisted values via `SettingsFile`.

2. **UI Binding**
   - Each page/window sets `DataContext` to `DataContext.Instance` (XAML or code).
   - Controls bind to properties on `Settings` (e.g., `RacingWheelEnableForceFeedback`, pedal calibration settings, etc.).

3. **User Changes a Setting**
   - WPF binding pushes changes from UI into `Settings` properties.
   - `Settings.OnPropertyChanged`:
     - Optionally logs changes via `Logger`.
     - Sets `SettingsFile.QueueForSerialization = true`.
     - Raises `PropertyChanged` to refresh UI.

4. **Persistence**
   - `SettingsFile` (component) handles actual serialization:
     - Likely using `XmlSerializer` + `Serializer` helper.
     - Writes to disk so settings survive restarts.

**MVVM characterization:**

- **Model:** `Settings`, `Context`, `ContextSettings`.
- **ViewModel:** Mainly `Settings` + global `DataContext` (single shared VM).
- **View:** Pages, Windows, Controls.

The pattern is **MVVM-flavored** but:

- Uses a global singleton viewmodel.
- Uses a service-locator (`App.Instance`) pattern.
- Relies heavily on code-behind event handlers instead of `ICommand`.

### 4.2 Runtime / Hardware Flow

1. **Worker Thread & Simulator**
   - `App` owns a worker thread (`_workerThread`) and an `AutoResetEvent`.
   - This drives periodic work for telemetry updates.
   - `Simulator`:
     - Connects to iRacing via `IRSDKSharper.IRacingSdk`.
     - On each tick, pulls telemetry and updates its properties.

2. **Hardware Components Consume Telemetry**
   - `RacingWheel`:
     - Reads data from `Simulator` (e.g., speed, surface, slip).
     - Computes FFB torque using the selected algorithm.
   - `Pedals`:
     - Reads telemetry (brake pressure, ABS, etc.).
     - Applies calibration from `Settings`.
   - `LFE`, `Wind`, `AdminBoxx`, `VirtualJoystick`:
     - Consume relevant telemetry and configuration.

3. **Hardware Output**
   - `RacingWheel` + `DirectInput`:
     - Collaborate to send FFB packets over USB.
   - `VirtualJoystick`:
     - Exposes values as vJoy axes/buttons.
   - `AudioManager` / `Sounds`:
     - Play audio cues based on telemetry events.
   - `Telemetry`:
     - Packs selected values into `DataBufferStruct`.
     - Writes to memory-mapped file for other processes.

4. **Visual Feedback**
   - `Graph` (inherits `GraphBase`):
     - Uses `MainWindow._graphPage.Image` as its drawing surface.
     - Renders multichannel graphs (torque, forces, etc.).
   - `TelemetryDataViewer`, `SessionInfoViewer`, `HeaderDataViewer`:
     - Render textual / numeric metadata from IRSDK / telemetry.

**End-to-end mental pipeline:**

```text
iRacing (IRSDK)
   ↓
Simulator
   ↓
Components (RacingWheel, Pedals, LFE, Wind, etc.)
   ↓
Hardware & I/O (DirectInput, Audio, VirtualJoystick, AdminBoxx)
   ↓                ↓
Telemetry MMF       UI Viewers & Graphs
```

