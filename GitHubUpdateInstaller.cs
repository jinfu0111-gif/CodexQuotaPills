using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal sealed class GitHubUpdateStage
    {
        public string Directory;
        public string Executable;
        public string Version;
        public string ExecutableHash;
    }

    internal static class GitHubUpdateInstaller
    {
        internal static GitHubUpdateStage DownloadAndStage(GitHubReleaseUpdateSnapshot release)
        {
            if (!GitHubReleaseUpdateService.CanInstall(release)) throw new InvalidDataException("发布包缺少可信校验信息。");
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPills", "GitHubUpdates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "download.zip");
            Download(release.DownloadUrl, zip, release.AssetSize);
            VerifyHash(zip, release.AssetSha256);
            string target = Path.Combine(root, "CodexQuotaPills-" + release.LatestVersion + "-portable");
            ExtractValidated(zip, target, "CodexQuotaPills-" + release.LatestVersion + "-portable/");
            string executable = Path.Combine(target, "CodexQuotaPills.exe");
            if (!File.Exists(executable) || FileVersionInfo.GetVersionInfo(executable).FileVersion != release.LatestVersion + ".0")
                throw new InvalidDataException("程序版本与发布信息不一致。");
            GitHubUpdateStage stage = new GitHubUpdateStage();
            stage.Directory = target; stage.Executable = executable; stage.Version = release.LatestVersion;
            stage.ExecutableHash = Hash(executable);
            return stage;
        }

        internal static Process Apply(GitHubUpdateStage stage)
        {
            string managedRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPills", "GitHubUpdates")) + Path.DirectorySeparatorChar;
            if (stage == null || !Path.GetFullPath(stage.Directory).StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase) ||
                stage.Executable != Path.Combine(stage.Directory, "CodexQuotaPills.exe")) throw new InvalidDataException("无效的更新目录。");
            VerifyHash(stage.Executable, stage.ExecutableHash);
            string attempt = Path.Combine(Path.GetDirectoryName(stage.Directory), "apply-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(attempt);
            string helper = Path.Combine(attempt, "apply-update.ps1");
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexUsageOverlay.ApplyPillsUpdate.ps1"))
            using (FileStream file = new FileStream(helper, FileMode.CreateNew)) resource.CopyTo(file);
            string plan = Path.Combine(attempt, "plan.json");
            File.WriteAllText(plan, new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "OldExe", Path.GetFullPath(Assembly.GetExecutingAssembly().Location) },
                { "NewExe", stage.Executable }, { "Version", stage.Version }, { "Sha256", stage.ExecutableHash }
            }), new UTF8Encoding(false));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            // Arguments are data; use -File, never construct an executable PowerShell expression.
            ProcessStartInfo start = new ProcessStartInfo(powershell,
                "-NoProfile -NonInteractive -File " + Quote(helper) + " -PlanPath " + Quote(plan));
            start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
            return Process.Start(start);
        }

        private static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new InvalidDataException("无效的更新路径。");
            return "\"" + value + "\"";
        }

        private static void Download(string url, string path, long expectedSize)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            Uri current = new Uri(url);
            for (int redirects = 0; redirects <= 3; redirects++)
            {
                if (current.Scheme != "https" || !current.IsDefaultPort || !String.IsNullOrEmpty(current.UserInfo) ||
                    (current.Host != "github.com" && current.Host != "release-assets.githubusercontent.com"))
                    throw new InvalidDataException("下载跳转地址不可信。");
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(current);
                request.UserAgent = "CodexQuotaPills/" + GitHubReleaseUpdateService.CurrentVersion;
                request.AllowAutoRedirect = false; request.UseDefaultCredentials = false; request.Credentials = null;
                request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        Uri next;
                        if (!Uri.TryCreate(current, response.Headers[HttpResponseHeader.Location], out next))
                            throw new InvalidDataException("下载跳转无效。");
                        // Redirects may only leave GitHub for its release asset host.
                        if (next.Host != "release-assets.githubusercontent.com") throw new InvalidDataException("下载跳转地址不可信。");
                        current = next; continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK ||
                        (response.ContentLength >= 0 && response.ContentLength != expectedSize)) throw new InvalidDataException("发布包大小不匹配。");
                    using (Stream stream = response.GetResponseStream())
                    using (FileStream file = new FileStream(path, FileMode.CreateNew))
                    {
                        byte[] buffer = new byte[32768]; int read;
                        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (file.Length + read > expectedSize || DateTime.UtcNow > deadline) throw new InvalidDataException("下载超过限制。");
                            file.Write(buffer, 0, read);
                        }
                        if (file.Length != expectedSize) throw new InvalidDataException("发布包下载不完整。");
                    }
                    return;
                }
            }
            throw new InvalidDataException("下载跳转过多。");
        }

        internal static void ExtractValidated(string zip, string target, string prefix)
        {
            if (Directory.Exists(target)) throw new InvalidDataException("更新目录已存在。");
            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                if (archive.Entries.Count > 500) throw new InvalidDataException("发布包文件过多。");
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                // Validate the entire archive before writing any entry.
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.IndexOf('\\') >= 0 ||
                        name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0 || !names.Add(name) ||
                        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidDataException("发布包含无效文件路径。");
                    string relative = name.Substring(prefix.Length);
                    foreach (string segment in relative.Split('/'))
                        if (segment == "." || segment == ".." || segment.EndsWith(".") || segment.EndsWith(" ") ||
                            System.Text.RegularExpressions.Regex.IsMatch(segment,
                                @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            throw new InvalidDataException("发布包含无效文件路径。");
                    string resolved = Path.GetFullPath(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (relative.Length > 0 && !resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("发布包路径越界。");
                    total += entry.Length;
                    if (entry.Length > 32 * 1024 * 1024 || total > 128 * 1024 * 1024)
                        throw new InvalidDataException("发布包解压大小超过限制。");
                }
                Directory.CreateDirectory(target);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string relative = entry.FullName.Substring(prefix.Length);
                    if (relative.Length == 0) continue;
                    string destination = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, false);
                }
            }
        }

        internal static string Hash(string file)
        {
            using (SHA256 hash = SHA256.Create())
            using (Stream stream = File.OpenRead(file))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static void VerifyHash(string file, string expected)
        {
            if (String.IsNullOrEmpty(expected) || !String.Equals(Hash(file), expected, StringComparison.Ordinal))
                throw new InvalidDataException("SHA-256 校验失败，更新已停止。");
        }
    }
}
