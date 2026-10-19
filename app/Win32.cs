// Mac Skin: chamadas diretas ao Windows (user32, shell32, dwmapi) usadas pela dock, barra e botões.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MacSkin
{
    static class Win32
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string classe, string titulo);
        [DllImport("user32.dll")] static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);

        public static string Classe(IntPtr h) { var s = new StringBuilder(128); GetClassName(h, s, 128); return s.ToString(); }
        public static string Titulo(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
        public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }

        // estilos estendidos: fora do Alt+Tab (TOOLWINDOW), não rouba foco (NOACTIVATE), clique atravessa (TRANSPARENT)
        public const int EX_TOOL = 0x80, EX_NOACTIVATE = 0x08000000, EX_TRANSPARENT = 0x20;
        public static void Estilo(IntPtr h, int extra) { SetWindowLong(h, -20, GetWindowLong(h, -20) | extra); }

        // sempre no topo, sem ativar
        public static void MoverNoTopo(IntPtr h, int x, int y, int w, int hh) { SetWindowPos(h, new IntPtr(-1), x, y, w, hh, 0x0010); }
        // sem ativar, sem mudar a ordem
        public static void Mover(IntPtr h, int x, int y, int w, int hh) { SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 0x0010 | 0x0004); }

        // Win + tecla (A = configurações rápidas, N = notificações e calendário)
        public static void Win(byte tecla)
        {
            keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(tecla, 0, 0, UIntPtr.Zero);
            keybd_event(tecla, 0, 2, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }

        // ids dos processos com janela visível e com título (o que conta como "app aberto" na dock)
        public static HashSet<uint> PidsComJanela()
        {
            var r = new HashSet<uint>();
            EnumWindows((h, l) =>
            {
                if (IsWindowVisible(h) && GetAncestor(h, 3) == h && (GetWindowLong(h, -20) & EX_TOOL) == 0 && Titulo(h).Length > 0)
                    r.Add(Pid(h));
                return true;
            }, IntPtr.Zero);
            return r;
        }

        public static bool TelaCheia(IntPtr eu, int w, int h)
        {
            var f = GetForegroundWindow(); if (f == IntPtr.Zero || f == eu) return false;
            var n = Classe(f); if (n == "Progman" || n == "WorkerW" || n == "Shell_TrayWnd") return false;
            RECT r; GetWindowRect(f, out r); return r.L <= 0 && r.T <= 0 && r.R >= w && r.B >= h;
        }

        // barra de tarefas do Windows: oculta de vez (senão sobe por cima da dock quando o mouse encosta embaixo)
        // (a "Shell_TrayWnd" do próprio Mac Skin, que recebe os ícones da bandeja, fica de fora)
        static readonly uint Eu = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        public static void BarraWindows(bool mostrar)
        {
            foreach (var c in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr h = IntPtr.Zero;
                while ((h = FindWindowEx(IntPtr.Zero, h, c, null)) != IntPtr.Zero)
                    if (Pid(h) != Eu && IsWindowVisible(h) != mostrar) ShowWindow(h, mostrar ? 5 : 0);
            }
        }

        // ---------- AppBar: reserva uma faixa da tela (janelas maximizadas param antes dela) ----------
        [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public int msg; public int edge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(int m, ref APPBARDATA d);
        // borda: 1 = em cima, 3 = embaixo
        public static void Reservar(IntPtr h, int borda, int largura, int alturaTela, int faixa) { Bandeja.Direto(() => Reservar2(h, borda, largura, alturaTela, faixa)); }
        static void Reservar2(IntPtr h, int borda, int largura, int alturaTela, int faixa)
        {
            var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = h; d.edge = borda;
            SHAppBarMessage(0, ref d);
            d.rc.L = 0; d.rc.R = largura;
            if (borda == 1) { d.rc.T = 0; d.rc.B = faixa; } else { d.rc.B = alturaTela; d.rc.T = alturaTela - faixa; }
            SHAppBarMessage(2, ref d);
            if (borda == 1) d.rc.B = d.rc.T + faixa; else d.rc.T = d.rc.B - faixa;
            SHAppBarMessage(3, ref d);
        }
        public static void Liberar(IntPtr h) { Bandeja.Direto(() => { var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = h; SHAppBarMessage(1, ref d); }); }

        // ---------- vidro (acrylic) e cantos do Windows 11 ----------
        [StructLayout(LayoutKind.Sequential)] struct ACCENT { public int State; public int Flags; public int Color; public int Anim; }
        [StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int n);
        // acrylic tingido (cor AABBGGRR); o acrylic ignora recorte, mas respeita o canto redondo nativo (8 px)
        public static void Vidro(IntPtr h, int abgr, bool redondo)
        {
            var a = new ACCENT(); a.State = 4; a.Flags = 2; a.Color = abgr;
            int n = Marshal.SizeOf(a); IntPtr p = Marshal.AllocHGlobal(n); Marshal.StructureToPtr(a, p, false);
            var d = new WCAD(); d.Attr = 19; d.Data = p; d.Size = n; SetWindowCompositionAttribute(h, ref d); Marshal.FreeHGlobal(p);
            int canto = redondo ? 2 : 1; DwmSetWindowAttribute(h, 33, ref canto, 4);
            int semBorda = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(h, 34, ref semBorda, 4);   // sem a borda de 1 px do Windows 11
        }
        // área dos 3 botões desenhados pelo Windows (relativa à janela) e parte visível da janela
        public static RECT BotoesDwm(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 5, out r, 16) != 0) r = new RECT(); return r; }
        public static RECT Visivel(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r); return r; }

        // ---------- Lixeira ----------
        [StructLayout(LayoutKind.Sequential)] struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string raiz, ref SHQUERYRBINFO i);
        public static bool LixeiraCheia()
        {
            var i = new SHQUERYRBINFO(); i.cbSize = Marshal.SizeOf(i);
            return SHQueryRecycleBin(null, ref i) == 0 && i.i64NumItems > 0;
        }

        // ---------- bateria ----------
        [StructLayout(LayoutKind.Sequential)] public struct ENERGIA { public byte AC, Flags, Percent, Saver; public int Life, Full; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out ENERGIA e);

        // ---------- mensagens ----------
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    }
}
