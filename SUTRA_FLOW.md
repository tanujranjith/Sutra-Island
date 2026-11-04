# Sutra Flow

Sutra Flow is push-to-talk dictation built into Sutra Island. Hold **Right Ctrl**, speak, and let go. The text is transcribed on your PC, tidied up, and pasted wherever your cursor is.

## Using it

1. Click into any text field: a document, a chat box, a browser address bar.
2. Hold **Right Ctrl** and talk. After about a quarter of a second the island's content dissolves into a dot grid, and the columns move with your voice.
3. Let go. The dots flatten into a row, shimmer while the text is polished, and draw a green check when it's pasted.

A quick tap of Right Ctrl does nothing. Pressing another key within 0.3 seconds (Right Ctrl+C, Right Ctrl+V) works as a normal shortcut and cancels the dictation. After that the dictation locks: other keys are ignored and never reach the app, so an accidental arrow key can't move your cursor or stop you. Press **Esc** to cancel.

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
  - *Basic* (default) runs on your PC: removes "um" and "uh", collapses stutters ("the the"), turns spoken "comma", "period", "question mark", "new line", and "new paragraph" into punctuation, handles "scratch that", and fixes capitals. It does not resolve changes of mind like "tomato soup... actually no, chicken parmesan"; that needs AI.
  - *AI* runs Basic first, then asks the provider and model chosen under **AI cleanup provider**, **AI cleanup model**, and **AI cleanup reasoning** (by default, the same ones as Sutra Assistant) to resolve self-corrections ("Monday, no wait, Tuesday", or a whole restarted sentence) and spoken lists. If the provider is slow (over 8 seconds) or errors, or if the result looks unfaithful (much longer, much shorter without a spoken correction, or using words you didn't say), Flow falls back to the Basic text. Codex is not used for cleanup.
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
- Text only goes where you were dictating. The window, focused control, and caret must match at key-down, key-up, and right before the paste, and so must the focused field or browser tab wherever Windows UI Automation can read it. If anything moved, nothing is typed.
- When Flow can't paste safely, your clipboard is left alone. The text is held in memory for two minutes: click the notification, or use **Copy** under Settings → Sutra Flow → Last undelivered dictation. It is never saved to disk.
- Known limits: apps that draw their own caret and don't expose UI Automation can only be checked by window and focused control, so switching fields inside such an app can't be detected. In apps like Chrome, the very first dictation after launch can report "Focus moved" while accessibility wakes up; the text is held for you to copy.
- Pasting briefly puts the text on the clipboard. It is marked to stay out of Windows clipboard history (Win+V) and cloud clipboard. About half a second later your previous clipboard is put back, including images, rich text, and copied files. If you or another app copy something in that half second, Flow leaves it alone instead of overwriting it. If the clipboard holds something Flow can't save and restore exactly, Flow doesn't touch the clipboard and types single-line text as keystrokes instead. It never types Enter, so it can't send a message or submit a form; multi-line text is held for you to copy. If Windows stops the typing partway, only the part that wasn't typed is held, so copying it never repeats what's already in the field.
- Turning Flow off in Settings stops a recording and abandons any dictation still being transcribed or cleaned up; nothing is pasted.
- While Flow is recording, Island hides itself from its own microphone privacy indicator, since the dot grid already shows that the mic is on. Windows' own mic indicator still appears.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Notification: "Speech model downloading" | Wait for the download to finish (progress in Settings → Sutra Flow). |
| Notification: "Install the Microsoft Visual C++ Redistributable (x64)" | whisper.cpp needs the Visual C++ 2015–2022 runtime, including `vcomp140.dll`. Install the x64 redistributable from Microsoft and restart Island. |
| Notification: "Microphone access blocked" or "No microphone found" | Windows Settings → Privacy & security → Microphone: allow desktop apps. Check the default input device. |
| Red X with no notification | The recording was too short or too quiet. Hold the key a little longer and speak up. |
| Notification: "Didn't catch that" | Whisper heard nothing, or only a stock phrase like "Thank you" in a recording with almost no speech (a known Whisper habit on silence). A clearly spoken "Thank you" is pasted normally. |
| Notification: "Focus moved, so nothing was pasted" | You switched windows, tabs, or fields while Flow was working. Click where you want the text, click the notification to copy it, and press Ctrl+V. |
| Notification: "Can't paste into an admin window" | Windows blocks typing from a normal app into one running as administrator. Click the notification to copy the text and paste it yourself, or run Island as administrator. |
| Notification: "Windows blocked the paste" | Windows refused the keystrokes (for example on the lock screen or a secure desktop). Click the notification to copy the text. |
| Notification: "Your clipboard couldn't be saved, so multi-line text wasn't typed" | The clipboard held something Flow can't restore exactly, and typing line breaks could send or submit. Click the notification to copy the text. |
| Notification: "Couldn't paste; the clipboard is busy" | Another app kept the clipboard locked. Click the notification to copy the text once it frees up. |

Logs are in `%LOCALAPPDATA%\DynamicIsland.Windows\logs`, with lines starting `Sutra Flow:`.

## How it works

```
Right Ctrl held ─► FlowHotkeyHook ─► FlowRecorder (16 kHz mono, NAudio WinMM)
Right Ctrl up   ─► WhisperEngine (whisper.cpp) ─► FlowTextCleaner (Basic)
                ─► FlowAiCleaner (optional, Q provider) ─► FlowTextInjector (clipboard + Ctrl+V)
FlowPipeline runs one dictation at a time and publishes FlowStatus ─► IslandWindow.Flow / FlowDotMatrixView
```

| File | Role |
| --- | --- |
| `Services/Flow/FlowPipeline.cs` | The dictation lifecycle with no Windows dependencies: one session at a time (a new press is refused until the previous one finishes), a cancellation token per session that turning Flow off cancels, stage-by-stage checks, paste outcomes, settle timing. Unit-tested in `FlowPipelineTests`. |
| `Services/Flow/FlowController.cs` | Wires the hook, recorder, Whisper, cleanup, and paster into the pipeline; marshals status to the UI. |
| `Services/Flow/FlowHotkeyHook.cs` | Low-level keyboard hook on its own thread with its own message loop. The callback only queues events to a serial worker, so CPU-heavy transcription can't push it past Windows' `LowLevelHooksTimeout` (which silently removes slow hooks). Keys are never swallowed; injected keys are ignored. |
| `Services/Flow/FlowRecorder.cs` | Microphone capture and the smoothed loudness level that drives the dots. Each recording owns its device and samples; on release it records a 150 ms tail, then keeps every buffer until Windows reports the device stopped, so the last word isn't clipped. |
| `Services/Flow/WhisperEngine.cs` | Model download, native library extraction, transcription. |
| `Services/Flow/FlowTextCleaner.cs` | Basic cleanup rules and the safety check for AI output. Unit-tested in `FlowTextCleanerTests`. |
| `Services/Flow/FlowAiCleaner.cs` | AI cleanup prompt and provider call. |
| `Services/Flow/FlowTextInjector.cs` | Checks focus (window, focused child window, UI Automation element) and elevation, writes the clipboard with history exclusion, sends Ctrl+V and verifies Windows accepted it, then restores the previous clipboard only if its sequence number is unchanged. Falls back to Unicode typing (single-line only, no control keys) when the clipboard can't be fully restored. Never changes the clipboard on failure. |
| `Views/FlowDotMatrixView.cs` | The dot grid. Draws only while visible. |
| `Views/IslandWindow.Flow.cs` | Shows and hides the overlay with the island's standard motion. |
| `ViewModels/FlowSettingsViewModel.cs` | Settings → Sutra Flow. |

## Tests

`FlowPipelineTests` covers the lifecycle with fake audio and paste: rapid presses during processing, disabling mid-dictation, focus changes (window, caret, field, tab), blocked and elevated pastes, text handed back on failure, shortcut cancel, settle, and the "Thank you" rule. `FlowKeyGateTests` covers the shortcut window, the lock, and Esc. `FlowTextCleanerTests` covers Basic cleanup and the AI safety check. The CI workflow runs them on every pull request. The hook, microphone, Whisper, and clipboard code still need a manual check on Windows.

Settings live in `settings.json` as `FlowEnabled`, `FlowCleanup`, `FlowAiProvider` (empty means use the Assistant's), `FlowAiModel` (empty means the Assistant's model with the same provider, otherwise the provider's default), `FlowAiEffort` (default `low`; falls back to `auto` when the model doesn't support it), `FlowVocabulary`, and `FlowWordsDictated`. Keys come from the Sutra Assistant key store.

## Not yet included

- Hands-free (toggle) mode.
- A choice of push-to-talk key.
- Switching Sutra Assistant's voice input from Windows speech to Whisper.
- A command palette entry.

## Credits

Inspired by [WhimprFlow](https://github.com/Blueturboguy07/WhimprFlow) (MIT). Flow's AI cleanup instructions and examples are adapted from WhimprFlow's cleanup prompt; the rest is a native C# implementation. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).


## Lowering other audio while listening

When **Lower other audio while listening** is on (the default), Flow turns other apps down in Windows' per-app volume mixer while you hold Right Ctrl. It brings them back once the recording's tail has been captured. The master volume is never touched.

- Level: Mute, 10%, 20% (default), 30%, or 50% of each app's own volume (`FlowDuckAudio`, `FlowDuckPercent`).
- Only sessions that are playing are lowered. Sutra Island's own audio and Windows system sounds are left alone.
- A session is restored only if it's still at the volume Flow set, so a change you make mid-dictation is kept.
- The volumes Flow lowered are written to `%LOCALAPPDATA%\DynamicIsland.Windows\SutraFlow\ducked.json` until they're restored. If the island exits unexpectedly, the next launch restores them.
- The rules are in `FlowDucking` (pure, covered by `FlowDuckingTests`). The Core Audio work is in `FlowAudioDucker`, on a background queue with a 120 ms fade.
