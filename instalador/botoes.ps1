# Mac Skin: ao passar o mouse, minimizar fica com fundo amarelo e maximizar com fundo verde
# (o fechar já fica vermelho no Windows), cobrindo o botão inteiro como o vermelho.
# É uma camada transparente que deixa o clique passar: os botões continuam do Windows.
# Os botões são achados por UI Automation (vale também para Office, Edge e apps Electron,
# que desenham botões próprios). Rodar com powershell -STA.
$Dir = $PSScriptRoot
$LogArq = Join-Path $Dir 'botoes.log'
$mutex = New-Object Threading.Mutex($false, 'Local\MacSkinBotoes')
if (-not $mutex.WaitOne(0)) { return }   # já está aberto

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace MacSkin -Name Botoes -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
[DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
// área dos 3 botões desenhados pelo Windows (DWMWA_CAPTION_BUTTON_BOUNDS), relativa à janela
[DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int n);
public static RECT BotoesDwm(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 5, out r, 16) != 0) r = new RECT(); return r; }
// parte visível da janela na tela (sem a borda invisível de 8 px, que na janela maximizada fica fora da tela)
public static RECT Visivel(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r); return r; }
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
// janela principal sob o mouse (a camada é WS_EX_TRANSPARENT, então não atrapalha)
public static IntPtr SobMouse(POINT p) { return GetAncestor(WindowFromPoint(p), 2); }
// sempre no topo, sem ativar
public static void Mover(IntPtr h, int x, int y, int w, int hh) { SetWindowPos(h, new IntPtr(-1), x, y, w, hh, 0x0010); }
public static string Classe(IntPtr h) { var s = new System.Text.StringBuilder(128); GetClassName(h, s, 128); return s.ToString(); }
public static string Titulo(IntPtr h) { var s = new System.Text.StringBuilder(128); GetWindowText(h, s, 128); return s.ToString(); }
'@

function Log($m) { Add-Content $LogArq "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m" -ErrorAction SilentlyContinue }

