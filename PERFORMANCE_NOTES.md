# Performance review — v1.2.4

The review covered rendering, media/FFT, service polling, persistence, screenshot buffers, startup/shutdown, command/settings search, timers, and assistant/provider lifetimes. An independent integration review checked the resulting changes.

The plan was to remove repeated work first, bound any added caches, preserve pixels and animation settings, then verify the changed lifetimes and full interface. No resolution, FFT size, playback refresh interval, animation duration, or export-quality reduction was used.

## Implemented

- **Audio:** compute the 4,096 Hann coefficients once, using the exact previous expression. This adds a fixed 32 KiB table and removes 4,096 cosine evaluations per FFT analysis. Analysis cadence, overlapping sample windows, smoothing, and bar motion remain unchanged.
- **Animation:** stop the 33 ms dispatcher timer when the visualizer is idle, disabled, or hidden. Restart its frame stopwatch on resume. Reuse identical frozen frost materials in a bounded 128-entry cache and avoid repeated native hide calls while frost is suspended. Existing expansion, notification, timer, and palette timings remain unchanged.
- **Assistant:** reuse formatted replies in a per-view-model three-entry cache. Input/output strings above 128 Ki characters are not cached; clearing chat or disposing the view model clears its cache. Streaming replaces old entries rather than retaining a transcript of intermediate responses. The earlier screenshot pass also removed a duplicate response-string allocation per streamed chunk.
- **Screenshots:** share the existing GDI capture operation, then copy BGRA pixels directly to a detached frozen WPF image. This removes in-memory PNG encoding/decoding from capture and cropping. Release the full desktop bitmap before clipboard retries or preview presentation. Save still encodes the original pixels as PNG on a worker thread.
- **Disk:** insert each notification batch in its previous acceptance order, then queue one history snapshot/write for that batch. Keep seven-day retention, the 100-item bound, duplicate filtering, and flush semantics. Mutations and snapshot capture are serialized.
- **Widgets:** disabled weather and stock widgets stop timers and cancel pending HTTP work while retaining cached values. Enabled services allow one refresh batch at a time, cancel obsolete configurations, and publish only the latest configuration. Re-enabling immediately requests fresh data. Connectivity polling also stops when disabled and coalesces a refresh if re-enabled during an existing poll. Configured enabled polling intervals remain unchanged.
- **Search:** precompute the fixed settings-search corpus. Keep ordinal case-insensitive matching, all-word matching, original result order, and the eight-result cap.
- **Startup:** use a registered Settings-activation wait instead of keeping a worker blocked for the application lifetime; unregister before disposing its signal. Present requested Settings before awaiting media-manager discovery.
- **Codex lifecycle:** bind pending requests and queued writes to their originating subprocess. Ignore retired readers/exits so they cannot fail requests or reset initialization on a replacement connection. Scope temporary images and owned-thread cleanup before image preparation, including cancellation and write failures; allow pending gate releases to finish during disposal.
- **Existing safeguards retained:** empty network-adapter results are cached with network-change invalidation; screenshot pins are bounded to three/128 MiB; small crops retain only selected pixels; selection dragging invalidates only changed shading/edges; screenshots create no automatic files or telemetry.

## Measurements

Local Release measurements on .NET 10.0.10, October 28, 2026. These compare the original and replacement operations on identical synthetic input in one process, after warmup. They are not total app RAM or FPS measurements.

| Operation | Original work | Reused/direct work |
| --- | --- | --- |
| Four 1920 × 1080 BGRA images: PNG encode/decode versus direct pixel copy | 846.19 ms; 100,092,656 managed bytes allocated | 9.12 ms; 3,784 managed bytes allocated |
| 1,000 identical frost-material requests: construct versus reuse | 114.24 ms; 4,432,080 managed bytes allocated | 0.445 ms; 80 managed bytes allocated |

The screenshot fixture uses high-entropy pixels, so PNG compression cost can exceed an ordinary desktop's cost. Timings exclude screen acquisition, clipboard integration, UI presentation, and PNG saving. Native GDI/WIC pixel allocations still exist; the managed-allocation column does not measure those buffers or resident memory. Material reuse retains a bounded set of frozen brushes instead of recreating them per binding/morph.

## Verification

- 509 unit tests passed, including every cached Hann coefficient's IEEE-754 bits versus the original formula, search behavior, bounded text-cache lifetimes, history ordering/limits, and concurrent history additions with flushes. Thirteen focused Codex lifecycle cases cover retired readers/exits/messages, queued writes across replacement/disposal, active failures/approval denial, and temporary-image cleanup during preparation failures or cancellation. They use controlled in-memory pipes and local fixture files, without launching Codex or contacting providers.
- 45 checks passed in the isolated native performance run: exact screenshot BGRA/alpha/crop pixels, source modification/disposal independence, bottom-up rows, pixel-identical frost materials, timer stop/resume, and fake-transport widget refresh/cancellation/reenable/disposal races. These fixtures do not capture the user's desktop or contact providers.
- 477 additional native interface checks passed: Sutra/notifications/media (120), command panels/assistant (223), graphite/material/settings (123), and screenshot preview/annotation/clipboard (11). Rendered island and assistant fixtures were visually inspected. Native frost separately hid over Settings and resumed across three cycles; three cold/warm expansion measurements reached the full 992-DIP fixture width.
- Reproduce the native performance run with `ISLAND_PREVIEW=1`, an absolute isolated `ISLAND_PREVIEW_DATA`, and `ISLAND_VERIFY_PERFORMANCE=1`, then run `--verify-sutra-update`. It writes `captures/performance-metrics.json` and `captures/sutra-checks.txt` under the isolated data directory.

## Further work requiring separate measurement

Timer list synchronization can scan quadratically with unusually large lists, and stopwatch laps are not virtualized. A dedicated large-list fixture should establish a benefit before changing scrolling or collection behavior. The large mascot decode was retained to preserve high-DPI rendering and existing animation sharpness.

The Codex reconnect and temporary-image cleanup findings were corrected during the final release review and covered by focused failure fixtures. Live provider reconnect and physical mixed-DPI/multi-monitor selection remain outside the synthetic checks.

References: [Microsoft WPF object performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-object-behavior), [WPF memory-backed bitmap implementation](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Imaging/CachedBitmap.cs), [Windows clipboard privacy formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats).
