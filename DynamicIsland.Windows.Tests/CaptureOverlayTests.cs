using System.Drawing;
using DynamicIsland.Windows.Services;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CaptureOverlayTests
{
    [Fact]
    public void ReverseDragIsNormalizedInsideTheOverlay()
    {
        var region = CaptureOverlayGeometry.NormalizeSelection(
            new Point(500, 400), new Point(120, 80), new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new Rectangle(120, 80, 380, 320), region);
    }

    [Fact]
    public void SelectionNearAnEdgeIsClampedInsteadOfGrowingOutsideTheDesktop()
    {
        var region = CaptureOverlayGeometry.NormalizeSelection(
            new Point(1900, 1060), new Point(2600, 1400), new Rectangle(0, 0, 1920, 1080));

        Assert.Equal(new Rectangle(1900, 1060, 20, 20), region);
    }

    [Fact]
    public void NegativeMonitorCoordinatesMapToTheFrozenBitmapOrigin()
    {
        var desktop = new Rectangle(-1920, -200, 3840, 1280);
        var selectionOnLeftMonitor = new Rectangle(-1500, 100, 300, 200);

        var bitmapRegion = CaptureOverlayGeometry.ScreenToClient(selectionOnLeftMonitor, desktop);

        Assert.Equal(new Rectangle(420, 300, 300, 200), bitmapRegion);
    }

    [Fact]
    public void PartiallyOffscreenWindowIsClippedToTheDesktop()
    {
        var desktop = new Rectangle(-2560, 0, 5120, 1440);
        var window = new Rectangle(-2700, 100, 500, 400);

        var bitmapRegion = CaptureOverlayGeometry.ScreenToClient(window, desktop);

        Assert.Equal(new Rectangle(0, 100, 360, 400), bitmapRegion);
    }

    [Fact]
    public void WindowSelectionUsesTopmostWindowFirst()
    {
        var ordered = new[]
        {
            new Rectangle(100, 100, 400, 300),
            new Rectangle(0, 0, 1920, 1080)
        };

        Assert.Equal(0, CaptureOverlayGeometry.HitTest(ordered, new Point(150, 150)));
        Assert.Equal(1, CaptureOverlayGeometry.HitTest(ordered, new Point(1800, 900)));
        Assert.Equal(-1, CaptureOverlayGeometry.HitTest(ordered, new Point(-10, 900)));
    }

    [Fact]
    public void ToolbarCentersOnANegativeCoordinateMonitor()
    {
        var location = CaptureOverlayGeometry.PlaceToolbar(
            new Rectangle(-1920, 0, 1920, 1080), new Size(360, 52), 24);

        Assert.Equal(-1140, location.X);
        Assert.Equal(24, location.Y);
    }

    [Fact]
    public void ToolbarStaysInsideASmallMonitor()
    {
        var location = CaptureOverlayGeometry.PlaceToolbar(
            new Rectangle(0, 0, 320, 200), new Size(360, 52), 16);

        Assert.Equal(0, location.X);
        Assert.Equal(16, location.Y);
    }

    [Theory]
    [InlineData(96u, 40, 40)]
    [InlineData(120u, 40, 50)]
    [InlineData(144u, 40, 60)]
    [InlineData(192u, 40, 80)]
    public void PhysicalMeasurementsScaleWithMonitorDpi(uint dpi, int designPixels, int expected)
    {
        Assert.Equal(expected, CaptureOverlayGeometry.ScaleForDpi(designPixels, dpi));
    }

    [Fact]
    public void DpiScaleNeverDropsBelowPhysicalPixels()
    {
        Assert.Equal(1, CaptureOverlayGeometry.ScaleForDpi(1, 72));
    }
}
