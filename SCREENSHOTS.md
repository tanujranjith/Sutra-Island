# Screenshot tool

Open **Screenshot** in the expanded island menu or type `screenshot` (also `capture` / `snip`) in the command palette to choose Region, Active window, or All screens. **Ctrl+Alt+S** opens this capture-mode chooser first; capture starts only after you choose a mode. Change it in Settings → Shortcuts & behavior; shortcut conflicts are shown there. Escape or right-click cancels selection.

Captures automatically copy to the clipboard. The preview offers Copy, Save PNG, freehand Draw, Undo, and Pin. Copy/Save/Pin include your annotations at original pixel dimensions. Pinned images can be dragged, resized, closed with Escape, or double-clicked. A new capture replaces the previous preview.

Images stay in memory until explicitly saved. The tool does not perform OCR, call an AI provider, upload pixels, or add telemetry. Clipboard data requests exclusion from Windows clipboard history and cloud synchronization. Other applications with clipboard access remain under the user's control.

RAM and disk bounds: no screenshot cache, no automatic files, one capture operation at a time, at most three pinned images, and a 128 MiB pinned-pixel budget. Very large captures above 100 million pixels are rejected; use a smaller region. Small region captures retain only selected pixels. Capture and preview use direct pixel copies without intermediate PNG buffers; the desktop buffer is released before clipboard retries or preview presentation. Saving encodes PNG on a worker thread. No screenshots are downsampled for export.

Selection uses physical desktop pixels, including negative monitor positions and regions spanning monitors. Active-window capture is the visible window rectangle clipped to the desktop; covered or protected content may not be available. Validate mixed-DPI layouts on the intended monitors.

The fixed Settings shortcut is **Ctrl+Alt+Shift+S**. Ctrl+Alt+S opens the screenshot mode chooser by default.
