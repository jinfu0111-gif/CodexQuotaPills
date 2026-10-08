using System;
using System.IO;
using System.Globalization;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class ResetRadarBannerTests
    {
        internal static void PillLayoutAlignsTextAndCentersClose()
        {
            foreach (int width in new[] { 160, 280, 450 })
            {
                Rectangle title = ResetRadarBannerVisuals.TitleBounds(width, 48);
                Rectangle detail = ResetRadarBannerVisuals.DetailBounds(width, 48);
                Rectangle close = ResetRadarBannerVisuals.CloseButtonBounds(width, 48);
                Rectangle hit = ResetRadarBannerVisuals.CloseHitBounds(width, 48);
                Assert(title.Left == detail.Left, "banner lines do not share their left edge");
                Assert(title.Right < hit.Left && detail.Right < hit.Left, "text overlaps close target");
                Assert(close.Width == close.Height, "close button is not circular");
                Assert(close.Top + close.Height / 2 == 24, "close button is not centered on banner midline");
                Assert(hit.Contains(close), "close circle is outside clickable area");
                Assert(new Rectangle(0, 0, width, 48).Contains(hit), "close target leaves banner");
                Assert(!hit.Contains(new Point(width - 10, 4)), "old top-corner target still closes banner");
            }
            Assert(UpdateMenuVisuals.ContrastRatio(QuotaPillVisuals.Text, QuotaPillVisuals.Surface) >= 4.5d,
                "shared pill text has insufficient contrast");
        }

        internal static void NewInformationExpiresAndCannotReplay()
        {
            string root = Path.Combine(Path.GetTempPath(), "pills-banner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "state.json");
            DateTimeOffset now = DateTimeOffset.Now;
            ResetRadarData data = new ResetRadarData { Status = ResetRadarStatus.ScheduledUpcoming,
                NetworkAvailable = true, EventKind = "scheduled", SourceUrl = "https://x.com/thsottiaux/status/123",
                EvidencePostId = "123", AnnouncedAt = now, EffectiveAt = now.AddHours(1), EffectiveUntil = now.AddHours(2) };
            ResetRadarBannerPolicy policy = new ResetRadarBannerPolicy(path);
            policy.Observe(data, now);
            Assert(!policy.ShouldDisplay(now), "startup replayed an old announcement");
            data.SourceUrl = "https://x.com/thsottiaux/status/124"; data.EvidencePostId = "124";
            policy.Observe(data, now);
            Assert(policy.ShouldDisplay(now), "new information did not show");
            data.FetchedAt = now.AddSeconds(5); data.Confidence = .99; // Polling and countdown text are not new announcements.
            policy.Observe(data, now.AddSeconds(5));
            Assert(policy.ShouldDisplay(now.AddSeconds(9.999)), "banner closed before ten seconds");
            Assert(!policy.ShouldDisplay(now.AddSeconds(10)), "banner persisted past ten seconds");
            policy.Observe(data, now.AddSeconds(11));
            Assert(!policy.ShouldDisplay(now.AddSeconds(11)), "refresh replayed announcement");
            policy = new ResetRadarBannerPolicy(path);
            policy.Observe(data, now.AddSeconds(12));
            Assert(!policy.ShouldDisplay(now.AddSeconds(12)), "restart replayed announcement");
            data.EventKind = "completed"; data.Status = ResetRadarStatus.CompletedToday; data.EffectiveAt = now;
            policy.Observe(data, now);
            Assert(policy.ShouldDisplay(now), "completion of scheduled reset was not new information");
            policy.Dismiss(); policy.Observe(data, now);
            Assert(!policy.ShouldDisplay(now), "manual close replayed announcement");
            data.EvidencePostId = "125"; data.IsFromCache = true;
            policy.Observe(data, now); Assert(!policy.ShouldDisplay(now), "cache showed banner");
            data.IsFromCache = false; data.NetworkAvailable = false;
            policy.Observe(data, now); Assert(!policy.ShouldDisplay(now), "offline showed banner");
            data.NetworkAvailable = true; data.Status = ResetRadarStatus.ScheduledUpcoming; data.EffectiveUntil = now.AddSeconds(-1);
            policy.Observe(data, now); Assert(!policy.ShouldDisplay(now), "expired schedule showed banner");
            File.Delete(path); Directory.Delete(root);
        }
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
