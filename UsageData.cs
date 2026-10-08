using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexUsageOverlay
{
    internal sealed class UsageData
    {
        public string Plan = "ChatGPT";
        public bool HasPlan;
        public int? ShortRemaining;
        public bool HasShortRemaining;
        public string ShortResetText = "待刷新";
        public bool HasShortResetText;
        public int? WeeklyRemaining;
        public bool HasWeeklyRemaining;
        public string WeeklyResetText = "待刷新";
        public bool HasWeeklyResetText;
        public string RateLimitStatus = "待刷新";
        public bool HasRateLimitStatus;
        public int? AvailableResetCredits;
        public bool HasAvailableResetCredits;
        public long? ResetCreditExpiresAt;
        public bool HasResetCreditExpiry;
        // One entry per available credit; null means this credit's expiry is unknown.
        public long?[] ResetCreditExpiries = new long?[0];
        public bool HasResetCreditDetails;
        public string ProfileTokensText = String.Empty;
        public long? LifetimeTokens;
        public string Source = "缓存";
        public string LastError = String.Empty;
        public DateTime UpdatedUtc = DateTime.MinValue;

        public UsageData Clone()
        {
            UsageData copy = (UsageData)MemberwiseClone();
            copy.ResetCreditExpiries = (long?[])(ResetCreditExpiries ?? new long?[0]).Clone();
            return copy;
        }
    }

    internal static class UsageDataMerger
    {
        internal static bool MergeInto(UsageData target, UsageData incoming)
        {
            if (target == null)
                throw new ArgumentNullException("target");
            if (incoming == null)
                throw new ArgumentNullException("incoming");

            bool changed = false;
            if (incoming.HasPlan && !String.IsNullOrWhiteSpace(incoming.Plan))
            {
                if (target.Plan != incoming.Plan)
                {
                    target.Plan = incoming.Plan;
                    changed = true;
                }
                if (!target.HasPlan)
                {
                    target.HasPlan = true;
                    changed = true;
                }
            }
            if (incoming.HasShortRemaining)
            {
                if (target.ShortRemaining != incoming.ShortRemaining)
                {
                    target.ShortRemaining = incoming.ShortRemaining;
                    changed = true;
                }
                if (!target.HasShortRemaining)
                {
                    target.HasShortRemaining = true;
                    changed = true;
                }
            }
            if (incoming.HasShortResetText)
            {
                if (target.ShortResetText != incoming.ShortResetText)
                {
                    target.ShortResetText = incoming.ShortResetText;
                    changed = true;
                }
                if (!target.HasShortResetText)
                {
                    target.HasShortResetText = true;
                    changed = true;
                }
            }
            if (incoming.HasWeeklyRemaining)
            {
                if (target.WeeklyRemaining != incoming.WeeklyRemaining)
                {
                    target.WeeklyRemaining = incoming.WeeklyRemaining;
                    changed = true;
                }
                if (!target.HasWeeklyRemaining)
                {
                    target.HasWeeklyRemaining = true;
                    changed = true;
                }
            }
            if (incoming.HasWeeklyResetText)
            {
                if (target.WeeklyResetText != incoming.WeeklyResetText)
                {
                    target.WeeklyResetText = incoming.WeeklyResetText;
                    changed = true;
                }
                if (!target.HasWeeklyResetText)
                {
                    target.HasWeeklyResetText = true;
                    changed = true;
                }
            }
            if (incoming.HasRateLimitStatus)
            {
                if (target.RateLimitStatus != incoming.RateLimitStatus)
                {
                    target.RateLimitStatus = incoming.RateLimitStatus;
                    changed = true;
                }
                if (!target.HasRateLimitStatus)
                {
                    target.HasRateLimitStatus = true;
                    changed = true;
                }
            }
            if (incoming.HasAvailableResetCredits)
            {
                // A new inventory without detail must not retain previous cards' dates.
                if (!incoming.HasResetCreditDetails && target.HasResetCreditDetails)
                {
                    target.ResetCreditExpiries = new long?[0];
                    target.HasResetCreditDetails = false;
                    changed = true;
                }
                if (!incoming.HasResetCreditExpiry && target.HasResetCreditExpiry)
                {
                    target.ResetCreditExpiresAt = null;
                    target.HasResetCreditExpiry = false;
                    changed = true;
                }
                if (target.AvailableResetCredits != incoming.AvailableResetCredits)
                {
                    target.AvailableResetCredits = incoming.AvailableResetCredits;
                    changed = true;
                }
                if (!target.HasAvailableResetCredits)
                {
                    target.HasAvailableResetCredits = true;
                    changed = true;
                }
            }
            if (incoming.HasResetCreditExpiry)
            {
                if (target.ResetCreditExpiresAt != incoming.ResetCreditExpiresAt)
                {
                    target.ResetCreditExpiresAt = incoming.ResetCreditExpiresAt;
                    changed = true;
                }
                if (!target.HasResetCreditExpiry)
                {
                    target.HasResetCreditExpiry = true;
                    changed = true;
                }
            }
            if (incoming.HasResetCreditDetails)
            {
                long?[] next = incoming.ResetCreditExpiries ?? new long?[0];
                long?[] previous = target.ResetCreditExpiries ?? new long?[0];
                bool same = next.Length == previous.Length;
                for (int index = 0; same && index < next.Length; index++)
                    same = next[index] == previous[index];
                if (!same || !target.HasResetCreditDetails)
                {
                    target.ResetCreditExpiries = (long?[])next.Clone();
                    target.HasResetCreditDetails = true;
                    changed = true;
                }
            }
            if (!String.IsNullOrWhiteSpace(incoming.ProfileTokensText) &&
                incoming.ProfileTokensText != "待刷新" && target.ProfileTokensText != incoming.ProfileTokensText)
            {
                target.ProfileTokensText = incoming.ProfileTokensText;
                changed = true;
            }
            if (incoming.LifetimeTokens.HasValue && target.LifetimeTokens != incoming.LifetimeTokens)
            {
                target.LifetimeTokens = incoming.LifetimeTokens;
                changed = true;
            }
            if (!String.IsNullOrWhiteSpace(incoming.Source))
                target.Source = incoming.Source;
            target.LastError = incoming.LastError ?? String.Empty;
            return changed;
        }
    }

    internal static class ResetCreditDetails
    {
        internal static bool IsValidExpiry(long value)
        {
            return value > 0 && value <= 253402300799L;
        }

        internal static string Serialize(UsageData usage)
        {
            if (!usage.HasResetCreditDetails) return String.Empty;
            List<string> values = new List<string>();
            foreach (long? expiry in usage.ResetCreditExpiries ?? new long?[0])
                values.Add(expiry.HasValue && IsValidExpiry(expiry.Value)
                    ? expiry.Value.ToString(CultureInfo.InvariantCulture) : "?");
            // Prefix distinguishes a known empty inventory from absent legacy data.
            return "v1:" + String.Join(",", values.ToArray());
        }

        internal static bool TryDeserialize(string text, out long?[] expiries)
        {
            expiries = new long?[0];
            if (text == null || !text.StartsWith("v1:", StringComparison.Ordinal)) return false;
            string body = text.Substring(3);
            if (body.Length == 0) return true;
            string[] values = body.Split(',');
            List<long?> result = new List<long?>();
            foreach (string value in values)
            {
                long expiry;
                if (value == "?") result.Add(null);
                else if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out expiry) && IsValidExpiry(expiry)) result.Add(expiry);
                else return false;
            }
            expiries = result.ToArray();
            return true;
        }
    }

    internal static class UsageDisplayText
    {
        internal static string Build(UsageData usage, int availableTextWidth)
        {
            if (usage == null)
                return "Codex 用量正在载入";

            string planLabel = String.IsNullOrWhiteSpace(usage.Plan)
                ? "CHATGPT"
                : usage.Plan.ToUpperInvariant();
            string weeklyRemaining = FormatRemaining(
                usage.WeeklyRemaining, usage.RateLimitStatus != "待刷新");
            string resetText = FormatResetText(usage.WeeklyResetText);
            string compactResetText = FormatCompactResetText(resetText);
            string tokensText = String.IsNullOrWhiteSpace(usage.ProfileTokensText)
                ? "待刷新"
                : usage.ProfileTokensText;
            bool abnormalStatus = IsAbnormalRateLimitStatus(usage.RateLimitStatus);
            string statusText = FormatRateLimitStatus(usage.RateLimitStatus);
            List<string> sections = new List<string>();

            if (availableTextWidth >= 500)
            {
                sections.Add(planLabel);
                sections.Add("周用量剩余：" + weeklyRemaining + "·" + resetText);
                if (abnormalStatus)
                    sections.Add("状态：" + statusText);
                if (usage.AvailableResetCredits.HasValue)
                    sections.Add("重置券：" + usage.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture));
                sections.Add("累计Token：" + tokensText);
                return String.Join(" | ", sections.ToArray());
            }

            if (availableTextWidth >= 390)
            {
                sections.Add(planLabel);
                sections.Add("余" + weeklyRemaining + (String.IsNullOrWhiteSpace(compactResetText)
                    ? String.Empty
                    : "·" + compactResetText));
                if (abnormalStatus)
                    sections.Add(statusText);
                if (usage.AvailableResetCredits.HasValue)
                    sections.Add("券" + usage.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture));
                sections.Add("Token：" + tokensText);
                return String.Join(" | ", sections.ToArray());
            }

            if (availableTextWidth < 270)
                return "Token：" + tokensText;

            sections.Add(planLabel);
            sections.Add("余" + weeklyRemaining);
            sections.Add("Token：" + tokensText);
            return String.Join(" | ", sections.ToArray());
        }

        private static string FormatRemaining(int? remaining, bool hasQuotaData)
        {
            return remaining.HasValue
                ? remaining.Value.ToString(CultureInfo.InvariantCulture) + "%"
                : (hasQuotaData ? "—" : "待刷新");
        }

        private static bool IsAbnormalRateLimitStatus(string status)
        {
            return !String.IsNullOrWhiteSpace(status) &&
                !String.Equals(status, "正常", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(status, "待刷新", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(status, "normal", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(status, "pending", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatRateLimitStatus(string status)
        {
            if (String.Equals(status, "rate_limit_reached", StringComparison.OrdinalIgnoreCase))
                return "额度已用完";
            if (String.Equals(status, "rate_limit_warning", StringComparison.OrdinalIgnoreCase))
                return "接近额度上限";
            if (String.Equals(status, "normal", StringComparison.OrdinalIgnoreCase))
                return "正常";
            if (String.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                return "待刷新";
            if (String.IsNullOrWhiteSpace(status))
                return "待刷新";
            return status.Length <= 12 ? status : "额度状态异常";
        }

        private static string FormatResetText(string resetText)
        {
            if (String.IsNullOrWhiteSpace(resetText) || resetText == "—" || resetText == "待刷新")
                return resetText;
            return resetText.Replace(" ", String.Empty) + "重置";
        }

        private static string FormatCompactResetText(string resetText)
        {
            if (String.IsNullOrWhiteSpace(resetText) || resetText == "—" || resetText == "待刷新")
                return String.Empty;
            return resetText.Replace("月", "/").Replace("日", " ").Replace("重置", String.Empty);
        }
    }
}
