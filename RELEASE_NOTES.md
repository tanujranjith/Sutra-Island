# Sutra Island v1.2.5

- Open screenshot capture directly with Region selected, then switch to Window or Full screen from the floating toolbar.
- Confirm successful clipboard copies with a compact in-preview toast; dismiss the preview after ten seconds or when clicking outside it.
- Repaint the selection overlay during region dragging to prevent stale dimming blocks from trailing the selection.
- Improve per-monitor capture bounds and window detection across mixed-DPI and multi-monitor desktops.

---
# Sutra Island v1.2.4

- Add local region, active-window, and all-screen screenshots from Ctrl+Alt+S, the expanded menu, or the command palette. Choose a mode before capture; copy automatically, then optionally save PNG, draw, undo, or pin the result.
- Match screenshot controls and preview motion to the graphite island. Keep captures local and move the fixed Settings shortcut to Ctrl+Alt+Shift+S.
- Reuse exact FFT window coefficients, immutable frost materials, and bounded assistant reply formatting.
- Stop the high-frequency visualizer timer when idle or hidden, preserving its existing visible playback cadence and animation settings.
- Copy local screenshot pixels directly into a detached full-resolution preview, avoiding PNG encoding/decoding before Save.
- Persist notification batches with one history snapshot, retaining ordering, deduplication, retention, and reliable shutdown flushing.
- Suspend disabled weather, stock, and connectivity polling; serialize widget refreshes and discard obsolete results after configuration changes.
- Reuse settings search text while preserving search matches and ordering.
- Replace the lifetime-blocked Settings activation worker with a registered wait, and show requested Settings before media discovery completes.
- Fix Codex subprocess ownership during reconnect/shutdown and clean up temporary images when turn preparation is cancelled or fails.

---
# Sutra Island v1.2.3

- Open the screenshot mode chooser from Ctrl+Alt+S before capturing, matching the island menu and command palette.
- Suppress hotkey auto-repeat and keep an in-progress selection undisturbed.

---
# Sutra Island v1.2.2

- Replace screenshot controls with rounded graphite buttons, a segmented capture-mode strip, lavender Copy action, and a visible active Draw state.
- Add keyboard focus styling and labeled action icons without changing screenshot resolution or capture behavior.

---
# Sutra Island v1.2.1

- Fix the screenshot hotkey collision by moving Settings to Ctrl+Alt+Shift+S.
- Add screenshot, capture, and snip commands to the palette, with the local capture-mode chooser.

---
# Sutra Island v1.2.0

- Add local region, active-window, and all-screen screenshots, automatic clipboard copy, optional PNG saving, annotation and bounded pinning.
- Reuse the notification/palette morph for the screenshot preview and add a customizable capture shortcut.
- Reduce duplicate assistant streaming allocations and repeated empty adapter discovery; move screenshot image work off the UI thread.

---
# Sutra Island v1.1.9

This release brings the recent native app updates to GitHub, including:

- A graphite interface across the island, assistant, settings, menus, and command palette, with frosted surfaces and clearer rounded corners.
- Sutra Assistant chat, opt-in Windows voice, supported island and device actions, and animated action feedback and mascot expressions.
- Smoother island, notification, timer, and palette transitions, with reduced-motion support and fixes for stale glass backdrops.
- Restored media seek controls and equalizer, improved mini-pill playback transitions, and coalesced repeated seeks to reduce playback glitches.
- A command palette with arithmetic, percentages, unit conversions, single-variable linear equations, recalled command history, and dedicated tool panels.
- Improved named timer commands and editor layout, clipboard image previews, and audio output switching.
- Self-contained source assets, updated guides and current interface screenshots, and final release verification.

Download `DynamicIsland.exe` for the standalone app, or extract the complete Codex test ZIP to include the pinned official Codex runtime. Checksums are in `SHA256SUMS.txt`.

---
# Sutra Island v1.1.8

- Package the existing Sutra logos inside the source repository so clean checkouts can build without files from a neighboring workspace.
- Refresh the README and assistant setup documentation for the current application.
- Add current interface screenshots and a local release review with verification scope and limits; clean up Git whitespace errors.

---
# Sutra Island v1.1.7

- Solve single-variable linear equations directly in the command palette, with copyable results and clear feedback for unsupported equations.

---
# Sutra Island v1.1.6

- Render the expanded island without a temporary bitmap cache to keep content sharp throughout the animation.
- Shorten the opening motion while preserving reduced motion and responsive interruption behavior.

---
# Sutra Island v1.1.5

- Keep the softer crossfade while removing extra moving content and restoring a quicker shell settle to reduce work late in the animation.

---
# Sutra Island v1.1.4

- Refine island expansion and collapse with a softer settle and a subtle upward content reveal. Keep geometry monotonic, preserve interruption handling, and respect reduced motion.

---
# Sutra Island v1.1.3

- Reduce work when the island expands by measuring the destination layout once and avoiding duplicate presentation resets and unchanged native window positioning.
- Keep the existing animation timing, easing, reduced motion, and dynamic content sizing.

