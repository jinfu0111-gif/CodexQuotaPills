using System;
using System.Collections.Generic;

namespace CodexUsageOverlay
{
    internal static class ResetCreditTransportTests
    {
        internal static void InventoryRequiresAccountBoundRealDetails()
        {
            ResetCreditInventory data;
            long expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 500;
            var account = Account("user@example.test");
            var limits = Limits(expiry);
            Assert(CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data), "valid inventory rejected");
            Assert(data.AccountHash.Length == 64 && data.Credits.Length == 1 && data.HasDetails, "identity/details missing");
            string firstHash = data.AccountHash;
            limits["accountId"] = "another-workspace";
            Assert(CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data) && data.AccountHash != firstHash,
                "workspace change shares redemption identity");
            limits.Remove("accountId");
            Assert(!CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data), "identityless inventory accepted");
            limits = Limits(expiry);
            account["account"] = Obj("type", "apiKey", "email", "user@example.test");
            Assert(!CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data), "API key inventory accepted");
            account = Account("user@example.test");
            Details(limits)["credits"] = null;
            Assert(CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data) &&
                data.AvailableCount == 1 && !data.HasDetails && data.Credits.Length == 0, "count-only response invented a card");
            Details(limits)["credits"] = new object[] { Card(expiry), Card(expiry + 20) };
            Assert(CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data) && data.Credits.Length == 0,
                "conflicting duplicate ID is actionable");
            Details(limits)["credits"] = new object[] { Card(null) };
            Assert(CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out data) && !data.Credits[0].ExpiresAt.HasValue,
                "missing expiry was fabricated");
        }

        internal static void ConsumeIsNarrowAndRechecksLiveOwnership()
        {
            long expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 500;
            var limits = Limits(expiry);
            var account = Account("user@example.test");
            ResetCreditInventory inventory;
            Assert(CodexAppServerClient.TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out inventory), "fixture rejected");
            int writes = 0, detailsReads = 0, accountReads = 0;
            string expectedKey = Guid.NewGuid().ToString("D");
            string response = "reset";
            bool switchUser = false;
            bool switchWorkspace = false;
            using (CodexAppServerClient client = new CodexAppServerClient(delegate(string method, object parameters) {
                if (method == "account/read") {
                    accountReads++;
                    if (switchWorkspace && accountReads % 2 == 0) limits["accountId"] = "workspace-switched-during-preflight";
                    return switchUser ? Account("different@example.test") : account;
                }
                if (method == "account/rateLimits/read") {
                    detailsReads++;
                    Assert((bool)((IDictionary<string, object>)parameters)["excludeResetCreditDetails"] == false, "details not explicitly requested");
                    return limits;
                }
                Assert(method == "account/rateLimitResetCredit/consume", "generic write method used");
                writes++;
                var args = (IDictionary<string, object>)parameters;
                Assert((string)args["creditId"] == "fixture-credit" && (string)args["idempotencyKey"] == expectedKey,
                    "specific credit or persisted key was omitted");
                return response == null ? null : Obj("outcome", response);
            })) {
                string outcome;
                Assert(client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == "reset" &&
                    writes == 1 && detailsReads == 2 && accountReads == 2, "successful consume skipped real preflight");
                response = null;
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == null && writes == 2,
                    "unknown result became success");
                response = "alreadyRedeemed";
                Assert(client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == "alreadyRedeemed" && writes == 3,
                    "idempotent outcome not accepted");
                switchUser = true;
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && writes == 3,
                    "account change sent a consume");
                switchUser = false;
                switchWorkspace = true;
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && writes == 3,
                    "same-email workspace changed during preflight but still sent a consume");
                switchWorkspace = false;
                limits["accountId"] = "fixture-workspace";
                Details(limits)["availableCount"] = 0;
                Details(limits)["credits"] = new object[0];
                Assert(client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == "noCredit" && writes == 3,
                    "manually spent credit sent a consume");
                Details(limits)["availableCount"] = 1;
                Details(limits)["credits"] = new object[] { Card(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 700) };
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && writes == 3,
                    "credit consumed earlier than ten minutes");
                Details(limits)["credits"] = new object[] { Card(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1) };
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && writes == 3,
                    "expired credit consumed");
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", "not-a-uuid", out outcome) && writes == 3,
                    "unpersistable idempotency key accepted");
                Details(limits)["credits"] = null;
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == null && writes == 3,
                    "count-only inventory incorrectly made the card terminal");
                Details(limits)["credits"] = new object[0];
                Assert(!client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == null && writes == 3,
                    "truncated inventory incorrectly made the card terminal");
                Details(limits)["credits"] = new object[] { Card(expiry) };
                Assert(client.TryConsumeResetCredit(inventory.AccountHash, "fixture-credit", expectedKey, out outcome) && outcome == "alreadyRedeemed" && writes == 4,
                    "recovered card details could not retry the same logical attempt");
                bool rejected = false;
                try { client.ReadOnlyRequest("account/rateLimitResetCredit/consume", null); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected, "read-only RPC allowlist was expanded to write");
            }
        }

        private static Dictionary<string, object> Account(string email) { return Obj("account", Obj("type", "chatgpt", "email", email, "planType", "prolite")); }
        private static Dictionary<string, object> Limits(long expiry) {
            return Obj("accountId", "fixture-workspace", "rateLimitResetCredits", Obj("availableCount", 1, "credits", new object[] { Card(expiry) }));
        }
        private static IDictionary<string, object> Details(IDictionary<string, object> limits) { return (IDictionary<string, object>)limits["rateLimitResetCredits"]; }
        private static Dictionary<string, object> Card(long? expiry) {
            return Obj("id", "fixture-credit", "resetType", "codexRateLimits", "status", "available", "expiresAt", expiry);
        }
        private static Dictionary<string, object> Obj(params object[] pairs) {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result[(string)pairs[i]] = pairs[i + 1];
            return result;
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
