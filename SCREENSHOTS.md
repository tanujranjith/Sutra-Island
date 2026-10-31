# Screenshot tool

Open **Screenshot** in the expanded island menu, type `screenshot` (also `capture` / `snip`) in the command palette, or press **Ctrl+Alt+S**. The capture overlay opens immediately with region selection armed; drag a region, click **Window** and then a window, or choose **Full screen** and click anywhere or press Enter. The compact toolbar stays inside the overlay, so mode changes never send you back to the Island. Escape or right-click cancels without saving or copying. Change the shortcut in Settings → Shortcuts & behavior; conflicts are shown there.

Captures automatically copy to the clipboard and show a brief confirmation in the preview. The preview offers Copy, Save PNG, freehand Draw, Undo, and Pin, then closes after ten seconds or when you click outside it. Copy/Save/Pin include your annotations at original pixel dimensions. Pinned images can be dragged, resized, closed with Escape, or double-clicked. A new capture replaces the previous preview.

Images stay in memory until explicitly saved. The tool does not perform OCR, call an AI provider, upload pixels, or add telemetry. Clipboard data requests exclusion from Windows clipboard history and cloud synchronization. Other applications with clipboard access remain under the user's control.

RAM and disk bounds: no screenshot cache, no automatic files, one capture operation at a time, at most three pinned images, and a 128 MiB pinned-pixel budget. Very large captures above 100 million pixels are rejected; use a smaller region. Small region captures retain only selected pixels. Capture and preview use direct pixel copies without intermediate PNG buffers; the desktop buffer is released before clipboard retries or preview presentation. Saving encodes PNG on a worker thread. No screenshots are downsampled for export.

Selection uses physical desktop pixels, including negative monitor positions and regions spanning monitors. The frozen desktop is grabbed before the overlay appears, so the Island and capture toolbar are excluded from the result. Window capture uses the visible window frame clipped to the desktop; covered or protected content may not be available. Mixed-DPI layouts are handled with per-monitor physical coordinates, but validate unusual monitor arrangements on the intended hardware.

The fixed Settings shortcut is **Ctrl+Alt+Shift+S**. Ctrl+Alt+S opens the capture overlay by default.
