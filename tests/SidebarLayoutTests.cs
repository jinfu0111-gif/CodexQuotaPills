using System;
using System.Drawing;
using System.IO;

namespace CodexUsageOverlay
{
    internal static class SidebarLayoutTests
    {
        internal static void FourPositionsAndThemesRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "pills-sidebar-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                foreach (OverlayPlacement placement in Enum.GetValues(typeof(OverlayPlacement)))
                    foreach (string theme in new[] { "Auto", "Light", "Dark" })
                    {
                        Assert(OverlaySettingsStore.SaveToPath(new OverlaySettings { Placement = placement, UiTheme = theme }, path), "save failed");
                        OverlaySettings loaded = OverlaySettingsStore.LoadFromPath(path);
                        Assert(loaded.Placement == placement && loaded.UiTheme == theme, "setting did not round trip");
                    }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        internal static void SidebarGeometryAndRightPopups()
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                foreach (int origin in new[] { -2400, 0, 1800 })
                {
                    Rectangle window = new Rectangle(origin, 50, (int)(1000 * scale), (int)(800 * scale));
                    Rectangle area = new Rectangle(origin - 20, 0, (int)(1100 * scale), (int)(1000 * scale));
                    Rectangle sidebar = new Rectangle(window.Location, new Size((int)(52 * scale), window.Height));
                    Rectangle capsule = OverlayPlacementLayout.CalculateBounds(OverlayPlacement.Sidebar,
                        window, area, new Size(400, 28), scale, sidebar);
                    Assert(sidebar.Contains(capsule) && capsule.Height == (int)(ComposerUsagePillLayout.SidebarHeight * scale), "sidebar geometry invalid");
                    Assert(capsule.Top >= window.Top + (int)(400 * scale) && capsule.Bottom <= window.Bottom - (int)(110 * scale), "navigation icons or avatar covered");
                    Point position = PopupAnchorPlacement.Calculate(capsule,
                        new Size((int)(PlacementMenuLayout.LogicalWidth * scale), (int)(PlacementMenuLayout.LogicalHeight * scale)), window, true, sidebar);
                    Assert(position.X >= sidebar.Right && position.X < window.Left + (int)(80 * scale), "popup did not open from rail to right");
                    Assert(window.Contains(new Rectangle(position, new Size((int)(PlacementMenuLayout.LogicalWidth * scale), (int)(PlacementMenuLayout.LogicalHeight * scale)))), "popup escaped window");
                }
            Assert(OverlayPlacementLayout.CalculateBounds(OverlayPlacement.Sidebar, new Rectangle(0, 0, 1000, 800),
                new Rectangle(0, 0, 1920, 1080), new Size(252, 58), 1f, Rectangle.Empty).IsEmpty,
                "unknown sidebar was drawn over content");
            Assert(OverlayPlacementLayout.CalculateBounds(OverlayPlacement.Sidebar, new Rectangle(0, 0, 1000, 650),
                new Rectangle(0, 0, 1920, 1080), new Size(40, 184), 1f, new Rectangle(0, 0, 52, 650)).IsEmpty,
                "short window covered navigation icons");
            Assert(OverlayPlacementLayout.CalculateBounds(OverlayPlacement.Sidebar, new Rectangle(0, 0, 1000, 800),
                new Rectangle(0, 0, 1920, 1080), new Size(40, 184), 1f, new Rectangle(0, 0, 290, 800)).IsEmpty,
                "chat list was mistaken for narrow rail");
        }

