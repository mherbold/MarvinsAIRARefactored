# Speech-to-Text

## Related Source Files
- `Components/SpeechToText.cs` — Chrome-based Web Speech API bridge component
- `Classes/ChromeLauncher.cs` — Launches a headless Chrome/Edge process
- `Classes/ChromeSTTBridge.cs` — WebSocket/pipe bridge between Chrome and the app
- `Components/ChatQueue.cs` — Queues recognized speech for downstream processing
- `Pages/SpeechToTextPage.xaml/.cs` — STT settings UI
- `Windows/SpeechToTextWindow.xaml/.cs` — Floating transcript overlay window

---

> **ADMINBOXX builds**: Speech-to-text is entirely compiled out when the `ADMINBOXX` preprocessor constant is defined.

---

## Architecture Overview

MAIRA does **not** use the Windows built-in speech APIs. Instead it bridges the **Web Speech API** in Chromium:

```
iRacing session
	└─ SpeechToText component
		   └─ ChromeLauncher  → launches Edge/Chrome with a local HTML page
		   └─ ChromeSTTBridge → communicates with the page via WebView2 / named pipe
				└─ recognized text → ChatQueue → downstream consumers
```

1. `ChromeLauncher` starts a Microsoft Edge (or Chrome) process pointed at a local HTML file from `My Documents\MarvinsAIRA Refactored\STT\`.
2. The HTML page activates `webkitSpeechRecognition` and forwards results over a WebSocket or named pipe.
3. `ChromeSTTBridge` receives the text and pushes it onto `ChatQueue`.
4. `SpeechToText` manages the lifecycle (start, stop, restart on error) and exposes a clean API to the rest of the app.

`Microsoft.Web.WebView2` (v1.0.3800.47) is used for the embedded browser bridge where a separate process is not preferred.

---

## STT Asset Files

HTML/JS assets live in `My Documents\MarvinsAIRA Refactored\STT\` and are deployed by the post-build `xcopy` step. These files are **not** embedded resources — they are edited-in-place to tweak recognition behavior without recompiling.

When modifying STT assets:
- Keep the WebSocket/pipe message format in sync with `ChromeSTTBridge`.
- Test with both Edge and Chrome if supporting both.

---

## Chat Queue (`ChatQueue`)

`ChatQueue` is a thread-safe queue of recognized utterances. Downstream components (overlays, future automation) dequeue from it on the MAIRA worker thread.

---

## Floating Transcript Window

`SpeechToTextWindow` is an always-on-top transparent overlay that shows the last N recognized phrases. It is managed by `TopLevelWindow` like all other overlays. Position and opacity are user-configurable and persisted in settings.
