using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SigmaLabCam.Core;

namespace SigmaLabCam.UI
{
    public sealed partial class MainForm
    {
        private readonly List<ToolStripItem> _needImage = new List<ToolStripItem>();
        private ToolStripMenuItem _miUndo, _miRecent;

        private ToolStripMenuItem Item(string text, Action a, Keys keys = Keys.None, string icon = null, bool needsImage = true)
        {
            var mi = new ToolStripMenuItem(text);
            if (icon != null) mi.Image = Icons.Get(icon);
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            mi.Click += (s, e) => a();
            if (needsImage) _needImage.Add(mi);
            return mi;
        }

        private static ToolStripMenuItem Sub(string text, params ToolStripItem[] items)
        {
            var mi = new ToolStripMenuItem(text);
            mi.DropDownItems.AddRange(items);
            return mi;
        }

        private static ToolStripSeparator Sep() { return new ToolStripSeparator(); }

        private MenuStrip BuildMenu()
        {
            var ms = new MenuStrip { Renderer = Theme.Renderer, Font = Theme.UiFont, BackColor = Color.White, Padding = new Padding(Theme.S(4), Theme.S(2), 0, Theme.S(2)) };

            _miRecent = new ToolStripMenuItem("Последние файлы");
            _miRecent.DropDownOpening += (s, e) => FillRecent();
            _miRecent.DropDownItems.Add("(пусто)");

            var file = Sub("Файл",
                Item("Открыть…", OpenDialog, Keys.Control | Keys.O, "open", false),
                _miRecent,
                Item("Захват с камеры (AverTV)…", CaptureWindow, Keys.Control | Keys.K, "camera", false),
                Item("Создать тестовую таблицу…", ChartGeneratorDialog, Keys.Control | Keys.N, "chart", false),
                Sep(),
                Item("Сохранить", Save, Keys.Control | Keys.S, "save"),
                Item("Сохранить как…", SaveAs, Keys.Control | Keys.Shift | Keys.S),
                Item("Сохранить все изображения в папку…", SaveAllToFolder, Keys.None, null, false),
                Sep(),
                Item("Закрыть", CloseActive, Keys.Control | Keys.W),
                Item("Закрыть все", CloseAll, Keys.None, null, false),
                Sep(),
                Item("Печать…", PrintActive, Keys.Control | Keys.P),
                Sep(),
                new ToolStripMenuItem("Выход", null, (s, e) => Close()) { ShortcutKeyDisplayString = "Alt+F4" });

            _miUndo = Item("Отменить", Undo, Keys.Control | Keys.Z, "undo");
            var edit = Sub("Правка",
                _miUndo,
                Sep(),
                Item("Дублировать", Duplicate, Keys.Control | Keys.Shift | Keys.D),
                Item("Переименовать…", Rename),
                Sep(),
                Item("Выделить всё", SelectAll, Keys.Control | Keys.A),
                Item("Снять выделение", SelectNone, Keys.Control | Keys.Shift | Keys.A),
                Item("Задать выделение точно…", SpecifyRoi, Keys.Control | Keys.Shift | Keys.E),
                Item("Удалить все точки", () => { if (_ws.Active != null) { _ws.Active.Points.Clear(); SyncPoints(false); } }),
                Sep(),
                Item("Настройки…", OptionsDialog, Keys.None, null, false));

            var image = Sub("Изображение",
                Sub("Тип",
                    Item("8 бит (полутоновое)", () => ConvertType(PixelType.Gray8)),
                    Item("RGB", () => ConvertType(PixelType.Rgb24))),
                Sep(),
                Item("Обрезать по выделению", Crop, Keys.Control | Keys.Shift | Keys.X),
                Item("Инвертировать", () => ApplyToActive("Инвертирование", ImageOps.Invert), Keys.Control | Keys.Shift | Keys.I),
                Sub("Отразить / повернуть",
                    Item("Отразить по горизонтали", () => ApplyToActive("Отражение", ImageOps.FlipHorizontal)),
                    Item("Отразить по вертикали", () => ApplyToActive("Отражение", ImageOps.FlipVertical)),
                    Item("Повернуть на 90° вправо", () => ApplyToActive("Поворот", d => ImageOps.Rotate90(d, true))),
                    Item("Повернуть на 90° влево", () => ApplyToActive("Поворот", d => ImageOps.Rotate90(d, false)))),
                Sep(),
                Sub("Масштаб просмотра",
                    Item("Увеличить\t+", () => _viewer.ZoomIn(), Keys.None, "zoomin"),
                    Item("Уменьшить\t−", () => _viewer.ZoomOut(), Keys.None, "zoomout"),
                    Item("Исходный размер (100%)", () => _viewer.ActualSize()),
                    Item("Вписать в окно", () => _viewer.FitToWindow(), Keys.Control | Keys.D0, "fit")),
                Item("Лупа", ToggleLoupe, Keys.Control | Keys.L, "loupe"),
                Item("Сведения об изображении", ShowImageInfo, Keys.Control | Keys.I));

            var geometry = Sub("Геометрия",
                Item("Внести дисторсию объектива (k1, k2)…", LensDistortDialog, Keys.None, "distortion"),
                Item("Исправить дисторсию по коэффициентам (k1, k2)…", LensCorrectDialog, Keys.None, "corrected"),
                Item("Исправить дисторсию по профилю камеры…", CameraCorrectDialog),
                Sep(),
                Item("Сферические искажения (1.12)…", () => SphereDialog(false), Keys.None, "sphere"),
                Item("Коррекция сферических искажений (1.13)…", () => SphereDialog(true)),
                Item("Адаптивная линейная интерполяция (рис. 1.9)", FillGapsCommand),
                Sep(),
                Item("Модель сферических искажений — исследование (Mathcad «Дисторсия»)…", SphereWindow, Keys.None, null, false));

            var analyze = Sub("Измерения",
                Item("Distortion — измерение дисторсии (Imatest)…", DistortionWindow, Keys.F6, "distortion", false),
                Item("Калибровка камеры и выпрямление (ASRix)…", () => CalibrationWindow(), Keys.F7, "calib", false),
                Sep(),
                Item("Коэффициенты искажений K1–K4, табл. 1.1 (Mathcad)…", () => CoefficientsWindow(), Keys.F9, "coef", false),
                Item("Построить график по таблице…", PlotTableWindow, Keys.None, "plot", false));

            var charts = Sub("Таблицы",
                Item("Генератор тестовых таблиц…", ChartGeneratorDialog, Keys.None, "chart", false),
                Item("Имитация съёмки таблицы камерой…", ShootDialog, Keys.None, "camera"));

            var help = Sub("Справка",
                Item("Руководство пользователя", ShowGuide, Keys.F1, "help", false),
                Item("Соответствие программ методички (AverTV, Imatest, ASRix, Mathcad)", () => ShowGuide("map"), Keys.None, null, false),
                Sep(),
                Item("Лицензионное соглашение", () => LicenseForm.ShowLicense(this), Keys.None, null, false),
                Item("О программе", ShowAbout, Keys.None, null, false));

            ms.Items.AddRange(new ToolStripItem[] { file, edit, image, geometry, analyze, charts, help });
            return ms;
        }

