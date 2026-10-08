using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class ComposerUsagePillsTests
    {
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
            Assert(joined == "PRO", joined);
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
                ComposerUsagePill plan = layout.Pills[0];
                Point forgivingEdge = new Point(
                    plan.Bounds.Left - 2,
                    plan.Bounds.Top + plan.Bounds.Height / 2);
                Assert(layout.HitTest(forgivingEdge) == null,
                    "exact hit test unexpectedly included the outer edge");
                Assert(layout.HitTest(
                    ComposerUsagePillKind.Plan, forgivingEdge, 3, 2) == plan,
                    "expanded PLUS hit target missed the outer edge");
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

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
