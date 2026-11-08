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
        bool aberto, fechando;

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
                Win32.Camuflar(hVidro, true);   // aberta uma vez só, camuflada; aparece e some sem ser recriada
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
                hJanela = new WindowInteropHelper(janela).Handle;
                Win32.Estilo(hJanela, Win32.EX_TOOL | Win32.EX_NOACTIVATE | Win32.EX_TRANSPARENT);
                Win32.Camuflar(hJanela, true);
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
            trilhoG = trilho;
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

            // modo interativo (aberto pelo clique na barra): clicar ou arrastar na barra muda o volume
            fundo.MouseLeftButtonDown += (s, e) =>
            {
                if (!interativo) return;
                var p = e.GetPosition(trilhoG);
                if (p.Y < -16 || p.Y > 24 || p.X < -16 || p.X > Trilho + 16) return;
                arrastando = true; fundo.CaptureMouse(); AjustarPor(e); e.Handled = true;
            };
            fundo.MouseMove += (s, e) => { if (arrastando) AjustarPor(e); };
            fundo.MouseLeftButtonUp += (s, e) => { if (arrastando) { arrastando = false; fundo.ReleaseMouseCapture(); } };
            fundo.LostMouseCapture += delegate { arrastando = false; };
            cliqueForaT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            cliqueForaT.Tick += delegate { VigiarCliqueFora(); };
            atual = this;

            escondeT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            escondeT.Tick += delegate { escondeT.Stop(); Esconder(); };
            nativoT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            nativoT.Tick += delegate { OcultarNativo(); };
            dispositivoT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            dispositivoT.Tick += delegate { ConferirDispositivo(); };
        }
        Border pista; Canvas marcas;
        Grid trilhoG; IntPtr hJanela;
        bool interativo, arrastando;
        DispatcherTimer cliqueForaT;
        int fechouEm = Environment.TickCount - 100000;
        static Volume atual;

        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        // clique no ícone de volume da barra: abre o cartão para mexer com o mouse; de novo (ou clicando fora) fecha
        public static void Alternar() { if (atual != null) atual.AlternarInterno(); }

        void AlternarInterno()
        {
            if (interativo) { Fechar(); return; }
            // clicar no ícone com o cartão aberto: o clique fora já fechou (no botão para baixo) e logo chega aqui; não reabre
            if (Environment.TickCount - fechouEm < 400) return;
            try
            {
                interativo = true;
                Exibir(ultimo < 0 ? Nivel : ultimo, mudo ?? Silenciado);
                Win32.SemEstilo(hJanela, Win32.EX_TRANSPARENT);
                escondeT.Stop();
                GetAsyncKeyState(1); GetAsyncKeyState(2);   // zera o "apertado desde a última vez" do clique que abriu
                cliqueForaT.Start();
            }
            catch (Exception e) { Log("abrir: " + e.Message); SairInterativo(); }
        }

        void SairInterativo()
        {
            interativo = false; arrastando = false;
            cliqueForaT.Stop();
            try { fundo.ReleaseMouseCapture(); } catch { }
            if (hJanela != IntPtr.Zero) Win32.Estilo(hJanela, Win32.EX_TRANSPARENT);
        }

        void Fechar()
        {
            fechouEm = Environment.TickCount;
            SairInterativo();
            Esconder();
        }

        void VigiarCliqueFora()
        {
            // 0x8000 = apertado agora; 0x0001 = apertado desde a última checagem (um toque rápido no touchpad cabe
            // inteiro entre duas checagens de 50 ms e passava despercebido)
            int esq = GetAsyncKeyState(1), dir = GetAsyncKeyState(2);
            if (((esq | dir) & 0x8001) == 0) return;
            if (arrastando) return;
            Win32.POINT p; Win32.GetCursorPos(out p);
            var a = fundo.PointToScreen(new Point(0, 0));
            if (p.X >= a.X && p.X <= a.X + L * f && p.Y >= a.Y && p.Y <= a.Y + A * f) return;   // clicou dentro do cartão
            Fechar();
        }

        // posição do mouse na barra (0 a 1) vira volume
        void AjustarPor(System.Windows.Input.MouseEventArgs e)
        {
            if (ep == null) return;
            float v = (float)Math.Max(0, Math.Min(1, e.GetPosition(trilhoG).X / Trilho));
            try
            {
                var ctx = Guid.Empty;
                mudouEm = Environment.TickCount;
                ep.SetMasterVolumeLevelScalar(v, ref ctx);
                if (mudo == true) ep.SetMute(false, ref ctx);
                ultimo = v; mudo = false;
                Publicar(v, false);
                Exibir(v, false);
            }
            catch (Exception ex) { Log("ajustar: " + ex.Message); }
        }

        public void Iniciar()
        {
            // as duas janelas ficam abertas (camufladas) desde o início: abrir o cartão não paga o Show de janela nova
            try
            {
                vidro.Show();
                janela.Owner = vidro;   // o cartão fica sempre na frente do vidro
                janela.Show();
                f = PresentationSource.FromVisual(janela).CompositionTarget.TransformToDevice.M22;
            }
            catch (Exception e) { Log("janelas: " + e.Message); }
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
            if (c != claro) { claro = c; Tema(c); if (hVidro != IntPtr.Zero) Win32.Vidro(hVidro, c ? 0x1EF8F6F6 : 0x24383030, true); }

            titulo.Text = brilho ? "Brilho da Tela" : epNome;
            double pct = Math.Max(0, Math.Min(1, v));
            double alvo = m ? 0 : pct * Trilho;
            // brilho: sol pequeno à esquerda e sol cheio à direita; volume: alto-falante com as ondas conforme o nível
            iconeEsq.Text = ((char)(brilho ? 0xEC8A : 0xE992)).ToString();
            iconeDir.Text = ((char)(brilho ? 0xE706 : m ? 0xE74F : pct <= 0 ? 0xE992 : pct < 0.34 ? 0xE993 : pct < 0.67 ? 0xE994 : 0xE995)).ToString();
            if (aberto) nivel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(70)));
            else { nivel.BeginAnimation(FrameworkElement.WidthProperty, null); nivel.Width = alvo; }

            // o vidro (acrylic) é outra janela e não esmaece: antes ele aparecia sozinho, como uma barra escura, enquanto o
            // cartão ainda vinha no fade. Agora o cartão é desenhado inteiro ainda camuflado e os dois aparecem juntos.
            if (!aberto)
            {
                aberto = true; fechando = false;
                janela.BeginAnimation(UIElement.OpacityProperty, null);
                janela.Opacity = 1;
                PosicionarVidro();
                Revelar();
            }
            else if (fechando)   // mudou de novo durante o fade de saída: o cartão já está na tela, volta o vidro
            {
                fechando = false; ++revelacao;
                janela.BeginAnimation(UIElement.OpacityProperty, null);
                janela.Opacity = 1;
                Win32.Camuflar(hVidro, false); Win32.Camuflar(hJanela, false);   // o cartão também, se fechou antes de aparecer
            }

            escondeT.Stop();
            if (brilho && interativo) SairInterativo();   // brilho apareceu: o cartão volta a ser só um aviso
            if (!interativo) escondeT.Start();
            // o indicador nativo aparece uns instantes depois da mudança: vigia por um tempo e esconde
            nativoRestante = 75; OcultarNativo(); nativoT.Start();
        }

        void PosicionarVidro()
        {
            var p = fundo.PointToScreen(new Point(Recuo, Recuo));
            Win32.Mover(hVidro, (int)p.X, (int)p.Y, (int)((L - 2 * Recuo) * f), (int)((A - 2 * Recuo) * f));
        }

        // a janela em camadas mostra o quadro novo 1 a 2 quadros depois: espera o cartão estar desenhado e tira a
        // camuflagem das duas no mesmo instante (vidro e cartão chegam juntos)
        int revelacao;
        void Revelar()
        {
            int minha = ++revelacao, quadros = 0;
            EventHandler aoDesenhar = null;
            aoDesenhar = delegate
            {
                if (++quadros < 3 && minha == revelacao) return;
                CompositionTarget.Rendering -= aoDesenhar;
                if (minha != revelacao || !aberto || fechando) return;
                Win32.NoTopo(hVidro); Win32.NoTopo(hJanela);   // na frente de outras janelas "sempre no topo"
                Win32.Camuflar(hVidro, false); Win32.Camuflar(hJanela, false);
            };
            CompositionTarget.Rendering += aoDesenhar;
        }

        void Esconder()
        {
            if (!aberto || fechando) return;
            fechando = true;
            int minha = ++revelacao;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            anim.Completed += delegate
            {
                if (!fechando || minha != revelacao) return;   // mudou de novo durante o fade
                fechando = false; aberto = false;
                Win32.Camuflar(hJanela, true);
            };
            Win32.Camuflar(hVidro, true);   // o acrylic não esmaece: some junto com o começo do fade
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
