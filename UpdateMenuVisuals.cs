using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class UpdateMenuPalette
    {
        public Color Surface;
        public Color SurfaceAlt;
        public Color Border;
        public Color Text;
        public Color MutedText;
        public Color Accent;
        public Color Hover;
        public Color Danger;
        public Color DangerHover;
    }

    internal static class UpdateMenuVisuals
    {
        internal const string HeaderTag = "update-menu-header";
        internal const string DangerTag = "update-menu-danger";

        internal static UpdateMenuPalette CreateRainbowPalette()
        {
            return new UpdateMenuPalette
            {
                Surface = Color.FromArgb(248, 249, 252),
                SurfaceAlt = Color.FromArgb(248, 249, 252),
                Border = Color.FromArgb(218, 223, 233),
                Text = Color.FromArgb(55, 65, 81),
                MutedText = Color.FromArgb(100, 110, 125),
                Accent = Color.FromArgb(109, 65, 190),
                Hover = Color.FromArgb(240, 235, 252),
                Danger = Color.FromArgb(165, 48, 76),
                DangerHover = Color.FromArgb(255, 237, 242)
            };
        }

        internal static void Apply(
            ContextMenuStrip menu,
            ToolStripMenuItem versionItem,
            ToolStripMenuItem checkItem,
            ToolStripMenuItem downloadItem,
            ToolStripMenuItem exitItem,
            float scale)
        {
            int width = Scale(148, scale);
            int horizontalPadding = Scale(5, scale);
            int itemWidth = width - horizontalPadding * 2;
            UpdateMenuPalette palette = CreateRainbowPalette();
            menu.AutoSize = false;
            menu.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
            menu.Padding = new Padding(horizontalPadding, Scale(3, scale), horizontalPadding, Scale(10, scale));
            menu.MinimumSize = Size.Empty;
            menu.Size = new Size(width, Scale(174, scale));
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.Text;
            menu.Renderer = new OverlayUpdateMenuRenderer(palette, scale);

            ConfigureItem(versionItem, itemWidth, Scale(31, scale), Scale(12, scale));
            ConfigureItem(checkItem, itemWidth, Scale(35, scale), Scale(12, scale));
            ConfigureItem(downloadItem, itemWidth, Scale(35, scale), Scale(12, scale));
            ConfigureItem(exitItem, itemWidth, Scale(35, scale), Scale(12, scale));
            versionItem.Tag = HeaderTag;
            exitItem.Tag = DangerTag;

            for (int index = 0; index < menu.Items.Count; index++)
            {
                ToolStripSeparator separator = menu.Items[index] as ToolStripSeparator;
                if (separator != null)
                {
                    separator.Available = index == 1;
                    separator.AutoSize = false;
                    separator.Size = new Size(itemWidth, Scale(5, scale));
                    separator.Margin = Padding.Empty;
                }
                else
                    ConfigureItem(menu.Items[index], itemWidth,
                        Scale(menu.Items[index] == versionItem ? 25 : 30, scale), Scale(8, scale));
            }
            int height = menu.Padding.Vertical + Scale(3, scale);
            foreach (ToolStripItem item in menu.Items)
                if (item.Available) height += item.Height;
            menu.Size = new Size(width, height);
        }

        internal static double ContrastRatio(Color first, Color second)
        {
            double light = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
            double dark = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
            return (light + 0.05d) / (dark + 0.05d);
        }

        internal static Rectangle CenteredPillBounds(
            int menuWidth, int itemLeft, int itemWidth, int itemHeight, float scale)
        {
            int desiredWidth = Math.Min(itemWidth - 8,
                Math.Max(88, (int)Math.Round(112 * Math.Max(0.75f, scale))));
            int left = (menuWidth - desiredWidth) / 2 - itemLeft;
            return new Rectangle(left, 2, Math.Max(1, desiredWidth),
                Math.Max(1, itemHeight - 4));
        }

        private static void ConfigureItem(ToolStripItem item, int width, int height, int leftPadding)
        {
            item.AutoSize = false;
            item.Size = new Size(width, height);
            item.Margin = Padding.Empty;
            item.Padding = new Padding(leftPadding, 0, Scale(7, width / 252f), 0);
            item.TextAlign = ContentAlignment.MiddleLeft;
        }

        private static int Scale(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * Math.Max(0.75f, scale)));
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126d * Linear(color.R) + 0.7152d * Linear(color.G) + 0.0722d * Linear(color.B);
        }

        private static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= 0.03928d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }
    }

    internal sealed class OverlayUpdateContextMenu : ContextMenuStrip
    {
        private readonly Timer dismissTimer;
        private Rectangle anchorBounds;
        private DateTime? pointerOutsideSince;

        internal OverlayUpdateContextMenu()
        {
            dismissTimer = new Timer();
            dismissTimer.Interval = 100;
            dismissTimer.Tick += CheckPointerLocation;
        }

        internal void SetAnchor(Rectangle bounds)
        {
            anchorBounds = bounds;
            pointerOutsideSince = null;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            pointerOutsideSince = null;
            dismissTimer.Start();
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            dismissTimer.Stop();
            pointerOutsideSince = null;
            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                dismissTimer.Dispose();
            base.Dispose(disposing);
        }

        private void CheckPointerLocation(object sender, EventArgs e)
        {
            Point pointer = Control.MousePosition;
            Rectangle anchor = anchorBounds;
            anchor.Inflate(6, 6);
            if (Bounds.Contains(pointer) || anchor.Contains(pointer))
            {
                pointerOutsideSince = null;
                return;
            }
            if (!pointerOutsideSince.HasValue)
                pointerOutsideSince = DateTime.UtcNow;
            else if (DateTime.UtcNow - pointerOutsideSince.Value >= TimeSpan.FromMilliseconds(250))
                Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;
            using (GraphicsPath path = OverlayUpdateMenuRenderer.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), Math.Max(12, Width / 15)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null)
                    old.Dispose();
            }
        }
    }

    internal sealed class OverlayUpdateMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly UpdateMenuPalette palette;
        private readonly float scale;

        internal OverlayUpdateMenuRenderer(UpdateMenuPalette palette, float scale)
        {
            this.palette = palette;
            this.scale = Math.Max(0.75f, scale);
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds, palette.SurfaceAlt, palette.Surface, LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(background, bounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(0, 0,
                Math.Max(1, e.ToolStrip.Width - 1), Math.Max(1, e.ToolStrip.Height - 1));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRectangle(bounds, Math.Max(12, e.ToolStrip.Width / 15)))
            using (Pen border = new Pen(palette.Border, 1f))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (IsHeader(e.Item))
                return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = PillBounds(e.Item);
            using (GraphicsPath path = RoundedRectangle(bounds, Math.Max(2, bounds.Height / 2)))
            using (Brush hover = new SolidBrush(e.Item.Selected ? (IsDanger(e.Item) ? palette.DangerHover : palette.Hover) : Color.White))
            using (Pen border = new Pen(palette.Border))
            {
                e.Graphics.FillPath(hover, path);
                e.Graphics.DrawPath(border, path);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            Rectangle bounds = PillBounds(e.Item);
            using (Pen separator = new Pen(Color.FromArgb(120, palette.Border), 1f))
                e.Graphics.DrawLine(separator, bounds.Left, y, bounds.Right, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            int menuWidth = e.Item.Owner == null ? e.Item.Width : e.Item.Owner.Width;
            Rectangle bounds = IsHeader(e.Item)
                ? new Rectangle(-e.Item.Bounds.Left, 0, menuWidth, e.Item.Height)
                : PillBounds(e.Item);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter;
            Color textColor = IsDanger(e.Item) ? palette.Danger : palette.Text;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds,
                textColor, flags);
        }

        private Rectangle PillBounds(ToolStripItem item)
        {
            int menuWidth = item.Owner == null ? item.Width : item.Owner.Width;
            return UpdateMenuVisuals.CenteredPillBounds(
                menuWidth, item.Bounds.Left, item.Width, item.Height, scale);
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static bool IsHeader(ToolStripItem item)
        {
            return String.Equals(item.Tag as string, UpdateMenuVisuals.HeaderTag, StringComparison.Ordinal);
        }

        private static bool IsDanger(ToolStripItem item)
        {
            return String.Equals(item.Tag as string, UpdateMenuVisuals.DangerTag, StringComparison.Ordinal);
        }
    }
}
