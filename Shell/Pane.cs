// Базовые типы: содержимое области (Pane) и интерфейс хоста.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SourceSDKHub
{
    // Содержимое вкладки внутри области: Главная, Проводник или встроенная программа.
    internal abstract class Pane : Control
    {
        public Area Area;
        public event EventHandler TitleChanged;

        protected Pane()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg1;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
        }

        public abstract string Title { get; }
        public abstract string IconGlyph { get; }

        // Строка для сохранения раскладки (null - не сохранять, например встроенные программы).
        public virtual string Serialize() { return null; }
        public virtual void Activated() { }

        protected void RaiseTitleChanged()
        {
            if (TitleChanged != null) TitleChanged(this, EventArgs.Empty);
        }
    }

    internal interface IShellHost
    {
        List<ToolInfo> Tools { get; }
        string BaseDir { get; }
        Settings Config { get; }
        List<string> Pins { get; }
        void TogglePin(string path);
        event EventHandler PinsChanged;

        void OpenTool(ToolInfo t, string args, Area target);
        void AddToolDialog();
        void RemoveCustomTool(ToolInfo t);
        void NewExplorerTab(string path, Area target);
        void OpenModel(string path, Area target);

        bool ClosePane(Pane p, bool force);
        void SplitArea(Area a, bool vertical, double ratio);
        void CloseArea(Area a);
        void ShowPaneMenu(Pane p, Point screen);
        void ShowAddMenu(Area a, Point screen);
        void SetStatus(string text);
        void Notify(NoteKind kind, string title, string text);
    }
}
