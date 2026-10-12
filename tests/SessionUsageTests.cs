using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CodexUsageOverlay;

internal static class SessionUsageTests
{
    internal static void AccountZeroNeedsConfirmation()
    {
        DateTime now = DateTime.UtcNow;
        UsageData data = new UsageData();
        AccountTokenPolicy.Apply(data, Incoming("a", 100, now));
        AccountTokenPolicy.Apply(data, Incoming("a", 0, now.AddSeconds(15)));
        Check(data.ProfileTokensText == "待确认" && data.LifetimeTokens == 100, "transient zero displayed");
        AccountTokenPolicy.Apply(data, Incoming("a", 105, now.AddSeconds(30)));
        Check(data.ProfileTokensText == "105", "positive observation not recovered");
        AccountTokenPolicy.Apply(data, Incoming("a", 0, now.AddSeconds(45)));
        AccountTokenPolicy.Apply(data, Incoming("a", 0, now.AddSeconds(46)));
        Check(data.ProfileTokensText == "待确认", "zero confirmed without separated poll");
        AccountTokenPolicy.Apply(data, Incoming("a", 0, now.AddSeconds(60)));
        Check(data.ProfileTokensText == "0" && data.LifetimeTokens == 0, "genuine reset never confirms");
        AccountTokenPolicy.Apply(data, Incoming("a", 200, now.AddSeconds(75)));
        AccountTokenPolicy.Apply(data, Incoming("b", 0, now.AddSeconds(90)));
        Check(data.ProfileTokensText == "待确认" && !data.LifetimeTokens.HasValue, "old account value carried to new account");
        AccountTokenPolicy.Apply(data, Incoming("b", 0, now.AddSeconds(105)));
        Check(data.ProfileTokensText == "0", "new account genuine zero not confirmed");
        AccountTokenPolicy.Apply(data, Incoming("b", null, now.AddSeconds(110)));
        Check(data.ProfileTokensText == "待确认", "missing usage treated as real zero");
        AccountTokenPolicy.Apply(data, Incoming(null, 0, now.AddSeconds(120)));
        AccountTokenPolicy.Apply(data, Incoming(null, 0, now.AddSeconds(135)));
        Check(data.ProfileTokensText == "待确认", "unbound zero confirmed");
        Check(AccountTokenPolicy.Identity("private@example.invalid").Length == 64, "identity was not hashed");
        AccountTokenPolicy.MarkUnavailable(data);
        Check(data.ProfileTokensText == "待确认", "failed read displayed as live");
    }
    private static UsageData Incoming(string identity, long? tokens, DateTime time)
    { return new UsageData { AccountTokensChecked = true, AccountTokenIdentity = identity ?? "", LifetimeTokens = tokens, UpdatedUtc = time }; }

    private static string Metric(string input, string cached, string output, string total, string window)
    {
        return "{\"timestamp\":\"2026-10-09T08:30:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":" + input +
            ",\"cached_input_tokens\":" + cached + ",\"output_tokens\":" + output + ",\"reasoning_output_tokens\":4,\"total_tokens\":" + total + "},\"model_context_window\":" + window + "}}}";
    }
    internal static void CacheAndContextAreDifferent()
    {
        Check(ComposerUsagePillLayout.OpensSessionUsage(OverlayPlacement.ComposerBottom, ComposerUsagePillKind.Tokens), "Token card has no entry");
        Check(!ComposerUsagePillLayout.OpensSessionUsage(OverlayPlacement.Sidebar, ComposerUsagePillKind.WeeklyQuota), "sidebar hover was replaced by click");
        Check(!ComposerUsagePillLayout.OpensSessionUsage(OverlayPlacement.TitleBar, ComposerUsagePillKind.ShortQuota), "5h menu entry replaced");
        SessionUsageSnapshot data = new SessionUsageSnapshot { Total = 10000 };
        string valid = Metric("100", "80", "10", "110", "200");
        SessionUsageReader.ApplyMetricLine(data, valid);
        Check(data.CacheRatio == "80%" && data.ContextRatio == "55%", "cache and context conflated");
        Check(data.Total == 10000 && data.Context == 110 && data.Output == 10, "reasoning double counted or cumulative used as context");
        SessionUsageReader.ApplyMetricLine(data, "{\"timestamp\":\"x\",\"type\":\"response_item\",\"payload\":{\"type\":\"token_count\",\"info\":{}}}");
        Check(data.Context == 110, "non-metric body was read");
        SessionUsageReader.ApplyMetricLine(data, Metric("100", "101", "10", "110", "200"));
        Check(!data.Input.HasValue && data.Status.Contains("无法验证"), "invalid cache carried old live data");
        SessionUsageReader.ApplyMetricLine(data, Metric("-1", "0", "10", "9", "200"));
        Check(!data.Input.HasValue, "negative tokens accepted");
        SessionUsageReader.ApplyMetricLine(data, Metric("0", "0", "0", "0", "null"));
        Check(data.Input == 0 && data.CacheRatio == "—" && data.ContextRatio == "—", "zero denominator or missing window invented");
        SessionUsageReader.ApplyMetricLine(data, valid);
        SessionUsageReader.ApplyMetricLine(data, "{\"timestamp\":\"2026-10-09T08:31:00Z\",\"type\":\"compacted\",\"payload\":{\"message\":\"not parsed\"}}");
        Check(data.ObservedCompactions == 1 && data.AwaitingAfterCompaction && !data.Context.HasValue, "pre-compaction context retained");
        SessionUsageReader.ApplyMetricLine(data, valid);
        Check(!data.AwaitingAfterCompaction && data.Context == 110, "fresh post-compaction data not accepted");
        SessionUsageReader.ApplyMetricLine(data, valid.Replace("\"type\":\"event_msg\"", "\"ordinal\":123,\"type\":\"event_msg\""));
        Check(data.Input == 100, "desktop ordinal metadata not supported");
    }

