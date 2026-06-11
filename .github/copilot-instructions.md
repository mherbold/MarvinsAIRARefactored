# Copilot Instructions

## Project Overview

**MarvinsAIRA Refactored** (v2.0) is a Windows desktop application written in **C# 13 / .NET 9** using **WPF** (Windows Presentation Foundation). It is a sim-racing companion tool for **iRacing** that provides advanced force-feedback processing, steering effects, pedal haptics, hardware integrations, and various overlays.

The project has a second build target called **AdminBoxx**, controlled via the `ADMINBOXX` preprocessor constant. When `ADMINBOXX` is defined, many features are disabled and the app runs as a simpler hardware-controller utility.

---

## Project Rules

- Only use the editor's built-in edit/apply tools (`replace_string_in_file`, `multi_replace_string_in_file`, `create_file`) to modify files.
- Do not use PowerShell, terminal commands, or shell scripts to write file content under any circumstances. If an edit cannot be applied directly, explain why and stop.

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

> **Important for agents resolving file paths:**
> The solution root and the main application project share the same name, creating a **nested directory**:
> `[repo root]/MarvinsAIRARefactored/MarvinsAIRARefactored.csproj`
> All application source files (Components, DataContext, Windows, Pages, etc.) live under
> `[repo root]/MarvinsAIRARefactored/` — **not** directly under `[repo root]/`.
>
> When constructing absolute paths, always use `get_projects_in_solution` first to confirm the project path, then derive file paths from `[workspace root] + project directory`. Do **not** trust paths returned by `code_search` directly — they can silently omit the project subdirectory segment.

---

## Architecture & Key Patterns

### Global Singleton (`App`)
`App` (in `App.xaml.cs`) is the central singleton accessed via `App.Instance!`. It owns and initializes every component. All components call `App.Instance!` to access sibling services. This is the primary way components communicate.
