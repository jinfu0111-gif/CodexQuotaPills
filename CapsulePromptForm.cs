using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class CapsulePromptForm : Form
    {
        internal CapsulePromptForm(string title, string message, Rectangle anchor, bool installationDecision = false)
        {
            Text = title; FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.Manual; ShowInTaskbar = false;
            ClientSize = new Size(430, 190);
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            UpdateMenuPalette palette = UpdateMenuVisuals.CreateRainbowPalette();
            Controls.Add(new Label { Text = title, Bounds = new Rectangle(20, 18, 350, 28),
                Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 11f, FontStyle.Bold) });
            Controls.Add(new Label { Text = message, Bounds = new Rectangle(22, 56, 386, 74) });
            UpdatePillButton close = new UpdatePillButton("×", palette, false) {
                Bounds = new Rectangle(386, 16, 26, 26), DialogResult = DialogResult.Cancel };
            close.Click += delegate { Close(); }; Controls.Add(close);
            if (installationDecision)
            {
                UpdatePillButton defer = new UpdatePillButton("退出后生效", palette, true) {
                    Bounds = new Rectangle(20, 144, 124, 32), DialogResult = DialogResult.No };
                UpdatePillButton force = new UpdatePillButton("立即关闭并更新", palette, false) {
                    Bounds = new Rectangle(153, 144, 150, 32), DialogResult = DialogResult.Yes, Danger = true };
                UpdatePillButton cancel = new UpdatePillButton("取消", palette, false) {
                    Bounds = new Rectangle(312, 144, 98, 32), DialogResult = DialogResult.Cancel };
                Controls.Add(defer); Controls.Add(force); Controls.Add(cancel);
                AcceptButton = defer; CancelButton = cancel; // Preserve safe default; never force shutdown via Enter.
            }
            else
            {
                UpdatePillButton ok = new UpdatePillButton("知道了", palette, true) {
                    Bounds = new Rectangle(294, 144, 116, 32), DialogResult = DialogResult.OK };
                Controls.Add(ok); AcceptButton = ok; CancelButton = ok;
            }
            PopupUiContext.Bind(this, anchor, palette);
        }
    }
}
