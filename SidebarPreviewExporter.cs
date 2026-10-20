using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class SidebarPreviewExporter
    {
        // Uses the application's real capsule and menu renderers, with example data.
        // Exits before creating the live usage / automatic-resume services.
        internal static void Export(string output)
        {
            Directory.CreateDirectory(output);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            foreach (bool dark in new[] { false, true })
            {
                PopupUiContext.Dark = dark;
                PopupUiContext.Theme = dark ? "Dark" : "Light";
                string menuFile = Path.Combine(output, "sidebar-menu-" + (dark ? "dark" : "light") + ".png");
                using (PlacementSelectorForm form = new PlacementSelectorForm(null))
                    form.ExportPreview(menuFile, OverlayPlacement.Sidebar);
                using (Bitmap bitmap = UiRendering.CreateLayeredBitmap(450, 800))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Font font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular))
                using (Brush side = new SolidBrush(dark ? Color.FromArgb(22, 26, 33) : Color.FromArgb(239, 244, 249)))
                using (Bitmap menu = new Bitmap(menuFile))
                {
                    graphics.Clear(dark ? Color.FromArgb(18, 21, 27) : Color.White);
                    graphics.FillRectangle(side, new Rectangle(0, 0, 52, 800));
                    // Preview chrome is illustrative; rings and menu are the real renderers.
                    using (Brush muted = new SolidBrush(QuotaPillVisuals.MutedText))
                    using (StringFormat center = UiRendering.CreateTextFormat())
                    {
                        center.Alignment = StringAlignment.Center; center.LineAlignment = StringAlignment.Center;
                        foreach (int y in new[] { 60, 104, 148, 192, 236, 280, 324 })
                            graphics.DrawString("·", font, muted, new Rectangle(8, y, 36, 36), center);
                        graphics.DrawString("○", font, muted, new Rectangle(8, 746, 36, 36), center);
                    }
                    Rectangle capsule = OverlayPlacementLayout.CalculateBounds(OverlayPlacement.Sidebar,
                        new Rectangle(0, 0, 450, 800), new Rectangle(0, 0, 450, 800), new Size(40, ComposerUsagePillLayout.SidebarHeight), 1f,
                        new Rectangle(0, 0, 52, 800));
                    graphics.TranslateTransform(capsule.Left, capsule.Top);
                    ComposerUsagePillLayout layout = ComposerUsagePillLayout.CreateSidebar(new UsageData {
                        Plan = "Plus", ShortRemaining = 56, WeeklyRemaining = 71,
                        ProfileTokensText = "17.2亿", AvailableResetCredits = 2 }, graphics, font, capsule.Width);
                    ComposerUsagePillLayout.Draw(graphics, layout, font, false, false);
                    graphics.ResetTransform();
                    Rectangle plus = layout.Pills[0].RingBounds;
                    plus.Offset(capsule.Location);
                    Point position = PopupAnchorPlacement.Calculate(plus, menu.Size,
                        new Rectangle(0, 0, 450, 800), true, new Rectangle(0, 0, 52, 800));
                    graphics.DrawImageUnscaled(menu, position);
                    using (Bitmap viewport = bitmap.Clone(new Rectangle(0, 400, 450, 400), PixelFormat.Format32bppArgb))
                        viewport.Save(Path.Combine(output, "sidebar-" + (dark ? "dark" : "light") + "-preview.png"), ImageFormat.Png);
                }
            }
            PopupUiContext.Dark = false;
            PopupUiContext.Theme = "Auto";
            using (QuotaHoverCardForm form = new QuotaHoverCardForm())
                form.ExportPreview(Path.Combine(output, "sidebar-quota-details-preview.png"),
                    ComposerUsagePillTooltips.BuildSidebarCard(new UsageData {
                        Plan = "Plus", ProfileTokensText = "17.2亿", ShortResetText = "14:52", HasShortResetText = true,
                        WeeklyResetText = "10/14", HasWeeklyResetText = true, AvailableResetCredits = 2
                    }, null, DateTimeOffset.Now));
            ExportHostBars(Path.Combine(output, "placement-bars-preview.png"));
            ExportComposerHidden(Path.Combine(output, "composer-hide-preview.png"));
        }

        private static void ExportComposerHidden(string path)
        {
            Rectangle window = new Rectangle(0, 0, 1205, 300);
            UsageData usage = new UsageData { Plan = "Plus", ShortRemaining = 81, WeeklyRemaining = 97 };
            OverlaySettings minimal = new OverlaySettings { ShowPlanPill = false, ShowTokenPill = false, ShowResetCreditsPill = false };
            using (Bitmap bitmap = UiRendering.CreateLayeredBitmap(window.Width, window.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Brush text = new SolidBrush(QuotaPillVisuals.Text))
            using (Brush muted = new SolidBrush(QuotaPillVisuals.MutedText))
            using (Brush chrome = new SolidBrush(Color.FromArgb(239, 244, 249)))
            using (Pen border = new Pen(QuotaPillVisuals.Border))
            {
                graphics.Clear(Color.White); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.FillRectangle(chrome, 0, 0, window.Width, 44);
                graphics.DrawString("文件       编辑       查看       帮助", font, text, 120, 15);
                graphics.DrawString("—           □          ×", font, text, 1080, 15);
                graphics.DrawLine(border, 290, 44, 290, 270);
                graphics.DrawLine(border, 610, 44, 610, 270);
                graphics.DrawString("Codex 聊天", font, text, 308, 65);
                graphics.DrawString("内置浏览器 · ChatGPT", font, text, 632, 65);
                graphics.DrawString("输入框空间不足：胶囊隐藏，空间恢复并稳定 400ms 后重新显示（示意）", font, muted, 290, 278);
                Rectangle editor = new Rectangle(322, 184, 21, 36);
                Rectangle context = new Rectangle(318, 216, 28, 28);
                Rectangle action = new Rectangle(562, 216, 28, 28);
                Rectangle container = new Rectangle(306, 168, 288, 88);
                Rectangle pane = new Rectangle(290, 44, 320, 226);
                using (GraphicsPath input = ComposerUsagePillLayout.RoundedRectangle(container, 20))
                    graphics.DrawPath(border, input);
                using (GraphicsPath input = ComposerUsagePillLayout.RoundedRectangle(new Rectangle(630, 168, 550, 88), 20))
                    graphics.DrawPath(border, input);
                graphics.DrawString("与 Codex 协作", font, muted, 322, 186);
                graphics.DrawString("询问 ChatGPT", font, muted, 649, 186);
                graphics.DrawString("+", font, text, 326, 222);
                graphics.DrawString("GPT-6.1 Sol 高   ♩   ↑", font, text, 375, 222);
                string hostKind;
                Rectangle host = ComposerPlacementPolicy.FindAttachedHost(editor, context, action, container, pane,
                    new[] { editor, context, action, new Rectangle(375, 216, 182, 28) }, window, 1f, out hostKind);
                DateTime now = DateTime.UtcNow; OverlayPlacement effective;
                Rectangle bounds = ComposerPlacementPolicy.Resolve(new IntPtr(1), window, window,
                    new Size(ComposerUsagePillLayout.MeasureWidth(usage, graphics, font, minimal), 28), 1,
                    new ComposerProbeSnapshot { Handle = new IntPtr(1), Window = window, Scale = 1,
                        CapturedUtc = now, State = ComposerProbeState.Composer, ToolbarHost = host,
                        Pane = pane, HostKind = hostKind }, now, out effective);
                if (!bounds.IsEmpty)
                {
                    graphics.TranslateTransform(bounds.X, bounds.Y);
                    ComposerUsagePillLayout.Draw(graphics, ComposerUsagePillLayout.Create(usage, graphics, font, bounds.Width, minimal), font, false, false);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void ExportHostBars(string path)
        {
            Rectangle window = new Rectangle(0, 0, 1205, 986);
            UsageData usage = new UsageData { Plan = "Plus", ShortRemaining = 92, WeeklyRemaining = 64,
                ProfileTokensText = "20.4亿", AvailableResetCredits = 3 };
            using (Bitmap bitmap = UiRendering.CreateLayeredBitmap(1205, 180))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Brush text = new SolidBrush(QuotaPillVisuals.Text))
            using (Brush chrome = new SolidBrush(Color.FromArgb(239, 244, 249)))
            {
                graphics.Clear(Color.White);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                OverlayPlacement[] placements = { OverlayPlacement.TitleBar, OverlayPlacement.ComposerBottom };
                for (int i = 0; i < placements.Length; i++)
                {
                    OverlayPlacement placement = placements[i];
                    int viewportTop = i == 0 ? 0 : 886;
                    graphics.TranslateTransform(0, i * 90 - viewportTop);
                    if (i == 0)
                    {
                        graphics.FillRectangle(chrome, 0, 0, 1205, 44);
                        graphics.DrawString("文件       编辑       查看       帮助", font, text, 120, 15);
                        graphics.DrawString("—           □          ×", font, text, 1080, 15);
                    }
                    else
                    {
                        using (Pen border = new Pen(QuotaPillVisuals.Border))
                        using (GraphicsPath input = ComposerUsagePillLayout.RoundedRectangle(new Rectangle(379, 890, 736, 86), 20))
                            graphics.DrawPath(border, input);
                        graphics.DrawString("与 Codex 协作", font, text, 395, 908);
                        graphics.DrawString("+      帮我批准", font, text, 397, 938);
                        graphics.DrawString("GPT-6.1 Sol      ♩      ↑", font, text, 935, 938);
                    }
                    OverlaySettings renderSettings = placement == OverlayPlacement.ComposerBottom
                        ? new OverlaySettings { ShowPlanPill = false, ShowTokenPill = false, ShowResetCreditsPill = false } : null;
                    Size preferred = new Size(ComposerUsagePillLayout.MeasureWidth(usage, graphics, font, renderSettings), 28);
                    Rectangle bounds;
                    if (placement == OverlayPlacement.ComposerBottom)
                    {
                        // Example accessibility geometry, not a claim of live Codex detection.
                        Rectangle host = ComposerPlacementPolicy.FindToolbarHost(new Rectangle(395, 902, 700, 24),
                            new Rectangle(379, 890, 736, 86), new Rectangle(1078, 936, 32, 32),
                            new[] { new Rectangle(397, 936, 128, 32), new Rectangle(935, 936, 139, 32) }, window, 1f);
                        OverlayPlacement effective;
                        DateTime now = DateTime.UtcNow;
                        bounds = ComposerPlacementPolicy.Resolve(new IntPtr(1), window, window, preferred, 1f,
                            new ComposerProbeSnapshot { Handle = new IntPtr(1), Window = window, Scale = 1f,
                                CapturedUtc = now, State = ComposerProbeState.Composer, ToolbarHost = host,
                                Pane = new Rectangle(290, 44, 900, 940), HostKind = "Toolbar" }, now, out effective);
                    }
                    else bounds = OverlayPlacementLayout.CalculateBounds(placement, window, window, preferred, 1f, Rectangle.Empty);
                    if (!bounds.IsEmpty)
                    {
                        graphics.TranslateTransform(bounds.X, bounds.Y);
                        ComposerUsagePillLayout.Draw(graphics, ComposerUsagePillLayout.Create(usage, graphics, font, bounds.Width, renderSettings), font, false, false);
                    }
                    graphics.ResetTransform();
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }
}
