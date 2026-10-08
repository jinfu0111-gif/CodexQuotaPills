using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class AutoResumeForm : Form
    {
        private readonly AutoResumeService service;
        private readonly UpdateMenuPalette palette = UpdateMenuVisuals.CreateRainbowPalette();
        private readonly Label summary;
        private readonly ListBox list;
        private readonly UpdatePillButton toggle;
        private readonly UpdatePillButton cancel;
        private readonly Timer timer;
        private bool preview;

        internal AutoResumeForm(AutoResumeService service, Rectangle anchor)
        {
            this.service = service;
            Text = "额度恢复 · 自动续跑";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = palette.Surface;
            ClientSize = new Size(540, 340);
            DoubleBuffered = true;
            KeyPreview = true;
            Label title = Label("额度恢复 · 自动续跑", new Rectangle(18, 12, 440, 30));
            title.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 12f, FontStyle.Bold);
            UpdatePillButton close = Button("×", new Rectangle(494, 13, 28, 28), false);
            close.Click += delegate { Close(); };
            summary = Label("后台自动监测中，无需手动扫描或开启", new Rectangle(19, 46, 500, 28));
            summary.ForeColor = palette.MutedText;
            list = new ListBox();
            list.Bounds = new Rectangle(18, 82, 504, 166);
            list.BorderStyle = BorderStyle.None;
            list.BackColor = palette.Surface;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 54;
            list.IntegralHeight = false;
            list.DrawItem += DrawCandidate;
            list.SelectedIndexChanged += delegate { cancel.Enabled = list.SelectedItem is ResumeCandidate; };
            Controls.Add(list);
            toggle = Button("暂停自动续跑", new Rectangle(18, 261, 156, 32), true);
            toggle.Click += delegate
            {
                if (this.service == null) return;
                this.service.SetEnabled(!this.service.Enabled);
                RefreshView();
            };
            UpdatePillButton scan = Button("立即检查", new Rectangle(192, 261, 156, 32), false);
            scan.Click += delegate { if (this.service != null) this.service.RequestScan(); };
            cancel = Button("取消所选", new Rectangle(366, 261, 156, 32), false);
            cancel.Enabled = false;
            cancel.Click += delegate
            {
                ResumeCandidate item = list.SelectedItem as ResumeCandidate;
                if (item != null && this.service != null) this.service.Cancel(item.Key);
                RefreshView();
            };
            Label note = Label("默认自动检测并续跑；每 30 秒复查，额度提前恢复也会继续。\n仅处理额度中断，每个对话最多 3 次；关闭面板不暂停监测。", new Rectangle(20, 301, 500, 34));
            note.ForeColor = palette.MutedText;
            note.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular);
            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate { RefreshView(); };
            Shown += delegate
            {
                Location = PopupAnchorPlacement.NearPlus(anchor, Size, Screen.FromRectangle(anchor).WorkingArea);
                if (!preview && this.service != null) { RefreshView(); this.service.RequestScan(); timer.Start(); }
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Location = PopupAnchorPlacement.NearPlus(anchor, Size, Screen.FromRectangle(anchor).WorkingArea);
        }

        private Label Label(string text, Rectangle bounds)
        {
            Label label = new Label { Text = text, Bounds = bounds, ForeColor = palette.Text, BackColor = palette.Surface,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Controls.Add(label);
            return label;
        }
        private UpdatePillButton Button(string text, Rectangle bounds, bool primary)
        {
            UpdatePillButton button = new UpdatePillButton(text, palette, primary) { Bounds = bounds };
            Controls.Add(button);
            return button;
        }
        private void RefreshView()
        {
            if (service == null) return;
            summary.Text = service.Scanning ? "正在扫描未归档对话…（不会发送到其他窗口）" : service.Message;
            toggle.Text = service.Enabled ? "暂停自动续跑" : "恢复自动续跑";
            string selected = (list.SelectedItem as ResumeCandidate) == null ? null : ((ResumeCandidate)list.SelectedItem).Key;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (ResumeCandidate c in service.Snapshot())
            {
                int index = list.Items.Add(c);
                if (c.Key == selected) list.SelectedIndex = index;
            }
            if (list.Items.Count == 0) list.Items.Add(service.Scanning ? "正在检查…" : "暂未发现因额度中断的对话");
            list.EndUpdate();
            cancel.Enabled = list.SelectedItem is ResumeCandidate;
        }
        private void DrawCandidate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(e.Bounds.X + 1, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 5);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (GraphicsPath path = Round(card, 13))
            using (Brush fill = new SolidBrush(selected ? palette.Hover : Color.White))
            using (Pen border = new Pen(selected ? palette.Accent : palette.Border))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            ResumeCandidate c = list.Items[e.Index] as ResumeCandidate;
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            Rectangle title = new Rectangle(card.Left + 13, card.Top + 5, card.Width - 26, 20);
            TextRenderer.DrawText(e.Graphics, c == null ? Convert.ToString(list.Items[e.Index]) : c.Title, Font, title, palette.Text, flags);
            if (c != null)
                TextRenderer.DrawText(e.Graphics, c.Status, Font, new Rectangle(card.Left + 13, card.Top + 25, card.Width - 26, 18), palette.MutedText, flags);
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
            using (GraphicsPath path = Round(new Rectangle(0, 0, Width, Height), 14))
            { Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null) timer.Dispose();
            base.Dispose(disposing);
        }
        private static GraphicsPath Round(Rectangle bounds, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            p.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            p.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            p.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        internal void ExportPreview(string path)
        {
            preview = true;
            summary.Text = "已检查 24 个未归档对话，发现 2 个额度中断 · 自动续跑开启";
            toggle.Text = "暂停自动续跑";
            list.Items.Add(new ResumeCandidate { Title = "示例 · 完成胶囊功能开发", Status = "等待 5h / 周额度恢复 · 10/8 18:30" });
            list.Items.Add(new ResumeCandidate { Title = "示例 · 修复测试并验证", Status = "额度中断 · 等待原对话状态验证" });
            Show();
            Application.DoEvents();
            using (Bitmap bitmap = new Bitmap(Width, Height))
            { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(path, ImageFormat.Png); }
            Hide();
        }
    }
}
