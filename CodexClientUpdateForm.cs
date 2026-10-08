using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Blues19.CodexInstaller;

namespace CodexUsageOverlay
{
    // A compact, deliberately separate surface: the existing GitHub menu updates
    // Quota Pills; this form updates the Microsoft-signed Codex desktop MSIX.
    internal sealed class CodexClientUpdateForm : Form
    {
        private readonly UpdateMenuPalette palette = UpdateMenuVisuals.CreateRainbowPalette();
        private readonly Label installedValue;
        private readonly Label latestValue;
        private readonly Label status;
        private readonly Panel progressFill;
        private readonly UpdatePillButton checkButton;
        private readonly UpdatePillButton downloadButton;
        private readonly UpdatePillButton updateButton;
        private readonly UpdatePillButton closeButton;
        private CancellationTokenSource cancellation;
        private PackageInfo latest;
        private InstalledPackage installed;
        private string packagePath;
        private bool busy;
        private bool previewMode;

        internal CodexClientUpdateForm(Rectangle anchor)
        {
            Text = "更新 Codex 客户端";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = palette.Surface;
            ClientSize = new Size(396, 310);
            DoubleBuffered = true;
            KeyPreview = true;

            Label title = AddLabel("更新 Codex 客户端", new Rectangle(18, 15, 295, 26),
                12f, FontStyle.Bold, palette.Text);
            title.TextAlign = ContentAlignment.MiddleLeft;
            closeButton = AddButton("×", new Rectangle(352, 13, 28, 28), false);
            closeButton.Click += delegate { Close(); };

            AddLabel("微软官方 MSIX  ·  与胶囊自身更新分开",
                new Rectangle(19, 43, 358, 22), 8.5f, FontStyle.Regular, palette.MutedText);

            Panel versionCard = new Panel();
            versionCard.Location = new Point(16, 72);
            versionCard.Size = new Size(364, 83);
            versionCard.BackColor = Color.White;
            versionCard.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(palette.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, versionCard.Width - 1, versionCard.Height - 1);
            };
            Controls.Add(versionCard);
            AddCardLabel(versionCard, "本机版本", new Rectangle(14, 10, 90, 27), palette.MutedText);
            installedValue = AddCardLabel(versionCard, "检测中…", new Rectangle(105, 10, 239, 27), palette.Text);
            AddCardLabel(versionCard, "最新版本", new Rectangle(14, 45, 90, 27), palette.MutedText);
            latestValue = AddCardLabel(versionCard, "等待检查", new Rectangle(105, 45, 239, 27), palette.Text);

            status = AddLabel("正在读取本机 Codex 版本…",
                new Rectangle(19, 169, 360, 37), 9f, FontStyle.Regular, palette.MutedText);
            status.AutoEllipsis = true;

            Panel progressTrack = new Panel();
            progressTrack.Location = new Point(18, 211);
            progressTrack.Size = new Size(360, 5);
            progressTrack.BackColor = Color.FromArgb(224, 227, 235);
            Controls.Add(progressTrack);
            progressFill = new Panel();
            progressFill.Size = new Size(0, 5);
            progressFill.Location = Point.Empty;
            progressFill.BackColor = palette.Accent;
            progressTrack.Controls.Add(progressFill);

            checkButton = AddButton("检查更新", new Rectangle(16, 232, 114, 34), false);
            downloadButton = AddButton("仅下载", new Rectangle(141, 232, 114, 34), false);
            updateButton = AddButton("一键更新", new Rectangle(266, 232, 114, 34), true);
            checkButton.Click += delegate { StartWork(CheckForUpdate); };
            downloadButton.Click += delegate { StartWork(DownloadLatest); };
            updateButton.Click += delegate { StartWork(UpdateCodex); };

