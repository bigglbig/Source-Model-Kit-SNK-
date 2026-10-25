// Единая тема оболочки: палитра, шрифты и базовые контролы в одном стиле.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SourceSDKHub
{
    internal static class Theme
    {
        // Роли цветов (значения задаёт Apply):
        // Bg0 - фон окна и «щели» между областями; Bg1 - содержимое областей;
        // Bg2 - заголовки и панели; Bg3 - наведение; Bg4 - нажатие/неактивное выделение.
        public static Color Bg0, Bg1, Bg2, Bg3, Bg4, Field;
        public static Color Border, Text, TextDim, Accent, AccentDim, OnAccent, Good, Bad;
        public static bool IsDark = true;
        public static string Key = "dark";
        public static int MaxRadius = 99;     // 0 - строгие прямые углы (стиль Valve)

        public static readonly string[][] Themes =
        {
            new string[] { "dark", Loc.T("Тёмная") },
            new string[] { "light", Loc.T("Светлая (серая, как GitHub)") },
            new string[] { "valve", Loc.T("Оригинальная (Valve, хаки)") },
        };

        public static void Apply(string key)
        {
            IsDark = true; MaxRadius = 99; OnAccent = Color.White;
            switch (key)
            {
                case "light":
                    IsDark = false;
                    Bg0 = Color.FromArgb(222, 227, 233); Bg1 = Color.FromArgb(246, 248, 250);
                    Bg2 = Color.FromArgb(234, 238, 242); Bg3 = Color.FromArgb(217, 223, 230);
                    Bg4 = Color.FromArgb(200, 208, 217); Field = Color.White;
                    Border = Color.FromArgb(208, 215, 222);
                    Text = Color.FromArgb(36, 41, 47); TextDim = Color.FromArgb(87, 96, 106);
                    Accent = Color.FromArgb(9, 105, 218); AccentDim = Color.FromArgb(205, 229, 252);
                    Good = Color.FromArgb(26, 127, 55); Bad = Color.FromArgb(207, 34, 46);
                    break;
                case "valve":
                    MaxRadius = 0;
                    Bg0 = Color.FromArgb(62, 70, 55); Bg1 = Color.FromArgb(76, 88, 68);
                    Bg2 = Color.FromArgb(89, 103, 79); Bg3 = Color.FromArgb(103, 120, 92);
                    Bg4 = Color.FromArgb(118, 138, 105); Field = Color.FromArgb(58, 66, 51);
                    Border = Color.FromArgb(126, 140, 112);
                    Text = Color.FromArgb(236, 241, 228); TextDim = Color.FromArgb(184, 196, 170);
                    Accent = Color.FromArgb(196, 181, 80); AccentDim = Color.FromArgb(136, 124, 48);
                    OnAccent = Color.FromArgb(40, 46, 32);
                    Good = Color.FromArgb(190, 235, 120); Bad = Color.FromArgb(240, 140, 118);
                    break;
                default:
                    key = "dark";
                    Bg0 = Color.FromArgb(16, 17, 21); Bg1 = Color.FromArgb(26, 28, 34);
                    Bg2 = Color.FromArgb(34, 37, 45); Bg3 = Color.FromArgb(46, 50, 61);
                    Bg4 = Color.FromArgb(60, 66, 81); Field = Bg0;
                    Border = Color.FromArgb(52, 56, 68);
                    Text = Color.FromArgb(226, 230, 238); TextDim = Color.FromArgb(139, 146, 163);
                    Accent = Color.FromArgb(96, 142, 255); AccentDim = Color.FromArgb(44, 66, 124);
                    Good = Color.FromArgb(108, 205, 140); Bad = Color.FromArgb(240, 112, 112);
                    break;
            }
            Key = key;
        }

        public static Color Role(int r) { return r == 0 ? Bg0 : r == 1 ? Bg1 : Bg2; }

        // Живая перекраска всего дерева контролов после смены темы.
        public static void Reskin(Control c)
        {
            if (c is Pane) { c.BackColor = Bg1; c.ForeColor = Text; }
            else if (c is Area) c.BackColor = Bg1;
            else if (c is Workspace) c.BackColor = Bg0;
            else if (c is Form) { c.BackColor = Bg0; c.ForeColor = Text; }
            else if (c is DarkInput) { DarkInput d = (DarkInput)c; d.Box.BackColor = Field; d.Box.ForeColor = Text; }
            else if (c is FastListView || c is FastTreeView)
            {
                c.BackColor = Bg1; c.ForeColor = Text;
                if (c.IsHandleCreated) DarkControl(c);
                FastListView lv = c as FastListView;
                if (lv != null && lv.IsHandleCreated)
                {
                    IntPtr hdr = Native.SendMessage(lv.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
                    if (hdr != IntPtr.Zero) Native.SetWindowTheme(hdr, IsDark ? "DarkMode_ItemsView" : "ItemsView", null);
                }
            }
            else if (c is SplitContainer)
            {
                SplitContainer sc = (SplitContainer)c;
                sc.BackColor = Border; sc.Panel1.BackColor = Bg1; sc.Panel2.BackColor = Bg1;
            }
            else if (c is FlowLayoutPanel) { c.BackColor = Bg1; if (c.IsHandleCreated) DarkControl(c); }
            else if (c is Label) { c.BackColor = Bg1; c.ForeColor = TextDim; }
            else if (c is Panel) c.BackColor = Bg1;
            foreach (Control ch in c.Controls) Reskin(ch);
            c.Invalidate();
        }

        public static readonly Font Ui;
        public static readonly Font UiBold;
        public static readonly Font UiSmall;
        public static readonly Font Icon;
        public static readonly Font IconSmall;
        public static readonly Font Hero;
        public static readonly string IconFace;

        static Theme()
        {
            Apply("dark");
            IconFace = HasFont("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : "Segoe UI Symbol";
            Ui = new Font("Segoe UI", 9f);
            UiBold = new Font("Segoe UI Semibold", 9.5f);
            UiSmall = new Font("Segoe UI", 8.25f);
            Icon = new Font(IconFace, 10f);
            IconSmall = new Font(IconFace, 8f);
            Hero = new Font("Segoe UI Light", 26f);
        }

        private static bool HasFont(string name)
        {
            foreach (FontFamily f in FontFamily.Families) if (f.Name == name) return true;
            return false;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static int Bgr(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Тёмный заголовок окна (Windows 10 1809+ / Windows 11).
        public static void DarkenWindow(IntPtr h)
        {
            try
            {
                int v = IsDark ? 1 : 0;
                if (Native.DwmSetWindowAttribute(h, 20, ref v, 4) != 0) Native.DwmSetWindowAttribute(h, 19, ref v, 4);
                int cap = Bgr(Bg0), txt = Bgr(Text), brd = Bgr(Border);
                Native.DwmSetWindowAttribute(h, 35, ref cap, 4);  // цвет заголовка (Win11)
                Native.DwmSetWindowAttribute(h, 36, ref txt, 4);
                Native.DwmSetWindowAttribute(h, 34, ref brd, 4);
            }
            catch { }
        }

        // Тёмные нативные полосы прокрутки, заголовки списков и т.п.
        public static void DarkControl(Control c)
        {
            try { Native.SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            radius = Math.Min(radius, MaxRadius);
            int d = radius * 2;
            if (d <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawText(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, s, f, r, c,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | extra);
        }

        public static void ApplyMenu(ContextMenuStrip m)
        {
            m.Font = Ui;
            m.Renderer = new DarkRenderer();
            m.BackColor = Bg2;
            m.ForeColor = Text;
            m.ShowImageMargin = false;
        }

        // Глифы Segoe MDL2 Assets
        public const string GBack = "";
        public const string GForward = "";
        public const string GUp = "";
        public const string GRefresh = "";
        public const string GAdd = "";
        public const string GClose = "";
        public const string GMore = "";
        public const string GHome = "";
        public const string GFolder = "";
        public const string GFolderOpen = "";
        public const string GApp = "";
        public const string GFull = "";
        public const string GUnfull = "";
        public const string GMonitor = "";
        public const string GLayout = "";
        public const string GSearch = "";
        public const string GCheckOn = "";
        public const string GCheckOff = "";
        public const string GView = "";
        public const string GPalette = "\uE790";
        public const string GPin = "\uE734";
        public const string GPinned = "\uE735";
        public const string GBell = "\uEA8F";
        public const string GChevron = "";
    }

    // ---------------------------------------------------------------
    // Тёмные меню
    // ---------------------------------------------------------------
    internal class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Bg2; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Bg2; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Bg2; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Bg2; } }
        public override Color MenuItemSelected { get { return Theme.Bg4; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Bg4; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Bg4; } }
        public override Color MenuItemBorder { get { return Theme.Bg4; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color CheckBackground { get { return Theme.AccentDim; } }
        public override Color CheckSelectedBackground { get { return Theme.AccentDim; } }
        public override Color CheckPressedBackground { get { return Theme.AccentDim; } }
    }

    internal class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextDim;
            base.OnRenderArrow(e);
        }
    }

    // ---------------------------------------------------------------
    // Подсказки
    // ---------------------------------------------------------------
    internal static class Tips
    {
        private static ToolTip tip;

        private static void Ensure()
        {
            if (tip != null) return;
            tip = new ToolTip();
            tip.OwnerDraw = true;
            tip.InitialDelay = 450;
            tip.ShowAlways = true;
            tip.Popup += delegate(object s, PopupEventArgs e)
            {
                Size sz = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), Theme.Ui);
                e.ToolTipSize = new Size(sz.Width + 16, sz.Height + 10);
            };
            tip.Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Bg3)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Ui, e.Bounds, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
        }

        public static void Set(Control c, string text)
        {
            Ensure();
            tip.SetToolTip(c, text);
        }
    }

    // ---------------------------------------------------------------
    // Плоская кнопка (иконка + текст)
    // ---------------------------------------------------------------
    internal class FlatButton : Control
    {
        public string Glyph;
        public string Caption;
        public bool Primary;
        public bool Toggle;       // клик переключает Toggled
        public bool Toggled;
        public bool LeftAlign;
        public bool Square;                      // прямоугольная заливка (кнопки окна)
        public Color HoverBg = Color.Empty, HoverFg = Color.Empty;
        public int Badge;          // число в кружке (уведомления)
        public int BaseRole = 2;
        private Color Base { get { return Theme.Role(BaseRole); } }
        private bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Ui;
            Height = 28;
            TabStop = false;
        }

        public FlatButton(string glyph, string caption, string tip) : this()
        {
            Glyph = glyph; Caption = caption;
            if (!string.IsNullOrEmpty(tip)) Tips.Set(this, tip);
            FitWidth();
        }

        public void FitWidth()
        {
            int w = 16;
            if (!string.IsNullOrEmpty(Glyph)) w += 16;
            if (!string.IsNullOrEmpty(Caption))
            {
                w += TextRenderer.MeasureText(Caption, Theme.Ui, new Size(2000, 40), TextFormatFlags.NoPadding).Width;
                if (!string.IsNullOrEmpty(Glyph)) w += 6;
            }
            Width = Math.Max(w, 28);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnClick(EventArgs e)
        {
            if (Toggle) Toggled = !Toggled;
            Invalidate();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Base);
            Color bg = Base;
            if (Primary) bg = down ? Theme.Mix(Theme.Accent, Color.Black, 0.25) : hover ? Theme.Mix(Theme.Accent, Color.White, 0.15) : Theme.Accent;
            else if (down) bg = Theme.Bg4;
            else if (hover) bg = HoverBg.IsEmpty ? Theme.Bg3 : HoverBg;
            else if (Toggled && !Toggle) bg = Theme.Bg3;

            if (bg != Base)
            {
                if (Square) using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, 0, 0, Width, Height);
                else
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 4))
                    using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                }
            }

            if (Badge > 0)
            {
                Rectangle br = new Rectangle(Width - 15, 1, 14, 14);
                using (SolidBrush bb = new SolidBrush(Theme.Accent)) g.FillEllipse(bb, br);
                Theme.DrawText(g, Badge > 9 ? "9+" : Badge.ToString(), Theme.IconSmall, br, Theme.OnAccent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Color fg = !Enabled ? Theme.TextDim : Primary ? Theme.OnAccent : (Toggled ? Theme.Accent : Theme.Text);
            if (hover && Enabled && !HoverFg.IsEmpty) fg = HoverFg;
            bool hasG = !string.IsNullOrEmpty(Glyph), hasC = !string.IsNullOrEmpty(Caption);
            int gw = hasG ? 16 : 0;
            int cw = hasC ? TextRenderer.MeasureText(Caption, Theme.Ui, new Size(2000, 40), TextFormatFlags.NoPadding).Width : 0;
            int total = gw + cw + (hasG && hasC ? 6 : 0);
            int x = LeftAlign ? 8 : Math.Max(4, (Width - total) / 2);
            if (hasG)
            {
                Theme.DrawText(g, Glyph, Theme.Icon, new Rectangle(x, 0, gw, Height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += gw + 6;
            }
            if (hasC)
                Theme.DrawText(g, Caption, Theme.Ui, new Rectangle(x, 0, Width - x - 2, Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------------------------------------------------------------
    // Поле ввода в общем стиле
    // ---------------------------------------------------------------
    internal class DarkInput : Control
    {
        public readonly TextBox Box = new TextBox();
        public string Hint = "";
        public string Glyph;

        public DarkInput()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 28;
            Font = Theme.Ui;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Field;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Ui;
            Box.Enter += delegate { Invalidate(); };
            Box.Leave += delegate { Invalidate(); };
            Box.TextChanged += delegate { Invalidate(); };
            Controls.Add(Box);
        }

        public override string Text
        {
            get { return Box.Text; }
            set { Box.Text = value; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int left = string.IsNullOrEmpty(Glyph) ? 8 : 28;
            Box.SetBounds(left, Math.Max(0, (Height - Box.Height) / 2), Math.Max(10, Width - left - 8), Box.Height);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Field);
            using (Pen p = new Pen(Box.Focused ? Theme.Accent : Theme.Border))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (!string.IsNullOrEmpty(Glyph))
                Theme.DrawText(g, Glyph, Theme.IconSmall, new Rectangle(8, 0, 16, Height), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Box.Text.Length == 0 && !Box.Focused && Hint.Length > 0)
                Theme.DrawText(g, Hint, Theme.Ui, new Rectangle(Box.Left, 0, Box.Width, Height), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnGotFocus(EventArgs e) { Box.Focus(); base.OnGotFocus(e); }
    }

    // ---------------------------------------------------------------
    // Тонкая строка состояния
    // ---------------------------------------------------------------
    internal class StatusLine : Control
    {
        private string left = "", right = "";
        public int FillRole = 0;
        public int RightPad;            // место справа под переключатель языка

        public StatusLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 24;
            Font = Theme.UiSmall;
        }

        public string LeftText { get { return left; } set { if (left != value) { left = value; Invalidate(); } } }
        public string RightText { get { return right; } set { if (right != value) { right = value; Invalidate(); } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Role(FillRole));
            int rw = TextRenderer.MeasureText(right, Theme.UiSmall, new Size(2000, 40), TextFormatFlags.NoPadding).Width;
            Theme.DrawText(g, left, Theme.UiSmall, new Rectangle(10, 0, Math.Max(10, Width - rw - 30 - RightPad), Height), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, right, Theme.UiSmall, new Rectangle(Width - rw - 10 - RightPad, 0, rw + 2, Height), Theme.TextDim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // Переключатель языка интерфейса RUS / ENG
    internal class LangSwitch : Control
    {
        private static readonly string[] Codes = { "ru", "en" }, Labels = { "RUS", "ENG" };
        private int hover = -1;
        public event Action<string> Picked;

        public LangSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = 88; Cursor = Cursors.Hand;
            Tips.Set(this, Loc.T("Язык интерфейса (RUS / ENG)"));
        }

        private Rectangle Seg(int i) { return new Rectangle(6 + i * 40, 2, 38, Math.Max(10, Height - 4)); }

        private int HitTest(Point p)
        {
            for (int i = 0; i < Codes.Length; i++) if (Seg(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Role(0));
            for (int i = 0; i < Codes.Length; i++)
            {
                Rectangle r = Seg(i);
                bool act = Codes[i] == Loc.Code;
                if (act) using (SolidBrush b = new SolidBrush(Theme.AccentDim)) g.FillRectangle(b, r);
                else if (i == hover) using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, r);
                Theme.DrawText(g, Labels[i], act ? Theme.UiBold : Theme.UiSmall, r, act ? Theme.Accent : Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && h >= 0 && Picked != null) Picked(Codes[h]);
            base.OnMouseDown(e);
        }
    }

    // Список без мерцания и в тёмной теме
    internal class FastListView : ListView
    {
        public FastListView()
        {
            DoubleBuffered = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Bg1;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            ShowItemToolTips = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkControl(this);
            try
            {
                IntPtr hdr = Native.SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
                if (hdr != IntPtr.Zero) Native.SetWindowTheme(hdr, Theme.IsDark ? "DarkMode_ItemsView" : "ItemsView", null);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
            base.WndProc(ref m);
        }
    }

    internal class FastTreeView : TreeView
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x8000; // TVS_NOHSCROLL
                return cp;
            }
        }

        public FastTreeView()
        {
            DoubleBuffered = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Bg1;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            HideSelection = false;
            ShowLines = false;
            ItemHeight = 22;
            FullRowSelect = false;
            HotTracking = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkControl(this);
        }
    }
}
