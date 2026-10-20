using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Automation;

namespace CodexUsageOverlay
{
    internal enum ComposerProbeState { Unavailable, NoComposer, Composer }

    internal sealed class ComposerProbeSnapshot
    {
        internal IntPtr Handle;
        internal Rectangle Window;
        internal float Scale;
        internal DateTime CapturedUtc;
        internal ComposerProbeState State;
        internal Rectangle ToolbarHost;
        internal Rectangle Pane;
        internal Rectangle Editor;
        internal Rectangle LeftAnchor;
        internal string HostKind;
        internal IList<Rectangle> FloatingBounds;

        internal ComposerProbeSnapshot InWindow(Rectangle target)
        {
            if (Window == target) return this;
            if (Window.Size != target.Size) return null;
            int dx = target.Left - Window.Left, dy = target.Top - Window.Top;
            Func<Rectangle, Rectangle> move = delegate(Rectangle box) {
                if (!box.IsEmpty) box.Offset(dx, dy);
                return box;
            };
            var floating = new List<Rectangle>();
            foreach (Rectangle box in FloatingBounds ?? new Rectangle[0]) floating.Add(move(box));
            return new ComposerProbeSnapshot { Handle = Handle, Window = target, Scale = Scale,
                CapturedUtc = CapturedUtc, State = State, ToolbarHost = move(ToolbarHost),
                Pane = move(Pane), Editor = move(Editor), LeftAnchor = move(LeftAnchor), HostKind = HostKind, FloatingBounds = floating };
        }
    }

    internal static class ComposerPlacementPolicy
    {
        internal static bool IsFresh(ComposerProbeSnapshot snapshot, IntPtr handle, Rectangle window,
            float scale, DateTime now)
        {
            return snapshot != null && snapshot.Handle == handle && snapshot.Window == window &&
                Math.Abs(snapshot.Scale - scale) < 0.01f && snapshot.CapturedUtc <= now &&
                now - snapshot.CapturedUtc <= TimeSpan.FromMilliseconds(750);
        }

        internal static Rectangle Resolve(IntPtr handle, Rectangle window, Rectangle area, Size preferred,
            float scale, ComposerProbeSnapshot snapshot, DateTime now, out OverlayPlacement effective)
        {
            effective = OverlayPlacement.ComposerBottom;
            if (IsFresh(snapshot, handle, window, scale, now))
            {
                if (snapshot.State == ComposerProbeState.NoComposer) return Rectangle.Empty;
                if (snapshot.State == ComposerProbeState.Composer && snapshot.HostKind == "Toolbar" &&
                    window.Contains(snapshot.Pane) && snapshot.Pane.Contains(snapshot.ToolbarHost) &&
                    (snapshot.LeftAnchor.IsEmpty || (snapshot.Pane.Contains(snapshot.LeftAnchor) &&
                        snapshot.ToolbarHost.Left == snapshot.LeftAnchor.Right + Math.Max(1, (int)Math.Round(4 * scale)))) &&
                    preferred.Width <= snapshot.ToolbarHost.Width && preferred.Height <= snapshot.ToolbarHost.Height)
                {
                    Rectangle bottom = OverlayPlacementLayout.InHost(OverlayPlacement.ComposerBottom,
                        snapshot.ToolbarHost, area, preferred, scale);
                    if (!bottom.IsEmpty)
                    {
                        // A popup elsewhere in Codex is unrelated to this capsule.
                        // Test the final capsule bounds, not the whole window/toolbar.
                        foreach (Rectangle popup in snapshot.FloatingBounds ?? new Rectangle[0])
                            if (popup.IntersectsWith(bottom)) return Rectangle.Empty;
                        effective = OverlayPlacement.ComposerBottom;
                        return bottom;
                    }
                }
            }
            return Rectangle.Empty;
        }

