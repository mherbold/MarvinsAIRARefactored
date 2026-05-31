# Force Feedback

## Related Source Files
- `Components/RacingWheel.cs` — FFB algorithms and wheel output
- `Components/SteeringEffects.cs` — Understeer / oversteer / SeatOfPants effects
- `Components/DirectInput.cs` — DirectInput device polling and FFB output
- `Components/LFE.cs` — Low Frequency Effects (bass shaker) via DirectSound
- `Components/MultimediaTimer.cs` — High-resolution multimedia timer (~17 ms tick)
- `Classes/RlsWheelVelocityPredictor.cs` — RLS adaptive filter for wheel velocity prediction
- `Classes/MathZ.cs` — Math helpers used throughout FFB processing
- `Pages/RacingWheelPage.xaml/.cs` — UI for FFB settings
- `Pages/SteeringEffectsPage.xaml/.cs` — UI for steering-effects settings

---

## Multimedia Timer

`MultimediaTimer` uses the Windows `timeSetEvent` API (via `PInvoke/WinMM.cs`) at a ~17 ms period to drive high-priority FFB output on a dedicated **MAIRA Multimedia Timer Worker Thread**. This thread calls into `RacingWheel` and `DirectInput` on every tick.

The timer is started/stopped by `App` along with the rest of the component lifecycle.

---

## DirectInput Device Polling & FFB Output

`DirectInput.cs` manages all DirectInput devices:
- Enumerates joysticks and racing wheels via `SharpDX.DirectInput`.
- Polls devices for button/axis state on the multimedia timer thread.
- Sends force-feedback effect updates to the selected wheel device.
- Integrates Stream Deck as a fake DirectInput device (using a dedicated fake GUID) so it participates in the button-mapping system alongside real hardware.

---

## FFB Algorithms (`RacingWheel`)

`RacingWheel` implements several selectable FFB processing pipelines. The active algorithm is chosen per-user-setting:

| Algorithm | Description |
|---|---|
| `Native60Hz` | Raw iRacing 60 Hz torque output — no processing |
| `Native360Hz` | Raw iRacing 360 Hz sub-tick torque — smoother than 60 Hz |
| `DetailBooster` | Enhances small detail forces while preserving peaks |
| `DeltaLimiter` | Limits the rate-of-change of the FFB signal |
| `SlewAndTotalCompression` | Dual compression stages: slew-rate then total |
| `MultiAdjustmentToolkit` | Fully configurable multi-source blending (most powerful) |

**Sub-tick data**: iRacing exposes 360 Hz arrays (6 samples per 60 Hz frame, `_ST` suffix on datum names). `SamplesPerFrame360Hz = 6` is the constant used throughout.

**Telemetry datum caching**: `IRacingSdkDatum` handles are resolved on the first call and reused every frame — never look them up by name inside a hot loop.

---

## RLS Wheel Velocity Predictor

`RlsWheelVelocityPredictor` implements a **Recursive Least Squares (RLS)** adaptive filter that predicts future wheel velocity to compensate for USB/FFB output latency. It fits a polynomial model to recent velocity samples and extrapolates forward by a configurable number of milliseconds.

Key points:
- Used inside the multimedia timer callback — must be allocation-free on the hot path.
- The forgetting factor (`lambda`) controls how quickly old samples are discarded.
- Call `Reset()` when the wheel device changes or on simulator disconnect.

---

## Steering Effects (`SteeringEffects`)

`SteeringEffects` computes three additional FFB layers that overlay the base torque:

| Effect | Source signal | Purpose |
|---|---|---|
| **Understeer** | Slip angle difference front vs rear | Simulates front-end push / plowing feel |
| **Oversteer** | Rear slip angle / yaw rate | Simulates rear stepping out |
| **Seat Of Pants (SoP)** | Lateral G-force | Simulates chassis movement felt through the seat |

### Calibration Files
Each car requires a per-car **calibration CSV** stored at:
```
My Documents\MarvinsAIRA Refactored\Calibration\<CarName>.csv
```
The calibration routine drives the car slowly through a range of steering angles and records the relationship between steering input and lateral forces to build the reference curve.

If no calibration file exists for the current car, steering effects are disabled automatically.

### Adding or Changing Steering-Effects Settings
- Add new properties to `DataContext/Settings.cs` following the per-context pattern (see [`Agents/settings-context.md`](../Agents/settings-context.md)).
- Add matching properties to `DataContext/ContextSettings.cs`.
- Update `Pages/SteeringEffectsPage.xaml` — always use `controls:MairaKnob` or `controls:MairaSwitch` (see [`Agents/ui-wpf-controls.md`](../Agents/ui-wpf-controls.md)).

---

## Low Frequency Effects (`LFE`)

`LFE` captures audio from a configured DirectSound device and reprocesses it as tactile vibration output for bass shakers / buttkickers.

- Runs on a dedicated high-priority **LFE Worker Thread**.
- Uses `SharpDX.DirectSound` for audio capture.
- Output is routed back out via DirectInput FFB or a separate audio device depending on configuration.

---

## MathZ Hot-Path Helpers

Frequently used in FFB processing — all methods are `[MethodImpl(AggressiveInlining)]`:

| Method | Description |
|---|---|
| `MathZ.Lerp(a, b, t)` | Linear interpolation |
| `MathZ.Smoothstep(edge0, edge1, x)` | Smooth Hermite interpolation |
| `MathZ.Clamp(v, min, max)` | Value clamp |
| `MathZ.Map(v, inMin, inMax, outMin, outMax)` | Range remapping |

Do **not** remove `AggressiveInlining` from these methods — they are on the multimedia timer hot path.