---
# Sutra Island v1.1.2

- Support natural relative seek commands such as forward 30 seconds and back 30 seconds directly from the command palette.

---
# Sutra Island v1.1.1

- Make the audio-output arrow toggle the picker closed on a second click, while preserving outside-click dismissal and device selection.

---
# Sutra Island v1.1.0

- Prevent the island glass backdrop from remaining above Settings when the island is demoted; restore it after Settings closes.
- Keep media controls reachable on narrow displays with a scrollable dashboard rather than clipping the controls.
- Include the recent timer, assistant, command palette, notification, and graphite interface improvements.
- Verify the app using isolated data across desktop rendering, notifications, timer motion, palette tools, provider and comparison workflows, and unit tests.

---
# Sutra Island v1.0.57

- Support timer cancel, timer pause, and timer resume, including named timers. Rank the fully matched command ahead of a generic prefix.
- Give the timer list room for multiple rows, show an empty state, reduce the countdown ring, and remove the redundant timer footer.
- Keep controls usable in narrow layouts and preserve existing timer selection and animation behavior.

---
# Sutra Island v1.0.56

- Keep the native glass behind the stationary island while the opaque timer panel opens and closes, avoiding unnecessary blur resizing on each frame.
- Preserve timer morph timing, easing, rounded clipping, interruption behavior, and reduced motion.

---
# Sutra Island v1.0.55

- Remove the topmost backdrop change that could put a large frosted rectangle over the island and Settings.
- Keep the earlier glass handoff and the established backdrop stacking behavior.

---
# Sutra Island v1.0.54

- Bring the frosted background back during expansion, after the fastest part of the morph, instead of waiting for the animation to finish.
- Match the glass window's topmost status to its island so foreground windows remain properly blurred.
- Preserve reduced-motion behavior, clean rounded corners and the existing animation timing.

---
# Sutra Island v1.0.53

- Keep the full-date tooltip on compact and date controls, preventing it from appearing over the expanded dashboard.
- Move wireless adapter discovery off the dispatcher and update Wi-Fi indicators only when the state changes.
- Avoid unnecessary spectrum property notifications and stop visualizer rendering work when neither visualizer is visible.

---
# Sutra Island v1.0.52

- Restore the live seven-band equalizer beside the track title in the expanded graphite dashboard. It follows the existing visualizer setting and audio activity.

---
# Sutra Island v1.0.51

- Stop resizing the Windows acrylic backdrop during the island expansion and collapse animation. Use the opaque graphite fallback during the morph and restore frost when it settles.
- Keep the same animation durations and easing, including reduced-motion behavior.

---
# Sutra Island v1.0.50

- Reduce work during island animation by avoiding repeated native clipping, capture policy updates, and whole-theme refreshes.
- Keep the palette's dark material stable throughout its opening morph.
- Restore the compact assistant pill background and prevent expanded assistant content from showing the dashboard underneath.
- Keep the graphite shell readable while idle.

---
# Sutra Island v1.0.49

- Keep the native acrylic rectangle inside the panel and draw its rounded ends with WPF. This removes square backing at the corners even when Windows ignores the native clipping region.
- Blend the opaque edge protection into the frosted center, preserving readable graphite surfaces and transparent space outside each rounded panel.
- Apply the fix to the compact/expanded island, Settings, assistant, and command palette. Preserve command recall and existing interactions.

---
# Sutra Island v1.0.48

- Move real Windows frost to a separate non-layered, rounded native backdrop behind the island, Settings, and command palette. Remove acrylic and native clipping from the layered Settings window to prevent rectangular corner artifacts.
- Use an opaque graphite fallback whenever native blur is unavailable, including idle states; remove sharp see-through text from menus and dropdowns.
- Keep native backdrop windows non-interactive, hidden from Alt-Tab, synchronized with panel geometry/DPI, and disposed with their parent. Preserve screenshot-exclusion preferences.
- Add PowerShell-style Up/Down recall of the last 50 typed commands, including arguments, across palette openings in the current app session. Keep full text in memory only. Preserve suggestion navigation for a new nonempty query and arrow controls inside tool panels.
- Verify 449 unit checks and 112 native UI checks, including complete command recall, fallback readability, menus, command tools, animation, and warm reopening. Inspect actual desktop composition against a synthetic backdrop; WPF-only renders do not verify OS blur.

---
# Sutra Island v1.0.47

- Replace the overly clear graphite transparency with a denser tinted glass material. Background text has much less influence on the interface.
- Add a shared reflective edge highlight and retain fully opaque foreground text and controls.
- Enable native acrylic frosting behind the rounded Settings window, updating its native clipping region on resize and DPI changes. Keep the denser readable tint as the fallback when native frosting is unavailable.
- Apply matching material density to the island, assistant, timer, command palette, and menus. Preserve palette morph blending, existing command tools, popup behavior, and reduced motion.

---
# Sutra Island v1.0.46

