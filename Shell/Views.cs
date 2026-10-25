// Панели: встроенная программа и «Главная».

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SourceSDKHub
{
    // ------------------------------------------------------------------
    // Встроенная программа (окно чужого процесса через SetParent)
    // ------------------------------------------------------------------
    internal class ToolPane : Pane
    {
        private readonly ToolInfo tool;
        private readonly string argsOverride;
        private readonly Panel host;
        private readonly Label status;
        private readonly FlatButton actionBtn;
        private bool idle;
        private bool overlay;                 // окно не дочернее, а привязано к области (владелец - оболочка)
        private readonly Timer dockTimer = new Timer();
        private readonly Timer finder;
        private readonly Timer watcher;
        private Process proc;
        private IntPtr hwnd = IntPtr.Zero;
        private long origStyle, origExStyle;
        private int attempts;
        private bool detaching;

        public ToolInfo Tool { get { return tool; } }
        public string ArgsOverride { get { return argsOverride; } }
        public bool Running { get { return proc != null && !SafeExited(); } }
        public event EventHandler ToolExited;

        public override string IconGlyph { get { return Theme.GApp; } }
        public override string Serialize() { return string.IsNullOrEmpty(argsOverride) ? "T|" + tool.Name : null; }
        public override string Title
        {
            get
            {
                if (string.IsNullOrEmpty(argsOverride)) return tool.Name;
                string f = argsOverride.Trim().Trim('"');
                try { f = System.IO.Path.GetFileName(f); } catch { }
                return tool.Name + " · " + f;
            }
        }

        public ToolPane(ToolInfo t, string args) : this(t, args, true) { }

        // autoStart=false: программа без встраивания не запускается сама (этапы), а ждёт кнопки.
        public ToolPane(ToolInfo t, string args, bool autoStart)
        {
            tool = t;
            argsOverride = args;
            BackColor = Theme.Bg1;

            host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Bg1;
            host.Resize += delegate { FitChild(); };
            Controls.Add(host);

            status = new Label();
            status.AutoSize = false;
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.ForeColor = Theme.TextDim;
            status.BackColor = Theme.Bg1;
            status.Font = new Font("Segoe UI", 11f);
            status.Text = Loc.T("Запуск: ") + t.Name + "…";
            host.Controls.Add(status);

            actionBtn = new FlatButton(null, Loc.T("Запустить"), null);
            actionBtn.BaseRole = 1; actionBtn.Primary = true;
            actionBtn.SetBounds(0, 0, 150, 32);
            actionBtn.Visible = false;
            actionBtn.Click += delegate { if (idle) Start(); else ShowExternal(); };
            host.Controls.Add(actionBtn);
            actionBtn.BringToFront();
            host.Resize += delegate { actionBtn.Location = new Point((host.Width - actionBtn.Width) / 2, host.Height / 2 + 36); };
            if (!autoStart && !t.Embed)
            {
                idle = true;
                status.Text = t.Name + Loc.T(" ещё не запущен.");
                actionBtn.Visible = true;
            }

            dockTimer.Interval = 40;
            dockTimer.Tick += delegate { SyncOverlay(); };

            finder = new Timer();
            finder.Interval = 200;
            finder.Tick += FinderTick;

            watcher = new Timer();
            watcher.Interval = 700;
            watcher.Tick += WatcherTick;
        }

        private bool SafeExited()
        {
            try { return proc.HasExited; } catch { return true; }
        }

        private void ShowExternal()
        {
            if (proc == null) return;
            IntPtr h = Native.FindMainWindow(proc.Id);
            if (h == IntPtr.Zero) { status.Text = tool.Name + Loc.T(": окно не найдено."); return; }
            Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetForegroundWindow(h);
        }

        public void Start()
        {
            idle = false;
            actionBtn.Visible = false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                string args = argsOverride ?? tool.Args ?? "";
                if (tool.IsJar)
                {
                    // Java-GUI запускаем через javaw. Для bspsrc.jar - тот же главный класс,
                    // что и в оригинальном bspsrc.bat; для прочих jar - обычный -jar.
                    psi.FileName = "javaw";
                    if (System.IO.Path.GetFileName(tool.Path).Equals("bspsrc.jar", StringComparison.OrdinalIgnoreCase))
                        psi.Arguments = "-cp \"" + tool.Path + "\" info.ata4.bspsrc.app.src.BspSourceLauncher " + args;
                    else
                        psi.Arguments = "-jar \"" + tool.Path + "\" " + args;
                }
                else
                {
                    psi.FileName = tool.Path;
                    psi.Arguments = args;
                }
                psi.WorkingDirectory = !string.IsNullOrEmpty(tool.WorkDir) ? tool.WorkDir : System.IO.Path.GetDirectoryName(tool.Path);
                psi.UseShellExecute = false;
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                status.ForeColor = Theme.Bad;
                status.Text = Loc.T("Не удалось запустить ") + tool.Name + ":\n" + ex.Message +
                    (tool.IsJar ? Loc.T("\n\nДля BSPSrc нужна установленная Java (JRE 8+).") : "");
                return;
            }

            if (!tool.Embed)
            {
                status.Text = tool.Name + Loc.T(" запущен в отдельном окне.");
                actionBtn.Caption = Loc.T("Показать окно"); actionBtn.Visible = true; actionBtn.Invalidate();
                watcher.Start();
                return;
            }

            attempts = 0;
            finder.Start();
            watcher.Start();
        }

        private void FinderTick(object s, EventArgs e)
        {
            if (proc == null) { finder.Stop(); return; }
            attempts++;
            if (SafeExited()) { finder.Stop(); return; }

            IntPtr h = Native.FindMainWindow(proc.Id);
            if (h != IntPtr.Zero)
            {
                finder.Stop();
                Attach(h);
                return;
            }
            if (attempts > 150) // ~30 секунд
            {
                finder.Stop();
                status.Text = Loc.T("Окно ") + tool.Name + Loc.T(" не удалось встроить.\nПриложение работает в отдельном окне.");
                actionBtn.Caption = Loc.T("Показать окно"); actionBtn.Visible = true; actionBtn.Invalidate();
            }
        }

        private void Attach(IntPtr h)
        {
            hwnd = h;
            origStyle = Native.GetStyle(h, Native.GWL_STYLE);
            origExStyle = Native.GetStyle(h, Native.GWL_EXSTYLE);
            if (tool.Overlay) { AttachOverlay(h); return; }

            long st = origStyle;
            st &= ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU |
                    Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX | Native.WS_POPUP |
                    Native.WS_BORDER | Native.WS_DLGFRAME);
            st |= Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS | Native.WS_CLIPCHILDREN;

            long ex = origExStyle;
            ex &= ~(Native.WS_EX_DLGMODALFRAME | Native.WS_EX_WINDOWEDGE |
                    Native.WS_EX_CLIENTEDGE | Native.WS_EX_STATICEDGE | Native.WS_EX_APPWINDOW);

            Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetParent(h, host.Handle);
            Native.SetStyle(h, Native.GWL_STYLE, st);
            Native.SetStyle(h, Native.GWL_EXSTYLE, ex);
            Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);

            status.Visible = false;
            FitChild();
            Native.ShowWindow(h, Native.SW_SHOW);
        }

        // ---- режим «закреплённое окно» ----
        private void AttachOverlay(IntPtr h)
        {
            overlay = true;
            Form f = FindForm();
            long st = origStyle;
            st &= ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX | Native.WS_BORDER | Native.WS_DLGFRAME);
            long ex = (origExStyle | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
            Native.ShowWindow(h, Native.SW_RESTORE);
            if (f != null) Native.SetStyle(h, Native.GWLP_HWNDPARENT, f.Handle.ToInt64());   // окно принадлежит оболочке
            Native.SetStyle(h, Native.GWL_STYLE, st);
            Native.SetStyle(h, Native.GWL_EXSTYLE, ex);
            Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            status.Visible = false;
            dockTimer.Start();
            SyncOverlay();
        }

        // Ставит окно ровно поверх области, прячет, если вкладка не видна.
        private void SyncOverlay()
        {
            if (!overlay || hwnd == IntPtr.Zero) return;
            if (!Native.IsWindow(hwnd)) { dockTimer.Stop(); return; }
            Form f = FindForm();
            bool show = host.IsHandleCreated && host.Visible && f != null && f.Visible && f.WindowState != FormWindowState.Minimized;
            if (!show) { if (Native.IsWindowVisible(hwnd)) Native.ShowWindow(hwnd, 0); return; }
            Rectangle r = host.RectangleToScreen(host.ClientRectangle);
            if (r.Width <= 0 || r.Height <= 0) return;
            Native.RECT cur;
            Native.GetWindowRect(hwnd, out cur);
            bool same = cur.Left == r.Left && cur.Top == r.Top && cur.Right - cur.Left == r.Width && cur.Bottom - cur.Top == r.Height;
            if (same && Native.IsWindowVisible(hwnd)) return;
            Native.SetWindowPos(hwnd, Native.HWND_TOP, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void FitChild()
        {
            if (overlay) { SyncOverlay(); return; }
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            int w = host.ClientSize.Width, h = host.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            if (tool.Stretch)
            {
                Native.MoveWindow(hwnd, 0, 0, w, h, true);
            }
            else
            {
                Native.RECT r;
                Native.GetWindowRect(hwnd, out r);
                int cw = r.Right - r.Left, ch = r.Bottom - r.Top;
                if (cw <= 0 || ch <= 0) { cw = 400; ch = 300; }
                int x = Math.Max(0, (w - cw) / 2);
                int y = Math.Max(0, (h - ch) / 3);
                Native.MoveWindow(hwnd, x, y, cw, ch, true);
            }
        }

        // Фокус встроенной программе при переключении на её вкладку.
        // (Никаких AttachThreadInput: склейка очередей ввода ломает переключение раскладок.)
        public override void Activated()
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            if (overlay) { SyncOverlay(); Native.SetForegroundWindow(hwnd); return; }
            Native.SetFocus(hwnd);
        }

        private void WatcherTick(object s, EventArgs e)
        {
            if (proc == null) return;
            if (SafeExited() && !detaching)
            {
                watcher.Stop();
                finder.Stop();
                // Некоторые лаунчеры (например, Java) завершают стартовый процесс,
                // но окно уже встроено - тогда его HWND ещё жив.
                if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd)) return;
                if (hwnd == IntPtr.Zero && tool.Embed && !detaching)
                {
                    // процесс завершился, так и не показав окно (например, передал запуск уже открытому экземпляру)
                    idle = true;
                    status.ForeColor = Theme.TextDim;
                    status.Text = tool.Name + Loc.T(" завершился, не открыв окно.\nВозможно, он уже запущен отдельно - закройте его и нажмите «Запустить».");
                    actionBtn.Caption = Loc.T("Запустить"); actionBtn.Visible = true; actionBtn.Invalidate();
                    return;
                }
                if (ToolExited != null) ToolExited(this, EventArgs.Empty);
            }
            else if (hwnd != IntPtr.Zero && !Native.IsWindow(hwnd) && !detaching)
            {
                watcher.Stop();
                if (ToolExited != null) ToolExited(this, EventArgs.Empty);
            }
        }

        // Вернуть окно в обычный вид (отдельное окно).
        public void Detach()
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            detaching = true;
            dockTimer.Stop();
            if (overlay) Native.SetStyle(hwnd, Native.GWLP_HWNDPARENT, 0);
            Native.SetParent(hwnd, IntPtr.Zero);
            Native.SetStyle(hwnd, Native.GWL_STYLE, origStyle);
            Native.SetStyle(hwnd, Native.GWL_EXSTYLE, origExStyle);
            Native.SetWindowPos(hwnd, IntPtr.Zero, 120, 120, 1100, 700,
                Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
            hwnd = IntPtr.Zero;
            status.Visible = true;
            status.Text = tool.Name + Loc.T(" открепился в отдельное окно.\nЗакройте вкладку или перезапустите её через меню.");
        }

        // Мягко закрыть приложение (даём ему шанс спросить о сохранении).
        public bool CloseTool()
        {
            watcher.Stop();
            finder.Stop();
            try
            {
                if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd))
                {
                    Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    // ждём до 4 секунд, пока приложение закроется само (может быть диалог сохранения)
                    for (int i = 0; i < 40; i++)
                    {
                        System.Threading.Thread.Sleep(100);
                        Application.DoEvents();
                        if (!Native.IsWindow(hwnd)) break;
                    }
                    if (Native.IsWindow(hwnd))
                    {
                        // окно осталось (например, спросило «Сохранить?») - не убиваем без ведома пользователя
                        watcher.Start();
                        return false;
                    }
                }
                if (proc != null && !SafeExited())
                {
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(1500)) proc.Kill();
                }
            }
            catch { }
            return true;
        }

        public void ForceKill()
        {
            watcher.Stop();
            finder.Stop();
            try { if (proc != null && !SafeExited()) proc.Kill(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { finder.Dispose(); watcher.Dispose(); dockTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    // Карточка инструмента на главной
    // ------------------------------------------------------------------
    internal class ToolCard : Control
    {
        private static readonly Font BigIcon = new Font(Theme.IconFace, 16f);
        private static readonly Font MidIcon = new Font(Theme.IconFace, 15f);
        public readonly ToolInfo Tool;      // null - карточка «Добавить»
        private Bitmap icon;
        private bool hover;

        public ToolCard(ToolInfo t)
        {
            Tool = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(290, 118);
            Margin = new Padding(0, 0, 14, 14);
            Cursor = Cursors.Hand;
            if (t != null && t.Exists)
            {
                try { using (Icon ic = Icon.ExtractAssociatedIcon(t.Path)) icon = ic.ToBitmap(); } catch { }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && icon != null) { icon.Dispose(); icon = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Tool == null)
            {
                using (GraphicsPath p = Theme.RoundRect(r, 8))
                using (Pen pen = new Pen(hover ? Theme.Accent : Theme.Border) { DashStyle = DashStyle.Dash })
                {
                    if (hover) using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillPath(b, p);
                    g.DrawPath(pen, p);
                }
                Theme.DrawText(g, Theme.GAdd, BigIcon, new Rectangle(0, 14, Width, 40),
                    hover ? Theme.Accent : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, Loc.T("Добавить инструмент"), Theme.Ui, new Rectangle(0, 58, Width, 24),
                    hover ? Theme.Text : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            using (GraphicsPath p = Theme.RoundRect(r, 8))
            {
                using (SolidBrush b = new SolidBrush(hover ? Theme.Bg3 : Theme.Bg2)) g.FillPath(b, p);
                using (Pen pen = new Pen(hover ? Theme.Accent : Theme.Border)) g.DrawPath(pen, p);
            }

            Rectangle ib = new Rectangle(16, 16, 40, 40);
            using (GraphicsPath p = Theme.RoundRect(ib, 8))
            using (SolidBrush b = new SolidBrush(Theme.Bg1)) g.FillPath(b, p);
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, new Rectangle(ib.X + 6, ib.Y + 6, 28, 28));
            }
            else
                Theme.DrawText(g, Theme.GApp, MidIcon, ib, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Theme.DrawText(g, Tool.Name, Theme.UiBold, new Rectangle(68, 14, Width - 80, 22), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Loc.T(Tool.Description ?? ""),Theme.UiSmall, new Rectangle(68, 36, Width - 80, 48), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

            bool ok = Tool.Exists;
            Color c = ok ? Theme.Good : Theme.Bad;
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, 16, Height - 24, 7, 7);
            Theme.DrawText(g, ok ? Loc.T("Готов к запуску") : Loc.T("Не найден: ") + System.IO.Path.GetFileName(Tool.Path), Theme.UiSmall,
                new Rectangle(30, Height - 30, Width - 44, 20), c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    internal class HeroHeader : Control
    {
        public HeroHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            Theme.DrawText(g, "Source Model Kit", Theme.Hero, new Rectangle(28, 14, Width - 40, 50), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Loc.T("Все утилиты Source SDK и файлы проекта — в одном окне. Откройте инструмент карточкой или разделите окно на области."),
                Theme.Ui, new Rectangle(30, 68, Width - 50, 22), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    internal class HintsStrip : Control
    {
        private static readonly string[][] Items =
        {
            new string[] { "F11", Loc.T("Полный экран на текущем мониторе") },
            new string[] { Loc.T("Ctrl + Пробел"), Loc.T("Развернуть область под курсором / вернуть") },
            new string[] { "Ctrl + \\", Loc.T("Разделить область (с Shift — по горизонтали)") },
            new string[] { Loc.T("Тяните уголок"), Loc.T("Угол заголовка области: разделение, как в Blender") },
            new string[] { Loc.T("Тяните вкладку"), Loc.T("Перенос вкладки в любую другую область") },
            new string[] { "Ctrl + W / T", Loc.T("Закрыть вкладку / новая вкладка проводника") },
        };

        public HintsStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 28, 0, Width - 28, 0);
            Theme.DrawText(g, Loc.T("Горячие клавиши и жесты"), Theme.UiBold, new Rectangle(28, 12, 400, 22), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int cols = Math.Max(1, Math.Min(2, (Width - 56) / 420));
            int colW = (Width - 56) / cols;
            for (int i = 0; i < Items.Length; i++)
            {
                int cx = 28 + (i % cols) * colW, cy = 40 + (i / cols) * 24;
                Rectangle key = new Rectangle(cx, cy, 120, 20);
                using (GraphicsPath gp = Theme.RoundRect(key, 4))
                using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillPath(b, gp);
                Theme.DrawText(g, Items[i][0], Theme.UiSmall, key, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, Items[i][1], Theme.UiSmall, new Rectangle(cx + 130, cy, colW - 140, 20), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // ------------------------------------------------------------------
    // Главная
    // ------------------------------------------------------------------
    internal class HomePane : Pane
    {
        private readonly IShellHost host;
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        private readonly HintsStrip hints = new HintsStrip();
        private readonly ContextMenuStrip cm = new ContextMenuStrip();
        private ToolInfo menuTool;

        public override string Title { get { return Loc.T("Главная"); } }
        public override string IconGlyph { get { return Theme.GHome; } }
        public override string Serialize() { return "H"; }

        public HomePane(IShellHost h)
        {
            host = h;
            HeroHeader hero = new HeroHeader();
            hero.Dock = DockStyle.Top; hero.Height = 104;

            FlatButton openExplorer = new FlatButton(Theme.GFolderOpen, Loc.T("Новый проводник"), Loc.T("Открыть проводник в этой области"));
            openExplorer.BaseRole = 1; openExplorer.Primary = true;
            openExplorer.Click += delegate { host.NewExplorerTab(null, Area); };
            openExplorer.SetBounds(28, 100, openExplorer.Width + 8, 30);
            hero.Controls.Add(openExplorer);
            FlatButton openModel = new FlatButton(Theme.GApp, Loc.T("Просмотр модели"), Loc.T("Открыть просмотрщик .mdl в этой области"));
            openModel.BaseRole = 1;
            openModel.Click += delegate { host.OpenModel(null, Area); };
            openModel.SetBounds(28 + openExplorer.Width + 16, 100, openModel.Width + 8, 30);
            hero.Controls.Add(openModel);
            hero.Height = 144;

            hints.Dock = DockStyle.Bottom; hints.Height = 150;

            cards.Dock = DockStyle.Fill;
            cards.AutoScroll = true;
            cards.BackColor = Theme.Bg1;
            cards.Padding = new Padding(28, 8, 14, 8);
            cards.HandleCreated += delegate { Theme.DarkControl(cards); };

            Controls.Add(cards);
            Controls.Add(hints);
            Controls.Add(hero);

            Theme.ApplyMenu(cm);
            ToolStripMenuItem rm = new ToolStripMenuItem(Loc.T("Удалить из списка"));
            rm.Click += delegate { if (menuTool != null) host.RemoveCustomTool(menuTool); };
            cm.Items.Add(rm);

            Rebuild();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            hints.Visible = Height > 520;
        }

        public void Rebuild()
        {
            cards.SuspendLayout();
            Control[] old = new Control[cards.Controls.Count];
            cards.Controls.CopyTo(old, 0);
            cards.Controls.Clear();
            foreach (Control c in old) c.Dispose();
            cards.Controls.Clear();
            foreach (ToolInfo t in host.Tools)
            {
                ToolCard card = new ToolCard(t);
                ToolInfo captured = t;
                card.MouseUp += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) host.OpenTool(captured, null, Area);
                    else if (e.Button == MouseButtons.Right && !captured.Builtin)
                    {
                        menuTool = captured;
                        cm.Show(card, e.Location);
                    }
                };
                cards.Controls.Add(card);
            }
            ToolCard add = new ToolCard(null);
            add.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) host.AddToolDialog(); };
            cards.Controls.Add(add);
            cards.ResumeLayout(true);
        }
    }
}
