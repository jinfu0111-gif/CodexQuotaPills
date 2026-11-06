using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Only hidden in-memory forms. Never call Show, ShowDialog or Application.Run.
internal static class ValidateOffscreenPreviews
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    [STAThread]
    private static int Main(string[] args)
    {
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int shown = 0;
        foreach (string name in new[] { "FirstRunGuideForm", "CodexClientUpdateForm", "AutoResumeForm", "AutoResetCreditsForm", "SessionUsageForm" })
        {
            Type type = assembly.GetType("CodexUsageOverlay." + name, true);
            object[] parameters = name == "FirstRunGuideForm" ? new object[0] :
                name == "CodexClientUpdateForm" ? new object[] { new Rectangle(0, 0, 400, 30) } :
                name == "SessionUsageForm" ? new object[] { new Rectangle(0, 0, 60, 28), null } :
                new object[] { null, new Rectangle(0, 0, 400, 30) };
            using (Form form = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null))
            {
                form.Shown += delegate { shown++; };
                if (name == "FirstRunGuideForm")
                {
                    for (int index = 0; index < 6; index++)
                    {
                        type.GetField("pageIndex", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, index);
                        type.GetMethod("RenderPage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                        using (Bitmap image = (Bitmap)type.GetMethod("ExportPreviewBitmap").Invoke(form,
                            new object[] { new Rectangle(600, 100, 60, 28), new Rectangle(0, 0, 1920, 1080) }))
                            image.Save(Path.Combine(output, name + index + ".png"));
                        AssertInvisible(form, shown);
                    }
                }
                else
                {
                    type.GetMethod("ExportPreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form,
                        new object[] { Path.Combine(output, name + ".png") });
                    AssertInvisible(form, shown);
                }
                Console.WriteLine("PASS " + name + " offline render; Shown=0, native Visible=False");
            }
        }
        Console.WriteLine("MessagesSent=0; ServicesCreated=0; ProductionSettingsWrites=0; UpdaterActions=0");
        return 0;
    }

    private static void AssertInvisible(Form form, int shown)
    {
        if (shown != 0 || form.Visible || (form.IsHandleCreated && IsWindowVisible(form.Handle)))
            throw new InvalidOperationException("An offline preview displayed a native window.");
    }
}