- Make graphite island backgrounds translucent while preserving opaque foreground text, artwork, and controls. Remove duplicate background paint that previously accumulated into an opaque surface.
- Carry the graphite material, lavender selection and focus colors, neutral typography, rounded shell, and dark scrollbars through the command palette and its custom tool panels.
- Extend the translucent material to assistant, timer, Settings, menus, and live timer/accessory cards. Preserve existing Light/Custom island preferences.
- Blend the palette morph surface into its final translucent card without changing entrance timing or scaling text. Keep reduced-motion and hidden warm-cache behavior.
- Verify rendered alpha and light/dark backdrop captures, command tools, popup stability, keyboard controls, and palette presentation/performance using isolated preview data.

---
# Sutra Island v1.0.45

- Replace the missing assistant-launcher glyph with the supplied mascot image.
- Apply shared graphite styling to island and assistant context menus, built-in edit menus, submenu rows, checkmarks, separators, dropdown items, tooltips, and audio-output actions.
- Match the system tray menu to the graphite palette while keeping its existing actions and check states.
- Keep the island expanded while its context menus are open. Preserve real menu commands, keyboard dismissal, nested menus, and screen-access toggles.

---
# Sutra Island v1.0.44

- Restore back/forward 30-second controls alongside previous, play/pause, and next in the graphite media dashboard. Keep the existing coalesced seeking implementation.
- Keep the expanded island stable while its audio output picker is open. Stop hover collapse and dimming while choosing an output, preserve popup anchoring, and resume normal hover behavior after dismissal.
- Add native regression checks with an unpinned island for both the pointer-leave and collapse-timer paths.

---
# Sutra Island v1.0.43 — Graphite

- Recreate the supplied graphite concept in the native expanded island, Sutra Assistant, timer, and Settings screens: charcoal surfaces, lavender accents, quieter dividers, compact typography, and rounded controls.
- Add the live media/system dashboard and connection footer, unboxed assistant replies with an inset composer, a two-column timer, and a three-column Settings workspace with a live preview.
- Add combined hours/minutes/seconds timer entry, retaining the one-second to one-day duration limit.
- Keep configured compact island dimensions, provider/account selection, keyboard submission, voice, screen assistance, notifications, and saved controls. Migrate existing System/Dark themes to Graphite while retaining Light/Custom choices.

---
# Sutra Island v1.0.42

- Move the thinking bulb from the reply status pill to the upper-right corner of the conversation-header mascot. Keep it above the eyes and clear of the title.
- Light and pulse the corner bulb during capture, thinking and streaming; hide it after completion or Stop. Respect reduced motion and stop animation on hidden/closed surfaces.
- Keep the reply's responding label, shimmer, dots and streaming caret. Add focused native checks and header-size captures for the corner bulb.

---
# Sutra Island v1.0.41

- Handle assistant Enter in PreviewKeyDown, before the multiline TextBox consumes it. Enter sends; Shift+Enter inserts a newline at the caret or replaces selected text. Ignore held-key repeats and preserve drafts while a response is running.
- Verify routed Enter submission and multiline modifier behavior in the native WPF harness.

---
# Sutra Island v1.0.40

- Replace the rotating reply mark with a warm idea bulb: breathing glow, illuminated rays and a gentle pulse throughout thinking and streaming. Keep static reduced-motion presentation and cleanup on Stop, completion and hiding.
- Replace fragile raster eye patches with one continuous native face surface and animated vector eyes. Remove the duplicate static mascot underneath, retain the original ribbon artwork, and use shared higher-resolution art with consistent antialiasing. Add rendered blink checks at the real 26/32/40-pixel icon sizes.
- Keep the selected provider label tied to saved provider/account identity even if the dropdown temporarily clears during account refresh. Restore selection after provider-list updates; show Codex subscription, API-key or sign-in labels from the actual account mode.
- Verify provider label fallbacks, streaming bulb activity and compact mascot rendering in the native WPF harness.

---
# Sutra Island v1.0.39

- Port Sutra floating chat's thinking pill into native WPF: spinning four-point mark, pulsing opacity, 1.9-second sheen, 2.2-second text shimmer, staggered bouncing dots, stepped streaming caret and 260ms reply entrance. Preserve the original timings and attribution in embedded source references.
- Add a native eye rig to the existing Sutra Assistant mascot in the header, compact assistant and launch control: natural blinks, thinking/listening gaze and subtle speaking motion. Preserve the original PNG and stationary ribbon artwork. The inspected web checkout has a static mascot image; these eye motions are new native work rather than a copied web eye rig.
- Stop all repeating motion when hidden or closed; keep a static neutral mascot and cue under reduced motion. Preserve immediate real streaming updates and existing device-action feedback.
- Verify blink keyframes, expression changes, streaming-caret lifecycle and full UI regressions with isolated native captures.

---
# Sutra Island v1.0.38

