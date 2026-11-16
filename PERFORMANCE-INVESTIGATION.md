# Sutra Island performance audit — 2026-11-13

## Purpose and completion boundary

Audit the running Windows application's performance paths and implement evidenced improvements that retain its animation, rendering, speech recognition and feature quality. This audit covers the active WPF source in this directory and the shared Q core, rather than historical copies, marketing projects or backups elsewhere in the workspace. Changes are deliberately limited to unnecessary waits, blocking work and allocations. No animation durations, frame cadence, image dimensions, recognition thresholds, models or provider response limits were reduced.

The broad source review and selected performance fixes are complete. This is not an exhaustive runtime profile of every feature or a guarantee that every possible optimization has been found. Live microphone latency and GPU frame times remain unmeasured. Desktop interaction was attempted through both available computer-use runtimes; both failed because their native host/pipe was unavailable. Those limits prevent claiming that the user's exact shortcut and spoken wake delays have been reproduced or completely eliminated.

## Method and evidence

Reviewed startup and hotkey dispatch, palette creation/priming/presentation and command execution, Whisper model loading and wake segmentation, voice UI updates, Q streaming and Codex startup, audio spectrum and media polling, timer persistence, settings/history/logging, screenshot/OCR capture, material caching, animated view lifetimes, and background polling. Searches covered blocking reads/writes, synchronous waits, timer/render subscriptions and repeated allocations. This was a focused review of the main paths, not line-by-line inspection of every file.

Used existing isolated WPF fixture suites and the 742-test unit project. Added adversarial checks for a deliberately blocked settings save, dismissal during that save, active-audio allocations, and exact speech samples. Artifacts reside under artifacts/performance-baseline, performance-after, performance-final, panels-after, performance-tests, sutra-final and upgrades-final. Fixture settings/data are isolated from the user's production settings by ISLAND_PREVIEW and ISLAND_PREVIEW_DATA.

## Implemented findings

### P1 — First Whisper wake pays model preparation cost

Evidence: WhisperEngine.TranscribeAsync called GetFactory only after the audio segment was complete. GetFactory verifies a roughly 148 MB model, extracts native libraries when needed, and loads Whisper. WhisperVoiceInput.StartWake previously opened the microphone without preparing this model. This adds model preparation to the first utterance's critical path.

Fix: WarmUpAsync uses the same engine semaphore as transcription and disposal, and wake startup schedules it on a worker task. Queued warm-up respects the wake-session cancellation token; errors are observed and logged. Repeated starts reuse an already loaded factory. Native loading that has already started cannot be cancelled midway.

Quality: same model, integrity verification, transcript checks, speaker profile, sensitivity, cooldown and one-breath command handling. This removes preparation from the critical path only when it finishes before the first wake. It does not remove inference time or the 450 ms endpoint silence. No measured wake-speed percentage is claimed. Loading sooner can increase memory earlier in the session; that is a deliberate tradeoff for responsiveness.

### P1 — Opening a command panel waits for recent-command persistence

Evidence: CommandPaletteViewModel.ExecuteMatchAsync awaited its save callback before calling OpenPanel. Slow storage or an already queued settings save therefore delayed panel appearance even after the command had been resolved. Existing close commands already initiated dismissal before saving.

Fix: Open the requested panel before awaiting persistence. Continue to await the save before completing command execution. Remove the later OpenPanel so completion cannot resurrect a panel dismissed during the save.

Verification: a TaskCompletionSource holds saving indefinitely; the volume panel appears while execution remains pending. Resetting the panel while saving and subsequently releasing the save leaves it closed. Both new checks pass. Existing command-panel controls, subscriptions, persistence and binding checks also pass. This finding concerns panels opened by commands; it is not proof of the cause of initial Ctrl+K palette latency.

### P2 — Active speech allocates an extra array every 30 ms

Evidence: VoiceSegmenter.Push converted each active input span to an array before adding it to the retained utterance list. The microphone already provides the frame; this temporary array served no ownership purpose after AddRange copied it again. At 16 kHz with 480-sample frames, that is about 1,920 bytes of sample data per active frame, roughly 64 KB/second plus array headers.

Fix: grow the utterance list as required and copy the input span directly into its owned storage. Pre-roll still owns its frame copies, and completed utterances still return detached arrays.

Verification: after existing capacity is established, four active frames allocate zero temporary managed bytes. A synthetic segment retains all 24 speech frames sample-for-sample followed by all 15 silent frames, and emits at the unchanged 450 ms boundary. Full voice/wake-related unit tests passed in the 742-test run.

### P1/P2 — Codex discovery/checksums can occupy the UI thread and allocate entire executables

Evidence: StartAsync's uncontended semaphore can complete synchronously. Resolve was then evaluated before the next await, potentially on the caller's UI thread. Bundled runtime verification used File.ReadAllBytes for each required executable, allocating an array as large as the file before hashing it.

