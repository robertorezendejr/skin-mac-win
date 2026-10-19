# Mac Skin: barra de menus no alto da tela, como no macOS. Com a barra do Windows oculta pela dock,
# é aqui que ficam bateria, Wi-Fi, volume e relógio. À esquerda: menu do sistema e o app ativo.
# Reserva a faixa de cima (AppBar), vidro acrylic, some em tela cheia. Rodar com powershell -STA.
$Dir = $PSScriptRoot
$LogArq = Join-Path $Dir 'barra.log'
$mutex = New-Object Threading.Mutex($false, 'Local\MacSkinBarra')
if (-not $mutex.WaitOne(0)) { return }   # já está aberta

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms
Add-Type -Namespace MacSkin -Name Barra -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public int msg; public int edge; public RECT rc; public IntPtr lParam; }
[DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(int m, ref APPBARDATA d);
[StructLayout(LayoutKind.Sequential)] struct ACCENT { public int State; public int Flags; public int Color; public int Anim; }
[StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }
[DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
[DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
// reserva a faixa de cima para as janelas maximizadas começarem abaixo da barra
public static void Reservar(IntPtr h, int largura, int faixa) {
    var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = h; d.edge = 1;
    SHAppBarMessage(0, ref d);
    d.rc.L = 0; d.rc.R = largura; d.rc.T = 0; d.rc.B = faixa;
    SHAppBarMessage(2, ref d); d.rc.B = d.rc.T + faixa; SHAppBarMessage(3, ref d);
}
public static void Liberar(IntPtr h) { var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = h; SHAppBarMessage(1, ref d); }
public static void Vidro(IntPtr h, int abgr) {
    var a = new ACCENT(); a.State = 4; a.Flags = 2; a.Color = abgr;
    int n = Marshal.SizeOf(a); IntPtr p = Marshal.AllocHGlobal(n); Marshal.StructureToPtr(a, p, false);
    var d = new WCAD(); d.Attr = 19; d.Data = p; d.Size = n; SetWindowCompositionAttribute(h, ref d); Marshal.FreeHGlobal(p);
    int semBorda = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(h, 34, ref semBorda, 4);
    int quadrado = 1; DwmSetWindowAttribute(h, 33, ref quadrado, 4);
}
// Win + tecla (A = configurações rápidas, N = notificações e calendário)
public static void Win(byte tecla) {
    keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(tecla, 0, 0, UIntPtr.Zero);
    keybd_event(tecla, 0, 2, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero);
}
public static string Classe(IntPtr h) { var s = new System.Text.StringBuilder(128); GetClassName(h, s, 128); return s.ToString(); }
public static string Titulo(IntPtr h) { var s = new System.Text.StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
public static bool TelaCheia(IntPtr eu, int w, int h) {
    var f = GetForegroundWindow(); if (f == IntPtr.Zero || f == eu) return false;
    var n = Classe(f); if (n == "Progman" || n == "WorkerW" || n == "Shell_TrayWnd") return false;
    RECT r; GetWindowRect(f, out r); return r.L <= 0 && r.T <= 0 && r.R >= w && r.B >= h;
}
'@

function Log($m) { Add-Content $LogArq "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m" -ErrorAction SilentlyContinue }
function Pincel($hex) { New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($hex)) }

# ---------- medidas e fontes ----------
$Altura = 28
$Icones = New-Object Windows.Media.FontFamily 'Segoe Fluent Icons, Segoe MDL2 Assets'
$Texto = New-Object Windows.Media.FontFamily 'Inter, Segoe UI'
$ptbr = [Globalization.CultureInfo]'pt-BR'

# ---------- janela ----------
# como no macOS Golden Gate: sem fundo, itens brancos direto sobre o papel de parede, com sombra leve
$win = New-Object Windows.Window -Property @{
    Title = 'Mac Skin Barra'; WindowStyle = 'None'; AllowsTransparency = $true; ResizeMode = 'NoResize'; ShowInTaskbar = $false; Topmost = $true
    Background = [Windows.Media.Brushes]::Transparent; ShowActivated = $false
    Left = 0; Top = 0; Width = [Windows.SystemParameters]::PrimaryScreenWidth; Height = $Altura
}
$raiz = New-Object Windows.Controls.Grid -Property @{
    Effect = (New-Object Windows.Media.Effects.DropShadowEffect -Property @{ BlurRadius = 6; ShadowDepth = 0; Opacity = 0.45 }) }
$win.Content = $raiz
$Branco = Pincel '#FFFFFFFF'
$Clicavel = Pincel '#01FFFFFF'   # quase invisível: sem isso o clique atravessa a barra transparente
$esq = New-Object Windows.Controls.StackPanel -Property @{ Orientation = 'Horizontal'; HorizontalAlignment = 'Left'; Margin = '10,0,0,0' }
$dir = New-Object Windows.Controls.StackPanel -Property @{ Orientation = 'Horizontal'; HorizontalAlignment = 'Right'; Margin = '0,0,8,0' }
[void]$raiz.Children.Add($esq); [void]$raiz.Children.Add($dir)
$script:textos = New-Object Collections.ArrayList   # tudo que muda de cor com o tema

# item clicável: fundo arredondado ao passar o mouse, como os itens da barra do Mac
function Item($painel, $conteudo, $acao) {
    $b = New-Object Windows.Controls.Border -Property @{ Child = $conteudo; Padding = '8,0'; Margin = '1,3'; CornerRadius = 5
        Background = $Clicavel; VerticalAlignment = 'Stretch' }
    $b.Add_MouseEnter({ param($s) $s.Background = Pincel '#33FFFFFF' })
    $b.Add_MouseLeave({ param($s) $s.Background = $Clicavel })
    if ($acao) { $b.Add_MouseLeftButtonUp($acao) }
    [void]$painel.Children.Add($b)
    $b
}
function Rotulo($txt, $peso = 'Normal') {
    New-Object Windows.Controls.TextBlock -Property @{ Text = $txt; FontFamily = $Texto; FontSize = 13; FontWeight = $peso; VerticalAlignment = 'Center'; Foreground = $Branco }
}
function Glifo($cod, $tam = 15) {
    New-Object Windows.Controls.TextBlock -Property @{ Text = [string][char]$cod; FontFamily = $Icones; FontSize = $tam; VerticalAlignment = 'Center'; Foreground = $Branco }
}
# desenho em traço branco (ícones que a fonte do Windows não tem igual ao Mac)
function Desenho($geo, $l, $a, $preenchido = $false) {
    $p = New-Object Windows.Shapes.Path -Property @{ Data = [Windows.Media.Geometry]::Parse($geo); Width = $l; Height = $a; Stretch = 'Uniform'
        VerticalAlignment = 'Center'; StrokeThickness = 1.5; Stroke = $Branco }
    if ($preenchido) { $p.Fill = $Branco }
    $p
}

# ---------- esquerda: logo (menu do sistema) e app ativo ----------
$logo = New-Object Windows.Shapes.Path -Property @{ Width = 13; Height = 13; Stretch = 'Uniform'; VerticalAlignment = 'Center'; Fill = $Branco
    Data = [Windows.Media.Geometry]::Parse('M0,0 H6 V6 H0 Z M7,0 H13 V6 H7 Z M0,7 H6 V13 H0 Z M7,7 H13 V13 H7 Z') }
$menuSis = New-Object Windows.Controls.ContextMenu
function Opcao($nome, $acao) {
    if (-not $nome) { [void]$menuSis.Items.Add((New-Object Windows.Controls.Separator)); return }
    $mi = New-Object Windows.Controls.MenuItem -Property @{ Header = $nome }
    $mi.Add_Click($acao); [void]$menuSis.Items.Add($mi)
}
function Confirmar($msg) { [Windows.MessageBox]::Show($msg, 'Mac Skin', 'YesNo', 'Question') -eq 'Yes' }
Opcao 'Sobre este computador' { Start-Process 'ms-settings:about' }
Opcao $null
Opcao 'Configurações do Sistema…' { Start-Process 'ms-settings:' }
Opcao 'App Store…' { Start-Process 'ms-windows-store:' }
Opcao $null
Opcao 'Forçar Encerrar…' { Start-Process taskmgr.exe }
Opcao $null
Opcao 'Suspender' { [void][Windows.Forms.Application]::SetSuspendState('Suspend', $false, $false) }
Opcao 'Reiniciar…' { if (Confirmar 'Reiniciar o computador agora?') { shutdown.exe /r /t 0 } }
Opcao 'Desligar…' { if (Confirmar 'Desligar o computador agora?') { shutdown.exe /s /t 0 } }
Opcao $null
Opcao 'Bloquear Tela' { rundll32.exe 'user32.dll,LockWorkStation' }
Opcao "Encerrar Sessão de $env:USERNAME…" { if (Confirmar 'Encerrar a sessão agora? Apps abertos serão fechados.') { shutdown.exe /l } }
# a barra não pega foco; para clicar fora fechar o menu, ela pega o foco enquanto o menu está aberto
$menuSis.Add_Opened({ [void][MacSkin.Barra]::SetForegroundWindow($script:hwnd) })
$win.Add_Deactivated({ $menuSis.IsOpen = $false })
$itemLogo = Item $esq $logo {
    param($s)
    $menuSis.PlacementTarget = $s; $menuSis.Placement = 'Bottom'; $menuSis.IsOpen = $true
}
$appAtivo = Rotulo '' 'SemiBold'
[void](Item $esq $appAtivo $null)

# ---------- direita (na ordem do Mac): volume, Wi-Fi, bateria, Bluetooth, Central de Controle, relógio ----------
$vol = Glifo 0xE767
[void](Item $dir $vol { [MacSkin.Barra]::Win(0x41) })
$wifi = Glifo 0xE701
[void](Item $dir $wifi { [MacSkin.Barra]::Win(0x41) })
# bateria do Mac: pílula branca que enche com a carga, número dentro e o "pino" à direita
$batCorpo = New-Object Windows.Controls.Grid -Property @{ Width = 25; Height = 12; VerticalAlignment = 'Center' }
$batFundo = New-Object Windows.Controls.Border -Property @{ CornerRadius = 3.5; Background = (Pincel '#59FFFFFF') }
$batNivel = New-Object Windows.Controls.Border -Property @{ CornerRadius = 3.5; Background = $Branco; HorizontalAlignment = 'Left' }
$batTxt = New-Object Windows.Controls.TextBlock -Property @{ FontFamily = $Texto; FontSize = 9; FontWeight = 'Bold'
    HorizontalAlignment = 'Center'; VerticalAlignment = 'Center'; Foreground = (Pincel '#E6000000') }
[void]$batCorpo.Children.Add($batFundo); [void]$batCorpo.Children.Add($batNivel); [void]$batCorpo.Children.Add($batTxt)
$bat = New-Object Windows.Controls.StackPanel -Property @{ Orientation = 'Horizontal' }
$batRaio = Desenho 'M6,0 L0,8 H4 L3,14 L9,5.5 H5 Z' 7 11 $true; $batRaio.StrokeThickness = 0; $batRaio.Margin = '0,0,4,0'   # raio quando está carregando
[void]$bat.Children.Add($batRaio); [void]$bat.Children.Add($batCorpo)
[void]$bat.Children.Add((New-Object Windows.Controls.Border -Property @{ Width = 1.5; Height = 4; Margin = '1,0,0,0'; CornerRadius = 1; Background = (Pincel '#80FFFFFF'); VerticalAlignment = 'Center' }))
$itemBat = Item $dir $bat { Start-Process 'ms-settings:batterysaver' }
[void](Item $dir (Glifo 0xE702 14) { Start-Process 'ms-settings:bluetooth' })
# Central de Controle: dois interruptores
[void](Item $dir (Desenho 'M3,0.75 H11 A2.25,2.25 0 0 1 11,5.25 H3 A2.25,2.25 0 0 1 3,0.75 Z M3,7.75 H11 A2.25,2.25 0 0 1 11,12.25 H3 A2.25,2.25 0 0 1 3,7.75 Z M3,1.5 A1.5,1.5 0 1 0 3.01,1.5 Z M11,8.5 A1.5,1.5 0 1 0 11.01,8.5 Z' 15 13) { [MacSkin.Barra]::Win(0x41) })
$relogio = Rotulo '' 'Medium'
[void](Item $dir $relogio { [MacSkin.Barra]::Win(0x4E) })

# ---------- atualização ----------
$nomes = @{}   # pid -> nome do app (FileDescription), para não ler o executável toda hora
function Nome-App($h) {
    $c = [MacSkin.Barra]::Classe($h)
    if ($c -in 'Progman', 'WorkerW', 'Shell_TrayWnd') { return 'Explorador de Arquivos' }
    $id = 0; [void][MacSkin.Barra]::GetWindowThreadProcessId($h, [ref]$id)
    if ($nomes.ContainsKey($id)) { return $nomes[$id] }
    $n = try {
        $p = Get-Process -Id $id
        if ($p.ProcessName -eq 'ApplicationFrameHost') { ([MacSkin.Barra]::Titulo($h) -split ' [-–] ')[-1] }   # apps da Store
        elseif ($p.ProcessName -eq 'explorer') { 'Explorador de Arquivos' }
        else { $d = $p.MainModule.FileVersionInfo.FileDescription; if ($d) { $d } else { $p.ProcessName } }
    } catch { '' }
    if ($c -ne 'ApplicationFrameHost') { $nomes[$id] = $n }
    $n
}

function Atualizar-Rapido {   # 1x por segundo: relógio, app ativo, bateria, tela cheia
    try {
        $agora = Get-Date
        $t = $agora.ToString("ddd'.' d 'de' MMM'.'  HH:mm", $ptbr)
        $relogio.Text = $t.Substring(0, 1).ToUpper() + $t.Substring(1)   # "Seg. 19 de out.  17:10"
        $h = [MacSkin.Barra]::GetForegroundWindow()
        if ($h -ne $script:hwnd -and [MacSkin.Barra]::Titulo($h) -notlike 'Mac Skin*') { $appAtivo.Text = Nome-App $h }

        $ps = [Windows.Forms.SystemInformation]::PowerStatus
        if ($ps.BatteryChargeStatus -band 128) { $itemBat.Visibility = 'Collapsed' }   # sem bateria (desktop)
        else {
            $pct = [int][Math]::Round($ps.BatteryLifePercent * 100)
            $batTxt.Text = "$pct"
            $batNivel.Width = [Math]::Max(3, 25 * $pct / 100)
            $batRaio.Visibility = $(if ($ps.PowerLineStatus -eq 'Online') { 'Visible' } else { 'Collapsed' })
        }
        $win.Visibility = $(if ([MacSkin.Barra]::TelaCheia($script:hwnd, $script:telaW, $script:telaH)) { 'Hidden' } else { 'Visible' })
    } catch { Log "rápido: $($_.Exception.Message)" }
}

function Atualizar-Lento {   # a cada 10 s: Wi-Fi
    try {
        $w = netsh.exe wlan show interfaces 2>$null
        $sinal = ($w | Select-String '^\s*(Sinal|Signal)\s*:\s*(\d+)%' | Select-Object -First 1)
        if ($sinal) {
            $pct = [int]$sinal.Matches[0].Groups[2].Value
            $wifi.Text = [string][char]$(if ($pct -ge 75) { 0xE701 } elseif ($pct -ge 50) { 0xE874 } elseif ($pct -ge 25) { 0xE873 } else { 0xE872 })
        } elseif ([Net.NetworkInformation.NetworkInterface]::GetIsNetworkAvailable()) { $wifi.Text = [string][char]0xE839 }   # cabo
        else { $wifi.Text = [string][char]0xF384 }   # sem rede
    } catch { Log "lento: $($_.Exception.Message)" }
}

# ---------- ciclo de vida ----------
$win.Add_SourceInitialized({
    $script:hwnd = (New-Object Windows.Interop.WindowInteropHelper $win).Handle
    $ex = [MacSkin.Barra]::GetWindowLong($script:hwnd, -20)
    [void][MacSkin.Barra]::SetWindowLong($script:hwnd, -20, ($ex -bor 0x80 -bor 0x08000000))   # fora do Alt+Tab, não rouba o foco
    $f = [Windows.PresentationSource]::FromVisual($win).CompositionTarget.TransformToDevice.M22
    $script:telaW = [int]([Windows.SystemParameters]::PrimaryScreenWidth * $f)
    $script:telaH = [int]([Windows.SystemParameters]::PrimaryScreenHeight * $f)
    [MacSkin.Barra]::Reservar($script:hwnd, $script:telaW, [int]($Altura * $f))
    $win.Top = 0; $win.Left = 0
    Atualizar-Lento; Atualizar-Rapido
})
$win.Add_Closed({ [MacSkin.Barra]::Liberar($script:hwnd) })
$t1 = New-Object Windows.Threading.DispatcherTimer -Property @{ Interval = [TimeSpan]::FromSeconds(1) }
$t1.Add_Tick({ Atualizar-Rapido }); $t1.Start()
$t2 = New-Object Windows.Threading.DispatcherTimer -Property @{ Interval = [TimeSpan]::FromSeconds(10) }
$t2.Add_Tick({ Atualizar-Lento }); $t2.Start()

# erro inesperado vai para o log em vez de fechar a barra (se fechar mesmo assim, o vigia reabre)
$win.Dispatcher.Add_UnhandledException({ param($s, $e) Log "falha: $($e.Exception.Message)"; $e.Handled = $true })
Log 'barra aberta'
# Show + Dispatcher.Run (não ShowDialog: esconder uma janela de diálogo, como na tela cheia, encerra o programa)
$win.Add_Closed({ $win.Dispatcher.InvokeShutdown() })
try { $win.Show(); [Windows.Threading.Dispatcher]::Run() } catch { Log "erro: $($_.Exception.Message)" }
$mutex.ReleaseMutex()
