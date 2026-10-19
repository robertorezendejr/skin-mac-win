# Mac Skin: aplica/remove o visual macOS no Windows, só no usuário atual (nada pede admin).
# Tudo que é alterado vai antes para backup.json, e "-Acao remover" restaura exatamente o que havia.
param(
    [ValidateSet('instalar', 'remover', 'cursores-original', 'claro', 'escuro', 'automatico', 'auto')]
    [string]$Acao = 'auto',
    [string]$Componentes = 'base'
)
$ErrorActionPreference = 'Continue'
$Dir = $PSScriptRoot
$BackupArq = Join-Path $Dir 'backup.json'
$LogArq = Join-Path $Dir 'macskin.log'
$Per = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'

function Log($m) { $l = "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') $m"; Write-Host $l; Add-Content $LogArq $l -ErrorAction SilentlyContinue }

# ---------- backup ----------
$bk = @{ reg = New-Object Collections.ArrayList; chaves = New-Object Collections.ArrayList
         ini = New-Object Collections.ArrayList; lnk = New-Object Collections.ArrayList; arquivos = New-Object Collections.ArrayList }
if (Test-Path $BackupArq) {
    $j = Get-Content $BackupArq -Raw | ConvertFrom-Json
    foreach ($s in 'reg', 'chaves', 'ini', 'lnk', 'arquivos') { foreach ($i in @($j.$s)) { if ($null -ne $i) { [void]$bk[$s].Add($i) } } }
}
function Salvar-Backup { $bk | ConvertTo-Json -Depth 5 | Set-Content $BackupArq -Encoding UTF8 }

function Set-Reg($Path, $Name, $Value, $Type = 'DWord') {
    try {
        $n = if ($Name -eq '(default)') { '' } else { $Name }
        if (-not ($bk.reg | Where-Object { $_.Path -eq $Path -and $_.Name -eq $Name })) {
            if (-not (Test-Path $Path)) {
                # guarda a chave mais alta que não existia, para apagar na remoção
                $p = $Path; while (-not (Test-Path (Split-Path $p))) { $p = Split-Path $p }
                if ($bk.chaves -notcontains $p) { [void]$bk.chaves.Add($p) }
                [void]$bk.reg.Add([pscustomobject]@{ Path = $Path; Name = $Name; Existia = $false })
            } else {
                $k = Get-Item $Path
                $existia = $k.GetValueNames() -contains $n
                $o = [pscustomobject]@{ Path = $Path; Name = $Name; Existia = $existia; Tipo = $null; Valor = $null }
                if ($existia) {
                    $o.Tipo = $k.GetValueKind($n).ToString()
                    $o.Valor = $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
                    if ($o.Tipo -eq 'Binary') { $o.Valor = [Convert]::ToBase64String($o.Valor) }
                }
                [void]$bk.reg.Add($o)
            }
        }
        if (-not (Test-Path $Path)) { New-Item $Path -Force | Out-Null }
        New-ItemProperty $Path -Name $Name -Value $Value -PropertyType $Type -Force -ErrorAction Stop | Out-Null
    } catch { Log "  aviso: não deu para gravar $Path\$Name ($($_.Exception.Message))" }
}

# ---------- Win32 (refresh imediato; se a política bloquear Add-Type, vale no próximo login) ----------
function Win32 {
    if (-not ('MacSkin.W' -as [type])) {
        Add-Type -Namespace MacSkin -Name W -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SystemParametersInfo(int a, int b, string c, int d);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, int m, IntPtr w, string l, int f, int t, out IntPtr r);
'@
    }
}
function Atualizar-Sistema($wallpaper) {
    try {
        Win32
        if ($null -ne $wallpaper) { [void][MacSkin.W]::SystemParametersInfo(0x14, 0, $wallpaper, 3) }
        [void][MacSkin.W]::SystemParametersInfo(0x57, 0, $null, 3)   # recarrega cursores
        $r = [IntPtr]::Zero
        foreach ($s in 'ImmersiveColorSet', 'WindowsThemeElement') { [void][MacSkin.W]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [IntPtr]::Zero, $s, 2, 3000, [ref]$r) }
    } catch { Log "  aviso: atualização ao vivo bloqueada; faça logoff/login para ver tudo ($($_.Exception.Message))" }
}
function Reiniciar-Explorer {
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep 2
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
}

