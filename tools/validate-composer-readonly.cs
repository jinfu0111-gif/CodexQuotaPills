using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

// Exercise the production background reader without creating a form, starting
// account/resume services, reading editor contents or invoking any UI action.
internal static class ValidateComposerReadonly
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [MTAThread] private static int Main(string[] args)
    {
        Assembly assembly = Assembly.LoadFrom(args[0]);
        Type probeType = assembly.GetType("CodexUsageOverlay.ComposerWindowProbe", true);
        Type policy = assembly.GetType("CodexUsageOverlay.ComposerPlacementPolicy", true);
        Type visibilityType = assembly.GetType("CodexUsageOverlay.ComposerVisibilityGate", true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object probe = Activator.CreateInstance(probeType, flags, null, new object[] { null }, null);
        var read = probeType.GetMethod("Read", flags);
        object visibility = Activator.CreateInstance(visibilityType, true);
        var resolve = visibilityType.GetMethod("Resolve", flags);
        Type placement = assembly.GetType("CodexUsageOverlay.OverlayPlacement", true);
        IntPtr handle = IntPtr.Zero;
        foreach (Process process in Process.GetProcessesByName("ChatGPT"))
            if (process.MainWindowHandle != IntPtr.Zero) { handle = process.MainWindowHandle; break; }
        if (handle == IntPtr.Zero) { Console.WriteLine("WindowFound=False"); return 2; }
        var root = System.Windows.Automation.AutomationElement.FromHandle(handle);
        var native = root.Current.BoundingRectangle;
        Rectangle window = Rectangle.FromLTRB((int)Math.Floor(native.Left), (int)Math.Floor(native.Top),
            (int)Math.Ceiling(native.Right), (int)Math.Ceiling(native.Bottom));
        float scale = GetDpiForWindow(handle) / 96f;
        int available = 0, fresh = 0, attached = 0;
        int visibleDecisions = 0, hiddenDecisions = 0;
        Rectangle lastHost = Rectangle.Empty;
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < 40; i++)
            {
                object snapshot = read.Invoke(probe, new object[] { handle, window, scale });
                DateTime now = DateTime.UtcNow;
                if (snapshot != null)
                {
                    available++;
                    bool valid = (bool)policy.GetMethod("IsFresh", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { snapshot, handle, window, scale, now });
                    if (valid) fresh++;
                    Rectangle host = (Rectangle)snapshot.GetType().GetField("ToolbarHost", flags).GetValue(snapshot);
                    Rectangle pane = (Rectangle)snapshot.GetType().GetField("Pane", flags).GetValue(snapshot);
                    if (valid && !host.IsEmpty && pane.Contains(host)) { attached++; lastHost = host; }
                }
                object[] parameters = { handle, window, window, new Size((int)(140 * scale), (int)(28 * scale)),
                    scale, snapshot, now, Enum.Parse(placement, "ComposerBottom") };
                Rectangle decision = (Rectangle)resolve.Invoke(visibility, parameters);
                if (parameters[7].ToString() != "ComposerBottom") throw new InvalidOperationException("Input mode escaped to another placement");
                if (decision.IsEmpty) hiddenDecisions++;
                else
                {
                    Rectangle host = (Rectangle)snapshot.GetType().GetField("ToolbarHost", flags).GetValue(snapshot);
                    if (!host.Contains(decision)) throw new InvalidOperationException("Visible decision is outside toolbar");
                    visibleDecisions++;
                }
                Thread.Sleep(75);
            }
        }
        finally { ((IDisposable)probe).Dispose(); }
        Console.WriteLine("Samples=40 Available=" + available + " Fresh=" + fresh + " Attached=" + attached);
        Console.WriteLine("LastHost=" + lastHost + " ElapsedMilliseconds=" + watch.ElapsedMilliseconds);
        Console.WriteLine("StableVisibleDecisions=" + visibleDecisions + " HiddenDecisions=" + hiddenDecisions);
        Console.WriteLine("VisibleWindowsCreated=0 MessagesSent=0 EditorContentsRead=0 SettingsWrites=0");
        return attached > 0 ? (visibleDecisions > 0 ? 0 : 1) : (hiddenDecisions == 40 ? 0 : 1);
    }
}
