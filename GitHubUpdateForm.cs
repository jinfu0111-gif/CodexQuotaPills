using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal sealed class GitHubUpdateForm : Form
    {
        private readonly GitHubReleaseUpdateService service;
        private readonly Label status = new Label();
        private readonly Button check = new Button();
        private readonly Button download = new Button();
        private readonly Button install = new Button();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly object sync = new object();
        private GitHubUpdateStage staged;
        private bool busy;
        private string message;
        private System.Diagnostics.Process helper;

        internal GitHubUpdateForm(GitHubReleaseUpdateService updates, Rectangle? anchor = null)
        {
            service = updates;
            Text = "额度胶囊 · GitHub 更新"; ClientSize = new Size(460, 238);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            TopMost = true; ShowInTaskbar = !anchor.HasValue; AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            Label version = new Label { Text = "当前版本 v" + GitHubReleaseUpdateService.CurrentVersion,
                Bounds = new Rectangle(22, 18, 414, 28) };
            version.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 12f, FontStyle.Bold);
            Controls.Add(version);
            status.Bounds = new Rectangle(24, 56, 408, 76); Controls.Add(status);
            Controls.Add(new Label { Text = "更新来自 " + GitHubReleaseUpdateService.Repository + "\n下载校验后启用；保留旧版本与自启动备份。",
                ForeColor = Color.DimGray, Bounds = new Rectangle(24, 137, 410, 42) });
            check.Text = "重新检查"; check.Bounds = new Rectangle(24, 191, 104, 30);
            check.Click += delegate { lock (sync) message = null; service.RequestCheck(true); RefreshState(); };
            download.Text = "下载并校验"; download.Bounds = new Rectangle(138, 191, 132, 30);
            download.Click += Download; install.Text = "启用新版本"; install.Bounds = new Rectangle(280, 191, 132, 30);
            install.Click += delegate {
                try { helper = GitHubUpdateInstaller.Apply(staged); message = "正在切换，结果写入更新目录的 result.json。"; busy = true; RefreshState(); }
                catch { message = "无法启用更新。旧版本仍可继续使用，请稍后重试。"; RefreshState(); }
            };
            Controls.Add(check); Controls.Add(download); Controls.Add(install);
            StartPosition = anchor.HasValue ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
            if (anchor.HasValue) Location = PopupAnchorPlacement.NearPlus(anchor.Value, Size,
                Screen.FromRectangle(anchor.Value).WorkingArea, 8);
            timer.Interval = 400; timer.Tick += delegate { RefreshState(); }; timer.Start();
            Shown += delegate { service.RequestCheck(true); RefreshState(); };
        }

        private void Download(object sender, EventArgs args)
        {
            GitHubReleaseUpdateSnapshot release = service.Snapshot();
            if (!GitHubReleaseUpdateService.CanInstall(release)) return;
            lock (sync) { busy = true; message = "正在下载并校验 v" + release.LatestVersion + "…"; }
            RefreshState();
            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    GitHubUpdateStage result = GitHubUpdateInstaller.DownloadAndStage(release);
                    lock (sync) { staged = result; message = "v" + result.Version + " 下载与校验完成，点击“启用新版本”切换。"; }
                }
                catch { lock (sync) message = "下载或校验失败，当前版本保持运行。可以重试。"; }
                finally { lock (sync) busy = false; }
            });
        }

        private void RefreshState()
        {
            GitHubReleaseUpdateSnapshot value = service.Snapshot();
            lock (sync)
            {
                if (helper != null && helper.HasExited)
                {
                    helper.Dispose(); helper = null; busy = false;
                    message = "更新切换未完成，当前版本仍在运行。请查看更新目录的 result.json 或重试。";
                }
                status.Text = message ?? (value.IsChecking ? "正在检查 GitHub 正式版本…" :
                    value.LastError.Length > 0 ? value.LastError :
                    value.UpdateAvailable ? "发现新版本 v" + value.LatestVersion +
                        (GitHubReleaseUpdateService.CanInstall(value) ? "，可以下载。" : "，发布包尚未提供可验证的下载。") :
                    value.LastCheckedUtc.HasValue ? "已是最新正式版本 v" + value.LatestVersion : "点击检查获取最新正式版本。");
                check.Enabled = !busy && !value.IsChecking;
                download.Enabled = !busy && !value.IsChecking && staged == null && GitHubReleaseUpdateService.CanInstall(value);
                install.Enabled = !busy && staged != null;
            }
        }

        protected override void Dispose(bool disposing)
        { if (disposing) { timer.Dispose(); if (helper != null) helper.Dispose(); } base.Dispose(disposing); }
    }
}
