// Диалоги в общем стиле + хранилище настроек.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SourceSDKHub
{
    // ------------------------------------------------------------------
    // Описание инструмента
    // ------------------------------------------------------------------
    public class ToolInfo
    {
        public string Name;          // название во вкладке
        public string Description;   // подпись на главной
        public string Path;          // exe / jar / bat (абсолютный путь)
        public string Args;          // аргументы
        public string WorkDir;       // рабочая папка
        public bool Stretch;         // true - растягивать окно на всю вкладку, false - центрировать
        public bool Builtin;         // встроенный (автообнаружение) или добавлен пользователем
        public bool Embed = true;    // встраивать окно во вкладку
        public bool Overlay;         // «закреплённое окно»: настоящее окно, привязанное к области (для Electron/VS Code)

        public bool IsJar { get { return Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase); } }
        public bool Exists { get { return File.Exists(Path); } }
    }

    // ------------------------------------------------------------------
    // Настройки (key=value)
    // ------------------------------------------------------------------
    internal class Settings
    {
        private readonly Dictionary<string, string> d = new Dictionary<string, string>();
        private readonly string path;

        public Settings(string p)
        {
            path = p;
            try
            {
                if (!File.Exists(p)) return;
                foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0 || raw.StartsWith(";")) continue;
                    d[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        public string Get(string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
        public bool GetBool(string k, bool def) { string v; return d.TryGetValue(k, out v) ? v == "1" : def; }
        public int GetInt(string k, int def)
        {
            string v; int r;
            return d.TryGetValue(k, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }
        public void Set(string k, string v) { d[k] = v ?? ""; }
        public void SetBool(string k, bool v) { d[k] = v ? "1" : "0"; }
        public void SetInt(string k, int v) { d[k] = v.ToString(CultureInfo.InvariantCulture); }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; Настройки Source Model Kit");
                foreach (KeyValuePair<string, string> kv in d) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Базовая форма диалога
    // ------------------------------------------------------------------
    internal class DarkForm : Form
    {
        public DarkForm()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg1;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            KeyPreview = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkenWindow(Handle);
        }

        protected FlatButton MakeButton(string text, bool primary, DialogResult result)
        {
            FlatButton b = new FlatButton(null, text, null);
            b.BaseRole = 1;
            b.Primary = primary;
            b.Width = Math.Max(88, b.Width + 12);
            b.Height = 30;
            b.Click += delegate { DialogResult = result; Close(); };
            return b;
        }
    }

    internal class MessageForm : DarkForm
    {
        public int Choice = -1;

        public MessageForm(string text, string title, string[] buttons)
        {
            Text = title;
            Size box = TextRenderer.MeasureText(text, Theme.Ui, new Size(420, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int w = Math.Max(380, Math.Min(520, box.Width + 56));
            ClientSize = new Size(w, box.Height + 100);

            Label l = new Label();
            l.Text = text; l.AutoSize = false;
            l.SetBounds(24, 24, w - 48, box.Height + 6);
            l.ForeColor = Theme.Text; l.BackColor = Theme.Bg1;
            Controls.Add(l);

            int x = w - 20;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int idx = i;
                FlatButton b = new FlatButton(null, buttons[i], null);
                b.BaseRole = 1;
                b.Primary = i == 0;
                b.Width = Math.Max(88, b.Width + 12);
                b.Height = 30;
                b.Left = x - b.Width; b.Top = ClientSize.Height - 48;
                x = b.Left - 8;
                b.Click += delegate { Choice = idx; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(b);
            }
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }
    }

    internal class PromptForm : DarkForm
    {
        private readonly DarkInput input = new DarkInput();
        public string Value { get { return input.Text; } }

        public PromptForm(string title, string label, string value)
        {
            Text = title;
            ClientSize = new Size(420, 130);
            Label l = new Label();
            l.Text = label; l.AutoSize = true; l.Left = 20; l.Top = 18;
            l.ForeColor = Theme.TextDim; l.BackColor = Theme.Bg1;
            input.SetBounds(20, 42, 380, 28);
            input.Text = value;
            FlatButton ok = MakeButton("OK", true, DialogResult.OK);
            FlatButton cancel = MakeButton(Loc.T("Отмена"), false, DialogResult.Cancel);
            ok.Left = 400 - ok.Width - cancel.Width - 8; ok.Top = 84;
            cancel.Left = 400 - cancel.Width; cancel.Top = 84;
            Controls.AddRange(new Control[] { l, input, ok, cancel });
            AcceptButton = null;
            input.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
            Shown += delegate { input.Box.Focus(); input.Box.SelectAll(); };
        }
    }

    internal static class Dlg
    {
        // Возвращает индекс нажатой кнопки или -1.
        public static int Message(IWin32Window owner, string text, string title, params string[] buttons)
        {
            if (buttons.Length == 0) buttons = new string[] { "OK" };
            using (MessageForm f = new MessageForm(text, title, buttons))
            {
                f.ShowDialog(owner);
                return f.Choice;
            }
        }

        public static void Info(IWin32Window owner, string text)
        {
            Message(owner, text, "Source Model Kit", "OK");
        }

        public static bool Confirm(IWin32Window owner, string text, string yes, string no)
        {
            return Message(owner, text, "Source Model Kit", yes, no) == 0;
        }

        public static string Prompt(IWin32Window owner, string title, string label, string value)
        {
            using (PromptForm f = new PromptForm(title, label, value))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Value : null;
        }
    }

    // ------------------------------------------------------------------
    // Диалог «Добавить инструмент»
    // ------------------------------------------------------------------
    internal class AddToolForm : DarkForm
    {
        public ToolInfo Result;
        private readonly DarkInput name = new DarkInput();
        private readonly DarkInput path = new DarkInput();
        private readonly DarkInput args = new DarkInput();
        private readonly FlatButton stretch, embed;

        public AddToolForm()
        {
            Text = Loc.T("Добавить инструмент");
            ClientSize = new Size(560, 280);

            Label l1 = Cap(Loc.T("Название"), 20, 18);
            name.SetBounds(20, 38, 520, 28);

            Label l2 = Cap(Loc.T("Файл (exe / jar / bat)"), 20, 78);
            path.SetBounds(20, 98, 482, 28);
            FlatButton browse = new FlatButton(Theme.GFolderOpen, null, Loc.T("Выбрать файл"));
            browse.BaseRole = 1; browse.SetBounds(508, 98, 32, 28);
            browse.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = Loc.T("Программы (*.exe;*.jar;*.bat;*.cmd)|*.exe;*.jar;*.bat;*.cmd|Все файлы|*.*");
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        path.Text = d.FileName;
                        if (name.Text.Length == 0) name.Text = System.IO.Path.GetFileNameWithoutExtension(d.FileName);
                    }
                }
            };

            Label l3 = Cap(Loc.T("Аргументы запуска"), 20, 138);
            args.SetBounds(20, 158, 520, 28);

            stretch = Check(Loc.T("Растягивать окно на всю вкладку"), 20, 196);
            embed = Check(Loc.T("Встраивать в оболочку (иначе — отдельное окно)"), 20, 222);

            FlatButton ok = MakeButton(Loc.T("Добавить"), true, DialogResult.None);
            FlatButton cancel = MakeButton(Loc.T("Отмена"), false, DialogResult.Cancel);
            ok.Left = 540 - ok.Width - cancel.Width - 8; ok.Top = 234;
            cancel.Left = 540 - cancel.Width; cancel.Top = 234;
            ok.Click += delegate
            {
                if (name.Text.Trim().Length == 0 || !File.Exists(path.Text.Trim()))
                {
                    Dlg.Info(this, Loc.T("Укажите название и существующий файл."));
                    return;
                }
                ToolInfo t = new ToolInfo();
                t.Name = name.Text.Trim();
                t.Path = path.Text.Trim();
                t.Args = args.Text;
                t.Stretch = stretch.Toggled;
                t.Embed = embed.Toggled;
                t.Description = "Пользовательский инструмент";
                t.Builtin = false;
                Result = t;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { l1, name, l2, path, browse, l3, args, stretch, embed, ok, cancel });
        }

        private Label Cap(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true; l.Left = x; l.Top = y;
            l.ForeColor = Theme.TextDim; l.BackColor = Theme.Bg1;
            return l;
        }

        private FlatButton Check(string text, int x, int y)
        {
            FlatButton b = new FlatButton();
            b.BaseRole = 1; b.Toggle = true; b.Toggled = true; b.LeftAlign = true;
            b.SetBounds(x, y, 400, 26);
            b.Caption = text;
            b.Glyph = Theme.GCheckOn;
            b.Click += delegate { b.Glyph = b.Toggled ? Theme.GCheckOn : Theme.GCheckOff; b.Invalidate(); };

            return b;
        }
    }
}
