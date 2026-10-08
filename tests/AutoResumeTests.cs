using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal static class AutoResumeTests
    {
        private const string Id = "11111111-1111-1111-1111-111111111111";
        private static Dictionary<string, object> Obj(params object[] pairs) { return ResumeJson.Obj(pairs); }
        private static Dictionary<string, object> State()
        {
            return Obj("id", Id, "hostId", "local", "resumeState", "resumed", "requests", new object[0],
                "threadRuntimeStatus", Obj("type", "systemError"), "cwd", "F:\\test", "latestModel", "test-model",
                "currentPermissions", Obj("approvalPolicy", "on-request"), "turns", new[] { Turn("failed", "usageLimitExceeded") });
        }
        private static Dictionary<string, object> Turn(string status, string error)
        { return Obj("turnId", "failed-turn", "status", status, "error", Obj("codexErrorInfo", error)); }
        private static ResumeDecision Assess(object state) { return AutoResumePolicy.AssessDesktop(state, Id); }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

        internal static void StructuredErrorsOnly()
        {
            Assert(Assess(State()).Eligible, "verified quota error rejected");
            foreach (string status in new[] { "completed", "interrupted", "inProgress" })
            { var s = State(); s["turns"] = new[] { Turn(status, "usageLimitExceeded") }; Assert(!Assess(s).Eligible, status + " resumed"); }
            foreach (string info in new[] { "Unauthorized", "Other", "usage limit reached", "HttpConnectionFailed" })
            { var s = State(); s["turns"] = new[] { Turn("failed", info) }; Assert(!Assess(s).Eligible, "fuzzy or other error resumed"); }
            var completed = State(); completed["turns"] = new[] { Turn("failed", "usageLimitExceeded"), Turn("completed", null) };
            Assert(!Assess(completed).Eligible, "older quota failure resumed");
        }
        internal static void AttentionAndSettingsGuard()
        {
            foreach (string key in new[] { "requests", "queuedFollowUps" })
            { var s = State(); s[key] = new[] { Obj("method", "approval") }; Assert(!Assess(s).Eligible, key + " bypassed"); }
            foreach (string key in new[] { "parentThreadId", "threadGoalResumeConfirmation" })
            { var s = State(); s[key] = "present"; Assert(!Assess(s).Eligible, key + " bypassed"); }
            var paused = State(); paused["threadGoal"] = Obj("status", "paused"); Assert(!Assess(paused).Eligible, "paused goal resumed");
            var active = State(); active["threadRuntimeStatus"] = Obj("type", "active"); Assert(!Assess(active).Eligible, "active thread resumed");
            var remote = State(); remote["hostId"] = "remote"; Assert(!Assess(remote).Eligible, "remote thread resumed");
            var changed = State(); changed["latestModel"] = "different-model";
            Assert(Assess(changed).Fingerprint != Assess(State()).Fingerprint, "model not bound");
            changed = State(); changed["currentPermissions"] = Obj("approvalPolicy", "never");
            Assert(Assess(changed).Fingerprint != Assess(State()).Fingerprint, "permissions not bound");
            var titleOnly = State(); titleOnly["title"] = "new title";
            Assert(Assess(titleOnly).Fingerprint == Assess(State()).Fingerprint, "title falsely invalidates work");
        }
        internal static void CanonicalHistoryMustBeComplete()
        {
            var s = State();
            s["turnHistory"] = Obj("kind", "canonical", "history", Obj("islands", new[] {
                Obj("newerBoundary", Obj("status", "exhausted"), "entries", new[] { Obj("value", "k") }) },
                "entitiesByKey", Obj("k", Turn("failed", "usageLimitExceeded"))));
            Assert(Assess(s).Eligible, "canonical quota error rejected");
            var history = ResumeJson.Get(s["turnHistory"], "history");
            var tail = ((IList)ResumeJson.Get(history, "islands"))[0];
            ResumeJson.Map(ResumeJson.Get(tail, "newerBoundary"))["status"] = "unknown";
            Assert(!Assess(s).Eligible, "incomplete history authorized resume");
        }
        private static object QuotaWindow(double used, long reset) { return Obj("usedPercent", used, "resetsAt", reset, "windowDurationMins", 300); }
        internal static void QuotaRecheckedAndWeeklyHonored()
        {
            const long now = 1800000000;
            object response = Obj("rateLimits", Obj("primary", QuotaWindow(100, now + 600), "secondary", QuotaWindow(100, now + 6000)));
            ResumeQuota q = AutoResumePolicy.Quota(response, now);
            Assert(!q.Ready && q.RetryAt == now + 6060, "later weekly reset not honored");
            response = Obj("rateLimits", Obj("primary", QuotaWindow(20, now + 600), "secondary", QuotaWindow(30, now + 6000)));
            Assert(AutoResumePolicy.Quota(response, now).Ready, "fresh usable quota rejected");
            response = Obj("rateLimits", Obj("primary", QuotaWindow(20, now - 1)));
            Assert(!AutoResumePolicy.Quota(response, now).Ready, "stale snapshot authorized resume");
            response = Obj("rateLimitsByLimitId", Obj("other", Obj("primary", QuotaWindow(0, now + 1))));
            Assert(!AutoResumePolicy.Quota(response, now).Ready, "wrong limit bucket selected");
            response = Obj("rateLimits", Obj("primary", QuotaWindow(Double.NaN, now + 1)));
            Assert(!AutoResumePolicy.Quota(response, now).Ready, "NaN authorized resume");
            Assert(!AutoResumePolicy.Quota(null, now).Ready, "missing quota authorized resume");
        }
        internal static void IntentSurvivesRestartAndCancellation()
        {
            string folder = Path.Combine(Path.GetTempPath(), "quota-resume-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "ledger.json");
            try
            {
                AutoResumeLedger ledger = new AutoResumeLedger { Enabled = true };
                ledger.Fingerprints["thread:turn"] = new string('a', 64);
                ledger.Intent("thread:turn", "thread");
                ledger.Save(path);
                ledger = AutoResumeLedger.Load(path);
                Assert(!ledger.CanSend("thread:turn", "thread"), "restart retried uncertain intent");
                Assert(ledger.Fingerprints["thread:turn"] == new string('a', 64), "fingerprint lost on restart");
                ledger.Outcomes["thread:cancel"] = "cancelled";
                Assert(!ledger.CanSend("thread:cancel", "thread"), "cancel ignored");
                ledger.Intent("thread:turn2", "thread"); ledger.Intent("thread:turn3", "thread");
                Assert(!ledger.CanSend("thread:turn4", "thread"), "budget ignored");
                ledger.Enabled = false;
                Assert(!ledger.CanSend("other:turn", "other"), "disabled watcher sent");
                File.WriteAllText(path, "{invalid");
                bool rejected = false; try { AutoResumeLedger.Load(path); } catch { rejected = true; }
                Assert(rejected, "corrupt ledger silently reset");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        internal static void AutomaticDefaultsMigrateOnceAndPreservePause()
        {
            string folder = Path.Combine(Path.GetTempPath(), "quota-resume-defaults-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "ledger.json");
            try
            {
                Assert(AutoResumeLedger.Load(path).Enabled, "new install requires manual enable");
                AutoResumeLedger old = new AutoResumeLedger();
                old.Fingerprints["thread:turn"] = new string('a', 64);
                old.Intent("thread:turn", "thread");
                old.Enabled = false;
                old.Save(path);
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\":2", "\"version\":1"));
                AutoResumeLedger migrated = AutoResumeLedger.Load(path);
                Assert(migrated.Enabled, "old opt-in ledger did not become automatic");
                Assert(!migrated.CanSend("thread:turn", "thread") && migrated.Counts["thread"] == 1,
                    "migration reset deduplication or budget");
                migrated.Enabled = false;
                migrated.Save(path);
                Assert(!AutoResumeLedger.Load(path).Enabled, "explicit v2 pause was re-enabled on restart");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        private sealed class FakeDesktop : IResumeDesktop
        {
            private readonly Action send;
            internal FakeDesktop(Action send) { this.send = send; }
            public IDictionary<string, object> Snapshot(string id) { Assert(id == Id, "wrong thread snapshot"); return State(); }
            public string Resume(string id, string fingerprint, string messageId, Func<bool> guard)
            {
                Assert(id == Id && fingerprint == Assess(State()).Fingerprint, "wrong resume fingerprint");
                if (!guard()) throw new IOException("cancelled");
                send();
                return "new-turn";
            }
            public void Dispose() { }
        }

        internal static void BackgroundScanAndEarlyRecoveryNeedNoClicks()
        {
            string folder = Path.Combine(Path.GetTempPath(), "quota-resume-background-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "ledger.json");
            int used = 100, reads = 0, sends = 0;
            long reset = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200;
            Func<string, object, object> read = delegate(string method, object parameters)
            {
                if (method == "thread/list") return Obj("data", new[] { Obj("id", Id, "name", "isolated fixture") });
                if (method == "thread/turns/list") return Obj("data", new[] { Turn("failed", "usageLimitExceeded") });
                if (method == "account/read") return Obj("account", Obj("type", "chatgpt", "email", "fixture@example.invalid"));
                if (method == "account/rateLimits/read")
                {
                    Interlocked.Increment(ref reads);
                    return Obj("rateLimits", Obj("primary", QuotaWindow(Interlocked.CompareExchange(ref used, 0, 0), reset)));
                }
                throw new Exception("unexpected read: " + method);
            };
            Func<IResumeDesktop> desktop = delegate { return new FakeDesktop(delegate { Interlocked.Increment(ref sends); }); };
            AutoResumeService monitor = null;
            try
            {
                // The real background timer runs the full production scan and
                // dispatch path, but every external boundary is an isolated fake.
                monitor = new AutoResumeService(path, read, desktop, 10, 60);
                WaitUntil(delegate { return Interlocked.CompareExchange(ref reads, 0, 0) > 0; }, "startup never scanned automatically");
                Assert(monitor.Enabled && sends == 0, "zero quota resumed or default was disabled");
                Interlocked.Exchange(ref used, 15);
                WaitUntil(delegate { return Interlocked.CompareExchange(ref sends, 0, 0) == 1; }, "early recovery needed manual scan or waited for scheduled reset");
                Thread.Sleep(180);
                Assert(sends == 1, "background scans repeated continuation");
                Assert(monitor.SetEnabled(false), "pause failed");
                monitor.Dispose();
                WaitUntil(delegate { return !monitor.Scanning; }, "scan did not finish");
                Assert(!AutoResumeLedger.Load(path).Enabled, "pause was not persisted");
                WaitUntil(delegate
                {
                    try { using (FileStream check = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true; }
                    catch (IOException) { return false; }
                }, "process lock was not released");
                monitor = new AutoResumeService(path, read, desktop, 10, 60);
                WaitUntil(delegate { return monitor.Snapshot().Count == 1; }, "paused service no longer automatically detected tasks");
                Assert(!monitor.Enabled && sends == 1, "paused restart dispatched continuation");
            }
            finally
            {
                if (monitor != null) monitor.Dispose();
                // Disposal releases its lock asynchronously after the worker.
                for (int i = 0; i < 30 && Directory.Exists(folder); i++)
                { try { Directory.Delete(folder, true); } catch (IOException) { Thread.Sleep(20); } }
            }
        }

        private static void WaitUntil(Func<bool> condition, string error)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert(condition(), error);
        }
        internal static void FutureVersionsRequireVerifiedContract()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            string path = Path.Combine(root, "OpenAI.Codex_26.930.6422.0_x64__2p2nqsd0c76g0", "app", "ChatGPT.exe");
            Assert(CodexDesktopResumeClient.IsSupportedServer(path), "validated app rejected");
            Assert(CodexDesktopResumeClient.IsSupportedServer(path.Replace("26.930.6422.0", "26.999.0.0")), "compatible future app path rejected");
            Assert(!CodexDesktopResumeClient.IsSupportedServer("C:\\fake" + path.Substring(2)), "lookalike path accepted");
            Assert(!CodexDesktopResumeClient.IsSupportedServer(path.Replace("2p2nqsd0c76g0", "fakepublisher")), "fake publisher accepted");
            string declarations = "\"thread-stream-state-changed\":11,\"thread-stream-following-changed\":1,\"thread-owner-discovery\":1,\"thread-follower-start-turn\":2,\"thread-archived\":2,\"thread-queued-followups-changed\":2";
            string implementation = "case`thread-follower-start-turn`: e.startTurn(t.params.conversationId,t.params.turnStart); inheritThreadSettings: true; supportsUntrustedAppInput:!0";
            Assert(CodexDesktopContract.Matches(declarations, implementation), "compatible contract rejected");
            Assert(!CodexDesktopContract.Matches(declarations.Replace(":11", ":12"), implementation), "unknown state protocol accepted");
            Assert(!CodexDesktopContract.Matches(declarations.Replace("start-turn\":2", "start-turn\":3"), implementation), "unknown dispatch protocol accepted");
            Assert(!CodexDesktopContract.Matches(declarations, implementation.Replace("inheritThreadSettings:", "other:")), "missing inheritance accepted");
            Assert(!CodexDesktopContract.Matches(declarations + ",\"thread-follower-start-turn\":3", implementation), "conflicting protocol accepted");
        }

        internal static void IsolatedDispatchUsesOriginalOwnerAndCancel()
        {
            RunIsolatedPipe(true);
            RunIsolatedPipe(false);
            RunIsolatedPipe(true, 12, true);
            RunIsolatedPipe(true, 11, false);
            RunIsolatedPipe(true, 11, true, true);
        }
        internal static void ArchiveBoundsAreValidated()
        {
            string path = Path.Combine(Path.GetTempPath(), "quota-contract-" + Guid.NewGuid().ToString("N") + ".asar");
            try
            {
                string declarations = "\"thread-stream-state-changed\":11,\"thread-stream-following-changed\":1,\"thread-owner-discovery\":1,\"thread-follower-start-turn\":2,\"thread-archived\":2,\"thread-queued-followups-changed\":2";
                string implementation = "case`thread-follower-start-turn`: e.startTurn(t.params.conversationId,t.params.turnStart); inheritThreadSettings: true; supportsUntrustedAppInput:!0";
                byte[] first = Encoding.UTF8.GetBytes(declarations), second = Encoding.UTF8.GetBytes(implementation);
                object firstEntry = Obj("offset", "0", "size", first.Length);
                object tree = Obj("files", Obj(".vite", Obj("files", Obj("build", Obj("files", Obj(
                    "src-test.js", firstEntry, "bootstrap-test.js", Obj("offset", first.Length.ToString(), "size", second.Length)))))));
                for (int scenario = 0; scenario < 3; scenario++)
                {
                    if (scenario == 2) ResumeJson.Map(firstEntry)["offset"] = "-1";
                    byte[] header = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(tree));
                    using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
                    {
                        writer.Write((uint)4); writer.Write((uint)(header.Length + 8));
                        writer.Write((uint)(header.Length + 4)); writer.Write((uint)header.Length); writer.Write(header);
                        writer.Write(first); if (scenario != 1) writer.Write(second);
                    }
                    bool rejected = false;
                    try { CodexDesktopContract.ValidateArchive(path); } catch (IOException) { rejected = true; }
                    Assert(rejected == (scenario != 0), "truncated/out-of-bounds contract not rejected: " + scenario);
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static void RunIsolatedPipe(bool allow, int protocol = 11, bool capability = true, bool changeBeforeSend = false)
        {
            string name = "quota-resume-isolated-" + Guid.NewGuid().ToString("N");
            int dispatches = 0;
            Exception serverError = null;
            using (NamedPipeServerStream server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                Thread serverThread = new Thread(delegate()
                {
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    try
                    {
                        server.WaitForConnection();
                        while (true)
                        {
                            int length = BitConverter.ToInt32(ReadBytes(server, 4), 0);
                            object request = json.DeserializeObject(Encoding.UTF8.GetString(ReadBytes(server, length)));
                            string method = ResumeJson.Text(request, "method");
                            if (method == "thread-stream-following-changed")
                            {
                                if (Object.Equals(ResumeJson.Get(ResumeJson.Get(request, "params"), "following"), true))
                                    WriteFrame(server, Obj("type", "broadcast", "method", "thread-stream-state-changed", "sourceClientId", "test-owner", "version", protocol,
                                        "params", Obj("conversationId", Id, "hostId", "local", "change", Obj("type", "snapshot", "conversationState", State()))));
                                if (changeBeforeSend && Object.Equals(ResumeJson.Get(ResumeJson.Get(request, "params"), "following"), true))
                                    WriteFrame(server, Obj("type", "broadcast", "method", "thread-queued-followups-changed", "version", 2,
                                        "params", Obj("conversationId", Id)));
                                continue;
                            }
                            object result;
                            if (method == "initialize") result = Obj("clientId", "test-follower");
                            else if (method == "thread-owner-discovery") result = Obj("supportsUntrustedAppInput", capability);
                            else if (method == "thread-follower-start-turn")
                            {
                                Assert(ResumeJson.Text(request, "targetClientId") == "test-owner", "dispatch not addressed to owner");
                                object start = ResumeJson.Get(ResumeJson.Get(request, "params"), "turnStart");
                                Assert(ResumeJson.Text(ResumeJson.Get(start, "request"), "threadId") == Id, "dispatch changed conversation");
                                Assert(Object.Equals(ResumeJson.Get(ResumeJson.Get(start, "context"), "inheritThreadSettings"), true), "settings inheritance absent");
                                Assert(ResumeJson.Text(ResumeJson.Get(start, "request"), "clientUserMessageId") != null, "dedup message id absent");
                                dispatches++;
                                result = Obj("result", Obj("turn", Obj("id", "new-turn")));
                            }
                            else throw new Exception("unexpected write method: " + method);
                            WriteFrame(server, Obj("type", "response", "requestId", ResumeJson.Get(request, "requestId"), "method", method,
                                "resultType", "success", "handledByClientId", "test-owner", "result", result));
                        }
                    }
                    catch (EndOfStreamException) { }
                    catch (IOException ex) { if (protocol == 11 && capability && !changeBeforeSend) serverError = ex; }
                    catch (Exception ex) { serverError = ex; }
                });
                serverThread.IsBackground = true;
                serverThread.Start();
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(3000);
                    using (CodexDesktopResumeClient desktop = new CodexDesktopResumeClient(pipe))
                    {
                        bool cancelled = false;
                        try {
                            ResumeDecision d = Assess(desktop.Snapshot(Id));
                            Assert(desktop.Resume(Id, d.Fingerprint, Guid.NewGuid().ToString(), delegate { return allow; }) == "new-turn", "turn not confirmed");
                        }
                        catch (IOException) { cancelled = true; }
                        Assert(cancelled == (!allow || protocol != 11 || !capability || changeBeforeSend), "compatibility/cancellation guard ignored");
                    }
                }
                Assert(serverThread.Join(3000), "isolated server did not exit");
                if (serverError != null) throw serverError;
                Assert(dispatches == (allow && protocol == 11 && capability && !changeBeforeSend ? 1 : 0), "incorrect dispatch count");
            }
        }
        private static byte[] ReadBytes(Stream stream, int length)
        {
            byte[] bytes = new byte[length];
            int offset = 0;
            while (offset < length) { int count = stream.Read(bytes, offset, length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
            return bytes;
        }
        private static void WriteFrame(Stream stream, object value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
            byte[] header = BitConverter.GetBytes(bytes.Length);
            stream.Write(header, 0, 4); stream.Write(bytes, 0, bytes.Length); stream.Flush();
        }

        internal static int LiveReadOnly()
        {
            using (CodexAppServerClient reader = new CodexAppServerClient())
            {
                object page = reader.ReadOnlyRequest("thread/list", Obj("limit", 10, "archived", false,
                    "sortKey", "updated_at", "modelProviders", new object[0], "sourceKinds", new[] { "appServer", "cli", "exec", "vscode", "unknown" }));
                IList rows = ResumeJson.List(ResumeJson.Get(page, "data"));
                if (rows == null) { Console.WriteLine("ReadOnlyList=failed"); return 1; }
                Console.WriteLine("ReadOnlyListCount=" + rows.Count);
                int checkedTurns = 0;
                int errors = 0;
                int liveSnapshots = 0;
                foreach (object row in rows)
                {
                    string id = ResumeJson.Text(row, "id");
                    object turnsPage = reader.ReadOnlyRequest("thread/turns/list", Obj("threadId", id, "limit", 1,
                        "sortDirection", "desc", "itemsView", "notLoaded"));
                    IList turns = ResumeJson.List(ResumeJson.Get(turnsPage, "data"));
                    if (turns == null) continue;
                    checkedTurns++;
                    if (turns.Count > 0 && AutoResumePolicy.IsQuotaFailure(turns[0])) errors++;
                    if (liveSnapshots == 0)
                    {
                        try
                        {
                            using (CodexDesktopResumeClient desktop = new CodexDesktopResumeClient())
                            {
                                object state = desktop.Snapshot(id);
                                Console.WriteLine("LiveRuntime=" + ResumeJson.Text(ResumeJson.Get(state, "threadRuntimeStatus"), "type"));
                                Console.WriteLine("LiveDecision=" + AutoResumePolicy.AssessDesktop(state, id).Reason);
                                Console.WriteLine("GoalConfirmationPresent=" + (ResumeJson.Get(state, "threadGoalResumeConfirmation") != null));
                                Console.WriteLine("LiveTurnStatus=" + ResumeJson.Text(AutoResumePolicy.LatestDesktopTurn(state), "status"));
                                liveSnapshots++;
                            }
                        }
                        catch (Exception ex) { Console.WriteLine("LiveReadOnlyError=" + ex.GetType().Name + ": " + ex.Message); }
                    }
                }
                Console.WriteLine("ReadOnlyLatestTurns=" + checkedTurns);
                Console.WriteLine("StructuredQuotaFailures=" + errors);
                Console.WriteLine("LiveSnapshots=" + liveSnapshots);
                object liveQuota = reader.ReadOnlyRequest("account/rateLimits/read", null);
                Console.WriteLine("LiveQuotaRead=" + (liveQuota != null));
                Console.WriteLine("MessagesSent=0");
                return checkedTurns > 0 && liveSnapshots > 0 ? 0 : 1;
            }
        }
        internal static int LiveScanReadOnly()
        {
            using (AutoResumeService monitor = new AutoResumeService(true))
            {
                monitor.RequestScan();
                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                while (monitor.Scanning && DateTime.UtcNow < deadline) Thread.Sleep(100);
                Console.WriteLine("ScanStatus=" + monitor.Message);
                Console.WriteLine("QuotaInterruptedCount=" + monitor.Snapshot().Count);
                Console.WriteLine("AutomaticDispatchEnabled=" + monitor.Enabled);
                Console.WriteLine("MessagesSent=0");
                return !monitor.Scanning && monitor.Message.StartsWith("已检查 ", StringComparison.Ordinal) ? 0 : 1;
            }
        }
    }
}
