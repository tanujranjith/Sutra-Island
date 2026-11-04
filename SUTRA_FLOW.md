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

Model files are downloaded from a fixed upstream revision and checked against pinned SHA-256 digests before use. If verification fails, Flow asks for a fresh download.

The whisper.cpp native libraries are embedded in the exe and extracted on first use to `%LOCALAPPDATA%\DynamicIsland.Windows\SutraFlow\runtime\<version>\`. Expect about one to two seconds of transcription for a short sentence on a typical CPU.

## Privacy

- Audio never leaves your PC. Recording starts only while Right Ctrl is held and stops when you let go. Recordings are kept in memory and discarded after transcription.
- With AI cleanup, the transcript, cleanup instructions and up to 60 vocabulary entries are sent to the selected provider using its configured key. The code-editor name is also included when code-aware cleanup is active. Local basic cleanup does not send that request.
- Auto-add corrected words is off for new settings. If enabled, it reads the original non-password field through Windows accessibility after dictation, skips fields over 20,000 characters, compares later edits, and saves learned words to local vocabulary. Vocabulary may be included in AI cleanup requests. Existing explicit settings are preserved.
- Flow captures the window and caret at key-down, then checks them again at key-up and before pasting. It also requests a Windows UI Automation field ID asynchronously at each point. If focus changes or a field cannot be verified, it holds the text for you to copy. The asynchronous field-ID read cannot completely rule out a very fast same-window focus change with indistinguishable native focus and caret readings.
- When Flow can't paste safely, your clipboard is left alone. The text is held in memory for two minutes: click the notification, or use **Copy** under Settings → Sutra Flow → Last undelivered dictation. It is never saved to disk.
- Known limits: apps that do not expose a stable focused field through UI Automation cannot be verified safely; dictation is held for you to copy instead of being pasted automatically. In apps like Chrome, the first dictation after launch may be held while accessibility wakes up.
- Pasting briefly puts the text on the clipboard. It is marked to stay out of Windows clipboard history (Win+V) and cloud clipboard. About half a second later Flow attempts to restore the captured clipboard formats, including supported images, rich text, copied files, and privacy markers. Restore is best-effort: a locked clipboard can prevent it, and a small race remains between checking the clipboard and changing it. If a change is detected after Flow writes its text, Flow leaves the newer clipboard alone. If the clipboard holds something Flow can't save and restore exactly, Flow doesn't touch the clipboard and types single-line text as keystrokes instead. It never types Enter, so it can't send a message or submit a form; multi-line text is held for you to copy. If Windows stops the typing partway, only the part that wasn't typed is held, so copying it never repeats what's already in the field.
- Turning Flow off in Settings stops a recording and abandons any dictation still being transcribed or cleaned up; nothing is pasted. A recording still held after three minutes is stopped and discarded as a safety cutoff.
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
| `Services/Flow/FlowTextInjector.cs` | Checks focus (window, focused child window, UI Automation element) and elevation, writes the clipboard with history exclusion, sends Ctrl+V and verifies Windows accepted it, then restores the previous clipboard only if its sequence number is unchanged. Falls back to Unicode typing (single-line only, no control keys) when the clipboard can't be fully restored. Restore is best-effort and a partial keystroke sequence can have effects even if delivery is reported as incomplete. |
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


## Expressive mode

**Expressive mode** (`FlowExpressive`, off by default) keeps hesitations and drawn-out sounds such as "uhhhh", "hmm", and "OOOOHHhhhh" instead of cleaning them away.

- Whisper gets a prompt written in that style ("Umm, uhhh... hmm. Ohhh, okay..."), which nudges it to write the sounds down rather than drop them. Whisper decides how many letters a drawn-out sound gets, so the exact length isn't guaranteed.
- Basic cleanup skips filler removal and still fixes repeated words, spoken punctuation, and capitals.
- AI cleanup adds an EXPRESSIVE MODE rule that keeps those sounds exactly as written. It uses expressive examples in place of the filler-removal one.


## Settings added Nov 3 (Wispr-style settings pass)

| Setting | Key | Default | What it does |
|---|---|---|---|
| Push-to-talk key | `FlowPushKey` | Right Ctrl | Right Ctrl, Right Alt, or Right Shift. Right Alt's own press and release are swallowed so the app menu bar doesn't open, which means AltGr characters won't type while it's selected. |
| Microphone | `FlowMicrophone` | Windows default | Picks a WinMM input by name and falls back to the default if that mic is unplugged. |
| Language | `FlowLanguage` | English | English uses `ggml-base.en`. Auto-detect and other languages download `ggml-base.bin` (148 MB) once. AI cleanup is told never to translate. |
| Smart formatting | `FlowSmartFormatting` | on | Off: no automatic capitals, closing period, or lists. Spoken punctuation still works. |
| Dictation sounds | `FlowSounds` | on | Synthesized cues: start, stop (played after the mic's tail), and error. |
| Variable recognition | `FlowCodeVariables` | on | In VS Code, Cursor, Windsurf, and other editors: "camel/snake/pascal/kebab/constant case …" becomes an identifier, and AI cleanup gets a code-editor rule. |
| File tagging | `FlowFileTagging` | on | In Cursor and Windsurf: "at index dot tsx" becomes `@index.tsx` (known extensions only, single-word names). |
| Creator mode | `FlowCreatorMode` | off | "Dictating with Sutra Flow" shows on the island while dictating. |
| Vocabulary import | — | — | .txt or .csv files, one term per line or comma-separated. Up to 300 terms; Whisper uses the first 60. |
| Reset Sutra Flow | — | — | Resets every `Flow*` setting except the word count. The model stays on disk. |

The target app is the foreground process when the key goes down. `FlowCodeText` is pure and covered by `FlowCodeTextTests`.

Not built yet: command mode (speak an edit to selected text), an always-on Flow bar, and a taskbar icon option. Launch at login already exists under Shortcuts & behavior.