        // A separate toolbar row is required. Never reserve space inside the editable text area.
        internal static Rectangle FindToolbarHost(Rectangle editor, Rectangle container, Rectangle send,
            IList<Rectangle> occupied, Rectangle window, float scale)
        {
            int gap = Math.Max(1, (int)Math.Round(4 * scale));
            int height = Math.Max(1, (int)Math.Round(28 * scale));
            if (editor.IsEmpty || container.IsEmpty || send.IsEmpty ||
                !window.Contains(container) || !container.Contains(editor) || !container.Contains(send))
                return Rectangle.Empty;
            Rectangle row = new Rectangle(container.Left + gap * 2,
                send.Top + (send.Height - height) / 2, container.Width - gap * 4, height);
            if (row.Top < editor.Bottom + gap || !container.Contains(row) ||
                send.Height < (int)Math.Round(20 * scale) || send.Height > (int)Math.Round(64 * scale))
                return Rectangle.Empty;
            List<Rectangle> blocks = new List<Rectangle>(occupied ?? new Rectangle[0]);
            blocks.Add(editor); blocks.Add(send);
            List<Rectangle> free = new List<Rectangle> { row };
            foreach (Rectangle raw in blocks)
            {
                Rectangle block = raw; block.Inflate(gap, gap);
                if (!block.IntersectsWith(row)) continue;
                List<Rectangle> next = new List<Rectangle>();
                foreach (Rectangle slot in free)
                {
                    if (!slot.IntersectsWith(block)) { next.Add(slot); continue; }
                    if (block.Left > slot.Left)
                        next.Add(Rectangle.FromLTRB(slot.Left, slot.Top, Math.Min(slot.Right, block.Left), slot.Bottom));
                    if (block.Right < slot.Right)
                        next.Add(Rectangle.FromLTRB(Math.Max(slot.Left, block.Right), slot.Top, slot.Right, slot.Bottom));
                }
                free = next;
            }
            Rectangle largest = Rectangle.Empty;
            foreach (Rectangle slot in free)
                if (slot.Width >= (int)Math.Round(110 * scale) && slot.Width > largest.Width) largest = slot;
            return largest;
        }

        internal static Rectangle FindAttachedHost(Rectangle editor, Rectangle context, Rectangle action,
            Rectangle container, Rectangle pane, IList<Rectangle> occupied, Rectangle window, float scale,
            out string kind)
        {
            kind = "None";
            if (pane.IsEmpty || !window.Contains(pane) || !pane.Contains(editor) ||
                !pane.Contains(context) || !pane.Contains(action)) return Rectangle.Empty;
            Rectangle toolbar = FindToolbarHost(editor, Rectangle.Intersect(container, pane), action, occupied, window, scale);
            if (!toolbar.IsEmpty && pane.Contains(toolbar)) { kind = "Toolbar"; return toolbar; }
            return Rectangle.Empty;
        }

        internal static Rectangle FindAnchoredToolbarHost(Rectangle editor, Rectangle approval, Rectangle action,
            Rectangle container, Rectangle pane, IList<Rectangle> occupied, Rectangle window, float scale)
        {
            int gap = Math.Max(1, (int)Math.Round(4 * scale));
            int height = Math.Max(1, (int)Math.Round(28 * scale));
            // Chromium can report a display:contents ancestor's unclipped union
            // beyond the scroll pane. Only the visible intersection may host us;
            // every identified composer control must still fit inside it.
            if (pane.IsEmpty || !window.Contains(pane)) return Rectangle.Empty;
            container = Rectangle.Intersect(container, pane);
            if (approval.IsEmpty || !container.Contains(approval) || !pane.Contains(container) ||
                !window.Contains(pane) || !container.Contains(editor) || !container.Contains(action)) return Rectangle.Empty;
            Rectangle row = Rectangle.FromLTRB(approval.Right + gap,
                action.Top + (action.Height - height) / 2, action.Left - gap,
                action.Top + (action.Height - height) / 2 + height);
            if (row.Width <= 0 || !container.Contains(row) || row.Top < editor.Bottom + gap ||
                Math.Abs(approval.Top - action.Top) > (int)Math.Round(12 * scale)) return Rectangle.Empty;
            int right = row.Right;
            foreach (Rectangle raw in occupied ?? new Rectangle[0])
            {
                Rectangle block = raw; block.Inflate(gap, gap);
                if (!block.IntersectsWith(row)) continue;
                // Only the contiguous gap immediately after the permission button
                // is allowed. An obstacle never makes us jump to a later gap.
                if (block.Left <= row.Left && block.Right > row.Left) return Rectangle.Empty;
                right = Math.Min(right, block.Left);
            }
            return right > row.Left ? Rectangle.FromLTRB(row.Left, row.Top, right, row.Bottom) : Rectangle.Empty;
        }
    }