Fix: execute resolver work through Task.Run with the startup cancellation token, then retain normal validation/startup flow. Hash via a read stream instead of a whole-file byte array. Every required executable is still fully hashed and checked against the same manifest. No integrity checks are bypassed or cached.

Verification: Release publish and the unit suite, which links the resolver source, pass. The panel fixture also verified the installed Codex version. No authenticated Codex request was sent. Runtime initialization still waits for full validation and the existing serialized startup gate prevents duplicate starts.

### P2 — Flow success glyph repeatedly constructs identical path arrays

Evidence: FlowDotMatrixView.CheckIndex constructed the same seven-tuple checkmark path for each invocation while rendering its dot grid.

Fix: reuse a private static readonly path. Coordinates, sequence, math, brushes and frame scheduling are unchanged. The final Release build includes this allocation removal. Its pixel identity follows from using the identical immutable coordinate data; no standalone before/after GPU benchmark was performed for this small change.

## Other areas and disposition

| Area | Evidence and decision |
| --- | --- |
| Initial palette presentation | Already creates its HWND, measures/renders offscreen, performs a transparent first show, and warms hidden layout at idle/resume. Preserve this. Fixture measured 38.6 ms synchronous initial presentation and 4.6 ms after forced cache pressure; hidden warming took 47.7 ms. These exclude the 380 ms reveal and are not physical shortcut-to-photon timings. |
| Palette animation | Existing tests confirm connected intermediate geometry, stable text layout, final hit testing, interrupted dismissal/reopen, reduced motion and negative-monitor coordinates. Preserve durations and appearance. |
| Whisper endpoint/inference | 450 ms wake silence, shared-engine semaphore and inference remain latency contributors. Shortening silence, lowering model quality or accepting unvalidated interim results risks clipped commands/false wakes; no such change was made. Busy wake checks currently discard overlapping segments. Changing that policy needs recorded speech/false-positive testing. |
| Q streaming and text | Q uses StringBuilder and bounded conversation history; the view model skips stale queued snapshots and has a bounded text-format cache. Per-chunk ToString, property fanout and dispatch can still cost CPU for long responses. No representative trace established a bottleneck; coalescing could change streaming behavior and needs separate latency tests. |
| Media and spectrum | Media separates identity/artwork updates from playback progress and retries artwork only within a bounded transition window. Polling adapts when idle. Spectrum runs on a background capture thread; visible animation cadence is preserved. Existing checks prove hidden visualizer timers stop and resume. Native shutdown joins have a two-second bound; not established as a steady-state issue. |
| Connectivity/system monitor | Wi-Fi discovery is already off-thread with one active refresh. Monitoring runs on a worker with adapter caching. Preserve configured freshness; lowering poll frequency would change product quality. |
| Weather/stocks | Existing tests pass for coalescing overlapping refreshes, latest-configuration wins, disabling/cancelling work and rejecting late publication. No duplicate-request rewrite needed. |
| Notifications/history | History already batches accepted notifications and queues persistence off-thread. Source polling/events have deduplication and timeout/restart ownership tests. History remains bounded to 100 items/seven days. No reduction in notification responsiveness. |
| Timer persistence | Timer state is serialized/written synchronously on actual state changes. This can stall a slow disk, but writes are not unconditional frame updates. Moving them requires preserving save ordering, shutdown durability and storage warnings; no measured recurring stall established here. Retain durability. |
| Settings/logging | Some synchronous file open/rename and logging operations remain. Settings are normalized and serialized through a gate. Future worker-writer changes need immutable snapshots and explicit flush/error behavior. No blind fire-and-forget persistence was introduced. |
| Screenshot/OCR | Screenshot copying already avoids a PNG round trip for local selection/preview, preserving pixels and alpha. Q screen capture uses a worker and cancellation; OCR remains awaited so providers retain the same text context. |
| Frost/materials | Existing bounded immutable cache is pixel-equivalent. Native backdrop caches geometry/material state before native updates. Preserve material quality and shadow/blur behavior. |
| Clock/battery/theme | Background-priority/configurable polling; battery only publishes changed state and clock targets time boundaries. No broad service-frequency reductions. |

## Measurements and test results

