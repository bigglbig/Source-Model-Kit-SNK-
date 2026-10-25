// Уведомления: всплывающие «тосты» поверх оболочки (в том числе поверх встроенных программ)
// и журнал событий, который открывается колокольчиком в верхней панели.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SourceSDKHub
{
    internal enum NoteKind { Info, Good, Warn, Bad }

    internal class Note
    {
        public DateTime Time;
        public NoteKind Kind;
        public string Title, Text;
    }

    internal class Notifier
    {
        private readonly Form owner;
        private readonly List<ToastForm> live = new List<ToastForm>();
        public readonly List<Note> History = new List<Note>();
        public int Unread;
        public bool ToastsEnabled = true;
        public event EventHandler Changed;

        public Notifier(Form o) { owner = o; }

        public void Push(NoteKind kind, string title, string text)
        {
            Note n = new Note();
            n.Time = DateTime.Now; n.Kind = kind; n.Title = title; n.Text = text ?? "";
            History.Insert(0, n);
            if (History.Count > 60) History.RemoveAt(History.Count - 1);
            Unread++;
            if (Changed != null) Changed(this, EventArgs.Empty);

            if (!ToastsEnabled || owner.IsDisposed || !owner.Visible || owner.WindowState == FormWindowState.Minimized) return;
            while (live.Count >= 4) live[0].Close();
            ToastForm t = new ToastForm(n);
            t.FormClosed += delegate { live.Remove(t); Relayout(); };
            live.Add(t);
            Relayout();
            t.Show(owner);
            Native.SetWindowPos(t.Handle, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);   // поверх закреплённых окон
        }

        public void Clear()
        {
            History.Clear(); Unread = 0;
            foreach (ToastForm t in new List<ToastForm>(live)) t.Close();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void MarkRead()
        {
            if (Unread == 0) return;
            Unread = 0;
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Relayout()
        {
            if (owner.IsDisposed) return;
            Rectangle r = owner.RectangleToScreen(owner.ClientRectangle);
            int y = r.Bottom - 36;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                ToastForm t = live[i];
                y -= t.Height + 8;
                t.Location = new Point(r.Right - t.Width - 16, y);
            }
        }
    }

    internal class ToastForm : Form
    {
        private readonly Note note;
        private readonly Timer life = new Timer();

        public ToastForm(Note n)
        {
            note = n;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Theme.Ui;
            int textH = TextRenderer.MeasureText(n.Text, Theme.UiSmall, new Size(296, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            ClientSize = new Size(340, Math.Min(120, 40 + Math.Max(textH, n.Text.Length == 0 ? 0 : 14)));
            Cursor = Cursors.Hand;
            life.Interval = n.Kind == NoteKind.Bad || n.Kind == NoteKind.Warn ? 9000 : 5000;
            life.Tick += delegate { life.Stop(); Close(); };
            life.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
                return cp;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { life.Stop(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { life.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Close(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) life.Dispose();
            base.Dispose(disposing);
        }

        public static Color KindColor(NoteKind k)
        {
            return k == NoteKind.Good ? Theme.Good : k == NoteKind.Bad ? Theme.Bad : k == NoteKind.Warn ? Color.FromArgb(230, 170, 60) : Theme.Accent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg2);
            Color kc = KindColor(note.Kind);
            using (SolidBrush b = new SolidBrush(kc)) g.FillRectangle(b, 0, 0, 4, Height);
            using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            Theme.DrawText(g, note.Title, Theme.UiBold, new Rectangle(16, 8, Width - 28, 20), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (note.Text.Length > 0)
                Theme.DrawText(g, note.Text, Theme.UiSmall, new Rectangle(16, 30, Width - 32, Height - 36), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }
}
