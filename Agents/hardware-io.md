# Hardware I/O

## Related Source Files
- `Components/AdminBoxx.cs` — USB LED button box hardware
- `Components/Wind.cs` — USB twin-fan (or quad-fan) wind simulator
- `Components/SeatBeltTensioner.cs` — USB seat belt tensioner
- `Components/VirtualJoystick.cs` — vJoy virtual joystick output
- `Components/StreamDeck.cs` — Elgato Stream Deck integration
- `Components/HidHotPlugMonitor.cs` — USB hot-plug detection
- `Classes/UsbSerialPortHelper.cs` — USB serial port abstraction (shared by AdminBoxx, Wind, SBT)
- `Classes/ButtonMappings.cs` — Input button mapping logic
- `Classes/LogitechGSDK.cs` — Logitech G-SDK wheel LED support
- `Pages/AdminBoxxPage.xaml/.cs` — AdminBoxx UI
- `Pages/WindPage.xaml/.cs` — Wind simulator UI
- `Pages/SeatBeltTensionerPage.xaml/.cs` — SBT UI
- `Arduino/Wind/Wind.ino` — Arduino sketch for the wind simulator firmware

---

## USB Serial Architecture

All custom MAIRA hardware communicates over **USB serial ports**, abstracted by `UsbSerialPortHelper`. This class handles:
- Port discovery by USB vendor/product ID or by USB product name string.
- Connection, disconnection, and reconnection on hot-plug events.
- Framed message send/receive.

`HidHotPlugMonitor` listens for Windows `WM_DEVICECHANGE` messages and notifies all hardware components when a USB device is added or removed so they can attempt reconnection.

---

## AdminBoxx

An **8 × 4 RGB LED button box** built on the Adafruit ItsyBitsy M4 microcontroller.

| Property | Value |
|---|---|
| USB VID | `0x239A` |
| USB PID | `0x80F2` |
| Communication | USB serial (via `UsbSerialPortHelper`) |

- Each button has an individually addressable RGB LED.
- Buttons are exposed to the rest of the app via the DirectInput button-mapping system using a **fake device GUID**.
- LED color and blink patterns are updated from the MAIRA worker thread.
- Disabled when the `ADMINBOXX` preprocessor constant is **not** defined; the AdminBoxx build configuration is a separate stripped-down app.

---

## Wind Simulator

A dual-fan (or quad-fan) wind simulator driven by an Arduino.

| Property | Value |
|---|---|
| USB identification | Product name string `"MAIRA WIND"` |
| Communication | USB serial (via `UsbSerialPortHelper`) |
| Arduino sketch | `Arduino/Wind/Wind.ino` |

- Fan speed is proportional to the car's speed or a configurable wind effect.
- Supports both a 2-fan and 4-fan configuration — the firmware auto-detects at startup.
- The component sends a simple speed byte (0–255) per fan over serial.

---

## Seat Belt Tensioner (SBT)

A USB-controlled seat belt tensioner providing braking haptic feedback.

| Property | Value |
|---|---|
| USB identification | Product name string `"MAIRA SBT"` |
| Communication | USB serial (via `UsbSerialPortHelper`) |
| Asset files | `My Documents\MarvinsAIRA Refactored\SBT\` |

- Tensioner force is derived from brake pressure / deceleration telemetry.
- SBT asset files (waveform definitions) are copied to the documents folder by the post-build step.

---

## vJoy Virtual Joystick

`VirtualJoystick` outputs axis and button data to a **vJoy virtual joystick device**, allowing MAIRA to feed processed signals into other sim software.

- Requires the vJoy driver to be installed separately.
- Uses `vJoyInterfaceWrap.dll` (local DLL reference).
- Configured via the DirectInput button-mapping system.

---

## Elgato Stream Deck

`StreamDeck` integrates the Elgato Stream Deck as a mappable input/output device:
- Uses `OpenMacroBoard.SDK` + `StreamDeckSharp` NuGet packages.
- Registered as a **fake DirectInput device** with a fixed GUID so it participates in the standard button-mapping UI alongside real hardware.
- Button images and labels are updated from the MAIRA worker thread whenever state changes.

---

## Logitech Wheel LEDs

`LogitechGSDK.cs` wraps the Logitech G-SDK (`LogitechSteeringWheelEnginesWrapper.dll`) for RPM LED bar control on Logitech wheels. It is called from the multimedia timer thread.

---

## Button Mapping System

`ButtonMappings` tracks which physical button (on any recognized device — real DirectInput device, Stream Deck, or AdminBoxx) is mapped to which MAIRA action.

- Mappings are stored in `Settings.xml` and serialized with the rest of the settings.
- `UpdateButtonMappingsWindow` (in `Windows/`) is the UI for configuring mappings.
- When a button is pressed during the mapping capture flow, `DirectInput` fires an event that `ButtonMappings` listens to and records.

---

## Hot-Plug Detection

`HidHotPlugMonitor` registers a hidden window to receive `WM_DEVICECHANGE` notifications from Windows. On device arrival or removal it invokes a callback that allows `AdminBoxx`, `Wind`, `SeatBeltTensioner`, and `StreamDeck` to attempt reconnection or clean up their handle.
