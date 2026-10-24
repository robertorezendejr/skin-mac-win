# Mac Skin Dock: dock no estilo macOS (WPF), sem admin. Rodar com powershell -STA.
# Itens = atalhos em .\dock (ordem pelo nome). Reserva a borda de baixo (AppBar), aumenta os ícones
# sob o mouse, pula ao abrir, marca os apps abertos e some quando um app está em tela cheia.
$Dir = $PSScriptRoot
$LogArq = Join-Path $Dir 'dock.log'
$mutex = New-Object Threading.Mutex($false, 'Local\MacSkinDock')
if (-not $mutex.WaitOne(0)) { return }   # já está aberta

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing
Add-Type -Namespace MacSkin -Name Dock -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public int msg; public int edge; public RECT rc; public IntPtr lParam; }
[DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(int m, ref APPBARDATA d);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
// reserva uma faixa na borda de baixo para as janelas maximizadas não ficarem atrás da dock
public static void Reservar(IntPtr h, int largura, int alturaTela, int faixa) {
    var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = h; d.edge = 3;
    SHAppBarMessage(0, ref d);
    d.rc.L = 0; d.rc.R = largura; d.rc.B = alturaTela; d.rc.T = alturaTela - faixa;
    SHAppBarMessage(2, ref d); d.rc.T = d.rc.B - faixa; SHAppBarMessage(3, ref d);
}
public static void Liberar(IntPtr h) { var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = h; SHAppBarMessage(1, ref d); }
// Liquid Glass: desfoque acrylic do Windows atrás da janela, tingido (cor AABBGGRR). O acrylic ignora
// recorte (SetWindowRgn), mas respeita o canto redondo nativo do Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE).
[StructLayout(LayoutKind.Sequential)] struct ACCENT { public int State; public int Flags; public int Color; public int Anim; }
[StructLayout(LayoutKind.Sequential)] struct WCAD { public int Attr; public IntPtr Data; public int Size; }
[DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);
[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr depois, int x, int y, int w, int hh, uint f);
[DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int n);
public static void Vidro(IntPtr h, int abgr) {
    var a = new ACCENT(); a.State = 4; a.Flags = 2; a.Color = abgr;
    int n = Marshal.SizeOf(a); IntPtr p = Marshal.AllocHGlobal(n); Marshal.StructureToPtr(a, p, false);
    var d = new WCAD(); d.Attr = 19; d.Data = p; d.Size = n; SetWindowCompositionAttribute(h, ref d); Marshal.FreeHGlobal(p);
    int redondo = 2; DwmSetWindowAttribute(h, 33, ref redondo, 4);
    int semBorda = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(h, 34, ref semBorda, 4);   // sem a borda de 1 px do Windows 11
}
public static void Posicionar(IntPtr h, int x, int y, int w, int hh) { SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 0x0010 | 0x0004); }
// barra do Windows: oculta de vez (senão ela sobe por cima da dock quando o mouse encosta embaixo)
[DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr pai, IntPtr depois, string classe, string titulo);
[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
public static void Barra(bool mostrar) {
    foreach (var c in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }) {
        IntPtr h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, c, null)) != IntPtr.Zero)
            if (IsWindowVisible(h) != mostrar) ShowWindow(h, mostrar ? 5 : 0);
    }
}
public static bool TelaCheia(IntPtr eu, int w, int h) {
    var f = GetForegroundWindow(); if (f == IntPtr.Zero || f == eu) return false;
    var c = new System.Text.StringBuilder(64); GetClassName(f, c, 64);
    var n = c.ToString(); if (n == "Progman" || n == "WorkerW" || n == "Shell_TrayWnd") return false;
    RECT r; GetWindowRect(f, out r); return r.L <= 0 && r.T <= 0 && r.R >= w && r.B >= h;
}
'@

function Log($m) { Add-Content $LogArq "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m" -ErrorAction SilentlyContinue }

# ---------- medidas (em DIPs) ----------
$Base = 52; $Gap = 6; $Pad = 8; $Margem = 6; $Max = 1.7; $Raio = 22
$Sigma = ($Base + $Gap) * 1.1
$Altura = $Base + 2 * $Pad
$AlturaJanela = [Math]::Ceiling($Base * $Max) + 2 * $Pad + $Margem + 48

