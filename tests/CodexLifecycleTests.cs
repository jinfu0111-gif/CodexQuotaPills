using System;

namespace CodexUsageOverlay
{
    internal static class CodexLifecycleTests
    {
        public static void MissingOverlayRecoversWhileCodexRuns()
        {
            Assert(!CodexLifecyclePolicy.ShouldStartOverlay(false, false),
                "overlay starts without Codex");
            Assert(CodexLifecyclePolicy.ShouldStartOverlay(true, false),
                "overlay does not start when Codex appears");
            // At the next poll a rapid update still reads running=true, while
            // the old overlay may only now finish closing. It must be restored.
            Assert(CodexLifecyclePolicy.ShouldStartOverlay(true, false),
                "missing overlay does not recover after an unobserved restart");
            Assert(!CodexLifecyclePolicy.ShouldStartOverlay(true, true),
                "duplicate overlay starts");
            Assert(!CodexLifecyclePolicy.ShouldStartOverlay(false, true), "starts after Codex closes");
        }

        public static void FailedStartsAreThrottled()
        {
            DateTime now = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
            Assert(!CodexLifecyclePolicy.RetryAllowed(now.AddMilliseconds(4999), now.AddSeconds(5)), "start failures spin");
            Assert(CodexLifecyclePolicy.RetryAllowed(now.AddSeconds(5), now.AddSeconds(5)), "retry never recovers");
            Assert(CodexLifecycleLauncher.ReadyEventName("bad-token") == null, "untrusted event accepted");
            Assert(CodexLifecycleLauncher.ReadyEventName(Guid.NewGuid().ToString("N")) != null, "launch acknowledgement unavailable");
        }

        public static void MissingCodexUsesGracePeriod()
        {
            DateTime now = new DateTime(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);
            DateTime? missingSince = CodexLifecyclePolicy.UpdateMissingSince(
                false, now, null);
            Assert(missingSince == now, "missing timestamp is not recorded");
            Assert(!CodexLifecyclePolicy.MissingGracePeriodElapsed(
                missingSince, now.AddMilliseconds(1999), TimeSpan.FromSeconds(2)),
                "overlay exits before the grace period");
            Assert(CodexLifecyclePolicy.MissingGracePeriodElapsed(
                missingSince, now.AddSeconds(2), TimeSpan.FromSeconds(2)),
                "overlay remains after the grace period");
            Assert(CodexLifecyclePolicy.UpdateMissingSince(
                true, now.AddSeconds(1), missingSince) == null,
                "Codex recovery does not clear the missing timestamp");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