    // Confirmed obstruction hides immediately. A temporary unreadable provider
    // does not erase an already validated anchor. That lease expires after 1.5s
    // without a successful capture, and cannot survive resize, DPI or target changes.
    internal sealed class ComposerVisibilityGate
    {
        private Rectangle candidate, pane, window;
        private IntPtr handle;
        private float scale;
        private DateTime since, lastCapture, lastGood;
        private int observations;
        private bool shown;
        private bool moving;
        private DateTime motionGraceUntil;

        internal void SetMoving(bool value, DateTime now)
        {
            if (moving && !value && shown) motionGraceUntil = now.AddMilliseconds(750);
            moving = value;
        }

        internal void Reset()
        {
            candidate = Rectangle.Empty; observations = 0;
            since = lastCapture = lastGood = motionGraceUntil = DateTime.MinValue; shown = moving = false;
        }

        internal Rectangle Resolve(IntPtr nextHandle, Rectangle nextWindow, Rectangle area, Size preferred,
            float nextScale, ComposerProbeSnapshot snapshot, DateTime now, out OverlayPlacement effective)
        {
            effective = OverlayPlacement.ComposerBottom;
            bool sameTarget = handle == nextHandle && window.Size == nextWindow.Size &&
                Math.Abs(scale - nextScale) < 0.01f && candidate.Size == preferred;
            if (!candidate.IsEmpty && !sameTarget) Reset();
            if (!candidate.IsEmpty && window != nextWindow)
            {
                int dx = nextWindow.Left - window.Left, dy = nextWindow.Top - window.Top;
                candidate.Offset(dx, dy); pane.Offset(dx, dy); window = nextWindow;
            }
            bool compatible = snapshot == null || (snapshot.Handle == nextHandle &&
                snapshot.Window.Size == nextWindow.Size && Math.Abs(snapshot.Scale - nextScale) < 0.01f &&
                snapshot.CapturedUtc <= now);
            if (!compatible) { Reset(); return Rectangle.Empty; }
            snapshot = snapshot == null ? null : snapshot.InWindow(nextWindow);
            if (shown && ComposerPlacementPolicy.IsFresh(snapshot, nextHandle, nextWindow, nextScale, now) &&
                snapshot.State == ComposerProbeState.Composer && snapshot.HostKind == "Toolbar" &&
                nextWindow.Contains(snapshot.Pane) && snapshot.Pane.Contains(snapshot.ToolbarHost) &&
                (snapshot.LeftAnchor.IsEmpty || (snapshot.Pane.Contains(snapshot.LeftAnchor) &&
                    snapshot.ToolbarHost.Left == snapshot.LeftAnchor.Right + Math.Max(1, (int)Math.Round(4 * nextScale)) &&
                    candidate.Left == snapshot.ToolbarHost.Left)) &&
                snapshot.ToolbarHost.Contains(candidate) && area.Contains(candidate))
            {
                foreach (Rectangle popup in snapshot.FloatingBounds ?? new Rectangle[0])
                    if (popup.IntersectsWith(candidate)) { Reset(); return Rectangle.Empty; }
                lastGood = snapshot.CapturedUtc;
                pane = snapshot.Pane;
                return candidate;
            }
            Rectangle next = ComposerPlacementPolicy.Resolve(nextHandle, nextWindow, area, preferred,
                nextScale, snapshot, now, out effective);
            if (next.IsEmpty)
            {
                bool confirmed = ComposerPlacementPolicy.IsFresh(snapshot, nextHandle, nextWindow, nextScale, now) &&
                    snapshot.State != ComposerProbeState.Unavailable;
                if (!confirmed && shown && now >= lastGood &&
                    (moving || now <= motionGraceUntil || now - lastGood <= TimeSpan.FromMilliseconds(1500)) &&
                    nextWindow.Contains(candidate) && area.Contains(candidate)) return candidate;
                Reset(); return Rectangle.Empty;
            }
            if (candidate != next || handle != nextHandle || window != nextWindow || pane != snapshot.Pane ||
                Math.Abs(scale - nextScale) >= 0.01f || now < since || snapshot.CapturedUtc < lastCapture)
            {
                candidate = next; handle = nextHandle; window = nextWindow; pane = snapshot.Pane; scale = nextScale;
                since = now; lastCapture = snapshot.CapturedUtc; observations = 1;
                lastGood = snapshot.CapturedUtc; shown = false;
                return Rectangle.Empty;
            }
            if (snapshot.CapturedUtc > lastCapture)
            {
                lastCapture = snapshot.CapturedUtc;
                if (observations < 2) observations++;
            }
            lastGood = snapshot.CapturedUtc;
            shown = observations >= 2 && now - since >= TimeSpan.FromMilliseconds(400);
            return shown ? candidate : Rectangle.Empty;
        }
    }