function Pincel($hex) { New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($hex)) }

function Carregar-Icone($arq) {
    try {
        if ($arq -like '*.ico' -and (Test-Path $arq)) {
            $d = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri]$arq, 'None', 'OnLoad')
            return ($d.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1)
        }
        $i = [Drawing.Icon]::ExtractAssociatedIcon($arq)
        return [Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon($i.Handle, [Windows.Int32Rect]::Empty, [Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
    } catch { Log "ícone: $arq ($($_.Exception.Message))"; return $null }
}

# ---------- itens ----------
$sh = New-Object -ComObject WScript.Shell
$apps = foreach ($f in Get-ChildItem (Join-Path $Dir 'dock') -Filter *.lnk -ErrorAction SilentlyContinue | Sort-Object Name) {
    $l = $sh.CreateShortcut($f.FullName)
    $ico = [Environment]::ExpandEnvironmentVariables(($l.IconLocation -replace ',-?\d+$', ''))
    if (-not $ico) { $ico = if ($l.TargetPath) { $l.TargetPath } else { $f.FullName } }
    # o instalador grava o processo do app na Descrição (os atalhos abrem por shell:AppsFolder via explorer)
    $exe = if ($l.Arguments -like 'shell:AppsFolder*') { $l.Description }
           elseif ($l.TargetPath) { [IO.Path]::GetFileNameWithoutExtension($l.TargetPath) } else { 'explorer' }
    [pscustomobject]@{ Nome = $f.BaseName -replace '^\d+\s*', ''; Abrir = $f.FullName; Exe = $exe; Icone = $ico }
}
$lixo = Join-Path $Dir 'icones\TrashIcon.ico'
$lixoCheio = Join-Path $Dir 'icones\FullTrashIcon.ico'

# ---------- janelas: vidro (desfoque, atrás) + dock (ícones, transparente, na frente) ----------
$vidro = New-Object Windows.Window -Property @{
    Title = 'Mac Skin Dock vidro'; WindowStyle = 'None'; ResizeMode = 'NoResize'; ShowInTaskbar = $false; Topmost = $true
    Background = [Windows.Media.Brushes]::Transparent; Left = -10000; Top = -10000; Width = 10; Height = 10; ShowActivated = $false
}
$vidro.Add_SourceInitialized({
    $script:hVidro = (New-Object Windows.Interop.WindowInteropHelper $vidro).Handle
    [Windows.Interop.HwndSource]::FromHwnd($script:hVidro).CompositionTarget.BackgroundColor = [Windows.Media.Colors]::Transparent
    $ex = [MacSkin.Dock]::GetWindowLong($script:hVidro, -20)
    [void][MacSkin.Dock]::SetWindowLong($script:hVidro, -20, ($ex -bor 0x80 -bor 0x08000000 -bor 0x20))   # fora do Alt+Tab, sem foco, sem clique
})
$win = New-Object Windows.Window -Property @{
    Title = 'Mac Skin Dock'; WindowStyle = 'None'; AllowsTransparency = $true; ResizeMode = 'NoResize'
    Background = [Windows.Media.Brushes]::Transparent; Topmost = $true; ShowInTaskbar = $false
    Left = 0; Width = [Windows.SystemParameters]::PrimaryScreenWidth; Height = $AlturaJanela
    Top = [Windows.SystemParameters]::PrimaryScreenHeight - $AlturaJanela
}
$raiz = New-Object Windows.Controls.Grid
$fundo = New-Object Windows.Controls.Border -Property @{ Height = $Altura; VerticalAlignment = 'Bottom'; CornerRadius = $Raio; BorderThickness = 1
    Effect = (New-Object Windows.Media.Effects.DropShadowEffect -Property @{ BlurRadius = 28; ShadowDepth = 6; Direction = 270; Opacity = 0.28 }) }
# brilho do vidro: reflexo no alto da borda e leve luz descendo pela superfície
function Degrade($cima, $baixo) {
    $g = New-Object Windows.Media.LinearGradientBrush -Property @{ StartPoint = '0,0'; EndPoint = '0,1' }
    $g.GradientStops.Add((New-Object Windows.Media.GradientStop ([Windows.Media.ColorConverter]::ConvertFromString($cima)), 0))
    $g.GradientStops.Add((New-Object Windows.Media.GradientStop ([Windows.Media.ColorConverter]::ConvertFromString($baixo)), 1))
    $g
}
$linha = New-Object Windows.Controls.StackPanel -Property @{ Orientation = 'Horizontal'; VerticalAlignment = 'Bottom'; Margin = "$Pad,0,$Pad,$Pad" }
$dock = New-Object Windows.Controls.Grid -Property @{ HorizontalAlignment = 'Center'; VerticalAlignment = 'Bottom'; Margin = "0,0,0,$Margem" }
# o vidro (janela acrylic) fica $Recuo para dentro: com o canto redondo de 8 px do Windows ele cabe
# inteiro na curva de $Raio, sem quinas retas para fora; a faixa do recuo leva a mesma cor do vidro
$Recuo = 5
$aro = New-Object Windows.Controls.Border -Property @{ Height = $Altura; VerticalAlignment = 'Bottom'; CornerRadius = $Raio; BorderThickness = $Recuo; IsHitTestVisible = $false }
[void]$dock.Children.Add($aro); [void]$dock.Children.Add($fundo); [void]$dock.Children.Add($linha)
$texto = New-Object Windows.Controls.TextBlock -Property @{ FontSize = 13; FontFamily = 'Inter, Segoe UI' }
$rotulo = New-Object Windows.Controls.Border -Property @{ CornerRadius = 6; Padding = '10,4'; Child = $texto
    HorizontalAlignment = 'Left'; VerticalAlignment = 'Top'; Visibility = 'Hidden'; IsHitTestVisible = $false }
[void]$raiz.Children.Add($dock); [void]$raiz.Children.Add($rotulo)
$win.Content = $raiz

# a dock não pega foco (WS_EX_NOACTIVATE); sem foco, clicar fora não fecha o menu.
# Ao abrir um menu, a dock pega o foco: clicar fora a desativa e o menu fecha.
function Ligar-Menu($m) {
    $m.Add_Opened({ [void][MacSkin.Dock]::SetForegroundWindow($script:hwnd) })
    $win.Add_Deactivated({ $m.IsOpen = $false }.GetNewClosure())
}
$menu = New-Object Windows.Controls.ContextMenu
$m1 = New-Object Windows.Controls.MenuItem -Property @{ Header = 'Editar itens da Dock' }
$m1.Add_Click({ Start-Process explorer.exe (Join-Path $Dir 'dock') })
$script:mostrarBarra = $false
$m3 = New-Object Windows.Controls.MenuItem -Property @{ Header = 'Mostrar a barra do Windows'; IsCheckable = $true }
$m3.Add_Click({ $script:mostrarBarra = $m3.IsChecked; [MacSkin.Dock]::Barra($script:mostrarBarra) })
$m2 = New-Object Windows.Controls.MenuItem -Property @{ Header = 'Fechar a Dock' }
$m2.Add_Click({ $win.Close() })
[void]$menu.Items.Add($m1); [void]$menu.Items.Add($m3); [void]$menu.Items.Add($m2)
$dock.ContextMenu = $menu
Ligar-Menu $menu

$script:els = New-Object Collections.ArrayList   # { Caixa; Ponto; Pulo; App; Centro; Atual; Alvo; Largura }
function Novo-Item($app, $fonte) {
    $img = New-Object Windows.Controls.Image -Property @{ Source = $fonte; Stretch = 'Uniform' }
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($img, 'HighQuality')
    $pulo = New-Object Windows.Media.TranslateTransform
    $img.RenderTransform = $pulo
    $ponto = New-Object Windows.Shapes.Ellipse -Property @{ Width = 4; Height = 4; VerticalAlignment = 'Bottom'; Margin = '0,0,0,-6'; Visibility = 'Hidden' }
    $badgeTexto = New-Object Windows.Controls.TextBlock -Property @{
        FontSize = 9.5; FontWeight = 'Bold'; FontFamily = 'Inter, Segoe UI'; Foreground = [Windows.Media.Brushes]::White
        HorizontalAlignment = 'Center'; VerticalAlignment = 'Center'; Margin = '0,-1,0,0'
    }
    $badge = New-Object Windows.Controls.Border -Property @{
        Background = (Pincel '#FF3B30'); BorderBrush = [Windows.Media.Brushes]::White; BorderThickness = 1.5
        CornerRadius = 5; MinWidth = 10; MinHeight = 10; HorizontalAlignment = 'Right'; VerticalAlignment = 'Top'
        Margin = '0,1,1,0'; Visibility = 'Hidden'; Child = $badgeTexto; IsHitTestVisible = $false
    }
    $badge.RenderTransform = $pulo
    $caixa = New-Object Windows.Controls.Grid -Property @{ Width = $Base; Height = $Base; VerticalAlignment = 'Bottom'
        Margin = "$($Gap / 2),0,$($Gap / 2),0"; Background = [Windows.Media.Brushes]::Transparent }
    [void]$caixa.Children.Add($img); [void]$caixa.Children.Add($ponto); [void]$caixa.Children.Add($badge)
    $el = [pscustomobject]@{ Caixa = $caixa; Img = $img; Ponto = $ponto; Badge = $badge; BadgeTexto = $badgeTexto; Pulo = $pulo; App = $app; Centro = 0.0; Atual = 1.0; Alvo = 1.0; Largura = $Base + $Gap; Notif = $null }
    $caixa.Tag = $el
    $caixa.Add_MouseLeftButtonDown({ param($s, $e)
        $script:arrasto = @{ El = $s.Tag; X0 = $e.GetPosition($raiz).X; Ativo = $false }
        [void]$s.CaptureMouse() })
    $caixa.Add_MouseLeftButtonUp({ param($s)
        $s.ReleaseMouseCapture()
        $a = $script:arrasto; $script:arrasto = $null
        if ($a -and $a.Ativo) { Salvar-Ordem } else { Abrir $s.Tag } })
    if ($app.Abrir -like '*.lnk') {   # a Lixeira fica sempre
        $mi = New-Object Windows.Controls.MenuItem -Property @{ Header = 'Remover da Dock'; Tag = $el }
        $mi.Add_Click({ param($s) Remover-Item $s.Tag })
        $caixa.ContextMenu = New-Object Windows.Controls.ContextMenu
        [void]$caixa.ContextMenu.Items.Add($mi)
        Ligar-Menu $caixa.ContextMenu
    }
    [void]$linha.Children.Add($caixa); [void]$script:els.Add($el)
}

foreach ($a in $apps) { $fonte = Carregar-Icone $a.Icone; if ($fonte) { Novo-Item $a $fonte } }
$sep = New-Object Windows.Controls.Border -Property @{ Width = 1; Height = $Base * 0.75; Margin = '6,0,6,8'; VerticalAlignment = 'Bottom' }
[void]$linha.Children.Add($sep)
$script:larguraSep = 13
$script:lixeira = $null
if (Test-Path $lixo) {
    Novo-Item ([pscustomobject]@{ Nome = 'Lixeira'; Abrir = 'shell:RecycleBinFolder'; Exe = $null }) (Carregar-Icone $lixo)
    $script:lixeira = $script:els[$script:els.Count - 1]
}

# centros sem ampliação, relativos ao meio da dock: a ampliação não mexe neles (sem tremedeira)
function Calcular-Centros {
    $total = ($script:els | Measure-Object Largura -Sum).Sum + $script:larguraSep
    $x = - $total / 2
    foreach ($el in $script:els) {
        if ($el -eq $script:lixeira) { $x += $script:larguraSep }
        $el.Centro = $x + $el.Largura / 2; $x += $el.Largura
    }
}
Calcular-Centros

# arrastar para reordenar: troca de lugar ao passar do meio do vizinho; a ordem vai para os nomes (01, 02...)
function Arrastar($x) {
    $a = $script:arrasto
    if (-not $a.Ativo) { if ([Math]::Abs($x - $a.X0) -lt 8 -or $a.El -eq $script:lixeira) { return }; $a.Ativo = $true }
    $apps = @($script:els | Where-Object { $_ -ne $script:lixeira })
    $rel = $x - $raiz.ActualWidth / 2
    $alvo = 0; for ($i = 0; $i -lt $apps.Count; $i++) { if ($rel -gt $apps[$i].Centro) { $alvo = $i } }
    if ($alvo -ne $apps.IndexOf($a.El)) {
        $linha.Children.Remove($a.El.Caixa); $linha.Children.Insert($alvo, $a.El.Caixa)
        $script:els.Remove($a.El); $script:els.Insert($alvo, $a.El)
        Calcular-Centros
    }
}
function Salvar-Ordem {
    try {
        $apps = @($script:els | Where-Object { $_ -ne $script:lixeira })
        foreach ($el in $apps) {   # nome temporário primeiro, para não colidir com outro atalho
            $tmp = Join-Path (Split-Path $el.App.Abrir) "~$([guid]::NewGuid()).lnk"
            Rename-Item -LiteralPath $el.App.Abrir $tmp; $el.App.Abrir = $tmp
        }
        $n = 1
        foreach ($el in $apps) {
            $novo = Join-Path (Split-Path $el.App.Abrir) ('{0:D2} {1}.lnk' -f $n++, $el.App.Nome)
            Rename-Item -LiteralPath $el.App.Abrir $novo; $el.App.Abrir = $novo
        }
    } catch { Log "ordem: $($_.Exception.Message)" }
}

# tira o ícone na hora e apaga o atalho da pasta da dock (o app em si não é tocado)
function Remover-Item($el) {
    try {
        Remove-Item -LiteralPath $el.App.Abrir -Force
        $linha.Children.Remove($el.Caixa); $script:els.Remove($el); $script:sobre = $null
        Calcular-Centros
    } catch { Log "remover $($el.App.Nome): $($_.Exception.Message)" }
}

# ---------- ações ----------
function Pular($el) {
    $an = New-Object Windows.Media.Animation.DoubleAnimation -Property @{ From = 0; To = -18; AutoReverse = $true
        Duration = [Windows.Duration]([TimeSpan]::FromMilliseconds(260)); RepeatBehavior = (New-Object Windows.Media.Animation.RepeatBehavior ([double]2))
        EasingFunction = (New-Object Windows.Media.Animation.QuadraticEase) }
    $el.Pulo.BeginAnimation([Windows.Media.TranslateTransform]::YProperty, $an)
}
function Abrir($el) {
    try {
        $a = $el.App
        if ($a.Exe -and $a.Exe -ne 'explorer') {
            $p = Get-Process -Name $a.Exe -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
            if ($p) {
                if ([MacSkin.Dock]::IsIconic($p.MainWindowHandle)) { [void][MacSkin.Dock]::ShowWindow($p.MainWindowHandle, 9) }
                [void][MacSkin.Dock]::SetForegroundWindow($p.MainWindowHandle); return
            }
        }
        Start-Process $a.Abrir
        Pular $el
    } catch { Log "abrir $($el.App.Nome): $($_.Exception.Message)" }
}

# ---------- tema, apps abertos, lixeira, tela cheia ----------
$shellApp = New-Object -ComObject Shell.Application
function Atualizar {
    try {
        $claro = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize').AppsUseLightTheme -ne 0
        if ($claro -ne $script:claro) {
            $script:claro = $claro
            # o desfoque vem da janela de vidro; aqui só o brilho por cima (Liquid Glass)
            [MacSkin.Dock]::Vidro($script:hVidro, $(if ($claro) { 0x30F8F6F6 } else { 0x40261E1E }))
            $aro.BorderBrush = Pincel $(if ($claro) { '#B3EDEDEF' } else { '#B3252528' })
            $fundo.Background = $(if ($claro) { Degrade '#38FFFFFF' '#0AFFFFFF' } else { Degrade '#22FFFFFF' '#05FFFFFF' })
            $fundo.BorderBrush = $(if ($claro) { Degrade '#E6FFFFFF' '#40FFFFFF' } else { Degrade '#80FFFFFF' '#1AFFFFFF' })
            $sep.Background = Pincel $(if ($claro) { '#40000000' } else { '#40FFFFFF' })
            $rotulo.Background = Pincel $(if ($claro) { '#EEF2F2F4' } else { '#EE2C2C30' })
            $texto.Foreground = Pincel $(if ($claro) { '#FF1D1D1F' } else { '#FFF5F5F7' })
            foreach ($el in $script:els) { $el.Ponto.Fill = Pincel $(if ($claro) { '#CC1D1D1F' } else { '#CCF5F5F7' }) }
        }
        $abertos = @{}
        $titulos = @{}
        foreach ($p in Get-Process) {
            if ($p.MainWindowHandle -ne 0) {
                $exe = $p.ProcessName.ToLower()
                $abertos[$exe] = 1
                if ($p.MainWindowTitle) { $titulos[$exe] = $p.MainWindowTitle }
            }
        }
        $abertos['explorer'] = 1   # como o Finder: sempre aberto
        foreach ($el in $script:els) {
            if ($el.App.Exe) {
                $exe = $el.App.Exe.ToLower()
                $el.Ponto.Visibility = $(if ($abertos[$exe]) { 'Visible' } else { 'Hidden' })
                $isTeams = ($exe -eq 'teams' -or $exe -eq 'ms-teams' -or $el.App.Nome -like '*Teams*')
                $isOutlook = ($exe -eq 'outlook' -or $el.App.Nome -like '*Outlook*')
                if (-not $isTeams -and -not $isOutlook) {
                    if ($el.Notif) { $el.Notif = $null; $el.Badge.Visibility = 'Hidden' }
                    continue
                }
                $t = $titulos[$exe]
                $n = $null
                if ($t -and ($t.IndexOf('(') -ge 0) -and ($t -match '\(([0-9+]+)\)')) { $n = $Matches[1] }
                if ($n -ne $el.Notif) {
                    $el.Notif = $n
                    if ($n) {
                        $el.BadgeTexto.Text = $n
                        $el.Badge.MinWidth = 16; $el.Badge.MinHeight = 16; $el.Badge.CornerRadius = 8; $el.Badge.Padding = '3,0'
                        $el.Badge.Visibility = 'Visible'
                    } else {
                        $el.Badge.Visibility = 'Hidden'
                    }
                }
            }
        }
        if ($script:lixeira) {
            $cheia = $shellApp.NameSpace(10).Items().Count -gt 0
            if ($cheia -ne $script:cheia) { $script:cheia = $cheia; $script:lixeira.Img.Source = Carregar-Icone $(if ($cheia -and (Test-Path $lixoCheio)) { $lixoCheio } else { $lixo }) }
        }
        $telaCheia = [MacSkin.Dock]::TelaCheia($script:hwnd, $script:telaW, $script:telaH)
        $dock.Visibility = $(if ($telaCheia) { 'Hidden' } else { 'Visible' })
        $vidro.Visibility = $dock.Visibility
    } catch { Log "atualizar: $($_.Exception.Message)" }
}

# ---------- ampliação ----------
$script:sobre = $null
$win.Add_MouseMove({
    param($s, $e)
    if ($script:arrasto -and $e.LeftButton -eq 'Pressed') { Arrastar $e.GetPosition($raiz).X }
    $x = $e.GetPosition($raiz).X - $raiz.ActualWidth / 2
    $script:sobre = $null; $perto = ($Base + $Gap) / 2
    foreach ($el in $script:els) {
        $d = $x - $el.Centro
        $el.Alvo = 1 + ($Max - 1) * [Math]::Exp(- ($d * $d) / (2 * $Sigma * $Sigma))
        if ([Math]::Abs($d) -lt $perto) { $script:sobre = $el }
    }
})
$win.Add_MouseLeave({ foreach ($el in $script:els) { $el.Alvo = 1.0 }; $script:sobre = $null })
[Windows.Media.CompositionTarget]::add_Rendering({
    foreach ($el in $script:els) {
        if ([Math]::Abs($el.Alvo - $el.Atual) -gt 0.002) {
            $el.Atual += ($el.Alvo - $el.Atual) * 0.3
            $el.Caixa.Width = $Base * $el.Atual; $el.Caixa.Height = $Base * $el.Atual
        }
    }
    if ($script:sobre) {
        $texto.Text = $script:sobre.App.Nome
        $c = $script:sobre.Caixa
        $pt = $c.TranslatePoint((New-Object Windows.Point ($c.ActualWidth / 2), 0), $raiz)
        $rotulo.Margin = "$($pt.X - $rotulo.ActualWidth / 2),$($pt.Y - $rotulo.ActualHeight - 10),0,0"
        $rotulo.Visibility = 'Visible'
    } else { $rotulo.Visibility = 'Hidden' }
    # o vidro acompanha o fundo da dock (que alarga quando os ícones crescem), recuado $Recuo
    if ($script:hVidro -and $fundo.ActualWidth -gt 0) {
        $f = $script:f
        $p = $fundo.PointToScreen((New-Object Windows.Point $Recuo, $Recuo))
        $w = [int](($fundo.ActualWidth - 2 * $Recuo) * $f); $h = [int](($fundo.ActualHeight - 2 * $Recuo) * $f)
        $r = "$([int]$p.X),$([int]$p.Y),$w,$h"
        if ($r -ne $script:rVidro) { $script:rVidro = $r; [MacSkin.Dock]::Posicionar($script:hVidro, [int]$p.X, [int]$p.Y, $w, $h) }
    }
})

# ---------- ciclo de vida ----------
$win.Add_SourceInitialized({
    $script:hwnd = (New-Object Windows.Interop.WindowInteropHelper $win).Handle
    $ex = [MacSkin.Dock]::GetWindowLong($script:hwnd, -20)
    [void][MacSkin.Dock]::SetWindowLong($script:hwnd, -20, ($ex -bor 0x80 -bor 0x08000000))   # fora do Alt+Tab, não rouba o foco
    $script:f = $f = [Windows.PresentationSource]::FromVisual($win).CompositionTarget.TransformToDevice.M22
    $script:telaW = [int]([Windows.SystemParameters]::PrimaryScreenWidth * $f)
    $script:telaH = [int]([Windows.SystemParameters]::PrimaryScreenHeight * $f)
    [MacSkin.Dock]::Reservar($script:hwnd, $script:telaW, $script:telaH, [int](($Altura + $Margem * 2) * $f))
    Atualizar
})
$win.Add_Closed({ [MacSkin.Dock]::Liberar($script:hwnd); [MacSkin.Dock]::Barra($true); $vidro.Close() })
$timer = New-Object Windows.Threading.DispatcherTimer -Property @{ Interval = [TimeSpan]::FromSeconds(1.5) }
$timer.Add_Tick({ Atualizar })
$timer.Start()
# o Explorer volta a mostrar a barra (ao reiniciar, Win+T...): checagem rápida e barata
$timerBarra = New-Object Windows.Threading.DispatcherTimer -Property @{ Interval = [TimeSpan]::FromMilliseconds(250) }
$timerBarra.Add_Tick({ if (-not $script:mostrarBarra) { [MacSkin.Dock]::Barra($false) } })
$timerBarra.Start()

# erro inesperado vai para o log em vez de fechar a dock (se fechar mesmo assim, o vigia reabre)
$win.Dispatcher.Add_UnhandledException({ param($s, $e) Log "falha: $($e.Exception.Message)"; $e.Handled = $true })
Log "dock aberta com $($script:els.Count) itens"
$vidro.Show()
$win.Owner = $vidro   # a dock fica sempre na frente do vidro
# Show + Dispatcher.Run (não ShowDialog: esconder uma janela de diálogo encerra o programa)
$win.Add_Closed({ $win.Dispatcher.InvokeShutdown() })
try { $win.Show(); [Windows.Threading.Dispatcher]::Run() } catch { Log "erro: $($_.Exception.Message)" }
$mutex.ReleaseMutex()
