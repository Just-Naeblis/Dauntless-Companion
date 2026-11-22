using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DauntlessCompanion
{
    static class Dpi
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)] static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);
        static readonly IntPtr SystemAware = new IntPtr(-2);
        public static bool On;
        public static double K = 1.0;

        public static void Init()
        {
            try
            {
                IntPtr u = GetModuleHandle("user32.dll");
                if (u == IntPtr.Zero || GetProcAddress(u, "SetThreadDpiAwarenessContext") == IntPtr.Zero) return;
                if (!SetProcessDPIAware()) return;
                int d = 96;
                IntPtr dc = GetDC(IntPtr.Zero);
                try { d = GetDeviceCaps(dc, 88); } finally { ReleaseDC(IntPtr.Zero, dc); }
                On = true;
                K = Math.Max(1.0, Math.Min(4.0, d / 96.0));
            }
            catch { }
        }

        static void Run(IntPtr ctx, Action a)
        {
            IntPtr old = IntPtr.Zero;
            if (On) { try { old = SetThreadDpiAwarenessContext(ctx); } catch { } }
            try { a(); }
            finally { if (old != IntPtr.Zero) { try { SetThreadDpiAwarenessContext(old); } catch { } } }
        }

        public static void Scaled(Action a) { Run(SystemAware, a); }
        public static Action ForBars(Action a) { return delegate { Run(SystemAware, a); }; }
        public static Action<string> ForBars(Action<string> a) { return delegate(string v) { Run(SystemAware, delegate { a(v); }); }; }
    }

    static class Ui
    {
        public const int RefWorkAreaH = 1440;
        public const double MinK = 0.85;
        public static double K = 1.0;
        public static double Fit = 1.0;
        public static int Px(int v) { return (int)Math.Round(v * K); }
        public static float PxF(float v) { return (float)(v * K); }
        public static float Pt(float v) { return (float)(v * Fit); }

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font UiFont(float pt) { return UiFont("Segoe UI", pt, FontStyle.Regular); }
        public static Font UiFont(float pt, FontStyle style) { return UiFont("Segoe UI", pt, style); }

        public static Font UiFont(string family, float pt, FontStyle style)
        {
            float size = Pt(pt);
            string key = family + "|" + size.ToString("R", CultureInfo.InvariantCulture) + "|" + ((int)style).ToString(CultureInfo.InvariantCulture);
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(family, size, style);
                fonts[key] = f;
            }
            return f;
        }

        public static double KFor(double dpiK, int workAreaH)
        {
            if (workAreaH <= 0) return dpiK;
            return Math.Max(MinK, Math.Min(dpiK, workAreaH / (double)RefWorkAreaH));
        }

        public static void Init(double dpiK, int workAreaH)
        {
            K = KFor(dpiK, workAreaH);
            Fit = K / Math.Max(1.0, dpiK);
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        public static bool Down(int vk) { return GetAsyncKeyState(vk) < 0; }
    }

    static class ModPaths
    {
        public static string Root()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (string.Equals(Path.GetFileName(b), "build", StringComparison.OrdinalIgnoreCase))
            {
                string up = Path.GetDirectoryName(b);
                if (!string.IsNullOrEmpty(up)) return up;
            }
            return b;
        }

        public static string Data() { return Path.Combine(Root(), "data"); }

        public static void MoveIn(string fromDir, string toDir, string name)
        {
            try
            {
                string from = Path.Combine(fromDir, name), to = Path.Combine(toDir, name);
                if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
            }
            catch (Exception fail) { Diag.Fail("moving " + name + " into the data folder", fail); }
        }
    }

    static class Diag
    {
        static readonly object gate = new object();
        static readonly List<string> queue = new List<string>();
        static readonly HashSet<string> seen = new HashSet<string>();

        public static void Fail(string where, Exception e)
        {
            string msg = e == null ? "unknown error" : e.Message;
            lock (gate)
            {
                if (seen.Count >= 200 || !seen.Add(where + "|" + msg)) return;
                queue.Add("Something went wrong while " + where + ": " + msg);
            }
        }

        public static void Drain(Action<string> log)
        {
            string[] items;
            lock (gate)
            {
                if (queue.Count == 0) return;
                items = queue.ToArray();
                queue.Clear();
            }
            foreach (string m in items) log(m);
        }
    }

    static class SafeFile
    {
        public static void Write(string path, string text)
        {
            UTF8Encoding enc = new UTF8Encoding(false);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, enc);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null, true);
                else File.Move(tmp, path);
            }
            catch (Exception)
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }

    static class AppIcon
    {
        public const string DefaultTheme = "thrax";
        static Icon full, small;
        static readonly Dictionary<string, Icon> themed = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        static string PathFor(string dir, string name)
        {
            return Path.Combine(Path.Combine(dir, "icons"), name + ".ico");
        }

        public static void Load(string dir)
        {
            try
            {
                string path = PathFor(dir, DefaultTheme);
                if (!File.Exists(path)) return;
                full = new Icon(path);
                small = new Icon(path, SystemInformation.SmallIconSize);
            }
            catch { full = null; small = null; }
        }

        public static Icon Full { get { return full ?? SystemIcons.Application; } }
        public static Icon Small { get { return small ?? SystemIcons.Application; } }

        public static Icon ForTheme(string theme)
        {
            string name = (theme ?? "").Trim().ToLowerInvariant();
            if (name == "" || name == DefaultTheme || !Regex.IsMatch(name, "^[a-z]+$")) return Full;
            Icon ic;
            if (themed.TryGetValue(name, out ic)) return ic ?? Full;
            try
            {
                string path = PathFor(ModPaths.Root(), name);
                if (File.Exists(path)) ic = new Icon(path);
            }
            catch (Exception fail) { Diag.Fail("loading icons\\" + name + ".ico", fail); ic = null; }
            themed[name] = ic;
            return ic ?? Full;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                using (Process me = Process.GetCurrentProcess())
                    foreach (string name in new string[] { me.ProcessName, "BehemothHealthBarsOverlay" })
                        foreach (Process p in Process.GetProcessesByName(name))
                        {
                            try { if (p.Id != me.Id) { p.Kill(); p.WaitForExit(2000); } } catch { }
                            p.Dispose();
                        }
            }
            catch { }

            try
            {
                Dpi.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                new App(Options.Parse(args)).Run();
                return 0;
            }
            catch (Exception e)
            {
                try
                {
                    string log = Path.Combine(ModPaths.Data(), "healthbar-overlay.log");
                    File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ERROR: the overlay stopped: " + e + "\r\n");
                }
                catch { }
                return 1;
            }
        }
    }

    class OverlayForm : Form
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
            public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct ULWINFO
        {
            public int cbSize; public IntPtr hdcDst; public IntPtr pptDst; public IntPtr psize; public IntPtr hdcSrc;
            public IntPtr pptSrc; public int crKey; public IntPtr pblend; public int dwFlags; public IntPtr prcDirty;
        }
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindowIndirect(IntPtr hwnd, ref ULWINFO info);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);

        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;
        Bitmap canvas;
        int canvasW, canvasH;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000;
                cp.ExStyle |= 0x20;
                cp.ExStyle |= 0x80;
                cp.ExStyle |= 0x08000000;
                return cp;
            }
        }

        public Bitmap Canvas(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (canvas != null && w == canvasW && h == canvasH) return canvas;
            FreeCanvas();
            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                BITMAPINFOHEADER bi = new BITMAPINFOHEADER();
                bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0;
                IntPtr bits;
                dib = CreateDIBSection(screenDc, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed for " + w + "x" + h);
                memDc = CreateCompatibleDC(screenDc);
                oldObj = SelectObject(memDc, dib);
                canvas = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                canvasW = w; canvasH = h;
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); }
            return canvas;
        }

        public void Present(int x, int y)
        {
            if (canvas == null) return;
            GdiFlush();
            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                SIZE size = new SIZE(); size.cx = canvasW; size.cy = canvasH;
                POINT source = new POINT();
                POINT target = new POINT(); target.X = x; target.Y = y;
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0; blend.BlendFlags = 0; blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                UpdateLayeredWindow(this.Handle, screenDc, ref target, ref size, memDc, ref source, 0, ref blend, 2);
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); }
        }

        public bool Present(int x, int y, Rectangle dirty)
        {
            if (canvas == null || dirty.Width <= 0 || dirty.Height <= 0) return false;
            GdiFlush();
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr mem = Marshal.AllocHGlobal(48);
            try
            {
                POINT target = new POINT(); target.X = x; target.Y = y;
                SIZE size = new SIZE(); size.cx = canvasW; size.cy = canvasH;
                POINT source = new POINT();
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0; blend.BlendFlags = 0; blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                RECT rc = new RECT(); rc.Left = dirty.Left; rc.Top = dirty.Top; rc.Right = dirty.Right; rc.Bottom = dirty.Bottom;
                IntPtr pDst = mem, pSize = IntPtr.Add(mem, 8), pSrc = IntPtr.Add(mem, 16), pBlend = IntPtr.Add(mem, 24), pRc = IntPtr.Add(mem, 32);
                Marshal.StructureToPtr(target, pDst, false);
                Marshal.StructureToPtr(size, pSize, false);
                Marshal.StructureToPtr(source, pSrc, false);
                Marshal.StructureToPtr(blend, pBlend, false);
                Marshal.StructureToPtr(rc, pRc, false);
                ULWINFO info = new ULWINFO();
                info.cbSize = Marshal.SizeOf(typeof(ULWINFO));
                info.hdcDst = screenDc; info.pptDst = pDst; info.psize = pSize; info.hdcSrc = memDc; info.pptSrc = pSrc;
                info.crKey = 0; info.pblend = pBlend; info.dwFlags = 2; info.prcDirty = pRc;
                return UpdateLayeredWindowIndirect(this.Handle, ref info);
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(mem); ReleaseDC(IntPtr.Zero, screenDc); }
        }

        void FreeCanvas()
        {
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (memDc != IntPtr.Zero)
            {
                if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                DeleteDC(memDc);
                memDc = IntPtr.Zero; oldObj = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
            canvasW = 0; canvasH = 0;
        }

        protected override void Dispose(bool disposing)
        {
            FreeCanvas();
            base.Dispose(disposing);
        }
    }

    class FrameClock
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        const uint HighResolution = 0x2;
        const uint TimerAllAccess = 0x1F0003;

        readonly Control owner;
        readonly MethodInvoker frame;
        readonly System.Threading.ManualResetEvent wake = new System.Threading.ManualResetEvent(false);
        System.Threading.Thread thread;
        IntPtr waitTimer = IntPtr.Zero;
        bool highRes;
        volatile bool stop;
        volatile bool running;
        volatile int fps = 30;
        int pending;

        public FrameClock(Control owner, MethodInvoker onFrame)
        {
            this.owner = owner;
            frame = delegate
            {
                try { onFrame(); }
                finally { System.Threading.Interlocked.Exchange(ref pending, 0); }
            };
        }

        public int Fps { get { return fps; } set { fps = Math.Max(10, Math.Min(240, value)); } }
        public bool HighResolutionTimer { get { return highRes; } }

        public bool Running
        {
            get { return running; }
            set { running = value; if (value) wake.Set(); }
        }

        public void Start()
        {
            waitTimer = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, HighResolution, TimerAllAccess);
            highRes = waitTimer != IntPtr.Zero;
            if (!highRes) waitTimer = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0, TimerAllAccess);
            thread = new System.Threading.Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "frame clock";
            thread.Start();
        }

        public void Stop()
        {
            stop = true;
            wake.Set();
            if (thread != null) thread.Join(500);
            if (waitTimer != IntPtr.Zero) { CloseHandle(waitTimer); waitTimer = IntPtr.Zero; }
        }

        void SleepUntil(long target)
        {
            long now = Stopwatch.GetTimestamp();
            if (target <= now) return;
            if (waitTimer != IntPtr.Zero)
            {
                long due = -(long)((target - now) * 10000000.0 / Stopwatch.Frequency);
                if (due < 0 && SetWaitableTimer(waitTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    WaitForSingleObject(waitTimer, 1000);
                    return;
                }
            }
            System.Threading.Thread.Sleep(Math.Max(1, (int)((target - now) * 1000 / Stopwatch.Frequency)));
        }

        void Loop()
        {
            bool fine = false;
            try
            {
                long next = 0;
                while (!stop)
                {
                    if (!running)
                    {
                        if (fine) { timeEndPeriod(1); fine = false; }
                        wake.Reset();
                        if (!running && !stop) wake.WaitOne();
                        next = 0;
                        continue;
                    }
                    if (!highRes && !fine) { timeBeginPeriod(1); fine = true; }
                    long step = Stopwatch.Frequency / fps;
                    long now = Stopwatch.GetTimestamp();
                    if (next == 0 || next < now - step) next = now;
                    next += step;
                    SleepUntil(next);
                    if (stop || !running) continue;
                    if (System.Threading.Interlocked.CompareExchange(ref pending, 1, 0) == 0)
                    {
                        try { owner.BeginInvoke(frame); }
                        catch { System.Threading.Interlocked.Exchange(ref pending, 0); }
                    }
                }
            }
            catch (Exception fail) { Diag.Fail("running the animation timer", fail); }
            finally { if (fine) timeEndPeriod(1); }
        }
    }

    class Bar
    {
        public string Id = "";
        public string Name = "";
        public double Hp, Max, Shield, ShieldMax, SgFill, SgMax, From;
        public int Rage, Aether, Eng;
        public bool HasFrom, HasEng;
        public bool Dying;
        public DateTime DyingAt;

        public Bar Copy() { return (Bar)MemberwiseClone(); }
    }

    class State
    {
        public long Time;
        public List<Bar> Bars = new List<Bar>();
    }

    class KeySpec
    {
        public int Vk;
        public List<int> Mods = new List<int>();
        public string Text = "";
    }

    class TrailState { public double From, To; public DateTime Start; public int Eng; }

    class GlowState { public double From, To; public DateTime T; }

    class Ghost { public Bar Bar; public int Slot; }

    class CachedImage { public string Key; public Bitmap Bmp; public int Cx, Cw, TopH, LeftW, RightX; }

    class Options
    {
        public string Dir = "";
        public int Width = 0, Height = 34, FromBottom = 200, FromTop = 0, StaleSeconds = 10;
        public string Position = "Top";
        public double NameSize = 15, NumberSize = 12.5, Slant = 0.45;
        public string HotKey = "F8";
        public string[] GameProcess = new string[] { "Dauntless*", "Archon*" };
        public bool NoFocusCheck, QuitWhenGameCloses;

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            CultureInfo inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].Trim();
                if (!a.StartsWith("-")) continue;
                string k = a.TrimStart('-').ToLowerInvariant();
                string v = (i + 1 < args.Length) ? args[i + 1] : "";
                double d;
                bool num = double.TryParse(v, NumberStyles.Float, inv, out d);
                switch (k)
                {
                    case "nofocuscheck": o.NoFocusCheck = true; continue;
                    case "quitwhengamecloses": o.QuitWhenGameCloses = true; continue;
                }
                bool used = true;
                switch (k)
                {
                    case "dir": o.Dir = v; break;
                    case "position": o.Position = v; break;
                    case "hotkey": o.HotKey = v; break;
                    case "gameprocess":
                        List<string> list = new List<string>();
                        foreach (string p in v.Split(',')) { if (p.Trim() != "") list.Add(p.Trim()); }
                        if (list.Count > 0) o.GameProcess = list.ToArray();
                        break;
                    case "width": if (num) o.Width = App.I(d); break;
                    case "height": if (num) o.Height = App.I(d); break;
                    case "frombottom": if (num) o.FromBottom = App.I(d); break;
                    case "fromtop": if (num) o.FromTop = App.I(d); break;
                    case "staleseconds": if (num) o.StaleSeconds = App.I(d); break;
                    case "namesize": if (num) o.NameSize = d; break;
                    case "numbersize": if (num) o.NumberSize = d; break;
                    case "slant": if (num) o.Slant = d; break;
                    default: used = false; break;
                }
                if (used) i++;
            }
            return o;
        }
    }

    static class Json
    {
        const int MaxDepth = 64;

        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i, 0);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("text after the end");
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static void Expect(string s, ref int i, char c)
        {
            Ws(s, ref i);
            if (i >= s.Length || s[i] != c) throw new FormatException("expected " + c);
            i++;
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("nested too deep");
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string key = Str(s, ref i);
                    Expect(s, ref i, ':');
                    d[key] = Value(s, ref i, depth + 1);
                    Ws(s, ref i);
                    if (i >= s.Length) throw new FormatException("unexpected end");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected , or }");
                }
            }
            if (c == '[')
            {
                i++;
                List<object> list = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(Value(s, ref i, depth + 1));
                    Ws(s, ref i);
                    if (i >= s.Length) throw new FormatException("unexpected end");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw new FormatException("expected , or ]");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("unexpected character " + c);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("expected a string");
            i++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unfinished string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("unfinished string");
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }

        public static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            if (v is double) return (double)v;
            if (v is bool) return ((bool)v) ? 1 : 0;
            double r;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            return 0;
        }

        public static string Text(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    class App
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int I(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v >= int.MaxValue) return int.MaxValue;
            if (v <= int.MinValue) return int.MinValue;
            return (int)Math.Round(v, MidpointRounding.ToEven);
        }

        static PointF P(double x, double y) { return new PointF((float)x, (float)y); }

        readonly Options opt;
        readonly string root;
        readonly string file, namesFile, settingsFile, posFile, logFile;

        int logCount;
        readonly HashSet<string> logSeen = new HashSet<string>();
        int logOn = -1;
        readonly List<string> logHeld = new List<string>();

        Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string settingsStamp;
        string[] gameProcs;
        Regex[] gameRegex;
        static readonly Regex[] ExcludeRegex = new Regex[] { Wild("*Forever*"), Wild("*Launcher*"), Wild("*Setup*"), Wild("*Updater*"), Wild("DauntlessCompanion*") };
        bool noFocus;
        string hotKeyText;

        int gap = -1;
        int barH, labelH, capH = 3, nameDrop;
        bool sgOn = true, divOn = true;
        bool barsOn = true, settingsPreviewOn = true;
        Color divColor = Color.FromArgb(255, 6, 8, 12);
        double divThick = 1;
        int sgH = 5, barW = 600, totalW = 600, totalH, blockH;
        int cfgW, cfgFromTop, cfgFromBottom, cfgBarH;
        bool cfgTop;
        double curName, curNum, baseName, baseNum, slant;
        double offX, offY, mulW = 1.0, mulH = 1.0;
        int lastBaseW = 900, sizeTicks, moveTicks;
        int scrW = 1920, scrH = 1080;
        bool posDirty;
        int padX = 3, vgap = 6, hgap;
        bool sideBySide, rowMode;
        bool trailOn = true;
        SolidBrush trailBrush;
        double trailMin = 7.5;
        SolidBrush nameBrush = new SolidBrush(Color.White);
        SolidBrush numBrush = new SolidBrush(Color.White);
        bool showNums = true, showPct = true;
        bool deathOn = true;
        double deathSec = 1.5;
        bool quarterOn;
        int glowSize = -1;
        double glowStrength = 0.9;
        bool glowAnimOn = true, glowAnimOut = true;
        double glowAnimSec = 0.8;
        string glowStyle = "line";
        bool pmOn, pmRage, pmAether, pmDead;
        double[] pmHp = new double[] { 1.0, 1.0 };
        DateTime pmDeadAt;
        DateTime pmReviveAt = DateTime.MaxValue;
        Dictionary<string, KeySpec> pmKeys = new Dictionary<string, KeySpec>();
        readonly Dictionary<string, bool> pmKeyWas = new Dictionary<string, bool>();
        double lingerSec = 2.5;
        readonly Dictionary<string, GlowState> glowStarts = new Dictionary<string, GlowState>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> glowUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool endWasDown;
        bool rageOn = true, aetherOn = true;
        Color rageColor = Color.FromArgb(255, 220, 0, 0);
        Color aetherColor = Color.FromArgb(255, 3, 187, 218);
        int stackMode = 2;
        bool stackUp;
        string lastIds = "";
        bool lastFocused, wasStale;
        int winX, winY;
        List<Bar> bars = new List<Bar>();
        Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DateTime namesStamp = DateTime.MinValue;
        State lastState;
        string lastStateText;
        string lastSig = "";
        bool userHidden, gameFocused;
        string lastForeground = "";
        int ticks;
        bool wasDown, sawGame;
        int missing;
        Process gameProc;
        int fgPid;
        IntPtr fgHwnd = IntPtr.Zero, gameHwnd = IntPtr.Zero;
        DateTime gameScanAt = DateTime.MinValue;
        uint namePid;
        string namePidName;

        SettingsWindow settingsWin;
        KeySpec settingsKey;
        bool gameWasFront, overlayBehind, refocusSettings, avoidSettings;

        bool SetBehind(bool behind)
        {
            bool restored = !behind && overlayBehind;
            try
            {
                const uint F = 0x0001 | 0x0002 | 0x0010;
                foreach (Form f in new Form[] { form, pinForm })
                {
                    if (f == null || f.IsDisposed || !f.IsHandleCreated) continue;
                    if (behind)
                    {
                        if (gameHwnd == IntPtr.Zero || !Native.IsWindow(gameHwnd)) continue;
                        if (!overlayBehind) Native.SetWindowPos(f.Handle, new IntPtr(-2), 0, 0, 0, 0, F);
                        IntPtr prev = Native.GetWindow(gameHwnd, 3);
                        if (prev != f.Handle) Native.SetWindowPos(f.Handle, prev, 0, 0, 0, 0, F);
                    }
                    else if (overlayBehind) Native.SetWindowPos(f.Handle, new IntPtr(-1), 0, 0, 0, 0, F);
                }
                overlayBehind = behind;
            }
            catch { }
            return restored;
        }
        KeySpec mvUp, mvDown, mvLeft, mvRight, mvReset, szUp, szDown, szLeft, szRight, szReset;
        bool settingsKeyWas;
        KeySpec clearKey;
        bool clearKeyWas;
        string clearFile;
        int myPid;

        KeySpec hideKey;
        bool hkSet;

        readonly SolidBrush trackBrush = new SolidBrush(Color.FromArgb(150, 10, 12, 16));
        Color fillTop = Color.FromArgb(240, 196, 40, 36);
        Color fillBottom = Color.FromArgb(240, 128, 18, 18);
        bool condensed;
        readonly SolidBrush shieldBrush = new SolidBrush(Color.FromArgb(75, 70, 170, 255));
        readonly SolidBrush shieldEdgeBrush = new SolidBrush(Color.FromArgb(235, 140, 205, 255));
        readonly Pen framePen = new Pen(Color.FromArgb(170, 230, 230, 230), 1f);
        readonly Pen tickPen = new Pen(Color.FromArgb(75, 255, 255, 255), 1f);
        readonly Pen capPen = new Pen(Color.FromArgb(200, 235, 235, 235), 2.5f);
        readonly Pen shieldEdgePen = new Pen(Color.FromArgb(235, 140, 205, 255), 2f);
        readonly SolidBrush outlineBrush = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        readonly Pen sgEdgePen = new Pen(Color.FromArgb(130, 14, 18, 24), 1f);
        readonly SolidBrush sgTrackBrush = new SolidBrush(Color.FromArgb(170, 6, 14, 18));
        Color sgTop = Color.FromArgb(255, 66, 239, 245);
        Color sgBottom = Color.FromArgb(255, 66, 239, 245);
        Font fontName, fontNums;
        readonly StringFormat centerFmt, nameFmt;

        readonly Dictionary<string, TrailState> trails = new Dictionary<string, TrailState>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> trailUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> frameTrailSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> frameGlowSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        const double TrailSec = 0.5;
        bool trailAnimating;
        bool[] lastAnim = new bool[0];
        List<Bar> lastAnimBars;
        int lastCanvasW = -1, lastCanvasH = -1, lastWinX, lastWinY;
        int animFps = 30;
        readonly List<Ghost> ghosts = new List<Ghost>();
        List<Bar> prevSrc = new List<Bar>();
        List<Bar> prevShown = new List<Bar>();
        readonly Dictionary<string, CachedImage> textCache = new Dictionary<string, CachedImage>();
        readonly Dictionary<string, CachedImage> glowCache = new Dictionary<string, CachedImage>();

        OverlayForm form;
        OverlayForm pinForm;
        Bitmap pinBmp;
        string pinStamp, pinPos = "left";
        bool pinDirty;
        int pinX = int.MinValue, pinY = int.MinValue;
        ApplicationContext ctx;
        NotifyIcon tray;
        ToolStripItem itemToggle, itemInfo;
        Timer timer, keyTimer;
        FrameClock clock;

        public App(Options o)
        {
            opt = o;
            root = ModPaths.Root();
            string dir = string.IsNullOrEmpty(o.Dir) ? ModPaths.Data() : o.Dir;
            try { Directory.CreateDirectory(dir); } catch { }
            ModPaths.MoveIn(root, dir, "healthbar-position.json");
            ModPaths.MoveIn(root, dir, "healthbar-overlay.log");
            file = Path.Combine(dir, "healthbar.json");
            clearFile = Path.Combine(dir, "healthbar-clear.txt");
            posFile = Path.Combine(dir, "healthbar-position.json");
            namesFile = Path.Combine(dir, "behemoth-names.txt");
            settingsFile = Path.Combine(dir, "settings.ini");
            logFile = Path.Combine(dir, "healthbar-overlay.log");

            SetGameProcs(o.GameProcess);
            noFocus = o.NoFocusCheck;
            hotKeyText = o.HotKey;
            barH = o.Height;
            cfgBarH = o.Height;
            labelH = I(Math.Round(o.NameSize * 2.33));
            cfgW = o.Width;
            cfgFromTop = o.FromTop;
            cfgFromBottom = o.FromBottom;
            cfgTop = o.Position.Trim().ToLowerInvariant() != "bottom";
            curName = o.NameSize; baseName = o.NameSize;
            curNum = o.NumberSize; baseNum = o.NumberSize;
            slant = o.Slant;
            totalH = labelH + o.Height + capH;
            blockH = totalH;

            fontName = new Font("Segoe UI", (float)(o.NameSize * 96.0 / 72.0), FontStyle.Bold, GraphicsUnit.Pixel);
            fontNums = new Font("Segoe UI", (float)(o.NumberSize * 96.0 / 72.0), FontStyle.Bold, GraphicsUnit.Pixel);

            centerFmt = new StringFormat();
            centerFmt.Alignment = StringAlignment.Center;
            centerFmt.LineAlignment = StringAlignment.Center;
            centerFmt.FormatFlags = StringFormatFlags.NoWrap;

            nameFmt = new StringFormat();
            nameFmt.Alignment = StringAlignment.Center;
            nameFmt.LineAlignment = StringAlignment.Far;
            nameFmt.Trimming = StringTrimming.EllipsisCharacter;
            nameFmt.FormatFlags = StringFormatFlags.NoWrap;
        }

        void Log(string m)
        {
            try
            {
                if (logCount >= 300) return;
                string line = DateTime.Now.ToString("HH:mm:ss", Inv) + "  " + m + "\r\n";
                if (logOn < 0) { logHeld.Add(line); return; }
                if (logOn == 0) return;
                logCount++;
                File.AppendAllText(logFile, line);
            }
            catch { }
        }

        void SetLogging(bool on)
        {
            int want = on ? 1 : 0;
            if (logOn == want) return;
            bool first = logOn < 0;
            logOn = want;
            try
            {
                if (on)
                {
                    if (first)
                    {
                        File.WriteAllText(logFile, "");
                        foreach (string line in logHeld) { logCount++; File.AppendAllText(logFile, line); }
                    }
                    else Log("Logging is on (set in settings.ini)");
                }
                else if (first && File.Exists(logFile))
                {
                    try { File.Delete(logFile); } catch { }
                }
            }
            catch { }
            logHeld.Clear();
        }

        void LogOnce(string key, string m)
        {
            if (logSeen.Add(key)) Log(m);
        }

        void LogError(string where, Exception e)
        {
            string at = "";
            if (e.StackTrace != null)
            {
                string[] lines = e.StackTrace.Split('\n');
                if (lines.Length > 0) at = " (" + lines[0].Trim() + ")";
            }
            LogOnce(where + ":" + e.Message, "Something went wrong in " + where + ": " + e.Message + at);
        }

        static Regex Wild(string pattern)
        {
            return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        void SetGameProcs(string[] list)
        {
            gameProcs = list;
            gameRegex = new Regex[list.Length];
            for (int i = 0; i < list.Length; i++) gameRegex[i] = Wild(list[i]);
        }

        bool IsGameName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (Regex x in ExcludeRegex) if (x.IsMatch(name)) return false;
            foreach (Regex p in gameRegex) if (p.IsMatch(name)) return true;
            return false;
        }

        string ForegroundProcessName()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                fgHwnd = h;
                if (h == IntPtr.Zero) return "";
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return "";
                fgPid = (int)pid;
                if (pid == namePid && namePidName != null && (ticks % 25) != 0) return namePidName;
                using (Process p = Process.GetProcessById((int)pid)) { namePidName = p.ProcessName; }
                namePid = pid;
                return namePidName;
            }
            catch
            {
                namePidName = null;
                return "";
            }
        }

        IntPtr GameWindow()
        {
            if (gameHwnd != IntPtr.Zero && Native.IsWindow(gameHwnd)) return gameHwnd;
            gameHwnd = IntPtr.Zero;
            DateTime now = DateTime.UtcNow;
            if ((now - gameScanAt).TotalSeconds < 2) return IntPtr.Zero;
            gameScanAt = now;
            foreach (Process pr in Process.GetProcesses())
            {
                try
                {
                    if (gameHwnd == IntPtr.Zero && IsGameName(pr.ProcessName) && pr.MainWindowHandle != IntPtr.Zero) gameHwnd = pr.MainWindowHandle;
                }
                catch { }
                pr.Dispose();
            }
            return gameHwnd;
        }

        int GlowSize(double h)
        {
            if (glowSize > 0) return glowSize;
            return I(Math.Max(5, Math.Round(h * 0.25)));
        }

        void UpdateFonts()
        {
            double en = Math.Max(6.0 * Dpi.K, Math.Min(60.0 * Dpi.K, Math.Round(baseName * mulH * Dpi.K * 2) / 2));
            double eu = Math.Max(6.0 * Dpi.K, Math.Min(60.0 * Dpi.K, Math.Round(baseNum * mulH * Dpi.K * 2) / 2));
            if (en != curName)
            {
                Font old = fontName;
                fontName = new Font("Segoe UI", (float)(en * 96.0 / 72.0), FontStyle.Bold, GraphicsUnit.Pixel);
                old.Dispose();
                curName = en;
            }
            if (eu != curNum)
            {
                Font old = fontNums;
                fontNums = new Font("Segoe UI", (float)(eu * 96.0 / 72.0), FontStyle.Bold, GraphicsUnit.Pixel);
                old.Dispose();
                curNum = eu;
            }
            labelH = I(Math.Round(curName * 2.33));
        }

        Rectangle GameRect()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            IntPtr gw = GameWindow();
            if (gw != IntPtr.Zero)
            {
                Native.RECT r;
                if (Native.GetWindowRect(gw, out r) && (r.Right - r.Left) > 200 && (r.Bottom - r.Top) > 200)
                    screen = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            return screen;
        }

        void PinTick()
        {
            Rectangle screen = GameRect();
            double scale = Math.Max(0.75, Math.Min(2.5, screen.Height / 1080.0));
            string path = Path.Combine(root, "data", "pinned-build.txt");
            string clearText = Setting("clear_key", "F4").Trim();
            if (clearText == "") clearText = "F4";
            int pinOpacity;
            if (!int.TryParse(Setting("pinned_build_opacity", "96").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pinOpacity)) pinOpacity = 96;
            pinOpacity = Math.Max(0, Math.Min(100, pinOpacity));
            string stamp = (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture) : "none") + "|" + scale.ToString("0.00", CultureInfo.InvariantCulture) + "|" + clearText + "|" + pinOpacity.ToString(CultureInfo.InvariantCulture);
            if (stamp != pinStamp)
            {
                pinStamp = stamp;
                if (pinBmp != null) { pinBmp.Dispose(); pinBmp = null; }
                pinBmp = SettingsWindow.RenderPinnedBuild(root, scale, clearText, pinOpacity);
                pinDirty = true;
            }
            if (pinBmp == null || !gameFocused || userHidden)
            {
                if (pinForm.Visible) pinForm.Hide();
                pinX = int.MinValue;
                return;
            }
            int m = I(24 * scale), w = pinBmp.Width, h = pinBmp.Height;
            int x = pinPos.EndsWith("right") ? screen.Right - w - m : screen.Left + m;
            int y = pinPos.StartsWith("top") ? screen.Top + I(90 * scale) : pinPos.StartsWith("bottom") ? screen.Bottom - h - I(140 * scale) : screen.Top + (screen.Height - h) / 2;
            if (pinDirty || x != pinX || y != pinY)
            {
                if (pinDirty)
                {
                    Bitmap c = pinForm.Canvas(pinBmp.Width, pinBmp.Height);
                    using (Graphics g = Graphics.FromImage(c))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(pinBmp, new Rectangle(0, 0, pinBmp.Width, pinBmp.Height));
                    }
                    pinDirty = false;
                }
                pinForm.Present(x, y);
                pinX = x;
                pinY = y;
            }
            if (!pinForm.Visible) pinForm.Show();
        }

        void UnpinBuild()
        {
            if (settingsWin != null && !settingsWin.IsDisposed) Dpi.Scaled(delegate { settingsWin.UnpinAll(); });
            else SettingsWindow.UnpinInFile(root);
            if (pinBmp != null) { pinBmp.Dispose(); pinBmp = null; }
            if (pinForm != null && pinForm.Visible) pinForm.Hide();
            pinX = int.MinValue;
            pinStamp = null;
            Log("Pinned build cleared");
        }

        static int Count(Dictionary<string, int> d, string k) { int c; d.TryGetValue(k, out c); return c; }

        static readonly Regex HexColor = new Regex("^#?([0-9a-fA-F]{6})$");
        static readonly Regex RgbColor = new Regex("^(\\d{1,3})\\s*,\\s*(\\d{1,3})\\s*,\\s*(\\d{1,3})$");

        static readonly Regex InlineComment = new Regex(";|#(\\s|$)");

        Rectangle winBounds = Rectangle.Empty;
        int winRefWorkAreaH;

        State ReadState()
        {
            if (!File.Exists(file)) return null;
            try
            {
                string text;
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length > 1048576) return lastState;
                    using (StreamReader reader = new StreamReader(fs))
                        text = reader.ReadToEnd();
                }
                if (string.IsNullOrWhiteSpace(text)) return lastState;
                if (lastState != null && text == lastStateText) return lastState;
                Dictionary<string, object> j = Json.Parse(text) as Dictionary<string, object>;
                if (j == null || !j.ContainsKey("bars")) return lastState;
                State st = new State();
                st.Time = (long)Math.Round(Json.Num(j, "time"));
                List<object> list = j["bars"] as List<object>;
                if (list != null)
                {
                    foreach (object o in list)
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        Bar b = new Bar();
                        b.Id = Json.Text(d, "id");
                        b.Name = Json.Text(d, "name");
                        b.Hp = Json.Num(d, "hp");
                        b.Max = Json.Num(d, "max");
                        b.Shield = Json.Num(d, "shield");
                        b.ShieldMax = Json.Num(d, "shieldMax");
                        b.SgFill = Json.Num(d, "sgFill");
                        b.SgMax = Json.Num(d, "sgMax");
                        b.Rage = I(Json.Num(d, "rage"));
                        b.Aether = I(Json.Num(d, "aether"));
                        b.HasFrom = d.ContainsKey("from");
                        b.From = Json.Num(d, "from");
                        b.HasEng = d.ContainsKey("eng");
                        b.Eng = I(Json.Num(d, "eng"));
                        st.Bars.Add(b);
                    }
                }
                lastState = st;
                lastStateText = text;
                return st;
            }
            catch { return lastState; }
        }

        List<Bar> PreviewBars()
        {
            string[] pNames = new string[] { "Primary Health Bar Preview", "Secondary Health Bar & Shield Preview" };
            double[] hpOf = new double[] { 123456789, 98765432 };
            double[] sgOf = new double[] { 0.6, 0.2 };
            double[] shOf = new double[] { 0, 45 };
            List<Bar> list = new List<Bar>();
            for (int pi = 0; pi < 2; pi++)
            {
                Bar bar = new Bar();
                bar.Id = "__preview" + (pi + 1) + "__";
                bar.Name = pNames[pi];
                bar.Hp = Math.Round(hpOf[pi] * pmHp[pi]);
                bar.Max = 123456789;
                bar.Shield = shOf[pi];
                bar.ShieldMax = 100;
                bar.SgFill = sgOf[pi];
                bar.SgMax = 100;
                bar.Rage = pmRage ? 1 : 0;
                bar.Aether = pmAether ? 1 : 0;
                if (pmDead)
                {
                    if ((DateTime.UtcNow - pmDeadAt).TotalSeconds >= lingerSec) continue;
                    bar.Hp = 0;
                    bar.SgFill = 0;
                }
                list.Add(bar);
            }
            return list;
        }

        string Sig(List<Bar> list)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                Bar b = list[i];
                if (i > 0) sb.Append(';');
                sb.Append(b.Id).Append(':').Append(b.Hp.ToString("R", Inv)).Append(':').Append(b.Max.ToString("R", Inv))
                  .Append(':').Append(b.Shield.ToString("R", Inv)).Append(':').Append(b.ShieldMax.ToString("R", Inv))
                  .Append(':').Append(b.SgFill.ToString("R", Inv)).Append(':').Append(b.SgMax.ToString("R", Inv))
                  .Append(':').Append(b.Dying ? 1 : 0).Append(':').Append(b.Rage).Append(':').Append(b.Aether);
            }
            sb.Append('|').Append(barW).Append('|').Append(barH).Append('|').Append(labelH).Append('|').Append(curName.ToString(Inv))
              .Append('|').Append(curNum.ToString(Inv)).Append('|').Append(sgOn).Append('|').Append(winX).Append(',').Append(winY)
              .Append('|').Append(stackUp).Append('|').Append(quarterOn).Append('|').Append(glowSize).Append('|').Append(glowStrength.ToString(Inv))
              .Append('|').Append(glowStyle).Append('|').Append(rageColor.ToArgb()).Append('|').Append(aetherColor.ToArgb());
            return sb.ToString();
        }

        void RedrawNow()
        {
            try
            {
                if (userHidden || !gameFocused) return;
                List<Bar> list;
                if (form.Visible && bars.Count > 0) list = bars;
                else if (pmOn || SettingsPreview()) { list = PreviewBars(); avoidSettings = !pmOn; }
                else return;
                bars = list;
                UpdateLayout(list.Count);
                if (!form.Visible) form.Show();
                RenderBars();
                lastSig = Sig(list);
            }
            catch (Exception e) { LogError("redraw", e); }
        }

        void MainTick()
        {
            Diag.Drain(Log);
            ticks++;
            if (pmDead && DateTime.UtcNow >= pmReviveAt)
            {
                pmDead = false;
                pmHp = new double[] { 1.0, 1.0 };
                pmReviveAt = DateTime.MaxValue;
                lastSig = "";
            }
            if ((ticks % 10) == 1) { LoadNames(); LoadSettings(); }

            if (opt.QuitWhenGameCloses && (ticks % 10) == 5)
            {
                if (gameProc != null)
                {
                    bool gone;
                    try { gone = gameProc.HasExited; } catch { gone = true; }
                    if (gone) missing++; else missing = 0;
                    if (missing >= 3) { Log("Game closed. Shutting down overlay"); ctx.ExitThread(); return; }
                }
                else
                {
                    bool running = false;
                    List<string> found = new List<string>();
                    foreach (Process pr in Process.GetProcesses())
                    {
                        try { if (IsGameName(pr.ProcessName) && pr.MainWindowHandle != IntPtr.Zero) { running = true; found.Add(pr.ProcessName); } } catch { }
                        pr.Dispose();
                    }
                    if (running)
                    {
                        sawGame = true;
                        missing = 0;
                        LogOnce("matched", "Treating these as the game: " + string.Join(", ", found.ToArray()));
                    }
                    else missing++;
                    if (sawGame && missing >= 5) { Log("Couldn't find game; overlay closing."); ctx.ExitThread(); return; }
                }
            }

            if (noFocus) gameFocused = true;
            else
            {
                string fg = ForegroundProcessName();
                lastForeground = fg;
                bool focused = IsGameName(fg);
                if (focused) gameHwnd = fgHwnd;
                if (focused && !gameWasFront && SettingsShown()) refocusSettings = true;
                if (!focused || !SettingsShown()) refocusSettings = false;
                gameWasFront = focused;
                bool restored = SetBehind(!focused && SettingsShown() && !SettingsInFront() && fgHwnd != IntPtr.Zero);
                if (!focused && SettingsShown()) focused = true;
                else if (fgHwnd == IntPtr.Zero && gameFocused) focused = true;
                gameFocused = focused;
                if (refocusSettings && !Native.Down(0x12) && !Native.Down(0x01) && !Native.Down(0x02))
                {
                    refocusSettings = false;
                    Dpi.Scaled(delegate { settingsWin.BringUp(); });
                }
                if (restored) RedrawNow();
            }

            List<Bar> list = new List<Bar>();
            State st = ReadState();
            bool stale = false;
            if (barsOn && st != null && st.Bars.Count > 0)
            {
                long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - st.Time;
                if (age <= opt.StaleSeconds) list = new List<Bar>(st.Bars); else stale = true;
            }

            List<string> idList = new List<string>();
            foreach (Bar b in list) idList.Add(b.Id);
            string ids = string.Join(", ", idList.ToArray());
            if (ids != lastIds)
            {
                Log(ids != "" ? "bars from the mod: " + ids : "bars from the mod: none");
                lastIds = ids;
            }
            if (list.Count > 0 && gameFocused != lastFocused)
            {
                if (gameFocused) Log("Game is back in front, showing overlay again");
                else Log("Game isn't in front (" + lastForeground + " is), hiding the overlay");
            }
            lastFocused = gameFocused;
            if (stale != wasStale)
            {
                if (stale) Log("healthbar.json hasn't changed in " + opt.StaleSeconds + " s, hiding the bars");
                else Log("healthbar.json updated again, redrawing bars");
                wasStale = stale;
            }

            if (!noFocus && gameProc == null && fgPid > 0 && fgPid != myPid && IsGameName(lastForeground))
            {
                try
                {
                    gameProc = Process.GetProcessById(fgPid);
                    missing = 0;
                    Log("Game found: " + gameProc.ProcessName + " (id " + fgPid + ")");
                }
                catch { }
            }

            avoidSettings = false;
            if (list.Count == 0 && (pmOn || SettingsPreview())) { list = PreviewBars(); avoidSettings = !pmOn; }

            list = UpdateGhosts(list, gameFocused && !userHidden);

            if (list.Count > 0 && gameFocused && !userHidden)
            {
                LogOnce("firstbars", "Showing the first bars (" + list.Count + "), game in front: " + gameFocused);
                UpdateLayout(list.Count);
                string sig = Sig(list);
                if (!form.Visible)
                {
                    form.Show();
                    lastSig = "";
                }
                if (sig != lastSig)
                {
                    bars = list;
                    RenderBars();
                    lastSig = sig;
                }
            }
            else if (form.Visible)
            {
                form.Hide();
                lastSig = "";
            }
        }

        void TrailTick()
        {
            if (trailAnimating && form.Visible && gameFocused && !userHidden && bars.Count > 0)
            {
                RenderBars(true);
                return;
            }
            trailAnimating = false;
            clock.Running = false;
        }

        public void Run()
        {
            form = new OverlayForm();
            form.FormBorderStyle = FormBorderStyle.None;
            form.ShowInTaskbar = false;
            form.TopMost = true;
            form.StartPosition = FormStartPosition.Manual;
            form.Size = new Size(10, 10);
            pinForm = new OverlayForm();
            pinForm.FormBorderStyle = FormBorderStyle.None;
            pinForm.ShowInTaskbar = false;
            pinForm.TopMost = true;
            pinForm.StartPosition = FormStartPosition.Manual;
            pinForm.Size = new Size(10, 10);

            SetHotKey(opt.HotKey);

            ctx = new ApplicationContext();
            tray = new NotifyIcon();
            AppIcon.Load(root);
            tray.Icon = AppIcon.Small;
            tray.Text = "Dauntless Companion";
            using (Process self = Process.GetCurrentProcess()) myPid = self.Id;
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripItem itemSettings = menu.Items.Add("Settings...");
            itemSettings.Font = new Font(itemSettings.Font, FontStyle.Bold);
            itemSettings.Click += delegate { OpenSettings(); };
            itemToggle = menu.Items.Add("Show / hide overlay (" + hotKeyText + " in game)");
            itemInfo = menu.Items.Add("Foreground app: (none yet)");
            itemInfo.Enabled = false;
            ToolStripItem itemExit = menu.Items.Add("Exit");
            itemToggle.Click += delegate { userHidden = !userHidden; };
            itemExit.Click += delegate { ctx.ExitThread(); };
            menu.Opening += delegate
            {
                string name = lastForeground;
                if (string.IsNullOrEmpty(name)) name = "(none yet)";
                itemInfo.Text = "Foreground app: " + name;
            };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenSettings(); };
            tray.Visible = true;

            timer = new Timer();
            timer.Interval = 200;
            timer.Tick += delegate
            {
                try { MainTick(); } catch (Exception e) { LogError("main timer", e); }
                try { PinTick(); } catch (Exception e) { LogError("pinned build", e); }
            };

            clock = new FrameClock(form, delegate { try { TrailTick(); } catch (Exception e) { LogError("animation", e); } });

            keyTimer = new Timer();
            keyTimer.Interval = 40;
            keyTimer.Tick += delegate { try { KeyTick(); } catch (Exception e) { LogError("key timer", e); } };

            LoadPosition();
            LoadNames();
            LoadSettings();
            Log("Overlay started: " + Application.ExecutablePath + " (.NET " + Environment.Version + "), watching " + file);
            Log("Launch options: Position=" + opt.Position + " Width=" + opt.Width + " Height=" + opt.Height + " FromTop=" + opt.FromTop + " QuitWhenGameCloses=" + opt.QuitWhenGameCloses);
            timer.Start();
            keyTimer.Start();
            clock.Start();
            Log("Animations are using the " + (clock.HighResolutionTimer ? "high-resolution timer" : "standard timer"));
            try
            {
                Application.Run(ctx);
            }
            finally
            {
                timer.Stop();
                keyTimer.Stop();
                clock.Stop();
                tray.Visible = false;
                tray.Dispose();
                if (settingsWin != null && !settingsWin.IsDisposed)
                {
                    try { settingsWin.FlushPending(); } catch (Exception e) { LogError("saving on exit", e); }
                    settingsWin.Dispose();
                }
                form.Dispose();
                pinForm.Dispose();
                if (pinBmp != null) pinBmp.Dispose();
            }
        }

        void UpdateLayout(int n)
        {
            Rectangle screen = GameRect();
            IntPtr gw = gameHwnd;
            int baseW = cfgW;
            if (baseW <= 0) baseW = Math.Max(500, I(screen.Width * 0.35));
            lastBaseW = baseW;
            if (n < 1) n = 1;
            int w = Math.Max(200, Math.Min(I(baseW * mulW), I(screen.Width * 0.92)));
            barW = w;
            barH = I(Math.Max(12 * Dpi.K, Math.Min(150 * Dpi.K, Math.Round(cfgBarH * mulH))));
            UpdateFonts();
            sgH = I(Math.Max(3, Math.Round(barH * 0.15)));
            int extra = sgOn ? sgH : 0;
            blockH = labelH + barH + extra + capH + 1;
            double sl = Math.Min(slant * barH, w / 3.0);
            padX = I(Math.Max(3, Math.Ceiling(sl * (extra + capH) / barH) + 2));
            if (rageOn || aetherOn) padX = I(Math.Max(padX, Math.Ceiling(sl * (extra + capH) / barH) + GlowSize(barH) + 2));
            vgap = gap;
            if (vgap < 0) vgap = I(Math.Max(4, Math.Round(barH * 0.2)));
            rowMode = sideBySide && n > 1;
            if (rowMode)
            {
                int hg = gap;
                if (hg < 0) hg = I(Math.Max(16, Math.Round(barH * 0.8)));
                int room = I(screen.Width * 0.98) - 2 * padX - (n - 1) * hg;
                if (n * w > room) { w = I(Math.Max(150, Math.Floor((double)room / n))); barW = w; }
                hgap = hg;
                totalW = n * w + (n - 1) * hg + 2 * padX;
                totalH = blockH;
            }
            else
            {
                hgap = 0;
                totalW = w + 2 * padX;
                totalH = n * blockH + (n - 1) * vgap;
            }
            scrW = Math.Max(1, screen.Width);
            scrH = Math.Max(1, screen.Height);
            int baseX = screen.X + I((screen.Width - totalW) / 2.0);
            int baseY;
            if (cfgTop)
            {
                int top = cfgFromTop;
                if (top <= 0) top = I(screen.Height * 0.15);
                baseY = screen.Y + top;
            }
            else baseY = screen.Bottom - cfgFromBottom - blockH;
            int x = baseX + I(offX * scrW);
            x = Math.Max(screen.X, Math.Min(x, screen.X + screen.Width - totalW));
            offX = (double)(x - baseX) / scrW;
            int py = baseY + I(offY * scrH);
            py = Math.Max(screen.Y, Math.Min(py, screen.Y + screen.Height - blockH));
            offY = (double)(py - baseY) / scrH;
            bool up = (stackMode == 2) ? ((py + blockH / 2.0) > (screen.Y + screen.Height / 2.0)) : (stackMode == 1);
            stackUp = up && !rowMode;
            int y = py;
            if (stackUp) y = py - (totalH - blockH);
            y = Math.Max(screen.Y, Math.Min(y, screen.Y + screen.Height - totalH));
            winX = x;
            winY = y;
            if (avoidSettings) AvoidSettingsWindow(screen);
            LogOnce("area:" + screen.X + "," + screen.Y + "," + screen.Width + "," + screen.Height,
                "Laid out the bars: game area at " + screen.X + "," + screen.Y + " size " + screen.Width + "x" + screen.Height +
                " (game window found: " + (gw != IntPtr.Zero) + "); bar " + w + "x" + barH + ", window at " + x + "," + y + ", top=" + cfgTop);
        }

        void AvoidSettingsWindow(Rectangle screen)
        {
            if (!SettingsShown() || settingsWin.WindowState == FormWindowState.Minimized) return;
            Rectangle sw = settingsWin.Bounds;
            if (!new Rectangle(winX, winY, totalW, totalH).IntersectsWith(sw)) return;
            int gapPx = I(10 * Dpi.K);
            int above = sw.Top - gapPx - totalH;
            int below = sw.Bottom + gapPx;
            if (above >= screen.Top) winY = above;
            else if (below + totalH <= screen.Bottom) winY = below;
            else winY = Math.Max(screen.Top, Math.Min(above, screen.Bottom - totalH));
        }

        double Trail(string key, double frac, double startFrac, int eng)
        {
            DateTime now = DateTime.UtcNow;
            TrailState st;
            trails.TryGetValue(key, out st);
            if (st == null || (eng > 0 && st.Eng != eng))
            {
                st = new TrailState();
                st.From = frac; st.To = frac; st.Start = now; st.Eng = eng;
                if (startFrac > frac + 0.0005 && (startFrac - frac) * 100 >= trailMin)
                {
                    st.From = Math.Min(1.0, startFrac);
                    trailAnimating = true;
                }
                trails[key] = st;
                return Math.Max(st.From, frac);
            }
            double p = (now - st.Start).TotalSeconds / TrailSec;
            double pos = st.To;
            if (p < 1) { double pp = Math.Max(0.0, p); pos = st.From + (st.To - st.From) * (1 - (1 - pp) * (1 - pp)); }
            if (frac < st.To - 0.0005)
            {
                if ((st.To - frac) * 100 >= trailMin) { st.From = Math.Max(pos, frac); st.To = frac; st.Start = now; pos = st.From; }
                else if (p < 1 && st.From > st.To + 0.0005) st.To = frac;
                else { st.From = frac; st.To = frac; pos = frac; }
            }
            else if (frac > st.To + 0.0005)
            {
                st.From = frac; st.To = frac; st.Start = now; pos = frac;
            }
            if (pos > frac + 0.0005) trailAnimating = true;
            return Math.Max(pos, frac);
        }

        static Dictionary<string, int> CountIds(List<Bar> list)
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Bar b in list) { int c; d.TryGetValue(b.Id, out c); d[b.Id] = c + 1; }
            return d;
        }

        List<Bar> UpdateGhosts(List<Bar> src, bool active)
        {
            DateTime now = DateTime.UtcNow;
            src = new List<Bar>(src);
            if (!active || !deathOn)
            {
                ghosts.Clear();
            }
            else
            {
                Dictionary<string, int> oldCount = CountIds(prevSrc);
                Dictionary<string, int> newCount = CountIds(src);
                bool anyPreview = false;
                foreach (Bar b in src) if (b.Id.StartsWith("__preview", StringComparison.Ordinal)) anyPreview = true;
                if (anyPreview)
                {
                    for (int i = ghosts.Count - 1; i >= 0; i--)
                        if (ghosts[i].Bar.Id.StartsWith("__preview", StringComparison.Ordinal)) ghosts.RemoveAt(i);
                }
                foreach (string k in new List<string>(oldCount.Keys))
                {
                    int lost = oldCount[k] - Count(newCount, k);
                    if (lost <= 0) continue;
                    foreach (Bar b in prevSrc)
                    {
                        if (lost <= 0) break;
                        bool dies = b.Hp <= 0 && b.Max > 0;
                        if (string.Equals(b.Id, k, StringComparison.OrdinalIgnoreCase) && dies)
                        {
                            int slot = 0;
                            for (int j = 0; j < prevShown.Count; j++)
                                if (object.ReferenceEquals(prevShown[j], b)) { slot = j; break; }
                            Bar copy = b.Copy();
                            copy.Dying = true;
                            copy.DyingAt = now;
                            Ghost g = new Ghost(); g.Bar = copy; g.Slot = slot;
                            ghosts.Add(g);
                            lost--;
                        }
                    }
                }
            }
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if ((now - ghosts[i].Bar.DyingAt).TotalSeconds >= deathSec) ghosts.RemoveAt(i);
            prevSrc = src;
            List<Bar> list = new List<Bar>(src);
            List<Ghost> sorted = new List<Ghost>(ghosts);
            for (int i = 1; i < sorted.Count; i++)
            {
                Ghost cur = sorted[i];
                int j = i - 1;
                while (j >= 0 && sorted[j].Slot > cur.Slot) { sorted[j + 1] = sorted[j]; j--; }
                sorted[j + 1] = cur;
            }
            foreach (Ghost gh in sorted)
            {
                int at = Math.Max(0, Math.Min(gh.Slot, list.Count));
                list.Insert(at, gh.Bar);
            }
            if (ghosts.Count > 0) trailAnimating = true;
            prevShown = list;
            return new List<Bar>(list);
        }

        double GlowEdge(string key, double target, bool isRage, DateTime now, bool instant)
        {
            double offPos = isRage ? 0.0 : 1.0;
            DateTime past = now.AddDays(-1);
            GlowState st;
            if (!glowStarts.TryGetValue(key, out st))
            {
                st = new GlowState(); st.From = offPos; st.To = offPos; st.T = past;
                glowStarts[key] = st;
            }
            double T = Math.Max(0.05, glowAnimSec);
            double p = Math.Min(1.0, Math.Max(0.0, (now - st.T).TotalSeconds / T));
            double cur = st.From + (st.To - st.From) * (1 - Math.Pow(1 - p, 3));
            if (instant)
            {
                st.From = target; st.To = target; st.T = past; cur = target;
            }
            else if (Math.Abs(target - st.To) > 0.000001)
            {
                bool grow = isRage ? (target > cur) : (target < cur);
                bool anim = grow ? glowAnimOn : glowAnimOut;
                if (anim) { st.From = cur; st.To = target; st.T = now; }
                else { st.From = target; st.To = target; st.T = past; cur = target; }
            }
            bool moving = ((now - st.T).TotalSeconds < T) && (Math.Abs(st.To - st.From) > 0.000001);
            if (moving) trailAnimating = true;
            if (!moving && Math.Abs(cur - offPos) < 0.000001) glowStarts.Remove(key);
            else glowUsed.Add(key);
            return cur;
        }

        static double Ux(double u, double x, double w, double s, double far, double xL, double mid, double xR)
        {
            if (u <= 0) return x - 3 * far - s;
            if (u >= 1) return x + w + 3 * far;
            if (u <= 0.5) return xL + (mid - xL) * (u / 0.5);
            return mid + (xR - mid) * ((u - 0.5) / 0.5);
        }

        GraphicsPath SweepBand(float x, float y, float w, float h, float s, float gh, int gsz, double u0, double u1)
        {
            double far = 4 * gsz + s;
            double yb = y + h + gh;
            double xL = x - far - s, mid = x + w / 2.0 - s / 2.0, xR = x + w + far;
            double b0 = Ux(u0, x, w, s, far, xL, mid, xR), b1 = Ux(u1, x, w, s, far, xL, mid, xR);
            double yt = y - far, ybb = yb + far;
            double k1 = (y + h - yt) * s / h, k2 = (ybb - y - h) * s / h;
            GraphicsPath band = new GraphicsPath();
            band.AddPolygon(new PointF[] { P(b0 + k1, yt), P(b1 + k1, yt), P(b1 - k2, ybb), P(b0 - k2, ybb) });
            return band;
        }

        void DrawStateGlow(Graphics g, float x, float y, float w, float h, float s, float gh, Color color, double u0, double u1)
        {
            if (u1 - u0 <= 0.001) return;
            int gsz = GlowSize(h);
            double gs = s * gh / h;
            double yb = y + h + gh;
            double far = 4 * gsz + s;
            using (GraphicsPath edge = new GraphicsPath())
            using (GraphicsPath bar = new GraphicsPath())
            using (GraphicsPath band = SweepBand(x, y, w, h, s, gh, gsz, u0, u1))
            using (Region region = new Region(new RectangleF((float)(x - far), (float)(y - far), (float)(w + 2 * far), (float)(yb - y + far))))
            {
                edge.AddLines(new PointF[] { P(x - gs, yb), P(x + s, y), P(x + w, y), P(x + w - s - gs, yb) });
                bar.AddPolygon(new PointF[] { P(x + s, y), P(x + w, y), P(x + w - s - gs, yb), P(x - gs, yb) });
                region.Exclude(bar);
                region.Intersect(band);
                Region oldClip = g.Clip;
                g.Clip = region;
                double per = 1 - Math.Pow(Math.Max(0.0, 1 - glowStrength), 1.0 / gsz);
                int a = Math.Min(255, I(Math.Max(1, Math.Round(255 * per * color.A / 255.0))));
                double fadeTop = y - far;
                RectangleF fadeRect = new RectangleF((float)(x - far - s), (float)fadeTop, (float)(w + 2 * far + 2 * s), (float)(yb - fadeTop + 1));
                LinearGradientBrush fade = new LinearGradientBrush(fadeRect, Color.FromArgb(a, color.R, color.G, color.B),
                    Color.FromArgb(0, color.R, color.G, color.B), LinearGradientMode.Vertical);
                Blend blend = new Blend(3);
                blend.Factors = new float[] { 0f, 0f, 1f };
                blend.Positions = new float[] { 0f, (float)Math.Min(0.99, Math.Max(0.01, (y - fadeTop) / (yb - fadeTop + 1))), 1f };
                fade.Blend = blend;
                try
                {
                    if (glowStyle == "line")
                    {
                        double lt = Math.Max(2.0, Math.Round(h * 0.09));
                        double ly = y - 2 - lt / 2;
                        double off = s * (2 + lt / 2) / h;
                        double lx0 = x + s + off, lx1 = x + w + off;
                        for (int k = 3; k >= 1; k--)
                        {
                            int ha = Math.Max(0, Math.Min(255, I(40 * glowStrength * color.A / 255.0)));
                            using (Pen halo = new Pen(Color.FromArgb(ha, color.R, color.G, color.B), (float)(lt + 3 * k)))
                            {
                                halo.StartCap = LineCap.Round;
                                halo.EndCap = LineCap.Round;
                                g.DrawLine(halo, (float)lx0, (float)ly, (float)lx1, (float)ly);
                            }
                        }
                        using (Pen pen = new Pen(color, (float)lt)) g.DrawLine(pen, (float)lx0, (float)ly, (float)lx1, (float)ly);
                    }
                    else
                    {
                        for (int k = gsz; k >= 1; k--)
                        {
                            using (Pen pen = new Pen(fade, (float)(2 * k)))
                            {
                                pen.LineJoin = LineJoin.Round;
                                g.DrawPath(pen, edge);
                            }
                        }
                    }
                }
                finally
                {
                    g.Clip = oldClip;
                    oldClip.Dispose();
                    fade.Dispose();
                }
            }
        }

        static void DrawPiece(Graphics g, Bitmap bmp, int dx, int dy, int sx, int sy, int sw, int sh, ImageAttributes ia)
        {
            if (sw <= 0 || sh <= 0) return;
            Rectangle dest = new Rectangle(dx + sx, dy + sy, sw, sh);
            if (ia != null) g.DrawImage(bmp, dest, sx, sy, sw, sh, GraphicsUnit.Pixel, ia);
            else g.DrawImage(bmp, dest, sx, sy, sw, sh, GraphicsUnit.Pixel);
        }

        void DrawGlowCached(string slot, Graphics g, float x, float y, float w, float h, float s, float gh, Color color, double u0, double u1)
        {
            if (u1 - u0 <= 0.001) return;
            int gsz = GlowSize(h);
            double lt = Math.Max(2.0, Math.Round(h * 0.09));
            int m = I(Math.Ceiling(Math.Max((double)gsz, lt + 9))) + 4;
            double gs = s * gh / h;
            int ix = I(Math.Floor(x - s - m)) - 2;
            int iy = I(Math.Floor(y - m)) - 2;
            float lx = x - ix, ly = y - iy;
            int bw = I(Math.Ceiling(lx + w + m)) + 4;
            int bh = I(Math.Ceiling(ly + h + gh)) + 4;
            string key = string.Format(Inv, "{0:0.00},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.00}|{6}|{7}|{8}|{9}",
                lx, ly, w, h, s, gh, color.ToArgb(), gsz, glowStrength, glowStyle);
            CachedImage c;
            glowCache.TryGetValue(slot, out c);
            if (c == null || c.Key != key)
            {
                if (c != null) c.Bmp.Dispose();
                Bitmap gb = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
                using (Graphics gg = Graphics.FromImage(gb))
                {
                    gg.Clear(Color.Transparent);
                    gg.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawStateGlow(gg, lx, ly, w, h, s, gh, color, 0.0, 1.0);
                }
                c = new CachedImage();
                c.Key = key; c.Bmp = gb;
                c.TopH = I(Math.Ceiling(ly)) + 2;
                c.LeftW = Math.Min(bw, I(Math.Ceiling(lx + s)) + 3);
                c.RightX = Math.Max(0, I(Math.Floor(lx + w - s - gs)) - 3);
                glowCache[slot] = c;
            }
            Region oldClip = null;
            GraphicsPath band = null;
            if (!(u0 <= 0 && u1 >= 1))
            {
                band = SweepBand(x, y, w, h, s, gh, gsz, u0, u1);
                oldClip = g.Clip;
                g.SetClip(band, CombineMode.Intersect);
            }
            try
            {
                Bitmap bmp = c.Bmp;
                int restH = bmp.Height - c.TopH;
                DrawPiece(g, bmp, ix, iy, 0, 0, bmp.Width, c.TopH, null);
                DrawPiece(g, bmp, ix, iy, 0, c.TopH, c.LeftW, restH, null);
                DrawPiece(g, bmp, ix, iy, c.RightX, c.TopH, bmp.Width - c.RightX, restH, null);
            }
            finally
            {
                if (oldClip != null) { g.Clip = oldClip; oldClip.Dispose(); }
                if (band != null) band.Dispose();
            }
        }

        static double Cut(double v, double unit)
        {
            return Math.Truncate(v / unit * 10.0 + (v < 0 ? -1e-9 : 1e-9)) / 10.0;
        }

        string HealthText(double v)
        {
            CultureInfo c = CultureInfo.CurrentCulture;
            if (!condensed) return v.ToString("N0", c);
            double a = Math.Abs(v);
            if (a >= 1e9) return Cut(v, 1e9).ToString("0.#", c) + "B";
            if (a >= 1e6) return Cut(v, 1e6).ToString("0.#", c) + "M";
            if (a >= 10000) return Cut(v, 1e3).ToString("0.#", c) + "K";
            return v.ToString("N0", c);
        }

        void DrawOutlined(Graphics g, string text, Font font, float x, float y, float w, float h, StringFormat fmt, Brush brush)
        {
            int[,] d = new int[,] { { -1, 0 }, { 1, 0 }, { 0, -1 }, { 0, 1 }, { 1, 1 } };
            for (int i = 0; i < 5; i++)
                g.DrawString(text, font, outlineBrush, new RectangleF(x + d[i, 0], y + d[i, 1], w, h), fmt);
            g.DrawString(text, font, brush, new RectangleF(x, y, w, h), fmt);
        }

        void DrawTextCached(string slot, Graphics g, string text, Font font, float x, float y, float w, float h, StringFormat fmt, SolidBrush brush, double alpha)
        {
            int ix = I(Math.Floor(x)) - 2, iy = I(Math.Floor(y)) - 2;
            float fx = x - ix, fy = y - iy;
            int bw = I(Math.Ceiling(w)) + 6, bh = I(Math.Ceiling(h)) + 6;
            string key = text + "|" + font.Name + "|" + font.Size.ToString(Inv) + "|" + font.Style + "|" + fmt.Alignment + fmt.LineAlignment +
                "|" + brush.Color.ToArgb() + "|" + outlineBrush.Color.ToArgb() + "|" + string.Format(Inv, "{0:0.00},{1:0.00},{2},{3}", fx, fy, w, h);
            CachedImage c;
            textCache.TryGetValue(slot, out c);
            if (c == null || c.Key != key)
            {
                if (c != null) c.Bmp.Dispose();
                Bitmap tb = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
                int cx = 0, cw = bw;
                using (Graphics tg = Graphics.FromImage(tb))
                {
                    tg.Clear(Color.Transparent);
                    tg.SmoothingMode = SmoothingMode.AntiAlias;
                    tg.TextRenderingHint = TextRenderingHint.AntiAlias;
                    DrawOutlined(tg, text, font, fx, fy, w, h, fmt, brush);
                    if (fmt.Alignment == StringAlignment.Center)
                    {
                        float tw = tg.MeasureString(text, font, new SizeF(w, h), fmt).Width;
                        cx = Math.Max(0, I(Math.Floor(fx + w / 2 - tw / 2)) - 4);
                        cw = Math.Min(bw - cx, I(Math.Ceiling(tw)) + 8);
                    }
                }
                c = new CachedImage();
                c.Key = key; c.Bmp = tb; c.Cx = cx; c.Cw = cw;
                textCache[slot] = c;
            }
            if (alpha >= 0.999)
            {
                DrawPiece(g, c.Bmp, ix, iy, c.Cx, 0, c.Cw, c.Bmp.Height, null);
            }
            else
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = (float)Math.Max(0.0, alpha);
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    DrawPiece(g, c.Bmp, ix, iy, c.Cx, 0, c.Cw, c.Bmp.Height, ia);
                }
            }
        }

        static PointF[] Slanted(double x, double y, double s, double len, double h)
        {
            return new PointF[] { P(x + s, y), P(x + s + len, y), P(x + len, y + h), P(x, y + h) };
        }

        void RenderBars() { RenderBars(false); }

        void RenderBars(bool partial)
        {
            RenderCore(partial);
            if (clock != null) clock.Running = trailAnimating;
        }

        void RenderCore(bool partial)
        {
            trailAnimating = false;
            Dictionary<string, int> trailSeen = frameTrailSeen;
            Dictionary<string, int> glowSeen = frameGlowSeen;
            trailSeen.Clear();
            glowSeen.Clear();
            glowUsed.Clear();
            trailUsed.Clear();
            DateTime gNow = DateTime.UtcNow;
            List<Bar> list = bars;
            int n = list.Count;
            int w = barW, h = barH, lh = labelH, cap = capH;
            double[] bx = new double[n], by = new double[n];
            Rectangle[] boxes = new Rectangle[n];
            int pv = ((rageOn || aetherOn) ? GlowSize(h) : 0) + 3;
            for (int i = 0; i < n; i++)
            {
                if (rowMode) { bx[i] = padX + i * (w + hgap); by[i] = 0; }
                else { bx[i] = padX; int slot = stackUp ? n - 1 - i : i; by[i] = slot * (blockH + vgap); }
                boxes[i] = Rectangle.FromLTRB(I(Math.Floor(bx[i] - padX)) - 1, I(Math.Floor(by[i])) - pv,
                    I(Math.Ceiling(bx[i] + w + padX)) + 1, I(Math.Ceiling(by[i] + blockH)) + pv);
            }
            bool part = partial && object.ReferenceEquals(list, lastAnimBars) && lastAnim.Length == n &&
                totalW == lastCanvasW && totalH == lastCanvasH && winX == lastWinX && winY == lastWinY;
            Rectangle dirty = Rectangle.Empty;
            if (part)
            {
                for (int i = 0; i < n; i++)
                    if (lastAnim[i]) dirty = dirty.IsEmpty ? boxes[i] : Rectangle.Union(dirty, boxes[i]);
                dirty.Intersect(new Rectangle(0, 0, Math.Max(1, totalW), Math.Max(1, totalH)));
                if (dirty.Width <= 0 || dirty.Height <= 0) part = false;
            }
            bool[] anim = new bool[n];
            bool anyAnim = false;
            Bitmap bmp = form.Canvas(totalW, totalH);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                if (part)
                {
                    g.SetClip(dirty);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (SolidBrush clr = new SolidBrush(Color.Transparent)) g.FillRectangle(clr, dirty);
                    g.CompositingMode = CompositingMode.SourceOver;
                }
                else g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) { anim[i - 1] = trailAnimating; anyAnim |= trailAnimating; }
                    trailAnimating = false;
                    Bar b = list[i];
                    double x0 = bx[i], top0 = by[i];
                    if (part && !boxes[i].IntersectsWith(dirty))
                    {
                        if (!b.Dying)
                        {
                            int c0; glowSeen.TryGetValue(b.Id, out c0); glowSeen[b.Id] = c0 + 1;
                            if (trailOn && trailBrush != null) { int c1; trailSeen.TryGetValue(b.Id, out c1); trailSeen[b.Id] = c1 + 1; }
                        }
                        continue;
                    }
                    double x = x0, bw = w, fade = 1.0;
                    if (b.Dying)
                    {
                        double dp = (DateTime.UtcNow - b.DyingAt).TotalSeconds / Math.Max(0.05, deathSec);
                        if (dp >= 1) continue;
                        trailAnimating = true;
                        dp = Math.Max(0.0, dp);
                        double ease = dp * dp * (3 - 2 * dp);
                        bw = w * (1 - ease);
                        x = x0 + (w - bw) / 2;
                        fade = Math.Max(0.0, 1 - dp / 0.6);
                        if (bw < 1) continue;
                    }
                    double y = top0 + lh;
                    double max = b.Max;
                    double hp = Math.Max(0.0, b.Hp);
                    double frac = 0.0;
                    if (max > 0) frac = Math.Min(1.0, hp / max);

                    double s = Math.Min(slant * h, bw / 3.0);
                    double iw = bw - s;
                    PointF[] outline = Slanted(x, y, s, bw - s, h);
                    bool rgOn = rageOn && b.Rage == 1 && hp > 0;
                    bool aeOn = aetherOn && b.Aether == 1 && hp > 0;
                    double gh0 = capH;
                    if (sgOn && b.SgMax > 0) gh0 += sgH;

                    string gk = b.Id;
                    int go;
                    glowSeen.TryGetValue(gk, out go);
                    glowSeen[gk] = go + 1;
                    gk = (b.HasEng && b.Eng > 0) ? "e" + b.Eng.ToString(Inv) : gk + "#" + go;
                    double tR = 0.0; if (rgOn) { tR = 1.0; if (aeOn) tR = 0.5; }
                    double tA = 1.0; if (aeOn) { tA = 0.0; if (rgOn) tA = 0.5; }
                    double fr = GlowEdge(gk + "|rage", tR, true, gNow, hp <= 0);
                    double fa = GlowEdge(gk + "|aether", tA, false, gNow, hp <= 0);
                    DrawGlowCached("rage#" + i, g, (float)x, (float)y, (float)bw, h, (float)s, (float)gh0, rageColor, 0.0, Math.Min(fr, fa));
                    DrawGlowCached("aether#" + i, g, (float)x, (float)y, (float)bw, h, (float)s, (float)gh0, aetherColor, fa, 1.0);

                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddPolygon(outline);
                        g.SetClip(path, CombineMode.Intersect);
                        g.FillPolygon(trackBrush, outline);
                        double fw = iw * frac;
                        if (trailOn && trailBrush != null)
                        {
                            string idk = b.Id;
                            int occ;
                            trailSeen.TryGetValue(idk, out occ);
                            trailSeen[idk] = occ + 1;
                            double sf = 0.0;
                            if (b.HasFrom && max > 0) sf = b.From / max;
                            int eng = b.HasEng ? b.Eng : 0;
                            string tkey = eng > 0 ? "e" + eng.ToString(Inv) : idk + "#" + occ;
                            trailUsed.Add(tkey);
                            double tw = iw * Trail(tkey, frac, sf, eng);
                            if (tw > fw + 0.5) g.FillPolygon(trailBrush, Slanted(x, y, s, tw, h));
                        }
                        if (fw > 0.5)
                        {
                            RectangleF rectF = new RectangleF((float)x, (float)y, (float)(fw + s + 1), h);
                            using (LinearGradientBrush grad = new LinearGradientBrush(rectF, fillTop, fillBottom, LinearGradientMode.Vertical))
                                g.FillPolygon(grad, Slanted(x, y, s, fw, h));
                        }
                        if (quarterOn)
                        {
                            foreach (double t in new double[] { 0.25, 0.5, 0.75 })
                                g.DrawLine(tickPen, (float)(x + s + iw * t), (float)(y + 1), (float)(x + iw * t), (float)(y + h - 1));
                        }
                        double sh = b.Shield, shMax = b.ShieldMax;
                        if (sh > 0 && shMax > 0)
                        {
                            double sw = iw * Math.Min(1.0, sh / shMax);
                            if (sw > 1)
                            {
                                g.FillPolygon(shieldBrush, Slanted(x, y, s, sw, h));
                                g.FillRectangle(shieldEdgeBrush, (float)(x + s), (float)y, (float)sw, 2f);
                                g.FillRectangle(shieldEdgeBrush, (float)x, (float)(y + h - 2), (float)sw, 2f);
                                g.DrawLine(shieldEdgePen, (float)(x + s + sw), (float)y, (float)(x + sw), (float)(y + h));
                            }
                        }
                        if (part) g.SetClip(dirty); else g.ResetClip();
                    }

                    double sgMax = b.SgMax;
                    bool hasSg = sgOn && sgMax > 0;
                    double gh = 0;
                    if (hasSg)
                    {
                        double sgFrac = Math.Max(0.0, Math.Min(1.0, b.SgFill));
                        double gy = y + h;
                        gh = sgH;
                        double gs = s * gh / h;
                        g.FillPolygon(sgTrackBrush, new PointF[] { P(x, gy), P(x + iw, gy), P(x + iw - gs, gy + gh), P(x - gs, gy + gh) });
                        double gfw = iw * sgFrac;
                        if (gfw > 0.5)
                        {
                            RectangleF gRect = new RectangleF((float)(x - gs - 1), (float)(gy - 1), (float)(gfw + gs + 2), (float)(gh + 2));
                            using (LinearGradientBrush gGrad = new LinearGradientBrush(gRect, sgTop, sgBottom, LinearGradientMode.Vertical))
                                g.FillPolygon(gGrad, new PointF[] { P(x, gy), P(x + gfw, gy), P(x + gfw - gs, gy + gh), P(x - gs, gy + gh) });
                        }
                        g.DrawLine(sgEdgePen, (float)(x - gs), (float)(gy + gh), (float)(x + iw - gs), (float)(gy + gh));
                    }

                    g.DrawPolygon(framePen, outline);
                    if (hasSg && divOn)
                    {
                        float dw = (float)Math.Max(2.0, Math.Round(h * 0.06));
                        if (divThick > 0) dw = (float)divThick;
                        using (Pen divPen = new Pen(divColor, dw))
                            g.DrawLine(divPen, (float)x, (float)(y + h), (float)(x + iw), (float)(y + h));
                    }
                    double dxe = s * cap / h;
                    double dxb = s * (gh + cap) / h;
                    g.DrawLine(capPen, (float)(x + s + dxe), (float)(y - cap), (float)(x - dxb), (float)(y + h + gh + cap));
                    g.DrawLine(capPen, (float)(x + bw + dxe), (float)(y - cap), (float)(x + bw - s - dxb), (float)(y + h + gh + cap));

                    string id = b.Id.ToLowerInvariant();
                    string name = b.Name;
                    string mapped = LookupName(id);
                    if (!string.IsNullOrEmpty(mapped)) name = mapped;
                    string nums;
                    if (showNums && showPct) nums = HealthText(hp) + " / " + HealthText(max) + string.Format(CultureInfo.CurrentCulture, "  ({0:P0})", frac);
                    else if (showNums) nums = HealthText(hp) + " / " + HealthText(max);
                    else if (showPct) nums = string.Format(CultureInfo.CurrentCulture, "{0:P0}", frac);
                    else nums = "";

                    if (fade > 0.02)
                    {
                        double nx = x0, nw = w;
                        if (nameFmt.Alignment != StringAlignment.Center)
                        {
                            double topSlant = Math.Min(slant * h, w / 3.0);
                            nx = x0 + topSlant + 2;
                            nw = w - topSlant - 4;
                        }
                        DrawTextCached("name#" + i, g, name.ToUpperInvariant(), fontName, (float)nx, (float)(top0 + nameDrop), (float)nw, lh - 4, nameFmt, nameBrush, fade);
                        if (nums != "") DrawTextCached("nums#" + i, g, nums, fontNums, (float)x0, (float)y, w, h, centerFmt, numBrush, fade);
                    }
                }
                if (n > 0) { anim[n - 1] = trailAnimating; anyAnim |= trailAnimating; }
                trailAnimating = anyAnim;
                if (!part)
                {
                    foreach (string k in new List<string>(glowStarts.Keys))
                        if (!glowUsed.Contains(k)) glowStarts.Remove(k);
                    foreach (string k in new List<string>(trails.Keys))
                        if (!trailUsed.Contains(k)) trails.Remove(k);
                }
            }
            lastAnim = anim; lastAnimBars = list;
            lastCanvasW = totalW; lastCanvasH = totalH; lastWinX = winX; lastWinY = winY;
            if (!(part && form.Present(winX, winY, dirty))) form.Present(winX, winY);
        }

        string Setting(string key, string def)
        {
            string v;
            if (settings.TryGetValue(key, out v)) return v;
            return def;
        }

        double Num(string key, double def, double min, double max, double? auto)
        {
            string v = Setting(key, null);
            if (v == null) return def;
            string s = v.Trim();
            if (auto.HasValue && string.Equals(s, "auto", StringComparison.OrdinalIgnoreCase)) return auto.Value;
            double d;
            if (double.TryParse(s, NumberStyles.Float, Inv, out d)) return Math.Max(min, Math.Min(max, d));
            LogOnce("badnum:" + key + "=" + s, "settings.ini: \"" + key + " = " + s + "\" isn't a number, using " + def.ToString(Inv));
            return def;
        }

        bool OnOff(string key, bool def)
        {
            string v = Setting(key, null);
            if (v == null) return def;
            string s = v.Trim().ToLowerInvariant();
            if (s == "on" || s == "true" || s == "yes" || s == "1") return true;
            if (s == "off" || s == "false" || s == "no" || s == "0") return false;
            return def;
        }

        internal static Color? ParseColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            Match m = HexColor.Match(t);
            if (m.Success)
            {
                string h = m.Groups[1].Value;
                return Color.FromArgb(255, Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
            }
            m = RgbColor.Match(t);
            if (m.Success)
            {
                int r = int.Parse(m.Groups[1].Value, Inv), g = int.Parse(m.Groups[2].Value, Inv), b = int.Parse(m.Groups[3].Value, Inv);
                if (r <= 255 && g <= 255 && b <= 255) return Color.FromArgb(255, r, g, b);
            }
            return null;
        }

        Color ColorSetting(string key, string def, Color fallback, string logKey)
        {
            string text = Setting(key, def);
            Color? c = ParseColor(text);
            if (c.HasValue) return c.Value;
            LogOnce(logKey + key + text, "settings.ini: \"" + key + " = " + text + "\" isn't a color (use #rrggbb or r, g, b), using " + def);
            return fallback;
        }

        static Dictionary<string, string> ReadSettingsFile(string path)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t == "" || t.StartsWith("#") || t.StartsWith(";") || t.StartsWith("[")) continue;
                int i = t.IndexOf('=');
                if (i < 1) continue;
                string value = t.Substring(i + 1);
                Match m = InlineComment.Match(value);
                if (m.Success) value = value.Substring(0, m.Index);
                value = value.Trim();
                if (value == "") continue;
                map[t.Substring(0, i).Trim().ToLowerInvariant()] = value;
            }
            return map;
        }

        void LoadSettings()
        {
            try
            {
                string stamp = "missing";
                if (File.Exists(settingsFile)) stamp = settingsFile + "|" + File.GetLastWriteTimeUtc(settingsFile).Ticks;
                if (settingsStamp == stamp) return;
                if (stamp == "missing")
                {
                    settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    LogOnce("nosettings", "Could not find settings.ini, using defaults");
                }
                else settings = ReadSettingsFile(settingsFile);
                settingsStamp = stamp;
                ApplySettings();
                lastSig = "";
            }
            catch (Exception e) { LogOnce("settingsRead:" + e.Message, "Something went wrong reading settings.ini: " + e.Message); }
        }

        void ApplySettings()
        {
            try
            {
                SetLogging(OnOff("overlay_log", false));
                cfgTop = Setting("position", opt.Position).Trim().ToLowerInvariant() != "bottom";
                cfgW = I(Num("bar_width", opt.Width, 0, 3000, 0) * Dpi.K);
                cfgBarH = I(Num("bar_height", opt.Height, 10, 120, null) * Dpi.K);
                barH = I(Math.Max(12 * Dpi.K, Math.Min(150 * Dpi.K, Math.Round(cfgBarH * mulH))));
                cfgFromTop = I(Num("from_top", opt.FromTop, 0, 5000, 0) * Dpi.K);
                cfgFromBottom = I(Num("from_bottom", opt.FromBottom, 0, 5000, null) * Dpi.K);
                gap = I(Num("gap", -1, -1, 200, -1));
                if (gap > 0) gap = I(gap * Dpi.K);
                slant = Num("slant", opt.Slant, 0, 1, null);
                baseName = Num("name_size", opt.NameSize, 6, 40, null);
                nameDrop = I(Num("name_offset", 0, 0, 20, null) * Dpi.K);
                baseNum = Num("number_size", opt.NumberSize, 6, 40, null);
                sgOn = OnOff("stagger_bar", true);
                barsOn = OnOff("health_bars", true);
                settingsPreviewOn = OnOff("settings_preview", true);
                showNums = OnOff("health_numbers", true);
                showPct = OnOff("health_percent", true);

                Color hc = ColorSetting("health_color", "#c42824", Color.FromArgb(255, 196, 40, 36), "badhealth:");
                fillTop = Color.FromArgb(240, hc.R, hc.G, hc.B);
                if (hc.R == 196 && hc.G == 40 && hc.B == 36) fillBottom = Color.FromArgb(240, 128, 18, 18);
                else fillBottom = Color.FromArgb(240, I(hc.R * 0.62), I(hc.G * 0.62), I(hc.B * 0.62));

                string nf = Setting("number_format", "full").Trim().ToLowerInvariant();
                if (nf != "full" && nf != "condensed")
                    LogOnce("badformat:" + nf, "settings.ini: \"number_format = " + nf + "\" should be full or condensed, Default: full");
                condensed = nf == "condensed";

                string na = Setting("name_align", "center").Trim().ToLowerInvariant();
                if (na == "left") nameFmt.Alignment = StringAlignment.Near;
                else if (na == "right") nameFmt.Alignment = StringAlignment.Far;
                else
                {
                    if (na != "center") LogOnce("badalign:" + na, "settings.ini: \"name_align = " + na + "\" should be left, center or right, Default: center");
                    nameFmt.Alignment = StringAlignment.Center;
                }
                sideBySide = OnOff("side_by_side", false);
                string su = Setting("stack_upward", "auto").Trim().ToLowerInvariant();
                if (su == "true" || su == "on" || su == "yes" || su == "1") stackMode = 1;
                else if (su == "false" || su == "off" || su == "no" || su == "0") stackMode = 0;
                else
                {
                    if (su != "auto") LogOnce("badstack:" + su, "settings.ini: \"stack_upward = " + su + "\" should be auto, true or false, Default: auto");
                    stackMode = 2;
                }

                SettingsWindow.ApplyTheme(Setting("theme_color", ""));
                trailOn = OnOff("damage_trail", true);
                trailMin = Num("damage_trail_min", 7.5, 7.5, 100, null);
                Color tc = ColorSetting("damage_trail_color", "#f5c76b", Color.FromArgb(255, 245, 199, 107), "badtrail:");
                SolidBrush oldTb = trailBrush;
                trailBrush = new SolidBrush(tc);
                if (oldTb != null) oldTb.Dispose();

                deathOn = OnOff("death_shrink", true);
                deathSec = Num("death_shrink_seconds", 0.7, 0.1, 2, null);

                SolidBrush oldName = nameBrush, oldNum = numBrush;
                nameBrush = new SolidBrush(ColorSetting("name_color", "#ffffff", Color.White, "badtext:"));
                numBrush = new SolidBrush(ColorSetting("number_color", "#ffffff", Color.White, "badtext:"));
                if (oldName != null) oldName.Dispose();
                if (oldNum != null) oldNum.Dispose();

                Color sc = ColorSetting("stagger_color", "#42eff5", Color.FromArgb(255, 66, 239, 245), "badcolor:");
                sgTop = sc;
                sgBottom = sc;

                quarterOn = OnOff("quarter_marks", true);
                rageOn = OnOff("enrage_glow", true);
                glowSize = I(Num("glow_size", -1, 2, 40, -1));
                if (glowSize > 0) glowSize = I(glowSize * Dpi.K);
                glowStrength = Num("glow_strength", 0.9, 0.1, 1, null);
                glowAnimOn = OnOff("glow_animation", true);
                glowAnimSec = Num("glow_animation_seconds", 0.6, 0.1, 3, null);
                glowAnimOut = OnOff("glow_animation_out", true);
                string gsText = Setting("glow_style", "line").Trim().ToLowerInvariant();
                if (gsText == "glow" || gsText == "line") glowStyle = gsText;
                else
                {
                    LogOnce("badstyle:" + gsText, "settings.ini: \"glow_style = " + gsText + "\" should be glow or line, Default: line");
                    glowStyle = "line";
                }
                lingerSec = Num("dead_bar_seconds", 1.5, 0, 60, null);
                aetherOn = OnOff("aether_glow", true);
                rageColor = ColorSetting("enrage_glow_color", "#dc0000", Color.FromArgb(255, 220, 0, 0), "badglow:");
                aetherColor = ColorSetting("aether_glow_color", "#03bbda", Color.FromArgb(255, 3, 187, 218), "badglow:");
                divOn = OnOff("divider", true);
                divColor = ColorSetting("divider_color", "#06080c", Color.FromArgb(255, 6, 8, 12), "baddiv:");
                divThick = Num("divider_thickness", 1, 1, 10, -1);
                if (divThick > 0) divThick *= Dpi.K;

                string fpsText = Setting("animation_fps", "60").Trim();
                if (fpsText == "30") animFps = 30;
                else
                {
                    if (fpsText != "60") LogOnce("badfps:" + fpsText, "settings.ini: \"animation_fps = " + fpsText + "\" should be 30 or 60, Default: 60");
                    animFps = 60;
                }
                if (clock != null) clock.Fps = animFps;

                string gp = Setting("game_process", "");
                if (gp.Trim() != "")
                {
                    List<string> procs = new List<string>();
                    foreach (string p in gp.Split(',')) if (p.Trim() != "") procs.Add(p.Trim());
                    SetGameProcs(procs.ToArray());
                }
                else SetGameProcs(opt.GameProcess);
                noFocus = OnOff("show_when_not_focused", opt.NoFocusCheck);

                string hk = Setting("hide_key", opt.HotKey).Trim();
                if (!string.Equals(hk, hotKeyText, StringComparison.OrdinalIgnoreCase) || !hkSet) SetHotKey(hk);
                Dictionary<string, KeySpec> keys = new Dictionary<string, KeySpec>();
                keys["toggle"] = KeySetting("preview_mode_key", "F9");
                keys["rage"] = KeySetting("preview_enrage_key", "NumPad1");
                keys["aether"] = KeySetting("preview_aether_key", "NumPad2");
                keys["hit"] = KeySetting("preview_hit_key", "NumPad3");
                keys["kill"] = KeySetting("preview_kill_key", "NumPad4");
                pmKeys = keys;
                settingsKey = KeySetting("settings_key", "F10");
                clearKey = KeySetting("clear_key", "F4");
                mvUp = KeySetting("move_up_key", "Ctrl+Alt+Up");
                mvDown = KeySetting("move_down_key", "Ctrl+Alt+Down");
                mvLeft = KeySetting("move_left_key", "Ctrl+Alt+Left");
                mvRight = KeySetting("move_right_key", "Ctrl+Alt+Right");
                mvReset = KeySetting("reset_position_key", "Ctrl+Alt+Home");
                szUp = KeySetting("taller_key", "Ctrl+Shift+Up");
                szDown = KeySetting("shorter_key", "Ctrl+Shift+Down");
                szLeft = KeySetting("narrower_key", "Ctrl+Shift+Left");
                szRight = KeySetting("wider_key", "Ctrl+Shift+Right");
                szReset = KeySetting("reset_size_key", "Ctrl+Shift+Home");
                pinPos = Setting("pinned_build_position", "left").Trim().ToLowerInvariant();

                UpdateFonts();
                Log("Settings loaded: position top=" + cfgTop + ", width " + cfgW + ", height " + barH + ", from_top " + cfgFromTop +
                    ", fonts " + curName.ToString(Inv) + "/" + curNum.ToString(Inv) + ", stagger " + sgOn + " " +
                    string.Format("#{0:x2}{1:x2}{2:x2}", sgTop.R, sgTop.G, sgTop.B) + ", hide key " + hotKeyText + ", animation " + animFps + " fps");
            }
            catch (Exception e) { LogError("settings", e); }
        }

        void LoadPosition()
        {
            try
            {
                if (!File.Exists(posFile)) return;
                Dictionary<string, object> j = Json.Parse(File.ReadAllText(posFile)) as Dictionary<string, object>;
                if (j == null) return;
                offX = Math.Max(-1.0, Math.Min(1.0, Json.Num(j, "offX")));
                offY = Math.Max(-1.0, Math.Min(1.0, Json.Num(j, "offY")));
                double mw = Json.Num(j, "mulW"), mh = Json.Num(j, "mulH");
                if (mw != 0) mulW = Math.Max(0.3, Math.Min(3.0, mw));
                if (mh != 0) mulH = Math.Max(0.3, Math.Min(4.0, mh));
                int ww = (int)Json.Num(j, "winW"), wh = (int)Json.Num(j, "winH");
                if (ww > 0 && wh > 0) winBounds = new Rectangle((int)Json.Num(j, "winX"), (int)Json.Num(j, "winY"), ww, wh);
                winRefWorkAreaH = (int)Json.Num(j, "winRefWorkAreaH");
            }
            catch (Exception fail) { Diag.Fail("reading healthbar-position.json", fail); }
        }

        void SavePosition()
        {
            try
            {
                string text = "{\"offX\":" + Math.Round(offX, 5).ToString(Inv) +
                              ",\"offY\":" + Math.Round(offY, 5).ToString(Inv) +
                              ",\"mulW\":" + Math.Round(mulW, 4).ToString(Inv) +
                              ",\"mulH\":" + Math.Round(mulH, 4).ToString(Inv);
                if (winBounds.Width > 0 && winBounds.Height > 0)
                    text += ",\"winX\":" + winBounds.X.ToString(Inv) + ",\"winY\":" + winBounds.Y.ToString(Inv) +
                            ",\"winW\":" + winBounds.Width.ToString(Inv) + ",\"winH\":" + winBounds.Height.ToString(Inv);
                if (winBounds.Width > 0 && winBounds.Height > 0 && winRefWorkAreaH > 0)
                    text += ",\"winRefWorkAreaH\":" + winRefWorkAreaH.ToString(Inv);
                text += "}";
                SafeFile.Write(posFile, text);
            }
            catch (Exception fail) { Diag.Fail("saving healthbar-position.json", fail); }
        }

        void LoadNames()
        {
            try
            {
                if (!File.Exists(namesFile)) return;
                DateTime stamp = File.GetLastWriteTimeUtc(namesFile);
                if (namesStamp == stamp) return;
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(namesFile))
                {
                    string t = line.Trim();
                    if (t == "" || t.StartsWith("#")) continue;
                    int i = t.IndexOf('=');
                    if (i < 1) continue;
                    string value = t.Substring(i + 1);
                    int hash = value.IndexOf('#');
                    if (hash >= 0) value = value.Substring(0, hash);
                    value = value.Trim();
                    if (value == "") continue;
                    map[t.Substring(0, i).Trim().ToLowerInvariant()] = value;
                }
                names = map;
                namesStamp = stamp;
                lastSig = "";
            }
            catch (Exception fail) { Diag.Fail("reading behemoth-names.txt", fail); }
        }

        string LookupName(string id)
        {
            string v;
            if (names.TryGetValue(id, out v)) return v;
            foreach (string tok in id.Split('_'))
            {
                if (tok != "" && names.TryGetValue(tok, out v))
                {
                    if (id.EndsWith("_beta")) return "Lesser " + v;
                    if (id.EndsWith("_trials")) return v + " (Trials)";
                    return v;
                }
            }
            return null;
        }

        internal static KeySpec ParseKey(string text)
        {
            int vk = 0;
            KeySpec k = new KeySpec();
            try
            {
                string keyName = null;
                foreach (string part in text.Split('+'))
                {
                    switch (part.Trim().ToLowerInvariant())
                    {
                        case "ctrl": k.Mods.Add(0x11); break;
                        case "alt": k.Mods.Add(0x12); break;
                        case "shift": k.Mods.Add(0x10); break;
                        default: keyName = part.Trim(); break;
                    }
                }
                if (keyName != null && Regex.IsMatch(keyName, "^[0-9]$")) keyName = "D" + keyName;
                if (!string.IsNullOrEmpty(keyName) && !Regex.IsMatch(keyName, "^[0-9]+$"))
                    vk = (int)(Keys)Enum.Parse(typeof(Keys), keyName, true);
            }
            catch { vk = 0; }
            if (vk <= 0 || vk > 254) return null;
            k.Vk = vk;
            k.Text = text;
            return k;
        }

        KeySpec KeySetting(string name, string def)
        {
            string t = Setting(name, def).Trim();
            KeySpec k = ParseKey(t);
            if (k == null)
            {
                LogOnce("badkey:" + name + t, "settings.ini: \"" + name + " = " + t + "\" isn't a key, using " + def);
                k = ParseKey(def);
            }
            return k;
        }

        static bool KeyDown(KeySpec k)
        {
            if (k == null) return false;
            if (!Native.Down(k.Vk)) return false;
            bool plain = k.Mods.Count == 0;
            foreach (int m in new int[] { 0x11, 0x12, 0x10 })
            {
                if (plain && m == 0x10) continue;
                if (k.Mods.Contains(m) != Native.Down(m)) return false;
            }
            return true;
        }

        void SetHotKey(string text)
        {
            KeySpec k = ParseKey(text);
            if (k == null)
            {
                LogOnce("badkey:" + text, "settings.ini: \"hide_key = " + text + "\" isn't a key, using F8");
                k = ParseKey("F8");
            }
            hideKey = k;
            hotKeyText = k.Text;
            hkSet = true;
            if (itemToggle != null) itemToggle.Text = "Show / hide overlay (" + k.Text + " in game)";
        }

        bool SettingsShown()
        {
            return settingsWin != null && !settingsWin.IsDisposed && settingsWin.Visible;
        }

        bool SettingsPreview()
        {
            return settingsPreviewOn && SettingsShown() && settingsWin.OnBarPage;
        }

        bool SettingsInFront()
        {
            return SettingsShown() && fgPid == myPid;
        }

        void OpenSettings()
        {
            try
            {
                if (!SettingsShown() && !pmOn)
                {
                    pmRage = false; pmAether = false; pmDead = false; pmHp = new double[] { 1.0, 1.0 };
                    pmReviveAt = DateTime.MaxValue;
                }
                Rectangle previewArea = Rectangle.Empty;
                if (winBounds.Width <= 0 && !SettingsShown() && !form.Visible && !pmOn && settingsPreviewOn)
                {
                    UpdateLayout(2);
                    previewArea = new Rectangle(winX, winY, totalW, totalH);
                }
                Dpi.Scaled(delegate
                {
                    if (settingsWin == null || settingsWin.IsDisposed)
                    {
                        Rectangle tgt = winBounds.Width > 0 ? winBounds : new Rectangle(Control.MousePosition, new Size(1, 1));
                        Ui.Init(Dpi.K, Screen.FromRectangle(tgt).WorkingArea.Height);
                        settingsWin = new SettingsWindow(settingsFile, Dpi.ForBars(new Action(ReloadSettingsNow)), Dpi.ForBars(new Action(ResetPosition)), Dpi.ForBars(new Action<string>(PreviewFromSettings)));
                        settingsWin.Log = Log;
                        settingsWin.ApplySavedBounds(winBounds, winRefWorkAreaH);
                        settingsWin.BoundsChanged = delegate(Rectangle r) { winBounds = r; winRefWorkAreaH = Screen.FromRectangle(r).WorkingArea.Height; SavePosition(); };
                    }
                    if (!previewArea.IsEmpty && !settingsWin.Visible) settingsWin.FitBelow(previewArea);
                    settingsWin.BringUp();
                });
                Log("Opened the settings window");
            }
            catch (Exception e) { LogError("settings window", e); }
        }

        void PreviewAction(string what)
        {
            switch (what)
            {
                case "rage": pmRage = !pmRage; break;
                case "aether": pmAether = !pmAether; break;
                case "hit":
                    if (pmDead) { pmDead = false; pmHp = new double[] { 1.0, 1.0 }; pmReviveAt = DateTime.MaxValue; }
                    for (int ti = 0; ti < 2; ti++)
                    {
                        if (pmHp[ti] < 0.25) pmHp[ti] = 1.0;
                        else pmHp[ti] = Math.Max(0.05, pmHp[ti] - 0.15);
                    }
                    break;
                case "kill":
                case "death":
                    if (pmDead) { pmDead = false; pmHp = new double[] { 1.0, 1.0 }; pmReviveAt = DateTime.MaxValue; }
                    else
                    {
                        pmDead = true;
                        pmDeadAt = DateTime.UtcNow;
                        pmReviveAt = what == "death" ? pmDeadAt.AddSeconds(lingerSec + deathSec + 0.8) : DateTime.MaxValue;
                    }
                    break;
            }
        }

        void PreviewFromSettings(string what)
        {
            try
            {
                PreviewAction(what);
                lastSig = "";
                RedrawNow();
            }
            catch (Exception e) { LogError("settings preview", e); }
        }

        void ReloadSettingsNow()
        {
            settingsStamp = null;
            LoadSettings();
            lastSig = "";
            RedrawNow();
        }

        void ResetPosition()
        {
            offX = 0.0; offY = 0.0; mulW = 1.0; mulH = 1.0;
            posDirty = false;
            SavePosition();
            ReloadSettingsNow();
            Log("Bar position and size were reset from the settings window");
        }

        void KeyTick()
        {
            if (settingsWin != null && !settingsWin.IsDisposed && settingsWin.Capturing) return;

            bool sDown = gameFocused && KeyDown(settingsKey);
            if (sDown && !settingsKeyWas)
            {
                if (SettingsInFront()) Dpi.Scaled(delegate { settingsWin.Hide(); });
                else OpenSettings();
            }
            settingsKeyWas = sDown;

            bool cDown = gameFocused && KeyDown(clearKey);
            if (cDown && !clearKeyWas)
            {
                try { File.WriteAllText(clearFile, "clear"); Log("Clear key pressed"); }
                catch (Exception e) { LogError("clear key", e); }
                if (pinBmp != null) UnpinBuild();
            }
            clearKeyWas = cDown;

            int dx = 0, dy = 0, dw = 0, dh = 0;
            bool changed = false;
            if (gameFocused && pmOn)
            {
                if (KeyDown(szLeft)) dw -= 1;
                if (KeyDown(szRight)) dw += 1;
                if (KeyDown(szDown)) dh -= 1;
                if (KeyDown(szUp)) dh += 1;
                if (KeyDown(szReset)) { mulW = 1.0; mulH = 1.0; posDirty = true; changed = true; }
            }
            if (dw != 0 || dh != 0)
            {
                sizeTicks++;
                changed = true;
                double stepH = 0.5 + Math.Min(2.5, sizeTicks / 25.0);
                double stepW = 2 + Math.Min(8, I(sizeTicks / 10.0));
                mulH += dh * stepH / Math.Max(1, cfgBarH);
                mulW += dw * stepW / Math.Max(100, lastBaseW);
                mulH = Math.Max(12.0 * Dpi.K / Math.Max(1, cfgBarH), Math.Min(150.0 * Dpi.K / Math.Max(1, cfgBarH), mulH));
                mulW = Math.Max(0.3, Math.Min(3.0, mulW));
                posDirty = true;
            }
            else sizeTicks = 0;
            if (gameFocused && pmOn)
            {
                if (KeyDown(mvLeft)) dx -= 1;
                if (KeyDown(mvRight)) dx += 1;
                if (KeyDown(mvUp)) dy -= 1;
                if (KeyDown(mvDown)) dy += 1;
                if (KeyDown(mvReset)) { offX = 0.0; offY = 0.0; posDirty = true; changed = true; }
            }
            if (dx != 0 || dy != 0)
            {
                moveTicks++;
                changed = true;
                int step = 3 + Math.Min(12, I(moveTicks / 8.0));
                offX += dx * step / (double)scrW;
                offY += dy * step / (double)scrH;
                posDirty = true;
            }
            else
            {
                moveTicks = 0;
                if (posDirty && dw == 0 && dh == 0) { SavePosition(); posDirty = false; }
            }

            KeySpec toggleKey;
            pmKeys.TryGetValue("toggle", out toggleKey);
            bool endDown = gameFocused && KeyDown(toggleKey);
            if (endDown && !endWasDown)
            {
                pmOn = !pmOn;
                pmRage = false; pmAether = false; pmHp = new double[] { 1.0, 1.0 }; pmDead = false;
                Log(pmOn ? "preview mode on" : "preview mode off");
                changed = true;
            }
            endWasDown = endDown;
            if ((pmOn || SettingsPreview()) && gameFocused)
            {
                foreach (string tk in new string[] { "rage", "aether", "hit", "kill" })
                {
                    KeySpec ks;
                    pmKeys.TryGetValue(tk, out ks);
                    bool tDown = KeyDown(ks);
                    bool was;
                    pmKeyWas.TryGetValue(tk, out was);
                    if (tDown && !was)
                    {
                        PreviewAction(tk);
                        changed = true;
                    }
                    pmKeyWas[tk] = tDown;
                }
            }

            if (changed) RedrawNow();

            bool down = KeyDown(hideKey);
            if (down && !wasDown && gameFocused) userHidden = !userHidden;
            wasDown = down;
        }
    }

    class IniDoc
    {
        static readonly Regex KeyLine = new Regex("^(\\s*)([A-Za-z0-9_]+)(\\s*=\\s*)(.*)$");
        static readonly Regex InlineComment = new Regex(";|#(\\s|$)");

        public readonly string Path;
        List<string> lines = new List<string>();
        Dictionary<string, int> index;
        string nl = "\r\n";
        bool endsWithNewLine = true;

        public IniDoc(string path) { Path = path; }

        public bool Loaded { get; private set; }

        public void Load()
        {
            List<string> fresh = new List<string>();
            string newNl = "\r\n";
            bool endsNl = true;
            if (File.Exists(Path))
            {
                string text = File.ReadAllText(Path);
                newNl = text.Contains("\r\n") ? "\r\n" : "\n";
                endsNl = text.EndsWith("\n");
                string[] parts = text.Replace("\r\n", "\n").Split('\n');
                int n = parts.Length;
                if (endsNl && n > 0) n--;
                for (int i = 0; i < n; i++) fresh.Add(parts[i]);
            }
            lines = fresh;
            index = null;
            nl = newNl;
            endsWithNewLine = endsNl;
            Loaded = true;
        }

        int FindSection(string section)
        {
            for (int j = 0; j < lines.Count; j++)
            {
                string t = lines[j].Trim();
                if (t.StartsWith("[" + section + "]", StringComparison.OrdinalIgnoreCase)) return j;
            }
            return -1;
        }

        public void Save()
        {
            string text = string.Join(nl, lines.ToArray());
            if (endsWithNewLine) text += nl;
            SafeFile.Write(Path, text);
        }

        static bool IsCommentOrSection(string line)
        {
            string t = line.TrimStart();
            return t == "" || t.StartsWith(";") || t.StartsWith("#") || t.StartsWith("[");
        }

        int Find(string key)
        {
            if (index == null)
            {
                Dictionary<string, int> fresh = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < lines.Count; i++)
                {
                    if (IsCommentOrSection(lines[i])) continue;
                    Match m = KeyLine.Match(lines[i]);
                    if (m.Success && !fresh.ContainsKey(m.Groups[2].Value)) fresh[m.Groups[2].Value] = i;
                }
                index = fresh;
            }
            int at;
            return index.TryGetValue(key, out at) ? at : -1;
        }

        public string Get(string key)
        {
            int i = Find(key);
            if (i < 0) return null;
            string value = KeyLine.Match(lines[i]).Groups[4].Value;
            Match c = InlineComment.Match(value);
            if (c.Success) value = value.Substring(0, c.Index);
            value = value.Trim();
            return value == "" ? null : value;
        }

        public void Set(string key, string section, string value) { Set(key, section, null, value); }

        public void Set(string key, string section, string oldSection, string value)
        {
            int i = Find(key);
            if (i >= 0)
            {
                Match m = KeyLine.Match(lines[i]);
                string head = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value;
                string rest = m.Groups[4].Value;
                Match c = InlineComment.Match(rest);
                string line = head + value;
                if (c.Success)
                {
                    int col = head.Length + c.Index;
                    line += new string(' ', Math.Max(1, col - line.Length)) + rest.Substring(c.Index);
                }
                lines[i] = line;
                return;
            }

            string newLine = key + " = " + value;
            index = null;
            int sec = FindSection(section);
            if (sec < 0 && oldSection != null) sec = FindSection(oldSection);
            if (sec < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim() != "") lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(newLine);
                return;
            }
            int last = sec;
            for (int j = sec + 1; j < lines.Count; j++)
            {
                if (lines[j].TrimStart().StartsWith("[")) break;
                if (lines[j].Trim() != "") last = j;
            }
            lines.Insert(last + 1, newLine);
        }
    }

    class SettingDef
    {
        public string Key, Section, Group, OldSection, Label, Desc, Kind, Def;
        public bool Restart, Auto, Hidden;
        public double Min, Max, Step;
        public int Decimals;
        public string[] Values, Labels;

        public SettingDef(string section, string group, string key, string kind, string def, string label, string desc)
        {
            Section = section; Group = group; Key = key; Kind = kind; Def = def; Label = label; Desc = desc;
        }
        public SettingDef Was(string oldSection) { OldSection = oldSection; return this; }
        public SettingDef Range(double min, double max, double step, int decimals) { Min = min; Max = max; Step = step; Decimals = decimals; return this; }
        public SettingDef Choices(string[] values, string[] labels) { Values = values; Labels = labels; return this; }
        public SettingDef WithAuto() { Auto = true; return this; }
        public SettingDef NeedsRestart() { Restart = true; return this; }
        public SettingDef Hide() { Hidden = true; return this; }
    }

    class BsPerk
    {
        public string Name, Type, Desc, Rank3, Rank6;
        public string[] Ranks = new string[7];
        public string RankText(int r)
        {
            if (r >= 1 && r <= 6 && !string.IsNullOrEmpty(Ranks[r])) return Ranks[r];
            return r >= 6 ? Rank6 : Rank3;
        }
    }

    static class BsOwned
    {
        static HashSet<string> have;
        static Dictionary<string, int> qty;
        static DateTime stamp = DateTime.MinValue;
        static bool tried;

        static string statePath;

        public static string StatePath()
        {
            if (statePath != null && File.Exists(statePath)) return statePath;
            const string rel = "DauntlessForever\\versions\\1.14.7\\data\\state.json";
            List<string> roots = new List<string>();
            foreach (string v in new string[] { Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("USERPROFILE") })
            {
                if (string.IsNullOrEmpty(v)) continue;
                int k = v.IndexOf("\\DauntlessForever\\", StringComparison.OrdinalIgnoreCase);
                if (k >= 0) roots.Add(v.Substring(0, k));
            }
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Folders"))
                {
                    string v = key == null ? null : key.GetValue("Local AppData") as string;
                    if (!string.IsNullOrEmpty(v)) roots.Add(v);
                }
            }
            catch { }
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Volatile Environment"))
                {
                    string v = key == null ? null : key.GetValue("USERPROFILE") as string;
                    if (!string.IsNullOrEmpty(v)) roots.Add(Path.Combine(v, "AppData\\Local"));
                }
            }
            catch { }
            string hd = Environment.GetEnvironmentVariable("HOMEDRIVE"), hp = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrEmpty(hd) && !string.IsNullOrEmpty(hp)) roots.Add(hd + hp + "\\AppData\\Local");
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            foreach (string r in roots)
            {
                try
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    string p = Path.Combine(r, rel);
                    if (File.Exists(p)) { statePath = p; return p; }
                }
                catch { }
            }
            return null;
        }

        public static void Refresh() { Refresh(false); }

        public static void Refresh(bool force)
        {
            if (tried && !force) return;
            tried = true;
            try
            {
                string path = StatePath();
                if (path == null) { have = null; return; }
                DateTime t = File.GetLastWriteTimeUtc(path);
                if (have != null && t == stamp) return;
                string text;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true)) text = sr.ReadToEnd();
                Dictionary<string, object> root = Json.Parse(text) as Dictionary<string, object>;
                object po;
                Dictionary<string, object> players = root != null && root.TryGetValue("players", out po) ? po as Dictionary<string, object> : null;
                if (players == null || players.Count == 0) return;
                Dictionary<string, object> player = null;
                string last = Json.Text(root, "lastPlayer");
                if (last != "" && players.ContainsKey(last)) player = players[last] as Dictionary<string, object>;
                if (player == null) foreach (object v in players.Values) { player = v as Dictionary<string, object>; if (player != null) break; }
                if (player == null) return;
                object io;
                Dictionary<string, object> invs = player.TryGetValue("inventory", out io) ? io as Dictionary<string, object> : null;
                if (invs == null) return;
                string charId = "";
                object co;
                List<object> chars = player.TryGetValue("characters", out co) ? co as List<object> : null;
                if (chars != null && chars.Count > 0 && chars[0] is Dictionary<string, object>) charId = Json.Text((Dictionary<string, object>)chars[0], "id");
                Dictionary<string, object> inv = null;
                if (charId != "" && invs.ContainsKey(charId)) inv = invs[charId] as Dictionary<string, object>;
                if (inv == null) foreach (object v in invs.Values) { inv = v as Dictionary<string, object>; if (inv != null) break; }
                if (inv == null) return;
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, int> q = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                object lo;
                List<object> inst = inv.TryGetValue("instancedItems", out lo) ? lo as List<object> : null;
                if (inst != null) foreach (object x in inst)
                {
                    Dictionary<string, object> d = x as Dictionary<string, object>;
                    if (d != null) { string id = Json.Text(d, "catalogId"); if (id != "") { set.Add(id); int c; q.TryGetValue(id, out c); q[id] = c + 1; } }
                }
                List<object> stacked = inv.TryGetValue("stackedItems", out lo) ? lo as List<object> : null;
                if (stacked != null) foreach (object x in stacked)
                {
                    Dictionary<string, object> d = x as Dictionary<string, object>;
                    double n = d == null ? 0 : Json.Num(d, "quantity");
                    if (n > 0) { string id = Json.Text(d, "catalogId"); if (id != "") { set.Add(id); int c; q.TryGetValue(id, out c); q[id] = c + (int)Math.Min(n, 100000); } }
                }
                set.Add("WP_EB_TRAINING");
                set.Add("LT_BASIC");
                qty = q;
                have = set;
                stamp = t;
            }
            catch (Exception fail) { Diag.Fail("reading the game's save file for owned items", fail); }
        }

        public static bool Known { get { return have != null; } }

        public static int Count(string id)
        {
            Dictionary<string, int> d = qty;
            int c;
            if (d == null || string.IsNullOrEmpty(id) || !d.TryGetValue(id, out c)) return 0;
            return c;
        }

        public static int State(string id)
        {
            HashSet<string> h = have;
            if (h == null || string.IsNullOrEmpty(id)) return 0;
            return h.Contains(id) ? 1 : -1;
        }

        public static void OwnedFirst<T>(List<T> list, Func<T, string> id)
        {
            if (have == null || list.Count < 2) return;
            List<T> yes = new List<T>(), no = new List<T>();
            foreach (T x in list) (State(id(x)) > 0 ? yes : no).Add(x);
            if (yes.Count == 0 || no.Count == 0) return;
            list.Clear();
            list.AddRange(yes);
            list.AddRange(no);
        }
    }

    class BsWeapon
    {
        public string Type, Name, Slot1, Slot2, Bond, Element, Icon, Effect;
        public string[] Perks;
        public bool Exotic;
        public bool Legendary { get { return Bond != ""; } }
    }

    class BsArmour { public string Piece, Name, Perk, Slot, Strength, Weakness, Icon, Effect = ""; public bool Exotic; }

    class BsGear { public string Id, Name, Slot = "", Icon = "", Desc = "", Type = "", Cat = ""; }

    class BsGroup
    {
        public string Perk, Slot;
        public int PerkIdx, SlotIdx;
        public bool Exotic;
        public readonly List<BsArmour> Pieces = new List<BsArmour>();
    }

    class BsResult
    {
        public BsGroup[] Parts = new BsGroup[4];
        public int Free;
        public List<string> CellPerks = new List<string>();
        public List<string[]> CellSpots = new List<string[]>();
        public BsWeapon Weapon;
    }

    struct BsHit { public int H, T, A, L, Free; }

    static class BuildSearch
    {
        public static readonly string[] Types = new string[] { "Alacrity", "Brutality", "Finesse", "Fortitude", "Insight" };
        public static readonly string[] Pieces = new string[] { "Head", "Torso", "Arms", "Legs" };
        public static readonly string[] Elements = new string[] { "Blaze", "Frost", "Radiant", "Shock", "Terra", "Umbral" };
        public static readonly string[] WeaponTypes = new string[] { "Hammer", "Axe", "Sword", "Chain Blades", "War Pike", "Repeater", "Aether Strikers" };
        public static List<BsPerk> Perks = new List<BsPerk>();
        public static List<BsWeapon> Weapons = new List<BsWeapon>();
        public static List<BsArmour> Armours = new List<BsArmour>();
        public static string LanternSlot = "Insight";
        public static List<BsGear> Lanterns = new List<BsGear>();
        public static List<BsGear> Omnicells = new List<BsGear>();
        public static List<BsGear> Parts = new List<BsGear>();
        public static Dictionary<string, string> ShareIds = new Dictionary<string, string>();
        public static Dictionary<string, string> ShareNames = new Dictionary<string, string>();
        public static string Error;
        static string loadedPath;
        static DateTime loadedStamp;

        public static bool Load(string path)
        {
            try
            {
                if (!File.Exists(path)) { Error = "build-data.txt is missing from the data folder."; return false; }
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (path == loadedPath && stamp == loadedStamp && Perks.Count > 0) return true;
                List<BsPerk> perks = new List<BsPerk>();
                List<BsWeapon> weapons = new List<BsWeapon>();
                List<BsArmour> armours = new List<BsArmour>();
                string lantern = "Insight";
                List<BsGear> lanterns = new List<BsGear>(), omnicells = new List<BsGear>(), parts = new List<BsGear>();
                Dictionary<string, string> ids = new Dictionary<string, string>(), names = new Dictionary<string, string>();
                foreach (string raw in File.ReadAllLines(path))
                {
                    if (raw.Length < 2 || raw.StartsWith(";")) continue;
                    string[] f = raw.Split('|');
                    if (f[0] == "P" && f.Length >= 6)
                    {
                        BsPerk p = new BsPerk();
                        p.Name = f[1]; p.Type = f[2]; p.Desc = f[3];
                        if (f.Length >= 10) { for (int r = 1; r <= 6; r++) p.Ranks[r] = f[3 + r].Replace("\\n", "\n"); p.Rank3 = p.Ranks[3]; p.Rank6 = p.Ranks[6]; }
                        else { p.Rank3 = f[4].Replace("\\n", "\n"); p.Rank6 = f[5].Replace("\\n", "\n"); }
                        perks.Add(p);
                    }
                    else if (f[0] == "W" && f.Length >= 9)
                    {
                        BsWeapon w = new BsWeapon();
                        w.Type = f[1]; w.Name = f[2]; w.Slot1 = f[3]; w.Slot2 = f[4];
                        w.Perks = f[5] == "" ? new string[0] : f[5].Split(',');
                        w.Bond = f[6]; w.Element = f[7]; w.Exotic = f[8] == "1";
                        w.Icon = f.Length >= 10 ? f[9] : "";
                        w.Effect = f.Length >= 11 ? f[10].Replace("\\n", "\n") : "";
                        weapons.Add(w);
                    }
                    else if (f[0] == "A" && f.Length >= 7)
                    {
                        BsArmour a = new BsArmour();
                        a.Piece = f[1]; a.Name = f[2]; a.Perk = f[4]; a.Slot = f[5]; a.Exotic = f[6] == "1";
                        a.Strength = f.Length >= 8 ? f[7] : "";
                        a.Weakness = f.Length >= 9 ? f[8] : "";
                        a.Icon = f.Length >= 10 ? f[9] : "";
                        a.Effect = f.Length >= 11 ? f[10].Replace("\\n", "\n") : "";
                        if (!a.Name.StartsWith("Krolachi ", StringComparison.Ordinal)) armours.Add(a);
                    }
                    else if (f[0] == "L" && f.Length >= 2) lantern = f[1];
                    else if (f[0] == "LT" && f.Length >= 6)
                    {
                        BsGear g = new BsGear();
                        g.Id = f[1]; g.Name = f[2]; g.Slot = f[3]; g.Icon = f[4]; g.Desc = f[5].Replace("\\n", "\n");
                        lanterns.Add(g);
                    }
                    else if (f[0] == "O" && f.Length >= 5)
                    {
                        BsGear g = new BsGear();
                        g.Id = f[1]; g.Name = f[2]; g.Icon = f[3]; g.Desc = f[4].Replace("\\n", "\n");
                        omnicells.Add(g);
                    }
                    else if (f[0] == "M" && f.Length >= 7)
                    {
                        BsGear g = new BsGear();
                        g.Id = f[1]; g.Type = f[2]; g.Cat = f[3]; g.Name = f[4]; g.Icon = f[5]; g.Desc = f[6].Replace("\\n", "\n");
                        parts.Add(g);
                    }
                    else if (f[0] == "I" && f.Length >= 4)
                    {
                        string k = f[1] == "W" && f.Length >= 5 ? "W|" + f[3] : f[1] == "A" && f.Length >= 5 ? "A|" + f[2] + "|" + f[3] : f[1] == "C" ? "C|" + f[2] : null;
                        string id = f[f.Length - 1];
                        if (k == null || id == "") continue;
                        ids[k] = id;
                        if (!names.ContainsKey(id)) names[id] = k;
                    }
                }
                weapons.Sort(delegate(BsWeapon x, BsWeapon y) { return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); });
                BuildIndexes(perks, weapons, lanterns, omnicells, parts);
                Perks = perks; Weapons = weapons; Armours = armours; LanternSlot = lantern;
                Lanterns = lanterns; Omnicells = omnicells; Parts = parts; ShareIds = ids; ShareNames = names;
                loadedPath = path; loadedStamp = stamp; Error = null;
                return perks.Count > 0;
            }
            catch (Exception e)
            {
                Error = "Couldn't read build-data.txt: " + e.Message;
                return false;
            }
        }

        static Dictionary<string, BsPerk> perkIndex;
        static Dictionary<string, BsWeapon> weaponIndex;
        static Dictionary<string, BsGear> lanternIndex, omnicellIndex, partIndex;

        static Dictionary<string, T> IndexBy<T>(List<T> list, Func<T, string> keyOf)
        {
            Dictionary<string, T> d = new Dictionary<string, T>();
            foreach (T item in list)
            {
                string k = keyOf(item);
                if (k != null && !d.ContainsKey(k)) d[k] = item;
            }
            return d;
        }

        static void BuildIndexes(List<BsPerk> perks, List<BsWeapon> weapons, List<BsGear> lanterns, List<BsGear> omnicells, List<BsGear> parts)
        {
            perkIndex = IndexBy<BsPerk>(perks, delegate(BsPerk x) { return x.Name; });
            weaponIndex = IndexBy<BsWeapon>(weapons, delegate(BsWeapon x) { return x.Name; });
            lanternIndex = IndexBy<BsGear>(lanterns, delegate(BsGear x) { return x.Id; });
            omnicellIndex = IndexBy<BsGear>(omnicells, delegate(BsGear x) { return x.Id; });
            partIndex = IndexBy<BsGear>(parts, delegate(BsGear x) { return x.Id; });
        }

        public static BsGear FindGear(List<BsGear> list, string id)
        {
            if (string.IsNullOrEmpty(id) || list == null) return null;
            Dictionary<string, BsGear> index = object.ReferenceEquals(list, Lanterns) ? lanternIndex
                : object.ReferenceEquals(list, Omnicells) ? omnicellIndex
                : object.ReferenceEquals(list, Parts) ? partIndex : null;
            BsGear hit;
            if (index != null) return index.TryGetValue(id, out hit) ? hit : null;
            foreach (BsGear g in list) if (g.Id == id) return g;
            return null;
        }

        public static string ShareId(string key)
        {
            string id;
            return ShareIds.TryGetValue(key, out id) ? id : "";
        }

        public static bool OwnedOnly;
        public static string[] FixedArmour = new string[4];

        public static string CellId(string perk) { return string.IsNullOrEmpty(perk) ? "" : ShareId("C|" + perk); }

        public static string WeaponId(BsWeapon w) { return w == null ? "" : ShareId("W|" + w.Name); }

        public static string ArmourId(BsArmour a) { return a == null ? "" : ShareId("A|" + a.Piece + "|" + a.Name); }

        public static string ShareName(string id)
        {
            string k;
            if (string.IsNullOrEmpty(id)) return null;
            if (ShareNames.TryGetValue(id, out k)) return k;
            string baseId = Regex.Replace(id, "_(UC|R)$", "_E");
            return ShareNames.TryGetValue(baseId, out k) ? k : null;
        }

        public static BsPerk FindPerk(string name)
        {
            if (name == null) return null;
            Dictionary<string, BsPerk> index = perkIndex;
            BsPerk hit;
            if (index != null) return index.TryGetValue(name, out hit) ? hit : null;
            foreach (BsPerk p in Perks) if (p.Name == name) return p;
            return null;
        }

        public static BsWeapon FindWeapon(string name)
        {
            if (name == null) return null;
            Dictionary<string, BsWeapon> index = weaponIndex;
            BsWeapon hit;
            if (index != null) return index.TryGetValue(name, out hit) ? hit : null;
            foreach (BsWeapon w in Weapons) if (w.Name == name) return w;
            return null;
        }

        static int TypeIndex(string t)
        {
            if (t == "Prismatic") return 5;
            for (int i = 0; i < Types.Length; i++) if (Types[i] == t) return i;
            return -1;
        }

        public static List<BsResult> Search(BsWeapon weapon, BsWeapon bond, Dictionary<string, int> req, bool exoticArmour, int maxResults, bool stopAtFirst, out int total)
        {
            return Search(weapon, bond, req, exoticArmour, maxResults, stopAtFirst, null, out total);
        }

        public static List<BsResult> Search(BsWeapon weapon, BsWeapon bond, Dictionary<string, int> req, bool exoticArmour, int maxResults, bool stopAtFirst, Func<bool> stop, out int total)
        {
            total = 0;
            List<BsResult> results = new List<BsResult>();
            if (weapon == null || req.Count == 0) return results;
            bool own = OwnedOnly && BsOwned.Known;
            if (own && BsOwned.State(WeaponId(weapon)) <= 0) return results;
            if (own && bond != null && weapon.Legendary && BsOwned.State(WeaponId(bond)) <= 0) return results;

            List<string> names = new List<string>(req.Keys);
            int n = names.Count;
            int[] want = new int[n], type = new int[n], baseHave = new int[n], cap = new int[n];
            Dictionary<string, int> idx = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                idx[names[i]] = i;
                want[i] = req[names[i]];
                BsPerk p = FindPerk(names[i]);
                type[i] = p == null ? 0 : Math.Max(0, TypeIndex(p.Type));
                cap[i] = own ? BsOwned.Count(CellId(names[i])) : int.MaxValue;
            }
            List<string> extras = new List<string>(weapon.Perks);
            if (bond != null && weapon.Legendary) extras.AddRange(bond.Perks);
            foreach (string p in extras) { int k; if (idx.TryGetValue(p, out k)) baseHave[k]++; }

            int[] baseSlots = new int[6];
            List<string[]> slotList = new List<string[]>();
            foreach (string s in new string[] { weapon.Slot1, weapon.Slot2 })
            {
                int t = TypeIndex(s);
                if (t < 0) continue;
                baseSlots[t]++;
                slotList.Add(new string[] { "Weapon", s });
            }
            int lt = TypeIndex(LanternSlot);
            if (lt >= 0) { baseSlots[lt]++; slotList.Add(new string[] { "Lantern", LanternSlot }); }

            List<BsGroup>[] groups = new List<BsGroup>[4];
            for (int pc = 0; pc < 4; pc++)
            {
                groups[pc] = new List<BsGroup>();
                Dictionary<string, BsGroup> byKey = new Dictionary<string, BsGroup>();
                foreach (BsArmour a in Armours)
                {
                    if (a.Piece != Pieces[pc] || a.Name.StartsWith("Recruit's", StringComparison.Ordinal)) continue;
                    bool fixedPiece = FixedArmour != null && pc < FixedArmour.Length && !string.IsNullOrEmpty(FixedArmour[pc]);
                    if (fixedPiece && a.Name != FixedArmour[pc]) continue;
                    if (a.Exotic && !exoticArmour && !fixedPiece) continue;
                    if (own && BsOwned.State(ArmourId(a)) <= 0) continue;
                    int k;
                    string perk = idx.TryGetValue(a.Perk, out k) ? a.Perk : "";
                    string key = perk + "|" + a.Slot;
                    BsGroup g;
                    if (!byKey.TryGetValue(key, out g))
                    {
                        g = new BsGroup();
                        g.Perk = perk; g.Slot = a.Slot; g.Exotic = a.Exotic;
                        g.PerkIdx = perk == "" ? -1 : k;
                        g.SlotIdx = TypeIndex(a.Slot);
                        byKey[key] = g;
                        groups[pc].Add(g);
                    }
                    g.Pieces.Add(a);
                }
                if (groups[pc].Count == 0) return results;
                foreach (BsGroup og in groups[pc]) BsOwned.OwnedFirst(og.Pieces, ArmourId);
            }

            List<BsHit> hits = new List<BsHit>();
            int count = 0;
            int[] have = new int[n];
            int[] slots = new int[6];
            int[] need = new int[6];
            List<BsGroup> G0 = groups[0], G1 = groups[1], G2 = groups[2], G3 = groups[3];
            for (int h = 0; h < G0.Count; h++)
            for (int t = 0; t < G1.Count; t++)
            {
                if (stop != null && stop()) { total = 0; return results; }
                for (int a = 0; a < G2.Count; a++)
                for (int l = 0; l < G3.Count; l++)
                {
                    BsGroup gh = G0[h], gt = G1[t], ga = G2[a], gl = G3[l];
                    Array.Copy(baseHave, have, n);
                    Array.Copy(baseSlots, slots, 6);
                    Array.Clear(need, 0, 6);
                    if (gh.PerkIdx >= 0) have[gh.PerkIdx]++;
                    if (gt.PerkIdx >= 0) have[gt.PerkIdx]++;
                    if (ga.PerkIdx >= 0) have[ga.PerkIdx]++;
                    if (gl.PerkIdx >= 0) have[gl.PerkIdx]++;
                    if (gh.SlotIdx >= 0) slots[gh.SlotIdx]++;
                    if (gt.SlotIdx >= 0) slots[gt.SlotIdx]++;
                    if (ga.SlotIdx >= 0) slots[ga.SlotIdx]++;
                    if (gl.SlotIdx >= 0) slots[gl.SlotIdx]++;
                    int cells = 0;
                    bool lack = false;
                    for (int i = 0; i < n; i++)
                    {
                        int d = want[i] - have[i];
                        if (d > cap[i]) { lack = true; break; }
                        if (d > 0) { need[type[i]] += d; cells += d; }
                    }
                    if (lack) continue;
                    int over = 0;
                    for (int i = 0; i < 5; i++) if (need[i] > slots[i]) over += need[i] - slots[i];
                    if (over > slots[5]) continue;
                    int all = slots[0] + slots[1] + slots[2] + slots[3] + slots[4] + slots[5];
                    BsHit hit;
                    hit.H = h; hit.T = t; hit.A = a; hit.L = l; hit.Free = all - cells;
                    count++;
                    if (stopAtFirst) { total = 1; return results; }
                    if (maxResults > 0 && (hits.Count < maxResults || hit.Free > hits[hits.Count - 1].Free))
                    {
                        int at = hits.Count;
                        while (at > 0 && hits[at - 1].Free < hit.Free) at--;
                        hits.Insert(at, hit);
                        if (hits.Count > maxResults) hits.RemoveAt(hits.Count - 1);
                    }
                }
            }
            total = count;

            for (int r = 0; r < hits.Count && r < maxResults; r++)
            {
                BsHit hit = hits[r];
                BsResult res = new BsResult();
                res.Parts[0] = G0[hit.H]; res.Parts[1] = G1[hit.T]; res.Parts[2] = G2[hit.A]; res.Parts[3] = G3[hit.L];
                res.Free = hit.Free;
                Array.Copy(baseHave, have, n);
                List<string[]> spotsAt = new List<string[]>(slotList);
                for (int pc = 0; pc < 4; pc++)
                {
                    if (res.Parts[pc].PerkIdx >= 0) have[res.Parts[pc].PerkIdx]++;
                    if (res.Parts[pc].SlotIdx >= 0) spotsAt.Insert(spotsAt.Count - (lt >= 0 ? 1 : 0), new string[] { Pieces[pc], res.Parts[pc].Slot });
                }
                string[] filled = new string[spotsAt.Count];
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int d = want[i] - have[i];
                        while (d > 0)
                        {
                            int spot = -1;
                            for (int s = 0; s < spotsAt.Count && spot < 0; s++)
                            {
                                if (filled[s] != null) continue;
                                if (pass == 0 && spotsAt[s][1] == Types[type[i]]) spot = s;
                                if (pass == 1 && spotsAt[s][1] == "Prismatic") spot = s;
                            }
                            if (spot < 0) break;
                            filled[spot] = names[i];
                            have[i]++;
                            d--;
                        }
                    }
                }
                for (int s = 0; s < spotsAt.Count; s++)
                {
                    if (filled[s] != null) res.CellPerks.Add(filled[s]);
                    res.CellSpots.Add(new string[] { spotsAt[s][0], spotsAt[s][1], filled[s] ?? "" });
                }
                results.Add(res);
            }
            return results;
        }

        public static List<BsResult> SearchAny(string weaponType, Dictionary<string, int> req, bool exoticArmour, int maxResults, bool stopAtFirst, out int total)
        {
            return SearchAny(weaponType, req, exoticArmour, maxResults, stopAtFirst, null, out total);
        }

        public static List<BsResult> SearchAny(string weaponType, Dictionary<string, int> req, bool exoticArmour, int maxResults, bool stopAtFirst, Func<bool> stop, out int total)
        {
            total = 0;
            List<BsResult> results = new List<BsResult>();
            if (req.Count == 0) return results;
            List<string> keys = new List<string>();
            Dictionary<string, List<BsWeapon>> byKey = new Dictionary<string, List<BsWeapon>>();
            foreach (BsWeapon w in Weapons)
            {
                if (w.Type != weaponType || w.Name.StartsWith("Recruit's", StringComparison.Ordinal)) continue;
                if (OwnedOnly && BsOwned.Known && BsOwned.State(WeaponId(w)) <= 0) continue;
                string[] s = new string[] { w.Slot1 ?? "", w.Slot2 ?? "" };
                Array.Sort(s, StringComparer.Ordinal);
                List<string> perks = new List<string>();
                foreach (string p in w.Perks) if (req.ContainsKey(p)) perks.Add(p);
                perks.Sort(StringComparer.Ordinal);
                string key = s[0] + "|" + s[1] + "|" + string.Join(",", perks.ToArray());
                List<BsWeapon> g;
                if (!byKey.TryGetValue(key, out g)) { g = new List<BsWeapon>(); byKey[key] = g; keys.Add(key); }
                g.Add(w);
            }
            List<BsResult> all = new List<BsResult>();
            foreach (string key in keys)
            {
                List<BsWeapon> g = byKey[key];
                BsOwned.OwnedFirst(g, WeaponId);
                if (stop != null && stop()) { total = 0; return results; }
                int t;
                List<BsResult> found = Search(g[0], null, req, exoticArmour, maxResults, stopAtFirst, stop, out t);
                if (stopAtFirst && t > 0) { total = 1; return results; }
                total += t * g.Count;
                foreach (BsResult r in found) { r.Weapon = g[0]; all.Add(r); }
            }
            int[] order = new int[all.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (Comparison<int>)delegate(int x, int y)
            {
                int c = all[y].Free.CompareTo(all[x].Free);
                return c != 0 ? c : x.CompareTo(y);
            });
            for (int i = 0; i < order.Length && (maxResults <= 0 || i < maxResults); i++) results.Add(all[order[i]]);
            return results;
        }
    }

    class SettingsWindow : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly Color CBg = Color.FromArgb(10, 11, 16);
        static readonly Color CPanel = Color.FromArgb(14, 15, 22);
        static readonly Color CRow = Color.FromArgb(18, 20, 28);
        static readonly Color CBorder = Color.FromArgb(32, 35, 48);
        static readonly Color CText = Color.FromArgb(234, 236, 244);
        static readonly Color CSub = Color.FromArgb(138, 144, 164);
        static readonly Color DefGold = Color.FromArgb(253, 122, 68);
        static readonly Color DefGoldBg = Color.FromArgb(54, 31, 25);
        static readonly Color DefGold6Faint = Color.FromArgb(162, 81, 51);
        static readonly Color CRestart = Color.FromArgb(222, 170, 64);
        static readonly Color CRestartBg = Color.FromArgb(44, 37, 22);
        static Color CGold = DefGold;
        static Color CGoldBg = DefGoldBg;
        static readonly Color CBtn = Color.FromArgb(19, 21, 30);
        static readonly Color CBtnHover = Color.FromArgb(29, 32, 44);
        static Color C6Faint = DefGold6Faint;
        static readonly Color DefPill = Color.FromArgb(108, 16, 37);
        static readonly Color DefHover = Color.FromArgb(253, 184, 54);
        static Color CPill = DefPill;
        static Color CHover = DefHover;

        Color shownGold = DefGold, shownGoldBg = DefGoldBg;

        static readonly string[] OnOff = new string[] { "true", "false" };
        static readonly string[] OnOffLabels = new string[] { "On", "Off" };

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr WindowFromDC(IntPtr hdc);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")] static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetClassLongW")] static extern int SetClassLong32(IntPtr hWnd, int index, int value);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int color);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);

        public Action<string> Log;
        string lastZLog = "";

        static int Px(int v) { return Ui.Px(v); }
        static float PxF(float v) { return Ui.PxF(v); }

        static bool IsTopMost(IntPtr h)
        {
            return h != IntPtr.Zero && (GetWindowLong(h, -20) & 0x8) != 0;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            try
            {
                IntPtr me = Handle;
                const uint Flags = 0x0001 | 0x0002 | 0x0010;
                bool wasTop = IsTopMost(me);
                if (wasTop) SetWindowPos(me, new IntPtr(-2), 0, 0, 0, 0, Flags);
                IntPtr fg = GetForegroundWindow();
                uint pid = 0;
                if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out pid);
                bool fgTop = IsTopMost(fg);
                if (ownPid == 0) using (Process self = Process.GetCurrentProcess()) ownPid = self.Id;
                if (fg != IntPtr.Zero && fg != me && pid != (uint)ownPid && !fgTop)
                    SetWindowPos(me, fg, 0, 0, 0, 0, Flags);
                string z = "settings window went to the back: it was always on top " + wasTop + ", the window now in front is always on top " + fgTop;
                if (z != lastZLog && Log != null) { lastZLog = z; Log(z); }
            }
            catch { }
        }

        static int ownPid;
        Timer clickTimer;
        bool mouseWasDown;

        void CheckClickOutside()
        {
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
            bool pressed = down && !mouseWasDown;
            mouseWasDown = down;
            if (!pressed || !Visible || dialogOpen > 0 || Capturing || !CloseOnClickOutside()) return;
            if (openDrop != null && !openDrop.IsDisposed && openDrop.Visible && openDrop.Bounds.Contains(Control.MousePosition)) return;
            if (!Bounds.Contains(Control.MousePosition)) Hide();
        }

        int dialogOpen;

        bool CloseOnClickOutside()
        {
            foreach (SettingDef d in defs)
                if (d.Key == "close_on_click_outside") return Normalize(d, Value(d)) == "true";
            return true;
        }

        DialogResult Ask(string text, string caption, MessageBoxIcon icon, MessageBoxDefaultButton def)
        {
            dialogOpen++;
            try { return MessageBox.Show(this, text, caption, MessageBoxButtons.YesNo, icon, def); }
            finally { dialogOpen--; }
        }

        readonly IniDoc ini;
        readonly Action onSaved, onResetPosition;
        readonly Action<string> onPreview;
        readonly List<SettingDef> defs = Defs();
        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, Label> navGroupLabels = new Dictionary<string, Label>();
        readonly Dictionary<string, List<Control>> navGroupItems = new Dictionary<string, List<Control>>();
        readonly HashSet<string> collapsedNav = new HashSet<string>();
        string[][] navGroups;
        readonly Panel content;
        readonly FlowLayoutPanel list;
        readonly StatusLabel status;
        readonly Timer saveTimer;
        readonly Dictionary<string, KeyValuePair<SettingDef, string>> unsaved = new Dictionary<string, KeyValuePair<SettingDef, string>>();
        readonly ToolTip tips = new ToolTip();
        readonly HashSet<string> restartPending = new HashSet<string>();
        string section = "Health Bar";

        static readonly string[] BarPages = new string[] { "Health Bar", "Stagger Bar", "Enrage & Aethercharge", "Animations", "Layout" };
        public bool OnBarPage { get { return Array.IndexOf(BarPages, section) >= 0; } }

        const string SettingsPage = "Settings";
        const string BarSettings = "Health Bar Settings";
        const string OverlaySettings = "Overlay Settings";
        const string KeySettings = "Keybinds";
        static readonly string[] SettingsTabs = new string[] { BarSettings, StatsSettings, BuildSettings, OverlaySettings, KeySettings };
        static readonly string[] SettingsTabNames = new string[] { "Health Bars", "Hunt Stats", "Build Search", "Overlay", "Keybinds" };
        static bool IsSettingsTab(string sec) { return Array.IndexOf(SettingsTabs, sec) >= 0; }
        static string NavOf(string sec) { return IsSettingsTab(sec) ? SettingsPage : sec; }
        DateTime fileStamp;

        const string StatsPage = "Hunt Stats";
        Button statsNav, statsReset, tabReset, bsClearAll, bsRefresh;
        Action layoutFooter;
        string statsTab = "All";
        Timer statsTimer;
        DateTime statsStamp = DateTime.MinValue;
        Dictionary<string, Dictionary<string, double>> statsData = new Dictionary<string, Dictionary<string, double>>();
        bool dirty, loading;
        RowCanvas captureCanvas;
        SettingDef captureDef;
        RowCanvas rowCanvas;
        StatsCanvas statsCanvas;
        FindCanvas findCanvas;

        public bool Capturing { get { return captureDef != null; } }

        public SettingsWindow(string settingsFile, Action saved, Action resetPosition, Action<string> preview)
        {
            ini = new IniDoc(settingsFile);
            try { ini.Load(); } catch (Exception fail) { Diag.Fail("reading settings.ini", fail); }
            ApplyTheme(ini.Get("theme_color"));
            ApplyAccents(ini.Get("pill_color"), ini.Get("hover_color"));
            shownGold = CGold; shownGoldBg = CGoldBg;
            onSaved = saved;
            onResetPosition = resetPosition;
            onPreview = preview;

            Text = "Dauntless Companion";
            ApplyThemeIcon();
            BackColor = CBg;
            ForeColor = CText;
            Font = Ui.UiFont(9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            StyleTooltips();
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            DoubleBuffered = true;
            FitToScreen();

            Panel leftCol = new Panel();
            leftCol.Dock = DockStyle.Left;
            leftCol.Width = NavW + Px(28);
            leftCol.BackColor = CBg;
            Panel navLine = new Panel();
            navLine.Dock = DockStyle.Right;
            navLine.Width = 1;
            navLine.BackColor = CBorder;

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = Px(48);
            footer.BackColor = CBg;
            Panel footLine = new Panel();
            footLine.Dock = DockStyle.Top;
            footLine.Height = 1;
            footLine.BackColor = CBorder;
            footer.Controls.Add(footLine);
            Panel leftFoot = new Panel();
            leftFoot.Dock = DockStyle.Bottom;
            leftFoot.Height = Px(48);
            leftFoot.BackColor = CBg;
            Panel leftFootEdge = new Panel();
            leftFootEdge.Dock = DockStyle.Right;
            leftFootEdge.Width = 1;
            leftFootEdge.BackColor = CBorder;
            leftFoot.Controls.Add(leftFootEdge);
            Button settingsBtn = MakeButton("Settings", Px(100));
            settingsBtn.Click += delegate { ShowSection(SettingsPage); };
            StyleButton(settingsBtn, "gear", false);
            leftFoot.Controls.Add(settingsBtn);
            Button readme = MakeButton("README", Px(100));
            readme.Click += delegate { OpenReadme(); };
            StyleButton(readme, "book", false);
            leftFoot.Controls.Add(readme);
            Action layoutLeftFoot = delegate
            {
                int pad = Px(14), gapB = Px(8);
                int bw = Math.Max(Px(60), (leftFoot.ClientSize.Width - 1 - pad * 2 - gapB) / 2);
                settingsBtn.Width = bw;
                readme.Width = bw;
                settingsBtn.Location = new Point(pad, Px(8));
                readme.Location = new Point(settingsBtn.Right + gapB, Px(8));
            };
            leftFoot.Resize += delegate { layoutLeftFoot(); };
            statsReset = MakeButton("Reset Stats", Px(136));
            statsReset.Location = new Point(Px(18), Px(8));
            StyleButton(statsReset, "reset", true);
            statsReset.Visible = false;
            statsReset.Click += delegate { ResetStats(); };
            tips.SetToolTip(statsReset, "Clears every lifetime stat for all weapon types.");
            tabReset = MakeButton("Reset Tab", Px(130));
            tabReset.Location = new Point(Px(18), Px(8));
            StyleButton(tabReset, "reset", true);
            tabReset.Visible = false;
            tabReset.Click += delegate { ResetPage(section); };
            tips.SetToolTip(tabReset, "Puts every setting on this tab back to its default. Other tabs are kept.");
            bsClearAll = MakeButton("Clear All", Px(120));
            bsClearAll.Location = new Point(Px(18), Px(8));
            StyleButton(bsClearAll, "reset", false);
            bsClearAll.Visible = false;
            bsClearAll.Click += delegate { ClearBuildSearch(); SetStatus("Build Search cleared", false, false, true); };
            bsRefresh = MakeButton("Refresh Inventory", Px(170));
            bsRefresh.Location = new Point(bsClearAll.Right + Px(12), Px(8));
            StyleButton(bsRefresh, "sync", false);
            bsRefresh.Visible = false;
            bsRefresh.Click += delegate { BsOwned.Refresh(true); RefreshSection(); SetStatus("Inventory refreshed", false, false, true); };
            tips.SetToolTip(bsRefresh, "Re-reads your owned items from the game's save.");
            Button close = MakeButton("Close", Px(110));
            close.Font = Ui.UiFont(9.5f, FontStyle.Bold);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += delegate { Hide(); };
            StyleButton(close, "x", false);
            status = new StatusLabel();
            status.UseMnemonic = false;
            status.Font = Ui.UiFont(9f);
            status.ForeColor = CSub;
            status.BackColor = Color.Transparent;
            status.AutoSize = false;
            status.TextAlign = ContentAlignment.MiddleRight;
            status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            footer.Controls.Add(statsReset);
            footer.Controls.Add(tabReset);
            footer.Controls.Add(bsClearAll);
            footer.Controls.Add(bsRefresh);
            footer.Controls.Add(close);
            footer.Controls.Add(status);
            layoutFooter = delegate
            {
                close.Location = new Point(footer.ClientSize.Width - close.Width - Px(18), Px(8));
                int left = statsReset.Visible ? statsReset.Right + Px(12) : tabReset.Visible ? tabReset.Right + Px(12) : bsRefresh.Visible ? bsRefresh.Right + Px(12) : bsClearAll.Visible ? bsClearAll.Right + Px(12) : Px(18);
                status.Location = new Point(left, Px(8));
                status.Size = new Size(Math.Max(Px(10), close.Left - left - Px(12)), close.Height);
            };
            footer.Resize += delegate { layoutFooter(); };

            FlowLayoutPanel nav = new FlowLayoutPanel();
            nav.Dock = DockStyle.Fill;
            nav.BackColor = CBg;
            nav.FlowDirection = FlowDirection.TopDown;
            nav.WrapContents = false;
            nav.AutoScroll = true;
            nav.Padding = new Padding(Px(14), Px(14), Px(12), Px(10));
            navGroups = new string[][]
            {
                new string[] { "Health Bars", "Health Bar", "Stagger Bar", "Enrage & Aethercharge", "Animations", "Layout" },
                new string[] { "Hunt Stats", StatsPage },
                new string[] { "Build Search", BuildPage, SavedPage }
            };
            for (int gi = 0; gi < navGroups.Length; gi++)
            {
                string gname = navGroups[gi][0];
                AddNavGroupLabel(nav, gname, gi > 0);
                for (int k = 1; k < navGroups[gi].Length; k++)
                {
                    string sec = navGroups[gi][k];
                    Button b = MakeButton(NavLabel(sec), NavW);
                    b.TextAlign = ContentAlignment.MiddleLeft;
                    b.Font = Ui.UiFont(10f);
                    b.Padding = new Padding(Px(8), 0, 0, 0);
                    b.Margin = new Padding(0, 0, 0, Px(4));
                    b.Height = Px(38);
                    b.FlatAppearance.BorderSize = 0;
                    b.FlatAppearance.MouseOverBackColor = CBg;
                    b.FlatAppearance.MouseDownBackColor = CBg;
                    b.BackColor = CBg;
                    b.Click += delegate { ShowSection(sec); };
                    StyleNavButton(b, sec);
                    navButtons[sec] = b;
                    navGroupItems[gname].Add(b);
                    nav.Controls.Add(b);
                }
            }
            Panel navEnd = new Panel();
            navEnd.Size = new Size(1, Px(10));
            navEnd.Margin = new Padding(0);
            navEnd.BackColor = CBg;
            nav.Controls.Add(navEnd);
            nav.ClientSizeChanged += delegate
            {
                int w = Math.Max(Px(60), nav.ClientSize.Width - nav.Padding.Horizontal);
                foreach (Control c in nav.Controls) if (c != navEnd && c.Width != w) c.Width = w;
            };
            nav.HandleCreated += delegate { try { SetWindowTheme(nav.Handle, "DarkMode_Explorer", null); } catch { } };
            statsNav = navButtons[StatsPage];
            buildNav = navButtons[BuildPage];
            LoadNavState();
            ApplyNav();

            content = new NoJumpPanel();
            content.Dock = DockStyle.Fill;
            content.AutoScroll = true;
            content.BackColor = CBg;
            content.Padding = new Padding(Px(18), Px(14), Px(18), Px(14));
            list = new FlowLayoutPanel();
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoSize = true;
            list.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            list.Location = new Point(content.Padding.Left, content.Padding.Top);
            list.Padding = new Padding(0, 0, 0, Px(14));
            list.BackColor = CBg;
            content.Controls.Add(list);
            SizeList();
            content.SizeChanged += delegate { if (!sizing) SizeList(); };
            list.SizeChanged += delegate { if (!sizing && list.Width != lastListW) LayoutRows(); };
            content.HandleCreated += delegate { try { SetWindowTheme(content.Handle, "DarkMode_Explorer", null); } catch { } };

            leftCol.Controls.Add(nav);
            leftCol.Controls.Add(navLine);
            leftCol.Controls.Add(leftFoot);
            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(leftCol);
            BufferControl(content);
            BufferControl(footer);
            BufferControl(leftCol);
            BufferControl(nav);
            Action fitLeftCol = delegate
            {
                int lw = Math.Max(Px(170), Math.Min(NavW + Px(28), (int)(ClientSize.Width * 0.26)));
                if (leftCol.Width != lw) leftCol.Width = lw;
            };
            Resize += delegate { fitLeftCol(); };
            fitLeftCol();

            saveTimer = new Timer();
            saveTimer.Interval = 350;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };

            FormClosing += delegate(object o, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            VisibleChanged += delegate { if (!Visible) { CancelCapture(); FlushPending(); } else if (section == StatsPage) RefreshStats(false); };
            Activated += delegate { ReloadIfChangedOutside(); if (section == StatsPage) RefreshStats(false); };
            ResizeEnd += delegate { if (BoundsChanged != null && WindowState == FormWindowState.Normal) BoundsChanged(Bounds); };

            statsTimer = new Timer();
            statsTimer.Interval = 2000;
            statsTimer.Tick += delegate { if (section == StatsPage) RefreshStats(false); };

            clickTimer = new Timer();
            clickTimer.Interval = 50;
            clickTimer.Tick += delegate { CheckClickOutside(); };
            VisibleChanged += delegate
            {
                if (Visible) return;
                if (openDrop != null && !openDrop.IsDisposed) openDrop.Close();
                HideChipTip();
            };

            VisibleChanged += delegate
            {
                if (Visible) mouseWasDown = true;
                if (Visible)
                {
                    string open = OpenSection(section);
                    if (open != section) ShowSection(open);
                }
                statsTimer.Enabled = Visible;
                clickTimer.Enabled = Visible;
            };

            LoadFile();
            UpdateStatsNav();
            section = OpenSection(section);
            ShowSection(section);
        }

        void AddNavGroupLabel(FlowLayoutPanel nav, string gname, bool gapAbove)
        {
            Label groupLabel = MakeLabel(gname.ToUpperInvariant(), 10f, FontStyle.Bold, CText);
            groupLabel.AutoSize = false;
            groupLabel.Size = new Size(NavW, Px(30));
            groupLabel.Padding = new Padding(Px(8), 0, 0, 0);
            groupLabel.Margin = new Padding(0, gapAbove ? Px(16) : 0, 0, Px(6));
            groupLabel.TextAlign = ContentAlignment.MiddleLeft;
            groupLabel.Cursor = Cursors.Hand;
            groupLabel.Click += delegate { ToggleNavGroup(gname); };
            groupLabel.MouseEnter += delegate { groupLabel.ForeColor = CGold; };
            groupLabel.MouseLeave += delegate { groupLabel.ForeColor = CText; };
            groupLabel.Paint += delegate(object o, PaintEventArgs e) { PaintNavGroup(groupLabel, gname, e.Graphics); };
            navGroupLabels[gname] = groupLabel;
            navGroupItems[gname] = new List<Control>();
            nav.Controls.Add(groupLabel);
        }

        string NavStatePath()
        {
            return System.IO.Path.Combine(ModPaths.Root(), "data", "settings-nav.txt");
        }

        void LoadNavState()
        {
            try
            {
                string p = NavStatePath();
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p))
                {
                    string t = line.Trim();
                    if (navGroupLabels.ContainsKey(t)) collapsedNav.Add(t);
                }
            }
            catch (Exception fail) { Diag.Fail("reading settings-nav.txt", fail); }
        }

        void SaveNavState()
        {
            try
            {
                string p = NavStatePath();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                File.WriteAllLines(p, new List<string>(collapsedNav).ToArray());
            }
            catch (Exception fail) { Diag.Fail("saving settings-nav.txt", fail); }
        }

        void ApplyNav()
        {
            foreach (KeyValuePair<string, Label> kv in navGroupLabels)
            {
                bool c = collapsedNav.Contains(kv.Key);
                kv.Value.Text = "";
                kv.Value.Invalidate();
                foreach (Control b in navGroupItems[kv.Key]) b.Visible = !c;
            }
        }

        static string NavGroupGlyph(string g)
        {
            switch (g)
            {
                case "Health Bars": return "pulse";
                case "Hunt Stats": return "chart";
                case "Build Search": return "search";
            }
            return null;
        }

        void PaintNavGroup(Label l, string gname, Graphics g)
        {
            g.Clear(l.Parent != null ? l.Parent.BackColor : CBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fc = l.ForeColor;
            int gs = Px(18);
            int x = Px(10);
            string glyph = NavGroupGlyph(gname);
            if (glyph != null) DrawGlyph(g, glyph, new RectangleF(x, (l.Height - gs) / 2f, gs, gs), fc);
            x += gs + Px(8);
            TextFormatFlags tf = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            string txt = gname.ToUpperInvariant();
            int tw = TextRenderer.MeasureText(g, txt, l.Font, new Size(int.MaxValue, l.Height), tf).Width;
            TextRenderer.DrawText(g, txt, l.Font, new Rectangle(x, 0, l.Width - x, l.Height), fc, tf);
            string arrow = collapsedNav.Contains(gname) ? "\u25B8" : "\u25BE";
            TextRenderer.DrawText(g, arrow, l.Font, new Rectangle(x + tw + Px(6), 0, Px(16), l.Height), fc, tf);
        }

        void ToggleNavGroup(string g)
        {
            if (!collapsedNav.Remove(g)) collapsedNav.Add(g);
            ApplyNav();
            SaveNavState();
        }

        string OpenSection(string cur)
        {
            if (navGroups == null) return cur;
            int start = -1;
            for (int i = 0; i < navGroups.Length && start < 0; i++)
                if (Array.IndexOf(navGroups[i], NavOf(cur), 1) >= 1) start = i;
            if (start < 0 || !collapsedNav.Contains(navGroups[start][0])) return cur;
            for (int k = 1; k < navGroups.Length; k++)
            {
                string[] grp = navGroups[(start + k) % navGroups.Length];
                if (!collapsedNav.Contains(grp[0])) return grp[1];
            }
            return cur;
        }

        public Action<Rectangle> BoundsChanged;

        public void FitBelow(Rectangle bars)
        {
            Rectangle wa = Screen.FromRectangle(bars).WorkingArea;
            int top = Math.Max(wa.Top, bars.Bottom + Px(12));
            int h = Math.Min(Height, wa.Bottom - top);
            if (h < MinimumSize.Height) return;
            int w = Math.Min(Width, wa.Width);
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, top, w, h);
        }

        public void ApplySavedBounds(Rectangle r, int refWorkAreaH)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            int curWorkAreaH = Screen.FromRectangle(r).WorkingArea.Height;
            if (refWorkAreaH > 0 && Math.Abs(curWorkAreaH - refWorkAreaH) > refWorkAreaH * 0.05)
            {
                double s = Ui.K / Ui.KFor(Dpi.K, refWorkAreaH);
                r = new Rectangle(r.X, r.Y, (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
            }
            bool onScreen = false;
            foreach (Screen sc in Screen.AllScreens)
            {
                Rectangle both = Rectangle.Intersect(sc.WorkingArea, r);
                if (both.Width >= 200 && both.Height >= 100) onScreen = true;
            }
            if (!onScreen) return;
            StartPosition = FormStartPosition.Manual;
            Rectangle wa = Screen.FromRectangle(r).WorkingArea;
            int w = Math.Min((int)(wa.Width * 0.9), Math.Max(MinimumSize.Width, r.Width));
            int h = Math.Min((int)(wa.Height * 0.9), Math.Max(MinimumSize.Height, r.Height));
            int x = Math.Max(wa.Left, Math.Min(r.X, wa.Right - w)), y = Math.Max(wa.Top, Math.Min(r.Y, wa.Bottom - h));
            Bounds = new Rectangle(x, y, w, h);
        }

        void ResetWindowBounds()
        {
            FitToScreen();
            CenterToScreen();
            if (BoundsChanged != null) BoundsChanged(Rectangle.Empty);
            SetStatus("Window position and size reset", false);
        }

        public void BringUp()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            IntPtr me = Handle;
            IntPtr fg = GetForegroundWindow();
            if (fg == me) return;
            uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint myThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (fgThread != 0 && fgThread != myThread) attached = AttachThreadInput(myThread, fgThread, true);
                TopMost = true;
                TopMost = false;
                BringWindowToTop(me);
                SetForegroundWindow(me);
                Activate();
            }
            finally
            {
                if (attached) AttachThreadInput(myThread, fgThread, false);
            }
        }

        readonly Font tipFont = new Font("Segoe UI", Ui.Pt(9f));
        void StyleTooltips()
        {
            tips.OwnerDraw = true;
            tips.Popup += delegate(object o, PopupEventArgs e)
            {
                Size t = TextRenderer.MeasureText(tips.GetToolTip(e.AssociatedControl), tipFont);
                e.ToolTipSize = new Size(t.Width + Px(20), t.Height + Px(12));
            };
            tips.Draw += delegate(object o, DrawToolTipEventArgs e)
            {
                Rectangle r = e.Bounds;
                try
                {
                    IntPtr hdc = e.Graphics.GetHdc();
                    IntPtr tipWnd = WindowFromDC(hdc);
                    e.Graphics.ReleaseHdc(hdc);
                    if (tipWnd != IntPtr.Zero) SetWindowRgn(tipWnd, CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, Px(12), Px(12)), false);
                }
                catch { }
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush fill = new SolidBrush(CPanel)) e.Graphics.FillRectangle(fill, r);
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, r.Width - 1, r.Height - 1), Px(6)))
                using (Pen edge = new Pen(CBorder))
                    e.Graphics.DrawPath(edge, path);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, r, CText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
        }

        void FitToScreen()
        {
            Rectangle wa = Screen.FromPoint(Control.MousePosition).WorkingArea;
            int maxW = (int)(wa.Width * 0.9), maxH = (int)(wa.Height * 0.9);
            MinimumSize = new Size(Math.Min(Px(640), maxW), Math.Min(Px(420), maxH));
            Size = new Size(Math.Min(Px(1080), maxW), Math.Min(Px(820), maxH));
        }

        bool sizing;

        void FinishSizing()
        {
            if (!sizing) return;
            sizing = false;
            PerformLayout();
            SizeList();
            if (list.Width != lastListW) LayoutRows();
            Invalidate(true);
            Update();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x231) sizing = true;
            base.WndProc(ref m);
            if (m.Msg == 0x232) FinishSizing();
        }

        static void BufferControl(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(c, true, null);
            }
            catch { }
        }

        static IntPtr darkBrush;

        void SetDarkClassBrush()
        {
            try
            {
                if (darkBrush == IntPtr.Zero) darkBrush = CreateSolidBrush(CBg.R | (CBg.G << 8) | (CBg.B << 16));
                if (IntPtr.Size == 8) SetClassLongPtr64(Handle, -10, darkBrush);
                else SetClassLong32(Handle, -10, darkBrush.ToInt32());
            }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (sizing && IsHandleCreated)
            {
                SizeList();
                if (list.Width != lastListW) LayoutRows();
                Invalidate(false);
                Update();
                if (rowCanvas != null && !rowCanvas.IsDisposed) rowCanvas.Update();
            }
        }

        void Cloak(bool on)
        {
            if (!IsHandleCreated) return;
            try { int v = on ? 1 : 0; DwmSetWindowAttribute(Handle, 13, ref v, 4); } catch { }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (value && !Visible) Cloak(true);
            base.SetVisibleCore(value);
            if (!value || IsDisposed) return;
            try
            {
                if (content != null && renderHold == 0)
                {
                    LayoutRows();
                    content.PerformLayout();
                }
                Refresh();
            }
            catch (Exception fail) { Diag.Fail("showing the settings window", fail); }
            Cloak(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Cloak(true);
            try { int on = 1; if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
            try
            {
                int edge = CBorder.R | (CBorder.G << 8) | (CBorder.B << 16);
                DwmSetWindowAttribute(Handle, 34, ref edge, 4);
                int cap = CBg.R | (CBg.G << 8) | (CBg.B << 16);
                DwmSetWindowAttribute(Handle, 35, ref cap, 4);
                int capText = CText.R | (CText.G << 8) | (CText.B << 16);
                DwmSetWindowAttribute(Handle, 36, ref capText, 4);
            }
            catch { }
            SetDarkClassBrush();
        }

        static int NavW { get { return Px(204); } }

        static readonly Dictionary<Button, string> btnGlyph = new Dictionary<Button, string>();
        static readonly HashSet<Button> btnDanger = new HashSet<Button>();
        static readonly HashSet<Button> btnSkip = new HashSet<Button>();

        static int SegPad { get { return Px(4); } }
        static int SegGap { get { return Px(2); } }
        static readonly Color CTray = Color.FromArgb(8, 9, 13);

        void LoadFile()
        {
            try
            {
                ini.Load();
                fileStamp = File.Exists(ini.Path) ? File.GetLastWriteTimeUtc(ini.Path) : DateTime.MinValue;
            }
            catch (Exception e) { SetStatus("Could not read settings.ini: " + e.Message, false); }
        }

        void ReloadIfChangedOutside()
        {
            try
            {
                if (dirty || Capturing) return;
                DateTime stamp = File.Exists(ini.Path) ? File.GetLastWriteTimeUtc(ini.Path) : DateTime.MinValue;
                if (stamp == fileStamp) return;
                LoadFile();
                ApplyThemeIcon();
                RetintTheme(ini.Get("theme_color"));
                RetintAccents();
                UpdateStatsNav();
                RefreshSection();
            }
            catch (Exception fail) { Diag.Fail("reloading settings.ini after it changed outside the window", fail); }
        }

        string Value(SettingDef d)
        {
            string v = ini.Get(d.Key);
            return v ?? d.Def;
        }

        void Change(SettingDef d, string value)
        {
            if (loading) return;
            string old = Value(d);
            if (string.Equals(old, value, StringComparison.Ordinal)) return;
            ini.Set(d.Key, d.Section, d.OldSection, value);
            unsaved[d.Key] = new KeyValuePair<SettingDef, string>(d, value);
            if (d.Key == "overlay_theme")
            {
                string newTheme = (value ?? "").Trim().ToLowerInvariant(), oldTheme = (old ?? "").Trim().ToLowerInvariant();
                if (ThemePresets.ContainsKey(newTheme)) SetThemeIcon(newTheme);
                else if (ThemePresets.ContainsKey(oldTheme)) SetThemeIcon(oldTheme);
                ApplyThemePreset(value);
                ApplyThemeIcon();
            }
            else if (!applyingPreset && Array.IndexOf(ThemePresetKeys, d.Key) >= 0) SetOverlayThemeCustom();
            if (d.Key == "theme_color") RetintTheme(value);
            if (d.Key == "pill_color" || d.Key == "hover_color") RetintAccents();
            if (d.Restart) restartPending.Add(d.Label);
            if (d.Key == "hunt_stats" || d.Key == "stats_layout") UpdateStatsNav();
            if (d.Key == "build_search" || d.Key == "weapon_layout" || d.Key == "owned_only") UpdateBuildNav();
            dirty = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        bool applyingPreset;

        SettingDef FindDef(string key)
        {
            foreach (SettingDef x in defs) if (x.Key == key) return x;
            return null;
        }

        void ApplyThemePreset(string preset)
        {
            string[] colors;
            if (!ThemePresets.TryGetValue(preset, out colors)) return;
            applyingPreset = true;
            try
            {
                for (int i = 0; i < ThemePresetKeys.Length; i++)
                {
                    SettingDef cd = FindDef(ThemePresetKeys[i]);
                    if (cd != null) Change(cd, colors[i]);
                }
            }
            finally { applyingPreset = false; }
            BeginInvoke((MethodInvoker)RefreshSection);
        }

        void SetThemeIcon(string preset)
        {
            SettingDef id = FindDef("theme_icon");
            if (id != null) Change(id, preset);
        }

        string SettingText(string key)
        {
            SettingDef d = FindDef(key);
            return (ini.Get(key) ?? (d != null ? d.Def : "")).Trim().ToLowerInvariant();
        }

        void ApplyThemeIcon()
        {
            string t = SettingText("overlay_theme");
            if (!ThemePresets.ContainsKey(t)) t = SettingText("theme_icon");
            if (!ThemePresets.ContainsKey(t)) t = AppIcon.DefaultTheme;
            Icon ic = AppIcon.ForTheme(t);
            if (!object.ReferenceEquals(Icon, ic)) Icon = ic;
        }

        void SetOverlayThemeCustom()
        {
            SettingDef td = FindDef("overlay_theme");
            if (td == null || Value(td) == "custom") return;
            Change(td, "custom");
            BeginInvoke((MethodInvoker)RefreshSection);
        }

        public void FlushPending()
        {
            saveTimer.Stop();
            SaveNow();
            if (savedSaveTimer != null && savedSaveTimer.Enabled)
            {
                savedSaveTimer.Stop();
                SaveSavedBuilds();
            }
        }

        void SaveNow()
        {
            if (!dirty) return;
            try
            {
                DateTime onDisk = File.Exists(ini.Path) ? File.GetLastWriteTimeUtc(ini.Path) : DateTime.MinValue;
                if (!ini.Loaded || onDisk != fileStamp)
                {
                    ini.Load();
                    foreach (KeyValuePair<SettingDef, string> u in unsaved.Values) ini.Set(u.Key.Key, u.Key.Section, u.Key.OldSection, u.Value);
                }
                ini.Save();
                unsaved.Clear();
                dirty = false;
                fileStamp = File.GetLastWriteTimeUtc(ini.Path);
                if (restartPending.Count > 0)
                {
                    List<string> names = new List<string>(restartPending);
                    SetStatus("Saved. Restart the game for: " + string.Join(", ", names.ToArray()), true, true, true);
                }
                else SetStatus("Saved", false, false, true);
                if (onSaved != null) onSaved();
            }
            catch (Exception e) { SetStatus("Could not save settings.ini: " + e.Message, false); }
        }

        void OpenReadme()
        {
            string path = System.IO.Path.Combine(ModPaths.Root(), "README.md");
            if (!File.Exists(path)) { SetStatus("README.md isn't in the mod folder", false); return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                Process.Start(psi);
                return;
            }
            catch (Exception e)
            {
                if (Log != null) Log("Couldn't open README.md with default app (" + e.Message + "), trying Notepad instead");
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("notepad.exe", "\"" + path + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception e)
            {
                SetStatus("Could not open README.md: " + e.Message, false);
                if (Log != null) Log("Couldn't open README.md in Notepad either: " + e.Message);
            }
        }

        Timer statusTimer;

        void SetStatus(string text, bool gold, bool restart = false, bool check = false)
        {
            status.Text = text;
            status.Check = check;
            status.Tag = restart ? "restart" : null;
            status.ForeColor = restart ? CRestart : gold ? CText : CSub;
            status.Invalidate();
            if (statusTimer == null)
            {
                statusTimer = new Timer();
                statusTimer.Tick += delegate
                {
                    statusTimer.Stop();
                    status.Text = "";
                    status.Check = false;
                    status.Tag = null;
                    status.Invalidate();
                };
            }
            statusTimer.Stop();
            if (check && text != "")
            {
                statusTimer.Interval = restart ? 10000 : 4000;
                statusTimer.Start();
            }
        }

        class StatusLabel : Label
        {
            public bool Check;
            static readonly Color CheckColor = Color.FromArgb(74, 201, 122);

            protected override void OnPaint(PaintEventArgs e)
            {
                TextFormatFlags tf = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                bool mark = Check && Text != "";
                int cw = mark ? Ui.Px(20) : 0;
                Rectangle tr = new Rectangle(cw, 0, Math.Max(0, Width - cw), Height);
                TextRenderer.DrawText(e.Graphics, Text, Font, tr, ForeColor, tf);
                if (!mark) return;
                int tw = Math.Min(tr.Width, TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, Height), tf).Width);
                float sz = Ui.PxF(11f);
                float x = Width - tw - Ui.PxF(17f), y = (Height - sz) / 2f;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(CheckColor, Ui.PxF(2f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(p, new PointF[] { new PointF(x, y + sz * 0.55f), new PointF(x + sz * 0.38f, y + sz * 0.9f), new PointF(x + sz, y + sz * 0.12f) });
                }
            }
        }

        void ResetPage(string page)
        {
            string tab = SettingsTabNames[Math.Max(0, Array.IndexOf(SettingsTabs, page))];
            DialogResult r = Ask("Reset the " + tab + " settings to their defaults?\n\nSettings on the other tabs, bar position and size, Hunt Stats, and saved builds will not be affected.",
                "Reset " + tab + " Settings", MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (r != DialogResult.Yes) return;
            foreach (SettingDef d in defs) if (d.Section == page) Change(d, d.Def);
            SaveNow();
            UpdateStatsNav();
            ShowSection(section);
        }

        int keepScrollY = -1;
        int renderHold;

        void HoldRender()
        {
            if (content == null || !content.IsHandleCreated) return;
            if (renderHold++ == 0) SendMessage(content.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        }

        void ReleaseRender()
        {
            if (renderHold <= 0) return;
            if (--renderHold > 0) return;
            if (content == null || content.IsDisposed || !content.IsHandleCreated) return;
            SendMessage(content.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            RedrawWindow(content.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
        }

        void RefreshSection()
        {
            keepScrollY = content != null ? -content.AutoScrollPosition.Y : -1;
            ShowSection(section);
        }

        void ShowSection(string name)
        {
            int restoreY = keepScrollY;
            keepScrollY = -1;
            HoldRender();
            bool queued = false;
            try
            {
                queued = ShowSectionCore(name, restoreY);
            }
            finally
            {
                if (!queued) ReleaseRender();
            }
        }

        bool ShowSectionCore(string name, int restoreY)
        {
            CancelCapture();
            if (name == SettingsPage) name = SettingsTabs[0];
            section = name;
            foreach (KeyValuePair<string, Button> kv in navButtons)
            {
                kv.Value.ForeColor = CText;
                kv.Value.Invalidate();
            }
            bool onStats = name == StatsPage && StatsOn();
            if (statsReset.Visible != onStats)
            {
                statsReset.Visible = onStats;
                if (layoutFooter != null) layoutFooter();
            }
            bool onTab = IsSettingsTab(name);
            if (tabReset.Visible != onTab)
            {
                tabReset.Visible = onTab;
                if (layoutFooter != null) layoutFooter();
            }
            bool onBuild = name == BuildPage && BuildOn();
            if (bsClearAll.Visible != onBuild)
            {
                bsClearAll.Visible = onBuild;
                bsRefresh.Visible = onBuild;
                if (layoutFooter != null) layoutFooter();
            }
            loading = true;
            list.SuspendLayout();
            try
            {
                Control[] old = new Control[list.Controls.Count];
                list.Controls.CopyTo(old, 0);
                list.Controls.Clear();
                foreach (Control c in old) c.Dispose();
                statsCanvas = null;
                bsResults = null;
                findCanvas = null;
                rowCanvas = null;
                if (name == StatsPage) { if (StatsOn()) AddStatsPage(); else AddStatsIntro(); }
                if (name == BuildPage) { if (BuildOn()) AddBuildPage(); else AddBuildIntro(); }
                if (name == SavedPage) AddSavedPage();
                if (name != StatsPage && name != BuildPage && name != SavedPage)
                {
                    rowCanvas = BuildRowCanvas(name);
                    list.Controls.Add(rowCanvas);
                }
            }
            finally
            {
                list.ResumeLayout();
                loading = false;
            }
            content.AutoScrollPosition = new Point(0, Math.Max(0, restoreY));
            LayoutRows();
            content.PerformLayout();
            string shown = name;
            if (IsHandleCreated)
                BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (section != shown || IsDisposed) return;
                        LayoutRows();
                        content.PerformLayout();
                        if (restoreY > 0) content.AutoScrollPosition = new Point(0, restoreY);
                    }
                    finally
                    {
                        ReleaseRender();
                    }
                });
            return IsHandleCreated;
        }

        static string[] PreviewButtons(string page, string group)
        {
            if (page == "Animations" && group == "Damage trail") return new string[] { "Play", "hit" };
            if (page == "Animations" && group == "Death") return new string[] { "Play", "death" };
            if (page == "Animations" && group == "Glow sweep") return new string[] { "Enrage on / off", "rage", "Aethercharge on / off", "aether" };
            if (page == "Enrage & Aethercharge" && group == "Enrage") return new string[] { "Preview on / off", "rage" };
            if (page == "Enrage & Aethercharge" && group == "Aethercharge") return new string[] { "Preview on / off", "aether" };
            return null;
        }

        int lastListW = -1;

        void SizeList()
        {
            int w = Math.Max(Px(300), content.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            list.MinimumSize = new Size(w, 0);
            list.MaximumSize = new Size(w, 0);
        }

        void LayoutRows()
        {
            lastListW = list.Width;
            LayoutRowsAt(Math.Max(Px(300), list.ClientSize.Width));
            list.PerformLayout();
            int needH = list.GetPreferredSize(new Size(list.Width, 0)).Height;
            int bottom = 0;
            foreach (Control c in list.Controls) bottom = Math.Max(bottom, c.Bottom + c.Margin.Bottom);
            needH = Math.Max(needH, bottom + list.Padding.Bottom);
            if (list.Height < needH) list.Height = needH;
            content.AutoScrollMinSize = new Size(0, needH + content.Padding.Vertical);
        }

        void LayoutRowsAt(int w)
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls)
            {
                RowCanvas rc = c as RowCanvas;
                if (rc != null) { rc.LayoutAt(w); continue; }
                StatsCanvas sc = c as StatsCanvas;
                if (sc != null) { sc.LayoutAt(w); continue; }
                FindCanvas fdc = c as FindCanvas;
                if (fdc != null) { fdc.LayoutAt(w); continue; }
                if ("wrap".Equals(c.Tag)) { c.MaximumSize = new Size(Math.Max(Px(100), w - Px(4) - c.Margin.Left), 0); continue; }
                Panel row = c as Panel;
                if (row == null) continue;
                if ("flow".Equals(row.Tag))
                {
                    row.MinimumSize = new Size(w, 0);
                    row.MaximumSize = new Size(w, 0);
                    foreach (Control fc in row.Controls) if (fc is Label) fc.MaximumSize = new Size(Math.Max(Px(100), w - Px(150)), 0);
                    continue;
                }
                if ("buildresults".Equals(row.Tag))
                {
                    row.MinimumSize = new Size(w, 0);
                    row.MaximumSize = new Size(w, 0);
                    int cardW = Math.Max(Px(320), w);
                    foreach (Control card in row.Controls)
                    {
                        if (card is Label) { card.MaximumSize = new Size(Math.Max(Px(100), w - Px(4)), 0); continue; }
                        CardCanvas cc = card as CardCanvas;
                        if (cc != null) { cc.LayoutAt(cardW); continue; }
                        card.MinimumSize = new Size(w, 0);
                        card.MaximumSize = new Size(w, 0);
                    }
                    row.PerformLayout();
                    continue;
                }
            }
            list.ResumeLayout();
        }

        static string Hex(Color c) { return string.Format("#{0:x2}{1:x2}{2:x2}", c.R, c.G, c.B); }

        static readonly string[] ThemePresetValues = { "custom", "torgadoro", "malkarion", "urska", "thrax", "agarus", "chronovore" };
        static readonly string[] ThemePresetLabels = { "Custom", "Torgadoro", "Malkarion", "Urska", "Thrax", "Agarus", "Chronovore" };
        static readonly string[] ThemeIconValues = { "torgadoro", "malkarion", "urska", "thrax", "agarus", "chronovore" };
        static readonly string[] ThemeIconLabels = { "Torgadoro", "Malkarion", "Urska", "Thrax", "Agarus", "Chronovore" };
        static readonly Dictionary<string, string[]> ThemePresets = new Dictionary<string, string[]>
        {
            { "torgadoro", new[] { "#fd7a44", "#6c1025", "#fdb836" } },
            { "malkarion", new[] { "#468ebe", "#242976", "#fdfdd6" } },
            { "urska", new[] { "#0c4db8", "#15287d", "#67d2f6" } },
            { "thrax", new[] { "#6b48bd", "#96257c", "#b779b3" } },
            { "agarus", new[] { "#008040", "#d6da1f", "#fd9a31" } },
            { "chronovore", new[] { "#d583a3", "#8c76ab", "#e6b097" } },
        };
        static readonly string[] ThemePresetKeys = { "theme_color", "pill_color", "hover_color" };

        static List<SettingDef> Defs()
        {
            List<SettingDef> d = new List<SettingDef>();
            string s = "Health Bar";
            string g = "Format";
            d.Add(new SettingDef(s, g, "max_bars", "choice", "2", "Max Bar Count", "How many health bars can be present at once.")
                .Choices(new string[] { "1", "2", "3", "4" }, new string[] { "1", "2", "3", "4" }).NeedsRestart().Was("Bars"));
            d.Add(new SettingDef(s, g, "health_numbers", "choice", "true", "Health Numbers", "Show the health amount inside the health bar.").Choices(OnOff, OnOffLabels).Was("Bars"));
            d.Add(new SettingDef(s, g, "health_percent", "choice", "true", "Health Percent", "Show the health % inside the health bar.").Choices(OnOff, OnOffLabels).Was("Bars"));
            d.Add(new SettingDef(s, g, "number_format", "choice", "full", "Number Format", "Full shows the whole number (109,232). Condensed shortens it to one decimal (109.2k). Numbers under 10,000 are still shown in full.")
                .Choices(new string[] { "full", "condensed" }, new string[] { "Full", "Condensed" }).Was("Bars"));
            d.Add(new SettingDef(s, g, "name_align", "choice", "center", "Name Placement", "Behemoth's name placement above the bar.")
                .Choices(new string[] { "left", "center", "right" }, new string[] { "Left", "Center", "Right" }).Was("Bars"));
            d.Add(new SettingDef(s, g, "name_offset", "num", "0", "Name Drop", "Moves the behemoth's name down toward the bar, in pixels.").Range(0, 20, 1, 0));
            d.Add(new SettingDef(s, g, "quarter_marks", "choice", "true", "Quarter Marks", "Faint lines at each quarter of the bar.").Choices(OnOff, OnOffLabels).Was("Bars"));
            g = "Colors";
            d.Add(new SettingDef(s, g, "health_color", "color", "#c42824", "Health Color", "Color of the health bar's fill. The bottom of the bar is drawn a shade darker.").Was("Bars"));
            d.Add(new SettingDef(s, g, "name_color", "color", "#ffffff", "Name Color", "Color of the behemoth's name above the bar.").Was("Bars"));
            d.Add(new SettingDef(s, g, "number_color", "color", "#ffffff", "Number Color", "Color of the health numbers and percentage.").Was("Bars"));
            g = "Bar Despawning";
            d.Add(new SettingDef(s, g, "idle_bar_seconds", "num", "30", "Idle Bar Despawn", "How long an inactive health bar remains on screen, in seconds.").Range(3, 3600, 1, 0).NeedsRestart().Was("Bars"));
            d.Add(new SettingDef(s, g, "dead_bar_seconds", "num", "1.5", "Dead Bar Despawn", "How long a dead behemoth's health bar remains on screen, in seconds.").Range(0, 60, 0.5, 1).NeedsRestart().Was("Bars"));

            s = "Stagger Bar"; g = null;
            d.Add(new SettingDef(s, g, "stagger_bar", "choice", "true", "Stagger Bar", "A strip under the health bar that fills as you get closer to staggering the behemoth.").Choices(OnOff, OnOffLabels));
            d.Add(new SettingDef(s, g, "stagger_color", "color", "#42eff5", "Stagger Color", "The color that fills up the stagger bar."));
            d.Add(new SettingDef(s, g, "divider", "choice", "true", "Divider", "A thin line between the health bar and the stagger bar.").Choices(OnOff, OnOffLabels));
            d.Add(new SettingDef(s, g, "divider_color", "color", "#06080c", "Divider Color", "Color of the divider line."));
            d.Add(new SettingDef(s, g, "divider_thickness", "num", "1", "Divider Thickness", "Thickness of the divider, in pixels.").Range(1, 10, 1, 0).WithAuto());

            s = "Enrage & Aethercharge";
            g = "Enrage";
            d.Add(new SettingDef(s, g, "enrage_glow", "choice", "true", "Enrage Indicator", "A glow or a bar along the top of the health bar while the behemoth is enraged.").Choices(OnOff, OnOffLabels).Was("State glow"));
            d.Add(new SettingDef(s, g, "enrage_glow_color", "color", "#dc0000", "Enrage Color", "Color of the enrage bar or glow.").Was("State glow"));
            g = "Aethercharge";
            d.Add(new SettingDef(s, g, "aether_glow", "choice", "true", "Aethercharge Indicator", "A glow or a bar along the top of the health bar while the behemoth is aethercharged.").Choices(OnOff, OnOffLabels).Was("State glow"));
            d.Add(new SettingDef(s, g, "aether_glow_color", "color", "#03bbda", "Aethercharge Color", "Color of the aethercharge bar or glow.").Was("State glow"));
            g = "Look";
            d.Add(new SettingDef(s, g, "glow_style", "choice", "line", "State Indicator Style", "Change between a faint glow along the top of the health bar or a plain color line.")
                .Choices(new string[] { "line", "glow" }, new string[] { "Line", "Glow" }).Was("State glow"));
            d.Add(new SettingDef(s, g, "glow_size", "num", "auto", "Glow Size", "How far the state indicator reaches, in pixels.").Range(2, 40, 1, 0).WithAuto().Was("State glow"));
            d.Add(new SettingDef(s, g, "glow_strength", "num", "0.9", "Glow Strength", "How bright the glow is.").Range(0.1, 1, 0.05, 2).Was("State glow"));

            s = "Animations";
            g = "Smoothness";
            d.Add(new SettingDef(s, g, "animation_fps", "choice", "60", "Animation Refresh Rate", "Refresh rate of the health bar animations. 60 is smoother and uses a little more CPU while they play.")
                .Choices(new string[] { "30", "60" }, new string[] { "30 fps", "60 fps" }));
            g = "Damage trail";
            d.Add(new SettingDef(s, g, "damage_trail", "choice", "true", "Damage Trail", "Highlights the chunk of health a 'big hit' took off, then shrinks it away.").Choices(OnOff, OnOffLabels).Was("Damage trail"));
            d.Add(new SettingDef(s, g, "damage_trail_color", "color", "#f5c76b", "Trail Color", "Color of the damage trail.").Was("Damage trail"));
            d.Add(new SettingDef(s, g, "damage_trail_min", "num", "7.5", "Big Hit Size", "How much health % a single attack must do to activate the animation.").Range(7.5, 100, 0.5, 1).Was("Damage trail"));
            g = "Death";
            d.Add(new SettingDef(s, g, "death_shrink", "choice", "true", "Death Animation", "Makes the health bar of a killed behemoth shrink in from both ends.").Choices(OnOff, OnOffLabels).Was("Death animation"));
            d.Add(new SettingDef(s, g, "death_shrink_seconds", "num", "0.7", "Animation Length", "How long the death animation lasts, in seconds.").Range(0.1, 2, 0.1, 1).Was("Death animation"));
            g = "Glow sweep";
            d.Add(new SettingDef(s, g, "glow_animation", "choice", "true", "Sweep In", "Makes the state indicators gradually sweep across the bar rather than popping in.").Choices(OnOff, OnOffLabels).Was("State glow"));
            d.Add(new SettingDef(s, g, "glow_animation_out", "choice", "true", "Sweep Out", "Makes the state indicators gradually sweep away rather than despawning.").Choices(OnOff, OnOffLabels).Was("State glow"));
            d.Add(new SettingDef(s, g, "glow_animation_seconds", "num", "0.6", "Sweep Length", "How long the sweeping animation lasts, in seconds.").Range(0.1, 3, 0.1, 1).Was("State glow"));

            s = "Layout"; g = null;
            d.Add(new SettingDef(s, g, "stack_upward", "choice", "auto", "Vertical Bar Stacking", "Where a second bar goes. Auto puts it below when the bars are at the top of the screen, above when they're at the bottom.")
                .Choices(new string[] { "auto", "false", "true" }, new string[] { "Auto", "Below", "Above" }));
            d.Add(new SettingDef(s, g, "side_by_side", "choice", "false", "Horizontal Bar Stacking", "Changes the health bars to spawn side by side instead of stacking vertically.").Choices(OnOff, OnOffLabels));
            d.Add(new SettingDef(s, g, "gap", "num", "auto", "Gap", "Space between bars, in pixels.").Range(0, 200, 1, 0).WithAuto());
            d.Add(new SettingDef(s, g, "slant", "num", "0.45", "Slant", "How angled the ends of the bar are. 0 is square.").Range(0, 1, 0.05, 2));

            s = BarSettings; g = null;
            d.Add(new SettingDef(s, g, "health_bars", "choice", "true", "Health Bars", "Shows health bars for the behemoths in your hunt.").Choices(OnOff, OnOffLabels));
            d.Add(new SettingDef(s, g, "settings_preview", "choice", "true", "Health Bar Previews", "Shows test bars while this window is on one of the Health Bars pages.").Choices(OnOff, OnOffLabels));

            s = KeySettings; g = "Overlay";
            d.Add(new SettingDef(s, g, "settings_key", "key", "F10", "Open Settings", "Opens this window while you're in game."));
            d.Add(new SettingDef(s, g, "hide_key", "key", "F8", "Toggle Overlay", "Toggles health bar rendering on / off."));
            d.Add(new SettingDef(s, g, "clear_key", "key", "F4", "Clear Bars", "Clears any health bars that are active and clears pinned builds overlay."));
            g = "Preview";
            d.Add(new SettingDef(s, g, "preview_mode_key", "key", "F9", "Preview Mode", "Shows test bars you can move and resize with the keys below."));
            d.Add(new SettingDef(s, g, "preview_enrage_key", "key", "NumPad1", "Preview: Enrage", "In preview mode, turns the enrage glow on or off."));
            d.Add(new SettingDef(s, g, "preview_aether_key", "key", "NumPad2", "Preview: Aethercharge", "In preview mode, turns the aethercharge glow on or off."));
            d.Add(new SettingDef(s, g, "preview_hit_key", "key", "NumPad3", "Preview: Big Hit", "In preview mode, takes a big chunk off the test bars to display the damage trail animation."));
            d.Add(new SettingDef(s, g, "preview_kill_key", "key", "NumPad4", "Preview: Death Animation", "In preview mode, kills the test bars to display the death animation. Press again to respawn the bars."));
            g = "Move Bars";
            d.Add(new SettingDef(s, g, "move_up_key", "key", "Ctrl+Alt+Up", "Move Up", "In preview mode, moves the health bars up. Hold to move faster."));
            d.Add(new SettingDef(s, g, "move_down_key", "key", "Ctrl+Alt+Down", "Move Down", "In preview mode, moves the health bars down. Hold to move faster."));
            d.Add(new SettingDef(s, g, "move_left_key", "key", "Ctrl+Alt+Left", "Move Left", "In preview mode, moves the health bars left. Hold to move faster."));
            d.Add(new SettingDef(s, g, "move_right_key", "key", "Ctrl+Alt+Right", "Move Right", "In preview mode, moves the health bars right. Hold to move faster."));
            d.Add(new SettingDef(s, g, "reset_position_key", "key", "Ctrl+Alt+Home", "Reset Position", "In preview mode, puts the health bars back in the middle."));
            g = "Resize Bars";
            d.Add(new SettingDef(s, g, "taller_key", "key", "Ctrl+Shift+Up", "Taller", "In preview mode, makes the health bars taller. Hold to resize faster."));
            d.Add(new SettingDef(s, g, "shorter_key", "key", "Ctrl+Shift+Down", "Shorter", "In preview mode, makes the health bars shorter. Hold to resize faster."));
            d.Add(new SettingDef(s, g, "wider_key", "key", "Ctrl+Shift+Right", "Wider", "In preview mode, makes the health bars wider. Hold to resize faster."));
            d.Add(new SettingDef(s, g, "narrower_key", "key", "Ctrl+Shift+Left", "Narrower", "In preview mode, makes the health bars narrower. Hold to resize faster."));
            d.Add(new SettingDef(s, g, "reset_size_key", "key", "Ctrl+Shift+Home", "Reset Size", "In preview mode, puts the health bars back to their normal size."));

            s = OverlaySettings; g = null;
            d.Add(new SettingDef(s, g, "auto_start_overlay", "choice", "true", "Start w/ Game", "Start the overlay automatically when the game starts.").Choices(OnOff, OnOffLabels).NeedsRestart().Was("Overlay window"));
            d.Add(new SettingDef(s, g, "show_when_not_focused", "choice", "false", "Always on Top", "Renders the overlay on top of any application instead of only Dauntless.").Choices(OnOff, OnOffLabels).Was("Overlay window"));
            d.Add(new SettingDef(s, g, "overlay_theme", "dropdown", "torgadoro", "Overlay Theme", "Applies a keystone behemoth's colors to the Primary, Secondary and Tertiary colors.")
                .Choices(ThemePresetValues, ThemePresetLabels));
            d.Add(new SettingDef(s, g, "theme_color", "color", "#fd7a44", "Primary Color", "Headings, labels and highlights."));
            d.Add(new SettingDef(s, g, "pill_color", "color", "#6c1025", "Secondary Color", "Selected pills and tabs."));
            d.Add(new SettingDef(s, g, "hover_color", "color", "#fdb836", "Tertiary Color", "Outline on hovered tiles and swatches."));
            d.Add(new SettingDef(s, g, "theme_icon", "choice", "torgadoro", "Window Icon", "The window's icon. Picking an Overlay Theme sets it, and it stays when a color change switches the theme to Custom.")
                .Choices(ThemeIconValues, ThemeIconLabels).Hide());
            d.Add(new SettingDef(s, g, "heading_size", "num", "10", "Heading Font Size", "Size of the group headings on each page, such as Format and Colors.").Range(8, 16, 0.5, 1));
            d.Add(new SettingDef(s, g, "close_on_click_outside", "choice", "true", "Close on Outside Click", "Closes this window when you click anywhere outside it.").Choices(OnOff, OnOffLabels));
            d.Add(new SettingDef(s, g, "overlay_log", "choice", "false", "Overlay Log", "Writes what the overlay is doing to healthbar-overlay.log in the data folder. Only needed when troubleshooting or reporting a bug.").Choices(OnOff, OnOffLabels).Was("Overlay window"));

            s = "Hunt Stats Settings"; g = null;
            d.Add(new SettingDef(s, g, "hunt_stats", "choice", "true", "Hunt Stats", "Keeps stats for each weapon type, shown on the Lifetime Stats page.").Choices(OnOff, OnOffLabels).Was("Miscellaneous"));
            d.Add(new SettingDef(s, g, "stats_layout", "choice", "tiles", "Hunt Stats Layout", "Show the Lifetime Stats page as tiles or as a list.")
                .Choices(new string[] { "tiles", "list" }, new string[] { "Tiles", "List" }).Was("Miscellaneous"));

            s = "Build Search Settings"; g = null;
            d.Add(new SettingDef(s, g, "build_search", "choice", "true", "Build Search", "Finds armor sets for the perks you want, on the Find Builds page.").Choices(OnOff, OnOffLabels).Was("Miscellaneous"));
            d.Add(new SettingDef(s, g, "weapon_layout", "choice", "tiles", "Picker Style", "Pick weapons, lanterns, omnicells and parts from picture tiles or a plain list.")
                .Choices(new string[] { "tiles", "list" }, new string[] { "Tiles", "List" }).Was("Miscellaneous"));
            d.Add(new SettingDef(s, g, "build_count", "num", "5", "Builds Shown", "How many builds are listed at first. The Show More button under them adds this many again.").Range(1, 25, 1, 0));
            d.Add(new SettingDef(s, g, "owned_only", "choice", "false", "Owned Gear Only", "Only finds builds where you own the weapon, every armor piece and every cell, read from your save.").Choices(OnOff, OnOffLabels));
            d.Add(new SettingDef(s, g, "cell_ranks", "choice", "3_6", "Perk Ranks Shown", "Which perk ranks the perk descriptions list: only +3 and +6, or every rank from +1 to +6.")
                .Choices(new string[] { "3_6", "all" }, new string[] { "+3 and +6", "+1 to +6" }));
            d.Add(new SettingDef(s, g, "pinned_build_position", "choice", "left", "Pinned Build Position", "Where a pinned build shows on screen. The Clear Bars key removes it.")
                .Choices(new string[] { "top_left", "top_right", "left", "right", "bottom_left", "bottom_right" }, new string[] { "Top Left", "Top Right", "Left", "Right", "Bottom Left", "Bottom Right" }));
            d.Add(new SettingDef(s, g, "pinned_build_opacity", "num", "96", "Pinned Build Opacity", "How solid the pinned build's background is, in percent.").Range(0, 100, 5, 0));

            return d;
        }

        static string DefaultText(SettingDef d)
        {
            string v = d.Def ?? "";
            if (d.Values != null && d.Labels != null)
            {
                int i = Array.IndexOf(d.Values, v);
                if (i >= 0 && i < d.Labels.Length) return d.Labels[i];
            }
            if (v.Length == 0) return "None";
            if (v == "auto") return "Auto";
            if (d.Kind == "color") return v.ToUpperInvariant();
            return v;
        }

        static Color StatColor(string key)
        {
            switch (key)
            {
                case "part_damage": return Color.FromArgb(0xFF, 0xDF, 0x66);
                case "stagger_damage": return Color.FromArgb(0x73, 0xA2, 0xFF);
                case "wound_damage": return Color.FromArgb(0xE0, 0x4F, 0x56);
                case "core_damage": return Color.FromArgb(0xD6, 0xD7, 0xD6);
                default: return CText;
            }
        }

        static Color AffinityColor(string e)
        {
            switch (e)
            {
                case "Umbral": return Color.FromArgb(0x75, 0x56, 0xFB);
                case "Terra": return Color.FromArgb(0x66, 0xCC, 0x66);
                case "Blaze": return Color.FromArgb(0xFF, 0x77, 0x45);
                case "Frost": return Color.FromArgb(0x17, 0xA5, 0xF4);
                case "Shock": return Color.FromArgb(0xFF, 0xFF, 0x66);
                case "Radiant": return Color.FromArgb(0xE1, 0x7E, 0x9F);
                default: return CText;
            }
        }

        static Color AccentOr(string hex, Color def)
        {
            Color? p = string.IsNullOrEmpty(hex) ? (Color?)null : App.ParseColor(hex);
            return p.HasValue ? Color.FromArgb(255, p.Value) : def;
        }

        static bool ApplyAccents(string pill, string hover)
        {
            Color np = AccentOr(pill, DefPill), nh = AccentOr(hover, DefHover);
            bool changed = np.ToArgb() != CPill.ToArgb() || nh.ToArgb() != CHover.ToArgb();
            CPill = np; CHover = nh;
            return changed;
        }

        void RetintAccents()
        {
            if (!ApplyAccents(ini.Get("pill_color"), ini.Get("hover_color"))) return;
            if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { RefreshSection(); Invalidate(true); }));
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(255, (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static bool ApplyTheme(string hex)
        {
            Color? p = string.IsNullOrEmpty(hex) ? (Color?)null : App.ParseColor(hex);
            Color g = p.HasValue ? Color.FromArgb(255, p.Value) : DefGold;
            if (g.ToArgb() == CGold.ToArgb()) return false;
            if (g.ToArgb() == DefGold.ToArgb())
            {
                CGold = DefGold; CGoldBg = DefGoldBg; C6Faint = DefGold6Faint;
            }
            else
            {
                CGold = g;
                CGoldBg = Mix(CBg, g, 0.18);
                C6Faint = Mix(CPanel, g, 0.62);
            }
            return true;
        }

        void RetintTheme(string hex)
        {
            ApplyTheme(hex);
            if (CGold.ToArgb() == shownGold.ToArgb()) return;
            Color oldG = shownGold, oldBg = shownGoldBg;
            shownGold = CGold; shownGoldBg = CGoldBg;
            Retint(this, oldG.ToArgb(), oldBg.ToArgb());
            if (IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { RefreshSection(); Invalidate(true); }));
        }

        void Retint(Control c, int oldG, int oldBg)
        {
            if (c.Tag is string && (string)c.Tag == "restart") return;
            if (c.ForeColor.ToArgb() == oldG) c.ForeColor = CGold;
            if (c.BackColor.ToArgb() == oldBg) c.BackColor = CGoldBg;
            ButtonBase bb = c as ButtonBase;
            if (bb != null && bb.FlatStyle == FlatStyle.Flat)
            {
                if (bb.FlatAppearance.BorderColor.ToArgb() == oldG) bb.FlatAppearance.BorderColor = CGold;
                if (bb.FlatAppearance.CheckedBackColor.ToArgb() == oldBg) bb.FlatAppearance.CheckedBackColor = CGoldBg;
            }
            foreach (Control k in c.Controls) Retint(k, oldG, oldBg);
        }

        static bool IsHot(Control c)
        {
            return c.Enabled && c.ClientRectangle.Contains(c.PointToClient(Control.MousePosition));
        }

        void StyleNavButton(Button b, string sec)
        {
            btnSkip.Add(b);
            b.MouseEnter += delegate { b.Invalidate(); };
            b.MouseLeave += delegate { b.Invalidate(); };
            b.Paint += delegate(object o, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(b.Parent != null ? b.Parent.BackColor : CBg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool on = sec == NavOf(section);
                bool hot = IsHot(b);
                Rectangle r = new Rectangle(0, 0, b.Width - 1, b.Height - 1);
                if (on || hot)
                {
                    using (GraphicsPath path = RoundedRect(r, Px(8)))
                    {
                        using (SolidBrush f = new SolidBrush(on ? CGoldBg : CBtn)) g.FillPath(f, path);
                        if (on) using (Pen p = new Pen(Mix(CGoldBg, CGold, 0.55))) g.DrawPath(p, path);
                    }
                }
                Color tc = on ? CText : hot ? CText : Mix(CSub, CText, 0.55);
                TextRenderer.DrawText(g, NavLabel(sec), b.Font, new Rectangle(Px(14), 0, b.Width - Px(18), b.Height), tc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            };
        }

        static void StyleButton(Button b, string glyph, bool danger)
        {
            bool first = !btnGlyph.ContainsKey(b);
            btnGlyph[b] = glyph ?? "";
            if (danger) btnDanger.Add(b); else btnDanger.Remove(b);
            if (!first) { b.Invalidate(); return; }
            b.Disposed += delegate { btnGlyph.Remove(b); btnDanger.Remove(b); btnSkip.Remove(b); };
            b.MouseEnter += delegate { b.Invalidate(); };
            b.MouseLeave += delegate { b.Invalidate(); };
            b.MouseDown += delegate { b.Invalidate(); };
            b.MouseUp += delegate { b.Invalidate(); };
            b.EnabledChanged += delegate { b.Invalidate(); };
            b.Paint += delegate(object o, PaintEventArgs e)
            {
                if (btnSkip.Contains(b) || b.Image != null) return;
                string gl;
                if (!btnGlyph.TryGetValue(b, out gl)) gl = "";
                bool dg = btnDanger.Contains(b);
                Graphics g = e.Graphics;
                g.Clear(b.Parent != null ? b.Parent.BackColor : CBg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool hot = IsHot(b);
                Color baseFill = b.BackColor;
                Color fill, edge, tc;
                if (dg)
                {
                    fill = hot ? Color.FromArgb(74, 22, 36) : Color.FromArgb(54, 16, 28);
                    edge = Color.FromArgb(126, 34, 54);
                    tc = Color.FromArgb(244, 120, 142);
                }
                else
                {
                    fill = !hot ? baseFill : baseFill.ToArgb() == CBtn.ToArgb() ? CBtnHover : Mix(baseFill, Color.White, 0.08);
                    Color bc = b.FlatAppearance.BorderSize == 0 ? fill : b.FlatAppearance.BorderColor;
                    edge = hot && bc.ToArgb() == CBorder.ToArgb() ? Mix(CBorder, CText, 0.18) : bc;
                    tc = b.Enabled ? b.ForeColor : CSub;
                }
                if (hot && (Control.MouseButtons & MouseButtons.Left) != 0 && b.Capture)
                {
                    fill = Mix(fill, Color.White, 0.12);
                    edge = dg ? Mix(edge, Color.White, 0.25) : CHover;
                }
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, b.Width - 1, b.Height - 1), Px(8)))
                {
                    using (SolidBrush f = new SolidBrush(fill)) g.FillPath(f, path);
                    using (Pen p = new Pen(edge)) g.DrawPath(p, path);
                }
                bool leftAlign = b.TextAlign == ContentAlignment.MiddleLeft || b.TextAlign == ContentAlignment.TopLeft || b.TextAlign == ContentAlignment.BottomLeft;
                int iw = string.IsNullOrEmpty(gl) ? 0 : Px(24);
                if (leftAlign)
                {
                    int lx = b.Padding.Left + Px(6);
                    if (iw > 0) DrawGlyph(g, gl, new RectangleF(lx, (b.Height - Px(16)) / 2f, Px(16), Px(16)), tc);
                    TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(lx + iw, 0, Math.Max(1, b.Width - lx - iw - b.Padding.Right - Px(4)), b.Height), tc,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    return;
                }
                Size ts = TextRenderer.MeasureText(g, b.Text, b.Font, new Size(Px(1000), b.Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int x = Math.Max(Px(6), (b.Width - (iw + ts.Width)) / 2);
                if (iw > 0) DrawGlyph(g, gl, new RectangleF(x, (b.Height - Px(16)) / 2f, Px(16), Px(16)), tc);
                TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(x + iw, 0, Math.Max(1, b.Width - x - iw), b.Height), tc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
        }

        static void DrawGlyph(Graphics g, string kind, RectangleF box, Color c)
        {
            float k = box.Width / 24f;
            Func<float, float, PointF> P = delegate(float x, float y) { return new PointF(box.X + x * k, box.Y + y * k); };
            Func<float, float, float, float, RectangleF> R = delegate(float x, float y, float w, float h) { return new RectangleF(box.X + x * k, box.Y + y * k, w * k, h * k); };
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(c, Math.Max(1.2f, 1.75f * k)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case "pulse":
                        g.DrawLines(pen, new PointF[] { P(2, 12), P(6, 12), P(9, 4), P(15, 20), P(18, 12), P(22, 12) });
                        break;
                    case "chart":
                        g.DrawLine(pen, P(6, 20), P(6, 13));
                        g.DrawLine(pen, P(12, 20), P(12, 4));
                        g.DrawLine(pen, P(18, 20), P(18, 9));
                        break;
                    case "gear":
                    {
                        PointF[] teeth = new PointF[32];
                        for (int i = 0; i < 32; i++)
                        {
                            double a = (i * 11.25 - 5.6) * Math.PI / 180.0;
                            float rr = (i / 2) % 2 == 0 ? 9.5f : 7.2f;
                            teeth[i] = P(12 + (float)(Math.Cos(a) * rr), 12 + (float)(Math.Sin(a) * rr));
                        }
                        g.DrawPolygon(pen, teeth);
                        g.DrawEllipse(pen, R(9, 9, 6, 6));
                        break;
                    }
                    case "search":
                        g.DrawEllipse(pen, R(4, 4, 14, 14));
                        g.DrawLine(pen, P(16, 16), P(21, 21));
                        break;
                    case "reset":
                        g.DrawArc(pen, R(3, 3, 18, 18), 180, -270);
                        g.DrawLine(pen, P(12, 3), P(6.5f, 5.2f));
                        g.DrawLine(pen, P(6.5f, 5.2f), P(3, 8));
                        g.DrawLines(pen, new PointF[] { P(3, 3), P(3, 8), P(8, 8) });
                        break;
                    case "sync":
                        g.DrawArc(pen, R(4, 4, 16, 16), 200, 140);
                        g.DrawArc(pen, R(4, 4, 16, 16), 20, 140);
                        foreach (float deg in new float[] { 340f, 160f })
                        {
                            double a = deg * Math.PI / 180.0;
                            float tx = 12 + 8 * (float)Math.Cos(a), ty = 12 + 8 * (float)Math.Sin(a);
                            float dx = -(float)Math.Sin(a), dy = (float)Math.Cos(a);
                            float bx = tx - dx * 3.5f, by = ty - dy * 3.5f;
                            g.DrawLines(pen, new PointF[] { P(bx - dy * 3f, by + dx * 3f), P(tx, ty), P(bx + dy * 3f, by - dx * 3f) });
                        }
                        break;
                    case "book":
                        g.DrawPolygon(pen, new PointF[] { P(2, 4), P(8, 4), P(12, 8), P(12, 20.5f), P(9.5f, 18), P(2, 18) });
                        g.DrawPolygon(pen, new PointF[] { P(22, 4), P(16, 4), P(12, 8), P(12, 20.5f), P(14.5f, 18), P(22, 18) });
                        break;
                    case "x":
                        g.DrawLine(pen, P(6, 6), P(18, 18));
                        g.DrawLine(pen, P(18, 6), P(6, 18));
                        break;
                }
            }
            g.SmoothingMode = oldSm;
        }

        static Color OnAccentText(Color c)
        {
            return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 175 ? Color.FromArgb(14, 14, 20) : Color.White;
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            return RoundedRectF(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
        }

        static GraphicsPath RoundedRectF(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            Label l = new Label();
            l.UseMnemonic = false;
            l.Text = text;
            l.AutoSize = true;
            l.Font = Ui.UiFont(size, style);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }

        static Button MakeButton(string text, int width)
        {
            Button b = new Button();
            b.UseMnemonic = false;
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = CBorder;
            b.FlatAppearance.MouseOverBackColor = CBtnHover;
            b.BackColor = CBtn;
            b.ForeColor = CText;
            b.Font = Ui.UiFont(9.5f);
            b.Size = new Size(width, Px(32));
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            StyleButton(b, null, false);
            return b;
        }

        static string Normalize(SettingDef d, string v)
        {
            string t = (v ?? "").Trim().ToLowerInvariant();
            if (d.Values == OnOff)
            {
                if (t == "on" || t == "true" || t == "yes" || t == "1") return "true";
                if (t == "off" || t == "false" || t == "no" || t == "0") return "false";
                return d.Def;
            }
            if (d.Key == "stack_upward")
            {
                if (t == "on" || t == "true" || t == "yes" || t == "1") return "true";
                if (t == "off" || t == "false" || t == "no" || t == "0") return "false";
                return "auto";
            }
            foreach (string x in d.Values) if (x == t) return x;
            return d.Def;
        }

        static string FormatNum(decimal v, int decimals)
        {
            return Math.Round(v, decimals).ToString(decimals > 0 ? "0.##" : "0", Inv);
        }

        static string KeyText(string v)
        {
            KeySpec k = App.ParseKey(v ?? "");
            return k == null ? (v ?? "") : v.Trim();
        }

        void CancelCapture()
        {
            if (captureDef == null) return;
            RowCanvas rc = captureCanvas;
            captureCanvas = null;
            captureDef = null;
            if (rc != null && !rc.IsDisposed) rc.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (captureDef == null) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape && (keyData & Keys.Modifiers) == Keys.None) { CancelCapture(); return true; }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin ||
                key == Keys.None) return true;
            string name = key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9) name = ((int)(key - Keys.D0)).ToString(Inv);
            string text = "";
            if ((keyData & Keys.Control) != 0) text += "Ctrl+";
            if ((keyData & Keys.Alt) != 0) text += "Alt+";
            if ((keyData & Keys.Shift) != 0) text += "Shift+";
            text += name;
            if (App.ParseKey(text) == null) { SetStatus("That key can't be used", false); return true; }
            SettingDef d = captureDef;
            Change(d, text);
            CancelCapture();
            return true;
        }

        RowCanvas BuildRowCanvas(string name)
        {
            RowCanvas rc = new RowCanvas(this);
            if (IsSettingsTab(name)) rc.AddTabs(name);
            string group = null;
            foreach (SettingDef d in defs)
            {
                if (d.Section != name || d.Hidden) continue;
                if (d.Group != null && d.Group != group) rc.AddHeading(name, d.Group, group == null);
                group = d.Group;
                rc.AddSetting(d, d.Desc + "\nDefault: " + DefaultText(d));
            }
            if (name == OverlaySettings)
                rc.AddButtonRow("Settings Window Position",
                    "Puts this window back in the middle of the screen at its normal size.", "Reset",
                    delegate { ResetWindowBounds(); });
            if (name == "Layout")
                rc.AddButtonRow("Position and size",
                    "In game, press your preview mode key (F9) to show test bars, then use the Move Bars and Resize Bars keys on the Keybinds tab. Reset puts them back in the middle at the normal size.",
                    "Reset",
                    delegate
                    {
                        if (onResetPosition != null) onResetPosition();
                        SetStatus("Position and size reset", false);
                    });
            return rc;
        }

        void StartCanvasCapture(RowCanvas rc, SettingDef d)
        {
            CancelCapture();
            captureCanvas = rc;
            captureDef = d;
            rc.Invalidate();
        }

        class RcPart
        {
            public int Kind;
            public Rectangle Rel, Rect;
            public string Value, Text, Tip;
        }

        class RcItem
        {
            public int Kind;
            public string Text, Desc;
            public bool Restart, First;
            public SettingDef Def;
            public Action Click;
            public float HeadPt;
            public Rectangle Bounds;
            public Size EditorSize, TitleSize, DescSize, BadgeSize;
            public Point EditorAt;
            public int DescW = -1, LabelY;
            public decimal Num;
            public readonly List<RcPart> Parts = new List<RcPart>();
        }

        class RowCanvas : Control
        {
            const int PTab = 1, PPreview = 2, PSeg = 3, PDrop = 4, PSwatch = 5, PHex = 6, PNumText = 7, PUp = 8, PDown = 9, PKey = 10, PButton = 11, PBadge = 12;
            const TextFormatFlags OneLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            const TextFormatFlags Wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            static readonly Color NumDim = Color.FromArgb(110, 116, 128);
            static readonly Color ArrowOff = Color.FromArgb(80, 86, 98);
            static int hexH;

            readonly SettingsWindow win;
            readonly List<RcItem> items = new List<RcItem>();
            readonly Timer repeat = new Timer();
            RcItem downItem, editItem, dropItem, skipDrop, repeatItem;
            RcPart hotPart, downPart;
            TextBox edit;
            bool editHex;
            Control anchor;
            int repeatDir;
            string tip;

            public RowCanvas(SettingsWindow win)
            {
                this.win = win;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = CBg;
                Margin = Padding.Empty;
                repeat.Tick += delegate
                {
                    repeat.Interval = 60;
                    Step(repeatItem, repeatDir);
                };
            }

            static int ArrowW { get { return Px(18); } }

            static int HexH
            {
                get
                {
                    if (hexH == 0)
                        using (TextBox t = new TextBox())
                        {
                            t.BorderStyle = BorderStyle.FixedSingle;
                            t.Font = Ui.UiFont("Consolas", 10f, FontStyle.Regular);
                            hexH = t.PreferredHeight;
                        }
                    return hexH;
                }
            }

            static Size Measure(string text, Font f)
            {
                return TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), OneLine);
            }

            static int TextW(string text, Font f)
            {
                return TextRenderer.MeasureText(text, f).Width;
            }

            public void AddTabs(string cur)
            {
                RcItem it = new RcItem();
                it.Kind = 0;
                it.Text = cur;
                for (int i = 0; i < SettingsTabs.Length; i++)
                {
                    RcPart p = new RcPart();
                    p.Kind = PTab;
                    p.Value = SettingsTabs[i];
                    p.Text = SettingsTabNames[i];
                    it.Parts.Add(p);
                }
                items.Add(it);
            }

            public void AddHeading(string page, string group, bool first)
            {
                RcItem it = new RcItem();
                it.Kind = 1;
                it.Text = group.ToUpperInvariant();
                it.First = first;
                it.HeadPt = win.HeadingSize;
                string[] buttons = PreviewButtons(page, group);
                if (buttons != null)
                {
                    Font bf = Ui.UiFont(8.5f, FontStyle.Bold);
                    for (int i = 0; i + 1 < buttons.Length; i += 2)
                    {
                        RcPart p = new RcPart();
                        p.Kind = PPreview;
                        p.Text = buttons[i];
                        p.Value = buttons[i + 1];
                        p.Tip = "Shows it on the test bars";
                        p.Rel = new Rectangle(0, 0, TextW(p.Text, bf) + Px(24), Px(28));
                        it.Parts.Add(p);
                    }
                }
                items.Add(it);
            }

            public void AddSetting(SettingDef d, string desc)
            {
                RcItem it = NewRow(d.Label, desc, d.Restart);
                it.Def = d;
                BuildEditor(it);
                items.Add(it);
            }

            public void AddButtonRow(string label, string desc, string button, Action click)
            {
                RcItem it = NewRow(label, desc, false);
                it.Click = click;
                RcPart p = new RcPart();
                p.Kind = PButton;
                p.Text = button;
                p.Rel = new Rectangle(0, 0, Px(80), Px(32));
                it.Parts.Add(p);
                it.EditorSize = p.Rel.Size;
                items.Add(it);
            }

            RcItem NewRow(string label, string desc, bool restart)
            {
                RcItem it = new RcItem();
                it.Kind = 2;
                it.Text = label;
                it.Desc = desc;
                it.Restart = restart;
                it.TitleSize = Measure(label, Ui.UiFont(10.5f, FontStyle.Bold));
                if (restart)
                {
                    Size bs = Measure("Restart", Ui.UiFont(8.5f, FontStyle.Bold));
                    it.BadgeSize = new Size(bs.Width + Px(12), bs.Height + 1 + Px(2));
                    RcPart b = new RcPart();
                    b.Kind = PBadge;
                    b.Tip = "Takes effect the next time you start the game.";
                    it.Parts.Add(b);
                }
                return it;
            }

            static RcPart Part(int kind, Rectangle rel, string value, string text)
            {
                RcPart p = new RcPart();
                p.Kind = kind;
                p.Rel = rel;
                p.Value = value;
                p.Text = text;
                return p;
            }

            void AddNumParts(RcItem it, int x, int y)
            {
                int w = Px(90), h = Px(30);
                it.Parts.Add(Part(PNumText, new Rectangle(x, y, w, h), null, null));
                it.Parts.Add(Part(PUp, new Rectangle(x + w - ArrowW - 1, y + 1, ArrowW, (h - 2) / 2), null, null));
                it.Parts.Add(Part(PDown, new Rectangle(x + w - ArrowW - 1, y + 1 + (h - 2) / 2, ArrowW, h - 2 - (h - 2) / 2), null, null));
            }

            void BuildEditor(RcItem it)
            {
                SettingDef d = it.Def;
                Font reg = Ui.UiFont(9.5f), bold = Ui.UiFont(9.5f, FontStyle.Bold);
                switch (d.Kind)
                {
                    case "choice":
                    {
                        int cols = d.Key == "pinned_build_position" ? 2 : 0, cellW = Px(48);
                        foreach (string lb in d.Labels) cellW = Math.Max(cellW, TextW(lb, bold) + Px(24));
                        int x = SegPad;
                        for (int i = 0; i < d.Values.Length; i++)
                        {
                            Rectangle r = new Rectangle(x, SegPad, Math.Max(Px(48), TextW(d.Labels[i], reg) + Px(24)), Px(30));
                            if (cols > 0) r = new Rectangle(SegPad + (i % cols) * (cellW + SegGap), SegPad + (i / cols) * (Px(30) + SegGap), cellW, Px(30));
                            x += r.Width + SegGap;
                            it.Parts.Add(Part(PSeg, r, d.Values[i], d.Labels[i]));
                        }
                        it.EditorSize = new Size(x - SegGap + SegPad, Px(30) + SegPad * 2);
                        if (cols > 0)
                            it.EditorSize = new Size(cols * (cellW + SegGap) - SegGap + SegPad * 2,
                                ((d.Values.Length + cols - 1) / cols) * (Px(30) + SegGap) - SegGap + SegPad * 2);
                        break;
                    }
                    case "dropdown":
                    {
                        int w = Px(140);
                        foreach (string lb in d.Labels) w = Math.Max(w, TextW(lb, reg) + Px(44));
                        it.Parts.Add(Part(PDrop, new Rectangle(0, 0, w, Px(30)), null, null));
                        it.EditorSize = new Size(w, Px(30));
                        break;
                    }
                    case "color":
                    {
                        RcPart sw = Part(PSwatch, new Rectangle(0, 0, Px(36), Px(30)), null, null);
                        sw.Tip = "Pick a color";
                        it.Parts.Add(sw);
                        it.Parts.Add(Part(PHex, new Rectangle(Px(44), Px(3), Px(86), HexH), null, null));
                        it.EditorSize = new Size(Px(130), Px(30));
                        break;
                    }
                    case "num":
                    {
                        bool isAuto;
                        it.Num = ParseNum(d, win.Value(d), out isAuto);
                        if (!d.Auto)
                        {
                            AddNumParts(it, 0, 0);
                            it.EditorSize = new Size(Px(90), Px(30));
                            break;
                        }
                        int x = SegPad;
                        foreach (string mode in new string[] { "auto", "custom" })
                        {
                            string text = mode == "auto" ? "Auto" : "Custom";
                            int w = TextW(text, reg) + Px(24);
                            it.Parts.Add(Part(PSeg, new Rectangle(x, SegPad, w, Px(30)), mode, text));
                            x += w + SegGap;
                        }
                        int hostH = Px(30) + SegPad * 2;
                        int bx = x - SegGap + SegPad + Px(10);
                        AddNumParts(it, bx, (hostH - Px(30)) / 2);
                        it.EditorSize = new Size(bx + Px(90), hostH);
                        break;
                    }
                    case "key":
                    {
                        RcPart k = Part(PKey, new Rectangle(0, 0, Px(150), Px(32)), null, null);
                        k.Tip = "Click, then press the key (with Ctrl, Alt or Shift if you want). Esc cancels.";
                        it.Parts.Add(k);
                        it.EditorSize = k.Rel.Size;
                        break;
                    }
                }
            }

            static decimal ParseNum(SettingDef d, string v, out bool isAuto)
            {
                v = (v ?? "").Trim();
                isAuto = d.Auto && string.Equals(v, "auto", StringComparison.OrdinalIgnoreCase);
                double num;
                if (isAuto || !double.TryParse(v, NumberStyles.Float, Inv, out num))
                {
                    if (!double.TryParse(d.Def, NumberStyles.Float, Inv, out num)) num = d.Min;
                }
                num = Math.Max(d.Min, Math.Min(d.Max, num));
                return (decimal)Math.Round(num, d.Decimals);
            }

            bool NumAuto(RcItem it)
            {
                bool isAuto;
                decimal v = ParseNum(it.Def, win.Value(it.Def), out isAuto);
                if (!isAuto) it.Num = v;
                return isAuto;
            }

            static string NumText(RcItem it)
            {
                return it.Num.ToString(it.Def.Decimals > 0 ? "F" + it.Def.Decimals : "0", Inv);
            }

            Color CurColor(RcItem it)
            {
                Color? parsed = App.ParseColor(win.Value(it.Def));
                return parsed.HasValue ? parsed.Value : App.ParseColor(it.Def.Def).Value;
            }

            static RcPart Find(RcItem it, int kind)
            {
                foreach (RcPart p in it.Parts) if (p.Kind == kind) return p;
                return null;
            }

            public void LayoutAt(int w)
            {
                w = Math.Max(Px(300), w);
                Font descF = Ui.UiFont(9f);
                int y = 0;
                foreach (RcItem it in items)
                {
                    if (it.Kind == 0)
                    {
                        int n = Math.Max(1, it.Parts.Count), gap = Px(6);
                        int each = Math.Max(Px(40), (w - (n - 1) * gap) / n), x = 0;
                        for (int k = 0; k < it.Parts.Count; k++)
                        {
                            int tw = k == n - 1 ? Math.Max(Px(40), w - (n - 1) * (each + gap)) : each;
                            it.Parts[k].Rect = new Rectangle(x, y, tw, Px(30));
                            x += tw + gap;
                        }
                        it.Bounds = new Rectangle(0, y, w, Px(30));
                        y += Px(30) + Px(12);
                        continue;
                    }
                    if (it.Kind == 1)
                    {
                        if (!it.First) y += Px(10);
                        int lh = Measure(it.Text, Ui.UiFont(it.HeadPt, FontStyle.Bold)).Height;
                        int h = lh;
                        foreach (RcPart p in it.Parts) h = Math.Max(h, p.Rel.Height);
                        int bx = w;
                        for (int k = it.Parts.Count - 1; k >= 0; k--)
                        {
                            RcPart p = it.Parts[k];
                            p.Rect = new Rectangle(bx - p.Rel.Width, y, p.Rel.Width, p.Rel.Height);
                            bx = p.Rect.Left - Px(6);
                        }
                        it.Bounds = new Rectangle(0, y, w, h);
                        it.LabelY = y + h - lh - Px(2);
                        y += h + Px(6);
                        continue;
                    }
                    Size es = it.EditorSize;
                    int editorX = w - es.Width - Px(16);
                    int dw = Math.Max(Px(120), editorX - Px(32));
                    if (dw != it.DescW)
                    {
                        it.DescW = dw;
                        it.DescSize = TextRenderer.MeasureText(it.Desc, descF, new Size(dw, int.MaxValue), Wrap);
                    }
                    int rowH = Math.Max(Px(36) + it.DescSize.Height + Px(12), es.Height + Px(24));
                    it.Bounds = new Rectangle(0, y, w, rowH);
                    it.EditorAt = new Point(editorX, y + (rowH - es.Height) / 2);
                    foreach (RcPart p in it.Parts)
                    {
                        if (p.Kind == PBadge)
                            p.Rect = new Rectangle(Px(16) + it.TitleSize.Width + Px(8), y + Px(12) + Px(2), it.BadgeSize.Width, it.BadgeSize.Height);
                        else
                            p.Rect = new Rectangle(it.EditorAt.X + p.Rel.X, it.EditorAt.Y + p.Rel.Y, p.Rel.Width, p.Rel.Height);
                    }
                    y += rowH + Px(8);
                }
                Size want = new Size(w, Math.Max(1, y));
                if (Size != want) Size = want;
                PlaceFloaters();
                Invalidate();
            }

            void PlaceFloaters()
            {
                if (anchor != null && dropItem != null)
                {
                    RcPart p = Find(dropItem, PDrop);
                    if (p != null) anchor.Location = new Point(p.Rect.X, p.Rect.Bottom - 1);
                }
                if (edit != null && editItem != null) PlaceEdit();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(CBg);
                Rectangle clip = e.ClipRectangle;
                foreach (RcItem it in items)
                {
                    if (!it.Bounds.IntersectsWith(clip)) continue;
                    if (it.Kind == 0) PaintTabs(g, it);
                    else if (it.Kind == 1) PaintHeading(g, it);
                    else PaintRow(g, it);
                }
            }

            bool Hot(RcPart p) { return p == hotPart && p != null; }
            bool Down(RcPart p) { return p == downPart && p == hotPart && p != null && (Control.MouseButtons & MouseButtons.Left) != 0; }

            void PaintButton(Graphics g, RcPart p, string text, Font f, Color baseFill, Color baseEdge, Color tc)
            {
                bool hot = Hot(p);
                Color fill = !hot ? baseFill : baseFill.ToArgb() == CBtn.ToArgb() ? CBtnHover : Mix(baseFill, Color.White, 0.08);
                Color edge = hot && baseEdge.ToArgb() == CBorder.ToArgb() ? Mix(CBorder, CText, 0.18) : baseEdge;
                if (Down(p))
                {
                    fill = Mix(fill, Color.White, 0.12);
                    edge = CHover;
                }
                Rectangle r = p.Rect;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Px(8)))
                {
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (Pen pen = new Pen(edge)) g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, text, f, new Rectangle(r.X + Px(6), r.Y, Math.Max(1, r.Width - Px(12)), r.Height), tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | OneLine | TextFormatFlags.EndEllipsis);
            }

            void PaintTabs(Graphics g, RcItem it)
            {
                foreach (RcPart p in it.Parts)
                {
                    bool on = p.Value == it.Text;
                    PaintButton(g, p, p.Text, Ui.UiFont(9.5f, on ? FontStyle.Bold : FontStyle.Regular),
                        on ? CPill : CBtn, on ? CPill : CBorder, on ? OnAccentText(CPill) : CText);
                }
            }

            void PaintHeading(Graphics g, RcItem it)
            {
                TextRenderer.DrawText(g, it.Text, Ui.UiFont(it.HeadPt, FontStyle.Bold), new Point(Px(2), it.LabelY), CGold, OneLine);
                foreach (RcPart p in it.Parts)
                    PaintButton(g, p, p.Text, Ui.UiFont(8.5f, FontStyle.Bold), CBtn, CBorder, CText);
            }

            void PaintRow(Graphics g, RcItem it)
            {
                Rectangle b = it.Bounds;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(b.X, b.Y, b.Width - 1, b.Height - 1), Px(10)))
                {
                    using (SolidBrush f = new SolidBrush(CRow)) g.FillPath(f, path);
                    using (Pen p = new Pen(CBorder)) g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, it.Text, Ui.UiFont(10.5f, FontStyle.Bold), new Point(b.X + Px(16), b.Y + Px(12)), CText, OneLine);
                TextRenderer.DrawText(g, it.Desc, Ui.UiFont(9f), new Rectangle(b.X + Px(16), b.Y + Px(36), it.DescW, it.DescSize.Height), CSub, Wrap);
                RcPart badge = Find(it, PBadge);
                if (badge != null)
                {
                    Rectangle r = badge.Rect;
                    using (GraphicsPath path = RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Px(4)))
                    {
                        using (SolidBrush f = new SolidBrush(CRestartBg)) g.FillPath(f, path);
                        using (Pen p = new Pen(CRestart)) g.DrawPath(p, path);
                    }
                    TextRenderer.DrawText(g, "Restart", Ui.UiFont(8.5f, FontStyle.Bold), r, CRestart,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | OneLine);
                }
                if (it.Def == null)
                {
                    RcPart btn = Find(it, PButton);
                    if (btn != null) PaintButton(g, btn, btn.Text, Ui.UiFont(9.5f), CBtn, CBorder, CText);
                    return;
                }
                switch (it.Def.Kind)
                {
                    case "choice": PaintSegments(g, it, Normalize(it.Def, win.Value(it.Def))); break;
                    case "dropdown": PaintDrop(g, it); break;
                    case "color": PaintColor(g, it); break;
                    case "num":
                    {
                        bool isAuto = NumAuto(it);
                        if (it.Def.Auto) PaintSegments(g, it, isAuto ? "auto" : "custom");
                        PaintNum(g, it, !isAuto);
                        break;
                    }
                    case "key":
                    {
                        RcPart k = Find(it, PKey);
                        bool cap = win.captureCanvas == this && win.captureDef == it.Def;
                        PaintButton(g, k, cap ? "Press a key..." : KeyText(win.Value(it.Def)), Ui.UiFont(9.5f), CBtn, cap ? CGold : CBorder, CText);
                        break;
                    }
                }
            }

            void PaintSegments(Graphics g, RcItem it, string cur)
            {
                Rectangle u = Rectangle.Empty;
                foreach (RcPart p in it.Parts) if (p.Kind == PSeg) u = u.IsEmpty ? p.Rect : Rectangle.Union(u, p.Rect);
                if (u.IsEmpty) return;
                u.Inflate(SegPad, SegPad);
                u.Width -= 1; u.Height -= 1;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(u, Px(9)))
                {
                    using (SolidBrush f = new SolidBrush(CTray)) g.FillPath(f, path);
                    using (Pen p = new Pen(CBorder)) g.DrawPath(p, path);
                }
                foreach (RcPart p in it.Parts)
                {
                    if (p.Kind != PSeg) continue;
                    bool on = p.Value == cur, hot = Hot(p);
                    Rectangle rc = new Rectangle(p.Rect.X, p.Rect.Y, p.Rect.Width - 1, p.Rect.Height - 1);
                    Color tc;
                    if (on)
                    {
                        Color pill = hot ? Mix(CPill, Color.White, 0.12) : CPill;
                        using (GraphicsPath path = RoundedRect(rc, Px(7)))
                        using (SolidBrush f = new SolidBrush(pill)) g.FillPath(f, path);
                        tc = OnAccentText(pill);
                    }
                    else
                    {
                        if (hot)
                            using (GraphicsPath path = RoundedRect(rc, Px(7)))
                            using (SolidBrush f = new SolidBrush(CBtnHover)) g.FillPath(f, path);
                        tc = hot ? CText : Mix(CSub, CText, 0.55);
                    }
                    TextRenderer.DrawText(g, p.Text, Ui.UiFont(9.5f, on ? FontStyle.Bold : FontStyle.Regular), p.Rect, tc,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | OneLine);
                }
            }

            void PaintDrop(Graphics g, RcItem it)
            {
                RcPart p = Find(it, PDrop);
                SettingDef d = it.Def;
                bool hot = Hot(p) || (dropItem == it && win.openDrop != null);
                string cur = Normalize(d, win.Value(d));
                int idx = Array.IndexOf(d.Values, cur);
                string text = idx >= 0 ? d.Labels[idx] : cur;
                Rectangle r = p.Rect;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Px(6)))
                using (SolidBrush f = new SolidBrush(hot ? CBtnHover : CBtn))
                using (Pen pen = new Pen(hot ? CHover : CBorder))
                {
                    g.FillPath(f, path);
                    g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, text, Ui.UiFont(9.5f), new Rectangle(r.X + Px(10), r.Y, r.Width - Px(32), r.Height), CText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | OneLine | TextFormatFlags.EndEllipsis);
                int cx = r.Right - Px(16), cy = r.Y + r.Height / 2;
                using (SolidBrush ab = new SolidBrush(CSub))
                    g.FillPolygon(ab, new Point[] { new Point(cx - Px(4), cy - Px(2)), new Point(cx + Px(4), cy - Px(2)), new Point(cx, cy + Px(3)) });
            }

            void PaintColor(Graphics g, RcItem it)
            {
                Color cur = CurColor(it);
                RcPart sw = Find(it, PSwatch);
                bool hot = Hot(sw);
                RectangleF rc = new RectangleF(sw.Rect.X + PxF(1f), sw.Rect.Y + PxF(1f), sw.Rect.Width - PxF(2.5f), sw.Rect.Height - PxF(2.5f));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRectF(rc, PxF(6f)))
                {
                    using (SolidBrush b = new SolidBrush(cur)) g.FillPath(b, path);
                    using (Pen p = new Pen(hot ? CHover : CBorder, hot ? 1.5f : 1f)) g.DrawPath(p, path);
                }
                RcPart hx = Find(it, PHex);
                if (editItem == it && editHex) return;
                Rectangle r = hx.Rect;
                g.SmoothingMode = SmoothingMode.None;
                using (SolidBrush f = new SolidBrush(CBtn)) g.FillRectangle(f, r);
                using (Pen p = new Pen(Hot(hx) ? Mix(CBorder, CText, 0.18) : CBorder)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                TextRenderer.DrawText(g, Hex(cur), Ui.UiFont("Consolas", 10f, FontStyle.Regular), new Rectangle(r.X + Px(2), r.Y, r.Width - Px(4), r.Height), CText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | OneLine);
            }

            void PaintNum(Graphics g, RcItem it, bool enabled)
            {
                RcPart box = Find(it, PNumText);
                Rectangle r = box.Rect;
                g.SmoothingMode = SmoothingMode.None;
                using (SolidBrush f = new SolidBrush(CBtn)) g.FillRectangle(f, r);
                foreach (RcPart p in it.Parts)
                {
                    if (p.Kind != PUp && p.Kind != PDown) continue;
                    bool lit = enabled && Hot(p);
                    using (SolidBrush f = new SolidBrush(lit ? CBtnHover : CBtn)) g.FillRectangle(f, p.Rect);
                    float cx = p.Rect.X + p.Rect.Width / 2f, cy = p.Rect.Y + p.Rect.Height / 2f + (p.Kind == PUp ? 0.5f : -0.5f);
                    float aw = PxF(4f), ah = PxF(2.25f);
                    PointF[] pts = p.Kind == PUp
                        ? new PointF[] { new PointF(cx - aw, cy + ah), new PointF(cx + aw, cy + ah), new PointF(cx, cy - ah) }
                        : new PointF[] { new PointF(cx - aw, cy - ah), new PointF(cx + aw, cy - ah), new PointF(cx, cy + ah) };
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush b = new SolidBrush(!enabled ? ArrowOff : lit ? CGold : CSub)) g.FillPolygon(b, pts);
                    g.SmoothingMode = SmoothingMode.None;
                }
                using (Pen p = new Pen(CBorder))
                {
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    g.DrawLine(p, r.Right - ArrowW - 2, r.Y + 1, r.Right - ArrowW - 2, r.Bottom - 2);
                }
                if (editItem == it && !editHex) return;
                TextRenderer.DrawText(g, NumText(it), Ui.UiFont(10f), new Rectangle(r.X + Px(6), r.Y, r.Width - ArrowW - Px(12), r.Height),
                    enabled ? CText : NumDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | OneLine);
            }

            bool HitAt(Point pt, out RcItem hitItem, out RcPart hitPart)
            {
                hitItem = null;
                hitPart = null;
                foreach (RcItem it in items)
                {
                    if (!it.Bounds.Contains(pt)) continue;
                    for (int k = it.Parts.Count - 1; k >= 0; k--)
                    {
                        RcPart p = it.Parts[k];
                        if (!p.Rect.Contains(pt)) continue;
                        if ((p.Kind == PNumText || p.Kind == PUp || p.Kind == PDown) && it.Def.Auto && NumAuto(it)) continue;
                        hitItem = it;
                        hitPart = p;
                        return true;
                    }
                    return false;
                }
                return false;
            }

            void InvalidatePart(RcPart p)
            {
                if (p == null) return;
                Rectangle r = p.Rect;
                r.Inflate(Px(6), Px(6));
                Invalidate(r);
            }

            void SetHot(RcItem it, RcPart p)
            {
                if (p == hotPart) return;
                if (repeatItem != null && p != downPart) StopRepeat();
                InvalidatePart(hotPart);
                hotPart = p;
                InvalidatePart(hotPart);
                Cursor = p != null && p.Kind != PBadge ? Cursors.Hand : Cursors.Default;
                string t = p != null ? p.Tip : null;
                if (t != tip)
                {
                    tip = t;
                    win.tips.SetToolTip(this, t);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                RcItem it;
                RcPart p;
                HitAt(e.Location, out it, out p);
                SetHot(it, p);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                SetHot(null, null);
                StopRepeat();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                RcItem it;
                RcPart p;
                HitAt(e.Location, out it, out p);
                if (editItem != null) CloseEdit(true);
                SetHot(it, p);
                downItem = it;
                downPart = p;
                skipDrop = p != null && p.Kind == PDrop && win.openDrop != null && dropItem == it ? it : null;
                if (p == null) return;
                InvalidatePart(p);
                if (p.Kind == PUp) StartRepeat(it, 1);
                else if (p.Kind == PDown) StartRepeat(it, -1);
                else if (p.Kind == PNumText) OpenEdit(it, false);
                else if (p.Kind == PHex) OpenEdit(it, true);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                StopRepeat();
                RcItem it = downItem;
                RcPart p = downPart;
                downItem = null;
                downPart = null;
                InvalidatePart(p);
                if (p == null) return;
                RcItem hi;
                RcPart hp;
                HitAt(e.Location, out hi, out hp);
                SetHot(hi, hp);
                if (hp != p) return;
                Activate(it, p);
            }

            void Activate(RcItem it, RcPart p)
            {
                SettingDef d = it.Def;
                switch (p.Kind)
                {
                    case PTab:
                    {
                        string key = p.Value;
                        if (win.section != key) win.BeginInvoke((MethodInvoker)delegate { win.ShowSection(key); });
                        break;
                    }
                    case PPreview:
                        if (win.onPreview != null) win.onPreview(p.Value);
                        break;
                    case PSeg:
                        if (d.Kind == "choice")
                        {
                            if (Normalize(d, win.Value(d)) != p.Value) win.Change(d, p.Value);
                        }
                        else
                        {
                            bool isAuto = NumAuto(it);
                            if (p.Value == "auto" && !isAuto) win.Change(d, "auto");
                            else if (p.Value == "custom" && isAuto) win.Change(d, FormatNum(it.Num, d.Decimals));
                        }
                        Invalidate(it.Bounds);
                        break;
                    case PDrop:
                        if (skipDrop == it) { skipDrop = null; break; }
                        OpenDrop(it);
                        break;
                    case PSwatch:
                        PickColor(it);
                        break;
                    case PKey:
                        win.StartCanvasCapture(this, d);
                        break;
                    case PButton:
                        if (it.Click != null) it.Click();
                        break;
                }
            }

            void OpenDrop(RcItem it)
            {
                SettingDef d = it.Def;
                string cur = Normalize(d, win.Value(d));
                List<BsItem> list = new List<BsItem>();
                for (int i = 0; i < d.Values.Length; i++) list.Add(new BsItem(d.Values[i], d.Labels[i]));
                if (anchor == null)
                {
                    anchor = new Control();
                    anchor.BackColor = CRow;
                    anchor.Size = new Size(1, 1);
                    anchor.TabStop = false;
                    Controls.Add(anchor);
                }
                dropItem = it;
                PlaceFloaters();
                win.OpenListDrop(anchor, list, cur, 0, delegate(string picked)
                {
                    if (IsDisposed) return;
                    win.Change(d, picked);
                    Invalidate(it.Bounds);
                });
                ToolStripDropDown dd = win.openDrop;
                if (dd != null) dd.Closed += delegate { if (!IsDisposed) Invalidate(it.Bounds); };
                Invalidate(it.Bounds);
            }

            void PickColor(RcItem it)
            {
                using (ColorDialog dlg = new ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.AnyColor = true;
                    dlg.Color = CurColor(it);
                    DialogResult picked;
                    win.dialogOpen++;
                    try { picked = dlg.ShowDialog(win); }
                    finally { win.dialogOpen--; }
                    if (picked != DialogResult.OK || IsDisposed) return;
                    win.Change(it.Def, Hex(Color.FromArgb(255, dlg.Color)));
                    Invalidate(it.Bounds);
                }
            }

            void StartRepeat(RcItem it, int dir)
            {
                repeatItem = it;
                repeatDir = dir;
                Step(it, dir);
                repeat.Interval = 400;
                repeat.Start();
            }

            void StopRepeat()
            {
                repeat.Stop();
                repeatItem = null;
            }

            void Step(RcItem it, int dir)
            {
                if (it == null || (it.Def.Auto && NumAuto(it))) return;
                SetNum(it, it.Num + dir * (decimal)it.Def.Step);
            }

            void SetNum(RcItem it, decimal v)
            {
                SettingDef d = it.Def;
                v = Math.Round(Math.Max((decimal)d.Min, Math.Min((decimal)d.Max, v)), d.Decimals);
                if (v != it.Num)
                {
                    it.Num = v;
                    if (!(d.Auto && NumAuto(it))) win.Change(d, FormatNum(v, d.Decimals));
                    Invalidate(it.Bounds);
                }
                if (edit != null && editItem == it && !editHex) edit.Text = NumText(it);
            }

            void OpenEdit(RcItem it, bool hex)
            {
                if (edit == null)
                {
                    edit = new TextBox();
                    edit.Visible = false;
                    edit.ForeColor = CText;
                    edit.BackColor = CBtn;
                    edit.KeyDown += EditKeyDown;
                    edit.Leave += delegate { CloseEdit(true); };
                    edit.MouseWheel += delegate(object o, MouseEventArgs e)
                    {
                        if (editItem != null && !editHex) Step(editItem, e.Delta > 0 ? 1 : -1);
                    };
                    Controls.Add(edit);
                }
                editItem = it;
                editHex = hex;
                if (hex)
                {
                    edit.BorderStyle = BorderStyle.FixedSingle;
                    edit.Font = Ui.UiFont("Consolas", 10f, FontStyle.Regular);
                    edit.TextAlign = HorizontalAlignment.Left;
                    edit.Text = Hex(CurColor(it));
                }
                else
                {
                    edit.BorderStyle = BorderStyle.None;
                    edit.Font = Ui.UiFont(10f);
                    edit.TextAlign = HorizontalAlignment.Right;
                    edit.Text = NumText(it);
                }
                PlaceEdit();
                edit.Visible = true;
                edit.Focus();
                edit.SelectAll();
                Invalidate(it.Bounds);
            }

            void PlaceEdit()
            {
                if (editHex)
                {
                    RcPart hx = Find(editItem, PHex);
                    edit.SetBounds(hx.Rect.X, hx.Rect.Y, hx.Rect.Width, hx.Rect.Height);
                    return;
                }
                Rectangle r = Find(editItem, PNumText).Rect;
                int h = edit.PreferredHeight;
                edit.SetBounds(r.X + Px(6), r.Y + Math.Max(0, (r.Height - h) / 2), r.Width - ArrowW - Px(12), h);
            }

            void CloseEdit(bool commit)
            {
                if (editItem == null) return;
                RcItem it = editItem;
                bool hex = editHex;
                string text = edit.Text;
                editItem = null;
                if (edit.Focused) { try { win.ActiveControl = null; } catch { } }
                edit.Visible = false;
                if (commit)
                {
                    if (hex) CommitHex(it, text);
                    else CommitNum(it, text);
                }
                Invalidate(it.Bounds);
            }

            void CommitNum(RcItem it, string text)
            {
                decimal v;
                string t = (text ?? "").Trim().Replace(',', '.');
                if (decimal.TryParse(t, NumberStyles.Float, Inv, out v)) SetNum(it, v);
                else if (edit != null && editItem == it) edit.Text = NumText(it);
            }

            void CommitHex(RcItem it, string text)
            {
                Color? c = App.ParseColor(text);
                if (c.HasValue) win.Change(it.Def, Hex(c.Value));
                if (edit != null && editItem == it) edit.Text = Hex(CurColor(it));
                Invalidate(it.Bounds);
            }

            void EditKeyDown(object o, KeyEventArgs e)
            {
                RcItem it = editItem;
                if (it == null) return;
                if (e.KeyCode == Keys.Escape) { CloseEdit(false); e.SuppressKeyPress = true; e.Handled = true; return; }
                if (editHex)
                {
                    if (e.KeyCode == Keys.Enter) { CommitHex(it, edit.Text); edit.SelectAll(); e.SuppressKeyPress = true; }
                    return;
                }
                if (e.KeyCode == Keys.Up) { Step(it, 1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Step(it, -1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { CommitNum(it, edit.Text); edit.SelectAll(); e.SuppressKeyPress = true; }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    repeat.Dispose();
                    if (win.captureCanvas == this)
                    {
                        win.captureCanvas = null;
                        win.captureDef = null;
                    }
                }
                base.Dispose(disposing);
            }
        }

        static readonly string[] StatWeapons = new string[] { "Hammer", "Axe", "Sword", "ChainBlades", "WarPike", "Repeaters", "AetherStrikers" };
        static readonly string[] StatWeaponNames = new string[] { "Hammer", "Axe", "Sword", "Chain Blades", "War Pike", "Repeaters", "Aether Strikers" };
        static readonly string[] StatWeaponIcons = new string[] { "RecruitsHammer", "RecruitsAxe", "RecruitsSword", "RecruitsChainBlades", "RecruitsWarPike", "RecruitsRepeaters", "RecruitsStrikers" };

        Image StatTabIcon(string key)
        {
            for (int i = 0; i < StatWeapons.Length; i++)
                if (StatWeapons[i] == key) return BsImage("weapons/generic/" + StatWeaponIcons[i] + ".png");
            return null;
        }
        static readonly string[] StatRowGroups = new string[]
        {
            "Damage", "Damage", "Damage", "Damage", "Damage", "Damage",
            "Combat Effects", "Combat Effects", "Combat Effects", "Combat Effects", "Combat Effects",
            "Hunt Performance", "Hunt Performance", "Hunt Performance", "Hunt Performance", "Hunt Performance", "Hunt Performance", "Hunt Performance",
            "Hunt Performance", "Hunt Performance", "Hunt Performance", "Hunt Performance"
        };
        static readonly string[][] StatRowKeys = new string[][]
        {
            new string[] { "damage" },
            new string[] { "biggest_hit" },
            new string[] { "part_damage" },
            new string[] { "core_damage" },
            new string[] { "stagger_damage" },
            new string[] { "wound_damage" },
            new string[] { "parts_broken" },
            new string[] { "interrupts" },
            new string[] { "staggers" },
            new string[] { "wounds" },
            new string[] { "procs" },
            new string[] { "kills" },
            new string[] { "most_slain" },
            new string[] { "most_weapon" },
            new string[] { "fauna_kills" },
            new string[] { "gatherables" },
            new string[] { "patrol_chests" },
            new string[] { "perfect_dodges" },
            new string[] { "heal_received" },
            new string[] { "damage_taken" },
            new string[] { "deaths" },
            new string[] { "bounties" }
        };
        static readonly string[] StatNames = new string[]
        {
            "Total Damage Dealt", "Biggest Hit", "Part Damage", "Core Damage", "Stagger Damage", "Wound Damage",
            "Parts Broken", "Interrupts", "Staggers", "Wounds", "Status Effects Inflicted",
            "Behemoths Slain", "Most Slain Behemoth", "Most Used Weapon", "Fauna Slain", "Gatherables Collected", "Patrol Chests Opened", "Perfect Dodges", "Healing Received", "Damage Taken", "Deaths", "Bounties Completed"
        };
        static readonly string[] StatKeys = AllStatKeys();

        static readonly string[] KillOrder = new string[]
        {
            "Agarus", "Alyra", "Boreus", "Lightbound Boreus", "Charrogg", "Sporestruck Charrogg", "Chronovore",
            "Drask", "Thunderdeep Drask", "Shadowtouched Drask", "Embermane", "Fenroar", "Frostwulf",
            "Gnasher", "Deepfrost Gnasher", "Flameborn Gnasher", "Hellion", "Kharabak",
            "Koshai", "Lightbound Koshai", "Shadowtouched Koshai", "Malkarion", "Nayzaga", "Shadowtouched Nayzaga",
            "Pangar", "Phaelanx", "Quillshot", "Flameborn Quillshot", "Rezakiri", "Flameborn Rezakiri",
            "Riftstalker", "Sahvyt", "Shrike", "Shrowd", "Skarn", "Deepfrost Skarn", "Skraev", "Stormclaw",
            "Thrax", "Torgadoro", "Urska", "Valomyr"
        };
        static readonly Dictionary<string, string> KillVariants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "host_radiant", "Lightbound Boreus" }, { "sq_sally_terrogg", "Sporestruck Charrogg" },
            { "fulgar_alpha", "Thunderdeep Drask" }, { "fulgar_umbral", "Shadowtouched Drask" },
            { "mcbeaver_frost", "Deepfrost Gnasher" }, { "mcbeaver_blaze", "Flameborn Gnasher" },
            { "paper_radiant", "Lightbound Koshai" }, { "paper_umbral", "Shadowtouched Koshai" },
            { "electroquill_umbral", "Shadowtouched Nayzaga" }, { "bullseye_blaze", "Flameborn Quillshot" },
            { "bugbear_blaze", "Flameborn Rezakiri" }, { "sq_ice_rock", "Deepfrost Skarn" }
        };
        static readonly Dictionary<string, string> KillBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "funguy", "Agarus" }, { "amber", "Alyra" }, { "host", "Boreus" }, { "sally", "Charrogg" },
            { "glitter", "Chronovore" }, { "fulgar", "Drask" }, { "lerawr", "Embermane" }, { "terramane", "Fenroar" },
            { "wolf", "Frostwulf" }, { "mcbeaver", "Gnasher" }, { "lavaroller", "Hellion" }, { "scissors", "Kharabak" },
            { "paper", "Koshai" }, { "thunderbird", "Malkarion" }, { "electroquill", "Nayzaga" }, { "mcrollin", "Pangar" },
            { "habanero", "Phaelanx" }, { "bullseye", "Quillshot" }, { "bugbear", "Rezakiri" }, { "shadowcat", "Riftstalker" },
            { "mantis", "Sahvyt" }, { "moonface", "Shrike" }, { "crowbear", "Shrowd" }, { "rock", "Skarn" },
            { "snowflake", "Skraev" }, { "shockmane", "Stormclaw" }, { "randall", "Thrax" }, { "armstrong", "Torgadoro" },
            { "mint", "Urska" }, { "salt", "Valomyr" }
        };
        const string KillsSection = "Behemoths";

        static string JournalName(string id)
        {
            string name;
            if (KillVariants.TryGetValue(id, out name)) return name;
            foreach (string part in id.Split('_'))
                if (KillBase.TryGetValue(part, out name)) return name;
            return "Other";
        }

        Dictionary<string, double> KillsByBehemoth()
        {
            Dictionary<string, double> result = new Dictionary<string, double>();
            bool all = statsTab == "All";
            string prefix = all ? "kill_" : "wkill_";
            foreach (KeyValuePair<string, Dictionary<string, double>> w in statsData)
            {
                if (!all && !string.Equals(w.Key, statsTab, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (KeyValuePair<string, double> kv in w.Value)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || kv.Value <= 0) continue;
                    string name = JournalName(kv.Key.Substring(prefix.Length));
                    double cur;
                    result.TryGetValue(name, out cur);
                    result[name] = cur + kv.Value;
                }
            }
            return result;
        }

        bool killsOpen;

        string MostUsedWeapon()
        {
            string best = null;
            double bestCount = 0;
            for (int i = 0; i < StatWeapons.Length; i++)
            {
                double c = StatValue(StatWeapons[i], "kills");
                if (c > bestCount) { best = StatWeaponNames[i]; bestCount = c; }
            }
            return best == null ? "-" : best;
        }

        string MostSlain()
        {
            Dictionary<string, double> kills = KillsByBehemoth();
            string best = null;
            double bestCount = 0;
            foreach (string n in KillOrder)
            {
                double c;
                if (kills.TryGetValue(n, out c) && c > bestCount) { best = n; bestCount = c; }
            }
            return best == null ? "-" : best + " (" + FormatStat(bestCount) + ")";
        }

        bool IsTextStat(string key) { return key == "most_weapon" || key == "most_slain"; }

        string StatText(string tab, string key)
        {
            if (key == "most_slain") return MostSlain();
            if (key == "most_weapon") return MostUsedWeapon();
            return FormatStat(StatValue(tab, key));
        }

        static string[] AllStatKeys()
        {
            List<string> keys = new List<string>();
            foreach (string[] row in StatRowKeys) keys.AddRange(row);
            return keys.ToArray();
        }

        static readonly string[] ProcKeys = new string[] { "procs_blaze", "procs_frost", "procs_shock", "procs_terra" };
        static readonly string[] ProcNames = new string[] { "Blaze", "Frost", "Shock", "Terra" };

        string ProcsTip(string tab)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ProcKeys.Length; i++)
            {
                if (i > 0) sb.Append("\n");
                sb.Append(ProcNames[i]).Append(": ").Append(FormatStat(StatValue(tab, ProcKeys[i])));
            }
            return sb.ToString();
        }

        bool StatsOn()
        {
            foreach (SettingDef d in defs)
                if (d.Key == "hunt_stats") return Normalize(d, Value(d)) == "true";
            return false;
        }

        void UpdateStatsNav()
        {
            if (section != StatsPage) return;
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { ShowSection(StatsPage); });
            else ShowSection(StatsPage);
        }

        SettingDef StatsDef()
        {
            foreach (SettingDef d in defs) if (d.Key == "hunt_stats") return d;
            return null;
        }

        void AddStatsIntro()
        {
            Label what = MakeLabel("Keeps lifetime stats for each weapon type: damage, biggest hit, kills, part breaks, staggers, wounds and more."
                + " Counting starts with your next hunt.", 10f, FontStyle.Regular, CText);
            what.Tag = "wrap";
            what.Margin = new Padding(Px(2), 0, 0, Px(14));
            list.Controls.Add(what);
            Button on = MakeButton("Turn On Hunt Stats", Px(170));
            on.ForeColor = CText;
            on.FlatAppearance.BorderColor = CGold;
            on.Margin = new Padding(0, 0, 0, Px(12));
            on.Click += delegate
            {
                SettingDef d = StatsDef();
                if (d == null) return;
                Change(d, "true");
                SaveNow();
            };
            list.Controls.Add(on);
            Label off = MakeLabel("You can turn it off again in Hunt Stats > Settings.", 9f, FontStyle.Regular, CSub);
            off.Margin = new Padding(Px(2), 0, 0, 0);
            list.Controls.Add(off);
        }

        string StatsPath()
        {
            return System.IO.Path.Combine(ModPaths.Data(), "hunt-stats.ini");
        }

        void LoadStats()
        {
            string path = StatsPath();
            try
            {
                if (!File.Exists(path) && File.Exists(path + ".bak")) path = path + ".bak";
                if (!File.Exists(path))
                {
                    statsData = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                    statsStamp = DateTime.MinValue;
                    return;
                }
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (stamp == statsStamp) return;
                Dictionary<string, Dictionary<string, double>> data = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, double> cur = null;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string t = raw.Trim();
                    if (t == "" || t.StartsWith(";") || t.StartsWith("#")) continue;
                    if (t.StartsWith("[") && t.EndsWith("]"))
                    {
                        string name = t.Substring(1, t.Length - 2).Trim();
                        if (!data.TryGetValue(name, out cur))
                        {
                            cur = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            data[name] = cur;
                        }
                        continue;
                    }
                    int eq = t.IndexOf('=');
                    if (cur == null || eq < 0) continue;
                    double v;
                    if (double.TryParse(t.Substring(eq + 1).Trim(), NumberStyles.Float, Inv, out v))
                        cur[t.Substring(0, eq).Trim()] = v;
                }
                statsData = data;
                statsStamp = stamp;
            }
            catch (Exception e)
            {
                if (Log != null) Log("Couldn't read hunt-stats.ini: " + e.Message);
            }
        }

        List<string> StatTabs()
        {
            List<string> tabs = new List<string>(StatWeapons);
            List<string> extra = new List<string>();
            foreach (string w in statsData.Keys)
            {
                bool known = false;
                foreach (string k in StatWeapons) if (string.Equals(k, w, StringComparison.OrdinalIgnoreCase)) known = true;
                if (!known && !string.Equals(w, KillsSection, StringComparison.OrdinalIgnoreCase)) extra.Add(w);
            }
            extra.Sort(StringComparer.OrdinalIgnoreCase);
            tabs.AddRange(extra);
            return tabs;
        }

        static string StatTabName(string key)
        {
            if (key == "All") return "Lifetime";
            for (int i = 0; i < StatWeapons.Length; i++) if (StatWeapons[i] == key) return StatWeaponNames[i];
            if (string.Equals(key, "Unknown", StringComparison.OrdinalIgnoreCase)) return "Unknown Weapon";
            return key;
        }

        double StatValue(string tab, string key)
        {
            if (key == "damage")
                return StatValue(tab, "core_damage") + StatValue(tab, "part_damage") + StatValue(tab, "stagger_damage") + StatValue(tab, "wound_damage");
            double total = 0;
            foreach (KeyValuePair<string, Dictionary<string, double>> w in statsData)
            {
                if (tab != "All" && !string.Equals(w.Key, tab, StringComparison.OrdinalIgnoreCase)) continue;
                double v;
                if (!w.Value.TryGetValue(key, out v)) continue;
                if (key == "biggest_hit") total = Math.Max(total, v);
                else total += v;
            }
            return total;
        }

        bool HasStats(string tab)
        {
            foreach (string k in StatKeys) if (StatValue(tab, k) > 0) return true;
            return false;
        }

        void AddStatsPage()
        {
            LoadStats();
            statsCanvas = NewStatsCanvas();
            FillStatsCanvas(statsCanvas);
            list.Controls.Add(statsCanvas);
        }

        void ResetStats()
        {
            DialogResult r = Ask("Are you sure you want to reset stats?\n\nThis cannot be undone.", "Reset Stats", MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            try
            {
                string path = StatsPath();
                string lockPath = path + ".lock";
                long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                try { if (File.Exists(lockPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath)).TotalSeconds > 30) File.Delete(lockPath); } catch { }
                FileStream lk = null;
                for (int i = 0; i < 20 && lk == null; i++)
                {
                    try { lk = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                    catch (IOException) { System.Threading.Thread.Sleep(25); }
                }
                if (lk == null) { SetStatus("Hunt stats are being saved by the game. Try again in a moment", false); return; }
                try
                {
                    byte[] stamp = Encoding.ASCII.GetBytes(now.ToString(Inv));
                    lk.Write(stamp, 0, stamp.Length);
                    lk.Flush();
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                    try { File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "hunt-stats-reset.txt"), now.ToString(Inv)); }
                    catch (Exception fail) { Diag.Fail("writing hunt-stats-reset.txt", fail); }
                }
                finally
                {
                    lk.Dispose();
                    try { File.Delete(lockPath); } catch (Exception fail) { Diag.Fail("removing hunt-stats.ini.lock", fail); }
                }
                statsData = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                statsStamp = DateTime.MinValue;
                statsTab = "All";
                ShowSection(StatsPage);
                SetStatus("Hunt stats reset", false);
                if (Log != null) Log("Hunt stats were reset from the settings window");
            }
            catch (Exception e)
            {
                SetStatus("Could not reset the stats: " + e.Message, false);
            }
        }

        bool StatsLayoutTiles()
        {
            foreach (SettingDef d in defs)
                if (d.Key == "stats_layout") return Normalize(d, Value(d)) == "tiles";
            return true;
        }

        static string ShortStat(double v)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            double a = Math.Abs(v);
            if (a >= 999950000) return (v / 1e9).ToString("0.#", c) + "B";
            if (a >= 999950) return (v / 1e6).ToString("0.#", c) + "M";
            if (a >= 10000) return (v / 1e3).ToString("0.#", c) + "K";
            return FormatStat(v);
        }

        static string FormatStat(double v)
        {
            return Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture);
        }

        void RefreshStats(bool force)
        {
            if (!StatsOn()) return;
            DateTime before = statsStamp;
            LoadStats();
            if (!force && statsStamp == before) return;
            if (statsCanvas == null || statsCanvas.IsDisposed) { ShowSection(StatsPage); return; }
            RedrawStats();
        }

        StatsCanvas NewStatsCanvas()
        {
            StatsCanvas sc = new StatsCanvas(this);
            sc.TabClick = delegate(string key)
            {
                if (statsTab == key) return;
                statsTab = key;
                RedrawStats();
            };
            sc.KillsClick = delegate
            {
                killsOpen = !killsOpen;
                RedrawStats();
            };
            return sc;
        }

        void RedrawStats()
        {
            if (statsCanvas == null || statsCanvas.IsDisposed) return;
            FillStatsCanvas(statsCanvas);
            LayoutRows();
        }

        void FillStatsCanvas(StatsCanvas sc)
        {
            List<string> keys = new List<string>();
            keys.Add("All");
            keys.AddRange(StatTabs());
            if (!keys.Contains(statsTab)) statsTab = "All";
            sc.Clear();
            sc.Tiles = StatsLayoutTiles();
            foreach (string k in keys) sc.AddTab(k, StatTabName(k), StatTabIcon(k), k == statsTab);
            string group = null;
            bool killsPending = false;
            for (int i = 0; i < StatRowKeys.Length; i++)
            {
                if (StatRowGroups[i] != group)
                {
                    if (killsPending) { AddKillItems(sc); killsPending = false; }
                    sc.AddHeading(StatRowGroups[i].ToUpperInvariant(), HeadingSize, group == null);
                    group = StatRowGroups[i];
                }
                string key = StatRowKeys[i][0];
                if (key == "most_weapon" && statsTab != "All") continue;
                bool isKills = key == "kills";
                string title = isKills ? StatNames[i] + (killsOpen ? "  ▾" : "  ▸") : StatNames[i];
                string tip = isKills ? "Click to see kills for each behemoth" : key == "procs" ? ProcsTip(statsTab) : null;
                double num = IsTextStat(key) ? double.NaN : StatValue(statsTab, key);
                sc.AddStat(key, title, StatText(statsTab, key), num, StatColor(key), tip, isKills, isKills && killsOpen);
                if (isKills && killsOpen)
                {
                    if (sc.Tiles) killsPending = true;
                    else AddKillItems(sc);
                }
            }
            if (killsPending) AddKillItems(sc);
            string note = HasStats(statsTab)
                ? "Stats are tracked during hunts while Hunt Stats is enabled. Damage, kills and staggers are credited to the weapon that's equipped when they occur."
                : (statsTab == "All"
                    ? "No stats recorded yet. Stats will be tracked from your next hunt while Hunt Stats is enabled."
                    : "No stats yet for " + StatTabName(statsTab) + ". Use it in a hunt while Hunt Stats is enabled.");
            sc.AddNote(note);
        }

        void AddKillItems(StatsCanvas sc)
        {
            Dictionary<string, double> kills = KillsByBehemoth();
            List<string> names = new List<string>();
            foreach (string n in KillOrder) if (kills.ContainsKey(n)) names.Add(n);
            if (kills.ContainsKey("Other")) names.Add("Other");
            if (names.Count == 0) { sc.AddKillNone("No behemoth kills recorded yet."); return; }
            foreach (string n in names) sc.AddKill(n, FormatStat(kills[n]));
        }

        class ScItem
        {
            public int Kind;
            public string Key, Text, Value, Tip, Shown, ShownTip;
            public double Num = double.NaN;
            public float Pt;
            public Color Color;
            public Image Pic;
            public bool On, Clickable, First;
            public Rectangle Bounds, NameRect, ValueRect;
        }

        class StatsCanvas : Control
        {
            const int KTab = 0, KHead = 1, KStat = 2, KKill = 3, KNone = 4, KNote = 5;
            const TextFormatFlags OneLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            const TextFormatFlags Wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

            readonly SettingsWindow win;
            readonly List<ScItem> items = new List<ScItem>();
            ScItem hot, down;
            string tip;
            public bool Tiles;
            public Action<string> TabClick;
            public Action KillsClick;

            public StatsCanvas(SettingsWindow win)
            {
                this.win = win;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = CBg;
                Margin = Padding.Empty;
            }

            public void Clear()
            {
                items.Clear();
                hot = null;
                down = null;
                Cursor = Cursors.Default;
                if (tip != null) { tip = null; win.tips.SetToolTip(this, null); }
            }

            ScItem Add(int kind, string key, string text)
            {
                ScItem it = new ScItem();
                it.Kind = kind;
                it.Key = key;
                it.Text = text;
                items.Add(it);
                return it;
            }

            public void AddTab(string key, string text, Image pic, bool on)
            {
                ScItem it = Add(KTab, key, text);
                it.Pic = pic;
                it.On = on;
                it.Clickable = true;
            }

            public void AddHeading(string text, float pt, bool first)
            {
                ScItem it = Add(KHead, null, text);
                it.Pt = pt;
                it.First = first;
            }

            public void AddStat(string key, string title, string value, double num, Color c, string tipText, bool clickable, bool on)
            {
                ScItem it = Add(KStat, key, title);
                it.Value = value;
                it.Num = num;
                it.Color = c;
                it.Tip = tipText;
                it.Clickable = clickable;
                it.On = on;
            }

            public void AddKill(string name, string value)
            {
                ScItem it = Add(KKill, null, name);
                it.Value = value;
            }

            public void AddKillNone(string text) { Add(KNone, null, text); }

            public void AddNote(string text) { Add(KNote, null, text); }

            static Size Measure(string text, Font f) { return TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), OneLine); }

            static Size MeasureWrap(string text, Font f, int w) { return TextRenderer.MeasureText(text, f, new Size(Math.Max(1, w), int.MaxValue), Wrap); }

            Font NameFont(ScItem it) { return Tiles ? Ui.UiFont(9f, FontStyle.Bold) : Ui.UiFont(10.5f, FontStyle.Bold); }

            Font ValueFont(ScItem it)
            {
                if (!Tiles) return Ui.UiFont(12f, FontStyle.Bold);
                return Ui.UiFont(double.IsNaN(it.Num) ? 11.5f : 15f, FontStyle.Bold);
            }

            void FitValue(ScItem it, Font f, int maxW)
            {
                it.Shown = it.Value;
                it.ShownTip = it.Tip;
                if (double.IsNaN(it.Num)) return;
                string full = FormatStat(it.Num);
                if (TextRenderer.MeasureText(full, f).Width <= maxW) { it.Shown = full; return; }
                it.Shown = ShortStat(it.Num);
                if (it.Clickable) return;
                it.ShownTip = it.Tip == null ? full : full + "\n" + it.Tip;
            }

            public void LayoutAt(int w)
            {
                w = Math.Max(Px(300), w);
                int y = 0, gap = Px(8), i = 0;
                while (i < items.Count)
                {
                    ScItem it = items[i];
                    if (it.Kind == KTab)
                    {
                        int tg = Px(6), tabW = Math.Max(Px(60), (w - 3 * tg) / 4), col = 0;
                        for (; i < items.Count && items[i].Kind == KTab; i++)
                        {
                            if (col == 4) { col = 0; y += Px(36); }
                            items[i].Bounds = new Rectangle(col * (tabW + tg), y, tabW, Px(30));
                            col++;
                        }
                        y += Px(36) + Px(12);
                        continue;
                    }
                    if (it.Kind == KHead)
                    {
                        if (!it.First) y += Px(10);
                        int h = Measure(it.Text, Ui.UiFont(it.Pt, FontStyle.Bold)).Height;
                        it.Bounds = new Rectangle(0, y, w, h);
                        it.NameRect = new Rectangle(Px(2), y - Px(2), w - Px(2), h);
                        y += h + Px(6);
                        i++;
                        continue;
                    }
                    if (it.Kind == KStat && Tiles)
                    {
                        int side = Math.Max(Px(90), (w - 3 * gap) / 4);
                        int end = i;
                        while (end < items.Count && items[end].Kind == KStat) end++;
                        int tileH = 0;
                        Size[] ns = new Size[end - i], vs = new Size[end - i];
                        for (int k = i; k < end; k++)
                        {
                            ScItem t = items[k];
                            Font vf = ValueFont(t);
                            FitValue(t, vf, side - Px(12));
                            ns[k - i] = MeasureWrap(t.Text, NameFont(t), side - Px(16));
                            vs[k - i] = double.IsNaN(t.Num) ? MeasureWrap(t.Shown, vf, side - Px(12)) : Measure(t.Shown, vf);
                            tileH = Math.Max(tileH, ns[k - i].Height + vs[k - i].Height + Px(34));
                        }
                        tileH = Math.Min(side, Math.Max(Px(64), tileH));
                        int col = 0;
                        for (int k = i; k < end; k++)
                        {
                            if (col == 4) { col = 0; y += tileH + gap; }
                            ScItem t = items[k];
                            int x = col * (side + gap);
                            t.Bounds = new Rectangle(x, y, side, tileH);
                            Size n = ns[k - i], v = vs[k - i];
                            t.NameRect = new Rectangle(x + (side - n.Width) / 2, y + Px(12), n.Width, n.Height);
                            t.ValueRect = new Rectangle(x + (side - v.Width) / 2, y + tileH - v.Height - Px(12), v.Width, v.Height);
                            col++;
                        }
                        y += tileH + gap + Px(2);
                        i = end;
                        continue;
                    }
                    if (it.Kind == KStat)
                    {
                        Font vf = ValueFont(it);
                        FitValue(it, vf, Math.Max(Px(80), w / 2));
                        Size n = Measure(it.Text, NameFont(it)), v = Measure(it.Shown, vf);
                        int h = Math.Max(Px(40), Math.Max(n.Height, v.Height) + Px(16));
                        it.Bounds = new Rectangle(0, y, w, h);
                        it.NameRect = new Rectangle(Px(16), y + (h - n.Height) / 2, n.Width, n.Height);
                        it.ValueRect = new Rectangle(w - v.Width - Px(16), y + (h - v.Height) / 2, v.Width, v.Height);
                        y += h + Px(6);
                        i++;
                        continue;
                    }
                    if (it.Kind == KKill)
                    {
                        Size n = Measure(it.Text, Ui.UiFont(9.5f)), v = Measure(it.Value, Ui.UiFont(10f, FontStyle.Bold));
                        int x0 = Px(24), rw = Math.Max(Px(100), w - Px(24));
                        int h = Math.Max(Px(28), Math.Max(n.Height, v.Height) + Px(8));
                        it.Bounds = new Rectangle(x0, y, rw, h);
                        it.NameRect = new Rectangle(x0 + Px(14), y + (h - n.Height) / 2, n.Width, n.Height);
                        it.ValueRect = new Rectangle(x0 + rw - v.Width - Px(16), y + (h - v.Height) / 2, v.Width, v.Height);
                        y += h + Px(3);
                        i++;
                        continue;
                    }
                    int left = it.Kind == KNone ? Px(26) : Px(2);
                    if (it.Kind == KNote) y += Px(4);
                    int tw = Math.Max(Px(100), w - Px(4) - left);
                    Size s = MeasureWrap(it.Text, Ui.UiFont(9f), tw);
                    it.Bounds = new Rectangle(left, y, tw, s.Height);
                    y += s.Height + (it.Kind == KNone ? Px(8) : Px(3));
                    i++;
                }
                Size want = new Size(w, Math.Max(1, y));
                if (Size != want) Size = want;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(CBg);
                Rectangle clip = e.ClipRectangle;
                foreach (ScItem it in items)
                {
                    if (!it.Bounds.IntersectsWith(clip)) continue;
                    switch (it.Kind)
                    {
                        case KTab: PaintTab(g, it); break;
                        case KHead:
                            TextRenderer.DrawText(g, it.Text, Ui.UiFont(it.Pt, FontStyle.Bold), it.NameRect.Location, CGold, OneLine);
                            break;
                        case KStat: PaintStat(g, it); break;
                        case KKill:
                            PaintCardAt(g, it.Bounds, Px(10), CBorder);
                            TextRenderer.DrawText(g, it.Text, Ui.UiFont(9.5f), it.NameRect.Location, CText, OneLine);
                            TextRenderer.DrawText(g, it.Value, Ui.UiFont(10f, FontStyle.Bold), it.ValueRect.Location, CText, OneLine);
                            break;
                        default:
                            TextRenderer.DrawText(g, it.Text, Ui.UiFont(9f), it.Bounds, CSub, Wrap);
                            break;
                    }
                }
            }

            static void PaintCardAt(Graphics g, Rectangle b, int radius, Color edge)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(b.X, b.Y, b.Width - 1, b.Height - 1), radius))
                {
                    using (SolidBrush f = new SolidBrush(CRow)) g.FillPath(f, path);
                    using (Pen p = new Pen(edge)) g.DrawPath(p, path);
                }
            }

            void PaintTab(Graphics g, ScItem it)
            {
                bool isHot = it == hot;
                Color baseFill = it.On ? CPill : CBtn, baseEdge = it.On ? CPill : CBorder;
                Color fill = !isHot ? baseFill : it.On ? Mix(baseFill, Color.White, 0.08) : CBtnHover;
                Color edge = isHot && !it.On ? Mix(CBorder, CText, 0.18) : baseEdge;
                if (isHot && it == down && (Control.MouseButtons & MouseButtons.Left) != 0)
                {
                    fill = Mix(fill, Color.White, 0.12);
                    edge = CHover;
                }
                Rectangle r = it.Bounds;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Px(8)))
                {
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (Pen pen = new Pen(edge)) g.DrawPath(pen, path);
                }
                Font f = Ui.UiFont(9.5f, it.On ? FontStyle.Bold : FontStyle.Regular);
                Color tc = it.On ? OnAccentText(CPill) : CText;
                int iw = it.Pic != null ? Px(26) : 0;
                int ts = TextRenderer.MeasureText(g, it.Text, f, new Size(Px(1000), r.Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                int x = r.X + Math.Max(Px(6), (r.Width - (iw + ts)) / 2);
                if (it.Pic != null)
                {
                    int s = Px(20);
                    InterpolationMode im = g.InterpolationMode;
                    PixelOffsetMode pm = g.PixelOffsetMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(it.Pic, new Rectangle(x, r.Y + (r.Height - s) / 2, s, s));
                    g.InterpolationMode = im;
                    g.PixelOffsetMode = pm;
                }
                TextRenderer.DrawText(g, it.Text, f, new Rectangle(x + iw, r.Y, Math.Max(1, r.Right - x - iw - Px(4)), r.Height), tc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | OneLine | TextFormatFlags.EndEllipsis);
            }

            void PaintStat(Graphics g, ScItem it)
            {
                if (Tiles) PaintCardAt(g, it.Bounds, Px(8), it.On ? CGold : CBorder);
                else PaintCardAt(g, it.Bounds, Px(10), CBorder);
                Font nf = NameFont(it), vf = ValueFont(it);
                if (Tiles)
                {
                    TextRenderer.DrawText(g, it.Text, nf, it.NameRect, CSub, Wrap | TextFormatFlags.HorizontalCenter);
                    TextFormatFlags vflags = double.IsNaN(it.Num) ? Wrap | TextFormatFlags.HorizontalCenter : OneLine | TextFormatFlags.HorizontalCenter;
                    TextRenderer.DrawText(g, it.Shown, vf, it.ValueRect, it.Color, vflags);
                }
                else
                {
                    TextRenderer.DrawText(g, it.Text, nf, it.NameRect.Location, CText, OneLine);
                    TextRenderer.DrawText(g, it.Shown, vf, it.ValueRect.Location, it.Color, OneLine);
                }
            }

            ScItem HitAt(Point pt)
            {
                foreach (ScItem it in items)
                    if ((it.Kind == KTab || it.Kind == KStat) && it.Bounds.Contains(pt)) return it;
                return null;
            }

            void SetHot(ScItem it)
            {
                if (it == hot) return;
                if (hot != null && hot.Kind == KTab) Invalidate(hot.Bounds);
                hot = it;
                if (hot != null && hot.Kind == KTab) Invalidate(hot.Bounds);
                Cursor = it != null && it.Clickable ? Cursors.Hand : Cursors.Default;
                string t = it == null ? null : it.Kind == KStat ? it.ShownTip : null;
                if (t != tip)
                {
                    tip = t;
                    win.tips.SetToolTip(this, t);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHot(HitAt(e.Location));
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                SetHot(null);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                ScItem it = HitAt(e.Location);
                SetHot(it);
                down = it != null && it.Clickable ? it : null;
                if (down != null && down.Kind == KTab) Invalidate(down.Bounds);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                ScItem d = down;
                down = null;
                if (d == null) return;
                if (d.Kind == KTab) Invalidate(d.Bounds);
                ScItem at = HitAt(e.Location);
                SetHot(at);
                if (at != d) return;
                if (d.Kind == KTab)
                {
                    string key = d.Key;
                    if (TabClick != null) win.BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) TabClick(key); });
                }
                else if (KillsClick != null) win.BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) KillsClick(); });
            }
        }

        const string BuildPage = "Build Search";
        const string StatsSettings = "Hunt Stats Settings";
        const string BuildSettings = "Build Search Settings";
        int bsExtra;

        static string NavLabel(string section)
        {
            if (section == StatsPage) return "Lifetime Stats";
            if (section == BuildPage) return "Find Builds";
            return section;
        }

        int BuildsShown()
        {
            foreach (SettingDef d in defs)
            {
                if (d.Key != "build_count") continue;
                double v;
                if (double.TryParse(Value(d), NumberStyles.Float, Inv, out v)) return Math.Max(1, Math.Min(25, (int)Math.Round(v)));
            }
            return 5;
        }
        Button buildNav;
        string bsType = "Axe", bsWeapon = "", bsBond;
        readonly Dictionary<string, string[]> bsTypeMemory = new Dictionary<string, string[]>();
        string bsLantern = "", bsOmnicell = "";
        readonly string[] bsParts = new string[] { "", "", "" };
        readonly string[] bsArmour = new string[] { "", "", "", "" };
        bool bsOwnedOnly
        {
            get
            {
                foreach (SettingDef d in defs) if (d.Key == "owned_only") return Normalize(d, Value(d)) == "true";
                return false;
            }
        }

        void SyncOwned()
        {
            BsOwned.Refresh();
            BuildSearch.OwnedOnly = bsOwnedOnly;
            BuildSearch.FixedArmour = bsArmour;
        }

        float HeadingSize
        {
            get
            {
                foreach (SettingDef d in defs)
                    if (d.Key == "heading_size")
                    {
                        double v;
                        if (double.TryParse(Value(d), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                            return (float)Math.Max(8, Math.Min(16, v));
                    }
                return 10f;
            }
        }

        bool bsAllRanks
        {
            get
            {
                foreach (SettingDef d in defs) if (d.Key == "cell_ranks") return Normalize(d, Value(d)) == "all";
                return false;
            }
        }
        string BsRanksText(BsPerk p, int width, int current)
        {
            List<string> rows = new List<string>();
            int[] ranks = bsAllRanks ? new int[] { 1, 2, 3, 4, 5, 6 } : new int[] { 3, 6 };
            foreach (int r in ranks)
            {
                string t = p.RankText(r);
                if (string.IsNullOrEmpty(t)) continue;
                rows.Add(BsWrap("+" + r + (r == current ? " (yours)" : "") + ": " + t, width));
            }
            return string.Join("\n\n", rows.ToArray());
        }
        readonly Dictionary<string, int> bsReq = new Dictionary<string, int>();
        readonly Dictionary<string, int> bsAuto = new Dictionary<string, int>();
        readonly Dictionary<string, int> bsAutoWeapon = new Dictionary<string, int>();
        readonly Dictionary<string, int> bsAutoArmor = new Dictionary<string, int>();

        void UpdateAutoPerks()
        {
            bsAuto.Clear();
            bsAutoWeapon.Clear();
            bsAutoArmor.Clear();
            BsWeapon w = BuildSearch.FindWeapon(bsWeapon);
            BsWeapon bond = string.IsNullOrEmpty(bsBond) ? null : BuildSearch.FindWeapon(bsBond);
            List<string> given = new List<string>();
            if (w != null) given.AddRange(w.Perks);
            if (w != null && w.Legendary && bond != null) given.AddRange(bond.Perks);
            List<string> armorGiven = new List<string>();
            for (int pc = 0; pc < bsArmour.Length; pc++)
            {
                if (string.IsNullOrEmpty(bsArmour[pc])) continue;
                foreach (BsArmour a in BuildSearch.Armours)
                    if (a.Piece == BuildSearch.Pieces[pc] && a.Name == bsArmour[pc]) { if (!string.IsNullOrEmpty(a.Perk)) armorGiven.Add(a.Perk); break; }
            }
            foreach (string p in given) AddAuto(bsAutoWeapon, p);
            foreach (string p in armorGiven) AddAuto(bsAutoArmor, p);
            foreach (string p in given) AddAuto(bsAuto, p);
            foreach (string p in armorGiven) AddAuto(bsAuto, p);
            foreach (KeyValuePair<string, int> kv in bsAuto)
            {
                int u;
                if (!bsReq.TryGetValue(kv.Key, out u)) continue;
                u = Math.Min(u, 2 - kv.Value);
                if (u <= 0) bsReq.Remove(kv.Key); else bsReq[kv.Key] = u;
            }
        }

        static void AddAuto(Dictionary<string, int> d, string perk)
        {
            int v;
            d.TryGetValue(perk, out v);
            d[perk] = Math.Min(2, v + 1);
        }

        static string GivesText(string who, Dictionary<string, int> d)
        {
            if (d.Count == 0) return "";
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in d) parts.Add("+" + (kv.Value * 3) + " " + kv.Key);
            return " (" + who + " gives " + string.Join(", ", parts.ToArray()) + ")";
        }

        int AutoOf(string perk) { int v; bsAuto.TryGetValue(perk, out v); return v; }
        string AutoFrom(string perk)
        {
            bool wp = bsAutoWeapon.ContainsKey(perk), ar = bsAutoArmor.ContainsKey(perk);
            return wp && ar ? "weapon and armor" : ar ? "armor" : "weapon";
        }

        int UserOf(string perk) { int v; bsReq.TryGetValue(perk, out v); return v; }
        int TotalOf(string perk) { return Math.Min(2, AutoOf(perk) + UserOf(perk)); }

        Dictionary<string, int> BuildReq()
        {
            Dictionary<string, int> req = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> kv in bsAuto) req[kv.Key] = TotalOf(kv.Key);
            foreach (KeyValuePair<string, int> kv in bsReq) req[kv.Key] = TotalOf(kv.Key);
            return req;
        }
        readonly Dictionary<string, FbItem> bsChips = new Dictionary<string, FbItem>();
        FlowLayoutPanel bsResults;
        volatile int bsGen;

        class BsItem
        {
            public string Name, Text, Aff = "";
            public BsItem(string name, string text) { Name = name; Text = text; }
            public bool Header { get { return Name == null; } }
            public override string ToString() { return Text; }
        }

        void ClearBuildSearch()
        {
            bsReq.Clear();
            bsWeapon = "";
            bsBond = null;
            bsLantern = "";
            bsOmnicell = "";
            for (int i = 0; i < bsParts.Length; i++) bsParts[i] = "";
            for (int i = 0; i < bsArmour.Length; i++) bsArmour[i] = "";
            bsTypeMemory.Clear();
            RefreshSection();
        }

        static string BsFamily(BsWeapon w)
        {
            if (w == null || string.IsNullOrEmpty(w.Icon)) return "";
            int k = w.Icon.LastIndexOf('/');
            return k < 0 ? "" : w.Icon.Substring(0, k).ToLowerInvariant();
        }

        static BsWeapon BsSibling(BsWeapon w, string type)
        {
            string fam = BsFamily(w);
            if (fam == "") return null;
            foreach (BsWeapon o in BuildSearch.Weapons)
                if (o.Type == type && o.Legendary == w.Legendary && o.Exotic == w.Exotic && o.Element == w.Element && BsFamily(o) == fam) return o;
            return null;
        }

        bool BuildOn()
        {
            SettingDef d = BuildDef();
            return d != null && Normalize(d, Value(d)) == "true";
        }

        SettingDef BuildDef()
        {
            foreach (SettingDef d in defs) if (d.Key == "build_search") return d;
            return null;
        }

        void UpdateBuildNav()
        {
            if (section != BuildPage) return;
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { RefreshSection(); });
            else ShowSection(BuildPage);
        }

        void AddBuildIntro()
        {
            Label what = MakeLabel("Choose a weapon and your desired perks to find armor combinations and the cells needed to complete the selected build."
                + " Prismatic slots, legendary bonds and lantern cell slots are all taken into account.", 10f, FontStyle.Regular, CText);
            what.Tag = "wrap";
            what.Margin = new Padding(Px(2), 0, 0, Px(14));
            list.Controls.Add(what);
            Button on = MakeButton("Turn On Build Search", Px(180));
            on.ForeColor = CText;
            on.FlatAppearance.BorderColor = CGold;
            on.Margin = new Padding(0, 0, 0, Px(12));
            on.Click += delegate
            {
                SettingDef d = BuildDef();
                if (d == null) return;
                Change(d, "true");
                SaveNow();
            };
            list.Controls.Add(on);
            Label off = MakeLabel("You can turn it off again in Build Search > Settings.", 9f, FontStyle.Regular, CSub);
            off.Margin = new Padding(Px(2), 0, 0, 0);
            list.Controls.Add(off);
        }

        static string BsSlotName(string s)
        {
            return string.IsNullOrEmpty(s) ? "none" : s;
        }

        static string BsWeaponText(BsWeapon w)
        {
            string t = w.Name + "  (" + BsSlotName(w.Slot1) + " / " + BsSlotName(w.Slot2) + ")";
            if (w.Exotic) t += "  Exotic";
            return t;
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return CPanel; } }
            public override Color ImageMarginGradientBegin { get { return CPanel; } }
            public override Color ImageMarginGradientMiddle { get { return CPanel; } }
            public override Color ImageMarginGradientEnd { get { return CPanel; } }
            public override Color MenuBorder { get { return CBorder; } }
            public override Color MenuItemBorder { get { return CGold; } }
            public override Color MenuItemSelected { get { return CGoldBg; } }
            public override Color MenuItemSelectedGradientBegin { get { return CGoldBg; } }
            public override Color MenuItemSelectedGradientEnd { get { return CGoldBg; } }
            public override Color SeparatorDark { get { return CBorder; } }
            public override Color SeparatorLight { get { return CBorder; } }
        }

        class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.ForeColor;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = CText;
                base.OnRenderArrow(e);
            }
        }

        void OpenListDrop(Control anchor, List<BsItem> items, string current, int width, Action<string> picked)
        {
            if (DropJustClosed(anchor)) return;
            ListBox lb = new ListBox();
            lb.BorderStyle = BorderStyle.None;
            lb.DrawMode = DrawMode.OwnerDrawFixed;
            lb.IntegralHeight = false;
            lb.ItemHeight = Px(26);
            lb.BackColor = CPanel;
            lb.ForeColor = CText;
            lb.Font = Ui.UiFont(9.5f);
            int widest = Px(width);
            int currentIndex = -1;
            using (Font bold = new Font("Segoe UI", Ui.Pt(9.5f), FontStyle.Bold))
                foreach (BsItem it in items)
                {
                    if (!it.Header && it.Name == current) currentIndex = lb.Items.Count;
                    lb.Items.Add(it);
                    widest = Math.Max(widest, TextRenderer.MeasureText(it.Text, bold).Width + Px(40));
                }
            int hot = -1;
            lb.DrawItem += delegate(object o, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                BsItem it = (BsItem)lb.Items[e.Index];
                bool on = e.Index == hot && !it.Header;
                using (SolidBrush fill = new SolidBrush(on ? CGoldBg : CPanel)) e.Graphics.FillRectangle(fill, e.Bounds);
                Rectangle tr = new Rectangle(e.Bounds.X + Px(10), e.Bounds.Y, e.Bounds.Width - Px(14), e.Bounds.Height);
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                if (it.Header)
                {
                    TextRenderer.DrawText(e.Graphics, it.Text.ToUpperInvariant(), Ui.UiFont(8.5f, FontStyle.Bold), tr, GroupColor(it.Text), flags);
                    return;
                }
                bool isCur = e.Index == currentIndex;
                TextRenderer.DrawText(e.Graphics, it.Text, Ui.UiFont(9.5f, isCur ? FontStyle.Bold : FontStyle.Regular), tr, AffinityColor(it.Aff ?? ""), flags);
            };
            lb.MouseMove += delegate(object o, MouseEventArgs e)
            {
                int i = lb.IndexFromPoint(e.Location);
                if (i == hot) return;
                int old = hot;
                hot = i;
                if (old >= 0 && old < lb.Items.Count) lb.Invalidate(lb.GetItemRectangle(old));
                if (hot >= 0 && hot < lb.Items.Count) lb.Invalidate(lb.GetItemRectangle(hot));
            };
            lb.HandleCreated += delegate { try { SetWindowTheme(lb.Handle, "DarkMode_Explorer", null); } catch { } };
            int h = Math.Min(items.Count * lb.ItemHeight, Px(440));
            lb.Size = new Size(widest + SystemInformation.VerticalScrollBarWidth, h);

            ToolStripControlHost host = new ToolStripControlHost(lb);
            host.AutoSize = false;
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.Size = lb.Size;
            ToolStripDropDown dd = new ToolStripDropDown();
            dd.Padding = new Padding(1);
            dd.BackColor = CBorder;
            dd.Items.Add(host);
            Action<int> choose = delegate(int i)
            {
                if (i < 0 || i >= lb.Items.Count) return;
                BsItem it = (BsItem)lb.Items[i];
                if (it.Header) return;
                dd.Close();
                if (it.Name == current) return;
                string name = it.Name;
                BeginInvoke((MethodInvoker)delegate { picked(name); });
            };
            lb.MouseUp += delegate(object o, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) choose(lb.IndexFromPoint(e.Location));
            };
            lb.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) choose(lb.SelectedIndex);
            };
            dd.Closed += delegate { if (openDrop == dd) openDrop = null; BeginInvoke((MethodInvoker)delegate { dd.Dispose(); }); };
            if (currentIndex >= 0 && !string.IsNullOrEmpty(current)) lb.TopIndex = Math.Max(0, currentIndex - 3);
            openDrop = dd;
            dd.Show(anchor, new Point(0, anchor.Height));
            RoundPopup(dd);
            FollowAnchor(dd, anchor, lb);
            lb.Focus();
        }

        static readonly Dictionary<string, Image> bsIcons = new Dictionary<string, Image>();

        Image BsIcon(string type)
        {
            return string.IsNullOrEmpty(type) ? null : BsImage(type + ".png");
        }

        Image BsImage(string rel)
        {
            return BsImageAt(ModPaths.Root(), rel);
        }

        static Image BsImageAt(string modDir, string rel)
        {
            Image img;
            if (string.IsNullOrEmpty(rel)) return null;
            if (bsIcons.TryGetValue(rel, out img)) return img;
            img = null;
            try
            {
                string path = System.IO.Path.Combine(modDir, "icons", rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))
                    using (Image raw = Image.FromStream(ms))
                        img = new Bitmap(raw);
            }
            catch { img = null; }
            bsIcons[rel] = img;
            return img;
        }

        static bool IsRecruit(string name)
        {
            return name != null && name.StartsWith("Recruit's", StringComparison.Ordinal);
        }

        static string ArmourEffect(BsArmour a)
        {
            return a == null || string.IsNullOrEmpty(a.Effect) ? "" : "Unique Effect:\n" + BsWrap(a.Effect, 70);
        }

        static string ArmourTip(BsArmour a)
        {
            string fx = ArmourEffect(a);
            return "Perk: " + (string.IsNullOrEmpty(a.Perk) ? "None" : a.Perk + " +3") + (fx == "" ? "" : "\n\n" + fx);
        }

        void RoundPopup(ToolStripDropDown d)
        {
            if (d == null || !d.IsHandleCreated) return;
            bool ok = false;
            try { int pref = 2; ok = DwmSetWindowAttribute(d.Handle, 33, ref pref, 4) == 0; } catch { }
            if (!ok) try { SetWindowRgn(d.Handle, CreateRoundRectRgn(0, 0, d.Width + 1, d.Height + 1, Px(12), Px(12)), true); } catch { }
        }

        static string ArmourGroup(BsArmour a)
        {
            return string.IsNullOrEmpty(a.Strength) ? "No Element" : a.Strength;
        }

        void OpenArmourDrop(Control anchor, List<BsArmour> armour, string noneText, string current, Action<string> picked)
        {
            if (DropJustClosed(anchor)) return;
            int tileW = Px(116), tileH = Px(150), gap = Px(6);
            Rectangle win = RectangleToScreen(ClientRectangle);
            int fitW = win.Width - Px(24) - Px(14) - gap - SystemInformation.VerticalScrollBarWidth - Px(2);
            int cols = Math.Max(2, Math.Min(5, (fitW + gap) / (tileW + gap)));
            int rowW = cols * (tileW + gap) - gap;
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.AutoScroll = true;
            flow.BackColor = CPanel;
            flow.Padding = new Padding(Px(10), Px(10), Px(4), Px(6));
            flow.HandleCreated += delegate { try { SetWindowTheme(flow.Handle, "DarkMode_Explorer", null); } catch { } };
            ToolStripDropDown dd = new ToolStripDropDown();
            WeaponTile currentTile = null;
            int contentH = flow.Padding.Vertical;
            WeaponTile none = new WeaponTile();
            none.Caption = noneText;
            none.Current = (current ?? "") == "";
            none.Size = new Size(tileW, tileH);
            none.Margin = new Padding(0, 0, gap, gap);
            none.Click += delegate
            {
                dd.Close();
                if ((current ?? "") == "") return;
                BeginInvoke((MethodInvoker)delegate { picked(""); });
            };
            List<string> keys = new List<string>(BuildSearch.Elements);
            keys.Add("No Element");
            foreach (BsArmour a in armour) if (!keys.Contains(ArmourGroup(a))) keys.Add(ArmourGroup(a));
            foreach (string key in keys)
            {
                List<BsArmour> group = new List<BsArmour>();
                foreach (BsArmour a in armour) if (ArmourGroup(a) == key) group.Add(a);
                if (group.Count == 0) continue;
                Label head = MakeLabel(key.ToUpperInvariant(), 8.5f, FontStyle.Bold, GroupColor(key));
                head.AutoSize = false;
                head.Size = new Size(rowW, Px(20));
                head.Margin = new Padding(Px(2), Px(6), 0, Px(6));
                flow.Controls.Add(head);
                contentH += head.Height + head.Margin.Vertical;
                foreach (BsArmour a in group)
                {
                    WeaponTile t = new WeaponTile();
                    string name = a.Name;
                    t.Caption = a.Name;
                    t.Pic = BsImage(a.Icon);
                    t.Elem = string.IsNullOrEmpty(a.Strength) ? null : BsImage("elements/" + a.Strength + ".png");
                    t.SlotA = BsIcon(a.Slot);
                    t.Current = a.Name == current;
                    t.Size = new Size(tileW, tileH);
                    t.Margin = new Padding(0, 0, gap, gap);
                    WeaponTile tipTile = t;
                    string tipText = ArmourTip(a);
                    Image[] tipSlots = new Image[] { t.SlotA };
                    t.MouseEnter += delegate { ShowChipTipSoon(tipTile, tipText, tipSlots); };
                    t.MouseLeave += delegate { HideChipTip(); };
                    t.Click += delegate
                    {
                        dd.Close();
                        if (name == current) return;
                        BeginInvoke((MethodInvoker)delegate { picked(name); });
                    };
                    if (t.Current) currentTile = t;
                    flow.Controls.Add(t);
                }
                contentH += ((group.Count + cols - 1) / cols) * (tileH + gap);
            }
            if (flow.Controls.Count > 0) flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);
            flow.Controls.Add(none);
            contentH += tileH + gap;
            Point under = anchor.PointToScreen(new Point(0, anchor.Height));
            int roomBelow = win.Bottom - Px(8) - under.Y, roomAbove = anchor.PointToScreen(Point.Empty).Y - win.Top - Px(8);
            bool above = roomBelow < Math.Min(contentH, Px(300)) && roomAbove > roomBelow;
            int maxH = Math.Max(Px(160), Math.Min(Px(520), above ? roomAbove : roomBelow));
            flow.Size = new Size(rowW + flow.Padding.Horizontal + gap + SystemInformation.VerticalScrollBarWidth, Math.Min(contentH, maxH));
            ToolStripControlHost host = new ToolStripControlHost(flow);
            host.AutoSize = false;
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.Size = flow.Size;
            dd.Padding = new Padding(1);
            dd.BackColor = CBorder;
            dd.Items.Add(host);
            dd.Closed += delegate { HideChipTip(); if (openDrop == dd) openDrop = null; BeginInvoke((MethodInvoker)delegate { dd.Dispose(); }); };
            int popW = flow.Width + 2, popH = flow.Height + 2;
            int px = Math.Max(win.Left + Px(8), Math.Min(under.X, win.Right - Px(8) - popW));
            int py = above ? anchor.PointToScreen(Point.Empty).Y - popH : under.Y;
            openDrop = dd;
            dd.Show(new Point(px, py));
            RoundPopup(dd);
            if (currentTile != null) flow.ScrollControlIntoView(currentTile);
            FollowAnchor(dd, anchor, flow);
            flow.Focus();
        }

        static Color GroupColor(string key)
        {
            if (key == "No Element") return Color.White;
            return Array.IndexOf(BuildSearch.Elements, key) >= 0 ? AffinityColor(key) : CGold;
        }

        static List<KeyValuePair<string, List<BsWeapon>>> WeaponGroups(List<BsWeapon> weapons)
        {
            List<KeyValuePair<string, List<BsWeapon>>> groups = new List<KeyValuePair<string, List<BsWeapon>>>();
            List<string> keys = new List<string>();
            keys.Add("Legendary");
            keys.AddRange(BuildSearch.Elements);
            keys.Add("No Element");
            foreach (string key in keys)
            {
                List<BsWeapon> g = new List<BsWeapon>();
                foreach (BsWeapon w in weapons)
                {
                    string k = w.Legendary ? "Legendary" : (w.Element == "" ? "No Element" : w.Element);
                    if (k == key) g.Add(w);
                }
                if (g.Count > 0) groups.Add(new KeyValuePair<string, List<BsWeapon>>(key, g));
            }
            foreach (BsWeapon w in weapons)
            {
                if (w.Legendary || w.Element == "" || keys.Contains(w.Element)) continue;
                bool found = false;
                foreach (KeyValuePair<string, List<BsWeapon>> kv in groups) if (kv.Key == w.Element) { kv.Value.Add(w); found = true; break; }
                if (!found) { List<BsWeapon> g = new List<BsWeapon>(); g.Add(w); groups.Add(new KeyValuePair<string, List<BsWeapon>>(w.Element, g)); }
            }
            return groups;
        }

        static string PlainEffect(BsWeapon w)
        {
            if (w == null || string.IsNullOrEmpty(w.Effect)) return "";
            string t = w.Effect.Trim();
            if (w.Legendary && t.StartsWith("Legendary Ability:", StringComparison.OrdinalIgnoreCase)) t = t.Substring("Legendary Ability:".Length).Trim();
            return t;
        }

        static string EffectText(BsWeapon w, int width)
        {
            string plain = PlainEffect(w);
            if (plain == "") return "";
            List<string> lines = new List<string>();
            foreach (string part in plain.Split('\n')) if (part.Trim() != "") lines.Add(BsWrap(part.Trim(), width));
            return string.Join("\n", lines.ToArray());
        }

        static string EffectsTip(BsWeapon w, BsWeapon bond)
        {
            if (w == null) return "";
            StringBuilder sb = new StringBuilder();
            if (w.Legendary)
            {
                if (EffectText(w, 70) != "") sb.Append("\n\nLegendary Effect:\n" + EffectText(w, 70));
                if (bond != null && EffectText(bond, 70) != "") sb.Append("\n\nUnique Effect (" + bond.Name + "):\n" + EffectText(bond, 70));
            }
            else if (EffectText(w, 70) != "") sb.Append("\n\nUnique Effect:\n" + EffectText(w, 70));
            return sb.ToString();
        }

        static string WeaponTip(BsWeapon w, BsWeapon bond)
        {
            if (w == null) return "";
            List<string> perks = new List<string>();
            foreach (string p in w.Perks) perks.Add(p + " +3");
            if (bond != null && w.Legendary) foreach (string p in bond.Perks) perks.Add(p + " +3 (bond)");
            string fx = EffectsTip(w, w.Legendary ? bond : null).Trim('\n');
            if (fx == "") fx = "Unique Effect:\nNone";
            return "Perk: " + (perks.Count == 0 ? "None" : string.Join(", ", perks.ToArray())) + "\n\n" + fx;
        }

        bool WeaponTiles()
        {
            foreach (SettingDef d in defs) if (d.Key == "weapon_layout") return Normalize(d, Value(d)) == "tiles";
            return true;
        }

        static string Opposite(string el)
        {
            switch (el ?? "")
            {
                case "Blaze": return "Frost";
                case "Frost": return "Blaze";
                case "Shock": return "Terra";
                case "Terra": return "Shock";
                case "Radiant": return "Umbral";
                case "Umbral": return "Radiant";
            }
            return "";
        }

        static string TypeIcon(string type)
        {
            foreach (BsWeapon w in BuildSearch.Weapons)
                if (w.Type == type && w.Name.StartsWith("Recruit's") && !string.IsNullOrEmpty(w.Icon)) return w.Icon;
            return "";
        }

        string SlotGearTip(BsGear g)
        {
            return g == null ? null : g.Name + (g.Desc != "" ? "\n\n" + BsWrap(g.Desc, 60) : "");
        }

        class WeaponTile : Control
        {
            public BsWeapon Weapon;
            public string Caption = "";
            public Image Pic, Elem, SlotA, SlotB;
            public bool Current;
            bool hover;

            public WeaponTile()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = CPanel;
                Font = Ui.UiFont(8.5f);
                Cursor = Cursors.Hand;
                Size = new Size(Px(116), Px(150));
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(CPanel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Px(6)))
                using (SolidBrush b = new SolidBrush(Current ? CGoldBg : hover ? CBtnHover : CBtn))
                using (Pen p = new Pen(Current ? CGold : hover ? CHover : CBorder))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                if (Pic != null) g.DrawImage(Pic, new Rectangle((Width - Px(72)) / 2, Px(8), Px(72), Px(72)));
                if (Elem != null) g.DrawImage(Elem, new Rectangle(Width - Px(26), Px(6), Px(20), Px(20)));
                Font f = Ui.UiFont(8.5f, Current ? FontStyle.Bold : FontStyle.Regular);
                if (Weapon == null && Pic != null)
                    TextRenderer.DrawText(g, Caption, f, new Rectangle(Px(6), Px(84), Width - Px(12), Px(32)), CText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                else if (Weapon == null)
                    TextRenderer.DrawText(g, Caption, f, new Rectangle(Px(6), 0, Width - Px(12), Height), CText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                else
                    TextRenderer.DrawText(g, Weapon.Name, f, new Rectangle(Px(6), Px(84), Width - Px(12), Px(32)), AffinityColor(Weapon.Element ?? ""),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                int y = Height - Px(28);
                if (SlotA != null && SlotB == null && Weapon == null) g.DrawImage(SlotA, new Rectangle((Width - Px(20)) / 2, y, Px(20), Px(20)));
                else if (SlotA != null) g.DrawImage(SlotA, new Rectangle(Width / 2 - Px(23), y, Px(20), Px(20)));
                if (SlotB != null) g.DrawImage(SlotB, new Rectangle(Width / 2 + Px(3), y, Px(20), Px(20)));
            }
        }

        void OpenWeaponDrop(Control anchor, List<BsWeapon> weapons, string current, string noneText, Action<string> picked)
        {
            if (DropJustClosed(anchor)) return;
            int tileW = Px(116), tileH = Px(150), gap = Px(6);
            Rectangle win = RectangleToScreen(ClientRectangle);
            int fitW = win.Width - Px(24) - Px(14) - gap - SystemInformation.VerticalScrollBarWidth - Px(2);
            int cols = Math.Max(2, Math.Min(5, (fitW + gap) / (tileW + gap)));
            int rowW = cols * (tileW + gap) - gap;
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.AutoScroll = true;
            flow.BackColor = CPanel;
            flow.Padding = new Padding(Px(10), Px(10), Px(4), Px(6));
            flow.HandleCreated += delegate { try { SetWindowTheme(flow.Handle, "DarkMode_Explorer", null); } catch { } };
            ToolStripDropDown dd = new ToolStripDropDown();
            WeaponTile currentTile = null;
            int contentH = flow.Padding.Vertical;
            WeaponTile none = new WeaponTile();
            none.Caption = noneText;
            none.Current = current == "";
            none.Size = new Size(tileW, tileH);
            none.Margin = new Padding(0, 0, gap, gap);
            none.Click += delegate
            {
                dd.Close();
                if (current == "") return;
                BeginInvoke((MethodInvoker)delegate { picked(""); });
            };
            foreach (KeyValuePair<string, List<BsWeapon>> grp in WeaponGroups(weapons))
            {
                List<BsWeapon> group = grp.Value;
                Label head = MakeLabel(grp.Key.ToUpperInvariant(), 8.5f, FontStyle.Bold, GroupColor(grp.Key));
                head.AutoSize = false;
                head.Size = new Size(rowW, Px(20));
                head.Margin = new Padding(Px(2), contentH > flow.Padding.Vertical ? Px(6) : 0, 0, Px(6));
                flow.Controls.Add(head);
                contentH += head.Height + head.Margin.Vertical;
                foreach (BsWeapon w in group)
                {
                    WeaponTile t = new WeaponTile();
                    t.Weapon = w;
                    t.Pic = BsImage(w.Icon);
                    t.Elem = string.IsNullOrEmpty(w.Element) ? null : BsImage("elements/" + w.Element + ".png");
                    t.SlotA = BsIcon(w.Slot1);
                    t.SlotB = BsIcon(w.Slot2);
                    t.Current = w.Name == current;
                    t.Size = new Size(tileW, tileH);
                    t.Margin = new Padding(0, 0, gap, gap);
                    string tip = WeaponTip(w, null);
                    WeaponTile tipTile = t;
                    string tipText = tip;
                    Image[] tipSlots = new Image[] { t.SlotA, t.SlotB };
                    t.MouseEnter += delegate { ShowChipTipSoon(tipTile, tipText, tipSlots); };
                    t.MouseLeave += delegate { HideChipTip(); };
                    string name = w.Name;
                    t.Click += delegate
                    {
                        dd.Close();
                        if (name == current) return;
                        BeginInvoke((MethodInvoker)delegate { picked(name); });
                    };
                    if (t.Current) currentTile = t;
                    flow.Controls.Add(t);
                }
                contentH += ((group.Count + cols - 1) / cols) * (tileH + gap);
            }
            if (flow.Controls.Count > 0) flow.SetFlowBreak(flow.Controls[flow.Controls.Count - 1], true);
            if (none.Current) currentTile = none;
            flow.Controls.Add(none);
            contentH += tileH + gap;
            Point under = anchor.PointToScreen(new Point(0, anchor.Height));
            int roomBelow = win.Bottom - Px(8) - under.Y, roomAbove = anchor.PointToScreen(Point.Empty).Y - win.Top - Px(8);
            bool above = roomBelow < Math.Min(contentH, Px(300)) && roomAbove > roomBelow;
            int maxH = Math.Max(Px(160), Math.Min(Px(520), above ? roomAbove : roomBelow));
            flow.Size = new Size(rowW + flow.Padding.Horizontal + gap + SystemInformation.VerticalScrollBarWidth, Math.Min(contentH, maxH));
            ToolStripControlHost host = new ToolStripControlHost(flow);
            host.AutoSize = false;
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.Size = flow.Size;
            dd.Padding = new Padding(1);
            dd.BackColor = CBorder;
            dd.Items.Add(host);
            dd.Closed += delegate { HideChipTip(); if (openDrop == dd) openDrop = null; BeginInvoke((MethodInvoker)delegate { dd.Dispose(); }); };
            int popW = flow.Width + 2, popH = flow.Height + 2;
            int px = Math.Max(win.Left + Px(8), Math.Min(under.X, win.Right - Px(8) - popW));
            int py = above ? anchor.PointToScreen(Point.Empty).Y - popH : under.Y;
            openDrop = dd;
            dd.Show(new Point(px, py));
            RoundPopup(dd);
            if (currentTile != null && currentTile != none) flow.ScrollControlIntoView(currentTile);
            else BeginInvoke((MethodInvoker)delegate { if (!flow.IsDisposed) flow.AutoScrollPosition = Point.Empty; });
            FollowAnchor(dd, anchor, flow);
            flow.Focus();
        }

        Control lastDropAnchor;
        int lastDropClosed;

        bool DropJustClosed(Control anchor)
        {
            return anchor == lastDropAnchor && unchecked(Environment.TickCount - lastDropClosed) < 400;
        }

        void FollowAnchor(ToolStripDropDown dd, Control anchor, Control keys)
        {
            dd.AutoClose = false;
            Point a0 = anchor.PointToScreen(Point.Empty);
            int dx = dd.Left - a0.X, dy = dd.Top - a0.Y;
            const MouseButtons anyButton = MouseButtons.Left | MouseButtons.Right | MouseButtons.Middle;
            bool wasDown = (Control.MouseButtons & anyButton) != 0;
            Timer t = new Timer();
            t.Interval = 15;
            t.Tick += delegate
            {
                if (dd.IsDisposed || !dd.Visible) { t.Stop(); return; }
                if (anchor.IsDisposed || !anchor.IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized) { dd.Close(); return; }
                bool down = (Control.MouseButtons & anyButton) != 0;
                if (down && !wasDown && !dd.Bounds.Contains(Control.MousePosition))
                {
                    if (anchor.RectangleToScreen(anchor.ClientRectangle).Contains(Control.MousePosition))
                    {
                        lastDropAnchor = anchor;
                        lastDropClosed = Environment.TickCount;
                    }
                    dd.Close();
                    return;
                }
                wasDown = down;
                Point a = anchor.PointToScreen(Point.Empty);
                Rectangle win = RectangleToScreen(ClientRectangle);
                int y = Math.Max(win.Top, Math.Min(a.Y + dy, win.Bottom - dd.Height));
                Point want = new Point(a.X + dx, y);
                if (dd.Location != want) dd.Location = want;
            };
            dd.Closed += delegate { t.Stop(); t.Dispose(); };
            keys.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) dd.Close(); };
            t.Start();
        }

        void OpenGearDrop(Control anchor, List<BsGear> gear, string none, string current, Action<string> picked, bool noNone = false)
        {
            if (DropJustClosed(anchor)) return;
            if (!WeaponTiles()) { OpenListDrop(anchor, GearItems(gear, none), current, anchor.Width, picked); return; }
            int tileW = Px(116), tileH = Px(124), gap = Px(6);
            Rectangle win = RectangleToScreen(ClientRectangle);
            int fitW = win.Width - Px(24) - Px(14) - gap - SystemInformation.VerticalScrollBarWidth - Px(2);
            int cols = Math.Max(2, Math.Min(5, (fitW + gap) / (tileW + gap)));
            int rowW = cols * (tileW + gap) - gap;
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.AutoScroll = true;
            flow.BackColor = CPanel;
            flow.Padding = new Padding(Px(10), Px(10), Px(4), Px(6));
            flow.HandleCreated += delegate { try { SetWindowTheme(flow.Handle, "DarkMode_Explorer", null); } catch { } };
            ToolStripDropDown dd = new ToolStripDropDown();
            WeaponTile currentTile = null;
            List<BsGear> all = new List<BsGear>();
            if (!noNone) all.Add(null);
            all.AddRange(gear);
            foreach (BsGear g in all)
            {
                WeaponTile t = new WeaponTile();
                string id = g != null ? g.Id : "";
                t.Caption = g != null ? g.Name : none;
                t.Pic = g != null ? BsImage(g.Icon) : null;
                t.Current = id == (current ?? "");
                t.Size = new Size(tileW, tileH);
                t.Margin = new Padding(0, 0, gap, gap);
                if (g != null)
                {
                    WeaponTile tipTile = t;
                    string tipText = g.Name + (g.Desc != "" ? "\n\n" + BsWrap(g.Desc, 60) : "");
                    t.MouseEnter += delegate { ShowChipTipSoon(tipTile, tipText); };
                    t.MouseLeave += delegate { HideChipTip(); };
                }
                t.Click += delegate
                {
                    dd.Close();
                    if (id == (current ?? "")) return;
                    BeginInvoke((MethodInvoker)delegate { picked(id); });
                };
                if (t.Current) currentTile = t;
                flow.Controls.Add(t);
            }
            int contentH = flow.Padding.Vertical + ((all.Count + cols - 1) / cols) * (tileH + gap);
            Point under = anchor.PointToScreen(new Point(0, anchor.Height));
            int roomBelow = win.Bottom - Px(8) - under.Y, roomAbove = anchor.PointToScreen(Point.Empty).Y - win.Top - Px(8);
            bool above = roomBelow < Math.Min(contentH, Px(300)) && roomAbove > roomBelow;
            int maxH = Math.Max(Px(160), Math.Min(Px(520), above ? roomAbove : roomBelow));
            flow.Size = new Size(rowW + flow.Padding.Horizontal + gap + SystemInformation.VerticalScrollBarWidth, Math.Min(contentH, maxH));
            ToolStripControlHost host = new ToolStripControlHost(flow);
            host.AutoSize = false;
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.Size = flow.Size;
            dd.Padding = new Padding(1);
            dd.BackColor = CBorder;
            dd.Items.Add(host);
            dd.Closed += delegate { HideChipTip(); if (openDrop == dd) openDrop = null; BeginInvoke((MethodInvoker)delegate { dd.Dispose(); }); };
            int popW = flow.Width + 2, popH = flow.Height + 2;
            int px = Math.Max(win.Left + Px(8), Math.Min(under.X, win.Right - Px(8) - popW));
            int py = above ? anchor.PointToScreen(Point.Empty).Y - popH : under.Y;
            openDrop = dd;
            dd.Show(new Point(px, py));
            RoundPopup(dd);
            if (currentTile != null) flow.ScrollControlIntoView(currentTile);
            FollowAnchor(dd, anchor, flow);
            flow.Focus();
        }

        static FlowLayoutPanel MakeFlow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Tag = "flow";
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = true;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.BackColor = CBg;
            f.Margin = new Padding(0, 0, 0, Px(4));
            return f;
        }

        void AddBuildPage()
        {
            HideChipTip();
            string dataPath = System.IO.Path.Combine(ModPaths.Data(), "build-data.txt");
            if (!BuildSearch.Load(dataPath))
            {
                Label err = MakeLabel(BuildSearch.Error ?? "No build data found.", 10f, FontStyle.Regular, CText);
                err.Tag = "wrap";
                list.Controls.Add(err);
                return;
            }
            findCanvas = new FindCanvas(this);
            findCanvas.ChipClick = ChipClicked;
            list.Controls.Add(findCanvas);
            findCanvas.AddText("Choose a weapon and your desired perks. Matching builds update as you go, ranked by how many unused cell slots they have.");
            float headPt = HeadingSize;
            findCanvas.AddHeading("Equipment", headPt, true);
            List<BsItem> typeItems = new List<BsItem>();
            bool typeFound = false;
            foreach (string t in BuildSearch.WeaponTypes)
            {
                typeItems.Add(new BsItem(t, t == "Repeater" ? "Repeaters" : t));
                if (t == bsType) typeFound = true;
            }
            if (!typeFound) bsType = BuildSearch.WeaponTypes[0];
            Action<string> pickType = delegate(string name)
            {
                if (name == bsType) return;
                string[] keep = new string[2 + bsParts.Length];
                keep[0] = bsWeapon; keep[1] = bsBond;
                for (int i = 0; i < bsParts.Length; i++) keep[2 + i] = bsParts[i];
                bsTypeMemory[bsType] = keep;
                string oldType = bsType;
                bsType = name;
                BsWeapon oldW = null, oldB = null;
                foreach (BsWeapon o in BuildSearch.Weapons)
                {
                    if (o.Type != oldType) continue;
                    if (o.Name == bsWeapon) oldW = o;
                    if (o.Name == bsBond) oldB = o;
                }
                BsWeapon newW = oldW != null ? BsSibling(oldW, name) : null;
                BsWeapon newB = newW != null && newW.Legendary && oldB != null ? BsSibling(oldB, name) : null;
                string[] back;
                bool remembered = bsTypeMemory.TryGetValue(name, out back);
                if (newW != null)
                {
                    bsWeapon = newW.Name;
                    bsBond = newB != null ? newB.Name : null;
                }
                else if (remembered && oldW == null)
                {
                    bsWeapon = back[0]; bsBond = back[1];
                }
                else
                {
                    bsWeapon = null;
                    bsBond = null;
                }
                for (int i = 0; i < bsParts.Length; i++) bsParts[i] = remembered ? back[2 + i] ?? "" : "";
                RefreshSection();
            };
            List<BsGear> typeGear = new List<BsGear>();
            foreach (string t in BuildSearch.WeaponTypes)
            {
                BsGear tg = new BsGear();
                tg.Id = t;
                tg.Name = t == "Repeater" ? "Repeaters" : t;
                tg.Icon = TypeIcon(t);
                typeGear.Add(tg);
            }
            string typeIcon = TypeIcon(bsType);
            AddFindSlot("Weapon Type", bsType == "Repeater" ? "Repeaters" : bsType, typeIcon != "" ? BsImage(typeIcon) : null, false, null,
                delegate(Control a)
                {
                    if (WeaponTiles()) OpenGearDrop(a, typeGear, "", bsType, pickType, true);
                    else OpenListDrop(a, typeItems, bsType, 240, pickType);
                });

            List<BsItem> weaponItems = new List<BsItem>();
            BsWeapon cur = null;
            List<BsWeapon> typeWeapons = new List<BsWeapon>();
            foreach (BsWeapon w in BuildSearch.Weapons) if (w.Type == bsType && !IsRecruit(w.Name)) typeWeapons.Add(w);
            foreach (KeyValuePair<string, List<BsWeapon>> grp in WeaponGroups(typeWeapons))
            {
                weaponItems.Add(new BsItem(null, grp.Key));
                foreach (BsWeapon w in grp.Value)
                {
                    BsItem wi = new BsItem(w.Name, BsWeaponText(w));
                    wi.Aff = w.Element ?? "";
                    weaponItems.Add(wi);
                    if (w.Name == bsWeapon) cur = w;
                }
            }
            weaponItems.Add(new BsItem("", "None (any weapon)"));
            if (cur == null)
            {
                bsWeapon = "";
                bsBond = null;
            }
            Action<string> pickWeapon = delegate(string name)
            {
                bsWeapon = name;
                bsBond = null;
                RefreshSection();
            };
            FbItem weaponSlot = AddFindSlot("Weapon", cur != null ? cur.Name : "Any weapon", cur != null ? BsImage(cur.Icon) : null, cur == null,
                cur != null ? WeaponTip(cur, null) : null, delegate(Control a)
                {
                    if (WeaponTiles()) OpenWeaponDrop(a, typeWeapons, bsWeapon, "None (any weapon)", pickWeapon);
                    else OpenListDrop(a, weaponItems, bsWeapon, 340, pickWeapon);
                }, cur != null ? new Image[] { BsIcon(cur.Slot1), BsIcon(cur.Slot2) } : null);
            if (cur != null && !string.IsNullOrEmpty(cur.Element)) weaponSlot.Elem = BsImage("elements/" + cur.Element + ".png");
            if (cur != null) weaponSlot.Aff = cur.Element ?? "";
            ClearWith(weaponSlot, cur != null, pickWeapon);

            if (cur != null && cur.Legendary)
            {
                List<BsItem> bondItems = new List<BsItem>();
                List<BsWeapon> bondWeapons = new List<BsWeapon>();
                BsWeapon bondCur = null;
                foreach (BsWeapon w in BuildSearch.Weapons)
                {
                    if (w.Type != cur.Type || w.Legendary || w.Exotic || w.Element != cur.Bond || IsRecruit(w.Name)) continue;
                    string perks = w.Perks.Length > 0 ? "  (" + string.Join(" +3, ", w.Perks) + " +3)" : "";
                    BsItem bi = new BsItem(w.Name, w.Name + perks);
                    bi.Aff = w.Element ?? "";
                    bondItems.Add(bi);
                    bondWeapons.Add(w);
                    if (w.Name == bsBond) bondCur = w;
                }
                bondItems.Add(new BsItem("", "No bond"));
                if (bondCur == null) bsBond = null;
                Action<string> pickBond = delegate(string name)
                {
                    bsBond = name == "" ? null : name;
                    RefreshSection();
                };
                FbItem bondSlot = AddFindSlot("Bond Weapon", bondCur != null ? bondCur.Name : "No bond", bondCur != null ? BsImage(bondCur.Icon) : null, bondCur == null,
                    bondCur != null ? WeaponTip(bondCur, null) : null, delegate(Control a)
                    {
                        if (WeaponTiles()) OpenWeaponDrop(a, bondWeapons, bsBond ?? "", "No bond", pickBond);
                        else OpenListDrop(a, bondItems, bsBond ?? "", 340, pickBond);
                    });
                if (bondCur != null && !string.IsNullOrEmpty(bondCur.Element)) bondSlot.Elem = BsImage("elements/" + bondCur.Element + ".png");
                if (bondCur != null) bondSlot.Aff = bondCur.Element ?? "";
                ClearWith(bondSlot, bondCur != null, pickBond);
            }

            List<BsGear> lanterns = LanternChoices();
            BsGear lanCur = BuildSearch.FindGear(lanterns, bsLantern);
            Action<string> pickLantern = delegate(string id)
            {
                bsLantern = id;
                RefreshSection();
            };
            ClearWith(AddFindSlot("Lantern", lanCur != null ? lanCur.Name : "Any Lantern", lanCur != null ? BsImage(lanCur.Icon) : null,
                lanCur == null, SlotGearTip(lanCur), delegate(Control a) { OpenGearDrop(a, lanterns, "Any Lantern", bsLantern, pickLantern); }), lanCur != null, pickLantern);

            findCanvas.AddHeading("Modifications", headPt, false);
            BsGear omniCur = BuildSearch.FindGear(BuildSearch.Omnicells, bsOmnicell);
            Action<string> pickOmni = delegate(string id)
            {
                bsOmnicell = id;
                RefreshSection();
            };
            ClearWith(AddFindSlot("Omnicell", omniCur != null ? omniCur.Name : "No Omnicell", omniCur != null ? BsImage(omniCur.Icon) : null,
                omniCur == null, SlotGearTip(omniCur), delegate(Control a) { OpenGearDrop(a, BuildSearch.Omnicells, "No Omnicell", bsOmnicell, pickOmni); }), omniCur != null, pickOmni);
            for (int pi = 0; pi < PartCount(bsType); pi++)
            {
                int slot = pi;
                List<BsGear> partChoices = PartsFor(bsType, slot, cur != null ? cur.Name : "");
                bsParts[slot] = FixPart(partChoices, bsParts[slot]);
                BsGear partCur = BuildSearch.FindGear(partChoices, bsParts[slot]);
                string partNone = "No " + PartLabel(slot, bsType);
                Action<string> pickPart = delegate(string id)
                {
                    bsParts[slot] = id;
                    RefreshSection();
                };
                ClearWith(AddFindSlot(PartLabel(slot, bsType), partCur != null ? partCur.Name : partNone, partCur != null ? BsImage(partCur.Icon) : null,
                    partCur == null, SlotGearTip(partCur), delegate(Control a) { OpenGearDrop(a, partChoices, partNone, bsParts[slot], pickPart); }), partCur != null && !ExoticParts.ContainsKey(partCur.Id), pickPart);
            }

            findCanvas.AddHeading("Armor", headPt, false);
            for (int pc = 0; pc < 4; pc++)
            {
                int piece = pc;
                string pieceName = BuildSearch.Pieces[pc];
                List<BsArmour> pieceArmour = new List<BsArmour>();
                foreach (BsArmour a in BuildSearch.Armours) if (a.Piece == pieceName && !IsRecruit(a.Name)) pieceArmour.Add(a);
                pieceArmour.Sort(delegate(BsArmour x, BsArmour y) { return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); });
                BsArmour armCur = null;
                foreach (BsArmour a in pieceArmour) if (a.Name == bsArmour[pc]) { armCur = a; break; }
                if (armCur == null) bsArmour[pc] = "";
                string armNone = "Any " + pieceName;
                List<BsItem> armItems = new List<BsItem>();
                armItems.Add(new BsItem("", armNone));
                List<string> armKeys = new List<string>(BuildSearch.Elements);
                armKeys.Add("No Element");
                foreach (BsArmour a in pieceArmour)
                {
                    string k = string.IsNullOrEmpty(a.Strength) ? "No Element" : a.Strength;
                    if (!armKeys.Contains(k)) armKeys.Add(k);
                }
                foreach (string k in armKeys)
                {
                    List<BsItem> g = new List<BsItem>();
                    foreach (BsArmour a in pieceArmour)
                    {
                        string ak = string.IsNullOrEmpty(a.Strength) ? "No Element" : a.Strength;
                        if (ak != k) continue;
                        BsItem ai = new BsItem(a.Name, a.Name + (string.IsNullOrEmpty(a.Perk) ? "" : "  (" + a.Perk + ")"));
                        ai.Aff = a.Strength ?? "";
                        g.Add(ai);
                    }
                    if (g.Count == 0) continue;
                    armItems.Add(new BsItem(null, k));
                    armItems.AddRange(g);
                }
                Action<string> pickArmour = delegate(string name)
                {
                    bsArmour[piece] = name ?? "";
                    RefreshSection();
                };
                FbItem armSlot = AddFindSlot(pieceName, armCur != null ? armCur.Name : armNone, armCur != null ? BsImage(armCur.Icon) : null, armCur == null, null,
                    delegate(Control a)
                    {
                        if (WeaponTiles()) OpenArmourDrop(a, pieceArmour, armNone, bsArmour[piece], pickArmour);
                        else OpenListDrop(a, armItems, bsArmour[piece], 340, pickArmour);
                    });
                if (armCur != null)
                {
                    armSlot.Aff = armCur.Strength ?? "";
                    if (!string.IsNullOrEmpty(armCur.Strength)) armSlot.Elem = BsImage("elements/" + armCur.Strength + ".png");
                    armSlot.Tip = ArmourTip(armCur);
                    armSlot.TipImages = new Image[] { BsIcon(armCur.Slot) };
                }
                ClearWith(armSlot, armCur != null, pickArmour);
            }

            findCanvas.AddHeading("Perks", headPt, false);
            findCanvas.AddPicked();
            UpdateAutoPerks();
            bsChips.Clear();
            for (int ti = 0; ti < BuildSearch.Types.Length; ti++)
            {
                string type = BuildSearch.Types[ti];
                findCanvas.AddColHead(type, BsIcon(type), ti);
                List<BsPerk> perks = new List<BsPerk>();
                foreach (BsPerk p in BuildSearch.Perks) if (p.Type == type) perks.Add(p);
                perks.Sort(delegate(BsPerk x, BsPerk y) { return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); });
                foreach (BsPerk p in perks) AddFindChip(p, ti);
            }
            findCanvas.AddHeading("Builds", headPt, false);
            bsResults = new FlowLayoutPanel();
            bsResults.Tag = "buildresults";
            bsResults.FlowDirection = FlowDirection.TopDown;
            bsResults.WrapContents = false;
            bsResults.AutoSize = true;
            bsResults.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bsResults.BackColor = CBg;
            bsResults.Margin = new Padding(0);
            list.Controls.Add(bsResults);
            Label n = MakeLabel("Assumes all gear is power surged and all cells are +3. Green outlines are gear you own, red outlines are gear you don't, read from your save. Gear data sourced from Dauntless-Builder.",
                9f, FontStyle.Regular, CSub);
            n.Tag = "wrap";
            n.Margin = new Padding(Px(2), Px(4), 0, 0);
            list.Controls.Add(n);
            RunBuildSearch();
        }

        ToolTip chipTip;
        Timer chipTipTimer;
        Control chipTipFor;
        ToolStripDropDown openDrop;
        string chipTipText;
        bool chipTipCell;
        Image[] chipTipImages;
        Rectangle chipTipRect;
        readonly Font cellTipFont = new Font("Segoe UI", Ui.Pt(10f));
        readonly Font cellTipBold = new Font("Segoe UI", Ui.Pt(10f), FontStyle.Bold);

        Size ChipTipSize(string text)
        {
            text = text ?? "";
            if (chipTipImages != null)
            {
                bool noText = text.Trim() == "";
                Size ts = noText ? Size.Empty : TextRenderer.MeasureText(text, tipFont);
                int gapH = noText ? 0 : tipFont.Height;
                int n = chipTipImages.Length;
                return new Size(Math.Max(ts.Width, n * Px(28) + (n - 1) * Px(6)), ts.Height + gapH + Px(28));
            }
            if (!chipTipCell) return TextRenderer.MeasureText(text, tipFont);
            int nl = text.IndexOf('\n');
            string head = nl < 0 ? text : text.Substring(0, nl);
            string body = nl < 0 ? "" : text.Substring(nl + 1);
            Size h = TextRenderer.MeasureText(head, cellTipBold);
            Size b = body == "" ? Size.Empty : TextRenderer.MeasureText(body, cellTipFont);
            return new Size(Math.Max(h.Width, b.Width), h.Height + b.Height);
        }

        void EnsureChipTip()
        {
            if (chipTip != null) return;
            chipTip = new ToolTip();
            chipTip.OwnerDraw = true;
            chipTip.UseAnimation = false;
            chipTip.UseFading = false;
            chipTip.Popup += delegate(object o, PopupEventArgs e)
            {
                Size t = ChipTipSize(chipTipText);
                e.ToolTipSize = new Size(t.Width + Px(20), t.Height + Px(12));
            };
            chipTip.Draw += delegate(object o, DrawToolTipEventArgs e)
            {
                Rectangle r = e.Bounds;
                try
                {
                    IntPtr hdc = e.Graphics.GetHdc();
                    IntPtr tipWnd = WindowFromDC(hdc);
                    e.Graphics.ReleaseHdc(hdc);
                    if (tipWnd != IntPtr.Zero) SetWindowRgn(tipWnd, CreateRoundRectRgn(0, 0, r.Width + 1, r.Height + 1, Px(12), Px(12)), false);
                }
                catch { }
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush fill = new SolidBrush(CPanel)) e.Graphics.FillRectangle(fill, r);
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, r.Width - 1, r.Height - 1), Px(6)))
                using (Pen edge = new Pen(CBorder))
                    e.Graphics.DrawPath(edge, path);
                if (chipTipImages != null)
                {
                    string it = e.ToolTipText ?? "";
                    int iy = r.Y + Px(6);
                    if (it.Trim() != "")
                    {
                        Size ts = TextRenderer.MeasureText(it, tipFont);
                        TextRenderer.DrawText(e.Graphics, it, tipFont, new Rectangle(r.X, iy, r.Width, ts.Height), CText,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                        iy += ts.Height + tipFont.Height;
                    }
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    int n = chipTipImages.Length;
                    int ix = r.X + (r.Width - (n * Px(28) + (n - 1) * Px(6))) / 2;
                    foreach (Image img in chipTipImages)
                    {
                        e.Graphics.DrawImage(img, new Rectangle(ix, iy, Px(28), Px(28)));
                        ix += Px(28) + Px(6);
                    }
                    return;
                }
                if (!chipTipCell)
                {
                    TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, r, CText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    return;
                }
                string tt = e.ToolTipText ?? "";
                int nl = tt.IndexOf('\n');
                string head = nl < 0 ? tt : tt.Substring(0, nl);
                string body = nl < 0 ? "" : tt.Substring(nl + 1);
                Size hs = TextRenderer.MeasureText(head, cellTipBold);
                Size bs = body == "" ? Size.Empty : TextRenderer.MeasureText(body, cellTipFont);
                int top = r.Y + (r.Height - hs.Height - bs.Height) / 2;
                TextRenderer.DrawText(e.Graphics, head, cellTipBold, new Rectangle(r.X, top, r.Width, hs.Height), CText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                if (body != "")
                    TextRenderer.DrawText(e.Graphics, body, cellTipFont, new Rectangle(r.X, top + hs.Height, r.Width, bs.Height), CText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            };
            chipTipTimer = new Timer();
            chipTipTimer.Interval = 350;
            chipTipTimer.Tick += delegate { chipTipTimer.Stop(); ShowChipTipNow(); };
        }

        void ShowChipTipSoon(Control chip, string text, bool cell = false)
        {
            EnsureChipTip();
            if (chipTipFor != null && chipTipFor != chip) chipTip.Hide(chipTipFor);
            chipTipFor = chip;
            chipTipText = text;
            chipTipCell = cell;
            chipTipImages = null;
            chipTipRect = Rectangle.Empty;
            chipTipTimer.Stop();
            chipTipTimer.Start();
        }

        void ShowChipTipSoon(Control host, Rectangle rect, string text, bool cell)
        {
            ShowChipTipSoon(host, text, cell);
            chipTipRect = rect;
        }

        void ShowChipTipSoon(Control host, Rectangle rect, string text, Image[] images)
        {
            ShowChipTipSoon(host, text, images);
            chipTipRect = rect;
        }

        void ShowChipTipSoon(Control chip, string text, Image[] images)
        {
            ShowChipTipSoon(chip, text);
            List<Image> keep = new List<Image>();
            if (images != null) foreach (Image img in images) if (img != null) keep.Add(img);
            chipTipImages = keep.Count == 0 ? null : keep.ToArray();
        }

        void ShowChipTipNow()
        {
            Control chip = chipTipFor;
            if (chip == null || chip.IsDisposed || !chip.IsHandleCreated) return;
            Rectangle cr = chipTipRect.IsEmpty ? chip.ClientRectangle : chipTipRect;
            if (!cr.Contains(chip.PointToClient(Cursor.Position))) return;
            Size t = ChipTipSize(chipTipText);
            int w = t.Width + Px(20), h = t.Height + Px(12);
            int x = cr.X + (cr.Width - w) / 2, y = cr.Y - h - Px(6);
            Rectangle wa = Screen.FromControl(chip).WorkingArea;
            Point at = chip.PointToScreen(new Point(x, y));
            if (at.Y < wa.Top) y = cr.Bottom + Px(6);
            if (at.X < wa.Left) x += wa.Left - at.X;
            else if (at.X + w > wa.Right) x -= at.X + w - wa.Right;
            chipTip.Show(chipTipText, chip, x, y);
        }

        void HideChipTip()
        {
            if (chipTipTimer != null) chipTipTimer.Stop();
            if (chipTip != null && chipTipFor != null && !chipTipFor.IsDisposed) chipTip.Hide(chipTipFor);
            chipTipFor = null;
        }

        static string BsWrap(string text, int width)
        {
            if ((text ?? "").IndexOf('\n') >= 0)
            {
                List<string> parts = new List<string>();
                foreach (string line in text.Split('\n')) parts.Add(BsWrap(line, width));
                return string.Join("\n", parts);
            }
            StringBuilder sb = new StringBuilder();
            int lineLen = 0;
            foreach (string word in (text ?? "").Split(' '))
            {
                if (word == "") continue;
                if (lineLen > 0 && lineLen + 1 + word.Length > width) { sb.Append('\n'); lineLen = 0; }
                else if (lineLen > 0) { sb.Append(' '); lineLen++; }
                sb.Append(word);
                lineLen += word.Length;
            }
            return sb.ToString();
        }

        void RunBuildSearch()
        {
            BsOwned.Refresh();
            bsExtra = 0;
            RefreshBuilds();
        }

        void RefreshBuilds()
        {
            RefreshBuilds(false);
        }

        void RefreshBuilds(bool append)
        {
            if (bsResults == null || bsResults.IsDisposed) return;
            int keepY = -content.AutoScrollPosition.Y;
            HoldRender();
            try
            {
                RefreshBuildsCore(append, keepY);
            }
            finally
            {
                ReleaseRender();
            }
        }

        void RefreshBuildsCore(bool append, int keepY)
        {
            SyncOwned();
            BsWeapon w = BuildSearch.FindWeapon(bsWeapon);
            BsWeapon bond = string.IsNullOrEmpty(bsBond) ? null : BuildSearch.FindWeapon(bsBond);
            int total = 0;
            List<BsResult> found = new List<BsResult>();
            Dictionary<string, int> want = BuildReq();
            if (bsReq.Count > 0)
            {
                int max = Math.Min(100, BuildsShown() + bsExtra);
                if (w != null) found = BuildSearch.Search(w, bond, want, true, max, false, out total);
                else found = BuildSearch.SearchAny(bsType, want, true, max, false, out total);
            }

            bsResults.SuspendLayout();
            try
            {
                int have = 0;
                if (append)
                {
                    for (int ci = bsResults.Controls.Count - 1; ci >= 0; ci--)
                    {
                        Control oc = bsResults.Controls[ci];
                        if ("buildcard".Equals(oc.Tag)) have++;
                        else { bsResults.Controls.RemoveAt(ci); oc.Dispose(); }
                    }
                }
                else
                {
                    Control[] old = new Control[bsResults.Controls.Count];
                    bsResults.Controls.CopyTo(old, 0);
                    bsResults.Controls.Clear();
                    foreach (Control c in old) c.Dispose();
                }
                string note = null;
                if (bsReq.Count == 0) note = "Select at least one perk above. Left-click to select the highest available value (+6 or +3), click again to lower it, or right-click to remove it.";
                else if (total == 0) note = w != null
                    ? "No combination can fit all selected perks with this weapon. Try lowering a perk to +3 or choosing a weapon with different cell slots."
                    : "No combination can fit all selected perks with any " + bsType + ". Try lowering a perk to +3.";
                if (note != null)
                {
                    Label nl = MakeLabel(note, 10f, FontStyle.Regular, CText);
                    nl.Margin = new Padding(Px(2), 0, 0, Px(8));
                    bsResults.Controls.Add(nl);
                }
                for (int i = have; i < found.Count; i++) bsResults.Controls.Add(MakeBuildCard(i + 1, found[i], w ?? found[i].Weapon, w != null ? bond : null));
                if (total > found.Count)
                {
                    FlowLayoutPanel moreBar = MakeFlow();
                    moreBar.Margin = new Padding(0, 0, 0, Px(4));
                    int step = Math.Min(BuildsShown(), total - found.Count);
                    if (found.Count < 100)
                    {
                        Button more = MakeButton("Show " + step + " More", 0);
                        more.Size = new Size(TextRenderer.MeasureText(more.Text, more.Font).Width + Px(30), Px(32));
                        more.ForeColor = CText;
                        more.FlatAppearance.BorderColor = CGold;
                        more.Click += delegate
                        {
                            bsExtra += BuildsShown();
                            BeginInvoke((MethodInvoker)delegate { RefreshBuilds(true); });
                        };
                        moreBar.Controls.Add(more);
                    }
                    Label count = MakeLabel("Showing the best " + found.Count + " of " + total.ToString("#,0", Inv) + " builds.", 9f, FontStyle.Regular, CSub);
                    count.Margin = new Padding(Px(10), Px(9), 0, 0);
                    moreBar.Controls.Add(count);
                    bsResults.Controls.Add(moreBar);
                }
            }
            finally
            {
                bsResults.ResumeLayout();
            }

            if (findCanvas != null && !findCanvas.IsDisposed)
            {
                List<string> picked = new List<string>();
                foreach (KeyValuePair<string, int> kv in want) picked.Add(kv.Key + " +" + (kv.Value * 3));
                findCanvas.SetPicked(bsReq.Count == 0 ? "No perks selected" + GivesText("Weapon", bsAutoWeapon) + GivesText("Armor", bsAutoArmor)
                    : picked.Count + (picked.Count == 1 ? " perk: " : " perks: ") + string.Join(", ", picked.ToArray())
                        + "   |   " + total.ToString("#,0", Inv) + (total == 1 ? " build" : " builds") + " found below");
            }
            if (!append) UpdateChipLimits(w, bond);
            if (!loading)
            {
                LayoutRows();
                content.PerformLayout();
                content.AutoScrollPosition = new Point(0, keepY);
            }
        }

        bool Fits(string perk, int level)
        {
            SyncOwned();
            BsWeapon w = BuildSearch.FindWeapon(bsWeapon);
            BsWeapon bond = string.IsNullOrEmpty(bsBond) ? null : BuildSearch.FindWeapon(bsBond);
            Dictionary<string, int> test = BuildReq();
            test[perk] = Math.Max(level, TotalOf(perk));
            return CanFit(w, bond, bsType, test, true, null);
        }

        static bool CanFit(BsWeapon w, BsWeapon bond, string type, Dictionary<string, int> req, bool exotic, Func<bool> stop)
        {
            int t;
            if (w != null) BuildSearch.Search(w, bond, req, exotic, 0, true, stop, out t);
            else BuildSearch.SearchAny(type, req, exotic, 0, true, stop, out t);
            return t > 0;
        }

        void UpdateChipLimits(BsWeapon w, BsWeapon bond)
        {
            SyncOwned();
            int gen = ++bsGen;
            Dictionary<string, int> req = BuildReq();
            Dictionary<string, int> mine = new Dictionary<string, int>(bsReq);
            List<string> names = new List<string>(bsChips.Keys);
            bool exotic = true;
            string wtype = bsType;
            Func<bool> stale = delegate { return gen != bsGen; };
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                Dictionary<string, int> room = new Dictionary<string, int>();
                try
                {
                    foreach (string name in names)
                    {
                        if (gen != bsGen) return;
                        int have;
                        req.TryGetValue(name, out have);
                        int best = 0;
                        if (have < 2)
                        {
                            for (int target = 2; target > have && best == 0; target--)
                            {
                                Dictionary<string, int> test = new Dictionary<string, int>(req);
                                test[name] = target;
                                if (CanFit(w, bond, wtype, test, exotic, stale)) best = target - have;
                            }
                        }
                        room[name] = best;
                    }
                }
                catch (Exception fail) { Diag.Fail("working out which perks still fit", fail); return; }
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (gen != bsGen) return;
                        foreach (KeyValuePair<string, int> kv in room)
                        {
                            FbItem chip;
                            if (!bsChips.TryGetValue(kv.Key, out chip)) continue;
                            bool picked = mine.ContainsKey(kv.Key) || chip.Level > 0;
                            chip.Hint = picked ? 0 : kv.Value;
                            chip.Blocked = !picked && kv.Value == 0;
                        }
                        if (findCanvas != null && !findCanvas.IsDisposed) findCanvas.Invalidate();
                    });
                }
                catch { }
            });
        }

        class TileCell { public string SlotType, Perk; }

        Image BsTypeIconForPerkOrSlot(string name)
        {
            BsPerk p = BuildSearch.FindPerk(name);
            return BsIcon(p != null ? p.Type : name);
        }

        BuildTile MakeArmourTile(BsGroup g, int piece, BsResult r)
        {
            BsArmour a = g.Pieces[0];
            BuildTile t = new BuildTile();
            t.Icon = BsTypeIconForPerkOrSlot;
            t.Pic = BsImage(a.Icon);
            t.Title = a.Name;
            t.Affinity = a.Strength ?? "";
            t.ArmourElems = true;
            t.ElemA = string.IsNullOrEmpty(a.Strength) ? null : BsImage("elements/" + a.Strength + ".png");
            t.ElemB = string.IsNullOrEmpty(a.Weakness) ? null : BsImage("elements/" + a.Weakness + ".png");
            t.PerkText = a.Perk == "" ? "No perk" : "+3 " + a.Perk;
            foreach (string[] spot in r.CellSpots)
                if (spot[0] == BuildSearch.Pieces[piece]) { TileCell c = new TileCell(); c.SlotType = spot[1]; c.Perk = spot[2]; t.Cells.Add(c); }
            t.Own = BsOwned.State(BuildSearch.ArmourId(a));
            return t;
        }

        static readonly string[] PartCats = new string[] { "part", "part2", "part3" };
        static readonly string[] ShareSlots = new string[] { "weapon", "bond", "part", "part2", "part3", "head", "chest", "arms", "legs", "lantern", "omnicell" };
        static readonly string[] SharePieces = new string[] { "head", "chest", "arms", "legs" };

        static int PartCount(string type) { return type == "Repeater" ? 3 : 2; }

        static string PartLabel(int i, string type)
        {
            bool rep = type == "Repeater";
            return i == 0 ? (rep ? "Chamber" : "Special") : i == 1 ? (rep ? "Grip" : "Mod") : "Mod";
        }

        static readonly Dictionary<string, string> ExoticParts = new Dictionary<string, string>
        {
            { "PART_DP_GRIP_EXOTIC_BOMBS", "The Twin Suns" },
            { "PART_DP_RECEIVER_EXOTIC_BOMBS", "The Twin Suns" },
            { "PART_EB_SPECIAL_CROWBEAR_EXOTIC", "The Hunger" },
            { "PART_IH_SPECIAL_SALLY_EXOTIC", "Molten Edict" },
            { "PART_MS_SPECIAL_BEAM", "The Godhand" },
        };

        static List<BsGear> PartsFor(string type, int i, string weapon)
        {
            List<BsGear> normal = new List<BsGear>(), locked = new List<BsGear>();
            foreach (BsGear g in BuildSearch.Parts)
            {
                if (g.Type != type || g.Cat != PartCats[i]) continue;
                string owner;
                if (!ExoticParts.TryGetValue(g.Id, out owner)) normal.Add(g);
                else if (owner == weapon) locked.Add(g);
            }
            return locked.Count > 0 ? locked : normal;
        }

        static string FixPart(List<BsGear> choices, string id)
        {
            foreach (BsGear g in choices) if (g.Id == id) return id;
            foreach (BsGear g in choices) if (ExoticParts.ContainsKey(g.Id)) return g.Id;
            return "";
        }

        static List<BsGear> LanternChoices()
        {
            List<BsGear> found = new List<BsGear>();
            foreach (BsGear g in BuildSearch.Lanterns) if (g.Slot != "") found.Add(g);
            return found;
        }

        static List<BsItem> GearItems(List<BsGear> src, string none)
        {
            List<BsItem> items = new List<BsItem>();
            items.Add(new BsItem("", none));
            foreach (BsGear g in src) items.Add(new BsItem(g.Id, g.Name));
            return items;
        }

        static string GetPart(SavedBuild b, int i) { return i == 0 ? b.Part1 : i == 1 ? b.Part2 : b.Part3; }

        static void SetPart(SavedBuild b, int i, string id)
        {
            if (i == 0) b.Part1 = id;
            else if (i == 1) b.Part2 = id;
            else b.Part3 = id;
        }

        static string GearTip(BsGear g)
        {
            return g.Name + (g.Desc != "" ? "\n\n" + BsWrap(g.Desc, 60) : "") + "\n\nClick to change.";
        }

        void FillLanternTile(BuildTile t, string id)
        {
            BsGear g = BuildSearch.FindGear(BuildSearch.Lanterns, id);
            t.Pic = g != null ? BsImage(g.Icon) : null;
            if (t.Pic == null) t.Pic = BsImage("Lantern.png");
            t.Title = g != null ? g.Name : "Any Lantern";
            t.PerkText = g != null ? "Lantern" : "Click to pick a lantern";
            t.Own = g != null ? BsOwned.State(g.Id) : 0;
            t.Tip = g != null ? GearTip(g) : "The lantern doesn't change the build, as long as it has an " + BuildSearch.LanternSlot + " cell slot.\n\nClick to pick one.";
        }

        BuildTile MakeGearTile(CardCanvas card, string label, List<BsGear> choices, string none, SavedBuild b, int part)
        {
            BuildTile t = new BuildTile();
            t.NoCells = true;
            t.Small = true;
            t.Badge = label;
            t.Anchor = card.NewAnchor();
            List<BsGear> src = part < 0 ? BuildSearch.Omnicells : BuildSearch.Parts;
            MethodInvoker fill = delegate
            {
                BsGear g = BuildSearch.FindGear(src, part < 0 ? b.Omnicell : GetPart(b, part));
                t.Pic = g != null ? BsImage(g.Icon) : null;
                t.Title = g != null ? g.Name : none;
                t.Own = g != null ? BsOwned.State(g.Id) : 0;
                t.Tip = g != null ? GearTip(g) : "Click to pick " + (part < 0 ? "an omnicell" : "a " + label.ToLowerInvariant()) + ".";
                card.Invalidate(t.Bounds);
            };
            fill();
            t.Open = delegate(Control anchor)
            {
                OpenGearDrop(anchor, choices, none, part < 0 ? b.Omnicell : GetPart(b, part), delegate(string id)
                {
                    if (part < 0) b.Omnicell = id;
                    else SetPart(b, part, id);
                    fill();
                    LoadoutChanged(b);
                });
            };
            return t;
        }

        void LoadoutChanged(SavedBuild b)
        {
            if (SavedBuilds().Contains(b)) SaveSavedBuilds();
            SavedBuild pinned = ReadPinned(ModDir());
            if (pinned != null && pinned.Key == b.Key)
            {
                pinned.Lantern = b.Lantern;
                pinned.Omnicell = b.Omnicell;
                pinned.Part1 = b.Part1;
                pinned.Part2 = b.Part2;
                pinned.Part3 = b.Part3;
                SetPinned(pinned);
            }
        }

        void OpenShareMenu(Control anchor, CardCanvas card, SavedBuild b, string name)
        {
            HideChipTip();
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.ShowImageMargin = false;
            m.BackColor = CPanel;
            m.Font = Ui.UiFont(9.5f);
            ToolStripItem link = m.Items.Add("Copy Build Link");
            ToolStripItem shot = m.Items.Add("Copy Build Card Screenshot");
            foreach (ToolStripItem it in m.Items) { it.ForeColor = CText; it.Padding = new Padding(Px(4), Px(4), Px(4), Px(4)); }
            link.Click += delegate { CopyBuildLink(b, name); };
            shot.Click += delegate { BeginInvoke((MethodInvoker)delegate { CopyCardScreenshot(card); }); };
            m.Closed += delegate { if (openDrop == m) openDrop = null; BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            openDrop = m;
            m.Show(anchor, new Point(anchor.Width - m.PreferredSize.Width, anchor.Height));
            RoundPopup(m);
        }

        void CopyBuildLink(SavedBuild b, string name)
        {
            List<string> missing = new List<string>();
            string code = ShareCode(b, name, missing);
            try { Clipboard.SetText(code); }
            catch (Exception e) { SetStatus("Could not copy the build link: " + e.Message, false); return; }
            SetStatus(missing.Count == 0 ? "Build link copied. The launcher's Builds import reads it." : "Build link copied without: " + string.Join(", ", missing.ToArray()), false);
        }

        void CopyCardScreenshot(CardCanvas card)
        {
            try
            {
                using (Bitmap bmp = card.Snapshot()) Clipboard.SetImage(bmp);
                SetStatus("Build card screenshot copied", false);
            }
            catch (Exception e) { SetStatus("Could not copy the screenshot: " + e.Message, false); }
        }

        static string Js(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            s = s ?? "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\b') sb.Append("\\b");
                else if (c == '\f') sb.Append("\\f");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c); sb.Append(s[++i]); }
                else if (char.IsSurrogate(c)) sb.Append("\\u" + ((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static List<string[]> SpotList(string spots)
        {
            List<string[]> found = new List<string[]>();
            foreach (string part in (spots ?? "").Split(';'))
            {
                string[] f = part.Split('/');
                if (f.Length >= 3) found.Add(f);
            }
            return found;
        }

        static string ShareCode(SavedBuild b, string name, List<string> missing)
        {
            BsWeapon w = BuildSearch.FindWeapon(b.Weapon);
            Dictionary<string, string> slots = new Dictionary<string, string>();
            foreach (string k in ShareSlots) slots[k] = "";
            Func<string, string, string> idOf = delegate(string key, string shown)
            {
                if (string.IsNullOrEmpty(shown)) return "";
                string v = BuildSearch.ShareId(key);
                if (v == "") missing.Add(shown);
                return v;
            };
            slots["weapon"] = idOf("W|" + b.Weapon, b.Weapon);
            if (b.Bond != "" && w != null && w.Legendary) slots["bond"] = idOf("W|" + b.Bond, b.Bond);
            for (int i = 0; i < 3; i++)
            {
                BsGear g = BuildSearch.FindGear(BuildSearch.Parts, GetPart(b, i));
                if (g != null && w != null && g.Type == w.Type && i < PartCount(w.Type)) slots[PartCats[i]] = g.Id;
            }
            string[] names = new string[] { b.Head, b.Torso, b.Arms, b.Legs };
            for (int pc = 0; pc < 4; pc++) slots[SharePieces[pc]] = idOf("A|" + BuildSearch.Pieces[pc] + "|" + names[pc], names[pc]);
            if (BuildSearch.FindGear(BuildSearch.Lanterns, b.Lantern) != null) slots["lantern"] = b.Lantern;
            if (BuildSearch.FindGear(BuildSearch.Omnicells, b.Omnicell) != null) slots["omnicell"] = b.Omnicell;

            List<string[]> spots = SpotList(b.Spots);
            Func<string, string> cellOf = delegate(string perk)
            {
                if (string.IsNullOrEmpty(perk)) return "";
                string v = BuildSearch.ShareId("C|" + perk);
                if (v == "") missing.Add("+3 " + perk);
                return v;
            };
            List<KeyValuePair<string, List<string>>> cells = new List<KeyValuePair<string, List<string>>>();
            List<string[]> ws = new List<string[]>();
            foreach (string[] f in spots) if (f[0] == "Weapon") ws.Add(f);
            List<string> wc = new List<string>();
            if (w != null)
                foreach (string type in new string[] { w.Slot1, w.Slot2 })
                {
                    if (string.IsNullOrEmpty(type)) continue;
                    string[] hit = null;
                    foreach (string[] f in ws) if (f[1] == type) { hit = f; break; }
                    if (hit == null && ws.Count > 0) hit = ws[0];
                    if (hit != null) ws.Remove(hit);
                    wc.Add(hit != null ? cellOf(hit[2]) : "");
                }
            cells.Add(new KeyValuePair<string, List<string>>("weapon", wc));
            string[] wheres = new string[] { "Head", "Torso", "Arms", "Legs", "Lantern" };
            string[] keys = new string[] { "head", "chest", "arms", "legs", "lantern" };
            for (int i = 0; i < wheres.Length; i++)
            {
                List<string> one = new List<string>();
                foreach (string[] f in spots) if (f[0] == wheres[i]) { one.Add(cellOf(f[2])); break; }
                cells.Add(new KeyValuePair<string, List<string>>(keys[i], one));
            }

            StringBuilder j = new StringBuilder();
            j.Append("{\"format\":\"dauntless-forever-build-v2\",\"build\":{\"name\":").Append(Js(name)).Append(",\"notes\":").Append(Js(b.Notes)).Append(",\"version\":\"1.14.7\",\"slots\":{");
            for (int i = 0; i < ShareSlots.Length; i++) j.Append(i > 0 ? "," : "").Append(Js(ShareSlots[i])).Append(':').Append(Js(slots[ShareSlots[i]]));
            j.Append("},\"cells\":{");
            for (int i = 0; i < cells.Count; i++)
            {
                j.Append(i > 0 ? "," : "").Append(Js(cells[i].Key)).Append(":[");
                for (int c = 0; c < cells[i].Value.Count; c++) j.Append(c > 0 ? "," : "").Append(Js(cells[i].Value[c]));
                j.Append(']');
            }
            j.Append("}}}");
            if (Encoding.UTF8.GetByteCount(j.ToString()) % 3 == 0) j.Append(' ');
            return "DFB2." + Convert.ToBase64String(Encoding.UTF8.GetBytes(j.ToString()));
        }

        static string JStr(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return "";
            return v as string ?? "";
        }

        static SavedBuild ParseShare(string text, List<string> missing, out string error)
        {
            error = null;
            text = (text ?? "").Trim();
            if (text.Length > 100000) { error = "That build link is too long to be a real build."; return null; }
            object root;
            try
            {
                if (text.StartsWith("DFB2."))
                {
                    string b64 = Regex.Replace(text.Substring(5), "\\s", "").Replace('-', '+').Replace('_', '/');
                    while (b64.Length % 4 != 0) b64 += "=";
                    root = Json.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                }
                else if (text.StartsWith("{")) root = Json.Parse(text);
                else { error = "Copy a build link (DFB2.) first, then press Import Build."; return null; }
            }
            catch { error = "That build link is damaged or incomplete."; return null; }
            Dictionary<string, object> top = root as Dictionary<string, object>;
            object inner;
            Dictionary<string, object> build = top != null && top.TryGetValue("build", out inner) && inner is Dictionary<string, object> ? (Dictionary<string, object>)inner : top;
            object so, co;
            Dictionary<string, object> slots = build != null && build.TryGetValue("slots", out so) ? so as Dictionary<string, object> : null;
            if (slots == null) { error = "That isn't a build link."; return null; }
            Dictionary<string, List<string>> cells = new Dictionary<string, List<string>>();
            if (build.TryGetValue("cells", out co))
            {
                Dictionary<string, object> cd = co as Dictionary<string, object>;
                List<object> cl = co as List<object>;
                if (cd != null)
                    foreach (KeyValuePair<string, object> kv in cd)
                    {
                        List<string> one = new List<string>();
                        List<object> arr = kv.Value as List<object>;
                        if (arr != null) foreach (object x in arr) one.Add(x as string ?? "");
                        cells[kv.Key] = one;
                    }
                else if (cl != null)
                {
                    string[] map = new string[] { "weapon", "weapon", "head", "chest", "arms", "legs" };
                    for (int i = 0; i < cl.Count && i < map.Length; i++)
                    {
                        if (!cells.ContainsKey(map[i])) cells[map[i]] = new List<string>();
                        cells[map[i]].Add(cl[i] as string ?? "");
                    }
                }
            }
            Func<string, int, string> cellAt = delegate(string key, int i)
            {
                List<string> one;
                return cells.TryGetValue(key, out one) && i < one.Count ? one[i] : "";
            };
            Func<string, string> perkOf = delegate(string id)
            {
                if (string.IsNullOrEmpty(id)) return "";
                string k = BuildSearch.ShareName(id);
                if (k != null && k.StartsWith("C|")) return k.Substring(2);
                missing.Add(id);
                return "";
            };

            SavedBuild b = new SavedBuild();
            string wk = BuildSearch.ShareName(JStr(slots, "weapon"));
            BsWeapon w = wk != null && wk.StartsWith("W|") ? BuildSearch.FindWeapon(wk.Substring(2)) : null;
            if (w == null) { error = JStr(slots, "weapon") == "" ? "That build has no weapon." : "That build's weapon isn't in the mod's build data."; return null; }
            b.Weapon = w.Name;
            string bk = BuildSearch.ShareName(JStr(slots, "bond"));
            BsWeapon bw = bk != null && bk.StartsWith("W|") ? BuildSearch.FindWeapon(bk.Substring(2)) : null;
            if (bw != null && w.Legendary) b.Bond = bw.Name;
            else if (JStr(slots, "bond") != "" && w.Legendary) missing.Add(JStr(slots, "bond"));

            List<string> spots = new List<string>();
            string[] wTypes = new string[] { w.Slot1, w.Slot2 };
            int wi = 0;
            foreach (string type in wTypes)
            {
                if (string.IsNullOrEmpty(type)) continue;
                spots.Add("Weapon/" + type + "/" + perkOf(cellAt("weapon", wi)));
                wi++;
            }
            string[] pieceNames = new string[4];
            for (int pc = 0; pc < 4; pc++)
            {
                string id = JStr(slots, SharePieces[pc]);
                string k = BuildSearch.ShareName(id);
                string prefix = "A|" + BuildSearch.Pieces[pc] + "|";
                pieceNames[pc] = k != null && k.StartsWith(prefix) ? k.Substring(prefix.Length) : "";
                if (pieceNames[pc] == "" && id != "") missing.Add(id);
                BsArmour a = null;
                foreach (BsArmour x in BuildSearch.Armours) if (x.Piece == BuildSearch.Pieces[pc] && x.Name == pieceNames[pc]) { a = x; break; }
                if (a != null && a.Slot != "") spots.Add(a.Piece + "/" + a.Slot + "/" + perkOf(cellAt(SharePieces[pc], 0)));
            }
            b.Head = pieceNames[0]; b.Torso = pieceNames[1]; b.Arms = pieceNames[2]; b.Legs = pieceNames[3];

            BsGear lan = BuildSearch.FindGear(BuildSearch.Lanterns, JStr(slots, "lantern"));
            if (lan != null) b.Lantern = lan.Id;
            else if (JStr(slots, "lantern") != "") missing.Add(JStr(slots, "lantern"));
            string lanSlot = lan != null ? lan.Slot : BuildSearch.LanternSlot;
            if (lanSlot != "") spots.Add("Lantern/" + lanSlot + "/" + perkOf(cellAt("lantern", 0)));
            BsGear omni = BuildSearch.FindGear(BuildSearch.Omnicells, JStr(slots, "omnicell"));
            if (omni != null) b.Omnicell = omni.Id;
            else if (JStr(slots, "omnicell") != "") missing.Add(JStr(slots, "omnicell"));
            for (int i = 0; i < 3; i++)
            {
                string id = JStr(slots, PartCats[i]);
                BsGear g = BuildSearch.FindGear(BuildSearch.Parts, id);
                if (g != null && g.Type == w.Type) SetPart(b, i, g.Id);
                else if (id != "") missing.Add(id);
            }
            b.Spots = string.Join(";", spots.ToArray());
            string nm = JStr(build, "name").Replace("\r", " ").Replace("\n", " ").Trim();
            b.Name = nm == "" ? "Imported Build" : (nm.Length > 60 ? nm.Substring(0, 60) : nm);
            b.Notes = JStr(build, "notes").Replace("\r\n", "\n").Replace("\r", "\n");
            if (b.Notes.Length > 2000) b.Notes = b.Notes.Substring(0, 2000);
            return b;
        }

        void ImportBuild()
        {
            string text = "";
            try { text = Clipboard.GetText(); } catch (Exception fail) { Diag.Fail("reading the clipboard", fail); }
            List<string> missing = new List<string>();
            string error;
            SavedBuild b = ParseShare(text, missing, out error);
            if (b == null) { SetStatus(error, false); return; }
            SavedBuilds().Insert(0, b);
            if (!SaveSavedBuilds()) return;
            List<string> shownMissing = new List<string>();
            foreach (string m in missing)
            {
                if (shownMissing.Count == 5) { shownMissing.Add("and " + (missing.Count - 5) + " more"); break; }
                shownMissing.Add(m.Length > 40 ? m.Substring(0, 40) + "..." : m);
            }
            SetStatus(missing.Count == 0 ? "Imported \"" + b.Name + "\"" : "Imported \"" + b.Name + "\" without: " + string.Join(", ", shownMissing.ToArray()), false);
            BeginInvoke((MethodInvoker)delegate { RefreshSection(); });
        }

        void ChipClicked(FbItem chip, MouseButtons button)
        {
            string name = chip.Key;
            int auto = AutoOf(name), mine = UserOf(name);
            if (button == MouseButtons.Left)
            {
                if (mine == 0)
                {
                    if (auto >= 2) return;
                    if (Fits(name, 2)) mine = 2 - auto;
                    else if (auto == 0 && Fits(name, 1)) mine = 1;
                    else return;
                }
                else mine--;
            }
            else if (button == MouseButtons.Right)
            {
                if (mine == 0) return;
                mine = 0;
            }
            else return;
            if (mine == 0) bsReq.Remove(name);
            else bsReq[name] = mine;
            chip.Level = TotalOf(name);
            if (findCanvas != null && !findCanvas.IsDisposed) findCanvas.Invalidate(chip.Bounds);
            RunBuildSearch();
        }

        FbItem AddFindSlot(string title, string caption, Image pic, bool empty, string tip, Action<Control> open, Image[] tipImages = null)
        {
            FbItem it = findCanvas.AddSlot(title, caption, pic, empty);
            it.Tip = string.IsNullOrEmpty(tip) ? null : tip;
            it.TipImages = tipImages;
            it.Open = open;
            return it;
        }

        static FbItem ClearWith(FbItem it, bool has, Action<string> pick)
        {
            if (has) it.Clear = pick;
            return it;
        }

        void AddFindChip(BsPerk p, int group)
        {
            string name = p.Name;
            FbItem chip = findCanvas.AddChip(name, group);
            chip.Level = TotalOf(name);
            chip.Tip = name + "\n\n" + BsWrap(p.Desc, 70) + "\n\n" + BsRanksText(p, 70, 0)
                + (AutoOf(name) > 0 ? "\n\nYour " + (AutoFrom(name)) + " already gives +" + (AutoOf(name) * 3) + "." : "");
            chip.CellTip = true;
            bsChips[name] = chip;
        }

        class FbItem
        {
            public int Kind, Group, Level, Hint;
            public string Key, Title, Text, Aff = "", Tip;
            public Image Pic, Elem;
            public Image[] TipImages;
            public bool Empty, First, Blocked, CellTip;
            public float Pt;
            public Action<Control> Open;
            public Action<string> Clear;
            public Control Anchor;
            public Rectangle Bounds, TextRect;
        }

        class FindCanvas : Control
        {
            public const int KText = 0, KHead = 1, KSlot = 2, KPicked = 3, KColHead = 4, KChip = 5;
            const TextFormatFlags OneLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            const TextFormatFlags Wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

            readonly SettingsWindow win;
            readonly List<FbItem> items = new List<FbItem>();
            FbItem hot, down;
            string tip;
            int lastW = -1;
            public Action<FbItem, MouseButtons> ChipClick;

            public FindCanvas(SettingsWindow win)
            {
                this.win = win;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                BackColor = CBg;
                Margin = Padding.Empty;
            }

            FbItem Add(int kind, string text)
            {
                FbItem it = new FbItem();
                it.Kind = kind;
                it.Text = text;
                items.Add(it);
                return it;
            }

            public void AddText(string text) { Add(KText, text); }

            public void AddHeading(string text, float pt, bool first)
            {
                FbItem it = Add(KHead, text.ToUpperInvariant());
                it.Pt = pt;
                it.First = first;
            }

            public FbItem AddSlot(string title, string caption, Image pic, bool empty)
            {
                FbItem it = Add(KSlot, caption);
                it.Title = title;
                it.Pic = pic;
                it.Empty = empty;
                Control a = new Control();
                a.TabStop = false;
                a.Region = new Region(Rectangle.Empty);
                Controls.Add(a);
                it.Anchor = a;
                return it;
            }

            public void AddPicked() { Add(KPicked, ""); }

            public void SetPicked(string text)
            {
                foreach (FbItem it in items)
                    if (it.Kind == KPicked)
                    {
                        if (it.Text == text) return;
                        it.Text = text;
                    }
                if (lastW > 0) LayoutAt(lastW);
            }

            public void AddColHead(string type, Image icon, int group)
            {
                FbItem it = Add(KColHead, type);
                it.Pic = icon;
                it.Group = group;
                it.Tip = type + " perks";
            }

            public FbItem AddChip(string perk, int group)
            {
                FbItem it = Add(KChip, perk);
                it.Key = perk;
                it.Group = group;
                return it;
            }

            static Size MeasureWrap(string text, Font f, int w) { return TextRenderer.MeasureText(text, f, new Size(Math.Max(1, w), int.MaxValue), Wrap); }

            public void LayoutAt(int w)
            {
                w = Math.Max(Px(300), w);
                lastW = w;
                int y = 0, i = 0;
                while (i < items.Count)
                {
                    FbItem it = items[i];
                    if (it.Kind == KText)
                    {
                        int tw = Math.Max(Px(100), w - Px(6));
                        Size s = MeasureWrap(it.Text, Ui.UiFont(9.5f), tw);
                        it.Bounds = new Rectangle(Px(2), y, tw, s.Height);
                        y += s.Height + Px(12);
                        i++;
                        continue;
                    }
                    if (it.Kind == KHead)
                    {
                        if (!it.First) y += Px(10);
                        int h = TextRenderer.MeasureText(it.Text, Ui.UiFont(it.Pt, FontStyle.Bold), new Size(int.MaxValue, int.MaxValue), OneLine).Height;
                        it.Bounds = new Rectangle(0, y, w, h);
                        it.TextRect = new Rectangle(Px(2), y - Px(2), w - Px(2), h);
                        y += h + Px(6);
                        i++;
                        continue;
                    }
                    if (it.Kind == KSlot)
                    {
                        int gap = Px(8);
                        int side = Math.Max(Px(110), (w - 3 * gap) / 4);
                        int slotH = Math.Min(side, Math.Max(Px(124), Math.Min(Px(164), side * 3 / 4)));
                        int col = 0;
                        for (; i < items.Count && items[i].Kind == KSlot; i++)
                        {
                            if (col == 4) { col = 0; y += slotH + gap; }
                            FbItem t = items[i];
                            t.Bounds = new Rectangle(col * (side + gap), y, side, slotH);
                            if (t.Anchor != null) t.Anchor.Bounds = t.Bounds;
                            col++;
                        }
                        y += slotH + gap + Px(2);
                        continue;
                    }
                    if (it.Kind == KPicked)
                    {
                        int tw = Math.Max(Px(100), w - Px(150));
                        Size s = MeasureWrap(it.Text, Ui.UiFont(9.5f), tw);
                        it.Bounds = new Rectangle(0, y, tw, s.Height);
                        y += s.Height + Px(8);
                        i++;
                        continue;
                    }
                    if (it.Kind == KColHead || it.Kind == KChip)
                    {
                        int colGap = Px(6);
                        int colW = Math.Max(Px(96), (w - 4 * colGap) / 5);
                        Dictionary<int, int> colY = new Dictionary<int, int>();
                        int bottom = y;
                        for (; i < items.Count && (items[i].Kind == KColHead || items[i].Kind == KChip); i++)
                        {
                            FbItem t = items[i];
                            int cy;
                            if (!colY.TryGetValue(t.Group, out cy)) cy = y;
                            int x = t.Group * (colW + colGap);
                            t.Bounds = new Rectangle(x, cy, colW, Px(40));
                            cy += Px(40) + Px(6);
                            colY[t.Group] = cy;
                            bottom = Math.Max(bottom, cy);
                        }
                        y = bottom + Px(8);
                        continue;
                    }
                    i++;
                }
                Size want = new Size(w, Math.Max(1, y));
                if (Size != want) Size = want;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(CBg);
                Rectangle clip = e.ClipRectangle;
                foreach (FbItem it in items)
                {
                    if (!it.Bounds.IntersectsWith(clip)) continue;
                    switch (it.Kind)
                    {
                        case KText:
                        case KPicked:
                            TextRenderer.DrawText(g, it.Text, Ui.UiFont(9.5f), it.Bounds, CSub, Wrap);
                            break;
                        case KHead:
                            TextRenderer.DrawText(g, it.Text, Ui.UiFont(it.Pt, FontStyle.Bold), it.TextRect.Location, CGold, OneLine);
                            break;
                        case KSlot: PaintSlot(g, it); break;
                        case KColHead: PaintColHead(g, it); break;
                        case KChip: PaintChip(g, it); break;
                    }
                }
            }

            void PaintSlot(Graphics g, FbItem it)
            {
                Rectangle r = it.Bounds;
                bool isHot = it == hot;
                SmoothingMode sm = g.SmoothingMode;
                InterpolationMode im = g.InterpolationMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (GraphicsPath path = RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Px(8)))
                using (SolidBrush b = new SolidBrush(isHot ? CBtnHover : CRow))
                using (Pen p = new Pen(isHot ? CHover : CBorder))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, it.Title.ToUpperInvariant(), Ui.UiFont(8.5f, FontStyle.Bold), new Rectangle(r.X + Px(8), r.Y + Px(10), r.Width - Px(16), Px(18)), CSub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                int top = Px(32), capH = Px(38);
                Color capC = it.Empty ? CSub : AffinityColor(it.Aff ?? "");
                TextFormatFlags capFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (it.Pic != null)
                {
                    int ps = Math.Max(Px(32), Math.Min(Px(110), Math.Min(r.Width - Px(32), r.Height - top - capH - Px(12))));
                    int px = r.X + (r.Width - ps) / 2, py = r.Y + top + (r.Height - top - capH - Px(8) - ps) / 2;
                    g.DrawImage(it.Pic, new Rectangle(px, py, ps, ps));
                    if (it.Elem != null) g.DrawImage(it.Elem, new Rectangle(r.Right - Px(30), r.Y + top, Px(20), Px(20)));
                    TextRenderer.DrawText(g, it.Text, Ui.UiFont(9f, it.Empty ? FontStyle.Regular : FontStyle.Bold),
                        new Rectangle(r.X + Px(8), r.Bottom - capH - Px(8), r.Width - Px(16), capH), capC, capFlags);
                }
                else
                {
                    TextRenderer.DrawText(g, it.Text, Ui.UiFont(it.Empty ? 9.5f : 11f, it.Empty ? FontStyle.Regular : FontStyle.Bold),
                        new Rectangle(r.X + Px(8), r.Y + top, r.Width - Px(16), r.Height - top - Px(16)), capC, capFlags);
                }
                int cx = r.Right - Px(16), cy = r.Bottom - Px(14);
                using (SolidBrush arrow = new SolidBrush(isHot ? CHover : CSub))
                    g.FillPolygon(arrow, new Point[] { new Point(cx - Px(5), cy - Px(2)), new Point(cx + Px(5), cy - Px(2)), new Point(cx, cy + Px(4)) });
                g.SmoothingMode = sm;
                g.InterpolationMode = im;
            }

            void PaintColHead(Graphics g, FbItem it)
            {
                Rectangle r = it.Bounds;
                if (it.Pic != null)
                {
                    int s = Px(34);
                    InterpolationMode im = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(it.Pic, new Rectangle(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s));
                    g.InterpolationMode = im;
                }
                else TextRenderer.DrawText(g, it.Text, Ui.UiFont(9.5f, FontStyle.Bold), r, CText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | OneLine);
            }

            void PaintChip(Graphics g, FbItem it)
            {
                Rectangle r = it.Bounds;
                bool on = it.Level > 0, blocked = it.Blocked;
                Color fill = on ? CGoldBg : blocked ? Color.FromArgb(21, 25, 32) : it == hot ? CBtnHover : CBtn;
                Color edge = on ? CGold : blocked ? Color.FromArgb(33, 39, 48) : CBorder;
                SmoothingMode sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Px(5)))
                using (SolidBrush b = new SolidBrush(fill))
                using (Pen p = new Pen(edge))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                g.SmoothingMode = sm;
                int badgeW = Px(28);
                Color tc = on ? CText : blocked ? Color.FromArgb(78, 84, 96) : CText;
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                Font bold = Ui.UiFont(10f, FontStyle.Bold);
                int tw = Math.Max(Px(20), r.Width - Px(8) - badgeW - Px(4));
                Size need = TextRenderer.MeasureText(g, it.Text, bold, new Size(tw, 0), flags);
                int th = Math.Min(need.Height, r.Height - Px(4));
                TextRenderer.DrawText(g, it.Text, bold, new Rectangle(r.X + Px(8), r.Y + (r.Height - th) / 2, tw, th), tc, flags);
                Rectangle badge = new Rectangle(r.Right - badgeW - Px(6), r.Y, badgeW, r.Height);
                TextFormatFlags bflags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | OneLine;
                if (on) TextRenderer.DrawText(g, "+" + (it.Level * 3), bold, badge, Color.White, bflags);
                else if (!blocked && it.Hint > 0) TextRenderer.DrawText(g, "+" + (it.Hint * 3), Ui.UiFont(10f), badge, Color.White, bflags);
            }

            FbItem HitAt(Point pt)
            {
                foreach (FbItem it in items)
                    if ((it.Kind == KSlot || it.Kind == KChip || it.Kind == KColHead) && it.Bounds.Contains(pt)) return it;
                return null;
            }

            static bool Clickable(FbItem it)
            {
                return it != null && (it.Kind == KSlot || (it.Kind == KChip && !it.Blocked));
            }

            void SetHot(FbItem it)
            {
                if (it == hot) return;
                if (hot != null && hot.Kind != KColHead) Invalidate(hot.Bounds);
                hot = it;
                if (hot != null && hot.Kind != KColHead) Invalidate(hot.Bounds);
                Cursor = Clickable(it) ? Cursors.Hand : Cursors.Default;
                string t = it != null && it.Kind == KColHead ? it.Tip : null;
                if (t != tip)
                {
                    tip = t;
                    win.tips.SetToolTip(this, t);
                }
                win.HideChipTip();
                if (it == null || it.Kind == KColHead || it.Tip == null) return;
                if (it.TipImages != null) win.ShowChipTipSoon(this, it.Bounds, it.Tip, it.TipImages);
                else win.ShowChipTipSoon(this, it.Bounds, it.Tip, it.CellTip);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHot(HitAt(e.Location));
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                SetHot(null);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                FbItem it = HitAt(e.Location);
                SetHot(it);
                down = it != null && (it.Kind == KSlot || it.Kind == KChip) ? it : null;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                FbItem d = down;
                down = null;
                if (d == null || HitAt(e.Location) != d) return;
                if (d.Kind == KChip)
                {
                    if (ChipClick != null) ChipClick(d, e.Button);
                    return;
                }
                if (e.Button == MouseButtons.Left && d.Open != null)
                {
                    win.HideChipTip();
                    d.Open(d.Anchor);
                }
                else if (e.Button == MouseButtons.Right && d.Clear != null)
                {
                    win.HideChipTip();
                    Action<string> clear = d.Clear;
                    win.BeginInvoke((MethodInvoker)delegate { clear(""); });
                }
            }
        }

        class BuildTile
        {
            public Image Pic, ElemA, ElemB;
            public string Affinity = "";
            public bool ArmourElems, NoCells, Small;
            public int Own;
            public string Tip;
            public string Title = "", PerkText = "", Badge = "";
            public readonly List<TileCell> Cells = new List<TileCell>();
            public Func<string, Image> Icon;
            public Action<Control> Open;
            public Control Anchor;
            public Rectangle Bounds;

            static readonly Color CMissing = Color.FromArgb(220, 78, 78);
            static readonly Color CellFill = Color.FromArgb(128, 52, 196);
            static readonly Color CellEdge = Color.FromArgb(186, 120, 240);

            static void DrawIcon(Graphics g, Image img, Rectangle r, float alpha)
            {
                if (img == null) return;
                if (alpha >= 0.99f) { g.DrawImage(img, r); return; }
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = alpha;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                }
            }

            public void Paint(Graphics g)
            {
                Rectangle r = Bounds;
                int ox = r.X, oy = r.Y, W = r.Width, H = r.Height;
                using (GraphicsPath path = RoundedRect(new Rectangle(ox, oy, W - 1, H - 1), Px(8)))
                using (SolidBrush b = new SolidBrush(CBtn))
                using (Pen p = new Pen(CBorder))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }

                Font font = Ui.UiFont(9f);
                int cellCol = NoCells ? 0 : Px(92);
                int cx = ox + W - cellCol;
                if (!NoCells) using (Pen sep = new Pen(CBorder)) g.DrawLine(sep, cx, oy + Px(8), cx, oy + H - Px(9));
                int n = Math.Max(1, Cells.Count);
                int slotH = (H - Px(8)) / n;
                for (int i = 0; i < Cells.Count; i++)
                {
                    TileCell c = Cells[i];
                    bool filled = c.Perk != "";
                    int d = Cells.Count > 1 ? Px(30) : Px(38);
                    int top = oy + Px(4) + i * slotH + (slotH - d - Px(16)) / 2;
                    Rectangle circle = new Rectangle(cx + (cellCol - d) / 2, top, d, d);
                    if (filled)
                    {
                        using (SolidBrush f = new SolidBrush(CellFill)) g.FillEllipse(f, circle);
                        using (Pen p = new Pen(CellEdge)) g.DrawEllipse(p, circle);
                    }
                    else using (Pen p = new Pen(CBorder, PxF(1.5f))) g.DrawEllipse(p, circle);
                    Image icon = Icon == null ? null : Icon(filled ? c.Perk : c.SlotType);
                    int pad = d / 5;
                    DrawIcon(g, icon, new Rectangle(circle.X + pad, circle.Y + pad, d - pad * 2, d - pad * 2), filled ? 1f : 0.35f);
                    string label = filled ? "+3 " + c.Perk : "Empty";
                    TextRenderer.DrawText(g, label, font, new Rectangle(cx + Px(2), circle.Bottom + 1, cellCol - Px(4), Px(16)), filled ? CText : CSub,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                int pic = Math.Min(Small ? Px(40) : Px(64), H - (Small ? Px(16) : Px(24)));
                if (Pic != null) g.DrawImage(Pic, new Rectangle(ox + Px(10), oy + (H - pic) / 2, pic, pic));
                if (Own < 0)
                {
                    PointF[] tri = new PointF[] { new PointF(ox + PxF(15f), oy + PxF(5f)), new PointF(ox + PxF(24f), oy + PxF(20f)), new PointF(ox + PxF(6f), oy + PxF(20f)) };
                    using (SolidBrush rb = new SolidBrush(CMissing)) g.FillPolygon(rb, tri);
                    TextRenderer.DrawText(g, "!", Ui.UiFont(7.5f, FontStyle.Bold), new Rectangle(ox + Px(6), oy + Px(8), Px(18), Px(12)), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }

                int tx = ox + Px(10) + pic + Px(10), tw = Math.Max(Px(30), cx - tx - Px(8));
                int y = oy + (Small ? Px(6) : Px(10));
                if (Small && Badge != "")
                {
                    TextRenderer.DrawText(g, Badge.ToUpperInvariant(), font, new Rectangle(tx, y, tw, Px(16)), CSub,
                        TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    y += 16;
                }
                Font bold = Ui.UiFont(Small ? 9.5f : 10f, FontStyle.Bold);
                TextFormatFlags wrap = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                Size ts = TextRenderer.MeasureText(g, Title, bold, new Size(tw, 0), wrap);
                int th = Math.Min(ts.Height, Small ? oy + H - y - Px(4) : Px(38));
                TextRenderer.DrawText(g, Title, bold, new Rectangle(tx, y, tw, th), AffinityColor(Affinity), wrap);
                y += th + Px(3);
                int ex = tx;
                TextFormatFlags one = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                if (ArmourElems)
                {
                    if (ElemA != null)
                    {
                        TextRenderer.DrawText(g, "+", font, new Rectangle(ex, y, Px(12), Px(18)), CText, one);
                        g.DrawImage(ElemA, new Rectangle(ex + Px(11), y + 1, Px(16), Px(16)));
                        ex += Px(34);
                    }
                    if (ElemB != null)
                    {
                        TextRenderer.DrawText(g, "-", font, new Rectangle(ex, y, Px(12), Px(18)), CText, one);
                        g.DrawImage(ElemB, new Rectangle(ex + Px(10), y + 1, Px(16), Px(16)));
                        ex += Px(34);
                    }
                }
                else if (ElemA != null)
                {
                    g.DrawImage(ElemA, new Rectangle(ex, y + 1, Px(16), Px(16)));
                    ex += Px(22);
                }
                if (ex > tx) y += Px(21);
                if (PerkText != "")
                {
                    TextRenderer.DrawText(g, PerkText, font, new Rectangle(tx, y, tw, Px(18)), CText, one | TextFormatFlags.EndEllipsis);
                    y += 19;
                }
                if (!Small && Badge != "" && y + Px(16) <= oy + H - Px(4))
                    TextRenderer.DrawText(g, Badge, font, new Rectangle(tx, oy + H - Px(24), tw, Px(18)), CSub, one | TextFormatFlags.EndEllipsis);
            }
        }

        class CardBtn
        {
            public int Kind;
            public bool On, ExpandedOnly;
            public string Tip;
            public Rectangle Bounds;
            public Action Click;
            public Control Anchor;
        }

        class CardPerk
        {
            public string Perk = "", Tip;
            public int Value, Over;
            public Image Icon;
            public Rectangle Bounds;
        }

        class CardCanvas : Control
        {
            public const int BChev = 0, BPin = 1, BCopy = 2, BShare = 3, BStar = 4;
            const TextFormatFlags OneLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            static readonly object TitleHit = new object(), RingHit = new object(), ThumbHit = new object();

            readonly SettingsWindow win;
            public string Title = "", Info = "", Preview = "", RingTip, PinKey;
            public bool Collapsed;
            public int RingHave, RingAll;
            public CardBtn Chev;
            public readonly List<CardBtn> RightBtns = new List<CardBtn>();
            public readonly List<BuildTile> Grid = new List<BuildTile>();
            public readonly List<BuildTile> Extras = new List<BuildTile>();
            public readonly List<CardPerk> Perks = new List<CardPerk>();
            public TextBox Rename, Notes;
            public Action Toggle;

            Rectangle ringRect, titleRect, infoRect, renameRect, notesHeadRect, notesRect, perkCol, perkArea;
            bool shownCollapsed;
            int perkScroll, perkContentH, lastW = -1, dragY = -1, dragScroll;
            object hot, down;
            string tip;

            public CardCanvas(SettingsWindow win)
            {
                this.win = win;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
                BackColor = CRow;
                Tag = "buildcard";
                Margin = new Padding(0, 0, 0, Px(10));
            }

            public Control NewAnchor()
            {
                Control a = new Control();
                a.TabStop = false;
                a.Region = new Region(Rectangle.Empty);
                Controls.Add(a);
                return a;
            }

            public CardBtn AddButton(int kind, bool expandedOnly)
            {
                CardBtn b = new CardBtn();
                b.Kind = kind;
                b.ExpandedOnly = expandedOnly;
                if (kind == BChev) Chev = b;
                else RightBtns.Add(b);
                return b;
            }

            public void SetPin(bool on)
            {
                foreach (CardBtn b in RightBtns)
                    if (b.Kind == BPin)
                    {
                        b.On = on;
                        b.Tip = on ? "Remove from screen." : "Pin to screen. The Clear Bars key removes it.";
                        if (!b.Bounds.IsEmpty) Invalidate(b.Bounds);
                    }
                UpdateTip();
            }

            public void Changed(CardBtn b)
            {
                if (!b.Bounds.IsEmpty) Invalidate(b.Bounds);
                UpdateTip();
            }

            public void UpdateTip()
            {
                string t = TipOf(hot);
                if (t == tip) return;
                tip = t;
                win.tips.SetToolTip(this, t);
            }

            string TipOf(object it)
            {
                CardBtn b = it as CardBtn;
                if (b != null) return b.Tip;
                if (it == RingHit) return RingTip;
                return null;
            }

            public void Relayout()
            {
                if (Chev != null) Chev.Tip = Collapsed ? "Expand build." : "Collapse build.";
                if (lastW > 0) LayoutAt(lastW);
                UpdateTip();
            }

            public void LayoutAt(int w)
            {
                lastW = w;
                if (Chev != null) Chev.Tip = Collapsed ? "Expand build." : "Collapse build.";
                int h = Arrange(w, Collapsed, true);
                Size want = new Size(w, h);
                if (Size != want) Size = want;
                Invalidate();
            }

            int PerkMax()
            {
                return Math.Max(0, perkContentH - perkArea.Height);
            }

            int Arrange(int w, bool collapsed, bool apply)
            {
                shownCollapsed = collapsed;
                int padX = Px(14), padY = Px(10), headH = Px(30);
                int inner = w - padX * 2;
                int x0 = padX, y0 = padY;
                int right = x0 + inner;
                foreach (CardBtn b in RightBtns)
                {
                    if (collapsed && b.ExpandedOnly) b.Bounds = Rectangle.Empty;
                    else
                    {
                        b.Bounds = new Rectangle(right - Px(28), y0 + (headH - Px(28)) / 2, Px(28), Px(28));
                        right = b.Bounds.X - Px(8);
                    }
                    if (apply && b.Anchor != null) b.Anchor.Bounds = b.Bounds.IsEmpty ? new Rectangle(0, 0, 1, 1) : b.Bounds;
                }
                renameRect = Rectangle.Empty;
                if (Rename != null)
                {
                    if (apply) Rename.Visible = !collapsed;
                    if (!collapsed)
                    {
                        int rw = Px(220), rh = Rename.Height;
                        renameRect = new Rectangle(right - rw, y0 + (headH - rh) / 2, rw, rh);
                        if (apply) Rename.Bounds = renameRect;
                        right = renameRect.X - Px(8);
                    }
                }
                if (Notes != null && apply) Notes.Visible = !collapsed;
                int x = x0;
                if (Chev != null)
                {
                    Chev.Bounds = new Rectangle(x0, y0 + (headH - Px(28)) / 2, Px(22), Px(28));
                    x = Chev.Bounds.Right + Px(4);
                }
                if (RingAll > 0)
                {
                    ringRect = new Rectangle(x, y0 + (headH - Px(28)) / 2, Px(28), Px(28));
                    x = ringRect.Right + Px(8);
                }
                else ringRect = Rectangle.Empty;
                Size ts = TextRenderer.MeasureText(Title, Ui.UiFont(10.5f, FontStyle.Bold), new Size(int.MaxValue, int.MaxValue), OneLine);
                titleRect = new Rectangle(x, y0 + Px(5), Math.Min(ts.Width, Math.Max(Px(40), right - x)), ts.Height);
                int after = titleRect.Right + Px(12);
                infoRect = new Rectangle(after, y0 + Px(7), Math.Max(Px(20), right - after), Px(18));
                int y = y0 + headH;
                if (collapsed)
                {
                    foreach (BuildTile t in Grid) if (apply && t.Anchor != null) t.Anchor.Bounds = new Rectangle(0, 0, 1, 1);
                    foreach (BuildTile t in Extras) if (apply && t.Anchor != null) t.Anchor.Bounds = new Rectangle(0, 0, 1, 1);
                    return y + padY;
                }
                y += Px(8);
                if (Notes != null)
                {
                    Size nh = TextRenderer.MeasureText("NOTES", Ui.UiFont(8.5f, FontStyle.Bold), new Size(int.MaxValue, int.MaxValue), OneLine);
                    notesHeadRect = new Rectangle(x0, y, nh.Width, nh.Height);
                    y += nh.Height + Px(4);
                    notesRect = new Rectangle(x0, y, inner, Px(56));
                    if (apply) Notes.Bounds = notesRect;
                    y += Px(56) + Px(10);
                }
                int gap = Px(10), tileGap = Px(8), tileH = Px(112);
                int lineW = PerkLineW();
                int perkW = lineW + Px(20);
                int gridW = Math.Max(Px(200), inner - perkW - gap);
                int gridH = tileH * 3 + tileGap * 2;
                int tileW = (gridW - tileGap) / 2;
                for (int ti = 0; ti < Grid.Count; ti++)
                {
                    BuildTile t = Grid[ti];
                    t.Bounds = new Rectangle(x0 + (ti % 2) * (tileW + tileGap), y + (ti / 2) * (tileH + tileGap), tileW, tileH);
                    if (apply && t.Anchor != null) t.Anchor.Bounds = t.Bounds;
                }
                perkCol = new Rectangle(x0 + gridW + gap, y, Math.Max(Px(80), inner - gridW - gap), gridH);
                perkArea = new Rectangle(perkCol.X + 1, perkCol.Y + Px(30), perkCol.Width - Px(2), perkCol.Height - Px(36));
                perkContentH = Px(4) + Perks.Count * Px(28);
                int avail = perkArea.Width - (perkContentH > perkArea.Height ? Px(10) : 0);
                int rowW = Math.Max(Px(40), Math.Min(lineW, avail));
                int left = Math.Max(0, (avail - rowW) / 2);
                for (int i = 0; i < Perks.Count; i++) Perks[i].Bounds = new Rectangle(perkArea.X + left, perkArea.Y + i * Px(28), rowW, Px(26));
                perkScroll = Math.Max(0, Math.Min(PerkMax(), perkScroll));
                y += gridH;
                if (Extras.Count > 0)
                {
                    y += tileGap;
                    int n = Extras.Count;
                    int ew = (inner - tileGap * (n - 1)) / n;
                    for (int ti = 0; ti < n; ti++)
                    {
                        BuildTile t = Extras[ti];
                        t.Bounds = new Rectangle(x0 + ti * (ew + tileGap), y, ew, Px(64));
                        if (apply && t.Anchor != null) t.Anchor.Bounds = t.Bounds;
                    }
                    y += Px(64);
                }
                return y + padY;
            }

            public Bitmap Snapshot()
            {
                int w = Width;
                int h = Arrange(w, false, false);
                Bitmap bmp = new Bitmap(w, h);
                try
                {
                    using (Graphics g = Graphics.FromImage(bmp)) Render(g, w, h, true);
                }
                finally
                {
                    Arrange(w, Collapsed, false);
                }
                return bmp;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Render(e.Graphics, Width, Height, false);
            }

            void Render(Graphics g, int w, int h, bool shot)
            {
                g.Clear(Parent != null ? Parent.BackColor : CBg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Px(10)))
                {
                    using (SolidBrush f = new SolidBrush(CRow)) g.FillPath(f, path);
                    using (Pen p = new Pen(CBorder)) g.DrawPath(p, path);
                }
                if (Chev != null) DrawBtn(g, Chev, !shownCollapsed);
                foreach (CardBtn b in RightBtns) if (!b.Bounds.IsEmpty) DrawBtn(g, b, b.On);
                if (!ringRect.IsEmpty) DrawRing(g);
                TextRenderer.DrawText(g, Title, Ui.UiFont(10.5f, FontStyle.Bold), titleRect, CText, OneLine | TextFormatFlags.EndEllipsis);
                if (shownCollapsed)
                    TextRenderer.DrawText(g, Preview, Ui.UiFont(9.5f, FontStyle.Italic), infoRect, CSub, OneLine | TextFormatFlags.EndEllipsis);
                else
                    TextRenderer.DrawText(g, Info, Ui.UiFont(9.5f), infoRect, CSub, OneLine | TextFormatFlags.EndEllipsis);
                if (shot && Rename != null && !renameRect.IsEmpty) DrawBox(g, renameRect, Rename.Text, Rename.Font, false);
                if (shownCollapsed) return;
                if (Notes != null)
                {
                    TextRenderer.DrawText(g, "NOTES", Ui.UiFont(8.5f, FontStyle.Bold), notesHeadRect.Location, CGold, OneLine);
                    if (shot) DrawBox(g, notesRect, Notes.Text, Notes.Font, true);
                }
                foreach (BuildTile t in Grid) t.Paint(g);
                DrawPerks(g);
                foreach (BuildTile t in Extras) t.Paint(g);
            }

            void DrawBox(Graphics g, Rectangle r, string text, Font f, bool wrap)
            {
                using (SolidBrush b = new SolidBrush(CBtn)) g.FillRectangle(b, r);
                using (Pen p = new Pen(CBorder)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                Rectangle tr = new Rectangle(r.X + Px(3), r.Y + Px(2), r.Width - Px(6), r.Height - Px(4));
                TextRenderer.DrawText(g, text ?? "", f, tr, CText,
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter));
            }

            void DrawPerks(Graphics g)
            {
                using (GraphicsPath path = RoundedRect(new Rectangle(perkCol.X, perkCol.Y, perkCol.Width - 1, perkCol.Height - 1), Px(8)))
                using (SolidBrush b = new SolidBrush(CBtn))
                using (Pen p = new Pen(CBorder))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                Font head = Ui.UiFont(8.5f, FontStyle.Bold);
                Size hs = TextRenderer.MeasureText("PERKS", head, new Size(int.MaxValue, int.MaxValue), OneLine);
                TextRenderer.DrawText(g, "PERKS", head, new Point(perkCol.X + (perkCol.Width - hs.Width) / 2, perkCol.Y + Px(8)), CGold, OneLine);
                if (Perks.Count == 0)
                {
                    Font sf = Ui.UiFont(9f);
                    Size ns = TextRenderer.MeasureText("No perks", sf, new Size(int.MaxValue, int.MaxValue), OneLine);
                    TextRenderer.DrawText(g, "No perks", sf, new Point(perkArea.X + (perkArea.Width - ns.Width) / 2, perkArea.Y), CSub, OneLine);
                    return;
                }
                Region old = g.Clip;
                g.SetClip(perkArea, CombineMode.Intersect);
                TextFormatFlags one = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
                Font f = Ui.UiFont(9f), bold = Ui.UiFont(9f, FontStyle.Bold);
                foreach (CardPerk cp in Perks)
                {
                    Rectangle r = cp.Bounds;
                    r.Y -= perkScroll;
                    if (r.Bottom < perkArea.Top || r.Top > perkArea.Bottom) continue;
                    if (cp.Icon != null) g.DrawImage(cp.Icon, new Rectangle(r.X + Px(2), r.Y + (r.Height - Px(18)) / 2, Px(18), Px(18)));
                    TextRenderer.DrawText(g, "+" + cp.Value, bold, new Rectangle(r.X + Px(24), r.Y, Px(28), r.Height), Color.White, one);
                    string text = cp.Perk + (cp.Over > 0 ? " (+" + cp.Over + " over)" : "");
                    TextRenderer.DrawText(g, text, f, new Rectangle(r.X + Px(52), r.Y, Math.Max(Px(10), r.Width - Px(54)), r.Height), CText, one | TextFormatFlags.EndEllipsis);
                }
                g.Clip = old;
                old.Dispose();
                int max = PerkMax();
                if (max <= 0) return;
                Rectangle thumb = ThumbRect();
                using (GraphicsPath tp = RoundedRect(thumb, Px(3)))
                using (SolidBrush tb = new SolidBrush(hot == ThumbHit || dragY >= 0 ? CSub : CBorder))
                    g.FillPath(tb, tp);
            }

            Rectangle ThumbRect()
            {
                int max = PerkMax();
                int th = Math.Max(Px(24), perkArea.Height * perkArea.Height / Math.Max(1, perkContentH));
                int ty = perkArea.Y + (max <= 0 ? 0 : (perkArea.Height - th) * perkScroll / max);
                return new Rectangle(perkArea.Right - Px(8), ty, Px(6), th);
            }

            void DrawRing(Graphics g)
            {
                Rectangle r = new Rectangle(ringRect.X + Px(2), ringRect.Y + Px(2), ringRect.Width - Px(5), ringRect.Height - Px(5));
                float frac = RingAll <= 0 ? 0f : (float)RingHave / RingAll;
                Color c = frac >= 0.999f ? CGold : CPill;
                using (Pen track = new Pen(CBorder, PxF(3f))) g.DrawEllipse(track, r);
                if (frac > 0f) using (Pen arc = new Pen(c, PxF(3f))) { arc.StartCap = LineCap.Round; arc.EndCap = LineCap.Round; g.DrawArc(arc, r, -90f, 360f * frac); }
                int pct = RingAll <= 0 ? 0 : (int)Math.Floor(100.0 * RingHave / RingAll);
                TextRenderer.DrawText(g, pct.ToString(CultureInfo.InvariantCulture), Ui.UiFont(pct >= 100 ? 6.5f : 7f, FontStyle.Bold), ringRect, CText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            void DrawBtn(Graphics g, CardBtn btn, bool on)
            {
                bool isHot = hot == btn;
                float cx = btn.Bounds.X + btn.Bounds.Width / 2f, cy = btn.Bounds.Y + btn.Bounds.Height / 2f;
                if (btn.Kind == BStar)
                {
                    PointF[] pts = new PointF[10];
                    float outer = Math.Min(btn.Bounds.Width, btn.Bounds.Height) / 2f - Px(2), innerR = outer * 0.42f;
                    for (int i = 0; i < 10; i++)
                    {
                        double a = (-90 + i * 36) * Math.PI / 180.0;
                        float rr = i % 2 == 0 ? outer : innerR;
                        pts[i] = new PointF(cx + (float)(Math.Cos(a) * rr), cy + 1 + (float)(Math.Sin(a) * rr));
                    }
                    Color star = Color.FromArgb(0xFF, 0xD2, 0x3F);
                    if (on) using (SolidBrush b = new SolidBrush(star)) g.FillPolygon(b, pts);
                    using (Pen p = new Pen(on ? star : isHot ? CHover : CSub, PxF(1.6f))) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, pts); }
                    return;
                }
                Color c = btn.Kind == BPin && on ? CGold : isHot ? CHover : CSub;
                using (Pen p = new Pen(c, PxF(1.6f)))
                {
                    p.LineJoin = LineJoin.Round;
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    if (btn.Kind == BChev)
                    {
                        PointF[] pts = on
                            ? new PointF[] { new PointF(cx - Px(5), cy - PxF(2.5f)), new PointF(cx, cy + PxF(2.5f)), new PointF(cx + Px(5), cy - PxF(2.5f)) }
                            : new PointF[] { new PointF(cx - PxF(2.5f), cy - Px(5)), new PointF(cx + PxF(2.5f), cy), new PointF(cx - PxF(2.5f), cy + Px(5)) };
                        g.DrawLines(p, pts);
                    }
                    else if (btn.Kind == BShare)
                    {
                        PointF a = new PointF(cx + Px(5), cy - Px(6)), b2 = new PointF(cx - Px(5), cy), c2 = new PointF(cx + Px(5), cy + Px(6));
                        g.DrawLine(p, b2, a);
                        g.DrawLine(p, b2, c2);
                        using (SolidBrush b = new SolidBrush(c))
                            foreach (PointF pt in new PointF[] { a, b2, c2 }) g.FillEllipse(b, pt.X - PxF(2.8f), pt.Y - PxF(2.8f), PxF(5.6f), PxF(5.6f));
                    }
                    else if (btn.Kind == BCopy)
                    {
                        g.DrawRectangle(p, cx - Px(6), cy - Px(8), Px(10), Px(12));
                        using (SolidBrush b = new SolidBrush(CRow)) g.FillRectangle(b, cx - Px(3), cy - Px(4), Px(10), Px(12));
                        g.DrawRectangle(p, cx - Px(3), cy - Px(4), Px(10), Px(12));
                    }
                    else
                    {
                        PointF[] head = new PointF[]
                        {
                            new PointF(cx - Px(4), cy - Px(8)), new PointF(cx + Px(4), cy - Px(8)), new PointF(cx + Px(3), cy - 1),
                            new PointF(cx + Px(6), cy + Px(2)), new PointF(cx - Px(6), cy + Px(2)), new PointF(cx - Px(3), cy - 1)
                        };
                        if (on) using (SolidBrush b = new SolidBrush(CGold)) g.FillPolygon(b, head);
                        g.DrawPolygon(p, head);
                        g.DrawLine(p, cx, cy + Px(2), cx, cy + Px(9));
                    }
                }
            }

            Rectangle PerkRect(CardPerk cp)
            {
                Rectangle r = cp.Bounds;
                r.Y -= perkScroll;
                return Rectangle.Intersect(r, perkArea);
            }

            object HitAt(Point pt)
            {
                if (Chev != null && Chev.Bounds.Contains(pt)) return Chev;
                foreach (CardBtn b in RightBtns) if (!b.Bounds.IsEmpty && b.Bounds.Contains(pt)) return b;
                if (Toggle != null && titleRect.Contains(pt)) return TitleHit;
                if (!ringRect.IsEmpty && ringRect.Contains(pt)) return RingHit;
                if (shownCollapsed) return null;
                foreach (BuildTile t in Grid) if (t.Bounds.Contains(pt)) return t;
                foreach (BuildTile t in Extras) if (t.Bounds.Contains(pt)) return t;
                if (perkArea.Contains(pt))
                {
                    if (PerkMax() > 0 && pt.X >= perkArea.Right - Px(10)) return ThumbHit;
                    foreach (CardPerk cp in Perks) if (PerkRect(cp).Contains(pt)) return cp;
                }
                return null;
            }

            Rectangle RectOf(object it)
            {
                CardBtn b = it as CardBtn;
                if (b != null) return b.Bounds;
                if (it == ThumbHit) return new Rectangle(perkArea.Right - Px(10), perkArea.Y, Px(10), perkArea.Height);
                return Rectangle.Empty;
            }

            void SetHot(object it)
            {
                if (it == hot) return;
                Rectangle oldR = RectOf(hot), newR = RectOf(it);
                hot = it;
                if (!oldR.IsEmpty) Invalidate(oldR);
                if (!newR.IsEmpty) Invalidate(newR);
                BuildTile bt = it as BuildTile;
                bool hand = it is CardBtn || it == TitleHit || (bt != null && bt.Open != null);
                Cursor = hand ? Cursors.Hand : Cursors.Default;
                UpdateTip();
                win.HideChipTip();
                if (bt != null && !string.IsNullOrEmpty(bt.Tip)) win.ShowChipTipSoon(this, bt.Bounds, bt.Tip, false);
                CardPerk cp = it as CardPerk;
                if (cp != null && cp.Tip != null) win.ShowChipTipSoon(this, PerkRect(cp), cp.Tip, true);
            }

            void SetScroll(int v)
            {
                v = Math.Max(0, Math.Min(PerkMax(), v));
                if (v == perkScroll) return;
                perkScroll = v;
                Invalidate(perkArea);
                hot = null;
                SetHot(HitAt(PointToClient(Control.MousePosition)));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragY >= 0)
                {
                    Rectangle thumb = ThumbRect();
                    int track = perkArea.Height - thumb.Height;
                    if (track > 0) SetScroll(dragScroll + (e.Y - dragY) * PerkMax() / track);
                    return;
                }
                SetHot(HitAt(e.Location));
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (dragY < 0) SetHot(null);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                object it = HitAt(e.Location);
                SetHot(it);
                if (it == ThumbHit && e.Button == MouseButtons.Left)
                {
                    Rectangle thumb = ThumbRect();
                    if (!thumb.Contains(e.Location))
                    {
                        SetScroll(perkScroll + (e.Y < thumb.Y ? -perkArea.Height : perkArea.Height));
                        down = null;
                        return;
                    }
                    dragY = e.Y;
                    dragScroll = perkScroll;
                    Capture = true;
                    Invalidate(RectOf(ThumbHit));
                    down = null;
                    return;
                }
                down = it is CardBtn || it == TitleHit || it is BuildTile ? it : null;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (dragY >= 0)
                {
                    dragY = -1;
                    Capture = false;
                    Invalidate(RectOf(ThumbHit));
                    hot = null;
                    SetHot(HitAt(e.Location));
                    return;
                }
                object d = down;
                down = null;
                if (d == null || e.Button != MouseButtons.Left || HitAt(e.Location) != d) return;
                CardBtn b = d as CardBtn;
                if (b != null)
                {
                    if (b.Click != null) b.Click();
                    return;
                }
                if (d == TitleHit)
                {
                    if (Toggle != null) Toggle();
                    return;
                }
                BuildTile t = d as BuildTile;
                if (t != null && t.Open != null)
                {
                    win.HideChipTip();
                    t.Open(t.Anchor);
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (!shownCollapsed && PerkMax() > 0 && perkArea.Contains(e.Location))
                {
                    SetScroll(perkScroll - e.Delta * Px(28) * 3 / 120);
                    HandledMouseEventArgs h = e as HandledMouseEventArgs;
                    if (h != null) h.Handled = true;
                    return;
                }
                base.OnMouseWheel(e);
            }
        }

        const string SavedPage = "Saved Builds";

        class SavedBuild
        {
            public string Name = "", Weapon = "", Bond = "", Head = "", Torso = "", Arms = "", Legs = "", Spots = "", Notes = "";
            public string Lantern = "", Omnicell = "", Part1 = "", Part2 = "", Part3 = "";
            public bool Collapsed;
            public string Key { get { return Weapon + "|" + Bond + "|" + Head + "|" + Torso + "|" + Arms + "|" + Legs + "|" + Spots; } }
            public SavedBuild Clone() { return (SavedBuild)MemberwiseClone(); }
        }

        List<SavedBuild> bsSaved;
        Timer savedSaveTimer;

        string SavedPath()
        {
            return System.IO.Path.Combine(ModPaths.Root(), "data", "saved-builds.txt");
        }

        List<SavedBuild> SavedBuilds()
        {
            if (bsSaved == null)
            {
                try { bsSaved = ReadSavedFile(SavedPath()); }
                catch (Exception e)
                {
                    SetStatus("Could not read saved-builds.txt, so nothing was changed: " + e.Message, false);
                    return new List<SavedBuild>();
                }
            }
            return bsSaved;
        }

        static List<SavedBuild> ReadSavedFile(string path)
        {
            List<SavedBuild> list = new List<SavedBuild>();
            if (!File.Exists(path)) return list;
            SavedBuild cur = null;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line == "" || line.StartsWith(";")) continue;
                if (line == "[build]") { cur = new SavedBuild(); list.Add(cur); continue; }
                int eq = line.IndexOf('=');
                if (cur == null || eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                if (k == "name") cur.Name = v;
                else if (k == "weapon") cur.Weapon = v;
                else if (k == "bond") cur.Bond = v;
                else if (k == "head") cur.Head = v;
                else if (k == "torso") cur.Torso = v;
                else if (k == "arms") cur.Arms = v;
                else if (k == "legs") cur.Legs = v;
                else if (k == "cells") cur.Spots = v;
                else if (k == "notes") cur.Notes = UnescapeNotes(v);
                else if (k == "collapsed") cur.Collapsed = v == "true";
                else if (k == "lantern") cur.Lantern = v;
                else if (k == "omnicell") cur.Omnicell = v;
                else if (k == "parts")
                {
                    string[] ps = v.Split(',');
                    cur.Part1 = ps.Length > 0 ? ps[0].Trim() : "";
                    cur.Part2 = ps.Length > 1 ? ps[1].Trim() : "";
                    cur.Part3 = ps.Length > 2 ? ps[2].Trim() : "";
                }
            }
            return list;
        }

        static void WriteSavedFile(string path, List<SavedBuild> builds)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            StringBuilder sb = new StringBuilder();
            sb.Append("; Dauntless Companion - builds saved with the star on Find Builds.\r\n");
            foreach (SavedBuild b in builds)
            {
                sb.Append("\r\n[build]\r\n");
                sb.Append("name = " + b.Name + "\r\n");
                sb.Append("weapon = " + b.Weapon + "\r\n");
                sb.Append("bond = " + b.Bond + "\r\n");
                sb.Append("head = " + b.Head + "\r\n");
                sb.Append("torso = " + b.Torso + "\r\n");
                sb.Append("arms = " + b.Arms + "\r\n");
                sb.Append("legs = " + b.Legs + "\r\n");
                sb.Append("cells = " + b.Spots + "\r\n");
                sb.Append("lantern = " + b.Lantern + "\r\n");
                sb.Append("omnicell = " + b.Omnicell + "\r\n");
                sb.Append("parts = " + b.Part1 + "," + b.Part2 + "," + b.Part3 + "\r\n");
                sb.Append("notes = " + EscapeNotes(b.Notes) + "\r\n");
                sb.Append("collapsed = " + (b.Collapsed ? "true" : "false") + "\r\n");
            }
            SafeFile.Write(path, sb.ToString());
        }

        bool SaveSavedBuilds()
        {
            if (bsSaved == null)
            {
                SetStatus("Could not read saved-builds.txt, so your builds weren't changed", false);
                return false;
            }
            try
            {
                WriteSavedFile(SavedPath(), bsSaved);
                return true;
            }
            catch (Exception e)
            {
                SetStatus("Could not save your builds: " + e.Message, false);
                return false;
            }
        }

        static string PinnedPath(string modDir)
        {
            return System.IO.Path.Combine(modDir, "data", "pinned-build.txt");
        }

        static SavedBuild ReadPinned(string modDir)
        {
            try
            {
                List<SavedBuild> list = ReadSavedFile(PinnedPath(modDir));
                return list.Count > 0 ? list[0] : null;
            }
            catch { return null; }
        }

        string ModDir() { return ModPaths.Root(); }

        readonly List<CardCanvas> pinCards = new List<CardCanvas>();

        void SetPinned(SavedBuild b)
        {
            try
            {
                string path = PinnedPath(ModDir());
                if (b == null) { if (File.Exists(path)) File.Delete(path); }
                else WriteSavedFile(path, new List<SavedBuild>(new SavedBuild[] { b }));
            }
            catch (Exception e) { SetStatus("Could not pin the build: " + e.Message, false); }
            RefreshPinButtons();
        }

        void RefreshPinButtons()
        {
            SavedBuild cur = ReadPinned(ModDir());
            string key = cur != null ? cur.Key : null;
            for (int i = pinCards.Count - 1; i >= 0; i--)
            {
                CardCanvas pc = pinCards[i];
                if (pc.IsDisposed) { pinCards.RemoveAt(i); continue; }
                pc.SetPin(pc.PinKey == key);
            }
        }

        public void UnpinAll()
        {
            SetPinned(null);
        }

        public static void UnpinInFile(string modDir)
        {
            try
            {
                string path = PinnedPath(modDir);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception fail) { Diag.Fail("removing pinned-build.txt", fail); }
        }

        public static Bitmap RenderPinnedBuild(string modDir, double scale, string clearKey, int opacity)
        {
            if (!BuildSearch.Load(System.IO.Path.Combine(modDir, "data", "build-data.txt"))) return null;
            SavedBuild pin = ReadPinned(modDir);
            if (pin == null) return null;
            BsWeapon w = BuildSearch.FindWeapon(pin.Weapon);
            BsResult r = FromSaved(pin);
            List<string> where = new List<string>();
            List<string> names = new List<string>();
            List<Image> pics = new List<Image>();
            where.Add("Weapon");
            names.Add(pin.Weapon + (pin.Bond != "" ? "  +  " + pin.Bond : ""));
            pics.Add(w != null ? BsImageAt(modDir, w.Icon) : null);
            for (int pc = 0; pc < 4; pc++)
            {
                BsArmour a = r.Parts[pc].Pieces[0];
                where.Add(BuildSearch.Pieces[pc]);
                names.Add(a.Name);
                pics.Add(BsImageAt(modDir, a.Icon));
            }
            where.Add("Lantern");
            BsGear pl = BuildSearch.FindGear(BuildSearch.Lanterns, pin.Lantern);
            names.Add(pl != null ? pl.Name : "Any Lantern");
            Image lp = pl != null ? BsImageAt(modDir, pl.Icon) : null;
            pics.Add(lp ?? BsImageAt(modDir, "Lantern.png"));
            BsGear po = BuildSearch.FindGear(BuildSearch.Omnicells, pin.Omnicell);
            if (po != null) { where.Add("Omnicell"); names.Add(po.Name); pics.Add(BsImageAt(modDir, po.Icon)); }
            for (int i = 0; i < 3; i++)
            {
                BsGear pp = BuildSearch.FindGear(BuildSearch.Parts, GetPart(pin, i));
                if (pp != null && w != null && pp.Type == w.Type) { where.Add("Part" + i); names.Add(pp.Name); pics.Add(BsImageAt(modDir, pp.Icon)); }
            }

            Func<double, int> S = delegate(double v) { return (int)Math.Round(v * scale); };
            int width = S(300), pad = S(12), pic = S(36), cellH = S(19), rowGap = S(8), titleH = S(24);
            List<List<string[]>> cells = new List<List<string[]>>();
            int hintH = S(16);
            int height = pad + titleH + hintH + S(8);
            for (int i = 0; i < where.Count; i++)
            {
                List<string[]> mine = new List<string[]>();
                foreach (string[] spot in r.CellSpots) if (spot[0] == where[i]) mine.Add(spot);
                cells.Add(mine);
                height += Math.Max(pic, S(20) + cellH * mine.Count) + rowGap;
            }
            height += pad - rowGap;
            opacity = Math.Max(0, Math.Min(100, opacity));

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font titleFont = new Font("Segoe UI", S(15), FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font nameFont = new Font("Segoe UI", S(13), FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font cellFont = new Font("Segoe UI", S(12.5), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font hintFont = new Font("Segoe UI", S(12.5), FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(240, 242, 246)))
            using (SolidBrush gold = new SolidBrush(CGold))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 158, 172)))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, width - 1, height - 1), S(10)))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(opacity * 255 / 100, 12, 15, 20)))
                using (Pen edge = new Pen(Color.FromArgb(Math.Min(170, opacity * 255 / 100), 70, 78, 92)))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(edge, path);
                }
                StringFormat one = new StringFormat(StringFormatFlags.NoWrap);
                one.Trimming = StringTrimming.EllipsisCharacter;
                one.LineAlignment = StringAlignment.Center;
                g.DrawString(pin.Name, titleFont, white, new RectangleF(pad, pad, width - pad * 2, titleH), one);
                g.DrawString(clearKey + " clears this popup", hintFont, dim, new RectangleF(pad, pad + titleH, width - pad * 2, hintH), one);
                int y = pad + titleH + hintH + S(8);
                int tx = pad + pic + S(10), tw = width - tx - pad;
                for (int i = 0; i < where.Count; i++)
                {
                    int rowH = Math.Max(pic, S(20) + cellH * cells[i].Count);
                    if (pics[i] != null) g.DrawImage(pics[i], new Rectangle(pad, y + (rowH - pic) / 2, pic, pic));
                    {
                        string af = i == 0 && w != null ? w.Element : i >= 1 && i <= 4 ? r.Parts[i - 1].Pieces[0].Strength : "";
                        using (SolidBrush nb = new SolidBrush(string.IsNullOrEmpty(af) ? Color.FromArgb(240, 242, 246) : AffinityColor(af)))
                            g.DrawString(names[i], nameFont, nb, new RectangleF(tx, y, tw, S(20)), one);
                    }
                    int cy = y + S(20);
                    foreach (string[] spot in cells[i])
                    {
                        bool filled = spot[2] != "";
                        BsPerk pk = filled ? BuildSearch.FindPerk(spot[2]) : null;
                        Image icon = BsImageAt(modDir, (pk != null ? pk.Type : spot[1]) + ".png");
                        int ic = S(16);
                        if (icon != null)
                        {
                            if (filled) g.DrawImage(icon, new Rectangle(tx, cy + (cellH - ic) / 2, ic, ic));
                            else
                                using (ImageAttributes ia = new ImageAttributes())
                                {
                                    ColorMatrix cm = new ColorMatrix();
                                    cm.Matrix33 = 0.35f;
                                    ia.SetColorMatrix(cm);
                                    g.DrawImage(icon, new Rectangle(tx, cy + (cellH - ic) / 2, ic, ic), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                                }
                        }
                        g.DrawString(filled ? "+3 " + spot[2] : "Empty " + BsSlotName(spot[1]) + " slot", cellFont, filled ? white : dim,
                            new RectangleF(tx + ic + S(6), cy, tw - ic - S(6), cellH), one);
                        cy += cellH;
                    }
                    y += rowH + rowGap;
                }
                one.Dispose();
            }
            return bmp;
        }

        static string EscapeNotes(string n)
        {
            return (n ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        }

        static string UnescapeNotes(string v)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    char c = v[++i];
                    sb.Append(c == 'n' ? '\n' : c);
                }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }

        static string NotesPreview(string n)
        {
            foreach (string line in (n ?? "").Split('\n')) if (line.Trim() != "") return line.Trim();
            return "No notes";
        }

        void SaveSavedBuildsSoon()
        {
            if (savedSaveTimer == null)
            {
                savedSaveTimer = new Timer();
                savedSaveTimer.Interval = 500;
                savedSaveTimer.Tick += delegate { savedSaveTimer.Stop(); SaveSavedBuilds(); };
            }
            savedSaveTimer.Stop();
            savedSaveTimer.Start();
        }

        static SavedBuild ToSaved(BsResult r, BsWeapon w, BsWeapon bond)
        {
            SavedBuild b = new SavedBuild();
            b.Weapon = w.Name;
            b.Bond = bond != null && w.Legendary ? bond.Name : "";
            b.Head = r.Parts[0].Pieces[0].Name;
            b.Torso = r.Parts[1].Pieces[0].Name;
            b.Arms = r.Parts[2].Pieces[0].Name;
            b.Legs = r.Parts[3].Pieces[0].Name;
            List<string> spots = new List<string>();
            foreach (string[] spot in r.CellSpots) spots.Add(spot[0] + "/" + spot[1] + "/" + spot[2]);
            b.Spots = string.Join(";", spots.ToArray());
            return b;
        }

        static BsResult FromSaved(SavedBuild b)
        {
            BsResult r = new BsResult();
            string[] names = new string[] { b.Head, b.Torso, b.Arms, b.Legs };
            for (int pc = 0; pc < 4; pc++)
            {
                BsArmour found = null;
                foreach (BsArmour a in BuildSearch.Armours)
                    if (a.Piece == BuildSearch.Pieces[pc] && a.Name == names[pc]) { found = a; break; }
                if (found == null)
                {
                    found = new BsArmour();
                    found.Piece = BuildSearch.Pieces[pc]; found.Name = names[pc]; found.Perk = "";
                    found.Slot = ""; found.Strength = ""; found.Weakness = ""; found.Icon = "";
                }
                BsGroup g = new BsGroup();
                g.Perk = found.Perk; g.Slot = found.Slot; g.Exotic = found.Exotic;
                g.Pieces.Add(found);
                r.Parts[pc] = g;
            }
            foreach (string part in b.Spots.Split(';'))
            {
                string[] f = part.Split('/');
                if (f.Length < 3) continue;
                r.CellSpots.Add(new string[] { f[0], f[1], f[2] });
                if (f[2] != "") r.CellPerks.Add(f[2]); else r.Free++;
            }
            return r;
        }

        SavedBuild FindSaved(string key)
        {
            foreach (SavedBuild b in SavedBuilds()) if (b.Key == key) return b;
            return null;
        }

        string NextBuildName()
        {
            for (int n = 1; ; n++)
            {
                string name = "Build " + n;
                bool used = false;
                foreach (SavedBuild b in SavedBuilds()) if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) { used = true; break; }
                if (!used) return name;
            }
        }

        void AddSavedPage()
        {
            string dataPath = System.IO.Path.Combine(ModPaths.Data(), "build-data.txt");
            if (!BuildSearch.Load(dataPath))
            {
                Label err = MakeLabel(BuildSearch.Error ?? "No build data found.", 10f, FontStyle.Regular, CText);
                err.Tag = "wrap";
                list.Controls.Add(err);
                return;
            }
            Label what = MakeLabel("Builds you've saved. Rename or duplicate them at the top right, add notes, and collapse them with the arrow. The pin shows a build on screen while you play (Clear Bars removes it). Press the star again to delete it.",
                9.5f, FontStyle.Regular, CSub);
            what.Tag = "wrap";
            what.Margin = new Padding(Px(2), 0, 0, Px(12));
            list.Controls.Add(what);
            FlowLayoutPanel importBar = MakeFlow();
            importBar.Margin = new Padding(0, 0, 0, Px(12));
            Button import = MakeButton("Import Build", Px(130));
            import.Click += delegate { ImportBuild(); };
            Label importNote = MakeLabel("Copy a build link (from the launcher or another player), then press Import Build.", 9.5f, FontStyle.Regular, CSub);
            importNote.Margin = new Padding(Px(10), Px(8), 0, 0);
            importBar.Controls.Add(import);
            importBar.Controls.Add(importNote);
            list.Controls.Add(importBar);
            FlowLayoutPanel holder = new FlowLayoutPanel();
            holder.Tag = "buildresults";
            holder.FlowDirection = FlowDirection.TopDown;
            holder.WrapContents = false;
            holder.AutoSize = true;
            holder.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            holder.BackColor = CBg;
            holder.Margin = new Padding(0);
            List<SavedBuild> saved = SavedBuilds();
            if (saved.Count == 0)
            {
                Label none = MakeLabel("No saved builds yet. Press the star at the top right of a build on Find Builds to save it here.", 10f, FontStyle.Regular, CText);
                none.Margin = new Padding(Px(2), 0, 0, Px(8));
                holder.Controls.Add(none);
            }
            foreach (SavedBuild b in saved)
            {
                BsWeapon w = BuildSearch.FindWeapon(b.Weapon);
                if (w == null)
                {
                    Label gone = MakeLabel(b.Name + ": the weapon \"" + b.Weapon + "\" isn't in the build data any more.", 9.5f, FontStyle.Regular, CSub);
                    gone.Margin = new Padding(Px(2), 0, 0, Px(8));
                    holder.Controls.Add(gone);
                    continue;
                }
                BsWeapon bond = b.Bond == "" ? null : BuildSearch.FindWeapon(b.Bond);
                holder.Controls.Add(MakeBuildCard(0, FromSaved(b), w, bond, b));
            }
            list.Controls.Add(holder);
        }

        Control MakeBuildCard(int number, BsResult r, BsWeapon w, BsWeapon bond)
        {
            return MakeBuildCard(number, r, w, bond, null);
        }

        class NoJumpPanel : Panel
        {
            protected override Point ScrollToControl(Control activeControl) { return AutoScrollPosition; }
        }

        bool OwnCounts(BsResult r, BsWeapon w, BsWeapon bond, out int have, out int all, out string tip)
        {
            have = 0; all = 0; tip = null;
            BsOwned.Refresh();
            if (!BsOwned.Known || r == null || w == null) return false;
            int itemsHave = 0, itemsAll = 0, cellsHave = 0, cellsAll = 0;
            List<string> items = new List<string>();
            items.Add(BuildSearch.WeaponId(w));
            if (bond != null && w.Legendary) items.Add(BuildSearch.WeaponId(bond));
            for (int pc = 0; pc < 4; pc++)
                if (r.Parts[pc] != null && r.Parts[pc].Pieces.Count > 0) items.Add(BuildSearch.ArmourId(r.Parts[pc].Pieces[0]));
            foreach (string id in items)
            {
                int st = BsOwned.State(id);
                if (st == 0) continue;
                itemsAll++;
                if (st > 0) itemsHave++;
            }
            Dictionary<string, int> used = new Dictionary<string, int>();
            foreach (string[] spot in r.CellSpots)
            {
                string id = BuildSearch.CellId(spot[2]);
                if (id == "") continue;
                int u;
                used.TryGetValue(id, out u);
                used[id] = ++u;
                cellsAll++;
                if (BsOwned.Count(id) >= u) cellsHave++;
            }
            if (itemsAll + cellsAll == 0) return false;
            have = itemsHave + cellsHave;
            all = itemsAll + cellsAll;
            tip = "You own " + itemsHave + " of " + itemsAll + " gear pieces" + (cellsAll > 0 ? " and " + cellsHave + " of " + cellsAll + " cells" : "") + ".";
            return true;
        }

        static int perkLineW;

        static int PerkLineW()
        {
            if (perkLineW > 0) return perkLineW;
            int widest = 0;
            using (Font f = new Font("Segoe UI", Ui.Pt(9f)))
                foreach (BsPerk p in BuildSearch.Perks) widest = Math.Max(widest, TextRenderer.MeasureText(p.Name, f).Width);
            perkLineW = Px(52) + Math.Max(Px(60), widest) + Px(4);
            return perkLineW;
        }

        static string CopyName(string name)
        {
            string n = name + " (Copy)";
            return n.Length > 60 ? n.Substring(0, 60) : n;
        }

        Control MakeBuildCard(int number, BsResult r, BsWeapon w, BsWeapon bond, SavedBuild saved)
        {
            BsOwned.Refresh();
            CardCanvas card = new CardCanvas(this);
            card.Title = saved != null ? saved.Name : "Build " + number;
            card.Info = r.Free == 1 ? "1 empty cell slot" : r.Free + " empty cell slots";
            int ownHave, ownAll;
            string ownTip;
            if (OwnCounts(r, w, bond, out ownHave, out ownAll, out ownTip))
            {
                card.RingHave = ownHave;
                card.RingAll = ownAll;
                card.RingTip = ownTip;
            }

            SavedBuild mine = saved != null ? saved : ToSaved(r, w, bond);
            if (saved == null)
            {
                mine.Lantern = bsLantern;
                mine.Omnicell = bsOmnicell;
                if (w.Type == bsType) { mine.Part1 = bsParts[0]; mine.Part2 = bsParts[1]; mine.Part3 = bsParts[2]; }
            }
            string key = mine.Key;
            CardBtn star = card.AddButton(CardCanvas.BStar, false);
            star.On = saved != null || FindSaved(key) != null;
            star.Tip = star.On ? "Delete build." : "Save build.";
            star.Click = delegate
            {
                if (saved != null)
                {
                    SavedBuilds().Remove(saved);
                    SaveSavedBuilds();
                    BeginInvoke((MethodInvoker)delegate { RefreshSection(); });
                    return;
                }
                SavedBuild had = FindSaved(key);
                if (had != null)
                {
                    SavedBuilds().Remove(had);
                    SaveSavedBuilds();
                    star.On = false;
                    star.Tip = "Save build.";
                }
                else
                {
                    mine.Name = NextBuildName();
                    SavedBuilds().Add(mine);
                    SaveSavedBuilds();
                    star.On = true;
                    star.Tip = "Delete build.";
                    SetStatus("Saved as \"" + mine.Name + "\" in Saved Builds", false, false, true);
                }
                card.Changed(star);
            };

            card.AddButton(CardCanvas.BPin, false).Click = delegate
            {
                SavedBuild curPin = ReadPinned(ModDir());
                if (curPin != null && curPin.Key == key) { SetPinned(null); return; }
                SavedBuild p = (saved != null ? saved : mine).Clone();
                p.Name = card.Title;
                SetPinned(p);
            };
            card.PinKey = key;
            SavedBuild pinNow = ReadPinned(ModDir());
            card.SetPin(pinNow != null && pinNow.Key == key);
            pinCards.Add(card);

            CardBtn share = card.AddButton(CardCanvas.BShare, false);
            share.Tip = "Share build.";
            share.Anchor = card.NewAnchor();
            share.Click = delegate { OpenShareMenu(share.Anchor, card, mine, card.Title); };

            if (saved != null)
            {
                CardBtn dup = card.AddButton(CardCanvas.BCopy, true);
                dup.Tip = "Duplicate build.";
                dup.Click = delegate
                {
                    SavedBuild copy = saved.Clone();
                    copy.Name = CopyName(saved.Name);
                    List<SavedBuild> all = SavedBuilds();
                    int at = all.IndexOf(saved);
                    all.Insert(at < 0 ? all.Count : at + 1, copy);
                    SaveSavedBuilds();
                    SetStatus("Duplicated as \"" + copy.Name + "\"", false);
                    BeginInvoke((MethodInvoker)delegate { RefreshSection(); });
                };

                TextBox rename = new TextBox();
                rename.BorderStyle = BorderStyle.FixedSingle;
                rename.BackColor = CBtn;
                rename.ForeColor = CText;
                rename.Font = Ui.UiFont(9.5f);
                rename.Width = Px(220);
                rename.MaxLength = 60;
                rename.Text = saved.Name;
                tips.SetToolTip(rename, "Rename this build");
                rename.TextChanged += delegate
                {
                    string n = rename.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                    saved.Name = n == "" ? "Unnamed Build" : n;
                    card.Title = saved.Name;
                    card.Relayout();
                    SavedBuild pinned = ReadPinned(ModDir());
                    if (pinned != null && pinned.Key == saved.Key && pinned.Name != saved.Name) { pinned.Name = saved.Name; SetPinned(pinned); }
                    SaveSavedBuildsSoon();
                };
                rename.KeyDown += delegate(object o, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ActiveControl = null; SaveSavedBuilds(); }
                };
                card.Rename = rename;
                card.Controls.Add(rename);

                TextBox notes = new TextBox();
                notes.Multiline = true;
                notes.AcceptsReturn = true;
                notes.ScrollBars = ScrollBars.Vertical;
                notes.BorderStyle = BorderStyle.FixedSingle;
                notes.BackColor = CBtn;
                notes.ForeColor = CText;
                notes.Font = Ui.UiFont(9.5f);
                notes.MaxLength = 2000;
                notes.Size = new Size(Px(300), Px(56));
                notes.Text = (saved.Notes ?? "").Replace("\n", "\r\n");
                notes.HandleCreated += delegate { try { SetWindowTheme(notes.Handle, "DarkMode_Explorer", null); } catch { } };
                tips.SetToolTip(notes, "Personal notes for this build");
                notes.TextChanged += delegate
                {
                    saved.Notes = notes.Text.Replace("\r\n", "\n").Replace("\r", "\n");
                    card.Preview = NotesPreview(saved.Notes);
                    SaveSavedBuildsSoon();
                };
                card.Notes = notes;
                card.Controls.Add(notes);
                card.Preview = NotesPreview(saved.Notes);

                card.AddButton(CardCanvas.BChev, false).Click = delegate { card.Toggle(); };
                card.Collapsed = saved.Collapsed;
                card.Toggle = delegate
                {
                    card.Collapsed = !card.Collapsed;
                    saved.Collapsed = card.Collapsed;
                    SaveSavedBuilds();
                    card.Relayout();
                    LayoutRows();
                    content.PerformLayout();
                };
            }

            BuildTile wt = new BuildTile();
            wt.Icon = BsTypeIconForPerkOrSlot;
            wt.Pic = BsImage(w.Icon);
            wt.Title = w.Name;
            wt.Affinity = w.Element ?? "";
            wt.ElemA = string.IsNullOrEmpty(w.Element) ? null : BsImage("elements/" + w.Element + ".png");
            if (wt.ElemA != null && Opposite(w.Element) != "")
            {
                wt.ArmourElems = true;
                wt.ElemB = wt.ElemA;
                wt.ElemA = BsImage("elements/" + Opposite(w.Element) + ".png");
            }
            List<string> wPerks = new List<string>();
            foreach (string p in w.Perks) wPerks.Add("+3 " + p);
            if (bond != null && w.Legendary) foreach (string p in bond.Perks) wPerks.Add("+3 " + p + " (bond)");
            wt.PerkText = wPerks.Count == 0 ? (w.Legendary ? "No bond picked" : "No perk") : string.Join(", ", wPerks.ToArray());
            foreach (string[] spot in r.CellSpots)
                if (spot[0] == "Weapon") { TileCell c = new TileCell(); c.SlotType = spot[1]; c.Perk = spot[2]; wt.Cells.Add(c); }
            wt.Own = BsOwned.State(BuildSearch.WeaponId(w));

            BuildTile lt = new BuildTile();
            lt.Icon = BsTypeIconForPerkOrSlot;
            foreach (string[] spot in r.CellSpots)
                if (spot[0] == "Lantern") { TileCell c = new TileCell(); c.SlotType = spot[1]; c.Perk = spot[2]; lt.Cells.Add(c); }
            FillLanternTile(lt, mine.Lantern);
            lt.Anchor = card.NewAnchor();
            lt.Open = delegate(Control a)
            {
                OpenGearDrop(a, LanternChoices(), "Any Lantern", mine.Lantern, delegate(string id)
                {
                    mine.Lantern = id;
                    FillLanternTile(lt, id);
                    card.Invalidate(lt.Bounds);
                    LoadoutChanged(mine);
                });
            };

            card.Grid.Add(wt);
            for (int pc = 0; pc < 4; pc++) card.Grid.Add(MakeArmourTile(r.Parts[pc], pc, r));
            card.Grid.Add(lt);

            card.Extras.Add(MakeGearTile(card, "Omnicell", BuildSearch.Omnicells, "No Omnicell", mine, -1));
            for (int pi = 0; pi < PartCount(w.Type); pi++)
            {
                List<BsGear> partChoices = PartsFor(w.Type, pi, w.Name);
                SetPart(mine, pi, FixPart(partChoices, GetPart(mine, pi)));
                card.Extras.Add(MakeGearTile(card, PartLabel(pi, w.Type), partChoices, "No " + PartLabel(pi, w.Type), mine, pi));
            }

            Dictionary<string, int> totals = new Dictionary<string, int>();
            List<string> order = saved != null ? new List<string>() : new List<string>(BuildReq().Keys);
            Action<string> add = delegate(string perk)
            {
                if (string.IsNullOrEmpty(perk)) return;
                int v;
                totals.TryGetValue(perk, out v);
                totals[perk] = v + 3;
                if (!order.Contains(perk)) order.Add(perk);
            };
            foreach (string p in w.Perks) add(p);
            if (bond != null && w.Legendary) foreach (string p in bond.Perks) add(p);
            for (int pc = 0; pc < 4; pc++) add(r.Parts[pc].Pieces[0].Perk);
            foreach (string p in r.CellPerks) add(p);
            List<string> sorted = new List<string>();
            foreach (string p in order) if (totals.ContainsKey(p) && totals[p] >= 6) sorted.Add(p);
            foreach (string p in order) if (totals.ContainsKey(p) && totals[p] < 6) sorted.Add(p);
            foreach (string p in sorted)
            {
                int v = totals[p];
                CardPerk line = new CardPerk();
                line.Perk = p;
                line.Value = Math.Min(6, v);
                line.Over = Math.Max(0, v - 6);
                line.Icon = BsTypeIconForPerkOrSlot(p);
                BsPerk info = BuildSearch.FindPerk(p);
                if (info != null)
                    line.Tip = p + " +" + line.Value + "\n\n" + (bsAllRanks ? BsRanksText(info, 60, line.Value) : BsWrap(info.RankText(line.Value >= 6 ? 6 : 3), 60));
                card.Perks.Add(line);
            }
            return card;
        }
    }
}
