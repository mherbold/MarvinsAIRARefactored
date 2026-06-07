# Copilot Instructions

## Project Rules

- `AGENTS.md` is the authoritative source for this project. **Always read it first.**
- Only use the editor's built-in edit/apply tools (`replace_string_in_file`, `multi_replace_string_in_file`, `create_file`) to modify files.
- Do not use PowerShell, terminal commands, or shell scripts to write file content under any circumstances. If an edit cannot be applied directly, explain why and stop.

## Quick Rules (apply to all tasks)

- **`var`** everywhere possible in C#; descriptive variable names.
- **Localize** all UI strings — never hardcode labels or units; use `Localization["Key"]`.
- **Custom controls only** — never use raw WPF `TextBox`, `ComboBox`, `Button`, `CheckBox`, `Slider`, `GroupBox`, or `TabItem` when a `Maira*` equivalent exists.
- **Settings ordering** — properties in `Settings.cs` and `ContextSettings.cs` must match UI top-to-bottom / left-to-right order.
- **XAML files** must be written without BOM (UTF-8 no BOM).

## SpeechToText Guidelines

- Preserve a streaming model: send audio only while radio transmission is active, keep a short post-transmit tail, and update the UI with partial and final transcript text.
