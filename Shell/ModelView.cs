// Просмотр моделей Source (.mdl) прямо в оболочке: текстуры, свет, бодигруппы, скины,
// анимации, скелет / хитбоксы / физика, автообновление текстур и модели при сохранении файлов.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SourceSDKHub
{
    // ------------------------------------------------------------------
    // Контролы боковой панели
    // ------------------------------------------------------------------
    internal interface IWheelTarget { bool TryWheel(int delta); }

    internal class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = false;
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Form f = FindForm();
            if (f != null && f == Form.ActiveForm && !(f.ActiveControl is TextBox)) Focus();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Control c = GetChildAtPoint(PointToClient(Cursor.Position));
            IWheelTarget t = c as IWheelTarget;
            if (t != null && t.TryWheel(e.Delta)) return;
            base.OnMouseWheel(e);
        }
    }

    internal class Stepper : Control
    {
        public string Caption = "";
        public string[] Items = new string[0];
        private int index;
        public event EventHandler Changed;
        private int hover;   // -1 влево, 1 вправо, 0 нет
        public int CaptionWidth = -1;   // -1 авто, 0 без подписи

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 30; Cursor = Cursors.Hand;
        }

        public int Index
        {
            get { return index; }
            set { if (value < 0 || value >= Items.Length || value == index) return; index = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        public void SetIndexSilently(int i) { index = Math.Max(0, Math.Min(i, Items.Length - 1)); Invalidate(); }

        private int ArrowW { get { return 26; } }
        private int CapW { get { return CaptionWidth >= 0 ? CaptionWidth : Math.Min(110, Width / 3); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cw = CapW;
            if (Caption.Length > 0 && cw > 0)
                Theme.DrawText(g, Caption, Theme.Ui, new Rectangle(0, 0, cw, Height), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Rectangle box = new Rectangle(cw, 2, Width - cw - 1, Height - 4);
            using (GraphicsPath p = Theme.RoundRect(box, 4))
            using (SolidBrush b = new SolidBrush(Parent != null && Parent.BackColor == Theme.Bg2 ? Theme.Bg1 : Theme.Bg2)) g.FillPath(b, p);
            if (hover != 0)
            {
                Rectangle ar = hover < 0 ? new Rectangle(box.X, box.Y, ArrowW, box.Height) : new Rectangle(box.Right - ArrowW, box.Y, ArrowW, box.Height);
                using (SolidBrush b = new SolidBrush(Theme.Bg3)) g.FillRectangle(b, ar);
            }
            Theme.DrawText(g, Theme.GBack, Theme.IconSmall, new Rectangle(box.X, box.Y, ArrowW, box.Height), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Theme.GForward, Theme.IconSmall, new Rectangle(box.Right - ArrowW, box.Y, ArrowW, box.Height), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            string t = Items.Length > 0 ? Items[index] : "—";
            Theme.DrawText(g, t, Theme.Ui, new Rectangle(box.X + ArrowW, box.Y, box.Width - 2 * ArrowW, box.Height), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private int Zone(Point p)
        {
            int cw = CapW;
            if (p.X < cw) return 0;
            if (p.X < cw + ArrowW) return -1;
            if (p.X > Width - ArrowW) return 1;
            return 2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = Zone(e.Location); int h = z == -1 ? -1 : z == 1 ? 1 : 0;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int z = Zone(e.Location);
            if (z == -1) Index = (index - 1 + Items.Length) % Math.Max(1, Items.Length);
            else if (z == 1) Index = (index + 1) % Math.Max(1, Items.Length);
            else if (z == 2 && Items.Length > 1)
            {
                ContextMenuStrip m = new ContextMenuStrip();
                Theme.ApplyMenu(m);
                m.ShowCheckMargin = true;
                for (int i = 0; i < Items.Length; i++)
                {
                    int ii = i;
                    ToolStripMenuItem mi = new ToolStripMenuItem(Items[i]);
                    mi.Checked = i == index;
                    mi.Click += delegate { Index = ii; };
                    m.Items.Add(mi);
                }
                m.Show(this, new Point(CapW, Height));
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Items.Length > 1) Index = (index + (e.Delta > 0 ? -1 : 1) + Items.Length) % Items.Length;
            base.OnMouseWheel(e);
        }
    }

    internal class MiniSlider : Control
    {
        public string Caption = "";
        public Func<float, string> Format;
        private float val = 0.5f;
        public event EventHandler Changed;
        private bool drag;

        public MiniSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 28; Cursor = Cursors.Hand;
        }

        public float Value
        {
            get { return val; }
            set { value = Math.Max(0f, Math.Min(1f, value)); if (value == val) return; val = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        public void SetSilently(float v) { val = Math.Max(0f, Math.Min(1f, v)); Invalidate(); }

        private int CapW { get { return Math.Min(110, Width / 3); } }
        private Rectangle Track { get { int cw = CapW; return new Rectangle(cw + 6, Height / 2 - 2 - (Format != null ? 4 : 0), Width - cw - 18, 4); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cw = CapW;
            Theme.DrawText(g, Caption, Theme.Ui, new Rectangle(0, 0, cw, Height), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Rectangle t = Track;
            using (SolidBrush b = new SolidBrush(Theme.Bg3)) g.FillRectangle(b, t);
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, t.X, t.Y, (int)(t.Width * val), t.Height);
            int kx = t.X + (int)(t.Width * val);
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, kx - 6, t.Y + 2 - 6, 12, 12);
            if (Format != null)
                Theme.DrawText(g, Format(val), Theme.UiSmall, new Rectangle(t.X, Height - 14, t.Width, 12), Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        private void SetFromX(int x) { Rectangle t = Track; Value = (float)(x - t.X) / Math.Max(1, t.Width); }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; base.OnMouseUp(e); }
    }

    // Заголовок секции, при желании сворачиваемой
    internal class CollapseHeader : Control
    {
        public bool Collapsible, Open = true;
        public string Badge = "";
        public event EventHandler Toggled;
        private bool hover;

        public CollapseHeader(string text, bool collapsible)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text; Collapsible = collapsible; Height = 28;
            if (collapsible) Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Collapsible && e.Button == MouseButtons.Left) { Open = !Open; Invalidate(); if (Toggled != null) Toggled(this, EventArgs.Empty); }
            base.OnMouseUp(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            int x = 0;
            if (Collapsible)
            {
                Theme.DrawText(g, Open ? Theme.GChevron : Theme.GForward, Theme.IconSmall, new Rectangle(0, 6, 16, 18),
                    hover ? Theme.Text : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x = 20;
            }
            Theme.DrawText(g, Text.ToUpperInvariant(), Theme.UiSmall, new Rectangle(x, 6, Width - x - 80, 18), hover && Collapsible ? Theme.Text : Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (Badge.Length > 0)
                Theme.DrawText(g, Badge, Theme.UiSmall, new Rectangle(Width - 90, 6, 90, 18), Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 2, Width, Height - 2);
        }
    }

    internal class InfoLabel : Control
    {
        public InfoLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            Theme.DrawText(e.Graphics, Text, Theme.UiSmall, ClientRectangle, Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    // Список материалов с прокруткой (до 64 штук)
    internal class MatList : Control, IWheelTarget
    {
        private const int RowH = 40;
        public const int MaxViewport = 240;
        public List<MatInfo> Items = new List<MatInfo>();
        public event Action<int, Point> RowMenu;
        private int hover = -1, scroll;

        public MatList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        private int Content { get { return Items.Count * RowH; } }
        private int MaxScroll { get { return Math.Max(0, Content - Height); } }

        public void Refit()
        {
            Height = Math.Max(RowH, Math.Min(MaxViewport, Content));
            scroll = Math.Min(scroll, MaxScroll);
            Invalidate();
        }

        public bool TryWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int ns = Math.Max(0, Math.Min(MaxScroll, scroll - Math.Sign(delta) * RowH));
            if (ns == scroll) return false;
            scroll = ns; Invalidate();
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sbw = MaxScroll > 0 ? 8 : 0;
            for (int i = 0; i < Items.Count; i++)
            {
                MatInfo m = Items[i];
                Rectangle r = new Rectangle(0, i * RowH - scroll, Width - sbw, RowH);
                if (r.Bottom < 0 || r.Y > Height) continue;
                if (i == hover) using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, r);
                Color c = m.Ok ? Theme.Good : Theme.Bad;
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, 6, r.Y + 8, 8, 8);
                Theme.DrawText(g, m.TexName + (m.Shader.Length > 0 ? "   " + m.Shader : ""), Theme.Ui, new Rectangle(22, r.Y + 2, r.Width - 26, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                string sub = m.Ok ? (m.Image.Width + "×" + m.Image.Height + (m.AlphaTest ? "  alphatest" : "") + (m.Translucent ? "  translucent" : "")) : (m.Error ?? Loc.T("нет данных"));
                Theme.DrawText(g, sub, Theme.UiSmall, new Rectangle(22, r.Y + 20, r.Width - 26, 16), m.Ok ? Theme.TextDim : Theme.Bad, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            if (sbw > 0)
            {
                using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, Width - sbw, 0, sbw, Height);
                int th = Math.Max(24, Height * Height / Content);
                int ty = (int)((Height - th) * (scroll / (float)MaxScroll));
                using (GraphicsPath p = Theme.RoundRect(new Rectangle(Width - sbw + 1, ty, sbw - 2, th), 3))
                using (SolidBrush b = new SolidBrush(Theme.Bg4)) g.FillPath(b, p);
            }
        }

        private int RowAt(Point p) { int i = (p.Y + scroll) / RowH; return i >= 0 && i < Items.Count ? i : -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int r = RowAt(e.Location);
            if (r != hover) { hover = r; Invalidate(); Tips.Set(this, r >= 0 ? (Items[r].VmtPath ?? Items[r].Error ?? "") : ""); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int r = RowAt(e.Location);
            if (r >= 0 && RowMenu != null) RowMenu(r, Cursor.Position);
            base.OnMouseUp(e);
        }
    }

    internal class HintPanel : Control
    {
        public string Title = "", Body = "";
        public bool Bad;
        private static readonly Font BigGlyph = new Font(Theme.IconFace, 28f);
        public HintPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            Theme.DrawText(g, Theme.GApp, BigGlyph, new Rectangle(0, Height / 2 - 90, Width, 50), Bad ? Theme.Bad : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Title, Theme.UiBold, new Rectangle(20, Height / 2 - 34, Width - 40, 24), Bad ? Theme.Bad : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Body, Theme.Ui, new Rectangle(40, Height / 2 - 6, Width - 80, 120), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    // ------------------------------------------------------------------
    // Элементы таймлайна
    // ------------------------------------------------------------------
    internal class Scrubber : Control
    {
        public int Frames = 1;
        private float val;
        public event EventHandler Scrubbed;    // пользователь тянет ползунок
        private bool drag;

        public Scrubber()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 30; Cursor = Cursors.Hand;
        }

        public float Value
        {
            get { return val; }
            set { value = Math.Max(0f, Math.Min(1f, value)); if (value == val) return; val = value; Invalidate(); }
        }

        private Rectangle Track { get { return new Rectangle(8, Height / 2 - 3, Width - 16, 6); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle t = Track;
            using (GraphicsPath p = Theme.RoundRect(t, 3)) using (SolidBrush b = new SolidBrush(Theme.Bg0)) g.FillPath(b, p);
            int fw = (int)(t.Width * val);
            if (fw > 0) using (GraphicsPath p = Theme.RoundRect(new Rectangle(t.X, t.Y, fw, t.Height), 3)) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            if (Frames > 1 && Frames <= 400)
            {
                int step = Frames > 120 ? 10 : Frames > 60 ? 5 : 1;
                using (Pen p = new Pen(Theme.Border))
                    for (int f = 0; f < Frames; f += step)
                    {
                        int x = t.X + (int)(t.Width * (f / (float)(Frames - 1)));
                        g.DrawLine(p, x, t.Bottom + 3, x, t.Bottom + 6);
                    }
            }
            int kx = t.X + fw;
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, kx - 7, Height / 2 - 7, 14, 14);
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, kx - 4, Height / 2 - 4, 8, 8);
        }

        private void SetFromX(int x)
        {
            Rectangle t = Track;
            float v = (float)(x - t.X) / Math.Max(1, t.Width);
            val = Math.Max(0f, Math.Min(1f, v));
            Invalidate();
            if (Scrubbed != null) Scrubbed(this, EventArgs.Empty);
        }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; base.OnMouseUp(e); }
    }

    // Поле «текущая секвенция»: имя, активность, длительность
    internal class SeqBox : Control
    {
        public string Caption = "", Detail = "";
        private bool hover;
        public SeqBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 30; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.RoundRect(r, 5))
            {
                using (SolidBrush b = new SolidBrush(hover ? Theme.Bg3 : Theme.Bg1)) g.FillPath(b, p);
                using (Pen pen = new Pen(hover ? Theme.Accent : Theme.Border)) g.DrawPath(pen, p);
            }
            Theme.DrawText(g, Theme.GChevron, Theme.IconSmall, new Rectangle(Width - 26, 0, 20, Height), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int dw = Detail.Length > 0 ? Math.Min(Width / 2, TextRenderer.MeasureText(Detail, Theme.UiSmall).Width + 8) : 0;
            Theme.DrawText(g, Caption, Theme.UiBold, new Rectangle(10, 0, Width - 40 - dw, Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (dw > 0) Theme.DrawText(g, Detail, Theme.UiSmall, new Rectangle(Width - 30 - dw, 0, dw, Height), Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------
    // Выпадающий список с поиском (для секвенций)
    // ------------------------------------------------------------------
    internal class PickItem { public string Text = "", Sub = ""; public object Tag; }

    internal class PickList : Control
    {
        private const int RowH = 30;
        public List<PickItem> All = new List<PickItem>();
        public List<PickItem> View = new List<PickItem>();
        public int Selected;
        public object Current;
        private int hover = -1, scroll;
        public event Action<PickItem> Picked;

        public PickList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Filter(string text)
        {
            View = new List<PickItem>();
            string t = (text ?? "").Trim();
            foreach (PickItem it in All)
                if (t.Length == 0 || it.Text.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0 || it.Sub.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0) View.Add(it);
            Selected = 0;
            if (Current != null) for (int i = 0; i < View.Count; i++) if (View[i].Tag == Current) { Selected = i; break; }
            scroll = 0; EnsureVisible(); Invalidate();
        }

        private int Max { get { return Math.Max(0, View.Count * RowH - Height); } }

        public void Step(int d)
        {
            if (View.Count == 0) return;
            Selected = Math.Max(0, Math.Min(View.Count - 1, Selected + d));
            EnsureVisible(); Invalidate();
        }

        private void EnsureVisible()
        {
            int top = Selected * RowH;
            if (top < scroll) scroll = top;
            else if (top + RowH > scroll + Height) scroll = top + RowH - Height;
            scroll = Math.Max(0, Math.Min(Max, scroll));
        }

        public void PickSelected() { if (Selected >= 0 && Selected < View.Count && Picked != null) Picked(View[Selected]); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); EnsureVisible(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            if (View.Count == 0)
                Theme.DrawText(g, Loc.T("Ничего не найдено"), Theme.Ui, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int sbw = Max > 0 ? 8 : 0;
            for (int i = 0; i < View.Count; i++)
            {
                int y = i * RowH - scroll;
                if (y + RowH < 0 || y > Height) continue;
                Rectangle r = new Rectangle(0, y, Width - sbw, RowH);
                bool cur = View[i].Tag == Current;
                if (i == Selected) using (SolidBrush b = new SolidBrush(Theme.AccentDim)) g.FillRectangle(b, r);
                else if (i == hover) using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, r);
                int sw = View[i].Sub.Length > 0 ? Math.Min(190, TextRenderer.MeasureText(View[i].Sub, Theme.UiSmall).Width + 6) : 0;
                Theme.DrawText(g, View[i].Text, cur ? Theme.UiBold : Theme.Ui, new Rectangle(12, y, r.Width - 20 - sw, RowH), cur ? Theme.Accent : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                if (sw > 0) Theme.DrawText(g, View[i].Sub, Theme.UiSmall, new Rectangle(r.Width - sw - 8, y, sw, RowH), Theme.TextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            if (sbw > 0)
            {
                int th = Math.Max(24, Height * Height / (View.Count * RowH));
                int ty = (int)((Height - th) * (scroll / (float)Max));
                using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, Width - sbw, 0, sbw, Height);
                using (SolidBrush b = new SolidBrush(Theme.Bg4)) g.FillRectangle(b, Width - sbw + 1, ty, sbw - 2, th);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int r = (e.Y + scroll) / RowH;
            if (r < 0 || r >= View.Count) r = -1;
            if (r != hover) { hover = r; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Max, scroll - Math.Sign(e.Delta) * RowH * 2));
            Invalidate();
            base.OnMouseWheel(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int r = (e.Y + scroll) / RowH;
            if (e.Button == MouseButtons.Left && r >= 0 && r < View.Count) { Selected = r; PickSelected(); }
            base.OnMouseUp(e);
        }
    }

    internal class PickerPopup : Form
    {
        private readonly DarkInput filter = new DarkInput();
        private readonly PickList list = new PickList();
        public PickItem Picked;
        private bool closing;

        private void CloseOnce() { if (closing || IsDisposed) return; closing = true; Close(); }

        public PickerPopup(string title, List<PickItem> items, object current)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Theme.Border;
            Padding = new Padding(1);
            Size = new Size(470, 470);
            filter.Hint = title; filter.Glyph = Theme.GSearch;
            filter.Dock = DockStyle.Top; filter.Height = 32;
            list.All = items; list.Current = current; list.Dock = DockStyle.Fill;
            Controls.Add(list); Controls.Add(filter);
            list.Filter("");
            list.Picked += delegate(PickItem it) { Picked = it; CloseOnce(); };
            filter.Box.TextChanged += delegate { list.Filter(filter.Text); };
            filter.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down) { list.Step(1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up) { list.Step(-1); e.Handled = true; }
                else if (e.KeyCode == Keys.PageDown) { list.Step(10); e.Handled = true; }
                else if (e.KeyCode == Keys.PageUp) { list.Step(-10); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { list.PickSelected(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { CloseOnce(); }
            };
            Deactivate += delegate { CloseOnce(); };
            Shown += delegate { Activate(); BeginInvoke(new MethodInvoker(delegate { ActiveControl = filter.Box; filter.Box.Focus(); })); };
        }
    }

    internal class HoverTip : Control
    {
        private string text = "";
        public HoverTip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Visible = false;
        }
        public void SetText(string t)
        {
            if (t == text) return;
            text = t;
            Size sz = TextRenderer.MeasureText(t, Theme.Ui, new Size(420, 0), TextFormatFlags.NoPadding);
            Size = new Size(sz.Width + 22, sz.Height + 16);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg3);
            using (Pen p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            Theme.DrawText(e.Graphics, text, Theme.Ui, new Rectangle(11, 8, Width - 22, Height - 16), Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);
        }
        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x20; return cp; } }   // WS_EX_TRANSPARENT: не перехватывает мышь
    }

    // ------------------------------------------------------------------
    // Панель просмотра модели
    // ------------------------------------------------------------------
    internal class ModelPane : Pane
    {
        private sealed class GpuTex { public uint Id; public int W, H; }

        private readonly IShellHost host;
        private readonly BarPanel bar = new BarPanel();
        private readonly Panel stage = new Panel();
        private readonly GlSurface surface = new GlSurface();
        private readonly HintPanel hint = new HintPanel();
        private readonly ScrollPanel side = new ScrollPanel();
        private readonly Panel timeline = new Panel();
        private FlatButton btnOpen, btnReload, btnGrid, btnWire, btnAuto;
        private readonly Timer reloadTimer = new Timer();
        private readonly Timer animTimer = new Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int retries;

        private MdlData model;
        private string modelPath;
        private List<string> roots = new List<string>();
        private MatInfo[] mats = new MatInfo[0];
        private GpuTex[] gpu = new GpuTex[0];
        private bool[] dirty = new bool[0];
        private GpuTex checker;
        private int[] bodySel = new int[0];
        private int skin;
        private int lightMode;               // 0 сцена, 1 фонарь камеры, 2 fullbright, 3 свет без текстур
        private float ambient = 0.30f, power = 0.9f;
        private bool showGrid = true, wire, autoReload = true;
        private bool showMesh = true, showSkel, showHit, showPhy;
        private bool matOpen;
        private int hitSet;

        // анимация
        private SeqRef curSeq;
        private float cycle;
        private bool playing, loop = true;
        private float speed = 1f;
        private readonly Dictionary<string, float> pnorm = new Dictionary<string, float>();
        private float[] world = new float[0], skinM = new float[0], bindWorld = new float[0];
        private float[] skPos, skNrm;
        private GCHandle hSkPos, hSkNrm;
        private bool skinned;
        private readonly List<LayerSpec> layers = new List<LayerSpec>();
        private float[] layerInfl = new float[0];
        private bool[] layerDiff = new bool[0];
        private InfoLabel layerSummary;

        private float yaw = -0.75f, pitch = 0.35f, dist = 100f, lightYaw = -0.2f, lightPitch = 0.8f;
        private float[] target = new float[3];
        private float radius = 50f, floorZ;
        private bool userMoved;
        private Point lastMouse;
        private MouseButtons dragButton;

        private GCHandle hPos, hNrm, hUv;
        private bool pinned;
        private readonly List<GCHandle> idxHandles = new List<GCHandle>();
        private readonly Dictionary<MdlSub, IntPtr> idxPtr = new Dictionary<MdlSub, IntPtr>();

        // таймлайн
        private SeqBox seqBox;
        private FlatButton btnPlay, btnStepBack, btnStepFwd, btnLoop, btnSeqPrev, btnSeqNext;
        private Scrubber scrub;
        private Stepper speedStep;
        private InfoLabel timeInfo;

        private Stepper skinStep, lightStep;
        private InfoLabel info;
        private MatList matList;
        private int hoverBone = -1, hoverBox = -1, hoverSolid = -1;
        private HoverTip hoverTip;
        private string boundsText = "";

        public ModelPane(IShellHost h, string path)
        {
            host = h;
            modelPath = string.IsNullOrEmpty(path) ? null : path;

            bar.Height = 40; bar.Dock = DockStyle.Top;
            btnOpen = new FlatButton(Theme.GFolderOpen, Loc.T("Открыть .mdl…"), Loc.T("Выбрать модель (можно перетащить файл сюда)"));
            btnReload = new FlatButton(Theme.GRefresh, null, Loc.T("Перезагрузить модель и текстуры"));
            btnGrid = new FlatButton(null, Loc.T("Сетка"), Loc.T("Показывать пол (G)")); btnGrid.Toggle = true; btnGrid.Toggled = true;
            btnWire = new FlatButton(null, Loc.T("Каркас"), Loc.T("Показывать рёбра (W)")); btnWire.Toggle = true;
            btnAuto = new FlatButton(null, Loc.T("Автообновление"), Loc.T("Перезагружать модель и текстуры при сохранении файлов")); btnAuto.Toggle = true; btnAuto.Toggled = true;
            btnOpen.Click += delegate { BrowseModel(); };
            btnReload.Click += delegate { ReloadAll(false); };
            btnGrid.Click += delegate { showGrid = btnGrid.Toggled; surface.Invalidate(); };
            btnWire.Click += delegate { wire = btnWire.Toggled; surface.Invalidate(); };
            btnAuto.Click += delegate { autoReload = btnAuto.Toggled; SetupWatchers(); };
            bar.Controls.AddRange(new Control[] { btnOpen, btnReload, btnGrid, btnWire, btnAuto });
            bar.Resize += delegate { LayoutBar(); };
            bar.Paint += delegate(object s, PaintEventArgs e)
            {
                string t = modelPath == null ? Loc.T("модель не выбрана") : modelPath;
                int x = btnOpen.Right + 10 + 40;
                Rectangle r = new Rectangle(x, 0, Math.Max(10, btnGrid.Left - x - 12), bar.Height);
                Theme.DrawText(e.Graphics, t, Theme.UiSmall, r, Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
            };

            loop = host.Config.GetBool("mv_loop", true);
            BuildTimeline();

            stage.Dock = DockStyle.Fill;
            surface.Dock = DockStyle.Fill;
            hint.Dock = DockStyle.Fill;
            hint.Title = Loc.T("Просмотр модели Source");
            hint.Body = Loc.T("Откройте .mdl кнопкой сверху или перетащите файл в это окно.\nТекстуры берутся из папки materials рядом с моделью и обновляются при сохранении.");
            stage.Controls.Add(surface);
            stage.Controls.Add(hint);
            hint.BringToFront();

            side.Dock = DockStyle.Right; side.Width = 300; side.BackColor = Theme.Bg1;
            side.Padding = new Padding(14, 8, 14, 8);
            side.AutoScroll = true;
            side.Resize += delegate { RelayoutSide(); };
            side.HandleCreated += delegate { Theme.DarkControl(side); };

            Controls.Add(stage);
            Controls.Add(timeline);
            Controls.Add(side);
            Controls.Add(bar);

            AllowDrop = true; surface.AllowDrop = true; hint.AllowDrop = true;
            DragEventHandler enter = delegate(object s, DragEventArgs e) { e.Effect = DropMdl(e) != null ? DragDropEffects.Copy : DragDropEffects.None; };
            DragEventHandler drop = delegate(object s, DragEventArgs e) { string f = DropMdl(e); if (f != null) LoadModel(f, false); };
            DragEnter += enter; DragOver += enter; DragDrop += drop;
            surface.DragEnter += enter; surface.DragOver += enter; surface.DragDrop += drop;
            hint.DragEnter += enter; hint.DragOver += enter; hint.DragDrop += drop;

            surface.Render += RenderScene;
            surface.Resize += delegate { if (!userMoved) FrameModel(); };
            surface.MouseEnter += delegate { if (!surface.Focused && Form.ActiveForm == FindForm()) surface.Focus(); };
            surface.MouseDown += delegate(object s, MouseEventArgs e) { surface.Focus(); lastMouse = e.Location; dragButton = e.Button; };
            surface.MouseMove += OnSurfaceMouseMove;
            surface.MouseUp += delegate { dragButton = MouseButtons.None; };
            surface.MouseLeave += delegate { ClearHover(); };
            surface.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                userMoved = true;
                dist = Math.Max(radius * 0.15f, Math.Min(radius * 20f, dist * (e.Delta > 0 ? 0.88f : 1.14f)));
                surface.Invalidate();
            };
            surface.MouseDoubleClick += delegate { FrameModel(); };
            surface.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F || e.KeyCode == Keys.R || e.KeyCode == Keys.Home) FrameModel();
                else if (e.KeyCode == Keys.G) { btnGrid.Toggled = !btnGrid.Toggled; showGrid = btnGrid.Toggled; btnGrid.Invalidate(); surface.Invalidate(); }
                else if (e.KeyCode == Keys.W) { btnWire.Toggled = !btnWire.Toggled; wire = btnWire.Toggled; btnWire.Invalidate(); surface.Invalidate(); }
                else if (e.KeyCode == Keys.Space) { TogglePlay(); e.Handled = true; }
                else if (e.KeyCode == Keys.Left) StepFrame(-1);
                else if (e.KeyCode == Keys.Right) StepFrame(1);
            };
            surface.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e) { e.IsInputKey = true; };

            reloadTimer.Interval = 600;
            reloadTimer.Tick += delegate { reloadTimer.Stop(); ProcessChanges(); };
            animTimer.Interval = 15;
            animTimer.Tick += OnAnimTick;

            lightMode = host.Config.GetInt("mv_light", 0);
            ambient = host.Config.GetInt("mv_amb", 30) / 100f;
            power = host.Config.GetInt("mv_pow", 90) / 100f;
            showSkel = host.Config.GetBool("mv_skel", false);
            showHit = host.Config.GetBool("mv_hit", false);
            showPhy = host.Config.GetBool("mv_phy", false);
            matOpen = host.Config.GetBool("mv_matopen", false);
            BuildSide();
            timeline.Visible = false;
        }

        public override string Title { get { return model != null ? Path.GetFileName(model.Path) : (modelPath != null ? Path.GetFileName(modelPath) : Loc.T("Просмотр модели")); } }
        public override string IconGlyph { get { return ""; } }
        public override string Serialize() { return "M|" + (modelPath ?? ""); }
        public override void Activated() { if (surface.IsHandleCreated) surface.Focus(); }
        public string CurrentPath { get { return modelPath; } }
        public string StartSeq;     // секвенция, выбираемая после загрузки (аргумент --seq=)

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutBar();
            if (modelPath != null && File.Exists(modelPath)) { string p = modelPath; BeginInvoke(new MethodInvoker(delegate { LoadModel(p, false); })); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reloadTimer.Stop(); animTimer.Stop();
                foreach (FileSystemWatcher w in watchers) w.Dispose();
                watchers.Clear();
                FreePinned();
            }
            base.Dispose(disposing);
        }

        private void LayoutBar()
        {
            int y = (bar.Height - 28) / 2;
            btnOpen.SetBounds(8, y, btnOpen.Width, 28);
            btnReload.SetBounds(btnOpen.Right + 4, y, 30, 28);
            int right = bar.Width - 8;
            btnAuto.SetBounds(right - btnAuto.Width, y, btnAuto.Width, 28); right -= btnAuto.Width + 4;
            btnWire.SetBounds(right - btnWire.Width, y, btnWire.Width, 28); right -= btnWire.Width + 4;
            btnGrid.SetBounds(right - btnGrid.Width, y, btnGrid.Width, 28);
        }

        private static string DropMdl(DragEventArgs e)
        {
            string[] f = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            if (f == null) return null;
            foreach (string s in f) if (s.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        // ---------------- привязки ----------------
        private List<string> Cfg(string key)
        {
            List<string> r = new List<string>();
            foreach (string s in host.Config.Get(key, "").Split('|')) if (s.Length > 0) r.Add(s);
            return r;
        }

        private void SaveCfg(string key, List<string> v) { host.Config.Set(key, string.Join("|", v.ToArray())); }

        private static string AnimKey(string path)
        {
            uint h = 2166136261;
            foreach (char c in path.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return "mvanim_" + h.ToString("x8");
        }

        private void OpenBindings()
        {
            List<string> mp = Cfg("matpaths"), mo = Cfg("modelpaths");
            List<string> an = model != null ? Cfg(AnimKey(model.Path)) : new List<string>();
            using (BindingsForm f = new BindingsForm(model != null ? Path.GetFileName(model.Path) : "", mp, mo, an,
                model != null && model.Anim != null ? model.Anim.Report : null))
            {
                f.ShowDialog(FindForm());
                if (!f.Changed) return;
                SaveCfg("matpaths", mp); SaveCfg("modelpaths", mo);
                if (model != null) SaveCfg(AnimKey(model.Path), an);
                host.Config.Save();
                ReloadAll(false);
            }
        }

        private void BrowseModel()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = Loc.T("Модели Source (*.mdl)|*.mdl|Все файлы|*.*");
                if (modelPath != null) try { d.InitialDirectory = Path.GetDirectoryName(modelPath); } catch { }
                if (d.ShowDialog(FindForm()) == DialogResult.OK) LoadModel(d.FileName, false);
            }
        }

        // ---------------- таймлайн ----------------
        private void BuildTimeline()
        {
            timeline.Dock = DockStyle.Bottom; timeline.Height = 92; timeline.BackColor = Theme.Bg2;
            timeline.Paint += delegate(object s, PaintEventArgs e) { using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, timeline.Width, 0); };

            seqBox = new SeqBox();
            seqBox.MouseUp += delegate(object s2, MouseEventArgs e2)
            {
                if (e2.Button == MouseButtons.Left) PickSequence();
                else if (e2.Button == MouseButtons.Right) ShowSeqMenu(Cursor.Position);
            };
            btnSeqPrev = new FlatButton(Theme.GBack, null, Loc.T("Предыдущая секвенция"));
            btnSeqNext = new FlatButton(Theme.GForward, null, Loc.T("Следующая секвенция"));
            btnSeqPrev.Click += delegate { StepSeq(-1); };
            btnSeqNext.Click += delegate { StepSeq(1); };

            btnPlay = new FlatButton("", null, Loc.T("Пуск / пауза (Пробел)")); btnPlay.Primary = true;
            btnStepBack = new FlatButton("", null, Loc.T("Кадр назад (←)"));
            btnStepFwd = new FlatButton("", null, Loc.T("Кадр вперёд (→)"));
            btnPlay.Click += delegate { TogglePlay(); };
            btnStepBack.Click += delegate { StepFrame(-1); };
            btnStepFwd.Click += delegate { StepFrame(1); };

            scrub = new Scrubber();
            scrub.Scrubbed += delegate { cycle = scrub.Value; playing = false; animTimer.Stop(); UpdatePlayGlyph(); ApplyPose(); UpdateTimelineText(); };
            timeInfo = new InfoLabel();
            btnLoop = new FlatButton("", null, Loc.T("Зациклить")); btnLoop.Toggle = true; btnLoop.Toggled = loop;
            btnLoop.Click += delegate { loop = btnLoop.Toggled; host.Config.SetBool("mv_loop", loop); };
            speedStep = new Stepper();
            speedStep.Items = new string[] { "0.1×", "0.25×", "0.5×", "1×", "1.5×", "2×", "4×" };
            speedStep.CaptionWidth = 0;
            speedStep.SetIndexSilently(3);
            speedStep.Changed += delegate { speed = new float[] { 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f, 4f }[speedStep.Index]; };
            Tips.Set(speedStep, Loc.T("Скорость воспроизведения"));

            timeline.Controls.AddRange(new Control[] { seqBox, btnSeqPrev, btnSeqNext, btnPlay, btnStepBack, btnStepFwd, scrub, timeInfo, btnLoop, speedStep });
            timeline.Resize += delegate { LayoutTimeline(); };
            LayoutTimeline();
        }

        private void LayoutTimeline()
        {
            int w = timeline.Width;
            int y1 = 10, y2 = 52;
            btnSeqPrev.SetBounds(10, y1, 30, 30);
            btnSeqNext.SetBounds(w - 40, y1, 30, 30);
            seqBox.SetBounds(44, y1, Math.Max(80, w - 88), 30);
            btnPlay.SetBounds(10, y2, 36, 30);
            btnStepBack.SetBounds(50, y2, 30, 30);
            btnStepFwd.SetBounds(84, y2, 30, 30);
            int right = w - 10;
            speedStep.SetBounds(right - 90, y2, 90, 30); right -= 98;
            btnLoop.SetBounds(right - 30, y2, 30, 30); right -= 38;
            timeInfo.SetBounds(right - 110, y2 + 2, 110, 28); right -= 116;
            scrub.SetBounds(122, y2, Math.Max(60, right - 122), 30);
        }

        private void UpdateTimelineVisibility()
        {
            bool has = model != null && model.Anim != null && model.Anim.Seqs.Count > 0;
            timeline.Visible = has;
            if (has) UpdateTimelineText();
        }

        private void UpdatePlayGlyph() { btnPlay.Glyph = playing ? "" : ""; btnPlay.Invalidate(); }

        // секвенция, по которой идёт шкала времени: основная, а при её отсутствии - первый слой
        private SeqRef TimeSeq { get { return curSeq != null ? curSeq : (layers.Count > 0 ? layers[0].Seq : null); } }

        private void UpdateTimelineText()
        {
            if (model == null || model.Anim == null) return;
            SeqRef ts = TimeSeq;
            if (curSeq == null)
            {
                seqBox.Caption = Loc.T("Поза по умолчанию");
                seqBox.Detail = model.Anim.Seqs.Count + Loc.T(" секвенций") + (layers.Count > 0 ? Loc.T("   + слоёв: ") + layers.Count : "");
            }
            else
            {
                seqBox.Caption = curSeq.Name;
                seqBox.Detail = (curSeq.Activity.Length > 0 ? curSeq.Activity + "   " : "") + curSeq.Frames + Loc.T(" кадр.  ") + curSeq.Fps.ToString("0.#", CultureInfo.InvariantCulture) + " fps"
                    + (curSeq.Source.Length > 0 ? "   [" + curSeq.Source + "]" : "") + (layers.Count > 0 ? Loc.T("   + слоёв: ") + layers.Count : "");
            }
            if (ts == null) { timeInfo.Text = ""; scrub.Frames = 1; scrub.Value = 0; }
            else
            {
                scrub.Frames = ts.Frames;
                int fr = (int)Math.Round(cycle * Math.Max(0, ts.Frames - 1));
                timeInfo.Text = Loc.T("кадр ") + fr + " / " + Math.Max(0, ts.Frames - 1) + "\n" + (cycle * ts.Duration).ToString("0.00", CultureInfo.InvariantCulture) + Loc.T(" с");
                scrub.Value = cycle;
            }
            seqBox.Invalidate(); scrub.Invalidate(); timeInfo.Invalidate();
        }

        private void PickSequence()
        {
            if (model == null || model.Anim == null) return;
            List<PickItem> items = new List<PickItem>();
            PickItem none = new PickItem(); none.Text = Loc.T("— Поза по умолчанию —"); none.Tag = null; none.Sub = "bind pose";
            items.Add(none);
            foreach (SeqRef sr in model.Anim.Seqs)
            {
                PickItem it = new PickItem();
                it.Text = sr.Name;
                it.Sub = (sr.Missing ? Loc.T("нет данных   ") : "") + sr.Frames + Loc.T(" кадр.") + (sr.Source.Length > 0 ? "   " + sr.Source : "") + (sr.Activity.Length > 0 ? "   " + sr.Activity : "");
                it.Tag = sr;
                items.Add(it);
            }
            PickerPopup pp = new PickerPopup(Loc.T("Поиск секвенции (") + model.Anim.Seqs.Count + ")", items, curSeq);
            Point p = seqBox.PointToScreen(new Point(0, 0));
            Screen scr = Screen.FromControl(this);
            int y = p.Y - pp.Height - 6;
            if (y < scr.WorkingArea.Top) y = p.Y + seqBox.Height + 6;
            pp.Location = new Point(Math.Max(scr.WorkingArea.Left, Math.Min(p.X, scr.WorkingArea.Right - pp.Width)), y);
            pp.FormClosed += delegate
            {
                if (pp.Picked == null) return;
                SelectSeq(pp.Picked.Tag as SeqRef, true);
            };
            pp.Show(FindForm());
        }

        private void StepSeq(int d)
        {
            if (model == null || model.Anim == null || model.Anim.Seqs.Count == 0) return;
            int i = curSeq == null ? (d > 0 ? -1 : 0) : model.Anim.Seqs.IndexOf(curSeq);
            i = (i + d + model.Anim.Seqs.Count) % model.Anim.Seqs.Count;
            SelectSeq(model.Anim.Seqs[i], true);
        }

        private void SelectSeq(SeqRef sr, bool play)
        {
            curSeq = sr;
            cycle = 0;
            playing = play && sr != null && sr.Frames > 1 && !sr.Missing;
            if (playing) { clock.Reset(); clock.Start(); animTimer.Start(); } else animTimer.Stop();
            UpdatePlayGlyph();
            if (sr != null && sr.Missing) host.Notify(NoteKind.Warn, Loc.T("Анимация недоступна"), sr.Name + Loc.T(": данные во внешнем .ani-файле не поддерживаются"));
            ApplyPose();
            BuildSide();
            UpdateTimelineText();
        }

        private void TogglePlay()
        {
            if (TimeSeq == null || TimeSeq.Frames <= 1) return;
            playing = !playing;
            if (playing)
            {
                if (cycle >= 1f) cycle = 0f;
                clock.Reset(); clock.Start(); animTimer.Start();
            }
            else animTimer.Stop();
            UpdatePlayGlyph();
        }

        private void StepFrame(int d)
        {
            if (TimeSeq == null || TimeSeq.Frames <= 1) return;
            playing = false; animTimer.Stop(); UpdatePlayGlyph();
            int n = TimeSeq.Frames - 1;
            int fr = (int)Math.Round(cycle * n) + d;
            if (fr < 0) fr = n;
            if (fr > n) fr = 0;
            cycle = fr / (float)n;
            ApplyPose(); UpdateTimelineText();
        }

        private void OnAnimTick(object s, EventArgs e)
        {
            if (!playing || TimeSeq == null) { animTimer.Stop(); return; }
            float dt = (float)clock.Elapsed.TotalSeconds; clock.Reset(); clock.Start();
            if (TimeSeq.Duration > 0.0001f)
            {
                cycle += dt * speed / TimeSeq.Duration;
                if (cycle >= 1f)
                {
                    if (loop) cycle -= (float)Math.Floor(cycle);
                    else { cycle = 1f; playing = false; animTimer.Stop(); UpdatePlayGlyph(); }
                }
            }
            foreach (LayerSpec l in layers)
                if (!l.Sync && l.Seq != null && l.Seq.Duration > 0.0001f) { l.Cycle += dt * speed / l.Seq.Duration; l.Cycle -= (float)Math.Floor(l.Cycle); }
            ApplyPose();
            UpdateTimelineText();
        }

        // вычисляет позу и (если нужно) перекладывает вершины
        private void ApplyPose()
        {
            if (model == null || model.Anim == null) { surface.Invalidate(); return; }
            AnimSet a = model.Anim;
            int nb = a.BoneCount;
            if (world.Length != nb * 12)
            {
                world = new float[nb * 12]; skinM = new float[nb * 12]; bindWorld = new float[nb * 12];
                a.BindPose(bindWorld, new float[nb * 12]);
            }
            if (layerInfl.Length != nb) { layerInfl = new float[nb]; layerDiff = new bool[nb]; }
            bool ok = false;
            if (layers.Count > 0) { ok = a.EvaluateLayers(curSeq, cycle, layers, pnorm, world, skinM, layerInfl, layerDiff); UpdateLayerSummary(); }
            else if (curSeq != null && !curSeq.Missing) ok = a.Evaluate(curSeq, cycle, pnorm, world, skinM);
            if (!ok) { Array.Copy(bindWorld, world, world.Length); skinned = false; }
            else if (nb > 0 && skPos != null)
            {
                AnimSet.SkinVertices(model, skinM, skPos, skNrm);
                skinned = true;
            }
            surface.Invalidate();
        }

        // ---------------- загрузка ----------------
        public void LoadModel(string path, bool quiet)
        {
            try
            {
                MdlData m = MdlLoader.Load(path, Cfg("matpaths"), Cfg("modelpaths"), Cfg(AnimKey(path)));
                int[] oldSel = bodySel; int oldSkin = skin;
                string oldSeq = curSeq != null ? curSeq.Name : null;
                float oldCycle = cycle; bool oldPlaying = playing;
                animTimer.Stop();
                FreePinned();
                model = m; modelPath = path;
                bodySel = new int[m.Parts.Count];
                if (quiet) for (int i = 0; i < bodySel.Length && i < oldSel.Length; i++) bodySel[i] = Math.Min(oldSel[i], m.Parts[i].Models.Count - 1);
                skin = quiet ? Math.Min(oldSkin, Math.Max(0, m.Skins.Length - 1)) : 0;
                curSeq = null; cycle = 0; playing = false; skinned = false; world = new float[0];
                List<string> oldLayers = new List<string>();
                if (quiet) foreach (LayerSpec l in layers) if (l.Seq != null) oldLayers.Add(l.Seq.Name);
                List<LayerSpec> oldSpecs = new List<LayerSpec>(layers);
                layers.Clear();
                hitSet = 0;
                if (quiet && oldSeq != null && m.Anim != null)
                    foreach (SeqRef sr in m.Anim.Seqs)
                        if (sr.Name == oldSeq) { curSeq = sr; cycle = oldCycle; playing = oldPlaying; break; }
                if (quiet && m.Anim != null)
                    for (int i = 0; i < oldSpecs.Count && i < oldLayers.Count; i++)
                        foreach (SeqRef sr in m.Anim.Seqs)
                            if (sr.Name == oldLayers[i]) { oldSpecs[i].Seq = sr; layers.Add(oldSpecs[i]); break; }
                PinArrays();
                ResolveMaterials();
                ComputeBounds();
                if (!quiet) FrameModel();
                hint.Visible = false;
                ApplyPose();
                if (playing) { clock.Reset(); clock.Start(); animTimer.Start(); }
                BuildSide();
                UpdatePlayGlyph();
                UpdateTimelineVisibility();
                SetupWatchers();
                RaiseTitleChanged();
                bar.Invalidate();
                surface.Invalidate();
                if (StartSeq != null && m.Anim != null)
                {
                    foreach (SeqRef sr in m.Anim.Seqs) if (string.Equals(sr.Name, StartSeq, StringComparison.OrdinalIgnoreCase)) { SelectSeq(sr, true); break; }
                    StartSeq = null;
                }
                if (quiet) host.Notify(NoteKind.Info, Loc.T("Модель обновлена"), Path.GetFileName(path));
                else if (m.AnimError != null) host.Notify(NoteKind.Warn, Loc.T("Анимации недоступны"), m.AnimError);
                if (!quiet && m.Anim != null)
                {
                    int miss = 0; foreach (IncludeInfo ii in m.Anim.Report) if (ii.Path == null && !ii.Extra) miss++;
                    if (miss > 0) host.Notify(NoteKind.Warn, Loc.T("Не найдены подключённые модели: ") + miss, Loc.T("Часть анимаций может быть недоступна. Откройте «Привязки» и добавьте папку с моделями."));
                }
            }
            catch (Exception ex)
            {
                if (model == null) { hint.Title = Loc.T("Не удалось открыть модель"); hint.Body = ex.Message; hint.Bad = true; hint.Visible = true; hint.BringToFront(); hint.Invalidate(); }
                host.Notify(NoteKind.Bad, Loc.T("Не удалось открыть модель"), ex.Message);
            }
        }

        private void ResolveMaterials()
        {
            roots = MatResolver.Roots(model.Path, Cfg("matpaths"));
            mats = new MatInfo[model.TexNames.Length];
            gpu = new GpuTex[mats.Length];
            dirty = new bool[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = MatResolver.Resolve(model.TexNames[i], model.CdTextures, roots, 2048);
                dirty[i] = true;
            }
        }

        private void PinArrays()
        {
            hPos = GCHandle.Alloc(model.Pos, GCHandleType.Pinned);
            hNrm = GCHandle.Alloc(model.Nrm, GCHandleType.Pinned);
            hUv = GCHandle.Alloc(model.Uv, GCHandleType.Pinned);
            skPos = new float[model.Pos.Length]; skNrm = new float[model.Nrm.Length];
            Array.Copy(model.Pos, skPos, skPos.Length); Array.Copy(model.Nrm, skNrm, skNrm.Length);
            hSkPos = GCHandle.Alloc(skPos, GCHandleType.Pinned);
            hSkNrm = GCHandle.Alloc(skNrm, GCHandleType.Pinned);
            pinned = true;
            foreach (MdlBodyPart bp in model.Parts)
                foreach (MdlBodyModel bm in bp.Models)
                    foreach (MdlSub s in bm.Subs)
                    {
                        GCHandle h = GCHandle.Alloc(s.Index, GCHandleType.Pinned);
                        idxHandles.Add(h);
                        idxPtr[s] = h.AddrOfPinnedObject();
                    }
        }

        private void FreePinned()
        {
            if (!pinned) return;
            hPos.Free(); hNrm.Free(); hUv.Free(); hSkPos.Free(); hSkNrm.Free();
            foreach (GCHandle h in idxHandles) h.Free();
            idxHandles.Clear(); idxPtr.Clear();
            pinned = false;
        }

        private void ComputeBounds()
        {
            float[] mn = { float.MaxValue, float.MaxValue, float.MaxValue }, mx = { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < model.VertexCount; i++)
                for (int k = 0; k < 3; k++)
                {
                    float v = model.Pos[i * 3 + k];
                    if (v < mn[k]) mn[k] = v;
                    if (v > mx[k]) mx[k] = v;
                }
            if (model.VertexCount == 0) { mn = new float[] { -10, -10, -10 }; mx = new float[] { 10, 10, 10 }; }
            for (int k = 0; k < 3; k++) target[k] = (mn[k] + mx[k]) / 2f;
            floorZ = mn[2];
            float dx = mx[0] - mn[0], dy = mx[1] - mn[1], dz = mx[2] - mn[2];
            radius = Math.Max(1f, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2f);
            boundsText = string.Format(CultureInfo.InvariantCulture, Loc.T("{0:0.#} × {1:0.#} × {2:0.#} ед."), dx, dy, dz);
        }

        private void FrameModel()
        {
            if (model == null) return;
            userMoved = false;
            float aspect = (float)Math.Max(1, surface.Width) / Math.Max(1, surface.Height);
            float fh = 2f * (float)Math.Atan(Math.Tan(0.4) * aspect);
            float f = Math.Min(0.8f, fh);
            dist = radius / (float)Math.Sin(f / 2f) * 1.12f;
            surface.Invalidate();
        }

        // ---------------- боковая панель ----------------
        private readonly List<Control> sideItems = new List<Control>();

        private FlatButton ToggleBtn(string text, bool on, Action<bool> set)
        {
            FlatButton b = new FlatButton(on ? Theme.GCheckOn : Theme.GCheckOff, text, null);
            b.BaseRole = 1; b.LeftAlign = true; b.Height = 26; b.Toggle = true; b.Toggled = on;
            b.Click += delegate { b.Glyph = b.Toggled ? Theme.GCheckOn : Theme.GCheckOff; b.Invalidate(); set(b.Toggled); surface.Invalidate(); };
            return b;
        }

        private void BuildSide()
        {
            int keepScroll = -side.AutoScrollPosition.Y;
            side.SuspendLayout();
            foreach (Control c in sideItems) { side.Controls.Remove(c); c.Dispose(); }
            sideItems.Clear();
            matList = null;
            if (model == null) { side.ResumeLayout(); return; }

            // --- бодигруппы и скин ---
            sideItems.Add(new CollapseHeader(Loc.T("Бодигруппы и скин"), false));
            int shown = 0;
            for (int i = 0; i < model.Parts.Count; i++)
            {
                MdlBodyPart bp = model.Parts[i];
                if (bp.Models.Count < 2) continue;
                Stepper st = new Stepper();
                st.Caption = bp.Name.Length > 0 ? bp.Name : "bodygroup " + i;
                string[] names = new string[bp.Models.Count];
                for (int k = 0; k < names.Length; k++)
                {
                    string n = bp.Models[k].Name;
                    if (n.Length == 0 || n.StartsWith("blank", StringComparison.OrdinalIgnoreCase) || bp.Models[k].Subs.Count == 0) n = Loc.T("(пусто)");
                    else n = Path.GetFileNameWithoutExtension(n);
                    names[k] = k + ": " + n;
                }
                st.Items = names;
                st.SetIndexSilently(bodySel[i]);
                int part = i;
                st.Changed += delegate(object s, EventArgs e) { bodySel[part] = ((Stepper)s).Index; ComputeInfo(); surface.Invalidate(); };
                sideItems.Add(st); shown++;
            }
            if (shown == 0)
            {
                InfoLabel none = new InfoLabel(); none.Text = Loc.T("В модели нет переключаемых бодигрупп."); none.Height = 20;
                sideItems.Add(none);
            }
            skinStep = new Stepper();
            skinStep.Caption = Loc.T("Скин");
            string[] sk = new string[model.Skins.Length];
            for (int i = 0; i < sk.Length; i++) sk[i] = i.ToString();
            skinStep.Items = sk;
            skinStep.SetIndexSilently(skin);
            skinStep.Changed += delegate { skin = skinStep.Index; surface.Invalidate(); };
            sideItems.Add(skinStep);

            // --- что показывать ---
            sideItems.Add(new CollapseHeader(Loc.T("Показать"), false));
            sideItems.Add(ToggleBtn(Loc.T("Модель"), showMesh, delegate(bool v) { showMesh = v; }));
            sideItems.Add(ToggleBtn(Loc.T("Скелет (кости)"), showSkel, delegate(bool v) { showSkel = v; host.Config.SetBool("mv_skel", v); }));
            bool hasHit = model.Anim != null && model.Anim.Main.HitSets.Count > 0 && model.Anim.Main.HitSets[0].Boxes.Count > 0;
            FlatButton hb = ToggleBtn(Loc.T("Хитбоксы") + (hasHit ? " (" + model.Anim.Main.HitSets[Math.Min(hitSet, model.Anim.Main.HitSets.Count - 1)].Boxes.Count + ")" : Loc.T(" — нет")), showHit, delegate(bool v) { showHit = v; host.Config.SetBool("mv_hit", v); });
            hb.Enabled = hasHit;
            sideItems.Add(hb);
            if (hasHit && model.Anim.Main.HitSets.Count > 1)
            {
                Stepper hs = new Stepper(); hs.Caption = Loc.T("Набор");
                string[] hn = new string[model.Anim.Main.HitSets.Count];
                for (int i = 0; i < hn.Length; i++) hn[i] = model.Anim.Main.HitSets[i].Name.Length > 0 ? model.Anim.Main.HitSets[i].Name : i.ToString();
                hs.Items = hn; hs.SetIndexSilently(hitSet);
                hs.Changed += delegate { hitSet = hs.Index; surface.Invalidate(); };
                sideItems.Add(hs);
            }
            bool hasPhy = model.Phy != null && model.Phy.Solids.Count > 0;
            FlatButton pb = ToggleBtn(Loc.T("Физика") + (hasPhy ? " (" + model.Phy.Solids.Count + ")" : Loc.T(" — нет .phy")), showPhy, delegate(bool v) { showPhy = v; host.Config.SetBool("mv_phy", v); });
            pb.Enabled = hasPhy;
            sideItems.Add(pb);

            // --- анимация ---
            if (model.Anim != null && model.Anim.Seqs.Count > 0)
            {
                sideItems.Add(new CollapseHeader(Loc.T("Анимация"), false));
                InfoLabel ai = new InfoLabel();
                if (curSeq == null)
                {
                    ai.Height = 34;
                    ai.Text = Loc.T("Секвенций: ") + model.Anim.Seqs.Count + Loc.T(". Выберите анимацию в панели внизу.");
                    sideItems.Add(ai);
                }
                else
                {
                    ai.Height = 64;
                    ai.Text = curSeq.Name + "\n" + (curSeq.Activity.Length > 0 ? curSeq.Activity + "\n" : "") + curSeq.Frames + Loc.T(" кадров, ") + curSeq.Fps.ToString("0.#", CultureInfo.InvariantCulture) + " fps" +
                        (curSeq.Additive ? Loc.T(", аддитивная") : "") + (curSeq.Looping ? Loc.T(", зациклена") : "") + (curSeq.Missing ? Loc.T(", данные недоступны") : "") +
                        "\n" + WeightListText(curSeq);
                    sideItems.Add(ai);
                    AnimFile f = model.Anim.Files[curSeq.File];
                    SeqDesc sd = curSeq.Desc;
                    foreach (int pi in new int[] { sd.ParamX, sd.ParamY })
                    {
                        if (pi < 0 || pi >= f.Params.Length) continue;
                        PoseParam pp = f.Params[pi];
                        MiniSlider sl = new MiniSlider();
                        sl.Caption = pp.Name; sl.Height = 38;
                        float start = pp.Start, end = pp.End;
                        float cur; if (!pnorm.TryGetValue(pp.Name, out cur)) cur = 0.5f;
                        sl.Format = delegate(float v) { return (start + v * (end - start)).ToString("0.##", CultureInfo.InvariantCulture); };
                        sl.SetSilently(cur);
                        string nm = pp.Name;
                        sl.Changed += delegate { pnorm[nm] = sl.Value; ApplyPose(); };
                        sideItems.Add(sl);
                    }
                }
            }

            if (model.Anim != null && model.Anim.Seqs.Count > 0) AddLayerSection();

            // --- привязки и зависимости ---
            sideItems.Add(new CollapseHeader(Loc.T("Привязки"), false));
            InfoLabel bi = new InfoLabel(); bi.Height = 50;
            int inc = model.Anim != null ? model.Anim.Report.Count : 0, miss2 = 0;
            if (model.Anim != null) foreach (IncludeInfo ii in model.Anim.Report) if (ii.Path == null) miss2++;
            bi.Text = Loc.T("Папки с материалами: ") + Cfg("matpaths").Count + Loc.T("\nПодключённые модели: ") + (inc - miss2) + Loc.T(" из ") + inc + (miss2 > 0 ? Loc.T(" (не найдено ") + miss2 + ")" : "") +
                Loc.T("\nДоп. анимации: ") + Cfg(AnimKey(model.Path)).Count;
            sideItems.Add(bi);
            FlatButton bb = new FlatButton(Theme.GFolderOpen, Loc.T("Настроить привязки…"), Loc.T("Папки с материалами и моделями, дополнительные файлы анимаций"));
            bb.BaseRole = 1; bb.LeftAlign = true; bb.Height = 30;
            bb.Click += delegate { OpenBindings(); };
            sideItems.Add(bb);

            // --- освещение ---
            sideItems.Add(new CollapseHeader(Loc.T("Освещение"), false));
            lightStep = new Stepper();
            lightStep.Caption = Loc.T("Режим");
            lightStep.Items = new string[] { Loc.T("Сцена (2 источника)"), Loc.T("Фонарь на камере"), Loc.T("Fullbright (без света)"), Loc.T("Только свет (без текстур)") };
            lightStep.SetIndexSilently(lightMode);
            lightStep.Changed += delegate { lightMode = lightStep.Index; host.Config.SetInt("mv_light", lightMode); surface.Invalidate(); };
            sideItems.Add(lightStep);
            MiniSlider amb = new MiniSlider(); amb.Caption = Loc.T("Окружение"); amb.Value = ambient;
            amb.Changed += delegate { ambient = amb.Value; host.Config.SetInt("mv_amb", (int)(ambient * 100)); surface.Invalidate(); };
            sideItems.Add(amb);
            MiniSlider pw = new MiniSlider(); pw.Caption = Loc.T("Яркость"); pw.Value = power;
            pw.Changed += delegate { power = pw.Value; host.Config.SetInt("mv_pow", (int)(power * 100)); surface.Invalidate(); };
            sideItems.Add(pw);
            InfoLabel lh = new InfoLabel(); lh.Text = Loc.T("Shift + перетаскивание — повернуть источник света."); lh.Height = 34;
            sideItems.Add(lh);

            // --- материалы (сворачиваемый список с прокруткой) ---
            CollapseHeader mh = new CollapseHeader(Loc.T("Материалы"), true);
            mh.Open = matOpen;
            int bad = 0; foreach (MatInfo mi in mats) if (!mi.Ok) bad++;
            mh.Badge = bad > 0 ? bad + Loc.T(" не найдено / ") + mats.Length : mats.Length.ToString();
            sideItems.Add(mh);
            matList = new MatList();
            matList.Items = new List<MatInfo>(mats);
            matList.Refit();
            matList.RowMenu += ShowMatMenu;
            matList.Visible = matOpen;
            sideItems.Add(matList);
            mh.Toggled += delegate
            {
                matOpen = mh.Open; host.Config.SetBool("mv_matopen", matOpen);
                if (matList != null) matList.Visible = matOpen;
                RelayoutSide();
            };

            // --- информация ---
            sideItems.Add(new CollapseHeader(Loc.T("Информация"), false));
            info = new InfoLabel(); info.Height = 96;
            sideItems.Add(info);
            ComputeInfo();

            foreach (Control c in sideItems) side.Controls.Add(c);
            RelayoutSide();
            side.ResumeLayout();
            try { side.AutoScrollPosition = new Point(0, keepScroll); } catch { }
        }

        private void RelayoutSide()
        {
            int w = side.ClientSize.Width - side.Padding.Horizontal;
            int y = side.Padding.Top;
            int scroll = side.AutoScrollPosition.Y;
            foreach (Control c in sideItems)
            {
                if (c == matList && !matOpen) continue;
                c.SetBounds(side.Padding.Left, y + scroll, Math.Max(80, w), c.Height);
                y += c.Height + (c is CollapseHeader ? 4 : 6);
            }
            side.AutoScrollMinSize = new Size(0, y + 10);
        }

        private void ComputeInfo()
        {
            if (info == null || model == null) return;
            int tris = 0;
            for (int i = 0; i < model.Parts.Count; i++) tris += model.Parts[i].Models[Math.Max(0, Math.Min(bodySel[i], model.Parts[i].Models.Count - 1))].Triangles;
            string s = Loc.T("Треугольников: ") + tris.ToString("N0") + Loc.T("\nВершин: ") + model.VertexCount.ToString("N0") + Loc.T("   Костей: ") + model.Bones +
                Loc.T("\nРазмер: ") + boundsText + "\nMDL v" + model.Version + "   " + (surface.RendererName ?? "");
            if (model.AnimError != null) s += Loc.T("\nАнимации: ") + model.AnimError;
            else if (model.Anim != null && model.Anim.Warnings.Count > 0) s += "\n" + model.Anim.Warnings[0];
            if (model.PhyError != null) s += Loc.T("\nФизика: ") + model.PhyError;
            info.Text = s;
        }

        // ---------------- слои анимации ----------------
        private static string WeightListText(SeqRef sr)
        {
            float[] w = sr.Desc.WeightList;
            if (w == null) return Loc.T("weightlist: нет");
            int n = 0; foreach (float v in w) if (v > 0.0001f) n++;
            return sr.Desc.AllOnes ? Loc.T("weightlist: все кости = 1 (не ограничивает)") : Loc.T("weightlist: влияет на ") + n + Loc.T(" из ") + w.Length + Loc.T(" костей");
        }

        private void LayerColor(int b)
        {
            if (layerInfl[b] <= 0.0001f) GL.glColor4f(0.55f, 0.58f, 0.62f, 0.9f);
            else if (layerDiff[b]) GL.glColor4f(1f, 0.55f, 0.12f, 1f);
            else GL.glColor4f(0.95f, 0.88f, 0.35f, 1f);
        }

        private void AddLayerSection()
        {
            CollapseHeader lh = new CollapseHeader(Loc.T("Слои анимаций"), false);
            lh.Badge = layers.Count > 0 ? layers.Count.ToString() : "";
            sideItems.Add(lh);
            InfoLabel il = new InfoLabel();
            if (layers.Count == 0)
            {
                il.Height = 64;
                il.Text = Loc.T("Наложите одну секвенцию на другую, чтобы увидеть, какие кости участвуют. ПКМ по полю секвенции внизу — тоже откроет это меню.");
            }
            else
            {
                il.Height = 48;
                il.Text = Loc.T("Скелет раскрашен: оранжевые — кости слоя, которые отличаются от основной анимации; жёлтые — слой на них есть, но совпадает; серые — слой не затрагивает.");
            }
            sideItems.Add(il);
            FlatButton add = new FlatButton(Theme.GAdd, Loc.T("Добавить слой…"), Loc.T("Выбрать секвенцию, которую нужно наложить на основную"));
            add.BaseRole = 1; add.LeftAlign = true; add.Height = 30;
            add.Click += delegate { PickLayerSeq(); };
            sideItems.Add(add);

            foreach (LayerSpec l0 in layers)
            {
                LayerSpec l = l0;
                InfoLabel nm = new InfoLabel(); nm.Height = 34;
                nm.Text = "▸ " + l.Seq.Name + "\n" + WeightListText(l.Seq) + (l.Seq.Additive ? Loc.T("  ·  аддитивная") : "");
                sideItems.Add(nm);
                MiniSlider ws = new MiniSlider(); ws.Caption = Loc.T("Вес слоя"); ws.Height = 38;
                ws.Format = delegate(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); };
                ws.SetSilently(l.Weight);
                ws.Changed += delegate { l.Weight = ws.Value; ApplyPose(); };
                sideItems.Add(ws);
                Stepper mk = new Stepper(); mk.Caption = Loc.T("Кости");
                mk.Items = new string[] { Loc.T("По weightlist секвенции"), Loc.T("Все кости"), l.Root >= 0 && l.Root < model.Anim.BoneCount ? Loc.T("От кости: ") + model.Anim.Main.Bones[l.Root].Name : Loc.T("Выбрать кость…") };
                mk.SetIndexSilently(l.Mask);
                mk.Changed += delegate
                {
                    l.Mask = mk.Index;
                    if (l.Mask == 2) { PickBone(l, mk); return; }
                    ApplyPose();
                };
                sideItems.Add(mk);
                Stepper md = new Stepper(); md.Caption = Loc.T("Режим");
                md.Items = new string[] { Loc.T("Авто (по типу анимации)"), Loc.T("Смешать (заменить кости)"), Loc.T("Добавить (минус кадр 0)") };
                md.SetIndexSilently(l.Mode);
                md.Changed += delegate { l.Mode = md.Index; ApplyPose(); };
                sideItems.Add(md);
                sideItems.Add(ToggleBtn(Loc.T("Фаза как у основной"), l.Sync, delegate(bool v) { l.Sync = v; if (!v) l.Cycle = cycle; ApplyPose(); }));
                FlatButton rm = new FlatButton(Theme.GClose, Loc.T("Убрать слой"), null); rm.BaseRole = 1; rm.LeftAlign = true; rm.Height = 28;
                rm.Click += delegate { layers.Remove(l); AfterLayersChanged(); };
                sideItems.Add(rm);
            }
            if (layers.Count > 0)
            {
                layerSummary = new InfoLabel(); layerSummary.Height = 96;
                sideItems.Add(layerSummary);
                UpdateLayerSummary();
                FlatButton cw = new FlatButton(null, Loc.T("Скопировать weightlist для QC"), Loc.T("Веса костей, которые сейчас выбраны для слоя, в формате $weightlist"));
                cw.BaseRole = 1; cw.LeftAlign = true; cw.Height = 30;
                cw.Click += delegate { CopyLayerQc(false); };
                sideItems.Add(cw);
                FlatButton cq = new FlatButton(null, Loc.T("Скопировать пример $sequence"), Loc.T("Заготовка секвенции со слоем (weightlist, fadein/fadeout, delta)"));
                cq.BaseRole = 1; cq.LeftAlign = true; cq.Height = 30;
                cq.Click += delegate { CopyLayerQc(true); };
                sideItems.Add(cq);
                FlatButton ca = new FlatButton(Theme.GClose, Loc.T("Убрать все слои"), null); ca.BaseRole = 1; ca.LeftAlign = true; ca.Height = 28;
                ca.Click += delegate { layers.Clear(); AfterLayersChanged(); };
                sideItems.Add(ca);
            }
            else layerSummary = null;
        }

        private void UpdateLayerSummary()
        {
            if (layerSummary == null || model == null || model.Anim == null || layerInfl.Length != model.Anim.BoneCount) return;
            int total = layerInfl.Length, used = 0, chg = 0;
            StringBuilder names = new StringBuilder();
            for (int b = 0; b < total; b++)
            {
                if (layerInfl[b] > 0.0001f) used++;
                if (layerInfl[b] > 0.0001f && layerDiff[b])
                {
                    chg++;
                    if (chg <= 10) { if (names.Length > 0) names.Append(", "); names.Append(model.Anim.Main.Bones[b].Name.Replace("ValveBiped.", "")); }
                }
            }
            string t = Loc.T("Задействовано костей: ") + used + Loc.T(" из ") + total + Loc.T("\nРеально отличаются от основной: ") + chg + (chg > 0 ? "\n" + names + (chg > 10 ? "…" : "") : "");
            if (layerSummary.Text != t) { layerSummary.Text = t; layerSummary.Invalidate(); }
        }

        private void AfterLayersChanged()
        {
            if (layers.Count > 0) showSkel = true;
            ApplyPose(); BuildSide(); UpdateTimelineText();
        }

        private void PickLayerSeq()
        {
            if (model == null || model.Anim == null) return;
            List<PickItem> items = new List<PickItem>();
            foreach (SeqRef sr in model.Anim.Seqs)
            {
                if (sr.Missing) continue;
                PickItem it = new PickItem();
                it.Text = sr.Name;
                it.Sub = sr.Frames + Loc.T(" кадр.") + (sr.Additive ? Loc.T("   аддитивная") : "") + (sr.Source.Length > 0 ? "   " + sr.Source : "");
                it.Tag = sr; items.Add(it);
            }
            PickerPopup pp = new PickerPopup(Loc.T("Какую секвенцию наложить (") + items.Count + ")", items, null);
            Point p = Cursor.Position;
            Screen scr = Screen.FromPoint(p);
            pp.Location = new Point(Math.Max(scr.WorkingArea.Left, Math.Min(p.X - pp.Width / 2, scr.WorkingArea.Right - pp.Width)), Math.Max(scr.WorkingArea.Top, Math.Min(p.Y - 40, scr.WorkingArea.Bottom - pp.Height)));
            pp.FormClosed += delegate
            {
                SeqRef sr = pp.Picked == null ? null : pp.Picked.Tag as SeqRef;
                if (sr == null) return;
                LayerSpec l = new LayerSpec(); l.Seq = sr;
                if (curSeq == null && layers.Count == 0) { l.Sync = true; }
                layers.Add(l);
                if (!playing && sr.Frames > 1) { playing = true; clock.Reset(); clock.Start(); animTimer.Start(); UpdatePlayGlyph(); }
                AfterLayersChanged();
            };
            pp.Show(FindForm());
        }

        private void PickBone(LayerSpec l, Stepper owner)
        {
            AnimSet a = model.Anim;
            List<PickItem> items = new List<PickItem>();
            for (int b = 0; b < a.BoneCount; b++)
            {
                PickItem it = new PickItem(); it.Text = a.Main.Bones[b].Name; it.Sub = "#" + b; it.Tag = b.ToString(); items.Add(it);
            }
            PickerPopup pp = new PickerPopup(Loc.T("Корневая кость слоя (") + items.Count + ")", items, null);
            Point p = Cursor.Position;
            Screen scr = Screen.FromPoint(p);
            pp.Location = new Point(Math.Max(scr.WorkingArea.Left, Math.Min(p.X - pp.Width, scr.WorkingArea.Right - pp.Width)), Math.Max(scr.WorkingArea.Top, Math.Min(p.Y - 40, scr.WorkingArea.Bottom - pp.Height)));
            pp.FormClosed += delegate
            {
                if (pp.Picked != null) l.Root = int.Parse((string)pp.Picked.Tag);
                else if (l.Root < 0) l.Mask = 0;
                AfterLayersChanged();
            };
            pp.Show(FindForm());
        }

        private void ShowSeqMenu(Point screen)
        {
            if (model == null || model.Anim == null) return;
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            ToolStripMenuItem a = new ToolStripMenuItem(Loc.T("Наложить слой…"));
            a.Click += delegate { PickLayerSeq(); };
            ToolStripMenuItem b = new ToolStripMenuItem(Loc.T("Убрать все слои (") + layers.Count + ")");
            b.Enabled = layers.Count > 0;
            b.Click += delegate { layers.Clear(); AfterLayersChanged(); };
            m.Items.Add(a); m.Items.Add(b);
            m.Show(screen);
        }

        private void CopyLayerQc(bool sequence)
        {
            if (layers.Count == 0 || model == null || model.Anim == null) return;
            AnimSet a = model.Anim;
            StringBuilder sb = new StringBuilder();
            foreach (LayerSpec l in layers)
            {
                string wn = l.Seq.Name + "_weights";
                float[] w = a.LayerWeights(l);
                sb.AppendLine("$weightlist \"" + wn + "\" {");
                for (int b = 0; b < w.Length; b++) sb.AppendLine("\t\"" + a.Main.Bones[b].Name + "\" " + w[b].ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("}");
                sb.AppendLine();
                if (sequence)
                {
                    bool add = l.Mode == 2 || (l.Mode == 0 && l.Seq.Additive);
                    sb.AppendLine("$sequence \"" + l.Seq.Name + "_layer\" {");
                    sb.AppendLine("\t\"anims\\" + l.Seq.Name + ".smd\"");
                    sb.AppendLine("\tweightlist \"" + wn + "\"");
                    sb.AppendLine("\tfadein 0.2");
                    sb.AppendLine("\tfadeout 0.2");
                    if (add) { sb.AppendLine("\tdelta"); sb.AppendLine("\tsubtract \"" + l.Seq.Name + Loc.T("\" 0   // вычесть кадр 0 (для $animation)")); }
                    sb.AppendLine("}");
                    sb.AppendLine();
                }
            }
            try { Clipboard.SetText(sb.ToString()); host.Notify(NoteKind.Info, Loc.T("Скопировано"), sequence ? Loc.T("Заготовка $weightlist и $sequence в буфере обмена") : Loc.T("$weightlist в буфере обмена")); }
            catch (Exception ex) { host.Notify(NoteKind.Bad, Loc.T("Не удалось скопировать"), ex.Message); }
        }

        private void ShowMatMenu(int row, Point screen)
        {
            MatInfo mi = mats[row];
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            string vmt = mi.VmtPath, vtf = mi.VtfPath;
            ToolStripMenuItem a = new ToolStripMenuItem(Loc.T("Показать VMT в Проводнике"));
            a.Enabled = vmt != null; a.Click += delegate { Reveal(vmt); };
            ToolStripMenuItem b = new ToolStripMenuItem(Loc.T("Показать текстуру (VTF) в Проводнике"));
            b.Enabled = vtf != null; b.Click += delegate { Reveal(vtf); };
            m.Items.Add(a); m.Items.Add(b);
            ToolInfo ve = null;
            foreach (ToolInfo t in host.Tools) if (t.Name == "VMT Editor" && t.Exists) ve = t;
            if (ve != null && vmt != null)
            {
                ToolStripMenuItem c = new ToolStripMenuItem(Loc.T("Открыть VMT в VMT Editor"));
                c.Click += delegate { host.OpenTool(ve, "\"" + vmt + "\"", Area); };
                m.Items.Add(c);
            }
            m.Show(screen);
        }

        private static void Reveal(string p)
        {
            try { Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch { }
        }

        // ---------------- автообновление ----------------
        private void SetupWatchers()
        {
            foreach (FileSystemWatcher w in watchers) w.Dispose();
            watchers.Clear();
            if (!autoReload || model == null) return;
            HashSet<string> dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { dirs.Add(Path.GetDirectoryName(model.Path)); } catch { }
            foreach (string r in roots)
            {
                string mp = Path.Combine(r, "materials");
                if (Directory.Exists(mp)) dirs.Add(mp);
            }
            foreach (string d in dirs)
            {
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(d);
                    w.IncludeSubdirectories = !string.Equals(d, Path.GetDirectoryName(model.Path), StringComparison.OrdinalIgnoreCase);
                    w.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                    w.Changed += OnFileEvent; w.Created += OnFileEvent; w.Renamed += OnFileEvent;
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch { }
            }
        }

        private void OnFileEvent(object s, FileSystemEventArgs e)
        {
            string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (ext != ".vtf" && ext != ".vmt" && ext != ".mdl" && ext != ".vvd" && ext != ".vtx" && ext != ".phy") return;
            lock (pending) pending.Add(e.FullPath);
            try { BeginInvoke(new MethodInvoker(delegate { reloadTimer.Stop(); reloadTimer.Start(); })); } catch { }
        }

        private void ProcessChanges()
        {
            List<string> changed;
            lock (pending) { changed = new List<string>(pending); pending.Clear(); }
            if (model == null || changed.Count == 0) return;

            bool modelChanged = false;
            foreach (string f in changed)
                foreach (string mf in model.Files)
                    if (string.Equals(f, mf, StringComparison.OrdinalIgnoreCase)) modelChanged = true;
            if (modelChanged) { LoadModel(model.Path, true); return; }

            List<string> updated = new List<string>();
            bool failed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                bool hit = !mats[i].Ok;
                foreach (string f in changed)
                    foreach (string mf in mats[i].Files)
                        if (string.Equals(f, mf, StringComparison.OrdinalIgnoreCase)) hit = true;
                if (!hit) continue;
                MatInfo old = mats[i];
                MatInfo nm = MatResolver.Resolve(model.TexNames[i], model.CdTextures, roots, 2048);
                if (!nm.Ok && old.Ok) { failed = true; continue; }       // файл ещё пишется - повторим
                if (nm.Ok || !old.Ok) { mats[i] = nm; dirty[i] = true; if (nm.Ok) updated.Add(model.TexNames[i]); }
            }
            if (failed && retries < 4) { retries++; foreach (string f in changed) lock (pending) pending.Add(f); reloadTimer.Start(); return; }
            retries = 0;
            if (updated.Count > 0)
            {
                if (matList != null) { matList.Items = new List<MatInfo>(mats); matList.Invalidate(); }
                host.Notify(NoteKind.Good, Loc.T("Текстура обновлена"), string.Join(", ", updated.ToArray()));
                surface.Invalidate();
            }
        }

        private void ReloadAll(bool quiet)
        {
            if (modelPath != null && File.Exists(modelPath)) LoadModel(modelPath, true);
            else BrowseModel();
        }


        // ---------------- подсказки при наведении ----------------
        private void CamBasis(out float[] eye, out float[] f, out float[] s, out float[] u)
        {
            float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
            eye = new float[] { target[0] + dist * cp * (float)Math.Cos(yaw), target[1] + dist * cp * (float)Math.Sin(yaw), target[2] + dist * sp };
            f = new float[] { target[0] - eye[0], target[1] - eye[1], target[2] - eye[2] }; Norm(f);
            s = Cross(f, new float[] { 0, 0, 1 }); Norm(s);
            u = Cross(s, f);
        }

        private static float Dot(float[] a, float[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }

        private static bool Project(float[] p, float[] eye, float[] f, float[] s, float[] u, float tanH, float aspect, int w, int h, out float px, out float py)
        {
            float[] d = { p[0] - eye[0], p[1] - eye[1], p[2] - eye[2] };
            float depth = Dot(d, f);
            px = py = 0;
            if (depth <= 0.01f) return false;
            float x = Dot(d, s) / (depth * tanH * aspect), y = Dot(d, u) / (depth * tanH);
            px = (x + 1f) * 0.5f * w; py = (1f - y) * 0.5f * h;
            return true;
        }

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (py - ay) * dy) / l2));
            float cx = ax + t * dx - px, cy = ay + t * dy - py;
            return (float)Math.Sqrt(cx * cx + cy * cy);
        }

        // луч против коробки в системе кости; возвращает расстояние или -1
        private float RayBox(float[] eye, float[] dir, int bone, float[] mn, float[] mx)
        {
            int bo = bone * 12;
            if (bo + 12 > world.Length) return -1f;
            float[] o = new float[3], d = new float[3];
            for (int j = 0; j < 3; j++)
            {
                float oo = 0, dd = 0;
                for (int i = 0; i < 3; i++)
                {
                    float r = world[bo + i * 4 + j];
                    oo += r * (eye[i] - world[bo + i * 4 + 3]);
                    dd += r * dir[i];
                }
                o[j] = oo; d[j] = dd;
            }
            float t0 = 0f, t1 = float.MaxValue;
            for (int j = 0; j < 3; j++)
            {
                if (Math.Abs(d[j]) < 1e-8f) { if (o[j] < mn[j] || o[j] > mx[j]) return -1f; continue; }
                float a = (mn[j] - o[j]) / d[j], b = (mx[j] - o[j]) / d[j];
                if (a > b) { float tmp = a; a = b; b = tmp; }
                if (a > t0) t0 = a;
                if (b < t1) t1 = b;
                if (t0 > t1) return -1f;
            }
            return t0;
        }

        private void UpdateHover(Point mp)
        {
            int nb = -1, nbx = -1, ns = -1;
            string text = null;
            if (model != null && model.Anim != null && world.Length == model.Anim.BoneCount * 12 && (showSkel || showHit || showPhy) && surface.Width > 0)
            {
                AnimSet a = model.Anim;
                int w = surface.Width, h = surface.Height;
                float tanH = (float)Math.Tan(0.4), aspect = (float)w / Math.Max(1, h);
                float[] eye, f, s, u;
                CamBasis(out eye, out f, out s, out u);

                if (showSkel)
                {
                    float best = 9f;
                    for (int b = 0; b < a.BoneCount; b++)
                    {
                        float px, py;
                        float[] pb = { world[b * 12 + 3], world[b * 12 + 7], world[b * 12 + 11] };
                        if (!Project(pb, eye, f, s, u, tanH, aspect, w, h, out px, out py)) continue;
                        float dj = (float)Math.Sqrt((px - mp.X) * (px - mp.X) + (py - mp.Y) * (py - mp.Y)) - 2f;
                        if (dj < best) { best = dj; nb = b; }
                        int par = a.Main.Bones[b].Parent;
                        if (par >= 0)
                        {
                            float qx, qy;
                            float[] pp = { world[par * 12 + 3], world[par * 12 + 7], world[par * 12 + 11] };
                            if (Project(pp, eye, f, s, u, tanH, aspect, w, h, out qx, out qy))
                            {
                                float ds = SegDist(mp.X, mp.Y, qx, qy, px, py);
                                if (ds < Math.Min(best, 7f)) { best = ds; nb = b; }
                            }
                        }
                    }
                }

                if (nb < 0 && (showHit || showPhy))
                {
                    float nx = 2f * mp.X / w - 1f, ny = 1f - 2f * mp.Y / h;
                    float[] dir = { f[0] + s[0] * nx * tanH * aspect + u[0] * ny * tanH, f[1] + s[1] * nx * tanH * aspect + u[1] * ny * tanH, f[2] + s[2] * nx * tanH * aspect + u[2] * ny * tanH };
                    Norm(dir);
                    float bestT = float.MaxValue;
                    if (showHit && a.Main.HitSets.Count > 0)
                    {
                        HitSet hs = a.Main.HitSets[Math.Min(hitSet, a.Main.HitSets.Count - 1)];
                        for (int i = 0; i < hs.Boxes.Count; i++)
                        {
                            HitBox hb = hs.Boxes[i];
                            if (hb.Bone < 0 || hb.Bone >= a.BoneCount) continue;
                            float t = RayBox(eye, dir, hb.Bone, hb.Min, hb.Max);
                            if (t >= 0 && t < bestT) { bestT = t; nbx = i; ns = -1; }
                        }
                    }
                    if (showPhy && model.Phy != null)
                        for (int i = 0; i < model.Phy.Solids.Count; i++)
                        {
                            PhySolid sd = model.Phy.Solids[i];
                            if (sd.Tris.Length == 0) continue;
                            float t = RayBox(eye, dir, Math.Max(0, Math.Min(sd.Bone, a.BoneCount - 1)), sd.BMin, sd.BMax);
                            if (t >= 0 && t < bestT) { bestT = t; ns = i; nbx = -1; }
                        }
                }

                if (nb >= 0)
                {
                    int par = a.Main.Bones[nb].Parent;
                    text = Loc.T("Кость: ") + a.Main.Bones[nb].Name + Loc.T("\nИндекс ") + nb + Loc.T("   ·   родитель: ") + (par >= 0 ? a.Main.Bones[par].Name : "—");
                    if (layers.Count > 0 && nb < layerInfl.Length)
                        text += Loc.T("\nСлои: ") + (layerInfl[nb] <= 0.0001f ? Loc.T("не затрагивает (основная анимация)") : Loc.T("вес ") + layerInfl[nb].ToString("0.##", CultureInfo.InvariantCulture) + (layerDiff[nb] ? Loc.T(", кость меняется") : Loc.T(", совпадает с основной")));
                }
                else if (nbx >= 0)
                {
                    HitBox hb = a.Main.HitSets[Math.Min(hitSet, a.Main.HitSets.Count - 1)].Boxes[nbx];
                    string bn = hb.Bone >= 0 && hb.Bone < a.BoneCount ? a.Main.Bones[hb.Bone].Name : "?";
                    text = Loc.T("Хитбокс: ") + (hb.Name.Length > 0 ? hb.Name : bn) + Loc.T("\nГруппа: ") + hb.Group + " — " + HitGroups[Math.Max(0, Math.Min(hb.Group, HitGroups.Length - 1))] +
                        Loc.T("\nКость: ") + bn + Loc.T("\nРазмер: ") + (hb.Max[0] - hb.Min[0]).ToString("0.#", CultureInfo.InvariantCulture) + " × " + (hb.Max[1] - hb.Min[1]).ToString("0.#", CultureInfo.InvariantCulture) + " × " + (hb.Max[2] - hb.Min[2]).ToString("0.#", CultureInfo.InvariantCulture);
                }
                else if (ns >= 0)
                {
                    PhySolid sd = model.Phy.Solids[ns];
                    text = Loc.T("Физическое тело #") + sd.Index + Loc.T("\nКость: ") + (sd.Bone >= 0 && sd.Bone < a.BoneCount ? a.Main.Bones[sd.Bone].Name : "?");
                }
            }
            if (nb != hoverBone || nbx != hoverBox || ns != hoverSolid) { hoverBone = nb; hoverBox = nbx; hoverSolid = ns; surface.Invalidate(); }
            if (text == null) { if (hoverTip != null) hoverTip.Visible = false; return; }
            if (hoverTip == null) { hoverTip = new HoverTip(); stage.Controls.Add(hoverTip); }
            hoverTip.SetText(text);
            int tx = mp.X + 16, ty = mp.Y + 18;
            if (tx + hoverTip.Width > stage.Width) tx = mp.X - hoverTip.Width - 8;
            if (ty + hoverTip.Height > stage.Height) ty = mp.Y - hoverTip.Height - 8;
            hoverTip.Location = new Point(Math.Max(0, tx), Math.Max(0, ty));
            hoverTip.Visible = true;
            hoverTip.BringToFront();
        }

        private void ClearHover()
        {
            if (hoverTip != null) hoverTip.Visible = false;
            if (hoverBone >= 0 || hoverBox >= 0 || hoverSolid >= 0) { hoverBone = hoverBox = hoverSolid = -1; surface.Invalidate(); }
        }

        // ---------------- камера и мышь ----------------
        private void OnSurfaceMouseMove(object s, MouseEventArgs e)
        {
            if (dragButton == MouseButtons.None) { UpdateHover(e.Location); return; }
            if (hoverTip != null && hoverTip.Visible) ClearHover();
            userMoved = true;
            int dx = e.X - lastMouse.X, dy = e.Y - lastMouse.Y;
            lastMouse = e.Location;
            if (dragButton == MouseButtons.Left && (Control.ModifierKeys & Keys.Shift) != 0)
            {
                lightYaw += dx * 0.01f;
                lightPitch = Math.Max(-1.4f, Math.Min(1.4f, lightPitch + dy * 0.01f));
            }
            else if (dragButton == MouseButtons.Left)
            {
                yaw -= dx * 0.008f;
                pitch = Math.Max(-1.5f, Math.Min(1.5f, pitch + dy * 0.008f));
            }
            else
            {
                float k = dist * 0.0016f;
                float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
                target[0] += (sy * dx) * k; target[1] += (-cy * dx) * k; target[2] += dy * k;
            }
            surface.Invalidate();
        }

        // ---------------- рендер ----------------
        private static float[] Perspective(float fovY, float aspect, float n, float f)
        {
            float t = (float)(1.0 / Math.Tan(fovY / 2));
            float[] m = new float[16];
            m[0] = t / aspect; m[5] = t; m[10] = (f + n) / (n - f); m[11] = -1; m[14] = 2 * f * n / (n - f);
            return m;
        }

        private static float[] LookAt(float[] eye, float[] c, float[] up)
        {
            float[] f = { c[0] - eye[0], c[1] - eye[1], c[2] - eye[2] };
            Norm(f);
            float[] s = Cross(f, up); Norm(s);
            float[] u = Cross(s, f);
            float[] m = new float[16];
            m[0] = s[0]; m[4] = s[1]; m[8] = s[2];
            m[1] = u[0]; m[5] = u[1]; m[9] = u[2];
            m[2] = -f[0]; m[6] = -f[1]; m[10] = -f[2];
            m[12] = -(s[0] * eye[0] + s[1] * eye[1] + s[2] * eye[2]);
            m[13] = -(u[0] * eye[0] + u[1] * eye[1] + u[2] * eye[2]);
            m[14] = f[0] * eye[0] + f[1] * eye[1] + f[2] * eye[2];
            m[15] = 1;
            return m;
        }

        private static void Norm(float[] v)
        {
            float l = (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (l > 1e-6f) { v[0] /= l; v[1] /= l; v[2] /= l; }
        }

        private static float[] Cross(float[] a, float[] b)
        {
            return new float[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private GpuTex MakeChecker()
        {
            byte[] px = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool c = ((x / 8) + (y / 8)) % 2 == 0;
                    int i = (y * 64 + x) * 4;
                    px[i] = (byte)(c ? 255 : 40); px[i + 1] = (byte)(c ? 0 : 40); px[i + 2] = (byte)(c ? 255 : 40); px[i + 3] = 255;
                }
            return Upload(64, 64, px);
        }

        private GpuTex Upload(int w, int h, byte[] rgba)
        {
            uint[] id = new uint[1];
            GL.glGenTextures(1, id);
            GL.glBindTexture(GL.TEXTURE_2D, id[0]);
            GL.glPixelStorei(GL.UNPACK_ALIGNMENT, 1);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.REPEAT);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.REPEAT);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR_MIPMAP_LINEAR);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.GENERATE_MIPMAP, 1);
            GL.glTexParameterf(GL.TEXTURE_2D, 0x84FE, 8f);       // анизотропия (если поддерживается)
            GL.glGetError();
            GL.glTexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA, w, h, 0, GL.RGBA, GL.UNSIGNED_BYTE, rgba);
            GpuTex t = new GpuTex(); t.Id = id[0]; t.W = w; t.H = h;
            return t;
        }

        private void UploadDirty()
        {
            if (checker == null) checker = MakeChecker();
            for (int i = 0; i < mats.Length; i++)
            {
                if (!dirty[i]) continue;
                dirty[i] = false;
                if (gpu[i] != null) { GL.glDeleteTextures(1, new uint[] { gpu[i].Id }); gpu[i] = null; }
                if (mats[i].Ok) gpu[i] = Upload(mats[i].Image.Width, mats[i].Image.Height, mats[i].Image.Rgba);
            }
        }

        private static float[] C4(float r, float g, float b, float a) { return new float[] { r, g, b, a }; }

        private void RenderScene(int w, int h)
        {
            Color bg = Theme.Bg1;
            GL.glViewport(0, 0, w, h);
            GL.glClearColor(bg.R / 255f * 0.9f, bg.G / 255f * 0.9f, bg.B / 255f * 0.9f, 1f);
            GL.glClear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
            if (model == null) return;
            UploadDirty();

            float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
            float[] eye = { target[0] + dist * cp * (float)Math.Cos(yaw), target[1] + dist * cp * (float)Math.Sin(yaw), target[2] + dist * sp };
            GL.glMatrixMode(GL.PROJECTION);
            GL.glLoadMatrixf(Perspective(0.8f, (float)w / Math.Max(1, h), Math.Max(0.5f, dist * 0.02f), dist * 30f + radius * 10f));
            GL.glMatrixMode(GL.MODELVIEW);
            GL.glLoadMatrixf(LookAt(eye, target, new float[] { 0, 0, 1 }));

            GL.glEnable(GL.DEPTH_TEST);
            GL.glDepthFunc(GL.LEQUAL);
            GL.glShadeModel(GL.SMOOTH);
            GL.glDisable(GL.CULL_FACE);
            GL.glEnable(GL.NORMALIZE);

            // ---- свет ----
            bool lit = lightMode != 2;
            if (lit)
            {
                float ly = lightYaw, lp = lightPitch;
                float[] L = { (float)(Math.Cos(lp) * Math.Cos(ly)), (float)(Math.Cos(lp) * Math.Sin(ly)), (float)Math.Sin(lp), 0f };
                GL.glEnable(GL.LIGHTING);
                GL.glLightModeli(GL.LIGHT_MODEL_TWO_SIDE, 1);
                GL.glLightModelfv(GL.LIGHT_MODEL_AMBIENT, C4(ambient, ambient, ambient, 1f));
                GL.glLightModeli(GL.LIGHT_MODEL_COLOR_CONTROL, (int)GL.SEPARATE_SPECULAR_COLOR);
                if (lightMode == 1)
                {
                    float[] save = LookAt(eye, target, new float[] { 0, 0, 1 });
                    GL.glLoadIdentity();
                    GL.glLightfv(GL.LIGHT0, GL.POSITION, C4(0.3f, 0.4f, 1f, 0f));
                    GL.glLoadMatrixf(save);
                    GL.glDisable(GL.LIGHT1);
                }
                else
                {
                    GL.glLightfv(GL.LIGHT0, GL.POSITION, L);
                    GL.glLightfv(GL.LIGHT1, GL.POSITION, C4(-L[0], -L[1], 0.25f, 0f));
                    GL.glLightfv(GL.LIGHT1, GL.DIFFUSE, C4(0.35f * power, 0.42f * power, 0.55f * power, 1f));
                    GL.glLightfv(GL.LIGHT1, GL.SPECULAR, C4(0f, 0f, 0f, 1f));
                    GL.glLightfv(GL.LIGHT1, GL.AMBIENT, C4(0f, 0f, 0f, 1f));
                    GL.glEnable(GL.LIGHT1);
                }
                GL.glLightfv(GL.LIGHT0, GL.DIFFUSE, C4(1f * power, 0.97f * power, 0.92f * power, 1f));
                GL.glLightfv(GL.LIGHT0, GL.SPECULAR, C4(0.5f * power, 0.5f * power, 0.5f * power, 1f));
                GL.glLightfv(GL.LIGHT0, GL.AMBIENT, C4(0f, 0f, 0f, 1f));
                GL.glEnable(GL.LIGHT0);
                GL.glEnable(GL.COLOR_MATERIAL);
                GL.glColorMaterial(GL.FRONT_AND_BACK, GL.AMBIENT_AND_DIFFUSE);
                GL.glMaterialfv(GL.FRONT_AND_BACK, GL.SPECULAR, C4(0.25f, 0.25f, 0.25f, 1f));
                GL.glMaterialf(GL.FRONT_AND_BACK, GL.SHININESS, 28f);
            }
            else GL.glDisable(GL.LIGHTING);

            if (showGrid) DrawGrid();

            // ---- геометрия ----
            if (showMesh)
            {
                GL.glEnableClientState(GL.VERTEX_ARRAY);
                GL.glEnableClientState(GL.NORMAL_ARRAY);
                GL.glEnableClientState(GL.TEXTURE_COORD_ARRAY);
                GL.glVertexPointer(3, GL.FLOAT, 0, skinned ? hSkPos.AddrOfPinnedObject() : hPos.AddrOfPinnedObject());
                GL.glNormalPointer(GL.FLOAT, 0, skinned ? hSkNrm.AddrOfPinnedObject() : hNrm.AddrOfPinnedObject());
                GL.glTexCoordPointer(2, GL.FLOAT, 0, hUv.AddrOfPinnedObject());

                for (int pass = 0; pass < 2; pass++)
                {
                    for (int bp = 0; bp < model.Parts.Count; bp++)
                    {
                        MdlBodyPart part = model.Parts[bp];
                        MdlBodyModel bm = part.Models[Math.Max(0, Math.Min(bodySel[bp], part.Models.Count - 1))];
                        foreach (MdlSub sub in bm.Subs)
                        {
                            int fam = Math.Max(0, Math.Min(skin, model.Skins.Length - 1));
                            int texIdx = sub.SkinRef >= 0 && sub.SkinRef < model.Skins[fam].Length ? model.Skins[fam][sub.SkinRef] : sub.SkinRef;
                            MatInfo mi = texIdx >= 0 && texIdx < mats.Length ? mats[texIdx] : null;
                            bool blend = mi != null && mi.Translucent && !mi.AlphaTest;
                            if ((pass == 1) != blend) continue;

                            GpuTex tex = null;
                            if (lightMode != 3)
                                tex = (texIdx >= 0 && texIdx < gpu.Length && gpu[texIdx] != null) ? gpu[texIdx] : checker;
                            if (tex != null) { GL.glEnable(GL.TEXTURE_2D); GL.glBindTexture(GL.TEXTURE_2D, tex.Id); GL.glTexEnvi(GL.TEXTURE_ENV, GL.TEXTURE_ENV_MODE, (int)GL.MODULATE); }
                            else GL.glDisable(GL.TEXTURE_2D);

                            if (lightMode == 3) GL.glColor4f(0.82f, 0.82f, 0.82f, 1f);
                            else if (mi != null) GL.glColor4f(Math.Min(1f, mi.Tint[0]), Math.Min(1f, mi.Tint[1]), Math.Min(1f, mi.Tint[2]), 1f);
                            else GL.glColor4f(1f, 1f, 1f, 1f);

                            if (mi != null && mi.AlphaTest) { GL.glEnable(GL.ALPHA_TEST); GL.glAlphaFunc(GL.GREATER, 0.5f); } else GL.glDisable(GL.ALPHA_TEST);
                            if (blend) { GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA); GL.glDepthMask(0); }
                            else { GL.glDisable(GL.BLEND); GL.glDepthMask(1); }

                            GL.glPolygonMode(GL.FRONT_AND_BACK, GL.FILL);
                            GL.glDrawElements(GL.TRIANGLES, sub.Index.Length, GL.UNSIGNED_INT, idxPtr[sub]);

                            if (wire)
                            {
                                GL.glDisable(GL.TEXTURE_2D); GL.glDisable(GL.LIGHTING); GL.glDisable(GL.BLEND); GL.glDisable(GL.ALPHA_TEST);
                                Color ac = Theme.Accent;
                                GL.glColor4f(ac.R / 255f, ac.G / 255f, ac.B / 255f, 1f);
                                GL.glPolygonMode(GL.FRONT_AND_BACK, GL.LINE);
                                GL.glEnable(GL.POLYGON_OFFSET_FILL); GL.glPolygonOffset(-1f, -1f);
                                GL.glDrawElements(GL.TRIANGLES, sub.Index.Length, GL.UNSIGNED_INT, idxPtr[sub]);
                                GL.glDisable(GL.POLYGON_OFFSET_FILL);
                                GL.glPolygonMode(GL.FRONT_AND_BACK, GL.FILL);
                                if (lit) GL.glEnable(GL.LIGHTING);
                            }
                        }
                    }
                }
                GL.glDepthMask(1);
                GL.glDisableClientState(GL.TEXTURE_COORD_ARRAY);
                GL.glDisableClientState(GL.NORMAL_ARRAY);
                GL.glDisableClientState(GL.VERTEX_ARRAY);
            }
            GL.glDisable(GL.ALPHA_TEST); GL.glDisable(GL.BLEND); GL.glDisable(GL.TEXTURE_2D); GL.glDisable(GL.LIGHTING);

            DrawOverlays();
        }

        // ---- скелет, хитбоксы, физика ----
        private void DrawOverlays()
        {
            if (model == null || model.Anim == null || (!showSkel && !showHit && !showPhy)) return;
            if (world.Length != model.Anim.BoneCount * 12) return;
            AnimSet a = model.Anim;
            GL.glDisable(GL.DEPTH_TEST);
            GL.glEnable(GL.BLEND); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);

            if (showPhy && model.Phy != null)
            {
                foreach (PhySolid s in model.Phy.Solids)
                {
                    if (s.Tris.Length == 0) continue;
                    float[] m = BoneMat(Math.Max(0, Math.Min(s.Bone, a.BoneCount - 1)) * 12);
                    bool hotS = s.Index == hoverSolid || model.Phy.Solids.IndexOf(s) == hoverSolid;
                    GL.glColor4f(0.35f, 0.9f, 0.55f, hotS ? 0.45f : 0.16f);
                    GL.glBegin(GL.TRIANGLES);
                    for (int i = 0; i < s.Tris.Length; i++) PhyVert(s, s.Tris[i], m);
                    GL.glEnd();
                    GL.glColor4f(0.35f, 0.95f, 0.6f, 0.85f);
                    GL.glBegin(GL.LINES);
                    for (int t = 0; t + 2 < s.Tris.Length; t += 3)
                    {
                        PhyVert(s, s.Tris[t], m); PhyVert(s, s.Tris[t + 1], m);
                        PhyVert(s, s.Tris[t + 1], m); PhyVert(s, s.Tris[t + 2], m);
                        PhyVert(s, s.Tris[t + 2], m); PhyVert(s, s.Tris[t], m);
                    }
                    GL.glEnd();
                }
            }

            if (showHit && a.Main.HitSets.Count > 0)
            {
                HitSet hs = a.Main.HitSets[Math.Min(hitSet, a.Main.HitSets.Count - 1)];
                GL.glLineWidth(1.5f);
                int[][] edges = { new int[] { 0, 1 }, new int[] { 2, 3 }, new int[] { 4, 5 }, new int[] { 6, 7 }, new int[] { 0, 2 }, new int[] { 1, 3 }, new int[] { 4, 6 }, new int[] { 5, 7 }, new int[] { 0, 4 }, new int[] { 1, 5 }, new int[] { 2, 6 }, new int[] { 3, 7 } };
                foreach (HitBox hb in hs.Boxes)
                {
                    if (hb.Bone < 0 || hb.Bone >= a.BoneCount) continue;
                    int bo = hb.Bone * 12;
                    float[] hc = HitColors[Math.Max(0, Math.Min(hb.Group, HitColors.Length - 1))];
                    bool hot = hs.Boxes.IndexOf(hb) == hoverBox;
                    GL.glLineWidth(hot ? 3.5f : 1.5f);
                    if (hot) GL.glColor4f(1f, 1f, 1f, 1f); else GL.glColor4f(hc[0], hc[1], hc[2], 0.95f);
                    float[][] c = new float[8][];
                    for (int i = 0; i < 8; i++)
                    {
                        float x = (i & 1) == 0 ? hb.Min[0] : hb.Max[0], y = (i & 2) == 0 ? hb.Min[1] : hb.Max[1], z = (i & 4) == 0 ? hb.Min[2] : hb.Max[2];
                        c[i] = new float[] {
                            world[bo] * x + world[bo + 1] * y + world[bo + 2] * z + world[bo + 3],
                            world[bo + 4] * x + world[bo + 5] * y + world[bo + 6] * z + world[bo + 7],
                            world[bo + 8] * x + world[bo + 9] * y + world[bo + 10] * z + world[bo + 11] };
                    }
                    GL.glBegin(GL.LINES);
                    foreach (int[] e in edges) { GL.glVertex3f(c[e[0]][0], c[e[0]][1], c[e[0]][2]); GL.glVertex3f(c[e[1]][0], c[e[1]][1], c[e[1]][2]); }
                    GL.glEnd();
                }
            }

            if (showSkel)
            {
                Color ac = Theme.Accent;
                bool lay = layers.Count > 0 && layerInfl.Length == a.BoneCount;
                GL.glLineWidth(lay ? 3f : 2f);
                GL.glColor4f(ac.R / 255f, ac.G / 255f, ac.B / 255f, 1f);
                GL.glBegin(GL.LINES);
                for (int b = 0; b < a.BoneCount; b++)
                {
                    int par = a.Main.Bones[b].Parent;
                    if (par < 0) continue;
                    if (lay) LayerColor(b);
                    GL.glVertex3f(world[par * 12 + 3], world[par * 12 + 7], world[par * 12 + 11]);
                    GL.glVertex3f(world[b * 12 + 3], world[b * 12 + 7], world[b * 12 + 11]);
                }
                GL.glEnd();
                GL.glPointSize(6f);
                GL.glColor4f(1f, 1f, 1f, 1f);
                GL.glBegin(GL.POINTS);
                for (int b = 0; b < a.BoneCount; b++)
                {
                    if (lay) LayerColor(b);
                    GL.glVertex3f(world[b * 12 + 3], world[b * 12 + 7], world[b * 12 + 11]);
                }
                GL.glEnd();
                if (hoverBone >= 0 && hoverBone < a.BoneCount)
                {
                    int hb2 = hoverBone, hp = a.Main.Bones[hb2].Parent;
                    GL.glColor4f(1f, 0.95f, 0.4f, 1f);
                    GL.glLineWidth(5f);
                    if (hp >= 0)
                    {
                        GL.glBegin(GL.LINES);
                        GL.glVertex3f(world[hp * 12 + 3], world[hp * 12 + 7], world[hp * 12 + 11]);
                        GL.glVertex3f(world[hb2 * 12 + 3], world[hb2 * 12 + 7], world[hb2 * 12 + 11]);
                        GL.glEnd();
                    }
                    GL.glPointSize(12f);
                    GL.glBegin(GL.POINTS);
                    GL.glVertex3f(world[hb2 * 12 + 3], world[hb2 * 12 + 7], world[hb2 * 12 + 11]);
                    GL.glEnd();
                }
            }
            GL.glLineWidth(1f);
            GL.glDisable(GL.BLEND);
            GL.glEnable(GL.DEPTH_TEST);
        }

        private float[] BoneMat(int boneOff)
        {
            float[] m = new float[12];
            if (boneOff + 12 <= world.Length) Array.Copy(world, boneOff, m, 0, 12);
            else { m[0] = 1; m[5] = 1; m[10] = 1; }
            return m;
        }

        private static readonly string[] HitGroups = { Loc.T("Общий"), Loc.T("Голова"), Loc.T("Грудь"), Loc.T("Живот"), Loc.T("Левая рука"), Loc.T("Правая рука"), Loc.T("Левая нога"), Loc.T("Правая нога"), Loc.T("Снаряжение") };
        private static readonly float[][] HitColors = {
            new float[] { 0.8f, 0.8f, 0.8f }, new float[] { 1f, 0.35f, 0.35f }, new float[] { 1f, 0.62f, 0.2f }, new float[] { 1f, 0.9f, 0.3f },
            new float[] { 0.35f, 0.7f, 1f }, new float[] { 0.35f, 0.95f, 0.95f }, new float[] { 0.55f, 0.95f, 0.4f }, new float[] { 0.8f, 0.5f, 1f }, new float[] { 0.9f, 0.9f, 0.9f } };

        private static void PhyVert(PhySolid s, int vi, float[] m)
        {
            float x = s.V[vi * 3], y = s.V[vi * 3 + 1], z = s.V[vi * 3 + 2];
            GL.glVertex3f(m[0] * x + m[1] * y + m[2] * z + m[3], m[4] * x + m[5] * y + m[6] * z + m[7], m[8] * x + m[9] * y + m[10] * z + m[11]);
        }

        private void DrawGrid()
        {
            GL.glDisable(GL.LIGHTING); GL.glDisable(GL.TEXTURE_2D);
            float step = 1f;
            while (step * 10 < radius * 1.2f) step *= 2f;
            int n = 10;
            float ext = step * n;
            Color bc = Theme.Border;
            GL.glLineWidth(1f);
            GL.glBegin(GL.LINES);
            for (int i = -n; i <= n; i++)
            {
                if (i == 0) continue;
                GL.glColor3f(bc.R / 255f, bc.G / 255f, bc.B / 255f);
                GL.glVertex3f(target[0] + i * step, target[1] - ext, floorZ); GL.glVertex3f(target[0] + i * step, target[1] + ext, floorZ);
                GL.glVertex3f(target[0] - ext, target[1] + i * step, floorZ); GL.glVertex3f(target[0] + ext, target[1] + i * step, floorZ);
            }
            GL.glColor3f(0.85f, 0.3f, 0.3f); GL.glVertex3f(-ext, 0, floorZ); GL.glVertex3f(ext, 0, floorZ);
            GL.glColor3f(0.35f, 0.8f, 0.4f); GL.glVertex3f(0, -ext, floorZ); GL.glVertex3f(0, ext, floorZ);
            GL.glEnd();
        }
    }
}

