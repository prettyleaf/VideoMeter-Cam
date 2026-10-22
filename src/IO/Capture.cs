using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using SigmaLabCam.Core;

namespace SigmaLabCam.IO
{
    /// <summary>Устройство видеозахвата (веб-камера, плата видеоввода, TV-тюнер).</summary>
    public sealed class CaptureDevice
    {
        public string Name;
        public string Path;          // DevicePath (если есть) — для различения одинаковых устройств
        public int Index;
        public override string ToString() { return Name; }
    }

    public sealed class CaptureFormat
    {
        public int Index;            // номер в IAMStreamConfig
        public int Width, Height;
        public string Subtype;
        public double Fps;
        public override string ToString()
        {
            return Width + "×" + Height + "  " + Subtype + (Fps > 0 ? "  " + Fps.ToString("0.##") + " к/с" : "");
        }
    }

    public sealed class CaptureInput
    {
        public int Pin;
        public string Name;
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Захват кадров с устройства DirectShow: источник → (кроссбар) → SampleGrabber (RGB24) → Null Renderer.
    /// Кадры приходят в фоновом потоке и копируются в буфер; <see cref="TakeFrame"/> отдаёт последний кадр.
    /// </summary>
    public sealed class CaptureSession : IDisposable
    {
        private IGraphBuilder _graph;
        private ICaptureGraphBuilder2 _builder;
        private IBaseFilter _source, _grabberFilter, _renderer;
        private ISampleGrabber _grabber;
        private IMediaControl _control;
        private IAMCrossbar _crossbar;
        private Callback _cb;
        private readonly object _lock = new object();
        private byte[] _frame;
        private long _frameNo;
        private int _width, _height, _stride, _bpp;
        private bool _bottomUp;
        private Guid _subtype;

        public int Width { get { return _width; } }
        public int Height { get { return _height; } }
        public long FrameNumber { get { return Interlocked.Read(ref _frameNo); } }
        public string SubtypeName { get { return SubtypeToString(_subtype); } }
        public bool HasCrossbar { get { return _crossbar != null; } }
        public CaptureDevice Device { get; private set; }

        // ------------------------------------------------------------------ перечисление

        public static List<CaptureDevice> Devices()
        {
            var list = new List<CaptureDevice>();
            object o = null;
            IEnumMoniker em = null;
            try
            {
                em = EnumDevices(out o);
                if (em == null) return list;
                var m = new IMoniker[1];
                int i = 0;
                while (em.Next(1, m, IntPtr.Zero) == 0)
                {
                    try
                    {
                        string name = ReadProp(m[0], "FriendlyName") ?? ("Устройство " + (i + 1));
                        list.Add(new CaptureDevice { Name = name, Path = ReadProp(m[0], "DevicePath"), Index = i });
                    }
                    finally { Marshal.ReleaseComObject(m[0]); m[0] = null; }
                    i++;
                }
            }
            catch (COMException) { }
            finally
            {
                if (em != null) Marshal.ReleaseComObject(em);
                if (o != null) Marshal.ReleaseComObject(o);
            }
            return list;
        }

        /// <summary>Перечислитель устройств категории «видеовход»; null — устройств нет.</summary>
        private static IEnumMoniker EnumDevices(out object devEnum)
        {
            devEnum = Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.SystemDeviceEnum));
            var de = (ICreateDevEnum)devEnum;
            Guid cat = Clsid.VideoInputDeviceCategory;
            IntPtr p;
            // S_FALSE (1) — в категории нет устройств
            if (de.CreateClassEnumerator(ref cat, out p, 0) != 0 || p == IntPtr.Zero) return null;
            try { return (IEnumMoniker)Marshal.GetObjectForIUnknown(p); }
            finally { Marshal.Release(p); }
        }

        private static string ReadProp(IMoniker m, string prop)
        {
            object bag = null;
            try
            {
                Guid iid = typeof(IPropertyBag).GUID;
                m.BindToStorage(null, null, ref iid, out bag);
                var pb = bag as IPropertyBag;
                if (pb == null) return null;
                object val;
                if (pb.Read(prop, out val, IntPtr.Zero) != 0) return null;
                return val as string;
            }
            catch (COMException) { return null; }
            finally { if (bag != null) Marshal.ReleaseComObject(bag); }
        }