    internal sealed class ComposerLayoutElement
    {
        internal Rectangle Bounds, Pane, Container;
        internal bool Editor, Context, Action, Approval;
        internal AutomationElement NativeContainer;
    }

    internal static class ComposerLayoutRecognition
    {
        internal static ComposerLayoutElement Select(IList<ComposerLayoutElement> elements, IList<Rectangle> occupied,
            Rectangle window, float scale, ComposerProbeSnapshot result)
        {
            ComposerLayoutElement selectedEditor = null, selectedContext = null, selectedAction = null;
            int matches = 0;
            foreach (ComposerLayoutElement context in elements)
            {
                if (!context.Context || context.Pane.IsEmpty || !window.Contains(context.Pane)) continue;
                ComposerLayoutElement editor = null, action = null;
                foreach (ComposerLayoutElement node in elements)
                {
                    if (node.Pane != context.Pane || !context.Pane.Contains(node.Bounds)) continue;
                    if (node.Editor && node.Bounds.Top <= context.Bounds.Bottom &&
                        node.Bounds.Bottom >= context.Bounds.Top - (int)(180 * scale) &&
                        node.Bounds.Left >= context.Bounds.Left - (int)(16 * scale) &&
                        (editor == null || node.Bounds.Bottom > editor.Bounds.Bottom)) editor = node;
                    if (node.Action && node.Bounds.Left > context.Bounds.Right &&
                        Math.Abs(node.Bounds.Top - context.Bounds.Top) <= (int)(12 * scale) &&
                        (action == null || node.Bounds.Right > action.Bounds.Right)) action = node;
                }
                if (editor == null || action == null) continue;
                matches++; selectedEditor = editor; selectedContext = context; selectedAction = action;
            }
            if (matches != 1) return null;
            result.State = ComposerProbeState.Composer;
            result.Pane = selectedContext.Pane; result.Editor = selectedEditor.Bounds;
            string kind;
            result.ToolbarHost = ComposerPlacementPolicy.FindAttachedHost(selectedEditor.Bounds,
                selectedContext.Bounds, selectedAction.Bounds, selectedContext.Container, selectedContext.Pane,
                occupied, window, scale, out kind);
            result.HostKind = kind;
            ComposerLayoutElement approval = null;
            foreach (ComposerLayoutElement node in elements)
                if (node.Approval && node.Pane == selectedContext.Pane && node.Container == selectedContext.Container &&
                    node.Bounds.Left >= selectedContext.Bounds.Right && node.Bounds.Right < selectedAction.Bounds.Left &&
                    Math.Abs(node.Bounds.Top - selectedContext.Bounds.Top) <= (int)Math.Round(12 * scale))
                {
                    if (approval != null) { result.ToolbarHost = Rectangle.Empty; result.HostKind = "None"; return null; }
                    approval = node;
                }
            if (approval != null)
            {
                result.LeftAnchor = approval.Bounds;
                result.ToolbarHost = ComposerPlacementPolicy.FindAnchoredToolbarHost(selectedEditor.Bounds,
                    approval.Bounds, selectedAction.Bounds, selectedContext.Container, selectedContext.Pane, occupied, window, scale);
                result.HostKind = result.ToolbarHost.IsEmpty ? "None" : "Toolbar";
            }
            return selectedContext;
        }
    }

