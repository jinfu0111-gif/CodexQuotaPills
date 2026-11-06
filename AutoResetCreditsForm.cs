using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class AutoResetCreditRow
    {
        internal string Title;
        internal string ExpiryLine;
        internal string ScheduleLine;
    }

    internal sealed class AutoResetCreditsForm : Form
    {
        private readonly AutoResetCreditsService service;
        private readonly UpdateMenuPalette palette = UpdateMenuVisuals.CreateRainbowPalette();
        private readonly Label summary;
        private readonly Label lastResult;
        private readonly Label checkedAt;
        private readonly ListBox list;
        private readonly UpdatePillButton toggle;
        private readonly UpdatePillButton refresh;
        private readonly Timer timer;
        private bool preview;

        internal AutoResetCreditsForm(AutoResetCreditsService service, Rectangle anchor)
        {
            this.service = service;
            Text = "重置卡 · 到期自动使用";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = false;
            BackColor = palette.Surface;
            ClientSize = new Size(568, 454);
            DoubleBuffered = true;
            KeyPreview = true;
            Label title = LabelAt("重置卡 · 到期自动使用", new Rectangle(18, 12, 480, 30));
            title.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 12f, FontStyle.Bold);
            UpdatePillButton close = ButtonAt("×", new Rectangle(522, 13, 28, 28), false);
            close.AccessibleName = "关闭重置卡管理";
            close.Click += delegate { Close(); };
            Label rule = LabelAt("到期前 10 分钟仍可用时自动使用；更早时继续保留。", new Rectangle(19, 45, 530, 27));
            rule.ForeColor = palette.MutedText;
            summary = LabelAt("待刷新真实重置卡…", new Rectangle(19, 75, 530, 25));
            lastResult = LabelAt("最近结果：尚无自动使用记录", new Rectangle(19, 103, 530, 39));
            lastResult.ForeColor = palette.MutedText;
            lastResult.TextAlign = ContentAlignment.TopLeft;
            list = new ListBox {
                Bounds = new Rectangle(18, 149, 532, 180),
                BorderStyle = BorderStyle.None,
                BackColor = palette.Surface,
                ForeColor = palette.Text,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 84,
                IntegralHeight = false,
                TabStop = false
            };
            list.DrawItem += DrawCredit;
            Controls.Add(list);
            checkedAt = LabelAt("最近检查：待刷新", new Rectangle(20, 337, 530, 25));
            checkedAt.ForeColor = palette.MutedText;
            checkedAt.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular);
            toggle = ButtonAt("启用到期自动使用", new Rectangle(18, 373, 210, 32), true);
            toggle.Click += delegate {
                if (this.service == null) return;
                this.service.SetEnabled(!this.service.Enabled);
                RefreshView();
            };
            refresh = ButtonAt("刷新重置卡", new Rectangle(390, 373, 160, 32), false);
            refresh.Click += delegate {
                if (this.service != null) this.service.RequestScan();
                RefreshView();
            };
            Label note = LabelAt("Codex 与胶囊运行且联网时每 30 秒检查；关闭面板不会暂停。\n本地不设剩余额度门槛，临期 10 分钟内直接向 OpenAI 发起重置卡兑换。", new Rectangle(20, 414, 530, 35));
            note.ForeColor = palette.MutedText;
            note.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular);
            note.TextAlign = ContentAlignment.TopLeft;
            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate { if (!preview && Visible) RefreshView(); };
            VisibleChanged += delegate {
                if (!preview && Visible && this.service != null) timer.Start();
                else timer.Stop();
            };
            Shown += delegate {
                if (!preview && this.service != null) {
                    RefreshView();
                    this.service.RequestScan();
                }
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Location = PopupAnchorPlacement.NearPlus(anchor, Size, Screen.FromRectangle(anchor).WorkingArea);
            PopupUiContext.Bind(this, anchor, palette);
            Apply(new AutoResetCreditsSnapshot {
                Message = "尚未检查重置卡", LastResult = String.Empty, Credits = new ResetCreditRecord[0]
            }, DateTime.UtcNow);
        }

        private Label LabelAt(string text, Rectangle bounds)
        {
            Label label = new Label { Text = text, Bounds = bounds, ForeColor = palette.Text,
                BackColor = palette.Surface, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Controls.Add(label);
            return label;
        }

        private UpdatePillButton ButtonAt(string text, Rectangle bounds, bool primary)
        {
            UpdatePillButton button = new UpdatePillButton(text, palette, primary) { Bounds = bounds };
            Controls.Add(button);
            return button;
        }

        private void RefreshView()
        {
            if (service != null && !IsDisposed) Apply(service.Snapshot(), DateTime.UtcNow);
        }

        internal void Apply(AutoResetCreditsSnapshot data, DateTime nowUtc)
        {
            data = data ?? new AutoResetCreditsSnapshot { Credits = new ResetCreditRecord[0] };
            string mode = data.Enabled ? "已启用" : "已暂停";
            summary.Text = mode + " · " + (data.Scanning ? "正在检查重置卡…" :
                String.IsNullOrWhiteSpace(data.Message) ? "等待下一次检查" : data.Message);
            toggle.Text = data.Enabled ? "暂停到期自动使用" : "启用到期自动使用";
            toggle.Enabled = service != null && !preview;
            refresh.Enabled = service != null && !preview && !data.Scanning;
            string result = String.IsNullOrWhiteSpace(data.LastResult) ? "尚无自动使用记录" : data.LastResult;
            lastResult.Text = "最近结果：" + result + (data.LastAttemptUtc == DateTime.MinValue ? String.Empty :
                "\n" + FormatLocal(data.LastAttemptUtc));
            checkedAt.Text = data.LastCheckedUtc == DateTime.MinValue ? "最近检查：待刷新" :
                "最近检查：" + FormatLocal(data.LastCheckedUtc) + " · 本机时间";
            list.BeginUpdate();
            list.Items.Clear();
            foreach (AutoResetCreditRow row in BuildRows(data, nowUtc)) list.Items.Add(row);
            list.EndUpdate();
        }

        internal static AutoResetCreditRow[] BuildRows(AutoResetCreditsSnapshot data, DateTime nowUtc)
        {
            List<AutoResetCreditRow> rows = new List<AutoResetCreditRow>();
            data = data ?? new AutoResetCreditsSnapshot();
            int index = 0;
            foreach (ResetCreditRecord credit in data.Credits ?? new ResetCreditRecord[0])
            {
                if (credit == null || !String.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase)) continue;
                index++;
                DateTime expiry;
                bool hasExpiry = TryExpiry(credit.ExpiresAt, out expiry);
                bool expired = hasExpiry && expiry <= nowUtc.ToUniversalTime();
                string title = "重置卡 " + index.ToString(CultureInfo.InvariantCulture) + (expired ? " · 已到期" : " · 可用");
                string schedule;
                if (!hasExpiry) schedule = "自动使用时间：待刷新";
                else if (expired) schedule = "已到期，等待服务刷新卡片状态";
                else if (!String.Equals(credit.ResetType, "codexRateLimits", StringComparison.Ordinal))
                    schedule = "卡片类型待确认，暂不自动使用";
                else if (expiry.AddMinutes(-10) <= nowUtc.ToUniversalTime())
                    schedule = data.Enabled ? "已进入到期前 10 分钟，等待检查使用" : "已进入到期前 10 分钟 · 自动使用已暂停";
                else schedule = "自动使用时间：" + FormatLocal(expiry.AddMinutes(-10)) + (data.Enabled ? String.Empty : " · 已暂停");
                rows.Add(new AutoResetCreditRow { Title = title,
                    ExpiryLine = "到期时间：" + (hasExpiry ? FormatLocal(expiry) : "待刷新"), ScheduleLine = schedule });
            }
            int missing = Math.Max(0, data.AvailableCount - index);
            if (missing > 0) rows.Add(new AutoResetCreditRow { Title = "另 " + missing.ToString(CultureInfo.InvariantCulture) + " 张重置卡",
                ExpiryLine = "卡片详情与到期时间待刷新", ScheduleLine = "确认真实卡片后再安排自动使用" });
            if (rows.Count == 0) rows.Add(new AutoResetCreditRow {
                Title = data.Available && data.LastCheckedUtc != DateTime.MinValue ? "当前没有可用重置卡" : "重置卡待刷新",
                ExpiryLine = data.Scanning ? "正在查询真实卡片状态…" : "等待下一次检查",
                ScheduleLine = "已使用的重置卡不会再次安排"
            });
            return rows.ToArray();
        }

        private static bool TryExpiry(long? unixSeconds, out DateTime expiry)
        {
            expiry = DateTime.MinValue;
            if (!unixSeconds.HasValue || !ResetCreditDetails.IsValidExpiry(unixSeconds.Value)) return false;
            try { expiry = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds.Value); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        private static string FormatLocal(DateTime utc)
        {
            return utc.ToLocalTime().ToString("M/d HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void DrawCredit(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            AutoResetCreditRow row = list.Items[e.Index] as AutoResetCreditRow;
            if (row == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(e.Bounds.Left + 1, e.Bounds.Top + 2, e.Bounds.Width - 4, e.Bounds.Height - 6);
            using (GraphicsPath path = Round(card, 12))
            using (Brush fill = new SolidBrush(palette.Surface))
            using (Pen border = new Pen(palette.Border)) {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            using (Font title = UiRendering.CreateTextFont(Font.Name, 9f, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, row.Title, title, new Rectangle(card.Left + 12, card.Top + 5, card.Width - 24, 23), palette.Text, flags);
            TextRenderer.DrawText(e.Graphics, row.ExpiryLine, Font, new Rectangle(card.Left + 12, card.Top + 29, card.Width - 24, 21), palette.MutedText, flags);
            TextRenderer.DrawText(e.Graphics, row.ScheduleLine, Font, new Rectangle(card.Left + 12, card.Top + 51, card.Width - 24, 21), palette.MutedText, flags);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (Pen border = new Pen(palette.Border)) e.Graphics.DrawPath(border, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 28 || Height < 28) return;
            using (GraphicsPath path = Round(new Rectangle(0, 0, Width, Height), 14)) {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null) timer.Dispose();
            base.Dispose(disposing);
        }

        private static GraphicsPath Round(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal void ExportPreview(string path)
        {
            preview = true;
            timer.Stop();
            DateTime now = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
            Apply(new AutoResetCreditsSnapshot { Enabled = true, Available = true, AvailableCount = 2,
                Message = "检测到 2 张可用重置卡 · 示例数据", LastCheckedUtc = now,
                LastResult = "尚未到使用时点，继续保留重置卡", Credits = new[] {
                    new ResetCreditRecord { Id = "preview-1", Status = "available", ResetType = "codexRateLimits",
                        ExpiresAt = new DateTimeOffset(now.AddHours(2)).ToUnixTimeSeconds() },
                    new ResetCreditRecord { Id = "preview-2", Status = "available", ResetType = "codexRateLimits",
                        ExpiresAt = new DateTimeOffset(now.AddDays(7)).ToUnixTimeSeconds() }
                } }, now);
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            PerformLayout();
            using (Bitmap bitmap = UiRendering.RenderOffscreen(this)) bitmap.Save(path, ImageFormat.Png);
        }
    }
}