$A = [Windows.Automation.AutomationElement]
$CT = [Windows.Automation.ControlType]
$condBotao = New-Object Windows.Automation.PropertyCondition $A::ControlTypeProperty, $CT::Button
$condFechar = New-Object Windows.Automation.AndCondition $condBotao, (New-Object Windows.Automation.OrCondition `
    (New-Object Windows.Automation.PropertyCondition $A::NameProperty, 'Fechar'),
    (New-Object Windows.Automation.PropertyCondition $A::NameProperty, 'Close'),
    (New-Object Windows.Automation.PropertyCondition $A::AutomationIdProperty, 'Close'))

# ---------- camada: fundo colorido do tamanho do botão + símbolo branco ----------
$cores = @{ 0 = '#FEBC2E'; 1 = '#28C840' }   # minimizar amarelo, maximizar verde (cores do macOS)
$simbolo = @{ 0 = 'M0,5 L10,5'; 1 = 'M0.5,0.5 H9.5 V9.5 H0.5 Z'; 2 = 'M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z' }   # 2 = restaurar

$win = New-Object Windows.Window -Property @{
    Title = 'Mac Skin Botoes'; WindowStyle = 'None'; AllowsTransparency = $true; ResizeMode = 'NoResize'
    Background = [Windows.Media.Brushes]::Transparent; Topmost = $true; ShowInTaskbar = $false; ShowActivated = $false
    Left = -10000; Top = -10000; Width = 10; Height = 10
}
$fundo = New-Object Windows.Controls.Grid
$desenho = New-Object Windows.Shapes.Path -Property @{ Width = 10; Height = 10; Stretch = 'None'; StrokeThickness = 1
    Stroke = [Windows.Media.Brushes]::White; HorizontalAlignment = 'Center'; VerticalAlignment = 'Center' }
[void]$fundo.Children.Add($desenho)
$win.Content = $fundo

# ---------- achar os botões da janela ----------
# posição relativa ao canto superior direito (os botões acompanham a borda direita)
function Medir($h, $r) { $m = Medir-Uia $h $r; if ($m) { $m } else { Medir-Dwm $h $r } }

# botões desenhados pelo Windows (Explorador de Arquivos e apps clássicos): não aparecem no UI Automation,
# mas o Windows informa a faixa dos três; cada um tem 1/3 dela
function Medir-Dwm($h, $r) {
    $c = [MacSkin.Botoes]::BotoesDwm($h)
    $v = [MacSkin.Botoes]::Visivel($h)
    # a faixa vem relativa à janela inteira (com a borda invisível): passa para a tela e recorta no visível,
    # senão o fundo colorido sobe 8 px na janela maximizada e o símbolo não cai em cima do original
    # Medido pixel a pixel contra o vermelho do fechar (Explorador de Arquivos, 100%):
    # - a largura informada inclui a borda invisível (sobra à esquerda: 8 px normal, 9 maximizada); os 3 botões
    #   ocupam o resto, encostados na borda direita visível
    # - normal: 1 borda fina em cima; maximizada: a faixa vem deslocada pela borda invisível para cima
    #   e o botão tem 2 bordas finas a menos de altura
    $borda = [Math]::Max(0, $v.L - $r.L); $fino = [MacSkin.Botoes]::GetSystemMetrics(6)
    $max = [MacSkin.Botoes]::IsZoomed($h)
    $ox = $(if ($max) { $borda } else { 0 })
    $dir = [Math]::Min($r.L + $ox + $c.R, $v.R)
    $l = [Math]::Round(($c.R - $c.L - $borda) / 3)
    $esq = $dir - 3 * $l
    if ($max) { $topo = $r.T + $borda + $c.T; $base = $r.T + $borda + $c.B - 2 * $fino }
    else { $topo = $r.T + $c.T + $fino; $base = $r.T + $c.B }
    $topo = [Math]::Max($topo, $v.T); $base = [Math]::Min($base, $v.B)
    if ($l -lt 20 -or ($base - $topo) -lt 15) { return $null }
    $out = for ($i = 0; $i -lt 2; $i++) {
        [pscustomobject]@{ Tipo = $i; DxDireita = $r.R - ($esq + $i * $l); DyTopo = $topo - $r.T; L = $l; A = $base - $topo }
    }
    return , @($out)
}

# botões desenhados pelo próprio app (Edge, Office, Electron): o UI Automation acha o "Fechar" e os vizinhos
function Medir-Uia($h, $r) {
    try {
        $fechar = $A::FromHandle($h).FindFirst('Descendants', $condFechar)
        if (-not $fechar -or $fechar.Current.IsOffscreen) { return $null }
        $fb = $fechar.Current.BoundingRectangle
        $pai = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($fechar)
        $linha = @($pai.FindAll('Children', $condBotao) | ForEach-Object { $_.Current.BoundingRectangle } |
            Where-Object { [Math]::Abs($_.Y - $fb.Y) -lt 4 -and [Math]::Abs($_.Height - $fb.Height) -lt 4 -and $_.X -lt $fb.X - 4 } |
            Sort-Object X | Select-Object -Last 2)
        if ($linha.Count -ne 2) { return $null }   # caixa de diálogo: só tem o fechar
        $out = for ($i = 0; $i -lt 2; $i++) {
            $b = $linha[$i]
            [pscustomobject]@{ Tipo = $i; DxDireita = $r.R - $b.X; DyTopo = $b.Y - $r.T; L = $b.Width; A = $b.Height }
        }
        return , @($out)
    } catch { return $null }
}

$script:alvo = [IntPtr]::Zero; $script:tam = ''; $script:botoes = $null; $script:visivel = $false
function Esconder { if ($script:visivel) { $win.Visibility = 'Hidden'; $script:visivel = $false } }

$timer = New-Object Windows.Threading.DispatcherTimer -Property @{ Interval = [TimeSpan]::FromMilliseconds(30) }
$timer.Add_Tick({
    try {
        $p = New-Object MacSkin.Botoes+POINT
        [void][MacSkin.Botoes]::GetCursorPos([ref]$p)
        $h = [MacSkin.Botoes]::SobMouse($p)
        if ($h -eq [IntPtr]::Zero -or $h -eq $script:eu) { Esconder; return }
        $r = New-Object MacSkin.Botoes+RECT
        [void][MacSkin.Botoes]::GetWindowRect($h, [ref]$r)
        # só mede (UI Automation, ~0,1 s) com o mouse no canto de cima à direita da janela
        if ($p.Y -gt $r.T + 80 -or $p.X -lt $r.R - 260) { Esconder; return }
        if ([MacSkin.Botoes]::Classe($h) -in 'Progman', 'WorkerW', 'Shell_TrayWnd' -or [MacSkin.Botoes]::Titulo($h) -like 'Mac Skin*') { Esconder; return }
        $tam = "$($r.R - $r.L)x$($r.B - $r.T)"
        if ($h -ne $script:alvo -or $tam -ne $script:tam) {   # janela nova ou mudou de tamanho: medir de novo
            $script:alvo = $h; $script:tam = $tam
            $script:botoes = Medir $h $r
        }
        $sobre = $null
        foreach ($b in $script:botoes) {
            $x = $r.R - $b.DxDireita; $y = $r.T + $b.DyTopo
            if ($p.X -ge $x -and $p.X -lt $x + $b.L -and $p.Y -ge $y -and $p.Y -lt $y + $b.A) { $sobre = $b; $bx = $x; $by = $y }
        }
        if (-not $sobre) { Esconder; return }
        $fundo.Background = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($cores[$sobre.Tipo]))
        $s = if ($sobre.Tipo -eq 1 -and [MacSkin.Botoes]::IsZoomed($h)) { 2 } else { $sobre.Tipo }
        $desenho.Data = [Windows.Media.Geometry]::Parse($simbolo[$s])
        [MacSkin.Botoes]::Mover($script:eu, [int]$bx, [int]$by, [int]$sobre.L, [int]$sobre.A)
        if (-not $script:visivel) { $win.Visibility = 'Visible'; $script:visivel = $true }
    } catch { Log "erro: $($_.Exception.Message)" }
})

$win.Add_SourceInitialized({
    $script:eu = (New-Object Windows.Interop.WindowInteropHelper $win).Handle
    $ex = [MacSkin.Botoes]::GetWindowLong($script:eu, -20)
    [void][MacSkin.Botoes]::SetWindowLong($script:eu, -20, ($ex -bor 0x80 -bor 0x20 -bor 0x08000000))   # fora do Alt+Tab, clique passa, sem foco
    $win.Visibility = 'Hidden'
    $timer.Start()
})

# erro inesperado vai para o log em vez de fechar (se fechar mesmo assim, o vigia reabre)
$win.Dispatcher.Add_UnhandledException({ param($s, $e) Log "falha: $($e.Exception.Message)"; $e.Handled = $true })
Log 'botões abertos'
# Show + Dispatcher.Run (não ShowDialog: esconder uma janela de diálogo encerra o ShowDialog e o programa)
$win.Add_Closed({ $win.Dispatcher.InvokeShutdown() })
try { $win.Show(); [Windows.Threading.Dispatcher]::Run() } catch { Log "erro: $($_.Exception.Message)" }
$mutex.ReleaseMutex()
