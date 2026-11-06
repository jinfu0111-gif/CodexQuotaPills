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
        private OverlayPlacement? hoveredPlacement;
        private PlacementMenuAction? hoveredAction;
        private Rectangle anchorScreenBounds = Rectangle.Empty;
        private DateTime? pointerOutsideSince;
        private float dpiScale = 1f;
        private bool showPlanPill = true;
        private bool showTokenPill = true;
        private bool showResetCreditsPill = true;
        private bool holdAnchorForDisplayToggle;
        private Rectangle anchorWindowBounds;
        private IntPtr ownerHandle = IntPtr.Zero;

        internal void SetPillVisibility(OverlaySettings settings)
        {
            showPlanPill = settings == null || settings.ShowPlanPill;
            showTokenPill = settings == null || settings.ShowTokenPill;
            showResetCreditsPill = settings == null || settings.ShowResetCreditsPill;
            if (IsHandleCreated) RenderLayered();
        }

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
            TopMost = false;
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
            IntPtr owner, OverlaySettings displaySettings = null)
        {
            selectedPlacement = placement;
            hoveredPlacement = null;
            hoveredAction = null;
            anchorScreenBounds = anchorBounds;
            holdAnchorForDisplayToggle = false;
            anchorWindowBounds = PopupUiContext.WindowBounds;
            ownerHandle = owner;
            SetPillVisibility(displaySettings);
            pointerOutsideSince = null;
            dpiScale = Math.Max(0.5f, scale);

            int width = Scale(LogicalWidth);
            int height = Scale(LogicalHeight);
            Point position = PopupAnchorPlacement.NearPlus(anchorBounds,
                new Size(width, height), workingArea, PopupUiContext.CapsuleGap(Scale(LogicalGap), dpiScale), Scale(4));
            SetBounds(position.X, position.Y, width, height, BoundsSpecified.All);
            if (!Visible)
            {
                Show();
                if (owner != IntPtr.Zero)
                    NativeMethods.SetOwner(Handle, owner);
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }
            if (owner != IntPtr.Zero)
                OverlayWindowPresentation.Recover(this, owner);
            RenderLayered();
            dismissTimer.Start();
        }

        public void HideSelector()
        {
            dismissTimer.Stop();
            holdAnchorForDisplayToggle = false;
            hoveredPlacement = null;
            hoveredAction = null;
            pointerOutsideSince = null;
            if (Visible)
                Hide();
        }

        internal void ExportPreview(string path, OverlayPlacement placement, OverlaySettings displaySettings = null)
        {
            selectedPlacement = placement;
            hoveredPlacement = null;
            hoveredAction = null;
            SetPillVisibility(displaySettings);
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
            if (PlacementMenuLayout.IsDisplayToggle(action.Value))
            {
                // Resizing the capsule row changes the 5h anchor. Keep this
                // open menu still while the user changes the two switches.
                holdAnchorForDisplayToggle = true;
                anchorWindowBounds = PopupUiContext.WindowBounds;
                pointerOutsideSince = null;
            }
            else HideSelector();
            if (actionSelected != null)
                actionSelected(action.Value);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point logical = ToLogical(e.Location);
            UpdateHoverState(logical);
            Cursor = PlacementMenuLayout.IsClickable(logical)
                ? Cursors.Hand
                : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredPlacement.HasValue || hoveredAction.HasValue)
            {
                hoveredPlacement = null;
                hoveredAction = null;
                RenderLayered();
            }
        }

        private void UpdateHoverState(Point logical)
        {
            OverlayPlacement? nextPlacement = PlacementMenuLayout.HitTestPlacement(logical);
            PlacementMenuAction? nextAction = nextPlacement.HasValue ? null : PlacementMenuLayout.HitTestAction(logical);
            if (nextPlacement != hoveredPlacement || nextAction != hoveredAction)
            {
                hoveredPlacement = nextPlacement;
                hoveredAction = nextAction;
                RenderLayered();
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_SETCURSOR &&
                (message.LParam.ToInt64() & 0xffff) == NativeMethods.HTCLIENT)
            {
                Point logical = ToLogical(PointToClient(Control.MousePosition));
                // Control.Cursor would synchronously re-enter WM_SETCURSOR.
                System.Windows.Forms.Cursor.Current =
                    PlacementMenuLayout.IsClickable(logical) ? Cursors.Hand : Cursors.Default;
                message.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Per-pixel alpha is rendered through UpdateLayeredWindow.
        }

        private void CheckPointerLocation(object sender, EventArgs e)
        {
            if (ownerHandle != IntPtr.Zero && Visible)
                OverlayWindowPresentation.Recover(this, ownerHandle);
            if (!PopupUiContext.Anchor.IsEmpty)
            {
                if (holdAnchorForDisplayToggle && PopupUiContext.WindowBounds.Size == anchorWindowBounds.Size)
                {
                    anchorScreenBounds.Offset(PopupUiContext.WindowBounds.X - anchorWindowBounds.X,
                        PopupUiContext.WindowBounds.Y - anchorWindowBounds.Y);
                    anchorWindowBounds = PopupUiContext.WindowBounds;
                }
                else
                {
                    holdAnchorForDisplayToggle = false;
                    anchorScreenBounds = PopupUiContext.Anchor;
                    anchorWindowBounds = PopupUiContext.WindowBounds;
                }
                Point position = PopupAnchorPlacement.NearPlus(anchorScreenBounds, Size,
                    PopupUiContext.WorkingArea, PopupUiContext.CapsuleGap(Scale(LogicalGap), dpiScale), Scale(4));
                if (Location != position) { Location = position; RenderLayered(); }
            }
            Point pointer = Control.MousePosition;
            if (Bounds.Contains(pointer))
                UpdateHoverState(ToLogical(PointToClient(pointer)));
            else if (hoveredPlacement.HasValue || hoveredAction.HasValue)
            {
                hoveredPlacement = null;
                hoveredAction = null;
                RenderLayered();
            }
            Rectangle anchor = anchorScreenBounds;
            anchor.Inflate(Scale(5), Scale(5));
            Rectangle bridge = Rectangle.FromLTRB(Math.Min(anchor.Right, Left) - Scale(4),
                anchor.Top - Scale(5), Math.Max(anchor.Right, Left) + Scale(4),
                anchor.Bottom + Scale(5));
            if (Bounds.Contains(pointer) || anchor.Contains(pointer) ||
                (selectedPlacement == OverlayPlacement.Sidebar && bridge.Contains(pointer)))
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
                using (Brush surface = new SolidBrush(QuotaPillVisuals.CardSurface))
                using (Pen border = new Pen(QuotaPillVisuals.Border, 1f))
                {
                    graphics.FillPath(surface, cardPath);
                    graphics.DrawPath(border, cardPath);
                }

                using (Font titleFont = UiRendering.CreateTextFont(
                    "Microsoft YaHei UI", 7.6f, FontStyle.Bold))
                using (Brush titleBrush = new SolidBrush(QuotaPillVisuals.Text))
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
                        new Rectangle(12, 92, 55, 30), labelFormat);
                    graphics.DrawString("外观主题", titleFont, titleBrush,
                        new Rectangle(12, 132, 55, 30), labelFormat);
                    graphics.DrawString("胶囊显示", titleFont, titleBrush,
                        new Rectangle(12, 172, 55, 30), labelFormat);
                    graphics.DrawString("进阶功能", titleFont, titleBrush,
                        new Rectangle(12, 212, 55, 30), labelFormat);
                }
                using (Pen divider = new Pen(Color.FromArgb(42, 148, 163, 184), 1f))
                {
                    graphics.DrawLine(divider, 12, 43, LogicalWidth - 12, 43);
                    graphics.DrawLine(divider, 12, 85, LogicalWidth - 12, 85);
                }
                DrawAction(graphics, PlacementMenuAction.CheckGitHubUpdate, "检查更新",
                    Color.FromArgb(59, 130, 246), false);

                DrawChoice(graphics, OverlayPlacement.TitleBar, "标题栏",
                    Color.FromArgb(139, 92, 246));
                DrawChoice(graphics, OverlayPlacement.ComposerBottom, "输入框",
                    Color.FromArgb(16, 185, 129));
                DrawChoice(graphics, OverlayPlacement.Sidebar, "侧边栏",
                    Color.FromArgb(236, 72, 153));
                DrawAction(graphics, PlacementMenuAction.ThemeAuto, "跟随系统", Color.FromArgb(139, 92, 246), false);
                DrawAction(graphics, PlacementMenuAction.ThemeLight, "浅色", Color.FromArgb(59, 130, 246), false);
                DrawAction(graphics, PlacementMenuAction.ThemeDark, "深色", Color.FromArgb(100, 116, 139), false);
                DrawAction(graphics, PlacementMenuAction.UpdateCodex, "更新 Codex",
                    Color.FromArgb(234, 146, 31), false);
                DrawAction(graphics, PlacementMenuAction.RefreshInterval, "刷新间隔",
                    Color.FromArgb(59, 130, 246), false);
                DrawAction(graphics, PlacementMenuAction.ExitApplication, "退出程序",
                    Color.FromArgb(219, 61, 94), true);
                DrawAction(graphics, PlacementMenuAction.SessionUsage, "会话数据",
                    Color.FromArgb(245, 158, 11), false);
                DrawAction(graphics, PlacementMenuAction.AutoResume, "任务续跑",
                    Color.FromArgb(16, 185, 129), false);
                DrawAction(graphics, PlacementMenuAction.AutoResetCredits, "重置卡",
                    Color.FromArgb(236, 72, 153), false);
                DrawAction(graphics, PlacementMenuAction.TogglePlan, "套餐 " + (showPlanPill ? "显示" : "隐藏"),
                    showPlanPill ? Color.FromArgb(139, 92, 246) : QuotaPillVisuals.MutedText, false);
                DrawAction(graphics, PlacementMenuAction.ToggleTokens, "Token " + (showTokenPill ? "显示" : "隐藏"),
                    showTokenPill ? Color.FromArgb(245, 158, 11) : QuotaPillVisuals.MutedText, false);
                DrawAction(graphics, PlacementMenuAction.ToggleResetCredits, "券 " + (showResetCreditsPill ? "显示" : "隐藏"),
                    showResetCreditsPill ? Color.FromArgb(236, 72, 153) : QuotaPillVisuals.MutedText, false);
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
            bool hovered = hoveredPlacement.HasValue && hoveredPlacement.Value == placement;
            DrawPill(graphics, bounds, text, accent, selected, hovered, false);
        }

        private void DrawAction(
            Graphics graphics,
            PlacementMenuAction action,
            string text,
            Color accent,
            bool danger)
        {
            bool selected = action == PlacementMenuAction.ThemeAuto && PopupUiContext.Theme == "Auto" ||
                action == PlacementMenuAction.ThemeLight && PopupUiContext.Theme == "Light" ||
                action == PlacementMenuAction.ThemeDark && PopupUiContext.Theme == "Dark" ||
                action == PlacementMenuAction.TogglePlan && showPlanPill ||
                action == PlacementMenuAction.ToggleTokens && showTokenPill ||
                action == PlacementMenuAction.ToggleResetCredits && showResetCreditsPill;
            bool hovered = hoveredAction.HasValue && hoveredAction.Value == action;
            DrawPill(graphics, PlacementMenuLayout.ActionBounds(action), text, accent, selected, hovered, danger);
        }

        private static void DrawPill(
            Graphics graphics,
            Rectangle bounds,
            string text,
            Color accent,
            bool selected,
            bool hovered,
            bool danger)
        {
            Color fillColor = selected
                ? (PopupUiContext.Dark ? Color.FromArgb(55, 44, 78) : Color.FromArgb(255, 242, 238, 255))
                : hovered
                    ? (PopupUiContext.Dark ? Color.FromArgb(46, 53, 68) : Color.FromArgb(252, 244, 247, 254))
                    : QuotaPillVisuals.Surface;
            Color borderColor = selected
                ? Color.FromArgb(165, accent)
                : hovered
                    ? Color.FromArgb(135, accent)
                    : QuotaPillVisuals.Border;
            using (GraphicsPath path = RoundedRectangle(bounds, 15))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(borderColor, selected || hovered ? 1.25f : 1f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            int dotY = bounds.Top + bounds.Height / 2 - 3;
            if (hovered)
            {
                using (Brush halo = new SolidBrush(Color.FromArgb(36, accent.R, accent.G, accent.B)))
                    graphics.FillEllipse(halo, bounds.Left + 5, dotY - 2, 10, 10);
            }
            using (Brush dot = new SolidBrush(accent))
                graphics.FillEllipse(dot, bounds.Left + 7, dotY, 6, 6);
            using (Font font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 7.5f,
                selected ? FontStyle.Bold : FontStyle.Regular))
            using (Brush textBrush = new SolidBrush(danger
                ? UpdateMenuVisuals.CreateRainbowPalette().Danger
                : QuotaPillVisuals.Text))
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
