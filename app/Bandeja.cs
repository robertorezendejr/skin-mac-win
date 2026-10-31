// Mac Skin: ícones em segundo plano (a bandeja do Windows, ao lado do relógio, que fica oculta com a barra).
// O Mac Skin cria a sua própria janela "Shell_TrayWnd" na frente da do Explorer: o Shell_NotifyIcon dos apps procura a
// janela por essa classe, então os ícones chegam aqui. Tudo é repassado ao Explorer, que segue com a bandeja em dia
// (e com as reservas de tela das AppBars). A Central de Controle mostra os ícones; clique e botão direito vão para o
// app como se fossem na bandeja do Windows (o "Sair" do Teams, por exemplo).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MacSkin
{
    class IconeBandeja
    {
        public IntPtr Janela; public uint Id, Mensagem, Versao; public Guid Guid;
        public string Dica = ""; public ImageSource Img; public bool Oculto;
    }

    static class Bandeja
    {
        public static readonly List<IconeBandeja> Icones = new List<IconeBandeja>();
        public static event Action Mudou;
        public static Func<Win32.RECT> Ancora;   // onde está a Central de Controle (para o app que pergunta onde está o ícone)

        const int Assinatura = 0x34753423, WM_COPYDATA = 0x4A;
        static IntPtr hwnd; static int msgCriada; static DispatcherTimer vigia;
        static readonly WndProc proc = Proc;   // fica guardado: o Windows chama por ponteiro

        static void Log(string m) { Comum.Log("bandeja.log", m); }

        public static void Iniciar()
        {
            var wc = new WNDCLASS { lpfnWndProc = proc, hInstance = GetModuleHandle(null), lpszClassName = "Shell_TrayWnd" };
            if (RegisterClass(ref wc) == 0) { Log("classe não registrou: " + Marshal.GetLastWin32Error()); return; }
            hwnd = CreateWindowEx(Win32.EX_TOOL | 0x8, "Shell_TrayWnd", "", unchecked((int)0x80000000), 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) { Log("janela não abriu: " + Marshal.GetLastWin32Error()); return; }
            msgCriada = RegisterWindowMessage("TaskbarCreated");
            NaFrente();
            // se o Explorer reiniciar (ou passar na frente), os apps voltam a registrar no dele: retoma e pede de novo
            vigia = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            vigia.Tick += delegate { if (FindWindow("Shell_TrayWnd", null) != hwnd) { Log("Explorer passou na frente, retomando"); NaFrente(); } };
            vigia.Start();
            Log("bandeja aberta");
        }

        public static void Parar()
        {
            if (vigia != null) vigia.Stop();
            if (hwnd != IntPtr.Zero) { DestroyWindow(hwnd); hwnd = IntPtr.Zero; }
        }

        // fica antes do Explorer na ordem das janelas e pede aos apps que registrem os ícones de novo (como quando o
        // Explorer reinicia)
        static void NaFrente()
        {
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            SendNotifyMessage(new IntPtr(0xFFFF), msgCriada, IntPtr.Zero, IntPtr.Zero);
        }

        // a reserva de tela da dock e da barra (SHAppBarMessage do próprio processo) não sobrevive ao repasse: durante
        // ela, esta janela vai para trás e o Windows fala direto com o Explorer
        public static void Direto(Action a)
        {
            if (hwnd == IntPtr.Zero) { a(); return; }
            SetWindowPos(hwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            try { a(); }
            finally { SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
        }

        static IntPtr Explorer()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
                if (h != hwnd) return h;
            return IntPtr.Zero;
        }

        static IntPtr Repassar(int m, IntPtr w, IntPtr l)
        {
            IntPtr e = Explorer(), r = IntPtr.Zero;
            if (e != IntPtr.Zero) SendMessageTimeout(e, m, w, l, 2, 1500, out r);
            return r;
        }

        static IntPtr Proc(IntPtr h, int m, IntPtr w, IntPtr l)
        {
            try
            {
                if (m == WM_COPYDATA) return Copia(w, l);
                // o resto que os apps mandam para a barra do Windows (comandos, mensagens próprias) vai para o Explorer
                if (m == 0x111 || (m >= 0x400 && m < 0x8000)) return Repassar(m, w, l);
            }
            catch (Exception e) { Log("mensagem " + m + ": " + e.Message); }
            return DefWindowProc(h, m, w, l);
        }

        // WM_COPYDATA: 0 = AppBar (dock e barra reservando a tela), 1 = ícone da bandeja, 3 = onde está o ícone
        static IntPtr Copia(IntPtr w, IntPtr l)
        {
            var cds = (COPYDATA)Marshal.PtrToStructure(l, typeof(COPYDATA));
            long tipo = cds.Tipo.ToInt64();
            if (tipo == 1 && cds.Tamanho >= 8 + 24 && Marshal.ReadInt32(cds.Dados) == Assinatura)
            {
                bool ok = Notificar(cds.Dados, cds.Tamanho);
                Repassar(WM_COPYDATA, w, l);
                return ok ? new IntPtr(1) : IntPtr.Zero;
            }
            if (tipo == 3 && cds.Tamanho >= 8 && Marshal.ReadInt32(cds.Dados) == Assinatura && Ancora != null)
            {
                var r = Ancora();
                return Marshal.ReadInt32(cds.Dados, 4) == 1 ? Ponto(r.L, r.T) : Ponto(r.R, r.B);
            }
            return Repassar(WM_COPYDATA, w, l);
        }
        static IntPtr Ponto(int x, int y) { return new IntPtr((y << 16) | (x & 0xFFFF)); }

        // NOTIFYICONDATA (formato de 32 bits, igual para todo app) logo depois do cabeçalho de 8 bytes
        static bool Notificar(IntPtr dados, int tamanho)
        {
            int msg = Marshal.ReadInt32(dados, 4);
            IntPtr d = dados + 8;
            var janela = new IntPtr(Marshal.ReadInt32(d, 4));
            uint id = (uint)Marshal.ReadInt32(d, 8), flags = (uint)Marshal.ReadInt32(d, 12);
            Guid g = Guid.Empty;
            if ((flags & 0x20) != 0 && tamanho >= 8 + 952) { var b = new byte[16]; Marshal.Copy(d + 936, b, 0, 16); g = new Guid(b); }
            var ic = Icones.Find(x => g != Guid.Empty ? x.Guid == g : x.Janela == janela && x.Id == id);

            bool ok = true;
            switch (msg)
            {
                case 0:   // NIM_ADD
                case 1:   // NIM_MODIFY (sem o ADD antes, só vale se trouxer ícone)
                    if (ic == null)
                    {
                        if (msg == 1 && (flags & 2) == 0) return false;
                        ic = new IconeBandeja(); Icones.Add(ic);
                    }
                    ic.Janela = janela; ic.Id = id; if (g != Guid.Empty) ic.Guid = g;
                    if ((flags & 1) != 0) ic.Mensagem = (uint)Marshal.ReadInt32(d, 16);
                    if ((flags & 2) != 0) { var img = Imagem(new IntPtr(Marshal.ReadInt32(d, 20))); if (img != null) ic.Img = img; }
                    if ((flags & 4) != 0) { var t = Marshal.PtrToStringUni(d + 24, 128); int z = t.IndexOf('\0'); ic.Dica = z < 0 ? t : t.Substring(0, z); }
                    if ((flags & 8) != 0 && (Marshal.ReadInt32(d, 284) & 1) != 0) ic.Oculto = (Marshal.ReadInt32(d, 280) & 1) != 0;
                    break;
                case 2: ok = ic != null; if (ic != null) Icones.Remove(ic); break;   // NIM_DELETE
                case 4: ok = ic != null; if (ic != null) ic.Versao = (uint)Marshal.ReadInt32(d, 800); break;   // NIM_SETVERSION
            }
            if (Mudou != null) Mudou();
            return ok;
        }

        static ImageSource Imagem(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return null;
            try { var s = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); s.Freeze(); return s; }
            catch { return null; }
        }

        // apps que fecharam sem tirar o ícone
        public static void Limpar() { Icones.RemoveAll(x => !IsWindow(x.Janela)); }

        // o mesmo que a bandeja do Windows manda ao app (x, y = ponto na tela, em pixels, para o menu abrir ali)
        public static void Clicar(IconeBandeja ic, bool direito, bool duplo, int x, int y)
        {
            if (ic.Mensagem == 0) return;
            AllowSetForegroundWindow(Win32.Pid(ic.Janela));   // para o menu/janela do app vir para a frente
            Action<int> env = ev =>
            {
                IntPtr wp = ic.Versao >= 4 ? Ponto(x, y) : new IntPtr(ic.Id);
                IntPtr lp = ic.Versao >= 4 ? new IntPtr(unchecked((int)((ic.Id << 16) | (uint)ev))) : new IntPtr(ev);
                SendNotifyMessage(ic.Janela, (int)ic.Mensagem, wp, lp);
            };
            if (duplo) { env(0x203); env(0x202); return; }
            if (direito) { env(0x204); env(0x205); if (ic.Versao >= 3) env(0x7B); }   // WM_CONTEXTMENU
            else { env(0x201); env(0x202); if (ic.Versao >= 3) env(0x400); }        // NIN_SELECT
        }

        // ---------- Win32 ----------
        delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS { public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; }
        [StructLayout(LayoutKind.Sequential)] struct COPYDATA { public IntPtr Tipo; public int Tamanho; public IntPtr Dados; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClass(ref WNDCLASS c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(int ex, string classe, string titulo, int estilo, int x, int y, int w, int h, IntPtr pai, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string c, string t);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll")] static extern bool SendNotifyMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int m, IntPtr w, IntPtr l, int f, int ms, out IntPtr r);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
    }

    // a caixa da Central de Controle: vidro do Windows, ícones em grade e o atalho para as configurações rápidas
    class PainelBandeja
    {
        const double Largura = 248;
        readonly Window jan, vidro;   // vidro: janela acrylic por trás (desfoca o fundo); jan: o conteúdo transparente por cima
        IntPtr hVidro;
        readonly Border aro, fundo;
        readonly WrapPanel grade;
        readonly TextBlock titulo, vazio, rapidas;
        readonly Border sep, itemRapidas;
        IntPtr h; bool? claro;
        // depois de um clique num ícone o app abre o menu/janela dele e a caixa perde o foco: ela continua aberta
        // até um clique fora dela e fora desse app (ou outro app vir para a frente)
        uint segurando; DispatcherTimer vigiaClique;
        Brush texto = Brushes.White, sobre = Comum.Cor("#26FFFFFF");
        static readonly Brush Nada = Comum.Cor("#01FFFFFF");
        static readonly FontFamily Fonte = new FontFamily("Inter, Segoe UI");

        public PainelBandeja()
        {
            jan = new Window
            {
                Title = "Mac Skin Bandeja", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                AllowsTransparency = true, Background = Brushes.Transparent, Width = Largura, SizeToContent = SizeToContent.Height, Left = -10000, Top = -10000
            };
            vidro = new Window
            {
                Title = "Mac Skin Bandeja vidro", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                Topmost = true, Background = Brushes.Transparent, Left = -10000, Top = -10000, Width = 10, Height = 10, ShowActivated = false
            };
            vidro.SourceInitialized += delegate
            {
                hVidro = new WindowInteropHelper(vidro).Handle;
                HwndSource.FromHwnd(hVidro).CompositionTarget.BackgroundColor = Colors.Transparent;
                Win32.Estilo(hVidro, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
            };
            aro = new Border { CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(Recuo), IsHitTestVisible = false };
            fundo = new Border { CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(1) };
            var pilha = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            titulo = new TextBlock { Text = "Em segundo plano", FontFamily = Fonte, FontSize = 12, FontWeight = FontWeights.SemiBold, Opacity = 0.6, Margin = new Thickness(4, 0, 0, 6) };
            grade = new WrapPanel();
            vazio = new TextBlock { Text = "Nenhum app em segundo plano", FontFamily = Fonte, FontSize = 12, Opacity = 0.6, Margin = new Thickness(4, 2, 0, 4) };
            sep = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 6) };
            rapidas = new TextBlock { Text = "Configurações rápidas…", FontFamily = Fonte, FontSize = 13 };
            itemRapidas = new Border { Child = rapidas, Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(5), Background = Nada };
            itemRapidas.MouseEnter += delegate { itemRapidas.Background = sobre; };
            itemRapidas.MouseLeave += delegate { itemRapidas.Background = Nada; };
            itemRapidas.MouseLeftButtonUp += delegate { jan.Hide(); Win32.Win(0x41); };
            pilha.Children.Add(titulo); pilha.Children.Add(grade); pilha.Children.Add(vazio); pilha.Children.Add(sep); pilha.Children.Add(itemRapidas);
            fundo.Child = pilha;
            var raiz = new Grid(); raiz.Children.Add(aro); raiz.Children.Add(fundo);
            jan.Content = raiz;
            jan.SizeChanged += delegate { PosicionarVidro(); };
            jan.IsVisibleChanged += delegate { if (!jan.IsVisible) vidro.Hide(); };

            jan.SourceInitialized += delegate
            {
                h = new WindowInteropHelper(jan).Handle;
                HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
                Win32.Estilo(h, Win32.EX_TOOL);
            };
            jan.Deactivated += delegate { if (segurando == 0) jan.Hide(); };   // clicou fora
            jan.Activated += delegate { Soltar(); };
            vigiaClique = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            vigiaClique.Tick += delegate { Vigiar(); };
            Bandeja.Mudou += delegate { if (jan.IsVisible) Montar(); };
        }

        // canto = canto de baixo à direita do item da barra, em DIPs
        public void Alternar(Point canto)
        {
            if (jan.IsVisible) { Esconder(); return; }
            Bandeja.Limpar();
            Montar();
            vidro.Show();
            if (jan.Owner == null) jan.Owner = vidro;   // o conteúdo fica sempre na frente do vidro
            claro = null; Tema();
            jan.Left = Math.Max(8, Math.Min(canto.X, SystemParameters.PrimaryScreenWidth - 8) - Largura);
            jan.Top = canto.Y + 5;
            jan.Show(); jan.UpdateLayout(); PosicionarVidro(); jan.Activate();
        }

        const double Raio = 16, Recuo = 4;
        void PosicionarVidro()
        {
            if (hVidro == IntPtr.Zero || !jan.IsVisible) return;
            var fonte = PresentationSource.FromVisual(jan);
            if (fonte == null) return;
            double f = fonte.CompositionTarget.TransformToDevice.M22;
            Win32.Mover(hVidro, (int)((jan.Left + Recuo) * f), (int)((jan.Top + Recuo) * f), (int)((jan.ActualWidth - 2 * Recuo) * f), (int)((jan.ActualHeight - 2 * Recuo) * f));
        }

        void Esconder() { Soltar(); jan.Hide(); }
        void Soltar() { segurando = 0; vigiaClique.Stop(); }

        void Vigiar()
        {
            if (!jan.IsVisible) { Soltar(); return; }
            uint meu = (uint)Process.GetCurrentProcess().Id;
            uint frente = Win32.Pid(Win32.GetForegroundWindow());
            if (frente != meu && frente != segurando && frente != 0) { Esconder(); return; }
            if ((GetAsyncKeyState(1) & 0x8000) == 0 && (GetAsyncKeyState(2) & 0x8000) == 0) return;
            Win32.POINT p; Win32.GetCursorPos(out p);
            IntPtr sob = Win32.GetAncestor(Win32.WindowFromPoint(p), 2);   // GA_ROOT
            if (sob != h && Win32.Pid(sob) != segurando) Esconder();
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        void Tema()
        {
            bool c = Comum.TemaClaro();
            if (c == claro) return;
            claro = c;
            // mesmo vidro do volume e dos menus: acrylic leve por trás, brilho no alto e borda de luz
            Win32.Vidro(hVidro, c ? 0x1EF8F6F6 : 0x24383030, true);
            aro.BorderBrush = Comum.Cor(c ? "#8CEDEDEF" : "#8C3A3A3E");
            fundo.Background = c ? Comum.Degrade("#55FFFFFF", "#14FFFFFF") : Comum.Degrade("#40FFFFFF", "#0EFFFFFF");
            fundo.BorderBrush = c ? Comum.Degrade("#F2FFFFFF", "#66FFFFFF") : Comum.Degrade("#B3FFFFFF", "#33FFFFFF");
            texto = c ? Comum.Cor("#E6000000") : Brushes.White;
            sobre = c ? Comum.Cor("#1A000000") : Comum.Cor("#26FFFFFF");
            jan.Foreground = texto;
            titulo.Foreground = vazio.Foreground = rapidas.Foreground = texto;
            sep.Background = c ? Comum.Cor("#1F000000") : Comum.Cor("#26FFFFFF");
            Montar();
        }

        void Montar()
        {
            grade.Children.Clear();
            foreach (var ic in Bandeja.Icones)
            {
                if (ic.Oculto || ic.Img == null) continue;
                var icone = ic;
                var img = new Image { Source = ic.Img, Width = 20, Height = 20 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var b = new Border { Width = 44, Height = 40, CornerRadius = new CornerRadius(7), Background = Nada, Child = img };
                string dica = ic.Dica.Length > 0 ? ic.Dica : Nome(ic);
                if (dica.Length > 0) b.ToolTip = dica;
                b.MouseEnter += delegate { b.Background = sobre; };
                b.MouseLeave += delegate { b.Background = Nada; };
                b.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) Clicar(b, icone, false, true); };
                b.MouseLeftButtonUp += delegate { Clicar(b, icone, false, false); };
                b.MouseRightButtonUp += (s, e) => { e.Handled = true; Clicar(b, icone, true, false); };
                b.MouseRightButtonDown += (s, e) => e.Handled = true;
                grade.Children.Add(b);
            }
            vazio.Visibility = grade.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Clicar(Border b, IconeBandeja ic, bool direito, bool duplo)
        {
            segurando = Win32.Pid(ic.Janela);
            vigiaClique.Start();
            var p = b.PointToScreen(new Point(b.ActualWidth / 2, b.ActualHeight / 2));
            Bandeja.Clicar(ic, direito, duplo, (int)p.X, (int)p.Y);
        }

        static string Nome(IconeBandeja ic)
        {
            try { return Process.GetProcessById((int)Win32.Pid(ic.Janela)).MainModule.FileVersionInfo.FileDescription ?? ""; }
            catch { return ""; }
        }
    }
}
