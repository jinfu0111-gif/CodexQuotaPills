using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal sealed class CodexAppServerClient : IDisposable
    {
        private const int RequestTimeoutMilliseconds = 8000;
        private readonly object gate = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private Process process;
        private StreamWriter inputWriter;
        private BlockingCollection<string> outputLines;
        private int nextRequestId = 1;
        private bool initialized;
        private string activeCodexHome = String.Empty;
        private string lastSyncedNativeAuthSource = String.Empty;

        public string LastError { get; private set; }

        public CodexAppServerClient() { }
#if AUTO_RESUME_TESTS
        private Func<string, object, IDictionary<string, object>> resetTestRequest;
        internal CodexAppServerClient(Func<string, object, IDictionary<string, object>> request)
        { resetTestRequest = request; }
#endif

        private IDictionary<string, object> ResetCreditRequest(string method, object parameters)
        {
#if AUTO_RESUME_TESTS
            if (resetTestRequest != null) return resetTestRequest(method, parameters);
#endif
            if (!EnsureStarted(true)) return null;
            return SendRequest(method, parameters,
                method == "account/rateLimitResetCredit/consume" ? 15000 : RequestTimeoutMilliseconds);
        }

        // Dedicated narrow write path. ReadOnlyRequest remains read-only.
        public bool TryReadResetCredits(out ResetCreditInventory inventory)
        {
            lock (gate)
            {
                inventory = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        if (ReadResetCreditsCore(out inventory))
                            return true;
                    }
                    catch
                    {
                        LastError = "重置卡实时读取失败";
                    }
#if AUTO_RESUME_TESTS
                    if (resetTestRequest != null) break;
#endif
                    ResetProcess();
                }
                return false;
            }
        }

        private bool ReadResetCreditsCore(out ResetCreditInventory inventory)
        {
            inventory = null;
            IDictionary<string, object> account = ResetCreditRequest("account/read", ObjectOf("refreshToken", false));
            IDictionary<string, object> limits = ResetCreditRequest("account/rateLimits/read", ObjectOf("excludeResetCreditDetails", false));
            if (!TryParseResetCreditInventory(account, limits, DateTime.UtcNow, out inventory))
            { LastError = "无法确认当前账户与重置卡明细"; return false; }
            // Recheck the authenticated identity after fetching the inventory.
            IDictionary<string, object> after = ResetCreditRequest("account/read", ObjectOf("refreshToken", false));
            ResetCreditInventory confirmed;
            IDictionary<string, object> latestLimits = ResetCreditRequest("account/rateLimits/read", ObjectOf("excludeResetCreditDetails", false));
            if (!TryParseResetCreditInventory(after, latestLimits, DateTime.UtcNow, out confirmed) ||
                confirmed.AccountHash != inventory.AccountHash)
            { inventory = null; LastError = "重置卡读取期间账户发生变化"; return false; }
            inventory = confirmed;
            LastError = String.Empty;
            return true;
        }

        public bool TryConsumeResetCredit(string expectedAccountHash, string creditId,
            string idempotencyKey, out string outcome)
        {
            outcome = null;
            Guid parsed;
            if (String.IsNullOrWhiteSpace(expectedAccountHash) || String.IsNullOrWhiteSpace(creditId) ||
                !Guid.TryParseExact(idempotencyKey, "D", out parsed)) return false;
            lock (gate)
            {
                try
                {
                    ResetCreditInventory current;
                    if (!ReadResetCreditsCore(out current) || current.AccountHash != expectedAccountHash)
                    { LastError = "自动使用重置卡前的账户复核失败"; return false; }
                    ResetCreditRecord selected = null;
                    foreach (ResetCreditRecord credit in current.Credits)
                        if (credit.Id == creditId) { selected = credit; break; }
                    if (selected == null || selected.Status != "available")
                    {
                        if (current.HasDetails && current.AvailableCount == current.Credits.Length)
                        { outcome = "noCredit"; return true; }
                        LastError = "重置卡明细暂缺，等待完整的实时记录";
                        return false;
                    }
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (selected.ResetType != "codexRateLimits" || !selected.ExpiresAt.HasValue ||
                        selected.ExpiresAt.Value <= now || selected.ExpiresAt.Value - now > 600)
                    { LastError = "该重置卡不在到期前10分钟的有效窗口内"; return false; }
                    IDictionary<string, object> result = ResetCreditRequest("account/rateLimitResetCredit/consume",
                        new Dictionary<string, object> { { "idempotencyKey", idempotencyKey }, { "creditId", creditId } });
                    string value = ReadString(result, "outcome");
                    if (value != "reset" && value != "alreadyRedeemed" &&
                        value != "nothingToReset" && value != "noCredit")
                    { LastError = "重置卡使用结果待确认，将保留原幂等键"; return false; }
                    outcome = value;
                    LastError = String.Empty;
                    return true;
                }
                catch { LastError = "重置卡使用结果待确认，将保留原幂等键"; ResetProcess(); return false; }
            }
        }

        internal static bool TryParseResetCreditInventory(IDictionary<string, object> accountResult,
            IDictionary<string, object> limitsResult, DateTime readUtc, out ResetCreditInventory inventory)
        {
            inventory = null;
            IDictionary<string, object> account = ReadObject(accountResult, "account");
            if (account == null || !UsageTrustPolicy.HasAuthenticatedAccount(ReadString(account, "type"))) return false;
            // The rate-limits response carries the workspace/account ID even when account/read doesn't.
            string accountId = ReadString(limitsResult, "accountId");
            if (String.IsNullOrWhiteSpace(accountId) || accountId.Length > 512) return false;
            string accountReadId = ReadString(account, "accountId");
            if (!String.IsNullOrWhiteSpace(accountReadId) && accountReadId != accountId) return false;
            string email = (ReadString(account, "email") ?? String.Empty).Trim().ToLowerInvariant();
            IDictionary<string, object> details = ReadObject(limitsResult, "rateLimitResetCredits");
            long count;
            if (!TryReadLong(details, "availableCount", out count) || count < 0 || count > Int32.MaxValue) return false;
            ResetCreditInventory next = new ResetCreditInventory {
                AccountHash = AccountTokenPolicy.Identity("chatgpt|" + accountId + "|" + email),
                ReadUtc = readUtc, AvailableCount = (int)count };
            object rowsValue;
            if (details.TryGetValue("credits", out rowsValue) && rowsValue != null)
            {
                object[] rows = rowsValue as object[];
                if (rows == null || rows.Length > 512) return false;
                next.HasDetails = true;
                Dictionary<string, ResetCreditRecord> unique = new Dictionary<string, ResetCreditRecord>(StringComparer.Ordinal);
                HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
                foreach (object row in rows)
                {
                    IDictionary<string, object> card = AsObject(row);
                    string id = ReadString(card, "id");
                    if (String.IsNullOrWhiteSpace(id) || id.Length > 512) continue;
                    string status = ReadString(card, "status");
                    string type = ReadString(card, "resetType");
                    long expiry;
                    long? expires = TryReadLong(card, "expiresAt", out expiry) && ResetCreditDetails.IsValidExpiry(expiry)
                        ? (long?)expiry : null;
                    ResetCreditRecord previous;
                    if (unique.TryGetValue(id, out previous))
                    {
                        if (previous.ExpiresAt != expires || previous.ResetType != type || previous.Status != status) conflicts.Add(id);
                        continue;
                    }
                    unique.Add(id, new ResetCreditRecord { Id = id, Status = status, ResetType = type, ExpiresAt = expires });
                }
                List<ResetCreditRecord> known = new List<ResetCreditRecord>();
                foreach (KeyValuePair<string, ResetCreditRecord> pair in unique)
                    if (!conflicts.Contains(pair.Key) && pair.Value.Status == "available") known.Add(pair.Value);
                if (known.Count > count) return false;
                known.Sort(delegate(ResetCreditRecord left, ResetCreditRecord right) {
                    int order = (left.ExpiresAt ?? Int64.MaxValue).CompareTo(right.ExpiresAt ?? Int64.MaxValue);
                    return order != 0 ? order : StringComparer.Ordinal.Compare(left.Id, right.Id);
                });
                next.Credits = known.ToArray();
            }
            inventory = next;
            return true;
        }

        // The resume monitor may inspect history, never generate through this
        // separate app-server: desktop-owned tools must stay with their owner.
        internal IDictionary<string, object> ReadOnlyRequest(string method, object parameters)
        {
            if (method != "thread/list" && method != "thread/read" &&
                method != "thread/turns/list" && method != "account/read" &&
                method != "account/rateLimits/read")
                throw new InvalidOperationException("Read-only request not allowed");
            bool requireChatGptAuth = method == "account/read" || method == "account/rateLimits/read";
            lock (gate)
            {
                try
                {
                    json.MaxJsonLength = 64 * 1024 * 1024;
                    if (!EnsureStarted(requireChatGptAuth)) return null;
                    return SendRequest(method, parameters);
                }
                catch
                {
                    LastError = "无法读取 Codex 会话状态";
                    ResetProcess();
                    return null;
                }
            }
        }

        public bool TryReadUsage(out UsageData usage)
        {
            lock (gate)
            {
                usage = new UsageData();
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    usage = new UsageData();
                    LastError = String.Empty;
                    try
                    {
                        if (EnsureStarted(true))
                        {
                            IDictionary<string, object> account = SendRequest("account/read",
                                ObjectOf("refreshToken", false));
                            IDictionary<string, object> rateLimits = SendRequest("account/rateLimits/read", null);
                            if (TryParseTrustedSnapshot(account, rateLimits, usage))
                            {
                                usage.AccountTokensChecked = true;
                                IDictionary<string, object> accountFields = ReadObject(account, "account");
                                string identity = ReadString(accountFields, "accountId");
                                if (String.IsNullOrWhiteSpace(identity))
                                {
                                    identity = ReadString(accountFields, "email");
                                    if (identity != null) identity = identity.Trim().ToLowerInvariant();
                                }
                                usage.AccountTokenIdentity = AccountTokenPolicy.Identity(identity);
                                IDictionary<string, object> tokenUsage = SendRequest("account/usage/read", null);
                                if (tokenUsage != null)
                                    ParseTokenUsage(tokenUsage, usage);

                                usage.Source = "Codex CLI app-server";
                                usage.UpdatedUtc = DateTime.UtcNow;
                                return true;
                            }
                            if (String.IsNullOrWhiteSpace(LastError))
                                LastError = "Codex app-server 没有返回可验证的登录账户与额度窗口";
                        }
                    }
                    catch (Exception exception)
                    {
                        LastError = exception.Message;
                    }
                    ResetProcess();
                }
                return false;
            }
        }

        private bool EnsureStarted(bool requireChatGptAuth = true)
        {
            string desiredCodexHome = ResolveEffectiveCodexHome(requireChatGptAuth);
            if (process != null && !process.HasExited && initialized &&
                String.Equals(activeCodexHome, desiredCodexHome, StringComparison.OrdinalIgnoreCase))
                return true;

            ResetProcess();
            outputLines = new BlockingCollection<string>();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ResolveCodexExecutable();
            startInfo.Arguments = "app-server";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = new UTF8Encoding(false);
            startInfo.StandardErrorEncoding = new UTF8Encoding(false);
            if (!String.IsNullOrWhiteSpace(desiredCodexHome))
                startInfo.EnvironmentVariables["CODEX_HOME"] = desiredCodexHome;

            process = new Process();
            process.StartInfo = startInfo;

            try
            {
                // Process.Start in .NET Framework itself constructs and flushes
                // its stdin writer. Set the no-BOM encoding BEFORE that happens.
                try { Console.InputEncoding = new UTF8Encoding(false); }
                catch (IOException) { } // A tray/WinExe process need not own a console.
                if (!process.Start())
                {
                    LastError = "无法启动 Codex CLI";
                    ResetProcess();
                    return false;
                }
                // .NET Framework's redirected stdin can emit a BOM depending
                // on the console encoding. Current Codex expects bare JSONL.
                inputWriter = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
                inputWriter.AutoFlush = true;
                Process startedProcess = process;
                BlockingCollection<string> startedQueue = outputLines;
                Thread outputReader = new Thread(delegate()
                {
                    try
                    {
                        string line;
                        while ((line = startedProcess.StandardOutput.ReadLine()) != null)
                            startedQueue.Add(line);
                    }
                    catch { }
                    finally { try { startedQueue.CompleteAdding(); } catch { } }
                });
                outputReader.IsBackground = true;
                outputReader.Start();
                Thread errorReader = new Thread(delegate()
                {
                    try { while (startedProcess.StandardError.ReadLine() != null) { } } catch { }
                });
                errorReader.IsBackground = true;
                errorReader.Start();
            }
            catch (Exception exception)
            {
                LastError = "无法启动 Codex CLI: " + exception.Message;
                ResetProcess();
                return false;
            }

            Dictionary<string, object> clientInfo = new Dictionary<string, object>();
            clientInfo["name"] = "codex_quota_pills";
            clientInfo["title"] = "Codex Quota Pills";
            clientInfo["version"] = "0.2.16";
            Dictionary<string, object> initializeParams = new Dictionary<string, object>();
            initializeParams["clientInfo"] = clientInfo;
            initializeParams["capabilities"] = ObjectOf("experimentalApi", true);

            IDictionary<string, object> initialize = SendRequest("initialize", initializeParams);
            if (initialize == null)
            {
                ResetProcess();
                return false;
            }
            SendNotification("initialized", new Dictionary<string, object>());
            initialized = true;
            activeCodexHome = desiredCodexHome;
            return true;
        }

        private string ResolveEffectiveCodexHome(bool requireChatGptAuth)
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            string defaultHome = String.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
                : configured.Trim();
            if (!requireChatGptAuth)
                return defaultHome;

            try
            {
                string authPath = Path.Combine(defaultHome, "auth.json");
                if (File.Exists(authPath) && HasChatGptOAuthTokens(File.ReadAllText(authPath, Encoding.UTF8)))
                    return defaultHome;

                string cpaPath = Path.Combine(defaultHome, "cpa-native-oauth.json");
                if (!File.Exists(cpaPath))
                    return defaultHome;

                IDictionary<string, object> cpa = json.DeserializeObject(File.ReadAllText(cpaPath, Encoding.UTF8)) as IDictionary<string, object>;
                string nativeAuth = ExtractNativeOAuthJson(cpa, "native_auth");
                if (String.IsNullOrWhiteSpace(nativeAuth))
                    nativeAuth = ExtractNativeOAuthJson(cpa, "original_auth");
                if (String.IsNullOrWhiteSpace(nativeAuth) || !HasChatGptOAuthTokens(nativeAuth))
                    return defaultHome;

                string bridgeHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CodexQuotaPills", "NativeAuthHome");
                Directory.CreateDirectory(bridgeHome);
                string bridgeAuthPath = Path.Combine(bridgeHome, "auth.json");
                bool needWrite = !File.Exists(bridgeAuthPath) ||
                    !String.Equals(lastSyncedNativeAuthSource, nativeAuth, StringComparison.Ordinal);
                if (!needWrite)
                {
                    try
                    {
                        if (!HasChatGptOAuthTokens(File.ReadAllText(bridgeAuthPath, Encoding.UTF8)))
                            needWrite = true;
                    }
                    catch { needWrite = true; }
                }
                if (needWrite)
                {
                    string tempPath = bridgeAuthPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tempPath, nativeAuth, new UTF8Encoding(false));
                    if (File.Exists(bridgeAuthPath)) File.Delete(bridgeAuthPath);
                    File.Move(tempPath, bridgeAuthPath);
                    lastSyncedNativeAuthSource = nativeAuth;
                }
                return bridgeHome;
            }
            catch
            {
                return defaultHome;
            }
        }

        private string ExtractNativeOAuthJson(IDictionary<string, object> cpa, string key)
        {
            if (cpa == null) return null;
            object value;
            if (!cpa.TryGetValue(key, out value) || value == null) return null;
            string text = value as string;
            if (!String.IsNullOrWhiteSpace(text)) return text;
            IDictionary<string, object> map = value as IDictionary<string, object>;
            return map != null ? json.Serialize(map) : null;
        }

        private bool HasChatGptOAuthTokens(string authJsonText)
        {
            if (String.IsNullOrWhiteSpace(authJsonText)) return false;
            try
            {
                IDictionary<string, object> parsed = json.DeserializeObject(authJsonText) as IDictionary<string, object>;
                if (parsed == null) return false;
                string mode = ReadString(parsed, "auth_mode");
                if (String.Equals(mode, "apikey", StringComparison.OrdinalIgnoreCase)) return false;
                IDictionary<string, object> tokens = ReadObject(parsed, "tokens");
                return tokens != null && tokens.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private IDictionary<string, object> SendRequest(string method, object parameters,
            int timeoutMilliseconds = RequestTimeoutMilliseconds)
        {
            if (process == null || process.HasExited)
            {
                LastError = "Codex app-server 已退出";
                return null;
            }

            int requestId = nextRequestId++;
            Dictionary<string, object> request = new Dictionary<string, object>();
            request["method"] = method;
            request["id"] = requestId;
            if (parameters != null)
                request["params"] = parameters;
            WriteMessage(request);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                int wait = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                string line;
                if (!outputLines.TryTake(out line, wait))
                    break;
                if (String.IsNullOrWhiteSpace(line))
                    continue;

                IDictionary<string, object> message;
                try
                {
                    message = json.DeserializeObject(line) as IDictionary<string, object>;
                }
                catch
                {
                    continue;
                }
                if (message == null || !MatchesId(message, requestId))
                    continue;

                object errorValue;
                if (message.TryGetValue("error", out errorValue) && errorValue != null)
                {
                    IDictionary<string, object> error = AsObject(errorValue);
                    LastError = error != null ? ReadString(error, "message") : "Codex app-server 请求失败";
                    return null;
                }

                object resultValue;
                if (!message.TryGetValue("result", out resultValue))
                {
                    LastError = "Codex app-server 返回缺少 result";
                    return null;
                }
                return AsObject(resultValue) ?? new Dictionary<string, object>();
            }

            LastError = method + " 请求超时";
            ResetProcess();
            return null;
        }

        private void SendNotification(string method, object parameters)
        {
            Dictionary<string, object> notification = new Dictionary<string, object>();
            notification["method"] = method;
            notification["params"] = parameters;
            WriteMessage(notification);
        }

        private void WriteMessage(IDictionary<string, object> message)
        {
            inputWriter.WriteLine(json.Serialize(message));
            inputWriter.Flush();
        }

        internal static bool TryParseTrustedSnapshot(IDictionary<string, object> accountResult,
            IDictionary<string, object> rateLimitsResult, UsageData usage)
        {
            if (usage == null)
                throw new ArgumentNullException("usage");

            string accountPlan = null;
            bool accountFound = accountResult != null && ParseAccount(accountResult, out accountPlan);
            string quotaPlan = null;
            bool quotaWindowFound = rateLimitsResult != null &&
                ParseRateLimits(rateLimitsResult, usage, out quotaPlan);
            if (!UsageTrustPolicy.HasVerifiedSnapshot(accountFound, quotaWindowFound))
                return false;

            string trustedPlan = UsageTrustPolicy.SelectTrustedPlan(quotaPlan, accountPlan);
            if (UsageTrustPolicy.IsUsablePlan(trustedPlan))
            {
                usage.Plan = NormalizePlan(trustedPlan);
                usage.HasPlan = true;
                if (!UsageTrustPolicy.IsProPlan(trustedPlan) && !usage.HasShortLimitState)
                {
                    usage.ShortUnlimited = false;
                    usage.HasShortLimitState = true;
                }
            }
            ConfirmAbsentShortLimit(rateLimitsResult, trustedPlan, usage);
            return true;
        }

        internal static bool ParseAccount(IDictionary<string, object> result, out string accountPlan)
        {
            accountPlan = null;
            object accountValue;
            if (!result.TryGetValue("account", out accountValue))
                return false;
            IDictionary<string, object> account = AsObject(accountValue);
            if (account == null)
                return false;
            accountPlan = ReadString(account, "planType");
            return UsageTrustPolicy.HasAuthenticatedAccount(ReadString(account, "type"));
        }

        internal static bool ParseRateLimits(IDictionary<string, object> result, UsageData usage,
            out string quotaPlan)
        {
            quotaPlan = null;
            IDictionary<string, object> limits = SelectCodexRateLimits(result);
            if (limits == null)
                return false;

            bool found = false;
            IDictionary<string, object> primary = ReadObject(limits, "primary");
            IDictionary<string, object> secondary = ReadObject(limits, "secondary");
            string primaryPlan;
            string secondaryPlan;
            found |= ParseQuotaWindow(primary, usage, out primaryPlan);
            found |= ParseQuotaWindow(secondary, usage, out secondaryPlan);
            if (!found)
                return false;

            quotaPlan = ReadString(limits, "planType");
            if (!UsageTrustPolicy.IsUsablePlan(quotaPlan))
                quotaPlan = UsageTrustPolicy.SelectTrustedPlan(secondaryPlan, primaryPlan);

            object reachedValue;
            if (limits.TryGetValue("rateLimitReachedType", out reachedValue))
            {
                usage.RateLimitStatus = NormalizeRateLimitStatus(reachedValue == null ? null :
                    Convert.ToString(reachedValue, CultureInfo.InvariantCulture), primary, secondary);
                usage.HasRateLimitStatus = true;
            }

            IDictionary<string, object> resetCredits = ReadObject(result, "rateLimitResetCredits");
            long availableCount;
            if (resetCredits != null && TryReadLong(resetCredits, "availableCount", out availableCount) && availableCount >= 0)
            {
                usage.AvailableResetCredits = (int)Math.Min(Int32.MaxValue, availableCount);
                usage.HasAvailableResetCredits = true;
                if (availableCount == 0)
                {
                    usage.ResetCreditExpiresAt = null;
                    usage.HasResetCreditExpiry = true;
                    usage.HasResetCreditDetails = true;
                    usage.ResetCreditExpiries = new long?[0];
                }
                else
                {
                    object creditRows;
                    if (resetCredits.TryGetValue("credits", out creditRows) &&
                        creditRows != null && !(creditRows is string))
                    {
                        IEnumerable rows = creditRows as IEnumerable;
                        if (rows != null)
                        {
                            long? earliestExpiry = null;
                            List<long?> expiries = new List<long?>();
                            foreach (object rowValue in rows)
                            {
                                IDictionary<string, object> row = AsObject(rowValue);
                                if (row == null || !String.Equals(
                                    ReadString(row, "status"), "available",
                                    StringComparison.OrdinalIgnoreCase))
                                    continue;
                                long expiresAt;
                                long? expiry = TryReadLong(row, "expiresAt", out expiresAt) &&
                                    ResetCreditDetails.IsValidExpiry(expiresAt) ? (long?)expiresAt : null;
                                expiries.Add(expiry);
                                if (expiry.HasValue && (!earliestExpiry.HasValue ||
                                    expiry.Value < earliestExpiry.Value)) earliestExpiry = expiry;
                            }
                            // Conflicting inventory is incomplete, never invent ownership.
                            if (expiries.Count <= availableCount)
                            {
                                expiries.Sort(delegate(long? left, long? right) {
                                    if (!left.HasValue) return right.HasValue ? 1 : 0;
                                    if (!right.HasValue) return -1;
                                    return left.Value.CompareTo(right.Value);
                                });
                                usage.ResetCreditExpiries = expiries.ToArray();
                                usage.HasResetCreditDetails = true;
                                usage.ResetCreditExpiresAt = earliestExpiry;
                                usage.HasResetCreditExpiry = true;
                            }
                        }
                    }
                }
            }

            return true;
        }

        private static IDictionary<string, object> SelectCodexRateLimits(IDictionary<string, object> result)
        {
            if (result == null) return null;
            IDictionary<string, object> byId = ReadObject(result, "rateLimitsByLimitId");
            IDictionary<string, object> codex = ReadObject(byId, "codex");
            return codex ?? ReadObject(result, "rateLimits");
        }

        private static void ConfirmAbsentShortLimit(IDictionary<string, object> result,
            string trustedPlan, UsageData usage)
        {
            if (!UsageTrustPolicy.IsProPlan(trustedPlan)) return;
            IDictionary<string, object> limits = SelectCodexRateLimits(result);
            if (limits == null) return;
            object primary;
            object secondary;
            // A partial response or an unreadable non-null window is not evidence
            // that a limit has been removed. Require the complete two-slot shape.
            if (!limits.TryGetValue("primary", out primary) ||
                !limits.TryGetValue("secondary", out secondary)) return;
            IDictionary<string, object> weekly = primary == null ? AsObject(secondary) :
                (secondary == null ? AsObject(primary) : null);
            long duration;
            double used;
            long reset;
            bool hasUsed = TryReadDouble(weekly, "usedPercent", out used);
            bool hasReset = TryReadLong(weekly, "resetsAt", out reset);
            if (!TryReadLong(weekly, "windowDurationMins", out duration) || duration != 10080 ||
                !UsageTrustPolicy.IsRealQuotaWindow(duration, hasUsed, used, hasReset, reset)) return;
            usage.ShortUnlimited = true;
            usage.HasShortLimitState = true;
            usage.ShortRemaining = null;
            usage.HasShortRemaining = false;
            usage.ShortResetText = "—";
            usage.HasShortResetText = false;
        }

        internal static bool ParseQuotaWindow(IDictionary<string, object> window, UsageData usage,
            out string windowPlan)
        {
            windowPlan = null;
            if (window == null)
                return false;

            long durationMinutes;
            bool hasDuration = TryReadLong(window, "windowDurationMins", out durationMinutes);
            double usedPercent;
            bool hasUsedPercent = TryReadDouble(window, "usedPercent", out usedPercent);
            long resetsAt;
            bool hasResetAt = TryReadLong(window, "resetsAt", out resetsAt);
            if (!hasDuration || !UsageTrustPolicy.IsRealQuotaWindow(durationMinutes,
                hasUsedPercent, usedPercent, hasResetAt, resetsAt))
                return false;

            windowPlan = ReadString(window, "planType");
            bool shortWindow = durationMinutes < 1440;
            if (shortWindow)
            {
                usage.ShortUnlimited = false;
                usage.HasShortLimitState = true;
            }
            if (hasUsedPercent && !Double.IsNaN(usedPercent) &&
                !Double.IsInfinity(usedPercent) && usedPercent >= 0d)
            {
                int remaining = (int)Math.Floor(Math.Max(0d,
                    Math.Min(100d, 100d - usedPercent + 0.0001d)));
                if (shortWindow)
                {
                    usage.ShortRemaining = remaining;
                    usage.HasShortRemaining = true;
                }
                else
                {
                    usage.WeeklyRemaining = remaining;
                    usage.HasWeeklyRemaining = true;
                }
            }

            if (hasResetAt && ResetCreditDetails.IsValidExpiry(resetsAt))
            {
                DateTime resetLocal = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(resetsAt).ToLocalTime();
                string resetText = shortWindow && resetLocal.Date == DateTime.Now.Date
                    ? resetLocal.ToString("HH:mm", CultureInfo.CurrentCulture)
                    : resetLocal.ToString("M月d日 HH:mm", CultureInfo.CurrentCulture);
                if (shortWindow)
                {
                    usage.ShortResetText = resetText;
                    usage.HasShortResetText = true;
                }
                else
                {
                    usage.WeeklyResetText = resetText;
                    usage.HasWeeklyResetText = true;
                }
            }
            return true;
        }

        private static string NormalizeRateLimitStatus(string reachedType,
            IDictionary<string, object> primary, IDictionary<string, object> secondary)
        {
            if (String.IsNullOrWhiteSpace(reachedType) ||
                String.Equals(reachedType, "none", StringComparison.OrdinalIgnoreCase))
                return "正常";

            string normalized = reachedType.Trim().ToLowerInvariant();
            if (normalized.Contains("primary")) return WindowReachedStatus(primary);
            if (normalized.Contains("secondary")) return WindowReachedStatus(secondary);
            if (normalized.Contains("credit")) return "积分不足";
            return reachedType.Trim();
        }

        private static string WindowReachedStatus(IDictionary<string, object> window)
        {
            long duration;
            if (!TryReadLong(window, "windowDurationMins", out duration) || duration <= 0)
                return "额度受限";
            return duration < 1440 ? "短期受限" : "周额度受限";
        }

        private static bool ParseTokenUsage(IDictionary<string, object> result, UsageData usage)
        {
            IDictionary<string, object> summary = ReadObject(result, "summary");
            long lifetimeTokens;
            if (summary == null || !TryReadLong(summary, "lifetimeTokens", out lifetimeTokens) || lifetimeTokens < 0)
                return false;
            usage.LifetimeTokens = lifetimeTokens;
            usage.ProfileTokensText = FormatLifetimeTokens(lifetimeTokens);
            return true;
        }

        internal static string FormatLifetimeTokens(long value)
        {
            if (value >= 100000000L)
                return (value / 100000000d).ToString("0.#", CultureInfo.InvariantCulture) + "亿";
            if (value >= 10000L)
                return (value / 10000d).ToString("0.#", CultureInfo.InvariantCulture) + "万";
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string NormalizePlan(string plan)
        {
            if (String.Equals(plan, "pro", StringComparison.OrdinalIgnoreCase)) return "Pro";
            if (String.Equals(plan, "plus", StringComparison.OrdinalIgnoreCase)) return "Plus";
            if (String.Equals(plan, "free", StringComparison.OrdinalIgnoreCase)) return "Free";
            if (String.Equals(plan, "team", StringComparison.OrdinalIgnoreCase)) return "Team";
            return plan.Trim();
        }

        private static Dictionary<string, object> ObjectOf(string key, object value)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result[key] = value;
            return result;
        }

        private static IDictionary<string, object> AsObject(object value)
        {
            return value as IDictionary<string, object>;
        }

        private static IDictionary<string, object> ReadObject(IDictionary<string, object> source, string key)
        {
            if (source == null)
                return null;
            object value;
            return source.TryGetValue(key, out value) ? AsObject(value) : null;
        }

        private static string ReadString(IDictionary<string, object> source, string key)
        {
            if (source == null)
                return null;
            object value;
            return source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static bool HasNumber(IDictionary<string, object> source, string key)
        {
            double ignored;
            return TryReadDouble(source, key, out ignored);
        }

        private static bool TryReadDouble(IDictionary<string, object> source, string key, out double value)
        {
            value = 0;
            if (source == null)
                return false;
            object raw;
            if (!source.TryGetValue(key, out raw) || raw == null)
                return false;
            return Double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadLong(IDictionary<string, object> source, string key, out long value)
        {
            value = 0;
            if (source == null)
                return false;
            object raw;
            if (!source.TryGetValue(key, out raw) || raw == null)
                return false;
            if (raw is long) { value = (long)raw; return true; }
            if (raw is int) { value = (int)raw; return true; }
            return Int64.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool MatchesId(IDictionary<string, object> message, int requestId)
        {
            object id;
            if (!message.TryGetValue("id", out id) || id == null)
                return false;
            int parsed;
            return Int32.TryParse(Convert.ToString(id, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed == requestId;
        }

        private static string ResolveCodexExecutable()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (!String.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            string besideOverlay = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex.exe");
            if (File.Exists(besideOverlay))
                return besideOverlay;

            foreach (Process chatGpt in Process.GetProcessesByName("ChatGPT"))
            {
                try
                {
                    string appFolder = Path.GetDirectoryName(chatGpt.MainModule.FileName);
                    string candidate = Path.Combine(appFolder, "resources", "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
                finally { chatGpt.Dispose(); }
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] npmCandidates = new[]
            {
                Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules",
                    "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"),
                Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules",
                    "@openai", "codex-win32-arm64", "vendor", "aarch64-pc-windows-msvc", "bin", "codex.exe")
            };
            foreach (string npmCandidate in npmCandidates)
            {
                if (File.Exists(npmCandidate))
                    return npmCandidate;
            }

            string desktopCliRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI", "Codex", "bin");
            try
            {
                if (Directory.Exists(desktopCliRoot))
                {
                    string[] versions = Directory.GetDirectories(desktopCliRoot);
                    Array.Sort(versions, delegate(string a, string b)
                    { return Directory.GetLastWriteTimeUtc(a).CompareTo(Directory.GetLastWriteTimeUtc(b)); });
                    for (int index = versions.Length - 1; index >= 0; index--)
                    {
                        string candidate = Path.Combine(versions[index], "codex.exe");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }
            catch { }

            string path = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (string folder in path.Split(Path.PathSeparator))
            {
                string cleanFolder = folder.Trim().Trim('"');
                if (cleanFolder.Length == 0)
                    continue;
                try
                {
                    string candidate = Path.Combine(cleanFolder, "codex.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }

            return "codex.exe";
        }

        private void ResetProcess()
        {
            initialized = false;
            activeCodexHome = String.Empty;
            if (inputWriter != null)
            {
                try { inputWriter.Dispose(); } catch { }
                inputWriter = null;
            }
            Process current = process;
            process = null;
            if (current != null)
            {
                try { current.StandardInput.Close(); }
                catch { }
                try
                {
                    if (!current.HasExited)
                    {
                        current.Kill();
                        current.WaitForExit(1000);
                    }
                }
                catch { }
                current.Dispose();
            }
            if (outputLines != null)
            {
                try { outputLines.CompleteAdding(); }
                catch { }
                outputLines.Dispose();
                outputLines = null;
            }
        }

        public void Dispose()
        {
            lock (gate)
                ResetProcess();
        }
    }
}
