using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace CodexUsageOverlay
{
    internal enum ComposerUsagePillKind
    {
        None,
        Plan,
        ShortQuota,
        WeeklyQuota,
        Tokens,
        ResetCredits
    }

    internal sealed class ComposerUsagePill
    {
        public ComposerUsagePillKind Kind;
        public string Text;
        public Color DotColor;
        public Rectangle Bounds;
    }

    internal sealed class ComposerUsagePillLayout
    {
        public readonly List<ComposerUsagePill> Pills = new List<ComposerUsagePill>();
        public Rectangle RefreshBounds = Rectangle.Empty;

        public ComposerUsagePill HitTest(Point location)
        {
            foreach (ComposerUsagePill pill in Pills)
            {
                if (pill.Bounds.Contains(location))
                    return pill;
            }
            return null;
        }

        public ComposerUsagePill HitTest(
            ComposerUsagePillKind kind,
            Point location,
            int inflateX,
            int inflateY)
        {
            foreach (ComposerUsagePill pill in Pills)
            {
                if (pill.Kind != kind)
                    continue;
                Rectangle bounds = pill.Bounds;
                bounds.Inflate(Math.Max(0, inflateX), Math.Max(0, inflateY));
                return bounds.Contains(location) ? pill : null;
            }
            return null;
        }

        public static int MeasureWidth(UsageData usage, Graphics graphics, Font font)
        {
            List<ComposerUsagePill> pills = BuildPills(usage);
            int width = 0;
            foreach (ComposerUsagePill pill in pills)
                width += MeasurePillWidth(pill.Text, graphics, font);
            if (pills.Count > 1)
                width += (pills.Count - 1) * 6;
            return Math.Max(110, width + 4);
        }

        public static ComposerUsagePillLayout Create(
            UsageData usage,
            Graphics graphics,
            Font font,
            int canvasWidth)
        {
            ComposerUsagePillLayout layout = new ComposerUsagePillLayout();
            List<ComposerUsagePill> candidates = BuildPills(usage);
            int available = Math.Max(80, canvasWidth - 4);

            // Token and reset-credit pills are useful details, but the two quota
            // windows and plan remain visible first when the Codex window is narrow.
            while (candidates.Count > 1 && MeasureCandidates(candidates, graphics, font) > available)
            {
                int optional = FindOptionalPill(candidates, "Token ");
                if (optional < 0)
                    optional = FindOptionalPill(candidates, "券 ");
                if (optional < 0)
                    optional = candidates.Count - 1;
                candidates.RemoveAt(optional);
            }

            int totalWidth = MeasureCandidates(candidates, graphics, font);
            int left = Math.Max(2, (canvasWidth - totalWidth) / 2);
            foreach (ComposerUsagePill pill in candidates)
            {
                int pillWidth = MeasurePillWidth(pill.Text, graphics, font);
                pill.Bounds = new Rectangle(left, 2, pillWidth, 24);
                layout.Pills.Add(pill);
                left += pillWidth + 6;
            }
            return layout;
        }

        public static void Draw(
            Graphics graphics,
            ComposerUsagePillLayout layout,
            Font font,
            bool refreshHovered,
            bool refreshPressed)
        {
            foreach (ComposerUsagePill pill in layout.Pills)
                DrawPill(graphics, pill, font);
        }

        public static void ExportPreview(string path)
        {
            UsageData usage = new UsageData
            {
                Plan = "Plus",
                HasPlan = true,
                ShortRemaining = 79,
                HasShortRemaining = true,
                WeeklyRemaining = 68,
                HasWeeklyRemaining = true,
                ProfileTokensText = "11.6亿",
                AvailableResetCredits = 2,
                HasAvailableResetCredits = true
            };
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using (Bitmap bitmap = UiRendering.CreateLayeredBitmap(450, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font font = UiRendering.CreateTextFont(
                "Microsoft YaHei UI", 8f, FontStyle.Regular))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.FromArgb(250, 248, 249, 251));
                ComposerUsagePillLayout layout = Create(usage, graphics, font, bitmap.Width);
                Draw(graphics, layout, font, false, false);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        internal static string[] BuildTexts(UsageData usage)
        {
            List<ComposerUsagePill> pills = BuildPills(usage);
            string[] result = new string[pills.Count];
            for (int index = 0; index < pills.Count; index++)
                result[index] = pills[index].Text;
            return result;
        }

        private static List<ComposerUsagePill> BuildPills(UsageData usage)
        {
            List<ComposerUsagePill> result = new List<ComposerUsagePill>();
            if (usage == null)
                usage = new UsageData();

            string plan = String.IsNullOrWhiteSpace(usage.Plan) ? "Codex" : usage.Plan.Trim();
            result.Add(NewPill(ComposerUsagePillKind.Plan,
                plan.ToUpperInvariant(), Color.FromArgb(139, 92, 246)));

            if (usage.HasShortRemaining || usage.ShortRemaining.HasValue)
                result.Add(NewPill(ComposerUsagePillKind.ShortQuota,
                    "5h " + ClampPercent(usage.ShortRemaining) + "%",
                    QuotaColor(usage.ShortRemaining, Color.FromArgb(59, 130, 246))));
            if (usage.HasWeeklyRemaining || usage.WeeklyRemaining.HasValue)
                result.Add(NewPill(ComposerUsagePillKind.WeeklyQuota,
                    "周 " + ClampPercent(usage.WeeklyRemaining) + "%",
                    QuotaColor(usage.WeeklyRemaining, Color.FromArgb(16, 185, 129))));
            if (!String.IsNullOrWhiteSpace(usage.ProfileTokensText) &&
                !String.Equals(usage.ProfileTokensText, "待刷新", StringComparison.Ordinal))
                result.Add(NewPill(ComposerUsagePillKind.Tokens,
                    "Token " + usage.ProfileTokensText.Trim(), Color.FromArgb(245, 158, 11)));
            if (usage.AvailableResetCredits.HasValue && usage.AvailableResetCredits.Value >= 0)
                result.Add(NewPill(ComposerUsagePillKind.ResetCredits,
                    "券 " + usage.AvailableResetCredits.Value.ToString(CultureInfo.InvariantCulture),
                    Color.FromArgb(236, 72, 153)));

            return result;
        }

        private static ComposerUsagePill NewPill(
            ComposerUsagePillKind kind, string text, Color color)
        {
            return new ComposerUsagePill { Kind = kind, Text = text, DotColor = color };
        }

        private static int ClampPercent(int? value)
        {
            return Math.Max(0, Math.Min(100, value ?? 0));
        }

        private static Color QuotaColor(int? remaining, Color normal)
        {
            if (!remaining.HasValue || remaining.Value > 20)
                return normal;
            if (remaining.Value > 0)
                return Color.FromArgb(245, 158, 11);
            return Color.FromArgb(239, 68, 68);
        }

        private static int MeasureCandidates(
            IList<ComposerUsagePill> pills,
            Graphics graphics,
            Font font)
        {
            int width = -6;
            foreach (ComposerUsagePill pill in pills)
                width += MeasurePillWidth(pill.Text, graphics, font) + 6;
            return width;
        }

        private static int MeasurePillWidth(string text, Graphics graphics, Font font)
        {
            return Math.Max(44, (int)Math.Ceiling(graphics.MeasureString(text, font).Width) + 26);
        }

        private static int FindOptionalPill(IList<ComposerUsagePill> pills, string prefix)
        {
            for (int index = pills.Count - 1; index >= 0; index--)
            {
                if (pills[index].Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return -1;
        }

        private static void DrawPill(Graphics graphics, ComposerUsagePill pill, Font font)
        {
            Rectangle shadowBounds = pill.Bounds;
            shadowBounds.Offset(0, 1);
            using (GraphicsPath shadowPath = RoundedRectangle(shadowBounds, 12))
            using (Brush shadow = new SolidBrush(QuotaPillVisuals.Shadow))
                graphics.FillPath(shadow, shadowPath);

            using (GraphicsPath path = RoundedRectangle(pill.Bounds, 12))
            using (Brush fill = new SolidBrush(QuotaPillVisuals.Surface))
            using (Pen border = new Pen(QuotaPillVisuals.Border, 1f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            int dotSize = 6;
            int dotLeft = pill.Bounds.Left + 9;
            int dotTop = pill.Bounds.Top + (pill.Bounds.Height - dotSize) / 2;
            using (Brush halo = new SolidBrush(Color.FromArgb(35,
                pill.DotColor.R, pill.DotColor.G, pill.DotColor.B)))
            using (Brush dot = new SolidBrush(pill.DotColor))
            {
                graphics.FillEllipse(halo, dotLeft - 2, dotTop - 2, dotSize + 4, dotSize + 4);
                graphics.FillEllipse(dot, dotLeft, dotTop, dotSize, dotSize);
            }

            Rectangle textBounds = new Rectangle(
                dotLeft + dotSize + 6,
                pill.Bounds.Top,
                Math.Max(1, pill.Bounds.Right - dotLeft - dotSize - 12),
                pill.Bounds.Height);
            using (Brush text = new SolidBrush(QuotaPillVisuals.Text))
            using (StringFormat format = UiRendering.CreateTextFormat())
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(pill.Text, font, text, textBounds, format);
            }
        }

        private static void DrawRefresh(
            Graphics graphics,
            Rectangle bounds,
            bool hovered,
            bool pressed)
        {
            Color fillColor = pressed
                ? Color.FromArgb(248, 225, 232, 240)
                : hovered
                    ? Color.FromArgb(248, 241, 245, 249)
                    : Color.FromArgb(244, 255, 255, 255);
            using (GraphicsPath path = RoundedRectangle(bounds, 12))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(Color.FromArgb(58, 100, 116, 139), 1f))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
            using (Font icon = new Font("Segoe UI Symbol", 9.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush brush = new SolidBrush(Color.FromArgb(255, 100, 116, 139)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                graphics.DrawString("↻", icon, brush, bounds, format);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class ComposerUsagePillTooltips
    {
        internal static bool IsInteractive(ComposerUsagePillKind kind)
        {
            return kind == ComposerUsagePillKind.WeeklyQuota ||
                kind == ComposerUsagePillKind.ResetCredits;
        }

        internal static QuotaHoverCardContent BuildCreditsCard(UsageData usage)
        {
            usage = usage ?? new UsageData();
            bool known = usage.AvailableResetCredits.HasValue;
            int count = Math.Max(0, usage.AvailableResetCredits ?? 0);
            List<string> rows = new List<string>();
            QuotaHoverCardContent card = new QuotaHoverCardContent();
            card.Title = "重置券 · " + (known ? "可用 " + count.ToString() + " 张" : "待刷新");
            long?[] expiries = usage.HasResetCreditDetails
                ? usage.ResetCreditExpiries ?? new long?[0] : new long?[0];
            int supplied = Math.Min(count, expiries.Length);
            for (int index = 0; index < supplied; index++)
                rows.Add("券 " + (index + 1).ToString() + "  " +
                    (expiries[index].HasValue && ResetCreditDetails.IsValidExpiry(expiries[index].Value)
                        ? FormatExpiry(expiries[index].Value, "yyyy/M/d HH:mm") + " 到期"
                        : "到期时间待刷新"));
            if (!known) rows.Add("数量与到期时间待刷新");
            else if (count == 0) rows.Add("当前没有可用重置券");
            else if (supplied < count)
                rows.Add((supplied == 0 ? "共 " : "另 ") + (count - supplied).ToString() +
                    " 张 · 到期时间待刷新");
            rows.Add(usage.Source == "缓存" || !String.IsNullOrWhiteSpace(usage.LastError)
                ? "本机时间 · 缓存，等待实时刷新" : "到期时间按本机时区显示");
            card.DetailLines = rows.ToArray();
            return card;
        }

        internal static QuotaHoverCardContent BuildCard(
            UsageData usage,
            ResetRadarData radar,
            DateTimeOffset now)
        {
            usage = usage ?? new UsageData();
            QuotaHoverCardContent content = new QuotaHoverCardContent();
            bool liveSignal = ResetRadarDisplay.ShouldShow(radar, now);
            content.TiboLine = liveSignal
                ? "Tibo  " + ResetRadarDisplay.BuildPillLabel(radar, now) + " · 非官方"
                : radar == null || radar.Status == ResetRadarStatus.Loading
                    ? "Tibo  预告待刷新"
                    : !radar.NetworkAvailable || radar.IsFromCache
                        ? "Tibo  数据离线 · 不作预告"
                        : "Tibo  暂无有效预告";
            content.ShortLine = BuildResetLine(
                "5h", usage.ShortResetText, usage.HasShortResetText);
            content.WeeklyLine = BuildResetLine(
                "周", usage.WeeklyResetText, usage.HasWeeklyResetText);

            int credits = Math.Max(0, usage.AvailableResetCredits ?? 0);
            if ((usage.HasAvailableResetCredits || usage.AvailableResetCredits.HasValue) &&
                credits > 0)
            {
                content.ExtraLine = "重置券  可用 " + credits.ToString() + " 张";
                if (usage.ResetCreditExpiresAt.HasValue &&
                    usage.ResetCreditExpiresAt.Value > 0)
                    content.ExtraLine += " · 最早到期 " +
                        FormatExpiry(usage.ResetCreditExpiresAt.Value);
                content.ExtraIsTibo = false;
                return content;
            }

            content.ExtraLine = usage.HasAvailableResetCredits || usage.AvailableResetCredits.HasValue
                ? "重置券  0 张"
                : "重置券  待刷新";
            content.ExtraIsTibo = false;
            return content;
        }

        private static string BuildResetLine(
            string label, string resetText, bool hasResetText)
        {
            if (!hasResetText || String.IsNullOrWhiteSpace(resetText) ||
                String.Equals(resetText, "待刷新", StringComparison.Ordinal) ||
                String.Equals(resetText, "—", StringComparison.Ordinal))
                return label + "  重置时间待刷新";
            return label + "  " + resetText.Trim() + " 重置";
        }

        private static string FormatExpiry(long unixSeconds)
        {
            return FormatExpiry(unixSeconds, "M/d HH:mm");
        }

        private static string FormatExpiry(long unixSeconds, string format)
        {
            try
            {
                DateTime local = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(unixSeconds).ToLocalTime();
                return local.ToString(format, CultureInfo.InvariantCulture);
            }
            catch
            {
                return "未知";
            }
        }
    }

    internal sealed class QuotaHoverCardContent
    {
        public string Title = "额度重置";
        public string[] DetailLines;
        public string ShortLine = String.Empty;
        public string WeeklyLine = String.Empty;
        public string ExtraLine = String.Empty;
        public string TiboLine = String.Empty;
        public bool ExtraIsTibo;

        public string RevisionKey
        {
            get
            {
                return Title + "|" + (DetailLines == null ? String.Empty : String.Join("|", DetailLines)) +
                    "|" + ShortLine + "|" + WeeklyLine + "|" + ExtraLine + "|" + TiboLine + "|" +
                    (ExtraIsTibo ? "tibo" : "credits");
            }
        }
    }
}
