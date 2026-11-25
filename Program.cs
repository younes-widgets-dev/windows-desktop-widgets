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
    // ---------- Settings (plain text file, no dependencies) ----------
    static class Settings
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopWidgets");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");
        static Dictionary<string, string> d = new Dictionary<string, string>();

        public static string NotesPath { get { return Path.Combine(Dir, "notes.txt"); } }

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
        public static double GetDouble(string k, double def) { double v; return double.TryParse(Get(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        public static bool GetBool(string k, bool def) { string v = Get(k, ""); return v == "" ? def : v == "1"; }
        public static void Set(string k, string v) { d[k] = v; }
        public static void Set(string k, int v) { d[k] = v.ToString(CultureInfo.InvariantCulture); }
        public static void Set(string k, double v) { d[k] = v.ToString(CultureInfo.InvariantCulture); }
        public static void Set(string k, bool v) { d[k] = v ? "1" : "0"; }
        public static void ClearPositions()
        {
            List<string> del = new List<string>();
            foreach (string k in d.Keys) if (k.EndsWith(".x") || k.EndsWith(".y")) del.Add(k);
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
    }

    // ---------- Base widget ----------
    abstract class Widget : Form
    {
        public string Id;
        public string Title;
        protected int BaseW, BaseH;
        protected double Scale = 1.0;
        protected Color Accent = Color.FromArgb(80, 160, 255);
        protected bool Locked, OnTop;
        protected Timer Tick;
        bool dragging; Point dragStart;
        protected Font F(float size, FontStyle st) { return new Font("Segoe UI", size, st, GraphicsUnit.Pixel); }
        static readonly Color[] Palette = { Color.FromArgb(80, 160, 255), Color.FromArgb(70, 210, 140), Color.FromArgb(255, 170, 60), Color.FromArgb(255, 100, 150), Color.FromArgb(170, 120, 255), Color.FromArgb(235, 235, 235) };
        static readonly string[] PaletteNames = { "Blue", "Green", "Orange", "Pink", "Purple", "White" };

        protected Widget(string id, string title, int w, int h, int intervalMs)
        {
            Id = id; Title = title; BaseW = w; BaseH = h;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 26, 32);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            Scale = Settings.GetDouble(id + ".scale", 1.0);
            Opacity = Settings.GetDouble(id + ".opacity", 0.92);
            Accent = Palette[Math.Max(0, Math.Min(Palette.Length - 1, Settings.GetInt(id + ".color", 0)))];
            Locked = Settings.GetBool(id + ".locked", false);
            OnTop = Settings.GetBool(id + ".top", false);
            TopMost = OnTop;
            ApplySize();
            int idx = Program.Widgets.Count;
            Location = new Point(Settings.GetInt(id + ".x", 40 + (idx % 4) * 250), Settings.GetInt(id + ".y", 40 + (idx / 4) * 200));

            Tick = new Timer();
            Tick.Interval = Settings.GetInt(id + ".interval", intervalMs);
            Tick.Tick += delegate { if (Visible) { OnTick(); Invalidate(); } };
            Tick.Start();
            BuildMenu();
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected void ApplySize()
        {
            ClientSize = new Size((int)(BaseW * Scale), (int)(BaseH * Scale));
            using (GraphicsPath p = Round(new Rectangle(0, 0, Width, Height), (int)(14 * Scale)))
                Region = new Region(p);
        }
        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected virtual void OnTick() { }
        protected abstract void Draw(Graphics g);
        protected virtual void OnWidgetClick(Point p) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform((float)Scale, (float)Scale);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 26, 32))) g.FillRectangle(b, 0, 0, BaseW + 1, BaseH + 1);
            using (SolidBrush b = new SolidBrush(Accent)) g.FillRectangle(b, 0, 0, BaseW, 3);
            Draw(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !Locked) { dragging = true; dragStart = e.Location; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y);
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging) { dragging = false; SavePos(); }
            if (e.Button == MouseButtons.Left) OnWidgetClick(new Point((int)(e.X / Scale), (int)(e.Y / Scale)));
            base.OnMouseUp(e);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); SendBack(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); SendBack(); }
        void SendBack()
        {
            if (!OnTop) Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
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
            ToolStripMenuItem op = new ToolStripMenuItem("Opacity");
            foreach (int pct in new int[] { 30, 50, 70, 85, 100 })
            {
                int pp = pct;
                op.DropDownItems.Add(pp + "%", null, delegate { Opacity = pp / 100.0; Settings.Set(Id + ".opacity", Opacity); Settings.Save(); });
            }
            ToolStripMenuItem sz = new ToolStripMenuItem("Size");
            foreach (int pct in new int[] { 70, 85, 100, 125, 150, 200 })
            {
                int pp = pct;
                sz.DropDownItems.Add(pp + "%", null, delegate { Scale = pp / 100.0; Settings.Set(Id + ".scale", Scale); Settings.Save(); ApplySize(); Invalidate(); });
            }
            ToolStripMenuItem col = new ToolStripMenuItem("Accent color");
            for (int i = 0; i < Palette.Length; i++)
            {
                int ii = i;
                col.DropDownItems.Add(PaletteNames[i], null, delegate { Accent = Palette[ii]; Settings.Set(Id + ".color", ii); Settings.Save(); Invalidate(); });
            }
            ToolStripMenuItem rf = new ToolStripMenuItem("Refresh rate");
            foreach (int ms in new int[] { 500, 1000, 2000, 5000, 10000 })
            {
                int mm = ms;
                rf.DropDownItems.Add((mm / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s", null, delegate { Tick.Interval = mm; Settings.Set(Id + ".interval", mm); Settings.Save(); });
            }
            ToolStripMenuItem top = new ToolStripMenuItem("Always on top"); top.Checked = OnTop; top.CheckOnClick = true;
            top.Click += delegate { OnTop = top.Checked; TopMost = OnTop; Settings.Set(Id + ".top", OnTop); Settings.Save(); if (!OnTop) SendBack(); };
            ToolStripMenuItem lk = new ToolStripMenuItem("Lock position"); lk.Checked = Locked; lk.CheckOnClick = true;
            lk.Click += delegate { Locked = lk.Checked; Settings.Set(Id + ".locked", Locked); Settings.Save(); };
            m.Items.Add(op); m.Items.Add(sz); m.Items.Add(col); m.Items.Add(rf); m.Items.Add(top); m.Items.Add(lk);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Hide this widget", null, delegate { SetShown(false); Program.RefreshTray(); });
            m.Items.Add("Exit all widgets", null, delegate { Program.ExitAll(); });
            ContextMenuStrip = m;
        }

        // drawing helpers
        protected void Text(Graphics g, string s, Font f, Brush b, float x, float y) { g.DrawString(s, f, b, x, y); }
        protected void Bar(Graphics g, float x, float y, float w, float h, double frac)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(50, 54, 64))) g.FillRectangle(bg, x, y, w, h);
            Color c = frac > 0.9 ? Color.FromArgb(255, 90, 90) : Accent;
            using (SolidBrush fg = new SolidBrush(c)) g.FillRectangle(fg, x, y, (float)(w * frac), h);
        }
        protected static readonly Brush White = new SolidBrush(Color.FromArgb(240, 240, 245));
        protected static readonly Brush Dim = new SolidBrush(Color.FromArgb(150, 155, 170));
    }

    // ---------- Clock ----------
    class ClockWidget : Widget
    {
        Font big, mid, small;
        public ClockWidget() : base("clock", "Clock", 230, 110, 1000)
        { big = F(46, FontStyle.Bold); mid = F(18, FontStyle.Regular); small = F(14, FontStyle.Regular); }
        protected override void Draw(Graphics g)
        {
            DateTime n = DateTime.Now;
            Text(g, n.ToString("HH:mm"), big, White, 12, 10);
            using (SolidBrush b = new SolidBrush(Accent)) Text(g, n.ToString("ss"), mid, b, 160, 36);
            Text(g, n.ToString("dddd, d MMMM yyyy"), small, Dim, 14, 78);
        }
    }

    // ---------- System (CPU + RAM) ----------
    class SystemWidget : Widget
    {
        Font head, val, small;
        long pi, pk, pu; double cpu; double ram; double usedGb, totGb;
        public SystemWidget() : base("system", "System", 230, 130, 1000)
        { head = F(13, FontStyle.Bold); val = F(26, FontStyle.Bold); small = F(12, FontStyle.Regular); long i, k, u; Native.GetSystemTimes(out i, out k, out u); pi = i; pk = k; pu = u; OnTick(); }
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
            if (Native.GlobalMemoryStatusEx(ref m))
            {
                totGb = m.ullTotalPhys / 1073741824.0; usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0; ram = usedGb / totGb;
            }
        }
        protected override void Draw(Graphics g)
        {
            Text(g, "CPU", head, Dim, 14, 12); Text(g, (cpu * 100).ToString("0") + "%", val, White, 14, 26); Bar(g, 14, 62, 200, 8, cpu);
            Text(g, "RAM", head, Dim, 14, 76); Text(g, usedGb.ToString("0.0") + " / " + totGb.ToString("0.0") + " GB", small, White, 130, 78);
            Bar(g, 14, 98, 200, 8, ram);
            Text(g, (ram * 100).ToString("0") + "%", small, Dim, 14, 108);
        }
    }

    // ---------- Disks ----------
    class DiskWidget : Widget
    {
        Font head, small; List<DriveInfo> drives = new List<DriveInfo>();
        public DiskWidget() : base("disk", "Disks", 230, 40 + 4 * 38, 5000)
        {
            head = F(13, FontStyle.Bold); small = F(12, FontStyle.Regular);
            foreach (DriveInfo d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && drives.Count < 4) drives.Add(d);
            BaseH = 22 + Math.Max(1, drives.Count) * 38; ApplySize();
        }
        protected override void Draw(Graphics g)
        {
            int y = 12;
            foreach (DriveInfo d in drives)
            {
                try
                {
                    double tot = d.TotalSize, free = d.TotalFreeSpace, used = tot - free;
                    Text(g, d.Name.TrimEnd('\\'), head, White, 14, y);
                    Text(g, (free / 1073741824.0).ToString("0") + " GB free of " + (tot / 1073741824.0).ToString("0"), small, Dim, 60, y + 1);
                    Bar(g, 14, y + 20, 200, 7, used / tot);
                }
                catch { }
                y += 38;
            }
        }
    }

    // ---------- Network ----------
    class NetWidget : Widget
    {
        Font head, val, small; long prevR, prevS; DateTime prevT; double down, up; double[] hist = new double[50];
        public NetWidget() : base("network", "Network", 230, 120, 1000)
        { head = F(13, FontStyle.Bold); val = F(18, FontStyle.Bold); small = F(12, FontStyle.Regular); Read(out prevR, out prevS); prevT = DateTime.UtcNow; }
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
        static string Fmt(double bps)
        {
            if (bps > 1048576) return (bps / 1048576).ToString("0.0") + " MB/s";
            return (bps / 1024).ToString("0") + " KB/s";
        }
        protected override void OnTick()
        {
            long r, s; Read(out r, out s); DateTime t = DateTime.UtcNow; double dt = (t - prevT).TotalSeconds; if (dt <= 0) return;
            down = Math.Max(0, (r - prevR) / dt); up = Math.Max(0, (s - prevS) / dt); prevR = r; prevS = s; prevT = t;
            Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = down;
        }
        protected override void Draw(Graphics g)
        {
            Text(g, "NETWORK", head, Dim, 14, 12);
            using (SolidBrush b = new SolidBrush(Accent)) Text(g, "v " + Fmt(down), val, b, 14, 30);
            Text(g, "^ " + Fmt(up), small, White, 14, 58);
            double max = 1024; foreach (double h in hist) if (h > max) max = h;
            PointF[] pts = new PointF[hist.Length];
            for (int i = 0; i < hist.Length; i++) pts[i] = new PointF(14 + i * (200f / (hist.Length - 1)), (float)(108 - (hist[i] / max) * 30));
            using (Pen p = new Pen(Accent, 1.5f)) g.DrawLines(p, pts);
        }
    }

    // ---------- Battery ----------
    class BatteryWidget : Widget
    {
        Font head, val, small;
        public BatteryWidget() : base("battery", "Battery", 230, 90, 5000)
        { head = F(13, FontStyle.Bold); val = F(26, FontStyle.Bold); small = F(12, FontStyle.Regular); }
        protected override void Draw(Graphics g)
        {
            PowerStatus ps = SystemInformation.PowerStatus;
            double p = ps.BatteryLifePercent; if (p > 1) p = 1;
            bool none = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
            Text(g, "BATTERY", head, Dim, 14, 12);
            Text(g, none ? "AC power" : (p * 100).ToString("0") + "%", val, White, 14, 26);
            if (!none)
            {
                Bar(g, 14, 62, 200, 8, p);
                string s = ps.PowerLineStatus == PowerLineStatus.Online ? "Charging" : "On battery";
                if (ps.PowerLineStatus != PowerLineStatus.Online && ps.BatteryLifeRemaining > 0) s += "  " + (ps.BatteryLifeRemaining / 3600) + "h " + (ps.BatteryLifeRemaining % 3600 / 60) + "m left";
                Text(g, s, small, Dim, 120, 38);
            }
        }
    }

    // ---------- Calendar ----------
    class CalendarWidget : Widget
    {
        Font head, small, bold; DateTime shown = DateTime.Today;
        public CalendarWidget() : base("calendar", "Calendar", 240, 210, 60000)
        { head = F(15, FontStyle.Bold); small = F(12, FontStyle.Regular); bold = F(12, FontStyle.Bold); }
        protected override void OnWidgetClick(Point p)
        {
            if (p.Y < 34) { shown = p.X < 120 ? shown.AddMonths(-1) : shown.AddMonths(1); Invalidate(); }
        }
        protected override void Draw(Graphics g)
        {
            Text(g, shown.ToString("MMMM yyyy"), head, White, 14, 10);
            Text(g, "<   >", head, Dim, 188, 10);
            string[] dn = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
            for (int i = 0; i < 7; i++) Text(g, dn[i], small, Dim, 14 + i * 31, 40);
            DateTime first = new DateTime(shown.Year, shown.Month, 1);
            int off = ((int)first.DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(shown.Year, shown.Month);
            for (int d = 1; d <= days; d++)
            {
                int idx = off + d - 1; int cx = 14 + (idx % 7) * 31, cy = 60 + (idx / 7) * 24;
                bool today = shown.Year == DateTime.Today.Year && shown.Month == DateTime.Today.Month && d == DateTime.Today.Day;
                if (today) using (SolidBrush b = new SolidBrush(Accent)) g.FillEllipse(b, cx - 5, cy - 2, 26, 22);
                Text(g, d.ToString(), today ? bold : small, today ? Brushes.Black : White, cx + (d < 10 ? 4 : 0), cy);
            }
        }
    }

    // ---------- Notes ----------
    class NotesWidget : Widget
    {
        TextBox tb; Font head; Timer save;
        public NotesWidget() : base("notes", "Notes", 240, 180, 60000)
        {
            head = F(13, FontStyle.Bold);
            tb = new TextBox();
            tb.Multiline = true; tb.BorderStyle = BorderStyle.None; tb.BackColor = Color.FromArgb(24, 26, 32); tb.ForeColor = Color.FromArgb(240, 240, 245);
            tb.Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel); tb.ScrollBars = ScrollBars.None;
            try { if (File.Exists(Settings.NotesPath)) tb.Text = File.ReadAllText(Settings.NotesPath); } catch { }
            Controls.Add(tb);
            save = new Timer(); save.Interval = 800;
            save.Tick += delegate { save.Stop(); try { Directory.CreateDirectory(Path.GetDirectoryName(Settings.NotesPath)); File.WriteAllText(Settings.NotesPath, tb.Text); } catch { } };
            tb.TextChanged += delegate { save.Stop(); save.Start(); };
            DoLayout();
            SizeChanged += delegate { DoLayout(); };
        }
        void DoLayout()
        {
            tb.Left = (int)(14 * Scale); tb.Top = (int)(32 * Scale); tb.Width = (int)((BaseW - 28) * Scale); tb.Height = (int)((BaseH - 44) * Scale);
            tb.Font = new Font("Segoe UI", (float)(12 * Scale), FontStyle.Regular, GraphicsUnit.Pixel);
        }
        protected override void Draw(Graphics g) { Text(g, "NOTES", head, Dim, 14, 10); }
    }

    // ---------- Stopwatch ----------
    class StopwatchWidget : Widget
    {
        Font head, val, small; Stopwatch sw = new Stopwatch();
        public StopwatchWidget() : base("stopwatch", "Stopwatch", 230, 90, 100)
        { head = F(13, FontStyle.Bold); val = F(30, FontStyle.Bold); small = F(12, FontStyle.Regular); }
        protected override void OnWidgetClick(Point p) { if (sw.IsRunning) sw.Stop(); else sw.Start(); Invalidate(); }
        protected override void Draw(Graphics g)
        {
            TimeSpan t = sw.Elapsed;
            Text(g, "STOPWATCH", head, Dim, 14, 12);
            Text(g, string.Format("{0:00}:{1:00}.{2:0}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 100), val, White, 14, 30);
            Text(g, sw.IsRunning ? "click: pause" : "click: start", small, Dim, 140, 14);
            if (!sw.IsRunning && t.TotalMilliseconds > 0) { using (SolidBrush b = new SolidBrush(Accent)) Text(g, "double-click: reset", small, b, 14, 70); }
        }
        protected override void OnDoubleClick(EventArgs e) { sw.Reset(); Invalidate(); }
    }

    // ---------- Program / tray ----------
    static class Program
    {
        public static List<Widget> Widgets = new List<Widget>();
        static NotifyIcon tray;

        [STAThread]
        static void Main()
        {
            bool created;
            System.Threading.Mutex mx = new System.Threading.Mutex(true, "DesktopWidgets_single_instance", out created);
            if (!created) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Settings.Load();

            Widgets.Add(new ClockWidget());
            Widgets.Add(new SystemWidget());
            Widgets.Add(new NetWidget());
            Widgets.Add(new DiskWidget());
            Widgets.Add(new BatteryWidget());
            Widgets.Add(new CalendarWidget());
            Widgets.Add(new NotesWidget());
            Widgets.Add(new StopwatchWidget());

            foreach (Widget w in Widgets)
                if (Settings.GetBool(w.Id + ".visible", w.Id == "clock" || w.Id == "system" || w.Id == "network" || w.Id == "calendar")) w.Show();

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
                ToolStripMenuItem it = new ToolStripMenuItem(w.Title); it.Checked = w.Visible; it.CheckOnClick = true;
                it.Click += delegate { ww.SetShown(it.Checked); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem auto = new ToolStripMenuItem("Start with Windows"); auto.CheckOnClick = true; auto.Checked = IsAutostart();
            auto.Click += delegate { SetAutostart(auto.Checked); };
            m.Items.Add(auto);
            m.Items.Add("Reset positions", null, delegate
            {
                Settings.ClearPositions(); Settings.Save(); int i = 0;
                foreach (Widget w in Widgets) { w.Location = new Point(40 + (i % 4) * 250, 40 + (i / 4) * 200); w.SavePos(); i++; }
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
