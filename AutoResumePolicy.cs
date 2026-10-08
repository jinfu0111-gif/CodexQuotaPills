using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    // Windows adaptation of progressrdx/codex-auto-resume's MIT policy.
    // No transcript text matching: only structured terminal usage-limit errors.
    internal static class ResumeJson
    {
        internal static IDictionary<string, object> Map(object value) { return value as IDictionary<string, object>; }
        internal static object Get(object value, string key)
        {
            IDictionary<string, object> map = Map(value);
            object result;
            return map != null && map.TryGetValue(key, out result) ? result : null;
        }
        internal static string Text(object value, string key) { return Get(value, key) as string; }
        internal static IList List(object value) { return value as IList; }
        internal static Dictionary<string, object> Obj(params object[] pairs)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result[(string)pairs[i]] = pairs[i + 1];
            return result;
        }
        internal static double? Number(object value)
        {
            double n;
            if (value == null || value is bool || !Double.TryParse(Convert.ToString(value,
                CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ||
                Double.IsNaN(n) || Double.IsInfinity(n)) return null;
            return n;
        }
        internal static string Hash(object value)
        {
            string serialized = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.Serialize(Canonical(value));
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(serialized))).Replace("-", "").ToLowerInvariant();
        }
        private static object Canonical(object value)
        {
            IDictionary<string, object> map = Map(value);
            if (map != null)
            {
                SortedDictionary<string, object> result = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> pair in map) result[pair.Key] = Canonical(pair.Value);
                return result;
            }
            IList list = List(value);
            if (list == null) return value;
            List<object> values = new List<object>();
            foreach (object item in list) values.Add(Canonical(item));
            return values;
        }
    }

    internal sealed class ResumeDecision
    {
        internal bool Eligible;
        internal string TurnId;
        internal string Reason;
        internal string Fingerprint;
    }

    internal sealed class ResumeQuota
    {
        internal bool Ready;
        internal long RetryAt;
        internal string Reason;
    }

    internal static class AutoResumePolicy
    {
        internal const string Continuation = "额度恢复后，请继续本会话原有的未完成工作。先核对当前进度与工作区，不要重复已完成步骤，不扩大范围，不绕过审批。若工作已经完成或需要用户决定，请明确说明并停止。";

        internal static bool IsQuotaFailure(object turn)
        {
            string info = ResumeJson.Text(ResumeJson.Get(turn, "error"), "codexErrorInfo");
            return ResumeJson.Text(turn, "status") == "failed" &&
                (info == "usageLimitExceeded" || info == "UsageLimitExceeded");
        }

        internal static object LatestDesktopTurn(object state)
        {
            object history = ResumeJson.Get(state, "turnHistory");
            if (ResumeJson.Text(history, "kind") == "canonical")
            {
                object body = ResumeJson.Get(history, "history");
                IList islands = ResumeJson.List(ResumeJson.Get(body, "islands"));
                if (islands == null || islands.Count == 0) return null;
                object tail = islands[islands.Count - 1];
                if (ResumeJson.Text(ResumeJson.Get(tail, "newerBoundary"), "status") != "exhausted") return null;
                IList entries = ResumeJson.List(ResumeJson.Get(tail, "entries"));
                if (entries == null || entries.Count == 0) return null;
                string key = ResumeJson.Text(entries[entries.Count - 1], "value");
                return key == null ? null : ResumeJson.Get(ResumeJson.Get(body, "entitiesByKey"), key);
            }
            IList turns = ResumeJson.List(ResumeJson.Get(state, "turns"));
            return turns == null || turns.Count == 0 ? null : turns[turns.Count - 1];
        }

        internal static ResumeDecision AssessDesktop(object state, string expectedId)
        {
            ResumeDecision d = new ResumeDecision { Reason = "状态不完整，不自动续跑" };
            if (ResumeJson.Text(state, "id") != expectedId || ResumeJson.Text(state, "hostId") != "local" ||
                !Object.Equals(ResumeJson.Get(state, "ephemeral"), false) && ResumeJson.Get(state, "ephemeral") != null)
                return d;
            // The validated Windows desktop snapshot omits ephemeral; persisted
            // thread metadata is checked separately before enrollment.
            if (ResumeJson.Text(state, "resumeState") != "resumed") { d.Reason = "等待原对话加载"; return d; }
            IList requests = ResumeJson.List(ResumeJson.Get(state, "requests"));
            if (requests == null || requests.Count != 0 || ResumeJson.Get(state, "threadGoalResumeConfirmation") != null)
            { d.Reason = "等待审批或用户确认，不自动续跑"; return d; }
            IList queued = ResumeJson.List(ResumeJson.Get(state, "queuedFollowUps"));
            if (queued != null && queued.Count != 0) { d.Reason = "已有待发送消息，不自动插队"; return d; }
            object goal = ResumeJson.Get(state, "threadGoal");
            if (goal != null && ResumeJson.Text(goal, "status") != "active")
            { d.Reason = "目标已暂停或结束，不自动续跑"; return d; }
            string runtime = ResumeJson.Text(ResumeJson.Get(state, "threadRuntimeStatus"), "type");
            if (runtime != "idle" && runtime != "systemError") { d.Reason = "正在执行或状态未同步"; return d; }
            object turn = LatestDesktopTurn(state);
            d.TurnId = ResumeJson.Text(turn, "turnId");
            if (String.IsNullOrWhiteSpace(d.TurnId) || !IsQuotaFailure(turn))
            { d.Reason = "最新一轮不是额度中断"; return d; }
            // Reject subagents and non-local roots, even if their errors match.
            if (ResumeJson.Get(state, "parentThreadId") != null) { d.Reason = "子任务不单独续跑"; return d; }
            if (ResumeJson.Get(state, "currentPermissions") == null || ResumeJson.Text(state, "cwd") == null ||
                ResumeJson.Text(state, "latestModel") == null) return d;
            Dictionary<string, object> fingerprint = new Dictionary<string, object>();
            foreach (string key in new[] { "id", "hostId", "cwd", "latestModel", "latestReasoningEffort",
                "latestCollaborationMode", "currentPermissions", "latestThreadSettings", "threadGoal",
                "threadGoalResumeConfirmation", "requests", "queuedFollowUps", "threadRuntimeStatus", "resumeState" })
                fingerprint[key] = ResumeJson.Get(state, key);
            fingerprint["turn"] = turn;
            d.Fingerprint = ResumeJson.Hash(fingerprint);
            d.Eligible = true;
            d.Reason = "因额度耗尽中断";
            return d;
        }

        internal static ResumeQuota Quota(object response, long now)
        {
            ResumeQuota q = new ResumeQuota { RetryAt = now + 60, Reason = "额度数据不完整，等待重查" };
            object bucket = ResumeJson.Get(response, "rateLimits");
            IDictionary<string, object> buckets = ResumeJson.Map(ResumeJson.Get(response, "rateLimitsByLimitId"));
            if (buckets != null && buckets.Count > 0)
            {
                bucket = ResumeJson.Get(buckets, "codex");
                if (bucket == null) { q.Reason = "未找到 Codex 额度桶"; return q; }
            }
            if (bucket == null || Object.Equals(ResumeJson.Get(bucket, "spendControlReached"), true) ||
                ResumeJson.Get(bucket, "individualLimit") != null) return q;
            string reached = ResumeJson.Text(bucket, "rateLimitReachedType");
            if (reached != null && reached != "none" && reached != "rate_limit_reached")
            { q.Reason = "账户存在额外限制，需要人工检查"; return q; }
            object primary = ResumeJson.Get(bucket, "primary");
            if (primary == null) return q;
            bool exhausted = false;
            foreach (object window in new[] { primary, ResumeJson.Get(bucket, "secondary") })
            {
                if (window == null) continue;
                double? used = ResumeJson.Number(ResumeJson.Get(window, "usedPercent"));
                double? reset = ResumeJson.Number(ResumeJson.Get(window, "resetsAt"));
                double? duration = ResumeJson.Number(ResumeJson.Get(window, "windowDurationMins"));
                if (!used.HasValue || used < 0 || used > 100 || !reset.HasValue || reset <= 0 ||
                    !duration.HasValue || duration <= 0) return q;
                if (used >= 100)
                {
                    exhausted = true;
                    q.RetryAt = Math.Max(q.RetryAt, (long)reset.Value + 60);
                }
                else if (reset <= now) { q.Reason = "额度快照已过期，等待实时刷新"; return q; }
            }
            if (exhausted) { q.Reason = "等待 5h / 周额度恢复"; return q; }
            if (reached == "rate_limit_reached") { q.Reason = "服务仍标记额度受限"; return q; }
            q.Ready = true;
            q.RetryAt = now;
            q.Reason = "额度已恢复";
            return q;
        }
    }
}
