using System;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace CodexUsageOverlay
{
    internal static class GitHubReleaseUpdateTests
    {
        public static void ManualCheckBypassesOnlyTimeThrottle()
        {
            DateTime now = new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Utc);
            DateTime recent = now.AddMinutes(-5);
            Assert(!GitHubReleaseUpdateService.CanStartCheck(
                false, false, now, recent, false),
                "automatic check bypassed the 24-hour throttle");
            Assert(GitHubReleaseUpdateService.CanStartCheck(
                false, false, now, recent, true),
                "manual check did not bypass the time throttle");
            Assert(!GitHubReleaseUpdateService.CanStartCheck(
                false, true, now, recent, true),
                "manual check bypassed an active request");
            Assert(!GitHubReleaseUpdateService.CanStartCheck(
                true, false, now, recent, true),
                "manual check bypassed disposal");
            Assert(GitHubReleaseUpdateService.UpdatesEnabled && GitHubReleaseUpdateService.Repository == "jinfu0111-gif/CodexQuotaPills",
                "updater is not bound to the owner's repository");
        }

        internal static void ReleasesAndArchivesAreValidated()
        {
            string prefix = "https://github.com/" + GitHubReleaseUpdateService.Repository;
            GitHubReleaseUpdateService.Release release = new GitHubReleaseUpdateService.Release {
                tag_name = "v9.0.0", html_url = prefix + "/releases/tag/v9.0.0",
                assets = new[] { new GitHubReleaseUpdateService.Asset { name = "CodexQuotaPills-9.0.0-portable.zip",
                    state = "uploaded", size = 123, digest = "sha256:" + new string('a',64),
                    browser_download_url = prefix + "/releases/download/v9.0.0/CodexQuotaPills-9.0.0-portable.zip" } }
            };
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Assert(GitHubReleaseUpdateService.CanInstall(GitHubReleaseUpdateService.ParseRelease(serializer.Serialize(release))), "valid release rejected");
            release.prerelease = true; Assert(GitHubReleaseUpdateService.ParseRelease(serializer.Serialize(release)) == null, "prerelease accepted");
            release.prerelease = false; release.draft = true;
            Assert(GitHubReleaseUpdateService.ParseRelease(serializer.Serialize(release)) == null, "draft accepted"); release.draft = false;
            release.assets[0].digest = null;
            Assert(!GitHubReleaseUpdateService.CanInstall(GitHubReleaseUpdateService.ParseRelease(serializer.Serialize(release))), "unchecked asset accepted");
            release.assets[0].digest = "sha256:" + new string('a',64); release.assets[0].browser_download_url = "https://evil.example/update.zip";
            Assert(!GitHubReleaseUpdateService.CanInstall(GitHubReleaseUpdateService.ParseRelease(serializer.Serialize(release))), "untrusted download accepted");
            Assert(!GitHubReleaseUpdateService.IsAllowedReleaseUrl(prefix + "/releases/tag/v9.0.0-rc1"), "prerelease tag accepted");
            Assert(!GitHubReleaseUpdateService.IsAllowedReleaseUrl(prefix + "/releases/tag/v9.0.0?x=1"), "query accepted");
            release.html_url = "https://github.com/other/repo/releases/tag/v9.0.0";
            Assert(GitHubReleaseUpdateService.ParseRelease(serializer.Serialize(release)) == null, "wrong repository accepted");
            string root = Path.Combine(Path.GetTempPath(), "pills-archive-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            int index = 0;
            foreach (string entry in new[] { "pkg/../outside.exe", "pkg/folder/../../outside.exe", "pkg/evil:stream", "pkg/back\\slash", "wrong/file", "pkg/CON.txt" })
            {
                string zip = Path.Combine(root, (++index) + ".zip");
                using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry(entry);
                bool rejected = false; string target = Path.Combine(root, "target" + index);
                try { GitHubUpdateInstaller.ExtractValidated(zip, target, "pkg/"); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && !Directory.Exists(target), "unsafe archive wrote files: " + entry); File.Delete(zip);
            }
            string valid = Path.Combine(root, "valid.zip");
            using (ZipArchive archive = ZipFile.Open(valid, ZipArchiveMode.Create))
            using (StreamWriter writer = new StreamWriter(archive.CreateEntry("pkg/readme.txt").Open())) writer.Write("ok");
            bool mismatch = false;
            try { GitHubUpdateInstaller.VerifyHash(valid, new string('0',64)); } catch (InvalidDataException) { mismatch = true; }
            Assert(mismatch, "checksum mismatch ignored");
            GitHubUpdateInstaller.VerifyHash(valid, GitHubUpdateInstaller.Hash(valid));
            string accepted = Path.Combine(root, "accepted"); GitHubUpdateInstaller.ExtractValidated(valid, accepted, "pkg/");
            Assert(File.ReadAllText(Path.Combine(accepted, "readme.txt")) == "ok", "valid archive not extracted");
            File.Delete(Path.Combine(accepted, "readme.txt")); Directory.Delete(accepted); File.Delete(valid); Directory.Delete(root);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
