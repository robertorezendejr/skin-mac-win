// Mac Skin: "Arrastar com três dedos" do macOS no touchpad de precisão do Windows.
// Lê o touchpad pelo Raw Input (HID Digitizer / Touch Pad), numa thread própria com a sua janela invisível (o arrasto
// não depende da tela do Mac Skin estar ocupada). Com exatamente 3 dedos encostados e em movimento, aperta o botão
// esquerdo e move o cursor; ao tirar os dedos solta depois de uma tolerância (para reposicionar os dedos, como no Mac).
// Tocar com 3 dedos sem mover não faz nada. Veio do projeto Arrastar3dedos (C), que deixa de iniciar sozinho.
// Opcional: arrastar3dedos.txt na pasta do Mac Skin, com as linhas  velocidade=1.5  tolerancia=300  desligado
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MacSkin
{
    // a máquina de estados do gesto, sem nada de Windows: quem usa liga o mouse e o relógio nos delegates (e os testes
    // ligam em listas)
    class MotorArrasto
    {
        public const int MaxContatos = 16;
        public const uint ContatoVelhoMs = 150;   // contato sem atualização por mais que isso é descartado
        public const double LimiarInicioPx = 3.0; // movimento mínimo para começar (3 toques parados não arrastam)

        public enum Estado { Ocioso, Armado, Arrastando, Soltando }

        public Action<bool> Botao;            // true = aperta, false = solta
        public Action<int, int> Mover;        // deslocamento do cursor em pixels (já com a velocidade)
        public Action<uint> ArmaSolta;        // pede que SoltarPorTempo() seja chamado em N ms
        public Action CancelaSolta;
        public double Velocidade = 1.5;
        public uint ToleranciaMs = 300;
        public bool Pausado;
        public Estado Atual { get { return estado; } }

        class Contato { public bool Usado, Tip; public uint Id; public double X, Y; public uint Tick; }
        readonly Contato[] cont = new Contato[MaxContatos];
        Estado estado = Estado.Ocioso;
        double baseX, baseY, restoX, restoY;
        uint mascaraIds;

        public MotorArrasto() { for (int i = 0; i < cont.Length; i++) cont[i] = new Contato(); }

        public void Atualiza(uint id, double x, double y, bool tip, uint agora)
        {
            Contato livre = null;
            foreach (var c in cont)
            {
                if (c.Usado && c.Id == id)
                {
                    c.X = x; c.Y = y; c.Tip = tip; c.Tick = agora;
                    if (!tip) c.Usado = false;
                    return;
                }
                if (!c.Usado && livre == null) livre = c;
            }
            if (tip && livre != null) { livre.Usado = true; livre.Id = id; livre.X = x; livre.Y = y; livre.Tip = true; livre.Tick = agora; }
        }

        public void Avalia(uint agora)
        {
            int n = 0; double sx = 0, sy = 0; uint mascara = 0;
            foreach (var c in cont)
            {
                if (!c.Usado) continue;
                if (unchecked(agora - c.Tick) > ContatoVelhoMs) { c.Usado = false; continue; }
                n++; sx += c.X; sy += c.Y; mascara |= 1u << (int)(c.Id & 31);
            }

            if (n != 3 || Pausado)
            {
                if (estado == Estado.Armado) estado = Estado.Ocioso;
                else if (estado == Estado.Arrastando) { estado = Estado.Soltando; ArmaSolta(ToleranciaMs); }
                return;
            }

            double cx = sx / 3, cy = sy / 3;
            switch (estado)
            {
                case Estado.Ocioso:
                    estado = Estado.Armado;
                    baseX = cx; baseY = cy; restoX = restoY = 0; mascaraIds = mascara;
                    break;
                case Estado.Soltando:   // os dedos voltaram dentro da tolerância: continua o mesmo arrasto
                    CancelaSolta();
                    estado = Estado.Arrastando;
                    baseX = cx; baseY = cy; mascaraIds = mascara;
                    break;
                case Estado.Armado:
                    {
                        double dx = cx - baseX, dy = cy - baseY;
                        if (mascara != mascaraIds) { baseX = cx; baseY = cy; mascaraIds = mascara; break; }
                        if (dx * dx + dy * dy < LimiarInicioPx * LimiarInicioPx) break;
                        Botao(true);
                        estado = Estado.Arrastando;
                        Move(dx, dy);
                        baseX = cx; baseY = cy;
                        break;
                    }
                case Estado.Arrastando:
                    if (mascara == mascaraIds) Move(cx - baseX, cy - baseY);
                    baseX = cx; baseY = cy; mascaraIds = mascara;
                    break;
            }
        }

        void Move(double dx, double dy)
        {
            restoX += dx * Velocidade; restoY += dy * Velocidade;
            int mx = (int)restoX, my = (int)restoY;
            restoX -= mx; restoY -= my;
            if (mx != 0 || my != 0) Mover(mx, my);
        }

        // a tolerância acabou sem os dedos voltarem
        public void SoltarPorTempo() { if (estado == Estado.Soltando) Encerra(); }

        public void Encerra()
        {
            CancelaSolta();
            if (estado == Estado.Arrastando || estado == Estado.Soltando) Botao(false);
            estado = Estado.Ocioso;
        }
    }

    // um touchpad de precisão: descritor HID e a escala de unidades do aparelho para pixels
    class Dispositivo
    {
        public IntPtr Handle, Pp;                 // Pp = dados pré-analisados do HID (memória nativa)
        public Arrastar3Dedos.ValueCaps[] Caps;
        public double EscX, EscY;                 // pixels por unidade lógica do touchpad (antes da velocidade)
        public double LogicoX, LogicoY;
    }

    static class Arrastar3Dedos
    {
        // ---------- HID ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct ValueCaps   // HIDP_VALUE_CAPS (72 bytes)
        {
            public ushort UsagePage; public byte ReportID, IsAlias; public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
            public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
            public ushort BitSize, ReportCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
            public uint UnitsExp, Units; public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
            public ushort Usage, Usage2, U3, U4, U5, U6, U7, U8;   // união Range/NotRange: o primeiro é o Usage (ou UsageMin)
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Caps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }
        [StructLayout(LayoutKind.Sequential)] struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out Caps c);
        [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int tipo, [Out] ValueCaps[] c, ref ushort n, IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int tipo, ushort pagina, ushort link, ushort usage, out uint valor, IntPtr pp, byte[] rel, uint tam);
        [DllImport("hid.dll")] static extern int HidP_GetUsages(int tipo, ushort pagina, ushort link, [Out] ushort[] usages, ref uint n, IntPtr pp, byte[] rel, uint tam);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices([In] RawInputDevice[] d, uint n, uint tam);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr dados, ref uint tam, uint tamCabecalho);
        [DllImport("user32.dll")] static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, IntPtr dados, ref uint tam);

        const int HidpInput = 0, Sucesso = 0x00110000;
        const ushort PgDesktop = 0x01, PgDigitizer = 0x0D, UX = 0x30, UY = 0x31, UTip = 0x42, UContactId = 0x51, UContactN = 0x54;

        static string ArquivoConfig { get { return Path.Combine(Comum.Dir, "arrastar3dedos.txt"); } }

        // ---------- janela e thread ----------
        delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS { public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClass(ref WNDCLASS c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(int ex, string classe, string titulo, int estilo, int x, int y, int w, int h, IntPtr pai, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr cb);
        [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, UIntPtr id);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
        [DllImport("kernel32.dll")] static extern uint GetTickCount();
        const uint WM_INPUT = 0x00FF, WM_TIMER = 0x0113, WM_CLOSE = 0x0010, WM_DESTROY = 0x0002;
        static readonly UIntPtr IdTimer = new UIntPtr(1);
        static readonly WndProc proc = Proc;   // fica guardado: o Windows chama por ponteiro

        static IntPtr hwnd;
        static Thread thread;
        static MotorArrasto motor;
        static readonly System.Collections.Generic.List<Dispositivo> dispositivos = new System.Collections.Generic.List<Dispositivo>();
        static int restantes;

        static void Log(string m) { Comum.Log("arrastar3dedos.log", m); }

        public static void Iniciar()
        {
            if (thread != null) return;
            var m = new MotorArrasto();
            try
            {
                if (File.Exists(ArquivoConfig))
                    foreach (var linha in File.ReadAllLines(ArquivoConfig))
                    {
                        var p = linha.Split('=');
                        string k = p[0].Trim().ToLowerInvariant(), v = p.Length > 1 ? p[1].Trim() : "";
                        double d; int t;
                        if (k == "desligado") { Log("desligado em arrastar3dedos.txt"); return; }
                        if (k == "velocidade" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0) m.Velocidade = d;
                        if (k == "tolerancia" && int.TryParse(v, out t) && t >= 0) m.ToleranciaMs = (uint)t;
                    }
            }
            catch (Exception e) { Log("arrastar3dedos.txt: " + e.Message); }
            motor = m;
            var pronto = new ManualResetEvent(false);
            thread = new Thread(() => Laco(pronto)) { IsBackground = true, Name = "Arrastar3Dedos" };
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
                // coordenadas e tamanho de tela reais, mesmo com monitores de escalas diferentes
                try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
                motor.Botao = Saida.Botao;
                motor.Mover = Saida.Mover;
                motor.ArmaSolta = ms => SetTimer(hwnd, IdTimer, ms, IntPtr.Zero);
                motor.CancelaSolta = () => KillTimer(hwnd, IdTimer);

                var wc = new WNDCLASS { lpfnWndProc = proc, hInstance = GetModuleHandle(null), lpszClassName = "MacSkinArrastar3Dedos" };
                RegisterClass(ref wc);
                hwnd = CreateWindowEx(0, wc.lpszClassName, "Mac Skin Arrastar3Dedos", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { Log("janela não abriu: " + Marshal.GetLastWin32Error()); return; }

                // RIDEV_INPUTSINK (0x100): recebe o touchpad mesmo com outra janela em primeiro plano
                var rid = new[] { new RawInputDevice { UsagePage = PgDigitizer, Usage = 0x05, Flags = 0x100, Target = hwnd } };
                if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RawInputDevice)))) { Log("touchpad de precisão não registrou: " + Marshal.GetLastWin32Error()); DestroyWindow(hwnd); hwnd = IntPtr.Zero; return; }
                AppDomain.CurrentDomain.ProcessExit += delegate { Encerrar(); };
                Log("arrastar com 3 dedos ligado (velocidade " + motor.Velocidade.ToString(CultureInfo.InvariantCulture) + ", tolerância " + motor.ToleranciaMs + " ms)");
            }
            catch (Exception e) { Log("não iniciou: " + e); return; }
            finally { pronto.Set(); }

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        }

        // nunca deixa o botão do mouse apertado para trás
        static void Encerrar() { try { if (motor != null) motor.Encerra(); } catch { } }

        static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                switch (m)
                {
                    case WM_INPUT: Entrada(l); break;
                    case WM_TIMER: if (w == (IntPtr)1) motor.SoltarPorTempo(); return IntPtr.Zero;
                    case WM_CLOSE: DestroyWindow(h); return IntPtr.Zero;
                    case WM_DESTROY: Encerrar(); hwnd = IntPtr.Zero; PostQuitMessage(0); return IntPtr.Zero;
                }
            }
            catch (Exception e) { Log("mensagem " + m + ": " + e.Message); }
            return DefWindowProc(h, m, w, l);
        }

        // ---------- touchpad ----------
        static void Entrada(IntPtr hRaw)
        {
            uint tam = 0;
            GetRawInputData(hRaw, 0x10000003 /* RID_INPUT */, IntPtr.Zero, ref tam, (uint)CabecalhoRaw);
            if (tam == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)tam);
            try
            {
                if (GetRawInputData(hRaw, 0x10000003, buf, ref tam, (uint)CabecalhoRaw) == uint.MaxValue) return;
                if (Marshal.ReadInt32(buf) != 2 /* RIM_TYPEHID */) return;
                IntPtr dev = Marshal.ReadIntPtr(buf, 8);
                var d = Abrir(dev);
                if (d == null) return;
                uint agora = GetTickCount();
                Relatorios(d, buf + CabecalhoRaw, agora);
                motor.Avalia(agora);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static int CabecalhoRaw { get { return IntPtr.Size == 8 ? 24 : 16; } }   // RAWINPUTHEADER

        // RAWHID: dwSizeHid, dwCount e os relatórios um atrás do outro
        static void Relatorios(Dispositivo d, IntPtr hid, uint agora)
        {
            uint tamRel = (uint)Marshal.ReadInt32(hid, 0), qtd = (uint)Marshal.ReadInt32(hid, 4);
            for (uint i = 0; i < qtd; i++)
            {
                var rel = new byte[tamRel];
                Marshal.Copy(hid + 8 + (int)(i * tamRel), rel, 0, (int)tamRel);
                Relatorio(d, rel, agora, motor);
            }
        }

        // Dispositivo para o touchpad cujo handle veio no Raw Input (lê o descritor HID uma vez só)
        public static Dispositivo Abrir(IntPtr h)
        {
            foreach (var x in dispositivos) if (x.Handle == h) return x;

            uint tam = 0;
            if (GetRawInputDeviceInfo(h, 0x20000005 /* RIDI_PREPARSEDDATA */, IntPtr.Zero, ref tam) != 0 || tam == 0) return null;
            IntPtr pp = Marshal.AllocHGlobal((int)tam);
            if (GetRawInputDeviceInfo(h, 0x20000005, pp, ref tam) == uint.MaxValue) { Marshal.FreeHGlobal(pp); return null; }

            Caps caps;
            if (HidP_GetCaps(pp, out caps) != Sucesso) { Marshal.FreeHGlobal(pp); return null; }
            ushort n = caps.NumberInputValueCaps;
            var vc = new ValueCaps[Math.Max(n, (ushort)1)];
            if (HidP_GetValueCaps(HidpInput, vc, ref n, pp) != Sucesso) { Marshal.FreeHGlobal(pp); return null; }
            Array.Resize(ref vc, n);

            // faixas lógica e física de X/Y: converte em pixels mantendo a proporção real do touchpad
            double lx = 0, ly = 0, fx = 0, fy = 0;
            foreach (var c in vc)
            {
                if (c.UsagePage != PgDesktop) continue;
                if (c.Usage == UX && lx == 0) { lx = (double)c.LogicalMax - c.LogicalMin; fx = (double)c.PhysicalMax - c.PhysicalMin; }
                if (c.Usage == UY && ly == 0) { ly = (double)c.LogicalMax - c.LogicalMin; fy = (double)c.PhysicalMax - c.PhysicalMin; }
            }
            if (lx <= 0) lx = 4096;
            if (ly <= 0) ly = lx;

            var d = new Dispositivo { Handle = h, Pp = pp, Caps = vc, LogicoX = lx, LogicoY = ly };
            d.EscX = Saida.LarguraTela() / lx;   // largura inteira do touchpad = 1 tela (x velocidade)
            d.EscY = (fx > 0 && fy > 0) ? d.EscX / (fx / lx) * (fy / ly) : d.EscX;
            dispositivos.Add(d);
            Log("touchpad detectado: " + vc.Length + " valores, lógico " + lx + "x" + ly + ", físico " + fx + "x" + fy + ", escala " + d.EscX.ToString("0.####", CultureInfo.InvariantCulture) + "x" + d.EscY.ToString("0.####", CultureInfo.InvariantCulture));
            return d;
        }

        // um relatório HID: cada dedo (coleção de ligação com X) vira uma atualização do motor
        public static void Relatorio(Dispositivo d, byte[] rel, uint agora, MotorArrasto m)
        {
            uint qtd;
            // Contact Count só vem no 1º relatório de cada quadro (modo híbrido); nos demais vem 0
            if (HidP_GetUsageValue(HidpInput, PgDigitizer, 0, UContactN, out qtd, d.Pp, rel, (uint)rel.Length) == Sucesso)
            { if (qtd > 0) restantes = (int)qtd; }
            else restantes = MotorArrasto.MaxContatos;

            foreach (var c in d.Caps)
            {
                if (restantes <= 0) break;
                if (c.UsagePage != PgDesktop || c.Usage != UX) continue;
                ushort lk = c.LinkCollection;
                uint x, y, id;
                if (HidP_GetUsageValue(HidpInput, PgDesktop, lk, UX, out x, d.Pp, rel, (uint)rel.Length) != Sucesso) continue;
                if (HidP_GetUsageValue(HidpInput, PgDesktop, lk, UY, out y, d.Pp, rel, (uint)rel.Length) != Sucesso) continue;
                if (HidP_GetUsageValue(HidpInput, PgDigitizer, lk, UContactId, out id, d.Pp, rel, (uint)rel.Length) != Sucesso) id = lk;

                var us = new ushort[16]; uint nu = 16; bool tip = false;
                if (HidP_GetUsages(HidpInput, PgDigitizer, lk, us, ref nu, d.Pp, rel, (uint)rel.Length) == Sucesso)
                    for (uint i = 0; i < nu; i++) if (us[i] == UTip) tip = true;

                restantes--;
                m.Atualiza(id, x * d.EscX, y * d.EscY, tip, agora);
            }
        }

        // zera o contador de contatos entre quadros (usado pelos testes)
        public static void NovoQuadro() { restantes = 0; }

        // ---------- mouse ----------
        public static class Saida
        {
            [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int dx, dy; public uint mouseData, flags, time; public IntPtr extra; }
            [StructLayout(LayoutKind.Sequential)] struct Input { public uint tipo; public MouseInput mi; }   // INPUT de mouse: o union tem o tamanho do maior membro (o do mouse, no x64)
            [StructLayout(LayoutKind.Sequential)] struct Ponto { public int X, Y; }
            [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, Input[] i, int tam);
            [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
            [DllImport("user32.dll")] static extern bool GetCursorPos(out Ponto p);

            public static int LarguraTela() { return GetSystemMetrics(0 /* SM_CXSCREEN */); }

            public static void Botao(bool desce)
            {
                bool trocado = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0;
                uint f = desce ? (trocado ? 0x8u : 0x2u) : (trocado ? 0x10u : 0x4u);   // RIGHTDOWN/LEFTDOWN, RIGHTUP/LEFTUP
                Enviar(new Input { tipo = 0, mi = new MouseInput { flags = f } });
            }

            public static void Mover(int mx, int my)
            {
                Ponto p; GetCursorPos(out p);
                int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);   // tela virtual
                int x = Math.Max(vx, Math.Min(vx + vw - 1, p.X + mx)), y = Math.Max(vy, Math.Min(vy + vh - 1, p.Y + my));
                var mi = new MouseInput
                {
                    dx = (int)(((long)(x - vx) * 65536 + vw - 1) / vw), dy = (int)(((long)(y - vy) * 65536 + vh - 1) / vh),
                    flags = 0x0001 | 0x8000 | 0x4000   // MOVE | ABSOLUTE | VIRTUALDESK
                };
                Enviar(new Input { tipo = 0, mi = mi });
            }

            static void Enviar(Input i) { SendInput(1, new[] { i }, Marshal.SizeOf(typeof(Input))); }
        }
    }
}