        private static IBaseFilter Bind(CaptureDevice d)
        {
            object o = null;
            IEnumMoniker em = null;
            try
            {
                em = EnumDevices(out o);
                if (em == null) throw new InvalidOperationException("Устройства видеозахвата не найдены.");
                var m = new IMoniker[1];
                int i = 0;
                IBaseFilter byIndex = null;
                while (em.Next(1, m, IntPtr.Zero) == 0)
                {
                    try
                    {
                        string path = ReadProp(m[0], "DevicePath"), name = ReadProp(m[0], "FriendlyName");
                        bool match = !string.IsNullOrEmpty(d.Path) ? path == d.Path : (i == d.Index && name == d.Name);
                        if (match || (byIndex == null && i == d.Index))
                        {
                            Guid iid = typeof(IBaseFilter).GUID;
                            object f;
                            m[0].BindToObject(null, null, ref iid, out f);
                            if (match)
                            {
                                if (byIndex != null) Marshal.ReleaseComObject(byIndex);
                                return (IBaseFilter)f;
                            }
                            byIndex = (IBaseFilter)f;
                        }
                    }
                    finally { Marshal.ReleaseComObject(m[0]); m[0] = null; }
                    i++;
                }
                if (byIndex != null) return byIndex;
                throw new InvalidOperationException("Устройство «" + d.Name + "» не найдено (отключено?).");
            }
            finally
            {
                if (em != null) Marshal.ReleaseComObject(em);
                if (o != null) Marshal.ReleaseComObject(o);
            }
        }

