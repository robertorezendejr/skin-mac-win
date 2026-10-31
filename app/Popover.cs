// Mac Skin Popover: caixas de vidro que abrem embaixo dos ícones da barra de menus, como no macOS:
// calendário (relógio), Wi-Fi e Bluetooth. Usam o mesmo vidro do menu da dock (MenuVidro).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MacSkin
{
    static class Popover
    {
        static readonly FontFamily Icones = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly FontFamily Texto = new FontFamily("Inter, Segoe UI");
        static readonly CultureInfo PtBr = new CultureInfo("pt-BR");
        static readonly Brush Azul = Comum.Cor("#0A84FF");
        static readonly Brush Vermelho = Comum.Cor("#FF3B30");

        // ---------- cores do tema ----------
        class Cores
        {
            public Brush Tx, Sub, Hover, Sep, Disco, DiscoTx;
            public Cores(bool c)
            {
                Tx = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF");
                Sub = Comum.Cor(c ? "#8C000000" : "#99FFFFFF");
                Hover = Comum.Cor(c ? "#1F000000" : "#2EFFFFFF");
                Sep = Comum.Cor(c ? "#26000000" : "#33FFFFFF");
                Disco = Comum.Cor(c ? "#33000000" : "#40FFFFFF");
                DiscoTx = Comum.Cor(c ? "#E6000000" : "#FFFFFFFF");
            }
        }

        // ---------- criação ----------
        public static ContextMenu Criar(Window janela, Func<IntPtr> hwnd, Action<ContextMenu> montar)
        {
            var m = new ContextMenu();
            MenuVidro.Aplicar(m, false, true, true);
            // fecha ao clicar fora (outro app/janela passa para a frente). Não dá para fechar quando a barra perde o foco:
            // clicar dentro da caixa (setas do calendário, interruptores) também tira o foco da barra
            var vigia = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            int aberturaEm = 0; bool apertado = false;
            vigia.Tick += delegate
            {
                if (!m.IsOpen) { vigia.Stop(); return; }
                if (Environment.TickCount - aberturaEm < 400) return;
                var src = PresentationSource.FromVisual(m) as System.Windows.Interop.HwndSource;
                bool botao = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
                if (botao && !apertado)   // clique novo: se não foi na caixa nem na barra, fecha
                {
                    Win32.POINT pt; Win32.GetCursorPos(out pt);
                    IntPtr sob = Win32.GetAncestor(Win32.WindowFromPoint(pt), 2);   // GA_ROOT
                    if (sob != hwnd() && (src == null || sob != src.Handle) && !Win32.Titulo(sob).StartsWith("Mac Skin Menu vidro")) Fechar(m);   // o vidro por trás da caixa também conta como dentro
                }
                apertado = botao;
            };
            m.Opened += delegate
            {
                aberturaEm = Environment.TickCount; apertado = true; Win32.SetForegroundWindow(hwnd()); vigia.Start();
                // a caixa não ativa ao clicar: ativar mudaria o vidro (o acrílico vira cor lisa em janela inativa)
                var src = PresentationSource.FromVisual(m) as System.Windows.Interop.HwndSource;
                if (src != null) Win32.Estilo(src.Handle, Win32.EX_NOACTIVATE);
            };
            int ultimoClique = -100000;
            m.PreviewMouseDown += delegate { ultimoClique = Environment.TickCount; };
            m.PreviewMouseUp += delegate { m.Dispatcher.BeginInvoke(new Action(() => Win32.SetForegroundWindow(hwnd())), System.Windows.Threading.DispatcherPriority.Background); };   // a barra continua com o foco: o vidro não muda
            m.Closed += delegate
            {
                if (proposital.Remove(m)) return;
                if (Environment.TickCount - ultimoClique < 600) m.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (m.IsOpen) return;
                    m.IsOpen = true;
                    m.Dispatcher.BeginInvoke(new Action(() => { if (m.IsOpen) MenuVidro.Reposicionar(m); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }));
            };
            m.Tag = montar;
            return m;
        }

        // o WPF fecha o menu ao clicar em algo que não é item de menu (setas, interruptores): se o fechamento veio de um clique
        // dentro da caixa e não foi de propósito, reabre na hora (o conteúdo continua montado)
        static readonly HashSet<ContextMenu> proposital = new HashSet<ContextMenu>();
        static void Fechar(ContextMenu m) { if (!m.IsOpen) return; proposital.Add(m); m.IsOpen = false; }

        // abre (ou fecha, se já estiver aberto); o conteúdo é montado de novo a cada abertura
        public static void Abrir(ContextMenu m, UIElement alvo, params ContextMenu[] outros)
        {
            bool estava = m.IsOpen;
            foreach (var o in outros) if (o != m) Fechar(o);
            if (estava) { Fechar(m); return; }
            try
            {
                m.Items.Clear();
                ((Action<ContextMenu>)m.Tag)(m);
                m.PlacementTarget = alvo; m.IsOpen = true;
            }
            catch (Exception e) { Comum.Log("barra.log", "popover: " + e); }
        }

        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        // ---------- peças ----------
        static TextBlock T(string txt, double tam, FontWeight peso, Brush cor)
        {
            return new TextBlock { Text = txt, FontFamily = Texto, FontSize = tam, FontWeight = peso, Foreground = cor, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        // clique tratado já na descida (preview) e marcado como tratado: o menu do WPF fecharia ao ver o clique em algo que não é item
        static void Clicavel(UIElement e, Action acao)
        {
            e.PreviewMouseLeftButtonDown += (s, a) => a.Handled = true;
            e.PreviewMouseLeftButtonUp += (s, a) => { a.Handled = true; acao(); };
        }

        static Border Linha(Cores c, UIElement conteudo, Action acao)
        {
            var b = new Border { Child = conteudo, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Background = Brushes.Transparent };
            if (acao != null)
            {
                b.MouseEnter += delegate { b.Background = c.Hover; };
                b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
                Clicavel(b, acao);
            }
            return b;
        }

        static UIElement Disco(Cores c, int glifo, bool azul)
        {
            var g = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0) };
            g.Children.Add(new Ellipse { Fill = azul ? Azul : c.Disco });
            g.Children.Add(new TextBlock { Text = ((char)glifo).ToString(), FontFamily = Icones, FontSize = 13, Foreground = azul ? Brushes.White : c.DiscoTx, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return g;
        }

        static Border Divisor(Cores c) { return new Border { Height = 1, Margin = new Thickness(8, 5, 8, 5), Background = c.Sep }; }

        static UIElement Rotulo(Cores c, string txt)
        {
            var t = T(txt, 12, FontWeights.SemiBold, c.Sub); t.Margin = new Thickness(10, 6, 10, 3); return t;
        }

        // interruptor do Mac: pílula azul quando ligado, cinza quando desligado, bolinha branca
        class Chave : Border
        {
            readonly Border bola; bool lig; readonly Cores c;
            public Action<bool> AoMudar;
            public Chave(Cores cores, bool ligado)
            {
                c = cores; Width = 40; Height = 24; CornerRadius = new CornerRadius(12); Cursor = Cursors.Hand;
                bola = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
                Child = bola; Definir(ligado);
                Clicavel(this, () => { var novo = !lig; Definir(novo); if (AoMudar != null) AoMudar(novo); });
            }
            public void Definir(bool ligado)
            {
                lig = ligado;
                Background = ligado ? Azul : c.Disco;
                bola.HorizontalAlignment = ligado ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
        }

        static UIElement Cabecalho(Cores c, string titulo, Chave chave)
        {
            var g = new Grid { Margin = new Thickness(10, 6, 10, 4) };
            g.Children.Add(T(titulo, 14, FontWeights.SemiBold, c.Tx));
            chave.HorizontalAlignment = HorizontalAlignment.Right; g.Children.Add(chave);
            return g;
        }

        // ---------- calendário ----------
        static int mesDeslocado;   // meses a partir de hoje
        static ContextMenu menuCal;

        public static void Calendario(ContextMenu m)
        {
            mesDeslocado = 0; menuCal = m;
            var dono = new Border { Width = 244, Margin = new Thickness(2) };
            m.Items.Add(dono);
            MontarMes(dono);
        }

        // o cabeçalho (com as setas) é criado uma vez só: trocar o elemento clicado fazia o menu perder o foco e fechar;
        // só o título e a grade de dias são refeitos
        static void MontarMes(Border dono)
        {
            var c = new Cores(Comum.TemaClaro());
            var raiz = new StackPanel { Margin = new Thickness(8, 4, 8, 6) };
            var titulo = T("", 12, FontWeights.Bold, Vermelho); titulo.HorizontalAlignment = HorizontalAlignment.Left;
            var corpo = new Border();
            Action atualizar = () => PreencherMes(titulo, corpo, c);
            var cab = new Grid { Margin = new Thickness(2, 2, 2, 6) };
            cab.Children.Add(titulo);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            nav.Children.Add(Seta(c, 0xE76B, () => { mesDeslocado--; atualizar(); }));
            nav.Children.Add(Seta(c, 0xEA3B, () => { mesDeslocado = 0; atualizar(); }));
            nav.Children.Add(Seta(c, 0xE76C, () => { mesDeslocado++; atualizar(); }));
            cab.Children.Add(nav);
            raiz.Children.Add(cab); raiz.Children.Add(corpo);
            dono.Child = raiz;
            atualizar();
        }

        static void PreencherMes(TextBlock titulo, Border corpo, Cores c)
        {
            var hoje = DateTime.Today;
            var base1 = new DateTime(hoje.Year, hoje.Month, 1).AddMonths(mesDeslocado);
            titulo.Text = PtBr.DateTimeFormat.GetMonthName(base1.Month).ToUpper(PtBr) + (base1.Year != hoje.Year ? " " + base1.Year : "");
            var grade = new UniformGrid7();
            string letras = "DSTQQSS";
            for (int i = 0; i < 7; i++) grade.Children.Add(Celula(letras[i].ToString(), c.Sub, false, 1, FontWeights.SemiBold));
            // sempre 6 linhas: o tamanho da caixa não muda de um mês para outro
            var d0 = base1.AddDays(-(int)base1.DayOfWeek);
            for (int i = 0; i < 42; i++)
            {
                var d = d0.AddDays(i);
                bool deste = d.Month == base1.Month, ehHoje = d.Date == hoje, fim = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
                var cel = Celula(d.Day.ToString(), ehHoje ? Brushes.White : fim ? c.Sub : c.Tx, ehHoje, deste ? 1 : 0.35, ehHoje ? FontWeights.Bold : FontWeights.Medium);
                // clicar num dia abre o Calendário do Microsoft 365 (compromissos); ele não fica rodando em segundo plano
                var marca = new Ellipse { Width = 26, Height = 26, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center };
                cel.Children.Insert(0, marca); cel.Background = Brushes.Transparent; cel.Cursor = Cursors.Hand;
                if (!ehHoje) { cel.MouseEnter += delegate { marca.Fill = c.Hover; }; cel.MouseLeave += delegate { marca.Fill = Brushes.Transparent; }; }
                Clicavel(cel, () => { if (menuCal != null) Fechar(menuCal); Agenda365.Abrir(); });
                grade.Children.Add(cel);
            }
            corpo.Child = grade;
        }

        static UIElement Seta(Cores c, int glifo, Action acao)
        {
            var t = new TextBlock { Text = ((char)glifo).ToString(), FontFamily = Icones, FontSize = 11, Foreground = c.Sub, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Child = t, Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            b.MouseEnter += delegate { b.Background = c.Hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            Clicavel(b, acao);
            return b;
        }

        static Grid Celula(string txt, Brush cor, bool destaque, double opac, FontWeight peso)
        {
            var g = new Grid { Height = 28 };
            if (destaque) g.Children.Add(new Ellipse { Width = 26, Height = 26, Fill = Vermelho, HorizontalAlignment = HorizontalAlignment.Center });
            var t = T(txt, 13, peso, cor); t.HorizontalAlignment = HorizontalAlignment.Center; t.Opacity = opac;
            g.Children.Add(t);
            return g;
        }

        // 7 colunas iguais
        class UniformGrid7 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid7() { Columns = 7; } }

        // ---------- Wi-Fi ----------
        public static void Wifi(ContextMenu m)
        {
            var c = new Cores(Comum.TemaClaro());
            string atual = null; var perfis = new List<string>();
            try
            {
                atual = SsidAtual();
                foreach (Match x in Regex.Matches(Netsh("wlan show profiles"), @"^[ \t]*[^:\r\n]+:[ \t]*(\S.*?)[ \t]*$", RegexOptions.Multiline))
                    { var nm = x.Groups[1].Value.Trim(); if (nm.Length > 0 && !perfis.Contains(nm)) perfis.Add(nm); }
            }
            catch (Exception e) { Comum.Log("barra.log", "wifi popover: " + e.Message); }
            if (atual != null) { perfis.Remove(atual); perfis.Insert(0, atual); }

            var chave = new Chave(c, atual != null);
            var lista = new StackPanel();
            var raiz = new StackPanel { Width = 300, Margin = new Thickness(2) };
            raiz.Children.Add(Cabecalho(c, "Wi-Fi", chave));
            raiz.Children.Add(Rotulo(c, perfis.Count > 0 ? "Redes Conhecidas" : "Nenhuma rede conhecida"));
            foreach (var nome in perfis.Take(6))
            {
                var n = nome; bool conectada = n == atual;
                var g = new StackPanel { Orientation = Orientation.Horizontal };
                g.Children.Add(Disco(c, 0xE701, conectada));
                g.Children.Add(T(n, 13, FontWeights.Medium, c.Tx));
                lista.Children.Add(Linha(c, g, () => { Fechar(m); if (!conectada) Task.Run(() => Netsh("wlan connect name=\"" + n + "\"")); }));
            }
            raiz.Children.Add(lista);
            raiz.Children.Add(Divisor(c));
            raiz.Children.Add(Linha(c, T("Ajustes de Wi-Fi…", 13, FontWeights.Normal, c.Tx), () => { Fechar(m); Abrir("ms-settings:network-wifi"); }));
            m.Items.Add(raiz);

            LigarRadio(m, chave, lista, "WiFi");
        }

        // nome da rede Wi-Fi conectada: o netsh só mostra o SSID com a Localização ligada; este caminho não precisa dela
        static string SsidAtual()
        {
            try
            {
                var sem = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 && n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .Select(n => n.Name).ToList();
                if (sem.Count == 0) return null;
                using (var q = new ManagementObjectSearcher(new ManagementScope(@"root\StandardCimv2"), new ObjectQuery("SELECT Name, InterfaceAlias FROM MSFT_NetConnectionProfile")))
                    foreach (ManagementObject o in q.Get())
                        if (sem.Contains(o["InterfaceAlias"] as string)) return (o["Name"] as string ?? "").Trim();
            }
            catch (Exception e) { Comum.Log("barra.log", "ssid: " + e.Message); }
            return null;
        }

        // ---------- Bluetooth ----------
        public static void Bluetooth(ContextMenu m)
        {
            var c = new Cores(Comum.TemaClaro());
            var aparelhos = LerBluetooth();
            var chave = new Chave(c, true);
            var lista = new StackPanel();
            var raiz = new StackPanel { Width = 300, Margin = new Thickness(2) };
            raiz.Children.Add(Cabecalho(c, "Bluetooth", chave));
            if (aparelhos.Count > 0) raiz.Children.Add(Rotulo(c, "Aparelhos"));
            foreach (var a in aparelhos)
            {
                var g = new StackPanel { Orientation = Orientation.Horizontal };
                g.Children.Add(Disco(c, GlifoAparelho(a.Key), false));
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(T(a.Key, 13, FontWeights.Medium, c.Tx));
                if (a.Value) col.Children.Add(T("Conectado", 11, FontWeights.Normal, c.Sub));
                g.Children.Add(col);
                var l = Linha(c, g, () => { Fechar(m); Abrir("ms-settings:bluetooth"); });
                if (!a.Value) l.Opacity = 0.5;
                lista.Children.Add(l);
            }
            raiz.Children.Add(lista);
            raiz.Children.Add(Divisor(c));
            raiz.Children.Add(Linha(c, T("Ajustes de Bluetooth…", 13, FontWeights.Normal, c.Tx), () => { Fechar(m); Abrir("ms-settings:bluetooth"); }));
            m.Items.Add(raiz);

            LigarRadio(m, chave, lista, "Bluetooth");
        }

        static int GlifoAparelho(string n)
        {
            n = n.ToLowerInvariant();
            if (Regex.IsMatch(n, "mouse|mx |master|trackpad")) return 0xE962;
            if (Regex.IsMatch(n, "fone|head|buds|airpods|jbl|wh-|wf-|earb")) return 0xE7F6;
            if (Regex.IsMatch(n, "key|tecl|kb|aula|k380|k780")) return 0xE765;
            if (Regex.IsMatch(n, "caixa|speaker|boom|echo")) return 0xE7F5;
            return 0xE702;
        }

        // aparelhos Bluetooth pareados (nome, conectado agora?), sem repetir nomes
        static List<KeyValuePair<string, bool>> LerBluetooth()
        {
            var r = new List<KeyValuePair<string, bool>>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnpEntity WHERE PNPClass='Bluetooth'"))
                    foreach (ManagementObject o in q.Get())
                    {
                        string id = o["DeviceID"] as string, nome = (o["Name"] as string ?? "").Trim();
                        if (id == null || nome == "" || !(id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase))) continue;
                        if (r.Any(k => k.Key == nome)) continue;
                        r.Add(new KeyValuePair<string, bool>(nome, Conectado(id)));
                    }
            }
            catch (Exception e) { Comum.Log("barra.log", "bluetooth lista: " + e.Message); }
            return r.OrderByDescending(k => k.Value).ThenBy(k => k.Key).ToList();
        }

        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint dev, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_DevNode_PropertyW(uint dev, ref DEVPROPKEY k, out uint tipo, byte[] buf, ref uint tam, uint flags);

        // DEVPKEY_Device_IsConnected
        static bool Conectado(string id)
        {
            try
            {
                uint dev; if (CM_Locate_DevNodeW(out dev, id, 0) != 0) return false;
                var k = new DEVPROPKEY { fmtid = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), pid = 15 };
                var buf = new byte[4]; uint tipo, tam = 4;
                return CM_Get_DevNode_PropertyW(dev, ref k, out tipo, buf, ref tam, 0) == 0 && buf[0] != 0;
            }
            catch { return false; }
        }

        // ---------- rádio (liga/desliga de verdade, sem admin) ----------
        const string ScriptRadio = @"
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$g=([System.WindowsRuntimeSystemExtensions].GetMethods()|?{$_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'})[0]
function A($t,$rt){$n=$g.MakeGenericMethod($rt).Invoke($null,@($t));$n.Wait(-1)|Out-Null;$n.Result}
[Windows.Devices.Radios.Radio,Windows.System.Devices,ContentType=WindowsRuntime]|Out-Null
[Windows.Devices.Radios.RadioAccessStatus,Windows.System.Devices,ContentType=WindowsRuntime]|Out-Null
A ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus])|Out-Null
$r=A ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
$x=$r|?{$_.Kind -eq '%KIND%'}|select -First 1
if('%ACAO%' -ne 'ler'){A ($x.SetStateAsync('%ACAO%')) ([Windows.Devices.Radios.RadioAccessStatus])|Out-Null;Write-Output '%ACAO%'}else{Write-Output $x.State}";

        static string Radio(string tipo, string acao)
        {
            var s = ScriptRadio.Replace("%KIND%", tipo).Replace("%ACAO%", acao);
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(s)))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000);
                return o.Trim();
            }
        }

        // lê o estado real do rádio em segundo plano (acerta a chave) e liga/desliga ao clicar
        static void LigarRadio(ContextMenu m, Chave chave, UIElement lista, string tipo)
        {
            Action<bool> aplicar = lig => { lista.Opacity = lig ? 1 : 0.35; lista.IsHitTestVisible = lig; };
            Task.Run(() =>
            {
                try
                {
                    bool lig = Radio(tipo, "ler") != "Off";
                    m.Dispatcher.BeginInvoke(new Action(() => { chave.Definir(lig); aplicar(lig); }));
                }
                catch (Exception e) { Comum.Log("barra.log", "radio ler: " + e.Message); }
            });
            chave.AoMudar = lig =>
            {
                aplicar(lig);
                Task.Run(() =>
                {
                    try { Radio(tipo, lig ? "On" : "Off"); }
                    catch (Exception e) { Comum.Log("barra.log", "radio definir: " + e.Message); }
                });
            };
        }

        // ---------- utilitários ----------
        static string Netsh(string args)
        {
            var psi = new ProcessStartInfo("netsh.exe", args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); return o; }
        }

        static void Abrir(string o) { try { Process.Start(o); } catch (Exception e) { Comum.Log("barra.log", "abrir " + o + ": " + e.Message); } }
    }

    // Calendário do Microsoft 365 (M365 Companions): abre só quando pedido, embaixo da barra à direita, e é encerrado
    // de vez quando a janela some (fechou ou clicou fora), para não ficar em segundo plano
    static class Agenda365
    {
        const string App = @"shell:AppsFolder\Microsoft.M365Companions_8wekyb3d8bbwe!Calendar";
        static System.Windows.Threading.DispatcherTimer vigia;
        static int abertoEm, sumiuEm, viuEm; static bool viu;

        static void Log(string m) { Comum.Log("barra.log", "agenda: " + m); }

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int n);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        // o app não esconde a janela ao sair: ela fica "camuflada" pelo DWM (continua visível para o Windows)
        static bool Aparente(IntPtr h)
        {
            if (h == IntPtr.Zero || !Win32.IsWindowVisible(h) || IsIconic(h)) return false;
            int camuflada; return DwmGetWindowAttribute(h, 14, out camuflada, 4) != 0 || camuflada == 0;
        }

        public static void Abrir()
        {
            try { Process.Start("explorer.exe", App); }
            catch (Exception e) { Log(e.Message); return; }
            abertoEm = Environment.TickCount; sumiuEm = 0; viu = false;
            if (vigia == null)
            {
                vigia = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                vigia.Tick += delegate { Vigiar(); };
            }
            vigia.Start();
        }

        static List<Process> Processos()
        {
            var r = new List<Process>();
            foreach (var nome in new[] { "Calendar", "CalendarLauncher" })
                foreach (var p in Process.GetProcessesByName(nome))
                {
                    try { if (p.MainModule.FileName.IndexOf("M365Companions", StringComparison.OrdinalIgnoreCase) >= 0) r.Add(p); }
                    catch { }
                }
            return r;
        }

        static void Vigiar()
        {
            try
            {
                var procs = Processos();
                IntPtr janela = IntPtr.Zero;
                foreach (var p in procs) { p.Refresh(); if (Aparente(p.MainWindowHandle)) { janela = p.MainWindowHandle; break; } }

                if (janela != IntPtr.Zero)
                {
                    if (!viu) { viu = true; viuEm = Environment.TickCount; Log("aberto"); }
                    sumiuEm = 0;
                    // o app se reposiciona sozinho ao surgir (animação): insiste por uns 2 s
                    if (Environment.TickCount - viuEm < 2000) Posicionar(janela);
                    return;
                }
                if (!viu)
                {
                    if (Environment.TickCount - abertoEm > 20000) vigia.Stop();   // não abriu: desiste
                    return;
                }
                if (sumiuEm == 0) { sumiuEm = Environment.TickCount; return; }
                if (Environment.TickCount - sumiuEm < 1200) return;
                // a janela sumiu: encerra o app para não ficar em segundo plano
                foreach (var p in procs) { try { p.Kill(); } catch { } }
                Log("encerrado (" + procs.Count + " processos)");
                vigia.Stop();
            }
            catch (Exception e) { Log(e.Message); vigia.Stop(); }
        }

        // embaixo da barra de menus, encostado à direita (como as caixas do relógio no Mac)
        static void Posicionar(IntPtr h)
        {
            try
            {
                var r = Win32.Visivel(h);
                int w = r.R - r.L, a = r.B - r.T;
                if (w <= 0 || a <= 0) return;
                var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                int x = area.Right - w - 8, y = area.Top + 6;
                if (Math.Abs(r.L - x) > 2 || Math.Abs(r.T - y) > 2)
                {
                    Win32.RECT j; Win32.GetWindowRect(h, out j);   // a janela inclui bordas invisíveis além da parte visível
                    Win32.Mover(h, x - (r.L - j.L), y - (r.T - j.T), j.R - j.L, j.B - j.T);
                }
            }
            catch (Exception e) { Log("posição: " + e.Message); }
        }
    }
}