        internal static void SidebarPreservesPillsAndHitTargets()
        {
            UsageData data = new UsageData { Plan = "Plus", ShortRemaining = 99, WeeklyRemaining = 88,
                ProfileTokensText = "16.3亿", AvailableResetCredits = 3 };
            using (Bitmap canvas = UiRendering.CreateLayeredBitmap(40, 184))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Font font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular))
            {
                ComposerUsagePillLayout layout = ComposerUsagePillLayout.CreateSidebar(data, graphics, font, 40);
                Assert(layout.SidebarRings && layout.Pills.Count == 2, "sidebar must contain exactly two quota rings");
                Assert(layout.Pills[0].Kind == ComposerUsagePillKind.ShortQuota && layout.Pills[1].Kind == ComposerUsagePillKind.WeeklyQuota,
                    "menu / Token / reset credits still occupy the rail");
                Assert(layout.Pills[0].Remaining == 99 && layout.Pills[1].Remaining == 88, "ring arc lost remaining quota");
                foreach (ComposerUsagePill pill in layout.Pills)
                {
                    Assert(new Rectangle(0, 0, 40, ComposerUsagePillLayout.SidebarHeight).Contains(pill.Bounds) &&
                        pill.RingBounds.Width == pill.RingBounds.Height && pill.Bounds.Contains(pill.RingBounds), "ring clipped");
                    Assert(pill.LabelBounds.Top > pill.RingBounds.Bottom && pill.Bounds.Contains(pill.LabelBounds) &&
                        pill.LabelBounds.Left == pill.RingBounds.Left && pill.LabelBounds.Width == pill.RingBounds.Width,
                        "label is not centered below ring");
                    Assert(layout.HitTest(new Point(pill.Bounds.Left + 5, pill.Bounds.Top + 5)) == pill, "hit target misplaced");
                }
                QuotaHoverCardContent details = ComposerUsagePillTooltips.BuildSidebarCard(data, null, DateTimeOffset.Now);
                Assert(String.Join("|", details.DetailLines).Contains("16.3亿") && String.Join("|", details.DetailLines).Contains("非当前会话上下文"), "Token detail or scope lost");
            }
            using (Bitmap canvas = new Bitmap(40, 120))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Font font = new Font("Segoe UI", 8f))
            {
                ComposerUsagePillLayout loading = ComposerUsagePillLayout.CreateSidebar(new UsageData(), graphics, font, 40);
                Assert(loading.Pills.Count == 2 && !loading.Pills[0].Remaining.HasValue, "loading removed menu entry or invented 0%");
            }
        }

        internal static void SidebarSeparatesMenuAndHover()
        {
            Assert(ComposerUsagePillLayout.MenuKind(OverlayPlacement.Sidebar) == ComposerUsagePillKind.ShortQuota, "5h is not menu anchor");
            foreach (ComposerUsagePillKind kind in Enum.GetValues(typeof(ComposerUsagePillKind)))
            {
                Assert(ComposerUsagePillLayout.ShowsTooltip(OverlayPlacement.Sidebar, kind) == (kind == ComposerUsagePillKind.WeeklyQuota),
                    "sidebar hover is not restricted to weekly quota");
                Assert(!ComposerUsagePillLayout.ShowsTooltip(OverlayPlacement.Sidebar, kind, true), "hover card covers open sidebar menu");
            }
            foreach (OverlayPlacement placement in new[] { OverlayPlacement.TitleBar, OverlayPlacement.ContentTop, OverlayPlacement.ComposerBottom })
            {
                Assert(ComposerUsagePillLayout.MenuKind(placement) == ComposerUsagePillKind.ShortQuota, "normal menu is not on 5h");
                Assert(!ComposerUsagePillLayout.ShowsTooltip(placement, ComposerUsagePillKind.ShortQuota), "5h hover obscures the menu");
                Assert(ComposerUsagePillLayout.ShowsTooltip(placement, ComposerUsagePillKind.ResetCredits), "normal reset-credit detail removed");
            }
        }

        internal static void HostBarsCenterAndComposerStartsLeft()
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            foreach (int origin in new[] { -2400, 0, 1800 })
            {
                Func<int, int> p = x => (int)Math.Round(x * scale);
                Rectangle window = new Rectangle(origin, 60, p(1205), p(986));
                Rectangle area = new Rectangle(origin - 100, 0, p(1400), p(1200));
                Size size = new Size(p(374), p(28));
                Rectangle top = OverlayPlacementLayout.CalculateBounds(OverlayPlacement.TitleBar, window, area, size, scale, Rectangle.Empty);
                Assert(Math.Abs(top.Top + top.Height / 2 - window.Top - p(22)) <= 1, "title bar not vertically centered");
                Assert(Math.Abs(top.Left + top.Width / 2 - (window.Left + p(320) + window.Right - p(138)) / 2) <= 1, "title bar not centered in free menu area");
                Rectangle middle = OverlayPlacementLayout.CalculateBounds(OverlayPlacement.ContentTop, window, area, size, scale, Rectangle.Empty);
                Assert(Math.Abs(middle.Top + middle.Height / 2 - window.Top - p(70)) <= 1, "header not vertically centered");
                Assert(Math.Abs(middle.Left + middle.Width / 2 - (window.Left + p(290) + window.Right) / 2) <= 1, "header not horizontally centered");
                Rectangle bottom = OverlayPlacementLayout.CalculateBounds(OverlayPlacement.ComposerBottom, window, area, size, scale, Rectangle.Empty);
                int composerLeft = window.Left + p(290) + (window.Width - p(290) - p(736)) / 2;
                Assert(bottom.Left == composerLeft + p(130), "composer is not left aligned after approval");
                Assert(Math.Abs(bottom.Top + bottom.Height / 2 - window.Bottom + p(42)) <= 1, "composer row not centered");
                Assert(bottom.Right <= composerLeft + p(736) - p(184), "composer covers model / send controls");
            }
            Rectangle realHost = new Rectangle(-1700, 380, 400, 32);
            Rectangle usable = new Rectangle(-2400, 0, 1920, 1080);
            Rectangle bounds = OverlayPlacementLayout.InHost(OverlayPlacement.ComposerBottom, realHost, usable, new Size(374, 28));
            Assert(bounds == new Rectangle(-1700, 382, 374, 28), "explicit toolbar host ignored");
            Assert(OverlayPlacementLayout.InHost(OverlayPlacement.ComposerBottom, new Rectangle(0, 0, 80, 28),
                new Rectangle(0, 0, 1200, 800), new Size(374, 28)).IsEmpty, "narrow toolbar covers controls");
        }

        internal static void BoundaryDetectionDoesNotGuessCollapsedSidebar()
        {
            Rectangle window = new Rectangle(-1000, 40, 1000, 800);
            int width = SidebarWindowProbe.DetectBoundary(window, 1f,
                (x, y) => x < window.Left + 52 ? Color.FromArgb(232, 240, 247) : Color.White);
            Assert(width >= 48 && width <= 56, "visible rail boundary not detected");
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                Rectangle scaled = new Rectangle(-1000, 40, (int)(1000 * scale), (int)(800 * scale));
                int darkWidth = SidebarWindowProbe.DetectBoundary(scaled, scale,
                    (x, y) => x < scaled.Left + 52 * scale ? Color.FromArgb(22, 26, 33) : Color.FromArgb(18, 21, 27));
                Assert(darkWidth >= 48 && darkWidth <= 56, "dark / scaled rail not detected");
            }
            Assert(SidebarWindowProbe.DetectBoundary(window, 1f,
                (x, y) => x < window.Left + 290 ? Color.LightGray : Color.White) == 0, "chat sidebar detected as navigation rail");
            Assert(SidebarWindowProbe.DetectBoundary(window, 1f, (x, y) => Color.White) == 0,
                "collapsed sidebar guessed");
            Assert(SidebarWindowProbe.DetectBoundary(window, 1f,
                (x, y) => y < 200 && x < window.Left + 52 ? Color.LightGray : Color.White) == 0,
                "single content block was mistaken for sidebar");
            SidebarBoundaryState state = new SidebarBoundaryState();
            state.Observe(52); state.Observe(0); state.Observe(0);
            Assert(state.Width == 52, "transient occlusion hid the overlay");
            state.Observe(52); state.Observe(0); state.Observe(0);
            Assert(state.Width == 52, "valid read did not recover hysteresis");
            state.Observe(0); Assert(state.Width == 0, "missing rail never confirmed");
            state.Observe(52); Assert(state.Width == 52, "rail did not recover after missing");
            state.Reset(); Assert(state.Width == 0, "cache leaked into another window");
            foreach (int height in new[] { 700, 800, 986 })
            using (Bitmap bitmap = new Bitmap(1000, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = UiRendering.CreateTextFont("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Brush rail = new SolidBrush(Color.FromArgb(232, 240, 247)))
            {
                graphics.Clear(Color.White); graphics.FillRectangle(rail, 0, 0, 52, height);
                Rectangle bounds = OverlayPlacementLayout.CalculateBounds(OverlayPlacement.Sidebar,
                    new Rectangle(0, 0, 1000, height), new Rectangle(0, 0, 1000, height), new Size(40, 184), 1f,
                    new Rectangle(0, 0, 52, height));
                graphics.TranslateTransform(bounds.X, bounds.Y);
                ComposerUsagePillLayout.Draw(graphics, ComposerUsagePillLayout.CreateSidebar(new UsageData {
                    Plan = "Plus", ShortRemaining = 56, WeeklyRemaining = 71, AvailableResetCredits = 2
                }, graphics, font, 40), font, false, false);
                int visible = SidebarWindowProbe.DetectBoundary(new Rectangle(0, 0, 1000, height), 1f,
                    (x, y) => bitmap.GetPixel(x, y));
                Assert(visible == 52, "our rings erased the rail boundary at height " + height);
            }
        }

        internal static void DarkPaletteIsReadableAndRestoresLight()
        {
            bool original = PopupUiContext.Dark;
            try
            {
                PopupUiContext.Dark = true;
                UpdateMenuPalette dark = UpdateMenuVisuals.CreateRainbowPalette();
                Assert(UpdateMenuVisuals.ContrastRatio(dark.Text, dark.Surface) >= 4.5, "dark text contrast too low");
                Assert(UpdateMenuVisuals.ContrastRatio(dark.MutedText, dark.Surface) >= 4.5, "dark secondary contrast too low");
                PopupUiContext.Dark = false;
                Assert(UpdateMenuVisuals.CreateRainbowPalette().Surface.R > 230, "light theme did not restore");
            }
            finally { PopupUiContext.Dark = original; }
        }

        internal static void PopupFocusAndCollapsePolicy()
        {
            Assert(PopupUiContext.ShouldDisplay(true, false, true, false, true, true), "foreground sidebar hidden");
            Assert(PopupUiContext.ShouldDisplay(true, false, false, true, true, true), "own dialog lost focus");
            Assert(!PopupUiContext.ShouldDisplay(true, false, false, false, true, true), "popup covers another app");
            Assert(!PopupUiContext.ShouldDisplay(true, true, false, true, true, true), "popup remained over minimized app");
            Assert(!PopupUiContext.ShouldDisplay(true, false, true, false, true, false), "popup stayed after sidebar collapse");
            Assert(!PopupUiContext.ShouldDisplay(false, false, false, true, false, true), "popup stayed after app disappeared");
        }

        private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
