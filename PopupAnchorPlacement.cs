using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    internal static class PopupAnchorPlacement
    {
        internal static Point NearPlus(
            Rectangle plusBounds,
            Size popupSize,
            Rectangle workingArea,
            int gap = 8,
            int margin = 8)
        {
            int minLeft = workingArea.Left + margin;
            int maxLeft = Math.Max(minLeft, workingArea.Right - popupSize.Width - margin);
            int centeredLeft = plusBounds.Left + (plusBounds.Width - popupSize.Width) / 2;
            int left = Math.Max(minLeft, Math.Min(centeredLeft, maxLeft));

            int minTop = workingArea.Top + margin;
            int maxTop = Math.Max(minTop, workingArea.Bottom - popupSize.Height - margin);
            int above = plusBounds.Top - popupSize.Height - gap;
            int top;
            if (above >= minTop)
                top = above;
            else if (plusBounds.Top - minTop >= popupSize.Height - 24)
                top = minTop; // Almost fits above; a small anchor overlap is preferable.
            else
            {
                int below = plusBounds.Bottom + gap;
                top = below + popupSize.Height <= workingArea.Bottom - margin
                    ? below
                    : minTop;
            }
            return new Point(left, Math.Max(minTop, Math.Min(top, maxTop)));
        }
    }
}