    // One MTA background worker. A slow or hung accessibility provider cannot block the overlay,
    // and cannot cause unbounded new workers. Pure window translation preserves anchors.
    // Reads geometry/identity only; no editor Name, ValuePattern, TextPattern, or action patterns.
    internal sealed class ComposerWindowProbe : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private Thread worker;
        private bool disposed;
        private IntPtr requestedHandle;
        private Rectangle requestedWindow;
        private float requestedScale;
        private long generation;
        private ComposerProbeSnapshot latest;
        private readonly Func<IntPtr, Rectangle, float, ComposerProbeSnapshot> capture;

        internal ComposerWindowProbe(Func<IntPtr, Rectangle, float, ComposerProbeSnapshot> reader = null)
        {
            capture = reader ?? Capture;
        }

        internal ComposerProbeSnapshot Read(IntPtr handle, Rectangle window, float scale)
        {
            lock (gate)
            {
                if (disposed) return null;
                if (requestedHandle != handle || requestedWindow != window || Math.Abs(requestedScale - scale) > 0.01f)
                {
                    bool layoutChanged = requestedHandle != handle || requestedWindow.Size != window.Size ||
                        Math.Abs(requestedScale - scale) > 0.01f;
                    requestedHandle = handle; requestedWindow = window; requestedScale = scale;
                    if (layoutChanged) { latest = null; generation++; }
                    wake.Set();
                }
                if (worker == null)
                {
                    worker = new Thread(Run) { IsBackground = true, Name = "Composer layout reader" };
                    worker.SetApartmentState(ApartmentState.MTA); worker.Start();
                }
                return latest == null ? null : latest.InWindow(window);
            }
        }

        internal void Invalidate()
        {
            lock (gate)
            {
                if (disposed) return;
                generation++; latest = null; requestedHandle = IntPtr.Zero; wake.Set();
            }
        }

        private void Run()
        {
            // Poll only on this bounded worker. Whole-window UIA events include every
            // streaming message and embedded browser update; subscribing to them used
            // to clear/cancel the composer snapshot continuously in 0.2.34.
            try
            {
                while (true)
                {
                    IntPtr handle; Rectangle window; float scale; long version;
                    lock (gate)
                    {
                        if (disposed) return;
                        handle = requestedHandle; window = requestedWindow; scale = requestedScale; version = generation;
                    }
                    if (handle != IntPtr.Zero)
                    {
                        ComposerProbeSnapshot result;
                        try { result = capture(handle, window, scale); }
                        catch { result = null; }
                        lock (gate)
                        {
                            if (!disposed && version == generation) latest = result;
                        }
                    }
                    wake.WaitOne(200);
                }
            }
            finally
            {
                wake.Dispose();
            }
        }

