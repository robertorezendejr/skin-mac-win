// Mac Skin Dock: dock no estilo macOS. Itens = atalhos em .\dock (ordem pelo nome).
// Reserva a borda de baixo (AppBar), aumenta os ícones sob o mouse, pula ao abrir, marca os apps abertos,
// some em tela cheia e esconde a barra do Windows. Vidro = janela acrylic atrás (Liquid Glass).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MacSkin
{
    class Dock
    {
        // medidas em DIPs
        const double Base = 52, Gap = 6, Pad = 8, Margem = 6, Max = 1.7, Raio = 22;
        const double Recuo = 5;   // o vidro fica 5 para dentro: com o canto de 8 px do Windows ele cabe na curva de 22
        const double LarguraSep = 13;
        static readonly double Sigma = (Base + Gap) * 1.1, Altura = Base + 2 * Pad;
        static readonly double AlturaJanela = Math.Ceiling(Base * Max) + 2 * Pad + Margem + 48;

        class Item
        {
            public Grid Caixa; public Image Img; public Ellipse Ponto; public TranslateTransform Pulo;
            public string Nome, Abrir, Exe; public double Centro, Atual = 1, Alvo = 1, Largura = Base + Gap;
        }

        readonly string pasta = System.IO.Path.Combine(Comum.Dir, "dock");
        readonly string lixo = System.IO.Path.Combine(Comum.Dir, @"icones\TrashIcon.ico");
        readonly string lixoCheio = System.IO.Path.Combine(Comum.Dir, @"icones\FullTrashIcon.ico");

        public Window Janela;
        Window vidro;
        Grid raiz, dock;
        Border fundo, aro, sep, rotulo;
        TextBlock texto;
        StackPanel linha;
        readonly List<Item> els = new List<Item>();
        Item lixeira, sobre, arrastando;
        double arrastoX0; bool arrastoAtivo;
        IntPtr hwnd, hVidro; double f = 1; int telaW, telaH;
        bool? claro; bool? cheia; bool mostrarBarra;
        string rVidro;
        readonly Dictionary<uint, string> nomesPid = new Dictionary<uint, string>();

        static void Log(string m) { Comum.Log("dock.log", m); }

        public Dock()
        {
            vidro = new Window
            {
                Title = "Mac Skin Dock vidro", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                Topmost = true, Background = Brushes.Transparent, Left = -10000, Top = -10000, Width = 10, Height = 10, ShowActivated = false
            };
            vidro.SourceInitialized += delegate
            {
                hVidro = new WindowInteropHelper(vidro).Handle;
                HwndSource.FromHwnd(hVidro).CompositionTarget.BackgroundColor = Colors.Transparent;
                Win32.Estilo(hVidro, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
            };

            Janela = new Window
            {
                Title = "Mac Skin Dock", WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                Left = 0, Width = SystemParameters.PrimaryScreenWidth, Height = AlturaJanela,
                Top = SystemParameters.PrimaryScreenHeight - AlturaJanela
            };
            raiz = new Grid();
            fundo = new Border
            {
                Height = Altura, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.28 }
            };
            aro = new Border { Height = Altura, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(Recuo), IsHitTestVisible = false };
            linha = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Pad, 0, Pad, Pad) };
            dock = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, Margem) };
            dock.Children.Add(aro); dock.Children.Add(fundo); dock.Children.Add(linha);
            texto = new TextBlock { FontSize = 13, FontFamily = new FontFamily("Inter, Segoe UI") };
            rotulo = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Child = texto,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Hidden, IsHitTestVisible = false
            };
            raiz.Children.Add(dock); raiz.Children.Add(rotulo);
            Janela.Content = raiz;

            // menu da dock (botão direito no fundo)
            var menu = new ContextMenu();
            var m1 = new MenuItem { Header = "Editar itens da Dock" };
            m1.Click += delegate { Process.Start("explorer.exe", "\"" + pasta + "\""); };
            var m3 = new MenuItem { Header = "Mostrar a barra do Windows", IsCheckable = true };
            m3.Click += delegate { mostrarBarra = m3.IsChecked; Win32.BarraWindows(mostrarBarra); };
            // igual ao botão "Desligar Mac Skin": marca desligado (o vigia não reabre) e fecha
            var m2 = new MenuItem { Header = "Desligar Mac Skin" };
            m2.Click += delegate { try { File.WriteAllText(System.IO.Path.Combine(Comum.Dir, "desligado"), ""); } catch { } Janela.Close(); };
            menu.Items.Add(m1); menu.Items.Add(m3); menu.Items.Add(m2);
            dock.ContextMenu = menu; LigarMenu(menu);

            CarregarItens();
            sep = new Border { Width = 1, Height = Base * 0.75, Margin = new Thickness(6, 0, 6, 8), VerticalAlignment = VerticalAlignment.Bottom };
            linha.Children.Insert(els.Count, sep);
            if (File.Exists(lixo))
            {
                lixeira = NovoItem("Lixeira", "shell:RecycleBinFolder", null, Comum.Icone(lixo));
            }
            CalcularCentros();

            Janela.MouseMove += AoMoverMouse;
            Janela.MouseLeave += delegate { foreach (var el in els) el.Alvo = 1; sobre = null; Animar(); };
            dock.SizeChanged += delegate { Animar(); };   // vidro acompanha quando a dock muda de tamanho

            Janela.SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(Janela).Handle;
                Win32.Estilo(hwnd, Win32.EX_TOOL | Win32.EX_NOACTIVATE);
                f = PresentationSource.FromVisual(Janela).CompositionTarget.TransformToDevice.M22;
                telaW = (int)(SystemParameters.PrimaryScreenWidth * f);
                telaH = (int)(SystemParameters.PrimaryScreenHeight * f);
                Win32.Reservar(hwnd, 3, telaW, telaH, (int)((Altura + Margem * 2) * f));
                Atualizar();
            };
            Janela.Closed += delegate { Fechar(); };

            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += delegate { Atualizar(); }; t.Start();
            // o Explorer volta a mostrar a barra (ao reiniciar, Win+T...): checagem rápida e barata
            var tb = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            tb.Tick += delegate { if (!mostrarBarra) Win32.BarraWindows(false); }; tb.Start();
        }

        public void Mostrar()
        {
            vidro.Show();
            Janela.Owner = vidro;   // a dock fica sempre na frente do vidro
            Janela.Show();
            Log("dock aberta com " + els.Count + " itens");
        }

        public void Fechar()
        {
            if (hwnd != IntPtr.Zero) { Win32.Liberar(hwnd); hwnd = IntPtr.Zero; }
            Win32.BarraWindows(true);
            try { vidro.Close(); } catch { }
        }

        // ---------- itens ----------
        void CarregarItens()
        {
            if (!Directory.Exists(pasta)) return;
            foreach (var arq in Directory.GetFiles(pasta, "*.lnk").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var l = Atalho.Ler(arq);
                    string ico = Environment.ExpandEnvironmentVariables(Regex.Replace(l.Icone, @",-?\d+$", ""));
                    if (ico.Length == 0) ico = l.Alvo.Length > 0 ? l.Alvo : arq;
                    // o instalador grava o processo do app na Descrição (os atalhos abrem por shell:AppsFolder)
                    string exe = l.Argumentos.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) ? l.Descricao
                        : l.Alvo.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(l.Alvo) : "explorer";
                    var img = Comum.Icone(ico);
                    if (img != null) NovoItem(Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(arq), @"^\d+\s*", ""), arq, exe, img);
                }
                catch (Exception e) { Log("item " + arq + ": " + e.Message); }
            }
        }

        Item NovoItem(string nome, string abrir, string exe, ImageSource img)
        {
            var el = new Item { Nome = nome, Abrir = abrir, Exe = exe };
            el.Img = new Image { Source = img, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(el.Img, BitmapScalingMode.HighQuality);
            el.Pulo = new TranslateTransform(); el.Img.RenderTransform = el.Pulo;
            el.Ponto = new Ellipse { Width = 4, Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -6), Visibility = Visibility.Hidden };
            el.Caixa = new Grid { Width = Base, Height = Base, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Gap / 2, 0, Gap / 2, 0), Background = Brushes.Transparent };
            el.Caixa.Children.Add(el.Img); el.Caixa.Children.Add(el.Ponto);
            el.Caixa.MouseLeftButtonDown += (s, e) => { arrastando = el; arrastoX0 = e.GetPosition(raiz).X; arrastoAtivo = false; el.Caixa.CaptureMouse(); };
            el.Caixa.MouseLeftButtonUp += delegate
            {
                el.Caixa.ReleaseMouseCapture();
                bool ativo = arrastoAtivo; arrastando = null; arrastoAtivo = false;
                if (ativo) SalvarOrdem(); else Abrir(el);
            };
            if (abrir.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))   // a Lixeira fica sempre
            {
                var mi = new MenuItem { Header = "Remover da Dock" };
                mi.Click += delegate { Remover(el); };
                el.Caixa.ContextMenu = new ContextMenu(); el.Caixa.ContextMenu.Items.Add(mi);
                LigarMenu(el.Caixa.ContextMenu);
            }
            linha.Children.Add(el.Caixa); els.Add(el);
            return el;
        }

        // a dock não pega foco; sem foco, clicar fora não fecha o menu. Ao abrir, ela pega o foco.
        void LigarMenu(ContextMenu m)
        {
            m.Opened += delegate { Win32.SetForegroundWindow(hwnd); };
            Janela.Deactivated += delegate { m.IsOpen = false; };
        }

        // centros sem ampliação, relativos ao meio da dock: a ampliação não mexe neles (sem tremedeira)
        void CalcularCentros()
        {
            double total = els.Sum(e => e.Largura) + LarguraSep;
            double x = -total / 2;
            foreach (var el in els)
            {
                if (el == lixeira) x += LarguraSep;
                el.Centro = x + el.Largura / 2; x += el.Largura;
            }
        }

        // ---------- ações ----------
        void Pular(Item el)
        {
            var an = new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(260))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new QuadraticEase() };
            el.Pulo.BeginAnimation(TranslateTransform.YProperty, an);
        }

        void Abrir(Item el)
        {
            try
            {
                if (!string.IsNullOrEmpty(el.Exe) && el.Exe != "explorer")
                {
                    var p = Process.GetProcessesByName(el.Exe).FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
                    if (p != null)
                    {
                        if (Win32.IsIconic(p.MainWindowHandle)) Win32.ShowWindow(p.MainWindowHandle, 9);
                        Win32.SetForegroundWindow(p.MainWindowHandle); return;
                    }
                }
                Process.Start(el.Abrir);
                Pular(el);
            }
            catch (Exception e) { Log("abrir " + el.Nome + ": " + e.Message); }
        }

        // tira o ícone na hora e apaga o atalho da pasta da dock (o app em si não é tocado)
        void Remover(Item el)
        {
            try
            {
                File.Delete(el.Abrir);
                linha.Children.Remove(el.Caixa); els.Remove(el); sobre = null;
                CalcularCentros();
            }
            catch (Exception e) { Log("remover " + el.Nome + ": " + e.Message); }
        }

        // arrastar para reordenar: troca de lugar ao passar do meio do vizinho; a ordem vai para os nomes (01, 02...)
        void Arrastar(double x)
        {
            if (!arrastoAtivo)
            {
                if (Math.Abs(x - arrastoX0) < 8 || arrastando == lixeira) return;
                arrastoAtivo = true;
            }
            var apps = els.Where(e => e != lixeira).ToList();
            double rel = x - raiz.ActualWidth / 2;
            int alvo = 0; for (int i = 0; i < apps.Count; i++) if (rel > apps[i].Centro) alvo = i;
            if (alvo != apps.IndexOf(arrastando))
            {
                linha.Children.Remove(arrastando.Caixa); linha.Children.Insert(alvo, arrastando.Caixa);
                els.Remove(arrastando); els.Insert(alvo, arrastando);
                CalcularCentros();
            }
        }

        void SalvarOrdem()
        {
            try
            {
                var apps = els.Where(e => e != lixeira).ToList();
                foreach (var el in apps)   // nome temporário primeiro, para não colidir com outro atalho
                {
                    string tmp = System.IO.Path.Combine(pasta, "~" + Guid.NewGuid() + ".lnk");
                    File.Move(el.Abrir, tmp); el.Abrir = tmp;
                }
                int n = 1;
                foreach (var el in apps)
                {
                    string novo = System.IO.Path.Combine(pasta, string.Format("{0:D2} {1}.lnk", n++, el.Nome));
                    File.Move(el.Abrir, novo); el.Abrir = novo;
                }
            }
            catch (Exception e) { Log("ordem: " + e.Message); }
        }

        // ---------- tema, apps abertos, lixeira, tela cheia ----------
        string NomeProcesso(uint pid)
        {
            string n;
            if (!nomesPid.TryGetValue(pid, out n))
            {
                try { n = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { n = ""; }
                nomesPid[pid] = n;
            }
            return n;
        }

        void Atualizar()
        {
            try
            {
                bool c = Comum.TemaClaro();
                if (c != claro)
                {
                    claro = c;
                    // o desfoque vem da janela de vidro; aqui só o brilho por cima (Liquid Glass)
                    Win32.Vidro(hVidro, c ? 0x30F8F6F6 : 0x40261E1E, true);
                    aro.BorderBrush = Comum.Cor(c ? "#B3EDEDEF" : "#B3252528");
                    fundo.Background = c ? Comum.Degrade("#38FFFFFF", "#0AFFFFFF") : Comum.Degrade("#22FFFFFF", "#05FFFFFF");
                    fundo.BorderBrush = c ? Comum.Degrade("#E6FFFFFF", "#40FFFFFF") : Comum.Degrade("#80FFFFFF", "#1AFFFFFF");
                    sep.Background = Comum.Cor(c ? "#40000000" : "#40FFFFFF");
                    rotulo.Background = Comum.Cor(c ? "#EEF2F2F4" : "#EE2C2C30");
                    texto.Foreground = Comum.Cor(c ? "#FF1D1D1F" : "#FFF5F5F7");
                    foreach (var el in els) el.Ponto.Fill = Comum.Cor(c ? "#CC1D1D1F" : "#CCF5F5F7");
                }
                var abertos = new HashSet<string>(Win32.PidsComJanela().Select(NomeProcesso));
                abertos.Add("explorer");   // como o Finder: sempre aberto
                foreach (var el in els)
                    if (!string.IsNullOrEmpty(el.Exe)) el.Ponto.Visibility = abertos.Contains(el.Exe.ToLowerInvariant()) ? Visibility.Visible : Visibility.Hidden;
                if (lixeira != null)
                {
                    bool ch = Win32.LixeiraCheia();
                    if (ch != cheia) { cheia = ch; var img = Comum.Icone(ch && File.Exists(lixoCheio) ? lixoCheio : lixo); if (img != null) lixeira.Img.Source = img; }
                }
                var vis = Win32.TelaCheia(hwnd, telaW, telaH) ? Visibility.Hidden : Visibility.Visible;
                if (dock.Visibility != vis) { dock.Visibility = vis; vidro.Visibility = vis; rVidro = null; Animar(); }
            }
            catch (Exception e) { Log("atualizar: " + e.Message); }
        }

        // ---------- ampliação ----------
        // o redesenho por quadro só roda enquanto algo se mexe; parada, a dock não gasta nada
        bool animando;
        void Animar()
        {
            if (animando) return;
            animando = true; CompositionTarget.Rendering += AoDesenhar;
        }

        void AoMoverMouse(object s, MouseEventArgs e)
        {
            Animar();
            if (arrastando != null && e.LeftButton == MouseButtonState.Pressed) Arrastar(e.GetPosition(raiz).X);
            double x = e.GetPosition(raiz).X - raiz.ActualWidth / 2, perto = (Base + Gap) / 2;
            sobre = null;
            foreach (var el in els)
            {
                double d = x - el.Centro;
                el.Alvo = 1 + (Max - 1) * Math.Exp(-(d * d) / (2 * Sigma * Sigma));
                if (Math.Abs(d) < perto) sobre = el;
            }
        }

        void AoDesenhar(object s, EventArgs e)
        {
            bool mexendo = false;
            foreach (var el in els)
            {
                if (Math.Abs(el.Alvo - el.Atual) > 0.002)
                {
                    el.Atual += (el.Alvo - el.Atual) * 0.3;
                    el.Caixa.Width = el.Caixa.Height = Base * el.Atual;
                    mexendo = true;
                }
                else if (el.Atual != el.Alvo) { el.Atual = el.Alvo; el.Caixa.Width = el.Caixa.Height = Base * el.Atual; mexendo = true; }
            }
            if (sobre != null)
            {
                texto.Text = sobre.Nome;
                var pt = sobre.Caixa.TranslatePoint(new Point(sobre.Caixa.ActualWidth / 2, 0), raiz);
                rotulo.Margin = new Thickness(pt.X - rotulo.ActualWidth / 2, pt.Y - rotulo.ActualHeight - 10, 0, 0);
                rotulo.Visibility = Visibility.Visible;
            }
            else rotulo.Visibility = Visibility.Hidden;

            // o vidro acompanha o fundo da dock (que alarga quando os ícones crescem), recuado
            if (hVidro != IntPtr.Zero && fundo.ActualWidth > 0 && dock.Visibility == Visibility.Visible)
            {
                var p = fundo.PointToScreen(new Point(Recuo, Recuo));
                int w = (int)((fundo.ActualWidth - 2 * Recuo) * f), h = (int)((fundo.ActualHeight - 2 * Recuo) * f);
                string r = (int)p.X + "," + (int)p.Y + "," + w + "," + h;
                if (r != rVidro) { rVidro = r; Win32.Mover(hVidro, (int)p.X, (int)p.Y, w, h); mexendo = true; }
            }
            if (!mexendo && sobre == null) { animando = false; CompositionTarget.Rendering -= AoDesenhar; }
        }
    }
}
