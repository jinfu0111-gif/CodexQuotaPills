using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class QuotaHoverCardForm : Form
    {
        private const int DefaultLogicalWidth = 238;
        private const int MinimumLogicalWidth = 188;
        private const int MaximumLogicalWidth = 342;
        private const int LogicalHeight = 135;
        private const int LogicalGap = 7;

        private QuotaHoverCardContent content = new QuotaHoverCardContent();
        private string renderedRevision = String.Empty;
        private Rectangle renderedBounds = Rectangle.Empty;
        private float dpiScale = 1f;
        private int logicalWidth = DefaultLogicalWidth;
        private int logicalHeight = LogicalHeight;

        public QuotaHoverCardForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = DefaultLogicalWidth;
            Height = LogicalHeight;
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
                parameters.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW |
                    NativeMethods.WS_EX_NOACTIVATE |
                    NativeMethods.WS_EX_LAYERED |
                    NativeMethods.WS_EX_TRANSPARENT;
                return parameters;
            }
        }

        public void ShowCard(
            QuotaHoverCardContent nextContent,
            Rectangle anchorScreenBounds,
            Rectangle workingArea,
            float scale,
            IntPtr owner)
        {
            if (nextContent == null)
            {
                HideCard();
                return;
            }

            content = nextContent;
            dpiScale = Math.Max(0.5f, scale);
            logicalWidth = MeasureContentWidth(content);
            logicalHeight = MeasureContentHeight(content);
            int width = Math.Max(1, (int)Math.Round(logicalWidth * dpiScale));
            int height = Math.Max(1, (int)Math.Round(logicalHeight * dpiScale));
            int left = anchorScreenBounds.Left + (anchorScreenBounds.Width - width) / 2;
            left = Math.Max(workingArea.Left, Math.Min(left, workingArea.Right - width));
            int top = anchorScreenBounds.Top - height -
                Math.Max(1, (int)Math.Round(LogicalGap * dpiScale));
            if (top < workingArea.Top)
                top = anchorScreenBounds.Bottom + Math.Max(1, (int)Math.Round(LogicalGap * dpiScale));
            top = Math.Max(workingArea.Top, Math.Min(top, workingArea.Bottom - height));
            Rectangle bounds = new Rectangle(left, top, width, height);
            bool boundsChanged = bounds != renderedBounds;
            if (boundsChanged)
            {
                SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height, BoundsSpecified.All);
                renderedBounds = bounds;
            }

            bool becameVisible = !Visible;
            if (becameVisible)
            {
                Show();
                if (owner != IntPtr.Zero)
                    NativeMethods.SetOwner(Handle, owner);
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }

            string revision = content.RevisionKey + "|" + dpiScale.ToString("0.###");
            if (becameVisible || boundsChanged ||
                !String.Equals(revision, renderedRevision, StringComparison.Ordinal))
            {
                RenderLayered();
                renderedRevision = revision;
            }
        }

        public void HideCard()
        {
            renderedRevision = String.Empty;
            if (Visible)
                Hide();
        }

        internal void ExportPreview(string path, QuotaHoverCardContent previewContent)
        {
            content = previewContent ?? new QuotaHoverCardContent();
            dpiScale = 1f;
            logicalWidth = MeasureContentWidth(content);
            logicalHeight = MeasureContentHeight(content);
            Size = new Size(logicalWidth, logicalHeight);
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            using (Bitmap bitmap = BuildRenderedBitmap())
                bitmap.Save(path, ImageFormat.Png);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_NCHITTEST)
            {
                message.Result = (IntPtr)NativeMethods.HTTRANSPARENT;
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Per-pixel alpha is rendered through UpdateLayeredWindow.
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

                Rectangle card = new Rectangle(2, 2, logicalWidth - 5, logicalHeight - 5);
                using (GraphicsPath shadowPath = RoundedRectangle(
                    new Rectangle(1, 3, logicalWidth - 3, logicalHeight - 4), 14))
                using (Brush shadow = new SolidBrush(Color.FromArgb(28, 15, 23, 42)))
                    graphics.FillPath(shadow, shadowPath);
                using (GraphicsPath cardPath = RoundedRectangle(card, 13))
                using (Brush surface = new SolidBrush(Color.FromArgb(250, 250, 250, 251)))
                using (Pen border = new Pen(Color.FromArgb(72, 148, 163, 184), 1f))
                {
                    graphics.FillPath(surface, cardPath);
                    graphics.DrawPath(border, cardPath);
                }

                using (Font titleFont = UiRendering.CreateTextFont(
                    "Microsoft YaHei UI", 8.5f, FontStyle.Bold))
                using (Font rowFont = UiRendering.CreateTextFont(
                    "Microsoft YaHei UI", 8.2f, FontStyle.Regular))
                using (Brush titleBrush = new SolidBrush(Color.FromArgb(255, 31, 41, 55)))
                using (Brush rowBrush = new SolidBrush(Color.FromArgb(255, 75, 85, 99)))
                using (Pen divider = new Pen(Color.FromArgb(55, 148, 163, 184), 1f))
                using (StringFormat format = UiRendering.CreateTextFormat())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    format.Trimming = StringTrimming.EllipsisCharacter;

                    graphics.DrawString(content.Title, titleFont, titleBrush,
                        new Rectangle(16, 8, logicalWidth - 32, 22), format);
                    graphics.DrawLine(divider, 16, 33, logicalWidth - 16, 33);
                    if (content.DetailLines != null)
                    {
                        for (int index = 0; index < content.DetailLines.Length; index++)
                            DrawRow(graphics, content.DetailLines[index], 43 + 23 * index,
                                index == content.DetailLines.Length - 1
                                    ? QuotaPillVisuals.MutedText : Color.FromArgb(236, 72, 153),
                                rowFont, rowBrush, format, logicalWidth);
                    }
                    else
                    {
                    DrawRow(graphics, content.ShortLine, 43,
                        Color.FromArgb(59, 130, 246), rowFont, rowBrush, format, logicalWidth);
                    DrawRow(graphics, content.WeeklyLine, 66,
                        Color.FromArgb(16, 185, 129), rowFont, rowBrush, format, logicalWidth);
                    DrawRow(graphics, content.ExtraLine, 89,
                        content.ExtraIsTibo
                            ? Color.FromArgb(139, 92, 246)
                            : Color.FromArgb(236, 72, 153),
                        rowFont, rowBrush, format, logicalWidth);
                    DrawRow(graphics, content.TiboLine, 112,
                        Color.FromArgb(139, 92, 246), rowFont, rowBrush, format, logicalWidth);
                    }
                }
            }
            return bitmap;
        }

        private static void DrawRow(
            Graphics graphics,
            string text,
            int centerY,
            Color dotColor,
            Font font,
            Brush textBrush,
            StringFormat format,
            int canvasWidth)
        {
            using (Brush halo = new SolidBrush(Color.FromArgb(
                34, dotColor.R, dotColor.G, dotColor.B)))
            using (Brush dot = new SolidBrush(dotColor))
            {
                graphics.FillEllipse(halo, 15, centerY - 5, 10, 10);
                graphics.FillEllipse(dot, 17, centerY - 3, 6, 6);
            }
            graphics.DrawString(text ?? String.Empty, font, textBrush,
                new Rectangle(31, centerY - 10, canvasWidth - 47, 20), format);
        }

        private static int MeasureContentWidth(QuotaHoverCardContent value)
        {
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 8.2f, FontStyle.Regular))
            {
                float widest = 0f;
                if (value != null && value.DetailLines != null)
                    foreach (string line in value.DetailLines)
                        widest = Math.Max(widest, graphics.MeasureString(line, font).Width);
                widest = Math.Max(widest, graphics.MeasureString(
                    value == null ? String.Empty : value.ShortLine, font).Width);
                widest = Math.Max(widest, graphics.MeasureString(
                    value == null ? String.Empty : value.WeeklyLine, font).Width);
                widest = Math.Max(widest, graphics.MeasureString(
                    value == null ? String.Empty : value.ExtraLine, font).Width);
                widest = Math.Max(widest, graphics.MeasureString(
                    value == null ? String.Empty : value.TiboLine, font).Width);
                return Math.Max(MinimumLogicalWidth,
                    Math.Min(MaximumLogicalWidth, (int)Math.Ceiling(widest) + 52));
            }
        }

        private static int MeasureContentHeight(QuotaHoverCardContent value)
        {
            return value != null && value.DetailLines != null
                ? 43 + Math.Max(1, value.DetailLines.Length) * 23 : LogicalHeight;
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
