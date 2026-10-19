// Mac Skin: ao passar o mouse, minimizar fica com fundo amarelo e maximizar com fundo verde
// (o fechar já fica vermelho no Windows), cobrindo o botão inteiro como o vermelho.
// Camada transparente que deixa o clique passar: os botões continuam do Windows.
// Os botões vêm do UI Automation (Edge, Office, Electron) ou, se não aparecerem lá, da área que o
// Windows informa (Explorador de Arquivos e apps clássicos). A busca roda fora da thread da tela.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MacSkin
{
    class Botoes
    {
        class Botao { public int Tipo; public double DxDireita, DyTopo, L, A; }   // Tipo 0 = minimizar, 1 = maximizar

        static readonly Brush[] Cores = { Comum.Cor("#FEBC2E"), Comum.Cor("#28C840") };   // amarelo e verde do macOS
        static readonly Geometry Minimizar = Geometry.Parse("M0,5 L10,5");
        static readonly Geometry Maximizar = Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");
        static readonly Geometry Restaurar = Geometry.Parse("M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z");

        public Window Janela;
        Grid fundo; Path desenho;
        IntPtr eu;
        bool visivel;
        // medições prontas e em andamento, por janela + tamanho
        readonly Dictionary<string, Botao[]> medidas = new Dictionary<string, Botao[]>();
        readonly HashSet<string> medindo = new HashSet<string>();

        static void Log(string m) { Comum.Log("botoes.log", m); }

        public Botoes()
        {
            Janela = new Window
            {
                Title = "Mac Skin Botoes", WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                Left = -10000, Top = -10000, Width = 10, Height = 10
            };
            fundo = new Grid();
            desenho = new Path { Width = 10, Height = 10, Stretch = Stretch.None, StrokeThickness = 1, Stroke = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            fundo.Children.Add(desenho);
            Janela.Content = fundo;
            Janela.SourceInitialized += delegate
            {
                eu = new WindowInteropHelper(Janela).Handle;
                Win32.Estilo(eu, Win32.EX_TOOL | Win32.EX_TRANSPARENT | Win32.EX_NOACTIVATE);   // fora do Alt+Tab, clique passa, sem foco
            };
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            t.Tick += delegate { Verificar(); }; t.Start();
        }

        public void Mostrar() { Janela.Show(); Esconder(); Log("botões abertos"); }

        void Esconder() { if (visivel || Janela.IsVisible) { Janela.Visibility = Visibility.Hidden; visivel = false; } }

        void Verificar()
        {
            try
            {
                Win32.POINT p; Win32.GetCursorPos(out p);
                IntPtr h = Win32.GetAncestor(Win32.WindowFromPoint(p), 2);
                if (h == IntPtr.Zero || h == eu) { Esconder(); return; }
                Win32.RECT r; Win32.GetWindowRect(h, out r);
                // só trabalha com o mouse no canto de cima à direita da janela (o resto do tempo, quase nada)
                if (p.Y > r.T + 80 || p.X < r.R - 260) { Esconder(); return; }
                string c = Win32.Classe(h);
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || Win32.Titulo(h).StartsWith("Mac Skin")) { Esconder(); return; }

                string chave = h.ToInt64() + ":" + (r.R - r.L) + "x" + (r.B - r.T) + ":" + Win32.IsZoomed(h);
                Botao[] bts;
                if (!medidas.TryGetValue(chave, out bts))
                {
                    if (!medindo.Contains(chave))
                    {
                        medindo.Add(chave);
                        var rr = r;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            var m = Medir(h, rr);
                            Janela.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                medindo.Remove(chave);
                                if (medidas.Count > 300) medidas.Clear();   // janelas fechadas ao longo do dia
                                medidas[chave] = m;
                            }));
                        });
                    }
                    Esconder(); return;
                }
                if (bts == null) { Esconder(); return; }

                Botao sob = null; double bx = 0, by = 0;
                foreach (var b in bts)
                {
                    double x = r.R - b.DxDireita, y = r.T + b.DyTopo;
                    if (p.X >= x && p.X < x + b.L && p.Y >= y && p.Y < y + b.A) { sob = b; bx = x; by = y; }
                }
                if (sob == null) { Esconder(); return; }
                fundo.Background = Cores[sob.Tipo];
                desenho.Data = sob.Tipo == 0 ? Minimizar : Win32.IsZoomed(h) ? Restaurar : Maximizar;
                Win32.MoverNoTopo(eu, (int)bx, (int)by, (int)sob.L, (int)sob.A);
                if (!visivel) { Janela.Visibility = Visibility.Visible; visivel = true; }
            }
            catch (Exception e) { Log("erro: " + e.Message); }
        }

        // ---------- achar os botões (fora da thread da tela) ----------
        static Botao[] Medir(IntPtr h, Win32.RECT r)
        {
            try { return MedirUia(h, r) ?? MedirDwm(h, r); }
            catch (Exception e) { Log("medir: " + e.Message); return null; }
        }

        static readonly System.Windows.Automation.Condition CondBotao = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        static readonly System.Windows.Automation.Condition CondFechar = new AndCondition(CondBotao, new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "Fechar"),
            new PropertyCondition(AutomationElement.NameProperty, "Close"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Close")));

        // botões desenhados pelo próprio app (Edge, Office, Electron): o UI Automation acha o "Fechar" e os vizinhos
        static Botao[] MedirUia(IntPtr h, Win32.RECT r)
        {
            try
            {
                var fechar = AutomationElement.FromHandle(h).FindFirst(TreeScope.Descendants, CondFechar);
                if (fechar == null || fechar.Current.IsOffscreen) return null;
                var fb = fechar.Current.BoundingRectangle;
                var pai = TreeWalker.ControlViewWalker.GetParent(fechar);
                var linha = pai.FindAll(TreeScope.Children, CondBotao).Cast<AutomationElement>()
                    .Select(e => e.Current.BoundingRectangle)
                    .Where(b => Math.Abs(b.Y - fb.Y) < 4 && Math.Abs(b.Height - fb.Height) < 4 && b.X < fb.X - 4)
                    .OrderBy(b => b.X).ToList();
                if (linha.Count < 2) return null;   // caixa de diálogo: só tem o fechar
                linha = linha.Skip(linha.Count - 2).ToList();
                return new[] { 0, 1 }.Select(i => new Botao { Tipo = i, DxDireita = r.R - linha[i].X, DyTopo = linha[i].Y - r.T, L = linha[i].Width, A = linha[i].Height }).ToArray();
            }
            catch { return null; }
        }

        // botões desenhados pelo Windows (Explorador de Arquivos e apps clássicos). Medido pixel a pixel contra o
        // vermelho do fechar: a largura informada inclui a borda invisível (8 px normal, 9 maximizada); os 3 botões
        // ocupam o resto, encostados na borda direita visível. Normal: 1 borda fina em cima. Maximizada: a faixa vem
        // deslocada pela borda invisível e o botão tem 2 bordas finas a menos de altura.
        static Botao[] MedirDwm(IntPtr h, Win32.RECT r)
        {
            var c = Win32.BotoesDwm(h);
            var v = Win32.Visivel(h);
            int borda = Math.Max(0, v.L - r.L), fino = Win32.GetSystemMetrics(6);
            bool max = Win32.IsZoomed(h);
            int ox = max ? borda : 0;
            double dir = Math.Min(r.L + ox + c.R, v.R);
            double l = Math.Round((c.R - c.L - borda) / 3.0);
            double esq = dir - 3 * l;
            double topo = max ? r.T + borda + c.T : r.T + c.T + fino;
            double baseY = max ? r.T + borda + c.B - 2 * fino : r.T + c.B;
            topo = Math.Max(topo, v.T); baseY = Math.Min(baseY, v.B);
            if (l < 20 || baseY - topo < 15) return null;
            return new[] { 0, 1 }.Select(i => new Botao { Tipo = i, DxDireita = r.R - (esq + i * l), DyTopo = topo - r.T, L = l, A = baseY - topo }).ToArray();
        }
    }
}
