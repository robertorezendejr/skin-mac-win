# Mac Skin: bolinhas vermelha/amarela/verde do macOS por cima dos botões fechar/minimizar/maximizar
# da janela ativa. É uma camada transparente que deixa o clique passar: os botões do Windows continuam
# no mesmo lugar e funcionando. Os botões são achados por UI Automation (vale também para Office,
# Edge e apps Electron, que desenham botões próprios). Rodar com powershell -STA.
$Dir = $PSScriptRoot
$LogArq = Join-Path $Dir 'botoes.log'
$mutex = New-Object Threading.Mutex($false, 'Local\MacSkinBotoes')
if (-not $mutex.WaitOne(0)) { return }   # já está aberto

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace MacSkin -Name Botoes -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
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

# ---------- bolinhas (cores do macOS) ----------
$D = 12   # diâmetro em DIPs
$cores = @(   # minimizar, maximizar, fechar: preenchimento, borda, símbolo
    @('#FEBC2E', '#DEA123', '#985712'), @('#28C840', '#1AAB29', '#0B650D'), @('#FF5F57', '#E0443E', '#4D0000'))
$simbolos = @('M3,6 L9,6', 'M6,3 L6,9 M3,6 L9,6', 'M3.5,3.5 L8.5,8.5 M8.5,3.5 L3.5,8.5')

$win = New-Object Windows.Window -Property @{
    Title = 'Mac Skin Botoes'; WindowStyle = 'None'; AllowsTransparency = $true; ResizeMode = 'NoResize'
    Background = [Windows.Media.Brushes]::Transparent; Topmost = $true; ShowInTaskbar = $false; ShowActivated = $false
    Left = -10000; Top = -10000; Width = 10; Height = 10
}
$tela = New-Object Windows.Controls.Canvas
$win.Content = $tela
$bolas = for ($i = 0; $i -lt 3; $i++) {
    $g = New-Object Windows.Controls.Grid -Property @{ Width = $D; Height = $D; Visibility = 'Hidden' }
    $cv = [Windows.Media.ColorConverter]
    [void]$g.Children.Add((New-Object Windows.Shapes.Ellipse -Property @{
        Fill = New-Object Windows.Media.SolidColorBrush ($cv::ConvertFromString($cores[$i][0]))
        Stroke = New-Object Windows.Media.SolidColorBrush ($cv::ConvertFromString($cores[$i][1])); StrokeThickness = 0.6 }))
    $s = New-Object Windows.Shapes.Path -Property @{ Data = [Windows.Media.Geometry]::Parse($simbolos[$i]); StrokeThickness = 1.3
        Stroke = New-Object Windows.Media.SolidColorBrush ($cv::ConvertFromString($cores[$i][2])); Visibility = 'Hidden'
        StrokeStartLineCap = 'Round'; StrokeEndLineCap = 'Round' }
    [void]$g.Children.Add($s)
    [void]$tela.Children.Add($g)
    [pscustomobject]@{ Caixa = $g; Simbolo = $s }
}

# ---------- achar os botões da janela ----------
# guarda a posição relativa ao canto superior direito (os botões acompanham a borda direita)
function Medir($h, $r) {
    try {
        $el = $A::FromHandle($h)
        $fechar = $el.FindFirst('Descendants', $condFechar)
        if (-not $fechar -or $fechar.Current.IsOffscreen) { return $null }
        $fb = $fechar.Current.BoundingRectangle
        $pai = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($fechar)
        $linha = @($pai.FindAll('Children', $condBotao) | ForEach-Object { $_.Current.BoundingRectangle } |
            Where-Object { [Math]::Abs($_.Y - $fb.Y) -lt 4 -and [Math]::Abs($_.Height - $fb.Height) -lt 4 -and $_.X -le $fb.X } |
            Sort-Object X | Select-Object -Last 3)
        # 3 botões: minimizar, maximizar, fechar; menos que isso (caixas de diálogo): só o fechar
        $ordem = if ($linha.Count -eq 3) { 0, 1, 2 } else { $linha = @($fb); , 2 }
        $out = for ($i = 0; $i -lt $linha.Count; $i++) {
            $b = $linha[$i]
            [pscustomobject]@{ Tipo = $ordem[$i]; DxDireita = $r.R - ($b.X + $b.Width / 2); DyTopo = ($b.Y + $b.Height / 2) - $r.T; L = $b.Width; A = $b.Height }
        }
        return , @($out)
    } catch { return $null }
}

