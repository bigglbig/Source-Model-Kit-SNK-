// Win32-обёртки, общие для всей оболочки.

using System;
using System.Runtime.InteropServices;

namespace SourceSDKHub
{
    internal static class Native
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L;
        public const long WS_SYSMENU = 0x00080000L;
        public const long WS_MINIMIZEBOX = 0x00020000L;
        public const long WS_MAXIMIZEBOX = 0x00010000L;
        public const long WS_BORDER = 0x00800000L;
        public const long WS_DLGFRAME = 0x00400000L;
        public const long WS_POPUP = 0x80000000L;
        public const long WS_CHILD = 0x40000000L;
        public const long WS_VISIBLE = 0x10000000L;
        public const long WS_CLIPSIBLINGS = 0x04000000L;
        public const long WS_CLIPCHILDREN = 0x02000000L;

        public const long WS_EX_DLGMODALFRAME = 0x00000001L;
        public const long WS_EX_WINDOWEDGE = 0x00000100L;
        public const long WS_EX_CLIENTEDGE = 0x00000200L;
        public const long WS_EX_STATICEDGE = 0x00020000L;
        public const long WS_EX_APPWINDOW = 0x00040000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;

        public const uint WM_CLOSE = 0x0010;
        public const int WM_ERASEBKGND = 0x0014;
        public const int WM_SETREDRAW = 0x000B;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        public const uint GW_OWNER = 4;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        public const int GWLP_HWNDPARENT = -8;
        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int idx);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int idx, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int idx, IntPtr value);

        public static long GetStyle(IntPtr h, int idx)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(h, idx).ToInt64();
            return (uint)GetWindowLong32(h, idx);
        }

        public static void SetStyle(IntPtr h, int idx, long value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, idx, new IntPtr(value));
            else SetWindowLong32(h, idx, unchecked((int)value));
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        // ---- тема / DWM ----
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---- иконки файловой системы ----
        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_TYPENAME = 0x400;
        public const uint SHGFI_SMALLICON = 0x1;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO fi, uint cb, uint flags);

        // ---- глобальный перехват клавиш ----
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int id, HookProc cb, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        // Ищет главное видимое окно процесса (без владельца, с заголовком).
        public static IntPtr FindMainWindow(int pid)
        {
            IntPtr best = IntPtr.Zero;
            int bestArea = 0;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p != (uint)pid) return true;
                if (!IsWindowVisible(h)) return true;
                if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
                if (GetWindowTextLength(h) == 0) return true;
                long ex = GetStyle(h, GWL_EXSTYLE);
                if ((ex & WS_EX_TOOLWINDOW) != 0) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                int area = (r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }
    }

    // Глобальные горячие клавиши оболочки: работают, даже когда фокус внутри
    // встроенной чужой программы (обычные WinForms-хоткеи туда не доходят).
    internal sealed class KeyHook : IDisposable
    {
        private readonly Native.HookProc proc;
        private IntPtr hook = IntPtr.Zero;
        public Func<int, bool> Handler;

        public KeyHook()
        {
            proc = Callback;
            hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0);
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && Handler != null)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x100 || msg == 0x104)
                {
                    int vk = Marshal.ReadInt32(lParam);
                    bool handled = false;
                    try { handled = Handler(vk); } catch { }
                    if (handled) return (IntPtr)1;
                }
            }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
    }
}
