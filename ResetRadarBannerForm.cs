using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class ResetRadarBannerForm : Form
    {
        public const int LogicalHeight = 48;
        public const int LogicalGap = 5;
        public const int LogicalWidth = 450;

        private readonly Action openSource;
        private readonly Action closeRequested;
        private ResetRadarData radar = new ResetRadarData();
        private OverlaySettings settings = new OverlaySettings();
        private string renderedRevision = String.Empty;
        private Rectangle renderedBounds = Rectangle.Empty;
        private float dpiScale = 1f;
        private bool hovered;
        private bool closeHovered;
        private DateTimeOffset? previewNow;

        public ResetRadarBannerForm(Action openSource, Action closeRequested)
        {
            this.openSource = openSource;
            this.closeRequested = closeRequested;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Cursor = Cursors.Hand;
            Width = 720;
            Height = LogicalHeight;
        }

        public static bool ShouldShow(ResetRadarData data)
        {
            return ResetRadarDisplay.ShouldShow(data, DateTimeOffset.Now);
        }

        public void UpdateBanner(ResetRadarData data, OverlaySettings visualSettings, Rectangle bounds, float scale)
        {
            if (!ShouldShow(data))
            {
                HideBanner();
                return;
            }

            radar = data.Clone();
            settings = visualSettings.Clone();
            dpiScale = Math.Max(0.5f, scale);
            bool boundsChanged = bounds != renderedBounds;
            if (boundsChanged)
            {
                SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height, BoundsSpecified.All);
                renderedBounds = bounds;
            }

            bool scheduled = radar.Status == ResetRadarStatus.ScheduledToday ||
                radar.Status == ResetRadarStatus.ScheduledUpcoming;
            string clockRevision = scheduled && radar.EffectiveAt.HasValue
                ? DateTimeOffset.Now.ToString("yyyyMMddHHmmss")
                : String.Empty;
            string revision = radar.RevisionKey + "|" + settings.Theme + "|" +
                settings.CustomBackgroundArgb.ToString() + "|" + settings.FontName + "|" +
                dpiScale.ToString("0.###") + "|" +
                (hovered ? "hover" : "normal") + "|" + (closeHovered ? "close" : "open") + "|" + clockRevision;
            bool becameVisible = !Visible;
            if (becameVisible)
            {
                Show();
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }
            if (becameVisible || boundsChanged || !String.Equals(revision, renderedRevision, StringComparison.Ordinal))
            {
                RenderLayered();
                renderedRevision = revision;
            }
        }

        public void HideBanner()
        {
            hovered = false;
            closeHovered = false;
            renderedRevision = String.Empty;
            if (Visible)
                Hide();
        }

        internal void ExportPreviews(
            string outputDirectory,
            ResetRadarData previewRadar,
            OverlaySettings previewSettings,
            DateTimeOffset displayNow)
        {
            ResetRadarData originalRadar = radar;
            OverlaySettings originalSettings = settings;
            string originalRevision = renderedRevision;
            Rectangle originalBounds = renderedBounds;
            float originalDpiScale = dpiScale;
            bool originalHovered = hovered;
            bool originalCloseHovered = closeHovered;
            DateTimeOffset? originalPreviewNow = previewNow;
            Size originalSize = Size;

            Directory.CreateDirectory(outputDirectory);
            try
            {
                radar = previewRadar.Clone();
                settings = previewSettings.Clone();
                dpiScale = 1f;
                previewNow = displayNow;
                Size = new Size(LogicalWidth, LogicalHeight);

                hovered = false;
                closeHovered = false;
                using (Bitmap normal = BuildRenderedBitmap())
                    normal.Save(Path.Combine(outputDirectory, "reset-radar-banner.png"), ImageFormat.Png);

                hovered = true;
                closeHovered = true;
                using (Bitmap close = BuildRenderedBitmap())
                    close.Save(Path.Combine(outputDirectory, "reset-radar-banner-close.png"), ImageFormat.Png);
            }
            finally
            {
                radar = originalRadar;
                settings = originalSettings;
                renderedRevision = originalRevision;
                renderedBounds = originalBounds;
                dpiScale = originalDpiScale;
                hovered = originalHovered;
                closeHovered = originalCloseHovered;
                previewNow = originalPreviewNow;
                Size = originalSize;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE |
                    NativeMethods.WS_EX_LAYERED;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!hovered)
            {
                hovered = true;
                renderedRevision = String.Empty;
                RenderLayered();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered || closeHovered)
            {
                hovered = false;
                closeHovered = false;
                renderedRevision = String.Empty;
                RenderLayered();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point logicalLocation = ToLogicalPoint(e.Location);
            bool nextCloseHovered = ResetRadarBannerVisuals.CloseHitBounds(LogicalCanvasWidth, LogicalCanvasHeight).Contains(logicalLocation);
            if (nextCloseHovered != closeHovered)
            {
                closeHovered = nextCloseHovered;
                renderedRevision = String.Empty;
                RenderLayered();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;
            if (ResetRadarBannerVisuals.CloseHitBounds(LogicalCanvasWidth, LogicalCanvasHeight).Contains(ToLogicalPoint(e.Location)))
            {
                if (closeRequested != null)
                    closeRequested();
                return;
            }
            if (openSource != null)
                openSource();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_NCHITTEST)
            {
                message.Result = (IntPtr)NativeMethods.HTCLIENT;
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Rendering is handled by UpdateLayeredWindow for per-pixel alpha.
        }

        private void RenderLayered()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
                return;
            using (Bitmap bitmap = BuildRenderedBitmap())
                NativeMethods.UpdateLayeredBitmap(Handle, bitmap, Left, Top);
        }

        private Bitmap BuildRenderedBitmap()
        {
            Bitmap bitmap = UiRendering.CreateLayeredBitmap(Width, Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);
                graphics.ScaleTransform(dpiScale, dpiScale);

                int canvasWidth = Math.Max(1, (int)Math.Round(Width / dpiScale));
                int canvasHeight = Math.Max(1, (int)Math.Round(Height / dpiScale));
                Rectangle card = new Rectangle(1, 1, Math.Max(1, canvasWidth - 3), Math.Max(1, canvasHeight - 3));
                Color dot = ResetRadarBannerVisuals.StatusDot(radar.Status);
                int radius = Math.Max(1, card.Height / 2);

                using (GraphicsPath shadowPath = RoundedRectangle(new Rectangle(0, 0, canvasWidth - 1, canvasHeight - 1), radius))
                using (Brush shadow = new SolidBrush(QuotaPillVisuals.Shadow))
                    graphics.FillPath(shadow, shadowPath);
                using (GraphicsPath cardPath = RoundedRectangle(card, radius))
                using (Brush background = new SolidBrush(QuotaPillVisuals.Surface))
                using (Pen borderPen = new Pen(QuotaPillVisuals.Border, 1f))
                {
                    graphics.FillPath(background, cardPath);
                    graphics.DrawPath(borderPen, cardPath);
                }
                using (Brush dotBrush = new SolidBrush(dot))
                using (Brush halo = new SolidBrush(Color.FromArgb(35, dot.R, dot.G, dot.B)))
                {
                    graphics.FillEllipse(halo, 12, 9, 10, 10);
                    graphics.FillEllipse(dotBrush, 14, 11, 6, 6);
                }

                Rectangle titleBounds = ResetRadarBannerVisuals.TitleBounds(canvasWidth, canvasHeight);
                Rectangle detailBounds = ResetRadarBannerVisuals.DetailBounds(canvasWidth, canvasHeight);
                using (Font titleFont = CreateBannerFont(settings.FontName, 8.5f))
                using (Font detailFont = UiRendering.CreateTextFont(settings.FontName, 7.8f, FontStyle.Regular))
                using (Brush titleBrush = new SolidBrush(QuotaPillVisuals.Text))
                using (Brush detailBrush = new SolidBrush(QuotaPillVisuals.MutedText))
                using (StringFormat titleFormat = UiRendering.CreateTextFormat())
                using (StringFormat detailFormat = UiRendering.CreateTextFormat())
                {
                    titleFormat.Alignment = StringAlignment.Near;
                    titleFormat.LineAlignment = StringAlignment.Center;
                    titleFormat.Trimming = StringTrimming.EllipsisCharacter;
                    titleFormat.FormatFlags |= StringFormatFlags.NoWrap;
                    detailFormat.Alignment = StringAlignment.Near;
                    detailFormat.LineAlignment = StringAlignment.Center;
                    detailFormat.Trimming = StringTrimming.EllipsisCharacter;
                    detailFormat.FormatFlags |= StringFormatFlags.NoWrap;

                    DateTimeOffset displayNow = previewNow ?? DateTimeOffset.Now;
                    string title = "TIBO RADAR · " +
                        ResetRadarDisplay.BuildHeadline(radar, displayNow) +
                        ResetRadarDisplay.ConfidenceSuffix(radar) + " · 非官方";
                    string detail = ResetRadarDisplay.BuildPrimaryLine(radar, displayNow);
                    graphics.DrawString(title, titleFont, titleBrush, titleBounds, titleFormat);
                    graphics.DrawString(detail, detailFont, detailBrush, detailBounds, detailFormat);
                }

                {
                    Rectangle closeButton = ResetRadarBannerVisuals.CloseButtonBounds(canvasWidth, canvasHeight);
                    using (Brush closeBackground = new SolidBrush(closeHovered
                        ? QuotaPillVisuals.DangerHover : QuotaPillVisuals.Surface))
                    using (Pen closeBorder = new Pen(QuotaPillVisuals.Border, 1f))
                    using (Pen closePen = new Pen(closeHovered ? QuotaPillVisuals.DangerText : QuotaPillVisuals.Text, 1.5f))
                    {
                        closePen.StartCap = LineCap.Round;
                        closePen.EndCap = LineCap.Round;
                        graphics.FillEllipse(closeBackground, closeButton);
                        graphics.DrawEllipse(closeBorder, closeButton);
                        graphics.DrawLine(closePen, closeButton.Left + 7, closeButton.Top + 7,
                            closeButton.Right - 7, closeButton.Bottom - 7);
                        graphics.DrawLine(closePen, closeButton.Right - 7, closeButton.Top + 7,
                            closeButton.Left + 7, closeButton.Bottom - 7);
                    }
                }
            }
            return bitmap;
        }

        private int LogicalCanvasWidth
        {
            get { return Math.Max(1, (int)Math.Round(Width / Math.Max(0.5f, dpiScale))); }
        }

        private int LogicalCanvasHeight
        {
            get { return Math.Max(1, (int)Math.Round(Height / Math.Max(0.5f, dpiScale))); }
        }

        private Point ToLogicalPoint(Point physicalPoint)
        {
            float scale = Math.Max(0.5f, dpiScale);
            return new Point((int)Math.Floor(physicalPoint.X / scale), (int)Math.Floor(physicalPoint.Y / scale));
        }

        private static Font CreateBannerFont(string fontName, float size)
        {
            return UiRendering.CreateTextFont(fontName, size, FontStyle.Bold);
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
