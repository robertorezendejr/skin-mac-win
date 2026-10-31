// Mac Skin Escolha: as caixas "Abrir", "Salvar como" e "Selecionar pasta" dos outros programas viram o nosso Finder.
// A caixa nativa (IFileDialog, #32770 com DUIViewWndClassName) é do próprio programa e não aceita tema: quando ela surge,
// o Mac Skin a esconde e abre "MacSkin.exe --escolher <hwnd>". Esse processo lê o tipo e a pasta da caixa escondida,
// mostra o Finder com uma barra de escolha embaixo e devolve a resposta à caixa original: escreve o caminho no campo
// de nome e manda o OK (ou o Cancelar). Se algo der errado, a caixa nativa volta a aparecer.
// O arquivo "caixas-nativas" na pasta do Mac Skin desliga a troca.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacSkin
{
    static class Escolha
    {
        public const int Respondeu = 0, Nativa = 3, NaoEntendeu = 4;

        delegate void WinEventProc(IntPtr gancho, uint ev, IntPtr h, int objeto, int filho, uint thread, uint tempo);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint de, uint ate, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr g);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint c);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string classe, string titulo);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, string l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, StringBuilder l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr pai, EnumProc f, IntPtr l);

        static WinEventProc proc; static IntPtr g1, g2, g3;
        static readonly HashSet<IntPtr> tratadas = new HashSet<IntPtr>();
        static readonly HashSet<IntPtr> ativas = new HashSet<IntPtr>();   // com o Finder aberto no lugar: a caixa se mostra de novo sozinha
        static readonly uint Eu = (uint)Process.GetCurrentProcess().Id;
        static readonly string Desligada = Path.Combine(Comum.Dir, "caixas-nativas");
        static readonly string Ultima = Path.Combine(Comum.Dir, "escolha-ultima.txt");

        static void Log(string m) { Comum.Log("escolha.log", m); }

        // ---------- no Mac Skin: vigia as caixas e troca pelo Finder ----------
        public static void Iniciar()
        {
            proc = (g, ev, h, o, f, t, tp) =>
            {
                if (o != 0 || h == IntPtr.Zero) return;
                try { if (ev == 0x8000) Nasceu(h); else Examinar(h); } catch (Exception e) { Log("erro: " + e.Message); }
            };
            g1 = SetWinEventHook(0x0010, 0x0010, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_SYSTEM_DIALOGSTART
            g2 = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_SHOW
            g3 = SetWinEventHook(0x8000, 0x8000, IntPtr.Zero, proc, 0, 0, 2);   // EVENT_OBJECT_CREATE
        }

        public static void Parar()
        {
            foreach (var g in new[] { g1, g2, g3 }) if (g != IntPtr.Zero) UnhookWinEvent(g);
            g1 = g2 = g3 = IntPtr.Zero;
        }

        // o painel DUIViewWndClassName nasce dentro da caixa bem antes de ela aparecer: a caixa já fica invisível
        // (sem isso ela piscava na tela até o EVENT_OBJECT_SHOW chegar)
        static void Nasceu(IntPtr filho)
        {
            if (Win32.Classe(filho) != "DUIViewWndClassName") return;
            IntPtr h = Win32.GetAncestor(filho, 1);   // GA_PARENT
            if (Win32.Classe(h) != "#32770" || Win32.Pid(h) == Eu || File.Exists(Desligada)) return;
            if (Janelas.NomeDoProcesso(Win32.Pid(h)) == "macskin") return;
            Win32.Invisivel(h);
        }

        static void Examinar(IntPtr h)
        {
            if (Win32.Classe(h) != "#32770" || Win32.Pid(h) == Eu) return;
            lock (tratadas)
            {
                if (ativas.Contains(h)) { Win32.Esconder(h); Log("a caixa se mostrou de novo; escondida"); return; }
                if (tratadas.Contains(h)) return;
            }
            if (FindWindowEx(h, IntPtr.Zero, "DUIViewWndClassName", null) == IntPtr.Zero) return;
            if (Tipo(h) == null) { Win32.Opaca(h); return; }   // tem o painel mas não é caixa de arquivo conhecida
            lock (tratadas) { if (!tratadas.Add(h)) return; if (tratadas.Count > 100) tratadas.RemoveWhere(x => !IsWindow(x)); }
            string quem = Janelas.NomeDoProcesso(Win32.Pid(h));
            if (File.Exists(Desligada) || quem == "macskin") { Win32.Opaca(h); return; }
            Win32.Esconder(h);   // antes de pintar: o usuário só vê o Finder
            Log("caixa '" + Win32.Titulo(h) + "' (" + Tipo(h) + ") de " + quem);
            var th = new Thread(() => Trocar(h)) { IsBackground = true };
            th.Start();
        }

        static void Trocar(IntPtr h)
        {
            int codigo = -1;
            lock (tratadas) ativas.Add(h);
            try
            {
                using (var p = Process.Start(Process.GetCurrentProcess().MainModule.FileName, "--escolher " + h.ToInt64()))
                {
                    p.WaitForExit(); codigo = p.ExitCode;
                }
            }
            catch (Exception e) { Log("não abriu o Finder: " + e.Message); }
            lock (tratadas) ativas.Remove(h);
            if (codigo == Nativa) { Devolver(h); return; }   // "Usar caixa do Windows"
            if (codigo != Respondeu) { Log("Finder saiu com " + codigo + "; devolvendo a caixa nativa"); Devolver(h); return; }
            // respondeu: a caixa fecha sozinha. Se continuar aberta e livre (o programa recusou o nome, por exemplo),
            // volta a nativa; desabilitada quer dizer que ela mesma abriu uma pergunta (substituir o arquivo?)
            int livre = 0;
            for (int i = 0; i < 1200 && IsWindow(h); i++)
            {
                Thread.Sleep(500);
                if (!IsWindow(h)) break;
                livre = IsWindowEnabled(h) ? livre + 1 : 0;
                if (livre >= 3) { Log("a caixa continuou aberta depois da resposta; devolvendo a nativa"); Devolver(h); return; }
            }
        }

        static void Devolver(IntPtr h)
        {
            if (!IsWindow(h)) return;
            Win32.Opaca(h); Win32.ShowWindow(h, 5); Win32.SetForegroundWindow(h);
        }

        // "pasta", "abrir" ou "salvar", pelos controles da caixa; null se não for uma caixa de arquivo conhecida
        static string Tipo(IntPtr h)
        {
            if (Filho(h, "Edit", 1152) != IntPtr.Zero) return "pasta";
            if (Filho(h, "ComboBoxEx32", 1148) != IntPtr.Zero) return "abrir";
            if (Filho(h, "Edit", 1001) != IntPtr.Zero) return "salvar";
            return null;
        }

        static IntPtr Filho(IntPtr pai, string classe, int id)
        {
            IntPtr r = IntPtr.Zero;
            EnumChildWindows(pai, (f, l) => { if (GetDlgCtrlID(f) == id && Win32.Classe(f) == classe) { r = f; return false; } return true; }, IntPtr.Zero);
            return r;
        }

        static string Texto(IntPtr h)
        {
            var s = new StringBuilder(2048); SendMessage(h, 0x000D, (IntPtr)2048, s); return s.ToString();   // WM_GETTEXT
        }

        // ---------- no processo do Finder (MacSkin.exe --escolher hwnd) ----------
        public static int Abrir(IntPtr caixa)
        {
            string tipo = IsWindow(caixa) ? Tipo(caixa) : null;
            if (tipo == null) return NaoEntendeu;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int codigo = Respondeu;
            app.DispatcherUnhandledException += (s, e) => { Log("falha: " + e.Exception); e.Handled = true; };
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try { new Seletor(caixa, tipo, c => { codigo = c; app.Shutdown(c); }).Mostrar(); }
            catch (Exception e) { Log("não abriu: " + e); return NaoEntendeu; }
            app.Run();
            return codigo;
        }

        class Seletor
        {
            readonly IntPtr caixa; readonly string tipo; readonly Action<int> sair;
            readonly Finder f; readonly Window w;
            TextBox nome; TextBlock escolhido; Border ok; TextBlock okTexto;
            bool respondido;

            public Seletor(IntPtr caixa, string tipo, Action<int> sair)
            {
                this.caixa = caixa; this.tipo = tipo; this.sair = sair;
                f = new Finder(PastaInicial());
                w = f.Janela;
                string titulo = Win32.Titulo(caixa);
                w.Title = (titulo.Length > 0 ? titulo : "Escolher") + " — Finder";
                MontarBarra(titulo);
                f.AoAbrir = sel =>
                {
                    var arqs = sel.Where(e => !e.EhPasta).ToList();
                    if (arqs.Count == 0) return false;
                    if (tipo == "abrir") Confirmar();
                    else if (tipo == "salvar") { nome.Text = arqs[0].Nome; nome.Focus(); nome.SelectAll(); }
                    return true;   // nos outros modos, duplo clique em arquivo não abre o arquivo
                };
                f.Mudou += Atualizar;
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.SourceInitialized += delegate
                {
                    var dono = GetWindow(caixa, 4);   // GW_OWNER: a janela do programa; o Finder fica sobre ela
                    if (dono != IntPtr.Zero) new WindowInteropHelper(w).Owner = dono;
                    // no centro do monitor do programa (com dois monitores, o WPF sozinho escolhe o outro às vezes)
                    var area = System.Windows.Forms.Screen.FromHandle(dono != IntPtr.Zero ? dono : caixa).WorkingArea;
                    var m = PresentationSource.FromVisual(w).CompositionTarget.TransformFromDevice;
                    Point a = m.Transform(new Point(area.Left, area.Top)), b = m.Transform(new Point(area.Right, area.Bottom));
                    w.Width = Math.Min(w.Width, b.X - a.X - 40); w.Height = Math.Min(w.Height, b.Y - a.Y - 40);
                    w.Left = a.X + (b.X - a.X - w.Width) / 2; w.Top = a.Y + (b.Y - a.Y - w.Height) / 2;
                };
                w.KeyDown += (s, e) => { if (e.Key == Key.Escape && !e.Handled && !f.PreviaAberta) { e.Handled = true; Cancelar(); } };
                w.Closed += delegate { if (!respondido) Cancelar(); };
                Atualizar();
            }

            public void Mostrar()
            {
                f.Mostrar();
                if (nome != null) { nome.Focus(); int ponto = nome.Text.LastIndexOf('.'); nome.Select(0, ponto > 0 ? ponto : nome.Text.Length); }
            }

            void MontarBarra(string titulo)
            {
                var caminho = (Border)w.FindName("caminhoFundo");
                var grade = (Grid)caminho.Parent;
                grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Brush texto = (Brush)w.Resources["Texto"], sec = (Brush)w.Resources["TextoSec"];

                var barra = new DockPanel { Margin = new Thickness(16, 12, 16, 12), LastChildFill = true };
                var botoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(botoes, System.Windows.Controls.Dock.Right);
                var nativa = Botao("Usar caixa do Windows", false, texto); nativa.ToolTip = "Mostra a caixa original do Windows no lugar do Finder";
                ((TextBlock)nativa.Child).Foreground = sec; nativa.Background = Brushes.Transparent; nativa.MinWidth = 0;
                nativa.MouseLeftButtonUp += delegate { UsarNativa(); };
                var cancelar = Botao("Cancelar", false, texto);
                cancelar.MouseLeftButtonUp += delegate { Cancelar(); };
                string rotulo = Texto(Filho(caixa, "Button", 1)).Replace("&", "");
                if (rotulo.Length == 0) rotulo = tipo == "pasta" ? "Selecionar pasta" : tipo == "abrir" ? "Abrir" : "Salvar";
                ok = Botao(rotulo, true, texto); okTexto = (TextBlock)ok.Child;
                ok.MouseLeftButtonUp += delegate { if (ok.IsEnabled) Confirmar(); };
                botoes.Children.Add(nativa); botoes.Children.Add(cancelar); botoes.Children.Add(ok);
                barra.Children.Add(botoes);

                var esq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                esq.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold, Foreground = texto, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
                if (tipo == "salvar")
                {
                    esq.Children.Add(new TextBlock { Text = "Nome:", Foreground = sec, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                    nome = new TextBox
                    {
                        Text = Texto(Filho(caixa, "Edit", 1001)), Width = 300, Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0),
                        Background = (Brush)w.Resources["Campo"], Foreground = texto, CaretBrush = texto, VerticalAlignment = VerticalAlignment.Center
                    };
                    nome.TextChanged += delegate { Atualizar(); };
                    nome.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (ok.IsEnabled) Confirmar(); } };
                    esq.Children.Add(new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = nome });
                }
                else
                {
                    escolhido = new TextBlock { Foreground = sec, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    esq.Children.Add(escolhido);
                }
                barra.Children.Add(esq);

                var fundo = new Border { BorderBrush = (Brush)w.Resources["Borda"], BorderThickness = new Thickness(0, 1, 0, 0), Background = (Brush)w.Resources["Pilula"], Child = barra };
                Grid.SetRow(fundo, grade.RowDefinitions.Count - 1);
                grade.Children.Add(fundo);
                w.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox) return;
                    // com algo selecionado o Finder trata (entra na pasta; arquivo cai no AoAbrir); sem seleção, confirma a pasta atual
                    if (tipo != "abrir" && f.Selecao().Count == 0 && ok.IsEnabled) { e.Handled = true; Confirmar(); }
                };
            }

            Border Botao(string rotulo, bool destaque, Brush texto)
            {
                var t = new TextBlock { Text = rotulo, FontSize = 13, FontWeight = destaque ? FontWeights.SemiBold : FontWeights.Normal, Foreground = destaque ? Brushes.White : texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Brush fundo = destaque ? Comum.Cor("#FF007AFF") : (Brush)w.Resources["Campo"], realce = destaque ? Comum.Cor("#FF2E91FF") : (Brush)w.Resources["Hover"];
                var b = new Border { MinWidth = 92, Height = 30, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(15), Background = fundo, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Child = t };
                b.MouseEnter += delegate { if (b.IsEnabled && b.Background != Brushes.Transparent) b.Background = realce; };
                b.MouseLeave += delegate { if (b.Background != Brushes.Transparent) b.Background = fundo; };
                b.IsEnabledChanged += delegate { b.Opacity = b.IsEnabled ? 1 : 0.4; b.Cursor = b.IsEnabled ? Cursors.Hand : Cursors.Arrow; };
                return b;
            }

            void Atualizar()
            {
                if (ok == null) return;
                string atual = f.Atual; bool real = atual != null && !Finder.EhVirtual(atual);
                var sel = f.Selecao();
                if (tipo == "pasta")
                {
                    var p = sel.Count == 1 && sel[0].EhPasta ? sel[0].Caminho : atual;
                    ok.IsEnabled = real || (sel.Count == 1 && sel[0].EhPasta);
                    string n = p == null ? "" : Path.GetFileName(p.TrimEnd('\\'));
                    escolhido.Text = !ok.IsEnabled ? "Escolha uma pasta" : n.Length > 0 ? n : p;
                }
                else if (tipo == "abrir")
                {
                    var arqs = sel.Where(x => !x.EhPasta).ToList();
                    bool soPasta = sel.Count == 1 && sel[0].EhPasta;
                    ok.IsEnabled = arqs.Count > 0 || soPasta;
                    escolhido.Text = arqs.Count == 1 ? arqs[0].Nome : arqs.Count > 1 ? arqs.Count + " arquivos selecionados" : soPasta ? sel[0].Nome : "Escolha um arquivo";
                }
                else ok.IsEnabled = real && nome.Text.Trim().Length > 0 && nome.Text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            }

            void Confirmar()
            {
                if (respondido) return;
                var sel = f.Selecao(); string atual = f.Atual;
                if (tipo == "pasta")
                {
                    string p = sel.Count == 1 && sel[0].EhPasta ? sel[0].Caminho : atual;
                    if (p == null || Finder.EhVirtual(p)) return;
                    Responder(Filho(caixa, "Edit", 1152), p);
                }
                else if (tipo == "abrir")
                {
                    var arqs = sel.Where(x => !x.EhPasta).ToList();
                    if (arqs.Count == 0)
                    {
                        if (sel.Count == 1 && sel[0].EhPasta) f.Entrar(sel[0].Caminho);   // como no Mac: o botão entra na pasta
                        return;
                    }
                    var campo = Filho(Filho(caixa, "ComboBoxEx32", 1148), "Edit", 1148);
                    var pastas = arqs.Select(x => Path.GetDirectoryName(x.Caminho)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (arqs.Count == 1) Responder(campo, arqs[0].Caminho);
                    else
                    {
                        // vários: a caixa só aceita nomes da pasta em que está; o caminho da pasta + OK a leva até lá
                        string pasta = pastas[0];
                        var nomes = string.Join(" ", arqs.Where(x => string.Equals(Path.GetDirectoryName(x.Caminho), pasta, StringComparison.OrdinalIgnoreCase)).Select(x => "\"" + x.Nome + "\""));
                        Esconder();
                        SendMessage(campo, 0x000C, IntPtr.Zero, pasta); Win32.PostMessage(caixa, 0x0111, (IntPtr)1, IntPtr.Zero);
                        Thread.Sleep(700);
                        Responder(campo, nomes);
                    }
                    Lembrar(pastas[0]);
                    return;
                }
                else
                {
                    if (!ok.IsEnabled) return;
                    Responder(Filho(caixa, "Edit", 1001), Path.Combine(atual, nome.Text.Trim()));
                }
                Lembrar(tipo == "pasta" ? atual : f.Atual);
            }

            void Responder(IntPtr campo, string valor)
            {
                if (campo == IntPtr.Zero) { Log("campo não encontrado; devolvendo a nativa"); UsarNativa(); return; }
                Esconder();
                SendMessage(campo, 0x000C, IntPtr.Zero, valor);                    // WM_SETTEXT
                Win32.PostMessage(caixa, 0x0111, (IntPtr)1, IntPtr.Zero);           // WM_COMMAND IDOK
                Log(tipo + ": " + valor);
                Fim(Respondeu);
            }

            void Cancelar()
            {
                if (respondido) return;
                Esconder();
                Win32.PostMessage(caixa, 0x0111, (IntPtr)2, IntPtr.Zero);           // WM_COMMAND IDCANCEL
                Log(tipo + ": cancelado");
                Fim(Respondeu);
            }

            void UsarNativa()
            {
                if (respondido) return;
                Esconder();   // quem mostra a nativa é o Mac Skin, quando este processo sair
                Log(tipo + ": caixa do Windows");
                Fim(Nativa);
            }

            void Esconder() { respondido = true; if (w.IsVisible) w.Hide(); }
            void Fim(int codigo) { respondido = true; w.Close(); sair(codigo); }

            static void Lembrar(string pasta) { try { if (pasta != null && Directory.Exists(pasta)) File.WriteAllText(Ultima, pasta); } catch { } }

            // a pasta em que a caixa abriu, pelo caminho da barra de endereço ("Documentos › www"); a barra só tem
            // os nomes de exibição, então o primeiro vira pasta conhecida (Documentos, unidade C:...) e o resto é descido
            string PastaInicial()
            {
                try
                {
                    var barra = Filho(caixa, "ToolbarWindow32", 1001);
                    var nomes = new List<string>();
                    if (barra != IntPtr.Zero)
                    {
                        // caminho comprido não cabe: as pastas de cima somem em "Todos os locais". Escondida, a caixa
                        // pode ficar bem larga por um instante para a barra mostrar tudo
                        Win32.RECT r; Win32.GetWindowRect(caixa, out r);
                        SetWindowPos(caixa, IntPtr.Zero, 0, 0, 6000, r.B - r.T, 0x0002 | 0x0004 | 0x0010);   // NOMOVE | NOZORDER | NOACTIVATE
                        Thread.Sleep(250);
                        var el = System.Windows.Automation.AutomationElement.FromHandle(barra);
                        foreach (System.Windows.Automation.AutomationElement e in el.FindAll(System.Windows.Automation.TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
                            nomes.Add(e.Current.Name);
                        SetWindowPos(caixa, IntPtr.Zero, 0, 0, r.R - r.L, r.B - r.T, 0x0002 | 0x0004 | 0x0010);
                    }
                    string p = Resolver(nomes);
                    Log("barra: " + string.Join(" › ", nomes) + " => " + (p ?? "?"));
                    if (p != null) return p;
                }
                catch (Exception e) { Log("pasta inicial: " + e.Message); }
                try { if (File.Exists(Ultima)) { string u = File.ReadAllText(Ultima).Trim(); if (Directory.Exists(u)) return u; } } catch { }
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            // nome de exibição do shell, o mesmo da barra de endereço (a raiz do OneDrive da empresa aparece como
            // "ROBERTO - ESTRUTURAL PADRÃO", não com o nome da pasta)
            [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IShellItem { void BindToHandler(); void GetParent(); void GetDisplayName(uint s, [MarshalAs(UnmanagedType.LPWStr)] out string n); }
            [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
            static extern void SHCreateItemFromParsingName(string p, IntPtr ctx, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IShellItem item);
            static string Exibicao(string p)
            {
                try { IShellItem i; SHCreateItemFromParsingName(p, IntPtr.Zero, typeof(IShellItem).GUID, out i); string n; i.GetDisplayName(0, out n); Marshal.ReleaseComObject(i); return n; }   // SIGDN_NORMALDISPLAY
                catch { return null; }
            }
            static bool Combina(string p, string nome)
            {
                return string.Equals(Path.GetFileName(p.TrimEnd('\\')), nome, StringComparison.OrdinalIgnoreCase) || string.Equals(Exibicao(p), nome, StringComparison.OrdinalIgnoreCase);
            }

            static string Resolver(List<string> nomes)
            {
                var raizes = new List<string>();
                foreach (var sf in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.UserProfile })
                    raizes.Add(Environment.GetFolderPath(sf));
                string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                raizes.Add(Path.Combine(perfil, "Downloads"));
                foreach (var v in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" }) { string o = Environment.GetEnvironmentVariable(v); if (!string.IsNullOrEmpty(o)) raizes.Add(o); }
                foreach (var d in DriveInfo.GetDrives()) raizes.Add(d.Name);
                try { raizes.AddRange(Directory.GetDirectories(perfil)); } catch { }
                string cur = null;
                foreach (var bruto in nomes)
                {
                    // a barra marca a direção do texto com caracteres invisíveis (U+200E...) antes de alguns nomes
                    string n = new string(bruto.Where(c => c != '‎' && c != '‏' && (c < '‪' || c > '‮')).ToArray()).Trim();
                    if (cur == null)
                    {
                        if (n.Length >= 4 && n.EndsWith(":)")) { string u = n.Substring(n.Length - 3, 2) + "\\"; if (Directory.Exists(u)) { cur = u; continue; } }   // "OS (C:)"
                        cur = raizes.FirstOrDefault(r => Directory.Exists(r) && string.Equals(Exibicao(r), n, StringComparison.OrdinalIgnoreCase));
                        continue;   // "Todos os locais", "Este Computador"... não são pastas: segue até achar a primeira
                    }
                    string direto = Path.Combine(cur, n);
                    if (Directory.Exists(direto)) { cur = direto; continue; }
                    string achada = null;
                    try { achada = Directory.EnumerateDirectories(cur).FirstOrDefault(d => Combina(d, n)); } catch { }
                    if (achada == null) break;   // não deu para descer: fica na mais funda que existe
                    cur = achada;
                }
                return cur;
            }
        }
    }
}
