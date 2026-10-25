// Этапы работы (как рабочие пространства в Blender): именованные раскладки областей.
// Этап хранит раскладку: какие области, какие вкладки в них, какие программы запущены.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SourceSDKHub
{
    internal class Stage
    {
        public string Name;
        public string Layout;     // текущая раскладка (запоминается автоматически)
        public string Default;    // исходная раскладка для «Сбросить» (у своих этапов нет)
    }

    internal static class StageStore
    {
        public static List<Stage> Load(string path)
        {
            List<Stage> res = new List<Stage>();
            if (!File.Exists(path)) return res;
            Stage cur = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.Equals("[Stage]", StringComparison.OrdinalIgnoreCase))
                {
                    if (cur != null && cur.Name != null && cur.Layout != null) res.Add(cur);
                    cur = new Stage();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (cur == null || eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                if (k == "name") cur.Name = v;
                else if (k == "layout") cur.Layout = v;
                else if (k == "default") cur.Default = v.Length == 0 ? null : v;
            }
            if (cur != null && cur.Name != null && cur.Layout != null) res.Add(cur);
            return res;
        }

        public static void Save(string path, List<Stage> stages)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Этапы работы Source Model Kit (раскладки областей)");
                foreach (Stage s in stages)
                {
                    sb.AppendLine("[Stage]");
                    sb.AppendLine("Name=" + s.Name);
                    sb.AppendLine("Layout=" + s.Layout);
                    sb.AppendLine("Default=" + (s.Default ?? ""));
                    sb.AppendLine();
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }

    // Сборка строки раскладки (формат тот же, что у Workspace.Serialize).
    internal static class LayoutBuilder
    {
        private static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }

        public static string Area(params string[] specs)
        {
            StringBuilder sb = new StringBuilder("A " + specs.Length + " ");
            foreach (string s in specs) sb.Append(B64(s)).Append(' ');
            sb.Append("0 ");
            return sb.ToString();
        }

        public static string Split(bool vertical, double ratio, string a, string b)
        {
            return "S " + (vertical ? "V" : "H") + " " + ratio.ToString("0.###", CultureInfo.InvariantCulture) + " " + a + b;
        }
    }

    // ------------------------------------------------------------------
    // Панель переключения этапов
    // ------------------------------------------------------------------
    internal class StageBar : Control
    {
        public List<Stage> Stages = new List<Stage>();
        public int Current;
        public event Action<int> StageSelected;
        public event Action<int> StageRename;
        public event Action<int, Point> StageContext;
        public event EventHandler AddClicked;

        private int hover = -2;     // -1 = кнопка «+»

        public StageBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 32;
            Cursor = Cursors.Hand;
            Tips.Set(this, Loc.T("Этапы работы (Ctrl+Alt+1…9). ПКМ — переименовать, сбросить, удалить"));
        }

        private static int TextW(string t)
        {
            return TextRenderer.MeasureText(t, Theme.Ui, new Size(1000, 30), TextFormatFlags.NoPadding).Width;
        }

        private int ItemW(int i) { return Math.Max(64, TextW(Loc.T(Stages[i].Name)) + 28); }

        public int PreferredWidth()
        {
            int w = 0;
            for (int i = 0; i < Stages.Count; i++) w += ItemW(i) + 2;
            return w + 32;
        }

        private int HitTest(Point p)
        {
            int x = 0;
            for (int i = 0; i < Stages.Count; i++)
            {
                int w = ItemW(i);
                if (p.X >= x && p.X < x + w) return i;
                x += w + 2;
            }
            if (p.X >= x && p.X < x + 28) return -1;
            return -2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = 0;
            for (int i = 0; i < Stages.Count; i++)
            {
                int w = ItemW(i);
                Rectangle r = new Rectangle(x, 3, w, Height - 6);
                bool sel = i == Current;
                if (sel || i == hover)
                    using (GraphicsPath p = Theme.RoundRect(r, 6))
                    using (SolidBrush b = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg2)) g.FillPath(b, p);
                Theme.DrawText(g, Loc.T(Stages[i].Name), sel ? Theme.UiBold : Theme.Ui, r, sel ? Theme.Text : Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (sel)
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X + 10, r.Bottom - 3, r.Width - 20, 2);
                x += w + 2;
            }
            Rectangle add = new Rectangle(x, 3, 28, Height - 6);
            if (hover == -1)
                using (GraphicsPath p = Theme.RoundRect(add, 6))
                using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillPath(b, p);
            Theme.DrawText(g, Theme.GAdd, Theme.IconSmall, add, hover == -1 ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -2; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h == -1 && e.Button == MouseButtons.Left) { if (AddClicked != null) AddClicked(this, EventArgs.Empty); }
            else if (h >= 0 && e.Button == MouseButtons.Left) { if (StageSelected != null) StageSelected(h); }
            else if (h >= 0 && e.Button == MouseButtons.Right) { if (StageContext != null) StageContext(h, Cursor.Position); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h >= 0 && e.Button == MouseButtons.Left && StageRename != null) StageRename(h);
            base.OnMouseDoubleClick(e);
        }
    }
}
