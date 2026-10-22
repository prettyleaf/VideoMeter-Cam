using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SigmaLabCam.UI;

namespace SigmaLabCam
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);

            Settings.Load();
            try
            {
                using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("SigmaLabCam.app.ico"))
                    if (st != null) Theme.AppIcon = new Icon(st);
            }
            catch { }

            var files = new List<string>();
            string shots = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shots" && i + 1 < args.Length) shots = args[++i];
                else if (File.Exists(args[i])) files.Add(args[i]);
            }

            var main = new MainForm();
            if (shots != null) ScreenshotTour.Attach(main, shots);
            else main.Shown += (s, e) => main.OpenAtStartup(files.ToArray());
            Application.Run(main);
        }

        private static int _crashShown;

        private static void ShowCrash(Exception ex)
        {
            if (ex == null || Interlocked.Exchange(ref _crashShown, 1) == 1) return;
            try
            {
                MessageBox.Show("Произошла непредвиденная ошибка:\n\n" + ex.Message +
                                "\n\nПрограмма продолжит работу; если ошибка повторяется, сохраните результаты и перезапустите её.\n\n" +
                                ex.GetType().Name + "\n" + ex.StackTrace, Ui.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            finally { Interlocked.Exchange(ref _crashShown, 0); }
        }
    }
}
