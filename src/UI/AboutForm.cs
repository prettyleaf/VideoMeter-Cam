using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SigmaLabCam.UI
{
    public sealed class AboutForm : Form
    {
        public const string Developer = "Колесников И.Д.";
        public const string Department = "Кафедра телевидения и управления (ТУ) ТУСУР";

        public AboutForm()
        {
            Theme.ApplyForm(this);
            Text = "О программе";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(580), Theme.S(540));
            BackColor = Color.White;

            var band = new Panel { Dock = DockStyle.Top, Height = Theme.S(124), BackColor = Theme.Primary };
            var logo = new PictureBox { Image = Theme.Logo(Theme.S(88)), SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(Theme.S(20), Theme.S(18)), BackColor = Theme.Primary };
            var name = new Label
            {
                Text = Ui.AppName,
                Font = Theme.TitleFont,
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(Theme.S(124), Theme.S(20)),
                BackColor = Theme.Primary
            };
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            var sub = new Label
            {
                Text = "SigmaLab Cam, версия " + v.Major + "." + v.Minor + "." + v.Build + "\n" +
                       "Программа кафедры телевидения и управления ТУСУР\n" +
                       "Измерение и коррекция координатных искажений",
                ForeColor = Color.FromArgb(225, 225, 245),
                AutoSize = true,
                Location = new Point(Theme.S(126), Theme.S(58)),
                BackColor = Theme.Primary
            };
            band.Controls.Add(logo);
            band.Controls.Add(name);
            band.Controls.Add(sub);

            var body = new Label
            {
                Location = new Point(Theme.S(24), Theme.S(140)),
                Size = new Size(Theme.S(532), Theme.S(330)),
                ForeColor = Theme.Text,
                Text =
                    "Учебное программное обеспечение кафедры телевидения и управления (ТУ) Томского государственного " +
                    "университета систем управления и радиоэлектроники (ТУСУР) для лабораторной работы № 4 «Измерение и " +
                    "коррекция координатных искажений в телевизионном изображении программными средствами» по дисциплинам " +
                    "«Измерительное телевидение» и «Телевизионные измерения».\n\n" +
                    "Содержит захват изображения с ТВ-камеры, модуль Distortion (аналог модуля программы Imatest), " +
                    "калибровку камеры и выпрямление изображения по сетке точек (аналог программы ASRix), расчёт " +
                    "коэффициентов координатных искажений и модель сферических искажений (вместо файлов Mathcad).\n\n" +
                    "Авторы лабораторной работы:\n" +
                    "    Каменский А.В. — доцент кафедры ТУ\n" +
                    "    Курячий М.И. — профессор кафедры ТУ\n\n" +
                    "Разработчик программы:\n" +
                    "    " + Developer + "\n\n" +
                    "© 2026 ТУСУР, кафедра телевидения и управления. Программа распространяется на условиях " +
                    "учебной лицензии кафедры ТУ (см. «Лицензионное соглашение»). Imatest — товарный знак Imatest LLC, " +
                    "Mathcad — товарный знак PTC Inc., ASRix — программа сторонних авторов; их код в программе не используется."
            };
            var ok = Theme.MakeButton("OK", true);
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(ClientSize.Width - Theme.S(112), ClientSize.Height - Theme.S(48));
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            var license = Theme.MakeButton("Лицензионное соглашение");
            license.Location = new Point(Theme.S(24), ClientSize.Height - Theme.S(48));
            license.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            license.Click += (s, e) => LicenseForm.ShowLicense(this);
            var dots = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(6), BackColor = Theme.Accent };
            Controls.Add(body);
            Controls.Add(ok);
            Controls.Add(license);
            Controls.Add(band);
            Controls.Add(dots);
            AcceptButton = ok;
            CancelButton = ok;
        }
    }

    /// <summary>Текст лицензионного соглашения (встроенный ресурс license.txt).</summary>
    public static class LicenseForm
    {
        public static string LoadText()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SigmaLabCam.license.txt"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        public static void ShowLicense(IWin32Window owner)
        {
            using (var f = new Form())
            {
                Theme.ApplyForm(f);
                f.Text = "Лицензионное соглашение";
                f.StartPosition = FormStartPosition.CenterParent;
                f.ShowInTaskbar = false;
                f.MinimizeBox = false;
                f.Size = new Size(Theme.S(640), Theme.S(620));
                Theme.FitToScreen(f);
                var tb = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.None,
                    Text = LoadText()
                };
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(14)), BackColor = Color.White };
                pad.Controls.Add(tb);
                var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(48), BackColor = Theme.SurfaceAlt };
                var close = Theme.MakeButton("Закрыть", true);
                close.DialogResult = DialogResult.OK;
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                close.Location = new Point(bar.Width - Theme.S(110), Theme.S(9));
                bar.Resize += (s, e) => close.Left = bar.Width - close.Width - Theme.S(14);
                bar.Controls.Add(close);
                f.Controls.Add(pad);
                f.Controls.Add(bar);
                f.AcceptButton = close;
                f.CancelButton = close;
                f.Shown += (s, e) => { tb.SelectionStart = 0; tb.SelectionLength = 0; };
                f.ShowDialog(owner);
            }
        }
    }

    /// <summary>Руководство в формате HTML: открывается в браузере по умолчанию.</summary>
    public static class HelpForm
    {
        public static void ShowHelp(IWin32Window owner, string anchor)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "SigmaLabCam");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "SigmaLabCam_help.html");
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SigmaLabCam.help.html"))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    File.WriteAllText(file, r.ReadToEnd(), new UTF8Encoding(true));
                string target = file;
                if (!string.IsNullOrEmpty(anchor))
                {
                    // переход к разделу через промежуточную страницу: якорь в пути к файлу ShellExecute не передаёт
                    target = Path.Combine(dir, "help_" + anchor + ".html");
                    File.WriteAllText(target,
                        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0; url=" +
                        Path.GetFileName(file) + "#" + anchor + "\"></head><body></body></html>", new UTF8Encoding(true));
                }
                Process.Start(target);
            }
            catch (Exception ex)
            {
                Ui.Error(owner, "Не удалось открыть руководство: " + ex.Message);
            }
        }
    }
}
