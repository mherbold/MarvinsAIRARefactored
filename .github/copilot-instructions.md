# Copilot Instructions

## Project Rules

- `AGENTS.md` is the authoritative source for this project. **Always read it first.**
- Only use the editor's built-in edit/apply tools (`replace_string_in_file`, `multi_replace_string_in_file`, `create_file`) to modify files. Do not use PowerShell, terminal commands, or shell scripts to write file content under any circumstances. If an edit cannot be applied directly, explain why and stop.

## Agent Sub-Files

`AGENTS.md` contains a **Sub-File Index**. For any task, load the relevant sub-file(s) before making changes:

| Task area | Sub-file to load |
|---|---|
| Force feedback, FFB algorithms, LFE, multimedia timer | `docs/agents/force-feedback.md` |
| AdminBoxx, Wind, SeatBeltTensioner, vJoy, StreamDeck, hot-plug | `docs/agents/hardware-io.md` |
| iRacing SDK, telemetry, memory-mapped IPC, drivers | `docs/agents/simulator-iracing.md` |
| Audio devices, sounds, CachedSound, XAudio2 | `docs/agents/audio-sounds.md` |
| Speech-to-text, Chrome bridge, WebView2 | `docs/agents/speech-to-text.md` |
| ElevenLabs TTS, voice slots, Commentary, phrase templates, API key | `docs/agents/text-to-speech.md` |
| Settings, per-context system, ContextSwitches, ContextSettings | `docs/agents/settings-context.md` |
| WPF controls, XAML patterns, Maira* controls, dialogs, artwork | `docs/agents/ui-wpf-controls.md` |
| Localization, .resx files, adding strings or languages | `docs/agents/localization.md` |

## Quick Rules (apply to all tasks)

- **`var`** everywhere possible in C#; descriptive variable names.
- **Localize** all UI strings — never hardcode labels or units; use `Localization["Key"]`.
- **Custom controls only** — never use raw WPF `TextBox`, `ComboBox`, `Button`, `CheckBox`, `Slider`, `GroupBox`, or `TabItem` when a `Maira*` equivalent exists (see `docs/agents/ui-wpf-controls.md`).
- **Settings ordering** — properties in `Settings.cs` and `ContextSettings.cs` must match UI top-to-bottom / left-to-right order.
- **XAML files** must be written without BOM (UTF-8 no BOM).
- **`KeyEventArgs` alias** — add `using KeyEventArgs = System.Windows.Input.KeyEventArgs;` in any `.xaml.cs` that handles keyboard events.
