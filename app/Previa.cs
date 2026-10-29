// Mac Skin Finder: Visualização Rápida, como o Quick Look do macOS. Espaço abre e fecha a prévia do item selecionado,
// as setas trocam de item com ela aberta e Esc fecha.
// Imagens, vídeo/áudio e texto o próprio WPF mostra; PDF, Office e o resto usam os preview handlers do Windows
// (os mesmos do painel de visualização do Explorador), rodando fora do processo (prevhost.exe) para um handler
// com defeito não derrubar a dock e a barra. Sem nada disso: ícone grande com nome, tipo, tamanho e data.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace MacSkin
{
    class Previa
    {
        readonly Window dono;
        Window w;
        Grid raiz;
        Border area;
        TextBlock titulo;
        Entrada atual;
        int vez;   // cada troca de item invalida o que ainda estiver carregando do item anterior
        MediaElement midia;
        Hospedeiro hosp;
        object handler;
        bool claro;

        public Action<int> Mover;   // setas com a prévia em foco: o Finder troca o item selecionado
        public bool Aberta { get { return w != null && w.IsVisible; } }

        public Previa(Window dono) { this.dono = dono; }

        static readonly HashSet<string> Imagens = Ext("jpg jpeg jpe jfif png gif bmp dib tif tiff ico webp heic heif");
        static readonly HashSet<string> Videos = Ext("mp4 m4v mov wmv avi mkv webm mpg mpeg 3gp");
        static readonly HashSet<string> Audios = Ext("mp3 wav wma m4a aac flac");
        static readonly HashSet<string> Textos = Ext("txt md log json xml csv tsv ini cfg conf yml yaml toml cs js ts tsx jsx py ps1 psm1 cmd bat sh sql html htm css scss java c cpp h hpp go rs rb php swift kt vb lua r nsi reg gitignore env");
        static HashSet<string> Ext(string s) { return new HashSet<string>(s.Split(' ').Select(e => "." + e), StringComparer.OrdinalIgnoreCase); }

        // ---------- abrir, trocar, fechar ----------
        public void Mostrar(Entrada e)
        {
            if (e == null) return;
            if (w == null) Criar();
            if (atual != null && atual.Caminho == e.Caminho && w.IsVisible) return;
            atual = e; vez++;
            Limpar();
            titulo.Text = e.Nome;
            string ext = Path.GetExtension(e.Caminho);
            if (e.EhPasta) Ficha(e);
            else if (Imagens.Contains(ext)) Imagem(e);
            else if (Videos.Contains(ext) || Audios.Contains(ext)) Midia(e, Audios.Contains(ext));
            else if (Textos.Contains(ext)) Texto(e);
            else if (!Handler(e)) TextoOuFicha(e);
            if (!w.IsVisible)
            {
                w.Show();
                // entra crescendo um pouco, como o Quick Look
                var esc = new ScaleTransform(0.94, 0.94);
                raiz.RenderTransform = esc; raiz.RenderTransformOrigin = new Point(0.5, 0.5);
                var t = new Duration(TimeSpan.FromMilliseconds(160));
                var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
                esc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, t) { EasingFunction = suave });
                esc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, t) { EasingFunction = suave });
                raiz.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, t));
            }
        }

        public void Fechar()
        {
            if (w == null) return;
            vez++;
            Limpar();
            atual = null;
            w.Hide();
            dono.Activate();
        }

        // para vídeo, solta o preview handler e esvazia a área
        void Limpar()
        {
            if (midia != null) { midia.Stop(); midia.Close(); midia = null; }
            if (handler != null)
            {
                try { ((IPreviewHandler)handler).Unload(); } catch { }
                try { Marshal.FinalReleaseComObject(handler); } catch { }
                handler = null;
            }
            if (hosp != null) { hosp.Dispose(); hosp = null; }
            area.Child = null;
            area.Background = Brushes.Transparent;
        }

        // ---------- janela ----------
        void Criar()
        {
            claro = Comum.TemaClaro();
            var texto = Comum.Cor(claro ? "#FF1D1D1F" : "#FFECECEC");
            w = new Window
            {
                Owner = dono, Title = "Visualização Rápida", ShowInTaskbar = false, ShowActivated = false,
                Background = Brushes.Transparent, FontFamily = new FontFamily("Inter, Segoe UI"), FontSize = 13, UseLayoutRounding = true,
                WindowStartupLocation = WindowStartupLocation.Manual, MinWidth = 320, MinHeight = 200, Width = 640, Height = 420
            };
            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });
            raiz = new Grid { Background = Comum.Cor(claro ? "#99F6F6F6" : "#99202022") };
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            raiz.RowDefinitions.Add(new RowDefinition());

            var topo = new Grid();
            var fechar = Botao("", "Fechar (Espaço)", texto);
            fechar.HorizontalAlignment = HorizontalAlignment.Left; fechar.Margin = new Thickness(10, 0, 0, 0);
            fechar.Click += delegate { Fechar(); };
            var abrir = Botao("", "Abrir (Enter)", texto);
            abrir.HorizontalAlignment = HorizontalAlignment.Right; abrir.Margin = new Thickness(0, 0, 10, 0);
            abrir.Click += delegate { Abrir(); };
            titulo = new TextBlock
            {
                Foreground = texto, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(56, 0, 56, 0)
            };
            topo.Children.Add(titulo); topo.Children.Add(fechar); topo.Children.Add(abrir);
            raiz.Children.Add(topo);

            area = new Border { ClipToBounds = true };
            Grid.SetRow(area, 1);
            raiz.Children.Add(area);
            w.Content = raiz;

            w.SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(w).Handle;
                HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
                int escuro = claro ? 0 : 1; DwmSetWindowAttribute(h, 20, ref escuro, 4);
                int fundo = 3; DwmSetWindowAttribute(h, 38, ref fundo, 4);   // vidro acrylic, como o fundo do Quick Look
                int canto = 2; DwmSetWindowAttribute(h, 33, ref canto, 4);
            };
            w.PreviewKeyDown += (s, e) =>
            {
                if (e.OriginalSource is TextBox && e.Key != Key.Escape && e.Key != Key.Space) return;   // setas rolam o texto
                if (e.Key == Key.Space || e.Key == Key.Escape) Fechar();
                else if (e.Key == Key.Enter) Abrir();
                else if ((e.Key == Key.Left || e.Key == Key.Up) && Mover != null) Mover(-1);
                else if ((e.Key == Key.Right || e.Key == Key.Down) && Mover != null) Mover(1);
                else return;
                e.Handled = true;
            };
            w.Closing += (s, e) => { e.Cancel = true; Fechar(); };   // Alt+F4 só esconde; a janela é reaproveitada
            dono.Closed += delegate { Limpar(); };
        }

        static Button Botao(string glifo, string dica, Brush cor)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glifo, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = cor },
                ToolTip = dica, Width = 30, Height = 26, VerticalAlignment = VerticalAlignment.Center, Focusable = false, Cursor = Cursors.Arrow
            };
            var f = new FrameworkElementFactory(typeof(Border));
            f.Name = "f";
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            f.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var c = new FrameworkElementFactory(typeof(ContentPresenter));
            c.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            c.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            f.AppendChild(c);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = f };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Comum.Cor("#26808080"), "f"));
            t.Triggers.Add(hover);
            b.Template = t;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        void Abrir()
        {
            if (atual == null) return;
            var c = atual.Caminho;
            Fechar();
            try { System.Diagnostics.Process.Start(c); } catch { }
        }

        // tamanho do conteúdo (em DIPs, sem a barra de cima), limitado à tela do Finder e centrado nela
        // proporcional = false: largura e altura limitadas cada uma por si (páginas de documento)
        void Tamanho(double lw, double la, bool proporcional = true)
        {
            var ponte = PresentationSource.FromVisual(dono);
            double dpi = ponte == null ? 1 : ponte.CompositionTarget.TransformToDevice.M11;
            var t = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(dono).Handle).WorkingArea;
            double tl = t.Width / dpi, ta = t.Height / dpi, maxL = tl * 0.85, maxA = ta * 0.85 - 44;
            double f = Math.Min(1, Math.Min(maxL / lw, maxA / la));
            double fl = proporcional ? f : Math.Min(1, maxL / lw), fa = proporcional ? f : Math.Min(1, maxA / la);
            w.Width = Math.Max(w.MinWidth, lw * fl);
            w.Height = Math.Max(w.MinHeight, la * fa + 44);
            w.Left = t.Left / dpi + (tl - w.Width) / 2;
            w.Top = t.Top / dpi + (ta - w.Height) / 2;
        }

        // ---------- imagem ----------
        void Imagem(Entrada e)
        {
            int minha = vez;
            Tamanho(w.Width, w.Height - 44);
            Task.Run(() => CarregarImagem(e.Caminho)).ContinueWith(t =>
            {
                if (minha != vez) return;
                if (t.Exception != null || t.Result == null) { TextoOuFicha(e); return; }
                var b = t.Result;
                area.Child = new Image { Source = b, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 0) };
                RenderOptions.SetBitmapScalingMode(area.Child, BitmapScalingMode.HighQuality);
                // o dobro do tamanho em pixels, limitado à tela
                Tamanho(Math.Max(b.PixelWidth * 2, 320), Math.Max(b.PixelHeight * 2, 200));
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static BitmapSource CarregarImagem(string caminho)
        {
            var d = BitmapDecoder.Create(new Uri(caminho), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource b = d.Frames[0];
            // fotos de celular: o WPF ignora a orientação gravada no EXIF
            int giro = 0;
            try
            {
                var m = d.Frames[0].Metadata as BitmapMetadata;
                object o = m == null ? null : m.GetQuery("/app1/ifd/{ushort=274}");
                int ori = o == null ? 1 : Convert.ToInt32(o);
                giro = ori == 3 || ori == 4 ? 180 : ori == 6 || ori == 5 ? 90 : ori == 8 || ori == 7 ? 270 : 0;
            }
            catch { }
            if (giro != 0) b = new TransformedBitmap(b, new RotateTransform(giro));
            b.Freeze();
            return b;
        }

        // ---------- vídeo e áudio ----------
        void Midia(Entrada e, bool audio)
        {
            int minha = vez;
            midia = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, Source = new Uri(e.Caminho) };
            var m = midia;
            m.MediaOpened += delegate { if (minha == vez && !audio && m.NaturalVideoWidth > 0) Tamanho(m.NaturalVideoWidth, m.NaturalVideoHeight); };
            m.MediaEnded += delegate { m.Position = TimeSpan.Zero; m.Play(); };   // repete, como no Mac
            m.MediaFailed += delegate { if (minha == vez) { Limpar(); Ficha(e); } };
            bool tocando = true;
            if (audio)
            {
                // áudio: a ficha do arquivo, tocando por trás
                Ficha(e);
                var g = new Grid(); g.Children.Add(area.Child); area.Child = null; g.Children.Add(m); area.Child = g;
                g.MouseLeftButtonUp += delegate { if (tocando) m.Pause(); else m.Play(); tocando = !tocando; };
            }
            else
            {
                area.Background = Brushes.Black;
                area.Child = m;
                m.MouseLeftButtonUp += delegate { if (tocando) m.Pause(); else m.Play(); tocando = !tocando; };
                Tamanho(960, 540);
            }
            m.Play();
        }

        // ---------- texto ----------
        void Texto(Entrada e)
        {
            int minha = vez;
            Tamanho(820, 900);
            Task.Run(() => LerTexto(e.Caminho)).ContinueWith(t =>
            {
                if (minha != vez) return;
                if (t.Exception != null || t.Result == null) { Ficha(e); return; }
                MostrarTexto(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void MostrarTexto(string s)
        {
            area.Background = Comum.Cor(claro ? "#FFFFFFFF" : "#FF1E1E1E");
            area.Child = new TextBox
            {
                Text = s, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Comum.Cor(claro ? "#FF1D1D1F" : "#FFECECEC"), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5,
                Padding = new Thickness(16, 12, 16, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, IsUndoEnabled = false
            };
        }

        // até 512 KB; null se parecer binário
        static string LerTexto(string caminho)
        {
            using (var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(f, new UTF8Encoding(false), true))
            {
                var buf = new char[512 * 1024];
                int n = r.ReadBlock(buf, 0, buf.Length);
                for (int i = 0; i < Math.Min(n, 4096); i++) if (buf[i] == '\0') return null;
                string s = new string(buf, 0, n);
                if (!r.EndOfStream) s += "\n\n…";
                return s;
            }
        }

        // tipo desconhecido e sem preview handler: se for texto pequeno, mostra o texto; senão, a ficha
        void TextoOuFicha(Entrada e)
        {
            if (e.EhPasta || e.Tamanho > 2 * 1024 * 1024) { Ficha(e); return; }
            int minha = vez;
            Ficha(e);
            Task.Run(() => LerTexto(e.Caminho)).ContinueWith(t =>
            {
                if (minha != vez || t.Exception != null || t.Result == null || t.Result.Trim().Length == 0) return;
                MostrarTexto(t.Result);
                Tamanho(820, 900);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------- ficha: ícone grande, nome, tipo, tamanho e data ----------
        void Ficha(Entrada e)
        {
            int minha = vez;
            var texto = Comum.Cor(claro ? "#FF1D1D1F" : "#FFECECEC");
            var sec = Comum.Cor(claro ? "#FF6E6E73" : "#FF9A9A9F");
            var img = new Image { Width = 180, Height = 180, Source = e.Grande ?? e.Icone, Margin = new Thickness(0, 0, 28, 0) };
            Shell.PedirImagem(e.Caminho, 256, i => { if (minha == vez) img.Source = i; });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = e.Nome, Foreground = texto, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 });
            info.Children.Add(new TextBlock { Text = e.Tipo ?? Shell.Tipo(e.Nome, e.EhPasta), Foreground = sec, Margin = new Thickness(0, 4, 0, 0) });
            var tam = new TextBlock { Text = e.EhPasta ? "" : e.TamanhoTexto, Foreground = sec, Margin = new Thickness(0, 10, 0, 0) };
            info.Children.Add(tam);
            info.Children.Add(new TextBlock { Text = "Modificado: " + e.DataTexto, Foreground = sec, Margin = new Thickness(0, 2, 0, 0) });
            if (e.EhPasta)
                Task.Run(() => Directory.EnumerateFileSystemEntries(e.Caminho).Count()).ContinueWith(t =>
                {
                    if (minha != vez || t.Exception != null) return;
                    tam.Text = t.Result == 1 ? "1 item" : t.Result.ToString("N0", Finder.PtBr) + " itens";
                }, TaskScheduler.FromCurrentSynchronizationContext());
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32) };
            sp.Children.Add(img); sp.Children.Add(info);
            area.Child = sp;
            Tamanho(640, 300);
        }

        // ---------- preview handlers do Windows (PDF, Word, Excel, PowerPoint...) ----------
        bool Handler(Entrada e)
        {
            Guid clsid;
            if (!AcharHandler(Path.GetExtension(e.Caminho), out clsid)) return false;
            int minha = vez;
            hosp = new Hospedeiro();
            area.Background = Comum.Cor("#FFFFFFFF");
            area.Child = hosp;
            Tamanho(1920, 1100, false);
            // o handler precisa da janela filha já criada e com tamanho
            w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (minha != vez || hosp == null) return;
                if (!IniciarHandler(clsid, e.Caminho)) { Limpar(); Ficha(e); }
            }));
            return true;
        }

        static bool AcharHandler(string ext, out Guid clsid)
        {
            clsid = Guid.Empty;
            if (string.IsNullOrEmpty(ext)) return false;
            uint n = 64; var sb = new StringBuilder((int)n);
            if (AssocQueryString(0, 16, ext, "{8895b1c6-b41f-4c1c-a562-0d564250836f}", sb, ref n) != 0) return false;   // ASSOCSTR_SHELLEXTENSION
            return Guid.TryParse(sb.ToString(), out clsid);
        }

        bool IniciarHandler(Guid clsid, string caminho)
        {
            try
            {
                Guid iu = new Guid("00000000-0000-0000-C000-000000000046");
                object o;
                if (CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iu, out o) != 0 || o == null) return false;   // CLSCTX_LOCAL_SERVER: no prevhost.exe
                handler = o;
                var f = o as IInitializeWithFile;
                if (f != null) f.Initialize(caminho, 0);
                else
                {
                    var s = o as IInitializeWithStream;
                    if (s == null) return false;
                    System.Runtime.InteropServices.ComTypes.IStream st;
                    if (SHCreateStreamOnFileEx(caminho, 0x40, 0, false, IntPtr.Zero, out st) != 0) return false;   // STGM_READ | SHARE_DENY_NONE
                    s.Initialize(st, 0);
                }
                var p = (IPreviewHandler)o;
                var r = hosp.Retangulo();
                p.SetWindow(hosp.Handle, ref r);
                p.DoPreview();
                var h = hosp;
                h.Mudou = () => { if (handler == o && h == hosp) { var rr = h.Retangulo(); try { p.SetRect(ref rr); } catch { } } };
                return true;
            }
            catch (Exception ex) { Comum.Log("finder.log", "prévia " + caminho + ": " + ex.Message); return false; }
        }

        // janela Win32 filha onde o preview handler desenha
        class Hospedeiro : HwndHost
        {
            public Action Mudou;
            protected override HandleRef BuildWindowCore(HandleRef pai)
            {
                var h = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000, 0, 0, 10, 10, pai.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);   // CHILD | VISIBLE | CLIPCHILDREN
                return new HandleRef(this, h);
            }
            protected override void DestroyWindowCore(HandleRef h) { DestroyWindow(h.Handle); }
            protected override void OnWindowPositionChanged(Rect r)
            {
                base.OnWindowPositionChanged(r);
                if (Mudou != null) Mudou();
            }
            public RECT Retangulo() { RECT r; GetClientRect(Handle, out r); return r; }
        }

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref RECT r);
            void SetRect(ref RECT r);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr hwnd);
            [PreserveSig] int TranslateAccelerator(IntPtr msg);
        }
        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string caminho, uint modo); }
        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithStream { void Initialize(System.Runtime.InteropServices.ComTypes.IStream s, uint modo); }

        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int AssocQueryString(uint flags, int str, string assoc, string extra, StringBuilder saida, ref uint n);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int SHCreateStreamOnFileEx(string arq, uint modo, uint attr, bool criar, IntPtr modelo, out System.Runtime.InteropServices.ComTypes.IStream s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(int ex, string classe, string nome, int estilo, int x, int y, int l, int a, IntPtr pai, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);
    }
}
