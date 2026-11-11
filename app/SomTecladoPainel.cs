// Mac Skin: bloco "Som de teclado" na Central de Controle (a caixa que abre pelo ícone da barra, ao lado do relógio).
// Chave liga/desliga, volume, perfil (com prévia) e "Mais opções" (modificadores, mouse, rolagem, espacial, variação,
// pausa em tela cheia, saída de áudio, atalho e a contagem de hoje). Tudo grava na hora em som-teclado.txt.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace MacSkin
{
    class BlocoSomTeclado
    {
        static readonly FontFamily Fonte = new FontFamily("Inter, Segoe UI");
        static readonly FontFamily Icones = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Brush Azul = Comum.Cor("#0A84FF");
        static readonly Brush Nada = Comum.Cor("#01FFFFFF");
        static readonly CultureInfo PtBr = new CultureInfo("pt-BR");

        public readonly StackPanel Raiz = new StackPanel();
        readonly Ellipse disco; readonly TextBlock discoIcone, nome, estado, perfilValor, setaPerfil, setaOpcoes, saidaValor, setaSaida, hoje, atalho;
        readonly MiniChave chave, cMod, cMouse, cRolagem, cTela;
        readonly Deslizante volume, espacial, variacao;
        readonly StackPanel listaPerfis = new StackPanel { Visibility = Visibility.Collapsed }, opcoes = new StackPanel { Visibility = Visibility.Collapsed }, listaSaidas = new StackPanel { Visibility = Visibility.Collapsed };
        Brush texto = Brushes.White, sub = Comum.Cor("#99FFFFFF"), sobre = Comum.Cor("#26FFFFFF"), apagado = Comum.Cor("#40FFFFFF");
        bool montando;

        public BlocoSomTeclado()
        {
            // cabeçalho: disco com o teclado, nome e estado, chave
            var cab = new Grid { Margin = new Thickness(4, 2, 2, 6) };
            cab.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cab.ColumnDefinitions.Add(new ColumnDefinition());
            cab.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var g = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) };
            disco = new Ellipse(); discoIcone = new TextBlock { Text = "", FontFamily = Icones, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(disco); g.Children.Add(discoIcone);
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nome = T("Som de teclado", 13, FontWeights.SemiBold); estado = T("", 11, FontWeights.Normal);
            textos.Children.Add(nome); textos.Children.Add(estado);
            chave = new MiniChave(v => SomTeclado.Alterar(a => a.Ligado = v)) { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(textos, 1); Grid.SetColumn(chave, 2);
            cab.Children.Add(g); cab.Children.Add(textos); cab.Children.Add(chave);
            Raiz.Children.Add(cab);

            // volume
            volume = new Deslizante(v => SomTeclado.Alterar(a => a.Volume = v), () => SomTeclado.Clique());
            Raiz.Children.Add(LinhaDeslizante("", volume, ""));

            // perfil
            perfilValor = T("", 12, FontWeights.Normal); setaPerfil = Seta();
            Raiz.Children.Add(Linha("Perfil", perfilValor, setaPerfil, () => Abrir(listaPerfis, setaPerfil)));
            Raiz.Children.Add(listaPerfis);

            // mais opções
            setaOpcoes = Seta();
            Raiz.Children.Add(Linha("Mais opções", null, setaOpcoes, () => Abrir(opcoes, setaOpcoes)));
            Raiz.Children.Add(opcoes);
            cMod = new MiniChave(v => SomTeclado.Alterar(a => a.Modificadores = v));
            cMouse = new MiniChave(v => SomTeclado.Alterar(a => a.Mouse = v));
            cRolagem = new MiniChave(v => SomTeclado.Alterar(a => a.Rolagem = v));
            cTela = new MiniChave(v => SomTeclado.Alterar(a => a.PausaTelaCheia = v));
            opcoes.Children.Add(LinhaChave("Shift, Ctrl, Alt e Win", cMod));
            opcoes.Children.Add(LinhaChave("Clique do mouse", cMouse));
            opcoes.Children.Add(LinhaChave("Rolagem", cRolagem));
            opcoes.Children.Add(LinhaChave("Pausar em tela cheia", cTela));
            espacial = new Deslizante(v => SomTeclado.Alterar(a => a.Espacial = v), () => SomTeclado.Previa(SomTeclado.Atual.Perfil));
            variacao = new Deslizante(v => SomTeclado.Alterar(a => a.Variacao = v), () => SomTeclado.Previa(SomTeclado.Atual.Perfil));
            opcoes.Children.Add(Rotulo("Som espacial")); opcoes.Children.Add(LinhaDeslizante("", espacial, ""));
            opcoes.Children.Add(Rotulo("Variação de tom")); opcoes.Children.Add(LinhaDeslizante("", variacao, ""));
            saidaValor = T("", 12, FontWeights.Normal); saidaValor.TextTrimming = TextTrimming.CharacterEllipsis; saidaValor.MaxWidth = 120; setaSaida = Seta();
            opcoes.Children.Add(Linha("Saída", saidaValor, setaSaida, () => { MontarSaidas(); Abrir(listaSaidas, setaSaida); }));
            opcoes.Children.Add(listaSaidas);
            atalho = T("", 11, FontWeights.Normal); atalho.Margin = new Thickness(8, 4, 8, 0);
            hoje = T("", 11, FontWeights.Normal); hoje.Margin = new Thickness(8, 2, 8, 2);
            opcoes.Children.Add(atalho); opcoes.Children.Add(hoje);
            opcoes.Children.Add(Linha("Editar sons…", null, null, EditorSomTeclado.Abrir));
            opcoes.Children.Add(Linha("Pasta de sons…", null, null, AbrirPastaSons));

            SomTeclado.Mudou += () => { if (Raiz.IsVisible) Atualizar(); };
        }

        public void Tema(bool claro, Brush tx, Brush sb)
        {
            texto = tx; sobre = sb;
            sub = Comum.Cor(claro ? "#8C000000" : "#99FFFFFF");
            apagado = Comum.Cor(claro ? "#33000000" : "#40FFFFFF");
            foreach (var m in new[] { chave, cMod, cMouse, cRolagem, cTela }) m.Tema(apagado);
            foreach (var d in new[] { volume, espacial, variacao }) d.Tema(apagado);
            Atualizar();
        }

        public void Atualizar()
        {
            montando = true;
            try
            {
                var a = SomTeclado.Atual;
                chave.Definir(a.Ligado); cMod.Definir(a.Modificadores); cMouse.Definir(a.Mouse); cRolagem.Definir(a.Rolagem); cTela.Definir(a.PausaTelaCheia);
                volume.Definir(a.Volume); espacial.Definir(a.Espacial); variacao.Definir(a.Variacao);
                disco.Fill = a.Ligado ? Azul : apagado;
                discoIcone.Foreground = a.Ligado ? Brushes.White : texto;
                nome.Foreground = texto;
                estado.Foreground = sub;
                estado.Text = !a.Ligado ? "Desligado" : SomTeclado.PausadoAgora ? "Pausado (tela cheia)" : SomTeclado.NomePerfil(a.Perfil);
                perfilValor.Text = SomTeclado.NomePerfil(a.Perfil);
                saidaValor.Text = NomeSaida(a.Saida);
                atalho.Text = "Atalho: " + SomTeclado.AtalhoLegivel(a.Atalho);
                int nHoje = SomTeclado.TeclasHoje;
                hoje.Text = "Hoje: " + nHoje.ToString("N0", PtBr) + (nHoje == 1 ? " tecla" : " teclas");
                foreach (var t in new[] { perfilValor, saidaValor, atalho, hoje }) t.Foreground = sub;
                foreach (var t in new[] { setaPerfil, setaOpcoes, setaSaida }) t.Foreground = sub;
                PintarTextos(Raiz);
                MontarPerfis();
                if (listaSaidas.Visibility == Visibility.Visible) MontarSaidas();
            }
            finally { montando = false; }
        }

        void PintarTextos(Panel p)
        {
            foreach (var c in p.Children)
            {
                var b = c as Border;
                if (b != null && b.Tag as string == "linha") { var g = b.Child as Grid; if (g != null && g.Children.Count > 0) { var t = g.Children[0] as TextBlock; if (t != null) t.Foreground = texto; } }
                var r = c as TextBlock; if (r != null && r.Tag as string == "rotulo") r.Foreground = sub;
                var gl = c as Grid; if (gl != null && gl.Tag as string == "deslizante") foreach (var x in gl.Children) { var t = x as TextBlock; if (t != null) t.Foreground = sub; }
                var sp = c as Panel; if (sp != null) PintarTextos(sp);
                if (b != null && b.Tag as string == "linha-chave") { var g = b.Child as Grid; if (g != null) ((TextBlock)g.Children[0]).Foreground = texto; }
            }
        }

        void MontarPerfis()
        {
            listaPerfis.Children.Clear();
            var a = SomTeclado.Atual;
            foreach (var p in SomTeclado.Perfis)
            {
                var perfil = p;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tocar = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Nada, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Ouvir", Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = "", FontFamily = Icones, FontSize = 10, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                tocar.MouseEnter += delegate { tocar.Background = sobre; }; tocar.MouseLeave += delegate { tocar.Background = Nada; };
                tocar.MouseLeftButtonUp += (s, e) => { e.Handled = true; SomTeclado.Previa(perfil.Id); };
                var t = T(perfil.Nome, 12, perfil.Id == a.Perfil ? FontWeights.SemiBold : FontWeights.Normal); t.Foreground = texto; t.VerticalAlignment = VerticalAlignment.Center;
                if (perfil.Licenca != null && perfil.Origem != null && !perfil.Origem.StartsWith("sintetizado")) t.ToolTip = "Licença: " + perfil.Licenca;
                var ok = new TextBlock { Text = perfil.Id == a.Perfil ? "" : "", FontFamily = Icones, FontSize = 11, Foreground = Azul, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(t, 1); Grid.SetColumn(ok, 2);
                g.Children.Add(tocar); g.Children.Add(t); g.Children.Add(ok);
                var linha = Clicavel(g, () => { SomTeclado.Alterar(x => x.Perfil = perfil.Id); SomTeclado.Previa(perfil.Id); });
                linha.Margin = new Thickness(12, 0, 0, 0);
                listaPerfis.Children.Add(linha);
            }
        }

        void MontarSaidas()
        {
            listaSaidas.Children.Clear();
            var a = SomTeclado.Atual;
            Action<string, string> add = (id, n) =>
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = T(n, 12, FontWeights.Normal); t.Foreground = texto; t.TextTrimming = TextTrimming.CharacterEllipsis;
                var ok = new TextBlock { Text = (a.Saida ?? "") == id ? "" : "", FontFamily = Icones, FontSize = 11, Foreground = Azul, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                Grid.SetColumn(ok, 1); g.Children.Add(t); g.Children.Add(ok);
                var l = Clicavel(g, () => { SomTeclado.Alterar(x => x.Saida = id); listaSaidas.Visibility = Visibility.Collapsed; setaSaida.Text = ""; Atualizar(); });
                l.Margin = new Thickness(12, 0, 0, 0);
                listaSaidas.Children.Add(l);
            };
            add("", "Padrão do Windows");
            foreach (var s in Wasapi.Saidas()) add(s.Key, s.Value.Length > 0 ? s.Value : s.Key);
        }

        static string NomeSaida(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Padrão";
            foreach (var s in Wasapi.Saidas()) if (s.Key == id) return s.Value;
            return "Padrão (a escolhida não está ligada)";
        }

        public static void AbrirPastaSons()
        {
            try
            {
                string d = Path.Combine(Substituicao.PastaDados, "sons");
                Directory.CreateDirectory(d);
                string leia = Path.Combine(d, "LEIA-ME.txt");
                if (!File.Exists(leia))
                    File.WriteAllText(leia,
                        "Pacotes de som do Som de teclado do Mac Skin\r\n\r\n" +
                        "Crie uma pasta por perfil aqui dentro, com arquivos WAV curtos:\r\n" +
                        "  comum-desce-1.wav, comum-desce-2.wav ...   (obrigatório)\r\n" +
                        "  comum-sobe-1.wav ...\r\n" +
                        "  espaco-desce-*.wav, enter-desce-*.wav, apagar-desce-*.wav, mod-desce-*.wav (e os -sobe-)\r\n" +
                        "  mouse-desce-*.wav, mouse-sobe-*.wav, rolagem-desce-*.wav\r\n" +
                        "e um perfil.txt com:  nome=Meu teclado   licenca=CC0   fonte=<de onde veio>\r\n\r\n" +
                        "Use só gravações suas ou de licença livre. O perfil aparece depois de reiniciar o Mac Skin.\r\n", new System.Text.UTF8Encoding(false));
                Process.Start("explorer.exe", "\"" + d + "\"");
            }
            catch (Exception e) { Comum.Log("som-teclado.log", "pasta de sons: " + e.Message); }
        }

        // ---------- peças ----------
        static TextBlock T(string s, double tam, FontWeight peso) { return new TextBlock { Text = s, FontFamily = Fonte, FontSize = tam, FontWeight = peso }; }
        static TextBlock Seta() { return new TextBlock { Text = "", FontFamily = Icones, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }; }
        TextBlock Rotulo(string s) { var t = T(s, 11, FontWeights.Normal); t.Margin = new Thickness(8, 6, 8, 0); t.Tag = "rotulo"; return t; }

        void Abrir(Panel p, TextBlock seta)
        {
            bool abrir = p.Visibility != Visibility.Visible;
            p.Visibility = abrir ? Visibility.Visible : Visibility.Collapsed;
            seta.Text = abrir ? "" : "";
            if (abrir) Atualizar();
        }

        Border Clicavel(UIElement conteudo, Action acao)
        {
            var b = new Border { Child = conteudo, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(6), Background = Nada, Cursor = Cursors.Hand };
            b.MouseEnter += delegate { b.Background = sobre; };
            b.MouseLeave += delegate { b.Background = Nada; };
            b.MouseLeftButtonUp += (s, e) => { if (!montando) acao(); };
            return b;
        }

        Border Linha(string titulo, TextBlock valor, TextBlock seta, Action acao)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = T(titulo, 12, FontWeights.Normal); g.Children.Add(t);
            if (valor != null) { valor.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(valor, 1); g.Children.Add(valor); }
            if (seta != null) { Grid.SetColumn(seta, 2); g.Children.Add(seta); }
            var b = Clicavel(g, acao); b.Tag = "linha";
            return b;
        }

        Border LinhaChave(string titulo, MiniChave c)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = T(titulo, 12, FontWeights.Normal); t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, 1); g.Children.Add(t); g.Children.Add(c);
            return new Border { Child = g, Padding = new Thickness(8, 3, 6, 3), Tag = "linha-chave" };
        }

        Grid LinhaDeslizante(string esq, Deslizante d, string dir)
        {
            var g = new Grid { Margin = new Thickness(8, 2, 8, 4), Tag = "deslizante" };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var a = new TextBlock { Text = esq, FontFamily = Icones, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var b = new TextBlock { Text = dir, FontFamily = Icones, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(d, 1); Grid.SetColumn(b, 2);
            g.Children.Add(a); g.Children.Add(d); g.Children.Add(b);
            return g;
        }
    }

    // interruptor do Mac, menor (36×20): azul ligado, cinza desligado
    class MiniChave : Border
    {
        static readonly Brush Azul = Comum.Cor("#0A84FF");
        readonly Border bola; bool lig; Brush apagado = Comum.Cor("#40FFFFFF");
        readonly Action<bool> aoMudar;
        public MiniChave(Action<bool> aoMudar)
        {
            this.aoMudar = aoMudar;
            Width = 36; Height = 20; CornerRadius = new CornerRadius(10); Cursor = Cursors.Hand;
            bola = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            Child = bola; Definir(false);
            MouseLeftButtonUp += (s, e) => { e.Handled = true; Definir(!lig); aoMudar(lig); };
        }
        public bool Ligada { get { return lig; } }
        public void Tema(Brush apagado) { this.apagado = apagado; Definir(lig); }
        public void Definir(bool v)
        {
            lig = v; Background = v ? Azul : apagado;
            bola.HorizontalAlignment = v ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }

    // controle deslizante 0–100 (trilho fino, parte azul e bolinha branca)
    class Deslizante : Grid
    {
        static readonly Brush Azul = Comum.Cor("#0A84FF");
        readonly Border trilho, cheio; readonly Ellipse bola;
        readonly Action<int> aoMudar; readonly Action aoSoltar;
        int valor; bool arrastando;
        public Deslizante(Action<int> aoMudar, Action aoSoltar)
        {
            this.aoMudar = aoMudar; this.aoSoltar = aoSoltar;
            Height = 18; Background = Comum.Cor("#01FFFFFF"); Cursor = Cursors.Hand;
            trilho = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            cheio = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Background = Azul };
            bola = new Ellipse { Width = 14, Height = 14, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.35 } };
            Children.Add(trilho); Children.Add(cheio); Children.Add(bola);
            SizeChanged += delegate { Desenhar(); };
            MouseLeftButtonDown += (s, e) => { arrastando = true; CaptureMouse(); Mover(e); e.Handled = true; };
            MouseMove += (s, e) => { if (arrastando) Mover(e); };
            MouseLeftButtonUp += (s, e) => { if (!arrastando) return; arrastando = false; ReleaseMouseCapture(); e.Handled = true; if (aoSoltar != null) aoSoltar(); };
            LostMouseCapture += delegate { arrastando = false; };
            Tema(Comum.Cor("#40FFFFFF"));
        }
        public int Valor { get { return valor; } }
        public void Tema(Brush apagado) { trilho.Background = apagado; }
        public void Definir(int v) { if (arrastando) return; valor = Math.Max(0, Math.Min(100, v)); Desenhar(); }
        void Mover(MouseEventArgs e)
        {
            double w = ActualWidth - 14; if (w <= 0) return;
            int v = (int)Math.Round(Math.Max(0, Math.Min(1, (e.GetPosition(this).X - 7) / w)) * 100);
            if (v == valor) return;
            valor = v; Desenhar(); aoMudar(v);
        }
        void Desenhar()
        {
            double w = Math.Max(0, ActualWidth - 14);
            double x = w * valor / 100.0;
            bola.Margin = new Thickness(x, 0, 0, 0);
            cheio.Width = x + 7;
        }
    }
}
