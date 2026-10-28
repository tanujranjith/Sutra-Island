using System.Drawing;
using Forms = System.Windows.Forms;

namespace DynamicIsland.Windows.Services;

internal sealed class GraphiteTrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    internal static readonly Color Surface = Color.FromArgb(29,31,37);
    internal static readonly Color Text = Color.FromArgb(244,242,244);
    internal static readonly Color Muted = Color.FromArgb(153,152,164);
    internal static readonly Color Accent = Color.FromArgb(185,165,253);
    public GraphiteTrayMenuRenderer() : base(new GraphiteColors()) { RoundedEdges = true; }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    { e.TextColor = e.Item.Enabled ? Text : Muted; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    { e.ArrowColor = e.Item?.Enabled != false ? Text : Muted; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var box=e.ImageRectangle;
        using var pen=new Pen(Accent,1.8f) { StartCap=System.Drawing.Drawing2D.LineCap.Round, EndCap=System.Drawing.Drawing2D.LineCap.Round };
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen,[new PointF(box.Left+2,box.Top+box.Height*.52f),new PointF(box.Left+box.Width*.4f,box.Bottom-3),new PointF(box.Right-2,box.Top+3)]);
    }
    private sealed class GraphiteColors : Forms.ProfessionalColorTable
    {
        public GraphiteColors() { UseSystemColors=false; }
        private static Color Hover => Color.FromArgb(54,47,66);
        private static Color Edge => Color.FromArgb(58,55,67);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Edge;
        public override Color MenuItemBorder => Color.FromArgb(94,82,117);
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Edge;
        public override Color SeparatorLight => Edge;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
