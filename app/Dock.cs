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
            public Border Badge; public TextBlock BadgeTexto;
            public string Nome, Abrir, Exe; public double Centro, Atual = 1, Alvo = 1, Largura = Base + Gap;
            public bool Fixo;                 // atalho na pasta da dock (senão é só um app aberto)
            public string AppId;              // identidade do app quando o atalho abre por shell:AppsFolder
            public string Chave;              // app aberto não fixado: identidade da janela (ou executável)
            public JanelaApp Modelo;          // app aberto: dados para "Manter na Dock"
            public List<JanelaApp> Janelas = new List<JanelaApp>();
            public string Notificacao;        // null = sem notificação, "" = bolinha vermelha, "1", "2" = número
            public bool IsTeams, IsOutlook;   // pré-calculado na criação: evita checks de texto repetidos
        }

        readonly string pasta = System.IO.Path.Combine(Comum.Dir, "dock");
        readonly string lixo = System.IO.Path.Combine(Comum.Dir, @"icones\TrashIcon.ico");
        readonly string lixoCheio = System.IO.Path.Combine(Comum.Dir, @"icones\FullTrashIcon.ico");
        static readonly Regex regTituloInicio = new Regex(@"^\s*\(([0-9+]+)\)", RegexOptions.Compiled);
        static readonly Regex regTituloGeral = new Regex(@"\(([0-9+]+)\)", RegexOptions.Compiled);
        readonly HashSet<IntPtr> janelasPiscando = new HashSet<IntPtr>();
        int msgShellHook;

        public Window Janela;
        Window vidro;
        Grid raiz, dock;
        Border fundo, aro, sep, sepAbertos, rotulo;
        TextBlock texto;
        StackPanel linha;
        readonly List<Item> els = new List<Item>();
        Item lixeira, sobre, arrastando;
        double arrastoX0; bool arrastoAtivo;
        IntPtr hwnd, hVidro; double f = 1; int telaW, telaH;
        bool? claro; bool? cheia; bool mostrarBarra;
        string rVidro;
        Brush corPonto = Brushes.White;

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
            var m4 = new MenuItem { Header = "Mostrar apps ocultos de novo" };
            m4.Click += delegate { MostrarOcultos(); };
            // desliga e liga de novo pelo macskin.ps1, num PowerShell à parte (este processo some no meio do caminho)
            var m5 = new MenuItem { Header = "Reiniciar Mac Skin" };
            m5.Click += delegate
            {
                try
                {
                    string ps = "'" + System.IO.Path.Combine(Comum.Dir, "macskin.ps1") + "'";
                    Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"& " + ps + " -Acao desligar; Start-Sleep 1; & " + ps + " -Acao ligar\"") { CreateNoWindow = true, UseShellExecute = false });
                }
                catch (Exception e) { Comum.Log("dock.log", "reiniciar: " + e.Message); }
            };
            menu.Items.Add(m1); menu.Items.Add(m4); menu.Items.Add(m3); menu.Items.Add(m5); menu.Items.Add(m2);
            dock.ContextMenu = menu; LigarMenu(menu); MenuVidro.Aplicar(menu, false);

            // ordem na dock: apps fixados | apps abertos não fixados | Lixeira, com separadores
            sepAbertos = NovoSeparador(); sepAbertos.Visibility = Visibility.Collapsed;
            sep = NovoSeparador();
            linha.Children.Add(sepAbertos); linha.Children.Add(sep);
            CarregarItens();
            if (File.Exists(lixo))
            {
                lixeira = NovoItem("Lixeira", "shell:RecycleBinFolder", null, Comum.Icone(lixo), false);
            }
            CalcularCentros();

            Janela.MouseMove += AoMoverMouse;
            Janela.MouseLeave += delegate { foreach (var el in els) el.Alvo = 1; sobre = null; Animar(); };
            dock.SizeChanged += delegate { Animar(); };   // vidro acompanha quando a dock muda de tamanho
            Bandeja.Mudou += delegate
            {
                RecalcularOutlookTray();
                try { Janela.Dispatcher.BeginInvoke((Action)Atualizar); } catch { }
            };
            RecalcularOutlookTray();

            Janela.SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(Janela).Handle;
                Win32.Estilo(hwnd, Win32.EX_TOOL | Win32.EX_NOACTIVATE);
                var src = HwndSource.FromHwnd(hwnd);
                if (src != null) src.AddHook(AoReceberMensagemWin32);
                Win32.RegisterShellHookWindow(hwnd);
                msgShellHook = Win32.RegisterWindowMessage("SHELLHOOK");
                f = PresentationSource.FromVisual(Janela).CompositionTarget.TransformToDevice.M22;
                telaW = (int)(SystemParameters.PrimaryScreenWidth * f);
                telaH = (int)(SystemParameters.PrimaryScreenHeight * f);
                Win32.Reservar(hwnd, 3, telaW, telaH, (int)((Altura + Margem * 2) * f));
                Atualizar();
            };
            Janela.Closed += delegate { Fechar(); };

            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += delegate { Atualizar(); }; t.Start();
            // checagem da barra: 1 s é mais que suficiente e economiza 75% das chamadas
            var tb = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tb.Tick += delegate { if (!mostrarBarra) Win32.BarraWindows(false); }; tb.Start();
        }

        bool outlookNovoEmail;
        void RecalcularOutlookTray()
        {
            bool novo = false;
            int countOutlook = 0;
            for (int i = 0; i < Bandeja.Icones.Count; i++)
            {
                var ic = Bandeja.Icones[i];
                if (ic.Janela == IntPtr.Zero) continue;
                uint pid = Win32.Pid(ic.Janela);
                if (pid == 0) continue;
                if (string.Equals(Janelas.NomeDoProcesso(pid), "outlook", StringComparison.OrdinalIgnoreCase))
                {
                    countOutlook++;
                    string dica = ic.Dica;
                    if (!string.IsNullOrEmpty(dica))
                    {
                        if (dica.IndexOf("novas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dica.IndexOf("unread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dica.IndexOf("não lidas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dica.IndexOf("new mail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (dica.IndexOf("e-mail", StringComparison.OrdinalIgnoreCase) >= 0 && dica.IndexOf("Outlook", StringComparison.OrdinalIgnoreCase) < 0) ||
                            !string.Equals(dica, "Microsoft Outlook", StringComparison.OrdinalIgnoreCase))
                        {
                            novo = true;
                            break;
                        }
                    }
                }
            }
            if (countOutlook > 1) novo = true;
            outlookNovoEmail = novo;
        }

        IntPtr AoReceberMensagemWin32(IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled)
        {
            if (m == 0x001A)   // WM_SETTINGCHANGE: tema do Windows mudou, limpa cache e atualiza
            {
                Comum.InvalidarTema();
                try { Janela.Dispatcher.BeginInvoke((Action)Atualizar); } catch { }
            }
            else if (m == msgShellHook)
            {
                int code = w.ToInt32();
                if (code == 0x8006 || code == 6)   // HSHELL_FLASH, HSHELL_REDRAW
                {
                    IntPtr fl = l;
                    if (fl != IntPtr.Zero)
                    {
                        lock (janelasPiscando) { janelasPiscando.Add(fl); }
                        try { Janela.Dispatcher.BeginInvoke((Action)Atualizar); } catch { }
                    }
                }
                else if (code == 4 || code == 0x8004)   // HSHELL_WINDOWACTIVATED, HSHELL_RUDEAPPACTIVATED
                {
                    IntPtr ativada = l;
                    bool alterou = false;
                    lock (janelasPiscando) { alterou = janelasPiscando.Remove(ativada); }
                    if (alterou) try { Janela.Dispatcher.BeginInvoke((Action)Atualizar); } catch { }
                }
            }
            return IntPtr.Zero;
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
            if (hwnd != IntPtr.Zero)
            {
                Win32.DeregisterShellHookWindow(hwnd);
                Win32.Liberar(hwnd);
                hwnd = IntPtr.Zero;
            }
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
                    bool porAppsFolder = l.Argumentos.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase);
                    string appId = porAppsFolder ? l.Argumentos.Substring("shell:AppsFolder".Length).TrimStart('\\') : null;
                    string ico = Environment.ExpandEnvironmentVariables(Regex.Replace(l.Icone, @",-?\d+$", ""));
                    // o instalador grava o processo do app na Descrição (os atalhos abrem por shell:AppsFolder)
                    string exe = porAppsFolder ? l.Descricao
                        : l.Alvo.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(l.Alvo) : "explorer";
                    string nome = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(arq), @"^\d+\s*", "");
                    if (ico.Length > 0 || !porAppsFolder)
                    {
                        var img = Comum.Icone(ico.Length > 0 ? ico : l.Alvo.Length > 0 ? l.Alvo : arq);
                        if (img != null)
                        {
                            var el = NovoItem(nome, arq, exe, img, true); el.AppId = appId;
                            if (!el.IsTeams && appId != null && appId.IndexOf("Teams", StringComparison.OrdinalIgnoreCase) >= 0) el.IsTeams = true;
                            if (!el.IsOutlook && appId != null && appId.IndexOf("Outlook", StringComparison.OrdinalIgnoreCase) >= 0) el.IsOutlook = true;
                        }
                    }
                    else
                    {
                        // fixado com "Manter na Dock" sem ícone próprio: o ícone vem do próprio app
                        var el = NovoItem(nome, arq, exe, null, true); el.AppId = appId;
                        if (!el.IsTeams && appId != null && appId.IndexOf("Teams", StringComparison.OrdinalIgnoreCase) >= 0) el.IsTeams = true;
                        if (!el.IsOutlook && appId != null && appId.IndexOf("Outlook", StringComparison.OrdinalIgnoreCase) >= 0) el.IsOutlook = true;
                        PorIcone(el, appId, null);
                    }
                }
                catch (Exception e) { Log("item " + arq + ": " + e.Message); }
            }
        }

        // ícone de um app sem .ico estilo Mac: logo grande do pacote (Store, apps web instalados); senão o do Iniciar
        // (shell:AppsFolder) ou o do executável, pedidos ao Windows em 256 px para não borrar
        static void PorIcone(Item el, string aumid, string exe)
        {
            string logo = Janelas.LogoDoPacote(aumid);
            var img = logo == null ? null : Comum.Icone(logo);
            if (img != null) { el.Img.Source = img; return; }
            string fonte = !string.IsNullOrEmpty(aumid) ? @"shell:AppsFolder\" + aumid : exe;
            if (!string.IsNullOrEmpty(fonte)) Shell.PedirImagem(fonte, 256, i => el.Img.Source = i);
        }

        static Border NovoSeparador()
        {
            return new Border { Width = 1, Height = Base * 0.75, Margin = new Thickness(6, 0, 6, 8), VerticalAlignment = VerticalAlignment.Bottom };
        }

        Item NovoItem(string nome, string abrir, string exe, ImageSource img, bool fixo)
        {
            var el = new Item { Nome = nome, Abrir = abrir, Exe = exe, Fixo = fixo };
            el.IsTeams = string.Equals(exe, "teams", StringComparison.OrdinalIgnoreCase)
                || string.Equals(exe, "ms-teams", StringComparison.OrdinalIgnoreCase)
                || (nome != null && nome.IndexOf("Teams", StringComparison.OrdinalIgnoreCase) >= 0);
            el.IsOutlook = string.Equals(exe, "outlook", StringComparison.OrdinalIgnoreCase)
                || (nome != null && nome.IndexOf("Outlook", StringComparison.OrdinalIgnoreCase) >= 0);
            el.Img = new Image { Source = img, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(el.Img, BitmapScalingMode.HighQuality);
            el.Pulo = new TranslateTransform(); el.Img.RenderTransform = el.Pulo;
            el.Ponto = new Ellipse { Width = 4, Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -6), Visibility = Visibility.Hidden };
            el.BadgeTexto = new TextBlock
            {
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Inter, Segoe UI"),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            };
            el.Badge = new Border
            {
                Background = Comum.Cor("#FF3B30"),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(5),
                MinWidth = 10,
                MinHeight = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 1, 0),
                Visibility = Visibility.Hidden,
                Child = el.BadgeTexto,
                IsHitTestVisible = false
            };
            el.Badge.RenderTransform = el.Pulo;
            el.Caixa = new Grid { Width = Base, Height = Base, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(Gap / 2, 0, Gap / 2, 0), Background = Brushes.Transparent };
            el.Caixa.Children.Add(el.Img); el.Caixa.Children.Add(el.Ponto); el.Caixa.Children.Add(el.Badge);
            el.Caixa.MouseLeftButtonDown += (s, e) => { arrastando = el; arrastoX0 = e.GetPosition(raiz).X; arrastoAtivo = false; el.Caixa.CaptureMouse(); };
            el.Caixa.MouseLeftButtonUp += delegate
            {
                el.Caixa.ReleaseMouseCapture();
                bool ativo = arrastoAtivo; arrastando = null; arrastoAtivo = false;
                if (ativo) SalvarOrdem(); else Abrir(el);
            };
            if (abrir != "shell:RecycleBinFolder")   // a Lixeira não tem menu
            {
                // o menu muda conforme o app está fixado e/ou aberto, então é montado na hora
                var m = new ContextMenu(); el.Caixa.ContextMenu = m; LigarMenu(m); MenuVidro.Aplicar(m, true);
                el.Caixa.ContextMenuOpening += delegate
                {
                    m.Items.Clear();
                    Action<string, Action> op = (t, a) => { var mi = new MenuItem { Header = t }; mi.Click += delegate { a(); }; m.Items.Add(mi); };
                    // várias janelas do app: uma por linha, como no Mac (minimizadas também)
                    if (el.Janelas.Count > 1)
                    {
                        foreach (var j in el.Janelas)
                        {
                            var h = j.H;
                            string t = Win32.Titulo(h); if (t.Length == 0) t = j.Titulo;
                            op((Win32.IsIconic(h) ? "◦ " : "") + (t.Length > 70 ? t.Substring(0, 70) + "…" : t).Replace("_", "__"), () => Trazer(h));
                        }
                        m.Items.Add(new Separator());
                    }
                    if (el.Fixo) op("Remover da Dock", () => Remover(el));
                    else { op("Manter na Dock", () => ManterNaDock(el)); op("Não mostrar na Dock", () => Ocultar(el)); }
                    if (el.Janelas.Count > 0) { m.Items.Add(new Separator()); op("Encerrar", () => Encerrar(el)); }
                };
            }
            // fixados antes do separador dos abertos; abertos antes do separador da Lixeira; Lixeira no fim
            int pos = abrir == "shell:RecycleBinFolder" ? linha.Children.Count : linha.Children.IndexOf(fixo ? sepAbertos : sep);
            linha.Children.Insert(pos, el.Caixa); els.Add(el);
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
            // percorre a linha na ordem da tela: ícones ocupam Largura, separadores visíveis LarguraSep
            var porCaixa = els.ToDictionary(e => (UIElement)e.Caixa);
            double total = 0;
            foreach (UIElement c in linha.Children)
                total += porCaixa.ContainsKey(c) ? porCaixa[c].Largura : c.Visibility == Visibility.Visible ? LarguraSep : 0;
            double x = -total / 2;
            foreach (UIElement c in linha.Children)
            {
                Item el;
                if (porCaixa.TryGetValue(c, out el)) { el.Centro = x + el.Largura / 2; x += el.Largura; }
                else if (c.Visibility == Visibility.Visible) x += LarguraSep;
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
                lock (janelasPiscando)
                {
                    foreach (var j in el.Janelas) janelasPiscando.Remove(j.H);
                }

                // Lixeira: um clique abre, outro clique fecha
                if (el == lixeira)
                {
                    var abertas = Win32.JanelasLixeira();
                    if (abertas.Count > 0) foreach (var j in abertas) Win32.PostMessage(j, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE
                    else { Process.Start("explorer.exe", "shell:RecycleBinFolder"); Pular(el); }
                    return;
                }
                // já aberto: uma janela minimizada volta primeiro; com o app já na frente, alterna para a próxima
                // janela dele; senão traz a de cima
                if (el.Janelas.Count > 0)
                {
                    var fg = Win32.GetForegroundWindow();
                    var alvo = el.Janelas.FirstOrDefault(x => Win32.IsIconic(x.H));
                    if (alvo == null && el.Janelas.Count > 1 && el.Janelas.Any(x => x.H == fg)) alvo = el.Janelas[el.Janelas.Count - 1];
                    Trazer((alvo ?? el.Janelas[0]).H); return;
                }
                if (el.Exe == "explorer")   // o Finder da dock é o nosso; o Explorador continua no Win+E
                {
                    Process.Start(Process.GetCurrentProcess().MainModule.FileName, "--finder");
                    Pular(el); return;
                }
                if (!el.Fixo) return;   // app aberto que acabou de fechar
                Process.Start(el.Abrir);
                Pular(el);
            }
            catch (Exception e) { Log("abrir " + el.Nome + ": " + e.Message); }
        }

        static void Trazer(IntPtr j)
        {
            if (Win32.IsIconic(j)) Win32.ShowWindow(j, 9);
            Win32.SetForegroundWindow(j);
        }

        // tira o ícone na hora e apaga o atalho da pasta da dock (o app em si não é tocado; se estiver aberto,
        // volta logo como app aberto)
        void Remover(Item el)
        {
            try
            {
                File.Delete(el.Abrir);
                TirarDaLinha(el);
                Atualizar();
            }
            catch (Exception e) { Log("remover " + el.Nome + ": " + e.Message); }
        }

        void TirarDaLinha(Item el)
        {
            linha.Children.Remove(el.Caixa); els.Remove(el); if (sobre == el) sobre = null;
            CalcularCentros();
        }

        // app aberto -> atalho na pasta da dock, no fim dos fixados (abre pelo mesmo caminho do Iniciar quando tem identidade)
        void ManterNaDock(Item el)
        {
            try
            {
                var j = el.Modelo;
                int n = Fixados().Count + 1;
                string lnk = System.IO.Path.Combine(pasta, string.Format("{0:D2} {1}.lnk", n, Regex.Replace(el.Nome, @"[\\/:*?""<>|]", "")));
                if (!string.IsNullOrEmpty(j.Aumid)) Atalho.Criar(lnk, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), @"shell:AppsFolder\" + j.Aumid, j.Processo);
                else Atalho.Criar(lnk, j.Caminho, "", j.Processo);
                el.Fixo = true; el.Abrir = lnk; el.AppId = string.IsNullOrEmpty(j.Aumid) ? null : j.Aumid; el.Chave = null;
                linha.Children.Remove(el.Caixa); linha.Children.Insert(linha.Children.IndexOf(sepAbertos), el.Caixa);
                Atualizar(); CalcularCentros();
            }
            catch (Exception e) { Log("manter " + el.Nome + ": " + e.Message); }
        }

        // apps abertos que não aparecem na dock (janelinhas de apps de bandeja etc.): um por linha em dock-ocultos.txt
        readonly string arqOcultos = System.IO.Path.Combine(Comum.Dir, "dock-ocultos.txt");
        HashSet<string> ocultos;
        HashSet<string> Ocultos()
        {
            if (ocultos == null)
            {
                ocultos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { if (File.Exists(arqOcultos)) foreach (var l in File.ReadAllLines(arqOcultos)) if (l.Trim().Length > 0 && !l.StartsWith("#")) ocultos.Add(l.Trim()); }
                catch { }
            }
            return ocultos;
        }
        void Ocultar(Item el)
        {
            try { File.AppendAllText(arqOcultos, el.Chave + Environment.NewLine); } catch (Exception e) { Log("ocultar: " + e.Message); }
            Ocultos().Add(el.Chave);
            TirarDaLinha(el); AtualizarAbertos();
        }
        void MostrarOcultos()
        {
            try { File.Delete(arqOcultos); } catch { }
            ocultos = null; AtualizarAbertos();
        }

        // pede para fechar (como o X da janela): o app pode perguntar se quer salvar
        void Encerrar(Item el)
        {
            foreach (var j in el.Janelas) Win32.PostMessage(j.H, 0x10, IntPtr.Zero, IntPtr.Zero);
        }

        List<Item> Fixados()
        {
            var porCaixa = els.ToDictionary(e => (UIElement)e.Caixa);
            var r = new List<Item>();
            foreach (UIElement c in linha.Children) { if (c == sepAbertos) break; Item el; if (porCaixa.TryGetValue(c, out el)) r.Add(el); }
            return r;
        }

        // arrastar para reordenar (só os fixados): troca de lugar ao passar do meio do vizinho; a ordem vai para os nomes (01, 02...)
        void Arrastar(double x)
        {
            if (!arrastoAtivo)
            {
                if (Math.Abs(x - arrastoX0) < 8 || !arrastando.Fixo) return;
                arrastoAtivo = true;
            }
            var apps = Fixados();
            double rel = x - raiz.ActualWidth / 2;
            int alvo = 0; for (int i = 0; i < apps.Count; i++) if (rel > apps[i].Centro) alvo = i;
            if (alvo != apps.IndexOf(arrastando))
            {
                linha.Children.Remove(arrastando.Caixa); linha.Children.Insert(alvo, arrastando.Caixa);
                CalcularCentros();
            }
        }

        void SalvarOrdem()
        {
            try
            {
                var apps = Fixados();
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
        // liga cada janela aberta a um ícone: o fixado do mesmo app ou um ícone de "aberto", criado na hora,
        // depois dos fixados (como no Mac). Fechou o app, o ícone de aberto some; o fixado fica.
        void AtualizarAbertos()
        {
            var janelas = Janelas.Abertas();
            for (int i = 0; i < els.Count; i++) els[i].Janelas.Clear();

            Item finder = null;
            for (int i = 0; i < els.Count; i++)
            {
                if (els[i].Fixo && els[i].Exe == "explorer") { finder = els[i]; break; }
            }

            var sobra = new List<JanelaApp>();
            for (int i = 0; i < janelas.Count; i++)
            {
                var j = janelas[i];
                Item dono = null;
                // Finder: as nossas janelas do Finder e as do Explorador de Arquivos
                if (j.Processo == "macskin" || (j.Processo == "explorer" && Win32.Classe(j.H) == "CabinetWClass")) dono = finder;
                if (dono == null)
                {
                    for (int k = 0; k < els.Count; k++)
                    {
                        if (els[k].Fixo && Combina(els[k], j)) { dono = els[k]; break; }
                    }
                }
                if (dono != null) dono.Janelas.Add(j);
                else if (!Ocultos().Contains(j.Chave)) sobra.Add(j);
            }

            for (int i = 0; i < sobra.Count; i++)
            {
                var j = sobra[i];
                Item el = null;
                for (int k = 0; k < els.Count; k++)
                {
                    if (!els[k].Fixo && els[k] != lixeira && els[k].Chave == j.Chave) { el = els[k]; break; }
                }
                if (el == null)
                {
                    el = NovoItem(Janelas.Nome(j), null, j.Processo, null, false);
                    el.Chave = j.Chave; el.Modelo = j; el.Ponto.Fill = corPonto;
                    PorIcone(el, j.Aumid, j.Caminho);
                }
                el.Janelas.Add(j);
            }

            for (int i = els.Count - 1; i >= 0; i--)
            {
                var el = els[i];
                if (!el.Fixo && el != lixeira && el.Janelas.Count == 0)
                    TirarDaLinha(el);
            }

            bool temAbertosNaoFixos = false;
            for (int i = 0; i < els.Count; i++)
            {
                if (!els[i].Fixo && els[i] != lixeira) { temAbertosNaoFixos = true; break; }
            }
            sepAbertos.Visibility = temAbertosNaoFixos ? Visibility.Visible : Visibility.Collapsed;

            var fg = Win32.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                lock (janelasPiscando) { janelasPiscando.Remove(fg); }
            }

            for (int i = 0; i < els.Count; i++)
            {
                var el = els[i];
                if (el != lixeira) el.Ponto.Visibility = el.Janelas.Count > 0 || el == finder ? Visibility.Visible : Visibility.Hidden;
                AtualizarBadge(el);
            }
            CalcularCentros();
        }

        void AtualizarBadge(Item el)
        {
            if (el == lixeira || (!el.IsTeams && !el.IsOutlook))
            {
                if (el.Notificacao != null)
                {
                    el.Notificacao = null;
                    el.Badge.Visibility = Visibility.Hidden;
                }
                return;
            }

            string notif = null;

            // 1. Título das janelas abertas
            for (int i = 0; i < el.Janelas.Count; i++)
            {
                var j = el.Janelas[i];
                string t = Win32.Titulo(j.H);
                if (string.IsNullOrEmpty(t)) t = j.Titulo ?? "";
                if (t.Length > 0 && t.IndexOf('(') >= 0)
                {
                    var m = regTituloGeral.Match(t);
                    if (m.Success)
                    {
                        string cont = m.Groups[1].Value;
                        int n;
                        if (int.TryParse(cont.TrimEnd('+'), out n) && n > 0)
                        {
                            notif = cont;
                            break;
                        }
                    }
                }
            }

            // 2. Outlook com novo e-mail na bandeja (booleano em cache, zero chamada)
            if (notif == null && el.IsOutlook && outlookNovoEmail)
                notif = "";

            // 3. Janelas piscando / solicitando atenção (HSHELL_FLASH)
            if (notif == null)
            {
                lock (janelasPiscando)
                {
                    for (int i = 0; i < el.Janelas.Count; i++)
                    {
                        if (janelasPiscando.Contains(el.Janelas[i].H))
                        {
                            notif = "";
                            break;
                        }
                    }
                }
            }

            if (notif != el.Notificacao)
            {
                el.Notificacao = notif;
                if (notif != null)
                {
                    if (string.IsNullOrEmpty(notif))
                    {
                        el.BadgeTexto.Text = "";
                        el.Badge.MinWidth = 10;
                        el.Badge.MinHeight = 10;
                        el.Badge.Width = 10;
                        el.Badge.Height = 10;
                        el.Badge.CornerRadius = new CornerRadius(5);
                        el.Badge.Padding = new Thickness(0);
                    }
                    else
                    {
                        el.BadgeTexto.Text = notif;
                        el.Badge.Width = double.NaN;
                        el.Badge.Height = double.NaN;
                        el.Badge.MinWidth = 16;
                        el.Badge.MinHeight = 16;
                        el.Badge.CornerRadius = new CornerRadius(8);
                        el.Badge.Padding = new Thickness(3, 0, 3, 0);
                    }
                    el.Badge.Visibility = Visibility.Visible;
                }
                else
                {
                    el.Badge.Visibility = Visibility.Hidden;
                }
            }
        }

        // a janela é deste app fixado? Pela identidade (Keep não é o Edge, mesmo rodando dentro dele) ou,
        // quando a janela não tem identidade, pelo processo
        static bool Combina(Item el, JanelaApp j)
        {
            if (!string.IsNullOrEmpty(el.AppId) && !string.IsNullOrEmpty(j.Aumid))
                return string.Equals(el.AppId, j.Aumid, StringComparison.OrdinalIgnoreCase)
                    || j.Aumid.StartsWith(el.AppId + ".", StringComparison.OrdinalIgnoreCase);   // ex.: perfis do Edge (MSEdge.xxxx)
            return !string.IsNullOrEmpty(el.Exe) && string.Equals(el.Exe, j.Processo, StringComparison.OrdinalIgnoreCase);
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
                    sep.Background = sepAbertos.Background = Comum.Cor(c ? "#40000000" : "#40FFFFFF");
                    rotulo.Background = Comum.Cor(c ? "#EEF2F2F4" : "#EE2C2C30");
                    texto.Foreground = Comum.Cor(c ? "#FF1D1D1F" : "#FFF5F5F7");
                    corPonto = Comum.Cor(c ? "#CC1D1D1F" : "#CCF5F5F7");
                    foreach (var el in els) el.Ponto.Fill = corPonto;
                }
                AtualizarAbertos();
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
        const int AtrasoVidro = 2;   // quadros de atraso do vidro em relação ao conteúdo (ajuste se ainda sobrar/faltar vidro)
        readonly Queue<int[]> filaVidro = new Queue<int[]>();
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

            // o vidro acompanha o fundo da dock (que alarga quando os ícones crescem), recuado.
            // SetWindowPos vale na hora, mas a janela da dock (em camadas) só aparece 1 a 3 quadros depois: sem atraso, o
            // vidro abria para as laterais antes do conteúdo e a dock "tremia". A fila segura o vidro para chegar junto.
            if (hVidro != IntPtr.Zero && fundo.ActualWidth > 0 && dock.Visibility == Visibility.Visible)
            {
                var p = fundo.PointToScreen(new Point(Recuo, Recuo));
                int w = (int)((fundo.ActualWidth - 2 * Recuo) * f), h = (int)((fundo.ActualHeight - 2 * Recuo) * f);
                filaVidro.Enqueue(new int[] { (int)p.X, (int)p.Y, w, h });
                if (filaVidro.Count > AtrasoVidro)
                {
                    var q = filaVidro.Dequeue();
                    string r = q[0] + "," + q[1] + "," + q[2] + "," + q[3];
                    if (r != rVidro) { rVidro = r; Win32.Mover(hVidro, q[0], q[1], q[2], q[3]); }
                }
                foreach (var q in filaVidro)
                    if (q[0] + "," + q[1] + "," + q[2] + "," + q[3] != rVidro) { mexendo = true; break; }
            }
            else filaVidro.Clear();
            if (!mexendo && sobre == null) { animando = false; filaVidro.Clear(); CompositionTarget.Rendering -= AoDesenhar; }
        }
    }
}
