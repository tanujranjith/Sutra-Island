# Compact voice mode (the Sutra Assistant voice orb)

Voice turns no longer open the big chat panel. The compact island itself becomes the voice UI: the
Sutra mascot slides into the album-art slot, listens, thinks, then shows a short answer or a live
system-command result, and hands the island back to the music.

Reference animations: `sutra-voice-flow-v2.gif` (question and answer) and `sutra-voice-volume.gif`
(system command). `make.py` and `make_volume.py` render them; their `frame()` functions hold the
exact timings. The mock is drawn on a 500 × 110 island; the app scales every size to the real
compact island (the default is 230 × 62).

## Setting

**Settings → Sutra Assistant → Compact voice mode**, on by default (`AppSettings.CompactVoiceMode`).
Turn it off and voice behaves exactly as before (the turn opens the chat panel when
"Auto-expand island for Sutra Assistant" is on). The orb is also skipped, and the panel used, when
the island is already expanded or set to always expanded, or when the assistant disclosure still
needs accepting. Typed chat always uses the panel.

## States and timings

| State | What shows | Timing |
| --- | --- | --- |
| Idle | The normal compact island. Nothing of the orb runs. | — |
| Wake | Island content fades out; the island widens ~38% (500 → 690 in the mock) with the island's own easing; a 2 px lavender rim (#B9A6FF, soft 80% glow) fades in. The mascot appears in the album slot: it fades in, slides 16 px from the left (cubic ease-out) and scales 0.72 → 1.0 (gentle back ease). It never drops from above. | content 280 ms; mascot from +150 ms, fade 500 ms, scale 600 ms; settles into Listening after 650 ms |
| Listening | "Listening…" (#787F96) until words arrive, then the live transcript (white, medium, one line; new words fade in; the tail stays visible). Eyes look up. Two cyan ripple rings (#50D7FF). Five voice bars, cyan → violet, driven by the real mic level (outer bars at 60%). | ripples 1.1 s per cycle, staggered; bars ≤ 30 Hz |
| Thinking | The transcript turns grey (#828A9C) with a white shimmer; the bars flatten to dots and fade; a cyan → violet arc orbits the mascot; eyes look up-right; the mascot bobs ±2 px. | shimmer 0.9 s; orbit ~420°/s |
| Answer | The island grows taller (110 → ~150 in the mock). Happy closed-arc eyes, a soft violet glow pulse, a ~3% breathe. The answer types out (at most 2 lines, then an ellipsis) and "Open in chat" appears. TTS plays when read-aloud is on. | grow 450 ms; breathe ~1.1 Hz; holds 3 s after the text or speech finishes |
| Action | System commands (through `IAssistantControls`): no height growth. Volume shows the live row (speaker icon with 1–3 waves, "Volume", slider #3C3E4A / #B9A6FF with a white knob, a large percentage) animating from the old level to `AssistantActionFeedback.Level`. Commands without a level show their icon and a one-line confirmation ("Muted"). Happy eyes. | value 1 s ease-in-out; holds 2 s after it settles |
| Error | Grey-blue desaturated mascot, flat eyes, a red dot, a short message ("Mic is blocked") and a muted "Fix" link (microphone or speech settings, otherwise Sutra Assistant settings). | auto-dismisses after 4 s |
| Exit | Content fades; the mascot fades, shrinks to 0.8 and slides 12 px left with one blink; the island returns to its size; the rim fades; the island content fades back in. | mascot 450 ms; content returns from 350 ms; done at 800 ms |

Text and slider colors follow the active theme. Spoken command results stay open through speech,
then hold for 3 s before exiting. The panel's auto-close timer waits while the orb owns the conversation.

The active orb uses a fully opaque, theme-matched surface, including with Graphite/acrylic enabled.
Media and desktop content must not show through voice text. The surface returns to the normal island
material on exit; a new wake during exit restores the solid backing immediately.

On Windows, run `dotnet run --project scripts/VoiceSurfaceCheck/VoiceSurfaceCheck.csproj -c Release`
to check the real WPF control over a magenta media backing. The check covers all active states in dark,
light, and custom themes, both motion settings, the enlarged answer area, interrupted exit, and live
theme changes. It does not start the desktop app, microphone, or provider services.

## Interaction

- **Esc** cancels at any point (stops listening, the response, and speech) and runs the exit. It is a
  global shortcut registered only while the orb is up.
- **Hover** pins the orb open; leaving restarts the hold. Hovering never expands the island into the panel.
- **Click the mascot** while it speaks to stop the speech.
- **Open in chat** opens the existing chat panel with this conversation.
- A **new wake** while an answer shows goes straight back to Listening.

## Reduced motion

When animation intensity is Reduced the orb uses crossfades only: no shell morph, scale, slide,
ripples, orbit, bob, breathe, shimmer, or typing.

## Code

- `Infrastructure/CompactVoice.cs`: the testable core. `CompactVoicePresenter` maps
  `AssistantVoiceState` plus the Q session's reply and `AssistantActionFeedback` to
  `CompactVoiceState { Idle, Wake, Listening, Thinking, Answer, Action, Error, Exit }` and says how long
  each state holds; `CompactVoicePolicy` decides when the orb applies and how it is sized and worded;
  `CompactVoiceMotion` lists the motion allowed per state.
- `Views/CompactVoiceView.xaml(.cs)`: the orb, hosted inside the compact island.
- `Views/IslandWindow.Voice.cs`: the shell morph, rim, and links.
- `ViewModels/IslandViewModel.Voice.cs`: wiring, the hold timer, and handing the session back.
- `Views/SutraMascotView`: adds the Happy and Flat eye shapes, the desaturated look, and a one-off blink.
- `Services/Q/WindowsVoicePlatform.cs`: interim transcripts and the mic level from the Windows recognizer.
  Whisper reports the final text only, so with Whisper the transcript appears when recognition completes.
- Tests: `DynamicIsland.Windows.Tests/CompactVoiceTests.cs`.
