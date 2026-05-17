# Why "Pass FFB through TF4ALL" is built this way

This explains the design behind the `RacingWheelPassFFBThroughTF4ALL`
option, for reviewers and future maintainers.

## The problem

On the Logitech G PRO, the rim rev/shift LEDs and PID force feedback
both reach the wheel over the same HID++ control path. In testing,
driving the rev LEDs while MAIRA's PID force feedback was active made
the wheel drop force feedback for roughly 1 to 1.5 seconds at a time
(reproduced and measured with USB captures). We could not
remove it by changing update rate, batching, or threading; it behaves
like a device-level limit on that shared path. In short, the rev
lights and PID force feedback could not both run on that path at once.

## The fix: deliver the force as Trueforce, not as PID

The wheel also has a separate Trueforce endpoint, independent of that
HID++ control path. The key point: to get the rev lights and force
feedback working together, MAIRA's force has to reach the wheel
through the Trueforce stream rather than as PID on the shared path.

When passthrough is enabled:

- MAIRA computes its force exactly as before, but no longer sends it
  to the wheel as PID. Its DirectInput force is pinned to zero and the
  force value is written into a small shared memory file
  (`Local\TF4ALL_MAIRA_FFB_v1`, force value only).
- TF4ALL reads that value, renders it as Logitech Trueforce on the
  Trueforce endpoint, and drives the rev/shift LEDs from its own
  telemetry.

With the force delivered as Trueforce instead of as PID, the rev
lights and force feedback run together. Confirmed on hardware: no
dropouts.

MAIRA stays the source of truth for force; only the delivery path
changes, and only when the user opts in.

## Why a standalone Trueforce implementation is required

A fair question is why MAIRA does not just use iRacing's own native
Trueforce. A MAIRA setup already has the user disable iRacing's force
feedback, so FFB itself is not the conflict. The conflict is the
Trueforce endpoint. Delivering MAIRA's force means writing it into the
Trueforce endpoint. If iRacing's native Trueforce is also enabled,
iRacing is writing that same endpoint at the same time. Two programs
writing one endpoint is what produces the 1 to 1.5 second FFB
dropouts.

So one program has to own the Trueforce endpoint and write both the
Trueforce signal and the force into it. iRacing's native Trueforce
stays off, and a standalone Trueforce implementation owns the endpoint
with MAIRA's force fed into it.

## Why TF4ALL does it (not MAIRA)

MAIRA could build its own Trueforce synthesizer. TF4ALL already has a
customizable Trueforce effects synthesizer, built and validated on
hardware. A tiny shared-memory contract (one float, plus a sequence
for tear-free reads) is far less work and risk than MAIRA duplicating
the entire Trueforce stack, and keeps responsibilities clean: MAIRA
does force, TF4ALL does Trueforce delivery and the LEDs.

## Scope and safety

- **Default off, opt-in.** The new switch ships off. With it off,
  MAIRA sends PID FFB to the wheel exactly as it does today; nothing
  changes for anyone who does not enable passthrough.
- **Only changes behavior when passthrough is enabled.** MAIRA's force
  calculation is unchanged; only the delivery path moves. Turning the
  switch back off restores normal PID output, no restart needed.
- **Requires TF4ALL running.** If passthrough is on but TF4ALL is not
  running, there is simply no force feedback until the user starts
  TF4ALL or turns passthrough off. It cannot damage the wheel or MAIRA
  state.
- **iRacing only.** TF4ALL only consumes the shared memory while
  iRacing is the active game; the link is inert in other titles.
- **The old "Enable Logitech RPM lights" option was removed.** In this
  design TF4ALL drives the rev LEDs; a separate MAIRA-side LED path
  would conflict with that, so it was removed.
- **Wheel coverage.** Validated on the Logitech G PRO. The RS50 and
  G923 use the same Trueforce wire protocol and are expected to behave
  the same way, but their rev-LED behavior has not yet been
  hardware-validated.

The byte-level protocol details and capture methodology live in the
TF4ALL project docs for anyone who wants the full detail.
