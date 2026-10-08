using System;
using System.IO;
using System.Security.Cryptography;
using Blues19.CodexInstaller;

namespace CodexUsageOverlay
{
    internal static class MsixUpdaterTests
    {
        internal static void TargetsOfficialPackage()
        {
            Assert(CodexProduct.StoreProductId == "9PLM9XGG6VKS", "wrong Store product");
            Assert(CodexProduct.PackageFamilyName == "OpenAI.Codex_2p2nqsd0c76g0",
                "wrong Codex package family");
            InstalledPackage parsed = AppxInstaller.ParseFullName(
                "OpenAI.Codex_26.928.1024.0_x64__2p2nqsd0c76g0");
            Assert(parsed != null && parsed.Name == CodexProduct.PackageName &&
                parsed.Version == new Version(26, 928, 1024, 0), "package version parsing failed");
        }

        internal static void RejectsUnverifiableCache()
        {
            PackageInfo package = new PackageInfo();
            package.SizeBytes = 8;
            package.DigestAlgorithm = "SHA256";
            package.DigestBase64 = "not-the-real-digest";
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 80, 75, 3, 4, 0, 0, 0, 0 });
                Assert(!Downloader.VerifyCachedPackage(path, package),
                    "cache with the wrong digest was accepted");
                package.DigestAlgorithm = "unknown";
                Assert(!Downloader.VerifyCachedPackage(path, package),
                    "cache with unknown digest algorithm was accepted");
                Assert(!Downloader.IsSupportedDigest("unknown"), "unknown algorithm was supported");
            }
            finally { File.Delete(path); }
        }

        internal static void KeepsUpdaterStateSeparate()
        {
            Assert(Paths.BaseDir.IndexOf("MsixUpdater", StringComparison.OrdinalIgnoreCase) >= 0,
                "updater state is not separated");
            Assert(Paths.LegacyPackageDirPath.StartsWith(Paths.BaseDir,
                StringComparison.OrdinalIgnoreCase), "legacy cache path changed unexpectedly");
            Assert(Paths.PackageDirPath.StartsWith(Paths.ExeDir,
                StringComparison.OrdinalIgnoreCase) ||
                Paths.PackageDirPath.StartsWith(Paths.BaseDir,
                StringComparison.OrdinalIgnoreCase),
                "package cache escaped app-owned directories");
            Assert(Paths.ChoosePackageDir(@"F:\portable", @"C:\legacy", true) ==
                @"F:\portable\packages", "writable portable cache was not preferred");
            Assert(Paths.ChoosePackageDir(@"F:\portable", @"C:\legacy", false) ==
                @"C:\legacy", "read-only portable cache did not use fallback");
        }

        internal static void MigratesVerifiedCacheWithoutRemovingOriginal()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "cqp-cache-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string source = Path.Combine(directory, "old.msix");
                string destination = Path.Combine(directory, "visible", "new.msix");
                byte[] bytes = new byte[] { 80, 75, 3, 4, 0, 0, 0, 0 };
                File.WriteAllBytes(source, bytes);
                PackageInfo package = new PackageInfo();
                package.SizeBytes = bytes.Length;
                package.DigestAlgorithm = "SHA256";
                using (SHA256 hash = SHA256.Create())
                    package.DigestBase64 = Convert.ToBase64String(hash.ComputeHash(bytes));
                Assert(Downloader.TryCopyVerifiedCache(source, destination, package),
                    "verified cache was not migrated");
                Assert(File.Exists(source) && Downloader.VerifyCachedPackage(destination, package),
                    "migration removed the original or corrupted the destination");
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