# ---------- tema claro/escuro + wallpaper ----------
function Set-Tema([bool]$claro) {
    $v = [int]$claro
    Set-Reg $Per AppsUseLightTheme $v
    Set-Reg $Per SystemUsesLightTheme $v
    $wp = Join-Path $Dir ("wallpaper\" + $(if ($claro) { 'claro.jpg' } else { 'escuro.jpg' }))
    if (Test-Path $wp) {
        Set-Reg 'HKCU:\Control Panel\Desktop' WallPaper $wp String
        Set-Reg 'HKCU:\Control Panel\Desktop' WallpaperStyle '10' String   # preencher
        Set-Reg 'HKCU:\Control Panel\Desktop' TileWallpaper '0' String
    } else { $wp = $null }
    Atualizar-Sistema $wp
    Log "tema: $(if ($claro) { 'claro' } else { 'escuro' })"
}
# preferência em tema.txt (escuro, claro ou auto); só "auto" segue o horário 07h/19h
$PrefArq = Join-Path $Dir 'tema.txt'
function Tema-Auto {
    $pref = if (Test-Path $PrefArq) { (Get-Content $PrefArq -Raw).Trim() } else { 'escuro' }
    if ($pref -eq 'auto') { $h = (Get-Date).Hour; Set-Tema ($h -ge 7 -and $h -lt 19) } else { Set-Tema ($pref -eq 'claro') }
}
function Fixar-Tema($pref) { Set-Content $PrefArq $pref; Tema-Auto }

# ---------- componentes ----------
function Instalar-Base {
    Log 'base: dock centralizada, cores, transparência'
    $adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    Set-Reg $adv TaskbarAl 1            # ícones no centro (Win11)
    Set-Reg $adv ShowTaskViewButton 0
    Set-Reg $adv TaskbarDa 0            # Widgets (alguns builds bloqueiam; segue sem)
    Set-Reg $adv TaskbarMn 0            # Chat
    Set-Reg $adv HideIcons 1            # desktop limpo
    Set-Reg 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' SearchboxTaskbarMode 0
    Set-Reg $Per EnableTransparency 1
    Set-Reg $Per ColorPrevalence 0
    # destaque azul macOS #007AFF
    Set-Reg 'HKCU:\Control Panel\Desktop' AutoColorization '0' String
    $acc = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
    [byte[]]$pal = 0x99,0xCA,0xFF,0, 0x66,0xAF,0xFF,0, 0x33,0x95,0xFF,0, 0x00,0x7A,0xFF,0,
                   0x00,0x62,0xCC,0, 0x00,0x49,0x99,0, 0x00,0x31,0x66,0, 0x69,0x79,0x7E,0
    Set-Reg $acc AccentPalette $pal Binary
    Set-Reg $acc AccentColorMenu 0xFFFF7A00     # ABGR
    Set-Reg $acc StartColorMenu 0xFFCC6200
    $dwm = 'HKCU:\Software\Microsoft\Windows\DWM'
    Set-Reg $dwm AccentColor 0xFFFF7A00
    Set-Reg $dwm ColorizationColor 0xC4007AFF   # ARGB
    Set-Reg $dwm ColorizationAfterglow 0xC4007AFF
    Set-Reg $dwm ColorPrevalence 0
    Set-Reg 'HKCU:\Software\Microsoft\Office\16.0\Common' 'UI Theme' 6   # Office (Outlook, Word...) segue o claro/escuro do Windows

    # alterna claro/escuro às 07h e 19h (tarefa do próprio usuário); o login é coberto pelo atalho em Inicializar
    try {
        $a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\macskin.ps1`" -Acao auto"
        $t = @((New-ScheduledTaskTrigger -Daily -At '07:00'), (New-ScheduledTaskTrigger -Daily -At '19:00'))
        Register-ScheduledTask -TaskName 'MacSkin Tema' -Action $a -Trigger $t -Force | Out-Null
        Log 'tarefa agendada: MacSkin Tema (07h claro / 19h escuro)'
    } catch { Log "  aviso: não deu para agendar a troca automática; ela ainda acontece a cada login ($($_.Exception.Message))" }
}

function Instalar-Cursores {
    Log 'cursores macOS'
    $c = Join-Path $Dir 'cursores'
    $k = 'HKCU:\Control Panel\Cursors'
    $mapa = [ordered]@{ Arrow = 'Pointer.cur'; Help = 'Help.cur'; AppStarting = 'Work.ani'; Wait = 'Busy.ani'
        Crosshair = 'Cross.cur'; IBeam = 'Text.cur'; NWPen = 'Handwriting.cur'; No = 'Unavailiable.cur'
        SizeNS = 'Vert.cur'; SizeWE = 'Horz.cur'; SizeNWSE = 'Dng1.cur'; SizeNESW = 'Dng2.cur'
        SizeAll = 'Move.cur'; UpArrow = 'Alternate.cur'; Hand = 'Link.cur'; Pin = 'Pin.cur'; Person = 'Person.cur' }
    foreach ($n in $mapa.Keys) { Set-Reg $k $n (Join-Path $c $mapa[$n]) ExpandString }
    Set-Reg $k '(default)' 'macOS' String
    Set-Reg "$k\Schemes" 'macOS' (($mapa.Values | ForEach-Object { Join-Path $c $_ }) -join ',') ExpandString
}

function Set-IconePasta($pasta, $ico) {
    if (-not $pasta -or -not (Test-Path $pasta)) { return }
    $ini = Join-Path $pasta 'desktop.ini'
    $existia = Test-Path $ini
    $orig = if ($existia) { [IO.File]::ReadAllText($ini) } else { $null }
    if (-not ($bk.ini | Where-Object { $_.Pasta -eq $pasta })) {
        [void]$bk.ini.Add([pscustomobject]@{ Pasta = $pasta; Existia = $existia; Conteudo = $orig; Atrib = (Get-Item $pasta -Force).Attributes.ToString() })
    }
    $linhas = @(if ($orig) { $orig -split "`r?`n" | Where-Object { $_ -notmatch '^\s*IconResource\s*=' } })
    if (-not ($linhas -match '^\[\.ShellClassInfo\]')) { $linhas = @('[.ShellClassInfo]') + $linhas }
    $novo = foreach ($l in $linhas) { $l; if ($l -match '^\[\.ShellClassInfo\]') { "IconResource=$ico,0" } }
    Escrever-Ini $ini ($novo -join "`r`n")
    if (-not $existia) { (Get-Item $pasta -Force).Attributes = (Get-Item $pasta -Force).Attributes -bor 'ReadOnly' }
}
function Escrever-Ini($ini, $texto) {
    if (Test-Path $ini) { (Get-Item $ini -Force).Attributes = 'Normal' }
    [IO.File]::WriteAllText($ini, $texto, [Text.Encoding]::Unicode)
    (Get-Item $ini -Force).Attributes = 'Hidden, System'
}

function Instalar-Icones {
    Log 'ícones da Apple'
    $i = Join-Path $Dir 'icones'
    $clsid = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID'
    $lix = "$clsid\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon"
    Set-Reg $lix '(default)' "$i\FullTrashIcon.ico,0" String
    Set-Reg $lix Empty "$i\TrashIcon.ico,0" String
    Set-Reg $lix Full "$i\FullTrashIcon.ico,0" String
    Set-Reg "$clsid\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\DefaultIcon" '(default)' "$i\DiscoInterno.ico,0" String
    Set-Reg "$clsid\{59031a47-3f72-44a7-89c5-5595fe6b30ee}\DefaultIcon" '(default)' "$i\HomeFolderIcon.ico,0" String
    Set-Reg "$clsid\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}\DefaultIcon" '(default)' "$i\GenericNetworkIcon.ico,0" String

    $sf = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders'
    Set-IconePasta $sf.Personal "$i\DocumentsFolderIcon.ico"
    Set-IconePasta $sf.'{374DE290-123F-4565-9164-39C4925E467B}' "$i\DownloadsFolder.ico"
    Set-IconePasta $sf.'My Pictures' "$i\PicturesFolderIcon.ico"
    Set-IconePasta $sf.'My Music' "$i\MusicFolderIcon.ico"
    Set-IconePasta $sf.'My Video' "$i\MovieFolderIcon.ico"

    # atalhos fixados na barra e do menu Iniciar do usuário (os de ProgramData exigem admin: ficam como estão)
    $porExe = @{ 'explorer.exe' = 'Finder'; 'msedge.exe' = 'Safari'; 'chrome.exe' = 'Google Chrome'
        'outlook.exe' = 'Mail'; 'winword.exe' = 'Microsoft Word'; 'excel.exe' = 'Microsoft Excel'; 'powerpnt.exe' = 'Microsoft PowerPoint'
        'code.exe' = 'Visual Studio Code'; 'notepad.exe' = 'TextEdit'; 'calc.exe' = 'Calculator'
        'wt.exe' = 'Terminal'; 'windowsterminal.exe' = 'Terminal'; 'powershell.exe' = 'Terminal'; 'pwsh.exe' = 'Terminal'; 'cmd.exe' = 'Terminal'
        'taskmgr.exe' = 'Activity Monitor'; 'snippingtool.exe' = 'Screenshot'; 'control.exe' = 'System Settings'
        'onedrive.exe' = 'OneDrive'; 'googledrivefs.exe' = 'Google Drive'; 'vlc.exe' = 'VLC'; 'claude.exe' = 'Claude'; 'antigravity ide.exe' = 'Antigravity'
        'acrord32.exe' = 'Preview'; 'acrobat.exe' = 'Preview' }
    $porNome = @{ 'file explorer' = 'Finder'; 'explorador de arquivos' = 'Finder'; 'explorer' = 'Finder' }
    $pastas = "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar", "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
    try { $sh = New-Object -ComObject WScript.Shell } catch { Log "  aviso: atalhos não alterados ($($_.Exception.Message))"; return }
    foreach ($f in Get-ChildItem $pastas -Recurse -Filter *.lnk -ErrorAction SilentlyContinue) {
        if ($f.FullName -like '*\Mac Skin*') { continue }   # atalhos do próprio Mac Skin (pasta e Inicializar)
        try {
            $l = $sh.CreateShortcut($f.FullName)
            $n = $porExe[[IO.Path]::GetFileName($l.TargetPath)]
            if (-not $n) { $n = $porNome[$f.BaseName] }
            if (-not $n -or -not (Test-Path (Join-Path $i "$n.ico"))) { continue }
            if (-not ($bk.lnk | Where-Object { $_.Arquivo -eq $f.FullName })) { [void]$bk.lnk.Add([pscustomobject]@{ Arquivo = $f.FullName; Icone = $l.IconLocation }) }
            $l.IconLocation = "$(Join-Path $i "$n.ico"),0"; $l.Save()
            Log "  atalho: $($f.Name) -> $n"
        } catch { Log "  aviso: $($f.Name) ($($_.Exception.Message))" }
    }
    & "$env:WINDIR\System32\ie4uinit.exe" -show 2>$null   # limpa o cache de ícones do usuário
}

function Instalar-Fonte {
    Log 'fonte Inter'
    $fd = "$env:LOCALAPPDATA\Microsoft\Windows\Fonts"
    New-Item $fd -ItemType Directory -Force | Out-Null
    foreach ($f in Get-ChildItem (Join-Path $Dir 'fontes') -Filter *.ttf) {
        $dest = Join-Path $fd $f.Name
        Copy-Item $f.FullName $dest -Force -ErrorAction SilentlyContinue   # reinstalação: a fonte já está em uso
        if ($bk.arquivos -notcontains $dest) { [void]$bk.arquivos.Add($dest) }
        $nome = if ($f.Name -like '*Italic*') { 'Inter Italic (TrueType)' } else { 'Inter (TrueType)' }
        Set-Reg 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts' $nome $dest String
    }
}

function Instalar-Dock {
    Log 'dock: atalhos fixados na barra, barra do Windows oculta'
    $d = Join-Path $Dir 'dock'
    if (-not (Test-Path $d)) {   # só na primeira vez: depois a pasta é do usuário (Editar itens da Dock)
        New-Item $d -ItemType Directory | Out-Null
        # AppID do Iniciar (não depende do idioma) -> ícone em icones\ e processo (ponto de "aberto")
        $padrao = @(
            @('Microsoft\.Windows\.Explorer$', 'Finder', 'explorer'), @('^MSEdge$', 'Safari', 'msedge'),
            @('OUTLOOK\.EXE', 'Mail', 'outlook'), @('M365Companions.*!Calendar$', 'Calendar', ''),
            @('^MSTeams', 'Microsoft Teams', 'ms-teams'), @('WINWORD\.EXE', 'Microsoft Word', 'winword'),
            @('EXCEL\.EXE', 'Microsoft Excel', 'excel'), @('POWERPNT\.EXE', 'Microsoft PowerPoint', 'powerpnt'),
            @('StickyNotes', 'Notes', 'Microsoft.Notes'), @('WindowsCalculator', 'Calculator', ''),
            @('^OpenAI\.', 'ChatGPT', ''), @('AnthropicClaude', 'Claude', 'claude'), @('AntigravityIDE', 'Antigravity', 'Antigravity IDE'),
            @('VisualStudioCode', 'Visual Studio Code', 'code'), @('WindowsTerminal', 'Terminal', 'WindowsTerminal'),
            @('WindowsStore', 'App Store', ''), @('immersivecontrolpanel', 'System Settings', ''))
        $apps = @(Get-StartApps)
        $sh = New-Object -ComObject WScript.Shell
        $n = 1
        foreach ($p in $padrao) {
            $a = $apps | Where-Object { $_.AppID -match $p[0] } | Select-Object -First 1
            $ico = Join-Path $Dir "icones\$($p[1]).ico"
            if (-not $a -or -not (Test-Path $ico)) { continue }
            $l = $sh.CreateShortcut((Join-Path $d ('{0:D2} {1}.lnk' -f $n++, ($a.Name -replace '[\\/:*?"<>|]', ''))))
            $l.TargetPath = "$env:WINDIR\explorer.exe"; $l.Arguments = "shell:AppsFolder\$($a.AppID)"
            $l.IconLocation = "$ico,0"; $l.Description = $p[2]   # Description = processo do app
            $l.Save()
        }
        Log "  $($n - 1) apps na dock"
    }
    # barra do Windows em "ocultar automaticamente" (StuckRects3, byte 8: 3 = oculta, 2 = fixa)
    $sr = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
    $v = (Get-ItemProperty $sr -ErrorAction SilentlyContinue).Settings
    if ($v) { $v = [byte[]]$v.Clone(); $v[8] = 3; Set-Reg $sr Settings $v Binary }
    # dock.ps1 e botoes.ps1 (bolinhas vermelha/amarela/verde) abrem a cada login
    foreach ($s in 'dock', 'botoes') {
        $lnk = Join-Path ([Environment]::GetFolderPath('Startup')) "Mac Skin $s.lnk"
        try {
            $l = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
            $l.TargetPath = 'powershell.exe'; $l.WindowStyle = 7
            $l.Arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\$s.ps1`""
            $l.Save()
            if ($bk.arquivos -notcontains $lnk) { [void]$bk.arquivos.Add($lnk) }
        } catch { Log "  aviso: $s.ps1 não vai abrir sozinho no login ($($_.Exception.Message))" }
    }
}
function Abrir-Dock {
    foreach ($s in 'dock', 'botoes') {
        if (Test-Path (Join-Path $Dir "$s.ps1")) {
            Start-Process powershell.exe "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\$s.ps1`"" -WindowStyle Hidden
        }
    }
}
function Fechar-Dock {
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*dock.ps1*' -or $_.CommandLine -like '*botoes.ps1*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

# ---------- remoção ----------
function Restaurar-Reg($r) {
    try {
        if (-not (Test-Path $r.Path)) { return }
        if ($r.Existia) {
            $v = if ($r.Tipo -eq 'Binary') { [Convert]::FromBase64String($r.Valor) } else { $r.Valor }
            New-ItemProperty $r.Path -Name $r.Name -Value $v -PropertyType $r.Tipo -Force | Out-Null
        } else { Remove-ItemProperty $r.Path -Name $r.Name -Force -ErrorAction SilentlyContinue }
    } catch { Log "  aviso: $($r.Path)\$($r.Name)" }
}

# devolve só o mouse de antes e tira os cursores do backup (o resto do visual fica)
function Remover-Cursores {
    Log 'cursores: voltando aos de antes'
    foreach ($r in @($bk.reg | Where-Object { $_.Path -like 'HKCU:\Control Panel\Cursors*' })) { Restaurar-Reg $r; [void]$bk.reg.Remove($r) }
    Atualizar-Sistema $null
}

function Remover {
    Log 'removendo e restaurando o original'
    Fechar-Dock
    try { Unregister-ScheduledTask -TaskName 'MacSkin Tema' -Confirm:$false -ErrorAction Stop } catch {}
    if ($bk.lnk.Count) {
        try { $sh = New-Object -ComObject WScript.Shell } catch { Log '  aviso: atalhos não restaurados' }
        foreach ($b in $bk.lnk) { try { if (Test-Path $b.Arquivo) { $l = $sh.CreateShortcut($b.Arquivo); $l.IconLocation = $b.Icone; $l.Save() } } catch { Log "  aviso: $($b.Arquivo)" } }
    }
    foreach ($b in $bk.ini) {
        $ini = Join-Path $b.Pasta 'desktop.ini'
        try {
            (Get-Item $b.Pasta -Force).Attributes = $b.Atrib
            if ($b.Existia) { Escrever-Ini $ini $b.Conteudo } elseif (Test-Path $ini) { Remove-Item $ini -Force }
        } catch { Log "  aviso: $ini" }
    }
    for ($x = $bk.reg.Count - 1; $x -ge 0; $x--) { Restaurar-Reg $bk.reg[$x] }
    foreach ($k in $bk.chaves) { Remove-Item $k -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($f in $bk.arquivos) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    $wp = (Get-ItemProperty 'HKCU:\Control Panel\Desktop').WallPaper
    Atualizar-Sistema $wp
    & "$env:WINDIR\System32\ie4uinit.exe" -show 2>$null
    Remove-Item $BackupArq -Force -ErrorAction SilentlyContinue
    Reiniciar-Explorer
    Log 'pronto: Windows como estava antes'
}

# ---------- principal ----------
$comp = $Componentes -split ','
switch ($Acao) {
    'instalar' {
        Log "instalando: $Componentes"
        if ($comp -contains 'base') { Instalar-Base }
        if ($comp -contains 'cursores') { Instalar-Cursores }
        if ($comp -contains 'icones') { Instalar-Icones }
        if ($comp -contains 'fonte') { Instalar-Fonte }
        if ($comp -contains 'dock') { Fechar-Dock; Instalar-Dock }
        Tema-Auto
        Salvar-Backup
        Reiniciar-Explorer
        if ($comp -contains 'dock') { Abrir-Dock }   # depois do Explorer, para a reserva de espaço valer
        Log 'pronto'
    }
    'remover' { Remover }
    'cursores-original' { Remover-Cursores; Salvar-Backup }
    'claro' { Fixar-Tema 'claro'; Salvar-Backup }
    'escuro' { Fixar-Tema 'escuro'; Salvar-Backup }
    'automatico' { Fixar-Tema 'auto'; Salvar-Backup }
    'auto' { Tema-Auto; Salvar-Backup }
}