            LinkLabel folder = AddLink("打开安装包目录", new Point(18, 280), 126);
            folder.LinkClicked += delegate { OpenFolder(); };
            LinkLabel official = AddLink("官方更新入口", new Point(272, 280), 108);
            official.TextAlign = ContentAlignment.MiddleRight;
            official.LinkClicked += delegate { OpenOfficialPage(); };

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
            Shown += delegate
            {
                if (!previewMode)
                {
                    SetLocationNear(anchor);
                    StartWork(CheckForUpdate);
                }
            };
            FormClosing += OnClosing;
            SetLocationNear(anchor);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (Pen border = new Pen(palette.Border))
                e.Graphics.DrawPath(border, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), 14))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private void SetLocationNear(Rectangle anchor)
        {
            Screen screen = Screen.FromRectangle(anchor);
            Location = PopupAnchorPlacement.NearPlus(
                anchor, Size, screen.WorkingArea);
        }

        private Label AddLabel(string text, Rectangle bounds, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = bounds.Location;
            label.Size = bounds.Size;
            label.ForeColor = color;
            label.BackColor = palette.Surface;
            label.Font = UiRendering.CreateTextFont("Microsoft YaHei UI", size, style);
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);
            return label;
        }

        private Label AddCardLabel(Control parent, string text, Rectangle bounds, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = bounds.Location;
            label.Size = bounds.Size;
            label.ForeColor = color;
            label.BackColor = Color.White;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            parent.Controls.Add(label);
            return label;
        }

        private LinkLabel AddLink(string text, Point point, int width)
        {
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.Location = point;
            link.Size = new Size(width, 21);
            link.LinkColor = palette.Accent;
            link.ActiveLinkColor = palette.Accent;
            link.VisitedLinkColor = palette.Accent;
            link.BackColor = palette.Surface;
            Controls.Add(link);
            return link;
        }

        private UpdatePillButton AddButton(string text, Rectangle bounds, bool primary)
        {
            UpdatePillButton button = new UpdatePillButton(text, palette, primary);
            button.Location = bounds.Location;
            button.Size = bounds.Size;
            Controls.Add(button);
            return button;
        }

        private void StartWork(Action<CancellationToken> operation)
        {
            if (busy) return;
            busy = true;
            cancellation = new CancellationTokenSource();
            checkButton.Enabled = false;
            downloadButton.Enabled = false;
            updateButton.Enabled = false;
            SetProgress(0);
            Thread worker = new Thread(delegate()
            {
                try
                {
                    operation(cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    SetStatus("已取消。本机 Codex 未被主动关闭。");
                }
                catch (Exception ex)
                {
                    SetStatus("更新失败：" + FirstLine(ex.Message));
                }
                finally
                {
                    Ui(delegate
                    {
                        busy = false;
                        checkButton.Enabled = true;
                        downloadButton.Enabled = true;
                        updateButton.Enabled = installed != null && latest != null &&
                            installed.Version != null && latest.Version != null &&
                            installed.Version < latest.Version;
                        cancellation.Dispose();
                        cancellation = null;
                    });
                }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        private void CheckForUpdate(CancellationToken token)
        {
            Logger log = new Logger(false);
            SetStatus("正在读取本机版本…");
            installed = AppxInstaller.GetInstalled(log);
            Ui(delegate
            {
                installedValue.Text = installed == null ? "未检测到 MSIX 安装" : installed.Version.ToString();
            });
            if (installed == null)
            {
                latest = null;
                Ui(delegate { latestValue.Text = "—"; });
                SetStatus("仅支持已安装的 Codex 商店/MSIX 版本。");
                return;
            }

            SetStatus("正在查询微软分发服务器…");
            latest = StoreApi.FindLatestPackage(log, token);
            Ui(delegate { latestValue.Text = latest.VersionText + "  ·  " + latest.SizeText; });
            SetStatus(installed.Version >= latest.Version
                ? "当前已是微软分发的最新版本。"
                : "发现新版；可仅下载，或一键更新。");
        }

        private void DownloadLatest(CancellationToken token)
        {
            CheckForUpdate(token);
            if (installed == null || latest == null) return;
            DownloadPackage(token);
        }

        private void UpdateCodex(CancellationToken token)
        {
            CheckForUpdate(token);
            if (installed == null || latest == null || installed.Version >= latest.Version) return;
            DownloadPackage(token);
            token.ThrowIfCancellationRequested();
            Logger log = new Logger(false);
            SetStatus("正在交给 Windows 安装；Codex 运行中会询问处理方式…");
            bool deferred = AppxInstaller.Install(packagePath, log,
                delegate(int percent) { SetProgress(percent); }, AskInUseDecision, token);
            if (deferred)
            {
                SetStatus("新版本已就位；完全退出 Codex 后生效。");
                SetProgress(100);
                return;
            }
            InstalledPackage now = AppxInstaller.GetInstalled(log);
            if (now == null || now.Version < latest.Version)
                throw new InvalidOperationException("Windows 已返回安装结果，但未读到目标版本；请检查更新日志。");
            installed = now;
            Ui(delegate { installedValue.Text = now.Version.ToString(); });
            SetProgress(100);
            SetStatus("Codex 客户端已更新至 " + now.Version + "。");
        }

        private void DownloadPackage(CancellationToken token)
        {
            Logger log = new Logger(false);
            packagePath = latest.LocalPath(Paths.PackageDir);
            if (Downloader.VerifyCachedPackage(packagePath, latest))
            {
                SetProgress(100);
                SetStatus("已校验本机缓存安装包，可直接安装。");
                return;
            }
            if (File.Exists(packagePath))
                File.Delete(packagePath); // Only the exact app-owned cache path.
            string oldPath = latest.LocalPath(Paths.LegacyPackageDirPath);
            if (File.Exists(oldPath) &&
                !String.Equals(oldPath, packagePath, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("正在迁移已校验的安装包到系统可访问目录…");
                if (Downloader.TryCopyVerifiedCache(oldPath, packagePath, latest))
                {
                    SetProgress(100);
                    SetStatus("已迁移并校验安装包，可直接安装。");
                    return;
                }
            }
            SetStatus("正在获取微软签名下载链接…");
            StoreApi.PrepareDownload(latest, log, token);
            Downloader downloader = new Downloader(
                delegate(string message) { log.Info(message); },
                delegate(Downloader.Progress progress)
                {
                    int percent = progress.Total <= 0 ? 0 :
                        (int)Math.Round(progress.Downloaded * 100d / progress.Total);
                    SetProgress(percent);
                    SetStatus("正在下载 " + percent + "%  ·  " +
                        Fmt.Size(progress.Downloaded) + " / " + Fmt.Size(progress.Total));
                }, token);
            downloader.Download(latest.Url, packagePath, latest.SizeBytes,
                latest.DigestBase64, latest.DigestAlgorithm);
            if (!Downloader.VerifyCachedPackage(packagePath, latest))
                throw new IOException("下载后的安装包未通过二次摘要校验。");
            SetProgress(100);
            SetStatus("安装包下载并校验完成。");
        }

        private InUseDecision AskInUseDecision()
        {
            DialogResult result = (DialogResult)Invoke(new Func<DialogResult>(delegate
            {
                return MessageBox.Show(this,
                    "Codex 正在运行。\n\n【否】下次完全退出 Codex 后生效（推荐）" +
                    "\n【是】立即关闭 Codex 并安装，可能中断当前任务" +
                    "\n【取消】放弃安装",
                    "Codex 正在运行", MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            }));
            if (result == DialogResult.Yes) return InUseDecision.ForceShutdown;
            if (result == DialogResult.No) return InUseDecision.DeferToNextExit;
            return InUseDecision.Abort;
        }

        private void SetStatus(string value)
        {
            Ui(delegate { status.Text = value; });
        }

        private void SetProgress(int percent)
        {
            Ui(delegate
            {
                int safe = Math.Max(0, Math.Min(100, percent));
                progressFill.Width = (int)Math.Round(360d * safe / 100d);
            });
        }

        private void Ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy) return;
            e.Cancel = true;
            if (cancellation != null) cancellation.Cancel();
            status.Text = "正在取消操作，请稍候…";
        }

        private void OpenFolder()
        {
            try
            {
                string directory = Paths.PackageDir;
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex) { status.Text = "打开目录失败：" + FirstLine(ex.Message); }
        }

        private void OpenOfficialPage()
        {
            try { Process.Start(CodexProduct.StorePageUrl); }
            catch (Exception ex) { status.Text = "打开官方页面失败：" + FirstLine(ex.Message); }
        }

        private static string FirstLine(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return "未知错误";
            return message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal void ExportPreview(string path)
        {
            previewMode = true;
            installedValue.Text = "示例 · 26.1002.7124.0";
            latestValue.Text = "示例 · 26.1002.7124.0";
            status.Text = "界面示例；实际版本请点击“检查更新”查询。";
            Location = new Point(-32000, -32000);
            Show();
            Application.DoEvents();
            using (Bitmap image = new Bitmap(Width, Height))
            {
                DrawToBitmap(image, new Rectangle(Point.Empty, Size));
                image.Save(path, ImageFormat.Png);
            }
            Hide();
        }
    }

    internal sealed class UpdatePillButton : Control
    {
        private readonly UpdateMenuPalette palette;
        private readonly bool primary;
        private bool hovered;

        internal UpdatePillButton(string text, UpdateMenuPalette palette, bool primary)
        {
            Text = text;
            this.palette = palette;
            this.primary = primary;
            Cursor = Cursors.Hand;
            Font = UiRendering.CreateTextFont("Microsoft YaHei UI", 9f, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(Height / 2, 16);
            using (GraphicsPath path = new GraphicsPath())
            {
                int diameter = radius * 2;
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                Color fill = !Enabled ? palette.SurfaceAlt :
                    primary ? (hovered ? Color.FromArgb(91, 51, 162) : palette.Accent) :
                    hovered ? palette.Hover : Color.White;
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen border = new Pen(primary && Enabled ? palette.Accent : palette.Border))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(border, path);
                }
            }
            Color text = !Enabled ? palette.MutedText : primary ? Color.White : palette.Text;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
