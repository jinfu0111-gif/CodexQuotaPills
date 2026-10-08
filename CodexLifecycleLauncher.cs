using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace CodexUsageOverlay
{
    // Ask the Windows service to launch in our interactive session instead of
    // inheriting a Codex/tool job that may be terminated during an app update.
    // The demand-only task has no triggers and is deleted after acknowledgement.
    internal static class CodexLifecycleLauncher
    {
        private const string ReadyPrefix = "Local\\CodexQuotaPills-WatcherReady-";

        internal static string ReadyEventName(string token)
        {
            Guid id;
            return Guid.TryParseExact(token, "N", out id) ? ReadyPrefix + id.ToString("N") : null;
        }

        internal static void Acknowledge(string token)
        {
            string name = ReadyEventName(token);
            if (name == null) return;
            try { using (EventWaitHandle ready = EventWaitHandle.OpenExisting(name)) ready.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
        }

        internal static bool TryLaunch(string executablePath)
        {
            string token = Guid.NewGuid().ToString("N");
            string taskName = "CodexQuotaPills-Launch-" + token;
            List<object> objects = new List<object>();
            object folder = null;
            bool registered = false;
            try
            {
                executablePath = Path.GetFullPath(executablePath);
                if (!File.Exists(executablePath) ||
                    !String.Equals(Path.GetFileName(executablePath), "CodexQuotaPills.exe", StringComparison.OrdinalIgnoreCase)) return false;
                using (EventWaitHandle ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName(token)))
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                using (Process current = Process.GetCurrentProcess())
                {
                    if (identity.User == null || current.SessionId <= 0) return false;
                    object service = Keep(objects, Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")));
                    Call(service, "Connect");
                    folder = Keep(objects, Call(service, "GetFolder", "\\"));
                    object definition = Keep(objects, Call(service, "NewTask", 0));
                    object info = Keep(objects, Get(definition, "RegistrationInfo"));
                    Set(info, "Source", "CodexQuotaPills");
                    Set(info, "Description", "One-time interactive launch of the Codex Quota Pills watcher.");
                    object principal = Keep(objects, Get(definition, "Principal"));
                    Set(principal, "UserId", identity.User.Value);
                    Set(principal, "LogonType", 3); // TASK_LOGON_INTERACTIVE_TOKEN; no password.
                    Set(principal, "RunLevel", 0); // Least privilege.
                    object settings = Keep(objects, Get(definition, "Settings"));
                    Set(settings, "Enabled", true);
                    Set(settings, "AllowDemandStart", true);
                    Set(settings, "ExecutionTimeLimit", "PT0S");
                    Set(settings, "DisallowStartIfOnBatteries", false);
                    Set(settings, "StopIfGoingOnBatteries", false);
                    Set(settings, "RunOnlyIfIdle", false);
                    Set(settings, "MultipleInstances", 2);
                    object actions = Keep(objects, Get(definition, "Actions"));
                    object action = Keep(objects, Call(actions, "Create", 0));
                    Set(action, "Path", executablePath);
                    Set(action, "Arguments", "--watcher --watcher-ready=" + token);
                    Set(action, "WorkingDirectory", Path.GetDirectoryName(executablePath));
                    object task = Keep(objects, Call(folder, "RegisterTaskDefinition", taskName,
                        definition, 2, identity.User.Value, null, 3, null)); // CREATE, never overwrite.
                    registered = true;
                    Keep(objects, Call(task, "RunEx", null, 4, current.SessionId, null));
                    bool started = ready.WaitOne(10000);
                    Log(started ? "independent-watcher-ready" : "independent-watcher-timeout");
                    return started;
                }
            }
            catch (Exception error)
            {
                Log("independent-launch-failed:" + error.GetType().Name);
                return false;
            }
            finally
            {
                // Deleting the definition does not terminate its running process.
                // Only the unique demand-only task created by this call is touched.
                if (registered && folder != null)
                {
                    try { Call(folder, "DeleteTask", taskName, 0); }
                    catch { Log("temporary-launch-task-cleanup-failed:" + token); }
                }
                for (int i = objects.Count - 1; i >= 0; i--)
                    try { if (Marshal.IsComObject(objects[i])) Marshal.FinalReleaseComObject(objects[i]); } catch { }
            }
        }

        internal static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaPills");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "lifecycle.log");
                if (File.Exists(file) && new FileInfo(file).Length > 65536) File.WriteAllText(file, String.Empty);
                File.AppendAllText(file, DateTime.UtcNow.ToString("o") + " pid=" + Process.GetCurrentProcess().Id + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private static object Keep(List<object> objects, object value) { if (value != null) objects.Add(value); return value; }
        private static object Call(object value, string name, params object[] args)
        { return value.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, value, args); }
        private static object Get(object value, string name)
        { return value.GetType().InvokeMember(name, BindingFlags.GetProperty, null, value, null); }
        private static void Set(object value, string name, object data)
        { value.GetType().InvokeMember(name, BindingFlags.SetProperty, null, value, new[] { data }); }
    }
}