- Remove the island's right-click-to-Settings shortcut. Keep explicit Settings controls and tray navigation available.
- Add an assistant reply activity cue with the supplied logo, a subtle orbit/halo and a staggered wave. Preserve distinct reading, thinking and responding labels.
- Reveal the first streamed answer once with a short fade/slide; render subsequent text immediately without restarting the entrance. Smoothly retire the activity row on completion or Stop.
- Respect reduced motion with a static cue. Stop repeating clocks when hidden, closed, cancelled or completed; keep error text readable and preserve existing device-action animations.
- Verify right-click behavior, explicit Settings access, streaming updates, lifecycle cleanup, and reduced motion in the isolated native WPF harness.

---
# Sutra Island v1.0.37

- Stop replaying the entire pill's collapse/fade animation when compact equalizer visibility changes.
- Give the equalizer a dedicated width/opacity transition, reversing from its current presentation during rapid playback changes. Keep the shell and remaining compact content visible and at the configured size.
- Share the equalizer lane with the Settings compact preview. Settle hidden/unloaded states and respect reduced motion without animation clocks.
- Extend native WPF verification to cover normal-motion entry, exit, rapid reversal, stable shell/content, and reduced-motion behavior.

---
# Sutra Island v1.0.36

- Recognize natural assistant seeks such as "can u go back 30 seconds in song", "rewind the song by thirty seconds", and "skip ahead by half a minute" locally. Normalize duration-first provider arguments into the same validated seek action.
- Share one seek queue across assistant, island buttons and palette scrubbing. Combine short bursts into fewer Windows position writes, preserving every relative step and input order. Serialize writes and use the latest accepted position while the source timeline catches up.
- Drop queued requests when their track/source changes, omit cancelled requests, clamp to track bounds, and retain actual media-app acceptance for assistant receipts. Rejected seeks do not advance the local position.
- Add tests for ten rapid three-second rewinds, clicks during an in-flight write, mixed directions and slider changes, boundaries, source/track changes, rejection, cancellation, and shutdown.

---
# Sutra Island v1.0.35

- Use the supplied Sutra and Sutra Assistant logos across Settings, assistant headers and launch controls, compact activity branding, command palette, window icons, executable and system tray. Keep media artwork and activity-specific symbols intact. Share decoded logo resources.
- Preserve the configured mini-pill width when audio starts. Remove the equalizer's 264-pixel minimum width, which exposed track titles and made the live island disagree with the compact preview.
- Verify paused-to-playing compact sizing in Apple and Stats layouts, logo resources, and the embedded tray icon in an isolated WPF preview.

---
# Sutra Island v1.0.34

- Add action-specific feedback to Sutra Assistant: a compact working orbit, completion checkmark, and a volume fill animated to the confirmed level. Timer, media, output, focus, notification, clipboard and settings actions use matching icons and labels.
- Drive feedback from structured execution state, including failure and cancellation. Ordinary chat clears the previous action cue. Fast local actions retain a completion animation without delaying the actual device operation or voice response.
- Respect reduced motion with immediate static states. Stop repeating animation when hidden, replaced, cancelled or closed; prevent stale success feedback from replaying over a newer action.
- Copy only Sutra Assistant logo.png and Sutra logo.png from the sibling Sutra assets directory into the workspace root assets folder. Verify both copies against their originals.

---
# Sutra Island v1.0.33
- Connect Sutra Assistant to island controls in typed and voice chat: verified volume/mute, playback/seek, audio outputs, timers, alarms, stopwatch/laps, focus, notifications, clipboard UI, navigation, appearance and standard settings.
- Run clear commands locally without a provider round trip. Let configured providers request validated island actions for natural phrasing, then replace the proposal with the local execution receipt. Preserve conversation context across actions.
- Check Windows readback for volume and mute and media-app acceptance for playback/seek. Reject unknown actions, invalid settings, ambiguous devices, cancellation, and control proposals in explanatory or negated questions. Screen assistance and Say mode do not execute actions.
- Keep action access limited to island controls. Clipboard history opens in its existing UI; provider credentials and assistant system prompts remain outside control writes.

- Interpret `%` as a calculator percentage rather than programming remainder. `5%1000`, `5 % 1000`, and `5% of 1000` now return 50; standalone `5%` returns 0.05.
- Support percentage multiplication/division and bare percentage increases/decreases, such as `1000 * 5%` = 50 and `1000 + 5%` = 1050. Retain precedence, parentheses, signs, decimal arithmetic, and validation.
- Use explicit `mod` for remainder calculations (`5 mod 2` = 1). Route arithmetic keywords to the calculator while retaining ordinary unit conversions.
- Add arithmetic regressions and native palette checks for the reported expression and related percentage behavior.

---
# Sutra Island v1.0.32

