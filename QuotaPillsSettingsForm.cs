using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class QuotaPillsSettingsForm : Form
    {
        private readonly NumericUpDown refreshSeconds;

        public OverlaySettings SelectedSettings { get; private set; }

        public QuotaPillsSettingsForm(OverlaySettings current, Rectangle? plusAnchor = null)
        {
            SelectedSettings = (current ?? new OverlaySettings()).Clone();
            Text = "额度胶囊 · 刷新间隔";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = !plusAnchor.HasValue;
            TopMost = true;
            StartPosition = plusAnchor.HasValue
                ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 214);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 9f, FontStyle.Regular);

            Label title = new Label();
            title.Text = "刷新间隔";
            title.Font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 13f, FontStyle.Bold);
            title.Location = new Point(22, 18);
            title.Size = new Size(360, 30);
            Controls.Add(title);

            Label description = new Label();
            description.Text = "额度来自本机 Codex app-server；不修改 Codex 安装文件。";
            description.ForeColor = Color.FromArgb(95, 99, 109);
            description.Location = new Point(24, 51);
            description.Size = new Size(380, 36);
            Controls.Add(description);

            Label refreshLabel = new Label();
            refreshLabel.Text = "自动刷新间隔";
            refreshLabel.Location = new Point(24, 102);
            refreshLabel.Size = new Size(110, 27);
            refreshLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(refreshLabel);

            refreshSeconds = new NumericUpDown();
            refreshSeconds.Minimum = 5;
            refreshSeconds.Maximum = 3600;
            refreshSeconds.Increment = 5;
            refreshSeconds.Value = Math.Max(5, Math.Min(3600, SelectedSettings.RefreshSeconds));
            refreshSeconds.Location = new Point(146, 103);
            refreshSeconds.Size = new Size(106, 26);
            Controls.Add(refreshSeconds);

            Label unit = new Label();
            unit.Text = "秒";
            unit.Location = new Point(260, 103);
            unit.Size = new Size(40, 26);
            unit.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(unit);

            Button save = new Button();
            save.Text = "保存";
            save.Location = new Point(322, 164);
            save.Size = new Size(84, 30);
            save.Click += SaveAndClose;
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(228, 164);
            cancel.Size = new Size(84, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            UpdatePillButton guide = new UpdatePillButton("使用指引", UpdateMenuVisuals.CreateRainbowPalette(), false);
            guide.Bounds = new Rectangle(24, 164, 106, 30);
            guide.Click += delegate
            {
                OverlaySettings guideSettings = SelectedSettings.Clone();
                guideSettings.Theme = "RainbowText";
                using (FirstRunGuideForm form = new FirstRunGuideForm(guideSettings))
                {
                    Rectangle anchor = plusAnchor ?? guide.RectangleToScreen(guide.ClientRectangle);
                    form.Shown += delegate { form.UpdateAnchor(anchor, Screen.FromRectangle(anchor).WorkingArea); };
                    form.ShowDialog(this);
                }
            };
            Controls.Add(guide);

            AcceptButton = save;
            CancelButton = cancel;

            if (plusAnchor.HasValue)
            {
                Rectangle anchor = plusAnchor.Value;
                PositionNearPlus(anchor);
                Shown += delegate { PositionNearPlus(anchor); };
            }
        }

        private void PositionNearPlus(Rectangle anchor)
        {
            Location = PopupAnchorPlacement.NearPlus(
                anchor, Size, Screen.FromRectangle(anchor).WorkingArea);
        }

        private void SaveAndClose(object sender, EventArgs e)
        {
            SelectedSettings.RefreshSeconds = Decimal.ToInt32(refreshSeconds.Value);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