$script:alvo = [IntPtr]::Zero; $script:tam = ''; $script:botoes = $null
function Esconder { $win.Visibility = 'Hidden'; $script:visivel = $false }

$timer = New-Object Windows.Threading.DispatcherTimer -Property @{ Interval = [TimeSpan]::FromMilliseconds(60) }
$timer.Add_Tick({
    try {
        $h = [MacSkin.Botoes]::GetForegroundWindow()
        if ($h -eq [IntPtr]::Zero -or $h -eq $script:eu -or [MacSkin.Botoes]::IsIconic($h)) { Esconder; return }
        $c = [MacSkin.Botoes]::Classe($h)
        if ($c -in 'Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd' -or [MacSkin.Botoes]::Titulo($h) -like 'Mac Skin*') { Esconder; return }
        $r = New-Object MacSkin.Botoes+RECT
        [void][MacSkin.Botoes]::GetWindowRect($h, [ref]$r)
        $tam = "$($r.R - $r.L)x$($r.B - $r.T)"
        if ($h -ne $script:alvo -or $tam -ne $script:tam) {   # janela nova ou mudou de tamanho: medir de novo
            $script:alvo = $h; $script:tam = $tam
            $script:botoes = Medir $h $r
        }
        if (-not $script:botoes) { Esconder; return }

        # janela da camada: cobre a faixa dos botões; coordenadas da tela vêm em pixels, o desenho em DIPs
        $f = $script:f
        $xs = $script:botoes | ForEach-Object { $r.R - $_.DxDireita }
        $x0 = [int](($xs | Measure-Object -Minimum).Minimum - 40); $x1 = [int](($xs | Measure-Object -Maximum).Maximum + 40)
        $yc = $r.T + $script:botoes[0].DyTopo
        $y0 = [int]($yc - 30)
        [MacSkin.Botoes]::Mover($script:eu, $x0, $y0, $x1 - $x0, 60)
        $p = New-Object MacSkin.Botoes+POINT
        [void][MacSkin.Botoes]::GetCursorPos([ref]$p)
        $sobre = $p.X -ge ($x0 + 15) -and $p.X -le ($x1 - 15) -and $p.Y -ge ($y0 + 10) -and $p.Y -le ($y0 + 50)
        foreach ($b in $bolas) { $b.Caixa.Visibility = 'Hidden' }
        foreach ($bt in $script:botoes) {
            $bola = $bolas[$bt.Tipo]
            [Windows.Controls.Canvas]::SetLeft($bola.Caixa, (($r.R - $bt.DxDireita) - $x0) / $f - $D / 2)
            [Windows.Controls.Canvas]::SetTop($bola.Caixa, ($yc - $y0) / $f - $D / 2)
            $bola.Caixa.Visibility = 'Visible'
            $bola.Simbolo.Visibility = $(if ($sobre) { 'Visible' } else { 'Hidden' })
        }
        if (-not $script:visivel) { $win.Visibility = 'Visible'; $script:visivel = $true }
    } catch { Log "erro: $($_.Exception.Message)" }
})

$win.Add_SourceInitialized({
    $script:eu = (New-Object Windows.Interop.WindowInteropHelper $win).Handle
    $ex = [MacSkin.Botoes]::GetWindowLong($script:eu, -20)
    [void][MacSkin.Botoes]::SetWindowLong($script:eu, -20, ($ex -bor 0x80 -bor 0x20 -bor 0x08000000))   # fora do Alt+Tab, clique passa, sem foco
    $script:f = [Windows.PresentationSource]::FromVisual($win).CompositionTarget.TransformToDevice.M22
    $timer.Start()
})

Log 'botões abertos'
try { [void]$win.ShowDialog() } catch { Log "erro: $($_.Exception.Message)" }
$mutex.ReleaseMutex()
