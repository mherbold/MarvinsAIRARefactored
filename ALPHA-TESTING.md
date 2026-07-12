# MAIRA FFB Graph Alpha — Tester Guide

**Build:** [Version 2.0.466.1058 (pre-release)](https://github.com/mherbold/MarvinsAIRARefactored/releases/tag/2.0.466.1058)

This alpha replaces MAIRA's entire force feedback system. The fixed set of FFB algorithms (Detail booster, Delta limiter, Slew and total compression, Multi adjustment toolkit, …) is gone; in its place is a **modular FFB graph** — an audio-DSP-style node editor where the force feedback signal chain is built out of small modules that you wire together yourself.

This document explains what changed versus the released (main branch) version, and then walks through every new feature in detail.

---

## Before you install

- **This build is invisible to the update checker.** The app will not offer it, and it is not the "Latest" release on GitHub. Download and run the installer manually from the release page above. The installer is code-signed like normal releases.
- **Your force feedback settings will NOT carry over.** There is no migration from the old algorithm settings to the graph system — everyone starts on the built-in graph. All of your *other* settings (pedals, sounds, overlays, commentary, G Tensioner, controller profiles, wheel force / max force, etc.) are preserved.
- **Old FFB recordings are incompatible.** The recording file format changed (v3, 360 Hz with many more channels). A fresh sample recording is installed for the preview graph, and you can record your own (see [Recordings](#recordings-and-the-preview-graph)).
- **Rolling back is safe.** Install the previous version over this one at any time. Note the graph data this alpha writes into `Settings.xml` will simply be ignored by the old version, and your old (dormant) algorithm settings are still in the file, so the old version picks up right where you left it.

---

## Summary — main branch vs. this branch

| Area | Main (released) | This alpha (ffb-stack) |
|---|---|---|
| FFB processing | 8 fixed algorithms with flat knobs | User-built **FFB graphs** (node editor, ~30 module types) |
| Wheel vibration effects | Fixed steering-effect + vibrate knobs | Separate **vibration graphs** built from generator modules |
| Processing rate | 60 Hz update with per-algorithm upsampling | Entire chain at **360 Hz**, dedicated high-rate output thread |
| Racing Wheel page | Algorithm / Output / Effects / Parked sections | FFB graph section + vibration effects section |
| Steering Effects page | Wheel force + vibration groups | Wheel groups moved into the graphs (thresholds/calibration remain) |
| FFB defaults | Per-algorithm defaults | **Built-in graphs** shipped with the app ("Marvin's awesome graph") |
| Sharing setups | Not possible | **Export / import** graphs as `.mairagraph` files |
| Recordings | Fixed 60-second, 2 channels | Toggle record up to 5 min, 22 channels, auto-stop on lap completion |
| Preview | Fixed-width graph | 1 pixel per sample, hover zoom + **data readout + track map** |
| First-run wizard | Picks an algorithm + preset knobs | Tunes the built-in graph's **detail gain** (25%–200%) |
| Crash/curb protection | Crash reduces force; curb partial | Both reduce force, both have **recovery time**; new defaults |
| Button mappings | Fixed set of mappable knobs | Any **module knob** can be bound to controller buttons |

Everything else (pedals, G Tensioner, Typhoon Wind, commentary, overlays, Trading Paints, AdminBoxx, …) is unchanged.

---

## The FFB graph system

### Concepts

A **graph** is a chain of **modules** connected by **wires**. Signals flow left to right, in Nm (torque), from one or more **sources**, through whatever processing you wire up, into the fixed **Output** module, which converts the final torque to the normalized signal sent to your wheelbase.

- Every module has an **enable switch** — a disabled module passes its input A through untouched (a disabled source outputs zero).
- Most modules take one input; mixers take two (A and B).
- You can have **many graphs** and switch between them with the graph selector — per car/track if you want (see [Per-context values](#per-context-values-and-scopes)).
- The graph structure (which modules, how wired) is global; the **knob values** are per-context, so the same graph can be tuned differently per car.

### The node editor

On the Racing Wheel page, the FFB graph section shows the graph as draggable node boxes:

- **Click** a node to select it — its settings appear in the panel below, and the preview graph taps its signals.
- **Right-click** a node to lock the *preview* to it while keeping a different node selected — so you can turn one module's knobs while watching another module's output.
- **Drag** a node to move it. A snap-to-grid toggle (dot-grid icon) and an auto-layout wand live in the corner of the canvas.
- **Drag a wire**: press on a connector dot (crosshair cursor) and drag to another module's connector — works from output→input or input→output. Invalid targets (cycles, the Output module's own output, etc.) simply won't connect.
- **Add a module** with the + button; the new module is spliced into the selected node's output wire. **Remove** a module with the − button on its settings card; its consumers are re-wired to its input so the signal keeps flowing.
- The canvas scrolls horizontally and grows as you drag nodes outward.

The **preview graph** below shows the selected module's signals replayed through a recording: red = input A, green = input B (dual-input modules), blue = output.

### Module reference

All values below are the defaults. Knob values can be click-stepped, dragged, or click-typed directly.

#### Sources (emit a signal, no inputs — one of each type per graph)

| Module | What it emits | Settings |
|---|---|---|
| **60 Hz source** | iRacing's 60 Hz steering torque, optionally predicted forward | Prediction mode (Disabled / Predict K1 / Predict K2, default K1), Prediction blend (30%) |
| **360 Hz source** | iRacing's raw 360 Hz steering torque | — |
| **LFE source** | Torque from the low-frequency-effects audio capture | — |
| **Soft lock source** | Opposing force past the car's steering lock | Strength (25%) |
| **Wheel velocity source** | Torque proportional to how fast the wheel is turning — the building block for friction/damping (pair with Speed gain) | — |
| **Wheel centering source** | Spring force pulling the wheel to center | Strength (75%) |

#### Generic DSP

| Module | What it does | Settings |
|---|---|---|
| **Gain** | Multiplies the signal | Gain (1.00×, range −5…+5) |
| **Compressor** | Squeezes torque above a threshold (audio-style) | Threshold (30 Nm), Knee (5 Nm), Ratio (4:1) |
| **High-pass filter** | Passes only the fast detail | Slope (One pole / Two pole), Cutoff (Hz) |
| **Low-pass filter** | Passes only the slow body of the force | Slope, Cutoff (Hz) |
| **Slew compressor** | Squeezes the *speed* of torque changes above a threshold | Threshold (75 Nm/s), Knee (30 Nm/s), Ratio (3:1), Peak mode |
| **Slew limiter** | Hard-limits how fast torque can change | Limit (360 Nm/s) |
| **Adaptive smoother** | One Euro filter — smooths hard when the signal is calm, opens up during fast changes | Amount (0–100%) |
| **Transient enhancer** | Outputs amplified attack transients only (nonlinear detail extractor) | Cutoff (7.2 Hz), Gain (1.00×) |

#### Mixers (two inputs)

| Module | What it does | Settings |
|---|---|---|
| **Add** | A + B | — |
| **Subtract** | A − B | — |
| **Blend** | Crossfade between A and B | Mix (50%) |
| **Adaptive blend** | Follows B (the anchor), letting A's detail through; direction flips snap the corner down and ease back | Cutoff (20 Hz), Peak cutoff (6 Hz), Hold (28 ms) |

#### Effects

| Module | What it does | Settings |
|---|---|---|
| **Speed gain** | Ramps gain between two car speeds — parked-strength, fade-ins, friction crossfades | Min/Max speed (0/30 m/s), Gain at min/max (1.00×) |
| **Torque dither** | Tiny alternating torque below a threshold to keep the wheel mechanism live | Strength (1%), Threshold (10%) |
| **Crash protection** | Cuts force during a crash | Long/Lat G force (8 g / 8 g), Duration (0.2 s), Force reduction (95%), Recovery time (0.2 s) |
| **Curb protection** | Cuts force over violent curb strikes — now actually reduces force like crash protection | Shock velocity (0.5 m/s), Duration (0.1 s), Force reduction (75%), Recovery time (0.1 s) |
| **Understeer / Oversteer / Seat-of-pants force** | Increase or decrease force with the steering-effect signal | Direction (None / Decrease / Increase), Strength (10%), Curve |

Crash protection, curb protection, and speed gain have a **test button** (vibrate icon) on their settings card so you can trigger the effect on demand.

#### Output (fixed, always last)

Converts the final Nm signal to the normalized wheel output, then applies two shapers that only make sense in normalized space:

- **Curve** — response curve bending (OFF at 0)
- **Soft limiter** — a compressor near full output instead of hard clipping: Threshold (90%), Knee (30%), Ratio (6:1), on by default

---

## Vibration graphs

The wheel vibration effects live in their own graphs, selected independently of the FFB graph (VIBRATION EFFECTS section). A vibration graph is a flat list of **generator** modules — no wiring, they all feed the vibration bus directly:

- **Understeer / Oversteer / Seat-of-pants vibration** — Pattern (sine, square, triangle, sawtooth in/out), Strength, Min/Max frequency, Curve
- **Shift RPM vibration** — pulses when it's time to shift up
- **Gear change vibration** — pulses on every gear change
- **ABS vibration** — vibrates while ABS is active
- **Road texture / Slip texture** — band-limited noise scaled by speed / tire slip (new!)

The built-in vibration graph is named **Default**.

---

## Built-in vs. custom graphs

The graph dropdowns are split into two categories:

- **Built-in** — shipped inside the app. Currently **Marvin's awesome graph** (FFB) and **Default** (vibration). Built-ins:
  - cannot be renamed or deleted,
  - cannot be structurally changed — no adding/removing modules, no re-wiring, no moving nodes,
  - **can** have every knob/switch adjusted (and those adjustments are per-context like everything else),
  - have a **Reset** button that restores the shipped structure *and* clears your knob adjustments for it,
  - are **updated automatically** when a new app version ships an updated copy — so alpha fixes to the built-in graph reach you on the next install without touching your custom graphs.
- **Custom** — yours. To modify a built-in's structure, press **New graph** and choose **Clone current graph**; the clone is fully editable.

**Marvin's awesome graph** (the shipped starting point) splits the 360 Hz torque into a low-pass body plus a high-pass detail branch with its own **detail gain** (this is the gain the first-run wizard tunes), recombines them, ramps force down when parked, adds smoothed wheel centering at low speed, mixes in LFE, and finishes with the Output soft limiter.

---

## Per-context values and scopes

Which graph is selected *and* all of its module values follow one context scope — right-click the graph selector's label to set it (default: per wheelbase + per car). Switch cars and your knob tweaks for that car come back; the graph structure itself is shared.

The vibration graph selection has its own independent scope.

---

## Sharing graphs (export / import)

The export/import buttons sit next to the graph management buttons:

- **Export** writes the current graph to a `.mairagraph` file (a built-in exports as an ordinary editable graph).
- **Import** validates and adds the file as a new custom graph (auto-renamed if the name is taken). FFB and vibration graph files are type-tagged, so you can't import one into the other's slot. Files from a newer app version are rejected with a clear message instead of silently mangling.

Only the graph travels — your per-car knob adjustments and button bindings stay local.

---

## Binding buttons to module knobs

Any module knob can be driven from your wheel/button box: **right-click the knob's + or − button** and map inputs in the usual mapping window (mapped buttons get the orange ring). Notes:

- Bindings are tied to the specific module in the specific graph — they only act while that graph is selected.
- Bindings are global (not part of controller profiles) and never ride along with graph export/import.
- The increment per press is the knob's normal click step.

---

## Recordings and the preview graph

The preview graph replays a recorded lap segment through the live graph, so every knob change is instantly visible without driving.

- **Record button** (on the preview, top-right): press once to start — you'll hear a rising beep — and again to stop (falling beep). Recording also stops automatically when:
  - you complete a lap (return within 100 m of where you started — start/finish line wrap handled correctly),
  - you go off track (the partial take is saved),
  - the 5-minute cap fills.
- Recordings capture the full 360 Hz tick context: torques, LFE, G forces, shock velocity, wheel position/velocity, steering angle/velocity, speed, RPM, gear, ABS, steering-effect signals, track position, and heading/velocity for the track map.
- The **choose recording** button picks which recording the preview uses. Recordings load on demand now (only one in memory), so having many costs nothing.
- The **beeps** are configurable on the Sounds page (enable, volume, frequency) like every other sound.

**Hover the preview** to get a three-panel popup:

1. **Zoom** — magnified view of the traces around the cursor.
2. **Data card** — every recorded value at that exact sample (time, track position, torques, steering, speed, gear, ABS, G forces, shock velocity, effect signals).
3. **Track map** — the recorded segment drawn as a track outline (~500 m across), with the car's position at the cursor (orange dot), recording start (green) and end (red).

---

## Engine internals (what you should feel)

- The whole processing chain now runs at a true **360 Hz** in a single burst per telemetry frame, handed to a dedicated high-rate output thread that streams torque to the wheelbase. Torque prediction (on the 60 Hz source) smooths the 60 Hz signal's staircase.
- A performance pass eliminated steady-state memory allocations in the hot path, so there are no garbage-collection hitches — force delivery should be glassy even during long sessions.
- Wheel output writes are skipped when the value hasn't changed, cutting driver overhead while parked.

---

## First-run wizard

The wizard's FFB style step no longer picks an algorithm. Its 7-position slider now sets the built-in graph's **detail gain**: 25% / 50% / 75% / 100% / 125% / 150% / 200%, from "Silky smooth" to "A lot of boost". You can re-run the wizard, or just turn the Gain knob in the graph yourself.

---

## Other changes in this build

- Crash protection defaults changed: lateral trigger 8 g (was 6), duration 0.2 s, recovery 0.2 s.
- Curb protection now genuinely reduces force (it previously only announced); both protections ramp smoothly back via the new recovery time.
- The old Racing Wheel page sections (Algorithm, Output, Crash/Curb protection, Parked effects, Effects) and the Steering Effects page's wheel groups are gone — all of that lives in the graphs now. Steering-effect *detection* settings (thresholds, calibration) remain on the Steering Effects page.

---

## Known gaps in this alpha

- **Online documentation still describes the old UI.** The racing wheel page docs will be rewritten before general release; until then, this document is the reference.
- **New UI strings are English-only** in translated languages — translations arrive with the general release.
- The old algorithm settings are still stored (dormant) in `Settings.xml` for rollback safety; they'll be removed in a later release.

## What to test / feedback

- Fresh-feel check: does the built-in graph feel right on your wheelbase and usual cars? Try the wizard slider extremes.
- Build a custom graph: clone the built-in, add/remove/rewire modules, confirm nothing crashes and the preview matches what you feel.
- Per-car tuning: tweak knobs on two different cars and confirm each car's values come back when you switch.
- Record laps at different tracks — verify the auto-stop on lap completion and the track map's shape.
- Bind a couple of module knobs to buttons and adjust while driving.
- Export a graph, send it to another tester, and have them import it.

Report anything odd (with your `Logs` folder from `Documents\MarvinsAIRA Refactored`) through the usual channels. Thank you for testing!
