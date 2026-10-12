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
        CheckGitHubUpdate,
        ThemeAuto,
        ThemeLight,
        ThemeDark,
        ToggleTokens,
        ToggleResetCredits,
        TogglePlan,
        SessionUsage
    }

    internal static class PlacementMenuLayout
    {
        internal const int LogicalWidth = 344;
        internal const int LogicalHeight = 324;

        internal static bool IsDisplayToggle(PlacementMenuAction action)
        {
            return action == PlacementMenuAction.ToggleTokens || action == PlacementMenuAction.ToggleResetCredits ||
                action == PlacementMenuAction.TogglePlan;
        }

        internal static bool IsThemeAction(PlacementMenuAction action)
        {
            return action >= PlacementMenuAction.ThemeAuto && action <= PlacementMenuAction.ThemeDark;
        }

        internal static Rectangle ChoiceBounds(OverlayPlacement placement)
        {
            int index = (int)placement;
            return new Rectangle(72 + (index % 2) * 131, 48 + (index / 2) * 36, 125, 30);
        }

        internal static Rectangle ActionBounds(PlacementMenuAction action)
        {
            if (action == PlacementMenuAction.CheckGitHubUpdate) return new Rectangle(LogicalWidth - 16 - 81, 8, 81, 30);
            if (action == PlacementMenuAction.AutoResume) return new Rectangle(72, 288, 256, 30);
            if (action == PlacementMenuAction.SessionUsage) return new Rectangle(72, 248, 256, 30);
            if (IsDisplayToggle(action))
            {
                int slot = action == PlacementMenuAction.TogglePlan ? 0 : action == PlacementMenuAction.ToggleTokens ? 1 : 2;
                return new Rectangle(72 + slot * 87, 208, slot == 2 ? 82 : 81, 30);
            }
            if (IsThemeAction(action))
                return new Rectangle(72 + ((int)action - (int)PlacementMenuAction.ThemeAuto) * 87, 168, 81, 30);
            int index = (int)action;
            int width = index == 2 ? 82 : 81;
            return new Rectangle(72 + index * 87, 128, width, 30);
        }

        internal static OverlayPlacement? HitTestPlacement(Point location)
        {
            foreach (OverlayPlacement placement in new[]
            {
                OverlayPlacement.TitleBar,
                OverlayPlacement.ContentTop,
                OverlayPlacement.ComposerBottom,
                OverlayPlacement.Sidebar
            })
            {
                if (ChoiceBounds(placement).Contains(location))
                    return placement;
            }
            return null;
        }

        internal static bool IsClickable(Point location)
        {
            return HitTestPlacement(location).HasValue || HitTestAction(location).HasValue;
        }

        internal static PlacementMenuAction? HitTestAction(Point location)
        {
            foreach (PlacementMenuAction action in new[]
            {
                PlacementMenuAction.UpdateCodex,
                PlacementMenuAction.RefreshInterval,
                PlacementMenuAction.ExitApplication,
                PlacementMenuAction.AutoResume,
                PlacementMenuAction.CheckGitHubUpdate,
                PlacementMenuAction.ThemeAuto,
                PlacementMenuAction.ThemeLight,
                PlacementMenuAction.ThemeDark,
                PlacementMenuAction.ToggleTokens,
                PlacementMenuAction.ToggleResetCredits,
                PlacementMenuAction.TogglePlan,
                PlacementMenuAction.SessionUsage
            })
            {
                if (ActionBounds(action).Contains(location))
                    return action;
            }
            return null;
        }
    }
}
