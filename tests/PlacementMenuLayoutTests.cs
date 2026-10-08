using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class PlacementMenuLayoutTests
    {
        internal static void UnifiedMenuActionsUseSeparatePills()
        {
            foreach (PlacementMenuAction action in new[]
            {
                PlacementMenuAction.UpdateCodex,
                PlacementMenuAction.RefreshInterval,
                PlacementMenuAction.ExitApplication,
                PlacementMenuAction.AutoResume,
                PlacementMenuAction.CheckGitHubUpdate
            })
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
            }

            foreach (OverlayPlacement placement in new[]
            {
                OverlayPlacement.TitleBar,
                OverlayPlacement.ContentTop,
                OverlayPlacement.ComposerBottom
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
