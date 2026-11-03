# Sutra Flow

Sutra Flow is push-to-talk dictation built into Sutra Island. Hold **Right Ctrl**, speak, and let go. The text is transcribed on your PC, tidied up, and pasted wherever your cursor is.

## Using it

1. Click into any text field: a document, a chat box, a browser address bar.
2. Hold **Right Ctrl** and talk. After about a quarter of a second the island's content dissolves into a dot grid, and the columns move with your voice.
3. Let go. The dots flatten into a row, shimmer while the text is polished, and draw a green check when it's pasted.

A quick tap of Right Ctrl does nothing, and Right Ctrl with another key (Ctrl+C, Ctrl+V) works as a normal shortcut and cancels the dictation.

### Island states

| What you see | Meaning |
| --- | --- |
| Dot columns rippling from the center | Listening |
| Dots collapse into one row | Key released, recording stopped |
| Diagonal shimmer | Transcribing and cleaning up |
| Green check | Pasted |
| Red X | Something went wrong; a Windows notification says why |

## Settings

**Settings → Sutra Flow**

- **Push-to-talk dictation:** turns Flow on or off. On by default.
- **Cleanup:**
  - *Off* pastes exactly what Whisper heard.
  - *Basic* (default) runs on your PC: removes "um" and "uh", collapses stutters ("the the"), turns spoken "comma", "period", "question mark", "new line", and "new paragraph" into punctuation, handles "scratch that", and fixes capitals.
  - *AI* runs Basic first, then asks the provider and model selected in Sutra Assistant to resolve self-corrections ("Monday, no wait, Tuesday") and spoken lists. If the provider is slow (over 8 seconds), errors, or rewrites too much, Flow falls back to the Basic text. Codex is not used for cleanup.
- **Vocabulary:** comma-separated names and terms (for example `Sutra, Tanuj, DCCC`). Whisper and AI cleanup use them as the spelling to match.
- **Speech model:** status of the Whisper model, with a Download button if it is missing.
- **Usage:** total words dictated.

## Speech model

Flow uses Whisper `base.en` (about 148 MB) through whisper.cpp. It is not bundled in the exe. It downloads once, automatically, the first time Island starts with Flow turned on, into:

```
%LOCALAPPDATA%\DynamicIsland.Windows\SutraFlow\models\ggml-base.en.bin
```

The whisper.cpp native libraries are embedded in the exe and extracted on first use to `%LOCALAPPDATA%\DynamicIsland.Windows\SutraFlow\runtime\<version>\`. Expect about one to two seconds of transcription for a short sentence on a typical CPU.

## Privacy

- Audio never leaves your PC. Recording starts only while Right Ctrl is held and stops when you let go. Recordings are kept in memory and discarded after transcription.
- With AI cleanup, only the transcribed text is sent to the provider you chose in Sutra Assistant, using the key already saved there.
- Pasting briefly puts the text on the clipboard. It is marked to stay out of Windows clipboard history (Win+V) and cloud clipboard, and your previous clipboard text is restored about half a second later. Images or files on the clipboard are not restored.
- While Flow is recording, Island hides itself from its own microphone privacy indicator, since the dot grid already shows that the mic is on. Windows' own mic indicator still appears.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Notification: "Speech model downloading" | Wait for the download to finish (progress in Settings → Sutra Flow). |
| Notification: "Install the Microsoft Visual C++ Redistributable (x64)" | whisper.cpp needs the Visual C++ 2015–2022 runtime, including `vcomp140.dll`. Install the x64 redistributable from Microsoft and restart Island. |
| Notification: "Microphone access blocked" or "No microphone found" | Windows Settings → Privacy & security → Microphone: allow desktop apps. Check the default input device. |
| Red X with no notification | The recording was too short or too quiet. Hold the key a little longer and speak up. |
| Nothing pastes into an admin window | Windows blocks input from a normal app into an elevated one. Run Island as administrator, or dictate into non-elevated apps. |
| Notification: "Couldn't paste; text is on the clipboard" | Another app had the clipboard locked. Press Ctrl+V yourself. |

Logs are in `%LOCALAPPDATA%\DynamicIsland.Windows\logs`, with lines starting `Sutra Flow:`.

## How it works

```
Right Ctrl held ─► FlowHotkeyHook ─► FlowRecorder (16 kHz mono, NAudio WinMM)
Right Ctrl up   ─► WhisperEngine (whisper.cpp) ─► FlowTextCleaner (Basic)
                ─► FlowAiCleaner (optional, Q provider) ─► FlowTextInjector (clipboard + Ctrl+V)
FlowController publishes FlowStatus ─► IslandWindow.Flow / FlowDotMatrixView
```

| File | Role |
| --- | --- |
| `Services/Flow/FlowController.cs` | State machine: hold threshold, recording, transcription, cleanup, paste, status. |
| `Services/Flow/FlowHotkeyHook.cs` | Low-level keyboard hook on its own thread with its own message loop. The callback only queues events to a serial worker, so CPU-heavy transcription can't push it past Windows' `LowLevelHooksTimeout` (which silently removes slow hooks). Keys are never swallowed; injected keys are ignored. |
| `Services/Flow/FlowRecorder.cs` | Microphone capture and the smoothed loudness level that drives the dots. |
| `Services/Flow/WhisperEngine.cs` | Model download, native library extraction, transcription. |
| `Services/Flow/FlowTextCleaner.cs` | Basic cleanup rules and the safety check for AI output. Unit-tested in `FlowTextCleanerTests`. |
| `Services/Flow/FlowAiCleaner.cs` | AI cleanup prompt and provider call. |
| `Services/Flow/FlowTextInjector.cs` | Clipboard write with history exclusion, `SendInput` Ctrl+V, clipboard restore. |
| `Views/FlowDotMatrixView.cs` | The dot grid. Draws only while visible. |
| `Views/IslandWindow.Flow.cs` | Shows and hides the overlay with the island's standard motion. |
| `ViewModels/FlowSettingsViewModel.cs` | Settings → Sutra Flow. |

Settings live in `settings.json` as `FlowEnabled`, `FlowCleanup`, `FlowVocabulary`, and `FlowWordsDictated`.

## Not yet included

- Hands-free (toggle) mode.
- A choice of push-to-talk key.
- Switching Sutra Assistant's voice input from Windows speech to Whisper.
- A command palette entry.

## Credits

Inspired by [WhimprFlow](https://github.com/Blueturboguy07/WhimprFlow) (MIT). Flow's AI cleanup instructions and examples are adapted from WhimprFlow's cleanup prompt; the rest is a native C# implementation. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
