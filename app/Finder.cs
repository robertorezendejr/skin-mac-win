// Mac Skin Finder: navegador de arquivos no visual do Finder do macOS (abre com MacSkin.exe --finder [pasta]).
// Barra lateral com vidro, abas (Ctrl+T), voltar/avançar (também nos botões do mouse), título, modos Lista/Ícones/Galeria,
// busca, caminho embaixo (clicável e editável, para copiar e colar).
// Arquivos de verdade do Windows: abrir, copiar/recortar/colar, arrastar, renomear, Lixeira, nova pasta,
// Propriedades e "Mostrar no Explorador" para o que não estiver aqui.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;

namespace MacSkin
{
    // um arquivo ou pasta na lista
    public class Entrada : INotifyPropertyChanged
    {
        public string Nome { get; set; }
        public string Caminho { get; set; }
        public bool EhPasta { get; set; }
        public long Tamanho { get; set; }
        public DateTime Data { get; set; }
        public string Tipo { get; set; }
        public string TamanhoTexto { get { return EhPasta ? "--" : Formatar(Tamanho); } }
        public string DataTexto { get { return Finder.DataMac(Data); } }
        ImageSource icone, grande;
        public ImageSource Icone { get { return icone; } set { icone = value; Avisar("Icone"); } }
        public ImageSource Grande { get { return grande; } set { grande = value; Avisar("Grande"); } }
        // cascata na lista: pasta aberta mostra o conteúdo logo abaixo, recuado pelo Nivel
        public int Nivel { get; set; }
        public bool Expansivel { get; set; }
        bool aberta;
        public bool Aberta { get { return aberta; } set { aberta = value; Avisar("Aberta"); Avisar("SetaTexto"); } }
        public string SetaTexto { get { return aberta ? "" : ""; } }   // chevron para baixo / para a direita
        public Visibility SetaVis { get { return Expansivel && EhPasta ? Visibility.Visible : Visibility.Hidden; } }
        public Thickness Recuo { get { return new Thickness(Nivel * 18, 0, 0, 0); } }
        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }

