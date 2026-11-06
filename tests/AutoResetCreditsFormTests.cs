using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class AutoResetCreditsFormTests
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        internal static void UnknownCreditDetailsNeverInventAnExpiry()
        {
            DateTime now = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
            AutoResetCreditsSnapshot snapshot = new AutoResetCreditsSnapshot { Enabled = true, Available = true,
                AvailableCount = 3, LastCheckedUtc = now, Credits = new[] {
                    new ResetCreditRecord { Id = "private-id-a", Status = "available", ResetType = "codexRateLimits", ExpiresAt = null },
                    new ResetCreditRecord { Id = "private-id-b", Status = "used", ResetType = "codexRateLimits", ExpiresAt = 1793300000L }
                } };
            AutoResetCreditRow[] rows = AutoResetCreditsForm.BuildRows(snapshot, now);
            Assert(rows.Length == 2 && rows[0].ExpiryLine.Contains("待刷新") &&
                rows[0].ScheduleLine.Contains("待刷新") && rows[1].Title.Contains("另 2 张"),
                "unknown expiry or incomplete inventory was fabricated or lost");
            Assert(!String.Join("|", new[] { rows[0].Title, rows[0].ExpiryLine, rows[0].ScheduleLine }).Contains("private-id"),
                "opaque credit ID leaked into the presentation");
            snapshot.Credits = new ResetCreditRecord[0];
            snapshot.AvailableCount = 0;
            snapshot.Available = false;
            snapshot.LastCheckedUtc = DateTime.MinValue;
            Assert(AutoResetCreditsForm.BuildRows(snapshot, now)[0].Title.Contains("待刷新"),
                "unknown inventory appeared as confirmed empty");
        }

        internal static void AutoUseTimeAndPauseStayScopedToEachCard()
        {
            DateTime now = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
            AutoResetCreditsSnapshot snapshot = new AutoResetCreditsSnapshot { Available = true,
                AvailableCount = 3, LastCheckedUtc = now, Credits = new[] {
                    Card(now.AddMinutes(30)), Card(now.AddMinutes(10)), Card(now.AddSeconds(-1))
                } };
            AutoResetCreditRow[] rows = AutoResetCreditsForm.BuildRows(snapshot, now);
            Assert(rows[0].ScheduleLine.Contains(now.AddMinutes(20).ToLocalTime().ToString("M/d HH:mm:ss")) &&
                rows[0].ScheduleLine.Contains("已暂停"), "a card did not retain its own ten-minute schedule and paused state");
            Assert(rows[1].ScheduleLine.Contains("到期前 10 分钟") && rows[1].ScheduleLine.Contains("暂停"),
                "the ten-minute boundary suggested sending while paused");
            Assert(rows[2].Title.Contains("已到期") && !rows[2].ScheduleLine.Contains("自动使用时间"),
                "expired card was still scheduled");
            snapshot.Enabled = true;
            snapshot.Credits[0].ResetType = "unknown";
            rows = AutoResetCreditsForm.BuildRows(snapshot, now);
            Assert(rows[0].ScheduleLine.Contains("类型待确认") && rows[1].ScheduleLine.Contains("等待检查使用"),
                "unsupported card type was scheduled or enabled state was not reflected");
        }

        internal static void PreviewDoesNotStartAServiceOrShowAWindow()
        {
            string folder = Path.Combine(Path.GetTempPath(), "reset-credit-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string output = Path.Combine(folder, "preview.png");
            int shown = 0;
            try {
                using (AutoResetCreditsForm form = new AutoResetCreditsForm(null, new Rectangle(0, 0, 60, 28))) {
                    form.Shown += delegate { shown++; };
                    form.ExportPreview(output);
                    Timer timer = (Timer)typeof(AutoResetCreditsForm).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Assert(File.Exists(output) && new FileInfo(output).Length > 0, "preview image missing");
                    Assert(shown == 0 && !form.Visible &&
                        (!form.IsHandleCreated || !IsWindowVisible(form.Handle)) && !timer.Enabled,
                        "offline preview showed a window or started a timer");
                    Assert(typeof(AutoResetCreditsForm).GetField("service", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) == null,
                        "preview created a production service");
                }
            }
            finally {
                if (File.Exists(output)) File.Delete(output);
                Directory.Delete(folder);
            }
        }

        private static ResetCreditRecord Card(DateTime expiry)
        {
            return new ResetCreditRecord { Id = Guid.NewGuid().ToString("N"), Status = "available",
                ResetType = "codexRateLimits", ExpiresAt = new DateTimeOffset(expiry).ToUnixTimeSeconds() };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
