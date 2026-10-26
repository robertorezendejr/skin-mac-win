// Mac Skin: barra de menus no alto, como no macOS Golden Gate: sem fundo, itens brancos sobre o papel de parede.
// Esquerda: menu do sistema e o app ativo. Direita: volume, rede, bateria, Bluetooth, Central de Controle (com os apps
// em segundo plano) e relógio.
// Reserva a faixa de cima (AppBar) e some em tela cheia.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MacSkin
{
    class Barra
    {
        const double Altura = 28;
        static readonly FontFamily Icones = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily Texto = new FontFamily("Inter, Segoe UI");
        static readonly CultureInfo PtBr = new CultureInfo("pt-BR");
        static readonly Brush Branco = Brushes.White;
        static readonly Brush Clicavel = Comum.Cor("#01FFFFFF");   // quase invisível: sem isso o clique atravessa a barra transparente

        public Window Janela;
        IntPtr hwnd; int telaW, telaH;
        StackPanel esq, dir;
        TextBlock appAtivo, relogio, wifi, batTxt;
        Border batNivel, itemBat, central;
        Path batRaio;
        ContextMenu menuSis;
        readonly Dictionary<uint, string> nomes = new Dictionary<uint, string>();
        int wifiGlifo = 0xE701;
        Canvas wifiDesenho; Path[] wifiPecas;

        static void Log(string m) { Comum.Log("barra.log", m); }

        public Barra()
        {
            Janela = new Window
            {
                Title = "Mac Skin Barra", WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, Topmost = true, Background = Brushes.Transparent, ShowActivated = false,
                Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = Altura
            };
            var raiz = new Grid { Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.45 } };
            Janela.Content = raiz;
            esq = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, 0) };
            dir = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
            raiz.Children.Add(esq); raiz.Children.Add(dir);

            // ---------- esquerda: logo (menu do sistema) e app ativo ----------
            var logo = new Path
            {
                Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Fill = Branco,
                Data = Geometry.Parse("M0,0 H6 V6 H0 Z M7,0 H13 V6 H7 Z M0,7 H6 V13 H0 Z M7,7 H13 V13 H7 Z")
            };
            menuSis = new ContextMenu();
            Opcao("Sobre este computador", () => Abrir("ms-settings:about"));
            Opcao(null, null);
            Opcao("Configurações do Sistema…", () => Abrir("ms-settings:"));
            Opcao("App Store…", () => Abrir("ms-windows-store:"));
            Opcao(null, null);
            Opcao("Forçar Encerrar…", () => Abrir("taskmgr.exe"));
            Opcao(null, null);
            Opcao("Suspender", () => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false));
            Opcao("Reiniciar…", () => { if (Confirmar("Reiniciar o computador agora?")) Abrir("shutdown.exe", "/r /t 0"); });
            Opcao("Desligar…", () => { if (Confirmar("Desligar o computador agora?")) Abrir("shutdown.exe", "/s /t 0"); });
            Opcao(null, null);
            Opcao("Bloquear Tela", () => Abrir("rundll32.exe", "user32.dll,LockWorkStation"));
            Opcao("Encerrar Sessão de " + Environment.UserName + "…", () => { if (Confirmar("Encerrar a sessão agora? Apps abertos serão fechados.")) Abrir("shutdown.exe", "/l"); });
            // a barra não pega foco; para clicar fora fechar o menu, ela pega o foco enquanto o menu está aberto
            menuSis.Opened += delegate { Win32.SetForegroundWindow(hwnd); };
            Janela.Deactivated += delegate { menuSis.IsOpen = false; };
            Item(esq, logo, s => { menuSis.PlacementTarget = s; menuSis.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menuSis.IsOpen = true; });
            appAtivo = Rotulo("", FontWeights.SemiBold);   // não exibido: só o logo fica à esquerda

            // ---------- direita (na ordem do Mac) ----------
            // volume do Mac: alto-falante cheio e dois arcos grossos de pontas redondas
            // arcos centrados em (8;6,5), raios 3,6 e 7,2: o mesmo espaço entre eles
            var volDesenho = new Canvas { Width = 17, Height = 13, VerticalAlignment = VerticalAlignment.Center };
            volDesenho.Children.Add(PecaWifi("M1.5,4.8 H4.2 L8,1.6 V11.4 L4.2,8.2 H1.5 Z", true));
            volDesenho.Children.Add(PecaWifi("M10.55,3.95 A3.6,3.6 0 0 1 10.55,9.05", false));
            volDesenho.Children.Add(PecaWifi("M13.09,1.41 A7.2,7.2 0 0 1 13.09,11.59", false));
            Item(dir, volDesenho, s => Win32.Win(0x41));    // volume
            wifi = Glifo(0xE701, 15);   // só para cabo / sem rede; o Wi-Fi é desenhado abaixo
            // Wi-Fi do Mac: cunha embaixo e dois arcos grossos de pontas redondas; o que passa do sinal fica apagado
            // centro na ponta da cunha (10;13,4); raios 4, 8 e 12: o mesmo espaço entre eles
            wifiDesenho = new Canvas { Width = 20, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            wifiPecas = new[]
            {
                PecaWifi("M10,13.4 L7.17,10.57 A4,4 0 0 1 12.83,10.57 Z", true),
                PecaWifi("M4.34,7.74 A8,8 0 0 1 15.66,7.74", false),
                PecaWifi("M1.52,4.92 A12,12 0 0 1 18.48,4.92", false)
            };
            foreach (var p in wifiPecas) wifiDesenho.Children.Add(p);
            var wifiBox = new Grid(); wifiBox.Children.Add(wifiDesenho); wifiBox.Children.Add(wifi);
            Item(dir, wifiBox, s => Win32.Win(0x41));
            // bateria do Mac: pílula branca que enche com a carga, número dentro e o "pino" à direita
            var corpo = new Grid { Width = 25, Height = 12, VerticalAlignment = VerticalAlignment.Center };
            corpo.Children.Add(new Border { CornerRadius = new CornerRadius(3.5), Background = Comum.Cor("#59FFFFFF") });
            batNivel = new Border { CornerRadius = new CornerRadius(3.5), Background = Branco, HorizontalAlignment = HorizontalAlignment.Left };
            corpo.Children.Add(batNivel);
            batTxt = new TextBlock { FontFamily = Texto, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Comum.Cor("#E6000000") };
            corpo.Children.Add(batTxt);
            var bat = new StackPanel { Orientation = Orientation.Horizontal };
            batRaio = Desenho("M6,0 L0,8 H4 L3,14 L9,5.5 H5 Z", 7, 11, true); batRaio.StrokeThickness = 0; batRaio.Margin = new Thickness(0, 0, 4, 0);
            bat.Children.Add(batRaio); bat.Children.Add(corpo);
            bat.Children.Add(new Border { Width = 1.5, Height = 4, Margin = new Thickness(1, 0, 0, 0), CornerRadius = new CornerRadius(1), Background = Comum.Cor("#80FFFFFF"), VerticalAlignment = VerticalAlignment.Center });
            itemBat = Item(dir, bat, s => Abrir("ms-settings:batterysaver"));
            // Bluetooth em traço grosso de pontas redondas, no mesmo peso dos arcos do volume e do Wi-Fi
            var bt = Desenho("M0.5,3.8 L7.5,10.2 L4,13.2 V0.8 L7.5,3.8 L0.5,10.2", 9, 15, false);
            bt.StrokeThickness = 2.2; bt.StrokeStartLineCap = PenLineCap.Round; bt.StrokeEndLineCap = PenLineCap.Round; bt.StrokeLineJoin = PenLineJoin.Round;
            Item(dir, bt, s => Abrir("ms-settings:bluetooth"));
            // Central de Controle: dois interruptores; abre a caixa dos apps em segundo plano (a bandeja do Windows)
            var painel = new PainelBandeja();
            // como no Mac: o de cima é contorno com a bolinha branca; o de baixo é pílula branca cheia com a bolinha escura
            var cc = new Canvas { Width = 14, Height = 13, VerticalAlignment = VerticalAlignment.Center };
            cc.Children.Add(new Path { Data = Geometry.Parse("M3,0.75 H11 A2.25,2.25 0 0 1 11,5.25 H3 A2.25,2.25 0 0 1 3,0.75 Z"), Stroke = Branco, StrokeThickness = 1.5 });
            cc.Children.Add(new Path { Data = Geometry.Parse("M1.5,3 A1.5,1.5 0 1 0 4.5,3 A1.5,1.5 0 1 0 1.5,3 Z"), Fill = Branco });
            cc.Children.Add(new Path { Data = Geometry.Parse("M3,7.75 H11 A2.25,2.25 0 0 1 11,12.25 H3 A2.25,2.25 0 0 1 3,7.75 Z"), Fill = Branco });
            cc.Children.Add(new Path { Data = Geometry.Parse("M9.5,10 A1.5,1.5 0 1 0 12.5,10 A1.5,1.5 0 1 0 9.5,10 Z"), Fill = Comum.Cor("#E6000000") });
            central = Item(dir, cc,
                s => { var b = (FrameworkElement)s; painel.Alternar(new Point(Janela.Left + b.TranslatePoint(new Point(b.ActualWidth, 0), Janela).X, Altura)); });
            Bandeja.Ancora = () =>
            {
                var a = central.PointToScreen(new Point(0, 0)); var z = central.PointToScreen(new Point(central.ActualWidth, central.ActualHeight));
                return new Win32.RECT { L = (int)a.X, T = (int)a.Y, R = (int)z.X, B = (int)z.Y };
            };
            relogio = Rotulo("", FontWeights.Medium);
            Item(dir, relogio, s => Win32.Win(0x4E));

            Janela.SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(Janela).Handle;
                Win32.Estilo(hwnd, Win32.EX_TOOL | Win32.EX_NOACTIVATE);
                double f = PresentationSource.FromVisual(Janela).CompositionTarget.TransformToDevice.M22;
                telaW = (int)(SystemParameters.PrimaryScreenWidth * f);
                telaH = (int)(SystemParameters.PrimaryScreenHeight * f);
                Win32.Reservar(hwnd, 1, telaW, telaH, (int)(Altura * f));
                Janela.Top = 0; Janela.Left = 0;
                AtualizarRapido();
            };
            Janela.Closed += delegate { Fechar(); };

            var t1 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t1.Tick += delegate { AtualizarRapido(); }; t1.Start();
            // Wi-Fi a cada 15 s, numa thread à parte (o netsh leva ~0,1 s e não pode travar a barra)
            var t2 = new Timer(delegate { LerWifi(); }, null, 0, 15000);
            GC.KeepAlive(t2); timerWifi = t2;
        }
        Timer timerWifi;

        public void Mostrar() { Janela.Show(); Log("barra aberta"); }
        public void Fechar() { if (hwnd != IntPtr.Zero) { Win32.Liberar(hwnd); hwnd = IntPtr.Zero; } }

        // ---------- peças ----------
        // item clicável: fundo arredondado ao passar o mouse, como os itens da barra do Mac
        Border Item(Panel painel, UIElement conteudo, Action<UIElement> acao)
        {
            var b = new Border { Child = conteudo, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(1, 3, 1, 3), CornerRadius = new CornerRadius(5), Background = Clicavel };
            b.MouseEnter += delegate { b.Background = Comum.Cor("#33FFFFFF"); };
            b.MouseLeave += delegate { b.Background = Clicavel; };
            if (acao != null) b.MouseLeftButtonUp += delegate { acao(b); };
            painel.Children.Add(b);
            return b;
        }
        static Path PecaWifi(string geo, bool cunha)
        {
            var p = new Path { Data = Geometry.Parse(geo), Stroke = Branco, StrokeThickness = cunha ? 1.2 : 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            if (cunha) p.Fill = Branco;
            return p;
        }
        static TextBlock Rotulo(string txt, FontWeight peso) { return new TextBlock { Text = txt, FontFamily = Texto, FontSize = 13, FontWeight = peso, VerticalAlignment = VerticalAlignment.Center, Foreground = Branco }; }
        static TextBlock Glifo(int cod, double tam) { return new TextBlock { Text = ((char)cod).ToString(), FontFamily = Icones, FontSize = tam, VerticalAlignment = VerticalAlignment.Center, Foreground = Branco }; }
        // desenho em traço branco (ícones que a fonte do Windows não tem igual ao Mac)
        static Path Desenho(string geo, double l, double a, bool preenchido)
        {
            var p = new Path { Data = Geometry.Parse(geo), Width = l, Height = a, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, StrokeThickness = 1.5, Stroke = Branco };
            if (preenchido) p.Fill = Branco;
            return p;
        }
        void Opcao(string nome, Action acao)
        {
            if (nome == null) { menuSis.Items.Add(new Separator()); return; }
            var mi = new MenuItem { Header = nome };
            mi.Click += delegate { acao(); };
            menuSis.Items.Add(mi);
        }
        static bool Confirmar(string msg) { return MessageBox.Show(msg, "Mac Skin", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
        static void Abrir(string o, string args = "") { try { Process.Start(o, args); } catch (Exception e) { Log("abrir " + o + ": " + e.Message); } }

        // ---------- atualização ----------
        // nome do app ativo: descrição do executável (com cache por processo)
        string NomeApp(IntPtr h)
        {
            string c = Win32.Classe(h);
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return "Explorador de Arquivos";
            uint id = Win32.Pid(h);
            string n;
            if (nomes.TryGetValue(id, out n)) return n;
            try
            {
                var p = Process.GetProcessById((int)id);
                if (p.ProcessName == "ApplicationFrameHost") { var t = Regex.Split(Win32.Titulo(h), " [-–] "); return t[t.Length - 1]; }   // apps da Store
                if (p.ProcessName == "explorer") n = "Explorador de Arquivos";
                else if (p.ProcessName == "MacSkin") n = "Finder";
                else { var d = p.MainModule.FileVersionInfo.FileDescription; n = string.IsNullOrEmpty(d) ? p.ProcessName : d; }
            }
            catch { n = ""; }
            nomes[id] = n;
            return n;
        }

        void AtualizarRapido()   // 1x por segundo: relógio, app ativo, bateria, Wi-Fi lido em segundo plano, tela cheia
        {
            try
            {
                string t = DateTime.Now.ToString("ddd'.' d 'de' MMM'.'  HH:mm", PtBr);
                relogio.Text = char.ToUpper(t[0]) + t.Substring(1);   // "Seg. 19 de out.  17:10"
                IntPtr h = Win32.GetForegroundWindow();
                if (h != hwnd && !Win32.Titulo(h).StartsWith("Mac Skin")) appAtivo.Text = NomeApp(h);

                Win32.ENERGIA e;
                if (Win32.GetSystemPowerStatus(out e) && (e.Flags & 128) == 0 && e.Percent <= 100)
                {
                    batTxt.Text = e.Percent.ToString();
                    batNivel.Width = Math.Max(3, 25 * e.Percent / 100.0);
                    batRaio.Visibility = e.AC == 1 ? Visibility.Visible : Visibility.Collapsed;
                    itemBat.Visibility = Visibility.Visible;
                }
                else itemBat.Visibility = Visibility.Collapsed;   // sem bateria (desktop)

                int forca = wifiGlifo == 0xE701 ? 3 : wifiGlifo == 0xE874 ? 2 : wifiGlifo == 0xE873 ? 1 : wifiGlifo == 0xE872 ? 0 : -1;
                wifiDesenho.Visibility = forca >= 0 ? Visibility.Visible : Visibility.Collapsed;
                wifi.Visibility = forca >= 0 ? Visibility.Collapsed : Visibility.Visible;
                if (forca >= 0) for (int i = 0; i < 3; i++) wifiPecas[i].Opacity = i <= forca - 1 || i == 0 ? 1 : 0.35;
                else wifi.Text = ((char)wifiGlifo).ToString();
                Janela.Visibility = Win32.TelaCheia(hwnd, telaW, telaH) ? Visibility.Hidden : Visibility.Visible;
            }
            catch (Exception ex) { Log("rápido: " + ex.Message); }
        }

        void LerWifi()
        {
            try
            {
                if (!NetworkInterface.GetAllNetworkInterfaces().Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up))
                {
                    wifiGlifo = NetworkInterface.GetIsNetworkAvailable() ? 0xE839 : 0xF384;   // cabo / sem rede
                    return;
                }
                var psi = new ProcessStartInfo("netsh.exe", "wlan show interfaces") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                string saida;
                using (var p = Process.Start(psi)) { saida = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); }
                var m = Regex.Match(saida, @"^\s*(Sinal|Signal)\s*:\s*(\d+)%", RegexOptions.Multiline);
                if (m.Success)
                {
                    int pct = int.Parse(m.Groups[2].Value);
                    wifiGlifo = pct >= 75 ? 0xE701 : pct >= 50 ? 0xE874 : pct >= 25 ? 0xE873 : 0xE872;
                }
                // o netsh só mostra o sinal com a Localização ligada no Windows; sem ela, um Wi-Fi conectado
                // ainda aparece aqui, só que sem a força do sinal (fica cheio)
                else if (NetworkInterface.GetAllNetworkInterfaces().Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up))
                    wifiGlifo = 0xE701;
                else wifiGlifo = NetworkInterface.GetIsNetworkAvailable() ? 0xE839 : 0xF384;   // cabo / sem rede
            }
            catch (Exception ex) { Log("wifi: " + ex.Message); }
        }
    }
}
