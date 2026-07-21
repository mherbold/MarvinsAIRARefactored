# MAIRA FFB Graph Alpha — Tester Guide

**Build:** [Version 2.0.475.54 (pre-release)](https://github.com/mherbold/MarvinsAIRARefactored/releases/tag/2.0.475.54) — the seventh alpha; see [New in this build](#new-in-this-build) for what changed since the sixth one.

This alpha replaces MAIRA's entire force feedback system. The fixed set of FFB algorithms (Detail booster, Delta limiter, Slew and total compression, Multi adjustment toolkit, …) is gone; in its place is a **modular FFB graph** — an audio-DSP-style node editor where the force feedback signal chain is built out of small modules that you wire together yourself.

This document explains what changed versus the released (main branch) version, and then walks through every new feature in detail.

---

## Before you install

- **This build is invisible to the update checker.** The app will not offer it, and it is not the "Latest" release on GitHub. Download and run the installer manually from the release page above. The installer is code-signed like normal releases.
- **Your force feedback settings will NOT carry over.** There is no migration from the old algorithm settings to the graph system — everyone starts on the built-in graph. All of your *other* settings (pedals, sounds, overlays, commentary, G Tensioner, controller profiles, wheel force / max force, etc.) are preserved.
- **FFB recordings from earlier builds are incompatible.** The recording file format changed in the fourth alpha (v5 — it captures the car's redline RPM for the engine RPM vibration); recordings made with any build before it no longer load. Fourteen fresh sample recordings covering a wide spread of cars and tracks are installed for the preview graph, and you can record your own (see [Recordings](#recordings-and-the-preview-graph)).
- **Rolling back is safe.** Install the previous version over this one at any time. Note the graph data this alpha writes into `Settings.xml` will simply be ignored by the old version, and your old (dormant) algorithm settings are still in the file, so the old version picks up right where you left it.

---

## New in this build

Changes since the sixth alpha (2.0.473.1208), for testers who already ran it:

- **Everything from released version 2.0.475.10 is now included.** The alpha branch has been synced with the released version — most notably the new **game bridge with Le Mans Ultimate support**, plus the USB CDC serial device detection fix, the audio fix when the iRacing simulator starts, and the off-screen main window recovery.
- **The game bridge is wired into the FFB graph engine.** Two integration pieces make the two new systems work together:
    - The bridge reconstructs your physical wheel angle from the steering axis (LMU clamps its reported steering value at full lock), so the graph's soft lock, wheel centering, and prediction inputs behave correctly.
    - The engine RPM vibration reads the car's redline and engine-running state from the game, so it sweeps to the right frequency and goes silent when the engine is off under LMU.

---

## Summary — main branch vs. this branch

| Area | Main (released) | This alpha (ffb-stack) |
|---|---|---|
| FFB processing | 8 fixed algorithms with flat knobs | User-built **FFB graphs** (node editor, ~30 module types) |
| Wheel vibration effects | Fixed steering-effect + vibrate knobs | **Generator nodes** inside the same FFB graph (incl. new engine RPM, road texture, and slip texture) |
| Processing rate | 60 Hz update with per-algorithm upsampling | Entire chain at **360 Hz**, dedicated high-rate output thread |
| Racing Wheel page | Algorithm / Output / Effects / Parked sections | One FFB graph section (vibrations included) |
| Steering Effects page | Wheel force + vibration groups | Wheel groups moved into the graphs (thresholds/calibration remain) |
| FFB defaults | Per-algorithm defaults | **Five built-in graphs** shipped with the app, one per old algorithm family |
| Sharing setups | Not possible | **Export / import** graphs as `.mairagraph` files |
| Latency compensation | — | **Prediction module** — adaptive lookahead up to 33 ms |
| Recordings | Fixed 60-second, 2 channels | Toggle record up to 5 min, 36 channels, auto-stop on lap completion |
| Preview | Fixed-width graph | 1 pixel per sample, horizontal zoom, hover zoom + **data readout**, **track map panel** |
| First-run wizard | Picks an algorithm + preset knobs | Tunes the flagship built-in graph's **detail gain** (25%–200%) |
| Crash/curb protection | Crash reduces force; curb partial | Both reduce force, both have **recovery time**; new defaults |
| Button mappings | Fixed set of mappable knobs | Any **module knob** can be bound to controller buttons |

Everything else (pedals, G Tensioner, Typhoon Wind, commentary, overlays, Trading Paints, AdminBoxx, the Le Mans Ultimate game bridge, …) is unchanged from the released version.

---

## The FFB graph system

### Concepts

A **graph** is a chain of **modules** connected by **wires**. Signals flow left to right, in Nm (torque), from one or more **sources**, through whatever processing you wire up, into the fixed **Output** module, which converts the final torque to the normalized signal sent to your wheelbase.

- Every module has an **enable switch** — a disabled module passes its input A through untouched (a disabled source outputs zero).
- Most modules take one input; mixers take two (A and B).
- You can have **many graphs** and switch between them with the graph selector — per car/track if you want (see [Per-context values](#per-context-values-and-scopes)).
- The graph structure (which modules, how wired) is global; the **knob values** are per-context, so the same graph can be tuned differently per car.

### Quick controls, description, and the node graph switch

The FFB graph section is built for two audiences. Below the graph selector sit the graph's **description** and its **pinned quick controls** — the settings the graph's author marked as the main knobs (pin them with the tiny switch on each setting in the module settings column; custom graphs only). Under those, the **"Show node graph"** switch reveals the full node editor; it is **off by default**, so a basic user only ever sees the quick controls and the preview. Everything below assumes the switch is on.

### The node editor

On the Racing Wheel page, the FFB graph section shows the graph as draggable node boxes:

- **Click** a node to select it — its settings appear in the column beside the graph, and the preview graph taps its signals.
- Nodes are **color-coded by role**: telemetry sources green, vibration generators blue, the Output node purple, ordinary chain modules gray; the selected node highlights orange.
- **Right-click** a node to lock the *preview* to it while keeping a different node selected — so you can turn one module's knobs while watching another module's output.
- **Drag** a node to move it. A snap-to-grid toggle (dot-grid icon) and an auto-layout wand live in the corner of the canvas.
- **Drag a wire**: press on a connector dot (crosshair cursor) and drag to another module's connector — works from output→input or input→output. Invalid targets (cycles, the Output module's own output, etc.) simply won't connect.
- **Add a module** with the + button; the new module is spliced into the selected node's output wire and placed at the splice point. **Remove** the selected module with the − button on the editor toolbar; its consumers are re-wired to its input so the signal keeps flowing.
- **Zoom** with Ctrl+mouse wheel — the view scales around the cursor.
- **Pan** by clicking and dragging empty canvas. Dragging a node past the viewport edge auto-crawls the view along with it.
- The canvas grows as you drag nodes outward.

- **Resize** the editor block with the two grab handles: the one on the horizontal seam sets the node graph's height, the one on the vertical seam sets the split between the node graph and the module settings column — both are remembered across sessions.
- Each node shows a live one-line **summary of its settings** under its name, so you can read the whole graph's tuning at a glance.

The **preview graph** below shows the selected module's signals replayed through a recording: red = input A, green = input B (dual-input modules), blue = output. Ctrl+mouse wheel over the preview zooms out horizontally (down to every 20th data point) to see more of the recording; the **track map panel** to its right shows the whole recorded segment with the currently visible range highlighted in orange.

### Module reference

All values below are the defaults. Knob values can be click-stepped, dragged, or click-typed directly.

#### Sources (emit a signal, no inputs — one of each type per graph)

| Module | What it emits | Settings |
|---|---|---|
| **60 Hz source** | iRacing's 60 Hz steering torque (pair with the Interpolator to smooth its staircase) | — |
| **360 Hz source** | iRacing's raw 360 Hz steering torque | — |
| **LFE source** | Torque from the low-frequency-effects audio capture | Strength (35%) |
| **Soft lock source** | Opposing force past the car's steering lock | Strength (25%) |
| **Wheel velocity source** | Torque proportional to how fast the wheel is turning — a damper (for dry friction use the Friction source) | Strength (100%) |
| **Friction source** | Dry friction via a dragged stick point (the same model wheel firmwares use): the wheel holds where you leave it, and turning past the stick region gives constant sliding drag — pair with Speed gain for parked steering weight | Strength (10%), Stick region (25°) |
| **Wheel centering source** | Spring force pulling the wheel to center | Strength (85%) |

#### Generic DSP

| Module | What it does | Settings |
|---|---|---|
| **Gain** | Multiplies the signal | Gain (1.25×, range −5…+5) |
| **Compressor** | Squeezes torque above a threshold (audio-style) | Threshold (30 Nm), Knee (5 Nm), Ratio (4:1) |
| **High-pass filter** | Passes only the fast detail | Slope (Two pole), Cutoff (8 Hz) |
| **Low-pass filter** | Passes only the slow body of the force | Slope (Two pole), Cutoff (8 Hz) |
| **Slew compressor** | Squeezes the *speed* of torque changes above a threshold | Threshold (75 Nm/s), Knee (30 Nm/s), Ratio (3:1), Peak mode |
| **Slew limiter** | Hard-limits how fast torque can change | Limit (360 Nm/s) |
| **Adaptive smoother** | One Euro filter — smooths hard when the signal is calm, opens up during fast changes | Amount (0–100%) |
| **Transient enhancer** | Outputs amplified attack transients only (nonlinear detail extractor) | Cutoff (7.2 Hz), Gain (1.00×) |
| **Interpolator** | Replaces a 60 Hz signal's staircase steps with a linear ramp across each frame — pure interpolation between known samples, adds one frame (~16.7 ms) of latency. Use on 60 Hz-derived branches only | — |
| **Prediction** | Shifts the signal into the future to counteract latency — an adaptive filter bank that learns each car as you drive (see [Inside the Prediction module](#inside-the-prediction-module-the-math)) | Horizon (K6), Correction limit (5.00 Nm), Strength (150%) |

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
| **Understeer / Oversteer / Seat-of-pants force** | Increase or decrease force with the steering-effect signal | Direction (understeer: Decrease, oversteer: None, seat-of-pants: Increase), Strength (10%), Curve |

Crash protection, curb protection, and speed gain have a **test button** (vibrate icon) on their settings card so you can trigger the effect on demand.

#### Output (fixed, always last)

Converts the final Nm signal to the normalized wheel output, then applies two shapers that only make sense in normalized space:

- **Curve** — response curve bending (OFF at 0)
- **Soft limiter** — a compressor near full output instead of hard clipping: Threshold (85%), Knee (30%), Ratio (6:1), on by default

Selecting the Output node in the editor previews the **complete** wheel signal — the processed main chain plus the vibration bus.

---

## Vibration effects (generator nodes)

The wheel vibration effects are **generator** modules inside the same FFB graph. They render as standalone nodes — no input or output connectors — because they don't process the signal chain: each one feeds the shared vibration bus, which is added to the wheel output *after* the Output module's curve and limiter (vibrations never get compressed away by the limiter). The add-module picker lists them under two vibration categories.

| Module | When it vibrates | Settings |
|---|---|---|
| **Understeer vibration** | While the understeer effect is active — amplitude follows the effect strength (smoothly interpolated) | Pattern (sawtooth in), Strength (5%), Frequency (15 Hz), Curve |
| **Oversteer vibration** | While the oversteer effect is active | Pattern (sawtooth out), Strength (5%), Frequency (10 Hz), Curve |
| **Seat-of-pants vibration** | With the seat-of-pants (vertical G) effect | Pattern (triangle), Strength (5%), Frequency (12.5 Hz), Curve |
| **Shift RPM vibration** | Pulses when it's time to shift up | Strength (3%), Frequency (50 Hz), Pulse duration (60 ms) |
| **Gear change vibration** | A 100 ms buzz on every gear change | Strength (5%), Frequency (31 Hz) |
| **ABS vibration** | Pulses while ABS is active | Strength (10%), Frequency (25 Hz), Pulse duration (40 ms) |
| **Engine RPM vibration** | Whenever the engine is running — frequency tracks the RPM up to the redline, voiced like a V8; Roughness at 0% gives a pure sine | Strength (1%), Frequency at redline RPM (50 Hz), Roughness (100%) |
| **Road texture** | Rumble that speeds up with the car — most of the frequency arrives by 30–60 MPH, topping out at 180 MPH | Strength (1%), Frequency (120 Hz) |
| **Slip texture** | Rumble driven by the summed understeer + oversteer effects — either end of the car sliding | Strength (5%), Frequency (80 Hz) |

Every vibration starts at zero phase when it triggers, and the pulsed ones restart their waveform with each pulse, so each event feels identical. The understeer / oversteer / seat-of-pants nodes (and slip texture) dim with a notice when the corresponding effect is disabled on the steering effects page.

---

## Built-in vs. custom graphs

The graph dropdowns are split into two categories:

- **Built-in** — shipped inside the app. Built-ins:
  - cannot be renamed or deleted,
  - cannot be structurally changed — no adding/removing modules, no re-wiring, no moving nodes,
  - **can** have every knob/switch adjusted (and those adjustments are per-context like everything else),
  - have a **Reset** button that restores the shipped structure *and* clears your knob adjustments for it,
  - are **updated automatically** when a new app version ships an updated copy — so alpha fixes to the built-in graphs reach you on the next install without touching your custom graphs.
- **Custom** — yours. To modify a built-in's structure, press **New graph** and choose **Clone current graph**; the clone is fully editable.

Five built-in graphs ship, each one the replacement for one of the old fixed algorithms. All five share the same back half — the steering-effect forces, crash protection, parked ramp-down, interpolated low-speed wheel centering and friction branches, LFE, soft lock, the Output soft limiter, and the full set of vibration generator nodes — and differ in how the torque signal is produced and conditioned up front:

- **Marvin's easy detail adjustment** *(the flagship — the wizard tunes it, and fresh installs start on it; replaces Native 360 Hz, Detail booster, and Delta limiter)* — splits the 360 Hz torque into two branches: the slow body goes through the **Prediction module** and a low-pass filter (prediction on the body only, so no noise is amplified), while the high-pass detail branch gets its own **detail gain** (the gain the first-run wizard tunes) and curb protection.
- **Marvin's native 60 Hz** *(replaces Native 60 Hz)* — the plain 60 Hz torque with curb protection, as simple as it gets.
- **Alan Le's slew and total compression** *(replaces Slew and total compression)* — the 360 Hz torque through a **slew compressor** (75 Nm/s, 3:1 — squeezes how *fast* torque can change) and a **compressor** (30 Nm, 4:1 — squeezes total torque), then curb protection.
- **Alan Le's hybrid** *(replaces the Multi adjustment toolkit's fixed hybrid mode)* — an **adaptive blend** rides the 60 Hz torque as the anchor while letting the 360 Hz detail through (8 Hz fixed corner), followed by the toolkit's whole adjustment chain: compressor and peak-mode slew compressor (both off by default), the detail stage (7.2 Hz low-pass + transient enhancer at a neutral 1.00×), and an adaptive smoother (0%).
- **Alan Le's adaptive hybrid** *(replaces the toolkit's variable hybrid mode)* — the same chain, but the blend's corner drops from 20 Hz to 6 Hz whenever the force direction flips and eases back over 28 ms, letting transients punch through with less anchor pull; ships with gentle peak-mode slew compression (180 Nm/s, 1.5:1) and 10% adaptive smoothing switched on.

---

## Per-context values and scopes

Which graph is selected *and* all of its module values follow one context scope — right-click the graph selector's label to set it (default: per wheelbase + per car). Switch cars and your knob tweaks for that car come back; the graph structure itself is shared.

---

## Sharing graphs (export / import)

The export/import buttons sit next to the graph management buttons:

- **Export** writes the current graph to a `.mairagraph` file (a built-in exports as an ordinary editable graph).
- **Import** validates and adds the file as a new custom graph (auto-renamed if the name is taken). FFB and vibration graph files are type-tagged, so you can't import one into the other's slot. Files from a newer app version are rejected with a clear message instead of silently mangling.
- **Graphs carry an identity.** Importing a file whose graph you already have (an updated version of a shared setup, say) opens a dialog instead of blindly duplicating: apply the file's module settings to the current car/track context, to the baseline, to both — or import it as a separate copy after all.

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
- Recordings capture the full 360 Hz tick context (36 channels): torques, LFE, G forces, steering angle/velocity, yaw/pitch/roll rates, lateral acceleration and velocity, front shock velocity and deflection, throttle/brake, speed, RPM (with shift and redline RPM), gear, ABS, steering-effect signals, track position, and heading/velocity for the track map.
- The **choose recording** button picks which recording the preview uses. Recordings load on demand now (only one in memory), so having many costs nothing.
- The **beeps** are configurable on the Sounds page (enable, volume, frequency) like every other sound.

**Hover the preview** to get a two-panel popup:

1. **Zoom** — magnified view of the traces around the cursor.
2. **Data card** — every recorded value at that exact sample (time, track position, torques, steering, speed, gear, ABS, G forces, shock velocity, effect signals).

The **track map** is now a permanent panel beside the preview graph: the whole recorded segment drawn as a track outline (north up), recording start in green and end in red, with the range currently visible in the preview highlighted in orange — it tracks scrolling and zooming.

---

## Engine internals (what you should feel)

- The whole processing chain now runs at a true **360 Hz** in a single burst per telemetry frame, handed to a dedicated high-rate output thread that streams torque to the wheelbase. The Interpolator module smooths 60 Hz staircases; the Prediction module shifts the signal earlier to counteract latency.
- A performance pass eliminated steady-state memory allocations in the hot path, so there are no garbage-collection hitches — force delivery should be glassy even during long sessions.
- Wheel output writes are skipped when the value hasn't changed, cutting driver overhead while parked.

---

## Inside the Prediction module (the math)

*For the mathematically inclined — nothing here is needed to use the module.*

### The problem

Let $y_t$ be iRacing's steering torque sampled at 360 Hz. Every physical path from tire to hand adds delay — telemetry transport, FFB processing, wheelbase drivetrain — so the torque you feel lags the physics. The module's goal is to output an estimate of $y_{t+k}$ at time $t$, shifting the whole waveform $k$ ticks ($k \times 2.78$ ms) earlier. The Horizon knob is $k$, from K1 to K12 (2.8–33 ms).

Two structural facts shape the design:

1. **Torque arrives in frames.** iRacing delivers telemetry at 60 Hz, and each frame carries the six most recent 360 Hz samples at once. MAIRA processes the whole frame in a single burst the moment it arrives — so when the engine computes the output for sub-tick $i \in \{0,\dots,5\}$ of a frame, the frame's *later* samples are already known.
2. **Torque is only partially predictable.** Measured on real recordings, only roughly 40–55% of the torque *change* over a 6–12 tick horizon is linearly predictable from the past. Any estimator must make peace with that ceiling.

### Frame anchoring

Naive prediction extrapolates $k$ ticks ahead from every tick. Frame anchoring exploits fact 1: with the newest known sample at in-frame index 5 (the *anchor* $a$), the target index $i + k$ is either

- **inside the frame** ($i + k \le 5$): the "future" sample is already in hand, and the module outputs it exactly — zero estimation error; or
- **beyond the frame**: the true extrapolation depth is only $d = i + k - 5$.

At K6 the depth $d$ ranges over $\{1,\dots,6\}$ with mean 3.5 — the module delivers a 6-tick lead while only ever guessing 1–6 ticks ahead. Since prediction error grows steeply with depth, halving the average depth buys more than any cleverer estimator at full depth. Each horizon needs exactly six depths (one per sub-tick), so the module maintains a bank of six independent predictors.

### The predictor bank

Each depth $d$ gets its own linear predictor $\hat y_{a+d} = \mathbf{w}_d^\top \mathbf{x}_a$ over a 49-dimensional feature vector built at the anchor:

- **24 torque lags** $y_a, y_{a-1}, \dots, y_{a-23}$ (one 60 Hz frame's worth of 360 Hz history ×4),
- **4 auxiliary telemetry channels × 6 frame-spaced lags** (indices $a, a-6, \dots, a-30$): steering wheel angle, steering wheel velocity, chassis lateral velocity, and pitch *acceleration* (the frame difference of pitch rate), each scaled to torque-commensurate variance,
- **1 constant bias** term.

The aux channels were chosen empirically, by greedy forward selection on six car/track recordings against a torque-plus-bias baseline: steering angle enters first (front slip angle → future self-aligning torque), then pitch acceleration (road inputs hit the chassis before they appear in measured torque), then steering velocity (only valuable *after* pitch rate is in — an interaction effect), with lateral velocity marginal. Channels you might expect to help — yaw rate, per-corner shock travel — never survive once the steering angle is in the regression.

### Learning — normalized LMS

The weights adapt online. Once per frame per depth, the truth for the prediction made $d$ ticks ago has just arrived, so with $\mathbf{x}$ the feature vector at anchor $a - d$:

$$e = y_a - \mathbf{w}_d^\top \mathbf{x}, \qquad \mathbf{w}_d \leftarrow \mathbf{w}_d + \mu \, \frac{e \, \mathbf{x}}{\varepsilon + \lVert \mathbf{x} \rVert^2}, \qquad \mu = 0.25 .$$

Three deliberate choices:

- **NLMS, not RLS.** Recursive least squares with exponential forgetting is the textbook "better" adaptive filter, but on these strongly autocorrelated (and, mid-corner, nearly constant) regressors its covariance matrix winds up and the weights explode — it diverged in offline testing. NLMS is unconditionally stable for $0 < \mu < 2$ and converges within a few corners of driving.
- **One update per frame, not six.** Consecutive anchors are highly correlated; updating on all six sub-ticks increases gradient-noise misadjustment and measured *worse* offline.
- **Weights initialize to persistence** ($\hat y = y_a$), so a cold filter passes the signal through unchanged instead of outputting garbage while it learns.

### The amplitude problem — why the Strength knob exists

A least-squares predictor approximates $\mathbb{E}[\,y_{a+d} \mid \mathbf{x}_a\,]$, and conditional expectations *shrink*: as linear predictability decays with depth, the optimizer pulls its estimate toward the recent mean. The result minimizes RMS error yet feels — and looks, in the preview — almost identical to no prediction at all, because the waveform's excursions barely move. This shrinkage, not estimator quality, is why naive prediction approaches show "no visible difference."

The fix is to re-expand the learned correction. The module outputs

$$\text{out}_t = \text{in}_t + \gamma \cdot \mathrm{clamp}_{\pm L}\!\left( \hat y_{t+k} - y_t \right)$$

where $\gamma$ is the Strength knob (default 150%) and $L$ the Correction limit (default 5 Nm). With $\gamma > 1$ the correction is deliberately over-driven past its MMSE amplitude, trading a little RMS error for the full-amplitude lead you can actually feel. When the target sample is *known* (the inside-the-frame case) $\gamma$ is capped at 100% — there is nothing to re-expand about exact data. The clamp $L$ bounds the worst a mis-adapted filter can inject during transients or relearning.

### Measured behavior

An offline test bench in the repo (`MarvinsAIRARefactored.PredictionLab`) replays real 360 Hz recordings through the exact shipped algorithm and scores three things: RMS error against the true future (normalized so 1.0 = the do-nothing persistence baseline), the *achieved* waveform shift (argmin of the cross-correlation lag — the honest number, immune to amplitude tricks), and high-frequency (>30 Hz) noise gain. Across six cars/tracks at the default K6 / 150% / 5 Nm:

- ≈ 11.5 ms average true shift (worst car 8.6 ms) — versus < 1 ms for the naive predictor this module replaced;
- RMS ratio ≈ 0.82, and **below 1.0 on every car** — the shifted signal tracks the true future better than doing nothing does;
- HF gain ≈ 1.24 — mild, and the built-in graph applies prediction to the low-pass body branch only, so no high-frequency noise is amplified at all.

K12 (33 ms) is *not* cleanly achievable — the predictability ceiling bites, and on some cars the shift metric collapses — which is why the default is K6 rather than the maximum.

One caveat you will notice: every preview refresh resets the engine, so the filters relearn across the visible window — the left edge of the preview always shows a weaker effect than the steady state you feel on track.

---

## First-run wizard

The wizard's FFB style step no longer picks an algorithm. Its 7-position slider now selects **Marvin's easy detail adjustment** and sets its **detail gain**: 25% / 50% / 75% / 100% / 125% / 150% / 200%, from "Silky smooth" to "A lot of boost". You can re-run the wizard, or just turn the Gain knob in the graph yourself.

---

## Other changes vs. the released version

- Crash protection defaults changed: lateral trigger 8 g (was 6), duration 0.2 s, recovery 0.2 s.
- Curb protection now genuinely reduces force (it previously only announced); both protections ramp smoothly back via the new recovery time.
- The old Racing Wheel page sections (Algorithm, Output, Crash/Curb protection, Parked effects, Effects) and the Steering Effects page's wheel groups are gone — all of that lives in the graphs now. Steering-effect *detection* settings (thresholds, calibration) remain on the Steering Effects page.

---

## Known gaps in this alpha

- **Online documentation still describes the old UI.** The racing wheel page docs will be rewritten before general release; until then, this document is the reference.
- The old algorithm settings are still stored (dormant) in `Settings.xml` for rollback safety; they'll be removed in a later release.

## What to test / feedback

- Fresh-feel check: does the flagship graph feel right on your wheelbase and usual cars? Try the wizard slider extremes.
- **Live with the node graph hidden for a session** (the new default): drive using only the pinned quick controls and the preview. Does anything feel missing? Then flip "Show node graph" on and off a few times mid-session and report any layout oddities.
- **Pin a few settings** on a custom graph (the tiny switch on each setting in the module settings column), confirm they appear above the editor with the right module captions, stay in sync with the settings column, and survive an export/import round trip.
- **Vibration test buttons:** with the car on track, walk through every vibration node's test button — each should shake the wheel on its own, the gear change one should fire once and re-arm, and all of them should gray out the moment you leave the car.
- **Try all five built-in graphs** back to back on the same car — especially the one matching the algorithm you used on the released version. Does its graph replacement feel like the algorithm it replaces?
- **Le Mans Ultimate (if you own it):** the game bridge is newly wired into the FFB graph engine and this combination has had the least real-world time of anything in the alpha. Drive LMU with a few built-in graphs and confirm the forces, soft lock, and the engine RPM vibration all behave like they do in iRacing.
- **Graph isolation:** disable a module (say the 360 Hz source) in one graph, switch to another graph, and confirm it is NOT disabled there — then switch back and confirm your change stuck. Same for knob values on sources and the Output module.
- Prediction module: does the wheel feel more connected — less "rubber band" between what the car does and what your hands feel? Try Strength at OFF vs. 150% back to back on the same car. Report any oscillation or buzzing at high Strength/Horizon settings (and which car).
- Give the prediction a few corners after loading into a car before judging it — it learns as you drive.
- Friction feel: bump the wheel at a standstill — it should thunk and hold with zero buzz or oscillation. Try different Stick region values (small = crisp, large = softer before breakaway).
- Engine RPM vibration: turn its Strength up, play with Roughness (0% pure tone ↔ 100% V8 burble), and confirm it goes silent the moment the engine dies.
- Vibration effects in the graph: confirm each vibration node previews on its own and shows up in the Output node's preview, and that disabling understeer/oversteer on the steering effects page dims the right nodes.
- Build a custom graph: clone the built-in, add/remove/rewire modules, confirm nothing crashes and the preview matches what you feel.
- Per-car tuning: tweak knobs on two different cars and confirm each car's values come back when you switch.
- Record laps at different tracks — verify the auto-stop on lap completion and the track map's shape.
- Bind a couple of module knobs to buttons and adjust while driving.
- Export a graph, send it to another tester, and have them import it.

Report anything odd (with your `Logs` folder from `Documents\MarvinsAIRA Refactored`) through the usual channels. Thank you for testing!
