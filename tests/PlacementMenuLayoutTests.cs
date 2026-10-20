using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class PlacementMenuLayoutTests
    {
        internal static void HeaderSizeAndCursorFeedbackAreConsistent()
        {
            Rectangle header = PlacementMenuLayout.ActionBounds(PlacementMenuAction.CheckGitHubUpdate);
            Rectangle common = PlacementMenuLayout.ActionBounds(PlacementMenuAction.UpdateCodex);
            Assert(header.Size == common.Size, "header update capsule larger than common actions");
            Assert(header.Right == PlacementMenuLayout.LogicalWidth - 16, "header lost right margin");
            foreach (PlacementMenuAction action in Enum.GetValues(typeof(PlacementMenuAction)))
            {
                Rectangle bounds = PlacementMenuLayout.ActionBounds(action);
                Assert(PlacementMenuLayout.IsClickable(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)), "action has no hand target");
            }
            Assert(!PlacementMenuLayout.IsClickable(new Point(120, 43)), "divider has hand cursor");
            using (UpdatePillButton button = new UpdatePillButton("test", UpdateMenuVisuals.CreateRainbowPalette(), false))
            {
                Assert(button.Cursor == System.Windows.Forms.Cursors.Hand, "enabled button not clickable cursor");
                button.Enabled = false;
                Assert(button.Cursor == System.Windows.Forms.Cursors.Default, "disabled button still suggests click");
                button.Enabled = true;
                Assert(button.Cursor == System.Windows.Forms.Cursors.Hand, "re-enabled button lacks hand cursor");
            }
        }
        internal static void UnifiedMenuActionsUseSeparatePills()
        {
            foreach (PlacementMenuAction action in Enum.GetValues(typeof(PlacementMenuAction)))
            {
                Rectangle bounds = PlacementMenuLayout.ActionBounds(action);
                Point center = new Point(bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2);
                Assert(PlacementMenuLayout.HitTestAction(center) == action,
                    "action pill did not map to " + action);
                Assert(!PlacementMenuLayout.HitTestPlacement(center).HasValue,
                    "action pill overlaps the placement row");
                Assert(bounds.Left >= 0 && bounds.Right <= PlacementMenuLayout.LogicalWidth &&
                    bounds.Bottom < PlacementMenuLayout.LogicalHeight,
                    "action pill falls outside the menu");
                if (PlacementMenuLayout.IsDisplayToggle(action))
                    Assert(!PlacementMenuLayout.IsThemeAction(action), "display toggle is mistaken for a theme change");
                foreach (PlacementMenuAction other in Enum.GetValues(typeof(PlacementMenuAction)))
                    if (other != action) Assert(!bounds.IntersectsWith(PlacementMenuLayout.ActionBounds(other)), "action pills overlap");
            }

            foreach (OverlayPlacement placement in new[]
            {
                OverlayPlacement.TitleBar,
                OverlayPlacement.ComposerBottom,
                OverlayPlacement.Sidebar
            })
            {
                Rectangle bounds = PlacementMenuLayout.ChoiceBounds(placement);
                Point center = new Point(bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2);
                Assert(PlacementMenuLayout.HitTestPlacement(center) == placement,
                    "placement pill did not map to " + placement);
                Assert(!PlacementMenuLayout.HitTestAction(center).HasValue,
                    "placement pill overlaps the action row");
            }
            Assert(PlacementMenuLayout.ChoiceBounds(OverlayPlacement.ContentTop).IsEmpty,
                "removed window-top placement still has a click target");
            Assert(!PlacementMenuLayout.HitTestAction(new Point(120, 43)).HasValue,
                "divider is clickable");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
