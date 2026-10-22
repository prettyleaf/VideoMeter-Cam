using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using SigmaLabCam.Core;

namespace SigmaLabCam.UI
{
    /// <summary>Поле ввода числа: принимает и точку, и запятую.</summary>
    public sealed class NumBox : TextBox
    {
        public int Decimals;
        public double Min = double.NegativeInfinity, Max = double.PositiveInfinity;
        public string Caption;

        public NumBox()
        {
            Width = Theme.S(90);
            TextAlign = HorizontalAlignment.Right;
        }

        public double Value
        {
            get
            {
                double v;
                return NumParse.TryParse(Text, out v) ? v : double.NaN;
            }
            set { Text = value.ToString(Decimals > 0 ? "0." + new string('#', Decimals) : "0", CultureInfo.CurrentCulture); }
        }

        public int IntValue { get { return (int)Math.Round(Value); } }

        public string Validate()
        {
            double v;
            if (!NumParse.TryParse(Text, out v)) return "Поле «" + Caption + "»: введите число.";
            if (Decimals == 0 && Math.Abs(v - Math.Round(v)) > 1e-9) return "Поле «" + Caption + "»: нужно целое число.";
            if (v < Min || v > Max)
            {
                string range = double.IsInfinity(Max) ? "не меньше " + Min : (double.IsInfinity(Min) ? "не больше " + Max : "от " + Min + " до " + Max);
                return "Поле «" + Caption + "»: значение должно быть " + range + ".";
            }
            return null;
        }
    }

    /// <summary>Диалог с параметрами в две колонки (подпись — поле), как GenericDialog в ImageJ.</summary>
    public class ParamDialog : Form
    {
        private readonly TableLayoutPanel _root;
        private readonly TableLayoutPanel _grid;
        private readonly Label _error;
        private readonly FlowLayoutPanel _buttons;
        private readonly Timer _debounce;
        public readonly Button OkButton, CancelBtn;
        public CheckBox PreviewBox;

        /// <summary>Вызывается при изменении любого поля (с задержкой 250 мс).</summary>
        public event EventHandler Changed;
        public Func<string> Validator;

        public ParamDialog(string title, string intro = null)
        {
            Theme.ApplyForm(this);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _root = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(Theme.S(14), Theme.S(12), Theme.S(14), Theme.S(10)),
                Location = Point.Empty
            };
            if (!string.IsNullOrEmpty(intro))
            {
                var l = new Label
                {
                    Text = intro,
                    AutoSize = true,
                    MaximumSize = new Size(Theme.S(460), 0),
                    ForeColor = Theme.Muted,
                    Margin = new Padding(0, 0, 0, Theme.S(8))
                };
                _root.Controls.Add(l);
            }
            _grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = Padding.Empty };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _root.Controls.Add(_grid);

            _error = new Label { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(Theme.S(460), 0), Visible = false, Margin = new Padding(0, Theme.S(6), 0, 0) };
            _root.Controls.Add(_error);

            _buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(10), 0, 0), WrapContents = false };
            CancelBtn = Theme.MakeButton("Отмена");
            CancelBtn.DialogResult = DialogResult.Cancel;
            OkButton = Theme.MakeButton("OK", true);
            OkButton.Click += (s, e) => TryAccept();
            _buttons.Controls.Add(CancelBtn);
            _buttons.Controls.Add(OkButton);
            _root.Controls.Add(_buttons);
            Controls.Add(_root);
            AcceptButton = OkButton;
            CancelButton = CancelBtn;

            _debounce = new Timer { Interval = 250 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Changed?.Invoke(this, EventArgs.Empty); };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Size pref = _root.GetPreferredSize(Size.Empty);
            if (ClientSize.Width < pref.Width || ClientSize.Height < pref.Height)
                ClientSize = new Size(Math.Max(ClientSize.Width, pref.Width), Math.Max(ClientSize.Height, pref.Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _debounce.Dispose();
            base.Dispose(disposing);
        }

        private void Touch()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        /// <summary>Немедленно вызвать Changed (например, при включении предпросмотра).</summary>
        public void FireChanged() { Changed?.Invoke(this, EventArgs.Empty); }

        public void AddRow(string label, Control c)
        {
            int row = _grid.RowCount++;
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label != null)
            {
                var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Theme.S(4), Theme.S(10), Theme.S(4)) };
                _grid.Controls.Add(l, 0, row);
                _grid.Controls.Add(c, 1, row);
            }
            else
            {
                _grid.Controls.Add(c, 0, row);
                _grid.SetColumnSpan(c, 2);
            }
            c.Margin = new Padding(0, Theme.S(3), 0, Theme.S(3));
        }

        public NumBox AddNumber(string label, double value, int decimals = 0, double min = double.NegativeInfinity, double max = double.PositiveInfinity, string unit = null)
        {
            var nb = new NumBox { Decimals = decimals, Min = min, Max = max, Caption = label.TrimEnd(':') };
            nb.Value = value;
            nb.TextChanged += (s, e) => Touch();
            if (unit != null)
            {
                var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                nb.Margin = Padding.Empty;
                p.Controls.Add(nb);
                p.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(Theme.S(4), Theme.S(4), 0, 0) });
                AddRow(label, p);
            }
            else AddRow(label, nb);
            return nb;
        }

        public ComboBox AddChoice(string label, object[] items, int index, int width = 260)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(width) };
            cb.Items.AddRange(items);
            if (items.Length > 0) cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index));
            cb.SelectedIndexChanged += (s, e) => Touch();
            AddRow(label, cb);
            return cb;
        }

        public CheckBox AddCheck(string text, bool value)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true };
            c.CheckedChanged += (s, e) => Touch();
            AddRow(null, c);
            return c;
        }

        public TextBox AddText(string label, string value, int lines = 1, int width = 260)
        {
            var t = new TextBox { Text = value, Width = Theme.S(width) };
            if (lines > 1)
            {
                t.Multiline = true;
                t.ScrollBars = ScrollBars.Vertical;
                t.Height = Theme.S(18) * lines + Theme.S(6);
                t.Font = Theme.MonoFont;
                t.AcceptsReturn = true;
            }
            t.TextChanged += (s, e) => Touch();
            AddRow(label, t);
            return t;
        }

        public Label AddNote(string text, bool warning = false)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(Theme.S(400), 0),
                ForeColor = warning ? Color.FromArgb(0xB0, 0x50, 0x00) : Theme.Muted
            };
            AddRow(null, l);
            return l;
        }

        public CheckBox EnablePreview(bool initial = false)
        {
            PreviewBox = new CheckBox { Text = "Предпросмотр", Checked = initial, AutoSize = true, Margin = new Padding(0, Theme.S(8), Theme.S(16), 0) };
            PreviewBox.CheckedChanged += (s, e) => FireChanged();
            _buttons.Controls.Add(PreviewBox);
            return PreviewBox;
        }

        public bool PreviewOn { get { return PreviewBox != null && PreviewBox.Checked; } }

        public void ShowError(string msg)
        {
            _error.Text = msg ?? "";
            _error.Visible = !string.IsNullOrEmpty(msg);
        }

        private void TryAccept()
        {
            foreach (Control c in AllControls(_grid))
            {
                var nb = c as NumBox;
                if (nb != null && nb.Enabled)
                {
                    string err = nb.Validate();
                    if (err != null) { ShowError(err); nb.Focus(); return; }
                }
            }
            if (Validator != null)
            {
                string err = Validator();
                if (err != null) { ShowError(err); return; }
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private static System.Collections.Generic.IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control cc in AllControls(c)) yield return cc;
            }
        }
    }
}
