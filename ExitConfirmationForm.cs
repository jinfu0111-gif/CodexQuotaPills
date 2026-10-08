using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class ExitConfirmationForm : Form
    {
        internal ExitConfirmationForm(Rectangle plusAnchor)
        {
            Text = "退出额度胶囊";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(320, 138);

            Label title = new Label();
            title.Text = "确定退出额度胶囊吗？";
            title.Font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 10f, FontStyle.Bold);
            title.Location = new Point(20, 18);
            title.Size = new Size(280, 27);
            Controls.Add(title);

            Label detail = new Label();
            detail.Text = "后台监听也会停止，重新运行程序即可恢复。";
            detail.ForeColor = Color.FromArgb(95, 99, 109);
            detail.Location = new Point(20, 55);
            detail.Size = new Size(280, 36);
            Controls.Add(detail);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(134, 96);
            cancel.Size = new Size(76, 29);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            Button exit = new Button();
            exit.Text = "退出";
            exit.Location = new Point(220, 96);
            exit.Size = new Size(76, 29);
            exit.ForeColor = Color.FromArgb(176, 38, 68);
            exit.DialogResult = DialogResult.Yes;
            Controls.Add(exit);

            AcceptButton = exit;
            CancelButton = cancel;
            PositionNearPlus(plusAnchor);
            Shown += delegate { PositionNearPlus(plusAnchor); };
        }

        private void PositionNearPlus(Rectangle anchor)
        {
            Location = PopupAnchorPlacement.NearPlus(
                anchor, Size, Screen.FromRectangle(anchor).WorkingArea);
        }
    }
}
