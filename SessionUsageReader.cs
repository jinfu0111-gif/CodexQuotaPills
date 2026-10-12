using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal sealed class SessionUsageEntry
    {
        internal string Id, Title, RolloutPath;
        internal long? Total;
        public override string ToString() { return Title; }
    }

    internal sealed class SessionUsageSnapshot
    {
        internal long? Total, Input, CachedInput, Output, Context, Window;
        internal DateTime RequestUtc;
        internal int ObservedCompactions;
        internal bool PartialHistory, AwaitingAfterCompaction, HistoryRead;
        internal string Status = "尚无可验证的请求用量";
        internal string CacheRatio
        {
            get { return Input.HasValue && Input > 0 && CachedInput.HasValue
                ? (CachedInput.Value * 100d / Input.Value).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : "—"; }
        }
        internal string ContextRatio
        {
            get { return Context.HasValue && Window.HasValue && Window > 0
                ? (Context.Value * 100d / Window.Value).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : "—"; }
        }
    }

    // Original native reader; no Node/Python dependency, no page injection.
    // Metadata SELECTs deliberately exclude conversation/message contents.
    internal sealed class SessionUsageReader
    {
        internal const int TailLimit = 8 * 1024 * 1024;
        private readonly string home;
        internal SessionUsageReader(string codexHome)
        {
            home = NormalizePath(codexHome);
        }
        internal static SessionUsageReader ForUser()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return new SessionUsageReader(String.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : configured);
        }

        internal List<SessionUsageEntry> List(out string error)
        {
            error = String.Empty;
            List<SessionUsageEntry> entries = new List<SessionUsageEntry>();
            try
            {
                string path = LatestDatabase(home);
                if (path == null) { error = "未找到本机会话数据库"; return entries; }
                using (MetadataDb db = new MetadataDb(path))
                    db.Query("SELECT id,title,rollout_path,tokens_used,source FROM threads WHERE archived=0 ORDER BY updated_at DESC LIMIT 5000",
                        null, delegate(IntPtr row) {
                            if (!IsUserConversationSource(MetadataDb.Text(row, 4))) return;
                            Guid id; string text = MetadataDb.Text(row, 0);
                            if (!Guid.TryParse(text, out id)) return;
                            string title = MetadataDb.Text(row, 1);
                            title = Regex.Replace(title ?? String.Empty, @"[\p{C}]", " ");
                            if (title.Length > 90) title = title.Substring(0, 90) + "…";
                            entries.Add(new SessionUsageEntry { Id = id.ToString(),
                                Title = String.IsNullOrWhiteSpace(title) ? "未命名会话 · " + text.Substring(0, 8) : title,
                                RolloutPath = MetadataDb.Text(row, 2), Total = MetadataDb.Number(row, 3) });
                        }, delegate { return entries.Count >= 100; });
            }
            catch { error = "会话数据库暂不可读或格式不兼容，请稍后刷新"; }
            return entries;
        }

        internal SessionUsageSnapshot Read(SessionUsageEntry selected)
        {
            SessionUsageSnapshot result = new SessionUsageSnapshot();
            try
            {
                Guid id;
                if (selected == null || !Guid.TryParse(selected.Id, out id)) return result;
                string path = LatestDatabase(home);
                if (path == null) { result.Status = "未找到本机会话数据库"; return result; }
                string rollout = null;
                // Re-read selected metadata; never substitute the most recent conversation.
                using (MetadataDb db = new MetadataDb(path))
                    db.Query("SELECT rollout_path,tokens_used,source FROM threads WHERE id=? AND archived=0 LIMIT 1",
                        id.ToString(), delegate(IntPtr row) {
                            if (!IsUserConversationSource(MetadataDb.Text(row, 2)))
                            { result.Status = "内部审核或子任务不在会话用量列表中"; return; }
                            rollout = MetadataDb.Text(row, 0); result.Total = MetadataDb.Number(row, 1);
                        });
                if (rollout == null) { if (!result.Status.Contains("内部")) result.Status = "所选会话已归档或不可读"; return result; }
                ReadRollout(rollout, id.ToString(), result);
            }
            catch { ClearRequest(result); result.Status = "读取失败，未沿用旧请求数据"; }
            return result;
        }

        internal static string LatestDatabase(string directory)
        {
            string latest = null; long highest = -1;
            foreach (string file in Directory.GetFiles(directory, "state_*.sqlite"))
            {
                Match match = Regex.Match(Path.GetFileName(file), @"^state_(\d+)\.sqlite$");
                long number;
                if (match.Success && Int64.TryParse(match.Groups[1].Value, out number) && number > highest)
                { highest = number; latest = file; }
            }
            // A newer incompatible schema must fail closed, not read stale older DBs.
            return latest;
        }

        internal static bool IsUserConversationSource(string source)
        {
            if (String.IsNullOrWhiteSpace(source)) return false;
            string text = source.Trim();
            if (text.StartsWith("subagent", StringComparison.OrdinalIgnoreCase)) return false;
            if (!text.StartsWith("{", StringComparison.Ordinal)) return true;
            try
            {
                IDictionary<string, object> fields = new JavaScriptSerializer { MaxJsonLength = 32768 }
                    .DeserializeObject(text) as IDictionary<string, object>;
                return fields != null && !fields.ContainsKey("subagent");
            }
            catch { return false; }
        }

        private void ReadRollout(string file, string id, SessionUsageSnapshot result)
        {
            string path = NormalizePath(file);
            string sessions = Path.Combine(home, "sessions") + Path.DirectorySeparatorChar;
            string archived = Path.Combine(home, "archived_sessions") + Path.DirectorySeparatorChar;
            if (!path.StartsWith(sessions, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(archived, StringComparison.OrdinalIgnoreCase))
            { result.Status = "会话记录路径不在 Codex 数据目录内"; return; }
            for (string cursor = path; cursor != null && cursor.Length >= home.Length; cursor = Path.GetDirectoryName(cursor))
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                { result.Status = "不读取重定向的会话记录路径"; return; }
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] prefix = new byte[512]; int count = fileStream.Read(prefix, 0, prefix.Length);
                string head = Encoding.UTF8.GetString(prefix, 0, count);
                Match identity = Regex.Match(head, @"^\s*\{\s*""timestamp""\s*:\s*""[^""]*""(?:\s*,\s*""ordinal""\s*:\s*\d+)?\s*,\s*""type""\s*:\s*""session_meta""\s*,\s*""payload""\s*:\s*\{[^{}]*?""id""\s*:\s*""([^""]+)""");
                if (!identity.Success || !String.Equals(identity.Groups[1].Value, id, StringComparison.OrdinalIgnoreCase))
                { result.Status = "会话记录身份无法验证"; return; }
                long length = fileStream.Length;
                long start = Math.Max(0, length - TailLimit);
                result.PartialHistory = start > 0;
                fileStream.Position = start;
                byte[] tail = new byte[(int)(length - start)]; int received = 0;
                while (received < tail.Length)
                { int n = fileStream.Read(tail, received, tail.Length - received); if (n == 0) break; received += n; }
                // Skip partial first/last lines and oversized records. Non-metric
                // records are never JSON-deserialized or retained by the product.
                int lineStart = 0;
                if (start > 0) { while (lineStart < received && tail[lineStart] != 10) lineStart++; lineStart++; }
                for (int index = lineStart; index < received; index++)
                {
                    if (tail[index] != 10) continue;
                    int size = index - lineStart;
                    if (size > 0)
                    {
                        string lineHead = Encoding.UTF8.GetString(tail, lineStart, Math.Min(size, 256));
                        if (IsMetricPrefix(lineHead))
                        {
                            if (IsCompactionPrefix(lineHead)) ApplyMetricLine(result, lineHead);
                            else if (size <= 256 * 1024) ApplyMetricLine(result, Encoding.UTF8.GetString(tail, lineStart, size));
                            else { ClearRequest(result); result.Status = "请求记录超出读取上限"; }
                        }
                    }
                    lineStart = index + 1;
                }
                result.HistoryRead = true;
            }
        }

        private static bool IsMetricPrefix(string prefix)
        {
            return IsCompactionPrefix(prefix) ||
                Regex.IsMatch(prefix, @"^\s*\{\s*""timestamp""\s*:\s*""[^""]*""(?:\s*,\s*""ordinal""\s*:\s*\d+)?\s*,\s*""type""\s*:\s*""event_msg""\s*,\s*""payload""\s*:\s*\{\s*""type""\s*:\s*""token_count""");
        }

        internal static string NormalizePath(string path)
        {
            // Rust's canonical Windows local paths may use the extended prefix.
            // Normalize only ordinary drive paths; never convert devices or UNC
            // destinations into trusted local session paths.
            if (path != null && path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                string local = path.Substring(4);
                if (!Regex.IsMatch(local, @"^[A-Za-z]:\\")) throw new IOException("Unsupported extended path");
                path = local;
            }
            return Path.GetFullPath(path);
        }
        private static bool IsCompactionPrefix(string prefix)
        {
            return Regex.IsMatch(prefix, @"^\s*\{\s*""timestamp""\s*:\s*""[^""]*""(?:\s*,\s*""ordinal""\s*:\s*\d+)?\s*,\s*""type""\s*:\s*""compacted""\s*[,}]");
        }
        internal static void ApplyMetricLine(SessionUsageSnapshot result, string line)
        {
            if (!IsMetricPrefix(line)) return;
            if (IsCompactionPrefix(line))
            {
                result.ObservedCompactions++;
                ClearRequest(result);
                result.AwaitingAfterCompaction = true;
                result.Status = "已观察到压缩，等待下一次请求用量";
                return; // Compaction summaries are deliberately not parsed.
            }
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 };
                IDictionary<string, object> root = json.DeserializeObject(line) as IDictionary<string, object>;
                IDictionary<string, object> payload = Object(root, "payload"), info = Object(payload, "info");
                if (info == null) return; // A null info heartbeat is not a new measurement.
                IDictionary<string, object> last = Object(info, "last_token_usage");
                long input, cached, output, total, window;
                bool hasWindow = Number(info, "model_context_window", out window) && window > 0;
                DateTime timestamp;
                if (!Number(last, "input_tokens", out input) || !Number(last, "cached_input_tokens", out cached) ||
                    !Number(last, "output_tokens", out output) || !Number(last, "total_tokens", out total) ||
                    cached > input ||
                    input > Int64.MaxValue - output || total != input + output ||
                    !DateTime.TryParse(Convert.ToString(root["timestamp"], CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out timestamp))
                { ClearRequest(result); result.Status = "请求字段无法验证"; return; }
                result.Input = input; result.CachedInput = cached; result.Output = output;
                result.Context = total; result.Window = hasWindow ? (long?)window : null; result.RequestUtc = timestamp;
                result.AwaitingAfterCompaction = false;
                result.Status = hasWindow ? "最近一次模型请求（非实时输入框）" : "最近请求已读取，上下文窗口待确认";
            }
            catch { ClearRequest(result); result.Status = "请求字段无法验证"; }
        }
        private static void ClearRequest(SessionUsageSnapshot result)
        { result.Input = result.CachedInput = result.Output = result.Context = result.Window = null; result.RequestUtc = DateTime.MinValue; }
        private static IDictionary<string, object> Object(IDictionary<string, object> source, string key)
        { object value; return source != null && source.TryGetValue(key, out value) ? value as IDictionary<string, object> : null; }
        private static bool Number(IDictionary<string, object> source, string key, out long number)
        {
            object value; number = 0;
            return source != null && source.TryGetValue(key, out value) && value != null &&
                Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out number) && number >= 0;
        }
    }

    internal sealed class MetadataDb : IDisposable
    {
        private IntPtr db;
        internal MetadataDb(string path)
        {
            int result = sqlite3_open_v2(Utf8(path), out db, 1, IntPtr.Zero); // SQLITE_OPEN_READONLY
            if (result != 0) { Dispose(); throw new IOException("Read-only database unavailable"); }
            sqlite3_busy_timeout(db, 200);
        }
        internal void Query(string sql, string parameter, Action<IntPtr> row, Func<bool> stop = null)
        {
            IntPtr statement = IntPtr.Zero;
            try
            {
                if (sqlite3_prepare_v2(db, Utf8(sql), -1, out statement, IntPtr.Zero) != 0)
                    throw new IOException("Unsupported metadata schema");
                if (parameter != null && sqlite3_bind_text(statement, 1, Utf8(parameter), -1, new IntPtr(-1)) != 0)
                    throw new IOException("Invalid metadata parameter");
                int result;
                while ((result = sqlite3_step(statement)) == 100)
                {
                    row(statement);
                    if (stop != null && stop()) return;
                }
                if (result != 101) throw new IOException("Metadata read failed");
            }
            finally { if (statement != IntPtr.Zero) sqlite3_finalize(statement); }
        }
        internal static string Text(IntPtr row, int column)
        {
            IntPtr ptr = sqlite3_column_text(row, column); int length = sqlite3_column_bytes(row, column);
            if (ptr == IntPtr.Zero || length < 0 || length > 32768) return null;
            byte[] bytes = new byte[length]; Marshal.Copy(ptr, bytes, 0, length); return Encoding.UTF8.GetString(bytes);
        }
        internal static long? Number(IntPtr row, int column)
        {
            if (sqlite3_column_type(row, column) != 1) return null;
            long value = sqlite3_column_int64(row, column); return value < 0 ? (long?)null : value;
        }
        private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }
        public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close(db); db = IntPtr.Zero; } }
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr stmt, IntPtr tail);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int length, IntPtr destructor);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr stmt, int column);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr stmt, int column);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr stmt, int column);
    }
}
