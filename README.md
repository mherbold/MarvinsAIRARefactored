# MAIRA Refactored (2.0)

**Marvin's Awesome iRacing App (MAIRA) Refactored** is a Windows desktop companion app for the [iRacing](https://www.iracing.com/) simulator. It is written in **C# 13 / .NET 9** using **WPF**.

- Website & documentation — https://mairapp.com
- Progress / roadmap on Trello — https://trello.com/b/o7vbR74U/maira-refactored-20
- Questions or suggestions — join the Discord server: https://discord.gg/Y7JN3BAz72

## Features

- **Force feedback** — advanced FFB processing for direct-drive and belt/gear wheels, with multiple algorithms (Native 60 Hz, Native 360 Hz, Detail Booster, and more), crash protection, and curb protection.
- **Steering effects** — wheel-position-based effects layered on top of the sim's FFB, with per-wheel calibration.
- **Pedal haptics** — Simagic HPR and other haptic pedal support driven by live telemetry (ABS, wheel lock, wheel spin, etc.).
- **G Tensioner** — seat-belt tensioner support, including SimHub-style DIY belt tensioner hardware.
- **Typhoon Wind** — wind simulator hardware control.
- **Game bridge** — feeds telemetry from other simulators into MAIRA's iRacing-native pipeline, so force feedback and effects work outside of iRacing. Currently supports Le Mans Ultimate, rFactor 2, Assetto Corsa, Assetto Corsa Competizione, Assetto Corsa EVO, Assetto Corsa Rally, and RaceRoom, with optional vJoy steering passthrough.
- **Audio** — sound effects and warnings, low-frequency effects (LFE), and per-device audio output management (FMOD).
- **AI commentary & speech** — text-to-speech race commentary (including ElevenLabs voices), speech-to-text for in-sim chat, and a chat queue.
- **Hardware integrations** — Elgato Stream Deck, vJoy virtual joystick, AdminBoxx controller, and HID hot-plug monitoring.
- **Trading Paints** — automatic livery downloading.
- **Overlays** — gap monitor, delta monitor, Grip-O-Meter, and cursor countdown overlays with per-context layouts.
- **App manager** — launches and manages your other sim-racing apps alongside the simulator.
- **Controller profiles, button mappings, and per-car/track contexts** — settings automatically switch with the car and track you are driving.
- **Localization** — the UI is fully localized into multiple languages.

The project also has a second build target, **AdminBoxx** (selected via the `ADMINBOXX` preprocessor constant), which strips out most sim-racing features and runs as a simpler hardware-controller utility.

## Building from source

Requirements:

- Windows 10 2004 (build 19041) or later
- Visual Studio 2022 (or later) with the .NET desktop development workload and the .NET 9 SDK

Open `MarvinsAIRARefactored.sln` in Visual Studio and build the **x64** platform. Note that the project uses COM references, which require the full Visual Studio MSBuild — plain `dotnet build` will fail with MSB4803.

Build configurations:

- **Debug** — extra logging; also writes `SessionInfo.yaml` and `TelemetryData.yaml` to the documents folder.
- **Release** — standard release build.
- **ADMINBOXX** — the AdminBoxx build target described above.

The app version is computed at build time from the current UTC date — there is no manual version bumping. A post-build step copies asset folders (sounds, recordings, calibration files, etc.) into `My Documents\MarvinsAIRA Refactored\`.

## Repository layout

| Path | Contents |
|---|---|
| `MarvinsAIRARefactored/` | The main WPF application project (note: nested under a folder with the same name as the repo) |
| `MarvinsAIRARefactored.Win32/` | Companion project for source-generated P/Invoke (CsWin32) bindings |
| `Arduino/` | Firmware for supported DIY hardware |
| `InnoSetup/` | Installer script |
| `Server/` | Server-side PHP and WordPress assets for mairapp.com |
| `Tools/` | Telemetry capture tools and analyzers used to develop the game bridges |

Inside the main project:

| Folder | Contents |
|---|---|
| `Components/` | Runtime services (force feedback, telemetry, audio, hardware integrations, cloud, etc.) — the engine room |
| `GameBridges/` | Per-simulator telemetry bridge adapters (LMU, rFactor 2, AC family, RaceRoom) |
| `DataContext/` | Settings, per-context settings, localization, and the root WPF binding object |
| `Pages/` | Feature UI pages hosted in the main window (one page per feature area) |
| `Windows/` | Top-level windows, dialogs, wizards, and overlay windows |
| `Controls/` | Custom `Maira*` controls used in place of raw WPF controls |
| `Viewers/` | Custom drawing controls that render telemetry / session data |
| `Classes/` | Helpers — math and signal processing, audio, recording, serialization, hardware utilities |
| `Converters/` | WPF value converters |
| `Themes/` | Dark and light themes plus per-control theme resources |
| `FMOD/`, `TTS/`, `Fonts/`, `Artwork/` | Audio runtime, text-to-speech assets, fonts, and artwork |

## Architecture overview

- **`App` (`App.xaml.cs`) is the global singleton and service locator.** It owns and initializes every component and exposes them via `App.Instance`. Components reach their siblings the same way.
- **`Components/` is the engine room.** `Simulator` connects to iRacing via [IRSDKSharper](https://github.com/mherbold/IRSDKSharper) (or to another sim via `GameBridge`); consumers like `RacingWheel`, `Pedals`, `LFE`, `TyphoonWind`, and `GTensioner` read that telemetry and drive hardware through `DirectInput`, serial ports, and vendor APIs. The `Telemetry` component republishes selected values through a memory-mapped file for external tools.
- **`DataContext/` is the view-model layer.** A global `DataContext.Instance` exposes `Settings` (persisted app-wide settings), per-car/track `ContextSettings`, and the `Localization` string table. WPF pages and controls bind directly to it, and changes are queued for serialization automatically.
- **The UI layer is `Windows/`, `Pages/`, and `Controls/`.** `MainWindow` is the shell hosting one page per feature area. All interactive controls are custom `Maira*` controls rather than raw WPF ones.

The pattern is MVVM-flavored, with a single shared view model, service-locator access via `App.Instance`, and code-behind event handlers — favoring composition and shallow inheritance throughout.

## Key dependencies

| Package / library | Purpose |
|---|---|
| [IRSDKSharper](https://github.com/mherbold/IRSDKSharper) | iRacing SDK wrapper — telemetry and session info |
| SharpDX.DirectInput | Racing wheel / joystick input and force feedback |
| FMOD | Audio playback |
| OpenMacroBoard.SDK + StreamDeckSharp | Elgato Stream Deck integration |
| Microsoft.Web.WebView2 | Embedded browser (speech-to-text bridge) |
| Microsoft.Windows.CsWin32 | Source-generated P/Invoke wrappers |
| vJoy | Virtual joystick output |
| SimagicHPR | Simagic HPR pedal haptics |
| Accord.NET | Machine-learning / signal-processing utilities |

## Contributing

Feel free to contribute to the project by forking the repo and submitting pull requests. Your contributions are greatly appreciated!

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
