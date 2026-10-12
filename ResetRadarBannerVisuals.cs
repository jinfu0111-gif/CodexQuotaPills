using System;
using System.Drawing;

namespace CodexUsageOverlay
{
    // One palette for the quota pills and their transient reminder.
    internal static class QuotaPillVisuals
    {
        internal static Color Surface { get { return PopupUiContext.Dark ? Color.FromArgb(248, 36, 41, 52) : Color.FromArgb(244, 255, 255, 255); } }
        internal static Color CardSurface { get { return PopupUiContext.Dark ? Color.FromArgb(255, 28, 33, 43) : Color.FromArgb(255, 248, 249, 252); } }
        internal static Color Border { get { return PopupUiContext.Dark ? Color.FromArgb(180, 83, 94, 113) : Color.FromArgb(58, 100, 116, 139); } }
        internal static Color Text { get { return PopupUiContext.Dark ? Color.FromArgb(235, 239, 246) : Color.FromArgb(255, 82, 88, 98); } }
        internal static Color MutedText { get { return PopupUiContext.Dark ? Color.FromArgb(167, 178, 197) : Color.FromArgb(255, 100, 110, 125); } }
        internal static readonly Color Shadow = Color.FromArgb(25, 15, 23, 42);
        internal static readonly Color CompletedAccent = Color.FromArgb(16, 185, 129);
        internal static readonly Color ScheduledAccent = Color.FromArgb(245, 158, 11);
        internal static readonly Color DangerText = Color.FromArgb(165, 48, 76);
        internal static readonly Color DangerHover = Color.FromArgb(255, 237, 242);
    }

    internal static class ResetRadarBannerVisuals
    {
        internal static Rectangle CloseButtonBounds(int width, int height)
        {
            const int diameter = 22;
            return new Rectangle(Math.Max(2, width - 36), Math.Max(1, (height - diameter) / 2), diameter, diameter);
        }

        internal static Rectangle CloseHitBounds(int width, int height)
        {
            Rectangle hit = CloseButtonBounds(width, height);
            hit.Inflate(5, 5);
            return Rectangle.Intersect(hit, new Rectangle(0, 0, width, height));
        }

        internal static Rectangle TitleBounds(int width, int height)
        {
            return TextBounds(width, height, 4, 20);
        }

        internal static Rectangle DetailBounds(int width, int height)
        {
            return TextBounds(width, height, 24, 19);
        }

        private static Rectangle TextBounds(int width, int height, int top, int lineHeight)
        {
            const int textLeft = 28;
            int textRight = CloseHitBounds(width, height).Left - 8;
            return new Rectangle(textLeft, top, Math.Max(1, textRight - textLeft), lineHeight);
        }

        internal static Color StatusDot(ResetRadarStatus status)
        {
            return status == ResetRadarStatus.CompletedToday
                ? QuotaPillVisuals.CompletedAccent : QuotaPillVisuals.ScheduledAccent;
        }
    }
}
