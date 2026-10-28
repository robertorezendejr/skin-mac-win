// Mac Skin Confirmacao: os diálogos de confirmação de exclusão do Explorador ("Excluir Vários Arquivos"...) em Liquid Glass.
// O diálogo nativo é do Explorer e não aceita tema: quando ele surge, é escondido antes de aparecer, o nosso cartão de
// vidro (acrylic) mostra o mesmo texto e os mesmos botões, e o clique é repassado ao botão original.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacSkin
{
    static class Confirmacao
    {
        const double L = 420, Raio = 22;
        static readonly FontFamily Fonte = new FontFamily("Inter, Segoe UI");
        static readonly FontFamily Icones = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        delegate void WinEventProc(IntPtr gancho, uint ev, IntPtr h, int objeto, int filho, uint thread, uint tempo);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint de, uint ate, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr g);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr pai, EnumProc f, IntPtr l);

        static WinEventProc proc; static IntPtr g1, g2, g3;
        static readonly HashSet<IntPtr> tratados = new HashSet<IntPtr>();
        static readonly uint Eu = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        static Window janela;

        static void Log(string m) { Comum.Log("confirmacao.log", m); }

        public static void Iniciar()
        {
            proc = (g, ev, h, o, f, t, tp) =>
            {
                if (o != 0 || h == IntPtr.Zero) return;
                try { Examinar(h); } catch (Exception e) { Log("erro: " + e.Message); }
            };
            g1 = SetWinEventHook(0x0010, 0x0010, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_SYSTEM_DIALOGSTART
            g2 = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_SHOW
            g3 = SetWinEventHook(0x8018, 0x8018, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_UNCLOAKED
        }

        public static void Parar()
        {
            foreach (var g in new[] { g1, g2, g3 }) if (g != IntPtr.Zero) UnhookWinEvent(g);
            g1 = g2 = g3 = IntPtr.Zero;
            if (janela != null) janela.Close();
        }

        static bool EhConfirmacao(IntPtr h)
        {
            if (Win32.Classe(h) != "#32770" || Win32.Pid(h) == Eu) return false;
            string t = Win32.Titulo(h);
            if (!t.StartsWith("Excluir", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("Confirmar", StringComparison.OrdinalIgnoreCase)) return false;
            string proc = Janelas.NomeDoProcesso(Win32.Pid(h));
            return proc == "explorer" || proc == "macskin";   // o Finder (outro processo do Mac Skin) também chama a exclusão do shell
        }

        static void Examinar(IntPtr h)
        {
            if (tratados.Contains(h)) return;
            if (Win32.Classe(h) == "#32770" && Win32.Pid(h) != Eu) Log("vi #32770 '" + Win32.Titulo(h) + "' de " + Janelas.NomeDoProcesso(Win32.Pid(h)));
            if (!EhConfirmacao(h)) return;
            tratados.Add(h);
            if (tratados.Count > 50) tratados.RemoveWhere(x => !IsWindow(x));
            Win32.Esconder(h);   // antes de pintar: o usuário só vê o nosso
            string titulo = Win32.Titulo(h);
            // os textos e botões vêm do UI Automation, que o Explorer responde de outra thread
            string msg = ""; var botoes = new List<Tuple<string, IntPtr>>(); var caixas = new List<Tuple<string, IntPtr>>();
            EnumChildWindows(h, (f, l) =>
            {
                string c = Win32.Classe(f), t = Win32.Titulo(f);
                int estilo = Win32.GetWindowLong(f, -16) & 0xF;   // BS_CHECKBOX, AUTOCHECKBOX, 3STATE, AUTO3STATE: caixa de seleção, não botão
                bool caixa = estilo == 2 || estilo == 3 || estilo == 5 || estilo == 6;
                if (c == "Button" && t.Length > 0 && caixa) caixas.Add(Tuple.Create(t.Replace("&", ""), f));
                else if (c == "Button" && t.Length > 0) botoes.Add(Tuple.Create(t.Replace("&", ""), f));
                else if (c == "Static" && t.Length > msg.Length) msg = t;
                return true;
            }, IntPtr.Zero);
            if (botoes.Count == 0 || msg.Length == 0) { Log("sem texto/botões em '" + titulo + "'; devolvendo o nativo"); Win32.MostrarSemAtivar(h); return; }
            Log("mostrando '" + titulo + "' com " + botoes.Count + " botões");
            Mostrar(h, titulo, msg, botoes, caixas);
        }

        static void Responder(IntPtr h, Tuple<string, IntPtr> b)
        {
            Win32.PostMessage(b.Item2, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK no botão original
        }

        // confirmação do próprio Mac Skin (Reiniciar, Desligar...): o mesmo cartão, com "Cancelar" e a ação; acao só roda se confirmar
        public static void Perguntar(string titulo, string msg, string rotulo, Action acao, char icone = (char)0xE7E8, string cor = "#FF3B30")
        {
            var cancelar = Tuple.Create("Cancelar", IntPtr.Zero); var sim = Tuple.Create(rotulo, IntPtr.Zero);
            Mostrar(titulo, msg, new List<Tuple<string, IntPtr>> { sim, cancelar }, new List<Tuple<string, IntPtr>>(), sim, cancelar,
                b => { if (b == sim) acao(); }, null, icone, cor);
        }

        static void Mostrar(IntPtr nativo, string titulo, string msg, List<Tuple<string, IntPtr>> botoes, List<Tuple<string, IntPtr>> caixas)
        {
            Mostrar(titulo, msg, botoes, caixas, botoes.FirstOrDefault(b => b.Item1 == "Sim") ?? botoes.First(), botoes.FirstOrDefault(b => b.Item1 == "Não") ?? botoes.Last(),
                b => Responder(nativo, b), item => Win32.PostMessage(item.Item2, 0x00F5, IntPtr.Zero, IntPtr.Zero), (char)0xE7BA, "#FF9F0A");
        }

        static void Mostrar(string titulo, string msg, List<Tuple<string, IntPtr>> botoes, List<Tuple<string, IntPtr>> caixas,
            Tuple<string, IntPtr> padrao, Tuple<string, IntPtr> negativo, Action<Tuple<string, IntPtr>> responder, Action<Tuple<string, IntPtr>> alternar, char icone, string cor)
        {
            bool c = Comum.TemaClaro();
            Brush texto = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF"), suave = Comum.Cor(c ? "#99000000" : "#B3FFFFFF");
            bool respondido = false;
            if (janela != null) janela.Close();
            var w = new Window
            {
                Title = "Mac Skin Confirmação", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                Topmost = true, Background = Brushes.Transparent, Width = L, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
            };
            janela = w;
            Action<Tuple<string, IntPtr>> escolher = b => { if (respondido) return; respondido = true; w.Close(); responder(b); };

            var iconeVidro = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Comum.Cor((c ? "#26" : "#33") + cor.Substring(1)), VerticalAlignment = VerticalAlignment.Top };
            iconeVidro.Child = new TextBlock { Text = icone.ToString(), FontFamily = Icones, FontSize = 22, Foreground = Comum.Cor(cor), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cabeca = new StackPanel { Margin = new Thickness(14, 2, 0, 0) };
            cabeca.Children.Add(new TextBlock { Text = titulo, FontFamily = Fonte, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = texto, TextWrapping = TextWrapping.Wrap });
            cabeca.Children.Add(new TextBlock { Text = msg, FontFamily = Fonte, FontSize = 13, Foreground = suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            var topo = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(iconeVidro, System.Windows.Controls.Dock.Left);
            topo.Children.Add(iconeVidro); topo.Children.Add(cabeca);

            var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            foreach (var b in botoes.AsEnumerable().Reverse())
            {
                var item = b;
                bool destaque = item == padrao;
                var rotulo = new TextBlock { Text = item.Item1, FontFamily = Fonte, FontSize = 13, FontWeight = destaque ? FontWeights.SemiBold : FontWeights.Normal, Foreground = destaque ? Brushes.White : texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var fundo = Comum.Cor(destaque ? "#FF007AFF" : (c ? "#1A000000" : "#26FFFFFF")); var realce = Comum.Cor(destaque ? "#FF2E91FF" : (c ? "#2E000000" : "#40FFFFFF"));
                var bt = new Border { MinWidth = 96, Height = 32, CornerRadius = new CornerRadius(16), Background = fundo, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, Child = rotulo };
                bt.MouseEnter += delegate { bt.Background = realce; }; bt.MouseLeave += delegate { bt.Background = fundo; };
                bt.MouseLeftButtonUp += delegate { escolher(item); };
                linha.Children.Add(bt);
            }

            var pilha = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
            pilha.Children.Add(topo);
            foreach (var cx in caixas)
            {
                var item = cx; bool marcada = false;
                var quadro = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), BorderBrush = suave, VerticalAlignment = VerticalAlignment.Center };
                var marca = new TextBlock { Text = ((char)0xE73E).ToString(), FontFamily = Icones, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
                quadro.Child = marca;
                var linhaCx = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(58, 14, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                linhaCx.Children.Add(quadro);
                linhaCx.Children.Add(new TextBlock { Text = item.Item1, FontFamily = Fonte, FontSize = 13, Foreground = texto, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center });
                linhaCx.MouseLeftButtonUp += delegate
                {
                    marcada = !marcada;
                    quadro.Background = marcada ? Comum.Cor("#FF007AFF") : Brushes.Transparent; quadro.BorderBrush = marcada ? Comum.Cor("#FF007AFF") : suave;
                    marca.Visibility = marcada ? Visibility.Visible : Visibility.Collapsed;
                    alternar(item);   // alterna a caixa original
                };
                pilha.Children.Add(linhaCx);
            }
            pilha.Children.Add(linha);
            var aro = new Border { CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(4), BorderBrush = Comum.Cor(c ? "#8CEDEDEF" : "#8C3A3A3E"), IsHitTestVisible = false };
            var cartao = new Border
            {
                CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(1),
                BorderBrush = c ? Comum.Degrade("#F2FFFFFF", "#66FFFFFF") : Comum.Degrade("#B3FFFFFF", "#33FFFFFF"),
                Background = c ? Comum.Degrade("#55FFFFFF", "#14FFFFFF") : Comum.Degrade("#40FFFFFF", "#0EFFFFFF"), Child = pilha
            };
            var raiz = new Grid(); raiz.Children.Add(aro); raiz.Children.Add(cartao);
            w.Content = raiz;

            w.SourceInitialized += delegate
            {
                var hw = new WindowInteropHelper(w).Handle;
                HwndSource.FromHwnd(hw).CompositionTarget.BackgroundColor = Colors.Transparent;
                Win32.Vidro(hw, c ? 0x1EF8F6F6 : 0x24383030, true);
            };
            w.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) escolher(negativo);
                else if (e.Key == Key.Enter) escolher(padrao);
            };
            // fechar por outro meio (Alt+F4) vale como "Não"; o nativo some quando for respondido
            w.Closed += delegate { if (!respondido) { respondido = true; responder(negativo); } if (janela == w) janela = null; };
            w.Show();
            w.UpdateLayout();
            w.Left = (SystemParameters.PrimaryScreenWidth - w.ActualWidth) / 2; w.Top = (SystemParameters.PrimaryScreenHeight - w.ActualHeight) / 2 - 40;   // centro da tela principal
            w.Activate();
            Log("janela: " + w.Left + "," + w.Top + " " + w.ActualWidth + "x" + w.ActualHeight + " vis=" + w.IsVisible);
        }
    }
}