- Baseline existing optimized screenshot path: four synthetic 1920x1080 copies took 7.16 ms with 4,472 managed bytes, versus 922.54 ms and 100,103,336 bytes for a PNG round trip. This optimization predates this audit; it is evidence to preserve it, not a newly delivered speedup.
- Baseline cached frost material: 1,000 calls took 0.82 ms/80 managed bytes versus 63.61 ms/4,432,080 bytes uncached. Again, existing behavior.
- New audio/persistence checks pass in the final isolated performance suite, along with the existing pixel equivalence, hidden animation and widget-request checks.
- Command-panel WPF suite completed through its final no-binding-errors check. Includes normal/reduced motion, forms, output loading/layout, seek, clipboard, action/reply/mascot animation and palette presentation checks. Captures are retained in panels-after/captures.
- Unit tests: first run 741 pass/1 failure in FlowPipelineTests.PastesIntoTheWindowFromKeyDown from collection modification during status enumeration. Inspection shows the fixture stores statuses in a mutable List while asynchronous status publication can continue. Immediate rerun: all 742 pass. Record as an intermittent fixture race, not a clean first-pass result.
- Full Sutra and general upgrade WPF runs did not finish: both tried to show an already closed fixture window. The Sutra close trace passes through WM_CLOSE, without identifying the origin. Do not claim these suites passed or attribute closure to the product changes without more evidence. Targeted performance and panel suites provide the successful relevant coverage.
- A 10.01-second sample of the pre-install desktop app consumed 0.828 CPU seconds, with about 456 MB working set and 338 MB private bytes. Concurrent fixture activity and unknown live app state make this contextual only: it is neither an idle baseline nor an improvement claim.

## Reproduction

Use the local .dotnet/dotnet.exe to publish Release win-x64 to artifacts/root-app-update. Unit command: dotnet test DynamicIsland.Windows.Tests/DynamicIsland.Windows.Tests.csproj -c Release. For WPF checks set ISLAND_PREVIEW=1 and an absolute ISLAND_PREVIEW_DATA directory, then launch the published executable with --verify-command-panels. Performance fixtures additionally set ISLAND_VERIFY_PERFORMANCE=1 and use --verify-sutra-update. Clear that variable for other suites. Check verification-error.txt as well as the trailing check lines; early passing checks alone do not establish suite completion.

## Deployment and remaining validation limits

The final published executable is installed as the workspace root island.exe, with the prior executable backed up. Installed SHA-256: C2CAB582D6F70ED07A6900DD31E8016DCB3745BEC3499E0FE77756DB57B066E5, equal to the published file. The app is launched through Shell.Application with --settings as required. See artifacts/performance-deployment.json for process verification.

Final shell verification confirms process 29940 is running from the root island.exe, and its Sutra Island Settings window is visible and owned by that same process. Evidence: artifacts/performance-window-verification.json. Final performance checks: 49 passing lines. Command-panel checks: 224 passing lines. The published and installed hashes were compared again after restart and still match.

Remaining hardware-dependent validation: physical shortcut-to-first-frame/focus after idle/resume, cold/warm spoken wake latency and accuracy, one-breath commands with the user's microphone, and GPU frame-time traces under real playback/notifications. No claim is made that these are measured or that all perceived delays are eliminated. They require a working desktop inspection host and representative audio. All concrete low-risk optimizations selected above have been implemented; conditional opportunities are retained here with their evidence and quality tradeoffs.

# Follow-up optimization pass — streaming and persistence

## Plan and decisions

Investigated the remaining candidates recorded above: streaming property fanout; settings, timer and logging file work on callers; wake scheduling. Implemented four changes supported by direct code-path evidence, then added blocked-worker and state-transition fixtures. The earlier timer/settings/logging dispositions are historical and superseded by this section. Wake endpoint and overlap policy remain unchanged for the reasons below.

## Changes

1. **Streamed text binding fanout:** OnQChanged now recognizes consecutive Streaming snapshots whose only difference is Response. It refreshes response text, formatted text, copy availability and response actions, and still updates the compact voice presenter. It retains every delivered text update. State, mode, status, prompt, context, provider, model, error or action changes take the original full refresh. Completion therefore updates stop/copy/retry and the rest of the UI. This avoids reevaluating unchanged provider/model options, comparison selectors, structural state and notification pumping on each token. The existing stale-snapshot check is preserved; no throttle, artificial delay or animation-cadence reduction was added.

2. **Settings persistence:** SaveAsync normalizes and captures an immutable UTF-8 snapshot on the caller before placing filesystem work in an ordered worker queue. Directory creation, opening/writing the temporary file and replacement run on the pool even when earlier saves are already complete. This also prevents queued writes from serializing later unsaved mutations of a live settings object. Each returned task still completes after its write attempt, the existing error log behavior is retained, and normal app shutdown flushes queued settings. Serialization remains on the caller because the mutable settings graph is UI-owned; moving serialization without a safe snapshot would create races.

3. **Logging:** Event timestamps and sanitized lines are captured at the event, then rotation and append run in order on a worker shared by all LoggingService instances for the same path. All messages are retained; no sampling or dropping was introduced. FlushAsync is invoked at normal shutdown after service disposal. This removes synchronous log-file I/O from UI callbacks and, specifically, Whisper's accepted-wake callback before WakeDetected is raised.