- Accept valid semantic Codex runtime versions, including the installed 0.162.0-alpha.17.2 build. Preserve prerelease/build labels while enforcing the existing minimum supported version; reject malformed or ambiguous output.
- Drain both version-probe output streams, check its exit code, bound the probe to ten seconds, and stop cancelled probe processes.
- Rename remaining visible Q branding to Sutra Assistant: conversation header, badges, assistant states, accessibility labels, settings text, comparison disclosure, and default Ask/Say prompts. Preserve internal preference keys, provider credentials, and Ctrl+Alt+Q compatibility.
- Add semantic-version policy regressions and a native read-only probe of the installed Codex executable. Verification does not sign in, read credentials, start an AI turn, or change account settings.

---
# Sutra Island v1.0.31

- Release the command palette's reveal mask once its opening animation settles. Device discovery and command-panel changes can then grow the full card without retaining an earlier clipped height.
- Keep the resized palette inside its monitor work area when asynchronous results change the window size. Notification banner masking remains unchanged.
- Verify delayed audio output discovery after the opening morph, loading/loaded bounds, filter clearing, empty searches, and switching away and back to the output panel.

---
# Sutra Island v1.0.30

- Show screenshot and image entries alongside text in clipboard history. Decode bounded previews and display the original pixel dimensions; copying an image restores its original Windows history item instead of a reduced thumbnail.
- Keep image entries available when a thumbnail cannot decode, report stale-item copy failures with refresh guidance, and release previews and original history references when the panel closes. Search "image" or "screenshot" to find pictures.
- Open the same image-aware history from the island menu and Ctrl+Alt+K.
- Remove double-applied padding from shared text field templates. Restore full-height carets in palette search panels, timer/alarm/focus/conversion forms, and island timer inputs while retaining the existing assistant composer and standard text field behavior.
- Extend native verification with real Windows bitmap decoding, mixed clipboard rows, original-item copy selection, stale-item failure, preview cleanup, empty/typed caret geometry, and scaled layouts.

---
# Sutra Island v1.0.29

- Accept `unpause` in the command palette as an alias for media play/resume.

---

# Sutra Island v1.0.28

- Ctrl+Alt+K now opens from the island using the notification morph: a connecting neck, expanding rounded material, and an unscaled command layout. Dismissal retracts the card; rapidly reopening cancels the previous hide. Reduced motion opens and closes immediately.
- Anchor the palette to the actual island shell and its monitor work area, including monitors with negative coordinates. Preserve command cards, keyboard selection, search, and favorites.
- Prepare the hidden command layout once a minute at idle priority and after resume without showing or activating a window. Reuse the render bitmap and prepare clean suggestions on dismissal so hotkey handling does less work.
- Move privacy registry and application-metadata reads off the dispatcher, prevent overlapping scans, and discard results after shutdown.
- Verify palette motion, anchoring, reduced motion, interrupted dismissal, hidden preparation, cache-pressure reopening, and input responsiveness during a blocked privacy scan in the isolated native harness. A real multi-hour idle interval is still a manual check.

---
# Sutra Island v1.0.27

- Add compact command panels for `stopwatch`, `focus`, `clipboard`, `notifications`, `convert`, and `audio output` in Ctrl+Alt+K.
- Keep stopwatch laps and timed focus sessions alive when the palette closes. Restore the previous focus preference on session completion; recover timed sessions after restart.
- Search clipboard text and copy original formatting without storing clip contents in palette recents. Search/filter notification history, remove entries, clear history, toggle mirroring, and dismiss the current banner.
- Convert compatible units live with category selection, swap, validation, and copy. Select the system audio output with a current-device marker and disconnected-device feedback.
- Replace the island's unbounded notification history popup with an opaque 380-DIP card, bounded scrolling, timestamps, previews, per-item removal, and clear-history action.

---
# Sutra Island v1.0.26

- Open a compact playback scrubber by selecting `seek` in Ctrl+Alt+K. Share Volume's slider styling, with track details, elapsed/total time, and ±10-second controls.
- Preview position while dragging and commit on release. Keep playback updates from moving the thumb during a drag; coalesce rapid keyboard adjustments while a seek is pending.
- Refresh for track changes and disable unavailable seeking. Detach progress updates and discard queued adjustments when the card closes. Existing seek commands with arguments still run directly.
- Verify command panels independently with `scripts/Verify-SutraUpdate.ps1 -CommandPanelsOnly` using isolated data and fake audio/media.

---

# Sutra Island v1.0.25

- Redesign Volume as a compact 440-DIP audio card with the output device, a smaller level readout, slider, presets, and mute. Remove the oversized heading, repeated instructions, and full-width Done button.
- Use an opaque dark surface to prevent underlying app text from bleeding through. Replace the default dotted slider focus rectangle with a subtle custom focus outline.
- Restore the full palette width when returning to search and preserve its horizontal center as the card resizes.

---

# Sutra Island v1.0.24

## Command panels

- Press Enter on `volume` to open a live slider with output-device information, 25/50/75% presets, mute, and keyboard controls. Hardware volume changes stay in sync while the panel is open.
- Remove the duplicate Volume visibility setting from ordinary audio searches. Find it as **Show volume in island** through explicit settings search.
- Open dedicated timer and alarm forms from bare commands: timer duration presets and labels, plus alarm time and repeat choices. Commands with arguments still execute directly.
- Escape returns from a panel to the same search; dismissing the palette releases its live audio subscription. Give commands meaningful icons instead of the generic triangle.

