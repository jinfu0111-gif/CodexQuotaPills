using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CodexUsageOverlay
{
    internal static class AutoResetCreditsTests
    {
        private const string AccountA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string AccountB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private static readonly DateTime Start = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        private static void Assert(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private sealed class Fixture : IDisposable
        {
            internal readonly string Folder = Path.Combine(Path.GetTempPath(), "quota-auto-reset-test-" + Guid.NewGuid().ToString("N"));
            internal string Pathname { get { return Path.Combine(Folder, "state.json"); } }
            internal DateTime Now = Start;
            internal ResetCreditInventory Inventory;
            internal Func<string, string, string, string> Reply;
            internal Action BeforeRead;
            internal bool StampReadTime = true;
            internal readonly List<string> Keys = new List<string>();
            internal readonly List<string> Ids = new List<string>();
            internal AutoResetCreditsService Service;
            internal Fixture()
            {
                Directory.CreateDirectory(Folder);
                Inventory = new ResetCreditInventory { AccountHash = AccountA, HasDetails = true,
                    AvailableCount = 1, ReadUtc = Now, Credits = new[] { Card("credit-1", 600) } };
            }
            internal ResetCreditRecord Card(string id, long delta)
            {
                return new ResetCreditRecord { Id = id, ResetType = "codexRateLimits", Status = "available",
                    ExpiresAt = AutoResetCreditsPolicy.UnixTime(Now) + delta };
            }
            internal ResetCreditInventory Read()
            {
                if (BeforeRead != null) BeforeRead();
                if (Inventory != null && StampReadTime) Inventory.ReadUtc = Now;
                return Inventory;
            }
            internal string Consume(string account, string id, string key)
            {
                AutoResetCreditsState stored = AutoResetCreditsState.Load(Pathname);
                AutoResetCreditAttempt intent;
                Assert(account == Inventory.AccountHash && stored.AccountHash == account,
                    "consume account was not bound to the durable state");
                Assert(stored.Attempts.TryGetValue(AutoResetCreditsPolicy.AttemptKey(account, id), out intent) &&
                    intent.IdempotencyKey == key && intent.Outcome == "pending",
                    "consume occurred before a durable idempotent intent");
                Keys.Add(key); Ids.Add(id);
                return Reply == null ? "reset" : Reply(account, id, key);
            }
            internal AutoResetCreditsService Open()
            {
                Service = new AutoResetCreditsService(Pathname, Read, Consume, delegate { return Now; });
                return Service;
            }
            internal void EnableAndScan()
            {
                Open(); Assert(Service.SetEnabled(true), "enable failed"); Service.ScanNowForTests();
            }
            public void Dispose()
            {
                if (Service != null) Service.Dispose();
                string resolved = Path.GetFullPath(Folder);
                if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                    !System.IO.Path.GetFileName(resolved).StartsWith("quota-auto-reset-test-", StringComparison.Ordinal))
                    throw new IOException("Invalid isolated fixture path");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }

        internal static void DefaultsPauseAndOfflineConfiguration()
        {
            using (Fixture f = new Fixture())
            {
                f.Open();
                Assert(!f.Service.Enabled, "new installation auto-spent a card");
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 0 && f.Service.Snapshot().AvailableCount == 1,
                    "disabled monitoring spent a card or hid its inventory");
                Assert(!AutoResetCreditsService.ConfigureEnabled(f.Pathname, true),
                    "offline configuration bypassed another service's lock");
                f.Service.Dispose();
                Assert(AutoResetCreditsService.ConfigureEnabled(f.Pathname, true), "offline enable failed");
                Assert(f.Keys.Count == 0 && AutoResetCreditsState.Load(f.Pathname).Enabled,
                    "offline configuration redeemed a card or did not save the switch");
                f.Open();
                Assert(f.Service.Enabled, "restart lost enabled preference");
                Assert(f.Service.SetEnabled(false), "pause failed");
                f.Service.ScanNowForTests();
                f.Service.Dispose(); f.Open(); f.Service.ScanNowForTests();
                Assert(!f.Service.Enabled && f.Keys.Count == 0, "pause was not durable across restart");
            }
            using (AutoResetCreditsService preview = new AutoResetCreditsService(true))
            {
                Assert(!preview.Enabled && !preview.SetEnabled(true), "read-only preview could enable redemption");
                preview.RequestScan(); preview.ScanNowForTests();
                Assert(preview.Snapshot().LastCheckedUtc == DateTime.MinValue,
                    "read-only preview contacted the live service");
            }
        }

        internal static void ExactExpiryBoundaryAndFreshness()
        {
            foreach (long delta in new long[] { 601, 600, 1, 0, -1 })
            using (Fixture f = new Fixture())
            {
                f.Inventory.Credits = new[] { f.Card("boundary", delta) };
                f.EnableAndScan();
                Assert(f.Keys.Count == (delta > 0 && delta <= 600 ? 1 : 0),
                    "expiry boundary was not strictly (0,600] seconds");
            }
            foreach (int age in new[] { 31, -6 })
            using (Fixture f = new Fixture())
            {
                f.StampReadTime = false; f.Inventory.ReadUtc = f.Now.AddSeconds(-age);
                f.EnableAndScan(); Assert(f.Keys.Count == 0, "stale or future inventory redeemed a card");
            }
        }

        internal static void MissingAndManuallyUsedCardsAreNeverRedeemed()
        {
            for (int index = 0; index < 8; index++)
            using (Fixture f = new Fixture())
            {
                ResetCreditRecord card = f.Inventory.Credits[0];
                if (index == 0) card.Id = null;
                else if (index == 1) card.ExpiresAt = null;
                else if (index == 2) card.ResetType = "other";
                else if (index == 3) card.Status = "redeemed";
                else if (index == 4) f.Inventory.HasDetails = false;
                else if (index == 5) { f.Inventory.AvailableCount = 0; f.Inventory.Credits = new ResetCreditRecord[0]; }
                else if (index == 6) f.Inventory.AccountHash = String.Empty;
                else card.ExpiresAt = Int64.MaxValue;
                f.EnableAndScan(); Assert(f.Keys.Count == 0, "unverified or manually used card was redeemed");
            }
            using (Fixture f = new Fixture())
            {
                f.Open(); Assert(f.Service.SetEnabled(true), "enable failed");
                f.Inventory.Credits = new ResetCreditRecord[0]; f.Inventory.AvailableCount = 0;
                f.Service.ScanNowForTests(); Assert(f.Keys.Count == 0, "manual use before the next poll was ignored");
            }
        }

        internal static void DuplicateIdsAndEqualExpiryRemainDistinct()
        {
            using (Fixture f = new Fixture())
            {
                f.Inventory.Credits = new[] { f.Card("a", 500), f.Card("a", 500), f.Card("b", 500) };
                f.Inventory.AvailableCount = 2;
                f.EnableAndScan();
                Assert(f.Keys.Count == 1 && f.Ids[0] == "a", "oldest deterministic card was not selected once");
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 1, "manual refresh bypassed the global throttle");
                f.Now = f.Now.AddSeconds(30); f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 2 && f.Ids[1] == "b" && f.Keys[0] != f.Keys[1],
                    "same-expiry cards were conflated or a successful card was repeated");
                f.Now = f.Now.AddSeconds(30); f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 2, "terminal card was redeemed twice while inventory lagged");
                AutoResetCreditsSnapshot copy = f.Service.Snapshot();
                copy.Credits[0].Id = "changed";
                Assert(f.Service.Snapshot().Credits[0].Id != "changed", "UI snapshot mutated monitored inventory");
            }
            using (Fixture f = new Fixture())
            {
                f.Inventory.Credits = new[] { f.Card("same", 500), f.Card("same", 400) };
                f.Inventory.AvailableCount = 1;
                f.EnableAndScan(); Assert(f.Keys.Count == 0, "conflicting duplicate ID was redeemed");
            }
        }

        internal static void UnknownOutcomeReusesDurableKeyAcrossRestart()
        {
            using (Fixture f = new Fixture())
            {
                f.Reply = delegate { return null; };
                f.EnableAndScan();
                Assert(f.Keys.Count == 1 && f.Service.Snapshot().LastResult.Contains("待确认"),
                    "unknown outcome was treated as success");
                f.Now = f.Now.AddSeconds(29); f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 1, "uncertain outcome retried without throttle");
                f.Service.Dispose(); f.Now = f.Now.AddSeconds(1); f.Open();
                f.Reply = delegate { return "alreadyRedeemed"; };
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 2 && f.Keys[0] == f.Keys[1],
                    "uncertain retry changed its idempotency key after restart");
                f.Service.Dispose(); f.Now = f.Now.AddSeconds(30); f.Open(); f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 2, "idempotent success was not terminal across restart");
            }
        }

        internal static void NothingToResetUsesNewLogicalAttemptAndNoCreditStops()
        {
            using (Fixture f = new Fixture())
            {
                f.Reply = delegate { return "nothingToReset"; };
                f.EnableAndScan();
                Assert(f.Service.Snapshot().LastResult.Contains("未消耗"), "nothingToReset falsely claimed a consumed card");
                f.Now = f.Now.AddSeconds(30); f.Reply = delegate { return "reset"; };
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 2 && f.Keys[0] != f.Keys[1],
                    "completed no-op reused a server-cached logical attempt");
            }
            using (Fixture f = new Fixture())
            {
                f.Reply = delegate { return "noCredit"; };
                f.EnableAndScan(); f.Now = f.Now.AddSeconds(30); f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 1, "noCredit repeatedly attempted an unavailable card");
            }
        }

        internal static void AccountSwitchPausesAndDisableDuringReadWins()
        {
            using (Fixture f = new Fixture())
            {
                f.Inventory.Credits = new[] { f.Card("later", 1000) };
                f.EnableAndScan();
                Assert(f.Keys.Count == 0, "future card was redeemed");
                f.Inventory.AccountHash = AccountB;
                f.Inventory.Credits = new[] { f.Card("other-account", 500) };
                f.Service.ScanNowForTests();
                Assert(!f.Service.Enabled && f.Keys.Count == 0 && f.Service.Snapshot().Message.Contains("账户"),
                    "account switch did not pause redemption");
                f.Service.Dispose(); f.Open();
                Assert(!f.Service.Enabled, "account switch pause did not survive restart");
                Assert(f.Service.SetEnabled(true), "explicit re-enable for new account failed");
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 1, "explicit new-account enable was not honored");
            }
            using (Fixture f = new Fixture())
            {
                f.Open(); Assert(f.Service.SetEnabled(true), "enable failed");
                f.BeforeRead = delegate { f.Service.SetEnabled(false); };
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 0 && !f.Service.Enabled, "pause during inventory fetch was bypassed");
            }
        }

        internal static void CorruptUnwritableAndCompetingStateStopWrites()
        {
            using (Fixture f = new Fixture())
            {
                File.WriteAllText(f.Pathname, "{broken");
                f.Open(); Assert(!f.Service.Enabled && !f.Service.Snapshot().Available && !f.Service.SetEnabled(true),
                    "corrupt state was silently reset or enabled");
                f.Service.ScanNowForTests(); Assert(f.Keys.Count == 0, "corrupt state redeemed a card");
            }
            using (Fixture f = new Fixture())
            {
                f.Open(); Assert(f.Service.SetEnabled(true), "enable failed");
                File.Delete(f.Pathname); Directory.CreateDirectory(f.Pathname);
                f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 0 && !f.Service.Enabled && !f.Service.Snapshot().Available,
                    "unwritable durable intent still sent a consume request");
            }
            using (Fixture f = new Fixture())
            {
                f.Open();
                using (AutoResetCreditsService other = new AutoResetCreditsService(f.Pathname, f.Read, f.Consume, delegate { return f.Now; }))
                {
                    Assert(!other.Snapshot().Available && !other.SetEnabled(true), "second process bypassed the state lock");
                    other.ScanNowForTests(); Assert(f.Keys.Count == 0, "second owner redeemed a card");
                }
            }
        }

        internal static void ExpiredPendingAttemptIsNeverSentAgain()
        {
            using (Fixture f = new Fixture())
            {
                f.Inventory.Credits = new[] { f.Card("soon", 1) };
                f.Reply = delegate { return null; };
                f.EnableAndScan();
                f.Service.Dispose(); f.Now = f.Now.AddSeconds(30); f.Open(); f.Service.ScanNowForTests();
                Assert(f.Keys.Count == 1, "expired uncertain attempt was sent again");
            }
        }

        internal static void SlowNetworkDoesNotBlockPauseSnapshotOrDispose()
        {
            using (Fixture f = new Fixture())
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ManualResetEvent uiCompleted = new ManualResetEvent(false))
            {
                f.Reply = delegate { entered.Set(); release.WaitOne(5000); return "reset"; };
                f.Open(); Assert(f.Service.SetEnabled(true), "enable failed");
                f.Service.RequestScan();
                Assert(entered.WaitOne(3000), "mock network was not reached");
                bool paused = false;
                Thread ui = new Thread(delegate() {
                    try
                    {
                        f.Service.Snapshot();
                        paused = f.Service.SetEnabled(false);
                        f.Service.Dispose();
                    }
                    finally { uiCompleted.Set(); }
                });
                ui.IsBackground = true;
                ui.Start();
                bool responsive = uiCompleted.WaitOne(1500);
                release.Set();
                ui.Join(4000);
                DateTime deadline = DateTime.UtcNow.AddSeconds(4);
                while (f.Service.Scanning && DateTime.UtcNow < deadline) Thread.Sleep(10);
                Assert(responsive && paused, "slow redemption blocked snapshot, pause, or dispose");
                Assert(!f.Service.Scanning, "disposed scan did not finish");
                AutoResetCreditsState stored = AutoResetCreditsState.Load(f.Pathname);
                Assert(!stored.Enabled && stored.LastResult.Contains("已自动使用"),
                    "pause/dispose lost the actual result of the already in-flight request");
            }
        }
    }
}
