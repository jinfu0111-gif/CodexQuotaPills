using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal sealed class AutoResetCreditsSnapshot
    {
        internal bool Enabled;
        internal bool Available;
        internal bool Scanning;
        internal string Message = "尚未检查重置卡";
        internal string LastResult = String.Empty;
        internal DateTime LastAttemptUtc = DateTime.MinValue;
        internal DateTime LastCheckedUtc = DateTime.MinValue;
        internal string AccountHash = String.Empty;
        internal int AvailableCount;
        internal bool HasDetails;
        internal ResetCreditRecord[] Credits = new ResetCreditRecord[0];
    }

    internal sealed class AutoResetCreditAttempt
    {
        internal string AccountHash;
        internal string CreditId;
        internal string IdempotencyKey;
        internal string Outcome;
        internal long ExpiresAt;
        internal long LastAttemptAt;
    }

    internal sealed class AutoResetCreditsState
    {
        internal bool Enabled;
        internal string AccountHash;
        internal string LastResult = String.Empty;
        internal long LastAttemptAt;
        internal readonly Dictionary<string, AutoResetCreditAttempt> Attempts =
            new Dictionary<string, AutoResetCreditAttempt>(StringComparer.Ordinal);

        internal static AutoResetCreditsState Load(string path)
        {
            AutoResetCreditsState state = new AutoResetCreditsState();
            if (!File.Exists(path)) return state;
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("重置卡记录过大");
            object data = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }
                .DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            if (ResumeJson.Number(ResumeJson.Get(data, "version")) != 1 ||
                !(ResumeJson.Get(data, "enabled") is bool)) throw new IOException("重置卡记录版本无效");
            state.Enabled = (bool)ResumeJson.Get(data, "enabled");
            object account = ResumeJson.Get(data, "accountHash");
            if (account != null && !AutoResetCreditsPolicy.IsAccountHash(account as string))
                throw new IOException("重置卡账户记录无效");
            state.AccountHash = account as string;
            state.LastResult = ResumeJson.Text(data, "lastResult") ?? String.Empty;
            if (state.LastResult.Length > 200 || !ReadTime(data, "lastAttemptAt", out state.LastAttemptAt))
                throw new IOException("重置卡结果记录无效");
            IDictionary<string, object> attempts = ResumeJson.Map(ResumeJson.Get(data, "attempts"));
            if (attempts == null || attempts.Count > 10000) throw new IOException("重置卡尝试记录无效");
            foreach (KeyValuePair<string, object> pair in attempts)
            {
                AutoResetCreditAttempt attempt = new AutoResetCreditAttempt();
                attempt.AccountHash = ResumeJson.Text(pair.Value, "accountHash");
                attempt.CreditId = ResumeJson.Text(pair.Value, "creditId");
                attempt.IdempotencyKey = ResumeJson.Text(pair.Value, "idempotencyKey");
                attempt.Outcome = ResumeJson.Text(pair.Value, "outcome");
                Guid key;
                if (!AutoResetCreditsPolicy.IsAccountHash(attempt.AccountHash) ||
                    !AutoResetCreditsPolicy.IsCreditId(attempt.CreditId) ||
                    !Guid.TryParseExact(attempt.IdempotencyKey, "D", out key) || key == Guid.Empty ||
                    !AutoResetCreditsPolicy.IsKnownOutcome(attempt.Outcome) ||
                    !ReadTime(pair.Value, "expiresAt", out attempt.ExpiresAt) || attempt.ExpiresAt == 0 ||
                    !ReadTime(pair.Value, "lastAttemptAt", out attempt.LastAttemptAt) ||
                    pair.Key != AutoResetCreditsPolicy.AttemptKey(attempt.AccountHash, attempt.CreditId))
                    throw new IOException("重置卡尝试记录损坏");
                state.Attempts.Add(pair.Key, attempt);
            }
            return state;
        }

        private static bool ReadTime(object data, string key, out long value)
        {
            value = 0;
            double? number = ResumeJson.Number(ResumeJson.Get(data, key));
            if (!number.HasValue || number.Value < 0 || number.Value > 253402300799L ||
                number.Value != Math.Floor(number.Value)) return false;
            value = (long)number.Value;
            return true;
        }

        internal void Save(string path)
        {
            Dictionary<string, object> entries = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, AutoResetCreditAttempt> pair in Attempts)
            {
                AutoResetCreditAttempt a = pair.Value;
                entries.Add(pair.Key, ResumeJson.Obj("accountHash", a.AccountHash, "creditId", a.CreditId,
                    "idempotencyKey", a.IdempotencyKey, "outcome", a.Outcome,
                    "expiresAt", a.ExpiresAt, "lastAttemptAt", a.LastAttemptAt));
            }
            if (entries.Count > 10000) throw new IOException("重置卡记录已达安全上限");
            string text = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }
                .Serialize(ResumeJson.Obj("version", 1, "enabled", Enabled, "accountHash", AccountHash,
                    "lastResult", LastResult, "lastAttemptAt", LastAttemptAt, "attempts", entries));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    internal static class AutoResetCreditsPolicy
    {
        internal const int LeadSeconds = 600;
        internal const int PollSeconds = 30;
        internal static bool IsAccountHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
        internal static bool IsCreditId(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && value.Length <= 512;
        }
        internal static bool IsKnownOutcome(string outcome)
        {
            return outcome == "pending" || outcome == "reset" || outcome == "alreadyRedeemed" ||
                outcome == "nothingToReset" || outcome == "noCredit";
        }
        internal static bool IsTerminal(string outcome)
        {
            return outcome == "reset" || outcome == "alreadyRedeemed" || outcome == "noCredit";
        }
        internal static string AttemptKey(string account, string id)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(account + "\n" + id)))
                    .Replace("-", "").ToLowerInvariant();
        }
        internal static long UnixTime(DateTime utc)
        {
            return (long)Math.Floor((utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
        }
        internal static DateTime FromUnix(long value)
        {
            return value <= 0 ? DateTime.MinValue :
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(value);
        }
        internal static bool IsEligible(ResetCreditRecord card, long now)
        {
            return card != null && IsCreditId(card.Id) && card.Status == "available" &&
                card.ResetType == "codexRateLimits" && card.ExpiresAt.HasValue &&
                ResetCreditDetails.IsValidExpiry(card.ExpiresAt.Value) &&
                card.ExpiresAt.Value > now && card.ExpiresAt.Value - now <= LeadSeconds;
        }
        internal static ResetCreditRecord Copy(ResetCreditRecord card)
        {
            return card == null ? null : new ResetCreditRecord { Id = card.Id, ResetType = card.ResetType,
                Status = card.Status, ExpiresAt = card.ExpiresAt };
        }
    }

    // Inventory and redemption stay separate from the read-only quota client.
    // The sole write is a named earned-reset consume with an on-disk idempotency key.
    internal sealed class AutoResetCreditsService : IDisposable
    {
        private readonly object gate = new object();
        private readonly string statePath;
        private readonly bool readOnly;
        private readonly Func<ResetCreditInventory> readInventory;
        private readonly Func<string, string, string, string> consume;
        private readonly Func<DateTime> utcNow;
        private readonly CodexAppServerClient client;
        private readonly Timer timer;
        private readonly bool backgroundEnabled;
        private FileStream processLock;
        private AutoResetCreditsState state = new AutoResetCreditsState();
        private AutoResetCreditsSnapshot view = new AutoResetCreditsSnapshot();
        private bool available;
        private bool disposed;
        private bool redemptionInFlight;
        private bool resourcesReleased;
        private int scanning;

        internal AutoResetCreditsService(bool readOnly = false)
            : this(readOnly, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPills", "AutoResetCredits", "state.json"), null, null,
                delegate { return DateTime.UtcNow; }, 1000, PollMilliseconds)
        {
        }

        internal AutoResetCreditsService(string path, Func<ResetCreditInventory> read,
            Func<string, string, string, string> redeem, Func<DateTime> clock)
            : this(false, path, read, redeem, clock, Timeout.Infinite, Timeout.Infinite)
        {
        }

        private const int PollMilliseconds = AutoResetCreditsPolicy.PollSeconds * 1000;

        // Used by the local activation step before starting this feature. It creates
        // no client or timer and cannot redeem a credit.
        internal static bool ConfigureEnabled(string path, bool enabled)
        {
            try
            {
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream configurationLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None))
                {
                    AutoResetCreditsState stored = AutoResetCreditsState.Load(path);
                    if (stored.Enabled != enabled)
                    {
                        stored.Enabled = enabled;
                        if (enabled) stored.AccountHash = null;
                    }
                    stored.Save(path);
                }
                return true;
            }
            catch { return false; }
        }

        private AutoResetCreditsService(bool readOnly, string path, Func<ResetCreditInventory> read,
            Func<string, string, string, string> redeem, Func<DateTime> clock, int dueTime, int interval)
        {
            this.readOnly = readOnly;
            backgroundEnabled = dueTime != Timeout.Infinite;
            statePath = path;
            utcNow = clock;
            if (!readOnly && (read == null || redeem == null)) client = new CodexAppServerClient();
            readInventory = read ?? delegate {
                ResetCreditInventory inventory;
                return client != null && client.TryReadResetCredits(out inventory) ? inventory : null;
            };
            consume = redeem ?? delegate(string account, string credit, string key) {
                string outcome;
                return client != null && client.TryConsumeResetCredit(account, credit, key, out outcome) ? outcome : null;
            };
            if (readOnly)
            {
                view.Message = "预览模式，不会自动使用重置卡";
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                    processLock = new FileStream(statePath + ".lock", FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                    state = AutoResetCreditsState.Load(statePath);
                    available = true;
                    view.Message = state.Enabled ? "后台监测中：到期前 10 分钟自动使用" : "已关闭自动使用；仅查看重置卡";
                    view.LastResult = state.LastResult;
                    view.LastAttemptUtc = AutoResetCreditsPolicy.FromUnix(state.LastAttemptAt);
                }
                catch
                {
                    state.Enabled = false;
                    view.Message = "记录不可用或已有监测进程，自动使用已停止";
                }
            }
            timer = new Timer(delegate { if (!this.readOnly && available && !disposed) RequestScan(); },
                null, readOnly ? Timeout.Infinite : dueTime, readOnly ? Timeout.Infinite : interval);
        }

        internal bool Enabled { get { lock (gate) return available && !disposed && !readOnly && state.Enabled; } }
        internal bool Scanning { get { return Interlocked.CompareExchange(ref scanning, 0, 0) != 0; } }
        internal AutoResetCreditsSnapshot Snapshot()
        {
            lock (gate)
            {
                List<ResetCreditRecord> cards = new List<ResetCreditRecord>();
                foreach (ResetCreditRecord card in view.Credits) cards.Add(AutoResetCreditsPolicy.Copy(card));
                return new AutoResetCreditsSnapshot { Enabled = Enabled, Available = available,
                    Scanning = Scanning, Message = view.Message, LastResult = view.LastResult,
                    LastAttemptUtc = view.LastAttemptUtc, LastCheckedUtc = view.LastCheckedUtc,
                    AccountHash = view.AccountHash, AvailableCount = view.AvailableCount,
                    HasDetails = view.HasDetails, Credits = cards.ToArray() };
            }
        }

        internal bool SetEnabled(bool enabled)
        {
            lock (gate)
            {
                if (disposed || readOnly || !available) return false;
                if (state.Enabled == enabled) return true;
                state.Enabled = enabled;
                // Re-enabling is an explicit choice for the currently signed-in account.
                if (enabled) state.AccountHash = null;
                if (!Persist()) return false;
                view.Message = enabled ? "后台监测中：到期前 10 分钟自动使用" :
                    redemptionInFlight ? "已关闭自动使用；上一请求结果确认中" : "已关闭自动使用；仅查看重置卡";
            }
            if (enabled && backgroundEnabled) RequestScan();
            return true;
        }

        internal void RequestScan()
        {
            if (!TryBeginScan()) return;
            ThreadPool.QueueUserWorkItem(delegate {
                try { Scan(); }
                catch { lock (gate) view.Message = "检查失败，未确认使用结果；稍后重查"; }
                finally { CompleteScan(); }
            });
        }

        internal void ScanNowForTests()
        {
            if (!TryBeginScan()) return;
            try { Scan(); }
            finally { CompleteScan(); }
        }

        private void Scan()
        {
            lock (gate) if (disposed || readOnly || !available) return;
            ResetCreditInventory inventory;
            try { inventory = readInventory(); }
            catch { inventory = null; }
            DateTime now = utcNow();
            if (!IsFresh(inventory, now))
            {
                lock (gate) if (!disposed) view.Message = "重置卡读取失败或数据过期，等待实时刷新";
                return;
            }
            ResetCreditRecord selected = null;
            AutoResetCreditAttempt attempt = null;
            lock (gate)
            {
                if (disposed || !available || readOnly) return;
                UpdateInventory(inventory);
                if (!state.Enabled)
                {
                    view.Message = "已关闭自动使用；仅查看重置卡";
                    return;
                }
                if (!inventory.HasDetails)
                {
                    view.Message = "重置卡数量已读取，明细待刷新；本次不会自动使用";
                    return;
                }
                if (state.AccountHash == null)
                {
                    state.AccountHash = inventory.AccountHash;
                    if (!Persist()) return;
                }
                else if (state.AccountHash != inventory.AccountHash)
                {
                    state.Enabled = false;
                    if (Persist()) view.Message = "登录账户已变化，已暂停；核对后可重新开启";
                    return;
                }
                bool conflicting;
                List<ResetCreditRecord> cards = UniqueCards(inventory.Credits, out conflicting);
                int availableRows = 0;
                foreach (ResetCreditRecord card in cards) if (card.Status == "available") availableRows++;
                if (conflicting || availableRows > inventory.AvailableCount)
                {
                    view.Message = "重置卡明细不一致，等待实时刷新";
                    return;
                }
                long nowUnix = AutoResetCreditsPolicy.UnixTime(now);
                cards.Sort(delegate(ResetCreditRecord left, ResetCreditRecord right) {
                    int order = Nullable.Compare<long>(left.ExpiresAt, right.ExpiresAt);
                    return order != 0 ? order : StringComparer.Ordinal.Compare(left.Id, right.Id);
                });
                FinalizeExpiredNothingToResetResult(nowUnix);
                if (state.LastAttemptAt > 0 && nowUnix - state.LastAttemptAt < AutoResetCreditsPolicy.PollSeconds)
                {
                    view.Message = "刚检查过使用结果，稍后继续监测";
                    ScheduleNextWakeup(cards, nowUnix);
                    return;
                }
                foreach (ResetCreditRecord card in cards)
                {
                    if (!AutoResetCreditsPolicy.IsEligible(card, nowUnix)) continue;
                    AutoResetCreditAttempt prior;
                    string recordKey = AutoResetCreditsPolicy.AttemptKey(inventory.AccountHash, card.Id);
                    if (state.Attempts.TryGetValue(recordKey, out prior))
                    {
                        if (AutoResetCreditsPolicy.IsTerminal(prior.Outcome) ||
                            prior.ExpiresAt != card.ExpiresAt.Value ||
                            nowUnix - prior.LastAttemptAt < AutoResetCreditsPolicy.PollSeconds) continue;
                        attempt = prior;
                    }
                    selected = card;
                    break;
                }
                if (selected == null)
                {
                    view.Message = inventory.AvailableCount == 0 ? "监测中：当前没有可用重置卡" :
                        "已检测到 " + inventory.AvailableCount.ToString(CultureInfo.InvariantCulture) + " 张可用重置卡 · 到期前 10 分钟自动使用";
                    ScheduleNextWakeup(cards, nowUnix);
                    return;
                }
                if (attempt == null)
                {
                    attempt = new AutoResetCreditAttempt { AccountHash = inventory.AccountHash,
                        CreditId = selected.Id, ExpiresAt = selected.ExpiresAt.Value,
                        IdempotencyKey = Guid.NewGuid().ToString("D") };
                    state.Attempts.Add(AutoResetCreditsPolicy.AttemptKey(inventory.AccountHash, selected.Id), attempt);
                }
                else if (attempt.Outcome == "nothingToReset")
                {
                    // The official client discards a completed non-consumption attempt's
                    // key. A later eligible window is a new logical redemption attempt.
                    attempt.IdempotencyKey = Guid.NewGuid().ToString("D");
                }
                attempt.Outcome = "pending";
                attempt.LastAttemptAt = nowUnix;
                state.LastAttemptAt = nowUnix;
                state.LastResult = "使用请求待确认";
                if (!Persist()) return; // No network write before durable intent.
                view.LastAttemptUtc = now;
                view.LastResult = state.LastResult;
            }
            // A pause wins until the durable attempt is handed to the transport.
            // Network calls run without the UI/state lock; an in-flight write cannot
            // be undone, but pause and snapshot requests remain responsive.
            lock (gate)
            {
                if (disposed || !available || !state.Enabled || state.AccountHash != inventory.AccountHash) return;
                redemptionInFlight = true;
                view.Message = "正在确认临期重置卡的使用结果";
            }
            string outcome;
            try { outcome = consume(inventory.AccountHash, selected.Id, attempt.IdempotencyKey); }
            catch { outcome = null; }
            lock (gate)
            {
                redemptionInFlight = false;
                if (outcome == "reset" || outcome == "alreadyRedeemed")
                {
                    attempt.Outcome = outcome;
                    state.LastResult = outcome == "reset" ? "已自动使用 1 张临期重置卡" : "该次重置已完成，未重复使用";
                }
                else if (outcome == "nothingToReset")
                {
                    attempt.Outcome = outcome;
                    state.LastResult = "已发起兑换，OpenAI 服务端返回无需重置（卡片未消耗），稍后重试";
                }
                else if (outcome == "noCredit")
                {
                    attempt.Outcome = outcome;
                    state.LastResult = "卡片已不可用，等待刷新可用卡片";
                }
                else
                {
                    // Unknown results retain the same durable key across retries/restarts.
                    state.LastResult = "使用结果待确认，稍后以同一请求重查";
                }
                view.LastResult = state.LastResult;
                if (!Persist()) return;
                view.Message = state.Enabled ? state.LastResult : "已关闭自动使用；" + state.LastResult;
            }
            // Always refresh from the service; do not infer new windows or credit counts.
            lock (gate) if (disposed || !available) return;
            ResetCreditInventory refreshed;
            try { refreshed = readInventory(); }
            catch { refreshed = null; }
            if (IsFresh(refreshed, utcNow()))
            {
                lock (gate)
                {
                    if (!disposed && refreshed.AccountHash == state.AccountHash)
                    {
                        UpdateInventory(refreshed);
                        bool ignored;
                        ScheduleNextWakeup(UniqueCards(refreshed.Credits, out ignored), AutoResetCreditsPolicy.UnixTime(utcNow()));
                    }
                }
            }
        }

        private void FinalizeExpiredNothingToResetResult(long nowUnix)
        {
            if (!state.LastResult.Contains("无需重置"))
                return;
            foreach (KeyValuePair<string, AutoResetCreditAttempt> pair in state.Attempts)
            {
                AutoResetCreditAttempt item = pair.Value;
                if (item != null && item.Outcome == "nothingToReset" && item.ExpiresAt > nowUnix)
                    return;
            }
            state.LastResult = "上次临期已发起兑换，OpenAI 服务端返回无需重置（未消耗）";
            view.LastResult = state.LastResult;
            Persist();
        }

        private void ScheduleNextWakeup(IList<ResetCreditRecord> cards, long nowUnix)
        {
            if (!backgroundEnabled || disposed || readOnly || !available || !state.Enabled || timer == null)
                return;
            long delaySeconds = AutoResetCreditsPolicy.PollSeconds;
            if (state.LastAttemptAt > 0 && nowUnix - state.LastAttemptAt < AutoResetCreditsPolicy.PollSeconds)
                delaySeconds = Math.Min(delaySeconds, Math.Max(1L, AutoResetCreditsPolicy.PollSeconds - Math.Max(0L, nowUnix - state.LastAttemptAt)));
            if (cards != null)
            {
                foreach (ResetCreditRecord card in cards)
                {
                    if (card == null || card.Status != "available" || card.ResetType != "codexRateLimits" || !card.ExpiresAt.HasValue)
                        continue;
                    long windowStart = card.ExpiresAt.Value - AutoResetCreditsPolicy.LeadSeconds;
                    if (windowStart > nowUnix && windowStart - nowUnix < delaySeconds)
                        delaySeconds = Math.Max(1L, windowStart - nowUnix);
                }
            }
            try { timer.Change((int)Math.Min( PollMilliseconds, delaySeconds * 1000L + 250L), PollMilliseconds); }
            catch { }
        }

        private bool TryBeginScan()
        {
            lock (gate)
            {
                return !disposed && !readOnly && available &&
                    Interlocked.CompareExchange(ref scanning, 1, 0) == 0;
            }
        }

        private static bool IsFresh(ResetCreditInventory inventory, DateTime now)
        {
            return inventory != null && AutoResetCreditsPolicy.IsAccountHash(inventory.AccountHash) &&
                inventory.AvailableCount >= 0 && inventory.Credits != null && inventory.Credits.Length <= 10000 &&
                now.Kind == DateTimeKind.Utc && inventory.ReadUtc.Kind == DateTimeKind.Utc &&
                (now - inventory.ReadUtc).TotalSeconds >= -5 && (now - inventory.ReadUtc).TotalSeconds <= 30;
        }
        private static List<ResetCreditRecord> UniqueCards(ResetCreditRecord[] cards, out bool conflicting)
        {
            conflicting = false;
            Dictionary<string, ResetCreditRecord> unique = new Dictionary<string, ResetCreditRecord>(StringComparer.Ordinal);
            foreach (ResetCreditRecord card in cards)
            {
                if (card == null || !AutoResetCreditsPolicy.IsCreditId(card.Id)) continue;
                ResetCreditRecord previous;
                if (unique.TryGetValue(card.Id, out previous))
                {
                    if (previous.ExpiresAt != card.ExpiresAt || previous.ResetType != card.ResetType || previous.Status != card.Status)
                        conflicting = true;
                }
                else unique.Add(card.Id, card);
            }
            return new List<ResetCreditRecord>(unique.Values);
        }
        private void UpdateInventory(ResetCreditInventory inventory)
        {
            List<ResetCreditRecord> cards = new List<ResetCreditRecord>();
            foreach (ResetCreditRecord card in inventory.Credits) if (card != null) cards.Add(AutoResetCreditsPolicy.Copy(card));
            view.AccountHash = inventory.AccountHash;
            view.AvailableCount = inventory.AvailableCount;
            view.HasDetails = inventory.HasDetails;
            view.Credits = cards.ToArray();
            view.LastCheckedUtc = inventory.ReadUtc;
        }
        private bool Persist()
        {
            try { state.Save(statePath); return true; }
            catch
            {
                state.Enabled = false;
                available = false;
                view.Message = "重置卡记录无法安全保存，自动使用已停止";
                return false;
            }
        }
        private void CompleteScan()
        {
            bool release = false;
            lock (gate)
            {
                redemptionInFlight = false;
                Interlocked.Exchange(ref scanning, 0);
                if (disposed && !resourcesReleased) { resourcesReleased = true; release = true; }
            }
            if (release) ReleaseResources();
        }
        private void ReleaseResources()
        {
            try { if (client != null) client.Dispose(); }
            finally { if (processLock != null) processLock.Dispose(); }
        }
        public void Dispose()
        {
            bool release = false;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (!Scanning && !resourcesReleased) { resourcesReleased = true; release = true; }
            }
            timer.Dispose();
            if (release) ReleaseResources();
        }
    }
}
