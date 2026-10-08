using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class PopupAnchorPlacementTests
    {
        internal static void CentersDialogsAbovePlusWhenSpaceAllows()
        {
            Rectangle plus = new Rectangle(400, 300, 50, 26);
            Rectangle area = new Rectangle(0, 0, 1000, 800);
            Point position = PopupAnchorPlacement.NearPlus(
                plus, new Size(430, 240), area);
            Assert(position.X + 215 == plus.Left + plus.Width / 2,
                "dialog is not centered on PLUS");
            Assert(position.Y + 240 + 8 == plus.Top,
                "dialog is not directly above PLUS");
        }

        internal static void KeepsNearlyFittingUpdaterAbovePlus()
        {
            Rectangle plus = new Rectangle(400, 300, 50, 26);
            Rectangle area = new Rectangle(0, 0, 1000, 800);
            Point position = PopupAnchorPlacement.NearPlus(
                plus, new Size(396, 310), area);
            Assert(position.Y == 8, "nearly fitting updater moved below PLUS");
            Assert(position.Y + 310 - plus.Top <= 24,
                "updater overlaps PLUS by too much");
        }

        internal static void FallsBelowOnlyWhenAboveCannotFit()
        {
            Rectangle plus = new Rectangle(400, 10, 50, 26);
            Rectangle area = new Rectangle(0, 0, 1000, 800);
            Point position = PopupAnchorPlacement.NearPlus(
                plus, new Size(430, 240), area);
            Assert(position.Y == plus.Bottom + 8,
                "dialog was clipped instead of using available space below");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
