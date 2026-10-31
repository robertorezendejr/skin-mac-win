// Mac Skin Volume: indicador de volume e de brilho no canto de cima à direita, como no macOS (Liquid Glass).
// Escuta o volume do Windows pela API de áudio e o brilho da tela pelo evento do WMI (sem consultar em intervalo),
// mostra um cartão de vidro acrylic com o nome e a barra, e esconde o indicador nativo do Windows enquanto o nosso aparece.
using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MacSkin
{
    class Volume
    {
        // medidas em DIPs
        const double L = 340, A = 74, Raio = 24, Recuo = 5, Folga = 20, TopoBarra = 28, Distancia = 8, BordaDir = 12, Trilho = 256;
        static readonly FontFamily Icones = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily Fonte = new FontFamily("Inter, Segoe UI");

        Window vidro, janela;
        IntPtr hVidro;
        Border fundo, aro, nivel;
        TextBlock titulo, iconeEsq, iconeDir;
        double f = 1;
        bool? claro;
        bool aberto;

        IAudioEndpointVolume ep; string epId, epNome = "Alto-falantes";
        VolCb retorno;
        float ultimo = -1; bool? mudo;
        DispatcherTimer escondeT, nativoT, dispositivoT;
        int nativoRestante;
        volatile int mudouEm = Environment.TickCount - 100000;   // quando o Windows avisou a última mudança (vem de outra thread)
        ManagementEventWatcher vigiaBrilho;

        static void Log(string m) { Comum.Log("volume.log", m); }

        public Volume()
        {
            vidro = new Window
            {
                Title = "Mac Skin Volume vidro", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                Topmost = true, Background = Brushes.Transparent, Left = -10000, Top = -10000, Width = 10, Height = 10, ShowActivated = false
            };
            vidro.SourceInitialized += delegate
            {
                hVidro = new WindowInteropHelper(vidro).Handle;
                HwndSource.FromHwnd(hVidro).CompositionTarget.BackgroundColor = Colors.Transparent;
                Win32.Estilo(hVidro, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
            };

            double w = L + 2 * Folga, h = A + 2 * Folga;
            janela = new Window
            {
                Title = "Mac Skin Volume", WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Opacity = 0,
                Width = w, Height = h,
                Left = SystemParameters.PrimaryScreenWidth - L - BordaDir - Folga, Top = TopoBarra + Distancia - Folga
            };
            janela.SourceInitialized += delegate
            {
                var hj = new WindowInteropHelper(janela).Handle;
                Win32.Estilo(hj, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
            };

            var raiz = new Grid();
            fundo = new Border
            {
                Width = L, Height = A, CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.28 }
            };
            aro = new Border { Width = L, Height = A, CornerRadius = new CornerRadius(Raio), BorderThickness = new Thickness(Recuo), IsHitTestVisible = false };

            titulo = new TextBlock { FontFamily = Fonte, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 11, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = Trilho + 44 };
            iconeEsq = new TextBlock { Text = ((char)0xE992).ToString(), FontFamily = Icones, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            iconeDir = new TextBlock { FontFamily = Icones, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
            pista = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
            nivel = new Border { Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var trilho = new Grid { Width = Trilho, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            trilho.Children.Add(pista); trilho.Children.Add(nivel);
            // marcas pontilhadas sob a barra, como no Mac
            marcas = new Canvas { Width = Trilho, Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -12) };
            for (int i = 0; i <= 16; i++)
            {
                var d = new System.Windows.Shapes.Ellipse { Width = 2, Height = 2 };
                Canvas.SetLeft(d, i * (Trilho - 2) / 16.0); marcas.Children.Add(d);
            }
            trilho.Children.Add(marcas);
            var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20) };
            linha.Children.Add(iconeEsq); linha.Children.Add(trilho); linha.Children.Add(iconeDir);
            var conteudo = new Grid { Width = L, Height = A, Margin = new Thickness(0), IsHitTestVisible = false };
            var interno = new Grid { Margin = new Thickness(16, 0, 16, 0) };
            interno.Children.Add(titulo); interno.Children.Add(linha);
            conteudo.Children.Add(interno);

            var cartao = new Grid { Width = L, Height = A, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cartao.Children.Add(aro); cartao.Children.Add(fundo); cartao.Children.Add(conteudo);
            raiz.Children.Add(cartao);
            janela.Content = raiz;

            escondeT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            escondeT.Tick += delegate { escondeT.Stop(); Esconder(); };
            nativoT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            nativoT.Tick += delegate { OcultarNativo(); };
            dispositivoT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            dispositivoT.Tick += delegate { ConferirDispositivo(); };
        }
        Border pista; Canvas marcas;

        public void Iniciar()
        {
            try { Win32.VigiarOsd(AoSurgirNativo); Ligar(); dispositivoT.Start(); Log("volume iniciado em " + epNome); }
            catch (Exception e) { Log("não iniciou: " + e.Message); }
            LigarBrilho();
        }

        public void Parar()
        {
            try { if (vigiaBrilho != null) { vigiaBrilho.Stop(); vigiaBrilho.Dispose(); } } catch { }
            try { Win32.PararOsd(); dispositivoT.Stop(); Desligar(); } catch { }
            try { janela.Close(); vidro.Close(); } catch { }
        }

        // ---------- áudio ----------
        void Ligar()
        {
            var enumerador = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev; enumerador.GetDefaultAudioEndpoint(0, 1, out dev);   // saída, console
            string id; dev.GetId(out id);
            Guid iid = typeof(IAudioEndpointVolume).GUID; object o;
            dev.Activate(ref iid, 23, IntPtr.Zero, out o);
            var novo = (IAudioEndpointVolume)o;
            retorno = new VolCb(delegate (float v, bool m) { mudouEm = Environment.TickCount; janela.Dispatcher.BeginInvoke(new Action(() => AoMudar(v, m))); });
            novo.RegisterControlChangeNotify(retorno);
            ep = novo; epId = id; epNome = NomeDe(dev);
            float vol; bool mu; ep.GetMasterVolumeLevelScalar(out vol); ep.GetMute(out mu);
            ultimo = vol; mudo = mu;
            Publicar(vol, mu);
        }

        // estado atual para o ícone da barra de menus (sempre na thread da interface)
        public static float Nivel = 1; public static bool Silenciado;
        public static event Action Alterou;
        static void Publicar(float v, bool m)
        {
            Nivel = v; Silenciado = m;
            var a = Alterou; if (a != null) a();
        }

        void Desligar()
        {
            if (ep != null && retorno != null) { try { ep.UnregisterControlChangeNotify(retorno); } catch { } }
            ep = null;
        }

        // trocou a saída padrão (fone, Bluetooth): passa a escutar o dispositivo novo, sem mostrar nada
        void ConferirDispositivo()
        {
            try
            {
                var enumerador = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev; enumerador.GetDefaultAudioEndpoint(0, 1, out dev);
                string id; dev.GetId(out id);
                if (id == epId) return;
                Desligar(); Ligar(); Log("dispositivo: " + epNome);
            }
            catch (Exception e) { Log("dispositivo: " + e.Message); }
        }

        static string NomeDe(IMMDevice dev)
        {
            try
            {
                IPropertyStore ps; dev.OpenPropertyStore(0, out ps);
                var chave = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };   // PKEY_Device_FriendlyName
                PROPVARIANT v; ps.GetValue(ref chave, out v);
                string s = v.p == IntPtr.Zero ? null : Marshal.PtrToStringUni(v.p);
                PropVariantClear(ref v);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch (Exception e) { Log("nome: " + e.Message); }
            return "Alto-falantes";
        }

        // ---------- brilho da tela ----------
        // as teclas de brilho (F6/F7 no Dell) mudam o brilho pelo firmware; o Windows avisa cada mudança neste evento.
        // Só existe em tela de notebook; em monitor externo o evento não vem e o cartão de brilho não aparece.
        void LigarBrilho()
        {
            try
            {
                vigiaBrilho = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                vigiaBrilho.EventArrived += (s, e) =>
                {
                    try
                    {
                        int b = Convert.ToInt32(e.NewEvent["Brightness"]);
                        mudouEm = Environment.TickCount;
                        janela.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { Exibir(b / 100f, false, true); }
                            catch (Exception ex) { Log("exibir brilho: " + ex.Message); }
                        }));
                    }
                    catch (Exception ex) { Log("evento brilho: " + ex.Message); }
                };
                vigiaBrilho.Start();
                Log("brilho vigiado");
            }
            catch (Exception e) { Log("brilho não iniciou: " + e.Message); }
        }

        // ---------- cartão ----------
        void AoMudar(float v, bool m)
        {
            bool igual = Math.Abs(v - ultimo) < 0.0005f && mudo == m;
            ultimo = v; mudo = m;
            Publicar(v, m);
            if (igual) return;
            try { Exibir(v, m); }
            catch (Exception e) { Log("exibir: " + e.Message); }
        }

        void Exibir(float v, bool m, bool brilho = false)
        {
            var c = Comum.TemaClaro();
            if (c != claro) { claro = c; Tema(c); }

            titulo.Text = brilho ? "Brilho da Tela" : epNome;
            double pct = Math.Max(0, Math.Min(1, v));
            double alvo = m ? 0 : pct * Trilho;
            // brilho: sol pequeno à esquerda e sol cheio à direita; volume: alto-falante com as ondas conforme o nível
            iconeEsq.Text = ((char)(brilho ? 0xEC8A : 0xE992)).ToString();
            iconeDir.Text = ((char)(brilho ? 0xE706 : m ? 0xE74F : pct <= 0 ? 0xE992 : pct < 0.34 ? 0xE993 : pct < 0.67 ? 0xE994 : 0xE995)).ToString();
            if (aberto) nivel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(70)));
            else { nivel.BeginAnimation(FrameworkElement.WidthProperty, null); nivel.Width = alvo; }

            if (!aberto)
            {
                aberto = true;
                vidro.Visibility = Visibility.Visible;
                vidro.Show();
                if (janela.Owner == null) janela.Owner = vidro;   // o cartão fica sempre na frente do vidro
                janela.Show();
                janela.UpdateLayout();
                f = PresentationSource.FromVisual(janela).CompositionTarget.TransformToDevice.M22;
                Win32.Vidro(hVidro, c ? 0x1EF8F6F6 : 0x24383030, true);
                PosicionarVidro();
                janela.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            }
            else janela.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(60)));

            escondeT.Stop(); escondeT.Start();
            // o indicador nativo aparece uns instantes depois da mudança: vigia por um tempo e esconde
            nativoRestante = 75; OcultarNativo(); nativoT.Start();
        }

        void PosicionarVidro()
        {
            var p = fundo.PointToScreen(new Point(Recuo, Recuo));
            Win32.Mover(hVidro, (int)p.X, (int)p.Y, (int)((L - 2 * Recuo) * f), (int)((A - 2 * Recuo) * f));
        }

        void Esconder()
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            anim.Completed += delegate
            {
                if (janela.Opacity > 0.01) return;   // mudou de novo durante o fade
                aberto = false; janela.Hide(); vidro.Hide();
            };
            vidro.Visibility = Visibility.Hidden;   // o acrylic não esmaece: some junto com o começo do fade
            janela.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        void Tema(bool c)
        {
            // vidro mais transparente que o da dock: o fundo aparece borrado por trás, com brilho no alto e borda de luz
            aro.BorderBrush = Comum.Cor(c ? "#8CEDEDEF" : "#8C3A3A3E");
            fundo.Background = c ? Comum.Degrade("#55FFFFFF", "#14FFFFFF") : Comum.Degrade("#40FFFFFF", "#0EFFFFFF");
            fundo.BorderBrush = c ? Comum.Degrade("#F2FFFFFF", "#66FFFFFF") : Comum.Degrade("#B3FFFFFF", "#33FFFFFF");
            var ponto = Comum.Cor(c ? "#59000000" : "#66FFFFFF");
            foreach (System.Windows.Shapes.Ellipse e in marcas.Children) e.Fill = ponto;
            var texto = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF");
            titulo.Foreground = iconeEsq.Foreground = iconeDir.Foreground = texto;
            pista.Background = Comum.Cor(c ? "#33000000" : "#40FFFFFF");
            nivel.Background = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF");
        }

        // ---------- indicador nativo do Windows ----------
        // o indicador surge junto com a mudança de volume, às vezes um instante antes do aviso da API: esconde na hora e,
        // se nenhuma mudança de volume ou de brilho veio logo depois (era o de modo avião, por exemplo), mostra de volta
        void AoSurgirNativo(IntPtr h)
        {
            Win32.Esconder(h);
            if (Environment.TickCount - mudouEm < 500) return;
            int quando = Environment.TickCount;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += delegate
            {
                t.Stop();
                if (mudouEm - quando < 0 && Environment.TickCount - mudouEm >= 500) Win32.MostrarSemAtivar(h);
            };
            t.Start();
        }

        int logsOsd;
        void OcultarNativo()
        {
            if (--nativoRestante <= 0) { nativoT.Stop(); return; }
            try
            {
                var diag = logsOsd < 3 ? new StringBuilder() : null;
                int n = Win32.OcultarOsdNativo(diag);
                if (diag != null && diag.Length > 0) { Log("indicador nativo escondido: " + diag); logsOsd++; }
            }
            catch (Exception e) { Log("nativo: " + e.Message); nativoT.Stop(); }
        }

        // ---------- COM da API de áudio do Windows (Core Audio) ----------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int fluxo, int estado, out IntPtr lista);
            void GetDefaultAudioEndpoint(int fluxo, int papel, out IMMDevice dev);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
            void RegisterEndpointNotificationCallback(IntPtr c);
            void UnregisterEndpointNotificationCallback(IntPtr c);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            void Activate(ref Guid iid, int contexto, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object interf);
            void OpenPropertyStore(int acesso, out IPropertyStore ps);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int estado);
        }

        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p, p2; }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount(out int n);
            void GetAt(int i, out PROPERTYKEY k);
            void GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
            void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
            void Commit();
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            void RegisterControlChangeNotify(IAudioEndpointVolumeCallback c);
            void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback c);
            void GetChannelCount(out int n);
            void SetMasterVolumeLevel(float db, ref Guid ctx);
            void SetMasterVolumeLevelScalar(float v, ref Guid ctx);
            void GetMasterVolumeLevel(out float db);
            void GetMasterVolumeLevelScalar(out float v);
            void SetChannelVolumeLevel(int canal, float db, ref Guid ctx);
            void SetChannelVolumeLevelScalar(int canal, float v, ref Guid ctx);
            void GetChannelVolumeLevel(int canal, out float db);
            void GetChannelVolumeLevelScalar(int canal, out float v);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mudo, ref Guid ctx);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mudo);
        }

        [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolumeCallback { void OnNotify(IntPtr dados); }

        // AUDIO_VOLUME_NOTIFICATION_DATA: Guid (16), BOOL mudo (4), float volume (4), ...
        [ClassInterface(ClassInterfaceType.None)]
        class VolCb : IAudioEndpointVolumeCallback
        {
            readonly Action<float, bool> aoMudar;
            public VolCb(Action<float, bool> a) { aoMudar = a; }
            public void OnNotify(IntPtr d)
            {
                try
                {
                    bool m = Marshal.ReadInt32(d, 16) != 0;
                    float v = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(d, 20)), 0);
                    aoMudar(v, m);
                }
                catch { }
            }
        }
    }
}
