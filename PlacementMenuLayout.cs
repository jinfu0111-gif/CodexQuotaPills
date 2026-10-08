using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal enum PlacementMenuAction
    {
        UpdateCodex,
        RefreshInterval,
        ExitApplication,
        AutoResume,
        CheckGitHubUpdate
    }

    internal static class PlacementMenuLayout
    {
        internal const int LogicalWidth = 344;
        internal const int LogicalHeight = 166;

        internal static Rectangle ChoiceBounds(OverlayPlacement placement)
        {
            int index = placement == OverlayPlacement.TitleBar ? 0 :
                placement == OverlayPlacement.ContentTop ? 1 : 2;
            int width = index == 2 ? 82 : 81;
            return new Rectangle(72 + index * 87, 48, width, 30);
        }

        internal static Rectangle ActionBounds(PlacementMenuAction action)
        {
            if (action == PlacementMenuAction.CheckGitHubUpdate) return new Rectangle(190, 8, 138, 30);
            if (action == PlacementMenuAction.AutoResume) return new Rectangle(72, 128, 256, 30);
            int index = (int)action;
            int width = index == 2 ? 82 : 81;
            return new Rectangle(72 + index * 87, 88, width, 30);
        }

        internal static OverlayPlacement? HitTestPlacement(Point location)
        {
            foreach (OverlayPlacement placement in new[]
            {
                OverlayPlacement.TitleBar,
                OverlayPlacement.ContentTop,
                OverlayPlacement.ComposerBottom
            })
            {
                if (ChoiceBounds(placement).Contains(location))
                    return placement;
            }
            return null;
        }

        internal static PlacementMenuAction? HitTestAction(Point location)
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
                if (ActionBounds(action).Contains(location))
                    return action;
            }
            return null;
        }
    }
}