        private ToolStrip BuildToolbar()
        {
            var ts = new ToolStrip
            {
                Renderer = Theme.Renderer,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(Theme.S(16), Theme.S(16)),
                Padding = new Padding(Theme.S(6), Theme.S(3), 0, Theme.S(3)),
                Font = Theme.UiFont
            };
            _tbSelect = new ToolStripButton(null, Icons.Get("rect")) { ToolTipText = "Прямоугольное выделение", Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbPan = new ToolStripButton(null, Icons.Get("hand")) { ToolTipText = "Перемещение (рука). Также: правая кнопка мыши или пробел", DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbPoint = new ToolStripButton(null, Icons.Get("point")) { ToolTipText = "Точка: щелчок ставит точку, перетаскивание — перемещает (Point List справа)", DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbLoupe = new ToolStripButton(null, Icons.Get("loupe")) { ToolTipText = "Лупа (Ctrl+L)", DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbSelect.Click += (s, e) => SetTool(ImageViewer.ToolMode.Select);
            _tbPan.Click += (s, e) => SetTool(ImageViewer.ToolMode.Pan);
            _tbPoint.Click += (s, e) => SetTool(ImageViewer.ToolMode.Point);
            _tbLoupe.Click += (s, e) => ToggleLoupe();

            ts.Items.AddRange(new ToolStripItem[]
            {
                btn(null, "open", "Открыть (Ctrl+O)", OpenDialog, false),
                btn(null, "save", "Сохранить (Ctrl+S)", Save, true),
                btn(null, "undo", "Отменить (Ctrl+Z)", Undo, true),
                new ToolStripSeparator(),
                _tbSelect, _tbPan, _tbPoint,
                btn(null, "zoomin", "Увеличить (+)", () => _viewer.ZoomIn(), true),
                btn(null, "zoomout", "Уменьшить (−)", () => _viewer.ZoomOut(), true),
                btn(null, "fit", "Вписать в окно (Ctrl+0)", () => _viewer.FitToWindow(), true),
                _tbLoupe,
                new ToolStripSeparator(),
                btn("Камера", "camera", "Захват изображения с камеры (Ctrl+K)", CaptureWindow, false),
                btn("Distortion", "distortion", "Измерение дисторсии по шахматному полю или сетке (F6)", DistortionWindow, false),
                btn("Калибровка", "calib", "Калибровка камеры и выпрямление изображения по сетке точек (F7)", () => CalibrationWindow(), false),
                btn("Коэффициенты", "coef", "Коэффициенты искажений K1–K4 и формулы (1.1)–(1.3) (F9)", () => CoefficientsWindow(), false),
                btn("Сфера", "sphere", "Модель сферических искажений (1.12), (1.13)", SphereWindow, false),
                btn("График", "plot", "Построить график по таблице", PlotTableWindow, false),
            });
            _needImage.Add(_tbLoupe);
            return ts;
        }

        private ToolStripButton btn(string text, string icon, string tip, Action a, bool needs)
        {
            var b = new ToolStripButton(text, Icons.Get(icon)) { ToolTipText = tip, DisplayStyle = text == null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText };
            b.Click += (s, e) => a();
            if (needs) _needImage.Add(b);
            return b;
        }

        private void SetTool(ImageViewer.ToolMode m)
        {
            _viewer.Tool = m;
            _tbSelect.Checked = m == ImageViewer.ToolMode.Select;
            _tbPan.Checked = m == ImageViewer.ToolMode.Pan;
            _tbPoint.Checked = m == ImageViewer.ToolMode.Point;
            _viewer.Cursor = m == ImageViewer.ToolMode.Pan ? Cursors.Hand : Cursors.Cross;
        }

        private void ToggleLoupe()
        {
            _viewer.ShowLoupe = !_viewer.ShowLoupe;
            _tbLoupe.Checked = _viewer.ShowLoupe;
            _viewer.Invalidate();
        }

        private void UpdateMenuState()
        {
            bool has = _ws.Active != null;
            foreach (var it in _needImage) it.Enabled = has;
            _miUndo.Enabled = has && _ws.Active.CanUndo;
            _miUndo.Text = has && _ws.Active.CanUndo && !string.IsNullOrEmpty(_ws.Active.UndoName) ? "Отменить: " + _ws.Active.UndoName : "Отменить";
        }

        private void FillRecent()
        {
            _miRecent.DropDownItems.Clear();
            if (Settings.Recent.Count == 0)
            {
                _miRecent.DropDownItems.Add(new ToolStripMenuItem("(пусто)") { Enabled = false });
                return;
            }
            foreach (string p in Settings.Recent)
            {
                string path = p;
                _miRecent.DropDownItems.Add(new ToolStripMenuItem(path, null, (s, e) => OpenFiles(new[] { path })));
            }
        }
    }
}
