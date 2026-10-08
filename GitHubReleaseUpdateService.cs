using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal sealed class GitHubReleaseUpdateSnapshot
    {
        public string CurrentVersion = GitHubReleaseUpdateService.CurrentVersion;
        public string LatestVersion = String.Empty;
        public string ReleaseUrl = String.Empty;
        public bool UpdateAvailable;
        public bool IsChecking;
        public DateTime? LastCheckedUtc;
        public string LastError = String.Empty;
        public string DownloadUrl = String.Empty;
        public string AssetSha256 = String.Empty;
        public long AssetSize;

        public GitHubReleaseUpdateSnapshot Clone()
        {
            return (GitHubReleaseUpdateSnapshot)MemberwiseClone();
        }
    }

    internal sealed class GitHubReleaseUpdateService : IDisposable
    {
        public static readonly string CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        public static readonly bool UpdatesEnabled = true;
        public const string Repository = "jinfu0111-gif/CodexQuotaPills";
        public const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

        private const string AllowedReleasePrefix = "/" + Repository + "/releases/tag/";
        private const int RequestTimeoutMilliseconds = 10000;
        private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromHours(24d);
        private static readonly Regex StableVersionPattern = new Regex(
            @"^(?:v)?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly object sync = new object();
        private GitHubReleaseUpdateSnapshot state = new GitHubReleaseUpdateSnapshot();
        private DateTime lastCheckAttemptUtc = DateTime.MinValue;
        private HttpWebRequest activeRequest;
        private bool checkRunning;
        private bool disposed;

        public GitHubReleaseUpdateSnapshot Snapshot()
        {
            lock (sync)
                return state.Clone();
        }

        public void RequestCheck()
        {
            RequestCheck(false);
        }

        public bool RequestCheck(bool force)
        {
            if (!UpdatesEnabled)
                return false;
            bool shouldStart = false;
            lock (sync)
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (CanStartCheck(disposed, checkRunning, nowUtc,
                    lastCheckAttemptUtc, force))
                {
                    checkRunning = true;
                    lastCheckAttemptUtc = nowUtc;
                    state.IsChecking = true;
                    shouldStart = true;
                }
            }

            if (!shouldStart)
                return false;

            try
            {
                if (ThreadPool.QueueUserWorkItem(delegate { CheckLatestRelease(); }))
                    return true;
            }
            catch
            {
            }

            lock (sync)
            {
                checkRunning = false;
                state.IsChecking = false;
            }
            return false;
        }

        internal static bool CanStartCheck(
            bool isDisposed,
            bool isRunning,
            DateTime nowUtc,
            DateTime lastAttemptUtc,
            bool force)
        {
            if (isDisposed || isRunning)
                return false;
            if (force)
                return true;
            return nowUtc >= lastAttemptUtc &&
                nowUtc - lastAttemptUtc >= MinimumCheckInterval;
        }

        private void CheckLatestRelease()
        {
            try
            {
                GitHubReleaseUpdateSnapshot updated = ParseRelease(ResolveLatestReleaseJson());
                if (updated == null)
                    throw new InvalidDataException("发布信息无效，请稍后重试。");
                updated.LastCheckedUtc = DateTime.UtcNow;
                lock (sync)
                {
                    if (!disposed)
                        state = updated;
                }
            }
            catch (Exception error)
            {
                lock (sync)
                {
                    if (!disposed)
                    {
                        state = new GitHubReleaseUpdateSnapshot();
                        state.LastError = error is WebException ? "暂时无法检查 GitHub：网络不可用，或尚无正式版本。" : "发布信息无法验证。";
                    }
                }
            }
            finally
            {
                lock (sync)
                {
                    activeRequest = null;
                    checkRunning = false;
                    state.IsChecking = false;
                }
            }
        }

        private string ResolveLatestReleaseJson()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestReleaseUrl);
            request.Method = "GET";
            request.Accept = "application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            request.UserAgent = "CodexUsageOverlay/" + CurrentVersion;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.UseDefaultCredentials = false;
            request.Credentials = null;
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";

            lock (sync)
            {
                if (disposed)
                {
                    request.Abort();
                    throw new ObjectDisposedException("GitHubReleaseUpdateService");
                }
                activeRequest = request;
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new WebException("Invalid release response.");
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream bytes = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (bytes.Length + read > 1024 * 1024) throw new InvalidDataException("Release response too large.");
                        bytes.Write(buffer, 0, read);
                    }
                    return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                }
            }
        }

        internal static GitHubReleaseUpdateSnapshot ParseRelease(string json)
        {
            Release release = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<Release>(json);
            if (release == null || release.draft || release.prerelease ||
                release.html_url != "https://github.com/" + Repository + "/releases/tag/" + release.tag_name) return null;
            GitHubReleaseUpdateSnapshot result = EvaluateReleaseUrl(release.html_url);
            if (result == null) return null;
            string name = "CodexQuotaPills-" + result.LatestVersion + "-portable.zip";
            string url = "https://github.com/" + Repository + "/releases/download/" + release.tag_name + "/" + name;
            Asset selected = null;
            foreach (Asset asset in release.assets ?? new Asset[0])
            {
                if (asset == null || asset.name != name) continue;
                if (selected != null) return null; // Ambiguous releases cannot be installed.
                selected = asset;
            }
            if (selected != null && selected.state == "uploaded" && selected.browser_download_url == url &&
                selected.size > 0 && selected.size <= 64 * 1024 * 1024 &&
                Regex.IsMatch(selected.digest ?? "", "^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            {
                result.DownloadUrl = url;
                result.AssetSize = selected.size;
                result.AssetSha256 = selected.digest.Substring(7).ToLowerInvariant();
            }
            return result;
        }

        internal static bool CanInstall(GitHubReleaseUpdateSnapshot value)
        {
            return value != null && value.UpdateAvailable && IsAllowedReleaseUrl(value.ReleaseUrl) &&
                value.DownloadUrl == "https://github.com/" + Repository + "/releases/download/" +
                    value.ReleaseUrl.Substring(value.ReleaseUrl.LastIndexOf('/') + 1) +
                    "/CodexQuotaPills-" + value.LatestVersion + "-portable.zip" &&
                Regex.IsMatch(value.AssetSha256 ?? "", "^[0-9a-f]{64}$") &&
                value.AssetSize > 0 && value.AssetSize <= 64 * 1024 * 1024;
        }

        public sealed class Release
        {
            public string tag_name { get; set; }
            public string html_url { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public Asset[] assets { get; set; }
        }
        public sealed class Asset
        {
            public string name { get; set; }
            public string state { get; set; }
            public string browser_download_url { get; set; }
            public string digest { get; set; }
            public long size { get; set; }
        }

        internal static GitHubReleaseUpdateSnapshot EvaluateReleaseUrl(string releaseUrl)
        {
            if (!UpdatesEnabled)
                return null;
            string releaseTag;
            SemanticVersion current;
            SemanticVersion latest;
            if (!TryGetReleaseTag(releaseUrl, out releaseTag) ||
                !SemanticVersion.TryParse(CurrentVersion, out current) ||
                !SemanticVersion.TryParse(releaseTag, out latest))
                return null;

            GitHubReleaseUpdateSnapshot result = new GitHubReleaseUpdateSnapshot();
            result.LatestVersion = latest.DisplayVersion;
            result.ReleaseUrl = releaseUrl;
            result.UpdateAvailable = latest.CompareTo(current) > 0;
            return result;
        }

        internal static bool IsAllowedReleaseUrl(string value)
        {
            if (!UpdatesEnabled)
                return false;
            string releaseTag;
            SemanticVersion version;
            return TryGetReleaseTag(value, out releaseTag) &&
                SemanticVersion.TryParse(releaseTag, out version);
        }

        private static bool TryGetReleaseTag(string value, out string releaseTag)
        {
            releaseTag = String.Empty;
            if (String.IsNullOrWhiteSpace(value))
                return false;

            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Port != 443 || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                return false;

            string path = "/" + uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            if (!path.StartsWith(AllowedReleasePrefix, StringComparison.Ordinal))
                return false;

            string escapedTag = path.Substring(AllowedReleasePrefix.Length);
            if (escapedTag.Length == 0 || escapedTag.IndexOf('/') >= 0 ||
                escapedTag.IndexOf('\\') >= 0)
                return false;

            try { releaseTag = Uri.UnescapeDataString(escapedTag); }
            catch { return false; }
            return true;
        }

        public void Dispose()
        {
            HttpWebRequest request;
            lock (sync)
            {
                if (disposed)
                    return;
                disposed = true;
                state.IsChecking = false;
                request = activeRequest;
                activeRequest = null;
            }

            if (request != null)
            {
                try { request.Abort(); }
                catch { }
            }
        }

        private struct SemanticVersion : IComparable<SemanticVersion>
        {
            private ulong major;
            private ulong minor;
            private ulong patch;
            private string displayVersion;

            public string DisplayVersion
            {
                get { return displayVersion; }
            }

            public static bool TryParse(string value, out SemanticVersion version)
            {
                version = new SemanticVersion();
                if (String.IsNullOrEmpty(value))
                    return false;

                Match match = StableVersionPattern.Match(value);
                ulong major;
                ulong minor;
                ulong patch;
                if (!match.Success ||
                    !UInt64.TryParse(match.Groups[1].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out major) ||
                    !UInt64.TryParse(match.Groups[2].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out minor) ||
                    !UInt64.TryParse(match.Groups[3].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out patch))
                    return false;

                version.major = major;
                version.minor = minor;
                version.patch = patch;
                version.displayVersion = value.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? value.Substring(1)
                    : value;
                return true;
            }

            public int CompareTo(SemanticVersion other)
            {
                int result = major.CompareTo(other.major);
                if (result != 0) return result;
                result = minor.CompareTo(other.minor);
                if (result != 0) return result;
                return patch.CompareTo(other.patch);
            }
        }
    }
}
