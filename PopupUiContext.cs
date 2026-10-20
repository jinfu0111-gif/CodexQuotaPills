using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexUsageOverlay
{
    // UI-thread context shared by every surface; never sends chat messages.
    internal static class PopupUiContext
    {
        internal static OverlayPlacement Placement;
        internal static Rectangle Anchor;
        internal static Rectangle Sidebar;
        internal static Rectangle WindowBounds;
        internal static Rectangle WorkingArea;
        internal static IntPtr CodexWindow;
        internal static bool Dark;
        internal static int ThemeRevision;
        internal static string Theme = "Auto";
        private static DateTime nextThemeRead;

        internal static bool RefreshTheme(string theme)
        {
            if (theme == Theme && DateTime.UtcNow < nextThemeRead) return false;
            Theme = theme;
            nextThemeRead = DateTime.UtcNow.AddSeconds(2);
            bool dark = theme == "Dark";
            if (theme == "Auto")
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
                }
                catch { dark = false; }
            }
            if (Dark == dark) return false;
            Dark = dark; ThemeRevision++; return true;
        }

        internal static int CapsuleGap(int normalGap, float scale)
        {
            return Placement == OverlayPlacement.TitleBar
                ? Math.Max(normalGap, (int)Math.Round(14 * scale)) : normalGap;
        }

        internal static Point Place(Rectangle anchor, Size size, Rectangle area, int gap, int margin)
        {
            if (Placement != OverlayPlacement.Sidebar || Sidebar.IsEmpty)
                return PopupAnchorPlacement.Calculate(anchor, size, area, false, Rectangle.Empty, gap, margin);
            Rectangle available = Rectangle.Intersect(area, WindowBounds);
            if (available.IsEmpty) available = area;
            return PopupAnchorPlacement.Calculate(anchor, size, available, true, Sidebar, gap, margin);
        }

        internal static bool OwnsForeground(IntPtr foreground)
        {
            foreground = GetAncestor(foreground, 2);
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            if (processId == GetCurrentProcessId()) return true; // Also native owned confirmation/file dialogs.
            foreach (Form form in Application.OpenForms)
                if (form.IsHandleCreated && foreground == form.Handle) return true;
            return false;
        }

        // Lifetime is bounded by the dialog; timers cannot outlive disposed forms.
        internal static void ShowGuide(FirstRunGuideForm guide, Form owner, OverlaySettings settings)
        {
            guide.ApplyTheme(settings);
            if (!guide.Visible)
            {
                Rectangle area = WorkingArea.IsEmpty ? Screen.FromRectangle(Anchor).WorkingArea : WorkingArea;
                guide.UpdateAnchor(Anchor, area);
                guide.Show(owner);
            }
            // Once visible, Bind owns position and focus recovery. Never write a
            // second, whole-overlay anchor from the main 250 ms update loop.
        }

        internal static void Bind(Form form, Rectangle initialAnchor, UpdateMenuPalette palette = null)
        {
            Timer timer = new Timer { Interval = 100 };
            bool hidden = false;
            int themeRevision = -1;
            EventHandler update = delegate
            {
                if (form.IsDisposed || !form.IsHandleCreated) return;
                IntPtr foreground = GetForegroundWindow();
                bool active = ShouldDisplay(CodexWindow == IntPtr.Zero || IsWindowVisible(CodexWindow),
                    CodexWindow != IntPtr.Zero && IsIconic(CodexWindow),
                    CodexWindow == IntPtr.Zero || foreground == CodexWindow, OwnsForeground(foreground),
                    Placement == OverlayPlacement.Sidebar, !Sidebar.IsEmpty);
                if (!active)
                {
                    SetPopupVisibility(form, false, ref hidden);
                    return;
                }
                SetPopupVisibility(form, true, ref hidden);
                if (CodexWindow != IntPtr.Zero) OverlayWindowPresentation.Recover(form);
                Rectangle anchor = Anchor.IsEmpty ? initialAnchor : Anchor;
                Rectangle area = WorkingArea.IsEmpty ? Screen.FromRectangle(anchor).WorkingArea : WorkingArea;
                FirstRunGuideForm guide = form as FirstRunGuideForm;
                if (guide != null) guide.UpdateAnchor(anchor, area);
                else
                {
                    Point location = Place(anchor, form.Size, area, 8, 8);
                    if (form.Location != location) form.Location = location;
                }
                if (themeRevision != ThemeRevision)
                {
                    themeRevision = ThemeRevision;
                    if (palette != null) palette.CopyFrom(UpdateMenuVisuals.CreateRainbowPalette());
                    if (guide != null) guide.ApplyTheme(new OverlaySettings { Theme = "RainbowText" });
                    else ApplyColors(form);
                    form.Invalidate(true);
                }
            };
            form.Shown += delegate
            {
                if (form.FormBorderStyle != FormBorderStyle.None)
                {
                    form.FormBorderStyle = FormBorderStyle.None;
                    UpdatePillButton close = new UpdatePillButton("×", UpdateMenuVisuals.CreateRainbowPalette(), false);
                    close.Bounds = new Rectangle(form.ClientSize.Width - 40, 12, 26, 26);
                    close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    close.Click += delegate { form.Close(); };
                    form.Controls.Add(close); close.BringToFront();
                }
                Fit(form);
                update(form, EventArgs.Empty); timer.Start();
            };
            form.Resize += delegate { Round(form); };
            form.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (form is FirstRunGuideForm) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = OverlayUpdateMenuRenderer.RoundedRectangle(
                    new Rectangle(0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1), 14))
                using (Pen border = new Pen(QuotaPillVisuals.Border)) e.Graphics.DrawPath(border, path);
            };
            form.KeyPreview = true;
            form.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) form.Close(); };
            timer.Tick += update;
            form.Disposed += delegate { timer.Dispose(); };
        }

        internal static void ApplyColors(Control root)
        {
            root.BackColor = QuotaPillVisuals.CardSurface;
            if (!(root is UpdatePillButton)) root.ForeColor = QuotaPillVisuals.Text;
            foreach (Control control in root.Controls)
            {
                // Keep progress bar accent and semantic buttons in their own renderer.
                if (control is Panel && control.Height <= 8) continue;
                ApplyColors(control);
                LinkLabel link = control as LinkLabel;
                if (link != null) { link.LinkColor = Dark ? Color.FromArgb(187, 155, 255) : Color.FromArgb(109, 65, 190); }
            }
        }

        internal static bool ShouldDisplay(bool appVisible, bool minimized, bool appForeground,
            bool ownedForeground, bool sidebarMode, bool sidebarKnown)
        {
            return appVisible && !minimized && (appForeground || ownedForeground) &&
                (!sidebarMode || sidebarKnown);
        }

        internal static void SetPopupVisibility(Form form, bool active, ref bool hidden)
        {
            // Demoting an owned topmost window also demotes its owner on Win32.
            // Hide only: an invisible window cannot cover another application.
            if (!active)
            {
                if (!hidden) { ShowWindow(form.Handle, 0); hidden = true; }
            }
            else if (hidden) { ShowWindow(form.Handle, 4); hidden = false; }
        }

        private static void Fit(Form form)
        {
            Rectangle area = WorkingArea.IsEmpty ? Screen.FromControl(form).WorkingArea : WorkingArea;
            if (Placement == OverlayPlacement.Sidebar && !WindowBounds.IsEmpty)
                area = Rectangle.Intersect(area, WindowBounds);
            if (area.IsEmpty) return;
            Size original = form.ClientSize;
            int width = Math.Min(original.Width, Math.Max(180, area.Width - 16));
            int height = Math.Min(original.Height, Math.Max(140, area.Height - 16));
            if (width != original.Width || height != original.Height)
            {
                form.AutoScroll = true; form.AutoScrollMinSize = original;
                form.ClientSize = new Size(width, height);
            }
            Round(form);
        }

        private static void Round(Form form)
        {
            if (form is FirstRunGuideForm) return;
            if (form.Width < 28 || form.Height < 28) return;
            using (GraphicsPath path = OverlayUpdateMenuRenderer.RoundedRectangle(
                new Rectangle(0, 0, form.Width, form.Height), 14))
            {
                Region previous = form.Region;
                form.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flag);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    }

    internal static class OverlayWindowPresentation
    {
        internal static bool IsAppVisible(IntPtr app)
        {
            if (app == IntPtr.Zero || !IsWindowVisible(app)) return false;
            try
            {
                int cloaked;
                if (DwmGetWindowAttribute(app, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            }
            catch { }
            return true;
        }

        internal static bool ShouldDisplayCapsule(bool appVisible, bool minimized)
        {
            return appVisible && !minimized;
        }

        // Operate only on our HWND. Do not attach input queues to the foreign
        // process or reorder/demote its window. External windows above the app
        // stay above the capsule and obscure only their actual overlap.
        // Legacy inventory fixture retained for the captured regression tests.
        // Never use this to place a real HWND; use FindInsertionFromPrevious.
        internal static IntPtr FindInsertion(IList<IntPtr> order, IntPtr app, IntPtr overlay,
            bool appTopmost, Func<IntPtr, bool> isTopmost)
        {
            IntPtr previous = IntPtr.Zero;
            foreach (IntPtr window in order)
            {
                if (window == overlay) continue;
                if (window == app)
                    return previous == IntPtr.Zero || (!appTopmost && isTopmost(previous)) ? IntPtr.Zero : previous;
                previous = window;
            }
            return overlay; // Missing app: no z-order mutation.
        }

        internal static bool PositionInAppStack(IntPtr overlay, IntPtr app)
        {
            if (overlay == IntPtr.Zero || app == IntPtr.Zero || !IsWindow(app)) return false;
            bool topmost = (GetWindowLong(app, -20) & 8) != 0;
            bool overlayTopmost = (GetWindowLong(overlay, -20) & 8) != 0;
            if (overlayTopmost != topmost)
                SetWindowPos(overlay, topmost ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0,
                    0x0001 | 0x0002 | 0x0010 | 0x0200);
            // EnumWindows is an inventory, not the authoritative adjacent HWND
            // chain (hosted/owned windows can be absent or enumerated differently).
            IntPtr previous = GetWindow(app, 3); // GW_HWNDPREV
            IntPtr after = FindInsertionFromPrevious(previous, overlay, topmost,
                previous != IntPtr.Zero && (GetWindowLong(previous, -20) & 8) != 0);
            if (after == overlay) return false;
            return SetWindowPos(overlay, after, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
        }

        internal static IntPtr FindInsertionFromPrevious(IntPtr previous, IntPtr overlay,
            bool appTopmost, bool previousTopmost)
        {
            if (previous == overlay) return overlay;
            return previous == IntPtr.Zero || (!appTopmost && previousTopmost) ? IntPtr.Zero : previous;
        }

        internal static bool Recover(Form form)
        {
            // Keep owned dialogs above their own overlay owner, whose position
            // already follows Codex. Ignore WinForms' hidden taskbar owner HWND.
            IntPtr app = GetWindow(form.Handle, 4);
            bool knownOwner = false;
            foreach (Form open in Application.OpenForms)
                if (open.IsHandleCreated && open.Handle == app) { knownOwner = true; break; }
            if (!knownOwner) app = PopupUiContext.CodexWindow;
            return Recover(form, app);
        }

        internal static bool Recover(Form form, IntPtr app)
        {
            bool changed = !IsWindowVisible(form.Handle);
            if (app == IntPtr.Zero || !IsWindow(app)) return false;
            PositionInAppStack(form.Handle, app);
            if (changed)
                SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0040 | 0x0200);
            return changed;
        }

        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute,
            out int value, int size);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
            int x, int y, int width, int height, uint flags);
    }
}