---

# Sutra Island v1.0.23

## Wake listening reliability

- Keep “Hey Sutra” armed when the assistant chat closes automatically or manually. Closing a completed chat no longer makes later wake detections get ignored.
- Save the wake-listening switch immediately, including explicit opt-out, without waiting for the Settings window to close.
- Add regression checks for chat auto-close followed by another wake, manual close, dictation cancellation, and immediate switch persistence.

---

# Sutra Island v1.0.22

## Notifications and date

- Deliver notifications on the first collapsed launch, without expanding the island first; recover through one-second default polling and immediately release deferred banners when interaction ends.
- Show two-second Caps Lock and Num Lock On/Off banners through the existing notification morph. Each key has its own switch. Urgent alerts take priority; ordinary banners resume afterward.
- Show the expanded date independently of battery and clock visibility. Add an optional compact date in Settings → Island content; it is off by default.
- Preserve animation timing, easing, and reduced-motion behavior. Move notification-history writes off the UI thread and skip hidden visualizer work.

## Ctrl + Alt + K command hub

- Keep the palette ready between uses and prepare its first native presentation without a visible window or focus activation.
- Execute the highlighted suggestion consistently, including suggestions shown with an empty query.
- Add volume levels/adjustments, current media-app opening, recentering, clipboard and notification history, notification controls, and assistant voice commands.
- Search settings by their names and descriptions; select a setting to open its section and highlight its control.
- Star commands or press Ctrl+D to favorite them. Show favorites and ten recent command identifiers without retaining prompts or arguments.

## Sutra Assistant

- Extend Q with ordinary chat and follow-ups that do not capture the screen. Choose Use screen for one question, or use the existing screen-assistance shortcut intentionally.
- Enable “Listen for Hey Sutra” in Settings → Sutra Assistant for local Windows wake detection. Wait for the Listening cue, ask the question, then read and hear the reply. Say the wake phrase again for the next turn.
- Use installed English Windows recognition and voices. No cloud voice service is used. Wake listening is off by default; typed replies remain silent, and voice replies can be muted in Settings.
- Stop cancels dictation, generation, and playback. New chat clears the conversation. Say mode continues to suggest wording.
- Microphone denial or missing speech resources leaves typed chat available. Install an English speech language in Windows Settings → Time & language → Speech if needed.

## Verification

- Run `scripts/Verify-SutraUpdate.ps1` for the unit suite and isolated native fixture checks. Pass `-Executable` to verify a published build.
- Native verification never starts a real microphone, speaker playback, media session, or AI provider request. A real microphone/wake-word check is still required on the target device after opting in.

---

# Sutra Island v1.0.21

## Media playback

- Make the expanded playback bars seekable by clicking or dragging across the timeline.

---

# Sutra Island v1.0.20

## Command palette

- Convert common length, weight, temperature, volume, time, area, speed, and data-size units as you type; press Enter or click to copy the result with its unit.
- Volume conversions use US customary measures. KB/MB/GB are decimal units; KiB/MiB/GiB are binary units.

## Notifications and animations

- Recover notification delivery through background polling when Windows events or reads are unavailable, and resume automatically after temporary failures or restored access.
- Dismissing a notification suppresses delayed duplicate copies and removes grouped copies from Windows when possible.
- Morph notification banners from the island and timer panels from the timer orb, including interrupted transitions and reduced-motion behavior.

## Startup

- Fall back to synchronous settings loading if the asynchronous startup read takes too long.
- Add `--show-island` to open the island directly.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.20-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.18

## Notification recovery

- Surface active Windows notifications that arrived while the island listener was stopped or restarting.
- Use the island's saved notification history to avoid replaying notifications it already presented.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.18-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.17

## Q vector equation formatting

- Render vector notation as readable text so missing combining-arrow glyphs do not appear as boxes.
- Convert common LaTeX vector matrix environments into correctly separated coordinate tuples.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.17-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.16

## Q response formatting

- Convert common inline and display math markup into readable Unicode in Q responses, including scripts, fractions, roots, Greek letters, and operators.
- Preserve the underlying response text for copying; only the on-screen display is formatted.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.16-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.15

## Notifications

- Refresh the Windows notification snapshot immediately after notification-added events, with overlapping polls serialized so an event during a poll triggers a follow-up refresh.
- Deliver notification content changes even when Windows reuses an existing notification ID; identical repeated messages remain deduplicated.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.15-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.14

## Notifications

- Capture Windows notification-added events as they happen, while retaining snapshot polling as a recovery path for missed events.
- Preserve the source app, title, and body when one notification has waited behind another island activity. Generic summaries are reserved for multiple waiting notifications.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.14-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.13

## Music visualizer

