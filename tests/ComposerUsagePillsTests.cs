using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class ComposerUsagePillsTests
    {
        public static void ProUnlimitedIsConsistentAcrossLayouts()
        {
            UsageData usage = new UsageData { Plan = "prolite", HasPlan = true,
                ShortUnlimited = true, HasShortLimitState = true,
                WeeklyRemaining = 73, HasWeeklyRemaining = true };
            Assert(String.Join("|", ComposerUsagePillLayout.BuildTexts(usage)) == "PRO|5h ∞|周 73%",
                "Pro internal plan or obsolete 5h quota leaked into capsule");
            QuotaHoverCardContent card = ComposerUsagePillTooltips.BuildSidebarCard(usage, null, DateTimeOffset.Now);
            Assert(card.Title == "PRO · 额度与用量" && card.ShortLine.Contains("无 5 小时限额") &&
                !card.ShortLine.Contains("重置"), "unlimited hover still waits for a reset");
            using (Bitmap canvas = new Bitmap(450, 120))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Font font = new Font("Microsoft Sans Serif", 8f))
            {
                ComposerUsagePillLayout sidebar = ComposerUsagePillLayout.CreateSidebar(usage, graphics, font, 40);
                Assert(sidebar.Pills[0].Unlimited && !sidebar.Pills[0].Remaining.HasValue,
                    "sidebar turned unlimited into a fake percentage");
                ComposerUsagePillLayout.Draw(graphics, sidebar, font, false, false);
                OverlaySettings minimal = new OverlaySettings { ShowPlanPill = false };
                Assert(ComposerUsagePillLayout.BuildTexts(usage, minimal)[0] == "5h ∞",
                    "hiding plan removed the unlimited menu entry");
            }
            usage.HasShortLimitState = false;
            Assert(String.Join("|", ComposerUsagePillLayout.BuildTexts(usage)) == "PRO|5h —|周 73%",
                "unconfirmed short limit state was displayed as unlimited");
        }

        public static void OptionalPillsToggleIndependentlyWithoutDataLoss()
        {
            UsageData usage = new UsageData { Plan = "Plus", ShortRemaining = 79, WeeklyRemaining = 68,
                ProfileTokensText = "11.6亿", LifetimeTokens = 1160000000L, AvailableResetCredits = 2,
                HasResetCreditDetails = true, ResetCreditExpiries = new long?[] { 1787500000L, 1788500000L } };
            using (Bitmap canvas = new Bitmap(600, 120))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Font font = new Font("Microsoft Sans Serif", 8f))
            {
                int coreWidth = 0;
                foreach (bool plan in new[] { false, true })
                foreach (bool tokens in new[] { false, true })
                foreach (bool credits in new[] { false, true })
                {
                    OverlaySettings settings = new OverlaySettings { ShowPlanPill = plan, ShowTokenPill = tokens, ShowResetCreditsPill = credits };
                    int width = ComposerUsagePillLayout.MeasureWidth(usage, graphics, font, settings);
                    ComposerUsagePillLayout layout = ComposerUsagePillLayout.Create(usage, graphics, font, width, settings);
                    Assert(layout.Pills.Count == 2 + (plan ? 1 : 0) + (tokens ? 1 : 0) + (credits ? 1 : 0), "hidden pills leave placeholder / displace quotas");
                    Assert(layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.Plan) == plan, "plan switch is not independent");
                    Assert(layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.Tokens) == tokens, "Token switch is not independent");
                    Assert(layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.ResetCredits) == credits, "credit switch is not independent");
                    Assert(String.Join("|", ComposerUsagePillLayout.BuildTexts(usage, settings)).Contains("Token ") == tokens,
                        "text and measured layout disagree");
                    foreach (ComposerUsagePill pill in layout.Pills)
                        Assert(new Rectangle(0, 0, width, 28).Contains(pill.Bounds), "visible pill is clipped");
                    if (!tokens && !credits) coreWidth = width;
                    else Assert(width > ComposerUsagePillLayout.MeasureWidth(usage, graphics, font,
                        new OverlaySettings { ShowPlanPill = plan, ShowTokenPill = false, ShowResetCreditsPill = false }), "hidden pills kept unused width");
                    ComposerUsagePillLayout sidebar = ComposerUsagePillLayout.CreateSidebar(usage, graphics, font, 40);
                    Assert(sidebar.Pills.Count == 2 && sidebar.Pills[0].Kind == ComposerUsagePillKind.ShortQuota &&
                        sidebar.Pills[1].Kind == ComposerUsagePillKind.WeeklyQuota, "preferences added optional pills to sidebar");
                }
                Assert(coreWidth > 0 && usage.LifetimeTokens == 1160000000L && usage.ProfileTokensText == "11.6亿" &&
                    usage.AvailableResetCredits == 2 && usage.ResetCreditExpiries.Length == 2, "display switch mutated source data");
                Assert(ComposerUsagePillTooltips.BuildCreditsCard(usage).DetailLines.Length > 1, "hiding removed credit expiry detail");
                Assert(String.Join("|", ComposerUsagePillTooltips.BuildSidebarCard(usage, null, DateTimeOffset.Now).DetailLines).Contains("11.6亿"),
                    "hiding the Token pill removed account summary details");
            }
        }

        public static void NativeQuotaFieldsAreExposed()
        {
            UsageData usage = new UsageData
            {
                Plan = "plus",
                HasPlan = true,
                ShortRemaining = 79,
                HasShortRemaining = true,
                WeeklyRemaining = 68,
                HasWeeklyRemaining = true,
                ProfileTokensText = "11.6亿",
                AvailableResetCredits = 1,
                HasAvailableResetCredits = true
            };

            string joined = String.Join("|", ComposerUsagePillLayout.BuildTexts(usage));
            Assert(joined == "PLUS|5h 79%|周 68%|Token 11.6亿|券 1", joined);
        }

        public static void UnavailableQuotaFieldsAreOmitted()
        {
            UsageData usage = new UsageData
            {
                Plan = "Pro",
                HasPlan = true,
                ProfileTokensText = "待刷新"
            };

            string joined = String.Join("|", ComposerUsagePillLayout.BuildTexts(usage));
            Assert(joined == "PRO|5h —", joined);
        }

        public static void PercentagesAreClamped()
        {
            UsageData usage = new UsageData
            {
                ShortRemaining = 117,
                HasShortRemaining = true,
                WeeklyRemaining = -8,
                HasWeeklyRemaining = true
            };

            string joined = String.Join("|", ComposerUsagePillLayout.BuildTexts(usage));
            Assert(joined.Contains("5h 100%"), joined);
            Assert(joined.Contains("周 0%"), joined);
        }

        public static void QuotaHoverShowsResetTimes()
        {
            UsageData usage = new UsageData
            {
                ShortResetText = "18:30",
                HasShortResetText = true,
                WeeklyResetText = "9月3日 08:00",
                HasWeeklyResetText = true
            };

            QuotaHoverCardContent content = ComposerUsagePillTooltips.BuildCard(
                usage, null, DateTimeOffset.Now);
            Assert(content.ShortLine == "5h  18:30 重置", content.ShortLine);
            Assert(content.WeeklyLine == "周  9月3日 08:00 重置", content.WeeklyLine);
            Assert(!ComposerUsagePillTooltips.IsInteractive(ComposerUsagePillKind.Plan),
                "plan pill still opened a popup");
            Assert(!ComposerUsagePillTooltips.IsInteractive(ComposerUsagePillKind.ShortQuota),
                "5h pill still opened a popup");
            Assert(ComposerUsagePillTooltips.IsInteractive(ComposerUsagePillKind.WeeklyQuota),
                "weekly pill did not open the combined popup");
        }

        public static void PlanHoverPrefersCreditsThenTibo()
        {
            UsageData usage = new UsageData
            {
                AvailableResetCredits = 2,
                HasAvailableResetCredits = true,
                ResetCreditExpiresAt = 1787500000L,
                HasResetCreditExpiry = true
            };
            DateTimeOffset now = new DateTimeOffset(new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Local));
            ResetRadarData radar = new ResetRadarData
            {
                Status = ResetRadarStatus.ScheduledToday,
                StatusLabel = "今日有预告",
                EffectiveAt = now.AddHours(2),
                EffectiveUntil = now.AddHours(3),
                NetworkAvailable = true,
                SourceUrl = "https://x.com/thsottiaux/status/123",
                Confidence = 0.8
            };

            QuotaHoverCardContent credits = ComposerUsagePillTooltips.BuildCard(
                usage, radar, now);
            Assert(credits.ExtraLine.Contains("重置券  可用 2 张"), credits.ExtraLine);
            Assert(credits.ExtraLine.Contains("· 最早到期 "), credits.ExtraLine);
            Assert(credits.TiboLine.Contains("Tibo  14:00后重置"), "credits hid Tibo");

            usage.AvailableResetCredits = 0;
            QuotaHoverCardContent tibo = ComposerUsagePillTooltips.BuildCard(
                usage, radar, now);
            Assert(tibo.ExtraLine == "重置券  0 张", tibo.ExtraLine);
            Assert(tibo.TiboLine.Contains("Tibo  14:00后重置"), tibo.TiboLine);
            Assert(tibo.TiboLine.Contains("非官方"), tibo.TiboLine);
            radar.IsFromCache = true;
            Assert(!ComposerUsagePillTooltips.BuildCard(usage, radar, now).TiboLine.Contains("后重置"), "cached prediction shown live");
            radar.IsFromCache = false;
            Assert(!ComposerUsagePillTooltips.BuildCard(usage, radar, now.AddHours(4)).TiboLine.Contains("后重置"), "expired prediction shown live");
        }

        public static void PlanClickTargetHasForgivingEdges()
        {
            UsageData usage = new UsageData { Plan = "Plus", HasPlan = true };
            using (Bitmap bitmap = new Bitmap(320, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font("Microsoft Sans Serif", 8f))
            {
                ComposerUsagePillLayout layout = ComposerUsagePillLayout.Create(
                    usage, graphics, font, bitmap.Width);
                ComposerUsagePill plan = layout.Pills.Find(p => p.Kind == ComposerUsagePillKind.ShortQuota);
                Point forgivingEdge = new Point(
                    plan.Bounds.Left - 2,
                    plan.Bounds.Top + plan.Bounds.Height / 2);
                Assert(layout.HitTest(forgivingEdge) == null,
                    "exact hit test unexpectedly included the outer edge");
                Assert(layout.HitTest(
                    ComposerUsagePillKind.ShortQuota, forgivingEdge, 3, 2) == plan,
                    "expanded 5h hit target missed the outer edge");
            }
        }

        public static void CreditsHoverListsEachExpiry()
        {
            UsageData usage = new UsageData { AvailableResetCredits = 3,
                HasResetCreditDetails = true, Source = "Codex CLI app-server",
                ResetCreditExpiries = new long?[] { 1787500000L, 1788500000L, null } };
            QuotaHoverCardContent card = ComposerUsagePillTooltips.BuildCreditsCard(usage);
            Assert(card.DetailLines.Length == 4 && card.DetailLines[0].StartsWith("券 1  ") &&
                card.DetailLines[1].StartsWith("券 2  "), "separate credit expiry rows missing");
            Assert(card.DetailLines[0] != card.DetailLines[1] &&
                card.DetailLines[2] == "券 3  到期时间待刷新", "unknown expiry was invented");
            Assert(card.DetailLines[0].Contains("2026/") &&
                card.DetailLines[3].Contains("本机时区"), "year or timezone missing");
            Assert(ComposerUsagePillTooltips.IsInteractive(ComposerUsagePillKind.ResetCredits),
                "pink pill cannot receive hover");
            string before = card.RevisionKey;
            usage.ResetCreditExpiries[1] = 1789500000L;
            Assert(before != ComposerUsagePillTooltips.BuildCreditsCard(usage).RevisionKey,
                "individual expiry change did not invalidate hover card");
        }

        public static void CreditsHoverHandlesMissingAndEmptyInventory()
        {
            UsageData usage = new UsageData { AvailableResetCredits = 2,
                ResetCreditExpiresAt = 1787500000L, HasResetCreditExpiry = true };
            QuotaHoverCardContent card = ComposerUsagePillTooltips.BuildCreditsCard(usage);
            Assert(card.DetailLines[0] == "共 2 张 · 到期时间待刷新",
                "legacy earliest date was duplicated into individual dates");
            usage.HasResetCreditDetails = true;
            usage.ResetCreditExpiries = new long?[] { 1787500000L };
            Assert(ComposerUsagePillTooltips.BuildCreditsCard(usage).DetailLines[1] ==
                "另 1 张 · 到期时间待刷新", "partial inventory pretended complete");
            usage.AvailableResetCredits = 0;
            Assert(ComposerUsagePillTooltips.BuildCreditsCard(usage).DetailLines[0] ==
                "当前没有可用重置券", "zero inventory showed stale credit");
            Assert(String.Join("|", ComposerUsagePillLayout.BuildTexts(usage)).Contains("券 0"),
                "known zero credit pill is missing");
        }

        public static void NarrowLayoutPreservesCreditsBeforeTokens()
        {
            UsageData usage = new UsageData { Plan = "Plus", ShortRemaining = 79,
                WeeklyRemaining = 68, AvailableResetCredits = 2, ProfileTokensText = "11.6亿" };
            using (Bitmap bitmap = new Bitmap(500, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = new Font("Microsoft Sans Serif", 8f))
            {
                int fullWidth = ComposerUsagePillLayout.MeasureWidth(usage, graphics, font);
                ComposerUsagePillLayout layout = ComposerUsagePillLayout.Create(
                    usage, graphics, font, fullWidth - 1);
                Assert(layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.ResetCredits) &&
                    !layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.Tokens),
                    "tokens displaced restored credit pill");
                ComposerUsagePill credit = layout.Pills.Find(p => p.Kind == ComposerUsagePillKind.ResetCredits);
                Assert(credit.DotColor == Color.FromArgb(236, 72, 153) &&
                    layout.HitTest(new Point(credit.Bounds.Left + 4, credit.Bounds.Top + 4)) == credit,
                    "pink credit or hit target missing");
                UsageData core = usage.Clone();
                core.AvailableResetCredits = null;
                core.ProfileTokensText = String.Empty;
                int coreWidth = ComposerUsagePillLayout.MeasureWidth(core, graphics, font);
                layout = ComposerUsagePillLayout.Create(usage, graphics, font, coreWidth);
                Assert(layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.Plan) &&
                    layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.ShortQuota) &&
                    layout.Pills.Exists(p => p.Kind == ComposerUsagePillKind.WeeklyQuota),
                    "credits displaced core quotas in narrow layout");
            }
        }

        public static void MenuSurvivesLoadingHiddenPlanAndNarrowWidths()
        {
            OverlaySettings minimal = new OverlaySettings { ShowPlanPill = false, ShowTokenPill = false, ShowResetCreditsPill = false };
            using (Bitmap canvas = new Bitmap(500, 32))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Font font = new Font("Microsoft Sans Serif", 8f))
            {
                foreach (UsageData usage in new[] { null, new UsageData { HasShortRemaining = true },
                    new UsageData { Plan = "5h long custom plan", ShortRemaining = 0, WeeklyRemaining = 64,
                        ProfileTokensText = "20.4亿", AvailableResetCredits = 3 } })
                foreach (OverlaySettings settings in new[] { minimal, new OverlaySettings() })
                foreach (int width in new[] { 80, 110, 160, 500 })
                {
                    ComposerUsagePillLayout layout = ComposerUsagePillLayout.Create(usage, graphics, font, width, settings);
                    ComposerUsagePill menu = layout.Pills.Find(p => p.Kind == ComposerUsagePillKind.ShortQuota);
                    Assert(menu != null && new Rectangle(0, 0, width, 28).Contains(menu.Bounds), "5h menu was trimmed or clipped");
                    Assert(layout.HitTest(new Point(menu.Bounds.Left + menu.Bounds.Width / 2, 14)) == menu, "loading menu is not clickable");
                    Assert(menu.Text == (usage != null && usage.ShortRemaining.HasValue ? "5h 0%" : "5h —"), "unknown quota masquerades as zero");
                }
                Assert(String.Join("|", ComposerUsagePillLayout.BuildTexts(new UsageData { ShortRemaining = 71, WeeklyRemaining = 61 }, minimal)) ==
                    "5h 71%|周 61%", "minimal mode did not reduce to two quota pills");
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
