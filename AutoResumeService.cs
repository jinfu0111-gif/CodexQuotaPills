using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal sealed class ResumeCandidate
    {
        internal string Id;
        internal string Title;
        internal string TurnId;
        internal string Status;
        internal string Fingerprint;
        internal string Key { get { return Id + ":" + TurnId; } }
        internal ResumeCandidate Clone() { return (ResumeCandidate)MemberwiseClone(); }
    }

    // Ledger is saved before dispatch. An intent without a confirmed result
    // stays uncertain after a crash/restart; it never becomes a retry request.
    internal sealed class AutoResumeLedger
    {
        internal bool Enabled = true;
        internal string AccountHash;
        internal Dictionary<string, string> Outcomes = new Dictionary<string, string>();
        internal Dictionary<string, int> Counts = new Dictionary<string, int>();
        internal Dictionary<string, string> Fingerprints = new Dictionary<string, string>();

        internal bool CanSend(string key, string thread)
        {
            int count;
            return Enabled && !Outcomes.ContainsKey(key) && (!Counts.TryGetValue(thread, out count) || count < 3);
        }
        internal void Intent(string key, string thread)
        {
            if (!CanSend(key, thread)) throw new InvalidOperationException("续跑已取消、已发送或达到上限");
            Outcomes[key] = "intent";
            int count;
            Counts.TryGetValue(thread, out count);
            Counts[thread] = count + 1;
        }
        internal static AutoResumeLedger Load(string path)
        {
            AutoResumeLedger ledger = new AutoResumeLedger();
            if (!File.Exists(path)) return ledger;
            object data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            double? version = ResumeJson.Number(ResumeJson.Get(data, "version"));
            if ((version != 1 && version != 2) || ResumeJson.Map(ResumeJson.Get(data, "outcomes")) == null ||
                ResumeJson.Map(ResumeJson.Get(data, "counts")) == null || !(ResumeJson.Get(data, "enabled") is bool))
                throw new IOException("续跑记录损坏，已停止自动续跑");
            // 0.2.16 explicitly switches the former opt-in feature to automatic.
            // Migrate v1 once; a subsequent explicit pause in v2 stays paused.
            ledger.Enabled = version == 1 || (bool)ResumeJson.Get(data, "enabled");
            ledger.AccountHash = ResumeJson.Text(data, "accountHash");
            foreach (KeyValuePair<string, object> pair in ResumeJson.Map(ResumeJson.Get(data, "outcomes")))
            {
                string value = pair.Value as string;
                if (value != "intent" && value != "sent" && value != "uncertain" && value != "cancelled")
                    throw new IOException("续跑记录不兼容");
                ledger.Outcomes.Add(pair.Key, value);
            }
            foreach (KeyValuePair<string, object> pair in ResumeJson.Map(ResumeJson.Get(data, "counts")))
            {
                double? value = ResumeJson.Number(pair.Value);
                if (!value.HasValue || value < 0 || value > Int32.MaxValue || value != Math.Floor(value.Value)) throw new IOException("续跑计数不兼容");
                ledger.Counts.Add(pair.Key, (int)value.Value);
            }
            IDictionary<string, object> fingerprints = ResumeJson.Map(ResumeJson.Get(data, "fingerprints"));
            if (fingerprints == null) throw new IOException("续跑状态指纹缺失");
            foreach (KeyValuePair<string, object> pair in fingerprints)
            {
                string hash = pair.Value as string;
                if (hash == null || hash.Length != 64) throw new IOException("续跑状态指纹损坏");
                ledger.Fingerprints.Add(pair.Key, hash);
            }
            return ledger;
        }
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            string content = new JavaScriptSerializer().Serialize(ResumeJson.Obj("version", 2, "enabled", Enabled,
                "accountHash", AccountHash, "outcomes", Outcomes, "counts", Counts, "fingerprints", Fingerprints));
            using (FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }

    internal sealed class AutoResumeService : IDisposable
    {
        private readonly object gate = new object();
        private readonly CodexAppServerClient reader = new CodexAppServerClient();
        private readonly Timer timer;
        private readonly string ledgerPath;
        private readonly Func<string, object, object> readRequest;
        private readonly Func<IResumeDesktop> desktopFactory;
        private FileStream processLock;
        private AutoResumeLedger ledger = new AutoResumeLedger();
        private List<ResumeCandidate> candidates = new List<ResumeCandidate>();
        private readonly Dictionary<string, string> observedFingerprints = new Dictionary<string, string>();
        private bool disposed;
        private bool available;
        private readonly bool readOnly;
        private int scanning;
        private string message = "后台自动监测即将开始，无需手动扫描";

        internal AutoResumeService(bool readOnly = false)
            : this(readOnly, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPills", "AutoResume", "ledger.json"), null, null, 1000, 30000)
        {
        }

#if AUTO_RESUME_TESTS
        internal AutoResumeService(string testPath, Func<string, object, object> readRequest,
            Func<IResumeDesktop> desktopFactory, int dueTime, int interval)
            : this(false, testPath, readRequest, desktopFactory, dueTime, interval)
        {
        }
#endif

        private AutoResumeService(bool readOnly, string path, Func<string, object, object> request,
            Func<IResumeDesktop> factory, int dueTime, int interval)
        {
            this.readOnly = readOnly;
            ledgerPath = path;
            readRequest = request ?? reader.ReadOnlyRequest;
            desktopFactory = factory ?? delegate { return new CodexDesktopResumeClient(); };
            try
            {
                if (!readOnly)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath));
                    processLock = new FileStream(ledgerPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    ledger = AutoResumeLedger.Load(ledgerPath);
                }
                available = true;
                if (readOnly) { ledger.Enabled = false; message = "只读检查模式，不会自动发送"; }
                else if (Persist()) message = ledger.Enabled ? "已自动开启后台监测；每 30 秒检查额度中断任务" : "已暂停自动续跑；后台仍会检测任务";
            }
            catch { message = "续跑记录不可用或已有监测进程；自动续跑已停止"; ledger.Enabled = false; }
            timer = new Timer(delegate { if (!this.readOnly && available && !disposed) RequestScan(); }, null, dueTime, interval);
        }
        internal bool Enabled { get { lock (gate) return !readOnly && available && ledger.Enabled && !disposed; } }
        internal bool Scanning { get { return Interlocked.CompareExchange(ref scanning, 0, 0) != 0; } }
        internal string Message { get { lock (gate) return message; } }
        internal List<ResumeCandidate> Snapshot()
        {
            lock (gate) return candidates.ConvertAll(c => c.Clone());
        }
        internal bool SetEnabled(bool enabled)
        {
            lock (gate)
            {
                if (readOnly || !available || disposed) return false;
                ledger.Enabled = enabled;
                if (enabled) ledger.AccountHash = null; // A deliberate re-enable may follow an account switch.
                if (!Persist()) return false;
                message = enabled ? "后台监测中：额度恢复后自动继续，每个对话最多 3 次" : "已暂停续跑；继续自动检测，但不发送消息";
            }
            if (enabled) RequestScan();
            return true;
        }
        internal void Cancel(string key)
        {
            lock (gate)
            {
                if (readOnly || !available || disposed) return;
                ledger.Outcomes[key] = "cancelled";
                Persist();
                foreach (ResumeCandidate c in candidates) if (c.Key == key) c.Status = "已取消此轮续跑";
            }
        }
        internal void RequestScan()
        {
            if (disposed || Interlocked.CompareExchange(ref scanning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Scan(); }
                catch { lock (gate) message = "读取失败，本次没有自动续跑；稍后重查"; }
                finally { Interlocked.Exchange(ref scanning, 0); }
            });
        }

        private void Scan()
        {
            if (disposed) return;
            List<ResumeCandidate> found = new List<ResumeCandidate>();
            string cursor = null;
            HashSet<string> seen = new HashSet<string>();
            int inspected = 0;
            bool truncated = false;
            do
            {
                IDictionary<string, object> query = ResumeJson.Obj("limit", 100, "archived", false,
                    "sortKey", "updated_at", "modelProviders", new object[0], "sourceKinds", new[] { "appServer", "cli", "exec", "vscode", "unknown" });
                if (cursor != null) query["cursor"] = cursor;
                object page = readRequest("thread/list", query);
                IList rows = ResumeJson.List(ResumeJson.Get(page, "data"));
                if (page == null || rows == null) throw new IOException();
                foreach (object row in rows)
                {
                    if (disposed) return;
                    if (inspected >= 500) { truncated = true; break; }
                    string id = ResumeJson.Text(row, "id");
                    Guid uuid;
                    if (id == null || !Guid.TryParse(id, out uuid) || !seen.Add(id)) continue;
                    if (ResumeJson.Get(row, "parentThreadId") != null || Object.Equals(ResumeJson.Get(row, "ephemeral"), true)) continue;
                    inspected++;
                    // Cheap latest-turn paging first. Legacy CLI falls back to
                    // thread/read. Neither call subscribes or starts a model.
                    object turnsPage = readRequest("thread/turns/list", ResumeJson.Obj("threadId", id,
                        "limit", 1, "sortDirection", "desc", "itemsView", "notLoaded"));
                    IList turns = ResumeJson.List(ResumeJson.Get(turnsPage, "data"));
                    object latest = turns != null && turns.Count > 0 ? turns[0] : null;
                    if (turnsPage == null)
                    {
                        object stored = readRequest("thread/read", ResumeJson.Obj("threadId", id, "includeTurns", true));
                        turns = ResumeJson.List(ResumeJson.Get(ResumeJson.Get(stored, "thread"), "turns"));
                        latest = turns != null && turns.Count > 0 ? turns[turns.Count - 1] : null;
                    }
                    if (!AutoResumePolicy.IsQuotaFailure(latest)) continue;
                    string turnId = ResumeJson.Text(latest, "id") ?? ResumeJson.Text(latest, "turnId");
                    if (String.IsNullOrWhiteSpace(turnId)) continue;
                    ResumeCandidate c = new ResumeCandidate { Id = id, TurnId = turnId,
                        Title = ResumeJson.Text(row, "name") ?? "未命名对话", Status = "额度中断 · 等待原对话状态验证" };
                    InspectLive(c);
                    found.Add(c);
                    if (inspected >= 500) break;
                }
                cursor = ResumeJson.Text(page, "nextCursor");
                if (inspected >= 500) { truncated = truncated || cursor != null; break; }
                if (cursor != null && !seen.Add("cursor:" + cursor)) throw new IOException();
            } while (cursor != null);
            lock (gate)
            {
                candidates = found;
                message = "已检查 " + inspected + " 个未归档对话，发现 " + found.Count + " 个额度中断" +
                    (truncated ? "（仅最近 500 个）" : "") + (ledger.Enabled ? " · 自动续跑开启" : " · 自动续跑关闭");
            }
            // Sequential dispatch; no mass concurrent resume or quota stampede.
            foreach (ResumeCandidate c in found)
            {
                if (!Enabled) break;
                if (c.Fingerprint != null) TryResume(c);
            }
        }

        private void InspectLive(ResumeCandidate c)
        {
            lock (gate)
            {
                string outcome;
                if (ledger.Outcomes.TryGetValue(c.Key, out outcome))
                {
                    c.Status = OutcomeText(outcome);
                    return;
                }
            }
            try
            {
                using (IResumeDesktop desktop = desktopFactory())
                {
                    ResumeDecision d = AutoResumePolicy.AssessDesktop(desktop.Snapshot(c.Id), c.Id);
                    c.Status = d.Reason;
                    if (!d.Eligible || d.TurnId != c.TurnId) return;
                    lock (gate)
                    {
                        string first;
                        if ((ledger.Fingerprints.TryGetValue(c.Key, out first) || observedFingerprints.TryGetValue(c.Key, out first)) && first != d.Fingerprint)
                        { c.Status = "模型、权限或任务状态已改变，不自动续跑"; return; }
                        observedFingerprints[c.Key] = d.Fingerprint;
                        if (ledger.Enabled && !ledger.Fingerprints.ContainsKey(c.Key))
                        { ledger.Fingerprints[c.Key] = d.Fingerprint; if (!Persist()) return; }
                    }
                    c.Fingerprint = d.Fingerprint;
                }
            }
            catch (IOException ex) { c.Status = ex.Message; }
            catch { c.Status = "无法验证原对话，不自动续跑"; }
        }

        private void TryResume(ResumeCandidate c)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (gate)
                if (!ledger.CanSend(c.Key, c.Id)) { c.Status = "已取消、已尝试或达到 3 次上限"; return; }
            object account = ResumeJson.Get(readRequest("account/read", ResumeJson.Obj("refreshToken", false)), "account");
            if (ResumeJson.Text(account, "type") != "chatgpt") { c.Status = "无法验证 ChatGPT 登录，不自动续跑"; return; }
            string email = ResumeJson.Text(account, "email");
            string accountId = ResumeJson.Text(account, "accountId");
            if (String.IsNullOrWhiteSpace(email) && String.IsNullOrWhiteSpace(accountId))
            { c.Status = "无法确认账户身份，不自动续跑"; return; }
            string identity = ResumeJson.Hash(ResumeJson.Obj("type", "chatgpt", "email", email, "accountId", accountId));
            lock (gate)
            {
                if (ledger.AccountHash != null && ledger.AccountHash != identity)
                { ledger.Enabled = false; Persist(); c.Status = "登录账户已改变，请人工检查后重新启用"; return; }
                if (ledger.AccountHash == null) { ledger.AccountHash = identity; if (!Persist()) return; }
            }
            ResumeQuota quota = AutoResumePolicy.Quota(readRequest("account/rateLimits/read", null), now);
            if (!quota.Ready)
            {
                c.Status = quota.Reason + " · 预计 " + DateTimeOffset.FromUnixTimeSeconds(quota.RetryAt).ToLocalTime().ToString("M/d HH:mm") + " · 每 30 秒复查";
                return;
            }
            try
            {
                if (!StillUnarchived(c.Id))
                { c.Status = "对话已归档或无法确认，本次不续跑"; return; }
                object latest = LatestStoredTurn(c.Id);
                if (!AutoResumePolicy.IsQuotaFailure(latest) ||
                    (ResumeJson.Text(latest, "id") ?? ResumeJson.Text(latest, "turnId")) != c.TurnId)
                { c.Status = "最新执行已变化，本次不续跑"; return; }
                using (IResumeDesktop desktop = desktopFactory())
                {
                    ResumeDecision fresh = AutoResumePolicy.AssessDesktop(desktop.Snapshot(c.Id), c.Id);
                    if (!fresh.Eligible || fresh.Fingerprint != c.Fingerprint || fresh.TurnId != c.TurnId)
                    { c.Status = "任务已变化，本次不续跑"; return; }
                    lock (gate)
                    {
                        if (disposed || !ledger.CanSend(c.Key, c.Id)) return;
                        ledger.Intent(c.Key, c.Id);
                        if (!Persist()) return;
                    }
                    try
                    {
                        desktop.Resume(c.Id, c.Fingerprint, Guid.NewGuid().ToString(), delegate
                        {
                            lock (gate) return !disposed && available && ledger.Enabled && ledger.Outcomes[c.Key] == "intent";
                        });
                        lock (gate) { ledger.Outcomes[c.Key] = "sent"; Persist(); }
                        c.Status = "已在原对话续跑";
                    }
                    catch
                    {
                        lock (gate)
                        {
                            if (ledger.Outcomes[c.Key] == "intent") ledger.Outcomes[c.Key] = "uncertain";
                            Persist();
                        }
                        c.Status = "未确认发送结果，请检查原对话；不自动重发";
                    }
                }
            }
            catch { c.Status = "原对话连接失败，本次没有续跑"; }
        }

        private object LatestStoredTurn(string id)
        {
            object page = readRequest("thread/turns/list", ResumeJson.Obj("threadId", id,
                "limit", 1, "sortDirection", "desc", "itemsView", "notLoaded"));
            IList turns = ResumeJson.List(ResumeJson.Get(page, "data"));
            if (page != null) return turns != null && turns.Count > 0 ? turns[0] : null;
            object stored = readRequest("thread/read", ResumeJson.Obj("threadId", id, "includeTurns", true));
            turns = ResumeJson.List(ResumeJson.Get(ResumeJson.Get(stored, "thread"), "turns"));
            return turns != null && turns.Count > 0 ? turns[turns.Count - 1] : null;
        }

        private bool StillUnarchived(string id)
        {
            // Re-read membership immediately before dispatch, not just at scan
            // enrollment. Unknown/malformed pages and pagination fail closed.
            string cursor = null;
            HashSet<string> cursors = new HashSet<string>();
            for (int pageIndex = 0; pageIndex < 5; pageIndex++)
            {
                if (!Enabled) return false;
                IDictionary<string, object> query = ResumeJson.Obj("limit", 100, "archived", false,
                    "sortKey", "updated_at", "modelProviders", new object[0],
                    "sourceKinds", new[] { "appServer", "cli", "exec", "vscode", "unknown" });
                if (cursor != null) query["cursor"] = cursor;
                object page = readRequest("thread/list", query);
                IList rows = ResumeJson.List(ResumeJson.Get(page, "data"));
                if (rows == null) return false;
                foreach (object row in rows)
                    if (ResumeJson.Text(row, "id") == id)
                        return ResumeJson.Get(row, "parentThreadId") == null &&
                            !Object.Equals(ResumeJson.Get(row, "ephemeral"), true);
                cursor = ResumeJson.Text(page, "nextCursor");
                if (cursor == null || !cursors.Add(cursor)) return false;
            }
            return false;
        }

        private bool Persist()
        {
            if (readOnly || processLock == null) return false;
            try { ledger.Save(ledgerPath); return true; }
            catch { available = false; ledger.Enabled = false; message = "无法保存防重复记录，自动续跑已停止"; return false; }
        }
        private static string OutcomeText(string outcome)
        {
            return outcome == "sent" ? "已在原对话续跑" : outcome == "cancelled" ? "已取消此轮续跑" : "上次发送结果未确认，不自动重发";
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
            }
            timer.Dispose();
            // Don't block the UI on a possibly pending read; release the process
            // lock only after the worker has exited and its client is disposed.
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (Scanning) Thread.Sleep(100);
                reader.Dispose();
                if (processLock != null) processLock.Dispose();
            });
        }
    }
}