4. **Timer persistence:** The timer engine still updates immediately and creates its immutable JSON snapshot on its owning dispatcher. File writing and replacement run in an ordered queue shared by services targeting the same full path. Failures are logged and marshal StorageWarning plus Changed back to the dispatcher. A disposed/shutting-down dispatcher is not required for the writer to finish. Disposal saves and flushes the final state before returning; read-only/future-version storage rules remain intact. Controls and tick timing do not wait on disk during normal operation.

A small OrderedBackgroundWork helper provides a TaskScheduler.Default continuation chain and flush task. Each operation runs even if a previous operation faulted. This centralizes ordering rather than creating independent Task.Run calls that could write an older snapshot last. No task depends on a UI continuation to finish, allowing shutdown to drain safely from the UI thread.

## Measurements and checks

Artifacts: artifacts/remaining-performance, artifacts/remaining-panels, artifacts/remaining-performance-tests. The initial follow-up fixture measured 100 synthetic text-only UI changes at 5.63 ms and 400 notifications versus the full refresh at 56.50 ms and 8,000 notifications. This is an isolated binding-fanout comparison, not a provider latency, real GPU frame-time or whole-app speedup claim. Every response update and final state/action change were checked.

With the settings worker deliberately held by an incomplete task, two SaveAsync calls and an input-priority dispatcher callback finished in 0.8 ms. Both saves remained pending until released; the final file contained the second requested snapshot rather than a subsequent unsaved mutation. A blocked log worker accepted 40 messages; flushing retained all 40 in order. Timer controls updated during a blocked write and disposal flushed the final state. Additional coverage deliberately targets a directory as a timer file to verify that background write failure still produces a visible storage warning.

The unit suite passed all 744 tests on this pass, including two new ordered-work tests: a blocked first operation followed by 100 ordered operations and a faulted operation followed by successful work. The existing command-panel suite passed through its final no-binding-errors check on the first follow-up build. Final-build rerun results and deployment evidence are recorded below after completion.

## Wake scheduling outcome

Reviewed StartWake, CheckWakeAsync, StopWake, SpeechInputService and AssistantVoiceController. The existing session epoch, cancellation token, current-configuration check and cooldown protect against audio completing after stop/restart or settings changes. While one transcription is in flight, another completed segment is discarded. Replacing this with an audio queue could make the assistant execute stale speech after the user changes sessions or focus; changing end-silence or transcription model would need acoustic/false-wake testing. No evidence establishes a safe quality-neutral change to those policies in this environment. The earlier model warm-up plus this pass's off-thread log write remove avoidable work without weakening those safeguards. The 744-test run includes wake-session invalidation and voice training/recognition policy regressions; it is not a live microphone speed test.

## Practical limits

Queued disk work preserves requested order and is flushed on normal application shutdown. Forced termination, power loss or an indefinitely stalled filesystem can interrupt pending work; asynchronous writes do not provide crash-proof storage. These queues intentionally retain every request rather than silently discarding state/log entries; an extended disk stall with sustained requests can grow the backlog. Returned settings-save tasks and explicit flushes remain the completion points for callers requiring persistence. Normal timer disposal can wait for storage at shutdown, while interactive timer commands remain responsive. No claim is made that serialization itself or external filesystem stalls have zero cost.

## Final follow-up result

Final Release publish completed without compiler warnings. Final isolated performance fixture: **59 passing checks**, through its final performance-metrics completion line, with no verification-error.txt. Final command-panel fixture: **224 passing checks**, through its final no-binding-errors line, with no verification-error.txt. Unit suite: **744 passed, zero failed**. git diff --check completed without whitespace errors (Git printed line-ending normalization notices).

The final streaming fixture measured 100 synthetic UI changes at **5.52 ms / 400 property notifications**, versus **55.39 ms / 8,000 notifications** through the original full refresh. Text-only updates and completion/action correctness checks passed. Blocked settings work plus input dispatch measured **0.8 ms**. These remain fixture measurements rather than end-to-end desktop latency measurements.

The final build is installed as the workspace root island.exe and matches the published file by SHA-256: **F77FFBCFEF5A097B28789353E3585084555DD9CCE4842C83848C8336B9AD276A**. The previous executable was backed up with a before-streaming-persistence timestamp. After desktop-shell relaunch with --settings, process **42084** is running from that exact root path. Its **Sutra Island Settings** window is visible and owned by process **42084**, verified after launch. Evidence: artifacts/remaining-performance-deployment.json.

This completes the selected streaming/persistence follow-up plan. It does not establish that every possible future optimization is exhausted or that physical microphone/shortcut latency is fully resolved. The remaining acoustic, GPU and hardware-dependent validation limits from the audit still apply.
