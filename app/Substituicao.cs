// Mac Skin Substituição de Texto: digite um atalho (ex.: @cpf) em qualquer app e ele vira o texto cadastrado.
// Motor: gancho de teclado do Windows numa thread própria; ao reconhecer um atalho, apaga o que foi digitado e escreve
// o texto (digitando, se for curto; colando, se tiver várias linhas). Tudo fica no computador (substituicoes.json).
// Editor: janela no visual do Finder, com o mesmo arranjo do Text Blaze (lista à esquerda, edição à direita).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacSkin
{
    public class AtalhoTexto
    {
        public string Nome { get; set; }
        public string Gatilho { get; set; }
        public string Texto { get; set; }
        public string Pasta { get; set; }
        public bool Ativo { get; set; }
        public int Usos { get; set; }
        public AtalhoTexto() { Nome = ""; Gatilho = ""; Texto = ""; Pasta = "Meus Atalhos"; Ativo = true; }
    }

    public class DadosSubstituicao
    {
        public bool Ligado { get; set; }
        public List<AtalhoTexto> Itens { get; set; }
        public DadosSubstituicao() { Ligado = true; Itens = new List<AtalhoTexto>(); }
    }

    static class Substituicao
    {
        public static DadosSubstituicao D = new DadosSubstituicao();
        static string Arq { get { return Path.Combine(Comum.Dir, "substituicoes.json"); } }
        static Dispatcher ui;
        static volatile AtalhoTexto[] ativos = new AtalhoTexto[0];   // cópia lida pela thread do gancho
        static DispatcherTimer salvarT;
        public static event Action Expandiu;   // um atalho foi usado (o editor atualiza a contagem)

        static void Log(string m) { Comum.Log("substituicao.log", m); }

        // ---------- dados ----------
        static void Carregar()
        {
            try
            {
                if (File.Exists(Arq))
                {
                    var d = new JavaScriptSerializer().Deserialize<DadosSubstituicao>(File.ReadAllText(Arq, Encoding.UTF8));
                    if (d != null && d.Itens != null) D = d;
                }
            }
            catch (Exception e) { Log("carregar: " + e.Message); }
            Atualizou();
        }

        public static void Salvar()
        {
            if (salvarT == null)
            {
                salvarT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                salvarT.Tick += delegate
                {
                    salvarT.Stop();
                    try { File.WriteAllText(Arq, new JavaScriptSerializer().Serialize(D), Encoding.UTF8); }
                    catch (Exception e) { Log("salvar: " + e.Message); }
                };
            }
            salvarT.Stop(); salvarT.Start();
        }

        // refaz a cópia que o gancho consulta (só os ativos, o atalho mais longo primeiro)
        public static void Atualizou()
        {
            ativos = D.Ligado ? D.Itens.Where(a => a.Ativo && !string.IsNullOrEmpty(a.Gatilho)).OrderByDescending(a => a.Gatilho.Length).ToArray() : new AtalhoTexto[0];
        }

        // ---------- motor ----------
        public static void Iniciar()
        {
            ui = Dispatcher.CurrentDispatcher;
            Carregar();
            var th = new Thread(() =>
            {
                teclado = Teclado; mouse = Mouse;
                IntPtr mod = GetModuleHandle(null);
                gTeclado = SetWindowsHookEx(13, teclado, mod, 0);   // WH_KEYBOARD_LL
                gMouse = SetWindowsHookEx(14, mouse, mod, 0);       // WH_MOUSE_LL
                Log("motor iniciado (" + D.Itens.Count + " atalhos)" + (gTeclado == IntPtr.Zero ? " — gancho falhou " + Marshal.GetLastWin32Error() : ""));
                MSG m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessage(ref m); }
            });
            th.IsBackground = true; th.Name = "Substituição"; th.Start();
        }

        public static void Parar()
        {
            if (gTeclado != IntPtr.Zero) UnhookWindowsHookEx(gTeclado);
            if (gMouse != IntPtr.Zero) UnhookWindowsHookEx(gMouse);
            gTeclado = gMouse = IntPtr.Zero;
        }

        const int Marca = 0x4D534B4E;   // dwExtraInfo das teclas que nós mesmos mandamos
        static HookProc teclado, mouse;
        static IntPtr gTeclado, gMouse, ultimaJanela;
        static readonly StringBuilder buf = new StringBuilder();
        static volatile bool expandindo;
        public static IntPtr JanelaEditor; public static volatile bool EditorPausa = true;

        static IntPtr Teclado(int n, IntPtr w, IntPtr l)
        {
            try
            {
                int msg = w.ToInt32();
                if (n >= 0 && (msg == 0x100 || msg == 0x104))
                {
                    var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(KBDLLHOOKSTRUCT));
                    if (k.extra.ToInt64() != Marca && !expandindo) Tecla(k);
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, n, w, l);
        }

        static IntPtr Mouse(int n, IntPtr w, IntPtr l)
        {
            int msg = w.ToInt32();
            if (n >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207)) buf.Clear();   // clicou: o cursor pode ter mudado de lugar
            return CallNextHookEx(IntPtr.Zero, n, w, l);
        }

        static void Tecla(KBDLLHOOKSTRUCT k)
        {
            uint vk = k.vk;
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C || vk == 0x14) return;   // só modificadores
            IntPtr fg = GetForegroundWindow();
            if (fg != ultimaJanela) { buf.Clear(); ultimaJanela = fg; }
            // no editor, só a caixa "Testar" expande: digitar "@cpf" no campo Atalho não pode virar o CPF
            if (fg == JanelaEditor && EditorPausa) { buf.Clear(); return; }
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0, alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            if (vk == 0x08) { if (buf.Length > 0) buf.Length--; return; }            // Backspace
            if (ctrl && !alt) { buf.Clear(); return; }                              // atalhos do app (Ctrl+V etc.)
            if (vk == 0x0D || vk == 0x09 || vk == 0x1B || (vk >= 0x21 && vk <= 0x28) || vk == 0x2E) { buf.Clear(); return; }   // Enter, Tab, Esc, setas, Delete

            var estado = new byte[256];
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) estado[0x10] = 0x80;
            if ((GetKeyState(0x14) & 1) != 0) estado[0x14] = 1;
            if (ctrl && alt) { estado[0x11] = 0x80; estado[0x12] = 0x80; }          // AltGr
            uint tid = GetWindowThreadProcessId(fg, IntPtr.Zero);
            var sb = new StringBuilder(8);
            int r = ToUnicodeEx(vk, k.scan, estado, sb, 8, 4, GetKeyboardLayout(tid));   // 4: não mexe no estado das teclas mortas
            if (r <= 0) return;
            buf.Append(sb.ToString(0, r));
            if (buf.Length > 100) buf.Remove(0, buf.Length - 100);

            string s = buf.ToString();
            foreach (var a in ativos)
                if (s.EndsWith(a.Gatilho, StringComparison.Ordinal))
                {
                    buf.Clear();
                    expandindo = true;
                    var alvo = a;
                    ui.BeginInvoke(new Action(() => Expandir(alvo)));
                    return;
                }
        }

        // ---------- escrever o texto ----------
        static void Expandir(AtalhoTexto a)
        {
            // espera soltar Shift/Ctrl/Alt/Win (o "@" costuma vir com Shift): senão o Backspace e o Ctrl+V saem modificados
            int inicio = Environment.TickCount;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            t.Tick += delegate
            {
                bool preso = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(v => (GetAsyncKeyState(v) & 0x8000) != 0);
                if (preso && Environment.TickCount - inicio < 600) return;
                t.Stop();
                try { Escrever(a); }
                catch (Exception e) { Log("expandir " + a.Gatilho + ": " + e.Message); expandindo = false; }
            };
            t.Start();
        }

        static void Escrever(AtalhoTexto a)
        {
            string copiado = "";
            try { if (Clipboard.ContainsText()) copiado = Clipboard.GetText(); } catch { }
            int depoisCursor;
            string texto = Montar(a.Texto, copiado, out depoisCursor);

            // sempre cola (como o Text Blaze): "digitar" letra por letra falha no Bloco de Notas do Windows 11,
            // que perde e repete caracteres quando eles chegam em rajada
            var lista = new List<INPUT>();
            for (int i = 0; i < a.Gatilho.Length; i++) { lista.Add(Vk(0x08, false)); lista.Add(Vk(0x08, true)); }
            Enviar(lista);

            Action fim = () =>
            {
                if (depoisCursor > 0)
                {
                    var setas = new List<INPUT>();
                    for (int i = 0; i < depoisCursor; i++) { setas.Add(Vk(0x25, false)); setas.Add(Vk(0x25, true)); }
                    Enviar(setas);
                }
                expandindo = false;
                a.Usos++; Salvar();
                var ev = Expandiu; if (ev != null) ev();
            };

            // cola pela área de transferência e depois devolve o que estava nela
            IDataObject antes = null;
            try { antes = CopiarAreaTransf(); } catch { }
            if (!PorTexto(texto.Replace("\r\n", "\n").Replace("\n", "\r\n"))) { expandindo = false; return; }
            // Ctrl+V em passos, com uma pausa curta: apps novos (Bloco de Notas do Windows 11) às vezes não veem o Ctrl
            // quando tudo chega numa rajada só, e recebem só o "v" (ou nada)
            Thread.Sleep(20);
            Enviar(new List<INPUT> { Vk(0x11, false) }); Thread.Sleep(25);
            Enviar(new List<INPUT> { Vk(0x56, false), Vk(0x56, true) }); Thread.Sleep(25);
            Enviar(new List<INPUT> { Vk(0x11, true) });
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += delegate
            {
                t.Stop();
                fim();
                try { if (antes != null) Clipboard.SetDataObject(antes, true); } catch { }
            };
            t.Start();
        }

        static bool PorTexto(string s)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    // fora do histórico (Win+V) e da nuvem: é só uma passagem, não algo que você copiou
                    var o = new DataObject();
                    o.SetData(DataFormats.UnicodeText, s);
                    o.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
                    o.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
                    o.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
                    Clipboard.SetDataObject(o, true); return true;
                }
                catch { Thread.Sleep(30); }
            }
            Log("área de transferência ocupada");
            return false;
        }

        static IDataObject CopiarAreaTransf()
        {
            var o = Clipboard.GetDataObject();
            if (o == null) return null;
            var c = new DataObject();
            foreach (var f in o.GetFormats(false))
            {
                try { var v = o.GetData(f); if (v != null) c.SetData(f, v); } catch { }
            }
            return c;
        }

        // campos: {data} {hora} {semana} {copiado} {cursor}
        public static string Montar(string modelo, string copiado, out int depoisCursor)
        {
            var pt = new CultureInfo("pt-BR"); var agora = DateTime.Now;
            string s = (modelo ?? "")
                .Replace("{data}", agora.ToString("dd/MM/yyyy"))
                .Replace("{hora}", agora.ToString("HH:mm"))
                .Replace("{semana}", pt.DateTimeFormat.GetDayName(agora.DayOfWeek))
                .Replace("{copiado}", copiado ?? "");
            s = s.Replace("\r\n", "\n");
            int i = s.IndexOf("{cursor}", StringComparison.Ordinal);
            depoisCursor = 0;
            if (i >= 0) { s = s.Remove(i, 8).Replace("{cursor}", ""); depoisCursor = s.Length - i; }
            return s;
        }

        static INPUT Vk(ushort vk, bool solta) { var x = new INPUT { type = 1 }; x.ki.vk = vk; x.ki.flags = solta ? 2u : 0u; x.ki.extra = new IntPtr(Marca); return x; }
        static INPUT Uni(char c, bool solta) { var x = new INPUT { type = 1 }; x.ki.scan = c; x.ki.flags = 4u | (solta ? 2u : 0u); x.ki.extra = new IntPtr(Marca); return x; }
        static void Enviar(List<INPUT> l) { if (l.Count > 0) SendInput((uint)l.Count, l.ToArray(), Marshal.SizeOf(typeof(INPUT))); }

        // ---------- Windows ----------
        delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vk, scan, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int v);
        [DllImport("user32.dll")] static extern short GetKeyState(int v);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint tid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicodeEx(uint vk, uint scan, byte[] estado, StringBuilder sb, int n, uint flags, IntPtr hkl);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int tam);

        // ---------- editor ----------
        static EditorSubstituicao editor;
        public static void AbrirEditor()
        {
            try
            {
                if (editor == null || !editor.Aberta) editor = new EditorSubstituicao();
                editor.Mostrar();
            }
            catch (Exception e) { Log("editor: " + e); }
        }
    }

    class EditorSubstituicao
    {
        readonly Window w;
        readonly StackPanel lista;
        readonly TextBox busca, nome, gatilho, texto, teste, pasta;
        readonly TextBlock contagem, aviso, usos, vazioTexto;
        readonly Border painel, vazio, ligado, testeCaixa;
        AtalhoTexto sel; bool montando;
        readonly HashSet<string> fechadas = new HashSet<string>();
        public bool Aberta = true;

        T Ach<T>(string n) where T : class { return (T)w.FindName(n); }

        public EditorSubstituicao()
        {
            w = (Window)XamlReader.Parse(Xaml);
            Tema();
            lista = Ach<StackPanel>("lista"); busca = Ach<TextBox>("busca"); nome = Ach<TextBox>("nome"); gatilho = Ach<TextBox>("gatilho");
            texto = Ach<TextBox>("texto"); teste = Ach<TextBox>("teste"); pasta = Ach<TextBox>("pasta");
            contagem = Ach<TextBlock>("contagem"); aviso = Ach<TextBlock>("aviso"); usos = Ach<TextBlock>("usos"); vazioTexto = Ach<TextBlock>("vazioTexto");
            painel = Ach<Border>("painel"); vazio = Ach<Border>("vazio"); ligado = Ach<Border>("ligado"); testeCaixa = Ach<Border>("testeCaixa");

            Ach<Button>("bFechar").Click += delegate { w.Close(); };
            Ach<Button>("bMin").Click += delegate { w.WindowState = WindowState.Minimized; };
            Ach<Button>("bMax").Click += delegate { w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            Ach<Button>("bNovo").Click += delegate { Novo(sel != null ? sel.Pasta : "Meus Atalhos"); };
            Ach<Button>("bPasta").Click += delegate { NovaPasta(); };
            Ach<Button>("bDuplicar").Click += delegate { Duplicar(); };
            Ach<Button>("bExcluir").Click += delegate { Excluir(); };
            Ach<Button>("bTestar").Click += delegate
            {
                testeCaixa.Visibility = testeCaixa.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (testeCaixa.Visibility == Visibility.Visible) { teste.Text = ""; teste.Focus(); }
            };
            Ach<Button>("cData").Click += delegate { Inserir("{data}"); };
            Ach<Button>("cHora").Click += delegate { Inserir("{hora}"); };
            Ach<Button>("cSemana").Click += delegate { Inserir("{semana}"); };
            Ach<Button>("cCopiado").Click += delegate { Inserir("{copiado}"); };
            Ach<Button>("cCursor").Click += delegate { Inserir("{cursor}"); };
            ligado.MouseLeftButtonUp += delegate { Substituicao.D.Ligado = !Substituicao.D.Ligado; Substituicao.Atualizou(); Substituicao.Salvar(); Chave(ligado, Substituicao.D.Ligado); };
            Ach<Border>("ativo").MouseLeftButtonUp += delegate
            {
                if (sel == null) return;
                sel.Ativo = !sel.Ativo; Chave(Ach<Border>("ativo"), sel.Ativo); Mudou(true);
            };

            busca.TextChanged += delegate { Montar(); };
            nome.TextChanged += delegate { if (!montando && sel != null) { sel.Nome = nome.Text; Mudou(true); } };
            gatilho.TextChanged += delegate { if (!montando && sel != null) { sel.Gatilho = gatilho.Text.Trim(); Mudou(true); } };
            texto.TextChanged += delegate { if (!montando && sel != null) { sel.Texto = texto.Text.Replace("\r\n", "\n"); Mudou(false); } };
            pasta.LostFocus += delegate
            {
                if (montando || sel == null) return;
                var p = pasta.Text.Trim(); sel.Pasta = p.Length > 0 ? p : "Meus Atalhos"; Mudou(true);
            };
            w.PreviewKeyDown += (s, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (ctrl && e.Key == Key.N) { Novo(sel != null ? sel.Pasta : "Meus Atalhos"); e.Handled = true; }
                else if (ctrl && e.Key == Key.F) { busca.Focus(); busca.SelectAll(); e.Handled = true; }
                else if (ctrl && e.Key == Key.D) { Duplicar(); e.Handled = true; }
            };
            Action usou = () => w.Dispatcher.BeginInvoke(new Action(() => { if (sel != null) usos.Text = TextoUsos(sel); }));
            Substituicao.Expandiu += usou;
            w.Closed += delegate { Aberta = false; Substituicao.Expandiu -= usou; Substituicao.JanelaEditor = IntPtr.Zero; };
            // pausa a substituição enquanto o foco está nos campos de edição; na caixa "Testar" ela funciona
            w.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => { Substituicao.EditorPausa = e.NewFocus != teste; }), true);

            w.SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(w).Handle;
                Substituicao.JanelaEditor = h;
                HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
                int escuro = Comum.TemaClaro() ? 0 : 1; DwmSetWindowAttribute(h, 20, ref escuro, 4);
                int fundo = 3; DwmSetWindowAttribute(h, 38, ref fundo, 4);
                int canto = 2; DwmSetWindowAttribute(h, 33, ref canto, 4);
            };

            Chave(ligado, Substituicao.D.Ligado);
            sel = Substituicao.D.Itens.OrderBy(a => a.Pasta).ThenBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();
            Montar(); Mostrar(sel);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);

        public void Mostrar()
        {
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }

        void Tema()
        {
            bool c = Comum.TemaClaro(); var r = w.Resources;
            r["Texto"] = Comum.Cor(c ? "#FF1D1D1F" : "#FFECECEC");
            r["TextoSec"] = Comum.Cor(c ? "#FF6E6E73" : "#FF9A9A9F");
            r["Fundo"] = Comum.Cor(c ? "#FFFFFFFF" : "#FF1E1E1E");
            r["Lateral"] = Comum.Cor(c ? "#66F2F2F4" : "#59262628");
            r["Selecao"] = Comum.Cor(c ? "#FF0063E1" : "#FF0A5BC4");
            r["Hover"] = Comum.Cor(c ? "#14000000" : "#1FFFFFFF");
            r["Pilula"] = Comum.Cor(c ? "#0F000000" : "#1AFFFFFF");
            r["Campo"] = Comum.Cor(c ? "#0A000000" : "#14FFFFFF");
            r["Borda"] = Comum.Cor(c ? "#1F000000" : "#1FFFFFFF");
            r["Azul"] = Comum.Cor("#0A84FF");
            r["Vermelho"] = Comum.Cor("#FF453A");
        }

        // ---------- lista (pastas e atalhos) ----------
        void Montar()
        {
            lista.Children.Clear();
            string q = busca.Text.Trim();
            var itens = Substituicao.D.Itens.Where(a => q.Length == 0
                || (a.Nome ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                || (a.Gatilho ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                || (a.Texto ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            foreach (var g in itens.GroupBy(a => string.IsNullOrEmpty(a.Pasta) ? "Meus Atalhos" : a.Pasta).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                string pastaNome = g.Key; bool fechada = fechadas.Contains(pastaNome) && q.Length == 0;
                lista.Children.Add(CabecalhoPasta(pastaNome, g.Count(), fechada));
                if (fechada) continue;
                foreach (var a in g.OrderBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)) lista.Children.Add(Linha(a));
            }
            int total = Substituicao.D.Itens.Count;
            contagem.Text = total == 1 ? "1 atalho" : total + " atalhos";
            if (itens.Count == 0)
                lista.Children.Add(new TextBlock { Text = q.Length > 0 ? "Nada encontrado para \"" + q + "\"" : "Nenhum atalho ainda", Margin = new Thickness(18, 12, 12, 0), Foreground = (Brush)w.Resources["TextoSec"], TextWrapping = TextWrapping.Wrap });
        }

        UIElement CabecalhoPasta(string nomePasta, int n, bool fechada)
        {
            var g = new Grid { Margin = new Thickness(8, 10, 8, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = ((char)0xE8B7).ToString(), FontFamily = Ico, FontSize = 15, Foreground = (Brush)w.Resources["TextoSec"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) });
            sp.Children.Add(new TextBlock { Text = nomePasta, FontWeight = FontWeights.Bold, FontSize = 13.5, Foreground = (Brush)w.Resources["Texto"], VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = "  " + n, FontSize = 12, Foreground = (Brush)w.Resources["TextoSec"], VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(sp);
            g.Children.Add(new TextBlock { Text = ((char)(fechada ? 0xE76C : 0xE70D)).ToString(), FontFamily = Ico, FontSize = 10, Foreground = (Brush)w.Resources["TextoSec"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            g.MouseLeftButtonUp += delegate { if (!fechadas.Remove(nomePasta)) fechadas.Add(nomePasta); Montar(); };
            return g;
        }

        static readonly FontFamily Ico = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        UIElement Linha(AtalhoTexto a)
        {
            bool eh = a == sel;
            var b = new Border { CornerRadius = new CornerRadius(7), Margin = new Thickness(8, 1, 8, 1), Padding = new Thickness(12, 6, 8, 6), Background = eh ? (Brush)w.Resources["Selecao"] : Brushes.Transparent };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = new TextBlock { Text = string.IsNullOrEmpty(a.Nome) ? "(sem nome)" : a.Nome, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = eh ? Brushes.White : (Brush)w.Resources["Texto"], Margin = new Thickness(18, 0, 8, 0) };
            if (!a.Ativo) n.Opacity = 0.45;
            g.Children.Add(n);
            if (!string.IsNullOrEmpty(a.Gatilho))
            {
                var p = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.2), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = eh ? Brushes.White : (Brush)w.Resources["Azul"], Child = new TextBlock { Text = a.Gatilho, FontSize = 12, Foreground = eh ? Brushes.White : (Brush)w.Resources["Texto"] } };
                if (!a.Ativo) p.Opacity = 0.45;
                Grid.SetColumn(p, 1); g.Children.Add(p);
            }
            b.Child = g;
            b.MouseEnter += delegate { if (a != sel) b.Background = (Brush)w.Resources["Hover"]; };
            b.MouseLeave += delegate { if (a != sel) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate { Mostrar(a); Montar(); };
            return b;
        }

        // ---------- edição ----------
        void Mostrar(AtalhoTexto a)
        {
            sel = a; montando = true;
            painel.Visibility = a == null ? Visibility.Collapsed : Visibility.Visible;
            vazio.Visibility = a == null ? Visibility.Visible : Visibility.Collapsed;
            vazioTexto.Text = Substituicao.D.Itens.Count == 0 ? "Crie seu primeiro atalho no botão +" : "Escolha um atalho na lista";
            testeCaixa.Visibility = Visibility.Collapsed;
            if (a != null)
            {
                nome.Text = a.Nome; gatilho.Text = a.Gatilho; texto.Text = (a.Texto ?? "").Replace("\n", "\r\n"); pasta.Text = a.Pasta;
                Chave(Ach<Border>("ativo"), a.Ativo); usos.Text = TextoUsos(a);
            }
            montando = false;
            Validar();
        }

        static string TextoUsos(AtalhoTexto a) { return a.Usos == 0 ? "Ainda não usado" : a.Usos == 1 ? "Usado 1 vez" : "Usado " + a.Usos + " vezes"; }

        void Mudou(bool lista)
        {
            Substituicao.Atualizou(); Substituicao.Salvar();
            Validar();
            if (lista) Montar();
        }

        void Validar()
        {
            string msg = "";
            if (sel != null)
            {
                var g = sel.Gatilho ?? "";
                if (g.Length == 0) msg = "Defina o atalho (o que você vai digitar).";
                else if (g.Length < 2) msg = "Use pelo menos 2 caracteres para não disparar sem querer.";
                else if (g.Contains(" ")) msg = "O atalho não pode ter espaços.";
                else
                {
                    var igual = Substituicao.D.Itens.FirstOrDefault(x => x != sel && x.Gatilho == g);
                    var prefixo = Substituicao.D.Itens.FirstOrDefault(x => x != sel && !string.IsNullOrEmpty(x.Gatilho) && x.Gatilho != g && (x.Gatilho.EndsWith(g) || g.EndsWith(x.Gatilho)));
                    if (igual != null) msg = "Já existe um atalho igual: \"" + igual.Nome + "\".";
                    else if (prefixo != null) msg = "Conflita com \"" + prefixo.Gatilho + "\" (" + prefixo.Nome + "): um termina com o outro.";
                }
            }
            aviso.Text = msg; aviso.Visibility = msg.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Inserir(string campo)
        {
            if (sel == null) return;
            int i = texto.CaretIndex;
            texto.Text = texto.Text.Insert(i, campo);
            texto.CaretIndex = i + campo.Length; texto.Focus();
        }

        void Novo(string pastaNome)
        {
            var a = new AtalhoTexto { Nome = "Novo atalho", Pasta = string.IsNullOrEmpty(pastaNome) ? "Meus Atalhos" : pastaNome };
            Substituicao.D.Itens.Add(a);
            busca.Text = ""; fechadas.Remove(a.Pasta);
            Mostrar(a); Mudou(true);
            nome.Focus(); nome.SelectAll();
        }

        void NovaPasta()
        {
            int n = 1; string p = "Nova pasta";
            while (Substituicao.D.Itens.Any(x => x.Pasta == p)) p = "Nova pasta " + (++n);
            Novo(p);
            pasta.Focus(); pasta.SelectAll();
        }

        void Duplicar()
        {
            if (sel == null) return;
            var a = new AtalhoTexto { Nome = sel.Nome + " (cópia)", Gatilho = sel.Gatilho + "2", Texto = sel.Texto, Pasta = sel.Pasta, Ativo = sel.Ativo };
            Substituicao.D.Itens.Add(a);
            Mostrar(a); Mudou(true);
            gatilho.Focus(); gatilho.SelectAll();
        }

        void Excluir()
        {
            if (sel == null) return;
            var a = sel;
            Confirmacao.Perguntar("Excluir o atalho \"" + (string.IsNullOrEmpty(a.Nome) ? a.Gatilho : a.Nome) + "\"?", "Esta ação não pode ser desfeita.", "Excluir", () =>
            {
                int i = Substituicao.D.Itens.IndexOf(a);
                Substituicao.D.Itens.Remove(a);
                var prox = Substituicao.D.Itens.Count == 0 ? null : Substituicao.D.Itens[Math.Min(Math.Max(0, i), Substituicao.D.Itens.Count - 1)];
                Mostrar(prox); Mudou(true);
            }, (char)0xE74D);   // lixeira
        }

        // interruptor do Mac (Border com uma bolinha dentro)
        void Chave(Border b, bool lig)
        {
            b.Background = lig ? (Brush)w.Resources["Azul"] : (Brush)w.Resources["Pilula"];
            var bola = b.Child as Border;
            if (bola != null) bola.HorizontalAlignment = lig ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Substituição de Texto' Width='1040' Height='680' MinWidth='760' MinHeight='460' WindowStartupLocation='CenterScreen'
        Background='Transparent' FontFamily='Inter, Segoe UI' FontSize='13' UseLayoutRounding='True'>
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight='52' ResizeBorderThickness='6' GlassFrameThickness='-1' CornerRadius='0' UseAeroCaptionButtons='False'/>
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <FontFamily x:Key='Ico'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
    <Style x:Key='Plano' TargetType='Button'>
      <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
      <Setter Property='Background' Value='{DynamicResource Pilula}'/>
      <Setter Property='Padding' Value='12,6'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='WindowChrome.IsHitTestVisibleInChrome' Value='True'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='8' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.8'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.6'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key='Redondo' TargetType='Button' BasedOn='{StaticResource Plano}'>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Grid><Ellipse x:Name='b' Fill='{TemplateBinding Background}'/><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.85'/></Trigger>
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
    <Style x:Key='Caixa' TargetType='TextBox'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
      <Setter Property='CaretBrush' Value='{DynamicResource Texto}'/>
      <Setter Property='SelectionBrush' Value='{DynamicResource Azul}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
    </Style>
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
        <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,2'><Track.Thumb><Thumb Style='{StaticResource Alca}'/></Track.Thumb></Track></Grid>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
  </Window.Resources>

  <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width='330' MinWidth='240' MaxWidth='560'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>

    <!-- lateral: botões, busca, lista por pasta e o interruptor geral -->
    <Border Background='{DynamicResource Lateral}'>
      <Grid>
        <Grid.RowDefinitions><RowDefinition Height='52'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='44'/></Grid.RowDefinitions>
        <TextBlock Text='Substituição de Texto' Margin='18,0,0,0' VerticalAlignment='Center' FontWeight='SemiBold' Foreground='{DynamicResource TextoSec}' IsHitTestVisible='False'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,0,12,0' VerticalAlignment='Center'>
          <Button x:Name='bPasta' Style='{StaticResource Redondo}' Width='30' Height='30' Margin='0,0,8,0' ToolTip='Nova pasta'>
            <TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8F4;' FontSize='13'/></Button>
          <Button x:Name='bNovo' Style='{StaticResource Redondo}' Width='30' Height='30' Background='{DynamicResource Azul}' Foreground='White' ToolTip='Novo atalho (Ctrl+N)'>
            <TextBlock FontFamily='{StaticResource Ico}' Text='&#xE710;' FontSize='13' FontWeight='Bold'/></Button>
        </StackPanel>
        <Border Grid.Row='1' Background='{DynamicResource Campo}' CornerRadius='9' Height='32' Margin='14,0,14,6' BorderBrush='{DynamicResource Borda}' BorderThickness='1'>
          <Grid><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE721;' FontSize='12' Margin='11,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}'/>
            <TextBox x:Name='busca' Style='{StaticResource Caixa}' Margin='30,0,10,0'/>
            <TextBlock Text='Buscar (Ctrl+F)' Margin='32,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}' IsHitTestVisible='False'>
              <TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Visibility' Value='Collapsed'/><Style.Triggers>
                <DataTrigger Binding='{Binding Text, ElementName=busca}' Value=''><Setter Property='Visibility' Value='Visible'/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock></Grid></Border>
        <ScrollViewer Grid.Row='2' VerticalScrollBarVisibility='Auto'><StackPanel x:Name='lista' Margin='0,0,0,10'/></ScrollViewer>
        <Border Grid.Row='3' BorderBrush='{DynamicResource Borda}' BorderThickness='0,1,0,0'>
          <Grid Margin='14,0'>
            <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
              <Border x:Name='ligado' Width='36' Height='21' CornerRadius='10.5' Cursor='Hand'>
                <Border Width='17' Height='17' CornerRadius='8.5' Background='White' Margin='2' HorizontalAlignment='Right'/></Border>
              <TextBlock Text='Substituição ligada' Margin='9,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource Texto}'/>
            </StackPanel>
            <TextBlock x:Name='contagem' HorizontalAlignment='Right' VerticalAlignment='Center' FontSize='11.5' Foreground='{DynamicResource TextoSec}'/>
          </Grid>
        </Border>
      </Grid>
    </Border>
    <GridSplitter Grid.Column='0' HorizontalAlignment='Right' Width='6' Margin='0,0,-3,0' Panel.ZIndex='5' Background='Transparent' ResizeDirection='Columns' ResizeBehavior='CurrentAndNext' Cursor='SizeWE' WindowChrome.IsHitTestVisibleInChrome='True'/>

    <!-- edição -->
    <Grid Grid.Column='1' Background='{DynamicResource Fundo}'>
      <Grid.RowDefinitions><RowDefinition Height='52'/><RowDefinition Height='*'/></Grid.RowDefinitions>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='bMin' Style='{StaticResource Janela}' Tag='#FEBC2E'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE921;' FontSize='10'/></Button>
        <Button x:Name='bMax' Style='{StaticResource Janela}' Tag='#28C840'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE922;' FontSize='10'/></Button>
        <Button x:Name='bFechar' Style='{StaticResource Janela}' Tag='#C42B1C'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8BB;' FontSize='10'/></Button>
      </StackPanel>

      <Border x:Name='vazio' Grid.Row='1' Visibility='Collapsed'>
        <StackPanel HorizontalAlignment='Center' VerticalAlignment='Center'>
          <TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8D2;' FontSize='42' HorizontalAlignment='Center' Foreground='{DynamicResource TextoSec}' Opacity='0.6'/>
          <TextBlock x:Name='vazioTexto' Margin='0,12,0,0' FontSize='14' Foreground='{DynamicResource TextoSec}' HorizontalAlignment='Center'/>
        </StackPanel>
      </Border>

      <Border x:Name='painel' Grid.Row='1' Padding='26,4,26,20'>
        <Grid>
          <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
          <!-- Nome e AtalhoTexto, como no Text Blaze -->
          <Grid>
            <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='16'/><ColumnDefinition/></Grid.ColumnDefinitions>
            <Border BorderBrush='{DynamicResource Borda}' BorderThickness='1' CornerRadius='10' Background='{DynamicResource Campo}' Padding='12,6,12,8'>
              <StackPanel>
                <TextBlock Text='Nome (descreve o atalho)' FontSize='11' Foreground='{DynamicResource TextoSec}'/>
                <Grid Margin='0,4,0,0'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8EC;' FontSize='14' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}'/>
                  <TextBox x:Name='nome' Style='{StaticResource Caixa}' FontSize='15' Margin='26,0,0,0'/></Grid>
              </StackPanel>
            </Border>
            <Border Grid.Column='2' BorderBrush='{DynamicResource Borda}' BorderThickness='1' CornerRadius='10' Background='{DynamicResource Campo}' Padding='12,6,8,8'>
              <Grid>
                <StackPanel Margin='0,0,86,0'>
                  <TextBlock Text='Atalho (o que você digita)' FontSize='11' Foreground='{DynamicResource TextoSec}'/>
                  <Grid Margin='0,4,0,0'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE92E;' FontSize='14' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}'/>
                    <TextBox x:Name='gatilho' Style='{StaticResource Caixa}' FontSize='15' Margin='26,0,0,0'/></Grid>
                </StackPanel>
                <Button x:Name='bTestar' Style='{StaticResource Plano}' HorizontalAlignment='Right' VerticalAlignment='Center' Foreground='{DynamicResource Azul}' FontWeight='SemiBold'>Testar</Button>
              </Grid>
            </Border>
          </Grid>
          <TextBlock x:Name='aviso' Grid.Row='1' Margin='4,8,0,0' Foreground='{DynamicResource Vermelho}' FontSize='12' Visibility='Collapsed' TextWrapping='Wrap'/>
          <!-- campos automáticos -->
          <WrapPanel Grid.Row='2' Margin='0,14,0,8'>
            <TextBlock Text='Inserir:' VerticalAlignment='Center' Margin='2,0,8,0' Foreground='{DynamicResource TextoSec}' FontSize='12'/>
            <Button x:Name='cData' Style='{StaticResource Plano}' Margin='0,0,6,4' ToolTip='Data de hoje (31/10/2026)'><StackPanel Orientation='Horizontal'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE787;' Margin='0,0,6,0' VerticalAlignment='Center'/><TextBlock Text='Data'/></StackPanel></Button>
            <Button x:Name='cHora' Style='{StaticResource Plano}' Margin='0,0,6,4' ToolTip='Hora atual (14:30)'><StackPanel Orientation='Horizontal'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE823;' Margin='0,0,6,0' VerticalAlignment='Center'/><TextBlock Text='Hora'/></StackPanel></Button>
            <Button x:Name='cSemana' Style='{StaticResource Plano}' Margin='0,0,6,4' ToolTip='Dia da semana (sábado)'><StackPanel Orientation='Horizontal'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8BF;' Margin='0,0,6,0' VerticalAlignment='Center'/><TextBlock Text='Dia da semana'/></StackPanel></Button>
            <Button x:Name='cCopiado' Style='{StaticResource Plano}' Margin='0,0,6,4' ToolTip='O que estiver copiado (Ctrl+C)'><StackPanel Orientation='Horizontal'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE77F;' Margin='0,0,6,0' VerticalAlignment='Center'/><TextBlock Text='Área de transferência'/></StackPanel></Button>
            <Button x:Name='cCursor' Style='{StaticResource Plano}' Margin='0,0,6,4' ToolTip='Onde o cursor fica depois de inserir'><StackPanel Orientation='Horizontal'><TextBlock FontFamily='{StaticResource Ico}' Text='&#xE8AC;' Margin='0,0,6,0' VerticalAlignment='Center'/><TextBlock Text='Cursor'/></StackPanel></Button>
          </WrapPanel>
          <!-- texto -->
          <Border Grid.Row='3' BorderBrush='{DynamicResource Borda}' BorderThickness='1' CornerRadius='10' Background='{DynamicResource Campo}'>
            <TextBox x:Name='texto' Style='{StaticResource Caixa}' FontSize='14.5' AcceptsReturn='True' AcceptsTab='True' TextWrapping='Wrap' VerticalContentAlignment='Top'
                     VerticalScrollBarVisibility='Auto' Padding='12,10'/>
          </Border>
          <!-- testar -->
          <Border x:Name='testeCaixa' Grid.Row='4' Margin='0,12,0,0' Visibility='Collapsed' BorderBrush='{DynamicResource Azul}' BorderThickness='1.5' CornerRadius='10' Padding='12,8'>
            <StackPanel>
              <TextBlock Text='Experimente: digite o atalho aqui embaixo' FontSize='11' Foreground='{DynamicResource TextoSec}'/>
              <TextBox x:Name='teste' Style='{StaticResource Caixa}' FontSize='14' Margin='0,4,0,0' AcceptsReturn='True' TextWrapping='Wrap' MinHeight='40'/>
            </StackPanel>
          </Border>
          <!-- rodapé -->
          <Grid Grid.Row='5' Margin='0,14,0,0'>
            <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
              <Border x:Name='ativo' Width='36' Height='21' CornerRadius='10.5' Cursor='Hand'>
                <Border Width='17' Height='17' CornerRadius='8.5' Background='White' Margin='2' HorizontalAlignment='Right'/></Border>
              <TextBlock Text='Ativo' Margin='9,0,18,0' VerticalAlignment='Center' Foreground='{DynamicResource Texto}'/>
              <TextBlock Text='Pasta' Margin='0,0,8,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}' FontSize='12'/>
              <Border BorderBrush='{DynamicResource Borda}' BorderThickness='1' CornerRadius='7' Background='{DynamicResource Campo}' Padding='8,3' MinWidth='140'>
                <TextBox x:Name='pasta' Style='{StaticResource Caixa}'/></Border>
              <TextBlock x:Name='usos' Margin='16,0,0,0' VerticalAlignment='Center' Foreground='{DynamicResource TextoSec}' FontSize='12'/>
            </StackPanel>
            <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
              <Button x:Name='bDuplicar' Style='{StaticResource Plano}' Margin='0,0,8,0' ToolTip='Ctrl+D'>Duplicar</Button>
              <Button x:Name='bExcluir' Style='{StaticResource Plano}' Foreground='{DynamicResource Vermelho}'>Excluir</Button>
            </StackPanel>
          </Grid>
        </Grid>
      </Border>
    </Grid>
  </Grid>
</Window>";
    }
}
