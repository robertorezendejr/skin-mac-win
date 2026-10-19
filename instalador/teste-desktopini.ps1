param($Script)
$ast = [System.Management.Automation.Language.Parser]::ParseFile($Script, [ref]$null, [ref]$null)
$ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false) | % { . ([scriptblock]::Create($_.Extent.Text)) }
$Dir = Join-Path ([IO.Path]::GetTempPath()) "mstest"; Remove-Item $Dir -Recurse -Force -EA SilentlyContinue; New-Item $Dir -ItemType Directory | Out-Null
$BackupArq = "$Dir/backup.json"; $LogArq = "$Dir/log"
$bk = @{ reg = New-Object Collections.ArrayList; chaves = New-Object Collections.ArrayList; ini = New-Object Collections.ArrayList; lnk = New-Object Collections.ArrayList; arquivos = New-Object Collections.ArrayList }
$p1 = "$Dir/Docs"; $p2 = "$Dir/Down"; New-Item $p1,$p2 -ItemType Directory | Out-Null
$orig = "[.ShellClassInfo]`r`nLocalizedResourceName=@%SystemRoot%\system32\shell32.dll,-21770`r`nIconResource=%SystemRoot%\system32\imageres.dll,-112`r`n"
[IO.File]::WriteAllText("$p1/desktop.ini", $orig, [Text.Encoding]::Unicode)
Set-IconePasta $p1 'C:\x\Docs.ico'; Set-IconePasta $p1 'C:\x\Docs.ico'; Set-IconePasta $p2 'C:\x\Down.ico'
$t = [IO.File]::ReadAllText("$p1/desktop.ini")
if (($t -split "`r`n" | ? { $_ -like 'IconResource*' }).Count -ne 1 -or $t -notmatch 'IconResource=C:\\x\\Docs.ico,0' -or $t -notmatch 'LocalizedResourceName') { throw "ini1 errado: $t" }
if ([IO.File]::ReadAllText("$p2/desktop.ini") -notmatch '^\[\.ShellClassInfo\]\r\nIconResource=C:\\x\\Down.ico,0') { throw 'ini2 errado' }
Salvar-Backup
# recarrega como o script real faz
$j = Get-Content $BackupArq -Raw | ConvertFrom-Json
$bk = @{ ini = @($j.ini) }
foreach ($b in $bk.ini) { $ini = Join-Path $b.Pasta 'desktop.ini'; (Get-Item $b.Pasta -Force).Attributes = $b.Atrib; if ($b.Existia) { Escrever-Ini $ini $b.Conteudo } elseif (Test-Path $ini) { Remove-Item $ini -Force } }
if ((Get-Item $p2).Attributes.ToString() -ne $bk.ini[1].Atrib) { throw 'atributo da pasta não restaurado' }
if ([IO.File]::ReadAllText("$p1/desktop.ini") -ne $orig) { throw 'restauração ini1 falhou' }
if (Test-Path "$p2/desktop.ini") { throw 'ini2 não removido' }
'OK: desktop.ini aplica, é idempotente e restaura igual ao original'
