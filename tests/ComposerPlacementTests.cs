using System;
using System.Drawing;
using System.Threading;

namespace CodexUsageOverlay
{
    internal static class ComposerPlacementTests
    {
        private static readonly IntPtr Handle = new IntPtr(42);
        private static readonly Rectangle Window = new Rectangle(0, 0, 1205, 986);
        private static readonly Size Size = new Size(140, 28);

        internal static void PublishedAssemblyCannotGuessComposer(System.Reflection.Assembly assembly)
        {
            Type placement = assembly.GetType("CodexUsageOverlay.OverlayPlacement", true);
            Type layout = assembly.GetType("CodexUsageOverlay.OverlayPlacementLayout", true);
            var method = layout.GetMethod("CalculateBounds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert(method != null, "missing production placement method");
            Rectangle bounds = (Rectangle)method.Invoke(null, new object[] {
                Enum.Parse(placement, "ComposerBottom"), Window, Window, Size, 1f, Rectangle.Empty
            });
            Assert(bounds.IsEmpty, "production assembly still guesses a bottom host from window geometry alone");
            VerifyProductionHidesWithoutHost(assembly);
            VerifyCapturedNarrowLayout(assembly);
            VerifyProductionPopupScopes(assembly);
            VerifyProductionKeepsValidatedAnchor(assembly);
            VerifyProductionPermissionAnchor(assembly);
            VerifyProductionRejectsMixedTimeCapture(assembly);
            SidebarLayoutTests.ActualAssemblyKeepsCapsuleInAppScope(assembly);
        }

        private static void VerifyProductionRejectsMixedTimeCapture(System.Reflection.Assembly assembly)
        {
            var stat=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var inst=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            Type probe=assembly.GetType("CodexUsageOverlay.ComposerWindowProbe",true);
            var missing=probe.GetMethod("MissingComposerState",stat);
            var complete=probe.GetMethod("CompleteCapture",stat);
            Assert(missing!=null && complete!=null,"production still publishes partial or moving capture as confirmed absence");
            Assert(missing.Invoke(null,new object[] { false,true }).ToString()=="Unavailable" &&
                missing.Invoke(null,new object[] { false,false }).ToString()=="NoComposer",
                "production does not distinguish partial recognition from confirmed absence");
            Type snapshotType=assembly.GetType("CodexUsageOverlay.ComposerProbeSnapshot",true);
            object snapshot=Activator.CreateInstance(snapshotType,true);
            var state=snapshotType.GetField("State",inst);
            state.SetValue(snapshot,Enum.Parse(state.FieldType,"NoComposer"));
            snapshotType.GetField("ToolbarHost",inst).SetValue(snapshot,new Rectangle(540,934,140,28));
            Rectangle moved=Window; moved.Offset(-19,23);
            complete.Invoke(null,new object[] { snapshot,Window,moved });
            Assert(state.GetValue(snapshot).ToString()=="Unavailable" &&
                ((Rectangle)snapshotType.GetField("ToolbarHost",inst).GetValue(snapshot)).IsEmpty,
                "production publishes a mixed-time host or absence after movement");
        }

        private static void VerifyProductionPermissionAnchor(System.Reflection.Assembly assembly)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Type element = assembly.GetType("CodexUsageOverlay.ComposerLayoutElement", true);
            Type snapshotType = assembly.GetType("CodexUsageOverlay.ComposerProbeSnapshot", true);
            Assert(element.GetField("Approval", flags) != null && snapshotType.GetField("LeftAnchor", flags) != null,
                "production still has no native permission-button anchor");
            Rectangle window = new Rectangle(272, 167, 1171, 988), pane = new Rectangle(563, 263, 876, 888);
            Rectangle container = new Rectangle(633, 1037, 736, 98), permission = new Rectangle(674, 1099, 86, 28);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(element));
            Action<Rectangle, bool, bool, bool, bool> add = delegate(Rectangle rect, bool edit, bool context, bool action, bool approval) {
                object item = Activator.CreateInstance(element, true);
                foreach (var pair in new[] { new { Name="Bounds", Value=rect }, new { Name="Pane", Value=pane }, new { Name="Container", Value=container } })
                    element.GetField(pair.Name, flags).SetValue(item, pair.Value);
                element.GetField("Editor", flags).SetValue(item, edit); element.GetField("Context", flags).SetValue(item, context);
                element.GetField("Action", flags).SetValue(item, action); element.GetField("Approval", flags).SetValue(item, approval); list.Add(item);
            };
            add(new Rectangle(645,1051,712,44),true,false,false,false);
            add(new Rectangle(641,1099,28,28),false,true,false,false);
            add(permission,false,false,false,true);
            add(new Rectangle(1333,1099,28,28),false,false,true,false);
            var occupied = new[] { permission, new Rectangle(641,1099,28,28), new Rectangle(1175,1099,122,28),
                new Rectangle(1297,1099,28,28), new Rectangle(1333,1099,28,28) };
            object snapshot = Activator.CreateInstance(snapshotType, true);
            assembly.GetType("CodexUsageOverlay.ComposerLayoutRecognition",true).GetMethod("Select",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] {list,occupied,window,1f,snapshot});
            Assert((Rectangle)snapshotType.GetField("LeftAnchor",flags).GetValue(snapshot)==permission, "permission anchor not retained");
            Assert((Rectangle)snapshotType.GetField("ToolbarHost",flags).GetValue(snapshot)==new Rectangle(764,1099,407,28),
                "captured native toolbar was not aligned immediately after permission mode");
        }

        private static void VerifyProductionKeepsValidatedAnchor(System.Reflection.Assembly assembly)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Type snapshotType = assembly.GetType("CodexUsageOverlay.ComposerProbeSnapshot", true);
            Type gateType = assembly.GetType("CodexUsageOverlay.ComposerVisibilityGate", true);
            object gate = Activator.CreateInstance(gateType, true);
            var resolve = gateType.GetMethod("Resolve", flags);
            Type placement = assembly.GetType("CodexUsageOverlay.OverlayPlacement", true);
            DateTime start = DateTime.UtcNow;
            Func<int, ComposerProbeState, Rectangle, Rectangle> step = delegate(int ms, ComposerProbeState state, Rectangle window) {
                object snapshot = Activator.CreateInstance(snapshotType, true);
                var source = Snapshot(state, start.AddMilliseconds(ms));
                foreach (var field in typeof(ComposerProbeSnapshot).GetFields(flags))
                {
                    var destination = snapshotType.GetField(field.Name, flags);
                    if (destination != null) destination.SetValue(snapshot, field.GetValue(source));
                }
                object[] args = { Handle, window, new Rectangle(-5000, -5000, 10000, 10000), Size, 1f,
                    snapshot, start.AddMilliseconds(ms), Enum.Parse(placement, "ComposerBottom") };
                return (Rectangle)resolve.Invoke(gate, args);
            };
            step(0, ComposerProbeState.Composer, Window);
            Rectangle shown = step(400, ComposerProbeState.Composer, Window);
            Assert(!shown.IsEmpty, "production baseline did not show");
            Assert(step(500, ComposerProbeState.Unavailable, Window) == shown,
                "production still hides a validated anchor on a single unreadable capture");
            var moved = Window; moved.Offset(83, -41); shown.Offset(83, -41);
            Assert(step(600, ComposerProbeState.Composer, moved) == shown,
                "production window translation still loses or delays its anchor");
            var setMoving=gateType.GetMethod("SetMoving",flags);
            setMoving.Invoke(gate,new object[] { true,start.AddMilliseconds(700) });
            Assert(step(800,ComposerProbeState.NoComposer,moved)==shown,
                "production native drag still trusts an in-flight false absence");
            setMoving.Invoke(gate,new object[] { false,start.AddMilliseconds(900) });
            Assert(step(901,ComposerProbeState.NoComposer,moved).IsEmpty,
                "production ignored confirmed absence from a post-release capture");
            Assert(assembly.GetType("CodexUsageOverlay.ComposerWindowProbe",true).GetMethod("SetMoving",flags)!=null,
                "production worker cannot pause captures during native movement");
        }

        private static void VerifyProductionPopupScopes(System.Reflection.Assembly assembly)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Type snapshotType = assembly.GetType("CodexUsageOverlay.ComposerProbeSnapshot", true);
            var floatingField = snapshotType.GetField("FloatingBounds", fields);
            Assert(floatingField != null, "production assembly has no spatial popup check");
            object snapshot = Activator.CreateInstance(snapshotType, true);
            foreach (string name in new[] { "Handle", "Window", "Scale", "CapturedUtc", "State", "Pane", "HostKind", "ToolbarHost" })
                snapshotType.GetField(name, fields).SetValue(snapshot, typeof(ComposerProbeSnapshot).GetField(name, fields)
                    .GetValue(Snapshot(ComposerProbeState.Composer, DateTime.UtcNow)));
            Type placement = assembly.GetType("CodexUsageOverlay.OverlayPlacement", true);
            var resolve = assembly.GetType("CodexUsageOverlay.ComposerPlacementPolicy", true).GetMethod("Resolve",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            floatingField.SetValue(snapshot, new[] { new Rectangle(49, 740, 219, 230), new Rectangle(800, 934, 50, 28) });
            object[] args = { Handle, Window, Window, Size, 1f, snapshot, DateTime.UtcNow, Enum.Parse(placement, "ComposerBottom") };
            Assert(!((Rectangle)resolve.Invoke(null, args)).IsEmpty, "unrelated popup or unused toolbar space hides the production capsule");
            floatingField.SetValue(snapshot, new[] { new Rectangle(620, 930, 90, 40) });
            Assert(((Rectangle)resolve.Invoke(null, args)).IsEmpty, "production capsule draws over an overlapping popup");
        }

        private static void VerifyProductionHidesWithoutHost(System.Reflection.Assembly assembly)
        {
            Type placement = assembly.GetType("CodexUsageOverlay.OverlayPlacement", true);
            Type policy = assembly.GetType("CodexUsageOverlay.ComposerPlacementPolicy", true);
            object[] args = { Handle, Window, Window, Size, 1f, null, DateTime.UtcNow, Enum.Parse(placement, "ComposerBottom") };
            Rectangle bounds = (Rectangle)policy.GetMethod("Resolve", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, args);
            Assert(bounds.IsEmpty && args[7].ToString() == "ComposerBottom", "production input mode still escapes to the title bar when unreadable");
        }

        internal static void CapturedNarrowLayoutIgnoresQuestionAndBrowser()
        {
            VerifyCapturedNarrowLayout(typeof(ComposerLayoutRecognition).Assembly);
        }

        private static void VerifyCapturedNarrowLayout(System.Reflection.Assembly assembly)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Type element = assembly.GetType("CodexUsageOverlay.ComposerLayoutElement", true);
            Type snapshotType = assembly.GetType("CodexUsageOverlay.ComposerProbeSnapshot", true);
            Type recognition = assembly.GetType("CodexUsageOverlay.ComposerLayoutRecognition", true);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(element));
            // Real 0.2.34 read-only UIA metadata: narrow main editor, another editor
            // in a request card, and a synthetic parent that does not contain the editor.
            Rectangle window = new Rectangle(454, 133, 1205, 986);
            Rectangle pane = new Rectangle(745, 229, 320, 886);
            Rectangle container = new Rectangle(761, 738, 736, 333);
            Action<Rectangle, Rectangle, bool, bool, bool> add = delegate(Rectangle rect, Rectangle ownPane, bool edit, bool context, bool action) {
                object item = Activator.CreateInstance(element, true);
                element.GetField("Bounds", flags).SetValue(item, rect);
                element.GetField("Pane", flags).SetValue(item, ownPane);
                element.GetField("Container", flags).SetValue(item, container);
                element.GetField("Editor", flags).SetValue(item, edit);
                element.GetField("Context", flags).SetValue(item, context);
                element.GetField("Action", flags).SetValue(item, action);
                list.Add(item);
            };
            add(new Rectangle(806, 976, 122, 19), pane, true, false, false);
            add(new Rectangle(988, 1004, 44, 28), pane, false, false, true);
            add(new Rectangle(802, 1059, 21, 36), pane, true, false, false);
            add(new Rectangle(769, 1063, 28, 28), pane, false, true, false);
            add(new Rectangle(1013, 1063, 28, 28), pane, false, false, true);
            // An embedded browser has the same editor class and button labels, but
            // no Codex pane ancestry. It must not contribute to composer ambiguity.
            add(new Rectangle(1100, 1030, 500, 40), Rectangle.Empty, true, false, false);
            add(new Rectangle(1100, 1070, 28, 28), Rectangle.Empty, false, true, false);
            add(new Rectangle(1570, 1070, 28, 28), Rectangle.Empty, false, false, true);
            var occupied = new System.Collections.Generic.List<Rectangle> {
                new Rectangle(806, 976, 122, 19), new Rectangle(988, 1004, 44, 28),
                new Rectangle(802, 1059, 21, 36), new Rectangle(769, 1063, 28, 28),
                new Rectangle(827, 1063, 122, 28), new Rectangle(949, 1063, 28, 28),
                new Rectangle(977, 1063, 28, 28), new Rectangle(1013, 1063, 28, 28)
            };
            object result = Activator.CreateInstance(snapshotType, true);
            recognition.GetMethod("Select", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { list, occupied, window, 1f, result });
            Rectangle host = (Rectangle)snapshotType.GetField("ToolbarHost", flags).GetValue(result);
            Assert(snapshotType.GetField("State", flags).GetValue(result).ToString() == "Composer", "main composer was lost among request/browser editors");
            Assert((Rectangle)snapshotType.GetField("Editor", flags).GetValue(result) == new Rectangle(802, 1059, 21, 36),
                "request card or browser became the main editor");
            Assert(host.IsEmpty, "narrow composer still moved pills above the input");
        }

        internal static void SingleLineHidesAtEveryDpi()
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                Func<Rectangle, Rectangle> r = b => new Rectangle(-2400 + (int)(b.X * scale), (int)(b.Y * scale), (int)(b.Width * scale), (int)(b.Height * scale));
                Rectangle editor = r(new Rectangle(395, 929, 530, 32));
                Rectangle context = r(new Rectangle(395, 929, 28, 32));
                Rectangle action = r(new Rectangle(1078, 929, 32, 32));
                Rectangle box = r(new Rectangle(379, 921, 736, 48));
                Rectangle pane = r(new Rectangle(290, 90, 900, 890));
                string kind;
                Rectangle host = ComposerPlacementPolicy.FindAttachedHost(editor, context, action, box, pane,
                    new[] { editor, context, action }, r(Window), scale, out kind);
                Assert(host.IsEmpty && kind == "None", "single-line composer still selected an outside-input host");
                Rectangle blocked = ComposerPlacementPolicy.FindAttachedHost(editor, context, action, box, pane,
                    new[] { editor, context, action, r(new Rectangle(379, 884, 736, 36)) }, r(Window), scale, out kind);
                Assert(blocked.IsEmpty, "occupied message text above composer was covered");
            }
        }

        internal static void MissingOrAmbiguousCodexAnchorsAreRejected()
        {
            var result = new ComposerProbeSnapshot();
            Rectangle pane = new Rectangle(290, 90, 900, 890);
            var nodes = new[] {
                new ComposerLayoutElement { Bounds = new Rectangle(395, 929, 21, 32), Pane = pane, Editor = true },
                new ComposerLayoutElement { Bounds = new Rectangle(1078, 929, 32, 32), Pane = pane, Action = true }
            };
            ComposerLayoutRecognition.Select(nodes, null, Window, 1f, result);
            Assert(result.State == ComposerProbeState.Unavailable, "question editor without attachment anchor was accepted");
            var full = new System.Collections.Generic.List<ComposerLayoutElement>(nodes);
            var context = new ComposerLayoutElement { Bounds = new Rectangle(379, 929, 28, 32), Pane = pane, Context = true };
            full.Add(context); full.Add(context);
            ComposerLayoutRecognition.Select(full, null, Window, 1f, result);
            Assert(result.State == ComposerProbeState.Unavailable, "two competing composer anchors were guessed");
            Assert(ComposerWindowProbe.IsVoiceLabel("开启语音聊天") && !ComposerWindowProbe.IsVoiceLabel("听写"), "idle right action confused with dictation");
        }

        private static ComposerProbeSnapshot Snapshot(ComposerProbeState state, DateTime now)
        {
            return new ComposerProbeSnapshot { Handle = Handle, Window = Window, Scale = 1,
                CapturedUtc = now, State = state, Pane = Window, HostKind = "Toolbar",
                ToolbarHost = new Rectangle(540, 934, 360, 28) };
        }

        internal static void SettingsDoesNotKeepOldBottomPosition()
        {
            DateTime now = DateTime.UtcNow; OverlayPlacement effective;
            Assert(ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1,
                Snapshot(ComposerProbeState.NoComposer, now), now, out effective).IsEmpty,
                "settings without a composer retained an overlay");
            Rectangle fallback = ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1,
                Snapshot(ComposerProbeState.Unavailable, now), now, out effective);
            Assert(effective == OverlayPlacement.ComposerBottom && fallback.IsEmpty,
                "an unreadable accessibility tree must hide without changing placement");
        }

        internal static void SingleLineCannotCoverText()
        {
            Rectangle editor = new Rectangle(395, 929, 530, 32);
            Rectangle send = new Rectangle(1078, 929, 32, 32);
            Rectangle host = ComposerPlacementPolicy.FindToolbarHost(editor, new Rectangle(379, 921, 736, 48),
                send, new[] { new Rectangle(901, 929, 160, 32) }, Window, 1);
            Assert(host.IsEmpty, "single-line editable text was treated as spare toolbar space");
            DateTime now = DateTime.UtcNow; OverlayPlacement effective;
            ComposerProbeSnapshot snapshot = Snapshot(ComposerProbeState.Composer, now); snapshot.ToolbarHost = host;
            Rectangle result = ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1, snapshot, now, out effective);
            Assert(effective == OverlayPlacement.ComposerBottom && result.IsEmpty, "unsafe single-line composer did not hide");
        }

        internal static void SeparateToolbarAvoidsAllOccupiedControls()
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            foreach (int origin in new[] { -2400, 0, 1800 })
            {
                Func<int, int> p = x => (int)Math.Round(x * scale);
                Func<Rectangle, Rectangle> r = box => new Rectangle(origin + p(box.X), p(box.Y), p(box.Width), p(box.Height));
                Rectangle editor = r(new Rectangle(395, 896, 700, 28));
                Rectangle send = r(new Rectangle(1078, 930, 32, 32));
                Rectangle[] obstacles = { r(new Rectangle(395, 930, 130, 32)), r(new Rectangle(918, 930, 146, 32)) };
                Rectangle host = ComposerPlacementPolicy.FindToolbarHost(editor, r(new Rectangle(379, 888, 736, 88)),
                    send, obstacles, r(Window), scale);
                Assert(!host.IsEmpty && !host.IntersectsWith(editor) && !host.IntersectsWith(send), "separate row lost its safe space");
                foreach (Rectangle obstacle in obstacles) Assert(!host.IntersectsWith(obstacle), "toolbar overlay covers a native control");
            }
        }

        internal static void AttachmentsAndLargeEditorForceFallback()
        {
            Rectangle container = new Rectangle(379, 760, 736, 216);
            Rectangle editor = new Rectangle(395, 780, 700, 156);
            Rectangle send = new Rectangle(1078, 930, 32, 32);
            Assert(ComposerPlacementPolicy.FindToolbarHost(editor, container, send, null, Window, 1).IsEmpty,
                "large editor intersecting the row still allowed a bottom overlay");
            editor = new Rectangle(395, 780, 700, 140);
            Assert(ComposerPlacementPolicy.FindToolbarHost(editor, container, send,
                new[] { new Rectangle(390, 929, 680, 34) }, Window, 1).IsEmpty,
                "attachment or toolbar text occupying the row was ignored");
            Assert(ComposerPlacementPolicy.FindToolbarHost(editor, container, send,
                new[] { new Rectangle(390, 929, 595, 34) }, Window, 1).IsEmpty,
                "a gap too narrow for 5h was accepted");
        }

        internal static void StaleWindowAndDpiResultsCannotRestoreBottom()
        {
            DateTime now = DateTime.UtcNow;
            ComposerProbeSnapshot snapshot = Snapshot(ComposerProbeState.Composer, now);
            Assert(ComposerPlacementPolicy.IsFresh(snapshot, Handle, Window, 1, now), "fresh same-window result rejected");
            Assert(!ComposerPlacementPolicy.IsFresh(snapshot, new IntPtr(99), Window, 1, now), "other window reused anchor");
            Assert(!ComposerPlacementPolicy.IsFresh(snapshot, Handle, new Rectangle(5, 0, 1205, 986), 1, now), "move reused anchor");
            Assert(!ComposerPlacementPolicy.IsFresh(snapshot, Handle, Window, 1.25f, now), "DPI switch reused anchor");
            Assert(!ComposerPlacementPolicy.IsFresh(snapshot, Handle, Window, 1, now.AddMilliseconds(751)), "stale result survived");
            Assert(!ComposerPlacementPolicy.IsFresh(snapshot, Handle, Window, 1, now.AddMilliseconds(-1)), "future clock result accepted");
            OverlayPlacement effective;
            Rectangle result = ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1, snapshot,
                now.AddSeconds(1), out effective);
            Assert(effective == OverlayPlacement.ComposerBottom && result.IsEmpty, "stale result did not hide");
            snapshot.ToolbarHost = new Rectangle(540, 1100, 360, 28);
            Assert(ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1, snapshot, now, out effective).IsEmpty,
                "outside-window host accepted");
        }

        internal static void PreferenceSurvivesFallbackAndRecovery()
        {
            OverlaySettings settings = new OverlaySettings { Placement = OverlayPlacement.ComposerBottom };
            DateTime now = DateTime.UtcNow; OverlayPlacement effective;
            ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1, null, now, out effective);
            Assert(effective == OverlayPlacement.ComposerBottom && settings.Placement == OverlayPlacement.ComposerBottom, "hiding changed preference");
            Rectangle result = ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1,
                Snapshot(ComposerProbeState.Composer, now), now, out effective);
            Assert(effective == OverlayPlacement.ComposerBottom && result.Top == 934, "safe layout did not restore bottom");
            Rectangle tiny = new Rectangle(0, 0, 400, 500);
            Assert(ComposerPlacementPolicy.Resolve(Handle, tiny, tiny, Size, 1, null, now, out effective).IsEmpty,
                "too-small title bar must hide for tray recovery");
        }

        internal static void ProbeIdentityRejectsSearchAndOtherButtons()
        {
            Assert(!ComposerWindowProbe.IsEditorIdentity("settings-search", "input"), "settings search became composer");
            Assert(!ComposerWindowProbe.IsEditorIdentity("", "ProseMirror-hideselection"), "partial class token became editor");
            Assert(ComposerWindowProbe.IsEditorIdentity("prompt-textarea", ""), "known input id rejected");
            Assert(ComposerWindowProbe.IsEditorIdentity("", "tiptap ProseMirror"), "rich editor class rejected");
            Assert(ComposerWindowProbe.IsSendLabel("发送") && ComposerWindowProbe.IsSendLabel("Stop"), "send/stop labels rejected");
            Assert(!ComposerWindowProbe.IsSendLabel("Add comment") && !ComposerWindowProbe.IsSendLabel("发送给联系人"), "unrelated action accepted");
            Assert(ComposerWindowProbe.IsContextLabel("添加文件等内容") &&
                !ComposerWindowProbe.IsContextLabel("Add comment"), "main composer context control confused with comment action");
            Assert(ComposerWindowProbe.IsEditorCandidate("", "ProseMirror", true, new Rectangle(802, 1059, 21, 36)),
                "real Chromium caret-sized editor was misclassified as no composer");
            Assert(!ComposerWindowProbe.IsEditorCandidate("", "ProseMirror", false, new Rectangle(802, 1059, 21, 36)),
                "non-focusable editor was used as writable composer");
        }

        internal static void SlowProbeHasOneWorkerAndDiscardsInvalidatedResult()
        {
            int calls = 0;
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ManualResetEvent finished = new ManualResetEvent(false))
            using (ComposerWindowProbe probe = new ComposerWindowProbe(delegate(IntPtr handle, Rectangle window, float scale) {
                Interlocked.Increment(ref calls); entered.Set(); release.WaitOne(3000);
                var result = Snapshot(ComposerProbeState.Composer, DateTime.UtcNow); finished.Set(); return result;
            }))
            {
                Assert(probe.Read(Handle, Window, 1) == null && entered.WaitOne(2000), "background probe did not start");
                for (int index = 0; index < 100; index++) Assert(probe.Read(Handle, Window, 1) == null, "slow probe blocked or returned stale data");
                Assert(calls == 1, "slow provider spawned extra workers");
                probe.Invalidate(); release.Set(); Assert(finished.WaitOne(2000), "fake provider did not finish");
                Thread.Sleep(50);
                Assert(probe.Read(IntPtr.Zero, Window, 1) == null, "invalidated in-flight result was published");
                // Dispose while the result is finishing: it must never publish or restart.
                probe.Dispose(); Assert(probe.Read(Handle, Window, 1) == null, "disposed probe published a result");
            }
        }

        internal static void VisibleHiddenRecoveryTraceRequiresStableCaptures()
        {
            DateTime start = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
            var gate = new ComposerVisibilityGate(); OverlayPlacement effective;
            Func<ComposerProbeSnapshot, int, Rectangle> step = (snapshot, ms) =>
                gate.Resolve(Handle, Window, Window, Size, 1f, snapshot, start.AddMilliseconds(ms), out effective);
            Assert(step(Snapshot(ComposerProbeState.Composer, start), 0).IsEmpty, "initial capture flashed before confirmation");
            Assert(step(Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(399)), 399).IsEmpty, "recovery was faster than 400ms");
            Assert(!step(Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(400)), 400).IsEmpty, "safe stable captures never appeared");
            Assert(!step(Snapshot(ComposerProbeState.Unavailable, start.AddMilliseconds(401)), 401).IsEmpty, "one failed capture erased a validated anchor");
            Assert(!step(Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(402)), 402).IsEmpty, "successful refresh restarted the show delay");
            Assert(!step(Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(802)), 802).IsEmpty, "restored space lost its anchor");
            ComposerProbeSnapshot attachment = Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(803));
            attachment.ToolbarHost = Rectangle.Empty;
            Assert(step(attachment, 803).IsEmpty, "attachment occupying the toolbar did not hide");
            Assert(step(Snapshot(ComposerProbeState.NoComposer, start.AddMilliseconds(804)), 804).IsEmpty, "settings page retained pills");
        }

        internal static void RepeatedCacheAndLayoutChangesCannotBypassRecovery()
        {
            DateTime start = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
            OverlayPlacement effective; var gate = new ComposerVisibilityGate();
            ComposerProbeSnapshot cached = Snapshot(ComposerProbeState.Composer, start);
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, cached, start, out effective).IsEmpty, "first sample appeared");
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, cached, start.AddMilliseconds(500), out effective).IsEmpty,
                "UI timer reuse of a single cache counted as two independent captures");
            cached.CapturedUtc = start.AddMilliseconds(501);
            Assert(!gate.Resolve(Handle, Window, Window, Size, 1f, cached, cached.CapturedUtc, out effective).IsEmpty, "second fresh sample was not accepted");
            cached.CapturedUtc = start.AddMilliseconds(502); cached.ToolbarHost.Offset(12, 0);
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, cached, cached.CapturedUtc, out effective).Left == 552, "established overlay blinked instead of smoothly following toolbar shift");
            cached.CapturedUtc = start.AddMilliseconds(902);
            Assert(!gate.Resolve(Handle, Window, Window, Size, 1f, cached, cached.CapturedUtc, out effective).IsEmpty, "new stable layout failed recovery");
            gate.Reset(); cached.CapturedUtc = start.AddMilliseconds(903);
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, cached, cached.CapturedUtc, out effective).IsEmpty, "foreground/placement reset retained visibility");
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, cached, start.AddMilliseconds(1700), out effective).IsEmpty, "expired cache restored visibility");
        }

        internal static void InputModeDoesNotShrinkOrAcceptAnOutsideHost()
        {
            DateTime now = DateTime.UtcNow; OverlayPlacement effective;
            ComposerProbeSnapshot snapshot = Snapshot(ComposerProbeState.Composer, now);
            snapshot.ToolbarHost = new Rectangle(540, 934, 139, 28);
            Assert(ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1f, snapshot, now, out effective).IsEmpty,
                "insufficient space silently removed selected pills instead of hiding");
            snapshot.ToolbarHost = new Rectangle(540, 934, 360, 28); snapshot.HostKind = "AboveComposer";
            Assert(ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1f, snapshot, now, out effective).IsEmpty,
                "legacy above-composer host was accepted");
            snapshot.HostKind = "Toolbar"; snapshot.Pane = new Rectangle(290, 90, 320, 886);
            Assert(ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1f, snapshot, now, out effective).IsEmpty,
                "host crossing into browser pane was accepted");
            snapshot.Pane = Window;
            Assert(ComposerPlacementPolicy.Resolve(Handle, Window, Window, Size, 1f, snapshot, now, out effective).Width == Size.Width,
                "valid toolbar space did not retain the full requested width");
        }

        internal static void UnrelatedFloatingGeometryPreservesVisibleCapsule()
        {
            DateTime start = DateTime.UtcNow; OverlayPlacement effective;
            var gate = new ComposerVisibilityGate();
            gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start), start, out effective);
            DateTime shown = start.AddMilliseconds(400);
            Assert(!gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, shown), shown, out effective).IsEmpty, "baseline failed to appear");
            Rectangle[] popups = { new Rectangle(274, 631, 321, 60), new Rectangle(881, 104, 302, 243),
                new Rectangle(49, 740, 219, 230), new Rectangle(800, 934, 50, 28), new Rectangle(680, 934, 60, 28) };
            for (int i = 0; i < popups.Length; i++)
            {
                DateTime now = start.AddMilliseconds(500 + i * 100);
                var snapshot = Snapshot(ComposerProbeState.Composer, now); snapshot.FloatingBounds = new[] { popups[i] };
                Rectangle result = gate.Resolve(Handle, Window, Window, Size, 1f, snapshot, now, out effective);
                Assert(result == new Rectangle(540, 934, 140, 28), "unrelated sidebar/source/avatar popup reset or moved the visible capsule");
            }
            Assert(ComposerWindowProbe.IsFloatingControl(System.Windows.Automation.ControlType.Window) &&
                ComposerWindowProbe.IsFloatingControl(System.Windows.Automation.ControlType.Menu) &&
                ComposerWindowProbe.IsFloatingControl(System.Windows.Automation.ControlType.ToolTip) &&
                !ComposerWindowProbe.IsFloatingControl(System.Windows.Automation.ControlType.Button), "popup control types misclassified");
        }

        internal static void OverlappingPopupHidesAndRecoveryStillWaits()
        {
            DateTime start = DateTime.UtcNow; OverlayPlacement effective; var gate = new ComposerVisibilityGate();
            gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start), start, out effective);
            DateTime ready = start.AddMilliseconds(400);
            gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, ready), ready, out effective);
            DateTime covered = start.AddMilliseconds(500);
            var popup = Snapshot(ComposerProbeState.Composer, covered); popup.FloatingBounds = new[] { new Rectangle(620, 930, 90, 40) };
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, popup, covered, out effective).IsEmpty, "overlapping popup did not hide immediately");
            DateTime clear = start.AddMilliseconds(600);
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, clear), clear, out effective).IsEmpty, "popup dismissal bypassed recovery wait");
            DateTime recovered = start.AddMilliseconds(1000);
            Assert(!gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, recovered), recovered, out effective).IsEmpty, "popup dismissal failed recovery");
        }

        internal static void TemporaryFailuresHaveBoundedLeaseAndNoBlink()
        {
            DateTime start = DateTime.UtcNow; OverlayPlacement effective;
            var gate = new ComposerVisibilityGate();
            gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start), start, out effective);
            DateTime ready = start.AddMilliseconds(400);
            Rectangle visible = gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, ready), ready, out effective);
            for (int i = 500; i < 1900; i += 100)
                Assert(gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Unavailable, start.AddMilliseconds(i)),
                    start.AddMilliseconds(i), out effective) == visible, "temporary failure flickered or moved the capsule");
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, null, start.AddMilliseconds(1901), out effective).IsEmpty,
                "unreadable captures extended the lease indefinitely");
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(2000)),
                start.AddMilliseconds(2000), out effective).IsEmpty, "expired lease bypassed recovery confirmation");
            Assert(!gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(2400)),
                start.AddMilliseconds(2400), out effective).IsEmpty, "expired lease never restored");
            var cached = Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(2400));
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, cached, start.AddMilliseconds(3901), out effective).IsEmpty,
                "cached success refreshed its own lease");
        }

        internal static void PureMovesFollowWithoutConfirmationAndResizeStillHides()
        {
            foreach (float dpi in new[] { 1f, 1.25f, 2f })
            {
                DateTime start = DateTime.UtcNow; OverlayPlacement effective; var gate = new ComposerVisibilityGate();
                var snapshot = Snapshot(ComposerProbeState.Composer, start); snapshot.Scale = dpi;
                gate.Resolve(Handle, Window, Window, Size, dpi, snapshot, start, out effective);
                snapshot.CapturedUtc = start.AddMilliseconds(400);
                Rectangle visible = gate.Resolve(Handle, Window, Window, Size, dpi, snapshot, snapshot.CapturedUtc, out effective);
                Rectangle area = new Rectangle(-5000, -5000, 10000, 10000);
                for (int i = 1; i <= 10; i++)
                {
                    Rectangle moved = Window; moved.Offset(-37 * i, 12 * i);
                    snapshot.CapturedUtc = start.AddMilliseconds(400 + i * 100);
                    Rectangle expected = visible; expected.Offset(-37 * i, 12 * i);
                    Assert(gate.Resolve(Handle, moved, area, Size, dpi, snapshot, snapshot.CapturedUtc, out effective) == expected,
                        "pure window translation broke its relative anchor");
                }
                Rectangle resized = Window; resized.Width--;
                Assert(gate.Resolve(Handle, resized, area, Size, dpi, null, start.AddMilliseconds(1500), out effective).IsEmpty,
                    "resize reused a translated anchor");
                Assert(snapshot.Window == Window && snapshot.ToolbarHost.Left == 540, "translation mutated the worker snapshot");
            }
        }

        internal static void SafeGapChangesKeepEstablishedPosition()
        {
            DateTime start = DateTime.UtcNow; OverlayPlacement effective; var gate = new ComposerVisibilityGate();
            gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start), start, out effective);
            var snapshot = Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(400));
            Rectangle visible = gate.Resolve(Handle, Window, Window, Size, 1f, snapshot, snapshot.CapturedUtc, out effective);
            snapshot = Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(500));
            snapshot.ToolbarHost = new Rectangle(520, 934, 380, 28);
            snapshot.FloatingBounds = new[] { new Rectangle(520, 934, 15, 28) };
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, snapshot, snapshot.CapturedUtc, out effective) == visible,
                "a safe changed gap moved/hid the established capsule");
            snapshot.CapturedUtc = start.AddMilliseconds(600);
            snapshot.FloatingBounds = new[] { new Rectangle(600, 934, 15, 28) };
            Assert(gate.Resolve(Handle, Window, Window, Size, 1f, snapshot, snapshot.CapturedUtc, out effective).IsEmpty,
                "pinning ignored actual capsule obstruction");
        }

        internal static void NativeMoveSessionPreservesAnchorUntilRecheck()
        {
            DateTime start = DateTime.UtcNow; OverlayPlacement effective; var gate = new ComposerVisibilityGate();
            gate.Resolve(Handle, Window, Window, Size, 1f, Snapshot(ComposerProbeState.Composer, start), start, out effective);
            var snapshot = Snapshot(ComposerProbeState.Composer, start.AddMilliseconds(400));
            Rectangle visible = gate.Resolve(Handle, Window, Window, Size, 1f, snapshot, snapshot.CapturedUtc, out effective);
            gate.SetMoving(true, start.AddMilliseconds(500));
            Rectangle area = new Rectangle(-5000, -5000, 10000, 10000);
            Rectangle moved = Window; moved.Offset(90, -30);
            Rectangle expected = visible; expected.Offset(90, -30);
            Assert(gate.Resolve(Handle, moved, area, Size, 1f, null, start.AddSeconds(8), out effective) == expected,
                "long native drag hid a validated relative anchor");
            gate.SetMoving(false, start.AddSeconds(8));
            Assert(gate.Resolve(Handle, moved, area, Size, 1f, null, start.AddMilliseconds(8700), out effective) == expected,
                "mouse release hid before background recheck");
            Assert(gate.Resolve(Handle, moved, area, Size, 1f, null, start.AddMilliseconds(8751), out effective).IsEmpty,
                "finished drag retained an unverified anchor indefinitely");
        }

        internal static void ProbePureMoveKeepsCacheAndInFlightGeneration()
        {
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var probe = new ComposerWindowProbe(delegate(IntPtr handle, Rectangle window, float dpi) {
                entered.Set(); release.WaitOne(2000);
                return Snapshot(ComposerProbeState.Composer, DateTime.UtcNow).InWindow(window);
            }))
            {
                probe.Read(Handle, Window, 1f); Assert(entered.WaitOne(2000), "probe did not start");
                Rectangle moved = Window; moved.Offset(60, -20);
                probe.Read(Handle, moved, 1f); release.Set();
                ComposerProbeSnapshot result = null;
                for (int i = 0; i < 50 && result == null; i++) { Thread.Sleep(10); result = probe.Read(Handle, moved, 1f); }
                Assert(result != null && result.Window == moved && result.ToolbarHost.Location == new Point(600, 914),
                    "pure movement discarded or failed to translate an in-flight capture");
                moved.Offset(10, 10); result = probe.Read(Handle, moved, 1f);
                Assert(result != null && result.ToolbarHost.Location == new Point(610, 924), "pure move erased a published cache");
                moved.Width--;
                Assert(probe.Read(Handle, moved, 1f) == null, "resize retained previous layout cache");
            }
        }

        internal static void PermissionAnchorNeverJumpsToAnotherGap()
        {
            Rectangle pane = Window, container = new Rectangle(379,888,736,88);
            Rectangle editor = new Rectangle(395,896,700,28), permission = new Rectangle(435,930,86,28);
            Rectangle send = new Rectangle(1078,930,32,32);
            var native = new[] { permission, send, new Rectangle(395,930,28,32),new Rectangle(918,930,146,32) };
            Rectangle host = ComposerPlacementPolicy.FindAnchoredToolbarHost(editor,permission,send,container,pane,native,Window,1f);
            Assert(host.Left == 525 && host.Right == 914, "left edge is not permission right plus fixed gap");
            var blocked = new System.Collections.Generic.List<Rectangle>(native) { new Rectangle(525,930,80,32) };
            Assert(ComposerPlacementPolicy.FindAnchoredToolbarHost(editor,permission,send,container,pane,blocked,Window,1f).IsEmpty,
                "blocked anchored gap jumped to a later free region");
            Assert(ComposerWindowProbe.IsPermissionLabel("更改权限") && ComposerWindowProbe.IsPermissionLabel("Change permissions") &&
                !ComposerWindowProbe.IsPermissionLabel("GPT-6.1 Sol 高"), "actual permission ARIA label confused with model selector");
        }

        internal static void ScrollingPaneDoesNotResetAnUnchangedToolbar()
        {
            DateTime start = DateTime.UtcNow; OverlayPlacement effective; var gate = new ComposerVisibilityGate();
            var snapshot = Snapshot(ComposerProbeState.Composer,start);
            snapshot.LeftAnchor = new Rectangle(450,934,86,28);
            gate.Resolve(Handle,Window,Window,Size,1f,snapshot,start,out effective);
            snapshot.CapturedUtc = start.AddMilliseconds(400);
            Rectangle shown = gate.Resolve(Handle,Window,Window,Size,1f,snapshot,snapshot.CapturedUtc,out effective);
            snapshot = Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(500));
            snapshot.LeftAnchor = new Rectangle(450,934,86,28); snapshot.Pane = new Rectangle(290,90,900,890);
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,snapshot,snapshot.CapturedUtc,out effective)==shown,
                "scrolling pane dimensions reset an unchanged native toolbar");
            snapshot.CapturedUtc = start.AddMilliseconds(600);
            snapshot.LeftAnchor.Offset(-20,0); snapshot.ToolbarHost.Offset(-20,0);
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,snapshot,snapshot.CapturedUtc,out effective).Left==520,
                "permission anchor move did not smoothly realign without blinking");
            Assert(ComposerWindowProbe.IsMainContentSurfaceClass("outline-none MainContentSurface-EnuIYE MainContentLeftBorder-IFAvO_") &&
                ComposerWindowProbe.IsMainContentSurfaceClass("_MainContentSurface_abc123") &&
                !ComposerWindowProbe.IsMainContentSurfaceClass("MainContentLeftBorder-IFAvO_"),
                "MainContentSurface class recognition broke");
            Assert(ComposerWindowProbe.IsSendLabel("引导") && ComposerWindowProbe.IsSendLabel("排队") &&
                ComposerWindowProbe.IsSendLabel("Steer") && ComposerWindowProbe.IsSendLabel("Queue") &&
                ComposerWindowProbe.IsPermissionLabel("Change permissions, automatic review enabled") &&
                ComposerWindowProbe.IsPermissionLabel("Approve for me"),
                "active turn steer/queue or auto-review permission labels were rejected");
        }

        internal static void OverflowingComposerContainerUsesVisibleIntersection()
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                Func<Rectangle, Rectangle> p = r => new Rectangle((int)Math.Round(r.X * scale),
                    (int)Math.Round(r.Y * scale), (int)Math.Round(r.Width * scale), (int)Math.Round(r.Height * scale));
                Rectangle window = p(new Rectangle(224, 238, 1171, 988));
                Rectangle pane = p(new Rectangle(515, 334, 876, 888));
                Rectangle container = p(new Rectangle(585, 1108, 736, 228));
                Rectangle editor = p(new Rectangle(597, 1122, 712, 44));
                Rectangle approval = p(new Rectangle(626, 1170, 86, 28));
                Rectangle send = p(new Rectangle(1285, 1170, 28, 28));
                var occupied = new[] { editor, approval, send, p(new Rectangle(1127,1170,122,28)) };
                Rectangle host = ComposerPlacementPolicy.FindAnchoredToolbarHost(editor, approval, send,
                    container, pane, occupied, window, scale);
                Assert(!host.IsEmpty && pane.Contains(host) && host.Left == approval.Right + (int)Math.Round(4 * scale),
                    "unclipped UIA contents ancestor hid a safe visible toolbar");
                Rectangle clippedPane = pane; clippedPane.Height = approval.Top - pane.Top + approval.Height / 2;
                Assert(ComposerPlacementPolicy.FindAnchoredToolbarHost(editor, approval, send,
                    container, clippedPane, occupied, window, scale).IsEmpty, "clipping allowed partially hidden native controls");
                var blocked = new System.Collections.Generic.List<Rectangle>(occupied) { host };
                Assert(ComposerPlacementPolicy.FindAnchoredToolbarHost(editor, approval, send,
                    container, pane, blocked, window, scale).IsEmpty, "visible intersection ignored actual occupation");
            }
        }

        internal static void PublishedAssemblyClipsOverflowingComposer(System.Reflection.Assembly assembly)
        {
            var method = assembly.GetType("CodexUsageOverlay.ComposerPlacementPolicy", true)
                .GetMethod("FindAnchoredToolbarHost", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Rectangle host = (Rectangle)method.Invoke(null, new object[] {
                new Rectangle(597,1122,712,44), new Rectangle(626,1170,86,28), new Rectangle(1285,1170,28,28),
                new Rectangle(585,1108,736,228), new Rectangle(515,334,876,888),
                new[] {new Rectangle(1127,1170,122,28)}, new Rectangle(224,238,1171,988), 1f });
            Assert(host == new Rectangle(716,1170,407,28), "production rejects the captured overflowing contents ancestor");
        }

        internal static void CapturedDragMismatchKeepsTheValidatedAnchor()
        {
            // User drag, 2026-11-06, sample 41: cached context (998,1143),
            // permission (1031,1143), send (1690,1143); the send's current bounds
            // were (1671,1166). Replay that observed (-19,+23) translation in
            // a later parent read. These parent rectangles are reconstructed;
            // the button rectangles and movement delta come from the capture.
            Rectangle window = new Rectangle(501,150,1315,1049);
            Rectangle laterPane = window; laterPane.Offset(-19,23);
            var elements = new[] {
                new ComposerLayoutElement { Context=true, Bounds=new Rectangle(998,1143,29,28), Pane=window, Container=window },
                new ComposerLayoutElement { Approval=true, Bounds=new Rectangle(1031,1143,74,28), Pane=window, Container=window },
                new ComposerLayoutElement { Action=true, Bounds=new Rectangle(1690,1143,29,28), Pane=laterPane, Container=laterPane }
            };
            var incomplete = new ComposerProbeSnapshot();
            Assert(ComposerLayoutRecognition.Select(elements,null,window,1f,incomplete)==null,
                "captured mixed-time geometry unexpectedly formed a complete composer");
            DateTime start = DateTime.UtcNow; OverlayPlacement effective;
            var gate = new ComposerVisibilityGate();
            gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start),start,out effective);
            Rectangle shown = gate.Resolve(Handle,Window,Window,Size,1f,
                Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(400)),start.AddMilliseconds(400),out effective);
            var failure = Snapshot(ComposerWindowProbe.MissingComposerState(false,true),start.AddMilliseconds(500));
            failure.ToolbarHost = Rectangle.Empty;
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,failure,start.AddMilliseconds(500),out effective)==shown,
                "captured context/permission/send mismatch was treated as confirmed absence and hid the capsule");
            Assert(ComposerWindowProbe.MissingComposerState(true,false)==ComposerProbeState.Unavailable &&
                ComposerWindowProbe.MissingComposerState(true,true)==ComposerProbeState.Unavailable,
                "partial editor/anchor recognition bypassed the temporary-failure lease");
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,failure,start.AddMilliseconds(1901),out effective).IsEmpty,
                "partial recognition extended the 1.5 second lease");
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(2000)),
                start.AddMilliseconds(2000),out effective).IsEmpty,"expired anchor recovered without confirmation");
            Assert(!gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(2400)),
                start.AddMilliseconds(2400),out effective).IsEmpty,"stable independent captures did not recover");
            failure = Snapshot(ComposerWindowProbe.MissingComposerState(false,false),start.AddMilliseconds(2500));
            Assert(failure.State==ComposerProbeState.NoComposer && gate.Resolve(Handle,Window,Window,Size,1f,failure,
                start.AddMilliseconds(2500),out effective).IsEmpty,"confirmed missing composer did not hide immediately");
        }

        internal static void MovingCaptureCannotPublishAbsenceOrMixedGeometry()
        {
            Rectangle moved=Window; moved.Offset(-19,23);
            Rectangle resized=Window; resized.Width+=1;
            foreach (Rectangle after in new[] { moved,resized,Rectangle.Empty })
                foreach (ComposerProbeState state in new[] { ComposerProbeState.Composer,ComposerProbeState.NoComposer })
                {
                    var snapshot=Snapshot(state,DateTime.UtcNow);
                    ComposerWindowProbe.CompleteCapture(snapshot,Window,after);
                    Assert(snapshot.State==ComposerProbeState.Unavailable && snapshot.ToolbarHost.IsEmpty,
                        "window changed during capture but published confirmed absence or a mixed-time host");
                }
            var unknown=Snapshot(ComposerProbeState.Composer,DateTime.UtcNow);
            ComposerWindowProbe.CompleteCapture(unknown,Rectangle.Empty,Window);
            Assert(unknown.State==ComposerProbeState.Unavailable && unknown.ToolbarHost.IsEmpty,
                "failed initial native bounds read published a host");
            foreach (ComposerProbeState state in new[] { ComposerProbeState.Composer,ComposerProbeState.NoComposer })
            {
                var snapshot=Snapshot(state,DateTime.UtcNow); snapshot.ToolbarHost=Rectangle.Empty;
                ComposerWindowProbe.CompleteCapture(snapshot,Window,Window);
                Assert(snapshot.State==state,"stable confirmed obstruction or absence became a transient failure");
            }
            DateTime start=DateTime.UtcNow; OverlayPlacement effective;
            var gate=new ComposerVisibilityGate();
            gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start),start,out effective);
            Rectangle shown=gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(400)),
                start.AddMilliseconds(400),out effective);
            gate.SetMoving(true,start.AddMilliseconds(500));
            var transient=Snapshot(ComposerProbeState.NoComposer,start.AddSeconds(8));
            ComposerWindowProbe.CompleteCapture(transient,Window,moved);
            Rectangle expected=shown; expected.Offset(-19,23);
            Assert(gate.Resolve(Handle,moved,new Rectangle(-5000,-5000,10000,10000),Size,1f,transient,
                start.AddSeconds(8),out effective)==expected,"moving capture erased the established relative anchor");
            Assert(gate.Resolve(Handle,resized,Window,Size,1f,null,start.AddMilliseconds(8100),out effective).IsEmpty,
                "resize retained the drag anchor");
        }

        internal static void NativeDragFreezesUntilPostReleaseValidation()
        {
            DateTime start=DateTime.UtcNow; OverlayPlacement effective;
            var gate=new ComposerVisibilityGate();
            gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start),start,out effective);
            Rectangle shown=gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(400)),
                start.AddMilliseconds(400),out effective);
            gate.SetMoving(true,start.AddMilliseconds(500));
            Rectangle moved=Window; moved.Offset(37,-19);
            Rectangle expected=shown; expected.Offset(37,-19);
            Rectangle area=new Rectangle(-5000,-5000,10000,10000);
            foreach (ComposerProbeState state in new[] { ComposerProbeState.NoComposer,ComposerProbeState.Composer })
            {
                var mixed=Snapshot(state,start.AddSeconds(8)); mixed.ToolbarHost=Rectangle.Empty;
                Assert(gate.Resolve(Handle,moved,area,Size,1f,mixed,start.AddSeconds(8),out effective)==expected,
                    "native drag trusted an in-flight false absence/empty host instead of its verified anchor");
            }
            DateTime released=start.AddSeconds(8);
            gate.SetMoving(false,released);
            var old=Snapshot(ComposerProbeState.NoComposer,released.AddMilliseconds(-1));
            Assert(gate.Resolve(Handle,moved,area,Size,1f,old,released.AddMilliseconds(100),out effective)==expected,
                "pre-release snapshot erased the release grace");
            var unavailable=Snapshot(ComposerProbeState.Unavailable,released.AddMilliseconds(700));
            Assert(gate.Resolve(Handle,moved,area,Size,1f,unavailable,released.AddMilliseconds(750),out effective)==expected,
                "temporary unreadability erased release grace before 750ms");
            Assert(gate.Resolve(Handle,moved,area,Size,1f,unavailable,released.AddMilliseconds(751),out effective).IsEmpty,
                "unavailable or old capture extended release grace");

            gate=new ComposerVisibilityGate();
            gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start),start,out effective);
            gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(400)),
                start.AddMilliseconds(400),out effective);
            gate.SetMoving(true,start.AddMilliseconds(500)); gate.SetMoving(false,start.AddMilliseconds(600));
            var fresh=Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(601)); fresh.ToolbarHost.Offset(8,0);
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,fresh,start.AddMilliseconds(601),out effective).Left==shown.Left+8,
                "post-release valid capture failed to recalibrate");
            gate.SetMoving(true,start.AddMilliseconds(700)); gate.SetMoving(false,start.AddMilliseconds(800));
            fresh=Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(801)); fresh.ToolbarHost=Rectangle.Empty;
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,fresh,start.AddMilliseconds(801),out effective).IsEmpty,
                "post-release confirmed insufficient space was ignored");
        }

        internal static void DragCannotConfirmOrCarryAnInvalidAnchor()
        {
            DateTime start=DateTime.UtcNow; OverlayPlacement effective;
            var gate=new ComposerVisibilityGate();
            gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start),start,out effective);
            gate.SetMoving(true,start.AddMilliseconds(100));
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddSeconds(2)),
                start.AddSeconds(2),out effective).IsEmpty,"drag displayed an unconfirmed anchor");
            gate.SetMoving(false,start.AddSeconds(2));
            Assert(gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(2001)),
                start.AddMilliseconds(2001),out effective).IsEmpty,"drag carried pre-move confirmation into recovery");
            Assert(!gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(2401)),
                start.AddMilliseconds(2401),out effective).IsEmpty,"two post-drag captures did not confirm");
            for(int change=0;change<4;change++)
            {
                gate=new ComposerVisibilityGate();
                gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start),start,out effective);
                gate.Resolve(Handle,Window,Window,Size,1f,Snapshot(ComposerProbeState.Composer,start.AddMilliseconds(400)),
                    start.AddMilliseconds(400),out effective);
                gate.SetMoving(true,start.AddMilliseconds(500));
                Rectangle target=Window; if(change==0)target.Width++;
                Assert(gate.Resolve(change==1?new IntPtr(43):Handle,target,Window,
                    change==3?new Size(Size.Width,Size.Height+1):Size,change==2?1.25f:1f,null,
                    start.AddMilliseconds(600),out effective).IsEmpty,"drag retained an anchor across size/target/DPI/height change");
            }
        }

        internal static void ProbeDragPausesAndDiscardsInFlightResults()
        {
            int calls=0;
            using(var secondEntered=new ManualResetEvent(false))
            using(var releaseSecond=new ManualResetEvent(false))
            using(var thirdEntered=new ManualResetEvent(false))
            using(var releaseThird=new ManualResetEvent(false))
            using(var probe=new ComposerWindowProbe(delegate(IntPtr handle,Rectangle window,float scale) {
                int call=Interlocked.Increment(ref calls);
                if(call==2) { secondEntered.Set(); releaseSecond.WaitOne(3000); }
                if(call==3) { thirdEntered.Set(); releaseThird.WaitOne(3000); }
                var snapshot=Snapshot(call==2?ComposerProbeState.NoComposer:ComposerProbeState.Composer,DateTime.UtcNow);
                return snapshot.InWindow(window);
            }))
            {
                try
                {
                    probe.Read(Handle,Window,1f);
                    Assert(secondEntered.WaitOne(2000),"probe did not enter its second capture");
                    probe.SetMoving(true);
                    Rectangle moved=Window; moved.Offset(-19,23);
                    var cached=probe.Read(Handle,moved,1f);
                    Assert(cached!=null && cached.State==ComposerProbeState.Composer && cached.ToolbarHost.Left==521,
                        "pausing lost the last verified translated cache");
                    releaseSecond.Set(); Thread.Sleep(280);
                    Assert(Interlocked.CompareExchange(ref calls,0,0)==2 &&
                        probe.Read(Handle,moved,1f).State==ComposerProbeState.Composer,
                        "paused worker continued capturing or published its old in-flight absence");
                    moved.Offset(3,4); probe.Read(Handle,moved,1f);
                    probe.SetMoving(false);
                    Assert(thirdEntered.WaitOne(2000),"release failed to wake capture");
                    Assert(probe.Read(Handle,moved,1f)==null,"release reused a pre-move cache as fresh validation");
                    releaseThird.Set(); ComposerProbeSnapshot fresh=null;
                    for(int i=0;i<100&&fresh==null;i++) { Thread.Sleep(10); fresh=probe.Read(Handle,moved,1f); }
                    Assert(fresh!=null && fresh.State==ComposerProbeState.Composer && fresh.Window==moved && fresh.ToolbarHost.Left==524,
                        "resumed capture did not use the final requested window");
                    probe.SetMoving(true); moved.Width++;
                    Assert(probe.Read(Handle,moved,1f)==null,"resize retained paused geometry");
                    probe.Invalidate();
                    probe.Read(Handle,Window,1f); fresh=null;
                    for(int i=0;i<100&&fresh==null;i++) { Thread.Sleep(10); fresh=probe.Read(Handle,Window,1f); }
                    Assert(fresh!=null,"invalidation left the worker paused");
                }
                finally { releaseSecond.Set(); releaseThird.Set(); }
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
