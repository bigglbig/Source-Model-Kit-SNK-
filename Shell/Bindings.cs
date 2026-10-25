// Окно «Привязки и зависимости» просмотрщика моделей:
// папки с материалами, папки с моделями для $includemodel и дополнительные файлы анимаций.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SourceSDKHub
{
    // Простой список строк с выделением
    internal class PathList : Control
    {
        private const int RowH = 26;
        public List<string> Items = new List<string>();
        public Func<string, Color> ColorOf;       // цвет строки (по умолчанию - обычный текст)
        public int Selected = -1;
        private int scroll;

        public PathList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        private int Max { get { return Math.Max(0, Items.Count * RowH - Height); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg0);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (Items.Count == 0)
                Theme.DrawText(g, Loc.T("— пусто —"), Theme.Ui, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            for (int i = 0; i < Items.Count; i++)
            {
                int y = i * RowH - scroll + 1;
                if (y + RowH < 0 || y > Height) continue;
                Rectangle r = new Rectangle(1, y, Width - 2 - (Max > 0 ? 8 : 0), RowH);
                if (i == Selected) using (SolidBrush b = new SolidBrush(Theme.AccentDim)) g.FillRectangle(b, r);
                Color c = ColorOf != null ? ColorOf(Items[i]) : Theme.Text;
                Theme.DrawText(g, Items[i], Theme.Ui, new Rectangle(r.X + 8, r.Y, r.Width - 12, r.Height), c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
            }
            if (Max > 0)
            {
                int th = Math.Max(20, Height * Height / (Items.Count * RowH));
                int ty = (int)((Height - th) * (scroll / (float)Max));
                using (SolidBrush b = new SolidBrush(Theme.Bg4)) g.FillRectangle(b, Width - 7, ty + 1, 5, th);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = (e.Y - 1 + scroll) / RowH;
            Selected = i >= 0 && i < Items.Count ? i : -1;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Max, scroll - Math.Sign(e.Delta) * RowH));
            Invalidate();
            base.OnMouseWheel(e);
        }

        public void RemoveSelected() { if (Selected >= 0 && Selected < Items.Count) { Items.RemoveAt(Selected); Selected = -1; Invalidate(); } }
    }

    internal class BindingsForm : DarkForm
    {
        private readonly PathList mats = new PathList(), models = new PathList(), anims = new PathList(), report = new PathList();
        public bool Changed;
        private readonly string modelName;

        // matPaths, modelPaths, animFiles - редактируются на месте; includes - отчёт (может быть null)
        public BindingsForm(string modelName, List<string> matPaths, List<string> modelPaths, List<string> animFiles, List<IncludeInfo> includes)
        {
            this.modelName = modelName;
            Text = Loc.T("Привязки и зависимости");
            ClientSize = new Size(680, 640);
            mats.Items = matPaths; models.Items = modelPaths; anims.Items = animFiles;

            int y = 14;
            y = Section(Loc.T("Папки с материалами (materials)"), Loc.T("Корень игры или проекта, где есть подпапка materials. Нужны, если часть материалов модели лежит не рядом с ней."), mats, y, 92,
                Loc.T("Добавить папку…"), delegate { AddFolder(mats, Loc.T("Папка, в которой лежит каталог materials")); });
            y = Section(Loc.T("Папки с моделями (для $includemodel)"), Loc.T("Корень, где есть подпапка models: оттуда подгружаются подключённые файлы анимаций (например, models/humans/…)."), models, y, 80,
                Loc.T("Добавить папку…"), delegate { AddFolder(models, Loc.T("Папка, в которой лежит каталог models")); });

            Label l = new Label();
            l.Text = Loc.T("Подключённые модели ($includemodel)"); l.Font = Theme.UiBold; l.AutoSize = true; l.ForeColor = Theme.Text; l.BackColor = Theme.Bg1; l.SetBounds(20, y, 400, 20);
            Controls.Add(l);
            report.SetBounds(20, y + 24, 640, 104);
            List<string> rep = new List<string>();
            if (includes != null)
                foreach (IncludeInfo ii in includes)
                    rep.Add((ii.Path != null ? "✓  " : "✗  ") + ii.Name + (ii.Path != null ? "     ← " + ii.Path + Loc.T("   (секвенций: ") + ii.Seqs + ")" : Loc.T("     — не найден, добавьте папку с моделями выше")));
            report.Items = rep;
            report.ColorOf = delegate(string s) { return s.StartsWith("✗") ? Theme.Bad : Theme.Text; };
            Controls.Add(report);
            y += 24 + 104 + 14;

            y = Section(Loc.T("Дополнительные анимации (.mdl) для этой модели"), Loc.T("Файлы с секвенциями подключаются по именам костей, как $includemodel. Привязка запоминается для ") + modelName + ".", anims, y, 92,
                Loc.T("Добавить .mdl…"), delegate { AddFiles(); });

            FlatButton ok = MakeButton(Loc.T("Готово"), true, DialogResult.OK);
            ok.SetBounds(560, ClientSize.Height - 48, 100, 32);
            Controls.Add(ok);
            AcceptButton = null;
        }

        private int Section(string title, string hint, PathList list, int y, int h, string addText, EventHandler add)
        {
            Label t = new Label();
            t.Text = title; t.Font = Theme.UiBold; t.AutoSize = true; t.ForeColor = Theme.Text; t.BackColor = Theme.Bg1; t.SetBounds(20, y, 600, 20);
            Label s = new Label();
            s.Text = hint; s.Font = Theme.UiSmall; s.AutoSize = false; s.ForeColor = Theme.TextDim; s.BackColor = Theme.Bg1; s.SetBounds(20, y + 20, 640, 32);
            list.SetBounds(20, y + 54, 520, h);
            FlatButton a = new FlatButton(Theme.GAdd, addText, null); a.BaseRole = 1; a.LeftAlign = true; a.SetBounds(550, y + 54, 110, 28);
            a.Width = 110; a.Caption = addText == Loc.T("Добавить папку…") ? Loc.T("Папка…") : ".mdl…";
            a.Click += add;
            FlatButton r = new FlatButton(Theme.GClose, Loc.T("Убрать"), null); r.BaseRole = 1; r.LeftAlign = true; r.SetBounds(550, y + 88, 110, 28);
            r.Click += delegate { list.RemoveSelected(); Changed = true; };
            Controls.AddRange(new Control[] { t, s, list, a, r });
            return y + 54 + h + 14;
        }

        private void AddFolder(PathList list, string desc)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = desc;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!list.Items.Contains(d.SelectedPath)) list.Items.Add(d.SelectedPath);
                Changed = true; list.Invalidate();
            }
        }

        private void AddFiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = Loc.T("Модели Source (*.mdl)|*.mdl");
                d.Multiselect = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (string f in d.FileNames) if (!anims.Items.Contains(f)) anims.Items.Add(f);
                Changed = true; anims.Invalidate();
            }
        }
    }
}
