# Screenshot overlay paint benchmark

Measures screenshot overlay painting against a synthetic desktop image at the current virtual-screen size (capped at 20 million pixels). It creates a hidden form and invokes its paint and mouse handlers directly; it does not capture desktop contents, show the overlay, move the real pointer, or choose a screenshot.

Build the app in Release, then build this standalone benchmark:

```powershell
.\.dotnet\dotnet.exe build .\scripts\OverlayPaintBench\OverlayPaintBench.csproj -c Release
.\.dotnet\dotnet.exe .\scripts\OverlayPaintBench\bin\Release\net10.0-windows10.0.19041.0\OverlayPaintBench.dll .\DynamicIsland.Windows\bin\Release\net10.0-windows10.0.19041.0\win-x64\DynamicIsland.Windows.dll .\artifacts\overlay-paint.json 80
```

To compare versions, preserve each version's built `DynamicIsland.Windows.dll` and `DynamicIsland.Q.Core.dll` together, and run the benchmark against each assembly path in separate processes. Run the pair sequentially without concurrent builds for comparable CPU load.

The JSON reports warmed median and p95 paint CPU time, mouse-handler time, and dirty-clip area. It excludes input-queue latency, WinForms buffer presentation, DWM presentation, and hardware cursor latency. A repeated endpoint can legitimately produce no invalidation.
