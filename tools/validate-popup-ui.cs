using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Isolated real WinForms verification: no OverlayForm, usage service, updater
// work, auto-resume service, original chats, settings writes, or startup changes.
internal static class PopupUiValidation
{
    private static Assembly assembly;
    private static readonly BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) { File.WriteAllText(args[1], "FAIL " + error); return 1; }
    }
    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        List<string> report = new List<string>();
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Rectangle window = new Rectangle(area.Left + 30, area.Top + 30,
            Math.Min(1000, area.Width - 60), Math.Min(740, area.Height - 60));
        Rectangle sidebar = new Rectangle(window.Location, new Size(52, window.Height));
        Rectangle anchor = new Rectangle(window.Left + 8, window.Bottom - 236, 36, 36);
        Set("Placement", Enum.Parse(Type("OverlayPlacement"), "Sidebar"));
        Set("Sidebar", sidebar); Set("WindowBounds", window); Set("WorkingArea", area); Set("Anchor", anchor);
        foreach (string name in new[] { "QuotaPillsSettingsForm", "ExitConfirmationForm", "AutoResumeForm", "CodexClientUpdateForm", "FirstRunGuideForm", "GitHubUpdateForm", "CapsulePromptForm" })
        {
            object settings = Activator.CreateInstance(Type("OverlaySettings"), true);
            Type("OverlaySettings").GetField("Theme", all).SetValue(settings, "RainbowText");
            object[] parameters = name == "GitHubUpdateForm" ? new object[] { null, (Rectangle?)anchor, true } :
                name == "CapsulePromptForm" ? new object[] { "Codex 正在运行", "隔离验证，不执行安装。", anchor, true } :
                name == "QuotaPillsSettingsForm" ? new object[] { settings, (Rectangle?)anchor } :
                name == "AutoResumeForm" ? new object[] { null, anchor } :
                name == "FirstRunGuideForm" ? new object[] { settings } : new object[] { anchor };
            using (Form form = (Form)Activator.CreateInstance(Type(name), all, null, parameters, null))
            {
                if (name == "CodexClientUpdateForm") Type(name).GetField("previewMode", all).SetValue(form, true);
                if (name == "FirstRunGuideForm") Type(name).GetMethod("UpdateAnchor", all).Invoke(form, new object[] { anchor, area });
                form.Show(); Pump();
                Verify(form, sidebar.Right, window, name);
                Rectangle moved = window; moved.Offset(20, -20);
                Rectangle movedSidebar = sidebar; movedSidebar.Offset(20, -20);
                Rectangle movedAnchor = anchor; movedAnchor.Offset(20, -20);
                Set("Sidebar", movedSidebar); Set("WindowBounds", moved); Set("Anchor", movedAnchor);
                Set("Dark", true); Set("ThemeRevision", 1);
                Pump(); Verify(form, movedSidebar.Right, moved, name);
                if (name != "FirstRunGuideForm" && form.BackColor.R >= 100) throw new Exception(name + " did not adopt dark theme");
                if (name == "ExitConfirmationForm")
                {
                    Button cancel = null;
                    foreach (Control control in form.Controls)
                        if (control is Button && control.Text == "取消") cancel = (Button)control;
                    if (cancel == null) throw new Exception("exit cancel button missing");
                    cancel.PerformClick();
                    if (form.DialogResult != DialogResult.Cancel) throw new Exception("capsule button broke cancel action");
                }
                if (name == "CapsulePromptForm")
                {
                    Button accept = form.AcceptButton as Button;
                    if (accept == null || accept.DialogResult != DialogResult.No) throw new Exception("installation safe default changed");
                }
                form.Close();
                report.Add("PASS " + name + " placement / follow / theme / close");
                Set("Sidebar", sidebar); Set("WindowBounds", window); Set("Anchor", anchor);
                Set("Dark", false); Set("ThemeRevision", 2);
            }
        }
        ValidateGuideStability(window, sidebar, area, anchor, report);
        ValidateNativeOwnerRecovery(window, area, anchor, report);
        ValidateButtonPainting(report);
        ValidateVisibilityMenu(window, sidebar, area, anchor, report);
        report.Add("MessagesSent=0"); report.Add("SettingsWrites=0"); report.Add("UpdaterActions=0");
        File.WriteAllLines(args[1], report.ToArray());
        return 0;
    }
    private static void ValidateNativeOwnerRecovery(Rectangle window, Rectangle area, Rectangle anchor, List<string> report)
    {
        object settings = Activator.CreateInstance(Type("OverlaySettings"), true);
        Type("OverlaySettings").GetField("Theme", all).SetValue(settings, "RainbowText");
        Set("Placement", Enum.Parse(Type("OverlayPlacement"), "TitleBar"));
        Set("WorkingArea", area); Set("Anchor", anchor);
        using (Form owner = new Form { Bounds = new Rectangle(window.Left + 100, window.Top + 100, 120, 60), TopMost = true, ShowInTaskbar = false })
        using (Form guide = (Form)Activator.CreateInstance(Type("FirstRunGuideForm"), all, null, new object[] { settings }, null))
        {
            owner.Show(); guide.Show(owner); Pump();
            // Reproduce the old exact Win32 ownership failure. A plain Visible
            // check (the old test) misses this, as does the managed TopMost flag.
            guide.TopMost = false; Pump();
            if (NativeTopmost(owner)) throw new Exception("old demotion failure did not reproduce");
            if (!owner.TopMost || !owner.Visible) throw new Exception("managed flag mismatch was not reproduced");
            report.Add("REPRO old popup demotion: owner.Visible/TopMost=true but native WS_EX_TOPMOST=false");
            MethodInfo recover = Type("OverlayWindowPresentation").GetMethod("Recover", all);
            IntPtr foreground = GetForegroundWindow();
            recover.Invoke(null, new object[] { owner });
            if (!NativeTopmost(owner) || !IsWindowVisible(owner.Handle)) throw new Exception("capsule native recovery failed");
            if (foreground != GetForegroundWindow()) throw new Exception("recovery stole keyboard focus");
            MethodInfo visibility = Type("PopupUiContext").GetMethod("SetPopupVisibility", all);
            object[] hide = { guide, false, false }; visibility.Invoke(null, hide); Pump();
            if (IsWindowVisible(guide.Handle) || !NativeTopmost(owner)) throw new Exception("hiding popup demoted owner");
            object[] restore = { guide, true, hide[2] }; visibility.Invoke(null, restore); Pump();
            if (!IsWindowVisible(guide.Handle) || !NativeTopmost(owner)) throw new Exception("popup failed recovery without owner demotion");
            guide.Close(); Pump();
            if (!owner.Visible || !IsWindowVisible(owner.Handle) || !NativeTopmost(owner)) throw new Exception("guide close lost native owner visibility / topmost");
            ShowWindow(owner.Handle, 0);
            if (IsWindowVisible(owner.Handle)) throw new Exception("native hidden state not reproduced");
            recover.Invoke(null, new object[] { owner });
            if (!IsWindowVisible(owner.Handle) || !NativeTopmost(owner)) throw new Exception("native hidden owner failed recovery");
            owner.Close();
        }
        report.Add("PASS native owner topmost / no-focus recovery / popup hide-show / guide close / native visibility recovery");
    }

    private static object MenuCallback<T>(Action<object> action)
    {
        return new Action<T>(delegate(T value) { action(value); });
    }

    private static void ValidateVisibilityMenu(Rectangle window, Rectangle sidebar, Rectangle area, Rectangle anchor, List<string> report)
    {
        foreach (bool dark in new[] { false, true })
        {
            Set("Placement", Enum.Parse(Type("OverlayPlacement"), "Sidebar"));
            Set("WindowBounds", window); Set("Sidebar", sidebar); Set("WorkingArea", area); Set("Anchor", anchor);
            Set("Dark", dark);
            object settings = Activator.CreateInstance(Type("OverlaySettings"), true);
            FieldInfo tokens = Type("OverlaySettings").GetField("ShowTokenPill", all);
            FieldInfo credits = Type("OverlaySettings").GetField("ShowResetCreditsPill", all);
            List<string> selected = new List<string>();
            Form menu = null;
            Action<object> action = delegate(object value) {
                string name = value.ToString(); selected.Add(name);
                FieldInfo field = name == "ToggleTokens" ? tokens : name == "ToggleResetCredits" ? credits : null;
                if (field == null) throw new Exception("display toggle dispatched a different action");
                field.SetValue(settings, !(bool)field.GetValue(settings));
                Type("PlacementSelectorForm").GetMethod("SetPillVisibility", all).Invoke(menu, new[] { settings });
            };
            object callback = typeof(PopupUiValidation).GetMethod("MenuCallback", all).MakeGenericMethod(Type("PlacementMenuAction"))
                .Invoke(null, new object[] { action });
            using (menu = (Form)Activator.CreateInstance(Type("PlacementSelectorForm"), all, null, new object[] { null, callback }, null))
            {
                Type("PlacementSelectorForm").GetMethod("ShowSelector", all).Invoke(menu, new object[] {
                    Enum.Parse(Type("OverlayPlacement"), "Sidebar"), anchor, area, 1f, IntPtr.Zero, settings });
                Point original = menu.Location;
                MethodInfo click = Type("PlacementSelectorForm").GetMethod("OnMouseDown", all);
                foreach (string name in new[] { "ToggleTokens", "ToggleResetCredits" })
                {
                    object value = Enum.Parse(Type("PlacementMenuAction"), name);
                    Rectangle bounds = (Rectangle)Type("PlacementMenuLayout").GetMethod("ActionBounds", all).Invoke(null, new[] { value });
                    click.Invoke(menu, new object[] { new MouseEventArgs(MouseButtons.Left, 1,
                        bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, 0) });
                    if (!menu.Visible) throw new Exception("switch dismissed the menu");
                }
                if ((bool)tokens.GetValue(settings) || (bool)credits.GetValue(settings) || selected.Count != 2)
                    throw new Exception("independent menu switches did not change state once each");
                if ((bool)Type("PlacementSelectorForm").GetField("showTokenPill", all).GetValue(menu) ||
                    (bool)Type("PlacementSelectorForm").GetField("showResetCreditsPill", all).GetValue(menu))
                    throw new Exception("menu did not repaint switched values");
                Rectangle resizedAnchor = anchor; resizedAnchor.Offset(60, 0); Set("Anchor", resizedAnchor);
                MethodInfo tick = Type("PlacementSelectorForm").GetMethod("CheckPointerLocation", all);
                tick.Invoke(menu, new object[] { null, EventArgs.Empty });
                if (menu.Location != original) throw new Exception("open menu jumped after capsule width changed");
                Rectangle movedWindow = window; movedWindow.Offset(20, -20); Set("WindowBounds", movedWindow);
                Rectangle movedSidebar = sidebar; movedSidebar.Offset(20, -20); Set("Sidebar", movedSidebar);
                resizedAnchor.Offset(20, -20); Set("Anchor", resizedAnchor);
                tick.Invoke(menu, new object[] { null, EventArgs.Empty });
                if (menu.Location != new Point(original.X + 20, original.Y - 20)) throw new Exception("held menu stopped following window movement");
                menu.Close();
            }
            report.Add("PASS visibility menu / " + (dark ? "dark" : "light") + " / separate clicks / stays open / width-stable / window-follow");
        }
        Set("Dark", false); Set("WindowBounds", window); Set("Sidebar", sidebar); Set("Anchor", anchor);
    }

    private static void ValidateButtonPainting(List<string> report)
    {
        foreach (bool dark in new[] { false, true })
        foreach (bool primary in new[] { false, true })
        foreach (bool enabled in new[] { false, true })
        {
            Set("Dark", dark);
            object palette = Type("UpdateMenuVisuals").GetMethod("CreateRainbowPalette", all).Invoke(null, null);
            Color background = (Color)palette.GetType().GetField("Surface", all).GetValue(palette);
            using (Panel parent = new Panel { BackColor = background })
            using (Button button = (Button)Activator.CreateInstance(Type("UpdatePillButton"), all, null, new object[] { "下一步", palette, primary }, null))
            using (Bitmap bitmap = new Bitmap(74, 30))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                parent.Controls.Add(button); button.Size = bitmap.Size; button.Enabled = enabled;
                graphics.Clear(Color.Magenta); // Simulate old/undefined double-buffer contents.
                Type("UpdatePillButton").GetMethod("OnPaint", all).Invoke(button, new object[] { new PaintEventArgs(graphics, button.ClientRectangle) });
                foreach (Point corner in new[] { new Point(0, 0), new Point(73, 0), new Point(0, 29), new Point(73, 29) })
                    if (bitmap.GetPixel(corner.X, corner.Y).ToArgb() != background.ToArgb())
                        throw new Exception("button retained stale corner pixels " + dark + "/" + primary + "/" + enabled);
                Type("UpdatePillButton").GetMethod("OnMouseEnter", all).Invoke(button, new object[] { EventArgs.Empty });
                Type("UpdatePillButton").GetMethod("OnPaint", all).Invoke(button, new object[] { new PaintEventArgs(graphics, button.ClientRectangle) });
                if (bitmap.GetPixel(0, 0).ToArgb() != background.ToArgb()) throw new Exception("hover retained corner pixels");
            }
        }
        Set("Dark", false);
        report.Add("PASS capsule button full background painting / light-dark / primary-secondary / enabled-disabled / hover");
    }

    private static bool NativeTopmost(Form form) { return (GetWindowLong(form.Handle, -20) & 8) != 0; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private static void ValidateGuideStability(Rectangle window, Rectangle sidebar, Rectangle area, Rectangle anchor, List<string> report)
    {
        // Exercise the exact guide entry point called by OverlayForm's 250 ms
        // timer, interleaved with the guide's own 100 ms timer. No live services.
        object settings = Activator.CreateInstance(Type("OverlaySettings"), true);
        Type("OverlaySettings").GetField("Theme", all).SetValue(settings, "RainbowText");
        MethodInfo show = Type("PopupUiContext").GetMethod("ShowGuide", all);
        foreach (string placement in new[] { "TitleBar", "Sidebar" })
        {
            Set("Placement", Enum.Parse(Type("OverlayPlacement"), placement));
            Set("WindowBounds", window); Set("WorkingArea", area); Set("Sidebar", sidebar); Set("Anchor", anchor);
            using (Form owner = new Form { Bounds = new Rectangle(window.Left + 100, window.Top + 100, 60, 60), ShowInTaskbar = false })
            using (Form guide = (Form)Activator.CreateInstance(Type("FirstRunGuideForm"), all, null, new object[] { settings }, null))
            {
                owner.Show(); show.Invoke(null, new object[] { guide, owner, settings }); Pump();
                Rectangle stable = guide.Bounds;
                for (int tick = 0; tick < 6; tick++)
                {
                    show.Invoke(null, new object[] { guide, owner, settings });
                    if (guide.Bounds != stable) throw new Exception("guide jumped at overlay tick in " + placement);
                    Pump();
                    if (guide.Bounds != stable) throw new Exception("guide jumped at binder tick in " + placement);
                }
                guide.Hide(); Pump();
                show.Invoke(null, new object[] { guide, owner, settings }); Pump();
                if (!guide.Visible || guide.Bounds != stable) throw new Exception("guide failed hidden-state recovery");
                guide.Close(); Pump();
                if (!owner.Visible || owner.IsDisposed) throw new Exception("closing guide hid its capsule owner");
                owner.Close();
            }
            report.Add("PASS guide " + placement + " stable across 250/100ms timers / hide-show recovery / owner survives close");
        }
    }
    private static void Verify(Form form, int edge, Rectangle window, string name)
    {
        if (form.Left < edge || form.Left > edge + 12) throw new Exception(name + " wrong horizontal position " + form.Bounds);
        if (!window.Contains(form.Bounds)) throw new Exception(name + " escaped app area " + form.Bounds);
        if (form.Region == null) throw new Exception(name + " has no rounded region");
    }
    private static void Pump() { for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(10); } }
    private static Type Type(string name) { return assembly.GetType("CodexUsageOverlay." + name, true); }
    private static void Set(string field, object value) { Type("PopupUiContext").GetField(field, all).SetValue(null, value); }
}
