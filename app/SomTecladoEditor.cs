// Mac Skin: janela "Som de Teclado" (abre pelo menu da Dock e pela Central de Controle).
// Lista de perfis à esquerda; à direita, o editor do perfil escolhido: nome, timbre (tom, corpo, clique, brilho, duração,
// som ao soltar), volume de cada tipo de tecla com ▶ para ouvir, e uma caixa para testar digitando.
// Cada ajuste grava na hora em %APPDATA%\Mac Skin Dados\sons-editados\<id>.txt e o perfil é refeito sem reiniciar nada.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace MacSkin
{
    class EditorSomTeclado
    {
        static EditorSomTeclado aberto;
        public static void Abrir()
        {
            try
            {
                if (aberto == null || !aberto.Aberta) aberto = new EditorSomTeclado();
                aberto.Mostrar();
            }
            catch (Exception e) { Comum.Log("som-teclado.log", "editor: " + e); }
        }
        public static Window Janela { get { return aberto != null && aberto.Aberta ? aberto.w : null; } }

        static readonly FontFamily Fonte = new FontFamily("Inter, Segoe UI");
        static readonly FontFamily Ico = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Brush Azul = Comum.Cor("#0A84FF");

        readonly Window w;
        readonly StackPanel lista = new StackPanel(), conteudo = new StackPanel { Margin = new Thickness(28, 0, 28, 24), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Stretch };
        readonly MiniChave chaveGeral;
        readonly TextBlock estadoGeral;
        Brush texto, textoSec, fundo, lateral, selecao, hover, pilula, campo, borda, apagado;
        string selId; EdicaoSom ed; bool montando;
        public bool Aberta = true;

        // controles do painel (refeitos a cada perfil escolhido)
        TextBox nome;
        readonly Deslizante[] timbre = new Deslizante[6];
        readonly TextBlock[] timbreValor = new TextBlock[6];
        readonly Deslizante[] grupos = new Deslizante[SomTeclado.NGrupos];
        readonly TextBlock[] gruposValor = new TextBlock[SomTeclado.NGrupos];

        public EditorSomTeclado()
        {
            Tema();
            w = new Window
            {
                Title = "Som de Teclado", Width = 960, Height = 700, MinWidth = 780, MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.Transparent,
                FontFamily = Fonte, FontSize = 13, UseLayoutRounding = true
            };
            // barra de rolagem fina, igual à do editor de Substituição de Texto
            w.Resources["TextoSec"] = textoSec;
            w.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
                "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "<Style TargetType='Thumb' x:Key='Alca'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>" +
                "<Border CornerRadius='3' Background='{DynamicResource TextoSec}' Opacity='0.55'/></ControlTemplate></Setter.Value></Setter></Style>" +
                "<Style TargetType='ScrollBar'><Setter Property='Background' Value='Transparent'/><Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,2'><Track.Thumb><Thumb Style='{StaticResource Alca}'/></Track.Thumb></Track>" +
                "</Grid></ControlTemplate></Setter.Value></Setter></Style></ResourceDictionary>"));
            WindowChrome.SetWindowChrome(w,new WindowChrome { CaptionHeight = 52, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

            var raiz = new Grid();
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- lateral ----
            var lat = new Grid { Background = lateral };
            lat.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            lat.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lat.RowDefinitions.Add(new RowDefinition());
            lat.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var titulo = T("Som de Teclado", 13, FontWeights.SemiBold, textoSec); titulo.Margin = new Thickness(18, 0, 0, 0); titulo.VerticalAlignment = VerticalAlignment.Center; titulo.IsHitTestVisible = false;
            lat.Children.Add(titulo);
            var rotPerfis = T("PERFIS", 10.5, FontWeights.SemiBold, textoSec); rotPerfis.Margin = new Thickness(20, 6, 0, 6); Grid.SetRow(rotPerfis, 1);
            lat.Children.Add(rotPerfis);
            var rol = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lista }; Grid.SetRow(rol, 2);
            lat.Children.Add(rol);
            // rodapé: novo perfil e chave geral
            var rod = new StackPanel { Margin = new Thickness(12, 8, 12, 12) }; Grid.SetRow(rod, 3);
            var bNovo = Botao("", "Novo perfil", true); bNovo.HorizontalAlignment = HorizontalAlignment.Stretch; bNovo.Margin = new Thickness(0, 0, 0, 10);
            bNovo.MouseLeftButtonUp += delegate { Duplicar(); };
            bNovo.ToolTip = "Cria um perfil seu a partir do perfil escolhido";
            rod.Children.Add(bNovo);
            var gl = new Grid { Margin = new Thickness(6, 0, 4, 0) };
            gl.ColumnDefinitions.Add(new ColumnDefinition()); gl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tl = new StackPanel();
            tl.Children.Add(T("Som de teclado", 12.5, FontWeights.SemiBold, texto));
            estadoGeral = T("", 11, FontWeights.Normal, textoSec); tl.Children.Add(estadoGeral);
            chaveGeral = new MiniChave(v => { SomTeclado.Alterar(a => a.Ligado = v); AtualizarGeral(); }) { VerticalAlignment = VerticalAlignment.Center };
            chaveGeral.Tema(apagado);
            Grid.SetColumn(chaveGeral, 1); gl.Children.Add(tl); gl.Children.Add(chaveGeral);
            rod.Children.Add(gl);
            lat.Children.Add(rod);
            raiz.Children.Add(lat);

            // ---- painel ----
            var pan = new Grid { Background = fundo };
            pan.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            pan.RowDefinitions.Add(new RowDefinition());
            var botoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            botoes.Children.Add(BotaoJanela("", false, () => w.WindowState = WindowState.Minimized));
            botoes.Children.Add(BotaoJanela("", false, () => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
            botoes.Children.Add(BotaoJanela("", true, () => w.Close()));
            pan.Children.Add(botoes);
            var rolP = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = conteudo }; Grid.SetRow(rolP, 1);
            pan.Children.Add(rolP);
            Grid.SetColumn(pan, 1);
            raiz.Children.Add(pan);
            w.Content = raiz;

            Action mudou = () => { if (!Aberta) return; AtualizarGeral(); MontarLista(); };
            SomTeclado.Mudou += mudou;
            w.Closed += delegate { Aberta = false; SomTeclado.Mudou -= mudou; GravarNome(); };
            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !(Keyboard.FocusedElement is TextBox)) w.Close(); };
            w.SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(w).Handle;
                HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
                int escuro = Comum.TemaClaro() ? 0 : 1; DwmSetWindowAttribute(h, 20, ref escuro, 4);
                int fundoDwm = 3; DwmSetWindowAttribute(h, 38, ref fundoDwm, 4);
                int canto = 2; DwmSetWindowAttribute(h, 33, ref canto, 4);
            };

            selId = SomTeclado.Atual.Perfil;
            if (SomTeclado.PerfilPorId(selId) == null && SomTeclado.Perfis.Length > 0) selId = SomTeclado.Perfis[0].Id;
            AtualizarGeral(); MontarLista(); MontarPainel();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);

        // testes: desenha a janela num PNG fora da tela, sem ativar nem roubar o foco (perfil = o que fica escolhido)
        public static void Foto(string arq, string perfil, double rolar)
        {
            var e = new EditorSomTeclado();
            if (perfil != null) e.Escolher(perfil, false);
            var w = e.w;
            w.ShowActivated = false; w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -6000; w.Top = 0;
            w.Show();
            Action bombear = () => w.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            bombear();
            if (rolar > 0) { var sv = ((Grid)((Grid)w.Content).Children[1]).Children[1] as ScrollViewer; if (sv != null) sv.ScrollToVerticalOffset(rolar); }
            w.UpdateLayout(); bombear();
            var raiz = (FrameworkElement)w.Content;
            int lw = (int)raiz.ActualWidth, lh = (int)raiz.ActualHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Comum.Cor(Comum.TemaClaro() ? "#FFF3F3F3" : "#FF202020"), null, new Rect(0, 0, lw, lh));
                dc.DrawRectangle(new VisualBrush(raiz), null, new Rect(0, 0, lw, lh));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(lw, lh, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var f = File.Create(arq)) enc.Save(f);
            w.Close();
        }

        public void Mostrar()
        {
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }

        void Tema()
        {
            bool c = Comum.TemaClaro();
            texto = Comum.Cor(c ? "#FF1D1D1F" : "#FFECECEC");
            textoSec = Comum.Cor(c ? "#FF6E6E73" : "#FF9A9A9F");
            fundo = Comum.Cor(c ? "#FFFFFFFF" : "#FF1E1E1E");
            lateral = Comum.Cor(c ? "#66F2F2F4" : "#59262628");
            selecao = Comum.Cor(c ? "#FF0063E1" : "#FF0A5BC4");
            hover = Comum.Cor(c ? "#14000000" : "#1FFFFFFF");
            pilula = Comum.Cor(c ? "#0F000000" : "#1AFFFFFF");
            campo = Comum.Cor(c ? "#0A000000" : "#14FFFFFF");
            borda = Comum.Cor(c ? "#1F000000" : "#1FFFFFFF");
            apagado = Comum.Cor(c ? "#26000000" : "#40FFFFFF");
        }

        void AtualizarGeral()
        {
            var a = SomTeclado.Atual;
            chaveGeral.Definir(a.Ligado);
            estadoGeral.Text = a.Ligado ? "Ligado · " + SomTeclado.AtalhoLegivel(a.Atalho) : "Desligado · " + SomTeclado.AtalhoLegivel(a.Atalho);
        }

        // ---------- lista de perfis ----------
        static string Descricao(SomTeclado.Perfil p)
        {
            if (p.Proprio) return "Seu perfil · a partir de " + SomTeclado.NomePerfilBruto(p.Base);
            if (p.Wav) return p.Editado ? "Pacote de WAV · volumes ajustados" : "Pacote de WAV";
            return p.Editado ? "Sintetizado · editado" : "Sintetizado";
        }

        void MontarLista()
        {
            lista.Children.Clear();
            string usado = SomTeclado.Atual.Perfil;
            foreach (var p in SomTeclado.Perfis)
            {
                var perfil = p; bool eh = p.Id == selId;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var icone = new TextBlock { Text = p.Wav ? "" : p.Proprio ? "" : "", FontFamily = Ico, FontSize = 14, Foreground = eh ? Brushes.White : textoSec, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var n = T(p.Nome, 13, FontWeights.SemiBold, eh ? Brushes.White : texto); n.TextTrimming = TextTrimming.CharacterEllipsis;
                var d = T(Descricao(p), 11, FontWeights.Normal, eh ? Comum.Cor("#D9FFFFFF") : textoSec); d.TextTrimming = TextTrimming.CharacterEllipsis;
                tx.Children.Add(n); tx.Children.Add(d);
                var ok = new TextBlock { Text = p.Id == usado ? "" : "", FontFamily = Ico, FontSize = 12, Foreground = eh ? Brushes.White : Azul, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Em uso" };
                Grid.SetColumn(tx, 1); Grid.SetColumn(ok, 2);
                g.Children.Add(icone); g.Children.Add(tx); g.Children.Add(ok);
                var b = new Border { Child = g, CornerRadius = new CornerRadius(8), Margin = new Thickness(10, 1, 10, 1), Padding = new Thickness(12, 7, 10, 7), Background = eh ? selecao : Brushes.Transparent, Cursor = Cursors.Hand };
                b.MouseEnter += delegate { if (perfil.Id != selId) b.Background = hover; };
                b.MouseLeave += delegate { if (perfil.Id != selId) b.Background = Brushes.Transparent; };
                b.MouseLeftButtonUp += delegate { Escolher(perfil.Id, true); };
                lista.Children.Add(b);
            }
        }

        // escolher um perfil na lista: passa a ser o perfil em uso (o que se edita é o que se ouve) e toca a prévia
        void Escolher(string id, bool tocar)
        {
            GravarNome();
            selId = id;
            if (SomTeclado.Atual.Perfil != id) SomTeclado.Alterar(a => a.Perfil = id);
            MontarLista(); MontarPainel();
            if (tocar) SomTeclado.Previa(id);
        }

        // ---------- painel do perfil ----------
        void MontarPainel()
        {
            montando = true;
            conteudo.Children.Clear();
            var p = SomTeclado.PerfilPorId(selId);
            if (p == null) { conteudo.Children.Add(T("Nenhum perfil.", 13, FontWeights.Normal, textoSec)); montando = false; return; }
            ed = CarregarEdicao(p);

            // cabeçalho: nome editável, origem e ações
            nome = new TextBox { Text = p.Nome, FontSize = 24, FontWeight = FontWeights.SemiBold, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = texto, CaretBrush = texto, SelectionBrush = Azul, Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 2), ToolTip = "Clique para renomear" };
            nome.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GravarNome(); Keyboard.ClearFocus(); e.Handled = true; } };
            nome.LostFocus += delegate { GravarNome(); };
            conteudo.Children.Add(nome);
            conteudo.Children.Add(T(Origem(p), 12, FontWeights.Normal, textoSec));
            var acoes = new WrapPanel { Margin = new Thickness(0, 14, 0, 18) };
            var bOuvir = Botao("", "Ouvir", true); bOuvir.MouseLeftButtonUp += delegate { SomTeclado.Previa(selId); };
            var bDup = Botao("", "Duplicar", false); bDup.MouseLeftButtonUp += delegate { Duplicar(); };
            bDup.ToolTip = "Cria um perfil seu com estes ajustes";
            acoes.Children.Add(bOuvir); acoes.Children.Add(bDup);
            if (p.Editado && !p.Proprio)
            {
                var bRest = Botao("", "Restaurar original", false); bRest.MouseLeftButtonUp += delegate { Restaurar(); };
                acoes.Children.Add(bRest);
            }
            else if (p.Proprio)
            {
                var bZerar = Botao("", "Zerar ajustes", false); bZerar.MouseLeftButtonUp += delegate { Restaurar(); };
                bZerar.ToolTip = "Volta todos os controles ao som de " + SomTeclado.NomePerfilBruto(p.Base);
                var bExc = Botao("", "Excluir", false); bExc.MouseLeftButtonUp += delegate { Excluir(); };
                acoes.Children.Add(bZerar); acoes.Children.Add(bExc);
            }
            if (p.Wav)
            {
                var bPasta = Botao("", "Abrir pasta dos WAV", false); bPasta.MouseLeftButtonUp += delegate { AbrirPasta(p.Origem); };
                acoes.Children.Add(bPasta);
            }
            conteudo.Children.Add(acoes);

            // timbre
            var cTimbre = Cartao("Timbre", p.Wav ? "Este perfil vem de arquivos WAV: o timbre está nos arquivos. Aqui você ajusta só o som ao soltar e os volumes." : "Arraste e solte para ouvir. Duplo clique volta ao original.");
            string[] nomes = { "Tom", "Corpo", "Clique", "Brilho", "Duração", "Som ao soltar" };
            string[] esq = { "Grave", "Fino", "Macio", "Abafado", "Curta", "Mudo" };
            string[] dir = { "Agudo", "Encorpado", "Estalado", "Brilhante", "Longa", "Cheio" };
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                bool liberado = !p.Wav || k == 5;
                timbre[k] = new Deslizante(v => { if (montando) return; DefinirTimbre(k, v); timbreValor[k].Text = TextoTimbre(k, v); }, () => Salvar(-1));
                timbre[k].Tema(apagado);
                timbre[k].PreviewMouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount != 2) return;
                    e.Handled = true; int neutro = k == 5 ? 100 : EdicaoSom.Neutro;
                    DefinirTimbre(k, neutro); timbre[k].Definir(neutro); timbreValor[k].Text = TextoTimbre(k, neutro); Salvar(-1);
                };
                timbreValor[k] = T("", 11.5, FontWeights.Normal, textoSec);
                var linha = LinhaControle(nomes[k], esq[k], timbre[k], dir[k], timbreValor[k], null);
                if (!liberado) { linha.IsEnabled = false; linha.Opacity = 0.38; }
                cTimbre.Item2.Children.Add(linha);
                int val = LerTimbre(k); timbre[k].Definir(val); timbreValor[k].Text = TextoTimbre(k, val);
            }
            conteudo.Children.Add(cTimbre.Item1);

            // volume por tecla
            var cVol = Cartao("Volume por tipo de tecla", "100% é o volume original do perfil; vai até 150%. O volume geral fica na Central de Controle.");
            string[] gn = { "Letras, números e símbolos", "Espaço", "Enter", "Apagar (Backspace e Delete)", "Shift, Ctrl, Alt e Win", "Clique do mouse", "Rolagem do mouse" };
            for (int g = 0; g < SomTeclado.NGrupos; g++)
            {
                int k = g;
                grupos[k] = new Deslizante(v => { if (montando) return; ed.Grupos[k] = v; gruposValor[k].Text = EdicaoSom.PorCento(v) + "%"; }, () => Salvar(k));
                grupos[k].Tema(apagado);
                grupos[k].PreviewMouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount != 2) return;
                    e.Handled = true; ed.Grupos[k] = EdicaoSom.GrupoNeutro; grupos[k].Definir(EdicaoSom.GrupoNeutro); gruposValor[k].Text = "100%"; Salvar(k);
                };
                gruposValor[k] = T("", 11.5, FontWeights.Normal, textoSec);
                var tocar = BotaoRedondo("", "Ouvir");
                tocar.MouseLeftButtonUp += (s, e) => { e.Handled = true; SomTeclado.PreviaGrupo(selId, k); };
                cVol.Item2.Children.Add(LinhaControle(gn[k], null, grupos[k], null, gruposValor[k], tocar));
                grupos[k].Definir(ed.Grupos[k]); gruposValor[k].Text = EdicaoSom.PorCento(ed.Grupos[k]) + "%";
            }
            var aviso = T("Mouse e rolagem só tocam se estiverem ligados em Central de Controle › Som de teclado › Mais opções.", 11, FontWeights.Normal, textoSec);
            aviso.TextWrapping = TextWrapping.Wrap; aviso.Margin = new Thickness(0, 8, 0, 0);
            cVol.Item2.Children.Add(aviso);
            conteudo.Children.Add(cVol.Item1);

            // testar
            var cTeste = Cartao("Testar", "Digite aqui para ouvir o perfil, mesmo com o som de teclado desligado.");
            var caixa = new TextBox { Height = 64, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 14, Background = campo, Foreground = texto, CaretBrush = texto, SelectionBrush = Azul, BorderBrush = borda, BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            caixa.PreviewKeyDown += (s, e) => { if (!e.IsRepeat) SomTeclado.TesteTecla(selId, Vk(e), true); };
            caixa.PreviewKeyUp += (s, e) => SomTeclado.TesteTecla(selId, Vk(e), false);
            cTeste.Item2.Children.Add(caixa);
            conteudo.Children.Add(cTeste.Item1);

            var rodape = T("Os ajustes são gravados na hora em " + EdicaoSom.Pasta + ". Para usar gravações suas, crie um pacote de WAV na pasta de sons.", 11, FontWeights.Normal, textoSec);
            rodape.TextWrapping = TextWrapping.Wrap; rodape.Margin = new Thickness(2, 0, 0, 6);
            conteudo.Children.Add(rodape);
            var lPasta = T("Abrir a pasta de sons…", 11.5, FontWeights.SemiBold, Azul); lPasta.Cursor = Cursors.Hand; lPasta.Margin = new Thickness(2, 0, 0, 0);
            lPasta.MouseLeftButtonUp += delegate { BlocoSomTeclado.AbrirPastaSons(); };
            conteudo.Children.Add(lPasta);
            montando = false;
        }

        static uint Vk(KeyEventArgs e)
        {
            var k = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            return (uint)KeyInterop.VirtualKeyFromKey(k);
        }

        static string Origem(SomTeclado.Perfil p)
        {
            if (p.Proprio) return "Seu perfil, feito a partir de " + SomTeclado.NomePerfilBruto(p.Base) + ".";
            if (p.Wav) return "Pacote de WAV em " + p.Origem + (string.IsNullOrEmpty(p.Licenca) ? "" : " · licença " + p.Licenca);
            return p.Editado ? "Perfil do Mac Skin, sintetizado, com os seus ajustes." : "Perfil do Mac Skin, sintetizado na hora (sem arquivos de áudio).";
        }

        EdicaoSom CarregarEdicao(SomTeclado.Perfil p)
        {
            var e = EdicaoSom.LerTodas().FirstOrDefault(x => x.Id == p.Id);
            if (e == null) e = new EdicaoSom { Id = p.Id, Base = p.Base ?? p.Id };
            return e;
        }

        int LerTimbre(int k)
        {
            switch (k) { case 0: return ed.Tom; case 1: return ed.Corpo; case 2: return ed.Clique; case 3: return ed.Brilho; case 4: return ed.Duracao; default: return ed.Soltar; }
        }
        void DefinirTimbre(int k, int v)
        {
            switch (k) { case 0: ed.Tom = v; break; case 1: ed.Corpo = v; break; case 2: ed.Clique = v; break; case 3: ed.Brilho = v; break; case 4: ed.Duracao = v; break; default: ed.Soltar = v; break; }
        }
        static string TextoTimbre(int k, int v)
        {
            if (k == 5) return v + "%";
            if (v == EdicaoSom.Neutro) return "original";
            int d = v - EdicaoSom.Neutro;
            return (d > 0 ? "+" : "−") + Math.Abs(d);
        }

        // grava, refaz o perfil e toca: a prévia do perfil (timbre) ou um toque do tipo de tecla mexido
        void Salvar(int grupo)
        {
            if (ed == null) return;
            string id = selId;
            bool eraEditado = SomTeclado.PerfilPorId(id) != null && SomTeclado.PerfilPorId(id).Editado;
            SomTeclado.SalvarEdicao(ed, () =>
            {
                if (grupo >= 0) SomTeclado.PreviaGrupo(id, grupo); else SomTeclado.Previa(id);
                if (!Aberta) return;
                MontarLista();
                if (!eraEditado && id == selId) MontarPainel();   // aparece o "Restaurar original"
            });
        }

        void GravarNome()
        {
            if (nome == null || ed == null || montando) return;
            string n = nome.Text.Trim();
            var p = SomTeclado.PerfilPorId(ed.Id);
            if (n.Length == 0 || p == null) { if (p != null) nome.Text = p.Nome; return; }
            if (n == p.Nome) return;
            ed.Nome = n;
            SomTeclado.SalvarEdicao(ed, () => { if (Aberta) MontarLista(); });
        }

        void Duplicar()
        {
            GravarNome();
            var p = SomTeclado.PerfilPorId(selId); if (p == null) return;
            var n = (ed ?? new EdicaoSom { Id = p.Id, Base = p.Base ?? p.Id }).Copia();
            n.Id = EdicaoSom.NovoId(SomTeclado.Perfis.Select(x => x.Id));
            n.Base = p.Base ?? p.Id;
            n.Nome = (p.Proprio ? p.Nome : "Meu " + p.Nome) + (p.Proprio ? " (cópia)" : "");
            SomTeclado.SalvarEdicao(n, () =>
            {
                if (!Aberta) return;
                Escolher(n.Id, false);
                nome.Focus(); nome.SelectAll();
            });
        }

        void Restaurar()
        {
            var p = SomTeclado.PerfilPorId(selId); if (p == null) return;
            string id = selId;
            if (p.Proprio) { ed.Restaurar(); SomTeclado.SalvarEdicao(ed, () => { if (Aberta) MontarPainel(); SomTeclado.Previa(id); }); }
            else SomTeclado.ApagarEdicao(id, () => { if (Aberta) { MontarLista(); MontarPainel(); } SomTeclado.Previa(id); });
        }

        void Excluir()
        {
            var p = SomTeclado.PerfilPorId(selId); if (p == null || !p.Proprio) return;
            if (MessageBox.Show(w, "Excluir o perfil \"" + p.Nome + "\"? Não dá para desfazer.", "Som de Teclado", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            string baseId = p.Base ?? "grafite";
            nome = null;
            SomTeclado.ApagarEdicao(p.Id, () => { if (Aberta) Escolher(SomTeclado.PerfilPorId(baseId) != null ? baseId : SomTeclado.Perfis[0].Id, false); });
        }

        static void AbrirPasta(string d)
        {
            try { if (Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\""); }
            catch (Exception e) { Comum.Log("som-teclado.log", "abrir pasta: " + e.Message); }
        }

        // ---------- peças ----------
        static TextBlock T(string s, double tam, FontWeight peso, Brush cor) { return new TextBlock { Text = s, FontFamily = Fonte, FontSize = tam, FontWeight = peso, Foreground = cor }; }

        Tuple<Border, StackPanel> Cartao(string titulo, string sub)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(titulo, 14, FontWeights.SemiBold, texto));
            if (sub != null) { var s = T(sub, 11.5, FontWeights.Normal, textoSec); s.TextWrapping = TextWrapping.Wrap; s.Margin = new Thickness(0, 2, 0, 10); sp.Children.Add(s); }
            var b = new Border { Child = sp, Background = campo, BorderBrush = borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 16) };
            return Tuple.Create(b, sp);
        }

        Grid LinhaControle(string titulo, string esq, Deslizante d, string dir, TextBlock valor, UIElement antes)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(antes != null ? 200 : 120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            if (antes != null) g.Children.Add(antes);
            var t = T(titulo, 12.5, FontWeights.Normal, texto); t.VerticalAlignment = VerticalAlignment.Center; t.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(t, 1); g.Children.Add(t);
            if (esq != null) { var a = T(esq, 11, FontWeights.Normal, textoSec); a.VerticalAlignment = VerticalAlignment.Center; a.Margin = new Thickness(0, 0, 10, 0); a.Width = 58; a.TextAlignment = TextAlignment.Right; Grid.SetColumn(a, 2); g.Children.Add(a); }
            Grid.SetColumn(d, 3); g.Children.Add(d);
            if (dir != null) { var b = T(dir, 11, FontWeights.Normal, textoSec); b.VerticalAlignment = VerticalAlignment.Center; b.Margin = new Thickness(10, 0, 0, 0); b.Width = 62; Grid.SetColumn(b, 4); g.Children.Add(b); }
            valor.VerticalAlignment = VerticalAlignment.Center; valor.TextAlignment = TextAlignment.Right; Grid.SetColumn(valor, 5); g.Children.Add(valor);
            return g;
        }

        Border Botao(string icone, string rotulo, bool principal)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = icone, FontFamily = Ico, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = principal ? Brushes.White : texto, Margin = new Thickness(0, 0, 7, 0) });
            sp.Children.Add(T(rotulo, 12.5, FontWeights.Normal, principal ? Brushes.White : texto));
            var b = new Border { Child = sp, Background = principal ? Azul : pilula, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand };
            b.MouseEnter += delegate { b.Opacity = 0.82; }; b.MouseLeave += delegate { b.Opacity = 1; };
            b.MouseLeftButtonDown += delegate { b.Opacity = 0.6; };
            return b;
        }

        Border BotaoRedondo(string icone, string dica)
        {
            var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = pilula, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = dica,
                Child = new TextBlock { Text = icone, FontFamily = Ico, FontSize = 10, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            b.MouseEnter += delegate { b.Background = hover; }; b.MouseLeave += delegate { b.Background = pilula; };
            return b;
        }

        Border BotaoJanela(string icone, bool fechar, Action acao)
        {
            var t = new TextBlock { Text = icone, FontFamily = Ico, FontSize = 10, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 46, Height = 32, Background = Brushes.Transparent, Child = t };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            b.MouseEnter += delegate { b.Background = fechar ? Comum.Cor("#E81123") : hover; if (fechar) t.Foreground = Brushes.White; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; t.Foreground = texto; };
            b.MouseLeftButtonUp += delegate { acao(); };
            return b;
        }
    }
}
