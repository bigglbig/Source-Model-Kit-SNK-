// Минимальная обёртка OpenGL 1.1 (opengl32.dll есть в каждой Windows) и контрол-поверхность для рисования.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SourceSDKHub
{
    internal static class GL
    {
        public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x100;
        public const uint DEPTH_TEST = 0xB71, LIGHTING = 0xB50, LIGHT0 = 0x4000, LIGHT1 = 0x4001, TEXTURE_2D = 0xDE1,
            CULL_FACE = 0xB44, BLEND = 0xBE2, ALPHA_TEST = 0xBC0, NORMALIZE = 0xBA1, COLOR_MATERIAL = 0xB57;
        public const uint MODELVIEW = 0x1700, PROJECTION = 0x1701;
        public const uint VERTEX_ARRAY = 0x8074, NORMAL_ARRAY = 0x8075, TEXTURE_COORD_ARRAY = 0x8078;
        public const uint FLOAT = 0x1406, UNSIGNED_INT = 0x1405, TRIANGLES = 4, LINES = 1, POINTS = 0;
        public const uint POSITION = 0x1203, AMBIENT = 0x1200, DIFFUSE = 0x1201, SPECULAR = 0x1202, SHININESS = 0x1601;
        public const uint FRONT_AND_BACK = 0x408, AMBIENT_AND_DIFFUSE = 0x1602;
        public const uint LIGHT_MODEL_AMBIENT = 0xB53, LIGHT_MODEL_TWO_SIDE = 0xB52, LIGHT_MODEL_COLOR_CONTROL = 0x81F8, SEPARATE_SPECULAR_COLOR = 0x81FA;
        public const uint TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
        public const uint LINEAR = 0x2601, LINEAR_MIPMAP_LINEAR = 0x2703, REPEAT = 0x2901, GENERATE_MIPMAP = 0x8191;
        public const uint RGBA = 0x1908, UNSIGNED_BYTE = 0x1401, UNPACK_ALIGNMENT = 0xCF5;
        public const uint GREATER = 0x204, LEQUAL = 0x203, SRC_ALPHA = 0x302, ONE_MINUS_SRC_ALPHA = 0x303;
        public const uint LINE = 0x1B01, FILL = 0x1B02, TEXTURE_ENV = 0x2300, TEXTURE_ENV_MODE = 0x2200, MODULATE = 0x2100, REPLACE = 0x1E01;
        public const uint SMOOTH = 0x1D01, FRONT = 0x404, BACK = 0x405, POLYGON_OFFSET_FILL = 0x8037;
        public const uint RENDERER = 0x1F01, VERSION = 0x1F02;

        private const string L = "opengl32.dll";
        [DllImport(L)] public static extern void glClearColor(float r, float g, float b, float a);
        [DllImport(L)] public static extern void glClear(uint mask);
        [DllImport(L)] public static extern void glEnable(uint cap);
        [DllImport(L)] public static extern void glDisable(uint cap);
        [DllImport(L)] public static extern void glViewport(int x, int y, int w, int h);
        [DllImport(L)] public static extern void glMatrixMode(uint mode);
        [DllImport(L)] public static extern void glLoadIdentity();
        [DllImport(L)] public static extern void glLoadMatrixf(float[] m);
        [DllImport(L)] public static extern void glLightfv(uint light, uint pname, float[] p);
        [DllImport(L)] public static extern void glLightModelfv(uint pname, float[] p);
        [DllImport(L)] public static extern void glLightModeli(uint pname, int p);
        [DllImport(L)] public static extern void glMaterialfv(uint face, uint pname, float[] p);
        [DllImport(L)] public static extern void glMaterialf(uint face, uint pname, float p);
        [DllImport(L)] public static extern void glColorMaterial(uint face, uint mode);
        [DllImport(L)] public static extern void glColor4f(float r, float g, float b, float a);
        [DllImport(L)] public static extern void glColor3f(float r, float g, float b);
        [DllImport(L)] public static extern void glEnableClientState(uint a);
        [DllImport(L)] public static extern void glDisableClientState(uint a);
        [DllImport(L)] public static extern void glVertexPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(L)] public static extern void glNormalPointer(uint type, int stride, IntPtr p);
        [DllImport(L)] public static extern void glTexCoordPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(L)] public static extern void glDrawElements(uint mode, int count, uint type, IntPtr indices);
        [DllImport(L)] public static extern void glGenTextures(int n, uint[] tex);
        [DllImport(L)] public static extern void glDeleteTextures(int n, uint[] tex);
        [DllImport(L)] public static extern void glBindTexture(uint target, uint tex);
        [DllImport(L)] public static extern void glTexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, byte[] data);
        [DllImport(L)] public static extern void glTexParameteri(uint target, uint pname, int p);
        [DllImport(L)] public static extern void glTexParameterf(uint target, uint pname, float p);
        [DllImport(L)] public static extern void glTexEnvi(uint target, uint pname, int p);
        [DllImport(L)] public static extern void glPixelStorei(uint pname, int p);
        [DllImport(L)] public static extern void glAlphaFunc(uint func, float r);
        [DllImport(L)] public static extern void glBlendFunc(uint s, uint d);
        [DllImport(L)] public static extern void glDepthMask(byte flag);
        [DllImport(L)] public static extern void glDepthFunc(uint f);
        [DllImport(L)] public static extern void glPolygonMode(uint face, uint mode);
        [DllImport(L)] public static extern void glPolygonOffset(float f, float u);
        [DllImport(L)] public static extern void glLineWidth(float w);
        [DllImport(L)] public static extern void glPointSize(float s);
        [DllImport(L)] public static extern void glBegin(uint mode);
        [DllImport(L)] public static extern void glEnd();
        [DllImport(L)] public static extern void glVertex3f(float x, float y, float z);
        [DllImport(L)] public static extern void glCullFace(uint m);
        [DllImport(L)] public static extern void glShadeModel(uint m);
        [DllImport(L)] public static extern uint glGetError();
        [DllImport(L)] public static extern IntPtr glGetString(uint name);
        [DllImport(L)] public static extern void glFinish();

        [DllImport(L)] public static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport(L)] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr ctx);
        [DllImport(L)] public static extern bool wglDeleteContext(IntPtr ctx);
    }

    internal static class Gdi
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize, nVersion;
            public uint dwFlags;
            public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift,
                cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits,
                cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }

        [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int fmt, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hdc);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    }

    // Окно с контекстом OpenGL. Рисование - в обработчике Render (контекст уже активен).
    internal class GlSurface : Control
    {
        private IntPtr hdc = IntPtr.Zero, ctx = IntPtr.Zero;
        public string Error;
        public string RendererName = "";
        public event Action<int, int> Render;

        public GlSurface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public bool Ready { get { return ctx != IntPtr.Zero; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20 | 0x1 | 0x2;  // CS_OWNDC | CS_HREDRAW | CS_VREDRAW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                hdc = Gdi.GetDC(Handle);
                Gdi.PIXELFORMATDESCRIPTOR pfd = new Gdi.PIXELFORMATDESCRIPTOR();
                pfd.nSize = (ushort)Marshal.SizeOf(typeof(Gdi.PIXELFORMATDESCRIPTOR));
                pfd.nVersion = 1;
                pfd.dwFlags = 0x4 | 0x20 | 0x1;     // DRAW_TO_WINDOW | SUPPORT_OPENGL | DOUBLEBUFFER
                pfd.iPixelType = 0; pfd.cColorBits = 32; pfd.cAlphaBits = 8; pfd.cDepthBits = 24;
                int fmt = Gdi.ChoosePixelFormat(hdc, ref pfd);
                if (fmt == 0 || !Gdi.SetPixelFormat(hdc, fmt, ref pfd)) { Error = Loc.T("Не удалось выбрать формат пикселей OpenGL"); return; }
                ctx = GL.wglCreateContext(hdc);
                if (ctx == IntPtr.Zero) { Error = Loc.T("Не удалось создать контекст OpenGL"); return; }
                GL.wglMakeCurrent(hdc, ctx);
                IntPtr r = GL.glGetString(GL.RENDERER);
                RendererName = r == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(r);
                GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex) { Error = Loc.T("OpenGL недоступен: ") + ex.Message; ctx = IntPtr.Zero; }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Release();
            base.OnHandleDestroyed(e);
        }

        private void Release()
        {
            try
            {
                if (ctx != IntPtr.Zero) { GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); GL.wglDeleteContext(ctx); ctx = IntPtr.Zero; }
                if (hdc != IntPtr.Zero) { Gdi.ReleaseDC(Handle, hdc); hdc = IntPtr.Zero; }
            }
            catch { }
        }

        // Делает контекст текущим на время вызова (например, для загрузки текстур).
        public bool MakeCurrent()
        {
            return ctx != IntPtr.Zero && GL.wglMakeCurrent(hdc, ctx);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ctx == IntPtr.Zero)
            {
                e.Graphics.Clear(Theme.Bg1);
                Theme.DrawText(e.Graphics, Error ?? Loc.T("OpenGL не инициализирован"), Theme.Ui, ClientRectangle, Theme.Bad,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (Width <= 0 || Height <= 0) return;
            GL.wglMakeCurrent(hdc, ctx);
            if (Render != null) Render(Width, Height);
            Gdi.SwapBuffers(hdc);
            GL.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        }
    }
}
