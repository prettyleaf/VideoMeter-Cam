using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SigmaLabCam.UI
{
    /// <summary>Сообщения, форматирование чисел, сохранение текстовых файлов.</summary>
    public static class Ui
    {
        public const string AppName = "СигмаЛаб-Кам";

        public static void Error(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void Info(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool Ask(IWin32Window owner, string msg)
        {
            return MessageBox.Show(owner, msg, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public static DialogResult AskCancel(IWin32Window owner, string msg)
        {
            return MessageBox.Show(owner, msg, AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        }

        /// <summary>Число для отображения: «∞» для бесконечности, «—» для NaN.</summary>
        public static string F(double v, int dec = 3)
        {
            if (double.IsNaN(v)) return "—";
            if (double.IsPositiveInfinity(v)) return "∞";
            if (double.IsNegativeInfinity(v)) return "−∞";
            return v.ToString("F" + dec, CultureInfo.CurrentCulture);
        }

        public static string G(double v)
        {
            if (double.IsNaN(v)) return "—";
            if (double.IsPositiveInfinity(v)) return "∞";
            if (double.IsNegativeInfinity(v)) return "−∞";
            double a = Math.Abs(v);
            if (a != 0 && (a < 1e-3 || a >= 1e6)) return v.ToString("0.###E+0", CultureInfo.CurrentCulture);
            if (a >= 1000) return v.ToString("F1", CultureInfo.CurrentCulture);
            if (a >= 10) return v.ToString("F2", CultureInfo.CurrentCulture);
            return v.ToString("F3", CultureInfo.CurrentCulture);
        }

        public static void SaveText(IWin32Window owner, string text, string defaultName)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV (разделитель «;») (*.csv)|*.csv|Текст (*.txt)|*.txt",
                FileName = defaultName,
                Title = "Сохранить данные"
            })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    // UTF-8 с BOM — чтобы Excel правильно показал кириллицу
                    File.WriteAllText(dlg.FileName, text, new UTF8Encoding(true));
                    Settings.LastDir = Path.GetDirectoryName(dlg.FileName);
                }
                catch (Exception ex) { Error(owner, "Не удалось сохранить файл: " + ex.Message); }
            }
        }

        /// <summary>Выполняет действие с курсором ожидания и показывает ошибку, если она возникла.</summary>
        public static bool Busy(Control owner, Action a)
        {
            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                a();
                return true;
            }
            catch (Exception ex)
            {
                Cursor.Current = old;
                Error(owner, ex.Message);
                return false;
            }
            finally
            {
                Cursor.Current = old;
            }
        }
    }
}
