using System;
using System.Collections.Generic;
using System.Drawing;
using SigmaLabCam.Core;
using SigmaLabCam.IO;

namespace SigmaLabCam.UI
{
    /// <summary>Открытое изображение: данные, выделение, точки, одноуровневая отмена.</summary>
    public sealed class ImageDoc
    {
        private ImageData _data;
        private ImageData _undo;
        private Bitmap _display, _thumb;

        public Rectangle Roi;
        /// <summary>Точки, поставленные инструментом «Точка» (координаты в пикселях изображения).</summary>
        public readonly List<PointD> Points = new List<PointD>();
        public bool Modified;
        public double ViewZoom = -1, ViewOx, ViewOy;

        public ImageDoc(ImageData d) { _data = d; }

        public ImageData Data { get { return _data; } }
        public string Name { get { return _data.Name; } set { _data.Name = value; } }
        public bool CanUndo { get { return _undo != null; } }
        public string UndoName;

        public event EventHandler Changed;

        public void Replace(ImageData d, string action, bool undoable = true)
        {
            ImageData old = _data;
            if (undoable) { _undo = old; UndoName = action; }
            _data = d;
            _data.Name = old.Name;
            _data.FilePath = old.FilePath;
            Modified = true;
            Invalidate();
            if (d.Width != old.Width || d.Height != old.Height) { Roi = Rectangle.Empty; Points.Clear(); }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (_undo == null) return;
            var t = _data;
            _data = _undo;
            _undo = t;
            _data.Name = t.Name;
            Invalidate();
            if (Roi.Right > _data.Width || Roi.Bottom > _data.Height) Roi = Rectangle.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private Bitmap _retired;

        /// <summary>
        /// Сбрасывает растр для показа. Прежний растр освобождается только при следующем сбросе:
        /// просмотрщик ещё может его рисовать, пока не получит новый.
        /// </summary>
        public void Invalidate()
        {
            if (_retired != null) _retired.Dispose();
            _retired = _display;
            _display = null;
            if (_thumb != null) { _thumb.Dispose(); _thumb = null; }
        }

        public Bitmap Display
        {
            get { return _display ?? (_display = ImageIO.ToBitmap(_data)); }
        }

        public Bitmap Thumbnail(int size)
        {
            if (_thumb != null && _thumb.Width == size) return _thumb;
            if (_thumb != null) _thumb.Dispose();
            _thumb = new Bitmap(size, size);
            using (var g = Graphics.FromImage(_thumb))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                float k = Math.Min((float)size / _data.Width, (float)size / _data.Height);
                float w = _data.Width * k, h = _data.Height * k;
                g.DrawImage(Display, (size - w) / 2, (size - h) / 2, w, h);
            }
            return _thumb;
        }

        public void Dispose()
        {
            Invalidate();
            if (_retired != null) { _retired.Dispose(); _retired = null; }
            _undo = null;
        }
    }

    /// <summary>Набор открытых изображений.</summary>
    public sealed class Workspace
    {
        public readonly List<ImageDoc> Docs = new List<ImageDoc>();
        private ImageDoc _active;

        public event EventHandler DocsChanged;
        public event EventHandler ActiveChanged;
        public event EventHandler ActiveDataChanged;

        public ImageDoc Active
        {
            get { return _active; }
            set
            {
                if (_active == value) return;
                _active = value;
                ActiveChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ImageDoc Add(ImageData d, bool activate = true)
        {
            d.Name = UniqueName(d.Name);
            var doc = new ImageDoc(d);
            doc.Changed += (s, e) =>
            {
                if (s == _active) ActiveDataChanged?.Invoke(this, EventArgs.Empty);
                DocsChanged?.Invoke(this, EventArgs.Empty);
            };
            Docs.Add(doc);
            DocsChanged?.Invoke(this, EventArgs.Empty);
            if (activate) Active = doc;
            return doc;
        }

        public void Close(ImageDoc doc)
        {
            int i = Docs.IndexOf(doc);
            if (i < 0) return;
            Docs.RemoveAt(i);
            doc.Dispose();
            DocsChanged?.Invoke(this, EventArgs.Empty);
            if (_active == doc) Active = Docs.Count == 0 ? null : Docs[Math.Min(i, Docs.Count - 1)];
        }

        public string UniqueName(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "Без имени";
            if (!Docs.Exists(d => d.Name == name)) return name;
            for (int k = 1; ; k++)
            {
                string n = name + "-" + k;
                if (!Docs.Exists(d => d.Name == n)) return n;
            }
        }

        public ImageDoc Find(string name) { return Docs.Find(d => d.Name == name); }

        public void NotifyDocs() { DocsChanged?.Invoke(this, EventArgs.Empty); }
    }
}
