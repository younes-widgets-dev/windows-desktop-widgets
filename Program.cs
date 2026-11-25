using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopWidgets
{
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
        public bool Top, Lock, Bar = true;

        public static readonly string[] ShapeNames = { "Rounded", "Rectangle", "Pill", "Ellipse / Circle", "Hexagon", "Octagon", "Diamond", "Cut corners", "Leaf", "Tab", "Squircle", "Parallelogram" };
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
        }
        public void Save(string id)
        {
            Settings.Set(id + ".shape", Shape); Settings.Set(id + ".radius", Radius); Settings.Set(id + ".mode", Mode); Settings.Set(id + ".bgalpha", BgAlpha);
            Settings.Set(id + ".borderw", BorderW); Settings.Set(id + ".pad", Pad); Settings.Set(id + ".fontpct", FontPct);
            Settings.Set(id + ".w", W); Settings.Set(id + ".h", H); Settings.Set(id + ".interval", Interval);
            Settings.Set(id + ".bg", C(Bg)); Settings.Set(id + ".fg", C(Fg)); Settings.Set(id + ".dim", C(Dim)); Settings.Set(id + ".accent", C(Accent)); Settings.Set(id + ".border", C(Border));
            Settings.Set(id + ".font", Font); Settings.Set(id + ".opacity100", (int)Math.Round(Opacity * 100));
            Settings.Set(id + ".top", Top); Settings.Set(id + ".locked", Lock); Settings.Set(id + ".bar", Bar);
            Settings.Save();
        }
        public void CopyLookFrom(Sty o)
        {
            Shape = o.Shape; Radius = o.Radius; Mode = o.Mode; BgAlpha = o.BgAlpha; BorderW = o.BorderW; Pad = o.Pad; FontPct = o.FontPct;
            Bg = o.Bg; Fg = o.Fg; Dim = o.Dim; Accent = o.Accent; Border = o.Border; Font = o.Font; Opacity = o.Opacity; Bar = o.Bar;
        }

        public static readonly string[] ThemeNames = { "Dark", "Light", "Acrylic dark", "Acrylic light", "Blur glass", "Clear (text only)", "Neon", "Terminal", "Sunset", "Pastel", "Midnight", "Paper" };
        public void ApplyTheme(string name)
        {
            Mode = 0; BgAlpha = 160; Shape = 0; Radius = 14; BorderW = 0; Bar = true; Opacity = 0.95; Pad = 0;
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

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public virtual void ApplyStyle()
        {
            if (FgB != null) { FgB.Dispose(); DimB.Dispose(); AccB.Dispose(); }
            FgB = new SolidBrush(St.Fg); DimB = new SolidBrush(St.Dim); AccB = new SolidBrush(St.Accent);
            foreach (Font f in fonts.Values) f.Dispose();
            fonts.Clear();
            ClientSize = new Size(Math.Max(60, St.W), Math.Max(40, St.H));
            using (GraphicsPath p = Shapes.Make(St.Shape, St.Radius, new RectangleF(0, 0, Width, Height)))
                Region = new Region(p);
            bool accent = St.Mode > 0;
            BackColor = accent ? Color.Black : St.Bg;
            TopMost = St.Top;
            if (!accent) Opacity = Math.Max(0.2, Math.Min(1.0, St.Opacity)); else if (Opacity < 1.0) Opacity = 1.0;
            if (IsHandleCreated) ApplyAccent();
            Tick.Interval = Math.Max(50, St.Interval);
            LW = (float)((Width - 2 * St.Pad) / Fs); LH = (float)((Height - 2 * St.Pad) / Fs);
            Invalidate();
        }
        void ApplyAccent()
        {
            if (St.Mode == 0) Native.SetAccent(Handle, 0, Color.Black);
            else if (St.Mode == 1) Native.SetAccent(Handle, 2, Color.FromArgb(St.BgAlpha, St.Bg));
            else if (St.Mode == 2) Native.SetAccent(Handle, 3, Color.FromArgb(St.BgAlpha, St.Bg));
            else Native.SetAccent(Handle, 4, Color.FromArgb(St.BgAlpha, St.Bg));
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyAccent(); }

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
                try { f = new Font(St.Font, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
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
        protected void Bar(Graphics g, float x, float y, float w, float h, double frac)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(70, St.Dim))) g.FillRectangle(bg, x, y, w, h);
            Color c = frac > 0.9 ? Color.FromArgb(255, 90, 90) : St.Accent;
            using (SolidBrush fg = new SolidBrush(c)) g.FillRectangle(fg, x, y, (float)(w * frac), h);
        }
        protected void Arc(Graphics g, RectangleF r, double frac, float width, Color c)
        {
            using (Pen bg = new Pen(Color.FromArgb(60, St.Dim), width)) g.DrawArc(bg, r, -90, 360);
            if (frac > 0.003) using (Pen p = new Pen(c, width)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, r, -90, (float)(360 * Math.Min(1, frac))); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(St.Mode > 0 ? Color.Black : St.Bg);
            using (GraphicsPath path = Shapes.Make(St.Shape, St.Radius, new RectangleF(0, 0, Width, Height)))
            {
                if (St.Bar)
                {
                    GraphicsState s = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    using (SolidBrush b = new SolidBrush(St.Accent)) g.FillRectangle(b, 0, 0, Width, Math.Max(2, (int)(3 * Fs)));
                    g.Restore(s);
                }
                if (St.BorderW > 0)
                {
                    float hw = St.BorderW / 2f;
                    using (GraphicsPath bp = Shapes.Make(St.Shape, St.Radius, new RectangleF(hw, hw, Width - St.BorderW, Height - St.BorderW)))
                    using (Pen pen = new Pen(St.Border, St.BorderW)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, bp); }
                }
            }
            g.TranslateTransform(St.Pad, St.Pad);
            g.ScaleTransform((float)Fs, (float)Fs);
            Draw(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { dragStart = Cursor.Position; formStart = Location; moved = false; dragging = !St.Lock; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Point c = Cursor.Position;
                if (Math.Abs(c.X - dragStart.X) + Math.Abs(c.Y - dragStart.Y) > 4) moved = true;
                if (dragging && moved) Location = new Point(formStart.X + c.X - dragStart.X, formStart.Y + c.Y - dragStart.Y);
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging && moved) SavePos();
            if (e.Button == MouseButtons.Left && !moved) OnWidgetClick(new PointF((float)((e.X - St.Pad) / Fs), (float)((e.Y - St.Pad) / Fs)));
            dragging = false;
            base.OnMouseUp(e);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); SendBack(); }
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
            ToolStripMenuItem lk = new ToolStripMenuItem("Lock position"); lk.CheckOnClick = true;
            lk.Click += delegate { St.Lock = lk.Checked; Restyle(); };
            ToolStripMenuItem top = new ToolStripMenuItem("Always on top"); top.CheckOnClick = true;
            top.Click += delegate { St.Top = top.Checked; Restyle(); };
            m.Opening += delegate { lk.Checked = St.Lock; top.Checked = St.Top; };
            m.Items.Add(top); m.Items.Add(lk);
            m.Items.Add("Duplicate this widget", null, delegate { Program.AddWidget(Type, this); });
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
            using (SolidBrush face = new SolidBrush(Color.FromArgb(40, St.Dim))) g.FillEllipse(face, cx - R, cy - R, 2 * R, 2 * R);
            using (Pen ring = new Pen(St.Dim, 2)) g.DrawEllipse(ring, cx - R, cy - R, 2 * R, 2 * R);
            for (int i = 0; i < 60; i++)
            {
                bool hr = i % 5 == 0; if (st == 4 && !hr) continue;
                double a = i * Math.PI / 30; float r1 = R - (hr ? R * 0.14f : R * 0.07f), r2 = R - 3;
                using (Pen p = new Pen(hr ? St.Fg : St.Dim, hr ? 2.5f : 1)) g.DrawLine(p, cx + (float)Math.Sin(a) * r1, cy - (float)Math.Cos(a) * r1, cx + (float)Math.Sin(a) * r2, cy - (float)Math.Cos(a) * r2);
            }
            if (st == 3 || st == 5)
            {
                string[] rom = { "XII", "I", "II", "III", "IIII", "V", "VI", "VII", "VIII", "IX", "X", "XI" };
                for (int i = 0; i < 12; i++)
                {
                    if (st == 3 && i % 3 != 0) continue;
                    double a = i * Math.PI / 6; float rr = R * 0.7f; string s = st == 3 ? (i == 0 ? "12" : i.ToString()) : rom[i];
                    float fs = R * (st == 3 ? 0.2f : 0.13f);
                    g.DrawString(s, F(fs, true), FgB, new RectangleF(cx + (float)Math.Sin(a) * rr - 20, cy - (float)Math.Cos(a) * rr - fs, 40, fs * 2), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
            }
            double sec = n.Second, min = n.Minute + sec / 60.0, hour = (n.Hour % 12) + min / 60.0;
            Hand(g, cx, cy, hour * 30, R * 0.5f, st == 4 ? 3f : 5f, St.Fg);
            Hand(g, cx, cy, min * 6, R * 0.76f, st == 4 ? 2f : 3.5f, St.Fg);
            if (Secs) Hand(g, cx, cy, sec * 6, R * 0.85f, 1.5f, St.Accent);
            using (SolidBrush b = new SolidBrush(St.Accent)) g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
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
            Txt(g, "NETWORK", 13, true, DimB, 2, 2);
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
            Txt(g, "BATTERY", 13, true, DimB, 2, 2);
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
        }
        protected override void OnRestyled() { DoLayout(); }
        public override void ApplyStyle() { base.ApplyStyle(); DoLayout(); }
        void DoLayout()
        {
            if (tb == null) return;
            float fs = (float)Fs;
            tb.Left = (int)(St.Pad + 8 * fs); tb.Top = (int)(St.Pad + 28 * fs);
            tb.Width = Math.Max(20, Width - 2 * St.Pad - (int)(16 * fs)); tb.Height = Math.Max(20, Height - 2 * St.Pad - (int)(36 * fs));
            tb.BackColor = St.Mode == 0 ? St.Bg : Color.FromArgb(255, Math.Min(255, St.Bg.R), Math.Min(255, St.Bg.G), Math.Min(255, St.Bg.B));
            tb.ForeColor = St.Fg;
            try { tb.Font = new Font(St.Font, 12f * fs, FontStyle.Regular, GraphicsUnit.Pixel); } catch { }
        }
        protected override void Draw(Graphics g) { Txt(g, "NOTES", 13, true, DimB, 6, 6); }
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
            Txt(g, "STOPWATCH", 13, true, DimB, 2, 2);
            Txt(g, sw.IsRunning ? "click: pause" : "click: start", 12, false, DimB, LW - 90, 4);
            Fit(g, string.Format("{0:00}:{1:00}.{2:0}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 100), 80, true, FgB, new RectangleF(0, 20, LW, LH - 36));
            Txt(g, "double-click: reset", 11, false, AccB, 2, LH - 16);
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
            Add("Corner radius", Track(0, 100, s.Radius, delegate (int v) { s.Radius = v; }));
            Add("Width (px)", Track(60, 900, s.W, delegate (int v) { s.W = v; }));
            Add("Height (px)", Track(40, 900, s.H, delegate (int v) { s.H = v; }));
            Add("Text / content size %", Track(40, 300, s.FontPct, delegate (int v) { s.FontPct = v; }));
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
            CheckBox bar = new CheckBox(); bar.Text = "Accent strip on top"; bar.Checked = s.Bar; bar.CheckedChanged += delegate { s.Bar = bar.Checked; w.Restyle(); };
            Add("", bar);

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
            Label note = new Label(); note.Text = "Glass/blur need Windows 10 1803+ or Windows 11."; note.AutoSize = true; note.ForeColor = SystemColors.GrayText;
            Add("", note);
        }
    }

    // ================= Program / tray =================
    static class Program
    {
        public static List<Widget> Widgets = new List<Widget>();
        static NotifyIcon tray;
        static readonly string[] Types = { "clock", "system", "network", "disk", "battery", "calendar", "notes", "stopwatch" };
        static readonly string[] TypeTitles = { "Clock", "System (CPU/RAM)", "Network", "Disks", "Battery", "Calendar", "Notes", "Stopwatch" };

        static Widget Make(string type, string id)
        {
            switch (type)
            {
                case "clock": return new ClockWidget(id);
                case "system": return new SystemWidget(id);
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Settings.Load();

            string removed = "," + Settings.Get("removed", "") + ",";
            for (int i = 0; i < Types.Length; i++) if (!removed.Contains("," + Types[i] + ",")) Widgets.Add(Make(Types[i], Types[i]));
            foreach (string id in Settings.Get("extra", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) Widgets.Add(Make(TypeOf(id), id));
            foreach (Widget w in Widgets)
            {
                w.Init();
                if (Settings.GetBool(w.Id + ".visible", w.Id == "clock" || w.Id == "system" || w.Id == "network" || w.Id == "calendar")) w.Show();
            }

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
            tray.Text = "Desktop Widgets";
            tray.Visible = true;
            RefreshTray();
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
        static bool IsAutostart()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("DesktopWidgets") != null; } catch { return false; }
        }
        static void SetAutostart(bool on)
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
