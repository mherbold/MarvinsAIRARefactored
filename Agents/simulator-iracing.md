# Simulator & iRacing Integration

## Related Source Files
- `Components/Simulator.cs` — iRacing SDK bridge (IRSDKSharper wrapper)
- `Components/Telemetry.cs` — Memory-mapped file IPC (exports data to SimHub etc.)
- `Components/Drivers.cs` — iRacing driver / car tracking
- `Components/TimingMarkers.cs` — Lap timing markers
- `Viewers/SessionInfoViewer.cs` — Debug viewer for session YAML
- `Viewers/TelemetryDataViewer.cs` — Debug viewer for telemetry data
- `Viewers/HeaderDataViewer.cs` — Debug viewer for SDK header data
- `Pages/SimulatorPage.xaml/.cs` — Simulator settings UI
- `Notes/SessionInfo.yaml` — Example session YAML dump (development reference)
- `Notes/TelemetryData.yaml` — Example telemetry dump (development reference)

---

## iRacing SDK Integration (`Simulator`)

`Simulator` wraps the **IRSDKSharper** library (v1.1.6). It is the single point of contact between MAIRA and the iRacing process.

### Lifecycle Events

| Event | Frequency | Notes |
|---|---|---|
| `OnConnected` | Once | iRacing launched and a session is running |
| `OnDisconnected` | Once | iRacing closed or session ended |
| `OnSessionInfo` | On change | Full YAML blob of session data (car list, track info, etc.) |
| `OnTelemetryData` | **60 Hz** | Per-frame telemetry tick; triggers the MAIRA worker thread |

`OnTelemetryData` signals `App._autoResetEvent` via `app.TriggerWorkerThread()`. The **MAIRA App Worker Thread** then wakes up and processes all components in sequence.

### Sub-Tick (360 Hz) Data

iRacing provides 6 sub-samples per 60 Hz frame for certain channels (e.g., steering torque). These are exposed as array datums with the `_ST` suffix. The constant `SamplesPerFrame360Hz = 6` is defined in `Simulator.cs` and used throughout the FFB pipeline.

### Datum Handle Caching

```csharp
// Resolve once — store the handle
var steeringTorqueDatum = _irsdk.Data.GetDatum( "SteeringWheelTorque_ST" );

// Use the cached handle every frame (fast path)
var torque = _irsdk.Data.GetFloat( steeringTorqueDatum, subSampleIndex );
```

**Never** call `GetDatum(string)` inside a per-frame or per-tick loop — resolve once on `OnConnected` or first use and cache the handle.

### Session YAML Parsing

`OnSessionInfo` provides a YAML string. MAIRA uses **YamlDotNet** to parse it into a typed object graph. The example YAML in `Notes/SessionInfo.yaml` is useful for offline development and testing.

---

## IPC / Telemetry Export (`Telemetry`)

`Telemetry.cs` exposes a subset of MAIRA's computed outputs to external tools (SimHub, dashboards, etc.) via a **Windows memory-mapped file**:

| Property | Value |
|---|---|
| MMF name | `Local\MAIRARefactoredTelemetry` |
| Layout | Fixed-layout `unsafe struct DataBufferStruct` |
| Update frequency | Every 60 Hz worker thread tick |

External readers map the same struct layout and read fields directly. Any change to `DataBufferStruct` field order or size is a **breaking change** for all external consumers — coordinate carefully.

---

## IRSDKSharper Reference

The **IRSDKSharper** library source lives at:
```
C:\Users\marvi\OneDrive\Documents\GitHub\IRSDKSharper
```
The key enum file is `IRacingSdkEnum.cs` in that directory. When you need to check enum values, read that file directly — do **not** guess values.

## Driver & Car Tracking (`Drivers`)

`Drivers` maintains a live list of all cars/drivers in the current session, updated from `OnSessionInfo` and `OnTelemetryData`:
- Car index, car number, driver name, iRating, license.
- Current lap, lap distance percentage, gap to player.
- Used by the Gap Monitor overlay and the Grip-O-Meter overlay.

---

## Timing Markers (`TimingMarkers`)

`TimingMarkers` records lap-split reference points. It fires events consumed by `Graph` and the recording system. Markers are stored per-car per-track and survive across sessions.

---

## Debug Build Extras

In `DEBUG` configuration:
- `SessionInfo.yaml` and `TelemetryData.yaml` are written to the documents folder on every `OnSessionInfo` / `OnTelemetryData` event. Compare against the checked-in `Notes/` files to spot SDK changes.
- Min/max `FrameRate` and `GpuUsage` are logged to the logger every second.

---

## Telemetry Data Viewers

The `Viewers/` classes are development-only helpers used in `DebugPage` to inspect live SDK data:

| Viewer | What it shows |
|---|---|
| `SessionInfoViewer` | Parsed YAML tree (all session info fields) |
| `TelemetryDataViewer` | All active telemetry channel names, types, and current values |
| `HeaderDataViewer` | Raw iRacing SDK memory-mapped file header fields |
