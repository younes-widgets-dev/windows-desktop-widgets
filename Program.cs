using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopWidgets
{
    static class Crash
    {
        static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopWidgets");
        public static void Log(object ex)
        {
            try { Directory.CreateDirectory(LogDir); string f = Path.Combine(LogDir, "error.log"); try { if (File.Exists(f) && new FileInfo(f).Length > 500000) File.Delete(f); } catch { } File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n"); } catch { }
        }
        public static void Install()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { Log(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log(e.ExceptionObject); };
            }
            catch { }
        }
    }
    // ================= Settings =================
    static class Settings
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopWidgets");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");
        static Dictionary<string, string> d = new Dictionary<string, string>();
        public static string NotesPath(string id) { return Path.Combine(Dir, id == "notes" ? "notes.txt" : "notes-" + id + ".txt"); }

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, string> kv in d) l.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { }
        }
        public static string Get(string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
        public static int GetInt(string k, int def) { int v; return int.TryParse(Get(k, ""), out v) ? v : def; }
        public static bool GetBool(string k, bool def) { string v = Get(k, ""); return v == "" ? def : v == "1"; }
        public static void Set(string k, string v) { d[k] = v; }
        public static void Set(string k, int v) { d[k] = v.ToString(CultureInfo.InvariantCulture); }
        public static void Set(string k, bool v) { d[k] = v ? "1" : "0"; }
        public static void ClearPositions()
        {
            List<string> del = new List<string>();
            foreach (string k in d.Keys) if (k.EndsWith(".x") || k.EndsWith(".y")) del.Add(k);
            foreach (string k in del) d.Remove(k);
        }
        public static void RemovePrefix(string prefix)
        {
            List<string> del = new List<string>();
            foreach (string k in d.Keys) if (k.StartsWith(prefix)) del.Add(k);
            foreach (string k in del) d.Remove(k);
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMSTATUS
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        public const int WS_EX_TOOLWINDOW = 0x80;
        [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Fmt; }
        [StructLayout(LayoutKind.Sequential)] struct BB { public int Flags; public int Enable; public IntPtr Rgn; public int Trans; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize, IntPtr hdcSrc, ref PT pptSrc, int crKey, ref BLEND pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr h, ref BB bb);
        public static void Present(Form f, Bitmap bmp, byte alpha)
        {
            IntPtr scr = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(scr), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0)); old = SelectObject(mem, hb);
                PT dst = new PT(); dst.x = f.Left; dst.y = f.Top; SZ size = new SZ(); size.cx = bmp.Width; size.cy = bmp.Height; PT src = new PT();
                BLEND bl = new BLEND(); bl.Op = 0; bl.Flags = 0; bl.Alpha = alpha; bl.Fmt = 1;
                UpdateLayeredWindow(f.Handle, scr, ref dst, ref size, mem, ref src, 0, ref bl, 2);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hb != IntPtr.Zero) DeleteObject(hb);
                DeleteDC(mem); ReleaseDC(IntPtr.Zero, scr);
            }
        }
        public static void Blur(Form f, bool on, GraphicsPath shape)
        {
            try
            {
                BB bb = new BB(); IntPtr hr = IntPtr.Zero; Region rg = null;
                if (on) { rg = new Region(shape); using (Graphics g = Graphics.FromHwnd(f.Handle)) hr = rg.GetHrgn(g); bb.Flags = 3; bb.Enable = 1; bb.Rgn = hr; }
                else { bb.Flags = 1; bb.Enable = 0; }
                DwmEnableBlurBehindWindow(f.Handle, ref bb);
                if (hr != IntPtr.Zero) DeleteObject(hr);
                if (rg != null) rg.Dispose();
            }
            catch { }
        }
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);

        // state: 0 off, 2 transparent gradient, 3 blur behind, 4 acrylic. color = ARGB
        public static void SetAccent(IntPtr hwnd, int state, Color c)
        {
            try
            {
                AccentPolicy ap = new AccentPolicy();
                ap.AccentState = state;
                ap.AccentFlags = 2;
                ap.GradientColor = (c.A << 24) | (c.B << 16) | (c.G << 8) | c.R;
                int size = Marshal.SizeOf(ap);
                IntPtr p = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(ap, p, false);
                WinCompAttrData data = new WinCompAttrData();
                data.Attribute = 19; data.Data = p; data.SizeOfData = size;
                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(p);
            }
            catch { }
        }
    }

    // ================= Style =================
    class Sty
    {
        public int Shape = 0, Radius = 14, Mode = 0, BgAlpha = 150, BorderW = 0, Pad = 0, FontPct = 100, W, H, Interval;
        public Color Bg = Color.FromArgb(24, 26, 32), Fg = Color.FromArgb(240, 240, 245), Dim = Color.FromArgb(150, 155, 170), Accent = Color.FromArgb(80, 160, 255), Border = Color.FromArgb(90, 255, 255, 255);
        public string Font = "Segoe UI";
        public double Opacity = 0.95;
        public bool Top, Lock, Bar = true, ShowTitle = true, Bold = false;
        public int BarPos = 0, BarW = 3, Grad = 0;
        public Color Bg2 = Color.FromArgb(48, 52, 66);
        public Dictionary<string, int> Ov = new Dictionary<string, int>();
        public static readonly string[] OvKeys = { "value", "label", "title", "barfill", "barbg", "warn", "graph", "ring", "accent2", "hourhand", "minhand", "sechand", "ticks", "numerals", "face", "dot", "cpu", "ram", "gpu", "cellbg", "divider" };

        public static readonly string[] ShapeNames = { "Rounded", "Rectangle", "Pill", "Ellipse / Circle", "Hexagon", "Octagon", "Diamond", "Cut corners", "Leaf", "Tab (top)", "Squircle", "Parallelogram", "Pentagon", "Trapezoid", "Pill (left)", "Tab (bottom)", "Ticket", "Shield", "Star", "Triangle", "Pill (right)", "Diagonal corners" };
        public static readonly string[] ModeNames = { "Solid", "Transparent tint", "Blur (glass)", "Acrylic (frosted glass)" };

        static int C(Color c) { return c.ToArgb(); }
        public void Load(string id, int dw, int dh, int di)
        {
            Shape = Settings.GetInt(id + ".shape", Shape); Radius = Settings.GetInt(id + ".radius", Radius);
            Mode = Settings.GetInt(id + ".mode", Mode); BgAlpha = Settings.GetInt(id + ".bgalpha", BgAlpha);
            BorderW = Settings.GetInt(id + ".borderw", BorderW); Pad = Settings.GetInt(id + ".pad", Pad);
            FontPct = Settings.GetInt(id + ".fontpct", FontPct);
            W = Settings.GetInt(id + ".w", dw); H = Settings.GetInt(id + ".h", dh); Interval = Settings.GetInt(id + ".interval", di);
            Bg = Color.FromArgb(Settings.GetInt(id + ".bg", C(Bg))); Fg = Color.FromArgb(Settings.GetInt(id + ".fg", C(Fg)));
            Dim = Color.FromArgb(Settings.GetInt(id + ".dim", C(Dim))); Accent = Color.FromArgb(Settings.GetInt(id + ".accent", C(Accent)));
            Border = Color.FromArgb(Settings.GetInt(id + ".border", C(Border)));
            Font = Settings.Get(id + ".font", Font);
            Opacity = Settings.GetInt(id + ".opacity100", 95) / 100.0;
            Top = Settings.GetBool(id + ".top", false); Lock = Settings.GetBool(id + ".locked", false); Bar = Settings.GetBool(id + ".bar", Bar);
            ShowTitle = Settings.GetBool(id + ".title", true); Bold = Settings.GetBool(id + ".bold", false);
            BarPos = Settings.GetInt(id + ".barpos", 0); BarW = Settings.GetInt(id + ".barw", 3); Grad = Settings.GetInt(id + ".grad", 0); Bg2 = Color.FromArgb(Settings.GetInt(id + ".bg2", C(Bg2)));
            Ov.Clear(); foreach (string k in OvKeys) { int v = Settings.GetInt(id + ".c." + k, 0); if (v != 0) Ov[k] = v; }
        }
        public void Save(string id)
        {
            Settings.Set(id + ".shape", Shape); Settings.Set(id + ".radius", Radius); Settings.Set(id + ".mode", Mode); Settings.Set(id + ".bgalpha", BgAlpha);
            Settings.Set(id + ".borderw", BorderW); Settings.Set(id + ".pad", Pad); Settings.Set(id + ".fontpct", FontPct);
            Settings.Set(id + ".w", W); Settings.Set(id + ".h", H); Settings.Set(id + ".interval", Interval);
            Settings.Set(id + ".bg", C(Bg)); Settings.Set(id + ".fg", C(Fg)); Settings.Set(id + ".dim", C(Dim)); Settings.Set(id + ".accent", C(Accent)); Settings.Set(id + ".border", C(Border));
            Settings.Set(id + ".font", Font); Settings.Set(id + ".opacity100", (int)Math.Round(Opacity * 100));
            Settings.Set(id + ".top", Top); Settings.Set(id + ".locked", Lock); Settings.Set(id + ".bar", Bar);
            Settings.Set(id + ".title", ShowTitle); Settings.Set(id + ".bold", Bold); Settings.Set(id + ".barpos", BarPos); Settings.Set(id + ".barw", BarW); Settings.Set(id + ".grad", Grad); Settings.Set(id + ".bg2", C(Bg2));
            foreach (string k in OvKeys) { int v; Settings.Set(id + ".c." + k, Ov.TryGetValue(k, out v) ? v : 0); }
            Settings.Save();
        }
        public void CopyLookFrom(Sty o)
        {
            Shape = o.Shape; Radius = o.Radius; Mode = o.Mode; BgAlpha = o.BgAlpha; BorderW = o.BorderW; Pad = o.Pad; FontPct = o.FontPct;
            Bg = o.Bg; Fg = o.Fg; Dim = o.Dim; Accent = o.Accent; Border = o.Border; Font = o.Font; Opacity = o.Opacity; Bar = o.Bar; ShowTitle = o.ShowTitle; Bold = o.Bold; BarPos = o.BarPos; BarW = o.BarW; Grad = o.Grad; Bg2 = o.Bg2; Ov = new Dictionary<string, int>(o.Ov);
        }

        public static readonly string[] ThemeNames = { "Dark", "Light", "Acrylic dark", "Acrylic light", "Blur glass", "Clear (text only)", "Neon", "Terminal", "Sunset", "Pastel", "Midnight", "Paper", "Dracula", "Nord", "Solarized dark", "Solarized light", "Gruvbox", "Monokai", "Cyberpunk", "Ocean gradient", "Forest gradient", "Rose gold", "Matrix", "Mono black", "Mono white", "High contrast", "Aqua glass", "Crimson glass", "Retro amber", "Violet gradient", "Sunrise gradient", "Slate" };
        public void ApplyTheme(string name)
        {
            Ov.Clear(); Mode = 0; BgAlpha = 160; Shape = 0; Radius = 14; BorderW = 0; Bar = true; Opacity = 0.95; Pad = 0; Grad = 0; BarPos = 0; BarW = 3; ShowTitle = true; Bold = false; Bg2 = Color.FromArgb(48, 52, 66);
            Border = Color.FromArgb(90, 255, 255, 255); Font = "Segoe UI";
            switch (name)
            {
                case "Light": Bg = Color.FromArgb(245, 245, 248); Fg = Color.FromArgb(30, 32, 40); Dim = Color.FromArgb(110, 115, 130); Accent = Color.FromArgb(0, 120, 215); BorderW = 1; Border = Color.FromArgb(220, 222, 228); break;
                case "Acrylic dark": Mode = 3; Bg = Color.FromArgb(20, 20, 32); BgAlpha = 150; Fg = Color.White; Dim = Color.FromArgb(190, 195, 210); Accent = Color.FromArgb(120, 190, 255); Bar = false; BorderW = 1; Radius = 20; break;
                case "Acrylic light": Mode = 3; Bg = Color.FromArgb(255, 255, 255); BgAlpha = 140; Fg = Color.FromArgb(20, 22, 30); Dim = Color.FromArgb(80, 85, 100); Accent = Color.FromArgb(0, 110, 210); Bar = false; BorderW = 1; Border = Color.FromArgb(120, 255, 255, 255); Radius = 20; break;
                case "Blur glass": Mode = 2; Bg = Color.Black; Fg = Color.White; Dim = Color.FromArgb(210, 215, 225); Accent = Color.FromArgb(255, 255, 255); Bar = false; BorderW = 1; Border = Color.FromArgb(110, 255, 255, 255); Radius = 22; break;
                case "Clear (text only)": Mode = 1; BgAlpha = 0; Bg = Color.Black; Fg = Color.White; Dim = Color.FromArgb(220, 220, 230); Accent = Color.White; Bar = false; Radius = 0; break;
                case "Neon": Bg = Color.FromArgb(10, 10, 22); Fg = Color.FromArgb(0, 255, 210); Dim = Color.FromArgb(0, 150, 130); Accent = Color.FromArgb(255, 0, 200); Border = Color.FromArgb(255, 0, 200); BorderW = 2; Radius = 6; Bar = false; break;
                case "Terminal": Bg = Color.FromArgb(0, 12, 0); Fg = Color.FromArgb(70, 255, 110); Dim = Color.FromArgb(30, 160, 60); Accent = Color.FromArgb(70, 255, 110); Border = Color.FromArgb(30, 160, 60); BorderW = 1; Shape = 1; Radius = 0; Bar = false; Font = "Consolas"; break;
                case "Sunset": Bg = Color.FromArgb(42, 20, 52); Fg = Color.FromArgb(255, 232, 215); Dim = Color.FromArgb(205, 160, 170); Accent = Color.FromArgb(255, 120, 80); Radius = 22; break;
                case "Pastel": Bg = Color.FromArgb(250, 235, 241); Fg = Color.FromArgb(90, 60, 80); Dim = Color.FromArgb(160, 125, 145); Accent = Color.FromArgb(240, 130, 170); Radius = 26; break;
                case "Midnight": Bg = Color.FromArgb(10, 14, 30); Fg = Color.FromArgb(220, 230, 255); Dim = Color.FromArgb(120, 135, 175); Accent = Color.FromArgb(100, 140, 255); Radius = 12; break;
                case "Paper": Bg = Color.FromArgb(250, 246, 235); Fg = Color.FromArgb(60, 50, 40); Dim = Color.FromArgb(140, 125, 105); Accent = Color.FromArgb(190, 90, 50); Radius = 4; BorderW = 1; Border = Color.FromArgb(215, 205, 185); Font = "Georgia"; break;
                case "Dracula": Bg = Color.FromArgb(40, 42, 54); Fg = Color.FromArgb(248, 248, 242); Dim = Color.FromArgb(139, 148, 190); Accent = Color.FromArgb(189, 147, 249); Radius = 12; break;
                case "Nord": Bg = Color.FromArgb(46, 52, 64); Fg = Color.FromArgb(236, 239, 244); Dim = Color.FromArgb(129, 161, 193); Accent = Color.FromArgb(136, 192, 208); Radius = 10; break;
                case "Solarized dark": Bg = Color.FromArgb(0, 43, 54); Fg = Color.FromArgb(238, 232, 213); Dim = Color.FromArgb(131, 148, 150); Accent = Color.FromArgb(181, 137, 0); Radius = 8; break;
                case "Solarized light": Bg = Color.FromArgb(253, 246, 227); Fg = Color.FromArgb(88, 110, 117); Dim = Color.FromArgb(147, 161, 161); Accent = Color.FromArgb(38, 139, 210); Radius = 8; break;
                case "Gruvbox": Bg = Color.FromArgb(40, 40, 40); Fg = Color.FromArgb(235, 219, 178); Dim = Color.FromArgb(168, 153, 132); Accent = Color.FromArgb(250, 189, 47); Radius = 6; break;
                case "Monokai": Bg = Color.FromArgb(39, 40, 34); Fg = Color.FromArgb(248, 248, 242); Dim = Color.FromArgb(166, 172, 140); Accent = Color.FromArgb(166, 226, 46); Radius = 6; break;
                case "Cyberpunk": Bg = Color.FromArgb(20, 6, 40); Bg2 = Color.FromArgb(60, 10, 70); Grad = 3; Fg = Color.FromArgb(255, 238, 0); Dim = Color.FromArgb(0, 230, 255); Accent = Color.FromArgb(255, 0, 140); Border = Color.FromArgb(0, 230, 255); BorderW = 2; Shape = 7; Radius = 14; Bar = false; break;
                case "Ocean gradient": Bg = Color.FromArgb(6, 40, 80); Bg2 = Color.FromArgb(0, 130, 160); Grad = 1; Fg = Color.White; Dim = Color.FromArgb(180, 225, 240); Accent = Color.FromArgb(120, 240, 230); Radius = 20; Bar = false; break;
                case "Forest gradient": Bg = Color.FromArgb(12, 40, 28); Bg2 = Color.FromArgb(40, 100, 60); Grad = 1; Fg = Color.FromArgb(230, 250, 235); Dim = Color.FromArgb(160, 205, 175); Accent = Color.FromArgb(160, 240, 120); Radius = 20; Bar = false; break;
                case "Rose gold": Bg = Color.FromArgb(60, 38, 42); Bg2 = Color.FromArgb(120, 80, 80); Grad = 3; Fg = Color.FromArgb(255, 235, 225); Dim = Color.FromArgb(225, 185, 175); Accent = Color.FromArgb(255, 190, 170); Radius = 24; Bar = false; break;
                case "Matrix": Bg = Color.Black; Fg = Color.FromArgb(0, 255, 70); Dim = Color.FromArgb(0, 140, 40); Accent = Color.FromArgb(0, 255, 70); Shape = 1; Radius = 0; Bar = false; Font = "Consolas"; BorderW = 1; Border = Color.FromArgb(0, 140, 40); break;
                case "Mono black": Bg = Color.Black; Fg = Color.White; Dim = Color.FromArgb(150, 150, 150); Accent = Color.White; Radius = 0; Shape = 1; Bar = false; BorderW = 1; Border = Color.FromArgb(70, 70, 70); break;
                case "Mono white": Bg = Color.White; Fg = Color.Black; Dim = Color.FromArgb(110, 110, 110); Accent = Color.Black; Radius = 0; Shape = 1; Bar = false; BorderW = 1; Border = Color.FromArgb(200, 200, 200); break;
                case "High contrast": Bg = Color.Black; Fg = Color.Yellow; Dim = Color.White; Accent = Color.Cyan; BorderW = 3; Border = Color.Yellow; Radius = 4; Bar = false; Bold = true; break;
                case "Aqua glass": Mode = 3; Bg = Color.FromArgb(0, 80, 110); BgAlpha = 120; Fg = Color.White; Dim = Color.FromArgb(200, 240, 250); Accent = Color.FromArgb(120, 255, 240); Bar = false; BorderW = 1; Border = Color.FromArgb(150, 160, 255, 250); Radius = 24; break;
                case "Crimson glass": Mode = 3; Bg = Color.FromArgb(90, 0, 20); BgAlpha = 130; Fg = Color.White; Dim = Color.FromArgb(255, 200, 205); Accent = Color.FromArgb(255, 110, 120); Bar = false; BorderW = 1; Border = Color.FromArgb(150, 255, 140, 150); Radius = 24; break;
                case "Retro amber": Bg = Color.FromArgb(20, 10, 0); Fg = Color.FromArgb(255, 176, 0); Dim = Color.FromArgb(160, 100, 0); Accent = Color.FromArgb(255, 176, 0); Shape = 1; Radius = 0; Bar = false; Font = "Consolas"; BorderW = 2; Border = Color.FromArgb(160, 100, 0); break;
                case "Violet gradient": Bg = Color.FromArgb(40, 16, 90); Bg2 = Color.FromArgb(150, 60, 180); Grad = 3; Fg = Color.White; Dim = Color.FromArgb(220, 190, 240); Accent = Color.FromArgb(255, 170, 240); Radius = 22; Bar = false; break;
                case "Sunrise gradient": Bg = Color.FromArgb(200, 70, 60); Bg2 = Color.FromArgb(255, 190, 90); Grad = 3; Fg = Color.White; Dim = Color.FromArgb(255, 235, 215); Accent = Color.FromArgb(80, 20, 40); Radius = 22; Bar = false; break;
                case "Slate": Bg = Color.FromArgb(55, 63, 78); Fg = Color.FromArgb(235, 240, 248); Dim = Color.FromArgb(150, 162, 182); Accent = Color.FromArgb(110, 200, 170); Radius = 16; break;
                default: Bg = Color.FromArgb(24, 26, 32); Fg = Color.FromArgb(240, 240, 245); Dim = Color.FromArgb(150, 155, 170); Accent = Color.FromArgb(80, 160, 255); break;
            }
        }
    }

    // ================= Shapes =================
    static class Shapes
    {
        static GraphicsPath RR(RectangleF r, float tl, float tr, float br, float bl)
        {
            GraphicsPath p = new GraphicsPath();
            p.StartFigure();
            if (tl > 0) p.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (tr > 0) p.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if (br > 0) p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl > 0) p.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }
        public static GraphicsPath Make(int shape, int radius, RectangleF r)
        {
            float m = Math.Min(r.Width, r.Height);
            float rad = Math.Max(0, Math.Min(radius, m / 2f));
            GraphicsPath p;
            switch (shape)
            {
                case 1: return RR(r, 0, 0, 0, 0);
                case 2: rad = m / 2f; return RR(r, rad, rad, rad, rad);
                case 3: p = new GraphicsPath(); p.AddEllipse(r); return p;
                case 4:
                    p = new GraphicsPath();
                    p.AddPolygon(new PointF[] { new PointF(r.X + r.Width * 0.25f, r.Y), new PointF(r.X + r.Width * 0.75f, r.Y), new PointF(r.Right, r.Y + r.Height / 2), new PointF(r.X + r.Width * 0.75f, r.Bottom), new PointF(r.X + r.Width * 0.25f, r.Bottom), new PointF(r.X, r.Y + r.Height / 2) });
                    return p;
                case 5:
                    { float c = m * 0.29f; p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X + c, r.Y), new PointF(r.Right - c, r.Y), new PointF(r.Right, r.Y + c), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.X + c, r.Bottom), new PointF(r.X, r.Bottom - c), new PointF(r.X, r.Y + c) });
                      return p; }
                case 6:
                    p = new GraphicsPath();
                    p.AddPolygon(new PointF[] { new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right, r.Y + r.Height / 2), new PointF(r.X + r.Width / 2, r.Bottom), new PointF(r.X, r.Y + r.Height / 2) });
                    return p;
                case 7:
                    { float c = rad; p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X + c, r.Y), new PointF(r.Right - c, r.Y), new PointF(r.Right, r.Y + c), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.X + c, r.Bottom), new PointF(r.X, r.Bottom - c), new PointF(r.X, r.Y + c) });
                      return p; }
                case 8: { float big = Math.Min(m / 2f, Math.Max(rad * 2, 10)); return RR(r, big, 4, big, 4); }
                case 9: return RR(r, rad, rad, 0, 0);
                case 10: rad = m * 0.38f; return RR(r, rad, rad, rad, rad);
                case 11:
                    { float o = Math.Min(r.Width * 0.3f, r.Height * 0.3f); p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X + o, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right - o, r.Bottom), new PointF(r.X, r.Bottom) });
                      return p; }
                case 12: p = new GraphicsPath(); p.AddPolygon(new PointF[] { new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right, r.Y + r.Height * 0.38f), new PointF(r.X + r.Width * 0.82f, r.Bottom), new PointF(r.X + r.Width * 0.18f, r.Bottom), new PointF(r.X, r.Y + r.Height * 0.38f) }); return p;
                case 13: p = new GraphicsPath(); p.AddPolygon(new PointF[] { new PointF(r.X + r.Width * 0.15f, r.Y), new PointF(r.Right - r.Width * 0.15f, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) }); return p;
                case 14: return RR(r, m / 2f, 0, 0, m / 2f);
                case 15: return RR(r, 0, 0, rad, rad);
                case 16:
                    { float n = Math.Min(r.Width, r.Height) * 0.12f; float my = r.Y + r.Height / 2; p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, my - n), new PointF(r.Right - n, my), new PointF(r.Right, my + n), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom), new PointF(r.X, my + n), new PointF(r.X + n, my), new PointF(r.X, my - n) });
                      return p; }
                case 17:
                    p = new GraphicsPath(); p.StartFigure();
                    p.AddLine(r.X, r.Y, r.Right, r.Y); p.AddLine(r.Right, r.Y, r.Right, r.Y + r.Height * 0.55f);
                    p.AddBezier(r.Right, r.Y + r.Height * 0.55f, r.Right, r.Y + r.Height * 0.85f, r.X + r.Width * 0.7f, r.Y + r.Height * 0.95f, r.X + r.Width / 2, r.Bottom);
                    p.AddBezier(r.X + r.Width / 2, r.Bottom, r.X + r.Width * 0.3f, r.Y + r.Height * 0.95f, r.X, r.Y + r.Height * 0.85f, r.X, r.Y + r.Height * 0.55f);
                    p.CloseFigure(); return p;
                case 18:
                    { PointF[] pts = new PointF[10]; float cx = r.X + r.Width / 2, cy = r.Y + r.Height * 0.53f;
                      for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float k = i % 2 == 0 ? 1f : 0.42f; pts[i] = new PointF(cx + (float)Math.Cos(a) * r.Width / 2 * k, cy + (float)Math.Sin(a) * r.Height / 2 * 1.04f * k); }
                      p = new GraphicsPath(); p.AddPolygon(pts); return p; }
                case 19: p = new GraphicsPath(); p.AddPolygon(new PointF[] { new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) }); return p;
                case 20: return RR(r, 0, m / 2f, m / 2f, 0);
                case 21: { float big = Math.Max(rad, 6); return RR(r, big, 0, big, 0); }
                default: return RR(r, rad, rad, rad, rad);
            }
        }
    }

    // ================= Base widget =================
    abstract class Widget : Form
    {
        public string Id, Type, Title;
        public Sty St = new Sty();
        protected int DefW, DefH, DefInterval;
        protected double Fs { get { return Math.Max(0.4, St.FontPct / 100.0); } }
        protected float LW, LH;   // content size in logical units (after padding)
        protected Timer Tick;
        bool dragging, moved; Point dragStart; Point formStart;
        Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        protected Brush FgB, DimB, AccB;
        StringFormat centerFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        protected Widget(string id, string type, string title, int w, int h, int intervalMs)
        {
            Id = id; Type = type; Title = title; DefW = w; DefH = h; DefInterval = intervalMs;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            St.Load(id, w, h, intervalMs);
            int idx = Program.Widgets.Count;
            Location = new Point(Settings.GetInt(id + ".x", 40 + (idx % 4) * 260), Settings.GetInt(id + ".y", 40 + (idx / 4) * 200));
            Tick = new Timer();
            Tick.Tick += delegate { if (Visible) { OnTick(); Invalidate(); } };
            Tick.Start();
        }

        public void Init() { ApplyStyle(); BuildMenu(); }
        public Color Ov(string k, Color d) { int v; return St.Ov.TryGetValue(k, out v) && v != 0 ? Color.FromArgb(v) : d; }
        public Control OvPicker(string key, Color def)
        {
            FlowLayoutPanel fp = new FlowLayoutPanel(); fp.AutoSize = true; fp.WrapContents = false; fp.Margin = new Padding(0);
            Button b = new Button(); b.Width = 110; b.Height = 24; b.FlatStyle = FlatStyle.Flat; b.Text = St.Ov.ContainsKey(key) ? "" : "default";
            b.BackColor = Ov(key, def);
            b.Click += delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = b.BackColor; cd.FullOpen = true;
                    if (cd.ShowDialog() == DialogResult.OK) { St.Ov[key] = Color.FromArgb(255, cd.Color).ToArgb(); b.BackColor = cd.Color; b.Text = ""; Restyle(); }
                }
            };
            Button r = new Button(); r.Text = "Reset"; r.Width = 54; r.Height = 24;
            r.Click += delegate { St.Ov.Remove(key); b.BackColor = def; b.Text = "default"; Restyle(); };
            fp.Controls.Add(b); fp.Controls.Add(r);
            return fp;
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | 0x80000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public virtual void ApplyStyle()
        {
            if (FgB != null) { FgB.Dispose(); DimB.Dispose(); AccB.Dispose(); }
            FgB = new SolidBrush(Ov("value", St.Fg)); DimB = new SolidBrush(Ov("label", St.Dim)); AccB = new SolidBrush(Ov("accent2", St.Accent));
            foreach (Font f in fonts.Values) f.Dispose();
            fonts.Clear();
            ClientSize = new Size(Math.Max(60, St.W), Math.Max(40, St.H));
            bool accent = St.Mode > 0;
            BackColor = accent ? Color.Black : St.Bg;
            TopMost = St.Top;
            if (IsHandleCreated) UpdateBlur();
            Tick.Interval = Math.Max(50, St.Interval);
            LW = (float)((Width - 2 * St.Pad) / Fs); LH = (float)((Height - 2 * St.Pad) / Fs);
            Invalidate();
        }
        public void UpdateBlur()
        {
            if (!IsHandleCreated) return;
            using (GraphicsPath p = Shapes.Make(St.Shape, St.Radius, new RectangleF(0, 0, Width, Height))) Native.Blur(this, St.Mode >= 2, p);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateBlur(); }

        public void Restyle() { St.Save(Id); ApplyStyle(); OnRestyled(); }
        protected virtual void OnRestyled() { }
        protected virtual void OnTick() { }
        protected abstract void Draw(Graphics g);
        protected virtual void OnWidgetClick(PointF p) { }
        public virtual void BuildExtra(Action<string, Control> add) { }

        // ---- fonts / drawing helpers (logical units) ----
        protected Font F(float size, bool bold)
        {
            string key = size.ToString("0.#", CultureInfo.InvariantCulture) + (bold ? "b" : "r");
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                try { f = new Font(St.Font, size, (bold || St.Bold) ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
                catch { f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
                fonts[key] = f;
            }
            return f;
        }
        protected void Txt(Graphics g, string s, float size, bool bold, Brush b, float x, float y) { g.DrawString(s, F(size, bold), b, x, y); }
        // Draw text centered in rect, shrinking font until it fits
        protected void Fit(Graphics g, string s, float maxSize, bool bold, Brush b, RectangleF r)
        {
            float sz = maxSize;
            while (sz > 6)
            {
                SizeF m = g.MeasureString(s, F(sz, bold));
                if (m.Width <= r.Width && m.Height <= r.Height) break;
                sz -= 2;
            }
            g.DrawString(s, F(sz, bold), b, r, centerFmt);
        }
        protected void Ttl(Graphics g, string s, float x, float y) { if (St.ShowTitle) using (SolidBrush tb = new SolidBrush(Ov("title", Ov("label", St.Dim)))) g.DrawString(s, F(13, true), tb, x, y); }
        protected static string Gb(double bytes) { return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture); }
        protected void Spark(Graphics g, double[] h, float x, float y, float w, float ht, double min, double maxFloor, Color c) { SparkC(g, h, x, y, w, ht, min, maxFloor, Ov("graph", c)); }
        protected void SparkC(Graphics g, double[] h, float x, float y, float w, float ht, double min, double maxFloor, Color c)
        {
            double max = maxFloor; foreach (double v in h) if (v > max) max = v;
            if (max <= min || w < 4 || ht < 4) return;
            PointF[] pts = new PointF[h.Length];
            for (int i = 0; i < h.Length; i++) pts[i] = new PointF(x + i * (w / (h.Length - 1)), (float)(y + ht - ((h[i] - min) / (max - min)) * ht));
            using (Pen p = new Pen(c, 1.5f)) g.DrawLines(p, pts);
        }
        protected void Bar(Graphics g, float x, float y, float w, float h, double frac) { BarC(g, x, y, w, h, frac, Ov("barfill", St.Accent)); }
        protected void BarC(Graphics g, float x, float y, float w, float h, double frac, Color fill)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(70, Ov("barbg", St.Dim)))) g.FillRectangle(bg, x, y, w, h);
            Color c = frac > 0.9 ? Ov("warn", Color.FromArgb(255, 90, 90)) : fill;
            using (SolidBrush fg = new SolidBrush(c)) g.FillRectangle(fg, x, y, (float)(w * frac), h);
        }
        protected void Arc(Graphics g, RectangleF r, double frac, float width, Color c) { ArcC(g, r, frac, width, Ov("ring", c)); }
        protected void ArcC(Graphics g, RectangleF r, double frac, float width, Color c)
        {
            using (Pen bg = new Pen(Color.FromArgb(60, Ov("barbg", St.Dim)), width)) g.DrawArc(bg, r, -90, 360);
            if (frac > 0.003) using (Pen p = new Pen(c, width)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, r, -90, (float)(360 * Math.Min(1, frac))); }
        }

        protected override void OnPaint(PaintEventArgs e) { Render(); }
        void Render()
        {
            if (!IsHandleCreated || !Visible || Width < 2 || Height < 2) return;
            using (Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.Clear(Color.Transparent);
                using (GraphicsPath path = Shapes.Make(St.Shape, St.Radius, new RectangleF(0, 0, Width, Height)))
                {
                    if (St.Mode == 0 && St.Grad > 0)
                    {
                        float ang = St.Grad == 1 ? 90f : (St.Grad == 2 ? 0f : 45f);
                        using (LinearGradientBrush gb = new LinearGradientBrush(new Rectangle(0, 0, Width + 1, Height + 1), St.Bg, St.Bg2, ang)) g.FillPath(gb, path);
                    }
                    else using (SolidBrush sb = new SolidBrush(St.Mode == 0 ? St.Bg : Color.FromArgb(St.BgAlpha, St.Bg))) g.FillPath(sb, path);
                    if (St.Bar)
                    {
                        GraphicsState s = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        int bw = Math.Max(1, (int)(St.BarW * Fs));
                        using (SolidBrush b = new SolidBrush(Ov("barstrip", St.Accent)))
                        {
                            if (St.BarPos == 0) g.FillRectangle(b, 0, 0, Width, bw);
                            else if (St.BarPos == 1) g.FillRectangle(b, 0, Height - bw, Width, bw);
                            else if (St.BarPos == 2) g.FillRectangle(b, 0, 0, bw, Height);
                            else g.FillRectangle(b, Width - bw, 0, bw, Height);
                        }
                        g.Restore(s);
                    }
                    if (St.BorderW > 0)
                    {
                        float hw = St.BorderW / 2f;
                        using (GraphicsPath bp = Shapes.Make(St.Shape, St.Radius, new RectangleF(hw, hw, Width - St.BorderW, Height - St.BorderW)))
                        using (Pen pen = new Pen(St.Border, St.BorderW)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, bp); }
                    }
                    g.SetClip(path);
                }
                g.TranslateTransform(St.Pad, St.Pad);
                g.ScaleTransform((float)Fs, (float)Fs);
                Draw(g);
                g.ResetTransform();
                Native.Present(this, bmp, (byte)(St.Mode == 0 ? Math.Max(20, Math.Min(100, (int)Math.Round(St.Opacity * 100))) * 255 / 100 : 255));
            }
        }
        void Unused(Graphics g)
        {
        }

        int edge; // 1 L, 2 R, 4 T, 8 B
        Size resStart; Point resLoc;
        int HitEdge(Point p)
        {
            if (St.Lock) return 0;
            int m = 8, e = 0;
            if (p.X < m) e |= 1; if (p.X >= Width - m) e |= 2; if (p.Y < m) e |= 4; if (p.Y >= Height - m) e |= 8;
            return e;
        }
        static Cursor CursorFor(int e)
        {
            if (e == 1 || e == 2) return Cursors.SizeWE;
            if (e == 4 || e == 8) return Cursors.SizeNS;
            if (e == 5 || e == 10) return Cursors.SizeNWSE;
            if (e == 6 || e == 9) return Cursors.SizeNESW;
            return Cursors.Default;
        }
        public void ResizeTo(int w, int h)
        {
            St.W = Math.Max(60, w); St.H = Math.Max(40, h);
            ClientSize = new Size(St.W, St.H);
            UpdateBlur();
            LW = (float)((Width - 2 * St.Pad) / Fs); LH = (float)((Height - 2 * St.Pad) / Fs);
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragStart = Cursor.Position; formStart = Location; moved = false;
                edge = HitEdge(e.Location);
                resStart = Size; resLoc = Location;
                dragging = !St.Lock && edge == 0;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Point c = Cursor.Position; int dx = c.X - dragStart.X, dy = c.Y - dragStart.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > 4) moved = true;
                if (edge != 0 && moved)
                {
                    int nw = resStart.Width, nh = resStart.Height, nx = resLoc.X, ny = resLoc.Y;
                    if ((edge & 2) != 0) nw += dx;
                    if ((edge & 1) != 0) { nw -= dx; nx += dx; }
                    if ((edge & 8) != 0) nh += dy;
                    if ((edge & 4) != 0) { nh -= dy; ny += dy; }
                    if (nw < 60) { if ((edge & 1) != 0) nx -= 60 - nw; nw = 60; }
                    if (nh < 40) { if ((edge & 4) != 0) ny -= 40 - nh; nh = 40; }
                    Location = new Point(nx, ny); ResizeTo(nw, nh);
                }
                else if (dragging && moved) Location = new Point(formStart.X + dx, formStart.Y + dy);
            }
            else Cursor = CursorFor(HitEdge(e.Location));
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (edge != 0 && moved) { St.Save(Id); SavePos(); }
            else if (dragging && moved) SavePos();
            if (e.Button == MouseButtons.Left && !moved) OnWidgetClick(new PointF((float)((e.X - St.Pad) / Fs), (float)((e.Y - St.Pad) / Fs)));
            dragging = false; edge = 0;
            base.OnMouseUp(e);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); SendBack(); UpdateBlur(); Invalidate(); }
        public new void Invalidate() { Render(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); SendBack(); }
        void SendBack()
        {
            if (!St.Top) Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        public void SavePos() { Settings.Set(Id + ".x", Left); Settings.Set(Id + ".y", Top); Settings.Save(); }
        public void SetShown(bool v)
        {
            Settings.Set(Id + ".visible", v); Settings.Save();
            if (v) { Show(); SendBack(); } else Hide();
        }

        void BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Customize everything...", null, delegate { CustomizeForm.Open(this); });
            ToolStripMenuItem th = new ToolStripMenuItem("Quick theme");
            foreach (string t in Sty.ThemeNames) { string tt = t; th.DropDownItems.Add(tt, null, delegate { St.ApplyTheme(tt); Restyle(); }); }
            m.Items.Add(th);
            ToolStripMenuItem op = new ToolStripMenuItem("Opacity");
            foreach (int pct in new int[] { 20, 30, 40, 50, 60, 70, 80, 90, 100 })
            {
                int pp = pct;
                op.DropDownItems.Add(pp + "%", null, delegate { if (St.Mode == 0) St.Opacity = pp / 100.0; else St.BgAlpha = pp * 255 / 100; Restyle(); });
            }
            m.Items.Add(op);
            ToolStripMenuItem sz = new ToolStripMenuItem("Size (whole widget)");
            foreach (int pct in new int[] { 50, 60, 70, 85, 100, 115, 125, 150, 175, 200, 250, 300 })
            {
                int pp = pct;
                sz.DropDownItems.Add(pp + "%", null, delegate { St.W = DefW * pp / 100; St.H = DefH * pp / 100; St.FontPct = pp; Restyle(); });
            }
            m.Items.Add(sz);
            ToolStripMenuItem col = new ToolStripMenuItem("Accent color");
            string[] cn = { "Blue", "Green", "Orange", "Pink", "Purple", "White", "Red", "Cyan", "Yellow", "Lime", "Teal", "Gold" };
            Color[] cc = { Color.FromArgb(80, 160, 255), Color.FromArgb(70, 210, 140), Color.FromArgb(255, 170, 60), Color.FromArgb(255, 100, 150), Color.FromArgb(170, 120, 255), Color.FromArgb(235, 235, 235), Color.FromArgb(255, 80, 80), Color.FromArgb(0, 230, 255), Color.FromArgb(255, 235, 60), Color.FromArgb(160, 240, 70), Color.FromArgb(0, 170, 160), Color.FromArgb(255, 200, 40) };
            for (int i = 0; i < cn.Length; i++) { Color c1 = cc[i]; col.DropDownItems.Add(cn[i], null, delegate { St.Accent = c1; Restyle(); }); }
            m.Items.Add(col);
            ToolStripMenuItem rf = new ToolStripMenuItem("Refresh rate");
            foreach (int ms in new int[] { 100, 250, 500, 1000, 2000, 5000, 10000, 60000 })
            {
                int mm = ms;
                rf.DropDownItems.Add((mm / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " s", null, delegate { St.Interval = mm; Restyle(); });
            }
            m.Items.Add(rf);
            ToolStripMenuItem lk = new ToolStripMenuItem("Lock position"); lk.CheckOnClick = true;
            lk.Click += delegate { St.Lock = lk.Checked; Restyle(); };
            ToolStripMenuItem top = new ToolStripMenuItem("Always on top"); top.CheckOnClick = true;
            top.Click += delegate { St.Top = top.Checked; Restyle(); };
            m.Opening += delegate { lk.Checked = St.Lock; top.Checked = St.Top; };
            m.Items.Add(top); m.Items.Add(lk);
            m.Items.Add("Duplicate this widget", null, delegate { Program.AddWidget(Type, this); });
            ToolStripMenuItem wm = new ToolStripMenuItem("Widgets: show / restore / add"); Program.AttachWidgetsMenu(wm); m.Items.Add(wm);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Hide", null, delegate { SetShown(false); Program.RefreshTray(); });
            m.Items.Add("Delete this widget", null, delegate { Program.RemoveWidget(this); });
            m.Items.Add("Exit all widgets", null, delegate { Program.ExitAll(); });
            ContextMenuStrip = m;
        }
    }

    // ================= Clock =================
    class ClockWidget : Widget
    {
        public static readonly string[] StyleNames = { "Digital", "Digital with seconds", "Stacked", "Analog classic", "Analog minimal", "Analog roman", "Binary", "Words", "Rings", "Flip cards", "Date card", "One line" };
        static readonly int[][] DefSize = {
            new[]{240,110}, new[]{260,90}, new[]{160,190}, new[]{200,200}, new[]{200,200}, new[]{200,200}, new[]{240,150}, new[]{260,130}, new[]{200,200}, new[]{260,120}, new[]{150,170}, new[]{300,60} };
        public int Style { get { return Settings.GetInt(Id + ".cstyle", 0); } set { Settings.Set(Id + ".cstyle", value); } }
        bool H24 { get { return Settings.GetBool(Id + ".h24", true); } }
        bool Secs { get { return Settings.GetBool(Id + ".secs", true); } }
        bool ShowDate { get { return Settings.GetBool(Id + ".date", true); } }
        string TzId { get { return Settings.Get(Id + ".tz", ""); } }
        string Label { get { return Settings.Get(Id + ".label", ""); } }

        public ClockWidget(string id) : base(id, "clock", "Clock", 240, 110, 1000)
        {
            int s = Style; if (!Settings.Get(id + ".w", "").Equals("")) return;
            St.W = DefSize[s][0]; St.H = DefSize[s][1];
        }
        DateTime NowT()
        {
            string id = TzId;
            if (id == "") return DateTime.Now;
            try { return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(id)); } catch { return DateTime.Now; }
        }
        public override void BuildExtra(Action<string, Control> add)
        {
            ComboBox cs = new ComboBox(); cs.DropDownStyle = ComboBoxStyle.DropDownList; cs.Width = 220;
            cs.Items.AddRange(StyleNames); cs.SelectedIndex = Math.Min(Style, StyleNames.Length - 1);
            cs.SelectedIndexChanged += delegate { Style = cs.SelectedIndex; St.W = DefSize[Style][0]; St.H = DefSize[Style][1]; Restyle(); };
            add("Clock type", cs);
            CheckBox c24 = new CheckBox(); c24.Text = "24-hour"; c24.Checked = H24; c24.CheckedChanged += delegate { Settings.Set(Id + ".h24", c24.Checked); Settings.Save(); Invalidate(); };
            add("", c24);
            CheckBox csec = new CheckBox(); csec.Text = "Show seconds / second hand"; csec.Checked = Secs; csec.CheckedChanged += delegate { Settings.Set(Id + ".secs", csec.Checked); Settings.Save(); Invalidate(); };
            add("", csec);
            CheckBox cd = new CheckBox(); cd.Text = "Show date"; cd.Checked = ShowDate; cd.CheckedChanged += delegate { Settings.Set(Id + ".date", cd.Checked); Settings.Save(); Invalidate(); };
            add("", cd);
            ComboBox tz = new ComboBox(); tz.DropDownStyle = ComboBoxStyle.DropDownList; tz.Width = 220;
            List<string> ids = new List<string>(); tz.Items.Add("Local time"); ids.Add("");
            foreach (TimeZoneInfo z in TimeZoneInfo.GetSystemTimeZones()) { tz.Items.Add(z.DisplayName); ids.Add(z.Id); }
            int si = ids.IndexOf(TzId); tz.SelectedIndex = si < 0 ? 0 : si;
            tz.SelectedIndexChanged += delegate { Settings.Set(Id + ".tz", ids[tz.SelectedIndex]); Settings.Save(); Invalidate(); };
            add("Time zone", tz);
            TextBox lb = new TextBox(); lb.Width = 220; lb.Text = Label;
            lb.TextChanged += delegate { Settings.Set(Id + ".label", lb.Text); Settings.Save(); Invalidate(); };
            add("Label (e.g. city)", lb);
        }

        protected override void Draw(Graphics g)
        {
            DateTime n = NowT(); int st = Style; float W = LW, H = LH;
            string lab = Label; float top = 0;
            if (lab != "" && st != 3 && st != 4 && st != 5 && st != 8 && st != 10) { Txt(g, lab.ToUpper(), 12, true, DimB, 2, 0); top = 16; H -= 16; g.TranslateTransform(0, top); }
            else if (lab != "") { Txt(g, lab.ToUpper(), 11, true, DimB, 2, 0); }
            string t = H24 ? n.ToString("HH:mm") : n.ToString("h:mm");
            string ap = H24 ? "" : n.ToString("tt", CultureInfo.InvariantCulture);
            string date = n.ToString("dddd, d MMMM yyyy");
            switch (st)
            {
                case 0:
                    {
                        float dh = ShowDate ? H * 0.28f : 0; float tw = Secs || ap != "" ? W * 0.78f : W;
                        Fit(g, t, 200, true, FgB, new RectangleF(0, 0, tw, H - dh));
                        if (Secs || ap != "") Fit(g, (Secs ? n.ToString("ss") : "") + (ap != "" ? "\n" + ap : ""), 40, false, AccB, new RectangleF(tw, 0, W - tw, H - dh));
                        if (ShowDate) Fit(g, date, 40, false, DimB, new RectangleF(0, H - dh, W, dh));
                        break;
                    }
                case 1:
                    {
                        float dh = ShowDate ? H * 0.28f : 0;
                        Fit(g, (H24 ? n.ToString("HH:mm:ss") : n.ToString("h:mm:ss ") + ap), 200, true, FgB, new RectangleF(0, 0, W, H - dh));
                        if (ShowDate) Fit(g, date, 40, false, DimB, new RectangleF(0, H - dh, W, dh));
                        break;
                    }
                case 2:
                    {
                        float dh = ShowDate ? H * 0.16f : 0; float rh = (H - dh) / 2;
                        Fit(g, H24 ? n.ToString("HH") : n.ToString("hh"), 200, true, FgB, new RectangleF(0, 0, W, rh));
                        Fit(g, n.ToString("mm"), 200, true, AccB, new RectangleF(0, rh, W, rh));
                        if (ShowDate) Fit(g, n.ToString("ddd d MMM"), 30, false, DimB, new RectangleF(0, H - dh, W, dh));
                        break;
                    }
                case 3: case 4: case 5: DrawAnalog(g, n, st, W, H, top); break;
                case 6: DrawBinary(g, n, W, H); break;
                case 7:
                    {
                        string[] hn = { "twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven" };
                        int m5 = ((n.Minute + 2) / 5) * 5; int hh = n.Hour; if (m5 == 60) { m5 = 0; hh++; }
                        string cur = hn[hh % 12], nxt = hn[(hh + 1) % 12]; string s;
                        switch (m5)
                        {
                            case 0: s = cur + " o'clock"; break;
                            case 5: s = "five past " + cur; break; case 10: s = "ten past " + cur; break;
                            case 15: s = "quarter past " + cur; break; case 20: s = "twenty past " + cur; break;
                            case 25: s = "twenty five past " + cur; break; case 30: s = "half past " + cur; break;
                            case 35: s = "twenty five to " + nxt; break; case 40: s = "twenty to " + nxt; break;
                            case 45: s = "quarter to " + nxt; break; case 50: s = "ten to " + nxt; break;
                            default: s = "five to " + nxt; break;
                        }
                        float dh = ShowDate ? H * 0.2f : 0;
                        Fit(g, "It's " + s, 80, true, FgB, new RectangleF(0, 0, W, H - dh));
                        if (ShowDate) Fit(g, date, 30, false, DimB, new RectangleF(0, H - dh, W, dh));
                        break;
                    }
                case 8:
                    {
                        float R = Math.Min(W, H) / 2 - 6; float cx = W / 2, cy = H / 2; float lw = Math.Max(5, R * 0.1f);
                        Arc(g, new RectangleF(cx - R, cy - R, 2 * R, 2 * R), ((n.Hour % 12) + n.Minute / 60.0) / 12.0, lw, St.Accent);
                        float R2 = R - lw * 1.7f; Arc(g, new RectangleF(cx - R2, cy - R2, 2 * R2, 2 * R2), (n.Minute + n.Second / 60.0) / 60.0, lw, Color.FromArgb(220, St.Accent));
                        float R3 = R2 - lw * 1.7f; if (Secs) Arc(g, new RectangleF(cx - R3, cy - R3, 2 * R3, 2 * R3), n.Second / 60.0, lw, Color.FromArgb(160, St.Accent));
                        float ir = (Secs ? R3 : R2) - lw; Fit(g, t, 100, true, FgB, new RectangleF(cx - ir, cy - ir * 0.6f, 2 * ir, ir * 1.2f));
                        break;
                    }
                case 9:
                    {
                        float dh = ShowDate ? H * 0.2f : 0; float bh = H - dh; float gap = 8; float bw = (W - gap) / 2;
                        string a = H24 ? n.ToString("HH") : n.ToString("hh"), b = n.ToString("mm");
                        for (int i = 0; i < 2; i++)
                        {
                            RectangleF r = new RectangleF(i * (bw + gap), 0, bw, bh);
                            using (SolidBrush cb = new SolidBrush(Color.FromArgb(60, St.Dim))) g.FillRectangle(cb, r);
                            Fit(g, i == 0 ? a : b, 200, true, FgB, r);
                            using (Pen p = new Pen(Color.FromArgb(200, St.Bg), 2)) g.DrawLine(p, r.X, r.Y + bh / 2, r.Right, r.Y + bh / 2);
                        }
                        if (ShowDate) Fit(g, date, 30, false, DimB, new RectangleF(0, H - dh, W, dh));
                        break;
                    }
                case 10:
                    {
                        float bandH = H * 0.24f;
                        using (SolidBrush cb = new SolidBrush(St.Accent)) g.FillRectangle(cb, 0, 0, W, bandH);
                        using (SolidBrush ink = new SolidBrush(Color.FromArgb(240, St.Bg.A == 255 ? St.Bg : Color.Black))) Fit(g, n.ToString("MMMM yyyy").ToUpper(), 40, true, ink, new RectangleF(0, 0, W, bandH));
                        Fit(g, n.Day.ToString(), 200, true, FgB, new RectangleF(0, bandH, W, H * 0.56f));
                        Fit(g, n.ToString("dddd"), 40, false, DimB, new RectangleF(0, H * 0.8f, W, H * 0.2f));
                        break;
                    }
                default:
                    Fit(g, (ShowDate ? n.ToString("ddd d MMM") + "   " : "") + (Secs ? (H24 ? n.ToString("HH:mm:ss") : n.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)) : (H24 ? n.ToString("HH:mm") : n.ToString("h:mm tt", CultureInfo.InvariantCulture))), 200, true, FgB, new RectangleF(0, 0, W, H));
                    break;
            }
        }

        void DrawAnalog(Graphics g, DateTime n, int st, float W, float H, float top)
        {
            float R = Math.Min(W, H) / 2 - 3; float cx = W / 2, cy = H / 2;
            using (SolidBrush face = new SolidBrush(Color.FromArgb(40, Ov("face", St.Dim)))) g.FillEllipse(face, cx - R, cy - R, 2 * R, 2 * R);
            using (Pen ring = new Pen(Ov("face", St.Dim), 2)) g.DrawEllipse(ring, cx - R, cy - R, 2 * R, 2 * R);
            for (int i = 0; i < 60; i++)
            {
                bool hr = i % 5 == 0; if (st == 4 && !hr) continue;
                double a = i * Math.PI / 30; float r1 = R - (hr ? R * 0.14f : R * 0.07f), r2 = R - 3;
                using (Pen p = new Pen(hr ? Ov("ticks", St.Fg) : Ov("ticks", St.Dim), hr ? 2.5f : 1)) g.DrawLine(p, cx + (float)Math.Sin(a) * r1, cy - (float)Math.Cos(a) * r1, cx + (float)Math.Sin(a) * r2, cy - (float)Math.Cos(a) * r2);
            }
            if (st == 3 || st == 5)
            {
                SolidBrush numB = new SolidBrush(Ov("numerals", St.Fg)); string[] rom = { "XII", "I", "II", "III", "IIII", "V", "VI", "VII", "VIII", "IX", "X", "XI" };
                for (int i = 0; i < 12; i++)
                {
                    if (st == 3 && i % 3 != 0) continue;
                    double a = i * Math.PI / 6; float rr = R * 0.7f; string s = st == 3 ? (i == 0 ? "12" : i.ToString()) : rom[i];
                    float fs = R * (st == 3 ? 0.2f : 0.13f);
                    g.DrawString(s, F(fs, true), numB, new RectangleF(cx + (float)Math.Sin(a) * rr - 20, cy - (float)Math.Cos(a) * rr - fs, 40, fs * 2), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
                numB.Dispose();
            }
            double sec = n.Second, min = n.Minute + sec / 60.0, hour = (n.Hour % 12) + min / 60.0;
            Hand(g, cx, cy, hour * 30, R * 0.5f, st == 4 ? 3f : 5f, Ov("hourhand", St.Fg));
            Hand(g, cx, cy, min * 6, R * 0.76f, st == 4 ? 2f : 3.5f, Ov("minhand", St.Fg));
            if (Secs) Hand(g, cx, cy, sec * 6, R * 0.85f, 1.5f, Ov("sechand", St.Accent));
            using (SolidBrush b = new SolidBrush(Ov("dot", St.Accent))) g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
            if (ShowDate && st != 4) g.DrawString(n.ToString("d MMM"), F(R * 0.11f, false), DimB, new RectangleF(cx - R * 0.4f, cy + R * 0.28f, R * 0.8f, R * 0.2f), new StringFormat { Alignment = StringAlignment.Center });
        }
        static void Hand(Graphics g, float cx, float cy, double deg, float len, float w, Color c)
        {
            double a = deg * Math.PI / 180;
            using (Pen p = new Pen(c, w)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, cx, cy, cx + (float)(Math.Sin(a) * len), cy - (float)(Math.Cos(a) * len)); }
        }
        void DrawBinary(Graphics g, DateTime n, float W, float H)
        {
            int[] digs = { n.Hour / 10, n.Hour % 10, n.Minute / 10, n.Minute % 10, n.Second / 10, n.Second % 10 };
            int[] bits = { 2, 4, 3, 4, 3, 4 };
            int cols = Secs ? 6 : 4; float cw = W / cols; float ch = (H - (ShowDate ? 18 : 0)) / 4f; float rad = Math.Min(cw, ch) * 0.36f;
            for (int c = 0; c < cols; c++)
                for (int b = 0; b < 4; b++)
                {
                    int bit = 3 - b; if (bit >= bits[c]) continue;
                    bool on = ((digs[c] >> bit) & 1) == 1; float x = c * cw + cw / 2, y = b * ch + ch / 2;
                    using (SolidBrush br = new SolidBrush(on ? St.Accent : Color.FromArgb(70, St.Dim))) g.FillEllipse(br, x - rad, y - rad, 2 * rad, 2 * rad);
                }
            if (ShowDate) Fit(g, n.ToString("ddd d MMM yyyy"), 20, false, DimB, new RectangleF(0, H - 18, W, 18));
        }
    }

    // ================= System =================
    class SystemWidget : Widget
    {
        long pi, pk, pu; double cpu, ram, usedGb, totGb;
        int Style { get { return Settings.GetInt(Id + ".sstyle", 0); } }
        public SystemWidget(string id) : base(id, "system", "System (CPU/RAM)", 240, 130, 1000)
        { long i, k, u; Native.GetSystemTimes(out i, out k, out u); pi = i; pk = k; pu = u; OnTick(); }
        public override void BuildExtra(Action<string, Control> add)
        {
            ComboBox cs = new ComboBox(); cs.DropDownStyle = ComboBoxStyle.DropDownList; cs.Width = 220;
            cs.Items.AddRange(new object[] { "Bars", "Rings" }); cs.SelectedIndex = Style;
            cs.SelectedIndexChanged += delegate { Settings.Set(Id + ".sstyle", cs.SelectedIndex); Settings.Save(); Invalidate(); };
            add("Style", cs);
        }
        protected override void OnTick()
        {
            long i, k, u;
            if (Native.GetSystemTimes(out i, out k, out u))
            {
                long idle = i - pi, total = (k - pk) + (u - pu);
                if (total > 0) cpu = 1.0 - (double)idle / total;
                pi = i; pk = k; pu = u;
            }
            Native.MEMSTATUS m = new Native.MEMSTATUS(); m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMSTATUS));
            if (Native.GlobalMemoryStatusEx(ref m)) { totGb = m.ullTotalPhys / 1073741824.0; usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0; ram = usedGb / totGb; }
        }
        protected override void Draw(Graphics g)
        {
            float W = LW, H = LH;
            if (Style == 1)
            {
                float R = Math.Min(W / 4f, H / 2f) - 8; float lw = Math.Max(5, R * 0.18f);
                for (int i = 0; i < 2; i++)
                {
                    float cx = W * (i == 0 ? 0.25f : 0.75f), cy = H / 2; double f = i == 0 ? cpu : ram;
                    Arc(g, new RectangleF(cx - R, cy - R, 2 * R, 2 * R), f, lw, f > 0.9 ? Color.FromArgb(255, 90, 90) : St.Accent);
                    Fit(g, (f * 100).ToString("0") + "%", 60, true, FgB, new RectangleF(cx - R * 0.7f, cy - R * 0.4f, R * 1.4f, R * 0.5f));
                    Fit(g, i == 0 ? "CPU" : "RAM", 30, false, DimB, new RectangleF(cx - R * 0.7f, cy + R * 0.1f, R * 1.4f, R * 0.4f));
                }
                return;
            }
            float h2 = H / 2;
            Txt(g, "CPU", 13, true, DimB, 2, 2); Txt(g, (cpu * 100).ToString("0") + "%", 26, true, FgB, 2, 16); Bar(g, 2, h2 - 12, W - 4, 8, cpu);
            Txt(g, "RAM", 13, true, DimB, 2, h2 + 2); Txt(g, usedGb.ToString("0.0") + " / " + totGb.ToString("0.0") + " GB", 12, false, FgB, W - 110, h2 + 4);
            Txt(g, (ram * 100).ToString("0") + "%", 26, true, FgB, 2, h2 + 16); Bar(g, 2, H - 10, W - 4, 8, ram);
        }
    }

    // ================= Disks =================
    class DiskWidget : Widget
    {
        List<DriveInfo> drives = new List<DriveInfo>();
        public DiskWidget(string id) : base(id, "disk", "Disks", 240, 100, 5000)
        {
            foreach (DriveInfo d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && drives.Count < 6) drives.Add(d);
            if (Settings.Get(id + ".h", "") == "") St.H = 20 + Math.Max(1, drives.Count) * 40;
        }
        protected override void Draw(Graphics g)
        {
            float rh = LH / Math.Max(1, drives.Count); float y = 0;
            foreach (DriveInfo d in drives)
            {
                try
                {
                    double tot = d.TotalSize, free = d.TotalFreeSpace;
                    Txt(g, d.Name.TrimEnd('\\'), 14, true, FgB, 2, y + 2);
                    g.DrawString((free / 1073741824.0).ToString("0") + " GB free of " + (tot / 1073741824.0).ToString("0"), F(12, false), DimB, new RectangleF(48, y + 4, LW - 50, 20));
                    Bar(g, 2, y + 24, LW - 4, 7, (tot - free) / tot);
                }
                catch { }
                y += rh;
            }
        }
    }

    // ================= Network =================
    class NetWidget : Widget
    {
        long prevR, prevS; DateTime prevT; double down, up; double[] hist = new double[60];
        public NetWidget(string id) : base(id, "network", "Network", 240, 120, 1000) { Read(out prevR, out prevS); prevT = DateTime.UtcNow; }
        static void Read(out long r, out long s)
        {
            r = 0; s = 0;
            try
            {
                foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPv4InterfaceStatistics st = n.GetIPv4Statistics(); r += st.BytesReceived; s += st.BytesSent;
                }
            }
            catch { }
        }
        static string Fmt(double bps) { return bps > 1048576 ? (bps / 1048576).ToString("0.0") + " MB/s" : (bps / 1024).ToString("0") + " KB/s"; }
        protected override void OnTick()
        {
            long r, s; Read(out r, out s); DateTime t = DateTime.UtcNow; double dt = (t - prevT).TotalSeconds; if (dt <= 0) return;
            down = Math.Max(0, (r - prevR) / dt); up = Math.Max(0, (s - prevS) / dt); prevR = r; prevS = s; prevT = t;
            Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = down;
        }
        protected override void Draw(Graphics g)
        {
            Ttl(g, "NETWORK", 2, 2);
            Txt(g, "v " + Fmt(down), 20, true, AccB, 2, 18); Txt(g, "^ " + Fmt(up), 13, false, FgB, 2, 44);
            double max = 1024; foreach (double h in hist) if (h > max) max = h;
            float gy = 66, gh = LH - gy - 2; if (gh < 10) return;
            PointF[] pts = new PointF[hist.Length];
            for (int i = 0; i < hist.Length; i++) pts[i] = new PointF(2 + i * ((LW - 4) / (hist.Length - 1)), (float)(gy + gh - (hist[i] / max) * gh));
            using (Pen p = new Pen(St.Accent, 1.5f)) g.DrawLines(p, pts);
        }
    }

    // ================= Battery =================
    class BatteryWidget : Widget
    {
        public BatteryWidget(string id) : base(id, "battery", "Battery", 240, 90, 5000) { }
        protected override void Draw(Graphics g)
        {
            PowerStatus ps = SystemInformation.PowerStatus;
            double p = ps.BatteryLifePercent; if (p > 1) p = 1;
            bool none = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
            Ttl(g, "BATTERY", 2, 2);
            Txt(g, none ? "AC power" : (p * 100).ToString("0") + "%", 26, true, FgB, 2, 16);
            if (!none)
            {
                Bar(g, 2, LH - 12, LW - 4, 8, p);
                string s = ps.PowerLineStatus == PowerLineStatus.Online ? "Charging" : "On battery";
                if (ps.PowerLineStatus != PowerLineStatus.Online && ps.BatteryLifeRemaining > 0) s += "  " + (ps.BatteryLifeRemaining / 3600) + "h " + (ps.BatteryLifeRemaining % 3600 / 60) + "m";
                Txt(g, s, 12, false, DimB, LW - 130, 24);
            }
        }
    }

    // ================= Calendar =================
    class CalendarWidget : Widget
    {
        DateTime shown = DateTime.Today;
        bool MondayFirst { get { return Settings.GetBool(Id + ".mon", true); } }
        public CalendarWidget(string id) : base(id, "calendar", "Calendar", 240, 210, 60000) { }
        public override void BuildExtra(Action<string, Control> add)
        {
            CheckBox c = new CheckBox(); c.Text = "Week starts on Monday"; c.Checked = MondayFirst;
            c.CheckedChanged += delegate { Settings.Set(Id + ".mon", c.Checked); Settings.Save(); Invalidate(); };
            add("", c);
        }
        protected override void OnTick() { if (shown.Month == DateTime.Today.AddDays(-1).Month && DateTime.Today.Day == 1) shown = DateTime.Today; }
        protected override void OnWidgetClick(PointF p)
        {
            if (p.Y < 30)
            {
                if (p.X < LW * 0.2f) shown = shown.AddMonths(-1);
                else if (p.X > LW * 0.8f) shown = shown.AddMonths(1);
                else shown = DateTime.Today;
                Invalidate();
            }
        }
        protected override void Draw(Graphics g)
        {
            float W = LW;
            Txt(g, "<", 16, true, DimB, 4, 6); Txt(g, ">", 16, true, DimB, W - 16, 6);
            Fit(g, shown.ToString("MMMM yyyy"), 18, true, FgB, new RectangleF(W * 0.2f, 2, W * 0.6f, 26));
            bool mon = MondayFirst;
            string[] dn = mon ? new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" } : new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
            float cw = W / 7f; float top = 34; float rh = Math.Min(26, (LH - top - 18) / 6f);
            for (int i = 0; i < 7; i++) g.DrawString(dn[i], F(12, false), DimB, new RectangleF(i * cw, top - 4, cw, 18), new StringFormat { Alignment = StringAlignment.Center });
            DateTime first = new DateTime(shown.Year, shown.Month, 1);
            int off = mon ? ((int)first.DayOfWeek + 6) % 7 : (int)first.DayOfWeek;
            int days = DateTime.DaysInMonth(shown.Year, shown.Month);
            for (int d = 1; d <= days; d++)
            {
                int idx = off + d - 1; float x = (idx % 7) * cw, y = top + 16 + (idx / 7) * rh;
                bool today = shown.Year == DateTime.Today.Year && shown.Month == DateTime.Today.Month && d == DateTime.Today.Day;
                RectangleF r = new RectangleF(x, y, cw, rh);
                if (today) using (SolidBrush b = new SolidBrush(St.Accent)) g.FillEllipse(b, x + cw / 2 - rh * 0.45f, y + rh * 0.05f, rh * 0.9f, rh * 0.9f);
                using (SolidBrush ink = new SolidBrush(St.Bg.A == 255 && St.Mode == 0 ? St.Bg : Color.Black))
                    g.DrawString(d.ToString(), F(12, today), today ? ink : FgB, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
        }
    }

    // ================= Notes =================
    class NotesWidget : Widget
    {
        TextBox tb; Timer save;
        public NotesWidget(string id) : base(id, "notes", "Notes", 240, 180, 60000)
        {
            tb = new TextBox(); tb.Multiline = true; tb.BorderStyle = BorderStyle.None; tb.ScrollBars = ScrollBars.None; tb.AcceptsReturn = true;
            try { if (File.Exists(Settings.NotesPath(Id))) tb.Text = File.ReadAllText(Settings.NotesPath(Id)); } catch { }
            Controls.Add(tb);
            save = new Timer(); save.Interval = 800;
            save.Tick += delegate { save.Stop(); try { Directory.CreateDirectory(Path.GetDirectoryName(Settings.NotesPath(Id))); File.WriteAllText(Settings.NotesPath(Id), tb.Text); } catch { } };
            tb.TextChanged += delegate { save.Stop(); save.Start(); };
            SizeChanged += delegate { DoLayout(); };
        }
        protected override void OnRestyled() { DoLayout(); }
        public override void ApplyStyle() { base.ApplyStyle(); DoLayout(); }
        void DoLayout()
        {
            if (tb == null) return;
            float fs = (float)Fs;
            tb.Left = (int)(St.Pad + 8 * fs); tb.Top = (int)(St.Pad + 28 * fs);
            tb.Width = Math.Max(20, Width - 2 * St.Pad - (int)(16 * fs)); tb.Height = Math.Max(20, Height - 2 * St.Pad - (int)(36 * fs));
            tb.BackColor = Color.FromArgb(255, St.Bg.R, St.Bg.G, St.Bg.B);
            tb.ForeColor = St.Fg;
            try { tb.Font = new Font(St.Font, 12f * fs, FontStyle.Regular, GraphicsUnit.Pixel); } catch { }
        }
        protected override void Draw(Graphics g) { Ttl(g, "NOTES", 6, 6); }
    }

    // ================= Stopwatch =================
    class StopwatchWidget : Widget
    {
        Stopwatch sw = new Stopwatch();
        public StopwatchWidget(string id) : base(id, "stopwatch", "Stopwatch", 240, 90, 100) { }
        protected override void OnWidgetClick(PointF p) { if (sw.IsRunning) sw.Stop(); else sw.Start(); Invalidate(); }
        protected override void OnDoubleClick(EventArgs e) { sw.Reset(); Invalidate(); }
        protected override void Draw(Graphics g)
        {
            TimeSpan t = sw.Elapsed;
            Ttl(g, "STOPWATCH", 2, 2);
            Txt(g, sw.IsRunning ? "click: pause" : "click: start", 12, false, DimB, LW - 90, 4);
            Fit(g, string.Format("{0:00}:{1:00}.{2:0}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 100), 80, true, FgB, new RectangleF(0, 20, LW, LH - 36));
            Txt(g, "double-click: reset", 11, false, AccB, 2, LH - 16);
        }
    }

    // ================= CPU (detailed) =================
    class CpuWidget : Widget
    {
        long pi, pk, pu; double cpu; double[] hist = new double[60]; double[] cores; PerformanceCounter[] pcs; int procs, threads, n;
        TimeSpan up; string name = "";
        bool ShowCores { get { return Settings.GetBool(Id + ".cores", true); } }
        bool ShowGraph { get { return Settings.GetBool(Id + ".graph", true); } }
        bool ShowInfo { get { return Settings.GetBool(Id + ".info", true); } }
        public CpuWidget(string id) : base(id, "cpu", "CPU (detailed)", 240, 190, 1000)
        {
            long i, k, u; Native.GetSystemTimes(out i, out k, out u); pi = i; pk = k; pu = u;
            cores = new double[Environment.ProcessorCount];
            try { name = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", ""); name = name == null ? "" : name.Trim(); } catch { }
        }
        public override void BuildExtra(Action<string, Control> add)
        {
            AddCheck(add, "Show graph", ".graph", true); AddCheck(add, "Show per-core usage", ".cores", true); AddCheck(add, "Show processes / uptime / CPU name", ".info", true);
        }
        void AddCheck(Action<string, Control> add, string text, string key, bool def)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.Checked = Settings.GetBool(Id + key, def); c.Width = 280;
            c.CheckedChanged += delegate { Settings.Set(Id + key, c.Checked); Settings.Save(); Invalidate(); };
            add("", c);
        }
        int tickN;
        protected override void OnTick()
        {
            long i, k, u;
            if (Native.GetSystemTimes(out i, out k, out u))
            {
                long idle = i - pi, total = (k - pk) + (u - pu);
                if (total > 0) cpu = 1.0 - (double)idle / total;
                pi = i; pk = k; pu = u;
            }
            Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = cpu * 100;
            if (ShowCores)
            {
                try
                {
                    if (pcs == null) { pcs = new PerformanceCounter[cores.Length]; for (int c = 0; c < pcs.Length; c++) pcs[c] = new PerformanceCounter("Processor", "% Processor Time", c.ToString()); }
                    for (int c = 0; c < pcs.Length; c++) cores[c] = pcs[c].NextValue() / 100.0;
                }
                catch { }
            }
            if (ShowInfo && tickN++ % 3 == 0)
            {
                try
                {
                    Process[] ps = Process.GetProcesses(); procs = ps.Length; int t = 0;
                    foreach (Process p in ps) { try { t += p.Threads.Count; } catch { } p.Dispose(); }
                    threads = t;
                }
                catch { }
                up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            }
        }
        protected override void Draw(Graphics g)
        {
            float W = LW, H = LH; float y = 0;
            Ttl(g, "CPU", 2, 0);
            Txt(g, (cpu * 100).ToString("0") + "%", 30, true, FgB, 2, 14);
            if (ShowInfo && name != "") g.DrawString(name, F(11, false), DimB, new RectangleF(88, 6, W - 90, 30), new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter });
            y = 50;
            if (ShowGraph) { float gh = Math.Min(48, H * 0.22f); Spark(g, hist, 2, y, W - 4, gh, 0, 100, St.Accent); y += gh + 6; }
            float footer = ShowInfo ? 18 : 0;
            if (ShowCores && H - y - footer > 14)
            {
                int cn = cores.Length; float gap = 3; float bw = Math.Max(3, (W - 4 - gap * (cn - 1)) / cn); float bh = H - y - footer - 2;
                for (int c = 0; c < cn; c++)
                {
                    float x = 2 + c * (bw + gap);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(70, St.Dim))) g.FillRectangle(bg, x, y, bw, bh);
                    float f = (float)Math.Max(0, Math.Min(1, cores[c]));
                    using (SolidBrush fg = new SolidBrush(f > 0.9 ? Color.FromArgb(255, 90, 90) : St.Accent)) g.FillRectangle(fg, x, y + bh * (1 - f), bw, bh * f);
                }
            }
            if (ShowInfo) Txt(g, procs + " processes  " + threads + " threads  up " + (int)up.TotalDays + "d " + up.Hours + "h " + up.Minutes + "m", 11, false, DimB, 2, H - 16);
        }
    }

    // ================= RAM (detailed) =================
    class RamWidget : Widget
    {
        double used, total, avail, commit, commitTot; int tickN; List<KeyValuePair<string, long>> top = new List<KeyValuePair<string, long>>();
        double[] hist = new double[60];
        public RamWidget(string id) : base(id, "ram", "RAM (detailed)", 240, 220, 1000) { OnTick(); }
        int TopN { get { return Settings.GetInt(Id + ".topn", 5); } }
        public override void BuildExtra(Action<string, Control> add)
        {
            CheckBox c1 = new CheckBox(); c1.Text = "Show graph"; c1.Checked = Settings.GetBool(Id + ".graph", true);
            c1.CheckedChanged += delegate { Settings.Set(Id + ".graph", c1.Checked); Settings.Save(); Invalidate(); }; add("", c1);
            CheckBox c2 = new CheckBox(); c2.Text = "Show top processes by memory"; c2.Checked = Settings.GetBool(Id + ".top", true); c2.Width = 260;
            c2.CheckedChanged += delegate { Settings.Set(Id + ".top", c2.Checked); Settings.Save(); Invalidate(); }; add("", c2);
            NumericUpDown n = new NumericUpDown(); n.Minimum = 1; n.Maximum = 15; n.Value = TopN; n.Width = 80;
            n.ValueChanged += delegate { Settings.Set(Id + ".topn", (int)n.Value); Settings.Save(); Invalidate(); }; add("Processes to list", n);
        }
        protected override void OnTick()
        {
            Native.MEMSTATUS m = new Native.MEMSTATUS(); m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMSTATUS));
            if (Native.GlobalMemoryStatusEx(ref m))
            {
                total = m.ullTotalPhys; avail = m.ullAvailPhys; used = total - avail; commitTot = m.ullTotalPageFile; commit = m.ullTotalPageFile - m.ullAvailPageFile;
                Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = used / total * 100;
            }
            if (Settings.GetBool(Id + ".top", true) && tickN++ % 3 == 0)
            {
                try
                {
                    Dictionary<string, long> d = new Dictionary<string, long>();
                    foreach (Process p in Process.GetProcesses())
                    {
                        try { long w = p.WorkingSet64; long cur; d.TryGetValue(p.ProcessName, out cur); d[p.ProcessName] = cur + w; } catch { }
                        p.Dispose();
                    }
                    List<KeyValuePair<string, long>> l = new List<KeyValuePair<string, long>>(d);
                    l.Sort(delegate (KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); });
                    top = l;
                }
                catch { }
            }
        }
        protected override void Draw(Graphics g)
        {
            float W = LW, H = LH; double f = total > 0 ? used / total : 0;
            Ttl(g, "RAM", 2, 0);
            Txt(g, (f * 100).ToString("0") + "%", 30, true, FgB, 2, 14);
            Txt(g, Gb(used) + " / " + Gb(total) + " GB", 13, true, FgB, W - 120, 18);
            Txt(g, "free " + Gb(avail) + " GB", 12, false, DimB, W - 120, 36);
            Bar(g, 2, 54, W - 4, 8, f);
            float y = 68;
            Txt(g, "Commit " + Gb(commit) + " / " + Gb(commitTot) + " GB", 11, false, DimB, 2, y); y += 18;
            if (Settings.GetBool(Id + ".graph", true)) { float gh = Math.Min(40, H * 0.18f); Spark(g, hist, 2, y, W - 4, gh, 0, 100, St.Accent); y += gh + 6; }
            if (Settings.GetBool(Id + ".top", true))
            {
                int n = Math.Min(TopN, top.Count);
                for (int i = 0; i < n && y + 16 <= H; i++)
                {
                    g.DrawString(top[i].Key, F(12, false), FgB, new RectangleF(2, y, W - 80, 16), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    g.DrawString((top[i].Value / 1048576.0).ToString("0") + " MB", F(12, false), DimB, new RectangleF(W - 80, y, 78, 16), new StringFormat { Alignment = StringAlignment.Far });
                    y += 16;
                }
            }
        }
    }

    // ================= Disk I/O =================
    class DiskIoWidget : Widget
    {
        PerformanceCounter rd, wr, act; double r, w, a; double[] hr = new double[60], hw = new double[60]; bool fail;
        public DiskIoWidget(string id) : base(id, "diskio", "Disk activity", 240, 150, 1000) { }
        protected override void OnTick()
        {
            try
            {
                if (rd == null && !fail)
                {
                    rd = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"); wr = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                    act = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                }
                if (rd != null) { r = rd.NextValue(); w = wr.NextValue(); a = Math.Min(100, act.NextValue()); }
            }
            catch { fail = true; }
            Array.Copy(hr, 1, hr, 0, hr.Length - 1); hr[hr.Length - 1] = r; Array.Copy(hw, 1, hw, 0, hw.Length - 1); hw[hw.Length - 1] = w;
        }
        static string Fmt(double b) { return b > 1048576 ? (b / 1048576).ToString("0.0") + " MB/s" : (b / 1024).ToString("0") + " KB/s"; }
        protected override void Draw(Graphics g)
        {
            float W = LW, H = LH;
            Ttl(g, "DISK ACTIVITY", 2, 0);
            if (fail) { Txt(g, "Counters not available", 12, false, DimB, 2, 24); return; }
            Txt(g, "Read " + Fmt(r), 14, true, AccB, 2, 18); Txt(g, "Write " + Fmt(w), 14, true, FgB, W / 2, 18);
            Txt(g, "Busy " + a.ToString("0") + "%", 12, false, DimB, 2, 40); Bar(g, 60, 45, W - 62, 6, a / 100.0);
            float gy = 60, gh = H - gy - 2; if (gh > 10)
            {
                double max = 1048576; foreach (double v in hr) max = Math.Max(max, v); foreach (double v in hw) max = Math.Max(max, v);
                Spark(g, hr, 2, gy, W - 4, gh, 0, max, St.Accent); Spark(g, hw, 2, gy, W - 4, gh, 0, max, St.Dim);
            }
        }
    }

    // ================= GPU =================
    class GpuWidget : Widget
    {
        List<PerformanceCounter> pcs = new List<PerformanceCounter>(); double g3d; double[] hist = new double[60]; bool fail; int tickN; string name = "";
        public GpuWidget(string id) : base(id, "gpu", "GPU", 240, 120, 1000)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-b80c-00805f9fa9cb}\0000"))
                    if (k != null) name = Convert.ToString(k.GetValue("DriverDesc", ""));
            }
            catch { }
        }
        protected override void OnTick()
        {
            try
            {
                if (tickN++ % 10 == 0)
                {
                    pcs.Clear();
                    PerformanceCounterCategory cat = new PerformanceCounterCategory("GPU Engine");
                    foreach (string inst in cat.GetInstanceNames()) if (inst.EndsWith("engtype_3D")) { try { pcs.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst)); } catch { } }
                    if (pcs.Count > 0) fail = false; else fail = true;
                }
                double sum = 0; foreach (PerformanceCounter p in pcs) { try { sum += p.NextValue(); } catch { } }
                g3d = Math.Min(100, sum);
            }
            catch { fail = true; }
            Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = g3d;
        }
        protected override void Draw(Graphics g)
        {
            Ttl(g, "GPU", 2, 0);
            if (name != "") g.DrawString(name, F(11, false), DimB, new RectangleF(40, 3, LW - 42, 16), new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            if (fail) { Txt(g, "GPU counters not available", 12, false, DimB, 2, 30); return; }
            Txt(g, g3d.ToString("0") + "%", 30, true, FgB, 2, 14);
            float gy = 54; Spark(g, hist, 2, gy, LW - 4, LH - gy - 2, 0, 100, St.Accent);
        }
    }


    // ================= Dashboard: CPU + RAM + GPU in one =================
    class DashWidget : Widget
    {
        long pi, pk, pu; double cpu, ram, gpu, usedGb, totGb;
        double[] hc = new double[60], hr = new double[60], hg = new double[60];
        List<PerformanceCounter> pcs = new List<PerformanceCounter>(); bool gfail; int tickN; string gname = "", cname = "";
        bool On(string k) { return Settings.GetBool(Id + ".d_" + k, true); }
        int DStyle { get { return Settings.GetInt(Id + ".d_style", 0); } }
        int Layout { get { return Settings.GetInt(Id + ".d_layout", 0); } }
        public DashWidget(string id) : base(id, "dash", "CPU + RAM + GPU", 380, 150, 1000)
        {
            long i, k, u; Native.GetSystemTimes(out i, out k, out u); pi = i; pk = k; pu = u;
            try { using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-b80c-00805f9fa9cb}\0000")) if (rk != null) gname = Convert.ToString(rk.GetValue("DriverDesc", "")); } catch { }
            try { cname = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", ""); cname = cname == null ? "" : cname.Trim(); } catch { }
            OnTick();
        }
        void Chk(Action<string, Control> add, string text, string key, bool def)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.Checked = Settings.GetBool(Id + key, def); c.Width = 280;
            c.CheckedChanged += delegate { Settings.Set(Id + key, c.Checked); Settings.Save(); Invalidate(); }; add("", c);
        }
        public override void BuildExtra(Action<string, Control> add)
        {
            Chk(add, "Show CPU", ".d_cpu", true); Chk(add, "Show RAM", ".d_ram", true); Chk(add, "Show GPU", ".d_gpu", true);
            ComboBox cs = new ComboBox(); cs.DropDownStyle = ComboBoxStyle.DropDownList; cs.Width = 220;
            cs.Items.AddRange(new object[] { "Bars + numbers", "Rings", "Live graphs" }); cs.SelectedIndex = Math.Min(2, DStyle);
            cs.SelectedIndexChanged += delegate { Settings.Set(Id + ".d_style", cs.SelectedIndex); Settings.Save(); Invalidate(); }; add("Style", cs);
            ComboBox ly = new ComboBox(); ly.DropDownStyle = ComboBoxStyle.DropDownList; ly.Width = 220;
            ly.Items.AddRange(new object[] { "Auto", "Side by side", "Stacked" }); ly.SelectedIndex = Math.Min(2, Layout);
            ly.SelectedIndexChanged += delegate { Settings.Set(Id + ".d_layout", ly.SelectedIndex); Settings.Save(); Invalidate(); }; add("Layout", ly);
            Chk(add, "Show mini graph (Bars style)", ".d_graph", true);
            Chk(add, "Show details (GB, GPU name)", ".d_detail", true);
            Chk(add, "Separator lines between parts", ".d_div", true);
            add("CPU color", OvPicker("cpu", Color.FromArgb(80, 160, 255)));
            add("RAM color", OvPicker("ram", Color.FromArgb(70, 210, 140)));
            add("GPU color", OvPicker("gpu", Color.FromArgb(255, 170, 60)));
            add("Part background", OvPicker("cellbg", Color.FromArgb(40, 40, 50)));
            add("Separator color", OvPicker("divider", Color.FromArgb(120, 150, 150, 150)));
        }
        protected override void OnTick()
        {
            long i, k, u;
            if (Native.GetSystemTimes(out i, out k, out u))
            {
                long idle = i - pi, total = (k - pk) + (u - pu);
                if (total > 0) cpu = 1.0 - (double)idle / total;
                pi = i; pk = k; pu = u;
            }
            Native.MEMSTATUS m = new Native.MEMSTATUS(); m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMSTATUS));
            if (Native.GlobalMemoryStatusEx(ref m)) { totGb = m.ullTotalPhys / 1073741824.0; usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0; ram = usedGb / totGb; }
            if (On("gpu"))
            {
                try
                {
                    if (tickN++ % 10 == 0)
                    {
                        pcs.Clear();
                        PerformanceCounterCategory cat = new PerformanceCounterCategory("GPU Engine");
                        foreach (string inst in cat.GetInstanceNames()) if (inst.EndsWith("engtype_3D")) { try { pcs.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst)); } catch { } }
                        gfail = pcs.Count == 0;
                    }
                    double sum = 0; foreach (PerformanceCounter p in pcs) { try { sum += p.NextValue(); } catch { } }
                    gpu = Math.Min(100, sum);
                }
                catch { gfail = true; }
            }
            Array.Copy(hc, 1, hc, 0, hc.Length - 1); hc[hc.Length - 1] = cpu * 100;
            Array.Copy(hr, 1, hr, 0, hr.Length - 1); hr[hr.Length - 1] = ram * 100;
            Array.Copy(hg, 1, hg, 0, hg.Length - 1); hg[hg.Length - 1] = gpu;
        }
        protected override void Draw(Graphics g)
        {
            List<string> names = new List<string>(); List<double> vals = new List<double>(); List<Color> cols = new List<Color>(); List<double[]> hs = new List<double[]>(); List<string> det = new List<string>();
            if (On("cpu")) { names.Add("CPU"); vals.Add(cpu); cols.Add(Ov("cpu", Color.FromArgb(80, 160, 255))); hs.Add(hc); det.Add(cname); }
            if (On("ram")) { names.Add("RAM"); vals.Add(ram); cols.Add(Ov("ram", Color.FromArgb(70, 210, 140))); hs.Add(hr); det.Add(usedGb.ToString("0.0") + " / " + totGb.ToString("0.0") + " GB"); }
            if (On("gpu")) { names.Add("GPU"); vals.Add(gfail ? 0 : gpu / 100.0); cols.Add(Ov("gpu", Color.FromArgb(255, 170, 60))); hs.Add(hg); det.Add(gfail ? "not available" : gname); }
            int n = names.Count; float W = LW, H = LH;
            if (n == 0) { Txt(g, "Turn on CPU / RAM / GPU in Customize", 12, false, DimB, 4, 4); return; }
            bool horiz = Layout == 1 || (Layout == 0 && W >= H * 1.3f);
            float cw = horiz ? W / n : W, ch = horiz ? H : H / n;
            StringFormat far = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            StringFormat near = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            bool hasBg = St.Ov.ContainsKey("cellbg");
            for (int i = 0; i < n; i++)
            {
                float x = horiz ? i * cw : 0, y = horiz ? 0 : i * ch;
                RectangleF r = new RectangleF(x + 4, y + 3, cw - 8, ch - 6);
                if (hasBg) using (SolidBrush cb = new SolidBrush(Ov("cellbg", Color.Gray))) g.FillRectangle(cb, r);
                if (i > 0 && On("div"))
                    using (Pen dp = new Pen(Ov("divider", Color.FromArgb(120, 150, 150, 150)), 1))
                    { if (horiz) g.DrawLine(dp, x, y + 6, x, y + ch - 6); else g.DrawLine(dp, x + 6, y, x + cw - 6, y); }
                double f = Math.Max(0, Math.Min(1, vals[i])); string pct = (f * 100).ToString("0") + "%";
                Color mc = cols[i];
                if (r.Width < 20 || r.Height < 20) continue;
                int st = DStyle;
                using (SolidBrush tb = new SolidBrush(Ov("title", mc)))
                {
                    if (st == 1)
                    {
                        float R = Math.Min(r.Width / 2f, r.Height / 2f) - 6; if (R < 10) continue; float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2; float lw = Math.Max(4, R * 0.18f);
                        ArcC(g, new RectangleF(cx - R, cy - R, 2 * R, 2 * R), f, lw, f > 0.9 ? Ov("warn", Color.FromArgb(255, 90, 90)) : mc);
                        Fit(g, pct, 60, true, FgB, new RectangleF(cx - R * 0.7f, cy - R * 0.5f, R * 1.4f, R * 0.6f));
                        if (St.ShowTitle) Fit(g, names[i], 30, true, tb, new RectangleF(cx - R * 0.7f, cy + R * 0.1f, R * 1.4f, R * 0.4f));
                    }
                    else if (st == 2)
                    {
                        SparkC(g, hs[i], r.X + 2, r.Y + 22, r.Width - 4, Math.Max(6, r.Height - 26), 0, 100, mc);
                        if (St.ShowTitle) g.DrawString(names[i], F(13, true), tb, r.X + 2, r.Y);
                        g.DrawString(pct, F(16, true), FgB, new RectangleF(r.X, r.Y - 1, r.Width - 2, 22), far);
                    }
                    else
                    {
                        float yy = r.Y;
                        if (St.ShowTitle) { g.DrawString(names[i], F(13, true), tb, r.X + 2, yy); yy += 15; }
                        float vs = Math.Max(14, Math.Min(30, Math.Min(r.Height * 0.3f, r.Width * 0.24f)));
                        g.DrawString(pct, F(vs, true), FgB, r.X + 2, yy); yy += vs + 4;
                        if (On("detail") && r.Height > 70 && det[i] != "") { g.DrawString(det[i], F(11, false), DimB, new RectangleF(r.X + 2, yy, r.Width - 4, 15), near); yy += 15; }
                        float by = r.Bottom - 9;
                        if (On("graph") && by - yy > 16) SparkC(g, hs[i], r.X + 2, yy + 2, r.Width - 4, by - yy - 6, 0, 100, mc);
                        BarC(g, r.X + 2, by, r.Width - 4, 8, f, mc);
                    }
                }
            }
        }
    }

    // ================= System info =================
    class InfoWidget : Widget
    {
        string os = "", ip = "", cpuName = ""; TimeSpan up;
        public InfoWidget(string id) : base(id, "sysinfo", "System info", 260, 170, 5000)
        {
            try { os = Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "Windows")) + " " + Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "")); } catch { }
            try { cpuName = Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "")).Trim(); } catch { }
            OnTick();
        }
        protected override void OnTick()
        {
            up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            try
            {
                foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation u in n.GetIPProperties().UnicastAddresses)
                        if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = u.Address.ToString() + " (" + n.Name + ")"; return; }
                }
                ip = "offline";
            }
            catch { }
        }
        protected override void Draw(Graphics g)
        {
            Ttl(g, "SYSTEM", 2, 0);
            Native.MEMSTATUS m = new Native.MEMSTATUS(); m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMSTATUS)); Native.GlobalMemoryStatusEx(ref m);
            string[] k = { "PC", "User", "OS", "CPU", "RAM", "Screen", "IP", "Uptime" };
            string[] v = { Environment.MachineName, Environment.UserName, os, cpuName + " (" + Environment.ProcessorCount + " threads)", Gb(m.ullTotalPhys) + " GB", Screen.PrimaryScreen.Bounds.Width + " x " + Screen.PrimaryScreen.Bounds.Height, ip, (int)up.TotalDays + "d " + up.Hours + "h " + up.Minutes + "m" };
            float rh = Math.Min(20, (LH - 18) / k.Length); float y = 18;
            for (int i = 0; i < k.Length; i++)
            {
                Txt(g, k[i], 11, true, DimB, 2, y + 1);
                g.DrawString(v[i], F(11, false), FgB, new RectangleF(52, y + 1, LW - 54, rh), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                y += rh;
            }
        }
    }

    // ================= Ping =================
    class PingWidget : Widget
    {
        System.Net.NetworkInformation.Ping ping = new System.Net.NetworkInformation.Ping(); bool pending; double last = -1; double[] hist = new double[60]; int sent, lost;
        string Host { get { return Settings.Get(Id + ".host", "1.1.1.1"); } }
        public PingWidget(string id) : base(id, "ping", "Ping", 240, 120, 2000)
        {
            ping.PingCompleted += delegate (object o, System.Net.NetworkInformation.PingCompletedEventArgs e)
            {
                pending = false; sent++;
                if (!e.Cancelled && e.Error == null && e.Reply != null && e.Reply.Status == IPStatus.Success) last = e.Reply.RoundtripTime; else { last = -1; lost++; }
                Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = last < 0 ? 0 : last;
            };
        }
        public override void BuildExtra(Action<string, Control> add)
        {
            TextBox tb = new TextBox(); tb.Width = 220; tb.Text = Host;
            tb.TextChanged += delegate { Settings.Set(Id + ".host", tb.Text.Trim()); Settings.Save(); sent = 0; lost = 0; };
            add("Host to ping", tb);
        }
        protected override void OnTick()
        {
            if (pending) return;
            try { pending = true; ping.SendAsync(Host, 1500, null); } catch { pending = false; }
        }
        protected override void Draw(Graphics g)
        {
            Ttl(g, "PING  " + Host, 2, 0);
            Txt(g, last < 0 ? (sent == 0 ? "..." : "timeout") : last.ToString("0") + " ms", 28, true, last < 0 && sent > 0 ? new SolidBrush(Color.FromArgb(255, 90, 90)) : FgB, 2, 14);
            Txt(g, "loss " + (sent == 0 ? 0 : lost * 100 / sent) + "%", 12, false, DimB, LW - 70, 24);
            float gy = 54; Spark(g, hist, 2, gy, LW - 4, LH - gy - 2, 0, 50, St.Accent);
        }
    }

    // ================= Customize dialog =================
    class CustomizeForm : Form
    {
        static Dictionary<string, CustomizeForm> open = new Dictionary<string, CustomizeForm>();
        Widget w; TableLayoutPanel tl;
        public static void Open(Widget w)
        {
            CustomizeForm f;
            if (open.TryGetValue(w.Id, out f) && !f.IsDisposed) { f.Activate(); return; }
            f = new CustomizeForm(w); open[w.Id] = f; f.Show();
        }
        void Add(string label, Control c)
        {
            int r = tl.RowCount++; tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = new Label(); l.Text = label; l.AutoSize = true; l.Margin = new Padding(0, 6, 8, 0); tl.Controls.Add(l, 0, r);
            c.Margin = new Padding(0, 3, 0, 3); tl.Controls.Add(c, 1, r);
        }
        void Section(string title)
        {
            Label l = new Label(); l.Text = title; l.Font = new Font(Font, FontStyle.Bold); l.AutoSize = true; l.Margin = new Padding(0, 14, 0, 2);
            int r = tl.RowCount++; tl.RowStyles.Add(new RowStyle(SizeType.AutoSize)); tl.Controls.Add(l, 0, r); tl.SetColumnSpan(l, 2);
        }
        TrackBar Track(int min, int max, int val, Action<int> on)
        {
            TrackBar t = new TrackBar(); t.Minimum = min; t.Maximum = max; t.Value = Math.Max(min, Math.Min(max, val)); t.Width = 230; t.TickStyle = TickStyle.None; t.AutoSize = false; t.Height = 26;
            t.ValueChanged += delegate { on(t.Value); w.Restyle(); };
            return t;
        }
        NumericUpDown Num(int min, int max, int val, Action<int> on)
        {
            NumericUpDown n = new NumericUpDown(); n.Minimum = min; n.Maximum = max; n.Value = Math.Max(min, Math.Min(max, val)); n.Width = 90;
            n.ValueChanged += delegate { on((int)n.Value); w.Restyle(); };
            return n;
        }
        Button ColorBtn(Color init, Action<Color> on)
        {
            Button b = new Button(); b.Width = 110; b.Height = 24; b.BackColor = Color.FromArgb(255, init); b.FlatStyle = FlatStyle.Flat; b.Text = "";
            b.Click += delegate
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = b.BackColor; cd.FullOpen = true;
                    if (cd.ShowDialog(this) == DialogResult.OK) { b.BackColor = cd.Color; on(cd.Color); w.Restyle(); }
                }
            };
            return b;
        }
        Button ColorBtnA(Color init, Action<Color> on) { return ColorBtn(init, delegate (Color c) { on(Color.FromArgb(init.A, c)); }); }

        public CustomizeForm(Widget widget)
        {
            w = widget; Text = "Customize - " + w.Title + " (" + w.Id + ")";
            Width = 470; Height = 720; StartPosition = FormStartPosition.CenterScreen; AutoScroll = true; ShowInTaskbar = true;
            tl = new TableLayoutPanel(); tl.AutoSize = true; tl.ColumnCount = 2; tl.Padding = new Padding(12); tl.Dock = DockStyle.Top;
            Controls.Add(tl);
            Sty s = w.St;

            Section("Theme");
            ComboBox th = new ComboBox(); th.DropDownStyle = ComboBoxStyle.DropDownList; th.Width = 220; th.Items.AddRange(Sty.ThemeNames);
            th.SelectedIndexChanged += delegate { s.ApplyTheme((string)th.SelectedItem); w.Restyle(); Close(); Open(w); };
            Add("Preset (resets style)", th);
            Button all = new Button(); all.Text = "Copy this look to ALL widgets"; all.Width = 220;
            all.Click += delegate { foreach (Widget o in Program.Widgets) if (o != w) { o.St.CopyLookFrom(s); o.Restyle(); } };
            Add("", all);

            w.BuildExtra(Add);

            Section("Shape and size");
            ComboBox sh = new ComboBox(); sh.DropDownStyle = ComboBoxStyle.DropDownList; sh.Width = 220; sh.Items.AddRange(Sty.ShapeNames); sh.SelectedIndex = s.Shape;
            sh.SelectedIndexChanged += delegate { s.Shape = sh.SelectedIndex; w.Restyle(); };
            Add("Shape", sh);
            NumericUpDown nw = Num(60, 3000, s.W, delegate (int v) { s.W = v; });
            NumericUpDown nh = Num(40, 3000, s.H, delegate (int v) { s.H = v; });
            Add("Width (exact px)", nw);
            Add("Height (exact px)", nh);
            ComboBox pre = new ComboBox(); pre.DropDownStyle = ComboBoxStyle.DropDownList; pre.Width = 220;
            string[] pn = { "Small 160x90", "Medium 240x120", "Large 320x160", "Wide 400x120", "Tall 160x260", "Square 200x200", "Big square 320x320", "Banner 520x90" };
            int[][] pv = { new[] { 160, 90 }, new[] { 240, 120 }, new[] { 320, 160 }, new[] { 400, 120 }, new[] { 160, 260 }, new[] { 200, 200 }, new[] { 320, 320 }, new[] { 520, 90 } };
            pre.Items.AddRange(pn);
            pre.SelectedIndexChanged += delegate { nw.Value = pv[pre.SelectedIndex][0]; nh.Value = pv[pre.SelectedIndex][1]; };
            Add("Size preset", pre);
            Button sameW = new Button(); sameW.Text = "Make ALL widgets this width"; sameW.Width = 220;
            sameW.Click += delegate { foreach (Widget o in Program.Widgets) if (o != w) { o.St.W = s.W; o.Restyle(); } };
            Add("", sameW);
            Button sameS = new Button(); sameS.Text = "Make ALL widgets this exact size"; sameS.Width = 220;
            sameS.Click += delegate { foreach (Widget o in Program.Widgets) if (o != w) { o.St.W = s.W; o.St.H = s.H; o.Restyle(); } };
            Add("", sameS);
            Label hint = new Label(); hint.Text = "Tip: you can also drag any widget edge or corner to resize it."; hint.AutoSize = true; hint.ForeColor = SystemColors.GrayText; Add("", hint);
            Add("Position X (px)", Num(-5000, 10000, w.Left, delegate (int v) { w.Left = v; w.SavePos(); }));
            Add("Position Y (px)", Num(-5000, 10000, w.Top, delegate (int v) { w.Top = v; w.SavePos(); }));
            Add("Content size % (text and graphics)", Num(30, 500, s.FontPct, delegate (int v) { s.FontPct = v; }));
            Add("Corner radius (px)", Num(0, 300, s.Radius, delegate (int v) { s.Radius = v; }));
            Add("Inner padding", Track(0, 80, s.Pad, delegate (int v) { s.Pad = v; }));

            Section("Background and glass");
            ComboBox md = new ComboBox(); md.DropDownStyle = ComboBoxStyle.DropDownList; md.Width = 220; md.Items.AddRange(Sty.ModeNames); md.SelectedIndex = s.Mode;
            md.SelectedIndexChanged += delegate { s.Mode = md.SelectedIndex; w.Restyle(); };
            Add("Background type", md);
            Add("Background color", ColorBtn(s.Bg, delegate (Color c) { s.Bg = c; }));
            Add("Tint strength (glass modes)", Track(0, 255, s.BgAlpha, delegate (int v) { s.BgAlpha = v; }));
            Add("Window opacity % (Solid only)", Track(20, 100, (int)Math.Round(s.Opacity * 100), delegate (int v) { s.Opacity = v / 100.0; }));

            Section("Colors");
            Add("Text color", ColorBtn(s.Fg, delegate (Color c) { s.Fg = c; }));
            Add("Secondary text color", ColorBtn(s.Dim, delegate (Color c) { s.Dim = c; }));
            Add("Accent color", ColorBtn(s.Accent, delegate (Color c) { s.Accent = c; }));
            Section("Color of each detail separately");
            Label cnote = new Label(); cnote.Text = "Each one can have its own color. Reset puts it back to follow the theme."; cnote.AutoSize = true; cnote.ForeColor = SystemColors.GrayText; Add("", cnote);
            Add("Main text / big numbers", w.OvPicker("value", s.Fg));
            Add("Labels / small text", w.OvPicker("label", s.Dim));
            Add("Widget title", w.OvPicker("title", s.Dim));
            Add("Highlights (seconds, minutes...)", w.OvPicker("accent2", s.Accent));
            Add("Bar fill", w.OvPicker("barfill", s.Accent));
            Add("Bar / ring track", w.OvPicker("barbg", s.Dim));
            Add("Warning (above 90%)", w.OvPicker("warn", Color.FromArgb(255, 90, 90)));
            Add("Graph lines", w.OvPicker("graph", s.Accent));
            Add("Ring / arc", w.OvPicker("ring", s.Accent));
            if (w.Type == "clock")
            {
                Add("Clock: hour hand", w.OvPicker("hourhand", s.Fg));
                Add("Clock: minute hand", w.OvPicker("minhand", s.Fg));
                Add("Clock: second hand", w.OvPicker("sechand", s.Accent));
                Add("Clock: tick marks", w.OvPicker("ticks", s.Fg));
                Add("Clock: numbers", w.OvPicker("numerals", s.Fg));
                Add("Clock: face / ring", w.OvPicker("face", s.Dim));
                Add("Clock: center dot", w.OvPicker("dot", s.Accent));
            }
            Button rall = new Button(); rall.Text = "Reset all detail colors"; rall.Width = 220;
            rall.Click += delegate { s.Ov.Clear(); w.Restyle(); Close(); Open(w); }; Add("", rall);
            CheckBox bar = new CheckBox(); bar.Text = "Accent strip on top"; bar.Checked = s.Bar; bar.CheckedChanged += delegate { s.Bar = bar.Checked; w.Restyle(); };
            Add("", bar);
            ComboBox bp = new ComboBox(); bp.DropDownStyle = ComboBoxStyle.DropDownList; bp.Width = 220; bp.Items.AddRange(new object[] { "Top", "Bottom", "Left", "Right" }); bp.SelectedIndex = s.BarPos;
            bp.SelectedIndexChanged += delegate { s.BarPos = bp.SelectedIndex; w.Restyle(); }; Add("Accent strip position", bp);
            Add("Accent strip thickness", Num(1, 40, s.BarW, delegate (int v) { s.BarW = v; }));
            ComboBox gr = new ComboBox(); gr.DropDownStyle = ComboBoxStyle.DropDownList; gr.Width = 220; gr.Items.AddRange(new object[] { "None", "Vertical", "Horizontal", "Diagonal" }); gr.SelectedIndex = s.Grad;
            gr.SelectedIndexChanged += delegate { s.Grad = gr.SelectedIndex; w.Restyle(); }; Add("Background gradient (Solid type)", gr);
            Add("Gradient second color", ColorBtn(s.Bg2, delegate (Color c) { s.Bg2 = c; }));
            CheckBox ti = new CheckBox(); ti.Text = "Show widget title"; ti.Checked = s.ShowTitle; ti.CheckedChanged += delegate { s.ShowTitle = ti.Checked; w.Restyle(); }; Add("", ti);
            CheckBox bd = new CheckBox(); bd.Text = "Bold text everywhere"; bd.Checked = s.Bold; bd.CheckedChanged += delegate { s.Bold = bd.Checked; w.Restyle(); }; Add("", bd);

            Section("Border");
            Add("Border width", Track(0, 10, s.BorderW, delegate (int v) { s.BorderW = v; }));
            Add("Border color", ColorBtn(s.Border, delegate (Color c) { s.Border = Color.FromArgb(s.Border.A == 0 ? 255 : 255, c); }));

            Section("Text font");
            ComboBox fn = new ComboBox(); fn.DropDownStyle = ComboBoxStyle.DropDownList; fn.Width = 220;
            foreach (FontFamily ff in FontFamily.Families) fn.Items.Add(ff.Name);
            int fi = fn.Items.IndexOf(s.Font); if (fi >= 0) fn.SelectedIndex = fi;
            fn.SelectedIndexChanged += delegate { s.Font = (string)fn.SelectedItem; w.Restyle(); };
            Add("Font", fn);

            Section("Behavior");
            ComboBox rf = new ComboBox(); rf.DropDownStyle = ComboBoxStyle.DropDownList; rf.Width = 220;
            int[] rates = { 100, 250, 500, 1000, 2000, 5000, 10000, 60000 };
            foreach (int r in rates) rf.Items.Add((r / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " s");
            int ri = Array.IndexOf(rates, s.Interval); rf.SelectedIndex = ri < 0 ? 3 : ri;
            rf.SelectedIndexChanged += delegate { s.Interval = rates[rf.SelectedIndex]; w.Restyle(); };
            Add("Refresh rate (slower = lighter)", rf);
            CheckBox top = new CheckBox(); top.Text = "Always on top"; top.Checked = s.Top; top.CheckedChanged += delegate { s.Top = top.Checked; w.Restyle(); };
            Add("", top);
            CheckBox lk = new CheckBox(); lk.Text = "Lock position"; lk.Checked = s.Lock; lk.CheckedChanged += delegate { s.Lock = lk.Checked; w.Restyle(); };
            Add("", lk);
            CheckBox sw = new CheckBox(); sw.Text = "Start with Windows (launch at login)"; sw.Width = 280; sw.Checked = Program.IsAutostart();
            sw.CheckedChanged += delegate { Program.SetAutostart(sw.Checked); }; Add("", sw);
            Label note = new Label(); note.Text = "Glass/blur need Windows 10 1803+ or Windows 11."; note.AutoSize = true; note.ForeColor = SystemColors.GrayText;
            Add("", note);
        }
    }

    // ================= Program / tray =================
    static class Program
    {
        public static List<Widget> Widgets = new List<Widget>();
        static NotifyIcon tray;
        static readonly string[] Types = { "clock", "dash", "system", "cpu", "ram", "gpu", "diskio", "network", "ping", "disk", "battery", "calendar", "notes", "stopwatch", "sysinfo" };
        static readonly string[] TypeTitles = { "Clock", "CPU + RAM + GPU (all in one)", "System (CPU + RAM)", "CPU (detailed, per-core)", "RAM (detailed, top processes)", "GPU", "Disk activity", "Network", "Ping", "Disks (space)", "Battery", "Calendar", "Notes", "Stopwatch", "System info" };

        static Widget Make(string type, string id)
        {
            switch (type)
            {
                case "clock": return new ClockWidget(id);
                case "system": return new SystemWidget(id);
                case "dash": return new DashWidget(id);
                case "cpu": return new CpuWidget(id);
                case "ram": return new RamWidget(id);
                case "gpu": return new GpuWidget(id);
                case "diskio": return new DiskIoWidget(id);
                case "ping": return new PingWidget(id);
                case "sysinfo": return new InfoWidget(id);
                case "network": return new NetWidget(id);
                case "disk": return new DiskWidget(id);
                case "battery": return new BatteryWidget(id);
                case "calendar": return new CalendarWidget(id);
                case "notes": return new NotesWidget(id);
                default: return new StopwatchWidget(id);
            }
        }
        static string TypeOf(string id) { return id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'); }

        public static Widget AddWidget(string type, Widget copyFrom)
        {
            int n = 2; string id;
            do { id = type + n; n++; } while (Widgets.Exists(x => x.Id == id));
            string extra = Settings.Get("extra", ""); extra = extra == "" ? id : extra + "," + id; Settings.Set("extra", extra);
            if (copyFrom != null) { Sty tmp = new Sty(); tmp.CopyLookFrom(copyFrom.St); tmp.W = copyFrom.St.W; tmp.H = copyFrom.St.H; tmp.Interval = copyFrom.St.Interval; tmp.Save(id); }
            Settings.Set(id + ".visible", true);
            Settings.Set(id + ".x", (copyFrom != null ? copyFrom.Left : 60) + 30); Settings.Set(id + ".y", (copyFrom != null ? copyFrom.Top : 60) + 30);
            Settings.Save();
            Widget w = Make(type, id); Widgets.Add(w); w.Init(); w.Show(); RefreshTray();
            return w;
        }
        public static void AttachWidgetsMenu(ToolStripMenuItem root)
        {
            root.DropDownItems.Add("...");
            root.DropDownOpening += delegate
            {
                root.DropDownItems.Clear();
                foreach (Widget w in Widgets)
                {
                    Widget ww = w;
                    ToolStripMenuItem it = new ToolStripMenuItem(w.Title + (w.Id != w.Type ? " (" + w.Id + ")" : "")); it.Checked = w.Visible;
                    it.Click += delegate { ww.SetShown(!ww.Visible); RefreshTray(); };
                    root.DropDownItems.Add(it);
                }
                root.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem rs = new ToolStripMenuItem("Restore deleted widgets");
                string removed = "," + Settings.Get("removed", "") + ",";
                for (int i = 0; i < Types.Length; i++)
                {
                    string t = Types[i];
                    if (removed.Contains("," + t + ",") && !Widgets.Exists(x => x.Id == t)) rs.DropDownItems.Add(TypeTitles[i], null, delegate { RestoreWidget(t); });
                }
                if (rs.DropDownItems.Count == 0) { ToolStripMenuItem none = new ToolStripMenuItem("(nothing deleted)"); none.Enabled = false; rs.DropDownItems.Add(none); }
                else rs.DropDownItems.Add("Restore ALL", null, delegate { RestoreAll(); });
                root.DropDownItems.Add(rs);
                ToolStripMenuItem add = new ToolStripMenuItem("Add another widget");
                for (int i = 0; i < Types.Length; i++) { string t = Types[i]; add.DropDownItems.Add(TypeTitles[i], null, delegate { AddWidget(t, null); }); }
                root.DropDownItems.Add(add);
                ToolStripMenuItem auto = new ToolStripMenuItem("Start with Windows"); auto.Checked = IsAutostart();
                auto.Click += delegate { SetAutostart(!IsAutostart()); };
                root.DropDownItems.Add(auto);
            };
        }
        public static void RestoreWidget(string id)
        {
            string[] rm = Settings.Get("removed", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> keep = new List<string>(); foreach (string e in rm) if (e != id) keep.Add(e);
            Settings.Set("removed", string.Join(",", keep.ToArray()));
            if (!Widgets.Exists(x => x.Id == id) && Array.IndexOf(Types, id) >= 0)
            {
                Settings.Set(id + ".visible", true); Settings.Save();
                Widget w = Make(id, id); Widgets.Add(w); w.Init(); w.Show();
            }
            Settings.Save(); RefreshTray();
        }
        public static void RestoreAll()
        {
            foreach (string t in Types) { string removed = "," + Settings.Get("removed", "") + ","; if (removed.Contains("," + t + ",")) RestoreWidget(t); }
        }
        public static void RemoveWidget(Widget w)
        {
            if (Widgets.Count <= 1) return;
            Widgets.Remove(w); w.Hide(); w.Dispose();
            string[] ex = Settings.Get("extra", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> keep = new List<string>(); foreach (string e in ex) if (e != w.Id) keep.Add(e);
            Settings.Set("extra", string.Join(",", keep.ToArray()));
            Settings.Set("removed", Settings.Get("removed", "") + "," + w.Id);
            Settings.RemovePrefix(w.Id + "."); Settings.Save(); RefreshTray();
        }

        [STAThread]
        static void Main()
        {
            bool created;
            System.Threading.Mutex mx = new System.Threading.Mutex(true, "DesktopWidgets_single_instance", out created);
            if (!created) return;
            Crash.Install();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Settings.Load();
            if (Settings.GetInt("ver", 0) < 4)
            {
                Settings.Set("system.visible", false); Settings.Set("cpu.visible", false); Settings.Set("ram.visible", false);
                Settings.Set("ver", 4); Settings.Save();
            }
            if (!Settings.GetBool("autostart_init", false)) { SetAutostart(true); Settings.Set("autostart_init", true); Settings.Save(); }
            else { AutoDiag(); if (IsAutostart()) SetAutostart(true); }

            string removed = "," + Settings.Get("removed", "") + ",";
            for (int i = 0; i < Types.Length; i++) if (!removed.Contains("," + Types[i] + ",")) Widgets.Add(Make(Types[i], Types[i]));
            foreach (string id in Settings.Get("extra", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) Widgets.Add(Make(TypeOf(id), id));
            foreach (Widget w in Widgets)
            {
                w.Init();
                if (Settings.GetBool(w.Id + ".visible", w.Id == "clock" || w.Id == "dash" || w.Id == "network" || w.Id == "calendar")) w.Show();
            }

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
            tray.Text = "Desktop Widgets";
            tray.Visible = true;
            RefreshTray();
            tray.MouseClick += delegate (object so, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Left)
                    try { typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(tray, null); } catch { }
            };
            tray.DoubleClick += delegate { foreach (Widget w in Widgets) if (w.Visible) w.Activate(); };
            Application.Run();
            mx.ReleaseMutex();
        }

        public static void RefreshTray()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (Widget w in Widgets)
            {
                Widget ww = w;
                ToolStripMenuItem it = new ToolStripMenuItem(w.Title + (w.Id != w.Type ? " (" + w.Id + ")" : "")); it.Checked = w.Visible; it.CheckOnClick = true;
                it.Click += delegate { ww.SetShown(it.Checked); };
                it.DropDownItems.Add("Customize...", null, delegate { CustomizeForm.Open(ww); });
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem wmt = new ToolStripMenuItem("Restore deleted widgets"); AttachWidgetsMenu(wmt); m.Items.Add(wmt);
            ToolStripMenuItem add = new ToolStripMenuItem("Add widget");
            for (int i = 0; i < Types.Length; i++) { string t = Types[i]; add.DropDownItems.Add(TypeTitles[i], null, delegate { AddWidget(t, null); }); }
            m.Items.Add(add);
            ToolStripMenuItem auto = new ToolStripMenuItem("Start with Windows"); auto.CheckOnClick = true; auto.Checked = IsAutostart();
            auto.Click += delegate { SetAutostart(auto.Checked); };
            m.Items.Add(auto);
            m.Items.Add("Reset positions", null, delegate
            {
                Settings.ClearPositions(); Settings.Save(); int i = 0;
                foreach (Widget w in Widgets) { w.Location = new Point(40 + (i % 4) * 260, 40 + (i / 4) * 200); w.SavePos(); i++; }
            });
            m.Items.Add("Exit", null, delegate { ExitAll(); });
            tray.ContextMenuStrip = m;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool IsAutostart()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("DesktopWidgets") != null; } catch { return false; }
        }
                static void Approve()
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")) k.SetValue("DesktopWidgets", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary); } catch { }
        }
        public static void AutoDiag()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopWidgets"); Directory.CreateDirectory(dir);
                object run = null, ap = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (k != null) run = k.GetValue("DesktopWidgets"); }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")) { if (k != null) ap = k.GetValue("DesktopWidgets"); }
                byte[] b = ap as byte[]; string st = b == null ? "none" : BitConverter.ToString(b);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  started; uptime " + ((uint)Environment.TickCount / 60000) + " min; exe=" + Application.ExecutablePath + "; Run=" + (run == null ? "missing" : "present") + "; StartupApproved=" + st + (b != null && b.Length > 0 && (b[0] & 1) == 1 ? " (DISABLED by Windows)" : "") + "\r\n";
                string f = Path.Combine(dir, "startup.log"); try { if (File.Exists(f) && new FileInfo(f).Length > 50000) File.Delete(f); } catch { }
                File.AppendAllText(f, line);
            }
            catch { }
        }
        public static void SetAutostart(bool on) { SetAutostartRaw(on); if (on) Approve(); }
        static void SetAutostartRaw(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (on) k.SetValue("DesktopWidgets", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("DesktopWidgets", false);
                }
            }
            catch { }
        }

        public static void ExitAll()
        {
            foreach (Widget w in Widgets) w.SavePos();
            Settings.Save();
            tray.Visible = false;
            Application.Exit();
        }
    }
}
