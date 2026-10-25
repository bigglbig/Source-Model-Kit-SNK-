// Рабочее пространство в стиле Blender: дерево делимых областей.
// Каждая область = заголовок с вкладками + содержимое (Главная / Проводник / программа).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SourceSDKHub
{
    // ------------------------------------------------------------------
    // Дерево разбиения
    // ------------------------------------------------------------------
    internal abstract class Node { public SplitNode Parent; }

    internal sealed class AreaNode : Node { public Area Area; }

    internal sealed class SplitNode : Node
    {
        public bool Vertical;          // true - области расположены рядом (линия вертикальная)
        public double Ratio = 0.5;     // доля первой области
        public Node A, B;
        public SplitBar Bar;
        public Rectangle Rect;         // прямоугольник, который занимает вся пара
    }

    // Линия между областями - тянется мышью
    internal class SplitBar : Control
    {
        private readonly Workspace ws;
        public readonly SplitNode Node;
        private bool hover, drag;

        public SplitBar(Workspace w, SplitNode n)
        {
            ws = w; Node = n;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = n.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
            BackColor = Theme.Bg0;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { drag = true; Capture = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag) ws.DragBar(Node, Cursor.Position);
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnDoubleClick(EventArgs e)
        {
            Node.Ratio = 0.5; ws.DoLayout();
            base.OnDoubleClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg0);
            if (!hover && !drag) return;
            using (SolidBrush b = new SolidBrush(Theme.Accent))
            {
                if (Node.Vertical) g.FillRectangle(b, Width / 2 - 1, 0, 2, Height);
                else g.FillRectangle(b, 0, Height / 2 - 1, Width, 2);
            }
        }
    }

    // Линия предпросмотра разреза (окно без рамки поверх всего)
    internal class SplitPreview : Form
    {
        public SplitPreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Accent;
            Opacity = 0.9;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW) | 0x20; // + прозрачность для мыши
                return cp;
            }
        }
    }

    // ------------------------------------------------------------------
    // Заголовок области: вкладки, кнопки, угловые ручки
    // ------------------------------------------------------------------
    internal class TabHeader : Control
    {
        private enum Hit { None, GripL, GripR, Tab, Close, Plus, More }

        private sealed class Slot { public Pane Pane; public Rectangle Rect, Close; }

        private const int GripW = 18;
        private readonly Area area;
        private int hoverTab = -1;
        private Hit hoverHit = Hit.None;
        private Pane downPane;
        private Point downPoint;
        private int dropIndex = -1;

        // разрез углом
        private bool gripActive, splitStarted, splitVertical;
        private Point gripStart;
        private SplitPreview preview;

        public TabHeader(Area a)
        {
            area = a;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AllowDrop = true;
            BackColor = Theme.Bg2;
        }

        // ---------- геометрия ----------
        private Rectangle MoreRect() { return new Rectangle(Width - GripW - 30, 3, 28, Height - 6); }

        private List<Slot> ComputeSlots(out Rectangle plus)
        {
            List<Slot> res = new List<Slot>();
            int left = GripW, right = MoreRect().X - 34;
            int avail = Math.Max(60, right - left);
            List<Pane> panes = area.Panes;
            int[] want = new int[panes.Count];
            int sum = 0;
            for (int i = 0; i < panes.Count; i++)
            {
                int tw = TextRenderer.MeasureText(panes[i].Title, Theme.Ui, new Size(1000, 30), TextFormatFlags.NoPadding).Width;
                want[i] = Math.Max(96, Math.Min(230, tw + 66));
                sum += want[i];
            }
            double k = sum > avail ? (double)avail / sum : 1.0;
            int x = left;
            for (int i = 0; i < panes.Count; i++)
            {
                int w = Math.Max(44, (int)(want[i] * k));
                Slot s = new Slot();
                s.Pane = panes[i];
                s.Rect = new Rectangle(x, 0, w, Height);
                s.Close = new Rectangle(x + w - 24, (Height - 18) / 2 + 1, 18, 18);
                res.Add(s);
                x += w;
            }
            plus = new Rectangle(Math.Min(x + 2, right + 2), 3, 28, Height - 6);
            return res;
        }

        private Hit HitTest(Point p, out int tab)
        {
            tab = -1;
            if (p.X < GripW && p.Y < Height) return Hit.GripL;
            if (p.X >= Width - GripW) return Hit.GripR;
            Rectangle plus;
            List<Slot> slots = ComputeSlots(out plus);
            if (plus.Contains(p)) return Hit.Plus;
            if (MoreRect().Contains(p)) return Hit.More;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].Rect.Contains(p)) continue;
                tab = i;
                if (slots[i].Close.Contains(p) && (slots[i].Pane == area.Current || hoverTab == i)) return Hit.Close;
                return Hit.Tab;
            }
            return Hit.None;
        }

        // ---------- рисование ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool active = area.IsActive;

            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);

            Rectangle plus;
            List<Slot> slots = ComputeSlots(out plus);
            for (int i = 0; i < slots.Count; i++)
            {
                Slot s = slots[i];
                bool sel = s.Pane == area.Current;
                bool hov = i == hoverTab;
                Rectangle r = s.Rect;
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Bg1)) g.FillRectangle(b, r.X, 4, r.Width, Height - 4);
                    using (SolidBrush b = new SolidBrush(active ? Theme.Accent : Theme.Border)) g.FillRectangle(b, r.X, 4, r.Width, 2);
                }
                else if (hov)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Bg3)) g.FillRectangle(b, r.X, 8, r.Width, Height - 9);
                }

                Color fg = sel ? Theme.Text : Theme.TextDim;
                Theme.DrawText(g, s.Pane.IconGlyph, Theme.IconSmall, new Rectangle(r.X + 10, 5, 16, Height - 6),
                    sel && active ? Theme.Accent : fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                bool showClose = sel || hov;
                int textRight = r.Right - (showClose ? 28 : 8);
                Theme.DrawText(g, s.Pane.Title, Theme.Ui, new Rectangle(r.X + 32, 5, Math.Max(10, textRight - r.X - 32), Height - 6), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                if (showClose)
                {
                    bool ch = hov && hoverHit == Hit.Close;
                    if (ch) using (SolidBrush b = new SolidBrush(Theme.Bg4)) g.FillEllipse(b, s.Close);
                    Theme.DrawText(g, Theme.GClose, Theme.IconSmall, s.Close, ch ? Theme.Text : Theme.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            if (dropIndex >= 0)
            {
                int dx = dropIndex < slots.Count ? slots[dropIndex].Rect.X : (slots.Count > 0 ? slots[slots.Count - 1].Rect.Right : GripW);
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, dx - 1, 4, 3, Height - 8);
            }

            DrawButton(g, plus, Theme.GAdd, hoverHit == Hit.Plus);
            DrawButton(g, MoreRect(), Theme.GMore, hoverHit == Hit.More);

            // угловые ручки (разрез области)
            using (Pen p = new Pen(hoverHit == Hit.GripL ? Theme.Accent : Theme.Border, 1.5f))
            {
                g.DrawLine(p, 4, 14, 14, 4);
                g.DrawLine(p, 4, 19, 19, 4);
            }
            using (Pen p = new Pen(hoverHit == Hit.GripR ? Theme.Accent : Theme.Border, 1.5f))
            {
                g.DrawLine(p, Width - 15, 4, Width - 5, 14);
                g.DrawLine(p, Width - 20, 4, Width - 5, 19);
            }
        }

        private void DrawButton(Graphics g, Rectangle r, string glyph, bool hov)
        {
            if (hov) using (SolidBrush b = new SolidBrush(Theme.Bg3)) g.FillEllipse(b, r.X, r.Y, r.Width, r.Height);
            Theme.DrawText(g, glyph, Theme.IconSmall, r, hov ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ---------- мышь ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (gripActive) { UpdateSplit(); return; }

            int t;
            Hit h = HitTest(e.Location, out t);
            if (h != hoverHit || t != hoverTab)
            {
                hoverHit = h; hoverTab = t;
                Cursor = (h == Hit.GripL || h == Hit.GripR) ? Cursors.Cross : Cursors.Default;
                Invalidate();
            }

            if (downPane != null && (e.Button & MouseButtons.Left) != 0)
            {
                if (Math.Abs(e.X - downPoint.X) > 6 || Math.Abs(e.Y - downPoint.Y) > 6)
                {
                    Pane p = downPane; downPane = null;
                    Workspace.DragPane = p;
                    try { DoDragDrop("sdk-tab", DragDropEffects.Move); }
                    finally { Workspace.DragPane = null; dropIndex = -1; Invalidate(); }
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverTab != -1 || hoverHit != Hit.None) { hoverTab = -1; hoverHit = Hit.None; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int t;
            Hit h = HitTest(e.Location, out t);
            area.WS.SetActive(area);
            Rectangle plus;
            List<Slot> slots = ComputeSlots(out plus);

            if (e.Button == MouseButtons.Left)
            {
                if (h == Hit.GripL || h == Hit.GripR)
                {
                    gripActive = true; splitStarted = false;
                    gripStart = Cursor.Position;
                    Capture = true;
                }
                else if (h == Hit.Tab || h == Hit.Close)
                {
                    Pane p = slots[t].Pane;
                    if (h == Hit.Tab && p != area.Current) area.Select(p);
                    downPane = h == Hit.Tab ? p : null;
                    downPoint = e.Location;
                    if (h == Hit.Close) downPane = null;
                }
                else if (h == Hit.Plus) area.Host.ShowAddMenu(area, PointToScreen(new Point(plus.X, Height)));
                else if (h == Hit.More) area.WS.ShowAreaMenu(area, PointToScreen(new Point(MoreRect().X, Height)));
            }
            else if (e.Button == MouseButtons.Middle && (h == Hit.Tab || h == Hit.Close))
            {
                area.Host.ClosePane(slots[t].Pane, false);
            }
            else if (e.Button == MouseButtons.Right && (h == Hit.Tab || h == Hit.Close))
            {
                area.Host.ShowPaneMenu(slots[t].Pane, Cursor.Position);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (gripActive)
            {
                gripActive = false; Capture = false;
                FinishSplit();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                int t;
                Hit h = HitTest(e.Location, out t);
                if (h == Hit.Close)
                {
                    Rectangle plus;
                    List<Slot> slots = ComputeSlots(out plus);
                    if (t >= 0 && t < slots.Count) area.Host.ClosePane(slots[t].Pane, false);
                }
            }
            downPane = null;
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int t;
            if (e.Button == MouseButtons.Left && HitTest(e.Location, out t) == Hit.None) area.WS.ToggleMaximize(area);
            base.OnMouseDoubleClick(e);
        }

        // ---------- разрез углом ----------
        private void UpdateSplit()
        {
            Point p = Cursor.Position;
            int dx = p.X - gripStart.X, dy = p.Y - gripStart.Y;
            if (!splitStarted)
            {
                if (Math.Abs(dx) < 8 && Math.Abs(dy) < 8) return;
                splitStarted = true;
                splitVertical = Math.Abs(dx) > Math.Abs(dy);
                if (preview == null) preview = new SplitPreview();
            }
            Rectangle ar = area.RectangleToScreen(area.ClientRectangle);
            Rectangle line;
            if (splitVertical)
            {
                int x = Math.Max(ar.X + 60, Math.Min(ar.Right - 60, p.X));
                line = new Rectangle(x - 1, ar.Y, 3, ar.Height);
            }
            else
            {
                int y = Math.Max(ar.Y + 80, Math.Min(ar.Bottom - 60, p.Y));
                line = new Rectangle(ar.X, y - 1, ar.Width, 3);
            }
            preview.Bounds = line;
            if (!preview.Visible) preview.Show();
        }

        private void FinishSplit()
        {
            if (preview != null) { preview.Hide(); preview.Dispose(); preview = null; }
            if (!splitStarted) return;
            splitStarted = false;
            Point p = Cursor.Position;
            Rectangle ar = area.RectangleToScreen(area.ClientRectangle);
            if (!ar.Contains(p)) return;   // отпустили вне области - отмена
            double ratio = splitVertical ? (double)(p.X - ar.X) / ar.Width : (double)(p.Y - ar.Y) / ar.Height;
            ratio = Math.Max(0.15, Math.Min(0.85, ratio));
            area.Host.SplitArea(area, splitVertical, ratio);
        }

        // ---------- перетаскивание вкладок ----------
        private int IndexAt(int x)
        {
            Rectangle plus;
            List<Slot> slots = ComputeSlots(out plus);
            for (int i = 0; i < slots.Count; i++)
                if (x < slots[i].Rect.X + slots[i].Rect.Width / 2) return i;
            return slots.Count;
        }

        protected override void OnDragEnter(DragEventArgs e) { OnDragOver(e); }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (Workspace.DragPane == null) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            int idx = IndexAt(PointToClient(new Point(e.X, e.Y)).X);
            if (idx != dropIndex) { dropIndex = idx; Invalidate(); }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            dropIndex = -1; Invalidate();
            base.OnDragLeave(e);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            Pane p = Workspace.DragPane;
            int idx = dropIndex < 0 ? area.Panes.Count : dropIndex;
            dropIndex = -1; Invalidate();
            if (p != null) area.WS.MovePane(p, area, idx);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && preview != null) { preview.Dispose(); preview = null; }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    // Область
    // ------------------------------------------------------------------
    internal class Area : Control
    {
        public const int HeaderH = 32;
        public readonly Workspace WS;
        public readonly IShellHost Host;
        public readonly TabHeader Header;
        public readonly List<Pane> Panes = new List<Pane>();
        public Pane Current;

        public Area(Workspace ws, IShellHost host)
        {
            WS = ws; Host = host;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg1;
            Header = new TabHeader(this);
            Controls.Add(Header);
        }

        public bool IsActive { get { return WS.Active == this; } }

        public void AddPane(Pane p, int index, bool select)
        {
            if (p.Area != null && p.Area != this) p.Area.Unlink(p);
            if (!Panes.Contains(p))
            {
                if (index < 0 || index > Panes.Count) Panes.Add(p); else Panes.Insert(index, p);
            }
            p.Area = this;
            p.Visible = false;
            if (p.Parent != this) Controls.Add(p);
            p.TitleChanged -= OnTitleChanged;
            p.TitleChanged += OnTitleChanged;
            LayoutContent();
            if (select || Current == null) Select(p); else Header.Invalidate();
        }

        // Убрать из списка, не трогая элемент управления (для переноса в другую область).
        public void Unlink(Pane p)
        {
            int i = Panes.IndexOf(p);
            if (i < 0) return;
            Panes.RemoveAt(i);
            p.TitleChanged -= OnTitleChanged;
            if (Current == p)
            {
                Current = null;
                if (Panes.Count > 0) Select(Panes[Math.Min(i, Panes.Count - 1)]);
            }
            Header.Invalidate();
        }

        // Окончательно убрать (потом вызывающий делает Dispose).
        public void RemovePane(Pane p)
        {
            Unlink(p);
            if (p.Parent == this) Controls.Remove(p);
            p.Area = null;
        }

        public void Select(Pane p)
        {
            Current = p;
            foreach (Pane q in Panes) q.Visible = q == p;
            LayoutContent();
            Header.Invalidate();
            WS.SetActive(this);
            if (p != null) p.Activated();
        }

        public void SelectOffset(int d)
        {
            if (Panes.Count < 2) return;
            int i = (Panes.IndexOf(Current) + d + Panes.Count) % Panes.Count;
            Select(Panes[i]);
        }

        private void OnTitleChanged(object s, EventArgs e) { Header.Invalidate(); }

        private void LayoutContent()
        {
            Header.SetBounds(0, 0, Width, HeaderH);
            int h = Math.Max(0, Height - HeaderH);
            foreach (Pane p in Panes) p.SetBounds(0, HeaderH, Width, h);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutContent();
        }
    }

    // ------------------------------------------------------------------
    // Рабочее пространство
    // ------------------------------------------------------------------
    internal class Workspace : Control
    {
        public const int Gap = 6;
        public const int Pad = 6;
        private const int MinSize = 140;

        public static Pane DragPane;

        private readonly IShellHost host;
        private readonly List<Area> areas = new List<Area>();
        private Node root;
        private Area active;
        private Area maximized;

        public Workspace(IShellHost h)
        {
            host = h;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg0;
        }

        public Area Active { get { return active; } }
        public Area Maximized { get { return maximized; } }
        public int AreaCount { get { return areas.Count; } }
        public List<Area> Areas { get { return new List<Area>(areas); } }

        // ---------- построение ----------
        private Area CreateArea()
        {
            Area a = new Area(this, host);
            areas.Add(a);
            Controls.Add(a);
            return a;
        }

        public Area InitSingle()
        {
            Area a = CreateArea();
            AreaNode n = new AreaNode(); n.Area = a;
            root = n;
            active = a;
            DoLayout();
            return a;
        }

        private AreaNode Find(Area a) { return FindIn(root, a); }

        private static AreaNode FindIn(Node n, Area a)
        {
            AreaNode an = n as AreaNode;
            if (an != null) return an.Area == a ? an : null;
            SplitNode s = (SplitNode)n;
            return FindIn(s.A, a) ?? FindIn(s.B, a);
        }

        private void Replace(Node old, Node nw)
        {
            SplitNode p = old.Parent;
            if (p == null) root = nw;
            else if (p.A == old) p.A = nw; else p.B = nw;
            nw.Parent = p;
        }

        public Area SplitArea(Area area, bool vertical, double ratio)
        {
            AreaNode an = Find(area);
            Area na = CreateArea();
            AreaNode nn = new AreaNode(); nn.Area = na;
            SplitNode sn = new SplitNode();
            sn.Vertical = vertical; sn.Ratio = ratio;
            Replace(an, sn);
            sn.A = an; sn.B = nn;
            an.Parent = sn; nn.Parent = sn;
            sn.Bar = new SplitBar(this, sn);
            Controls.Add(sn.Bar);
            maximized = null;
            DoLayout();
            return na;
        }

        public void RemoveArea(Area a)
        {
            if (areas.Count <= 1) return;
            AreaNode an = Find(a);
            SplitNode parent = an.Parent;
            Node other = parent.A == an ? parent.B : parent.A;
            Replace(parent, other);
            Controls.Remove(parent.Bar); parent.Bar.Dispose();
            areas.Remove(a);
            Controls.Remove(a);
            a.Dispose();
            if (maximized == a) maximized = null;
            if (active == a || active == null) active = FirstLeaf(other);
            DoLayout();
            if (active != null) active.Header.Invalidate();
        }

        private static Area FirstLeaf(Node n)
        {
            while (true)
            {
                AreaNode an = n as AreaNode;
                if (an != null) return an.Area;
                n = ((SplitNode)n).A;
            }
        }

        // Область, которая примет вкладки закрываемой.
        public Area NeighborOf(Area a)
        {
            AreaNode an = Find(a);
            if (an == null || an.Parent == null) return null;
            Node other = an.Parent.A == an ? an.Parent.B : an.Parent.A;
            return FirstLeaf(other);
        }

        // Снимает все панели и уничтожает области. Возвращает панели (для повторного использования).
        public List<Pane> Teardown()
        {
            List<Pane> all = new List<Pane>();
            foreach (Area a in areas) all.AddRange(a.Panes);
            foreach (Pane p in all) p.Area.Unlink(p);
            foreach (Pane p in all) { if (p.Parent != null) p.Parent.Controls.Remove(p); p.Area = null; }
            foreach (Area a in new List<Area>(areas)) { Controls.Remove(a); a.Dispose(); }
            areas.Clear();
            foreach (Control c in new List<Control>(SplitBars())) { Controls.Remove(c); c.Dispose(); }
            root = null; active = null; maximized = null;
            return all;
        }

        private IEnumerable<Control> SplitBars()
        {
            foreach (Control c in Controls) if (c is SplitBar) yield return c;
        }

        // ---------- активная область / развёртывание ----------
        public void SetActive(Area a)
        {
            if (a == active) return;
            Area old = active;
            active = a;
            if (old != null && !old.IsDisposed) old.Header.Invalidate();
            if (a != null) a.Header.Invalidate();
        }

        public void ToggleMaximize(Area a)
        {
            if (areas.Count <= 1) return;
            maximized = maximized == a ? null : a;
            SetActive(a);
            DoLayout();
            host.SetStatus(maximized != null ? Loc.T("Область развёрнута (Ctrl+Пробел — вернуть)") : Loc.T("Раскладка восстановлена"));
        }

        public Area AreaAt(Point screen)
        {
            foreach (Area a in areas)
                if (a.Visible && a.RectangleToScreen(a.ClientRectangle).Contains(screen)) return a;
            return null;
        }

        public void MovePane(Pane p, Area target, int index)
        {
            Area src = p.Area;
            if (src == null) return;
            if (src == target)
            {
                int old = target.Panes.IndexOf(p);
                if (old < 0) return;
                if (old < index) index--;
                if (index == old) { target.Select(p); return; }
                target.Panes.RemoveAt(old);
                target.Panes.Insert(Math.Max(0, Math.Min(index, target.Panes.Count)), p);
                target.Select(p);
                return;
            }
            src.Unlink(p);
            target.AddPane(p, index, true);
            AfterPaneRemoved(src);
        }

        // Вызывается, когда из области убрали вкладку.
        public void AfterPaneRemoved(Area a)
        {
            if (a != null && !a.IsDisposed && a.Panes.Count == 0) host.CloseArea(a);
        }

        // ---------- раскладка ----------
        public void DoLayout()
        {
            if (root == null || Width <= 0 || Height <= 0) return;
            Rectangle full = new Rectangle(Pad, Pad, Math.Max(10, Width - 2 * Pad), Math.Max(10, Height - 2 * Pad));
            SuspendLayout();
            if (maximized != null)
            {
                foreach (Area a in areas) a.Visible = a == maximized;
                foreach (Control c in SplitBars()) c.Visible = false;
                maximized.Bounds = full;
            }
            else
            {
                foreach (Area a in areas) if (!a.Visible) a.Visible = true;
                foreach (Control c in SplitBars()) if (!c.Visible) c.Visible = true;
                LayoutNode(root, full);
            }
            ResumeLayout(false);
        }

        private void LayoutNode(Node n, Rectangle r)
        {
            AreaNode an = n as AreaNode;
            if (an != null) { an.Area.Bounds = r; return; }
            SplitNode s = (SplitNode)n;
            s.Rect = r;
            int total = s.Vertical ? r.Width : r.Height;
            int avail = Math.Max(2, total - Gap);
            double ratio = s.Ratio;
            if (avail > 2 * MinSize) ratio = Math.Max((double)MinSize / avail, Math.Min(1.0 - (double)MinSize / avail, ratio));
            int a = (int)Math.Round(avail * ratio);
            s.Ratio = ratio;
            Rectangle ra, bar, rb;
            if (s.Vertical)
            {
                ra = new Rectangle(r.X, r.Y, a, r.Height);
                bar = new Rectangle(r.X + a, r.Y, Gap, r.Height);
                rb = new Rectangle(r.X + a + Gap, r.Y, avail - a, r.Height);
            }
            else
            {
                ra = new Rectangle(r.X, r.Y, r.Width, a);
                bar = new Rectangle(r.X, r.Y + a, r.Width, Gap);
                rb = new Rectangle(r.X, r.Y + a + Gap, r.Width, avail - a);
            }
            s.Bar.Bounds = bar;
            LayoutNode(s.A, ra);
            LayoutNode(s.B, rb);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        public void DragBar(SplitNode n, Point screen)
        {
            Point p = PointToClient(screen);
            int start = n.Vertical ? n.Rect.X : n.Rect.Y;
            int total = n.Vertical ? n.Rect.Width : n.Rect.Height;
            int avail = Math.Max(2, total - Gap);
            int pos = (n.Vertical ? p.X : p.Y) - start - Gap / 2;
            double r = (double)pos / avail;
            n.Ratio = Math.Max(0.02, Math.Min(0.98, r));
            DoLayout();
        }

        // ---------- меню области ----------
        public void ShowAreaMenu(Area a, Point screen)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            AddMenu(m, Loc.T("Разделить по вертикали"), "Ctrl+\\", delegate { host.SplitArea(a, true, 0.5); });
            AddMenu(m, Loc.T("Разделить по горизонтали"), "Ctrl+Shift+\\", delegate { host.SplitArea(a, false, 0.5); });
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem mx = AddMenu(m, maximized == a ? Loc.T("Вернуть раскладку") : Loc.T("Развернуть область"), Loc.T("Ctrl+Пробел"), delegate { ToggleMaximize(a); });
            mx.Enabled = areas.Count > 1;
            ToolStripMenuItem cl = AddMenu(m, Loc.T("Закрыть область"), "Ctrl+Shift+W", delegate { host.CloseArea(a); });
            cl.Enabled = areas.Count > 1;
            m.Show(screen);
        }

        private static ToolStripMenuItem AddMenu(ContextMenuStrip m, string text, string keys, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.ShortcutKeyDisplayString = keys;
            mi.Click += click;
            m.Items.Add(mi);
            return mi;
        }

        // ---------- сохранение раскладки ----------
        private static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        private static string UnB64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            if (root != null) Ser(root, sb);
            return sb.ToString().Trim();
        }

        private void Ser(Node n, StringBuilder sb)
        {
            AreaNode an = n as AreaNode;
            if (an != null)
            {
                List<string> items = new List<string>();
                int sel = 0;
                foreach (Pane p in an.Area.Panes)
                {
                    string s = p.Serialize();
                    if (s == null) continue;
                    if (p == an.Area.Current) sel = items.Count;
                    items.Add(s);
                }
                sb.Append("A ").Append(items.Count).Append(' ');
                foreach (string s in items) sb.Append(B64(s)).Append(' ');
                sb.Append(sel).Append(' ');
                return;
            }
            SplitNode sn = (SplitNode)n;
            sb.Append("S ").Append(sn.Vertical ? "V" : "H").Append(' ')
              .Append(sn.Ratio.ToString("0.####", CultureInfo.InvariantCulture)).Append(' ');
            Ser(sn.A, sb);
            Ser(sn.B, sb);
        }

        // Строит раскладку из строки. Возвращает false при ошибке (тогда нужно построить заново).
        public bool Restore(string data, Func<string, Pane> factory)
        {
            try
            {
                Queue<string> q = new Queue<string>(data.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                if (q.Count == 0) return false;
                root = Build(q, factory, null);
                foreach (Area a in areas) if (a.Panes.Count == 0) return false;
                active = areas[0];
                DoLayout();
                return true;
            }
            catch { return false; }
        }

        private Node Build(Queue<string> q, Func<string, Pane> factory, SplitNode parent)
        {
            string t = q.Dequeue();
            if (t == "A")
            {
                int n = int.Parse(q.Dequeue(), CultureInfo.InvariantCulture);
                Area a = CreateArea();
                AreaNode an = new AreaNode(); an.Area = a; an.Parent = parent;
                List<Pane> ps = new List<Pane>();
                for (int i = 0; i < n; i++)
                {
                    Pane p = factory(UnB64(q.Dequeue()));
                    if (p != null) { a.AddPane(p, -1, false); ps.Add(p); }
                }
                int sel = int.Parse(q.Dequeue(), CultureInfo.InvariantCulture);
                if (ps.Count == 0)
                {
                    Pane home = factory("H");
                    a.AddPane(home, -1, true);
                }
                else a.Select(ps[Math.Max(0, Math.Min(sel, ps.Count - 1))]);
                return an;
            }
            if (t == "S")
            {
                SplitNode sn = new SplitNode();
                sn.Parent = parent;
                sn.Vertical = q.Dequeue() == "V";
                sn.Ratio = double.Parse(q.Dequeue(), CultureInfo.InvariantCulture);
                sn.Bar = new SplitBar(this, sn);
                Controls.Add(sn.Bar);
                sn.A = Build(q, factory, sn);
                sn.B = Build(q, factory, sn);
                return sn;
            }
            throw new FormatException("layout");
        }
    }
}
