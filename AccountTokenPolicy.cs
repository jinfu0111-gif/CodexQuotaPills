using System;
using System.Security.Cryptography;
using System.Text;

namespace CodexUsageOverlay
{
    // Account usage is separate from thread totals. Never display an unverified
    // cached value as live, or carry a value between authenticated accounts.
    internal static class AccountTokenPolicy
    {
        internal static string Identity(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return String.Empty;
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id.Trim())))
                    .Replace("-", String.Empty).ToLowerInvariant();
        }

        internal static bool Apply(UsageData target, UsageData incoming)
        {
            DateTime now = incoming.UpdatedUtc == DateTime.MinValue ? DateTime.UtcNow : incoming.UpdatedUtc;
            string identity = incoming.AccountTokenIdentity ?? String.Empty;
            bool same = identity.Length > 0 && target.AccountTokenIdentity == identity;
            if (!same)
            {
                target.LifetimeTokens = null;
                target.PendingTokenZeroUtc = DateTime.MinValue;
            }
            target.AccountTokensChecked = true;
            target.AccountTokenIdentity = identity;
            target.AccountTokenReadUtc = now;
            long? value = incoming.LifetimeTokens;
            if (!value.HasValue || value.Value < 0)
            {
                target.ProfileTokensText = "待确认";
                target.PendingTokenZeroUtc = DateTime.MinValue;
                return true;
            }
            if (value.Value == 0 && (identity.Length == 0 || !same ||
                !target.LifetimeTokens.HasValue || target.LifetimeTokens.Value > 0))
            {
                // A second same-account observation, separated by at least one
                // minimum poll interval, can confirm a genuine counter reset.
                if (identity.Length == 0 || target.PendingTokenZeroUtc == DateTime.MinValue ||
                    (now - target.PendingTokenZeroUtc).TotalSeconds < 5)
                {
                    if (target.PendingTokenZeroUtc == DateTime.MinValue) target.PendingTokenZeroUtc = now;
                    target.ProfileTokensText = "待确认";
                    return true;
                }
            }
            target.LifetimeTokens = value;
            target.PendingTokenZeroUtc = DateTime.MinValue;
            target.ProfileTokensText = CodexAppServerClient.FormatLifetimeTokens(value.Value);
            return true;
        }

        internal static void MarkUnavailable(UsageData usage)
        {
            usage.ProfileTokensText = "待确认";
            usage.PendingTokenZeroUtc = DateTime.MinValue;
        }
    }
}
