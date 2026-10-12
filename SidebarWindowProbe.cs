using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexUsageOverlay
{
    internal sealed class SidebarBoundaryState
    {
        internal int Width { get; private set; }
        private int misses;
        internal void Observe(int detected)
        {
            if (detected >= 40 && detected <= 80) { Width = detected; misses = 0; }
            else if (++misses >= 3) Width = 0;
        }
        internal void Reset() { Width = 0; misses = 0; }
    }
    // Electron's accessibility tree is not exposed in every desktop build.
    // Read only visible, neutral background boundaries. No launch flags, input,
    // UI injection, screenshot persistence, or chat text parsing are involved.
    internal sealed class SidebarWindowProbe
    {
        private DateTime nextRead;
        private IntPtr lastWindow;
        private Size lastSize;
        private float lastScale;
        private readonly SidebarBoundaryState boundary = new SidebarBoundaryState();
        private Rectangle lastBounds;
        private readonly object sync = new object();
        private bool reading;

        internal Rectangle Read(IntPtr handle, Rectangle window, Rectangle area, float scale, bool canSample,
            bool synchronous = false)
        {
            lock (sync)
            {
            if (lastWindow != handle) boundary.Reset();
            if (lastWindow != handle || lastSize != window.Size || Math.Abs(lastScale - scale) > 0.01f)
                nextRead = DateTime.MinValue;
            lastWindow = handle; lastSize = window.Size; lastScale = scale;
            lastBounds = window;
            if (canSample && !reading && DateTime.UtcNow >= nextRead)
            {
                nextRead = DateTime.UtcNow.AddMilliseconds(750);
                reading = true;
                Action sample = delegate
                {
                    int detected = 0;
                    try
                    {
                    if (window.Width / scale >= 480 && window.Height / scale >= 700 && area.Contains(window.Location) &&
                    area.Contains(new Point(window.Left + (int)(90 * scale), window.Bottom - (int)(90 * scale))))
                    {
                        // One GPU readback instead of > 1,000 synchronous GetPixel calls.
                        // Sample stays in memory and is disposed immediately.
                        Point origin = new Point(window.Left + (int)(30 * scale), window.Top + (int)(90 * scale));
                        using (Bitmap sampleBitmap = new Bitmap(Math.Max(1, (int)(60 * scale)),
                            Math.Max(1, window.Height - (int)(180 * scale))))
                        using (Graphics graphics = Graphics.FromImage(sampleBitmap))
                        {
                            if (GetForegroundWindow() == handle)
                            {
                                graphics.CopyFromScreen(origin, Point.Empty, sampleBitmap.Size);
                                detected = DetectBoundary(window, scale, delegate(int x, int y)
                                {
                                    x -= origin.X; y -= origin.Y;
                                    return x < 0 || y < 0 || x >= sampleBitmap.Width || y >= sampleBitmap.Height
                                        ? Color.Empty : sampleBitmap.GetPixel(x, y);
                                });
                                if (GetForegroundWindow() != handle) detected = 0;
                            }
                        }
                    }
                    }
                    catch { detected = 0; }
                    finally
                    {
                        lock (sync)
                        {
                            if (lastWindow == handle && lastBounds == window && Math.Abs(lastScale - scale) < 0.01f && GetForegroundWindow() == handle)
                                boundary.Observe(detected);
                            reading = false;
                        }
                    }
                };
                if (synchronous) sample();
                else ThreadPool.QueueUserWorkItem(delegate { sample(); });
            }
            return boundary.Width == 0 ? Rectangle.Empty : new Rectangle(window.Left,
                window.Top, (int)Math.Round(boundary.Width * scale), window.Height);
            }
        }

        internal static int DetectBoundary(Rectangle window, float scale, Func<int, int, Color> pixel)
        {
            int bestWidth = 0, bestScore = 0;
            for (int width = 40; width <= 78; width += 2)
            {
                int score = 0;
                for (int row = 0; row < 6; row++)
                {
                    int y = window.Top + (int)Math.Round((100 +
                        Math.Max(0, window.Height / scale - 240) * (row + 0.5) / 6) * scale);
                    // Stay within the rail's outer gutter, not inside our own
                    // quota rings. Otherwise they can erase the very boundary
                    // used to keep them visible and cause a hide/show loop.
                    Color left = pixel(window.Left + (int)((width - 2) * scale), y);
                    Color right = pixel(window.Left + (int)((width + 2) * scale), y);
                    if (left.IsEmpty || right.IsEmpty) continue;
                    int difference = Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);
                    bool neutral = Math.Abs(left.R - left.G) <= 18 && Math.Abs(left.G - left.B) <= 22 &&
                        Math.Abs(right.R - right.G) <= 18 && Math.Abs(right.G - right.B) <= 22;
                    if (neutral && difference >= 9) score++;
                }
                if (score > bestScore) { bestScore = score; bestWidth = width; }
            }
            // Require agreement across the window, not one line in a code block.
            return bestScore >= 5 ? bestWidth + 2 : 0;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    }
}