        /// <summary>Форматы, которые сообщает устройство (IAMStreamConfig).</summary>
        public static List<CaptureFormat> Formats(CaptureDevice d)
        {
            var res = new List<CaptureFormat>();
            IBaseFilter src = null;
            object graph = null, builder = null;
            try
            {
                src = Bind(d);
                graph = Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.FilterGraph));
                builder = Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.CaptureGraphBuilder2));
                ((ICaptureGraphBuilder2)builder).SetFiltergraph((IGraphBuilder)graph);
                ((IGraphBuilder)graph).AddFilter(src, "Source");
                IAMStreamConfig sc = FindConfig((ICaptureGraphBuilder2)builder, src);
                if (sc == null) return res;
                try
                {
                    int count, size;
                    if (sc.GetNumberOfCapabilities(out count, out size) != 0) return res;
                    IntPtr caps = Marshal.AllocCoTaskMem(Math.Max(size, 256));
                    try
                    {
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr pmt;
                            if (sc.GetStreamCaps(i, out pmt, caps) != 0 || pmt == IntPtr.Zero) continue;
                            try
                            {
                                var mt = (AMMediaType)Marshal.PtrToStructure(pmt, typeof(AMMediaType));
                                int w, h; long avg;
                                if (ReadVideoInfo(mt, out w, out h, out avg))
                                    res.Add(new CaptureFormat { Index = i, Width = w, Height = Math.Abs(h), Subtype = SubtypeToString(mt.subType), Fps = avg > 0 ? 1e7 / avg : 0 });
                            }
                            finally { DeleteMediaType(pmt); }
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(caps); }
                }
                finally { Marshal.ReleaseComObject(sc); }
            }
            catch (COMException) { }
            finally
            {
                if (graph != null) Marshal.ReleaseComObject(graph);
                if (builder != null) Marshal.ReleaseComObject(builder);
                if (src != null) Marshal.ReleaseComObject(src);
            }
            return res;
        }

        private static IAMStreamConfig FindConfig(ICaptureGraphBuilder2 b, IBaseFilter src)
        {
            foreach (Guid cat in new[] { Clsid.PinCategoryCapture, Clsid.PinCategoryPreview })
                foreach (Guid type in new[] { Clsid.MediaTypeVideo, Clsid.MediaTypeInterleaved })
                {
                    object o = FindInterface(b, cat, type, src, typeof(IAMStreamConfig).GUID);
                    if (o != null) return (IAMStreamConfig)o;
                }
            return null;
        }

        /// <summary>ICaptureGraphBuilder2::FindInterface; type = Guid.Empty передаётся как NULL.</summary>
        private static object FindInterface(ICaptureGraphBuilder2 b, Guid category, Guid type, IBaseFilter f, Guid iid)
        {
            IntPtr pt = IntPtr.Zero;
            try
            {
                if (type != Guid.Empty)
                {
                    pt = Marshal.AllocCoTaskMem(16);
                    Marshal.Copy(type.ToByteArray(), 0, pt, 16);
                }
                object o;
                return b.FindInterface(ref category, pt, f, ref iid, out o) == 0 ? o : null;
            }
            catch (COMException) { return null; }
            catch (InvalidCastException) { return null; }
            finally { if (pt != IntPtr.Zero) Marshal.FreeCoTaskMem(pt); }
        }

        // ------------------------------------------------------------------ запуск

        /// <param name="format">номер формата из <see cref="Formats"/> или −1 — формат по умолчанию</param>
        public static CaptureSession Open(CaptureDevice d, int format)
        {
            var s = new CaptureSession { Device = d };
            try
            {
                s.Build(d, format);
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        private void Build(CaptureDevice d, int format)
        {
            _source = Bind(d);
            _graph = (IGraphBuilder)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.FilterGraph));
            _builder = (ICaptureGraphBuilder2)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.CaptureGraphBuilder2));
            Check(_builder.SetFiltergraph(_graph), "SetFiltergraph");
            Check(_graph.AddFilter(_source, "Source"), "AddFilter(source)");

            if (format >= 0)
            {
                IAMStreamConfig sc = FindConfig(_builder, _source);
                if (sc != null)
                {
                    try
                    {
                        int count, size;
                        if (sc.GetNumberOfCapabilities(out count, out size) == 0 && format < count)
                        {
                            IntPtr caps = Marshal.AllocCoTaskMem(Math.Max(size, 256));
                            try
                            {
                                IntPtr pmt;
                                if (sc.GetStreamCaps(format, out pmt, caps) == 0 && pmt != IntPtr.Zero)
                                {
                                    try { sc.SetFormat(pmt); }
                                    finally { DeleteMediaType(pmt); }
                                }
                            }
                            finally { Marshal.FreeCoTaskMem(caps); }
                        }
                    }
                    finally { Marshal.ReleaseComObject(sc); }
                }
            }

            _grabberFilter = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.SampleGrabber));
            _grabber = (ISampleGrabber)_grabberFilter;
            _renderer = (IBaseFilter)Activator.CreateInstance(Type.GetTypeFromCLSID(Clsid.NullRenderer));
            Check(_graph.AddFilter(_grabberFilter, "Grabber"), "AddFilter(grabber)");
            Check(_graph.AddFilter(_renderer, "Null"), "AddFilter(null)");

            int hr = -1;
            foreach (Guid sub in new[] { Clsid.SubtypeRgb24, Clsid.SubtypeRgb32, Clsid.SubtypeYuy2 })
            {
                var mt = new AMMediaType { majorType = Clsid.MediaTypeVideo, subType = sub, formatType = Clsid.FormatVideoInfo };
                _grabber.SetMediaType(mt);
                foreach (Guid cat in new[] { Clsid.PinCategoryCapture, Clsid.PinCategoryPreview })
                {
                    Guid c = cat, t = Clsid.MediaTypeVideo;
                    hr = _builder.RenderStream(ref c, ref t, _source, _grabberFilter, _renderer);
                    if (hr >= 0) break;
                }
                if (hr >= 0) break;
            }
            Check(hr, "подключение устройства (RenderStream)");

            var cmt = new AMMediaType();
            Check(_grabber.GetConnectedMediaType(cmt), "GetConnectedMediaType");
            try
            {
                int w, h; long avg;
                if (!ReadVideoInfo(cmt, out w, out h, out avg)) throw new InvalidOperationException("Неизвестный формат кадра.");
                _subtype = cmt.subType;
                _width = w;
                _height = Math.Abs(h);
                _bpp = _subtype == Clsid.SubtypeRgb32 ? 32 : (_subtype == Clsid.SubtypeYuy2 ? 16 : 24);
                _bottomUp = _subtype != Clsid.SubtypeYuy2 && h > 0;
                _stride = _subtype == Clsid.SubtypeYuy2 ? _width * 2 : ((_width * _bpp + 31) / 32) * 4;
            }
            finally { FreeMediaType(cmt); }

            _grabber.SetBufferSamples(false);
            _grabber.SetOneShot(false);
            _cb = new Callback(this);
            Check(_grabber.SetCallback(_cb, 1), "SetCallback");

            _crossbar = FindInterface(_builder, Clsid.LookUpstreamOnly, Guid.Empty, _source, typeof(IAMCrossbar).GUID) as IAMCrossbar;

            _control = (IMediaControl)_graph;
            hr = _control.Run();
            if (hr < 0) Check(hr, "запуск (Run)");
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException("DirectShow: ошибка «" + what + "» (код 0x" + hr.ToString("X8") + ").");
        }

        private sealed class Callback : ISampleGrabberCB
        {
            private readonly CaptureSession _s;
            public Callback(CaptureSession s) { _s = s; }
            public int SampleCB(double time, IntPtr sample) { return 0; }

            public int BufferCB(double time, IntPtr buffer, int len)
            {
                var s = _s;
                int need = s._stride * s._height;
                if (buffer == IntPtr.Zero || len < need) return 0;
                lock (s._lock)
                {
                    if (s._frame == null || s._frame.Length != need) s._frame = new byte[need];
                    Marshal.Copy(buffer, s._frame, 0, need);
                }
                Interlocked.Increment(ref s._frameNo);
                return 0;
            }
        }

        /// <summary>Последний принятый кадр (RGB 8 бит); null — кадров ещё не было.</summary>
        public ImageData TakeFrame()
        {
            byte[] f;
            lock (_lock)
            {
                if (_frame == null) return null;
                f = (byte[])_frame.Clone();
            }
            return Convert(f);
        }

        /// <summary>Копирует последний кадр в 24-битный буфер «сверху вниз» (для быстрого показа).</summary>
        public bool CopyBgr24(byte[] dst, int dstStride)
        {
            lock (_lock)
            {
                if (_frame == null) return false;
                for (int y = 0; y < _height; y++)
                {
                    int sy = _bottomUp ? _height - 1 - y : y;
                    int so = sy * _stride, d = y * dstStride;
                    if (_bpp == 24) Buffer.BlockCopy(_frame, so, dst, d, _width * 3);
                    else if (_bpp == 32)
                        for (int x = 0; x < _width; x++) { dst[d + x * 3] = _frame[so + x * 4]; dst[d + x * 3 + 1] = _frame[so + x * 4 + 1]; dst[d + x * 3 + 2] = _frame[so + x * 4 + 2]; }
                    else
                        for (int x = 0; x < _width; x += 2)
                        {
                            int y0 = _frame[so + x * 2], u = _frame[so + x * 2 + 1] - 128, y1 = _frame[so + x * 2 + 2], v = _frame[so + x * 2 + 3] - 128;
                            Yuv(y0, u, v, dst, d + x * 3);
                            if (x + 1 < _width) Yuv(y1, u, v, dst, d + x * 3 + 3);
                        }
                }
                return true;
            }
        }

        private static void Yuv(int y, int u, int v, byte[] dst, int o)
        {
            double c = (y - 16) * 1.164;
            dst[o + 2] = Clip(c + 1.596 * v);
            dst[o + 1] = Clip(c - 0.392 * u - 0.813 * v);
            dst[o] = Clip(c + 2.017 * u);
        }

        private static byte Clip(double v) { return (byte)(v <= 0 ? 0 : (v >= 255 ? 255 : v + 0.5)); }

        private ImageData Convert(byte[] f)
        {
            var img = new ImageData(_width, _height, PixelType.Rgb24);
            var line = new byte[_width * 3];
            for (int y = 0; y < _height; y++)
            {
                int sy = _bottomUp ? _height - 1 - y : y;
                int so = sy * _stride;
                if (_bpp == 24) Buffer.BlockCopy(f, so, line, 0, _width * 3);
                else if (_bpp == 32) for (int x = 0; x < _width; x++) { line[x * 3] = f[so + x * 4]; line[x * 3 + 1] = f[so + x * 4 + 1]; line[x * 3 + 2] = f[so + x * 4 + 2]; }
                else
                    for (int x = 0; x < _width; x += 2)
                    {
                        int y0 = f[so + x * 2], u = f[so + x * 2 + 1] - 128, y1 = f[so + x * 2 + 2], v = f[so + x * 2 + 3] - 128;
                        Yuv(y0, u, v, line, x * 3);
                        if (x + 1 < _width) Yuv(y1, u, v, line, x * 3 + 3);
                    }
                for (int x = 0; x < _width; x++)
                {
                    int i = y * _width + x;
                    img.Planes[2][i] = line[x * 3];
                    img.Planes[1][i] = line[x * 3 + 1];
                    img.Planes[0][i] = line[x * 3 + 2];
                }
            }
            return img;
        }

        // ------------------------------------------------------------------ кроссбар и свойства

        private static readonly Dictionary<int, string> PhysName = new Dictionary<int, string>
        {
            { 1, "Тюнер (Tuner)" }, { 2, "Композитный (Composite)" }, { 3, "S-Video" }, { 4, "RGB" }, { 5, "YRYBY" },
            { 6, "Serial Digital" }, { 7, "Parallel Digital" }, { 8, "SCSI" }, { 9, "AUX" }, { 10, "1394" }, { 11, "USB" },
            { 12, "Video Decoder" }, { 13, "Video Encoder" }, { 14, "SCART" }
        };

        /// <summary>Видеовходы платы захвата (кроссбар): композитный, S-Video, тюнер…</summary>
        public List<CaptureInput> Inputs(out int current)
        {
            current = -1;
            var res = new List<CaptureInput>();
            if (_crossbar == null) return res;
            int outs, ins;
            if (_crossbar.get_PinCounts(out outs, out ins) != 0) return res;
            int vout = VideoOutputPin(outs);
            if (vout < 0) return res;
            int routed;
            if (_crossbar.get_IsRoutedTo(vout, out routed) != 0) routed = -1;
            for (int i = 0; i < ins; i++)
            {
                int rel, type;
                if (_crossbar.get_CrossbarPinInfo(true, i, out rel, out type) != 0) continue;
                if (type >= 0x1000) continue; // звуковые входы
                if (_crossbar.CanRoute(vout, i) != 0) continue;
                string n;
                if (!PhysName.TryGetValue(type, out n)) n = "Вход " + i;
                res.Add(new CaptureInput { Pin = i, Name = n });
                if (i == routed) current = res.Count - 1;
            }
            return res;
        }

        private int VideoOutputPin(int outs)
        {
            for (int i = 0; i < outs; i++)
            {
                int rel, type;
                if (_crossbar.get_CrossbarPinInfo(false, i, out rel, out type) == 0 && type == 12) return i;
            }
            return outs > 0 ? 0 : -1;
        }

        public void SelectInput(CaptureInput input)
        {
            if (_crossbar == null || input == null) return;
            int outs, ins;
            if (_crossbar.get_PinCounts(out outs, out ins) != 0) return;
            int vout = VideoOutputPin(outs);
            if (vout >= 0) Check(_crossbar.Route(vout, input.Pin), "переключение входа");
        }

        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
        private static extern int OleCreatePropertyFrame(IntPtr hwndOwner, int x, int y, string lpszCaption, int cObjects,
            [MarshalAs(UnmanagedType.Interface)] ref object ppUnk, int cPages, IntPtr pPageClsID, int lcid, int dwReserved, IntPtr pvReserved);

        /// <summary>Окно свойств драйвера: устройства (яркость, контраст, стандарт ТВ…) или кроссбара.</summary>
        public bool ShowProperties(IntPtr owner, bool crossbar)
        {
            object target = crossbar ? (object)_crossbar : _source;
            var pages = target as ISpecifyPropertyPages;
            if (pages == null) return false;
            DsCauuid ca;
            if (pages.GetPages(out ca) != 0 || ca.cElems <= 0) return false;
            try
            {
                object o = target;
                OleCreatePropertyFrame(owner, 0, 0, crossbar ? "Вход" : (Device != null ? Device.Name : "Устройство"), 1, ref o, ca.cElems, ca.pElems, 0, 0, IntPtr.Zero);
            }
            finally { Marshal.FreeCoTaskMem(ca.pElems); }
            return true;
        }

        public void Dispose()
        {
            try { if (_control != null) _control.Stop(); } catch { }
            try { if (_grabber != null) _grabber.SetCallback(null, 1); } catch { }
            Release(ref _crossbar);
            _control = null;
            Release(ref _grabber);
            _grabberFilter = null;
            Release(ref _renderer);
            Release(ref _source);
            Release(ref _builder);
            Release(ref _graph);
        }

        private static void Release<T>(ref T o) where T : class
        {
            if (o != null)
            {
                try { Marshal.ReleaseComObject(o); } catch { }
                o = null;
            }
        }

        // ------------------------------------------------------------------ вспомогательное

        private static bool ReadVideoInfo(AMMediaType mt, out int w, out int h, out long avg)
        {
            w = h = 0; avg = 0;
            if (mt.formatPtr == IntPtr.Zero) return false;
            int bmi;
            if (mt.formatType == Clsid.FormatVideoInfo && mt.formatSize >= 88) bmi = 48;
            else if (mt.formatType == Clsid.FormatVideoInfo2 && mt.formatSize >= 112) bmi = 72;
            else return false;
            avg = Marshal.ReadInt64(mt.formatPtr, 40);
            w = Marshal.ReadInt32(mt.formatPtr, bmi + 4);
            h = Marshal.ReadInt32(mt.formatPtr, bmi + 8);
            return w > 0 && h != 0;
        }

        private static string SubtypeToString(Guid g)
        {
            if (g == Clsid.SubtypeRgb24) return "RGB24";
            if (g == Clsid.SubtypeRgb32) return "RGB32";
            byte[] b = g.ToByteArray();
            // FOURCC-подтипы: XXXXXXXX-0000-0010-8000-00AA00389B71
            if (b[4] == 0 && b[5] == 0 && b[6] == 0x10 && b[7] == 0 && b[8] == 0x80)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append(b[i] >= 32 && b[i] < 127 ? (char)b[i] : '?');
                return sb.ToString().Trim();
            }
            return g.ToString("B").Substring(1, 8);
        }

        private static void FreeMediaType(AMMediaType mt)
        {
            if (mt.formatSize != 0 && mt.formatPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.formatPtr);
            mt.formatSize = 0;
            mt.formatPtr = IntPtr.Zero;
            if (mt.unkPtr != IntPtr.Zero) { Marshal.Release(mt.unkPtr); mt.unkPtr = IntPtr.Zero; }
        }

        private static void DeleteMediaType(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return;
            var mt = (AMMediaType)Marshal.PtrToStructure(pmt, typeof(AMMediaType));
            FreeMediaType(mt);
            Marshal.FreeCoTaskMem(pmt);
        }

        // ------------------------------------------------------------------ COM-интерфейсы DirectShow

        private static class Clsid
        {
            public static readonly Guid SystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
            public static readonly Guid VideoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
            public static readonly Guid FilterGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770");
            public static readonly Guid CaptureGraphBuilder2 = new Guid("BF87B6E1-8C27-11d0-B3F0-00AA003761C5");
            public static readonly Guid SampleGrabber = new Guid("C1F400A0-3F08-11d3-9F0B-006008039E37");
            public static readonly Guid NullRenderer = new Guid("C1F400A4-3F08-11d3-9F0B-006008039E37");
            public static readonly Guid PinCategoryCapture = new Guid("fb6c4281-0353-11d1-905f-0000c0cc16ba");
            public static readonly Guid PinCategoryPreview = new Guid("fb6c4282-0353-11d1-905f-0000c0cc16ba");
            public static readonly Guid LookUpstreamOnly = new Guid("AC798BE0-98E3-11d1-B3F1-00AA003761C5");
            public static readonly Guid MediaTypeVideo = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid MediaTypeInterleaved = new Guid("73766169-0000-0010-8000-00AA00389B71");
            public static readonly Guid SubtypeRgb24 = new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770");
            public static readonly Guid SubtypeRgb32 = new Guid("e436eb7e-524f-11ce-9f53-0020af0ba770");
            public static readonly Guid SubtypeYuy2 = new Guid("32595559-0000-0010-8000-00AA00389B71");
            public static readonly Guid FormatVideoInfo = new Guid("05589f80-c356-11ce-bf01-00aa0055595a");
            public static readonly Guid FormatVideoInfo2 = new Guid("F72A76A0-EB0A-11d0-ACE4-0000C0CC16BA");
        }

        [StructLayout(LayoutKind.Sequential)]
        private class AMMediaType
        {
            public Guid majorType;
            public Guid subType;
            [MarshalAs(UnmanagedType.Bool)] public bool fixedSizeSamples = true;
            [MarshalAs(UnmanagedType.Bool)] public bool temporalCompression;
            public int sampleSize = 1;
            public Guid formatType;
            public IntPtr unkPtr;
            public int formatSize;
            public IntPtr formatPtr;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DsCauuid
        {
            public int cElems;
            public IntPtr pElems;
        }

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig] int CreateClassEnumerator([In] ref Guid type, [Out] out IntPtr enumMoniker, [In] int flags);
        }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig] int Read([In, MarshalAs(UnmanagedType.LPWStr)] string propertyName, [Out, MarshalAs(UnmanagedType.Struct)] out object pVar, [In] IntPtr pErrorLog);
            [PreserveSig] int Write([In, MarshalAs(UnmanagedType.LPWStr)] string propertyName, [In, MarshalAs(UnmanagedType.Struct)] ref object pVar);
        }

        [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IBaseFilter
        {
            [PreserveSig] int GetClassID(out Guid classID);
            [PreserveSig] int Stop();
            [PreserveSig] int Pause();
            [PreserveSig] int Run(long start);
            [PreserveSig] int GetState(int milliSecsTimeout, out int filterState);
            [PreserveSig] int SetSyncSource([In] IntPtr clock);
            [PreserveSig] int GetSyncSource([Out] out IntPtr clock);
            [PreserveSig] int EnumPins([Out] out IntPtr enumPins);
            [PreserveSig] int FindPin([In, MarshalAs(UnmanagedType.LPWStr)] string id, [Out] out IntPtr pin);
            [PreserveSig] int QueryFilterInfo([Out] IntPtr filterInfo);
            [PreserveSig] int JoinFilterGraph([In] IntPtr graph, [In, MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int QueryVendorInfo([Out, MarshalAs(UnmanagedType.LPWStr)] out string vendorInfo);
        }

        [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphBuilder
        {
            [PreserveSig] int AddFilter([In] IBaseFilter filter, [In, MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int RemoveFilter([In] IBaseFilter filter);
            [PreserveSig] int EnumFilters([Out] out IntPtr enumFilters);
            [PreserveSig] int FindFilterByName([In, MarshalAs(UnmanagedType.LPWStr)] string name, [Out] out IBaseFilter filter);
            [PreserveSig] int ConnectDirect([In] IntPtr pinOut, [In] IntPtr pinIn, [In] IntPtr mediaType);
            [PreserveSig] int Reconnect([In] IntPtr pin);
            [PreserveSig] int Disconnect([In] IntPtr pin);
            [PreserveSig] int SetDefaultSyncSource();
            [PreserveSig] int Connect([In] IntPtr pinOut, [In] IntPtr pinIn);
            [PreserveSig] int Render([In] IntPtr pinOut);
            [PreserveSig] int RenderFile([In, MarshalAs(UnmanagedType.LPWStr)] string file, [In, MarshalAs(UnmanagedType.LPWStr)] string playList);
            [PreserveSig] int AddSourceFilter([In, MarshalAs(UnmanagedType.LPWStr)] string fileName, [In, MarshalAs(UnmanagedType.LPWStr)] string filterName, [Out] out IBaseFilter filter);
            [PreserveSig] int SetLogFile(IntPtr hFile);
            [PreserveSig] int Abort();
            [PreserveSig] int ShouldOperationContinue();
        }

        [ComImport, Guid("93E5A4E0-2D50-11d2-ABFA-00A0C9C6E38D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICaptureGraphBuilder2
        {
            [PreserveSig] int SetFiltergraph([In] IGraphBuilder graph);
            [PreserveSig] int GetFiltergraph([Out] out IGraphBuilder graph);
            [PreserveSig] int SetOutputFileName([In] ref Guid type, [In, MarshalAs(UnmanagedType.LPWStr)] string fileName, [Out] out IBaseFilter baseFilter, [Out] out IntPtr fileSinkFilter);
            [PreserveSig] int FindInterface([In] ref Guid category, [In] IntPtr type, [In] IBaseFilter baseFilter, [In] ref Guid interfaceID, [Out, MarshalAs(UnmanagedType.IUnknown)] out object retInterface);
            [PreserveSig] int RenderStream([In] ref Guid category, [In] ref Guid mediaType, [In, MarshalAs(UnmanagedType.IUnknown)] object source, [In] IBaseFilter compressor, [In] IBaseFilter renderer);
            [PreserveSig] int ControlStream([In] ref Guid category, [In] ref Guid mediaType, [In] IBaseFilter filter, [In] IntPtr start, [In] IntPtr stop, [In] short startCookie, [In] short stopCookie);
            [PreserveSig] int AllocCapFile([In, MarshalAs(UnmanagedType.LPWStr)] string fileName, [In] long size);
            [PreserveSig] int CopyCaptureFile([In, MarshalAs(UnmanagedType.LPWStr)] string oldFileName, [In, MarshalAs(UnmanagedType.LPWStr)] string newFileName, [In, MarshalAs(UnmanagedType.Bool)] bool allowEscAbort, [In] IntPtr callback);
            [PreserveSig] int FindPin([In, MarshalAs(UnmanagedType.IUnknown)] object source, [In] int direction, [In] ref Guid category, [In] ref Guid mediaType, [In, MarshalAs(UnmanagedType.Bool)] bool unconnected, [In] int index, [Out] out IntPtr pin);
        }

        [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IMediaControl
        {
            [PreserveSig] int Run();
            [PreserveSig] int Pause();
            [PreserveSig] int Stop();
            [PreserveSig] int GetState([In] int msTimeout, [Out] out int filterState);
            [PreserveSig] int RenderFile([In, MarshalAs(UnmanagedType.BStr)] string fileName);
            [PreserveSig] int AddSourceFilter([In, MarshalAs(UnmanagedType.BStr)] string fileName, [Out, MarshalAs(UnmanagedType.IDispatch)] out object filterInfo);
            [PreserveSig] int get_FilterCollection([Out, MarshalAs(UnmanagedType.IDispatch)] out object unk);
            [PreserveSig] int get_RegFilterCollection([Out, MarshalAs(UnmanagedType.IDispatch)] out object unk);
            [PreserveSig] int StopWhenReady();
        }

        [ComImport, Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISampleGrabber
        {
            [PreserveSig] int SetOneShot([In, MarshalAs(UnmanagedType.Bool)] bool oneShot);
            [PreserveSig] int SetMediaType([In, MarshalAs(UnmanagedType.LPStruct)] AMMediaType pmt);
            [PreserveSig] int GetConnectedMediaType([Out, MarshalAs(UnmanagedType.LPStruct)] AMMediaType pmt);
            [PreserveSig] int SetBufferSamples([In, MarshalAs(UnmanagedType.Bool)] bool bufferThem);
            [PreserveSig] int GetCurrentBuffer(ref int bufferSize, IntPtr buffer);
            [PreserveSig] int GetCurrentSample(out IntPtr sample);
            [PreserveSig] int SetCallback(ISampleGrabberCB callback, int whichMethodToCallback);
        }

        [ComImport, Guid("0579154A-2B53-4994-B0D0-E773148EFF85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISampleGrabberCB
        {
            [PreserveSig] int SampleCB(double sampleTime, IntPtr sample);
            [PreserveSig] int BufferCB(double sampleTime, IntPtr buffer, int bufferLen);
        }

        [ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAMStreamConfig
        {
            [PreserveSig] int SetFormat([In] IntPtr pmt);
            [PreserveSig] int GetFormat([Out] out IntPtr pmt);
            [PreserveSig] int GetNumberOfCapabilities(out int count, out int size);
            [PreserveSig] int GetStreamCaps([In] int index, [Out] out IntPtr pmt, [In] IntPtr streamConfigCaps);
        }

        [ComImport, Guid("C6E13380-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAMCrossbar
        {
            [PreserveSig] int get_PinCounts([Out] out int outputPinCount, [Out] out int inputPinCount);
            [PreserveSig] int CanRoute([In] int outputPinIndex, [In] int inputPinIndex);
            [PreserveSig] int Route([In] int outputPinIndex, [In] int inputPinIndex);
            [PreserveSig] int get_IsRoutedTo([In] int outputPinIndex, [Out] out int inputPinIndex);
            [PreserveSig] int get_CrossbarPinInfo([In, MarshalAs(UnmanagedType.Bool)] bool isInputPin, [In] int pinIndex, [Out] out int pinIndexRelated, [Out] out int physicalType);
        }

        [ComImport, Guid("B196B28B-BAB4-101A-B69C-00AA00341D07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpecifyPropertyPages
        {
            [PreserveSig] int GetPages(out DsCauuid pPages);
        }
    }
}
