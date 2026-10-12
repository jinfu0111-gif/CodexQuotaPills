using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class UiRendering
    {
        public const float LogicalDpi = 96f;

        // Invoke the controls' actual managed paint methods without showing a
        // Form. DrawToBitmap alone skips children of an invisible parent.
        internal static Bitmap RenderOffscreen(Control root)
        {
            Form form = root as Form;
            if (form != null) form.AutoScaleMode = AutoScaleMode.None;
            PrepareOffscreenHandles(root);
            root.PerformLayout();
            using (OffscreenPainter painter = new OffscreenPainter())
                return painter.Render(root);
        }

        private static void PrepareOffscreenHandles(Control control)
        {
            // Hidden handles are allowed. Prepare them before measuring, so
            // DrawToBitmap cannot trigger late native layout/auto-scaling.
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) PrepareOffscreenHandles(child);
        }

        private sealed class OffscreenPainter : Control
        {
            internal Bitmap Render(Control control)
            {
                // Native GDI text does not maintain an alpha channel. An opaque
                // RGB surface avoids corrupted glyphs when compositing children.
                Bitmap bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height), PixelFormat.Format24bppRgb);
                bitmap.SetResolution(LogicalDpi, LogicalDpi);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(control.BackColor);
                    if (!control.HasChildren && !(control is Form))
                        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    else using (PaintEventArgs args = new PaintEventArgs(graphics, control.ClientRectangle))
                    {
                        InvokePaintBackground(control, args);
                        InvokePaint(control, args);
                    }
                    // These preview forms intentionally include all configured
                    // controls; do not consult inherited Visible on hidden roots.
                    for (int index = control.Controls.Count - 1; index >= 0; index--)
                    {
                        Control child = control.Controls[index];
                        if (child.Width <= 0 || child.Height <= 0) continue;
                        // GDI TextRenderer does not honor Graphics transforms;
                        // render each real control in its own local bitmap/DC.
                        using (Bitmap childImage = Render(child))
                            graphics.DrawImageUnscaled(childImage, child.Left, child.Top);
                    }
                }
                return bitmap;
            }
        }

        private static readonly string[] FallbackFontNames =
        {
            "Microsoft YaHei UI",
            "Segoe UI",
            "Microsoft Sans Serif"
        };

        private static readonly string[] SafeTextFontNames =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun", "NSimSun",
            "DengXian", "FangSong", "KaiTi", "Arial", "Tahoma", "Calibri", "Microsoft Sans Serif",
            "Microsoft JhengHei UI", "Microsoft JhengHei", "Yu Gothic UI", "Meiryo UI",
            "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC"
        };

        public static Bitmap CreateLayeredBitmap(int width, int height)
        {
            Bitmap bitmap = new Bitmap(
                Math.Max(1, width),
                Math.Max(1, height),
                PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(LogicalDpi, LogicalDpi);
            return bitmap;
        }

        public static Font CreateTextFont(string requestedName, float size, FontStyle style)
        {
            List<string> candidates = new List<string>();
            if (IsSafeTextFontName(requestedName))
                candidates.Add(requestedName.Trim());
            foreach (string fallback in FallbackFontNames)
            {
                if (!ContainsIgnoreCase(candidates, fallback))
                    candidates.Add(fallback);
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    Font font = new Font(candidate, size, style, GraphicsUnit.Point);
                    if (IsSafeTextFontName(font.FontFamily.Name) &&
                        String.Equals(font.FontFamily.Name, candidate, StringComparison.OrdinalIgnoreCase))
                        return font;
                    font.Dispose();
                }
                catch
                {
                }
            }

            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }

        public static string NormalizeFontName(string requestedName)
        {
            using (Font font = CreateTextFont(requestedName, 9f, FontStyle.Regular))
                return font.FontFamily.Name;
        }

        public static StringFormat CreateTextFormat()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags &= ~(StringFormatFlags.FitBlackBox |
                StringFormatFlags.LineLimit | StringFormatFlags.NoClip);
            return format;
        }

        public static bool IsSafeTextFontName(string fontName)
        {
            if (String.IsNullOrWhiteSpace(fontName))
                return false;
            string value = fontName.Trim();
            foreach (string safeName in SafeTextFontNames)
            {
                if (String.Equals(value, safeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool ContainsIgnoreCase(List<string> values, string candidate)
        {
            foreach (string value in values)
            {
                if (String.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
