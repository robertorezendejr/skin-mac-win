// Mac Skin: holofote do mouse. Tocar Ctrl duas vezes seguidas escurece todas as telas e deixa claro só um círculo em
// volta do cursor, que acompanha o mouse. Ctrl duas vezes de novo (ou Esc) desliga.
// Uma janela só, transparente a cliques, cobrindo a tela virtual inteira (todos os monitores) com alfa por pixel. A cada
// movimento só o quadrado em volta do cursor (o antigo e o novo) é redesenhado e mandado ao Windows (prcDirty): o custo
// não depende do tamanho das telas. Thread própria com o gancho de teclado e a janela, como o Arrastar3Dedos.
// Opcional: holofote.txt na pasta do Mac Skin, com as linhas  raio=88  escuro=60  desligado
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MacSkin
{
    static class Holofote
    {
        static string ArquivoConfig { get { return Path.Combine(Comum.Dir, "holofote.txt"); } }
        static void Log(string m) { Comum.Log("holofote.log", m); }

        // ajustes
        static int raioBase = 88;         // raio do círculo claro, em pixels a 100% de escala
        static int escuroPct = 60;        // quanto escurece fora do círculo
        const int ToqueMaxMs = 350;       // Ctrl apertado por mais que isso não conta como toque
        const int EntreToquesMs = 450;    // tempo máximo entre o 1º e o 2º toque
        const int FadeMs = 140;

        static Thread thread;
        static IntPtr hwnd, gancho;
        static WndProc proc = Proc;
        static HookProc procTeclado = Teclado;

        // ---------- ciclo de vida ----------
        public static void Iniciar()
        {
            if (thread != null) return;
            try
            {
                if (File.Exists(ArquivoConfig))
                    foreach (var linha in File.ReadAllLines(ArquivoConfig))
                    {
                        var p = linha.Split('=');
                        string k = p[0].Trim().ToLowerInvariant(), v = p.Length > 1 ? p[1].Trim() : "";
                        int n;
                        if (k == "desligado") { Log("desligado em holofote.txt"); return; }
                        if (k == "raio" && int.TryParse(v, out n) && n >= 30 && n <= 800) raioBase = n;
                        if (k == "escuro" && int.TryParse(v, out n) && n >= 10 && n <= 95) escuroPct = n;
                    }
            }
            catch (Exception e) { Log("holofote.txt: " + e.Message); }
            var pronto = new ManualResetEvent(false);
            thread = new Thread(() => Laco(pronto)) { IsBackground = true, Name = "Holofote" };
            thread.Start();
            pronto.WaitOne(3000);
        }

        public static void Parar()
        {
            if (hwnd != IntPtr.Zero) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (thread != null) thread.Join(1000);
            thread = null;
        }

        static void Laco(ManualResetEvent pronto)
        {
            try
            {
                // pixels reais em todos os monitores, mesmo com escalas diferentes
                try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
                var wc = new WNDCLASS { lpfnWndProc = proc, hInstance = GetModuleHandle(null), lpszClassName = "MacSkinHolofote" };
                RegisterClass(ref wc);
                // WS_EX_LAYERED | TRANSPARENT (cliques passam) | TOOLWINDOW (fora do Alt+Tab) | TOPMOST | NOACTIVATE
                hwnd = CreateWindowEx(0x80000 | 0x20 | 0x80 | 0x8 | 0x8000000, wc.lpszClassName, "Mac Skin Holofote", 0x80000000 /* WS_POPUP */,
                    0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Log("janela não abriu: " + Marshal.GetLastWin32Error()); return; }
                gancho = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, procTeclado, GetModuleHandle(null), 0);
                if (gancho == IntPtr.Zero) { Log("gancho de teclado falhou: " + Marshal.GetLastWin32Error()); DestroyWindow(hwnd); hwnd = IntPtr.Zero; return; }
                Log("holofote pronto (Ctrl duas vezes; raio " + raioBase + ", escuro " + escuroPct + "%)");
            }
            catch (Exception e) { Log("não iniciou: " + e); return; }
            finally { pronto.Set(); }

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            if (gancho != IntPtr.Zero) UnhookWindowsHookEx(gancho);
            gancho = IntPtr.Zero;
        }

        // ---------- Ctrl duas vezes ----------
        const int Marca = 0x4D534B4E;   // teclas mandadas pela Substituição de texto: não contam
        static bool ctrlBaixo, sujo, ativo;
        static uint ctrlDesceu, ultimoToque;

        static IntPtr Teclado(int n, IntPtr w, IntPtr l)
        {
            try
            {
                if (n >= 0)
                {
                    var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(KBDLLHOOKSTRUCT));
                    if (k.extra.ToInt64() != Marca) Tecla(w.ToInt32(), k.vk, k.time);
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, n, w, l);
        }

        static void Tecla(int msg, uint vk, uint t)
        {
            bool desce = msg == 0x100 || msg == 0x104, sobe = msg == 0x101 || msg == 0x105;
            bool ctrl = vk == 0x11 || vk == 0xA2 || vk == 0xA3;
            if (desce)
            {
                if (ctrl) { if (!ctrlBaixo) { ctrlBaixo = true; ctrlDesceu = t; sujo = false; } return; }   // repetição automática não reinicia
                sujo = true; ultimoToque = 0;                                       // Ctrl+C, Ctrl+V... não é toque
                if (vk == 0x1B && ativo) PostMessage(hwnd, WM_ALTERNAR, IntPtr.Zero, IntPtr.Zero);   // Esc desliga
                return;
            }
            if (!sobe || !ctrl) return;
            bool toque = ctrlBaixo && !sujo && unchecked(t - ctrlDesceu) <= ToqueMaxMs;
            ctrlBaixo = false;
            if (!toque) { ultimoToque = 0; return; }
            if (ultimoToque != 0 && unchecked(t - ultimoToque) <= EntreToquesMs)
            {
                ultimoToque = 0;
                PostMessage(hwnd, WM_ALTERNAR, IntPtr.Zero, IntPtr.Zero);   // fora do gancho: o gancho tem de voltar rápido
            }
            else ultimoToque = t == 0 ? 1 : t;
        }

        // ---------- janela ----------
        const uint WM_ALTERNAR = 0x8001;   // WM_APP + 1
        const uint WM_TIMER = 0x113, WM_CLOSE = 0x10, WM_DESTROY = 0x2, WM_DISPLAYCHANGE = 0x7E;
        const int IdMouse = 1, IdFade = 2;

        static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                switch (m)
                {
                    case WM_ALTERNAR: if (ativo) Esconder(); else Mostrar(); return IntPtr.Zero;
                    case WM_TIMER:
                        if (w == (IntPtr)IdMouse) Acompanhar();
                        else if (w == (IntPtr)IdFade) Fade();
                        return IntPtr.Zero;
                    case WM_DISPLAYCHANGE: if (ativo || bits != IntPtr.Zero) Fim(); break;   // monitores mudaram: a tela virtual não é mais a mesma
                    case WM_CLOSE: DestroyWindow(h); return IntPtr.Zero;
                    case WM_DESTROY: Fim(); hwnd = IntPtr.Zero; PostQuitMessage(0); return IntPtr.Zero;
                }
            }
            catch (Exception e) { Log("mensagem " + m + ": " + e.Message); }
            return DefWindowProc(h, m, w, l);
        }

        // tela virtual e bitmap
        static int vx, vy, vw, vh, raio, lado;
        static IntPtr dcMem, bmp, bmpAntigo, bits;
        static int[] holofote, linhaEscura;
        static int cx = int.MinValue, cy;          // centro desenhado agora (coordenadas da tela virtual)
        static int alfa, alfaAlvo;                 // opacidade geral da janela, para o fade
        static uint fadeInicio; static int fadeDe;
        static int giros;
        // memória fixa para os ponteiros do UPDATELAYEREDWINDOWINFO
        static IntPtr pDst, pTam, pSrc, pBlend, pSujo;

        static void Mostrar()
        {
            if (bits == IntPtr.Zero && !Preparar()) return;
            ativo = true;
            POINT c; GetCursorPos(out c);
            Pintar(c.X - vx, c.Y - vy, true);
            ComecarFade(255);
            ShowWindow(hwnd, 4 /* SW_SHOWNOACTIVATE */);
            SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);   // NOSIZE|NOMOVE|NOACTIVATE
            SetTimer(hwnd, (IntPtr)IdMouse, 8, IntPtr.Zero);
        }

        static void Esconder()
        {
            ativo = false;
            KillTimer(hwnd, (IntPtr)IdMouse);
            ComecarFade(0);
        }

        // apaga a janela e devolve a memória do bitmap (uns 20 MB com dois monitores)
        static void Fim()
        {
            ativo = false; alfa = alfaAlvo = 0;
            if (hwnd != IntPtr.Zero) { KillTimer(hwnd, (IntPtr)IdMouse); KillTimer(hwnd, (IntPtr)IdFade); ShowWindow(hwnd, 0); }
            if (dcMem != IntPtr.Zero) { SelectObject(dcMem, bmpAntigo); DeleteDC(dcMem); dcMem = IntPtr.Zero; }
            if (bmp != IntPtr.Zero) { DeleteObject(bmp); bmp = IntPtr.Zero; }
            bits = IntPtr.Zero; cx = int.MinValue;
        }

        static bool Preparar()
        {
            vx = GetSystemMetrics(76); vy = GetSystemMetrics(77); vw = GetSystemMetrics(78); vh = GetSystemMetrics(79);
            if (vw <= 0 || vh <= 0) return false;
            if (pDst == IntPtr.Zero) { pDst = Marshal.AllocHGlobal(8); pTam = Marshal.AllocHGlobal(8); pSrc = Marshal.AllocHGlobal(8); pBlend = Marshal.AllocHGlobal(4); pSujo = Marshal.AllocHGlobal(16); }
            Marshal.WriteInt32(pSrc, 0); Marshal.WriteInt32(pSrc, 4, 0);

            uint dpi = 96; try { dpi = GetDpiForSystem(); } catch { }
            raio = Math.Max(20, raioBase * (int)dpi / 96);
            double borda = 1.5;                  // borda seca: só o antisserrilhado
            lado = raio * 2 + 2;
            int a = escuroPct * 255 / 100;
            int escuro = a << 24;                // preto pré-multiplicado: só o alfa
            linhaEscura = new int[vw];
            for (int i = 0; i < vw; i++) linhaEscura[i] = escuro;

            holofote = new int[lado * lado];
            double meio = lado / 2.0, dentro = raio - borda;
            for (int y = 0; y < lado; y++)
                for (int x = 0; x < lado; x++)
                {
                    double dx = x + 0.5 - meio, dy = y + 0.5 - meio, d = Math.Sqrt(dx * dx + dy * dy);
                    double f = d <= dentro ? 0 : d >= raio ? 1 : (d - dentro) / borda;
                    f = f * f * (3 - 2 * f);    // smoothstep
                    holofote[y * lado + x] = ((int)Math.Round(a * f)) << 24;
                }

            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = vw, biHeight = -vh, biPlanes = 1, biBitCount = 32 };
            IntPtr dcTela = GetDC(IntPtr.Zero);
            bmp = CreateDIBSection(dcTela, ref bi, 0, out bits, IntPtr.Zero, 0);
            dcMem = CreateCompatibleDC(dcTela);
            ReleaseDC(IntPtr.Zero, dcTela);
            if (bmp == IntPtr.Zero || bits == IntPtr.Zero) { Log("bitmap " + vw + "x" + vh + " não criado"); Fim(); return false; }
            bmpAntigo = SelectObject(dcMem, bmp);
            for (int y = 0; y < vh; y++) Marshal.Copy(linhaEscura, 0, bits + y * vw * 4, vw);
            cx = int.MinValue;
            return true;
        }

        static void Acompanhar()
        {
            POINT c; GetCursorPos(out c);
            int x = c.X - vx, y = c.Y - vy;
            if (x != cx || y != cy) Pintar(x, y, false);
            // menus e janelas "sempre no topo" abertos depois podem passar na frente: reafirma de vez em quando
            if (++giros % 60 == 0) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }

        // apaga o círculo antigo, desenha o novo e manda ao Windows só o retângulo que mudou
        static void Pintar(int x, int y, bool tudo)
        {
            int ox = cx, oy = cy;
            int sx0 = int.MaxValue, sy0 = int.MaxValue, sx1 = int.MinValue, sy1 = int.MinValue;
            if (ox != int.MinValue) { Quadrado(ox, oy, false); Unir(ox, oy, ref sx0, ref sy0, ref sx1, ref sy1); }
            Quadrado(x, y, true); Unir(x, y, ref sx0, ref sy0, ref sx1, ref sy1);
            cx = x; cy = y;
            if (tudo) { sx0 = 0; sy0 = 0; sx1 = vw; sy1 = vh; }
            else { sx0 = Math.Max(0, sx0); sy0 = Math.Max(0, sy0); sx1 = Math.Min(vw, sx1); sy1 = Math.Min(vh, sy1); }
            if (sx1 <= sx0 || sy1 <= sy0) { if (tudo) Enviar(0, 0, vw, vh); return; }
            Enviar(sx0, sy0, sx1, sy1);
        }

        static void Unir(int x, int y, ref int x0, ref int y0, ref int x1, ref int y1)
        {
            int h = lado / 2;
            x0 = Math.Min(x0, x - h); y0 = Math.Min(y0, y - h); x1 = Math.Max(x1, x - h + lado); y1 = Math.Max(y1, y - h + lado);
        }

        // claro = desenha o holofote centrado em (x, y); senão devolve o escuro naquele quadrado
        static void Quadrado(int x, int y, bool claro)
        {
            int h = lado / 2, x0 = x - h, y0 = y - h;
            int ix0 = Math.Max(0, x0), ix1 = Math.Min(vw, x0 + lado);
            if (ix1 <= ix0) return;
            for (int yy = Math.Max(0, y0); yy < Math.Min(vh, y0 + lado); yy++)
            {
                IntPtr dst = bits + (yy * vw + ix0) * 4;
                if (claro) Marshal.Copy(holofote, (yy - y0) * lado + (ix0 - x0), dst, ix1 - ix0);
                else Marshal.Copy(linhaEscura, 0, dst, ix1 - ix0);
            }
        }

        static void Enviar(int x0, int y0, int x1, int y1)
        {
            Marshal.WriteInt32(pDst, vx); Marshal.WriteInt32(pDst, 4, vy);
            Marshal.WriteInt32(pTam, vw); Marshal.WriteInt32(pTam, 4, vh);
            Marshal.WriteInt32(pBlend, (alfa << 16) | (1 << 24));   // AC_SRC_OVER, SourceConstantAlpha, AC_SRC_ALPHA
            Marshal.WriteInt32(pSujo, x0); Marshal.WriteInt32(pSujo, 4, y0); Marshal.WriteInt32(pSujo, 8, x1); Marshal.WriteInt32(pSujo, 12, y1);
            var info = new UPDATELAYEREDWINDOWINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(UPDATELAYEREDWINDOWINFO)), pptDst = pDst, psize = pTam,
                hdcSrc = dcMem, pptSrc = pSrc, pblend = pBlend, dwFlags = 2 /* ULW_ALPHA */, prcDirty = pSujo
            };
            if (!UpdateLayeredWindowIndirect(hwnd, ref info)) Log("UpdateLayeredWindowIndirect falhou: " + Marshal.GetLastWin32Error());
        }

        // ---------- fade ----------
        static void ComecarFade(int alvo)
        {
            alfaAlvo = alvo; fadeDe = alfa; fadeInicio = GetTickCount();
            SetTimer(hwnd, (IntPtr)IdFade, 10, IntPtr.Zero);
            Fade();
        }

        static void Fade()
        {
            double p = Math.Min(1.0, unchecked(GetTickCount() - fadeInicio) / (double)FadeMs);
            p = 1 - (1 - p) * (1 - p);   // desacelera no fim
            alfa = (int)Math.Round(fadeDe + (alfaAlvo - fadeDe) * p);
            if (bits == IntPtr.Zero) return;
            int h = lado / 2;
            Enviar(Math.Max(0, cx - h), Math.Max(0, cy - h), Math.Max(1, Math.Min(vw, cx - h + lado)), Math.Max(1, Math.Min(vh, cy - h + lado)));
            if (p >= 1)
            {
                KillTimer(hwnd, (IntPtr)IdFade);
                if (alfaAlvo == 0) Fim();
            }
        }

        // ---------- Win32 ----------
        delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS { public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vk, scan, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        struct UPDATELAYEREDWINDOWINFO { public uint cbSize; public IntPtr hdcDst, pptDst, psize, hdcSrc, pptSrc; public uint crKey; public IntPtr pblend; public uint dwFlags; public IntPtr prcDirty; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WNDCLASS wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint ex, string cls, string nome, uint estilo, int x, int y, int w, int h, IntPtr pai, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint de, uint ate);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr f);
        [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, IntPtr id);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindowIndirect(IntPtr h, ref UPDATELAYEREDWINDOWINFO i);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
        [DllImport("kernel32.dll")] static extern uint GetTickCount();
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint uso, out IntPtr bits, IntPtr secao, uint ofs);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    }
}
