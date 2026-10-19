# Mac Skin: abre e mantém abertos a dock, a barra de menus e as cores dos botões das janelas.
# Roda a cada login (atalho em Inicializar) e reabre o que fechar ou cair, a cada 5 s.
# Com o arquivo "desligado" na pasta (Desligar Mac Skin), não reabre nada.
$Dir = $PSScriptRoot
$mutex = New-Object Threading.Mutex($false, 'Local\MacSkinVigia')
if (-not $mutex.WaitOne(0)) { return }   # já está vigiando
$LogArq = Join-Path $Dir 'vigia.log'
while ($true) {
    if (-not (Test-Path (Join-Path $Dir 'desligado'))) {
        $rodando = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue).CommandLine -join "`n"
        foreach ($s in 'barra', 'dock', 'botoes') {
            if ($rodando -notmatch "\\$s\.ps1") {
                # o erro de quem cair fica em <nome>-erro.txt (as janelas WPF não têm console para mostrar)
                Start-Process powershell.exe "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Dir\$s.ps1`"" -WindowStyle Hidden `
                    -RedirectStandardError (Join-Path $Dir "$s-erro.txt") -RedirectStandardOutput (Join-Path $Dir "$s-saida.txt")
                Add-Content $LogArq "$(Get-Date -f 'yyyy-MM-dd HH:mm:ss') abrindo $s" -ErrorAction SilentlyContinue
            }
        }
    }
    Start-Sleep 5
}
