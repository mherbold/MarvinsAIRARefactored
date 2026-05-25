# Copilot Instructions

## Project Rules

- `AGENTS.md` is the authoritative source for this project. **Always read it first.**
- Only use the editor's built-in edit/apply tools (`replace_string_in_file`, `multi_replace_string_in_file`, `create_file`) to modify files. Do not use PowerShell, terminal commands, or shell scripts to write file content under any circumstances. If an edit cannot be applied directly, explain why and stop.

## Agent Sub-Files

`AGENTS.md` contains a **Sub-File Index**. For any task, load the relevant sub-file(s) before making changes:

| Task area | Sub-file to load |
|---|---|
| Force feedback, FFB algorithms, LFE, multimedia timer | `Agents/force-feedback.md` |
| AdminBoxx, Wind, SeatBeltTensioner, vJoy, StreamDeck, hot-plug | `Agents/hardware-io.md` |
| iRacing SDK, telemetry, memory-mapped IPC, drivers | `Agents/simulator-iracing.md` |
| Audio devices, sounds, CachedSound, XAudio2 | `Agents/audio-sounds.md` |
| Speech-to-text, Chrome bridge, WebView2 | `Agents/speech-to-text.md` |
| ElevenLabs TTS, voice slots, Commentary, phrase templates, API key | `Agents/text-to-speech.md` |
| Settings, per-context system, ContextSwitches, ContextSettings | `Agents/settings-context.md` |
| WPF controls, XAML patterns, Maira* controls, dialogs, artwork | `Agents/ui-wpf-controls.md` |
| Localization, .resx files, adding strings or languages | `Agents/localization.md` |

## Quick Rules (apply to all tasks)

- **`var`** everywhere possible in C#; descriptive variable names.
- **Localize** all UI strings — never hardcode labels or units; use `Localization["Key"]`.
- **Custom controls only** — never use raw WPF `TextBox`, `ComboBox`, `Button`, `CheckBox`, `Slider`, `GroupBox`, or `TabItem` when a `Maira*` equivalent exists (see `Agents/ui-wpf-controls.md`).
- **Settings ordering** — properties in `Settings.cs` and `ContextSettings.cs` must match UI top-to-bottom / left-to-right order.
- **XAML files** must be written without BOM (UTF-8 no BOM).
- **`KeyEventArgs` alias** — add `using KeyEventArgs = System.Windows.Input.KeyEventArgs;` in any `.xaml.cs` that handles keyboard events.
- **File Encoding** — Always use explicit UTF-8 encoding when writing files via PowerShell. Use `[System.IO.File]::WriteAllText($path, $content, [System.Text.Encoding]::UTF8)` or `Set-Content -Encoding UTF8` — never use bare `Set-Content` without an `-Encoding` parameter, as it defaults to the system codepage and corrupts non-ASCII characters.
