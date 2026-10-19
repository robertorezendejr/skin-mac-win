// Mac Skin Finder: navegador de arquivos no visual do Finder do macOS (abre com MacSkin.exe --finder [pasta]).
// Barra lateral com vidro, voltar/avançar, título, modos Lista/Ícones/Galeria, busca, caminho embaixo.
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

        Window w;
        ListView lista; ListBox icones;
        StackPanel lateral, caminhoBarra;
        TextBlock titulo;
        TextBox busca;
        Button voltar, avancar;
        ToggleButton vLista, vIcones, vGaleria;
        readonly ObservableCollection<Entrada> itens = new ObservableCollection<Entrada>();
        ICollectionView visao;
        string atual;
        readonly List<string> historico = new List<string>();
        int pos = -1;
        string modo = "lista";
        string ordem = "Nome"; bool crescente = true;
        FileSystemWatcher vigia;
        DispatcherTimer recarregar;
        CancellationTokenSource buscaCancel;
        Point arrastoInicio; bool podeArrastar;
        IntPtr hwnd;

        static void Log(string m) { Comum.Log("finder.log", m); }

        public Finder(string inicial)
        {
            w = (Window)XamlReader.Parse(Xaml);
            lista = (ListView)w.FindName("lista"); icones = (ListBox)w.FindName("icones");
            lateral = (StackPanel)w.FindName("lateral"); caminhoBarra = (StackPanel)w.FindName("caminho");
            titulo = (TextBlock)w.FindName("titulo"); busca = (TextBox)w.FindName("busca");
            voltar = (Button)w.FindName("voltar"); avancar = (Button)w.FindName("avancar");
            vLista = (ToggleButton)w.FindName("vLista"); vIcones = (ToggleButton)w.FindName("vIcones"); vGaleria = (ToggleButton)w.FindName("vGaleria");

            icones.Resources["LarguraBloco"] = 112.0; icones.Resources["TamIcone"] = 64.0;
            visao = CollectionViewSource.GetDefaultView(itens);
            lista.ItemsSource = visao; icones.ItemsSource = visao;
            Tema();

            voltar.Click += delegate { if (pos > 0) { pos--; Navegar(historico[pos], false); } };
            avancar.Click += delegate { if (pos < historico.Count - 1) { pos++; Navegar(historico[pos], false); } };
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

            busca.TextChanged += delegate { Filtrar(); };
            busca.KeyDown += (s, e) => { if (e.Key == Key.Enter) BuscarTudo(busca.Text); if (e.Key == Key.Escape) { busca.Text = ""; lista.Focus(); } };

            foreach (var c in new Selector[] { lista, icones })
            {
                c.MouseDoubleClick += (s, e) => { if (Sob(e) != null) AbrirSelecionados(); };
                c.KeyDown += Teclas;
                c.PreviewMouseLeftButtonDown += (s, e) => { arrastoInicio = e.GetPosition(w); podeArrastar = Sob(e) != null; };
                c.PreviewMouseMove += AoArrastar;
                c.AllowDrop = true;
                c.Drop += AoSoltar;
                c.DragOver += (s, e) => { e.Effects = Efeito(e, DestinoSoltar(e)); e.Handled = true; };
                c.ContextMenu = new ContextMenu();   // sem um menu inicial o evento abaixo não dispara
                c.ContextMenuOpening += (s, e) => { ((FrameworkElement)s).ContextMenu = MenuItens(); };
            }
            lista.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var h = e.OriginalSource as GridViewColumnHeader;
                if (h == null || h.Column == null) return;
                string campo = (string)h.Column.Header == "Tamanho" ? "Tamanho" : (string)h.Column.Header == "Tipo" ? "Tipo" : (string)h.Column.Header == "Nome" ? "Nome" : "Data";
                crescente = ordem == campo ? !crescente : true; ordem = campo; Ordenar();
            }));
            w.PreviewKeyDown += (s, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                if (ctrl && e.Key == Key.F) { busca.Focus(); busca.SelectAll(); e.Handled = true; }
                else if (ctrl && e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { NovaJanela(atual); e.Handled = true; }
                else if (ctrl && e.Key == Key.W) { w.Close(); e.Handled = true; }
                else if (alt && e.SystemKey == Key.Left) { voltar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                else if (alt && e.SystemKey == Key.Right) { avancar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
                else if (alt && e.SystemKey == Key.Up) { Subir(); e.Handled = true; }
                else if (e.Key == Key.F5) { Navegar(atual, false); e.Handled = true; }
            };

            recarregar = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            recarregar.Tick += delegate { recarregar.Stop(); Carregar(atual, true); };

            w.SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(w).Handle;
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                Vidro();
            };
            w.Closed += delegate { if (vigia != null) vigia.Dispose(); };

            MontarLateral();
            Navegar(string.IsNullOrEmpty(inicial) || !Directory.Exists(inicial) ? Pasta(Environment.SpecialFolder.UserProfile, "Downloads") : inicial, true);
        }

        public void Mostrar() { w.Show(); w.Activate(); lista.Focus(); }

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
            Secao("Localizações");
            foreach (var var in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
            {
                string p = Environment.GetEnvironmentVariable(var);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !lateral.Children.OfType<FrameworkElement>().Any(x => (x.Tag as string) == p))
                    Atalho("", System.IO.Path.GetFileName(p).Replace("OneDrive - ", "OneDrive · "), p);
            }
            Atalho("", Environment.UserName, Pasta(Environment.SpecialFolder.UserProfile));
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                string nome = string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Network ? "Rede" : "Disco Local") : d.VolumeLabel;
                Atalho(d.DriveType == DriveType.Network ? "" : d.DriveType == DriveType.Removable ? "" : "", nome + " (" + d.Name.TrimEnd('\\') + ")", d.Name);
            }
        }

        void Secao(string nome)
        {
            lateral.Children.Add(new TextBlock { Text = nome, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 14, 0, 4), Foreground = (Brush)w.Resources["TextoSec"] });
        }

        void Atalho(string glifo, string nome, string destino)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glifo, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)w.Resources["Azul"] });
            sp.Children.Add(new TextBlock { Text = nome, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Border { Child = sp, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(8, 1, 8, 1), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Tag = destino };
            b.MouseEnter += delegate { if (destino != atual) b.Background = (Brush)w.Resources["Hover"]; };
            b.MouseLeave += delegate { if (destino != atual) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate
            {
                if (destino.StartsWith("shell:")) { Process.Start("explorer.exe", destino); return; }
                Navegar(destino, true);
            };
            // soltar arquivos num atalho da lateral copia/move para lá
            b.AllowDrop = destino != Recentes && !destino.StartsWith("shell:");
            b.Drop += (s, e) => { ReceberArquivos(e, destino); e.Handled = true; };
            lateral.Children.Add(b);
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
            if (destino != Recentes && !Directory.Exists(destino)) { MessageBox.Show(w, "A pasta não existe mais:\n" + destino, "Finder"); return; }
            if (novo)
            {
                if (pos < historico.Count - 1) historico.RemoveRange(pos + 1, historico.Count - pos - 1);
                historico.Add(destino); pos = historico.Count - 1;
            }
            atual = destino;
            voltar.IsEnabled = pos > 0; avancar.IsEnabled = pos < historico.Count - 1;
            titulo.Text = destino == Recentes ? "Recentes" : NomePasta(destino);
            w.Title = titulo.Text + " — Finder";
            if (busca.Text.Length > 0) busca.Text = "";
            MarcarLateral(); MontarCaminho(); Vigiar();
            Carregar(destino, false);
        }

        static string NomePasta(string p)
        {
            string n = System.IO.Path.GetFileName(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(n)) { try { var d = new DriveInfo(p); n = (string.IsNullOrEmpty(d.VolumeLabel) ? "Disco Local" : d.VolumeLabel) + " (" + p.TrimEnd('\\') + ")"; } catch { n = p; } }
            return n;
        }

        void Subir()
        {
            if (atual == Recentes) return;
            var pai = Directory.GetParent(atual.TrimEnd('\\'));
            if (pai != null) Navegar(pai.FullName, true);
        }

        // caminho clicável embaixo: Disco Local (C:) › Usuários › roberto › Downloads
        void MontarCaminho()
        {
            caminhoBarra.Children.Clear();
            if (atual == Recentes) { Pedaco("Recentes", null, true); return; }
            var partes = new List<string>();
            for (var d = new DirectoryInfo(atual); d != null; d = d.Parent) partes.Insert(0, d.FullName);
            for (int i = 0; i < partes.Count; i++) Pedaco(NomePasta(partes[i]), partes[i], i == partes.Count - 1);
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
                foreach (var e in Ordem(t.Result)) itens.Add(e);
                PedirIcones();
                if (antes != null) foreach (var e in itens.Where(x => antes.Contains(x.Caminho))) lista.SelectedItems.Add(e);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static List<Entrada> Ler(string destino)
        {
            var r = new List<Entrada>();
            try
            {
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
            var sel = Selecionados().ToList();
            var l = Ordem(itens.ToList()).ToList();
            itens.Clear(); foreach (var e in l) itens.Add(e);
            foreach (var e in sel) lista.SelectedItems.Add(e);
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

        void PedirIcones()
        {
            int g = geracao;
            int px = modo == "galeria" ? 160 : modo == "icones" ? 96 : 32;
            foreach (var e in itens)
            {
                var en = e;
                if (en.EhPasta)
                {
                    string esp; var mac = IconeMac(pastasEspeciais.TryGetValue(en.Caminho, out esp) ? esp : "GenericFolderIcon");
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
            visao.Filter = t.Length == 0 ? null : (Predicate<object>)(o => ((Entrada)o).Nome.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        // Enter na busca: procura em todas as subpastas (em segundo plano, até 2000 resultados)
        void BuscarTudo(string termo)
        {
            termo = termo.Trim();
            if (termo.Length == 0 || atual == Recentes) return;
            if (buscaCancel != null) buscaCancel.Cancel();
            var cts = buscaCancel = new CancellationTokenSource();
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
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------- abrir ----------
        IEnumerable<Entrada> Selecionados()
        {
            var c = modo == "lista" ? (System.Collections.IList)lista.SelectedItems : icones.SelectedItems;
            return c.Cast<Entrada>().ToList();
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
            if (sel.Count == 1 && sel[0].EhPasta) { Navegar(sel[0].Caminho, true); return; }
            foreach (var e in sel)
            {
                try { if (e.EhPasta) NovaJanela(e.Caminho); else Process.Start(e.Caminho); }
                catch (Exception ex) { Log("abrir " + e.Caminho + ": " + ex.Message); }
            }
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
            else if (e.Key == Key.Back) voltar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (e.Key == Key.Delete) Lixeira(shift);
            else if (e.Key == Key.F2) Renomear();
            else if (ctrl && e.Key == Key.C) Copiar(false);
            else if (ctrl && e.Key == Key.X) Copiar(true);
            else if (ctrl && e.Key == Key.V) Colar();
            else if (ctrl && shift && e.Key == Key.N) NovaPasta();
            else return;
            e.Handled = true;
        }

        // ---------- menus ----------
        ContextMenu MenuItens()
        {
            var sel = Selecionados().ToList();
            var m = new ContextMenu();
            Action<string, Action, bool> op = (nome, acao, ativo) => { var i = new MenuItem { Header = nome, IsEnabled = ativo }; i.Click += delegate { acao(); }; m.Items.Add(i); };
            bool tem = sel.Count > 0, um = sel.Count == 1, pastaReal = atual != Recentes;
            op("Abrir", AbrirSelecionados, tem);
            op("Abrir com…", () => Shell.AbrirCom(sel[0].Caminho), um && !sel[0].EhPasta);
            op("Abrir em Nova Janela", () => { foreach (var e in sel.Where(x => x.EhPasta)) NovaJanela(e.Caminho); }, sel.Any(x => x.EhPasta));
            m.Items.Add(new Separator());
            op("Mover para o Lixo", () => Lixeira(false), tem);
            m.Items.Add(new Separator());
            op("Renomear", Renomear, um);
            op("Copiar", () => Copiar(false), tem);
            op("Recortar", () => Copiar(true), tem);
            op("Colar Item", Colar, pastaReal && Clipboard.ContainsFileDropList());
            op("Nova Pasta", NovaPasta, pastaReal);
            m.Items.Add(new Separator());
            op("Copiar Caminho", () => Clipboard.SetText(tem ? string.Join(Environment.NewLine, sel.Select(e => e.Caminho)) : atual), true);
            op("Obter Informações", () => Shell.Propriedades(tem ? sel[0].Caminho : atual), tem || pastaReal);
            op("Mostrar no Explorador", MostrarNoExplorador, true);
            return m;
        }

        void MenuMais(Button b)
        {
            var m = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
            Action<string, Action> op = (nome, acao) => { var i = new MenuItem { Header = nome }; i.Click += delegate { acao(); }; m.Items.Add(i); };
            op("Nova Pasta", NovaPasta);
            op("Nova Janela do Finder", () => NovaJanela(atual));
            op("Abrir esta pasta no Explorador", MostrarNoExplorador);
            op("Atualizar", () => Navegar(atual, false));
            m.IsOpen = true;
        }

        void MostrarNoExplorador()
        {
            var sel = Selecionados().ToList();
            if (sel.Count > 0) Process.Start("explorer.exe", "/select,\"" + sel[0].Caminho + "\"");
            else if (atual != Recentes) Process.Start("explorer.exe", "\"" + atual + "\"");
        }

        // ---------- operações de arquivo (diálogos de progresso e conflito do próprio Windows) ----------
        void Lixeira(bool definitivo)
        {
            var sel = Selecionados().ToList();
            if (sel.Count == 0) return;
            if (definitivo && MessageBox.Show(w, "Apagar " + sel.Count + " item(ns) definitivamente? Não vai para a Lixeira.", "Finder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var opc = definitivo ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
            foreach (var e in sel)
            {
                try
                {
                    if (e.EhPasta) FileSystem.DeleteDirectory(e.Caminho, UIOption.OnlyErrorDialogs, opc);
                    else FileSystem.DeleteFile(e.Caminho, UIOption.OnlyErrorDialogs, opc);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log("lixo: " + ex.Message); }
            }
            Carregar(atual, false);
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
            if (atual == Recentes || !Clipboard.ContainsFileDropList()) return;
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
            if (atual == Recentes) return;
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
            var d = new Window
            {
                Title = tituloJanela, Owner = w, Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = (Brush)w.Resources["Fundo"]
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            var tb = new TextBox { Text = valor, FontSize = 13, Padding = new Thickness(6, 4, 6, 4) };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            string r = null;
            ok.Click += delegate { r = tb.Text; d.Close(); };
            sp.Children.Add(tb); sp.Children.Add(ok); d.Content = sp;
            d.Loaded += delegate
            {
                tb.Focus();
                int fim = valor.LastIndexOf('.'); tb.Select(0, fim > 0 ? fim : valor.Length);
            };
            d.ShowDialog();
            return r == null ? null : r.Trim();
        }

        // ---------- arrastar e soltar ----------
        void AoArrastar(object s, MouseEventArgs e)
        {
            if (!podeArrastar || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(w) - arrastoInicio;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            podeArrastar = false;
            var sel = Selecionados().Select(x => x.Caminho).ToArray();
            if (sel.Length == 0) return;
            DragDrop.DoDragDrop((DependencyObject)s, new DataObject(DataFormats.FileDrop, sel), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        // soltar sobre uma pasta da lista vai para ela; no vazio, para a pasta atual
        string DestinoSoltar(DragEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && !(d is ListBoxItem)) d = d is Visual ? VisualTreeHelper.GetParent(d) : null;
            var en = d == null ? null : ((ListBoxItem)d).DataContext as Entrada;
            return en != null && en.EhPasta ? en.Caminho : atual;
        }

        // igual ao Explorador: mesmo disco move, outro disco copia; Shift força mover, Ctrl força copiar
        static DragDropEffects Efeito(DragEventArgs e, string destino)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || destino == Recentes) return DragDropEffects.None;
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
            Transferir(origens, destino, ef == DragDropEffects.Move);
        }

        // ---------- atualização automática quando a pasta muda ----------
        void Vigiar()
        {
            if (vigia != null) { vigia.Dispose(); vigia = null; }
            if (atual == Recentes) return;
            try
            {
                vigia = new FileSystemWatcher(atual) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
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
      <Setter Property='Height' Value='28'/>
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
  </Window.Resources>

  <Grid x:Name='raiz'>
    <Grid.ColumnDefinitions><ColumnDefinition Width='228'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <!-- barra lateral sobre o vidro -->
    <Border Background='{DynamicResource Lateral}'>
      <ScrollViewer VerticalScrollBarVisibility='Auto' Margin='0,46,0,8'><StackPanel x:Name='lateral' TextElement.Foreground='{DynamicResource Texto}'/></ScrollViewer>
    </Border>
    <TextBlock Text='Finder' Margin='18,16,0,0' FontWeight='SemiBold' Foreground='{DynamicResource TextoSec}' IsHitTestVisible='False'/>

    <Grid Grid.Column='1' Background='{DynamicResource Fundo}'>
      <Grid.RowDefinitions><RowDefinition Height='52'/><RowDefinition Height='*'/><RowDefinition Height='30'/></Grid.RowDefinitions>
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
      </DockPanel>

      <!-- lista -->
      <ListView x:Name='lista' Grid.Row='1' BorderThickness='0' Background='Transparent' AlternationCount='2' SelectionMode='Extended'
                ItemContainerStyle='{StaticResource Linha}' VirtualizingPanel.IsVirtualizing='True' VirtualizingPanel.VirtualizationMode='Recycling'
                ScrollViewer.HorizontalScrollBarVisibility='Disabled'>
        <ListView.View><GridView>
          <GridViewColumn Header='Nome' Width='440'><GridViewColumn.CellTemplate><DataTemplate>
            <StackPanel Orientation='Horizontal'><Image Source='{Binding Icone}' Width='18' Height='18' Margin='0,0,8,0' RenderOptions.BitmapScalingMode='HighQuality'/>
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
      <ListBox x:Name='icones' Grid.Row='1' Visibility='Collapsed' BorderThickness='0' Background='Transparent' SelectionMode='Extended' Tag='icones' Padding='12'
               ItemContainerStyle='{StaticResource Bloco}' ScrollViewer.HorizontalScrollBarVisibility='Disabled'>
        <ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate></ListBox.ItemsPanel>
      </ListBox>

      <!-- caminho -->
      <Border Grid.Row='2' BorderBrush='{DynamicResource Borda}' BorderThickness='0,1,0,0'>
        <StackPanel x:Name='caminho' Orientation='Horizontal' Margin='10,0' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource TextoSec}'/></Border>
    </Grid>
  </Grid>
</Window>";
    }
}
