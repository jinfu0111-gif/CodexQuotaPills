using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class PlacementSelectorForm : Form
    {
        private const int LogicalWidth = PlacementMenuLayout.LogicalWidth;
        private const int LogicalHeight = PlacementMenuLayout.LogicalHeight;
        private const int LogicalGap = 6;

        private readonly Action<OverlayPlacement> selectionChanged;
        private readonly Action<PlacementMenuAction> actionSelected;
        private readonly Timer dismissTimer;
        private OverlayPlacement selectedPlacement;
        private Rectangle anchorScreenBounds = Rectangle.Empty;
        private DateTime? pointerOutsideSince;
        private float dpiScale = 1f;

        public PlacementSelectorForm(
            Action<OverlayPlacement> selectionChanged,
            Action<PlacementMenuAction> actionSelected = null)
        {
            this.selectionChanged = selectionChanged;
            this.actionSelected = actionSelected;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = LogicalWidth;
            Height = LogicalHeight;

            dismissTimer = new Timer();
            dismissTimer.Interval = 100;
            dismissTimer.Tick += CheckPointerLocation;
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
                    NativeMethods.WS_EX_LAYERED;
                return parameters;
            }
        }

        public void ShowSelector(
            OverlayPlacement placement,
            Rectangle anchorBounds,
            Rectangle workingArea,
            float scale,
            IntPtr owner)
        {
            selectedPlacement = placement;
            anchorScreenBounds = anchorBounds;
            pointerOutsideSince = null;
            dpiScale = Math.Max(0.5f, scale);

            int width = Scale(LogicalWidth);
            int height = Scale(LogicalHeight);
            int left = anchorBounds.Left + (anchorBounds.Width - width) / 2;
            left = Math.Max(workingArea.Left, Math.Min(left, workingArea.Right - width));
            int gap = Scale(LogicalGap);
            int top = anchorBounds.Top - height - gap;
            if (top < workingArea.Top)
                top = anchorBounds.Bottom + gap;
            top = Math.Max(workingArea.Top, Math.Min(top, workingArea.Bottom - height));

            SetBounds(left, top, width, height, BoundsSpecified.All);
            if (!Visible)
            {
                Show();
                if (owner != IntPtr.Zero)
                    NativeMethods.SetOwner(Handle, owner);
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }
            RenderLayered();
            dismissTimer.Start();
        }

        public void HideSelector()
        {
            dismissTimer.Stop();
            pointerOutsideSince = null;
            if (Visible)
                Hide();
        }

        internal void ExportPreview(string path, OverlayPlacement placement)
        {
            selectedPlacement = placement;
            dpiScale = 1f;
            Size = new Size(LogicalWidth, LogicalHeight);
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            using (Bitmap bitmap = BuildRenderedBitmap())
                bitmap.Save(path, ImageFormat.Png);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                dismissTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            OverlayPlacement? placement = PlacementMenuLayout.HitTestPlacement(ToLogical(e.Location));
            if (placement.HasValue)
            {
                selectedPlacement = placement.Value;
                HideSelector();
                if (selectionChanged != null)
                    selectionChanged(placement.Value);
                return;
            }

            PlacementMenuAction? action = PlacementMenuLayout.HitTestAction(ToLogical(e.Location));
            if (!action.HasValue)
                return;
            HideSelector();
            if (actionSelected != null)
                actionSelected(action.Value);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point logical = ToLogical(e.Location);
            Cursor = PlacementMenuLayout.HitTestPlacement(logical).HasValue ||
                PlacementMenuLayout.HitTestAction(logical).HasValue
                ? Cursors.Hand
                : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Per-pixel alpha is rendered through UpdateLayeredWindow.
        }

        private void CheckPointerLocation(object sender, EventArgs e)
        {
            Point pointer = Control.MousePosition;
            Rectangle anchor = anchorScreenBounds;
            anchor.Inflate(Scale(5), Scale(5));
            if (Bounds.Contains(pointer) || anchor.Contains(pointer))
            {
                pointerOutsideSince = null;
                return;
            }
            if (!pointerOutsideSince.HasValue)
            {
                pointerOutsideSince = DateTime.UtcNow;
                return;
            }
            if ((DateTime.UtcNow - pointerOutsideSince.Value).TotalMilliseconds >= 450d)
                HideSelector();
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

                Rectangle card = new Rectangle(2, 2, LogicalWidth - 5, LogicalHeight - 5);
                using (GraphicsPath shadowPath = RoundedRectangle(
                    new Rectangle(1, 3, LogicalWidth - 3, LogicalHeight - 4), 14))
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
                    "Microsoft YaHei UI", 7.6f, FontStyle.Bold))
                using (Brush titleBrush = new SolidBrush(Color.FromArgb(35, 39, 47)))
                using (StringFormat labelFormat = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    graphics.DrawString("额度胶囊 · v" + GitHubReleaseUpdateService.CurrentVersion, titleFont, titleBrush,
                        new Rectangle(12, 8, 174, 30), labelFormat);
                    graphics.DrawString("显示位置", titleFont, titleBrush,
                        new Rectangle(12, 48, 55, 30), labelFormat);
                    graphics.DrawString("常用功能", titleFont, titleBrush,
                        new Rectangle(12, 88, 55, 30), labelFormat);
                    graphics.DrawString("任务续跑", titleFont, titleBrush,
                        new Rectangle(12, 128, 55, 30), labelFormat);
                }
                using (Pen divider = new Pen(Color.FromArgb(42, 148, 163, 184), 1f))
                {
                    graphics.DrawLine(divider, 12, 43, LogicalWidth - 12, 43);
                    graphics.DrawLine(divider, 12, 83, LogicalWidth - 12, 83);
                }
                DrawAction(graphics, PlacementMenuAction.CheckGitHubUpdate, "检查 GitHub 更新",
                    Color.FromArgb(59, 130, 246), false);

                DrawChoice(graphics, OverlayPlacement.TitleBar, "上 · 标题栏",
                    Color.FromArgb(139, 92, 246));
                DrawChoice(graphics, OverlayPlacement.ContentTop, "中 · 窗口顶",
                    Color.FromArgb(59, 130, 246));
                DrawChoice(graphics, OverlayPlacement.ComposerBottom, "下 · 输入框",
                    Color.FromArgb(16, 185, 129));
                DrawAction(graphics, PlacementMenuAction.UpdateCodex, "更新 Codex",
                    Color.FromArgb(234, 146, 31), false);
                DrawAction(graphics, PlacementMenuAction.RefreshInterval, "刷新间隔",
                    Color.FromArgb(59, 130, 246), false);
                DrawAction(graphics, PlacementMenuAction.ExitApplication, "退出程序",
                    Color.FromArgb(219, 61, 94), true);
                DrawAction(graphics, PlacementMenuAction.AutoResume, "额度恢复后自动续跑",
                    Color.FromArgb(16, 185, 129), false);
            }
            return bitmap;
        }

        private void DrawChoice(
            Graphics graphics,
            OverlayPlacement placement,
            string text,
            Color accent)
        {
            Rectangle bounds = PlacementMenuLayout.ChoiceBounds(placement);
            bool selected = placement == selectedPlacement;
            DrawPill(graphics, bounds, text, accent, selected, false);
        }

        private void DrawAction(
            Graphics graphics,
            PlacementMenuAction action,
            string text,
            Color accent,
            bool danger)
        {
            DrawPill(graphics, PlacementMenuLayout.ActionBounds(action), text, accent, false, danger);
        }

        private static void DrawPill(
            Graphics graphics,
            Rectangle bounds,
            string text,
            Color accent,
            bool selected,
            bool danger)
        {
            Color fillColor = selected
                ? Color.FromArgb(255, 242, 238, 255)
                : Color.FromArgb(244, 255, 255, 255);
            Color borderColor = selected
                ? Color.FromArgb(165, accent)
                : Color.FromArgb(58, 100, 116, 139);
            using (GraphicsPath path = RoundedRectangle(bounds, 10))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(borderColor, selected ? 1.25f : 1f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            int dotY = bounds.Top + bounds.Height / 2 - 3;
            using (Brush dot = new SolidBrush(accent))
                graphics.FillEllipse(dot, bounds.Left + 7, dotY, 6, 6);
            using (Font font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 7.5f,
                selected ? FontStyle.Bold : FontStyle.Regular))
            using (Brush textBrush = new SolidBrush(danger
                ? Color.FromArgb(176, 38, 68)
                : Color.FromArgb(54, 60, 70)))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                Rectangle textBounds = new Rectangle(
                    bounds.Left + 17, bounds.Top, bounds.Width - 19, bounds.Height);
                graphics.DrawString(text, font, textBrush, textBounds, format);
            }
        }

        private Point ToLogical(Point point)
        {
            return new Point(
                (int)Math.Round(point.X / Math.Max(0.5f, dpiScale)),
                (int)Math.Round(point.Y / Math.Max(0.5f, dpiScale)));
        }

        private int Scale(int logicalPixels)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * dpiScale));
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
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
    }
}
