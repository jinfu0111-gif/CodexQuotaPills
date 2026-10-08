using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class ExportResetBannerPreview
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, Fields).SetValue(target, value); }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected executable and output directory.");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type dataType = assembly.GetType("CodexUsageOverlay.ResetRadarData", true);
        Type settingsType = assembly.GetType("CodexUsageOverlay.OverlaySettings", true);
        Type statusType = assembly.GetType("CodexUsageOverlay.ResetRadarStatus", true);
        Type bannerType = assembly.GetType("CodexUsageOverlay.ResetRadarBannerForm", true);
        object settings = Activator.CreateInstance(settingsType, true);
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        DateTimeOffset now = new DateTimeOffset(2026, 10, 8, 13, 0, 0, TimeSpan.FromHours(8));
        foreach (bool scheduled in new[] { false, true })
        {
            object data = Activator.CreateInstance(dataType, true);
            Set(data, "Status", Enum.Parse(statusType, scheduled ? "ScheduledToday" : "CompletedToday"));
            Set(data, "StatusLabel", scheduled ? "今日有预告" : "今日已重置");
            Set(data, "Detail", scheduled ? "预计今日 15:00 后有重置" : "Tibo 已宣布完成额度重置");
            Set(data, "ScopeLabel", "全部计划");
            Set(data, "AnnouncedAt", now.AddMinutes(-85));
            Set(data, "Confidence", 0.88d);
            Set(data, "NetworkAvailable", true);
            if (scheduled) { Set(data, "EffectiveAt", now.AddHours(2)); Set(data, "EffectiveUntil", now.AddHours(4)); }
            string prefix = scheduled ? "reset-radar-banner-scheduled-preview" : "reset-radar-banner-preview";
            using (Form form = (Form)Activator.CreateInstance(bannerType, new object[] { null, null }))
            {
                Set(form, "radar", data);
                Set(form, "settings", settings);
                Set(form, "previewNow", now);
                foreach (int scale in new[] { 1, 2 })
                {
                    Set(form, "dpiScale", (float)scale);
                    form.Size = new Size(450 * scale, 48 * scale);
                    foreach (bool closeHover in new[] { false, true })
                    {
                        if (scheduled && closeHover) continue;
                        Set(form, "hovered", closeHover);
                        Set(form, "closeHovered", closeHover);
                        string name = closeHover ? "reset-radar-banner-close-preview" : prefix;
                        if (scale == 2) name += "@2x";
                        using (Bitmap bitmap = (Bitmap)bannerType.GetMethod("BuildRenderedBitmap", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null))
                            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                        Console.WriteLine("Exported=" + name + ".png");
                    }
                }
            }
        }
        Console.WriteLine("ProgramVersion=" + assembly.GetName().Version);
        Console.WriteLine("NetworkRequests=0");
        Console.WriteLine("LiveReminderStateChanged=False");
        return 0;
    }
}
