// Встроенный проводник файлов: дерево папок + быстрый (виртуальный) список,
// копирование/вырезание/вставка, перетаскивание, переименование, корзина.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace SourceSDKHub
{
    internal class FsEntry
    {
        public string Name, Path;
        public bool IsDir, IsDrive;
        public long Size;
        public DateTime Modified;
        public string DriveType;
    }

    internal static class FileOps
    {
        public static string FormatSize(long b)
        {
            if (b < 1024) return b + Loc.T(" Б");
            string[] u = { Loc.T("КБ"), Loc.T("МБ"), Loc.T("ГБ"), Loc.T("ТБ") };
            double v = b / 1024.0; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v >= 100 ? "0" : "0.#") + " " + u[i];
        }

        public static string UniqueName(string dir, string name, bool isDir)
        {
            string stem = isDir ? name : System.IO.Path.GetFileNameWithoutExtension(name);
            string ext = isDir ? "" : System.IO.Path.GetExtension(name);
            string cand = name;
            int n = 1;
            while (File.Exists(System.IO.Path.Combine(dir, cand)) || Directory.Exists(System.IO.Path.Combine(dir, cand)))
            {
                n++;
                cand = stem + (n == 2 ? Loc.T(" - копия") : Loc.T(" - копия (") + (n - 1) + ")") + ext;
            }
            return cand;
        }

        public static bool IsInside(string child, string parent)
        {
            string c = child.TrimEnd('\\') + "\\", p = parent.TrimEnd('\\') + "\\";
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        // Копирование / перемещение со стандартными диалогами Windows (прогресс, конфликты имён).
        public static string Transfer(IEnumerable<string> sources, string destDir, bool move, out int done)
        {
            string error = null;
            done = 0;
            foreach (string s0 in sources)
            {
                try
                {
                    string s = s0.TrimEnd('\\');
                    bool isDir = Directory.Exists(s);
                    if (!isDir && !File.Exists(s)) continue;
                    string name = System.IO.Path.GetFileName(s);
                    string dst = System.IO.Path.Combine(destDir, name);
                    if (isDir && IsInside(destDir, s)) { error = Loc.T("Нельзя поместить папку внутрь самой себя."); continue; }
                    if (string.Equals(dst, s, StringComparison.OrdinalIgnoreCase))
                    {
                        if (move) continue;
                        dst = System.IO.Path.Combine(destDir, UniqueName(destDir, name, isDir));
                    }
                    if (isDir)
                    {
                        if (move) FileSystem.MoveDirectory(s, dst, UIOption.AllDialogs);
                        else FileSystem.CopyDirectory(s, dst, UIOption.AllDialogs);
                    }
                    else
                    {
                        if (move) FileSystem.MoveFile(s, dst, UIOption.AllDialogs);
                        else FileSystem.CopyFile(s, dst, UIOption.AllDialogs);
                    }
                    done++;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { error = ex.Message; }
            }
            return error;
        }
    }

    // Панель инструментов с нижней границей
    internal class BarPanel : Control
    {
        public bool TopLine;
        public int Role = 2;
        public bool CaptionArea;      // свободные места панели отдаются окну как заголовок (перетаскивание)

        protected override void WndProc(ref Message m)
        {
            if (CaptionArea && m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; }   // HTTRANSPARENT
            base.WndProc(ref m);
        }
        public BarPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Role(Role));
            using (Pen p = new Pen(Theme.Border))
            {
                int y = TopLine ? 0 : Height - 1;
                e.Graphics.DrawLine(p, 0, y, Width, y);
            }
            base.OnPaint(e);
        }
    }

    internal class ExplorerPane : Pane
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        private static readonly object Dummy = new object();
        private static readonly string[] ColNames = { Loc.T("Имя"), Loc.T("Изменён"), Loc.T("Тип"), Loc.T("Размер") };

        private readonly IShellHost host;
        private readonly BarPanel bar = new BarPanel();
        private readonly FlatButton btnBack, btnFwd, btnUp, btnRefresh, btnHidden, btnNew, btnPin;
        private readonly DarkInput address = new DarkInput();
        private readonly DarkInput filter = new DarkInput();
        private readonly SplitContainer split = new SplitContainer();
        private readonly SplitContainer leftSplit = new SplitContainer();
        private readonly PinList pinList;
        private readonly FastTreeView tree = new FastTreeView();
        private readonly FastListView list = new FastListView();
        private readonly StatusLine status = new StatusLine();
        private readonly ImageList icons = new ImageList();
        private readonly ImageList spacer = new ImageList();   // задаёт высоту строк списка
        private int hotRow = -1;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly System.Windows.Forms.Timer reloadTimer = new System.Windows.Forms.Timer();
        private FileSystemWatcher watcher;

        private readonly List<string> history = new List<string>();
        private int hi = -1;
        private string path = "";
        private string initialPath;
        private List<FsEntry> all = new List<FsEntry>();
        private List<FsEntry> shown = new List<FsEntry>();
        private int sortCol;
        private bool sortAsc = true;
        private bool showHidden;
        private int loadGen;
        private bool syncing;
        private string pendingEdit;
        private TreeNode pcNode;
        private FsEntry renaming;
        private string[] dragPaths;

        private int folderIcon, driveIcon, fileIcon;
        private readonly Dictionary<string, int> iconByExt = new Dictionary<string, int>();
        private readonly Dictionary<string, string> typeByExt = new Dictionary<string, string>();

        private bool loaded;
        private bool splitInit;

        // Пока вкладка не открывалась (скрыта), путь берётся из сохранённой раскладки.
        public string CurrentPath { get { return loaded ? path : (initialPath ?? ""); } }

        public override string Title
        {
            get
            {
                string cp = CurrentPath;
                if (cp.Length == 0) return loaded ? Loc.T("Этот компьютер") : Loc.T("Проводник");
                string n = System.IO.Path.GetFileName(cp.TrimEnd('\\'));
                return n.Length > 0 ? n : cp;
            }
        }
        public override string IconGlyph { get { return Theme.GFolderOpen; } }
        public override string Serialize() { return "E|" + CurrentPath; }

        public ExplorerPane(IShellHost h, string startPath)
        {
            host = h;
            initialPath = startPath;
            showHidden = host.Config.GetBool("showHidden", false);

            // ---- значки ----
            icons.ImageSize = new Size(16, 16);
            icons.ColorDepth = ColorDepth.Depth32Bit;
            folderIcon = LoadIcon("folder", true);
            driveIcon = LoadRealIcon(Environment.SystemDirectory.Substring(0, 3));
            if (driveIcon < 0) driveIcon = folderIcon;
            fileIcon = LoadIcon(".file", false);

            // ---- панель инструментов ----
            bar.Height = 40;
            btnBack = Tool(Theme.GBack, Loc.T("Назад (Alt+←)"));
            btnFwd = Tool(Theme.GForward, Loc.T("Вперёд (Alt+→)"));
            btnUp = Tool(Theme.GUp, Loc.T("Вверх (Alt+↑ / Backspace)"));
            btnRefresh = Tool(Theme.GRefresh, Loc.T("Обновить (F5)"));
            btnHidden = Tool(Theme.GView, Loc.T("Показывать скрытые файлы"));
            btnHidden.Toggle = true; btnHidden.Toggled = showHidden;
            btnNew = Tool(Theme.GFolder, Loc.T("Новая папка (Ctrl+Shift+N)"));
            btnPin = Tool(Theme.GPin, Loc.T("Закреплённые папки (быстрый доступ)"));
            btnPin.Click += delegate { ShowPinsMenu(); };
            btnBack.Click += delegate { Back(); };
            btnFwd.Click += delegate { Forward(); };
            btnUp.Click += delegate { Up(); };
            btnRefresh.Click += delegate { Reload(); };
            btnNew.Click += delegate { NewFolder(); };
            btnHidden.Click += delegate
            {
                showHidden = btnHidden.Toggled;
                host.Config.SetBool("showHidden", showHidden);
                Reload();
            };

            address.Glyph = Theme.GFolderOpen;
            address.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoTo(address.Text, 1, null, -1); list.Focus(); }
                else if (e.KeyCode == Keys.Escape) { address.Text = path; list.Focus(); e.SuppressKeyPress = true; }
            };
            address.Box.Enter += delegate { address.Box.SelectAll(); };

            filter.Glyph = Theme.GSearch;
            filter.Hint = Loc.T("Фильтр (Ctrl+F)");
            filter.Width = 180;
            filter.Box.TextChanged += delegate { Rebuild(null, null); };
            filter.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { filter.Text = ""; list.Focus(); e.SuppressKeyPress = true; }
            };

            bar.Controls.AddRange(new Control[] { btnBack, btnFwd, btnUp, btnRefresh, address, filter, btnPin, btnHidden, btnNew });
            bar.Resize += delegate { LayoutBar(); };

            // ---- дерево ----
            tree.ImageList = icons;
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += delegate(object s, DrawTreeNodeEventArgs ev)
            {
                if (ev.Bounds.Width <= 0) { ev.DrawDefault = true; return; }
                bool sel = (ev.State & TreeNodeStates.Selected) != 0;
                Color bg = sel ? (tree.Focused ? Theme.AccentDim : Theme.Bg4) : Theme.Bg1;
                Rectangle r = new Rectangle(ev.Bounds.X - 1, ev.Bounds.Y, ev.Bounds.Width + 6, ev.Bounds.Height);
                using (SolidBrush b = new SolidBrush(bg)) ev.Graphics.FillRectangle(b, r);
                Theme.DrawText(ev.Graphics, ev.Node.Text, Theme.Ui, new Rectangle(r.X + 2, r.Y, r.Width - 2, r.Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                ev.DrawDefault = false;
            };
            tree.GotFocus += delegate { tree.Invalidate(); };
            tree.LostFocus += delegate { tree.Invalidate(); };
            tree.BeforeExpand += delegate(object s, TreeViewCancelEventArgs e) { LoadChildren(e.Node); };
            tree.AfterSelect += delegate(object s, TreeViewEventArgs e)
            {
                if (syncing || e.Node == null) return;
                string p = e.Node.Tag as string;
                if (p != null) GoTo(p, 1, null, -1);
            };
            tree.NodeMouseClick += delegate(object s, TreeNodeMouseClickEventArgs ev)
            {
                string np = ev.Node.Tag as string;
                if (ev.Button != MouseButtons.Right || string.IsNullOrEmpty(np)) return;
                ContextMenuStrip m = new ContextMenuStrip();
                Theme.ApplyMenu(m);
                AddMenuItem(m, Loc.T("Открыть в новой вкладке"), delegate { host.NewExplorerTab(np, Area); });
                AddMenuItem(m, IsPinned(np) ? Loc.T("Открепить из быстрого доступа") : Loc.T("Закрепить в быстром доступе"), delegate { host.TogglePin(np); });
                m.Show(tree, ev.Location);
            };
            tree.AllowDrop = true;
            tree.DragOver += delegate(object s, DragEventArgs e)
            {
                TreeNode n = tree.GetNodeAt(tree.PointToClient(new Point(e.X, e.Y)));
                string dest = n == null ? null : n.Tag as string;
                e.Effect = EffectFor(e, string.IsNullOrEmpty(dest) ? null : dest);
            };
            tree.DragDrop += delegate(object s, DragEventArgs e)
            {
                TreeNode n = tree.GetNodeAt(tree.PointToClient(new Point(e.X, e.Y)));
                string dest = n == null ? null : n.Tag as string;
                DropInto(e, dest);
            };

            // ---- список ----
            list.VirtualMode = true;
            list.LabelEdit = true;
            spacer.ImageSize = new Size(20, 24);
            spacer.ColorDepth = ColorDepth.Depth32Bit;
            spacer.Images.Add(new Bitmap(20, 24));
            list.SmallImageList = spacer;
            list.OwnerDraw = true;
            list.DrawColumnHeader += DrawHeader;
            list.DrawItem += delegate(object s, DrawListViewItemEventArgs ev) { };
            list.DrawSubItem += DrawCell;
            list.GotFocus += delegate { list.Invalidate(); };
            list.LostFocus += delegate { list.Invalidate(); };
            list.MouseMove += delegate(object s, MouseEventArgs ev)
            {
                ListViewHitTestInfo ht = list.HitTest(ev.Location);
                int n = ht.Item == null ? -1 : ht.Item.Index;
                if (n != hotRow)
                {
                    int old = hotRow; hotRow = n;
                    try
                    {
                        if (old >= 0 && old < shown.Count) list.RedrawItems(old, old, false);
                        if (n >= 0 && n < shown.Count) list.RedrawItems(n, n, false);
                    }
                    catch { }
                }
            };
            list.MouseLeave += delegate
            {
                int old = hotRow; hotRow = -1;
                try { if (old >= 0 && old < shown.Count) list.RedrawItems(old, old, false); } catch { }
            };
            list.Columns.Add(ColNames[0], 300);
            list.Columns.Add(ColNames[1], 130);
            list.Columns.Add(ColNames[2], 140);
            list.Columns.Add(ColNames[3], 90, HorizontalAlignment.Right);
            list.RetrieveVirtualItem += delegate(object s, RetrieveVirtualItemEventArgs e)
            {
                e.Item = MakeItem(shown[e.ItemIndex]);
            };
            list.SearchForVirtualItem += delegate(object s, SearchForVirtualItemEventArgs e)
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    int idx = (e.StartIndex + i) % shown.Count;
                    if (shown[idx].Name.StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase)) { e.Index = idx; return; }
                }
            };
            list.ColumnClick += delegate(object s, ColumnClickEventArgs e)
            {
                if (sortCol == e.Column) sortAsc = !sortAsc; else { sortCol = e.Column; sortAsc = true; }
                Rebuild(SelectedNames(), null);
            };
            list.ItemActivate += delegate
            {
                List<FsEntry> sel = Selected();
                if (sel.Count > 0) OpenEntry(sel[0]);
            };
            list.SelectedIndexChanged += delegate { UpdateStatus(); };
            list.Resize += delegate { FitColumns(); };
            list.KeyDown += ListKeyDown;
            list.MouseUp += ListMouseUp;
            list.AfterLabelEdit += AfterRename;
            list.ItemDrag += delegate
            {
                List<FsEntry> sel = Selected();
                if (sel.Count == 0 || sel[0].IsDrive) return;
                StringCollection sc = new StringCollection();
                List<string> arr = new List<string>();
                foreach (FsEntry en in sel) { sc.Add(en.Path); arr.Add(en.Path); }
                DataObject d = new DataObject();
                d.SetFileDropList(sc);
                dragPaths = arr.ToArray();
                try { list.DoDragDrop(d, DragDropEffects.Copy | DragDropEffects.Move); }
                finally { dragPaths = null; }
            };
            list.AllowDrop = true;
            list.DragOver += delegate(object s, DragEventArgs e) { e.Effect = EffectFor(e, DropTargetAt(e)); };
            list.DragDrop += delegate(object s, DragEventArgs e) { DropInto(e, DropTargetAt(e)); };

            menu.Opening += BuildMenu;
            Theme.ApplyMenu(menu);
            list.ContextMenuStrip = menu;

            // ---- раскладка ----
            split.Dock = DockStyle.Fill;
            split.BackColor = Theme.Border;
            split.SplitterWidth = 1;
            split.FixedPanel = FixedPanel.Panel1;
            split.Panel1.BackColor = Theme.Bg1;
            split.Panel2.BackColor = Theme.Bg1;
            tree.Dock = DockStyle.Fill;
            list.Dock = DockStyle.Fill;
            pinList = new PinList(host);
            pinList.Dock = DockStyle.Fill;
            pinList.OpenPin += delegate(string pp) { GoTo(pp, 1, null, -1); };
            pinList.OpenPinNewTab += delegate(string pp) { host.NewExplorerTab(pp, Area); };
            pinList.Unpin += delegate(string pp) { host.TogglePin(pp); };
            pinList.DropFolders += delegate(string[] dirs)
            {
                foreach (string d in dirs) if (!IsPinned(d)) host.TogglePin(d);
            };
            leftSplit.Dock = DockStyle.Fill;
            leftSplit.Orientation = Orientation.Horizontal;
            leftSplit.BackColor = Theme.Border;
            leftSplit.SplitterWidth = 1;
            leftSplit.FixedPanel = FixedPanel.Panel2;
            leftSplit.Panel1.BackColor = Theme.Bg1;
            leftSplit.Panel2.BackColor = Theme.Bg1;
            leftSplit.Panel1MinSize = 60;
            leftSplit.Panel2MinSize = 60;
            leftSplit.Panel1.Controls.Add(tree);
            leftSplit.Panel2.Controls.Add(pinList);
            split.Panel1.Controls.Add(leftSplit);
            split.Panel2.Controls.Add(list);

            status.Dock = DockStyle.Bottom;
            status.Height = 24;
            status.FillRole = 2;

            bar.Dock = DockStyle.Top;
            Controls.Add(split);
            Controls.Add(status);
            Controls.Add(bar);

            reloadTimer.Interval = 350;
            reloadTimer.Tick += delegate { reloadTimer.Stop(); if (path.Length > 0) Reload(); };

            host.PinsChanged += OnPinsChanged;
            BuildTree();
            UpdateNav();
        }

        private FlatButton Tool(string glyph, string tip)
        {
            FlatButton b = new FlatButton(glyph, null, tip);
            b.Width = 30; b.Height = 28;
            return b;
        }

        private void DrawHeader(object s, DrawListViewColumnHeaderEventArgs ev)
        {
            Graphics g = ev.Graphics;
            Rectangle r = ev.Bounds;
            using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, r);
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
                g.DrawLine(p, r.Right - 1, r.Y + 5, r.Right - 1, r.Bottom - 6);
            }
            if (r.Width < 12) return;
            Theme.DrawText(g, ev.Header.Text, Theme.Ui, new Rectangle(r.X + 8, r.Y, r.Width - 16, r.Height),
                ev.ColumnIndex == sortCol ? Theme.Text : Theme.TextDim,
                (ev.ColumnIndex == 3 ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter);
        }

        private void DrawCell(object s, DrawListViewSubItemEventArgs ev)
        {
            int i = ev.ItemIndex;
            if (i < 0 || i >= shown.Count) return;
            Graphics g = ev.Graphics;
            Rectangle r = ev.Bounds;
            if (ev.ColumnIndex == 0) r.Width = list.Columns[0].Width;
            if (r.Width <= 0) return;
            // LVM_GETITEMSTATE: в виртуальном списке ItemState ненадёжен
            bool sel = ((long)Native.SendMessage(list.Handle, 0x102C, (IntPtr)i, (IntPtr)2) & 2) != 0;
            Color bg = sel ? (list.Focused ? Theme.AccentDim : Theme.Bg4) : (i == hotRow ? Theme.Bg2 : Theme.Bg1);
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, r);
            if (ev.ColumnIndex == 0)
            {
                int ic = IconFor(shown[i]);
                if (ic >= 0) icons.Draw(g, r.X + 4, r.Y + (r.Height - 16) / 2, ic);
                Theme.DrawText(g, ev.SubItem.Text, Theme.Ui, new Rectangle(r.X + 26, r.Y, Math.Max(0, r.Width - 30), r.Height),
                    Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            else
            {
                Theme.DrawText(g, ev.SubItem.Text, Theme.Ui, new Rectangle(r.X + 8, r.Y, Math.Max(0, r.Width - 16), r.Height),
                    sel ? Theme.Text : Theme.TextDim,
                    (ev.ColumnIndex == 3 ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter);
            }
        }

        // ---------------- закреплённые папки ----------------
        private static string PinKey(string p) { return p.Length > 3 ? p.TrimEnd('\\') : p; }

        private bool IsPinned(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            foreach (string x in host.Pins)
                if (string.Equals(PinKey(x), PinKey(p), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string PinTitle(string p)
        {
            string n = System.IO.Path.GetFileName(p.TrimEnd('\\'));
            return n.Length > 0 ? n : p;
        }

        private void OnPinsChanged(object s, EventArgs e)
        {
            pinList.Invalidate();
            UpdateNav();
        }

        private static ToolStripMenuItem AddMenuItem(ContextMenuStrip m, string text, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.Click += click;
            m.Items.Add(mi);
            return mi;
        }

        private void ShowPinsMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            Theme.ApplyMenu(m);
            foreach (string pin in host.Pins)
            {
                string pp = pin;
                ToolStripMenuItem mi = AddMenuItem(m, PinTitle(pin) + "    " + pin, delegate { GoTo(pp, 1, null, -1); });
                mi.Enabled = Directory.Exists(pin);
            }
            if (host.Pins.Count == 0) { ToolStripMenuItem none = AddMenuItem(m, Loc.T("Нет закреплённых папок"), null); none.Enabled = false; }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem t = AddMenuItem(m, IsPinned(path) ? Loc.T("Открепить эту папку") : Loc.T("Закрепить эту папку"),
                delegate { host.TogglePin(path); });
            t.Enabled = path.Length > 0;
            m.Show(btnPin, new Point(0, btnPin.Height + 2));
        }

        // Имя занимает всё свободное место; второстепенные колонки скрываются в узкой области.
        private void FitColumns()
        {
            if (list.Columns.Count < 4) return;
            int avail = list.ClientSize.Width;
            if (avail <= 0) return;
            int size = 84, date = avail >= 340 ? 124 : 0, type = avail >= 520 ? 130 : 0;
            if (avail < 220) size = 0;
            int name = Math.Max(120, avail - size - date - type - 2);
            list.BeginUpdate();
            list.Columns[0].Width = name;
            list.Columns[1].Width = date;
            list.Columns[2].Width = type;
            list.Columns[3].Width = size;
            list.EndUpdate();
        }

        private void LayoutBar()
        {
            int y = (bar.Height - 28) / 2;
            int x = 8;
            foreach (FlatButton b in new FlatButton[] { btnBack, btnFwd, btnUp, btnRefresh })
            { b.SetBounds(x, y, 30, 28); x += 32; }
            int right = bar.Width - 8;
            btnNew.SetBounds(right - 30, y, 30, 28); right -= 32;
            btnHidden.SetBounds(right - 30, y, 30, 28); right -= 32;
            btnPin.SetBounds(right - 30, y, 30, 28); right -= 34;
            int fw = bar.Width > 640 ? 180 : (bar.Width > 460 ? 120 : 0);
            filter.Visible = fw > 0;
            if (fw > 0) { filter.SetBounds(right - fw, y, fw, 28); right -= fw + 6; }
            address.SetBounds(x + 4, y, Math.Max(60, right - x - 4), 28);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            bool narrow = Width < 300;
            if (split.Panel1Collapsed != narrow) split.Panel1Collapsed = narrow;
            if (!splitInit && Width > 200 && Height > 160)
            {
                // ширина дерева и высота зоны закреплённых выставляются при первой настоящей раскладке
                splitInit = true;
                try
                {
                    if (!narrow) { split.Panel1MinSize = 110; split.SplitterDistance = Math.Min(220, Math.Max(140, Width / 3)); }
                    leftSplit.SplitterDistance = Math.Max(60, leftSplit.Height - Math.Max(110, leftSplit.Height * 38 / 100));
                }
                catch { }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutBar();
            FitColumns();
            string p = initialPath;
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) p = host.BaseDir;
            GoTo(p, 1, null, -1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reloadTimer.Stop();
                host.PinsChanged -= OnPinsChanged;
                if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
                icons.Dispose();
                spacer.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Activated() { if (list.IsHandleCreated) list.Focus(); }

        // ---------------- значки ----------------
        private int LoadIcon(string key, bool dir)
        {
            Native.SHFILEINFO fi = new Native.SHFILEINFO();
            IntPtr r = Native.SHGetFileInfo(key, dir ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL, ref fi,
                (uint)Marshal.SizeOf(fi), Native.SHGFI_ICON | Native.SHGFI_SMALLICON | Native.SHGFI_USEFILEATTRIBUTES);
            return AddIcon(fi);
        }

        private int LoadRealIcon(string path)
        {
            Native.SHFILEINFO fi = new Native.SHFILEINFO();
            Native.SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(fi), Native.SHGFI_ICON | Native.SHGFI_SMALLICON);
            return AddIcon(fi);
        }

        private int AddIcon(Native.SHFILEINFO fi)
        {
            if (fi.hIcon == IntPtr.Zero) return -1;
            try
            {
                using (Icon ic = Icon.FromHandle(fi.hIcon)) icons.Images.Add(ic.ToBitmap());
                return icons.Images.Count - 1;
            }
            finally { Native.DestroyIcon(fi.hIcon); }
        }

        private int IconFor(FsEntry e)
        {
            if (e.IsDrive) return driveIcon;
            if (e.IsDir) return folderIcon;
            string ext = System.IO.Path.GetExtension(e.Name).ToLowerInvariant();
            if (ext.Length == 0) return fileIcon;
            int idx;
            if (!iconByExt.TryGetValue(ext, out idx))
            {
                idx = LoadIcon(ext, false);
                if (idx < 0) idx = fileIcon;
                iconByExt[ext] = idx;
            }
            return idx;
        }

        private string TypeOf(FsEntry e)
        {
            if (e.IsDrive) return e.DriveType;
            if (e.IsDir) return Loc.T("Папка с файлами");
            string ext = System.IO.Path.GetExtension(e.Name).ToLowerInvariant();
            if (ext.Length == 0) return Loc.T("Файл");
            string t;
            if (!typeByExt.TryGetValue(ext, out t))
            {
                Native.SHFILEINFO fi = new Native.SHFILEINFO();
                Native.SHGetFileInfo(ext, Native.FILE_ATTRIBUTE_NORMAL, ref fi, (uint)Marshal.SizeOf(fi),
                    Native.SHGFI_TYPENAME | Native.SHGFI_USEFILEATTRIBUTES);
                t = string.IsNullOrEmpty(fi.szTypeName) ? Loc.T("Файл ") + ext.Substring(1).ToUpperInvariant() : fi.szTypeName;
                typeByExt[ext] = t;
            }
            return t;
        }

        private ListViewItem MakeItem(FsEntry e)
        {
            ListViewItem it = new ListViewItem(e.Name, 0);
            it.SubItems.Add(e.IsDrive ? "" : e.Modified.ToString("dd.MM.yyyy HH:mm"));
            it.SubItems.Add(TypeOf(e));
            it.SubItems.Add(e.IsDir && !e.IsDrive ? "" : FileOps.FormatSize(e.Size));
            return it;
        }

        // ---------------- дерево ----------------
        private void BuildTree()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            AddQuick(Loc.T("Рабочий стол"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddQuick(Loc.T("Документы"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddQuick(Loc.T("Загрузки"), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            AddQuick(Loc.T("Папка SDK"), host.BaseDir);

            pcNode = new TreeNode(Loc.T("Этот компьютер"));
            pcNode.Tag = "";
            pcNode.ImageIndex = pcNode.SelectedImageIndex = driveIcon;
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    TreeNode n = new TreeNode(d.Name.TrimEnd('\\'));
                    n.Tag = d.Name;
                    n.ImageIndex = n.SelectedImageIndex = driveIcon;
                    n.Nodes.Add(new TreeNode("…") { Tag = Dummy });
                    pcNode.Nodes.Add(n);
                }
            }
            catch { }
            tree.Nodes.Add(pcNode);
            pcNode.Expand();
            tree.EndUpdate();
            pcNode.Expand();
        }

        private void AddQuick(string title, string p) { AddQuick(title, p, false); }

        private void AddQuick(string title, string p, bool pinned)
        {
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) return;
            TreeNode n = new TreeNode(title);
            if (pinned) n.Name = "pin";
            n.Tag = p;
            n.ImageIndex = n.SelectedImageIndex = folderIcon;
            n.Nodes.Add(new TreeNode("…") { Tag = Dummy });
            tree.Nodes.Add(n);
        }

        private void LoadChildren(TreeNode n)
        {
            if (n.Nodes.Count != 1 || n.Nodes[0].Tag != Dummy) return;
            n.Nodes.Clear();
            string p = n.Tag as string;
            if (string.IsNullOrEmpty(p)) return;
            List<DirectoryInfo> dirs = new List<DirectoryInfo>();
            try
            {
                foreach (DirectoryInfo d in new DirectoryInfo(p).EnumerateDirectories())
                {
                    if (!showHidden && (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    dirs.Add(d);
                }
            }
            catch { }
            dirs.Sort(delegate(DirectoryInfo a, DirectoryInfo b) { return StrCmpLogicalW(a.Name, b.Name); });
            foreach (DirectoryInfo d in dirs)
            {
                TreeNode c = new TreeNode(d.Name);
                c.Tag = d.FullName;
                c.ImageIndex = c.SelectedImageIndex = folderIcon;
                c.Nodes.Add(new TreeNode("…") { Tag = Dummy });
                n.Nodes.Add(c);
            }
        }

        private void SyncTree(string p)
        {
            syncing = true;
            try
            {
                if (p.Length == 0) { pcNode.Expand(); tree.SelectedNode = pcNode; return; }
                string root = System.IO.Path.GetPathRoot(p);
                TreeNode cur = null;
                foreach (TreeNode n in pcNode.Nodes)
                    if (string.Equals((string)n.Tag, root, StringComparison.OrdinalIgnoreCase)) { cur = n; break; }
                if (cur == null) return;
                string rest = p.Substring(root.Length).Trim('\\');
                if (rest.Length > 0)
                {
                    foreach (string seg in rest.Split('\\'))
                    {
                        LoadChildren(cur);
                        TreeNode next = null;
                        foreach (TreeNode c in cur.Nodes)
                            if (string.Equals(c.Text, seg, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
                        if (next == null) break;
                        cur.Expand();
                        cur = next;
                    }
                }
                tree.SelectedNode = cur;
                cur.EnsureVisible();
            }
            catch { }
            finally { syncing = false; }
        }

        // ---------------- навигация ----------------
        public void Navigate(string p) { GoTo(p, 1, null, -1); }

        private void GoTo(string p, int kind, string select, int targetHi)
        {
            if (p == null) return;
            p = p.Trim().Trim('"');
            if (p.Length > 0)
            {
                if (p.Length == 2 && p[1] == ':') p += "\\";
                try { p = System.IO.Path.GetFullPath(p); }
                catch { Bad(Loc.T("Неверный путь")); return; }
                if (File.Exists(p)) { OpenPath(p); address.Text = path; return; }
                if (!Directory.Exists(p)) { Bad(Loc.T("Папка не найдена: ") + p); return; }
            }
            Load(p, kind, select, targetHi);
        }

        private void Bad(string msg)
        {
            status.LeftText = msg;
            host.SetStatus(msg);
            address.Text = path;
        }

        private void Back()
        {
            if (hi > 0) GoTo(history[hi - 1], 2, null, hi - 1);
        }

        private void Forward()
        {
            if (hi >= 0 && hi < history.Count - 1) GoTo(history[hi + 1], 2, null, hi + 1);
        }

        private void Up()
        {
            if (path.Length == 0) return;
            string parent = "";
            string trimmed = path.TrimEnd('\\');
            bool isRoot = trimmed.Length <= 2;
            if (!isRoot)
            {
                try { DirectoryInfo d = Directory.GetParent(trimmed); parent = d == null ? "" : d.FullName; }
                catch { parent = ""; }
            }
            string name = isRoot ? null : System.IO.Path.GetFileName(trimmed);
            GoTo(parent, 1, name, -1);
        }

        public void Reload() { Load(path, 0, null, -1); }

        private void UpdateNav()
        {
            btnBack.Enabled = hi > 0;
            btnFwd.Enabled = hi >= 0 && hi < history.Count - 1;
            btnUp.Enabled = path.Length > 0;
            btnNew.Enabled = path.Length > 0;
            pinList.Current = path;
            pinList.Invalidate();
            btnPin.Glyph = IsPinned(path) ? Theme.GPinned : Theme.GPin;
            btnPin.Toggled = IsPinned(path);
            btnPin.Invalidate();
        }

        private static List<FsEntry> Enumerate(string p, bool hidden)
        {
            List<FsEntry> res = new List<FsEntry>();
            if (p.Length == 0)
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    FsEntry e = new FsEntry();
                    e.IsDir = true; e.IsDrive = true; e.Path = d.Name;
                    string label = "";
                    try { if (d.IsReady) { label = d.VolumeLabel; e.Size = d.TotalSize; } } catch { }
                    if (label.Length == 0)
                        label = d.DriveType == System.IO.DriveType.Removable ? Loc.T("Съёмный диск") :
                                d.DriveType == System.IO.DriveType.CDRom ? Loc.T("CD-дисковод") :
                                d.DriveType == System.IO.DriveType.Network ? Loc.T("Сетевой диск") : Loc.T("Локальный диск");
                    e.Name = label + " (" + d.Name.TrimEnd('\\') + ")";
                    e.DriveType = d.DriveType == System.IO.DriveType.Fixed ? Loc.T("Локальный диск") : d.DriveType.ToString();
                    res.Add(e);
                }
                return res;
            }
            DirectoryInfo di = new DirectoryInfo(p);
            foreach (FileSystemInfo fi in di.EnumerateFileSystemInfos())
            {
                FileAttributes a = fi.Attributes;
                if (!hidden && (a & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                FsEntry e = new FsEntry();
                e.Name = fi.Name; e.Path = fi.FullName;
                e.IsDir = (a & FileAttributes.Directory) != 0;
                if (!e.IsDir) { FileInfo f = fi as FileInfo; if (f != null) e.Size = f.Length; }
                try { e.Modified = fi.LastWriteTime; } catch { }
                res.Add(e);
            }
            return res;
        }

        private void Load(string p, int kind, string select, int targetHi)
        {
            int gen = ++loadGen;
            bool hidden = showHidden;
            status.LeftText = Loc.T("Загрузка…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<FsEntry> res = null; string err = null;
                try { res = Enumerate(p, hidden); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (gen == loadGen) Apply(p, res, err, kind, select, targetHi);
                    }));
                }
                catch { }
            });
        }

        private void Apply(string p, List<FsEntry> res, string err, int kind, string select, int targetHi)
        {
            if (err != null)
            {
                status.LeftText = Loc.T("Нет доступа: ") + err;
                host.SetStatus(Loc.T("Нет доступа: ") + err);
                address.Text = path;
                return;
            }
            List<string> keep = kind == 0 ? SelectedNames() : null;
            loaded = true;
            bool newDir = !string.Equals(p, path, StringComparison.OrdinalIgnoreCase);
            path = p;
            all = res;
            if (kind == 1)
            {
                if (hi < 0 || !string.Equals(history[hi], p, StringComparison.OrdinalIgnoreCase))
                {
                    if (hi < history.Count - 1) history.RemoveRange(hi + 1, history.Count - hi - 1);
                    history.Add(p);
                    hi = history.Count - 1;
                }
            }
            else if (kind == 2) hi = targetHi;

            address.Text = p;
            if (newDir && filter.Text.Length > 0) filter.Text = "";
            Rebuild(keep, select);
            if (newDir && list.VirtualListSize > 0 && select == null) { try { list.EnsureVisible(0); } catch { } }
            Watch(p);
            if (newDir) SyncTree(p);
            UpdateNav();
            RaiseTitleChanged();

            if (pendingEdit != null)
            {
                string pe = pendingEdit; pendingEdit = null;
                int idx = IndexOfName(pe);
                if (idx >= 0)
                {
                    list.Focus();
                    renaming = shown[idx];
                    try { list.Items[idx].BeginEdit(); } catch { }
                }
            }
        }

        private int IndexOfName(string name)
        {
            for (int i = 0; i < shown.Count; i++)
                if (string.Equals(shown[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void Rebuild(List<string> keep, string select)
        {
            string f = filter.Text.Trim();
            if (f.Length == 0) shown = new List<FsEntry>(all);
            else
            {
                shown = new List<FsEntry>();
                foreach (FsEntry e in all)
                    if (e.Name.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0) shown.Add(e);
            }
            shown.Sort(Compare);

            for (int i = 0; i < ColNames.Length; i++)
                list.Columns[i].Text = ColNames[i] + (i == sortCol ? (sortAsc ? "  ▲" : "  ▼") : "");

            list.BeginUpdate();
            list.VirtualListSize = 0;
            list.VirtualListSize = shown.Count;
            int first = -1;
            if (keep != null || select != null)
            {
                for (int i = 0; i < shown.Count; i++)
                {
                    bool sel = (keep != null && keep.Contains(shown[i].Name)) ||
                               (select != null && string.Equals(shown[i].Name, select, StringComparison.OrdinalIgnoreCase));
                    if (!sel) continue;
                    list.SelectedIndices.Add(i);
                    if (first < 0) first = i;
                }
            }
            list.EndUpdate();
            if (first >= 0) { try { list.EnsureVisible(first); } catch { } }
            UpdateStatus();
        }

        private int Compare(FsEntry a, FsEntry b)
        {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            int r;
            switch (sortCol)
            {
                case 1: r = DateTime.Compare(a.Modified, b.Modified); break;
                case 2: r = string.Compare(TypeOf(a), TypeOf(b), StringComparison.CurrentCultureIgnoreCase); break;
                case 3: r = a.Size.CompareTo(b.Size); break;
                default: r = StrCmpLogicalW(a.Name, b.Name); break;
            }
            if (r == 0 && sortCol != 0) r = StrCmpLogicalW(a.Name, b.Name);
            return sortAsc ? r : -r;
        }

        private void Watch(string p)
        {
            try
            {
                if (watcher == null)
                {
                    watcher = new FileSystemWatcher();
                    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
                    watcher.Created += OnFsChange;
                    watcher.Deleted += OnFsChange;
                    watcher.Renamed += OnFsChange;
                }
                watcher.EnableRaisingEvents = false;
                if (p.Length > 0) { watcher.Path = p; watcher.EnableRaisingEvents = true; }
            }
            catch { }
        }

        private void OnFsChange(object s, EventArgs e)
        {
            try { BeginInvoke(new MethodInvoker(delegate { reloadTimer.Stop(); reloadTimer.Start(); })); }
            catch { }
        }

        // ---------------- выделение / статус ----------------
        private List<FsEntry> Selected()
        {
            List<FsEntry> r = new List<FsEntry>();
            foreach (int i in list.SelectedIndices) if (i >= 0 && i < shown.Count) r.Add(shown[i]);
            return r;
        }

        private List<string> SelectedNames()
        {
            List<string> r = new List<string>();
            foreach (FsEntry e in Selected()) r.Add(e.Name);
            return r;
        }

        private void UpdateStatus()
        {
            List<FsEntry> sel = Selected();
            if (sel.Count == 0)
                status.LeftText = shown.Count + Loc.T(" элем.") + (shown.Count != all.Count ? Loc.T(" (из ") + all.Count + Loc.T(", фильтр)") : "");
            else
            {
                long size = 0; bool any = false;
                foreach (FsEntry e in sel) if (!e.IsDir) { size += e.Size; any = true; }
                status.LeftText = Loc.T("Выбрано: ") + sel.Count + Loc.T(" из ") + shown.Count + (any ? "   •   " + FileOps.FormatSize(size) : "");
            }
            status.RightText = path.Length == 0 ? "" : path;
        }

        // ---------------- действия ----------------
        private void OpenEntry(FsEntry e)
        {
            if (e.IsDir) GoTo(e.Path, 1, null, -1); else OpenPath(e.Path);
        }

        private void OpenPath(string p)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(p);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(p);
                Process.Start(psi);
            }
            catch (Exception ex) { host.Notify(NoteKind.Bad, Loc.T("Не удалось открыть"), ex.Message); }
        }

        private void ListKeyDown(object s, KeyEventArgs e)
        {
            bool ctrl = e.Control, shift = e.Shift;
            if (e.KeyCode == Keys.Back) { Up(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { BeginRename(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(shift); e.Handled = true; }
            else if (ctrl && e.KeyCode == Keys.C) { CopyToClipboard(false); e.Handled = true; }
            else if (ctrl && e.KeyCode == Keys.X) { CopyToClipboard(true); e.Handled = true; }
            else if (ctrl && e.KeyCode == Keys.V) { Paste(); e.Handled = true; }
            else if (ctrl && e.KeyCode == Keys.A)
            {
                list.BeginUpdate();
                for (int i = 0; i < shown.Count; i++) list.SelectedIndices.Add(i);
                list.EndUpdate();
                e.Handled = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5: Reload(); return true;
                case Keys.Control | Keys.L: address.Box.Focus(); return true;
                case Keys.Control | Keys.F: filter.Box.Focus(); return true;
                case Keys.Alt | Keys.Left: Back(); return true;
                case Keys.Alt | Keys.Right: Forward(); return true;
                case Keys.Alt | Keys.Up: Up(); return true;
                case Keys.Control | Keys.Shift | Keys.N: NewFolder(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ListMouseUp(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.XButton1) { Back(); return; }
            if (e.Button == MouseButtons.XButton2) { Forward(); return; }
            if (e.Button == MouseButtons.Middle)
            {
                ListViewHitTestInfo ht = list.HitTest(e.Location);
                if (ht.Item != null && ht.Item.Index < shown.Count && shown[ht.Item.Index].IsDir)
                    host.NewExplorerTab(shown[ht.Item.Index].Path, Area);
            }
        }

        private void NewFolder()
        {
            if (path.Length == 0) return;
            try
            {
                string name = FileOps.UniqueName(path, Loc.T("Новая папка"), true);
                Directory.CreateDirectory(System.IO.Path.Combine(path, name));
                pendingEdit = name;
                Load(path, 0, name, -1);
            }
            catch (Exception ex) { host.Notify(NoteKind.Bad, Loc.T("Не удалось создать папку"), ex.Message); }
        }

        private void BeginRename()
        {
            if (list.SelectedIndices.Count != 1) return;
            int idx = list.SelectedIndices[0];
            if (idx >= shown.Count || shown[idx].IsDrive) return;
            renaming = shown[idx];
            try { list.Items[idx].BeginEdit(); } catch { }
        }

        private void AfterRename(object s, LabelEditEventArgs e)
        {
            e.CancelEdit = true;
            FsEntry en = renaming; renaming = null;
            string name = e.Label;
            if (en == null || name == null || name.Length == 0 || name == en.Name) return;
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                host.SetStatus(Loc.T("Имя содержит недопустимые символы."));
                return;
            }
            try
            {
                string dst = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(en.Path), name);
                if (en.IsDir) Directory.Move(en.Path, dst); else File.Move(en.Path, dst);
                Load(path, 0, name, -1);
            }
            catch (Exception ex) { host.Notify(NoteKind.Bad, Loc.T("Не удалось переименовать"), ex.Message); }
        }

        private void DeleteSelected(bool permanent)
        {
            List<FsEntry> sel = Selected();
            if (sel.Count == 0 || sel[0].IsDrive) return;
            if (permanent && !Dlg.Confirm(FindForm(),
                Loc.T("Удалить безвозвратно (") + sel.Count + Loc.T(" элем.)? Это действие нельзя отменить."), Loc.T("Удалить"), Loc.T("Отмена"))) return;
            RecycleOption opt = permanent ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
            string error = null;
            int removed = 0;
            foreach (FsEntry en in sel)
            {
                try
                {
                    removed++;
                    if (en.IsDir) FileSystem.DeleteDirectory(en.Path, UIOption.OnlyErrorDialogs, opt);
                    else FileSystem.DeleteFile(en.Path, UIOption.OnlyErrorDialogs, opt);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { error = ex.Message; }
            }
            if (error != null) host.Notify(NoteKind.Bad, Loc.T("Ошибка удаления"), error);
            else if (removed > 0) host.Notify(NoteKind.Good, permanent ? Loc.T("Удалено безвозвратно: ") + removed : Loc.T("Перемещено в корзину: ") + removed, path);
            Reload();
        }

        private void CopyToClipboard(bool cut)
        {
            List<FsEntry> sel = Selected();
            if (sel.Count == 0 || sel[0].IsDrive) return;
            try
            {
                StringCollection sc = new StringCollection();
                foreach (FsEntry en in sel) sc.Add(en.Path);
                DataObject d = new DataObject();
                d.SetFileDropList(sc);
                d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
                Clipboard.SetDataObject(d, true);
                host.SetStatus((cut ? Loc.T("Вырезано: ") : Loc.T("Скопировано: ")) + sel.Count);
            }
            catch (Exception ex) { host.SetStatus(Loc.T("Буфер обмена недоступен: ") + ex.Message); }
        }

        private void Paste()
        {
            if (path.Length == 0) return;
            try
            {
                IDataObject d = Clipboard.GetDataObject();
                if (d == null || !d.GetDataPresent(DataFormats.FileDrop)) return;
                string[] files = (string[])d.GetData(DataFormats.FileDrop);
                bool move = false;
                MemoryStream ms = d.GetData("Preferred DropEffect") as MemoryStream;
                if (ms != null)
                {
                    byte[] b = new byte[4];
                    if (ms.Read(b, 0, 4) == 4) { int v = BitConverter.ToInt32(b, 0); move = (v & 2) != 0 && (v & 1) == 0; }
                }
                int done;
                string err = FileOps.Transfer(files, path, move, out done);
                if (move) Clipboard.Clear();
                if (done > 0) host.Notify(NoteKind.Good, move ? Loc.T("Перемещено: ") + done : Loc.T("Скопировано: ") + done, Loc.T("в ") + path);
                if (err != null) host.Notify(NoteKind.Bad, Loc.T("Ошибка копирования"), err);
                Reload();
            }
            catch (Exception ex) { host.SetStatus(Loc.T("Не удалось вставить: ") + ex.Message); }
        }

        // ---------------- перетаскивание ----------------
        private string DropTargetAt(DragEventArgs e)
        {
            Point pt = list.PointToClient(new Point(e.X, e.Y));
            ListViewHitTestInfo ht = list.HitTest(pt);
            if (ht.Item != null && ht.Item.Index < shown.Count && shown[ht.Item.Index].IsDir)
            {
                string dp = shown[ht.Item.Index].Path;
                if (dragPaths != null)
                    foreach (string sp in dragPaths) if (FileOps.IsInside(dp, sp)) return null;
                return dp;
            }
            return path.Length == 0 ? null : path;
        }

        private DragDropEffects EffectFor(DragEventArgs e, string dest)
        {
            if (dest == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return DragDropEffects.None;
            bool ctrl = (e.KeyState & 8) != 0, shift = (e.KeyState & 4) != 0;
            string parent = System.IO.Path.GetDirectoryName(files[0].TrimEnd('\\'));
            if (parent != null && string.Equals(parent.TrimEnd('\\'), dest.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !ctrl)
                return DragDropEffects.None;
            DragDropEffects want;
            if (ctrl) want = DragDropEffects.Copy;
            else if (shift) want = DragDropEffects.Move;
            else
            {
                string r1 = System.IO.Path.GetPathRoot(files[0]) ?? "", r2 = System.IO.Path.GetPathRoot(dest) ?? "";
                want = string.Equals(r1, r2, StringComparison.OrdinalIgnoreCase) ? DragDropEffects.Move : DragDropEffects.Copy;
            }
            return want & e.AllowedEffect;
        }

        private void DropInto(DragEventArgs e, string dest)
        {
            DragDropEffects fx = EffectFor(e, dest);
            if (fx == DragDropEffects.None) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            int done;
            string err = FileOps.Transfer(files, dest, fx == DragDropEffects.Move, out done);
            if (done > 0) host.Notify(NoteKind.Good, fx == DragDropEffects.Move ? Loc.T("Перемещено: ") + done : Loc.T("Скопировано: ") + done, Loc.T("в ") + dest);
            if (err != null) host.Notify(NoteKind.Bad, Loc.T("Ошибка копирования"), err);
            Reload();
        }

        // ---------------- контекстное меню ----------------
        private void BuildMenu(object sender, System.ComponentModel.CancelEventArgs ce)
        {
            menu.Items.Clear();
            List<FsEntry> sel = Selected();
            bool one = sel.Count == 1;
            bool any = sel.Count > 0 && !sel[0].IsDrive;

            if (one)
            {
                FsEntry en = sel[0];
                AddItem(Loc.T("Открыть"), "Enter", delegate { OpenEntry(en); });
                if (en.IsDir && !en.IsDrive)
                {
                    AddItem(Loc.T("Открыть в новой вкладке"), "", delegate { host.NewExplorerTab(en.Path, Area); });
                    AddItem(IsPinned(en.Path) ? Loc.T("Открепить из быстрого доступа") : Loc.T("Закрепить в быстром доступе"), "", delegate { host.TogglePin(en.Path); });
                }
                else if (en.IsDir) AddItem(Loc.T("Открыть в новой вкладке"), "", delegate { host.NewExplorerTab(en.Path, Area); });
                else
                {
                    if (en.Name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                    {
                        string mp = en.Path;
                        AddItem(Loc.T("Просмотреть модель в оболочке"), "", delegate { host.OpenModel(mp, Area); });
                    }
                    ToolStripMenuItem with = new ToolStripMenuItem(Loc.T("Открыть в инструменте"));
                    foreach (ToolInfo t in host.Tools)
                    {
                        if (!t.Exists) continue;
                        ToolInfo tt = t; string fp = en.Path;
                        with.DropDownItems.Add(t.Name, null, delegate { host.OpenTool(tt, "\"" + fp + "\"", Area); });
                    }
                    if (with.DropDownItems.Count > 0) menu.Items.Add(with);
                }
                AddItem(Loc.T("Показать в Проводнике Windows"), "", delegate { Reveal(en.Path); });
                menu.Items.Add(new ToolStripSeparator());
            }
            if (any)
            {
                AddItem(Loc.T("Вырезать"), "Ctrl+X", delegate { CopyToClipboard(true); });
                AddItem(Loc.T("Копировать"), "Ctrl+C", delegate { CopyToClipboard(false); });
            }
            ToolStripMenuItem paste = AddItem(Loc.T("Вставить"), "Ctrl+V", delegate { Paste(); });
            paste.Enabled = path.Length > 0 && Clipboard.ContainsFileDropList();
            if (one)
                AddItem(Loc.T("Копировать путь"), "", delegate { try { Clipboard.SetText(sel[0].Path); } catch { } });
            if (any)
            {
                menu.Items.Add(new ToolStripSeparator());
                if (one) AddItem(Loc.T("Переименовать"), "F2", delegate { BeginRename(); });
                AddItem(Loc.T("Удалить"), "Del", delegate { DeleteSelected(false); });
            }
            if (path.Length > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                AddItem(Loc.T("Новая папка"), "Ctrl+Shift+N", delegate { NewFolder(); });
                if (sel.Count == 0) AddItem(Loc.T("Показать в Проводнике Windows"), "", delegate { Reveal(path); });
            }
            AddItem(Loc.T("Обновить"), "F5", delegate { Reload(); });
        }

        private ToolStripMenuItem AddItem(string text, string keys, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.ShortcutKeyDisplayString = keys;
            mi.Click += click;
            menu.Items.Add(mi);
            return mi;
        }

        private void Reveal(string p)
        {
            try
            {
                if (Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\"");
                else Process.Start("explorer.exe", "/select,\"" + p + "\"");
            }
            catch { }
        }
    }

    // Панель «Закреплённые папки» под деревом (нижняя зона левой колонки проводника).
    internal class PinList : Control
    {
        private const int HeadH = 28, RowH = 26;
        private readonly IShellHost host;
        public string Current = "";
        public event Action<string> OpenPin, OpenPinNewTab, Unpin;
        public event Action<string[]> DropFolders;
        private int hover = -1, scroll;

        public PinList(IShellHost h)
        {
            host = h;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AllowDrop = true;
            Font = Theme.Ui;
            Cursor = Cursors.Hand;
        }

        private static string Norm(string p) { return p.Length > 3 ? p.TrimEnd('\\') : p; }

        private int RowAt(Point p)
        {
            if (p.Y < HeadH) return -1;
            int i = (p.Y - HeadH + scroll) / RowH;
            return i >= 0 && i < host.Pins.Count ? i : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            List<string> pins = host.Pins;

            if (pins.Count == 0)
            {
                Theme.DrawText(g, Loc.T("Перетащите сюда папку\nили нажмите ★ на панели"), Theme.UiSmall,
                    new Rectangle(10, HeadH + 8, Width - 20, 60), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            }
            for (int i = 0; i < pins.Count; i++)
            {
                int y = HeadH + i * RowH - scroll;
                if (y + RowH < HeadH || y > Height) continue;
                string pin = pins[i];
                bool cur = string.Equals(Norm(pin), Norm(Current), StringComparison.OrdinalIgnoreCase);
                bool ok = Directory.Exists(pin);
                Rectangle r = new Rectangle(0, y, Width, RowH);
                if (cur || i == hover)
                    using (SolidBrush b = new SolidBrush(cur ? Theme.AccentDim : Theme.Bg2)) g.FillRectangle(b, r);
                Theme.DrawText(g, Theme.GPinned, Theme.IconSmall, new Rectangle(8, y, 18, RowH),
                    ok ? Theme.Accent : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                string name = System.IO.Path.GetFileName(pin.TrimEnd('\\'));
                if (name.Length == 0) name = pin;
                int nw = TextRenderer.MeasureText(name, Theme.Ui, new Size(1000, 30), TextFormatFlags.NoPadding).Width;
                Theme.DrawText(g, name, Theme.Ui, new Rectangle(32, y, Math.Max(10, Math.Min(nw + 2, Width - 40)), RowH),
                    ok ? Theme.Text : Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                int px = 32 + nw + 10;
                if (Width - px > 40)
                    Theme.DrawText(g, ok ? pin : Loc.T("папка недоступна"), Theme.UiSmall, new Rectangle(px, y, Width - px - 6, RowH),
                        ok ? Theme.TextDim : Theme.Bad, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            // заголовок поверх прокрутки
            using (SolidBrush b = new SolidBrush(Theme.Bg2)) g.FillRectangle(b, 0, 0, Width, HeadH);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, HeadH - 1, Width, HeadH - 1);
            Theme.DrawText(g, Theme.GPinned, Theme.IconSmall, new Rectangle(8, 0, 18, HeadH), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Loc.T("ЗАКРЕПЛЁННЫЕ"), Theme.UiSmall, new Rectangle(32, 0, Width - 60, HeadH), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (pins.Count > 0)
                Theme.DrawText(g, pins.Count.ToString(), Theme.UiSmall, new Rectangle(Width - 40, 0, 32, HeadH), Theme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int r = RowAt(e.Location);
            if (r != hover)
            {
                hover = r; Invalidate();
                Tips.Set(this, r >= 0 ? host.Pins[r] : "");
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, host.Pins.Count * RowH - (Height - HeadH));
            scroll = Math.Max(0, Math.Min(max, scroll - Math.Sign(e.Delta) * RowH));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int r = RowAt(e.Location);
            if (r < 0) return;
            string pin = host.Pins[r];
            if (e.Button == MouseButtons.Left) { if (OpenPin != null) OpenPin(pin); }
            else if (e.Button == MouseButtons.Middle) { if (OpenPinNewTab != null) OpenPinNewTab(pin); }
            else if (e.Button == MouseButtons.Right)
            {
                ContextMenuStrip m = new ContextMenuStrip();
                Theme.ApplyMenu(m);
                ToolStripMenuItem a = new ToolStripMenuItem(Loc.T("Открыть"));
                a.Click += delegate { if (OpenPin != null) OpenPin(pin); };
                ToolStripMenuItem b = new ToolStripMenuItem(Loc.T("Открыть в новой вкладке"));
                b.Click += delegate { if (OpenPinNewTab != null) OpenPinNewTab(pin); };
                ToolStripMenuItem c = new ToolStripMenuItem(Loc.T("Открепить"));
                c.Click += delegate { if (Unpin != null) Unpin(pin); };
                m.Items.Add(a); m.Items.Add(b); m.Items.Add(new ToolStripSeparator()); m.Items.Add(c);
                m.Show(this, e.Location);
            }
            base.OnMouseUp(e);
        }

        private static string[] Dirs(DragEventArgs e)
        {
            string[] files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            if (files == null) return new string[0];
            List<string> d = new List<string>();
            foreach (string f in files) if (Directory.Exists(f)) d.Add(f);
            return d.ToArray();
        }

        protected override void OnDragEnter(DragEventArgs e) { OnDragOver(e); }
        protected override void OnDragOver(DragEventArgs e)
        {
            e.Effect = Dirs(e).Length > 0 ? DragDropEffects.Link : DragDropEffects.None;
        }
        protected override void OnDragDrop(DragEventArgs e)
        {
            string[] d = Dirs(e);
            if (d.Length > 0 && DropFolders != null) DropFolders(d);
        }
    }
}
