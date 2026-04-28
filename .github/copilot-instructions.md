# Copilot Instructions

## Project Guidelines
- When doing bulk string replacements in XAML or resx files that contain non-ASCII Unicode characters (e.g., Català, Français, Čestina, Русский, 简体中文), always use [System.IO.File]::ReadAllText / .Replace() / [System.IO.File]::WriteAllText with [System.Text.UTF8Encoding]::new($false) in PowerShell. Never use Get-Content/Set-Content without explicit encoding — it silently corrupts non-ASCII characters. Never use replace_string_in_file for bulk multi-occurrence substitutions across Unicode-containing files.