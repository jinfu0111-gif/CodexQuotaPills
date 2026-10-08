using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CodexUsageOverlay
{
    internal enum CodexLifecycleSupervisorResult
    {
        Completed,
        AlreadyRunning
    }

    internal static class CodexLifecyclePolicy
    {
        internal static bool ShouldStartOverlay(
            bool codexRunning,
            bool overlayRunning)
        {
            // Restore a missing overlay even if a short Codex restart was not
            // observed between polls, or the overlay exited after Codex returned.
            return codexRunning && !overlayRunning;
        }

        internal static bool RetryAllowed(DateTime utcNow, DateTime nextAttemptUtc)
        {
            return utcNow >= nextAttemptUtc;
        }

        internal static DateTime? UpdateMissingSince(
            bool codexRunning,
            DateTime utcNow,
            DateTime? missingSinceUtc)
        {
            if (codexRunning)
                return null;
            return missingSinceUtc ?? utcNow;
        }

        internal static bool MissingGracePeriodElapsed(
            DateTime? missingSinceUtc,
            DateTime utcNow,
            TimeSpan gracePeriod)
        {
            return missingSinceUtc.HasValue &&
                utcNow - missingSinceUtc.Value >= gracePeriod;
        }
    }

    internal static class CodexDesktopProcess
    {
        internal static bool IsRunning()
        {
            Process[] processes = null;
            bool running = false;
            try
            {
                // The Windows Codex desktop app currently uses ChatGPT.exe. Do not
                // match codex.exe here: that is the CLI/app-server child process.
                processes = Process.GetProcessesByName("ChatGPT");
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            running = true;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (processes != null)
                {
                    foreach (Process process in processes)
                        process.Dispose();
                }
            }
            return running;
        }
    }

    internal static class CodexLifecycleSupervisor
    {
        private const string WatcherMutexName =
            "Local\\CodexQuotaPills-Watcher-7D474196";
        private const string StopEventName =
            "Local\\CodexQuotaPills-WatcherStop-7D474196";
        private const int PollMilliseconds = 1000;

        internal static CodexLifecycleSupervisorResult Run(string executablePath)
        {
            return Run(executablePath, null);
        }

        internal static CodexLifecycleSupervisorResult Run(string executablePath, string readyToken)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, WatcherMutexName, out created))
            {
                CodexLifecycleLauncher.Acknowledge(readyToken);
                if (!created)
                    return CodexLifecycleSupervisorResult.AlreadyRunning;

                using (EventWaitHandle stopEvent = new EventWaitHandle(
                    false, EventResetMode.AutoReset, StopEventName))
                {
                    Process overlay = null;
                    DateTime nextAttemptUtc = DateTime.MinValue;
                    CodexLifecycleLauncher.Log("watcher-started");
                    try
                    {
                        while (true)
                        {
                            bool codexRunning = CodexDesktopProcess.IsRunning();
                            bool overlayRunning = IsProcessRunning(overlay);
                            if (CodexLifecyclePolicy.ShouldStartOverlay(
                                codexRunning, overlayRunning) &&
                                CodexLifecyclePolicy.RetryAllowed(DateTime.UtcNow, nextAttemptUtc))
                            {
                                DisposeProcess(ref overlay);
                                nextAttemptUtc = DateTime.UtcNow.AddSeconds(5);
                                overlay = TryStartOverlay(executablePath);
                                CodexLifecycleLauncher.Log(overlay == null ? "overlay-start-failed" : "overlay-started:" + overlay.Id);
                            }

                            if (overlay != null && !IsProcessRunning(overlay))
                                DisposeProcess(ref overlay);

                            if (stopEvent.WaitOne(PollMilliseconds))
                                break;
                        }
                    }
                    finally
                    {
                        DisposeProcess(ref overlay);
                        CodexLifecycleLauncher.Log("watcher-stopped");
                    }
                }
            }
            return CodexLifecycleSupervisorResult.Completed;
        }

        internal static bool TryStartOverlayForCurrentSession(string executablePath)
        {
            if (!CodexDesktopProcess.IsRunning())
                return false;
            using (Process process = TryStartOverlay(executablePath))
                return process != null;
        }

        internal static void RequestStop()
        {
            try
            {
                using (EventWaitHandle stopEvent = EventWaitHandle.OpenExisting(StopEventName))
                    stopEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch
            {
            }
        }

        private static Process TryStartOverlay(string executablePath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                    return null;
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executablePath;
                startInfo.Arguments = "--codex-child";
                startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                return Process.Start(startInfo);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsProcessRunning(Process process)
        {
            if (process == null)
                return false;
            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void DisposeProcess(ref Process process)
        {
            if (process == null)
                return;
            process.Dispose();
            process = null;
        }
    }
}
