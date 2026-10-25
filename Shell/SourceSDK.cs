// Source Model Kit - рабочая оболочка для утилит Source SDK и работы с моделями.
// Исходные утилиты НЕ изменяются: они запускаются как отдельные процессы,
// а их окна встраиваются в области оболочки через Win32 SetParent.
//
// Код написан на C# 5, чтобы собираться встроенным в Windows компилятором
// (C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe) без Visual Studio.
// Файлы: Native, Theme, Dialogs, Pane, Explorer, Views, Workspace, SourceSDK (этот).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SourceSDKHub
{
    internal class MainForm : Form, IShellHost
    {
        private readonly string baseDir;
        private readonly string cfgPath;
        private readonly Settings settings;
        private readonly List<ToolInfo> tools = new List<ToolInfo>();

        private readonly BarPanel top = new BarPanel();
        private Notifier notifier;
        private FlatButton btnBell;
        private FlatButton btnMin, btnMaxW, btnClose;
        private FlatButton btnLayout, btnMonitor, btnFull;
        private readonly Workspace ws;
        private readonly StatusLine statusLine = new StatusLine();
        private readonly Timer pollTimer = new Timer();
        private readonly Timer statusTimer = new Timer();
        private KeyHook hook;

        private bool full, wasMax;
        private long savedStyle;
        private Rectangle savedBounds;
        private readonly bool argFull;
        private string argModel, argSeq;
        private readonly int argScreen;
        private string lastExplorerPath;

        // темы / этапы / закрепления
        private StageBar stageBar;
        private FlatButton btnTheme;
        private readonly List<Stage> stages = new List<Stage>();
        private int stageIndex = -1;
        private readonly List<ToolPane> pool = new List<ToolPane>();     // запущенные программы между этапами
        private readonly List<ToolPane> toStart = new List<ToolPane>();
        private readonly List<string> pins = new List<string>();
        private readonly string stagesPath;
        public event EventHandler PinsChanged;
        public List<string> Pins { get { return pins; } }

        public List<ToolInfo> Tools { get { return tools; } }
        public string BaseDir { get { return baseDir; } }
        public Settings Config { get { return settings; } }

        public MainForm(string[] args)
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            cfgPath = System.IO.Path.Combine(baseDir, "SourceSDK.tools.ini");
            settings = new Settings(System.IO.Path.Combine(baseDir, "SourceSDK.settings.ini"));
            stagesPath = System.IO.Path.Combine(baseDir, "SourceSDK.workspaces.ini");
            Theme.Apply(settings.Get("theme", "dark"));
            foreach (string pn in settings.Get("pins", "").Split('|'))
                if (pn.Length > 0 && !pins.Contains(pn)) pins.Add(pn);

            foreach (string a in args)
            {
                if (a == "--fullscreen" || a == "-f") argFull = true;
                else if (a.StartsWith("--screen=")) int.TryParse(a.Substring(9), out argScreen);
                else if (a.StartsWith("--seq=")) argSeq = a.Substring(6);
                else if (a.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) argModel = a;
            }

            Text = "Source Model Kit";
            MinimumSize = new Size(900, 560);
            BackColor = Theme.Bg0;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            KeyPreview = false;
            Icon = MakeIcon();

            DiscoverBuiltinTools();
            LoadCustomTools();
            AutoDetectTools();

            // ---- верхняя панель ----
            top.Height = 44;
            top.Dock = DockStyle.Top;
            top.Role = 0;
            top.CaptionArea = true;
            top.Paint += PaintBrand;
            btnMin = WinBtn("\uE921", Loc.T("Свернуть"));
            btnMaxW = WinBtn("\uE922", Loc.T("Развернуть / восстановить"));
            btnClose = WinBtn("\uE8BB", Loc.T("Закрыть"));
            btnClose.HoverBg = Color.FromArgb(232, 17, 35); btnClose.HoverFg = Color.White;
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            btnMaxW.Click += delegate
            {
                if (full) SetFullscreen(false);
                else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            };
            btnClose.Click += delegate { Close(); };

            btnLayout = new FlatButton(Theme.GLayout, Loc.T("Окна"), Loc.T("Разделение и раскладка областей"));
            btnLayout.BaseRole = 0; btnLayout.Height = 30; btnLayout.Width += 4;
            btnLayout.Click += delegate { ShowLayoutMenu(); };
            btnMonitor = new FlatButton(Theme.GMonitor, null, Loc.T("На другой монитор (Ctrl+F11)"));
            btnMonitor.BaseRole = 0; btnMonitor.Height = 30; btnMonitor.Width = 34;
            btnMonitor.Click += delegate { NextMonitor(); };
            btnFull = new FlatButton(Theme.GFull, null, Loc.T("Полный экран (F11)"));
            btnFull.BaseRole = 0; btnFull.Height = 30; btnFull.Width = 34;
            btnFull.Click += delegate { SetFullscreen(!full); };
            btnTheme = new FlatButton(Theme.GPalette, null, Loc.T("Тема оформления"));
            btnTheme.BaseRole = 0; btnTheme.Height = 30; btnTheme.Width = 34;
            btnTheme.Click += delegate { ShowThemeMenu(); };

            notifier = new Notifier(this);
            notifier.ToastsEnabled = settings.GetBool("toasts", true);
            btnBell = new FlatButton(Theme.GBell, null, Loc.T("Уведомления"));
            btnBell.BaseRole = 0; btnBell.Height = 30; btnBell.Width = 34;
            btnBell.Click += delegate { ShowNotesMenu(); };
            notifier.Changed += delegate { btnBell.Badge = notifier.Unread; btnBell.Invalidate(); };

            stageBar = new StageBar();
            stageBar.Stages = stages;
            stageBar.StageSelected += delegate(int i) { ActivateStage(i); };
            stageBar.StageRename += delegate(int i) { RenameStage(i); };
            stageBar.StageContext += ShowStageMenu;
            stageBar.AddClicked += delegate { AddStage(); };

            top.Controls.AddRange(new Control[] { stageBar, btnMin, btnMaxW, btnClose, btnLayout, btnBell, btnTheme, btnMonitor, btnFull });
            top.Resize += delegate { LayoutTop(); };

            // ---- рабочая область ----
            ws = new Workspace(this);
            ws.Dock = DockStyle.Fill;

            statusLine.Dock = DockStyle.Bottom;
            statusLine.LeftText = Loc.T("Готово");
            LangSwitch langSw = new LangSwitch();
            langSw.Dock = DockStyle.Right;
            statusLine.Controls.Add(langSw);
            statusLine.RightPad = langSw.Width;
            langSw.Picked += OnLangPicked;

            Controls.Add(ws);
            Controls.Add(statusLine);
            Controls.Add(top);

            // ---- положение окна ----
            StartPosition = FormStartPosition.Manual;
            Rectangle def = Screen.PrimaryScreen.WorkingArea;
            Rectangle b0 = new Rectangle(def.X + (def.Width - 1500) / 2, def.Y + (def.Height - 900) / 2, 1500, 900);
            string sb = settings.Get("bounds", "");
            string[] pp = sb.Split(',');
            int x, y, w, h;
            if (pp.Length == 4 && int.TryParse(pp[0], out x) && int.TryParse(pp[1], out y) &&
                int.TryParse(pp[2], out w) && int.TryParse(pp[3], out h) && w > 400 && h > 300)
            {
                Rectangle r = new Rectangle(x, y, w, h);
                foreach (Screen s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) { b0 = r; break; }
            }
            Bounds = b0;
            savedBounds = b0;

            // ---- служебные таймеры ----
            pollTimer.Interval = 150;
            pollTimer.Tick += PollActiveArea;
            pollTimer.Start();
            statusTimer.Interval = 7000;
            statusTimer.Tick += delegate { statusTimer.Stop(); statusLine.LeftText = Loc.T("Готово"); };

            FormClosing += OnFormClosing;
            LocationChanged += delegate { UpdateInfo(); };
            Resize += delegate { UpdateInfo(); UpdateChrome(); };

            Theme.Reskin(this);
            InitStages();
        }

        // ---------------- иконка ----------------
        private static Icon MakeIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Theme.RoundRect(new Rectangle(1, 1, 30, 30), 7))
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.White, 2.4f))
                    {
                        g.DrawRectangle(pen, 7, 7, 18, 18);
                        g.DrawLine(pen, 16, 7, 16, 25);
                        g.DrawLine(pen, 16, 16, 25, 16);
                    }
                    return Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch { return null; }
        }

        // ---------------- обнаружение инструментов ----------------
        private void DiscoverBuiltinTools()
        {
            AddBuiltin("Crowbar",
                "Декомпиляция, компиляция и упаковка моделей, анимаций, GMA/VPK",
                @"Crowbar.exe", null, true);

            AddBuiltin("VMT Editor",
                "Редактор материалов VMT и конвертер текстур в VTF",
                @"VMT_Editor_1.3.12\VMT_Editor.exe", null, true);

            AddBuiltin("VTF Version Changer",
                "Смена версии VTF-файлов",
                @"VTFVersionChanger\VTFversionChanger.exe", null, false);

            AddBuiltin("BSPSrc",
                "Декомпилятор карт BSP в VMF (Java)",
                @"BSPsrc\bspsrc.jar", null, true);
        }

        private void AddBuiltin(string name, string desc, string rel, string args, bool stretch)
        {
            ToolInfo t = new ToolInfo();
            t.Name = name;
            t.Description = desc;
            t.Path = System.IO.Path.Combine(baseDir, rel);
            t.Args = args;
            t.Stretch = stretch;
            t.Builtin = true;
            tools.Add(t);
        }

        // Формат ini (одна запись на секцию):
        // [Tool]
        // Name=...
        // Path=...   (абсолютный или относительный к папке SDK)
        // Args=...
        // Stretch=1
        // Embed=1
        private void LoadCustomTools()
        {
            if (!File.Exists(cfgPath)) return;
            ToolInfo cur = null;
            foreach (string raw in File.ReadAllLines(cfgPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.Equals("[Tool]", StringComparison.OrdinalIgnoreCase))
                {
                    if (cur != null && !string.IsNullOrEmpty(cur.Name) && !string.IsNullOrEmpty(cur.Path)) tools.Add(cur);
                    cur = new ToolInfo();
                    cur.Builtin = false;
                    cur.Stretch = true;
                    cur.Description = "Пользовательский инструмент";
                    continue;
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                switch (k.ToLowerInvariant())
                {
                    case "name": cur.Name = v; break;
                    case "path":
                        cur.Path = System.IO.Path.IsPathRooted(v) ? v : System.IO.Path.Combine(baseDir, v);
                        break;
                    case "args": cur.Args = v; break;
                    case "stretch": cur.Stretch = v == "1"; break;
                    case "embed": cur.Embed = v != "0"; break;
                    case "overlay": cur.Overlay = v == "1"; break;
                    case "description": cur.Description = v; break;
                }
            }
            if (cur != null && !string.IsNullOrEmpty(cur.Name) && !string.IsNullOrEmpty(cur.Path)) tools.Add(cur);
        }

        private void SaveCustomTools()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Пользовательские инструменты Source Model Kit");
            foreach (ToolInfo t in tools)
            {
                if (t.Builtin) continue;
                sb.AppendLine("[Tool]");
                sb.AppendLine("Name=" + t.Name);
                sb.AppendLine("Path=" + t.Path);
                sb.AppendLine("Args=" + (t.Args ?? ""));
                sb.AppendLine("Stretch=" + (t.Stretch ? "1" : "0"));
                sb.AppendLine("Embed=" + (t.Embed ? "1" : "0"));
                sb.AppendLine("Overlay=" + (t.Overlay ? "1" : "0"));
                sb.AppendLine("Description=" + (t.Description ?? ""));
                sb.AppendLine();
            }
            try { File.WriteAllText(cfgPath, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { Dlg.Info(this, Loc.T("Не удалось сохранить список:\n") + ex.Message); }
        }

        // ---------------- окно без системной рамки ----------------
        private FlatButton WinBtn(string glyph, string tip)
        {
            FlatButton b = new FlatButton(glyph, null, tip);
            b.BaseRole = 0; b.Square = true; b.Width = 46; b.Height = 44;
            return b;
        }

        private void UpdateChrome()
        {
            bool maxd = WindowState == FormWindowState.Maximized;
            Padding want = (full || maxd) ? Padding.Empty : new Padding(5);
            if (Padding != want) Padding = want;
            if (btnMaxW != null) { btnMaxW.Glyph = maxd ? "\uE923" : "\uE922"; btnMaxW.Invalidate(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero)      // WM_NCCALCSIZE: клиентская область = всё окно (нет системного заголовка)
            {
                if (Native.IsZoomed(Handle) && !full)
                {
                    Native.RECT r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                    int b = Native.GetSystemMetrics(32) + Native.GetSystemMetrics(92);   // рамка + padded border
                    r.Left += b; r.Top += b; r.Right -= b; r.Bottom -= b;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0084)                                  // WM_NCHITTEST
            {
                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                m.Result = (IntPtr)ChromeHit(p);
                return;
            }
            base.WndProc(ref m);
        }

        private int ChromeHit(Point p)
        {
            const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                      HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
            if (!full && WindowState == FormWindowState.Normal)
            {
                int e = 6, c = 16;
                if (p.Y < e) return p.X < c ? HTTOPLEFT : p.X >= Width - c ? HTTOPRIGHT : HTTOP;
                if (p.Y >= Height - e) return p.X < c ? HTBOTTOMLEFT : p.X >= Width - c ? HTBOTTOMRIGHT : HTBOTTOM;
                if (p.X < e) return p.Y < c ? HTTOPLEFT : p.Y >= Height - c ? HTBOTTOMLEFT : HTLEFT;
                if (p.X >= Width - e) return p.Y < c ? HTTOPRIGHT : p.Y >= Height - c ? HTBOTTOMRIGHT : HTRIGHT;
            }
            if (p.Y >= 0 && p.Y < Padding.Top + top.Height) return HTCAPTION;
            return HTCLIENT;
        }

        // ---------------- верхняя панель ----------------
        private void PaintBrand(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle logo = new Rectangle(14, 10, 24, 24);
            using (GraphicsPath p = Theme.RoundRect(logo, 6))
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            Theme.DrawText(g, Theme.GLayout, Theme.IconSmall, logo, Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, "Source Model Kit", Theme.UiBold, new Rectangle(46, 0, 160, top.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void LayoutTop()
        {
            int y = (top.Height - 30) / 2;
            int bw = 46, right = top.Width;
            btnClose.SetBounds(right - bw, 0, bw, top.Height); right -= bw;
            btnMaxW.SetBounds(right - bw, 0, bw, top.Height); right -= bw;
            btnMin.SetBounds(right - bw, 0, bw, top.Height); right -= bw + 8;
            btnFull.SetBounds(right - 34, y, 34, 30); right -= 38;
            btnMonitor.SetBounds(right - 34, y, 34, 30); right -= 38;
            btnTheme.SetBounds(right - 34, y, 34, 30); right -= 38;
            btnBell.SetBounds(right - 34, y, 34, 30); right -= 38;
            btnLayout.SetBounds(right - btnLayout.Width, y, btnLayout.Width, 30); right -= btnLayout.Width + 14;
            int x = 190;
            int sw = Math.Min(stageBar.PreferredWidth(), Math.Max(120, right - x - 10));
            stageBar.SetBounds(x, (top.Height - 32) / 2, sw, 32);
            x += sw + 14;
        }

        private void ShowLayoutMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            Area a = TargetArea();
            AddMenu(m, Loc.T("Разделить область по вертикали"), "Ctrl+\\", delegate { SplitArea(a, true, 0.5); });
            AddMenu(m, Loc.T("Разделить область по горизонтали"), "Ctrl+Shift+\\", delegate { SplitArea(a, false, 0.5); });
            ToolStripMenuItem mx = AddMenu(m, ws.Maximized != null ? Loc.T("Вернуть раскладку") : Loc.T("Развернуть область"), Loc.T("Ctrl+Пробел"), delegate { ws.ToggleMaximize(a); });
            mx.Enabled = ws.AreaCount > 1;
            ToolStripMenuItem cl = AddMenu(m, Loc.T("Закрыть область"), "Ctrl+Shift+W", delegate { CloseArea(a); });
            cl.Enabled = ws.AreaCount > 1;
            m.Items.Add(new ToolStripSeparator());
            AddMenu(m, Loc.T("Новая вкладка проводника"), "Ctrl+T", delegate { NewExplorerTab(null, a); });
            m.Items.Add(new ToolStripSeparator());
            AddMenu(m, full ? Loc.T("Выйти из полноэкранного режима") : Loc.T("Полный экран"), "F11", delegate { SetFullscreen(!full); });
            ToolStripMenuItem mon = AddMenu(m, Loc.T("Переместить на другой монитор"), "Ctrl+F11", delegate { NextMonitor(); });
            mon.Enabled = Screen.AllScreens.Length > 1;
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem rs = AddMenu(m, Loc.T("Сбросить этап к исходному виду"), "", delegate { ResetStage(stageIndex); });
            rs.Enabled = stageIndex >= 0 && stageIndex < stages.Count && stages[stageIndex].Default != null;
            m.Show(btnLayout, new Point(btnLayout.Width - 260 > 0 ? 0 : btnLayout.Width - 260, btnLayout.Height + 4));
        }

        private static ToolStripMenuItem AddMenu(ContextMenuStrip m, string text, string keys, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.ShortcutKeyDisplayString = keys;
            mi.Click += click;
            m.Items.Add(mi);
            return mi;
        }

        // ---------------- раскладка ----------------
        private Pane MakePane(string spec)
        {
            if (spec == "H") return new HomePane(this);
            if (spec.StartsWith("E|")) return new ExplorerPane(this, spec.Substring(2));
            if (spec.StartsWith("M|")) return new ModelPane(this, spec.Substring(2));
            if (spec.StartsWith("T|"))
            {
                ToolInfo t = FindTool(spec.Substring(2));
                if (t == null || !t.Exists) return null;
                foreach (ToolPane old in pool)
                    if (old.Tool == t && old.ArgsOverride == null) { pool.Remove(old); return old; }
                ToolPane np = new ToolPane(t, null, false);
                np.ToolExited += ToolExitedHandler;
                if (t.Embed) toStart.Add(np);
                return np;
            }
            return null;
        }

        private void BuildDefaultLayout(List<Pane> keep)
        {
            Area left = ws.InitSingle();
            left.AddPane(new ExplorerPane(this, lastExplorerPath ?? baseDir), -1, true);
            Area right = ws.SplitArea(left, true, 0.38);
            right.AddPane(new HomePane(this), -1, true);
            if (keep != null) foreach (Pane p in keep) right.AddPane(p, -1, false);
            ws.SetActive(right);
        }

        private Area TargetArea()
        {
            Area a = ws.AreaAt(Cursor.Position);
            return a ?? ws.Active ?? ws.Areas[0];
        }

        private bool pressHandled;

        // Оболочка активна, в том числе когда на переднем плане её «закреплённое» окно (VS Code).
        private bool ShellIsForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            return fg == Handle || (fg != IntPtr.Zero && Native.GetWindow(fg, Native.GW_OWNER) == Handle);
        }

        private void PollActiveArea(object s, EventArgs e)
        {
            // Активная область = та, где пользователь последний раз кликнул (в том числе внутри чужой программы).
            if (!Visible || !ShellIsForeground()) return;
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (!down) { pressHandled = false; return; }
            if (pressHandled) return;
            pressHandled = true;
            Area a = ws.AreaAt(Cursor.Position);
            if (a == null) return;
            ws.SetActive(a);
        }

        // ---------------- IShellHost ----------------
        public void SetStatus(string text)
        {
            statusLine.LeftText = text;
            statusTimer.Stop(); statusTimer.Start();
        }

        public void OpenTool(ToolInfo t, string args, Area target)
        {
            if (!t.Exists)
            {
                Dlg.Info(this, Loc.T("Файл не найден:\n") + t.Path + Loc.T("\n\nПоложите оболочку в корневую папку SDK (рядом с Crowbar.exe)."));
                return;
            }
            if (args == null)
            {
                foreach (Area ar in ws.Areas)
                    foreach (Pane p in ar.Panes)
                    {
                        ToolPane tp0 = p as ToolPane;
                        if (tp0 != null && tp0.Tool == t && tp0.ArgsOverride == null && tp0.Running)
                        {
                            if (ws.Maximized != null && ws.Maximized != ar) ws.ToggleMaximize(ws.Maximized);
                            ar.Select(tp0);
                            return;
                        }
                    }
            }
            Area a = target;
            if (a == null || a.IsDisposed) a = ws.Active ?? ws.Areas[0];
            ToolPane tp = new ToolPane(t, args);
            tp.ToolExited += ToolExitedHandler;
            a.AddPane(tp, -1, true);
            SetStatus(Loc.T("Запуск ") + t.Name + "…");
            tp.Start();
        }

        private void ToolExitedHandler(object s, EventArgs e) { OnToolExited((ToolPane)s); }

        private void OnToolExited(ToolPane p)
        {
            Area a = p.Area;
            if (a == null) return;
            Notify(NoteKind.Info, p.Tool.Name + Loc.T(" закрыт"), "");
            a.RemovePane(p);
            p.Dispose();
            ws.AfterPaneRemoved(a);
        }

        public void AddToolDialog()
        {
            using (AddToolForm f = new AddToolForm())
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Result != null)
                {
                    tools.Add(f.Result);
                    SaveCustomTools();
                    RefreshHome();
                    OpenTool(f.Result, null, null);
                }
            }
        }

        public void RemoveCustomTool(ToolInfo t)
        {
            tools.Remove(t);
            SaveCustomTools();
            RefreshHome();
            SetStatus(Loc.T("Список инструментов обновлён"));
        }

        private void RefreshHome()
        {
            foreach (Area a in ws.Areas)
                foreach (Pane p in a.Panes)
                {
                    HomePane h = p as HomePane;
                    if (h != null) h.Rebuild();
                }
        }

        public void NewExplorerTab(string path, Area target)
        {
            Area a = target;
            if (a == null || a.IsDisposed) a = ws.Active ?? ws.Areas[0];
            if (path == null)
            {
                ExplorerPane ep = a.Current as ExplorerPane;
                path = ep != null ? ep.CurrentPath : (lastExplorerPath ?? baseDir);
                if (string.IsNullOrEmpty(path)) path = baseDir;
            }
            lastExplorerPath = path;
            a.AddPane(new ExplorerPane(this, path), -1, true);
        }

        public void OpenModel(string path, Area target)
        {
            foreach (Area a in ws.Areas)
                foreach (Pane p in a.Panes)
                {
                    ModelPane mp = p as ModelPane;
                    if (mp == null) continue;
                    if (argSeq != null) { mp.StartSeq = argSeq; argSeq = null; }
                    if (path != null) mp.LoadModel(path, false);
                    a.Select(mp);
                    return;
                }
            Area t = target;
            if (t == null || t.IsDisposed) t = ws.Active ?? ws.Areas[0];
            ModelPane np = new ModelPane(this, path);
            if (argSeq != null) { np.StartSeq = argSeq; argSeq = null; }
            t.AddPane(np, -1, true);
        }

        public bool ClosePane(Pane p, bool force)
        {
            ToolPane tp = p as ToolPane;
            Area a = p.Area;
            if (tp != null)
            {
                if (force) tp.ForceKill();
                else if (!tp.CloseTool())
                {
                    // приложение показало свой диалог (например, «Сохранить изменения?») - не закрываем вкладку
                    if (a != null) a.Select(tp);
                    Dlg.Info(this, tp.Tool.Name + Loc.T(" ожидает ответа (возможно, вопрос о сохранении).\n") +
                        Loc.T("Ответьте в окне приложения или завершите его принудительно через меню вкладки (правая кнопка)."));
                    return false;
                }
            }
            if (a == null) return true;
            ExplorerPane ex = p as ExplorerPane;
            if (ex != null && ex.CurrentPath.Length > 0) lastExplorerPath = ex.CurrentPath;
            a.RemovePane(p);
            p.Dispose();
            ws.AfterPaneRemoved(a);
            return true;
        }

        public void SplitArea(Area a, bool vertical, double ratio)
        {
            ExplorerPane src = a.Current as ExplorerPane;
            string path = src != null && src.CurrentPath.Length > 0 ? src.CurrentPath : (lastExplorerPath ?? baseDir);
            Area na = ws.SplitArea(a, vertical, ratio);
            na.AddPane(new ExplorerPane(this, path), -1, true);
        }

        public void CloseArea(Area a)
        {
            if (ws.AreaCount <= 1)
            {
                if (a.Panes.Count == 0) a.AddPane(new HomePane(this), -1, true);
                return;
            }
            Area nb = ws.NeighborOf(a);
            foreach (Pane p in new List<Pane>(a.Panes))
            {
                if (p is ToolPane && nb != null) nb.AddPane(p, -1, false);
                else { a.RemovePane(p); p.Dispose(); }
            }
            ws.RemoveArea(a);
        }

        public void ShowPaneMenu(Pane p, Point screen)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            ToolPane tp = p as ToolPane;
            Area a = p.Area;
            if (tp != null)
            {
                AddMenu(m, Loc.T("Перезапустить"), "", delegate { RestartTool(tp); });
                AddMenu(m, Loc.T("Открепить в отдельное окно"), "", delegate { tp.Detach(); });
                m.Items.Add(new ToolStripSeparator());
            }
            ExplorerPane ep = p as ExplorerPane;
            if (ep != null) AddMenu(m, Loc.T("Дублировать вкладку"), "", delegate { NewExplorerTab(ep.CurrentPath, a); });
            if (a != null && a.Panes.Count > 1)
            {
                AddMenu(m, Loc.T("Вынести в новую область справа"), "", delegate { MoveToNewArea(p, true); });
                AddMenu(m, Loc.T("Вынести в новую область снизу"), "", delegate { MoveToNewArea(p, false); });
                m.Items.Add(new ToolStripSeparator());
            }
            AddMenu(m, Loc.T("Закрыть вкладку"), "Ctrl+W", delegate { ClosePane(p, false); });
            if (tp != null) AddMenu(m, Loc.T("Завершить принудительно"), "", delegate { ClosePane(p, true); });
            m.Show(screen);
        }

        private void MoveToNewArea(Pane p, bool vertical)
        {
            Area a = p.Area;
            Area na = ws.SplitArea(a, vertical, 0.5);
            ws.MovePane(p, na, 0);
        }

        private void RestartTool(ToolPane tp)
        {
            ToolInfo t = tp.Tool; string args = tp.ArgsOverride; Area a = tp.Area;
            if (!tp.CloseTool()) tp.ForceKill();
            if (a != null) { a.RemovePane(tp); }
            tp.Dispose();
            OpenTool(t, args, a);
        }

        public void ShowAddMenu(Area a, Point screen)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            AddMenu(m, Loc.T("Проводник"), "Ctrl+T", delegate { NewExplorerTab(null, a); });
            AddMenu(m, Loc.T("Просмотр модели (.mdl)"), "", delegate { OpenModel(null, a); });
            AddMenu(m, Loc.T("Главная"), "", delegate { a.AddPane(new HomePane(this), -1, true); });
            m.Items.Add(new ToolStripSeparator());
            foreach (ToolInfo t in tools)
            {
                ToolInfo tt = t;
                ToolStripMenuItem mi = AddMenu(m, t.Name, "", delegate { OpenTool(tt, null, a); });
                mi.Enabled = t.Exists;
            }
            m.Items.Add(new ToolStripSeparator());
            AddMenu(m, Loc.T("Добавить инструмент…"), "", delegate { AddToolDialog(); });
            m.Show(screen);
        }

        // ---------------- поиск / автообнаружение инструментов ----------------
        private ToolInfo FindTool(string name)
        {
            foreach (ToolInfo t in tools) if (t.Name == name) return t;
            return null;
        }

        // При первом запуске добавляем найденные VS Code и Photoshop (они не встраиваются, запускаются отдельно).
        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
        }

        // VS Code внутри оболочки запускается отдельным экземпляром со своей папкой данных
        // (иначе он передаст путь уже открытому окну и завершится). Настройки пользователя копируются один раз.
        private string VsCodeArgs()
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = System.IO.Path.Combine(app, @"SourceSDKHub\vscode");
            string user = System.IO.Path.Combine(dir, "User");
            try
            {
                if (!Directory.Exists(user))
                {
                    Directory.CreateDirectory(user);
                    string src = System.IO.Path.Combine(app, @"Code\User");
                    foreach (string f in new string[] { "settings.json", "keybindings.json" })
                    {
                        string sf = System.IO.Path.Combine(src, f);
                        if (File.Exists(sf)) File.Copy(sf, System.IO.Path.Combine(user, f));
                    }
                    string sn = System.IO.Path.Combine(src, "snippets");
                    if (Directory.Exists(sn)) CopyDir(sn, System.IO.Path.Combine(user, "snippets"));
                }
            }
            catch { }
            return "--user-data-dir \"" + dir + "\" \"" + baseDir + "\"";
        }

        private void AutoDetectTools()
        {
            ToolInfo old = FindTool("VS Code");
            if (old != null && !old.Builtin && old.Description != null && old.Description.StartsWith("Редактор для QC") && (!old.Embed || !old.Overlay))
            {
                old.Embed = true; old.Overlay = true; old.Stretch = true; old.Args = VsCodeArgs();
                old.Description = "Редактор для QC, скриптов и конфигов (встроен в оболочку)";
                SaveCustomTools();
            }
            if (settings.GetBool("detected", false)) return;
            settings.SetBool("detected", true);
            bool changed = false;
            string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? "";
            string la = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            string[] code =
            {
                System.IO.Path.Combine(la, @"Programs\Microsoft VS Code\Code.exe"),
                System.IO.Path.Combine(pf, @"Microsoft VS Code\Code.exe")
            };
            foreach (string c in code)
            {
                if (!File.Exists(c) || FindTool("VS Code") != null) continue;
                ToolInfo t = new ToolInfo();
                t.Name = "VS Code"; t.Path = c; t.Args = VsCodeArgs();
                t.Stretch = true; t.Embed = true; t.Overlay = true; t.Builtin = false;
                t.Description = "Редактор для QC, скриптов и конфигов (встроен в оболочку)";
                tools.Add(t); changed = true;
                break;
            }
            try
            {
                string adobe = System.IO.Path.Combine(pf, "Adobe");
                if (Directory.Exists(adobe) && FindTool("Photoshop") == null)
                    foreach (string d in Directory.GetDirectories(adobe, "Adobe Photoshop*"))
                    {
                        string exe = System.IO.Path.Combine(d, "Photoshop.exe");
                        if (!File.Exists(exe)) continue;
                        ToolInfo t = new ToolInfo();
                        t.Name = "Photoshop"; t.Path = exe; t.Stretch = true; t.Embed = false; t.Builtin = false;
                        t.Description = "Текстуры (открывается отдельным окном)";
                        tools.Add(t); changed = true;
                        break;
                    }
            }
            catch { }
            if (changed) SaveCustomTools();
            settings.Save();
        }

        // ---------------- уведомления ----------------
        public void Notify(NoteKind kind, string title, string text)
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(delegate { Notify(kind, title, text); })); return; }
            notifier.Push(kind, title, text);
        }

        private void ShowNotesMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            m.ShowCheckMargin = false;
            if (notifier.History.Count == 0)
            {
                ToolStripMenuItem none = AddMenu(m, Loc.T("Уведомлений пока нет"), "", null);
                none.Enabled = false;
            }
            int n = 0;
            foreach (Note note in notifier.History)
            {
                if (n++ >= 14) break;
                string line = note.Time.ToString("HH:mm") + "   " + note.Title + (note.Text.Length > 0 ? " — " + note.Text : "");
                if (line.Length > 90) line = line.Substring(0, 87) + "…";
                ToolStripMenuItem mi = AddMenu(m, line, "", null);
                mi.ForeColor = ToastForm.KindColor(note.Kind);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem toasts = AddMenu(m, notifier.ToastsEnabled ? Loc.T("Всплывающие уведомления: включены") : Loc.T("Всплывающие уведомления: выключены"), "",
                delegate { notifier.ToastsEnabled = !notifier.ToastsEnabled; settings.SetBool("toasts", notifier.ToastsEnabled); settings.Save(); });
            ToolStripMenuItem clr = AddMenu(m, Loc.T("Очистить журнал"), "", delegate { notifier.Clear(); });
            clr.Enabled = notifier.History.Count > 0;
            notifier.MarkRead();
            m.Show(btnBell, new Point(btnBell.Width - 360, btnBell.Height + 4));
        }

        // ---------------- закреплённые папки ----------------
        private static string PinNorm(string p) { return p.Length > 3 ? p.TrimEnd('\\') : p; }

        public void TogglePin(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string n = PinNorm(path);
            string found = null;
            foreach (string p in pins) if (string.Equals(PinNorm(p), n, StringComparison.OrdinalIgnoreCase)) found = p;
            if (found != null) { pins.Remove(found); Notify(NoteKind.Info, Loc.T("Откреплено"), n); }
            else { pins.Add(n); Notify(NoteKind.Good, Loc.T("Закреплено в быстром доступе"), n); }
            settings.Set("pins", string.Join("|", pins.ToArray()));
            settings.Save();
            if (PinsChanged != null) PinsChanged(this, EventArgs.Empty);
        }

        // ---------------- темы ----------------
        private void ShowThemeMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            m.ShowCheckMargin = true;
            foreach (string[] t in Theme.Themes)
            {
                string key = t[0];
                ToolStripMenuItem mi = AddMenu(m, t[1], "", delegate { SetTheme(key); });
                mi.Checked = key == Theme.Key;
            }
            m.Show(btnTheme, new Point(0, btnTheme.Height + 4));
        }

        private void SetTheme(string key)
        {
            if (key == Theme.Key) return;
            Theme.Apply(key);
            settings.Set("theme", Theme.Key);
            settings.Save();
            SuspendLayout();
            Theme.DarkenWindow(Handle);
            Theme.Reskin(this);
            ResumeLayout(true);
            Refresh();
            SetStatus(Loc.T("Тема: ") + Theme.Themes[Array.FindIndex(Theme.Themes, delegate(string[] x) { return x[0] == Theme.Key; })][1]);
        }

        // ---------------- этапы ----------------
        private Stage NewStage(string name, string layout, bool builtin)
        {
            Stage st = new Stage();
            st.Name = name; st.Layout = layout; st.Default = builtin ? layout : null;
            return st;
        }

        private List<Stage> DefaultStages()
        {
            string e = "E|" + baseDir;
            string vsc = FindTool("VS Code") != null ? "T|VS Code" : null;
            string ps = FindTool("Photoshop") != null ? "T|Photoshop" : "T|VTF Version Changer";
            List<Stage> r = new List<Stage>();
            r.Add(NewStage("Обзор", LayoutBuilder.Split(true, 0.4, LayoutBuilder.Area(e), LayoutBuilder.Area("H")), true));
            r.Add(NewStage("Модели", LayoutBuilder.Split(true, 0.24, LayoutBuilder.Area(e),
                LayoutBuilder.Split(true, 0.5, LayoutBuilder.Area("T|Crowbar"), LayoutBuilder.Area("M|"))), true));
            r.Add(NewStage("QC и код", LayoutBuilder.Split(true, 0.3, LayoutBuilder.Area(e),
                LayoutBuilder.Area(vsc != null ? vsc : e)), true));
            r.Add(NewStage("Текстуры", LayoutBuilder.Split(true, 0.28, LayoutBuilder.Area(e),
                LayoutBuilder.Split(false, 0.72, LayoutBuilder.Area("T|VMT Editor"), LayoutBuilder.Area(ps))), true));
            return r;
        }

        private void InitStages()
        {
            stages.Clear();
            stages.AddRange(StageStore.Load(stagesPath));
            if (stages.Count == 0) stages.AddRange(DefaultStages());
            else if (settings.GetInt("stagesver", 1) < 2)
            {
                // этап «Модели» получил просмотрщик: обновляем, если пользователь его не менял
                string oldDef = LayoutBuilder.Split(true, 0.34, LayoutBuilder.Area("E|" + baseDir), LayoutBuilder.Area("T|Crowbar"));
                foreach (Stage nd in DefaultStages())
                    foreach (Stage st in stages)
                        if (st.Name == nd.Name && st.Default != null && st.Default == oldDef)
                        {
                            if (st.Layout == st.Default) st.Layout = nd.Default;
                            st.Default = nd.Default;
                        }
            }
            if (settings.GetInt("stagesver", 1) < 2) StageStore.Save(stagesPath, stages);
            settings.SetInt("stagesver", 2);
            settings.Save();
            string want = settings.Get("stage", "");
            int idx = 0;
            for (int i = 0; i < stages.Count; i++) if (stages[i].Name == want) idx = i;
            ActivateStage(idx);
        }

        private void ActivateStage(int i)
        {
            if (i < 0 || i >= stages.Count) return;
            if (stageIndex >= 0 && stageIndex < stages.Count && ws.AreaCount > 0 && i == stageIndex) return;
            if (stageIndex >= 0 && stageIndex < stages.Count && ws.AreaCount > 0)
                stages[stageIndex].Layout = ws.Serialize();

            ReleasePanes(ws.Teardown());
            stageIndex = i;
            toStart.Clear();
            if (!ws.Restore(stages[i].Layout, MakePane))
            {
                ReleasePanes(ws.Teardown());
                BuildDefaultLayout(null);
            }
            if (pool.Count > 0)
            {
                // запущенные программы, которых нет в раскладке этапа, не закрываем - кладём в последнюю область
                List<Area> all = ws.Areas;
                Area last = all[all.Count - 1];
                foreach (ToolPane p in pool) last.AddPane(p, -1, false);
                pool.Clear();
            }
            foreach (ToolPane tp in toStart) tp.Start();
            toStart.Clear();
            stageBar.Current = i;
            stageBar.Invalidate();
            LayoutTop();
            if (ws.Active != null && ws.Active.Current != null) ws.Active.Current.Activated();
            SetStatus(Loc.T("Этап: ") + Loc.T(stages[i].Name));
        }

        // Запущенные программы переживают смену этапа, остальное пересоздаётся.
        private void ReleasePanes(List<Pane> panes)
        {
            foreach (Pane p in panes)
            {
                ToolPane tp = p as ToolPane;
                if (tp != null && tp.Running) pool.Add(tp); else p.Dispose();
            }
        }

        private void AddStage()
        {
            string name = Dlg.Prompt(this, Loc.T("Новый этап"), Loc.T("Название этапа (раскладка копируется с текущей):"), Loc.T("Этап ") + (stages.Count + 1));
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return;
            if (stageIndex >= 0 && stageIndex < stages.Count) stages[stageIndex].Layout = ws.Serialize();
            Stage st = NewStage(name.Trim(), ws.Serialize(), false);
            stages.Add(st);
            stageIndex = stages.Count - 1;
            stageBar.Current = stageIndex;
            LayoutTop();
            stageBar.Invalidate();
            StageStore.Save(stagesPath, stages);
            SetStatus(Loc.T("Создан этап: ") + st.Name);
        }

        private void RenameStage(int i)
        {
            string name = Dlg.Prompt(this, Loc.T("Переименовать этап"), Loc.T("Название:"), stages[i].Name);
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return;
            stages[i].Name = name.Trim();
            LayoutTop();
            stageBar.Invalidate();
            StageStore.Save(stagesPath, stages);
        }

        private void ResetStage(int i)
        {
            if (i < 0 || i >= stages.Count || stages[i].Default == null) return;
            stages[i].Layout = stages[i].Default;
            stageIndex = -1;           // не затирать только что сброшенную раскладку текущей
            ActivateStage(i);
        }

        private void ShowStageMenu(int i, Point screen)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            AddMenu(m, Loc.T("Переименовать…"), "", delegate { RenameStage(i); });
            AddMenu(m, Loc.T("Дублировать"), "", delegate
            {
                if (stageIndex >= 0 && stageIndex < stages.Count) stages[stageIndex].Layout = ws.Serialize();
                stages.Insert(i + 1, NewStage(stages[i].Name + Loc.T(" (копия)"), stages[i].Layout, false));
                if (stageIndex > i) stageIndex++;
                LayoutTop(); stageBar.Current = stageIndex; stageBar.Invalidate();
                StageStore.Save(stagesPath, stages);
            });
            ToolStripMenuItem rs = AddMenu(m, Loc.T("Сбросить к исходному виду"), "", delegate { ResetStage(i); });
            rs.Enabled = stages[i].Default != null;
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem del = AddMenu(m, Loc.T("Удалить этап"), "", delegate { DeleteStage(i); });
            del.Enabled = stages.Count > 1;
            m.Show(screen);
        }

        private void DeleteStage(int i)
        {
            if (stages.Count <= 1) return;
            if (!Dlg.Confirm(this, Loc.T("Удалить этап «") + Loc.T(stages[i].Name) +Loc.T("»? Запущенные программы не закроются."), Loc.T("Удалить"), Loc.T("Отмена"))) return;
            bool wasCurrent = i == stageIndex;
            stages.RemoveAt(i);
            if (wasCurrent) { stageIndex = -1; ActivateStage(Math.Min(i, stages.Count - 1)); }
            else { if (i < stageIndex) stageIndex--; stageBar.Current = stageIndex; LayoutTop(); stageBar.Invalidate(); }
            StageStore.Save(stagesPath, stages);
        }

        // ---------------- горячие клавиши ----------------
        private static bool IsDown(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        private bool HookHandler(int vk)
        {
            if (!ShellIsForeground()) return false;
            Keys k = (Keys)vk;
            if (IsDown(0x11)) k |= Keys.Control;
            if (IsDown(0x10)) k |= Keys.Shift;
            if (IsDown(0x12)) k |= Keys.Alt;
            if (!IsShellKey(k)) return false;
            BeginInvoke(new MethodInvoker(delegate { RunShellKey(k); }));
            return true;
        }

        private static bool IsShellKey(Keys k)
        {
            Keys main = k & Keys.KeyCode;
            if ((k & Keys.Modifiers) == (Keys.Control | Keys.Alt) && main >= Keys.D1 && main <= Keys.D9) return true;
            return k == Keys.F11 || k == (Keys.Control | Keys.F11) || k == (Keys.Control | Keys.Space) ||
                   k == (Keys.Control | Keys.Oem5) || k == (Keys.Control | Keys.Shift | Keys.Oem5);
        }

        private bool RunShellKey(Keys k)
        {
            Area a;
            switch (k)
            {
                case Keys.F11: SetFullscreen(!full); return true;
                case Keys.Control | Keys.F11: NextMonitor(); return true;
                case Keys.Control | Keys.Space: ws.ToggleMaximize(TargetArea()); return true;
                case Keys.Control | Keys.Oem5: SplitArea(TargetArea(), true, 0.5); return true;
                case Keys.Control | Keys.Shift | Keys.Oem5: SplitArea(TargetArea(), false, 0.5); return true;
                case Keys.Control | Keys.W:
                    a = ws.Active;
                    if (a != null && a.Current != null) ClosePane(a.Current, false);
                    return true;
                case Keys.Control | Keys.Shift | Keys.W:
                    if (ws.Active != null) CloseArea(ws.Active);
                    return true;
                case Keys.Control | Keys.T: NewExplorerTab(null, ws.Active); return true;
                case Keys.Control | Keys.Tab:
                    if (ws.Active != null) ws.Active.SelectOffset(1);
                    return true;
                case Keys.Control | Keys.Shift | Keys.Tab:
                    if (ws.Active != null) ws.Active.SelectOffset(-1);
                    return true;
            }
            if ((k & Keys.Modifiers) == (Keys.Control | Keys.Alt))
            {
                int si = (int)(k & Keys.KeyCode) - (int)Keys.D1;
                if (si >= 0 && si < 9 && si < stages.Count) { ActivateStage(si); return true; }
            }
            if ((k & Keys.Modifiers) == Keys.Control)
            {
                int d = (int)(k & Keys.KeyCode) - (int)Keys.D1;
                a = ws.Active;
                if (d >= 0 && d < 9 && a != null && d < a.Panes.Count) { a.Select(a.Panes[d]); return true; }
            }
            return false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (RunShellKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------- полный экран / мониторы ----------------
        private void SetFullscreen(bool on)
        {
            if (on == full || !IsHandleCreated) return;
            if (on)
            {
                Screen scr = Screen.FromControl(this);
                wasMax = WindowState == FormWindowState.Maximized;
                if (wasMax) WindowState = FormWindowState.Normal;
                savedBounds = Bounds;
                savedStyle = Native.GetStyle(Handle, Native.GWL_STYLE);
                Native.SetStyle(Handle, Native.GWL_STYLE, savedStyle & ~(Native.WS_CAPTION | Native.WS_THICKFRAME));
                MinimumSize = Size.Empty;
                ApplyFullBounds(scr);
                full = true;
            }
            else
            {
                Native.SetStyle(Handle, Native.GWL_STYLE, savedStyle);
                MinimumSize = new Size(900, 560);
                Native.SetWindowPos(Handle, IntPtr.Zero, savedBounds.X, savedBounds.Y, savedBounds.Width, savedBounds.Height,
                    Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED);
                if (wasMax) WindowState = FormWindowState.Maximized;
                full = false;
            }
            btnFull.Glyph = full ? Theme.GUnfull : Theme.GFull;
            btnFull.Invalidate();
            UpdateChrome();
            UpdateInfo();
            SetStatus(full ? Loc.T("Полный экран: F11 — выход, Ctrl+F11 — на другой монитор") : Loc.T("Оконный режим"));
        }

        private void ApplyFullBounds(Screen s)
        {
            Rectangle b = s.Bounds;
            Native.SetWindowPos(Handle, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
                Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED);
        }

        private void NextMonitor()
        {
            Screen[] sc = Screen.AllScreens;
            if (sc.Length < 2) { SetStatus(Loc.T("Подключен только один монитор")); return; }
            Screen cur = Screen.FromControl(this);
            int i = 0;
            for (int j = 0; j < sc.Length; j++) if (sc[j].DeviceName == cur.DeviceName) i = j;
            MoveToScreen(sc[(i + 1) % sc.Length]);
        }

        private void MoveToScreen(Screen nxt)
        {
            if (full) ApplyFullBounds(nxt);
            else
            {
                bool mx = WindowState == FormWindowState.Maximized;
                Rectangle cur = mx ? RestoreBounds : Bounds;
                if (mx) WindowState = FormWindowState.Normal;
                Rectangle wa = nxt.WorkingArea;
                int w = Math.Min(cur.Width, wa.Width), h = Math.Min(cur.Height, wa.Height);
                Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
                if (mx) WindowState = FormWindowState.Maximized;
            }
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            if (!IsHandleCreated) return;
            Screen[] sc = Screen.AllScreens;
            Screen cur = Screen.FromControl(this);
            int i = 1;
            for (int j = 0; j < sc.Length; j++) if (sc[j].DeviceName == cur.DeviceName) i = j + 1;
            statusLine.RightText = Loc.T("Экран ") + i + "/" + sc.Length + "  ·  " + cur.Bounds.Width + "×" + cur.Bounds.Height +
                Loc.T("  ·  F11 полный экран  ·  Ctrl+Пробел развернуть область  ·  Ctrl+\\ разделить");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkenWindow(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutTop();
            UpdateChrome();

            hook = new KeyHook();
            hook.Handler = HookHandler;

            Screen[] sc = Screen.AllScreens;
            Screen target = null;
            if (argScreen >= 1 && argScreen <= sc.Length) target = sc[argScreen - 1];
            else
            {
                string dn = settings.Get("screen", "");
                foreach (Screen s in sc) if (s.DeviceName == dn) target = s;
            }
            bool wantFull = argFull || settings.GetBool("full", false);
            bool wantMax = settings.GetBool("max", false);

            if (target != null && target.DeviceName != Screen.FromControl(this).DeviceName)
                MoveToScreen(target);
            if (wantFull) SetFullscreen(true);
            else if (wantMax) WindowState = FormWindowState.Maximized;
            UpdateInfo();
            if (argModel != null) OpenModel(argModel, null);
        }

        // ---------------- язык ----------------
        private string pendingLang;
        private bool restartAfterClose;

        private void OnLangPicked(string code)
        {
            if (code == Loc.Code) return;
            if (!Dlg.Confirm(this, Loc.T("Для смены языка нужен перезапуск оболочки. Перезапустить сейчас?"), Loc.T("Перезапустить"), Loc.T("Отмена"))) return;
            pendingLang = code; restartAfterClose = true;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (restartAfterClose)
                try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
        }

        // ---------------- закрытие ----------------
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            List<ToolPane> running = new List<ToolPane>();
            foreach (Area a in ws.Areas)
                foreach (Pane p in a.Panes)
                {
                    ToolPane tp = p as ToolPane;
                    if (tp != null && tp.Running) running.Add(tp);
                }
            if (running.Count > 0)
            {
                if (!Dlg.Confirm(this,
                    Loc.T("Открыто инструментов: ") + running.Count + Loc.T(". Закрыть все?\n(Приложения получат запрос на закрытие и смогут предложить сохранить работу.)"),
                    Loc.T("Закрыть всё"), Loc.T("Отмена"))) { e.Cancel = true; pendingLang = null; restartAfterClose = false; return; }

                foreach (ToolPane tp in running)
                {
                    if (!tp.CloseTool())
                    {
                        if (tp.Area != null) tp.Area.Select(tp);
                        Dlg.Info(this, tp.Tool.Name + Loc.T(" не закрылся (ожидает ответа в своём окне).\nЗавершите его вручную и повторите закрытие."));
                        e.Cancel = true; pendingLang = null; restartAfterClose = false;
                        return;
                    }
                }
            }
            if (pendingLang != null) settings.Set("lang", pendingLang);
            SaveState();
            if (hook != null) { hook.Dispose(); hook = null; }
        }

        private void SaveState()
        {
            try
            {
                Rectangle nb = full ? savedBounds : (WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
                settings.Set("bounds", nb.X + "," + nb.Y + "," + nb.Width + "," + nb.Height);
                settings.SetBool("max", full ? wasMax : WindowState == FormWindowState.Maximized);
                settings.SetBool("full", full);
                settings.Set("screen", Screen.FromControl(this).DeviceName);
                if (stageIndex >= 0 && stageIndex < stages.Count)
                {
                    stages[stageIndex].Layout = ws.Serialize();
                    settings.Set("stage", stages[stageIndex].Name);
                }
                StageStore.Save(stagesPath, stages);
                settings.Save();
            }
            catch { }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Loc.Init(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'), "SourceSDK.settings.ini"));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}