        public static string Formatar(long b)
        {
            if (b < 1000) return b + " bytes";
            string[] u = { "KB", "MB", "GB", "TB" };
            double v = b; int i = -1;
            do { v /= 1000; i++; } while (v >= 1000 && i < u.Length - 1);   // o Finder usa base 1000
            return v.ToString(v < 10 ? "0.#" : "0", Finder.PtBr) + " " + u[i];
        }
    }

    class Finder
    {
        public static readonly System.Globalization.CultureInfo PtBr = new System.Globalization.CultureInfo("pt-BR");
        const string Recentes = "::recentes";
        const string Lixo = "::lixeira";   // a Lixeira do Windows, lida direto de X:\$Recycle.Bin\<SID>
        static bool Virtual(string p) { return p == Recentes || p == Lixo; }
        static string NomeLocal(string p) { return p == Recentes ? "Recentes" : p == Lixo ? "Lixeira" : NomePasta(p); }

        Window w;
        ListView lista; ListBox icones;
        StackPanel lateral, caminhoBarra;
        TextBlock titulo, statusSelecao;
        TextBox busca, endereco;
        FrameworkElement limparLixo;
        Button bLimparLixo;
        Button voltar, avancar;
        ToggleButton vLista, vIcones, vGaleria;
        readonly ObservableCollection<Entrada> itens = new ObservableCollection<Entrada>();
        ICollectionView visao;
        string atual;
        List<string> historico = new List<string>();
        int pos = -1;
        // cada aba guarda o seu histórico; a ativa usa os campos acima
        class Aba { public List<string> Historico = new List<string>(); public int Pos = -1; }
        readonly List<Aba> abas = new List<Aba>();
        Aba ativa;
        UniformGrid abasGrade; Border abasFundo;
        string modo = "lista";
        string ordem = "Nome"; bool crescente = true;
        FileSystemWatcher vigia;
        DispatcherTimer recarregar, buscaAuto;
        CancellationTokenSource buscaCancel;
        bool emBusca;   // a lista mostra resultados da busca em subpastas (Enter), não o conteúdo da pasta
        Point arrastoInicio; bool podeArrastar;
        Canvas laco; FrameworkElement lacoRet;
        bool lacoAtivo; Point lacoInicio; List<object> lacoBase; Entrada lacoItem, pendente;
        IntPtr hwnd;
        Previa previa;

        static void Log(string m) { Comum.Log("finder.log", m); }

        // modo escolha (caixa de abrir/salvar de outro programa, ver Escolha.cs): quem escolhe decide o que o duplo clique
        // em arquivo faz (devolve true se tratou) e é avisado quando a pasta ou a seleção mudam
        public Func<List<Entrada>, bool> AoAbrir;
        public event Action Mudou;
        public Window Janela { get { return w; } }
        public string Atual { get { return atual; } }
        public bool PreviaAberta { get { return previa != null && previa.Aberta; } }
        public List<Entrada> Selecao() { return Selecionados().ToList(); }
        public static bool EhVirtual(string p) { return Virtual(p); }
        public void Entrar(string pasta) { Navegar(pasta, true); }
        void AvisarMudou() { var m = Mudou; if (m != null) m(); }

        string selecionarDepois;   // arquivo a selecionar quando a pasta terminar de carregar (vem do /select do Explorador)

        public Finder(string inicial, string selecionar = null)
        {
            selecionarDepois = selecionar;
            w = (Window)XamlReader.Parse(Xaml);
            lista = (ListView)w.FindName("lista"); icones = (ListBox)w.FindName("icones");
            laco = (Canvas)w.FindName("laco"); lacoRet = (FrameworkElement)w.FindName("lacoRet");
            lateral = (StackPanel)w.FindName("lateral"); caminhoBarra = (StackPanel)w.FindName("caminho");
            var colLateral = (ColumnDefinition)w.FindName("colLateral");
            colLateral.Width = new GridLength(larguraLateral);
            ((GridSplitter)w.FindName("divisa")).DragCompleted += delegate { larguraLateral = colLateral.ActualWidth; };
            titulo = (TextBlock)w.FindName("titulo"); statusSelecao = (TextBlock)w.FindName("statusSelecao"); busca = (TextBox)w.FindName("busca"); endereco = (TextBox)w.FindName("endereco");
            abasGrade = (UniformGrid)w.FindName("abas"); abasFundo = (Border)w.FindName("abasFundo");
            ativa = new Aba(); historico = ativa.Historico; abas.Add(ativa);
            ((Button)w.FindName("novaAba")).Click += delegate { NovaAba(atual); };
            VigiarArrastoAba();
            voltar = (Button)w.FindName("voltar"); avancar = (Button)w.FindName("avancar");
            vLista = (ToggleButton)w.FindName("vLista"); vIcones = (ToggleButton)w.FindName("vIcones"); vGaleria = (ToggleButton)w.FindName("vGaleria");

            icones.Resources["LarguraBloco"] = 112.0; icones.Resources["TamIcone"] = 64.0;
            visao = CollectionViewSource.GetDefaultView(itens);
            lista.ItemsSource = visao; icones.ItemsSource = visao;
            Tema();

            voltar.Click += delegate { if (pos > 0) { pos--; Navegar(historico[pos], false); } };
            avancar.Click += delegate { if (pos < historico.Count - 1) { pos++; Navegar(historico[pos], false); } };
            limparLixo = (FrameworkElement)w.FindName("limparLixo"); bLimparLixo = (Button)w.FindName("bLimparLixo");
            bLimparLixo.Click += delegate { EsvaziarLixeira(); };
            vLista.Click += delegate { Modo("lista"); };
            vIcones.Click += delegate { Modo("icones"); };
            vGaleria.Click += delegate { Modo("galeria"); };
            ((Button)w.FindName("mais")).Click += (s, e) => MenuMais((Button)s);
            ((Button)w.FindName("bMin")).Click += delegate { w.WindowState = WindowState.Minimized; };
            ((Button)w.FindName("bMax")).Click += delegate { w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            ((Button)w.FindName("bFechar")).Click += delegate { w.Close(); };
            w.StateChanged += delegate
            {
                ((TextBlock)((Button)w.FindName("bMax")).Content).Text = w.WindowState == WindowState.Maximized ? "" : "";
                // maximizada, a janela passa 8 px da tela: a margem mantém o conteúdo visível
                ((Grid)w.FindName("raiz")).Margin = w.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            };

            // digitou e parou: depois de 500 ms procura sozinho em todas as subpastas (o Enter continua procurando na hora)
            buscaAuto = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            buscaAuto.Tick += delegate { buscaAuto.Stop(); if (busca.Text.Trim().Length >= 2) BuscarTudo(busca.Text); };
            busca.TextChanged += delegate { Filtrar(); buscaAuto.Stop(); if (busca.IsKeyboardFocusWithin && busca.Text.Trim().Length >= 2) buscaAuto.Start(); };
            // caminho embaixo: clicar fora dos nomes (ou Ctrl+L / Alt+D) vira texto para copiar, colar ou digitar, como no Windows
            ((Border)w.FindName("caminhoFundo")).MouseLeftButtonDown += (s, e) => { if (endereco.Visibility != Visibility.Visible) { EditarCaminho(); e.Handled = true; } };
            endereco.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; IrPara(endereco.Text); }
                else if (e.Key == Key.Escape) { e.Handled = true; FecharCaminho(); lista.Focus(); }
            };
            endereco.LostKeyboardFocus += delegate { FecharCaminho(); };
            busca.KeyDown += (s, e) => { if (e.Key == Key.Enter) { buscaAuto.Stop(); BuscarTudo(busca.Text); } if (e.Key == Key.Escape) { busca.Text = ""; lista.Focus(); } };

            foreach (var c in new ListBox[] { lista, icones })
            {
                c.MouseDoubleClick += (s, e) => { if (Sob(e) != null) AbrirSelecionados(); };
                c.KeyDown += Teclas;
                // Espaço: a lista usaria para selecionar, por isso vem antes (Preview)
                c.PreviewKeyDown += (s, e) =>
                {
                    if (Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox) return;
                    if ((e.Key == Key.Right || e.Key == Key.Left) && TeclaCascata(e.Key)) { e.Handled = true; return; }
                    if (e.Key != Key.Space) return;
                    AlternarPrevia(); e.Handled = true;
                };
                c.SelectionChanged += delegate
                {
                    AtualizarStatusSelecao();
                    if (previa != null && previa.Aberta) previa.Mostrar(Selecionados().LastOrDefault());
                    AvisarMudou();
                };
                c.PreviewMouseLeftButtonDown += AoPressar;
                c.PreviewMouseMove += AoMoverLaco;
                c.PreviewMouseMove += AoArrastar;
                c.PreviewMouseLeftButtonUp += AoSoltarMouse;
                c.LostMouseCapture += delegate { if (lacoAtivo) { lacoAtivo = false; lacoRet.Visibility = Visibility.Collapsed; } };
                c.AllowDrop = true;
                c.Drop += AoSoltar;
                c.DragOver += (s, e) => { e.Effects = Efeito(e, DestinoSoltar(e)); e.Handled = true; };
                c.ContextMenu = new ContextMenu();   // sem um menu inicial o evento abaixo não dispara
                c.PreviewMouseRightButtonDown += (s, ev) =>
                {
                    var en = Sob(ev);
                    if (en != null)
                    {
                        if (!c.SelectedItems.Contains(en))
                        {
                            c.SelectedItem = en;
                        }
                    }
                    else
                    {
                        c.UnselectAll();
                    }
                };
                c.ContextMenuOpening += (s, ev) =>
                {
                    ev.Handled = true;
                    AbrirMenuContexto();
                };
            }
            lista.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var h = e.OriginalSource as GridViewColumnHeader;
                if (h == null || h.Column == null) return;
                string campo = (string)h.Column.Header == "Tamanho" ? "Tamanho" : (string)h.Column.Header == "Tipo" ? "Tipo" : (string)h.Column.Header == "Nome" ? "Nome" : "Data";
                crescente = ordem == campo ? !crescente : true; ordem = campo; Ordenar();
            }));
            // botões laterais do mouse: voltar e avançar, como no Explorador
            w.PreviewMouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.XButton1) { voltar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                else if (e.ChangedButton == MouseButton.XButton2) { avancar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            };
            w.PreviewKeyDown += (s, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                if (ctrl && e.Key == Key.F) { busca.Focus(); busca.SelectAll(); e.Handled = true; }
                else if (ctrl && e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { NovaJanela(atual); e.Handled = true; }
                else if (ctrl && e.Key == Key.W) { FecharAba(ativa); e.Handled = true; }
                else if (ctrl && e.Key == Key.T) { NovaAba(atual); e.Handled = true; }
                else if (ctrl && e.Key == Key.Tab && abas.Count > 1)
                {
                    int i = abas.IndexOf(ativa) + ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? abas.Count - 1 : 1);
                    Trocar(abas[i % abas.Count]); e.Handled = true;
                }
                else if (alt && e.SystemKey == Key.Left) { voltar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                else if (alt && e.SystemKey == Key.Right) { avancar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                else if (alt && e.SystemKey == Key.Up) { Subir(); e.Handled = true; }
                else if (e.Key == Key.F5) { Navegar(atual, false); e.Handled = true; }
                else if ((ctrl && e.Key == Key.L) || (alt && e.SystemKey == Key.D)) { EditarCaminho(); e.Handled = true; }
            };

            recarregar = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            recarregar.Tick += delegate { recarregar.Stop(); Carregar(atual, true); };

            w.SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(w).Handle;
                var src = HwndSource.FromHwnd(hwnd);
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                src.AddHook(Borda);
                Vidro();
            };
            Action remontar = () => { MontarLateral(); MarcarLateral(); };
            FavoritosMudaram += remontar;
            w.Closed += delegate { if (vigia != null) vigia.Dispose(); FavoritosMudaram -= remontar; };

            // soltar pastas no vazio da barra lateral as adiciona ao fim dos Favoritos
            var rolagem = (ScrollViewer)lateral.Parent;
            rolagem.Background = Brushes.Transparent; rolagem.AllowDrop = true;
            rolagem.DragOver += (s, e) => { e.Effects = PastasArrastadas(e).Count > 0 ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
            rolagem.Drop += (s, e) => { AdicionarFavoritos(PastasArrastadas(e), -1); e.Handled = true; };

            MontarLateral();
            Navegar(string.IsNullOrEmpty(inicial) || (!Directory.Exists(inicial) && inicial != Lixo) ?Pasta(Environment.SpecialFolder.UserProfile, "Downloads") : inicial, true);
            AtualizarStatusSelecao();
        }

        public void Mostrar()
        {
            w.Show(); w.Activate(); lista.Focus();
            // aberto por outro processo (o Explorador trocado pelo Finder), o Windows deixa a janela atrás da que estava em foco
            w.Topmost = true; w.Topmost = false;
        }

        // diagnóstico (MacSkin.exe --medir-finder pasta saida.txt): grava o tamanho real, em pixels, do ícone de cada linha e fecha
        public void Medir(string saida)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += delegate
            {
                t.Stop();
                var l = new List<string>();
                Action<DependencyObject> ande = null;
                ande = o =>
                {
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
                    {
                        var f = VisualTreeHelper.GetChild(o, i);
                        var img = f as Image; var en = img != null ? img.DataContext as Entrada : null;
                        if (en != null) l.Add((en.EhPasta ? "pasta   " : "arquivo ") + img.ActualWidth + "x" + img.ActualHeight + "  " + en.Nome);
                        ande(f);
                    }
                };
                ande(lista);
                File.WriteAllLines(saida, l);
                w.Close();
            };
            t.Start();
        }

        // ---------- aparência ----------
        bool claro;
        void Tema()
        {
            claro = Comum.TemaClaro();
            var r = w.Resources;
            r["Texto"] = Comum.Cor(claro ? "#FF1D1D1F" : "#FFECECEC");
            r["TextoSec"] = Comum.Cor(claro ? "#FF6E6E73" : "#FF9A9A9F");
            r["Fundo"] = Comum.Cor(claro ? "#FFFFFFFF" : "#FF1E1E1E");
            r["Lateral"] = Comum.Cor(claro ? "#66F2F2F4" : "#59262628");
            r["Zebra"] = Comum.Cor(claro ? "#FFF4F4F6" : "#FF262626");
            r["Selecao"] = Comum.Cor(claro ? "#FF0063E1" : "#FF0A5BC4");
            r["SelecaoLateral"] = Comum.Cor(claro ? "#1F000000" : "#2EFFFFFF");
            r["Hover"] = Comum.Cor(claro ? "#14000000" : "#1FFFFFFF");
            r["Pilula"] = Comum.Cor(claro ? "#0F000000" : "#1AFFFFFF");
            r["Campo"] = Comum.Cor(claro ? "#12000000" : "#1FFFFFFF");
            r["Borda"] = Comum.Cor(claro ? "#1A000000" : "#14FFFFFF");
            r["Azul"] = Comum.Cor("#FF0A84FF");
        }

        // bordas e cantos redimensionam antes de qualquer botão: sem isso os botões da barra de cima (que vão até a
        // beirada) tomam a borda e a janela não estica por ali
        IntPtr Borda(IntPtr h, int m, IntPtr wp, IntPtr lp, ref bool tratado)
        {
            if (m != 0x84 || w.WindowState == WindowState.Maximized) return IntPtr.Zero;   // WM_NCHITTEST
            long v = lp.ToInt64();
            int x = (short)(v & 0xFFFF), y = (short)((v >> 16) & 0xFFFF);
            Win32.RECT r; Win32.GetWindowRect(h, out r);
            int b = (int)(6 * PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice.M11);
            bool esq = x < r.L + b, dir = x >= r.R - b, cima = y < r.T + b, baixo = y >= r.B - b;
            int ht = cima && esq ? 13 : cima && dir ? 14 : baixo && esq ? 16 : baixo && dir ? 17 : esq ? 10 : dir ? 11 : cima ? 12 : baixo ? 15 : 0;
            if (ht == 0) return IntPtr.Zero;
            tratado = true; return new IntPtr(ht);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);
        void Vidro()
        {
            int escuro = claro ? 0 : 1; DwmSetWindowAttribute(hwnd, 20, ref escuro, 4);   // barra de título e bordas escuras
            int fundo = 3; DwmSetWindowAttribute(hwnd, 38, ref fundo, 4);                // vidro acrylic do Windows 11 atrás da barra lateral
            int canto = 2; DwmSetWindowAttribute(hwnd, 33, ref canto, 4);                // cantos redondos
        }

        // ---------- barra lateral ----------
        static string Pasta(Environment.SpecialFolder f, string sub = null)
        {
            string p = Environment.GetFolderPath(f);
            return sub == null ? p : System.IO.Path.Combine(p, sub);
        }

        void MontarLateral()
        {
            lateral.Children.Clear();
            Secao("Favoritos");
            Atalho("", "Recentes", Recentes);
            Atalho("", "Aplicativos", "shell:AppsFolder");
            Atalho("", "Mesa", Pasta(Environment.SpecialFolder.DesktopDirectory));
            Atalho("", "Documentos", Pasta(Environment.SpecialFolder.MyDocuments));
            Atalho("", "Downloads", Pasta(Environment.SpecialFolder.UserProfile, "Downloads"));
            Atalho("", "Imagens", Pasta(Environment.SpecialFolder.MyPictures));
            Atalho("", "Músicas", Pasta(Environment.SpecialFolder.MyMusic));
            Atalho("", "Vídeos", Pasta(Environment.SpecialFolder.MyVideos));
            var ultimoFixo = (Border)lateral.Children[lateral.Children.Count - 1];
            var favs = Favoritos();
            InsercaoFavorito(ultimoFixo, -2);
            for (int i = 0; i < favs.Count; i++)
            {
                if (!Directory.Exists(favs[i])) continue;
                string pasta = favs[i], nomeFav = System.IO.Path.GetFileName(pasta.TrimEnd('\\'));
                var b = Atalho("", nomeFav.Length > 0 ? nomeFav : pasta, pasta);
                b.ToolTip = pasta;
                b.ContextMenu = new ContextMenu();
                var tirar = new MenuItem { Header = "Remover da Barra Lateral" };
                tirar.Click += delegate { RemoverFavorito(pasta); };
                b.ContextMenu.Items.Add(tirar);
                InsercaoFavorito(b, i);
            }
            Secao("Localizações");
            foreach (var var in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
            {
                string p = Environment.GetEnvironmentVariable(var);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !lateral.Children.OfType<FrameworkElement>().Any(x => (x.Tag as string) == p))
                    GrupoNuvem(System.IO.Path.GetFileName(p).Replace("OneDrive - ", "OneDrive · "), p);
            }
            Atalho("", Environment.UserName, Pasta(Environment.SpecialFolder.UserProfile));
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                string nome = string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Network ? "Rede" : "Disco Local") : d.VolumeLabel;
                Atalho(d.DriveType == DriveType.Network ? "" : d.DriveType == DriveType.Removable ? "" : "", nome + " (" + d.Name.TrimEnd('\\') + ")", d.Name);
            }
            Atalho("", "Lixeira", Lixo);   // soltar aqui manda para a Lixeira
        }

        // ---------- pastas adicionadas aos Favoritos (arrastar para a barra lateral), uma por linha em finder-favoritos.txt ----------
        static readonly string arqFavoritos = System.IO.Path.Combine(Comum.Dir, "finder-favoritos.txt");
        static event Action FavoritosMudaram;

        static List<string> Favoritos()
        {
            try { if (File.Exists(arqFavoritos)) return File.ReadAllLines(arqFavoritos).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList(); }
            catch (Exception e) { Log("favoritos: " + e.Message); }
            return new List<string>();
        }

        static void SalvarFavoritos(List<string> favs)
        {
            try { File.WriteAllLines(arqFavoritos, favs); } catch (Exception e) { Log("salvar favoritos: " + e.Message); }
            if (FavoritosMudaram != null) FavoritosMudaram();
        }

        // indice < 0 põe no fim; uma pasta que já está nos Favoritos muda de lugar
        static void AdicionarFavoritos(List<string> pastas, int indice)
        {
            if (pastas.Count == 0) return;
            var favs = Favoritos();
            if (indice < 0 || indice > favs.Count) indice = favs.Count;
            foreach (var p in pastas)
            {
                int ja = favs.FindIndex(f => string.Equals(f.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                if (ja >= 0) { favs.RemoveAt(ja); if (indice > ja) indice--; }
            }
            favs.InsertRange(indice, pastas);
            SalvarFavoritos(favs);
        }

        static void RemoverFavorito(string pasta)
        {
            var favs = Favoritos();
            favs.RemoveAll(f => string.Equals(f, pasta, StringComparison.OrdinalIgnoreCase));
            SalvarFavoritos(favs);
        }

        static List<string> PastasArrastadas(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return new List<string>();
            return ((string[])e.Data.GetData(DataFormats.FileDrop)).Where(Directory.Exists).ToList();
        }

        // como no Finder do Mac: soltar uma pasta na borda de cima/baixo de um favorito insere um atalho ali (linha azul);
        // no meio continua copiando/movendo para dentro. indice = -2 é o último favorito fixo: só aceita inserir embaixo dele.
        void InsercaoFavorito(Border b, int indice)
        {
            Func<DragEventArgs, int> posicao = e =>
            {
                if (PastasArrastadas(e).Count == 0) return -1;
                double y = e.GetPosition(b).Y, h = b.ActualHeight;
                if (indice == -2) return y > h * 0.7 ? 0 : -1;
                if (y < h * 0.3) return indice;
                if (y > h * 0.7) return indice + 1;
                return -1;
            };
            Action<DragEventArgs> linha = e =>
            {
                int i = e == null ? -1 : posicao(e);
                b.BorderBrush = (Brush)w.Resources["Azul"];
                b.BorderThickness = i < 0 ? new Thickness(0) : i == indice ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
                b.Padding = new Thickness(8, 5 - b.BorderThickness.Top, 8, 5 - b.BorderThickness.Bottom);   // a linha não empurra os itens
            };
            b.PreviewDragOver += (s, e) =>
            {
                linha(e);
                if (posicao(e) >= 0) { e.Effects = DragDropEffects.Link; e.Handled = true; }
            };
            b.DragLeave += delegate { linha(null); };
            b.PreviewDrop += (s, e) =>
            {
                int i = posicao(e);
                linha(null);
                if (i < 0) return;
                AdicionarFavoritos(PastasArrastadas(e), i);
                e.Handled = true;
            };
        }

        void Secao(string nome)
        {
            lateral.Children.Add(new TextBlock { Text = nome, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 14, 0, 4), Foreground = (Brush)w.Resources["TextoSec"] });
        }

        // OneDrive vira um grupo expansível com as subpastas, incluindo os atalhos de bibliotecas do SharePoint
        // (pastas somente-leitura na raiz), como o painel de navegação do Explorador
        static double larguraLateral = 228;   // a largura arrastada vale para as próximas janelas
        static readonly HashSet<string> gruposFechados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void GrupoNuvem(string nome, string raiz)
        {
            string[] pastas;
            try
            {
                pastas = new DirectoryInfo(raiz).GetDirectories()
                    .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(d => d.FullName).ToArray();
            }
            catch { pastas = new string[0]; }

            bool aberto = !gruposFechados.Contains(raiz);
            var seta = new TextBlock { Text = aberto ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Width = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)w.Resources["TextoSec"], Background = Brushes.Transparent, Visibility = pastas.Length > 0 ? Visibility.Visible : Visibility.Hidden };
            var pai = Atalho("", nome, raiz, 0, seta);
            var filhos = new List<Border>();
            foreach (var p in pastas)
            {
                bool sharepoint;
                try { sharepoint = (File.GetAttributes(p) & FileAttributes.ReadOnly) != 0; } catch { sharepoint = false; }
                var f = Atalho(sharepoint ? "" : "", System.IO.Path.GetFileName(p), p, 18, null);
                f.Visibility = aberto ? Visibility.Visible : Visibility.Collapsed;
                filhos.Add(f);
            }
            seta.MouseLeftButtonUp += (s, e) =>
            {
                aberto = !aberto;
                if (aberto) gruposFechados.Remove(raiz); else gruposFechados.Add(raiz);
                seta.Text = aberto ? "" : "";
                foreach (var f in filhos) f.Visibility = aberto ? Visibility.Visible : Visibility.Collapsed;
                e.Handled = true;   // só expande/recolhe, não navega
            };
        }

        Border Atalho(string glifo, string nome, string destino) { return Atalho(glifo, nome, destino, 0, null); }

        Border Atalho(string glifo, string nome, string destino, double recuo, UIElement seta)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(recuo, 0, 0, 0) };
            if (seta != null) { sp.Margin = new Thickness(-8, 0, 0, 0); sp.Children.Add(seta); }
            sp.Children.Add(new TextBlock { Text = glifo, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)w.Resources["Azul"] });
            sp.Children.Add(new TextBlock { Text = nome, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Border { Child = sp, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(8, 1, 8, 1), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Tag = destino };
            b.MouseEnter += delegate { if (destino != atual) b.Background = (Brush)w.Resources["Hover"]; };
            b.MouseLeave += delegate { if (destino != atual) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate
            {
                if (destino.StartsWith("shell:")) { Explorador.Liberar(); Process.Start("explorer.exe", destino); return; }
                Navegar(destino, true);
            };
            // soltar arquivos num atalho da lateral copia/move para lá
            b.AllowDrop = destino != Recentes && !destino.StartsWith("shell:");
            b.Drop += (s, e) => { ReceberArquivos(e, destino); e.Handled = true; };
            b.DragOver += (s, e) => { e.Effects = Efeito(e, destino); e.Handled = true; };
            lateral.Children.Add(b);
            return b;
        }

        void MarcarLateral()
        {
            foreach (var b in lateral.Children.OfType<Border>())
                b.Background = string.Equals(b.Tag as string, atual, StringComparison.OrdinalIgnoreCase) ? (Brush)w.Resources["SelecaoLateral"] : Brushes.Transparent;
        }

        // ---------- navegação ----------
        void Navegar(string destino, bool novo)
        {
            if (destino == null) return;
            if (!Virtual(destino) && !Directory.Exists(destino)) { MessageBox.Show(w, "A pasta não existe mais:\n" + destino, "Finder"); return; }
            if (novo)
            {
                if (pos < historico.Count - 1) historico.RemoveRange(pos + 1, historico.Count - pos - 1);
                historico.Add(destino); pos = historico.Count - 1;
            }
            atual = destino;
            voltar.IsEnabled = pos > 0; avancar.IsEnabled = pos < historico.Count - 1;
            titulo.Text = NomeLocal(destino);
            w.Title = titulo.Text + " — Finder";
            limparLixo.Visibility = destino == Lixo ? Visibility.Visible : Visibility.Collapsed;   // o botão “Limpar Lixeira” só aparece na Lixeira
            var gv = lista.View as GridView;   // na Lixeira a data é a de quando o item foi apagado
            var colData = gv == null ? null : gv.Columns.FirstOrDefault(c => (c.Header as string) == "Data de Modificação" || (c.Header as string) == "Apagado em");
            if (colData != null) colData.Header = destino == Lixo ? "Apagado em" : "Data de Modificação";
            abertos.Clear();   // as pastas abertas em cascata valem só para a pasta atual
            emBusca = false;   // o Carregar abaixo já repõe a lista
            if (buscaCancel != null) buscaCancel.Cancel();
            if (busca.Text.Length > 0) busca.Text = "";
            ativa.Pos = pos; MontarAbas();
            MarcarLateral(); MontarCaminho(); Vigiar();
            Carregar(destino, false);
            AvisarMudou();
        }

        static string NomePasta(string p)
        {
            string n = System.IO.Path.GetFileName(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(n)) { try { var d = new DriveInfo(p); n = (string.IsNullOrEmpty(d.VolumeLabel) ? "Disco Local" : d.VolumeLabel) + " (" + p.TrimEnd('\\') + ")"; } catch { n = p; } }
            return n;
        }

        void Subir()
        {
            if (Virtual(atual)) return;
            var pai = Directory.GetParent(atual.TrimEnd('\\'));
            if (pai != null) Navegar(pai.FullName, true);
        }

        // caminho clicável embaixo: Disco Local (C:) › Usuários › roberto › Downloads
        void MontarCaminho()
        {
            caminhoBarra.Children.Clear();
            if (Virtual(atual)) { Pedaco(NomeLocal(atual), null, true); return; }
            var partes = new List<string>();
            for (var d = new DirectoryInfo(atual); d != null; d = d.Parent) partes.Insert(0, d.FullName);
            for (int i = 0; i < partes.Count; i++) Pedaco(NomePasta(partes[i]), partes[i], i == partes.Count - 1);
        }

        // ---------- abas ----------
        void NovaAba(string pasta)
        {
            ativa.Pos = pos;
            var a = new Aba(); abas.Insert(abas.IndexOf(ativa) + 1, a);
            ativa = a; historico = a.Historico; pos = -1;
            Navegar(pasta ?? Pasta(Environment.SpecialFolder.UserProfile, "Downloads"), true);
        }

        void Trocar(Aba a)
        {
            if (a == ativa) return;
            ativa.Pos = pos;
            ativa = a; historico = a.Historico; pos = a.Pos;
            Navegar(historico[pos], false);
        }

        void FecharAba(Aba a)
        {
            if (abas.Count == 1) { w.Close(); return; }
            int i = abas.IndexOf(a); abas.Remove(a);
            if (a != ativa) { MontarAbas(); return; }
            var n = abas[Math.Min(i, abas.Count - 1)];
            ativa = n; historico = n.Historico; pos = n.Pos;
            Navegar(historico[pos], false);
        }

        // arrastar uma aba para fora da faixa de abas (para cima ou para baixo) a destaca numa janela nova, como no Mac
        Aba arrastoAba;
        void VigiarArrastoAba()
        {
            w.PreviewMouseMove += (s, e) =>
            {
                if (arrastoAba == null) return;
                if (e.LeftButton != MouseButtonState.Pressed || abas.Count < 2) { arrastoAba = null; return; }
                var p = e.GetPosition(abasFundo);
                if (p.Y >= -24 && p.Y <= abasFundo.ActualHeight + 24) return;
                var aba = arrastoAba; arrastoAba = null;
                Destacar(aba, e.GetPosition(w));
            };
            w.PreviewMouseLeftButtonUp += delegate { arrastoAba = null; };
        }

        // a aba vira uma janela que acompanha o mouse enquanto o botão esquerdo continua apertado; soltar fixa a janela
        void Destacar(Aba aba, Point naJanela)
        {
            string pasta = aba.Historico[aba.Pos];
            var nova = new Finder(pasta); var nw = nova.w;
            double f = PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice.M11;
            double nl = w.WindowState == WindowState.Maximized ? w.RestoreBounds.Width : w.ActualWidth;
            double na = w.WindowState == WindowState.Maximized ? w.RestoreBounds.Height : w.ActualHeight;
            nw.WindowStartupLocation = WindowStartupLocation.Manual; nw.Width = nl; nw.Height = na;
            double dx = Math.Min(naJanela.X, nl - 40), dy = 60;   // o mouse segura a janela perto do alto, onde ficava a aba
            Action seguir = delegate
            {
                var c = System.Windows.Forms.Cursor.Position;
                nw.Left = c.X / f - dx; nw.Top = c.Y / f - dy;
            };
            seguir();
            nw.ShowActivated = false; nw.Show();
            FecharAba(aba);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
            t.Tick += delegate
            {
                if ((System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) == 0) { t.Stop(); nw.Activate(); return; }
                seguir();
            };
            t.Start();
        }

        void MontarAbas()
        {
            abasGrade.Children.Clear();
            abasFundo.Visibility = abas.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (abas.Count < 2) return;
            foreach (var a in abas)
            {
                var aba = a; bool eh = a == ativa;
                string p = a.Historico[a.Pos];
                var nome = new TextBlock
                {
                    Text = NomeLocal(p), FontSize = 12, Margin = new Thickness(22, 0, 22, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)w.Resources[eh ? "Texto" : "TextoSec"], FontWeight = eh ? FontWeights.SemiBold : FontWeights.Normal
                };
                var x = new TextBlock
                {
                    Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, Padding = new Thickness(5), Margin = new Thickness(3, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)w.Resources["TextoSec"],
                    Background = Brushes.Transparent, ToolTip = "Fechar aba", Visibility = eh ? Visibility.Visible : Visibility.Hidden
                };
                x.MouseLeftButtonDown += (s, e) => { e.Handled = true; FecharAba(aba); };
                var g = new Grid(); g.Children.Add(nome); g.Children.Add(x);
                var b = new Border
                {
                    Child = g, Height = 26, Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(6), ToolTip = Virtual(p) ? null : p,
                    Background = eh ? (Brush)w.Resources["Fundo"] : Brushes.Transparent
                };
                b.MouseEnter += delegate { x.Visibility = Visibility.Visible; if (!eh) b.Background = (Brush)w.Resources["Hover"]; };
                b.MouseLeave += delegate { if (!eh) { x.Visibility = Visibility.Hidden; b.Background = Brushes.Transparent; } };
                b.MouseLeftButtonDown += delegate { arrastoAba = aba; Trocar(aba); };   // Trocar refaz as abas: o arrasto é vigiado pela janela
                b.MouseUp += (s, e) => { if (e.ChangedButton == MouseButton.Middle) FecharAba(aba); };   // botão do meio fecha, como no navegador
                abasGrade.Children.Add(b);
            }
        }

        void EditarCaminho()
        {
            endereco.Text = Virtual(atual) ? "" : atual;
            caminhoBarra.Visibility = Visibility.Hidden; endereco.Visibility = Visibility.Visible;
            endereco.Focus();
            w.Dispatcher.BeginInvoke(new Action(endereco.SelectAll), DispatcherPriority.Input);   // depois do clique, senão ele desfaz a seleção
        }

        void FecharCaminho()
        {
            if (endereco.Visibility != Visibility.Visible) return;
            endereco.Visibility = Visibility.Collapsed; caminhoBarra.Visibility = Visibility.Visible;
        }

        // caminho digitado ou colado: pasta abre aqui; arquivo abre no app dele
        void IrPara(string texto)
        {
            string p = Environment.ExpandEnvironmentVariables(texto.Trim().Trim('"'));
            FecharCaminho(); lista.Focus();
            if (p.Length == 0) return;
            if (p.Length == 2 && p[1] == ':') p += "\\";
            if (Directory.Exists(p)) Navegar(System.IO.Path.GetFullPath(p), true);
            else if (File.Exists(p)) { try { Process.Start(p); } catch (Exception ex) { Log("abrir " + p + ": " + ex.Message); } }
            else MessageBox.Show(w, "Não foi possível encontrar:\n" + p, "Finder");
        }

        void Pedaco(string nome, string destino, bool ultimo)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, Foreground = (Brush)w.Resources["Azul"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            sp.Children.Add(new TextBlock { Text = nome, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, Style = (Style)w.Resources["Plano"], Padding = new Thickness(4, 1, 4, 1) };
            if (destino != null) b.Click += delegate { Navegar(destino, true); };
            caminhoBarra.Children.Add(b);
            if (!ultimo) caminhoBarra.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)w.Resources["TextoSec"] });
        }

        // ---------- leitura da pasta (em segundo plano) ----------
        int geracao;
        void Carregar(string destino, bool manterSelecao)
        {
            int g = ++geracao;
            var antes = manterSelecao ? new HashSet<string>(Selecionados().Select(e => e.Caminho)) : null;
            Shell.LimparFila();
            Task.Factory.StartNew(() => Ler(destino)).ContinueWith(t =>
            {
                if (g != geracao) return;   // o usuário já foi para outra pasta
                itens.Clear();
                foreach (var e in Ordem(t.Result))
                {
                    e.Expansivel = e.EhPasta && !Virtual(destino);
                    itens.Add(e);
                    if (e.Expansivel && abertos.Contains(e.Caminho)) Expandir(e, false);   // refresh mantém as pastas abertas
                }
                PedirIcones();
                if (antes != null) foreach (var e in itens.Where(x => antes.Contains(x.Caminho))) lista.SelectedItems.Add(e);
                if (selecionarDepois != null)
                {
                    var alvo = itens.FirstOrDefault(x => string.Equals(x.Caminho, selecionarDepois, StringComparison.OrdinalIgnoreCase));
                    selecionarDepois = null;
                    if (alvo != null) { lista.SelectedItem = alvo; lista.ScrollIntoView(alvo); }
                }
                AtualizarStatusSelecao();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static List<Entrada> Ler(string destino)
        {
            var r = new List<Entrada>();
            try
            {
                if (destino == Lixo) return LerLixo();
                if (destino == Recentes)
                {
                    // atalhos que o Windows guarda dos arquivos abertos recentemente
                    string rec = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    foreach (var f in new DirectoryInfo(rec).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(150))
                    {
                        try
                        {
                            string alvo = MacSkin.Atalho.Ler(f.FullName).Alvo;
                            if (alvo.Length == 0) continue;
                            if (File.Exists(alvo)) { var fi = new FileInfo(alvo); r.Add(Nova(fi.Name, fi.FullName, false, fi.Length, f.LastWriteTime)); }
                            else if (Directory.Exists(alvo)) r.Add(Nova(System.IO.Path.GetFileName(alvo), alvo, true, 0, f.LastWriteTime));
                        }
                        catch { }
                    }
                    return r;
                }
                foreach (var i in new DirectoryInfo(destino).EnumerateFileSystemInfos())
                {
                    if ((i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;   // como o Finder: sem ocultos
                    var fi = i as FileInfo;
                    r.Add(Nova(i.Name, i.FullName, fi == null, fi == null ? 0 : fi.Length, i.LastWriteTime));
                }
            }
            catch (Exception e) { Log("ler " + destino + ": " + e.Message); }
            return r;
        }

        static Entrada Nova(string nome, string caminho, bool pasta, long tam, DateTime data)
        {
            return new Entrada { Nome = nome, Caminho = caminho, EhPasta = pasta, Tamanho = tam, Data = data, Tipo = Shell.Tipo(nome, pasta) };
        }

        IEnumerable<Entrada> Ordem(IEnumerable<Entrada> l)
        {
            Comparison<Entrada> cmp;
            switch (ordem)
            {
                case "Tamanho": cmp = (a, b) => a.Tamanho.CompareTo(b.Tamanho); break;
                case "Tipo": cmp = (a, b) => string.Compare(a.Tipo, b.Tipo, StringComparison.CurrentCultureIgnoreCase); break;
                case "Data": cmp = (a, b) => a.Data.CompareTo(b.Data); break;
                default: cmp = (a, b) => Shell.StrCmpLogicalW(a.Nome, b.Nome); break;
            }
            var lst = l.ToList();
            lst.Sort((a, b) =>
            {
                if (atual != Recentes && a.EhPasta != b.EhPasta) return a.EhPasta ? -1 : 1;   // pastas primeiro
                int c = cmp(a, b); if (c == 0) c = Shell.StrCmpLogicalW(a.Nome, b.Nome);
                return crescente ? c : -c;
            });
            return lst;
        }

        void Ordenar()
        {
            var sel = new HashSet<string>(Selecionados().Select(x => x.Caminho));
            var topo = itens.Where(x => x.Nivel == 0).ToList();   // o conteúdo das pastas abertas é relido e reordenado
            foreach (var e in topo) e.Aberta = false;
            itens.Clear();
            foreach (var e in Ordem(topo))
            {
                itens.Add(e);
                if (e.Expansivel && abertos.Contains(e.Caminho)) Expandir(e, true);
            }
            foreach (var e in itens.Where(x => sel.Contains(x.Caminho))) lista.SelectedItems.Add(e);
        }

        // ---------- cascata (setinha / teclas → ←) ----------
        // pastas abertas na pasta atual; vale até navegar para outra
        readonly HashSet<string> abertos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // mostra o conteúdo da pasta logo abaixo dela, sem entrar nela
        void Expandir(Entrada e, bool pedirIcones)
        {
            if (!e.EhPasta || !e.Expansivel || e.Aberta) return;
            var filhos = Ordem(Ler(e.Caminho)).ToList();
            foreach (var f in filhos) { f.Nivel = e.Nivel + 1; f.Expansivel = true; }
            e.Aberta = true; abertos.Add(e.Caminho);
            int i = itens.IndexOf(e) + 1;
            foreach (var f in filhos) itens.Insert(i++, f);
            if (pedirIcones) PedirIcones(filhos);
            foreach (var f in filhos) if (abertos.Contains(f.Caminho)) Expandir(f, pedirIcones);
            AtualizarStatusSelecao();
        }

        void Recolher(Entrada e)
        {
            if (!e.Aberta) return;
            int i = itens.IndexOf(e) + 1;
            while (i < itens.Count && itens[i].Nivel > e.Nivel) itens.RemoveAt(i);
            e.Aberta = false; abertos.Remove(e.Caminho);
            string prefixo = e.Caminho.TrimEnd('\\') + "\\";
            abertos.RemoveWhere(p => p.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase));
            if (lista.SelectedItems.Count == 0) lista.SelectedItem = e;   // a seleção estava dentro do que fechou
            AtualizarStatusSelecao();
        }

        void AlternarPasta(Entrada e) { if (e.Aberta) Recolher(e); else Expandir(e, true); }

        void SelecionarEFocar(Entrada e)
        {
            lista.SelectedItems.Clear(); lista.SelectedItem = e;
            lista.ScrollIntoView(e); lista.UpdateLayout();
            var ct = lista.ItemContainerGenerator.ContainerFromItem(e) as ListBoxItem;
            if (ct != null) ct.Focus();
        }

        // → abre a pasta (se já aberta, desce para o primeiro item dela); ← fecha (se já fechada, sobe para a pasta de cima)
        bool TeclaCascata(Key k)
        {
            if (modo != "lista") return false;
            var sel = Selecionados().ToList();
            if (sel.Count == 0) return false;
            if (sel.Count > 1)
            {
                foreach (var e in sel) { if (k == Key.Right) Expandir(e, true); else Recolher(e); }
                return true;
            }
            var en = sel[0];
            int i = itens.IndexOf(en);
            if (k == Key.Right)
            {
                if (!en.EhPasta || !en.Expansivel) return true;
                if (!en.Aberta) Expandir(en, true);
                else if (i + 1 < itens.Count && itens[i + 1].Nivel > en.Nivel) SelecionarEFocar(itens[i + 1]);
            }
            else
            {
                if (en.Aberta) Recolher(en);
                else if (en.Nivel > 0)
                {
                    for (int j = i - 1; j >= 0; j--) if (itens[j].Nivel < en.Nivel) { SelecionarEFocar(itens[j]); break; }
                }
            }
            return true;
        }

        // ícones de pasta no estilo Mac (WhiteSur, instalados em icones\); o resto vem do Windows
        static readonly Dictionary<string, string> pastasEspeciais = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Pasta(Environment.SpecialFolder.MyDocuments), "DocumentsFolderIcon" }, { Pasta(Environment.SpecialFolder.UserProfile, "Downloads"), "DownloadsFolder" },
            { Pasta(Environment.SpecialFolder.MyPictures), "PicturesFolderIcon" }, { Pasta(Environment.SpecialFolder.MyMusic), "MusicFolderIcon" },
            { Pasta(Environment.SpecialFolder.MyVideos), "MovieFolderIcon" }, { Pasta(Environment.SpecialFolder.UserProfile), "HomeFolderIcon" },
        };
        static readonly Dictionary<string, ImageSource> icoMac = new Dictionary<string, ImageSource>();
        static ImageSource IconeMac(string nome)
        {
            ImageSource i;
            if (!icoMac.TryGetValue(nome, out i))
            {
                string arq = System.IO.Path.Combine(Comum.Dir, "icones", nome + ".ico");
                i = File.Exists(arq) ? Comum.Icone(arq) : null;
                icoMac[nome] = i;
            }
            return i;
        }

        // pasta do Mac desenhada em vetor: fundo com a aba, folha de papel saindo por cima e frente em degradê
        static ImageSource pastaMac;
        static ImageSource PastaMac()
        {
            if (pastaMac == null)
            {
                string x = "<DrawingImage xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><DrawingImage.Drawing><DrawingGroup>"
                    + "<GeometryDrawing Brush='Transparent' Geometry='M0,0 H100 V100 H0 Z'/>"
                    + "<GeometryDrawing Geometry='M5,22 Q5,12 15,12 H37 Q42,12 45,16 L49,21 H88 Q96,21 96,29 V80 Q96,90 86,90 H15 Q5,90 5,80 Z'><GeometryDrawing.Brush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#3C9BD6' Offset='0'/><GradientStop Color='#2F86C4' Offset='1'/></LinearGradientBrush></GeometryDrawing.Brush></GeometryDrawing>"
                    + "<GeometryDrawing Brush='#33000000' Geometry='M12,30 H89 V50 H12 Z'/>"
                    + "<GeometryDrawing Geometry='M13,22 Q13,19 16,19 H85 Q88,19 88,22 V60 H13 Z'><GeometryDrawing.Brush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#E4E8EC' Offset='1'/></LinearGradientBrush></GeometryDrawing.Brush></GeometryDrawing>"
                    + "<GeometryDrawing Geometry='M4,40 Q4,32 12,32 H89 Q97,32 97,40 V80 Q97,90 87,90 H14 Q4,90 4,80 Z'><GeometryDrawing.Brush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#84D4F8' Offset='0'/><GradientStop Color='#4DB4EA' Offset='0.45'/><GradientStop Color='#35A0DC' Offset='1'/></LinearGradientBrush></GeometryDrawing.Brush></GeometryDrawing>"
                    + "<GeometryDrawing Geometry='M5,40 Q5,33 12,33 H89 Q96,33 96,40'><GeometryDrawing.Pen><Pen Brush='#66FFFFFF' Thickness='1.2'/></GeometryDrawing.Pen></GeometryDrawing>"
                    + "</DrawingGroup></DrawingImage.Drawing></DrawingImage>";
                var d = (DrawingImage)System.Windows.Markup.XamlReader.Parse(x);
                d.Freeze();
                pastaMac = d;
            }
            return pastaMac;
        }

        void PedirIcones() { PedirIcones(itens.ToList()); }
        void PedirIcones(IEnumerable<Entrada> quais)
        {
            int g = geracao;
            int px = modo == "galeria" ? 160 : modo == "icones" ? 96 : 32;
            foreach (var e in quais)
            {
                var en = e;
                if (en.EhPasta)
                {
                    string esp; var mac = pastasEspeciais.TryGetValue(en.Caminho, out esp) ? IconeMac(esp) : PastaMac();
                    if (mac != null) { en.Icone = mac; en.Grande = mac; continue; }
                }
                Shell.PedirImagem(en.Caminho, px, img => { if (g != geracao) return; if (px <= 32) en.Icone = img; else { en.Grande = img; if (en.Icone == null) en.Icone = img; } });
                if (px > 32 && en.Icone == null) Shell.PedirImagem(en.Caminho, 32, img => { if (g == geracao && en.Icone == null) en.Icone = img; });
            }
        }

        // ---------- modos de exibição ----------
        void Modo(string m)
        {
            modo = m;
            vLista.IsChecked = m == "lista"; vIcones.IsChecked = m == "icones"; vGaleria.IsChecked = m == "galeria";
            lista.Visibility = m == "lista" ? Visibility.Visible : Visibility.Collapsed;
            icones.Visibility = m == "lista" ? Visibility.Collapsed : Visibility.Visible;
            // o XAML lê estes tamanhos (DynamicResource) nos modos Ícones e Galeria
            icones.Resources["LarguraBloco"] = m == "galeria" ? 190.0 : 112.0;
            icones.Resources["TamIcone"] = m == "galeria" ? 160.0 : 64.0;
            PedirIcones();
        }

        // ---------- busca ----------
        void Filtrar()
        {
            string t = busca.Text.Trim();
            // limpou a busca depois de um Enter: sai dos resultados e volta ao conteúdo da pasta
            if (t.Length == 0 && emBusca)
            {
                emBusca = false;
                if (buscaCancel != null) buscaCancel.Cancel();
                titulo.Text = NomeLocal(atual);
                visao.Filter = null;
                Carregar(atual, false);
                return;
            }
            visao.Filter = t.Length == 0 ? null : (Predicate<object>)(o => ((Entrada)o).Nome.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0);
            AtualizarStatusSelecao();
        }

        // Enter na busca: procura em todas as subpastas (em segundo plano, até 2000 resultados)
        void BuscarTudo(string termo)
        {
            termo = termo.Trim();
            if (termo.Length == 0 || Virtual(atual)) return;
            if (buscaCancel != null) buscaCancel.Cancel();
            var cts = buscaCancel = new CancellationTokenSource();
            emBusca = true;
            string raiz = atual; int g = ++geracao;
            titulo.Text = "Buscando “" + termo + "”…";
            visao.Filter = null;
            Task.Factory.StartNew(() =>
            {
                var r = new List<Entrada>();
                var pilha = new Stack<string>(); pilha.Push(raiz);
                while (pilha.Count > 0 && r.Count < 2000 && !cts.IsCancellationRequested)
                {
                    string d = pilha.Pop();
                    try
                    {
                        foreach (var i in new DirectoryInfo(d).EnumerateFileSystemInfos())
                        {
                            if ((i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            var fi = i as FileInfo;
                            if (fi == null && (i.Attributes & FileAttributes.ReparsePoint) == 0) pilha.Push(i.FullName);
                            if (i.Name.IndexOf(termo, StringComparison.CurrentCultureIgnoreCase) >= 0)
                                r.Add(Nova(i.Name, i.FullName, fi == null, fi == null ? 0 : fi.Length, i.LastWriteTime));
                        }
                    }
                    catch { }
                }
                return r;
            }, cts.Token).ContinueWith(t =>
            {
                if (g != geracao || t.IsCanceled) return;
                itens.Clear(); foreach (var e in Ordem(t.Result)) itens.Add(e);
                titulo.Text = "Busca: “" + termo + "” (" + t.Result.Count + ")";
                PedirIcones();
                AtualizarStatusSelecao();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------- abrir ----------
        IEnumerable<Entrada> Selecionados()
        {
            var c = modo == "lista" ? (System.Collections.IList)lista.SelectedItems : icones.SelectedItems;
            return c.Cast<Entrada>().ToList();
        }

        // Indicador permanente no rodapÃ©: acompanha seleÃ§Ã£o por clique, teclado e laÃ§o.
        void AtualizarStatusSelecao()
        {
            if (statusSelecao == null) return;
            int n = modo == "lista" ? lista.SelectedItems.Count : icones.SelectedItems.Count;
            int total = visao == null ? itens.Count : visao.Cast<object>().Count();
            string itensTexto = total == 1 ? "1 item" : total.ToString("N0", PtBr) + " itens";
            statusSelecao.Text = n == 0 ? itensTexto : itensTexto + " | " + (n == 1 ? "1 item selecionado" : n.ToString("N0", PtBr) + " itens selecionados");
            if (bLimparLixo != null) bLimparLixo.IsEnabled = itens.Count > 0;
        }

        static Entrada Sob(MouseEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is ListBoxItem)) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d == null ? null : ((ListBoxItem)d).DataContext as Entrada;
        }

        void AbrirSelecionados()
        {
            var sel = Selecionados().ToList();
            if (atual == Lixo)   // como no Mac: itens da Lixeira não abrem; Espaço mostra a prévia
            {
                if (sel.Count > 0) MessageBox.Show(w, "Para abrir “" + sel[0].Nome + "”, ponha o item de volta primeiro (botão direito → Pôr de Volta).", "Finder");
                return;
            }
            if (sel.Count == 1 && sel[0].EhPasta) { Navegar(sel[0].Caminho, true); return; }
            if (AoAbrir != null && AoAbrir(sel)) return;
            foreach (var e in sel)
            {
                try { if (e.EhPasta) NovaJanela(e.Caminho); else if (EhZip(e.Caminho)) Descompactar(e.Caminho); else Process.Start(e.Caminho); }
                catch (Exception ex) { Log("abrir " + e.Caminho + ": " + ex.Message); }
            }
        }

        public static bool EhZip(string p) { return p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p); }

        // zip, como no Utilitário de Compressão do Mac: abrir descompacta numa pasta ao lado, com o nome do zip ("nome 2"
        // se já existir), e a deixa selecionada. Se o zip tiver uma pasta só dentro, ela mesma vem para fora.
        public void Descompactar(string zip)
        {
            string pai = System.IO.Path.GetDirectoryName(zip), nome = System.IO.Path.GetFileNameWithoutExtension(zip);
            Func<string, string> livre = n =>
            {
                string d = System.IO.Path.Combine(pai, n);
                for (int i = 2; Directory.Exists(d) || File.Exists(d); i++) d = System.IO.Path.Combine(pai, n + " " + i);
                return d;
            };
            string destino = livre(nome);
            statusSelecao.Text = "Descompactando " + System.IO.Path.GetFileName(zip) + "…";
            Task.Factory.StartNew(() =>
            {
                // nomes sem a marca UTF-8 vêm na página de código do DOS, como o Explorador grava (850 no Brasil)
                var enc = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, destino, enc);
                var dentro = Directory.GetFileSystemEntries(destino);
                if (dentro.Length == 1 && Directory.Exists(dentro[0]))
                {
                    string unica = System.IO.Path.GetFileName(dentro[0]), temp = destino + ".tmp-macskin";
                    Directory.Move(destino, temp);
                    string final = livre(unica);
                    Directory.Move(System.IO.Path.Combine(temp, unica), final);
                    Directory.Delete(temp);
                    return final;
                }
                return destino;
            }).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    var erro = t.Exception.GetBaseException();
                    Log("descompactar " + zip + ": " + erro.Message);
                    try { if (Directory.Exists(destino)) Directory.Delete(destino, true); } catch { }
                    AtualizarStatusSelecao();
                    MessageBox.Show(w, "Não foi possível descompactar “" + System.IO.Path.GetFileName(zip) + "”.\n" + erro.Message, "Finder");
                    return;
                }
                selecionarDepois = t.Result;
                if (string.Equals(atual, pai, StringComparison.OrdinalIgnoreCase)) Carregar(atual, false); else Navegar(pai, true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Visualização Rápida (Espaço), como o Quick Look do Mac
        void AlternarPrevia()
        {
            if (previa != null && previa.Aberta) { previa.Fechar(); return; }
            var sel = Selecionados().LastOrDefault();
            if (sel == null) return;
            if (previa == null) previa = new Previa(w) { Mover = MoverSelecao };
            previa.Mostrar(sel);
        }

        void MoverSelecao(int d)
        {
            ListBox c = modo == "lista" ? lista : icones;
            int i = c.SelectedIndex + d;
            if (c.SelectedIndex < 0 || i < 0 || i >= c.Items.Count) return;
            c.SelectedIndex = i;
            c.ScrollIntoView(c.SelectedItem);
        }

        static void NovaJanela(string pasta)
        {
            try { Process.Start(Process.GetCurrentProcess().MainModule.FileName, "--finder \"" + pasta + "\""); } catch { }
        }

        // ---------- teclado ----------
        void Teclas(object s, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (e.Key == Key.Enter) AbrirSelecionados();
            else if (e.Key == Key.Escape && previa != null && previa.Aberta) previa.Fechar();
            else if (e.Key == Key.Back) voltar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (e.Key == Key.Delete) Lixeira(shift);
            else if (atual == Lixo && (e.Key == Key.F2 || (ctrl && (e.Key == Key.C || e.Key == Key.X)))) { }   // na Lixeira os arquivos têm nomes $R…: só Pôr de Volta
            else if (e.Key == Key.F2) Renomear();
            else if (ctrl && e.Key == Key.C) Copiar(false);
            else if (ctrl && e.Key == Key.X) Copiar(true);
            else if (ctrl && e.Key == Key.V) Colar();
            else if (ctrl && shift && e.Key == Key.N) NovaPasta();
            else return;
            e.Handled = true;
        }

        // ---------- menus ----------
        void AbrirMenuContexto()
        {
            var sel = Selecionados().ToList();
            var hwnd = new WindowInteropHelper(w).Handle;
            if (atual == Lixo) MenuLixo(sel);
            else if (sel.Count > 0)
            {
                var caminhos = sel.Select(x => x.Caminho).ToList();
                ShellMenu.MostrarMenu(
                    hwnd,
                    caminhos,
                    () => Carregar(atual, false),
                    sel.Any(x => x.EhPasta) ? (Action<string, bool>)((c, novaJanela) => { if (novaJanela) NovaJanela(c); else NovaAba(c); }) : null,
                    () => AlternarPrevia()
                );
            }
            else if (!Virtual(atual) && Directory.Exists(atual))
            {
                ShellMenu.MostrarMenuFundo(
                    hwnd,
                    atual,
                    () => Carregar(atual, false),
                    NovaPasta,
                    Colar,
                    () => Clipboard.ContainsFileDropList(),
                    MostrarNoExplorador
                );
            }
        }

        void MenuMais(Button b)
        {
            var m = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
            Action<string, Action> op = (nome, acao) => { var i = new MenuItem { Header = nome }; i.Click += delegate { acao(); }; m.Items.Add(i); };
            op("Nova Pasta", NovaPasta);
            op("Nova Janela do Finder", () => NovaJanela(atual));
            op("Abrir esta pasta no Explorador", MostrarNoExplorador);
            op("Atualizar", () => Navegar(atual, false));
            if (atual == Lixo) op("Esvaziar Lixeira…", EsvaziarLixeira);
            m.IsOpen = true;
        }

        void MostrarNoExplorador()
        {
            var sel = Selecionados().ToList();
            Explorador.Liberar();
            if (sel.Count > 0) Process.Start("explorer.exe", "/select,\"" + sel[0].Caminho + "\"");
            else if (atual == Lixo) Process.Start("explorer.exe", "shell:RecycleBinFolder");
            else if (atual != Recentes) Process.Start("explorer.exe", "\"" + atual + "\"");
        }

        // ---------- operações de arquivo (diálogos de progresso e conflito do próprio Windows) ----------
        void Lixeira(bool definitivo)
        {
            var sel = Selecionados().ToList();
            if (sel.Count == 0) return;
            if (atual == Lixo) { ApagarDoLixo(sel); return; }   // já está na Lixeira: Delete apaga de vez
            MandarParaLixeira(sel.Select(e => e.Caminho).ToList(), definitivo);
        }

        void MandarParaLixeira(List<string> caminhos, bool definitivo)
        {
            if (caminhos.Count == 0) return;
            if (definitivo && MessageBox.Show(w, "Apagar " + caminhos.Count + " item(ns) definitivamente? Não vai para a Lixeira.", "Finder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var opc = definitivo ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
            foreach (var c in caminhos)
            {
                try
                {
                    if (Directory.Exists(c)) FileSystem.DeleteDirectory(c, UIOption.OnlyErrorDialogs, opc);
                    else FileSystem.DeleteFile(c, UIOption.OnlyErrorDialogs, opc);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log("lixo: " + ex.Message); }
            }
            Carregar(atual, false);
        }

        // ---------- Lixeira ----------
        // cada disco guarda os itens apagados em X:\$Recycle.Bin\<SID>: o item em $Rxxxxxx.ext e, ao lado, o $Ixxxxxx.ext
        // com o tamanho, a hora em que foi apagado e o caminho original
        static readonly Dictionary<string, string> origemLixo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // $R → caminho original

        static List<string> PastasLixo()
        {
            var r = new List<string>();
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    string p = System.IO.Path.Combine(d.Name, "$Recycle.Bin", sid);
                    if (Directory.Exists(p)) r.Add(p);
                }
                catch { }
            }
            return r;
        }

        static string InfoLixo(string real) { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(real), "$I" + System.IO.Path.GetFileName(real).Substring(2)); }

        static List<Entrada> LerLixo()
        {
            var r = new List<Entrada>();
            foreach (var pasta in PastasLixo())
            {
                string[] infos;
                try { infos = Directory.GetFiles(pasta, "$I*"); } catch { continue; }
                foreach (var info in infos)
                {
                    try
                    {
                        string real = System.IO.Path.Combine(pasta, "$R" + System.IO.Path.GetFileName(info).Substring(2));
                        bool dir = Directory.Exists(real);
                        if (!dir && !File.Exists(real)) continue;
                        byte[] b = File.ReadAllBytes(info);
                        long versao = BitConverter.ToInt64(b, 0), tam = BitConverter.ToInt64(b, 8);
                        var quando = DateTime.FromFileTime(BitConverter.ToInt64(b, 16));
                        string origem = versao >= 2
                            ? System.Text.Encoding.Unicode.GetString(b, 28, Math.Max(0, BitConverter.ToInt32(b, 24) - 1) * 2)   // tamanho conta o \0 final
                            : System.Text.Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24)).TrimEnd('\0');
                        lock (origemLixo) origemLixo[real] = origem;
                        r.Add(Nova(System.IO.Path.GetFileName(origem), real, dir, dir ? 0 : tam, quando));   // Data = quando foi apagado
                    }
                    catch (Exception ex) { Log("lixeira " + info + ": " + ex.Message); }
                }
            }
            return r;
        }

        // Pôr de Volta, como no Mac: devolve o item à pasta de onde saiu (recriando-a se preciso; com outro nome se já houver um igual)
        void PorDeVolta(List<Entrada> sel)
        {
            foreach (var e in sel)
            {
                string origem;
                lock (origemLixo) origemLixo.TryGetValue(e.Caminho, out origem);
                if (string.IsNullOrEmpty(origem)) continue;
                try
                {
                    string pai = System.IO.Path.GetDirectoryName(origem);
                    Directory.CreateDirectory(pai);
                    string alvo = origem;
                    if (File.Exists(alvo) || Directory.Exists(alvo))
                        alvo = e.EhPasta ? NomeLivre(pai, System.IO.Path.GetFileName(origem), "") : NomeLivre(pai, System.IO.Path.GetFileNameWithoutExtension(origem), System.IO.Path.GetExtension(origem));
                    if (e.EhPasta) Directory.Move(e.Caminho, alvo); else File.Move(e.Caminho, alvo);
                    File.Delete(InfoLixo(e.Caminho));
                    Win32.AvisarPasta(pai); Win32.AvisarPasta(System.IO.Path.GetDirectoryName(e.Caminho));
                }
                catch (Exception ex) { MessageBox.Show(w, "Não foi possível pôr de volta “" + e.Nome + "”:\n" + ex.Message, "Finder"); }
            }
            Carregar(atual, false);
        }

        void ApagarDoLixo(List<Entrada> sel)
        {
            if (sel.Count == 0) return;
            foreach (var e in sel)
            {
                try
                {
                    if (e.EhPasta) Directory.Delete(e.Caminho, true); else File.Delete(e.Caminho);
                    File.Delete(InfoLixo(e.Caminho));
                    Win32.AvisarPasta(System.IO.Path.GetDirectoryName(e.Caminho));
                }
                catch (Exception ex) { MessageBox.Show(w, "Não foi possível apagar “" + e.Nome + "”:\n" + ex.Message, "Finder"); }
            }
            Carregar(atual, false);
        }

        void EsvaziarLixeira()
        {
            Win32.SHEmptyRecycleBin(new WindowInteropHelper(w).Handle, null, 0);   // pergunta e mostra o progresso do próprio Windows
            Carregar(atual, false);
        }

        void MenuLixo(List<Entrada> sel)
        {
            var m = new ContextMenu();
            Action<string, Action, bool> op = (nome, acao, ativo) => { var i = new MenuItem { Header = nome, IsEnabled = ativo }; i.Click += delegate { acao(); }; m.Items.Add(i); };
            if (sel.Count > 0)
            {
                op("Pôr de Volta", () => PorDeVolta(sel), true);
                op("Apagar Imediatamente…", () => ApagarDoLixo(sel), true);
                m.Items.Add(new Separator());
                op("Visualização Rápida", AlternarPrevia, true);
            }
            else op("Esvaziar Lixeira…", EsvaziarLixeira, itens.Count > 0);
            m.IsOpen = true;
        }

        void Copiar(bool recortar)
        {
            var sel = Selecionados().ToList();
            if (sel.Count == 0) return;
            var lst = new System.Collections.Specialized.StringCollection();
            lst.AddRange(sel.Select(e => e.Caminho).ToArray());
            var d = new DataObject();
            d.SetFileDropList(lst);
            d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(recortar ? 2 : 5)));   // 2 = mover
            Clipboard.SetDataObject(d, true);
        }

        void Colar()
        {
            if (Virtual(atual) || !Clipboard.ContainsFileDropList()) return;
            bool mover = false;
            var ms = Clipboard.GetData("Preferred DropEffect") as MemoryStream;
            if (ms != null) { var b = new byte[4]; ms.Read(b, 0, 4); mover = (BitConverter.ToInt32(b, 0) & 2) != 0; }
            Transferir(Clipboard.GetFileDropList().Cast<string>().ToList(), atual, mover);
            if (mover) Clipboard.Clear();
        }

        void Transferir(List<string> origens, string destino, bool mover)
        {
            foreach (var o in origens)
            {
                try
                {
                    string alvo = System.IO.Path.Combine(destino, System.IO.Path.GetFileName(o.TrimEnd('\\')));
                    if (string.Equals(System.IO.Path.GetDirectoryName(o.TrimEnd('\\')), destino.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        if (mover) continue;   // mover para a mesma pasta: nada a fazer
                        alvo = NomeLivre(destino, System.IO.Path.GetFileNameWithoutExtension(o) + " cópia", Directory.Exists(o) ? "" : System.IO.Path.GetExtension(o));
                    }
                    if (Directory.Exists(o))
                    {
                        if (mover) FileSystem.MoveDirectory(o, alvo, UIOption.AllDialogs);
                        else FileSystem.CopyDirectory(o, alvo, UIOption.AllDialogs);
                    }
                    else if (mover) FileSystem.MoveFile(o, alvo, UIOption.AllDialogs);
                    else FileSystem.CopyFile(o, alvo, UIOption.AllDialogs);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { MessageBox.Show(w, ex.Message, "Finder"); }
            }
            Carregar(atual, true);
        }

        static string NomeLivre(string pasta, string baseNome, string ext)
        {
            string p = System.IO.Path.Combine(pasta, baseNome + ext);
            for (int i = 2; File.Exists(p) || Directory.Exists(p); i++) p = System.IO.Path.Combine(pasta, baseNome + " " + i + ext);
            return p;
        }

        void NovaPasta()
        {
            if (Virtual(atual)) return;
            try
            {
                string p = NomeLivre(atual, "pasta sem título", "");
                Directory.CreateDirectory(p);
                Carregar(atual, false);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var e = itens.FirstOrDefault(x => x.Caminho == p);
                    if (e != null) { lista.SelectedItem = e; icones.SelectedItem = e; Renomear(); }
                }), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex) { MessageBox.Show(w, ex.Message, "Finder"); }
        }

        void Renomear()
        {
            var sel = Selecionados().ToList();
            if (sel.Count != 1) return;
            var e = sel[0];
            string novo = Perguntar("Renomear", e.Nome);
            if (string.IsNullOrWhiteSpace(novo) || novo == e.Nome) return;
            try
            {
                string alvo = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(e.Caminho), novo);
                if (e.EhPasta) Directory.Move(e.Caminho, alvo); else File.Move(e.Caminho, alvo);
                Carregar(atual, false);
            }
            catch (Exception ex) { MessageBox.Show(w, ex.Message, "Finder"); }
        }

        // caixinha de texto no estilo do Finder (nome selecionado sem a extensão, como no Mac)
        string Perguntar(string tituloJanela, string valor)
        {
            // Liquid Glass, como o indicador de volume: janela acrylic atrás + cartão transparente com brilho e borda de luz por cima
            const double L = 380, A = 150, Raio = 16, Recuo = 5, Folga = 20;
            bool c = Comum.TemaClaro();
            var fonte = new FontFamily("Inter, Segoe UI");
            var texto = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF");

            var vidro = new Window
            {
                Title = "Mac Skin vidro", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Owner = w,
                Background = Brushes.Transparent, Left = -10000, Top = -10000, Width = 10, Height = 10, ShowActivated = false
            };
            IntPtr hVidro = IntPtr.Zero;
            vidro.SourceInitialized += delegate
            {
                hVidro = new WindowInteropHelper(vidro).Handle;
                HwndSource.FromHwnd(hVidro).CompositionTarget.BackgroundColor = Colors.Transparent;
                Win32.Estilo(hVidro, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
            };
            vidro.Show();   // o diálogo precisa de um dono já exibido; o cartão fica sempre na frente do vidro

            Rect area = w.WindowState == WindowState.Maximized ? SystemParameters.WorkArea : new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight);
            var d = new Window
            {
                Title = tituloJanela, Owner = vidro, Width = L + 2 * Folga, Height = A + 2 * Folga, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = area.Left + (area.Width - L) / 2 - Folga, Top = area.Top + (area.Height - A) / 3 - Folga,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Brushes.Transparent
            };

            var fundo = new Border
            {
                Width = L, Height = A, CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(1),
                Background = c ? Comum.Degrade("#55FFFFFF", "#14FFFFFF") : Comum.Degrade("#40FFFFFF", "#0EFFFFFF"),
                BorderBrush = c ? Comum.Degrade("#F2FFFFFF", "#66FFFFFF") : Comum.Degrade("#B3FFFFFF", "#33FFFFFF"),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.28 }
            };
            var aro = new Border
            {
                Width = L, Height = A, CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(Recuo), IsHitTestVisible = false,
                BorderBrush = Comum.Cor(c ? "#8CEDEDEF" : "#8C3A3A3E")
            };

            // barra de título: nome à esquerda e X à direita, como o diálogo do Windows
            var titulo = new TextBlock { Text = tituloJanela, FontFamily = fonte, FontSize = 12, Foreground = texto, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            var fechar = new Border { Width = 46, Height = 38, Background = Brushes.Transparent, CornerRadius = new CornerRadius(0, Raio, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            fechar.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            fechar.MouseEnter += delegate { fechar.Background = Comum.Cor("#E5E81123"); };
            fechar.MouseLeave += delegate { fechar.Background = Brushes.Transparent; };
            var barra = new Grid { Height = 38, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
            barra.Children.Add(titulo); barra.Children.Add(fechar);
            barra.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource != fechar && e.OriginalSource != fechar.Child) d.DragMove(); };

            var tb = new TextBox
            {
                Text = valor, FontFamily = fonte, FontSize = 13, Foreground = texto, CaretBrush = texto, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 6, 10, 6), VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = (Brush)w.Resources["Azul"]
            };
            var campo = new Border
            {
                Child = tb, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(16, 44, 16, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = Comum.Cor(c ? "#99FFFFFF" : "#33FFFFFF"), BorderBrush = Comum.Cor(c ? "#26000000" : "#40FFFFFF")
            };
            var ok = new Border
            {
                Width = 84, Height = 30, CornerRadius = new CornerRadius(15), Background = (Brush)w.Resources["Azul"], Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 16, 16),
                Child = new TextBlock { Text = "OK", FontFamily = fonte, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };

            string r = null;
            Action confirmar = delegate { r = tb.Text; d.Close(); };
            ok.MouseLeftButtonUp += delegate { confirmar(); };
            fechar.MouseLeftButtonUp += delegate { d.Close(); };
            d.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { confirmar(); e.Handled = true; }
                else if (e.Key == Key.Escape) { d.Close(); e.Handled = true; }
            };

            var cartao = new Grid { Width = L, Height = A, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cartao.Children.Add(aro); cartao.Children.Add(fundo); cartao.Children.Add(campo); cartao.Children.Add(barra); cartao.Children.Add(ok);
            var raiz = new Grid(); raiz.Children.Add(cartao); d.Content = raiz;

            Action posicionar = delegate
            {
                if (hVidro == IntPtr.Zero || !d.IsLoaded) return;
                double f = PresentationSource.FromVisual(d).CompositionTarget.TransformToDevice.M22;
                var p = fundo.PointToScreen(new Point(Recuo, Recuo));
                Win32.Mover(hVidro, (int)p.X, (int)p.Y, (int)((L - 2 * Recuo) * f), (int)((A - 2 * Recuo) * f));
            };
            d.Loaded += delegate
            {
                Win32.Vidro(hVidro, c ? 0x1EF8F6F6 : 0x24383030, true);
                posicionar();
                tb.Focus();
                int fim = valor.LastIndexOf('.'); tb.Select(0, fim > 0 ? fim : valor.Length);
            };
            d.LocationChanged += delegate { posicionar(); };
            try { d.ShowDialog(); }
            finally { vidro.Close(); }
            return r == null ? null : r.Trim();
        }

        // ---------- seleção com o mouse ----------
        // Arrastar a partir do vazio (na lista, também fora da coluna Nome) desenha um laço que seleciona o que tocar; Ctrl soma à seleção.
        // Clicar num item que já faz parte de uma seleção múltipla não a desfaz, para dar para arrastar vários de uma vez.
        void AoPressar(object s, MouseButtonEventArgs e)
        {
            var c = (ListBox)s;
            arrastoInicio = e.GetPosition(w); podeArrastar = false; pendente = null;
            if (DentroDe<ScrollBar>(e) || DentroDe<GridViewColumnHeader>(e)) return;
            var en = Sob(e);
            // clicar na setinha só abre/fecha a pasta em cascata, sem selecionar nem entrar
            var alvo = e.OriginalSource as FrameworkElement;
            if (c == lista && en != null && en.EhPasta && en.Expansivel && alvo != null && (alvo.Tag as string) == "seta")
            {
                AlternarPasta(en); e.Handled = true; return;
            }
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool foraDoNome = c == lista && e.GetPosition(lista).X > ((GridView)lista.View).Columns[0].ActualWidth + 6;
            if (en != null && (!foraDoNome || shift || e.ClickCount > 1))
            {
                podeArrastar = true;
                if (!ctrl && !shift && e.ClickCount == 1 && c.SelectedItems.Count > 1 && c.SelectedItems.Contains(en)) { pendente = en; e.Handled = true; }
                return;
            }
            if (e.ClickCount > 1) return;
            lacoAtivo = true; lacoItem = en;
            lacoInicio = e.GetPosition(laco);
            lacoBase = ctrl ? c.SelectedItems.Cast<object>().ToList() : new List<object>();
            c.Focus(); c.CaptureMouse(); e.Handled = true;
        }

        void AoMoverLaco(object s, MouseEventArgs e)
        {
            if (!lacoAtivo) return;
            e.Handled = true;   // senão o ListBox faz a própria seleção por arrasto
            var p = e.GetPosition(laco);
            if (lacoRet.Visibility != Visibility.Visible &&
                Math.Abs(p.X - lacoInicio.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - lacoInicio.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var r = new Rect(lacoInicio, p);
            Canvas.SetLeft(lacoRet, r.X); Canvas.SetTop(lacoRet, r.Y); lacoRet.Width = r.Width; lacoRet.Height = r.Height;
            lacoRet.Visibility = Visibility.Visible;

            var c = (ListBox)s;
            var marcar = new HashSet<object>(lacoBase);
            foreach (var item in c.Items)
            {
                var ct = c.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (ct == null || !ct.IsVisible) continue;
                if (ct.TransformToVisual(laco).TransformBounds(new Rect(ct.RenderSize)).IntersectsWith(r)) marcar.Add(item);
            }
            foreach (var item in c.SelectedItems.Cast<object>().ToList()) if (!marcar.Contains(item)) c.SelectedItems.Remove(item);
            foreach (var item in marcar) if (!c.SelectedItems.Contains(item)) c.SelectedItems.Add(item);
        }

        void AoSoltarMouse(object s, MouseButtonEventArgs e)
        {
            var c = (ListBox)s;
            if (pendente != null)
            {
                // clicou sem arrastar num item da seleção: fica só ele
                if (podeArrastar) { c.SelectedItems.Clear(); c.SelectedItem = pendente; }
                pendente = null; e.Handled = true;
            }
            if (!lacoAtivo) return;
            lacoAtivo = false; e.Handled = true;
            bool moveu = lacoRet.Visibility == Visibility.Visible;
            lacoRet.Visibility = Visibility.Collapsed;
            c.ReleaseMouseCapture();
            if (moveu) return;
            // clique simples sem arrastar: no vazio limpa a seleção; fora da coluna Nome seleciona a linha
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (lacoItem == null) { if (!ctrl) c.SelectedItems.Clear(); return; }
            if (ctrl) { if (c.SelectedItems.Contains(lacoItem)) c.SelectedItems.Remove(lacoItem); else c.SelectedItems.Add(lacoItem); }
            else { c.SelectedItems.Clear(); c.SelectedItem = lacoItem; }
            var ct = c.ItemContainerGenerator.ContainerFromItem(lacoItem) as ListBoxItem;
            if (ct != null) ct.Focus();
        }

        static bool DentroDe<T>(MouseEventArgs e) where T : DependencyObject
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is T)) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d != null;
        }

        // ---------- arrastar e soltar ----------
        void AoArrastar(object s, MouseEventArgs e)
        {
            if (!podeArrastar || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(w) - arrastoInicio;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            podeArrastar = false;
            if (atual == Lixo) return;   // arrastar para fora levaria o nome $R…; use Pôr de Volta
            var sel = Selecionados().Select(x => x.Caminho).ToArray();
            if (sel.Length == 0) return;
            DragDrop.DoDragDrop((DependencyObject)s, new DataObject(DataFormats.FileDrop, sel), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        // soltar sobre uma pasta da lista vai para ela; no vazio, para a pasta atual
        string DestinoSoltar(DragEventArgs e)
        {
            if (atual == Lixo) return Lixo;   // soltar na janela da Lixeira manda para a Lixeira
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is ListBoxItem)) d = d is Visual ? VisualTreeHelper.GetParent(d) : null;
            var en = d == null ? null : ((ListBoxItem)d).DataContext as Entrada;
            return en != null && en.EhPasta ? en.Caminho : atual;
        }

        // igual ao Explorador: mesmo disco move, outro disco copia; Shift força mover, Ctrl força copiar
        static DragDropEffects Efeito(DragEventArgs e, string destino)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || destino == Recentes) return DragDropEffects.None;
            if (destino == Lixo) return DragDropEffects.Move;
            if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0) return DragDropEffects.Copy;
            if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0) return DragDropEffects.Move;
            var o = ((string[])e.Data.GetData(DataFormats.FileDrop)).FirstOrDefault();
            return o != null && string.Equals(System.IO.Path.GetPathRoot(o), System.IO.Path.GetPathRoot(destino), StringComparison.OrdinalIgnoreCase) ? DragDropEffects.Move : DragDropEffects.Copy;
        }

        void AoSoltar(object s, DragEventArgs e) { ReceberArquivos(e, DestinoSoltar(e)); }

        void ReceberArquivos(DragEventArgs e, string destino)
        {
            var ef = Efeito(e, destino);
            if (ef == DragDropEffects.None) return;
            var origens = ((string[])e.Data.GetData(DataFormats.FileDrop)).Where(o => !string.Equals(o.TrimEnd('\\'), destino.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
            if (destino == Lixo) { MandarParaLixeira(origens, false); return; }
            Transferir(origens, destino, ef == DragDropEffects.Move);
        }

        // ---------- atualização automática quando a pasta muda ----------
        void Vigiar()
        {
            if (vigia != null) { vigia.Dispose(); vigia = null; }
            if (atual == Recentes) return;
            try
            {
                vigia = new FileSystemWatcher(atual == Lixo ? PastasLixo().First() : atual) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (s, e) => w.Dispatcher.BeginInvoke(new Action(() => { if (busca.Text.Length == 0) { recarregar.Stop(); recarregar.Start(); } }));
                vigia.Created += h; vigia.Deleted += h; vigia.Changed += h;
                vigia.Renamed += (s, e) => h(s, e);
                vigia.EnableRaisingEvents = true;
            }
            catch { }
        }

        // datas como no Finder: "Hoje, 14:15", "Ontem, 19:20", "7 de out. de 2026, 21:44"
        public static string DataMac(DateTime d)
        {
            var hoje = DateTime.Today;
            if (d.Date == hoje) return "Hoje, " + d.ToString("HH:mm");
            if (d.Date == hoje.AddDays(-1)) return "Ontem, " + d.ToString("HH:mm");
            if (d.Date == hoje.AddDays(-2)) return "Anteontem, " + d.ToString("HH:mm");
            return d.ToString("d 'de' MMM'.' 'de' yyyy, HH:mm", PtBr).Replace("..", ".");
        }

        // ---------- desenho da janela ----------
        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Finder' Width='1180' Height='720' MinWidth='640' MinHeight='380' WindowStartupLocation='CenterScreen'
        Background='Transparent' FontFamily='Inter, Segoe UI' FontSize='13' UseLayoutRounding='True'>
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight='52' ResizeBorderThickness='6' GlassFrameThickness='-1' CornerRadius='0' UseAeroCaptionButtons='False'/>
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <FontFamily x:Key='Ico'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
    <Style x:Key='Plano' TargetType='ButtonBase'>
      <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Padding' Value='9,5'/>
      <Setter Property='Cursor' Value='Arrow'/>
      <Setter Property='WindowChrome.IsHitTestVisibleInChrome' Value='True'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ButtonBase'>
        <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
          <Trigger Property='ToggleButton.IsChecked' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Pilula}'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='Janela' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
      <Setter Property='Width' Value='46'/>
      <Setter Property='WindowChrome.IsHitTestVisibleInChrome' Value='True'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Border x:Name='b' Background='Transparent'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/><Setter Property='Foreground' Value='White'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType='GridViewColumnHeader'>
      <Setter Property='Foreground' Value='{DynamicResource TextoSec}'/>
      <Setter Property='HorizontalContentAlignment' Value='Left'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='GridViewColumnHeader'>
        <Grid Background='Transparent'>
          <Border BorderBrush='{DynamicResource Borda}' BorderThickness='0,0,1,1' Padding='10,5'>
            <ContentPresenter HorizontalAlignment='Left' TextBlock.FontSize='12' TextBlock.FontWeight='SemiBold'/></Border>
          <Thumb x:Name='PART_HeaderGripper' HorizontalAlignment='Right' Width='6' Opacity='0' Cursor='SizeWE'/>
        </Grid>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='Linha' TargetType='ListViewItem'>
      <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
      <Setter Property='Height' Value='36'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListViewItem'>
        <Border x:Name='b' Background='Transparent' CornerRadius='5' Margin='8,0'><GridViewRowPresenter VerticalAlignment='Center'/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='ItemsControl.AlternationIndex' Value='1'><Setter TargetName='b' Property='Background' Value='{DynamicResource Zebra}'/></Trigger>
          <Trigger Property='IsSelected' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Selecao}'/><Setter Property='Foreground' Value='White'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='Bloco' TargetType='ListBoxItem'>
      <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
      <Setter Property='Margin' Value='4'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
        <StackPanel Width='{DynamicResource LarguraBloco}'>
          <Border x:Name='fi' CornerRadius='8' Padding='4' HorizontalAlignment='Center' Background='Transparent'>
            <Image Source='{Binding Grande}' Height='{DynamicResource TamIcone}' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality'/></Border>
          <Border x:Name='fn' CornerRadius='4' Padding='4,1' HorizontalAlignment='Center' Margin='0,3,0,0' Background='Transparent'>
            <TextBlock Text='{Binding Nome}' TextAlignment='Center' TextWrapping='Wrap' MaxHeight='34' TextTrimming='CharacterEllipsis' FontSize='12'/></Border>
        </StackPanel>
        <ControlTemplate.Triggers>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='fi' Property='Background' Value='{DynamicResource Hover}'/>
            <Setter TargetName='fn' Property='Background' Value='{DynamicResource Selecao}'/><Setter Property='Foreground' Value='White'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <!-- barras de rolagem finas do macOS: sem setas, trilho transparente, alça arredondada que engrossa no hover -->
    <Style TargetType='Thumb' x:Key='Alca'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'>
        <Border CornerRadius='3' Background='{DynamicResource TextoSec}' Opacity='0.55'/>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType='ScrollBar'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Width' Value='10'/>
      <Setter Property='MinWidth' Value='10'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,2'>
            <Track.Thumb><Thumb Style='{StaticResource Alca}'/></Track.Thumb>
          </Track>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='PART_Track' Property='Margin' Value='1,2'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
      <Style.Triggers>
        <Trigger Property='Orientation' Value='Horizontal'>
          <Setter Property='Width' Value='Auto'/>
          <Setter Property='MinWidth' Value='0'/>
          <Setter Property='Height' Value='10'/>
          <Setter Property='MinHeight' Value='10'/>
          <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
            <Grid Background='Transparent'>
              <Track x:Name='PART_Track' IsDirectionReversed='False' Margin='2,3'>
                <Track.Thumb><Thumb Style='{StaticResource Alca}'/></Track.Thumb>
              </Track>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='PART_Track' Property='Margin' Value='2,1'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Trigger>
      </Style.Triggers>
    </Style>
  </Window.Resources>

  <Grid x:Name='raiz'>
    <Grid.ColumnDefinitions><ColumnDefinition x:Name='colLateral' Width='228' MinWidth='150' MaxWidth='520'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <!-- barra lateral sobre o vidro -->
    <Border Background='{DynamicResource Lateral}'>
      <Grid>
        <Grid.RowDefinitions><RowDefinition Height='*'/><RowDefinition Height='30'/></Grid.RowDefinitions>
        <ScrollViewer Grid.Row='0' VerticalScrollBarVisibility='Auto' Margin='0,46,0,8'><StackPanel x:Name='lateral' TextElement.Foreground='{DynamicResource Texto}'/></ScrollViewer>
        <!-- contagem no canto inferior esquerdo, na mesma posiÃ§Ã£o do Explorador do Windows -->
        <Border Grid.Row='1' BorderBrush='{DynamicResource Borda}' BorderThickness='0,1,0,0'>
          <TextBlock x:Name='statusSelecao' Margin='10,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}' FontSize='11.5' Text='0 itens'/>
        </Border>
      </Grid>
    </Border>
    <TextBlock Text='Finder' Margin='18,16,0,0' FontWeight='SemiBold' Foreground='{DynamicResource TextoSec}' IsHitTestVisible='False'/>
    <!-- arrastar a divisa alarga ou estreita a barra lateral -->
    <GridSplitter x:Name='divisa' Grid.Column='0' HorizontalAlignment='Right' VerticalAlignment='Stretch' Width='6' Margin='0,0,-3,0' Panel.ZIndex='5'
                  Background='Transparent' ResizeDirection='Columns' ResizeBehavior='CurrentAndNext' Cursor='SizeWE' WindowChrome.IsHitTestVisibleInChrome='True'/>

    <Grid Grid.Column='1' Background='{DynamicResource Fundo}'>
      <Grid.RowDefinitions><RowDefinition Height='52'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='30'/></Grid.RowDefinitions>
      <!-- barra de cima -->
      <DockPanel Margin='10,0,0,0' LastChildFill='False'>
        <Border DockPanel.Dock='Left' Background='{DynamicResource Pilula}' CornerRadius='15' VerticalAlignment='Center' Padding='2'>
          <StackPanel Orientation='Horizontal'>
            <Button x:Name='voltar' Style='{StaticResource Plano}' ToolTip='Voltar'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE76B;' FontSize='12'/></Button>
            <Button x:Name='avancar' Style='{StaticResource Plano}' ToolTip='Avançar'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE76C;' FontSize='12'/></Button>
          </StackPanel></Border>
        <TextBlock x:Name='titulo' DockPanel.Dock='Left' FontSize='15' FontWeight='Bold' Margin='14,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource Texto}' TextTrimming='CharacterEllipsis' MaxWidth='360'/>
        <StackPanel DockPanel.Dock='Right' Orientation='Horizontal'>
          <Button x:Name='bMin' Style='{StaticResource Janela}' Tag='#FEBC2E'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE921;' FontSize='10'/></Button>
          <Button x:Name='bMax' Style='{StaticResource Janela}' Tag='#28C840'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE922;' FontSize='10'/></Button>
          <Button x:Name='bFechar' Style='{StaticResource Janela}' Tag='#C42B1C'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8BB;' FontSize='10'/></Button>
        </StackPanel>
        <Border DockPanel.Dock='Right' Background='{DynamicResource Campo}' CornerRadius='15' Width='210' Height='30' Margin='8,0,14,0' VerticalAlignment='Center' WindowChrome.IsHitTestVisibleInChrome='True'>
          <Grid><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE721;' FontSize='12' Margin='11,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}'/>
            <TextBox x:Name='busca' Margin='30,0,10,0' VerticalAlignment='Center' Background='Transparent' BorderThickness='0' Foreground='{DynamicResource Texto}' CaretBrush='{DynamicResource Texto}'/>
            <TextBlock Text='Buscar' Margin='32,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}' IsHitTestVisible='False'>
              <TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Visibility' Value='Collapsed'/><Style.Triggers>
                <DataTrigger Binding='{Binding Text, ElementName=busca}' Value=''><Setter Property='Visibility' Value='Visible'/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock></Grid></Border>
        <Border DockPanel.Dock='Right' Background='{DynamicResource Pilula}' CornerRadius='15' VerticalAlignment='Center' Padding='2' Margin='8,0,0,0'>
          <Button x:Name='mais' Style='{StaticResource Plano}' ToolTip='Mais'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE712;' FontSize='13'/></Button></Border>
        <Border DockPanel.Dock='Right' Background='{DynamicResource Pilula}' CornerRadius='15' VerticalAlignment='Center' Padding='2'>
          <StackPanel Orientation='Horizontal'>
            <ToggleButton x:Name='vIcones' Style='{StaticResource Plano}' ToolTip='Ícones'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xF0E2;' FontSize='13'/></ToggleButton>
            <ToggleButton x:Name='vLista' Style='{StaticResource Plano}' ToolTip='Lista' IsChecked='True'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8FD;' FontSize='13'/></ToggleButton>
            <ToggleButton x:Name='vGaleria' Style='{StaticResource Plano}' ToolTip='Galeria'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8B9;' FontSize='13'/></ToggleButton>
          </StackPanel></Border>
        <Border x:Name='limparLixo' DockPanel.Dock='Right' Visibility='Collapsed' Background='{DynamicResource Pilula}' CornerRadius='15' VerticalAlignment='Center' Padding='2' Margin='0,0,8,0'>
          <Button x:Name='bLimparLixo' Style='{StaticResource Plano}' Padding='12,5'><TextBlock Text='Limpar Lixeira' FontSize='12.5'/></Button></Border>
      </DockPanel>

      <!-- lista -->
      <ListView x:Name='lista' Grid.Row='2' FontSize='15' BorderThickness='0' Background='Transparent' AlternationCount='2' SelectionMode='Extended'
                ItemContainerStyle='{StaticResource Linha}' VirtualizingPanel.IsVirtualizing='True' VirtualizingPanel.VirtualizationMode='Recycling'
                ScrollViewer.HorizontalScrollBarVisibility='Disabled'>
        <ListView.View><GridView>
          <GridViewColumn Header='Nome' Width='440'><GridViewColumn.CellTemplate><DataTemplate>
            <StackPanel Orientation='Horizontal' Margin='{Binding Recuo}'>
              <TextBlock Tag='seta' Text='{Binding SetaTexto}' Visibility='{Binding SetaVis}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' Width='16' TextAlignment='Center' VerticalAlignment='Center' Opacity='0.6' Background='Transparent'/>
              <Grid Width='28' Height='28' Margin='0,0,10,0'><Image Source='{Binding Icone}' RenderOptions.BitmapScalingMode='HighQuality'><Image.Style><Style TargetType='Image'><Setter Property='Width' Value='18'/><Setter Property='Height' Value='18'/><Style.Triggers><DataTrigger Binding='{Binding EhPasta}' Value='True'><Setter Property='Width' Value='28'/><Setter Property='Height' Value='28'/></DataTrigger></Style.Triggers></Style></Image.Style></Image></Grid>
              <TextBlock Text='{Binding Nome}' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/></StackPanel></DataTemplate></GridViewColumn.CellTemplate></GridViewColumn>
          <GridViewColumn Header='Tamanho' Width='100'><GridViewColumn.CellTemplate><DataTemplate>
            <TextBlock Text='{Binding TamanhoTexto}' Opacity='0.75'/></DataTemplate></GridViewColumn.CellTemplate></GridViewColumn>
          <GridViewColumn Header='Tipo' Width='190'><GridViewColumn.CellTemplate><DataTemplate>
            <TextBlock Text='{Binding Tipo}' Opacity='0.75' TextTrimming='CharacterEllipsis'/></DataTemplate></GridViewColumn.CellTemplate></GridViewColumn>
          <GridViewColumn Header='Data de Modificação' Width='210'><GridViewColumn.CellTemplate><DataTemplate>
            <TextBlock Text='{Binding DataTexto}' Opacity='0.75'/></DataTemplate></GridViewColumn.CellTemplate></GridViewColumn>
        </GridView></ListView.View>
      </ListView>
      <!-- ícones e galeria -->
      <ListBox x:Name='icones' Grid.Row='2' Visibility='Collapsed' BorderThickness='0' Background='Transparent' SelectionMode='Extended' Tag='icones' Padding='12'
               ItemContainerStyle='{StaticResource Bloco}' ScrollViewer.HorizontalScrollBarVisibility='Disabled'>
        <ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ListBox.ItemsPanel>
      </ListBox>
      <!-- laço de seleção (arrastar o mouse a partir do vazio) -->
      <Canvas x:Name='laco' Grid.Row='2' IsHitTestVisible='False' ClipToBounds='True'>
        <Grid x:Name='lacoRet' Visibility='Collapsed'><Border Background='{DynamicResource Azul}' Opacity='0.2'/><Border BorderBrush='{DynamicResource Azul}' BorderThickness='1'/></Grid></Canvas>

      <!-- abas (só com duas ou mais, como no Mac) -->
      <Border x:Name='abasFundo' Grid.Row='1' Visibility='Collapsed' Background='{DynamicResource Pilula}' BorderBrush='{DynamicResource Borda}' BorderThickness='0,1,0,1' Padding='4,3'>
        <DockPanel><Button x:Name='novaAba' DockPanel.Dock='Right' Style='{StaticResource Plano}' ToolTip='Nova aba (Ctrl+T)' Margin='4,0,0,0'>
            <TextBlock FontFamily='{StaticResource Ico}' Text='&#xE710;' FontSize='11' Foreground='{DynamicResource Texto}'/></Button>
          <UniformGrid x:Name='abas' Rows='1'/></DockPanel></Border>
      <!-- caminho -->
      <Border x:Name='caminhoFundo' Grid.Row='3' BorderBrush='{DynamicResource Borda}' BorderThickness='0,1,0,0' Background='Transparent'>
        <Grid>
          <StackPanel x:Name='caminho' Orientation='Horizontal' Margin='0,0,10,0' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource TextoSec}'/>
          <TextBox x:Name='endereco' Visibility='Collapsed' Margin='0,0,8,0' Padding='4,2' VerticalAlignment='Center' FontSize='11.5' Background='{DynamicResource Campo}' BorderThickness='0'
                   Foreground='{DynamicResource Texto}' CaretBrush='{DynamicResource Texto}'/></Grid></Border>
    </Grid>
  </Grid>
</Window>";
    }
}
