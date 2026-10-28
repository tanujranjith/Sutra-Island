# Shared Sutra Assistant motion

Based on Sutra by Tanuj Ranjith. Source checkout reviewed on 2026-10-25.

The floating assistant uses `styles/legacy/workspace-overrides.css` and `src/core/app.js` (`showThinkingIndicator`, `_streamMessageInto`). The full assistant mirrors its thinking treatment in `styles/views/assistant-view.css`.

`sutra-floating-assistant-motion.css` preserves the original floating-chat CSS excerpt. The full source stylesheet SHA-256 at import was `72DB4A7B44ADAAC15FF93E1CA131AA5EFF2FCE49561960C3D28D199E076602F2`. License and NOTICE are preserved alongside it.

Native WPF mapping in `AssistantReplyView`:

As of v1.0.40, the user's requested warm idea bulb replaces the rotating four-point mark. Other copied floating-chat timings remain; the bulb has its own native glow/ray pulse.

| Source | Native effect |
| --- | --- |
| chatbotThinkingSpin | four-point mark rotates every 2.6 seconds |
| chatbotThinkingPulse | mark opacity 0.6–1, 1.4-second cycle |
| chatbotThinkingSheen | 1.9-second sweep, movement ends at 60% |
| chatbotThinkingShimmer | 2.2-second label shimmer |
| chatbotThinkingDot | 1.2-second opacity/bounce cycle, delays 0 / 0.18 / 0.36 seconds |
| chatbotCaretBlink | 1.05-second stepped streaming caret |
| chatbotMsgIn | 260ms fade and 6px entrance, cubic-bezier(0.22,0.61,0.36,1) |

Real provider chunks remain immediate; the web app's synthetic word-streaming timer is not needed in the native streaming UI. Reduced-motion and hidden-surface cleanup apply to all effects.

## Mascot eye rig

The inspected floating-chat markup references a static PNG and has no independent eye-animation implementation. `SutraMascotView` is a new native eye rig using the user's existing `Sutra Assistant logo.png`, not a copied web eye rig. The original PNG remains unmodified; a continuous native face surface and vector eye layers are composited over it at runtime. This avoids the ghost edges left by earlier raster eye patches at small icon sizes. The ribbon art remains stationary. Native motions include natural blinking, thinking/listening gaze, and a subtle speaking expression. Reduced motion leaves a neutral open face.