- Keep the bars moving when loopback capture is active but its signal is below the spectrum analyzer's useful range. The real spectrum takes over again as soon as a usable signal returns.

## Downloads

- `DynamicIsland.exe`: updated self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.13-win-x64.zip`: app, setup documentation, and pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

---

# Sutra Island v1.0.12

## Settings

- New sidebar navigation, Overview page, and dedicated Shortcuts & behavior page.
- Search settings sections and collapse the live preview to make more room.
- Compact and expanded previews preserve the island's proportions.

## Q assistant

- Smaller header, message bubbles, and a unified composer with provider, model,
  reasoning effort, dictation, and send controls.
- Rounded dropdown menus with selection checkmarks and keyboard focus states.
- Provider labels distinguish OpenAI Platform API-key billing from Codex account
  access, including subscription and API-key authentication.
- Model discovery runs when Q opens or its provider changes; refresh the catalog
  manually or enter an exact model ID. Suggested models include GPT-6 Luna,
  GPT-6.1 Sol, GPT-6 Astra, Claude Sonnet 5.5, and Claude Opus 5.5.
- Codex effort choices come from the account's model catalog; API-provider effort
  choices use model-specific rules. Auto keeps the provider's default.
- Three softly pulsing dots replace the thinking shimmer and glow.
- The island stays open while its Q menus are being used.

## Commands and media

- Command palette for media, volume, timers, Q, and other island actions.
- `ask <question>` captures context and submits the question automatically.
- Repeated volume, mute, playback, and seek clicks accumulate correctly.

## Downloads

- `DynamicIsland.exe`: self-contained Windows x64 app.
- `DynamicIsland-Codex-Test-v1.0.12-win-x64.zip`: app, setup documentation, and
  pinned SHA-256-verified Codex 0.151.0 runtime. Extract the complete folder.
- `SHA256SUMS.txt`: checksums of both downloads.

Model availability depends on your provider account. The Codex test runtime
remains pinned; the standalone app can use a supported official Codex installation.

Validation: 215 automated tests passed and the Windows x64 release build succeeded.

---

# Dynamic Island v1.0.11

## Q and responsive pill controls

- Ctrl+Alt+Q expands the Island by default, with a setting to disable auto-expansion.
- Added adjustable Q auto-close behavior with a 1–300 second response delay.
- Added smaller pill width and height controls, left/right edge positioning, and hole-punch presentation.
- Fixed expanded edge pills being clipped off-screen and preserved hover expansion.
- Hole-punch mode keeps album artwork visible and shows Q output without the full expanded layout.

## Verification

- Release build: succeeded with 0 warnings and 0 errors.
- Root executable: rebuilt, hash-verified, and relaunched.

---

# Dynamic Island v1.0.10

## New playback and connectivity features

- Added an expanded music visualizer with animated fallback bars and optional real audio-spectrum response.
- Added Wi-Fi and Bluetooth status cards with one-click shortcuts to the corresponding Windows settings pages.
- Added settings toggles for the visualizer and connectivity cards.

## Verification

- Release build: succeeded with 0 warnings and 0 errors.
- Automated tests: 97 passed, 0 failed, 0 skipped.

---

# Dynamic Island v1.0.9

## Responsive expanded widgets

- Live widgets now fill the full accessory lane whenever connected AirPods are not present.
- When AirPods connect, their dedicated card returns and the widget cards contract into the shared row.
- Weather, countdown, meetings, battery time, world clocks, and stocks share reclaimed space evenly while retaining horizontal scrolling for larger sets.

## Verification

- Release build: succeeded with 0 warnings and 0 errors.
- Automated tests: 97 passed, 0 failed, 0 skipped.

---
# Dynamic Island v1.0.8

## Expanded live-widget layout

- Fixed the expanded island clipping the countdown widget when weather was also enabled.
- The live-widget rail now sizes itself to the visible cards, preserving the AirPods card while keeping weather and countdown fully visible together.
- Additional enabled widgets remain accessible with horizontal mouse-wheel scrolling.

## Verification

- Release build: succeeded with 0 warnings and 0 errors.
- Automated tests: 97 passed, 0 failed, 0 skipped.

---
# Dynamic Island v1.0.7

## Live activities and expanded layout

- Added the enabled live-widget row to the expanded island, including weather, countdown, meetings, battery time, world clocks, and stocks.
- Combined connected AirPods and live widgets into one compact horizontal lane while keeping them as separate cards.
- Added Apple-style camera and microphone privacy presentation: a minimal green/orange orb when compact and an inline Live Activity when expanded.
- Improved Windows privacy-sensor detection across nested packaged and non-packaged consent-store entries.

## Cleanup and reliability

- Removed the webcam presence, person-detection, face-enrollment, privacy-blur, and camera-automation subsystem, including OpenCV dependencies and the legacy Python camera build.
- Fixed Codex subscription thread creation to send the current `read-only` sandbox enum instead of the rejected `readOnly` value.
- Added regression coverage for the Codex thread sandbox contract.

## Verification

- Release build: succeeded with 0 warnings and 0 errors.
- Automated tests: 97 passed, 0 failed, 0 skipped.
- Native expanded-state validation: privacy Live Activity, AirPods card, and weather card confirmed in the running root app.

---

# Dynamic Island v1.0.6

## Provider API refresh

- Updated OpenAI API-key inference to the Responses API while preserving the separate ChatGPT/Codex subscription provider.
- Updated Anthropic Messages, Gemini `streamGenerateContent`, Groq, xAI/Grok, OpenRouter, DeepSeek, and native Ollama request and streaming contracts from their official developer documentation.
- Added current provider defaults, model discovery, provider/model-specific effort choices, full conversation history, and correct image payloads.
- **Auto** now preserves each model's own reasoning default; explicit model and effort selections are forwarded on the wire.

## Reliability and security

- API keys remain in the Windows user-scoped DPAPI secret store and are sent in provider-defined headers rather than logs or settings exports.
- Added structured, sanitized HTTP and in-stream errors, including request IDs, retry timing, blocked responses, and output-limit failures.
- Added wire-contract tests for every provider, including headers, URLs, models, efforts, images, SSE/NDJSON output, and post-HTTP-200 stream errors.
- Bundled-runtime verification now requires exact SHA-256 coverage for both official Codex executables.
- Fixed expanded AirPods-card clipping by keeping the transparent host window synchronized with dynamic shell content.

## Known limitations

- Hosted API providers use their own API billing and rate limits; only the ChatGPT/Codex provider can use eligible Codex subscription limits.
- Provider model catalogs change independently. Preview models may be renamed or retired, and model availability depends on the configured account.
- Automated tests validate the documented HTTP contracts without sending prompts through the user's paid API keys.

## Verification

- Release test suite: 114 passed.
- Release WPF publish and hash-verified Codex test package: succeeded.
- Clean-extraction smoke test: bundled Codex 0.151.0 selected, signed-in account restored, and 7 models discovered.

---

# Dynamic Island v1.0.5

## ChatGPT / Codex integration

- Added ChatGPT-managed device-code sign-in through the official Codex app-server; no OpenAI API key is required.
- Added live account, plan, usage/reset, model, and model-specific reasoning-effort controls in Settings and the expanded Island.
- Added clear signed-out, expired-session, network, model, usage-limit, server-exit, and cancellation errors.
- Sends the exact selected model/effort, interrupts stopped turns, and deletes only Dynamic-Island-owned Codex threads.
- Keeps the existing OpenAI API-key provider and all other providers intact.

## Codex test distribution and security

- Added a second `DynamicIsland-Codex-Test-v1.0.5-win-x64.zip` release asset with pinned official Codex 0.151.0 Windows binaries.
- Release packaging verifies OpenAI's published SHA-256 checksums; the app verifies a bundled runtime manifest before launch.
- Codex Q turns use an isolated local workspace, read-only/no-approval settings, and reject tool or interactive approval requests.
- OAuth tokens remain owned by Codex. Local diagnostics omit prompts, screenshots, device codes, tokens, and raw credentials.

## Reliability

- Repaired streamed response mojibake and made empty Codex responses actionable.
- Fixed repeated AirPods connection animations when the underlying connection did not change.
- Added timeout enforcement, dynamic model discovery, and regression coverage for runtime integrity, model/effort forwarding, thread ownership, output decoding, and AirPods connection policy.

## Known limitations

- The bundled Codex package is a test build while the official app-server protocol continues to evolve.
- ChatGPT access is limited to eligible Codex service usage; it is not a general OpenAI API key or API credit balance.
- Signing out affects the shared official Codex profile for the current Windows user.

## Verification

- Release test suite: 101 passed.
- Release WPF publish: succeeded with 0 errors.
- Live installed-runtime and bundled-runtime account/model discovery: passed with Codex 0.151.0.

---

# Dynamic Island v1.0.4

## AirPods and BLE device status

- Added native Windows Bluetooth LE detection for paired, connected AirPods and compatible Beats devices.
- Parses Apple manufacturer advertisements for model, left/right/case battery, charging, case-lid, and in-ear state.
- Handles rotating BLE advertisement addresses and uses the active audio endpoint as a connection fallback.
- Displays honest 10% battery ranges such as 90-99% when the public advertisement does not provide exact percentages.
- Keeps battery and model refreshes from replaying expanded-island animations.

## UI reliability

- Added the AirPods connection banner and expanded status card with the bundled AirPods render.
- Fixed clipped/overlapping layout and restored missing banner columns.
- Consolidated the island resize animation to avoid duplicate expand/shrink transitions.

## Deliberate limitation

ANC, transparency, gesture, and other AirPods controls are not included because Windows does not expose a safe public API for those controls in this driver-free integration.

## Verification

- Release test suite: 80 passed.
- Release WPF build: succeeded with 0 errors.
- Physical AirPods hardware validation remains adapter/device dependent and is not represented as automated coverage.