    internal static void SqliteIsReadOnlyAndSessionBound()
    {
        string directory = Path.Combine(Path.GetTempPath(), "quota-session-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string db = Path.Combine(directory, "state_5.sqlite");
            string id = Guid.NewGuid().ToString(), other = Guid.NewGuid().ToString();
            string sessions = Path.Combine(directory, "sessions"); Directory.CreateDirectory(sessions);
            string rollout = Path.Combine(sessions, "roll.jsonl");
            string meta = "{\"timestamp\":\"2026-10-09T08:00:00Z\",\"ordinal\":1,\"type\":\"session_meta\",\"payload\":{\"creator_user_id\":\"fixture-only\",\"id\":\"" + id + "\"}}\n";
            string metric = Metric("100", "80", "10", "110", "200");
            File.WriteAllText(rollout, meta + metric + "\n" + Metric("200", "80", "10", "210", "200"), new UTF8Encoding(false));
            CreateDb(db, "CREATE TABLE threads(id TEXT,title TEXT,rollout_path TEXT,tokens_used INTEGER,archived INTEGER,updated_at INTEGER,source TEXT); INSERT INTO threads VALUES('" +
                id + "','测试会话','" + rollout.Replace("'", "''") + "',10000,0,1,'vscode');");
            SessionUsageReader reader = new SessionUsageReader(directory);
            string error; List<SessionUsageEntry> list = reader.List(out error);
            Check(error == "" && list.Count == 1 && list[0].Title == "测试会话", "unicode metadata read failed");
            SessionUsageSnapshot data = reader.Read(list[0]);
            Check(data.Total == 10000 && data.Input == 100 && data.HistoryRead, "partial last line read or thread total lost");
            CreateDb(db, "UPDATE threads SET rollout_path='" + (@"\\?\" + rollout).Replace("'", "''") + "';");
            Check(reader.Read(list[0]).Input == 100, "canonical Windows extended session path rejected");
            CreateDb(db, "UPDATE threads SET rollout_path='" + rollout.Replace("'", "''") + "';");
            File.WriteAllText(rollout, meta + new string('x', SessionUsageReader.TailLimit + 1) + "\n" + metric + "\n", new UTF8Encoding(false));
            data = reader.Read(list[0]);
            Check(data.PartialHistory && data.HistoryRead && data.Input == 100, "bounded tail lost metrics or claims complete history");
            File.WriteAllText(rollout, meta.Replace(id, other) + metric + "\n", new UTF8Encoding(false));
            data = reader.Read(list[0]);
            Check(!data.Input.HasValue && !data.HistoryRead, "mismatched session used or invented zero compactions");
            string outside = Path.Combine(directory, "outside.jsonl"); File.WriteAllText(outside, meta + metric + "\n");
            CreateDb(db, "UPDATE threads SET rollout_path='" + outside.Replace("'", "''") + "';");
            data = reader.Read(list[0]);
            Check(!data.Input.HasValue && data.Status.Contains("路径"), "out-of-sessions path accepted");
            bool rejected = false;
            using (MetadataDb connection = new MetadataDb(db))
                try { connection.Query("DELETE FROM threads", null, delegate(IntPtr row) { }); }
                catch (IOException) { rejected = true; }
            Check(rejected, "database opened writable");
            CreateDb(Path.Combine(directory, "state_6.sqlite"), "CREATE TABLE incompatible(x INTEGER);");
            Check(reader.List(out error).Count == 0 && error.Length > 0, "new schema fell back to stale old DB");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void CreateDb(string path, string sql)
    {
        IntPtr connection;
        if (sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out connection, 6, IntPtr.Zero) != 0) throw new IOException("fixture DB unavailable");
        try { if (sqlite3_exec(connection, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) throw new IOException("fixture SQL failed"); }
        finally { sqlite3_close(connection); }
    }
    internal static void InternalTasksDoNotFillSelectionSlots()
    {
        Check(!SessionUsageReader.IsUserConversationSource("{\"subagent\":{\"other\":\"guardian\"}}"), "guardian accepted");
        Check(!SessionUsageReader.IsUserConversationSource("{\"subagent\":{\"thread_spawn\":{}}}"), "worker subagent accepted");
        Check(!SessionUsageReader.IsUserConversationSource("subagent:guardian"), "plain subagent accepted");
        Check(!SessionUsageReader.IsUserConversationSource(null) && !SessionUsageReader.IsUserConversationSource("{broken"), "unknown source treated as verified root");
        Check(SessionUsageReader.IsUserConversationSource("vscode") && SessionUsageReader.IsUserConversationSource("cli"), "normal roots lost");
        string directory = Path.Combine(Path.GetTempPath(), "quota-root-filter-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string db = Path.Combine(directory, "state_5.sqlite");
            string root = Guid.NewGuid().ToString(), guardian = Guid.NewGuid().ToString();
            StringBuilder sql = new StringBuilder("CREATE TABLE threads(id TEXT,title TEXT,rollout_path TEXT,tokens_used INTEGER,archived INTEGER,updated_at INTEGER,source TEXT);");
            sql.Append("INSERT INTO threads VALUES('").Append(root).Append("','Guardian review','',50,0,1,'vscode');");
            for (int index = 0; index < 120; index++)
                sql.Append("INSERT INTO threads VALUES('").Append(index == 0 ? guardian : Guid.NewGuid().ToString())
                    .Append("','Guardian review','',10,0,").Append(500 + index)
                    .Append(",' {\"subagent\":{\"other\":\"guardian\"}}');");
            CreateDb(db, sql.ToString());
            SessionUsageReader reader = new SessionUsageReader(directory); string error;
            List<SessionUsageEntry> list = reader.List(out error);
            Check(error == "" && list.Count == 1 && list[0].Id == root && list[0].Title == "Guardian review", "title filter used or internal rows occupied 100 slots");
            SessionUsageSnapshot rejected = reader.Read(new SessionUsageEntry { Id = guardian });
            Check(rejected.Status.Contains("内部") && !rejected.Total.HasValue, "restored internal choice was read");
            long totalRows = 0;
            using (MetadataDb connection = new MetadataDb(db))
                connection.Query("SELECT COUNT(*) FROM threads", null, delegate(IntPtr row) { totalRows = MetadataDb.Number(row, 0).Value; });
            Check(totalRows == 121, "internal tasks deleted instead of view filtered");
        }
        finally { Directory.Delete(directory, true); }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static int LiveReadOnly()
    {
        SessionUsageReader reader = SessionUsageReader.ForUser(); string error;
        List<SessionUsageEntry> list = reader.List(out error);
        Console.WriteLine("MetadataRead=" + (error.Length == 0));
        Console.WriteLine("Entries=" + list.Count);
        Console.WriteLine("GuardianTitlesInSelectableList=" + list.FindAll(delegate(SessionUsageEntry item) { return item.Title == "Guardian review"; }).Count);
        if (list.Count > 0)
        {
            SessionUsageSnapshot snapshot = reader.Read(list[0]);
            Console.WriteLine("DiagnosticNewestRecordOnly=True; ActiveConversationIdentified=False");
            Console.WriteLine("ThreadTotalPresent=" + snapshot.Total.HasValue);
            Console.WriteLine("RecentRequestPresent=" + snapshot.Input.HasValue);
            Console.WriteLine("ContextWindowPresent=" + snapshot.Window.HasValue);
            Console.WriteLine("PartialHistory=" + snapshot.PartialHistory);
            Console.WriteLine("ReadStatus=" + snapshot.Status);
        }
        Console.WriteLine("MessagesSent=0; DatabaseWrites=0; ConversationTextOutput=0; UIShown=0");
        return error.Length == 0 && list.Count > 0 ? 0 : 1;
    }
}
