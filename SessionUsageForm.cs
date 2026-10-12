using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class SessionUsageForm : Form
    {
        private readonly UpdateMenuPalette palette = UpdateMenuVisuals.CreateRainbowPalette();
        private readonly SessionUsageReader reader;
        private readonly Func<UsageData> account;
        private readonly UpdatePillButton choose, refresh;
        private readonly ListBox choices;
        private readonly Label[] rows = new Label[7];
        private readonly Label status;
        private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 15000 };
        private SessionUsageEntry selected;
        private List<SessionUsageEntry> entries = new List<SessionUsageEntry>();
        private bool busy, preview;
        internal SessionUsageEntry SelectedSession { get { return selected; } }
        internal void RestoreSelection(SessionUsageEntry entry)
        {
            selected = entry;
            if (entry != null) choose.Text = "所选会话：" + (entry.Title.Length > 26 ? entry.Title.Substring(0, 26) + "…" : entry.Title);
        }

        internal SessionUsageForm(Rectangle anchor, Func<UsageData> accountSnapshot)
        {
            account = accountSnapshot;
            reader = SessionUsageReader.ForUser();
            Text = "会话用量 · 只读";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = palette.Surface;
            ClientSize = new Size(426, 416); DoubleBuffered = true; KeyPreview = true;
            LabelAt("会话用量 · 只读", 18, 14, 342, 27, true);
            ButtonAt("×", new Rectangle(382, 14, 28, 28), false).Click += delegate { Close(); };
            LabelAt("请选择正常对话 · 内部审核/子任务已过滤（最多 100 个）", 18, 45, 392, 23, false);
            choose = ButtonAt("选择会话…", new Rectangle(16, 77, 394, 32), false);
            choose.Click += delegate { choices.Visible = !choices.Visible; if (choices.Visible) choices.BringToFront(); };
            for (int i = 0; i < rows.Length; i++) rows[i] = LabelAt("—", 21, 123 + i * 30, 386, 27, false);
            status = LabelAt("正在读取本机会话列表…", 20, 341, 386, 28, false);
            refresh = ButtonAt("刷新数据", new Rectangle(16, 376, 114, 30), false);
            refresh.Click += delegate { LoadData(true); };
            LabelAt("仅本地读取 · 不发送消息 · 不自动压缩", 140, 377, 266, 27, false);
            choices = new ListBox { Bounds = new Rectangle(17, 115, 392, 214),
                BorderStyle = BorderStyle.FixedSingle, Font = Font, BackColor = palette.Surface,
                ForeColor = palette.Text, Visible = false, IntegralHeight = false, ItemHeight = 30,
                DrawMode = DrawMode.OwnerDrawFixed };
            choices.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0) return;
                e.DrawBackground();
                TextRenderer.DrawText(e.Graphics, choices.Items[e.Index].ToString(), Font,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
                    (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : palette.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            choices.MouseMove += delegate(object sender, MouseEventArgs e) {
                choices.Cursor = !busy && choices.IndexFromPoint(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
            };
            choices.MouseLeave += delegate { choices.Cursor = Cursors.Default; };
            choices.Click += delegate {
                SessionUsageEntry entry = choices.SelectedItem as SessionUsageEntry;
                if (entry == null || busy) return;
                selected = entry; choices.Visible = false;
                choose.Text = "所选会话：" + (entry.Title.Length > 26 ? entry.Title.Substring(0, 26) + "…" : entry.Title);
                ClearRows(); LoadData(false);
            };
            Controls.Add(choices);
            poll.Tick += delegate { if (!preview && Visible && !choices.Visible) LoadData(false); };
            Shown += delegate { if (!preview) { LoadData(true); poll.Start(); } };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            PopupUiContext.Bind(this, anchor, palette);
            ClearRows();
        }

        private void ClearRows()
        {
            rows[0].Text = "账户累计 Token   " + (account == null ? "待确认" : account().ProfileTokensText);
            for (int i = 1; i < rows.Length; i++) rows[i].Text = "—";
        }
        private void LoadData(bool reloadList)
        {
            if (busy || IsDisposed) return;
            busy = true; refresh.Enabled = false; choose.Enabled = false;
            status.Text = "正在只读刷新…";
            SessionUsageEntry requested = selected;
            ThreadPool.QueueUserWorkItem(delegate {
                List<SessionUsageEntry> list = null; string error = String.Empty;
                SessionUsageSnapshot snapshot = null;
                try
                {
                    if (reloadList) list = reader.List(out error);
                    if (requested != null) snapshot = reader.Read(requested);
                }
                catch { error = "本机数据暂不可读，请稍后刷新"; }
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate {
                    if (IsDisposed) return;
                    busy = false; refresh.Enabled = true; choose.Enabled = true;
                    if (list != null)
                    {
                        entries = list; choices.Items.Clear();
                        foreach (SessionUsageEntry item in entries) choices.Items.Add(item);
                    }
                    if (snapshot != null) Apply(snapshot);
                    else { ClearRows(); status.Text = error.Length > 0 ? error : "请选择会话，不默认使用最近活跃对话"; }
                }); }
                catch (InvalidOperationException) { }
            });
        }

        private void Apply(SessionUsageSnapshot data)
        {
            ClearRows();
            rows[1].Text = "所选会话累计   " + Format(data.Total);
            rows[2].Text = "最近请求   输入 " + Format(data.Input) + "  ·  输出 " + Format(data.Output);
            rows[3].Text = "缓存输入   " + Format(data.CachedInput) + "  ·  命中 " + data.CacheRatio + "（输入的子集）";
            rows[4].Text = "上下文估算   " + data.ContextRatio + "  ·  " + Format(data.Context) + " / " + Format(data.Window);
            rows[5].Text = !data.HistoryRead ? "压缩事件   —（记录未确认）" :
                "压缩事件   本次读取观察到 " + data.ObservedCompactions + " 次" +
                (data.PartialHistory ? "（仅最近 8 MB）" : "");
            rows[6].Text = data.RequestUtc == DateTime.MinValue ? "请求时间   —" :
                "请求时间   " + data.RequestUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") + "（历史快照）";
            status.Text = data.Status;
        }
        private static string Format(long? value) { return value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—"; }
        private Label LabelAt(string text, int x, int y, int width, int height, bool title)
        {
            Label label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height),
                ForeColor = title ? palette.Text : palette.MutedText, BackColor = palette.Surface,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Font = title ? UiRendering.CreateTextFont("Microsoft YaHei UI", 12f, FontStyle.Bold) : Font };
            Controls.Add(label); return label;
        }
        private UpdatePillButton ButtonAt(string text, Rectangle bounds, bool primary)
        { UpdatePillButton button = new UpdatePillButton(text, palette, primary) { Bounds = bounds }; Controls.Add(button); return button; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = new GraphicsPath())
            using (Pen pen = new Pen(palette.Border))
            {
                path.AddArc(0, 0, 28, 28, 180, 90); path.AddArc(Width - 29, 0, 28, 28, 270, 90);
                path.AddArc(Width - 29, Height - 29, 28, 28, 0, 90); path.AddArc(0, Height - 29, 28, 28, 90, 90);
                path.CloseFigure(); e.Graphics.DrawPath(pen, path);
                e.Graphics.DrawLine(pen, 20, 116, Width - 20, 116);
                e.Graphics.DrawLine(pen, 20, 336, Width - 20, 336);
            }
        }
        internal void ExportPreview(string path)
        {
            preview = true; choose.Text = "所选会话：示例 · 胶囊开发";
            Apply(new SessionUsageSnapshot { Total = 3520000, Input = 64000, CachedInput = 51200,
                Output = 800, Context = 64800, Window = 258400, RequestUtc = new DateTime(2026, 10, 9, 8, 30, 0, DateTimeKind.Utc),
                ObservedCompactions = 2, HistoryRead = true, Status = "最近一次模型请求（非实时输入框）" });
            rows[0].Text = "账户累计 Token   20.4亿";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            // Parent visibility is false for every offscreen control; explicitly
            // remove the normally hidden selector rather than drawing its frame.
            Controls.Remove(choices);
            try { using (Bitmap bitmap = UiRendering.RenderOffscreen(this)) bitmap.Save(path, ImageFormat.Png); }
            finally { Controls.Add(choices); choices.Visible = false; }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) poll.Dispose(); base.Dispose(disposing); }
    }
}