        internal static ComposerProbeSnapshot Capture(IntPtr handle, Rectangle window, float scale)
        {
            ComposerProbeSnapshot result = new ComposerProbeSnapshot {
                Handle = handle, Window = window, Scale = scale, State = ComposerProbeState.Unavailable
            };
            // Timestamp at START: a provider that takes too long never publishes a fresh-looking result.
            result.CapturedUtc = DateTime.UtcNow;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(handle);
                Rectangle actual = Bounds(root.Current.BoundingRectangle);
                Rectangle tolerance = window; tolerance.Inflate((int)(16 * scale), (int)(16 * scale));
                if (!tolerance.Contains(actual)) return result;
                CacheRequest cache = new CacheRequest();
                cache.Add(AutomationElement.ControlTypeProperty);
                cache.Add(AutomationElement.BoundingRectangleProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                cache.Add(AutomationElement.IsKeyboardFocusableProperty);
                cache.Add(AutomationElement.ClassNameProperty);
                cache.Add(AutomationElement.AutomationIdProperty);
                AutomationElementCollection nodes;
                Condition leaves = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolTip),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
                using (cache.Activate()) nodes = root.FindAll(TreeScope.Descendants, leaves);
                if (nodes.Count > 12000) return result;
                List<Rectangle> floating = new List<Rectangle>();
                foreach (AutomationElement node in nodes)
                {
                    var data = node.Cached;
                    Rectangle rect = Bounds(data.BoundingRectangle);
                    if (!data.IsOffscreen && !rect.IsEmpty && IsFloatingControl(data.ControlType)) floating.Add(rect);
                }
                result.FloatingBounds = floating;
                List<ComposerLayoutElement> elements = new List<ComposerLayoutElement>();
                List<Rectangle> occupied = new List<Rectangle>();
                bool editorFound = false, contextFound = false;
                foreach (AutomationElement node in nodes)
                {
                    var data = node.Cached;
                    Rectangle rect = Bounds(data.BoundingRectangle);
                    if (data.IsOffscreen || rect.IsEmpty) continue;
                    if (IsFloatingControl(data.ControlType)) continue;
                    if (!window.Contains(rect)) continue;
                    // Popup children must not alter the native toolbar's free slot
                    // and make us move to another gap. Geometry is checked separately.
                    bool overlapsPopup = false;
                    foreach (Rectangle popup in floating)
                        if (popup.IntersectsWith(rect)) { overlapsPopup = true; break; }
                    if (overlapsPopup && HasFloatingAncestor(node, root)) continue;
                    if (rect.Top < window.Top + window.Height / 2) continue;
                    bool editor = IsEditorCandidate(data.AutomationId, data.ClassName, data.IsKeyboardFocusable, rect);
                    editorFound |= IsEditorIdentity(data.AutomationId, data.ClassName);
                    bool context = false, action = false, approval = false;
                    if (data.ControlType == ControlType.Button && rect.Height <= (int)(64 * scale))
                    {
                        // Only names of native-sized buttons are read; editor and Text names
                        // and patterns are never retrieved, including in the browser pane.
                        string label = node.Current.Name;
                        context = IsContextLabel(label); action = IsSendLabel(label) || IsVoiceLabel(label);
                        approval = IsPermissionLabel(label);
                        contextFound |= context;
                    }
                    if (!editor && !context && !action && !approval) continue;
                    Rectangle container;
                    AutomationElement nativeContainer;
                    Rectangle pane = ReadCodexPane(node, root, window, out container, out nativeContainer);
                    elements.Add(new ComposerLayoutElement { Bounds = rect, Pane = pane,
                        Container = container, Editor = editor, Context = context, Action = action,
                        Approval = approval, NativeContainer = nativeContainer });
                }
                if (nodes.Count < 10) return result; // Title-bar-only UIA is not proof of Settings.
                if (!editorFound && !contextFound)
                {
                    result.State = ComposerProbeState.NoComposer;
                    return result;
                }
                ComposerLayoutElement selected = ComposerLayoutRecognition.Select(elements, null, window, scale, result);
                // Identify the actual composer first, then inspect only its native
                // descendants. Scrolling message rectangles can extend behind the
                // sticky footer, but are not composer controls or obstructions.
                if (selected == null || selected.NativeContainer == null || result.LeftAnchor.IsEmpty)
                {
                    result.State = ComposerProbeState.Unavailable; result.ToolbarHost = Rectangle.Empty; return result;
                }
                foreach (AutomationElement node in nodes)
                {
                    var data = node.Cached;
                    Rectangle rect = Bounds(data.BoundingRectangle);
                    if (data.IsOffscreen || rect.IsEmpty || IsFloatingControl(data.ControlType) ||
                        !selected.Container.IntersectsWith(rect)) continue;
                    if (IsNativeComposerDescendant(node, selected.NativeContainer)) occupied.Add(rect);
                }
                ComposerLayoutRecognition.Select(elements, occupied, window, scale, result);
                return result;
            }
            catch { result.State = ComposerProbeState.Unavailable; result.ToolbarHost = Rectangle.Empty; return result; }
        }

        internal static bool IsFloatingControl(ControlType type)
        {
            return type == ControlType.Window || type == ControlType.Menu || type == ControlType.ToolTip;
        }

        private static bool HasFloatingAncestor(AutomationElement node, AutomationElement root)
        {
            AutomationElement parent = node;
            for (int level = 0; level < 16; level++)
            {
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
                if (parent == null || parent == root) return false;
                if (IsFloatingControl(parent.Current.ControlType)) return true;
            }
            return false;
        }

        private static Rectangle ReadCodexPane(AutomationElement node, AutomationElement root,
            Rectangle window, out Rectangle container, out AutomationElement nativeContainer)
        {
            container = Rectangle.Empty;
            nativeContainer = null;
            Rectangle thread = Rectangle.Empty;
            AutomationElement parent = node;
            for (int level = 0; level < 12; level++)
            {
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
                if (parent == null || parent == root) break;
                var data = parent.Current;
                Rectangle box = Bounds(data.BoundingRectangle);
                string className = data.ClassName ?? "";
                if (className.IndexOf("thread-scroll-container", StringComparison.Ordinal) >= 0 ||
                    className.IndexOf("group/thread-scroll-layout", StringComparison.Ordinal) >= 0)
                    thread = thread.IsEmpty ? box : Rectangle.Intersect(thread, box);
                else if (thread.IsEmpty && box.Width > 100 && box.Height > 40)
                {
                    container = box;
                    nativeContainer = parent;
                }
                if (className.IndexOf("_MainContentSurface_", StringComparison.Ordinal) >= 0 && !thread.IsEmpty)
                    return Rectangle.Intersect(window, Rectangle.Intersect(thread, box));
            }
            // ChatGPT's embedded website has editors and attachment buttons too, but
            // has no Codex MainContentSurface + thread-scroll ancestor pair.
            return Rectangle.Empty;
        }

        private static bool IsNativeComposerDescendant(AutomationElement node, AutomationElement container)
        {
            AutomationElement parent = node;
            for (int level = 0; level < 12; level++)
            {
                if (parent == null) return false;
                if (parent == container) return true;
                if (IsFloatingControl(parent.Current.ControlType)) return false;
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
            }
            return false;
        }

        internal static bool IsEditorIdentity(string id, string className)
        {
            return String.Equals(id, "prompt-textarea", StringComparison.Ordinal) ||
                Array.IndexOf((className ?? "").Split(' '), "ProseMirror") >= 0;
        }

        internal static bool IsEditorCandidate(string id, string className, bool focusable, Rectangle bounds)
        {
            return focusable && bounds.Width > 0 && bounds.Height > 0 && IsEditorIdentity(id, className);
        }

        internal static bool IsContextLabel(string value)
        {
            foreach (string label in new[] { "Add files and more", "Add files or context", "Add context", "添加文件等内容", "添加文件和上下文", "添加上下文" })
                if (String.Equals(value, label, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool IsPermissionLabel(string value)
        {
            foreach (string label in new[] { "更改权限", "Change permissions", "Change permission mode", "帮我批准", "Help me approve" })
                if (String.Equals(value, label, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool IsSendLabel(string value)
        {
            foreach (string label in new[] { "Send", "Send message", "Send prompt", "Stop", "Stop generating", "Stop response", "Stop turn", "发送", "发送消息", "发送提示", "停止", "停止生成", "停止运行" })
                if (String.Equals(value, label, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool IsVoiceLabel(string value)
        {
            foreach (string label in new[] { "开启语音聊天", "Start voice mode", "Start voice chat", "Open voice mode" })
                if (String.Equals(value, label, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Rectangle Bounds(System.Windows.Rect value)
        {
            if (value.IsEmpty || Double.IsNaN(value.X) || Double.IsInfinity(value.X) ||
                Double.IsNaN(value.Y) || Double.IsInfinity(value.Y) || value.Width <= 0 || value.Height <= 0 ||
                Double.IsNaN(value.Width) || Double.IsInfinity(value.Width) || Double.IsNaN(value.Height) || Double.IsInfinity(value.Height) ||
                value.X < Int32.MinValue / 2 || value.Y < Int32.MinValue / 2 ||
                value.Right > Int32.MaxValue / 2 || value.Bottom > Int32.MaxValue / 2) return Rectangle.Empty;
            return Rectangle.FromLTRB((int)Math.Floor(value.Left), (int)Math.Floor(value.Top),
                (int)Math.Ceiling(value.Right), (int)Math.Ceiling(value.Bottom));
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; latest = null; generation++; wake.Set();
                if (worker == null) wake.Dispose();
            }
            // Do not Join: provider calls may hang. The sole worker is a background thread.
        }
    }
}
