using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class OverlayInteractionTests
    {
        public static void MainUsageIsNotInteractive()
        {
            Rectangle usage = OverlayInteraction.GetMainUsageBounds(390, 28);
            Rectangle radar = new Rectangle(396, 5, 104, 18);
            Rectangle gear = new Rectangle(556, 2, 30, 24);
            Point usageCenter = new Point(usage.Left + usage.Width / 2,
                usage.Top + usage.Height / 2);
            Assert(!OverlayInteraction.IsHeaderInteractive(usageCenter, radar, gear),
                "main usage still owns mouse input");
            Assert(OverlayInteraction.IsHeaderInteractive(
                new Point(radar.Left + 2, radar.Top + 2), radar, gear),
                "radar stopped receiving input");
            Assert(OverlayInteraction.IsHeaderInteractive(
                new Point(gear.Left + 2, gear.Top + 2), radar, gear),
                "gear stopped receiving input");
        }

        public static void RadarStatusClickOpensRunway()
        {
            Rectangle bounds = new Rectangle(100, 2, 104, 24);
            Point center = new Point(bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2);
            ResetRadarStatus[] statuses =
            {
                ResetRadarStatus.Loading,
                ResetRadarStatus.Offline,
                ResetRadarStatus.NoSignal,
                ResetRadarStatus.ScheduledToday,
                ResetRadarStatus.ScheduledUpcoming,
                ResetRadarStatus.CompletedToday
            };
            foreach (ResetRadarStatus status in statuses)
            {
                Assert(OverlayInteraction.DecideResetRadarClick(
                    MouseButtons.Left, center, bounds) ==
                    OverlayMouseAction.OpenRunwayPage,
                    status + " click did not open Runway");
            }
            Assert(OverlayInteraction.DecideResetRadarClick(
                MouseButtons.Right, center, bounds) ==
                OverlayMouseAction.None, "right click unexpectedly opened Runway");
            Assert(OverlayInteraction.DecideResetRadarClick(
                MouseButtons.Left, new Point(bounds.Right, center.Y), bounds) ==
                OverlayMouseAction.None,
                "outside click unexpectedly opened Runway");
        }

        public static void UpdateMenuReflectsReleaseState()
        {
            UpdateMenuState initial = OverlayInteraction.BuildUpdateMenuState(
                new GitHubReleaseUpdateSnapshot());
            Assert(initial.CurrentVersionText == "当前版本 v" +
                GitHubReleaseUpdateService.CurrentVersion, initial.CurrentVersionText);
            Assert(initial.CanCheck, "initial check action was disabled");
            Assert(!initial.CanDownload && initial.DownloadUrl == String.Empty,
                "initial download action was enabled");
            Assert(GitHubReleaseUpdateService.UpdatesEnabled,
                "configured repository updater was disabled");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
