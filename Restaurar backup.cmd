@echo off
rem Mac Skin: volta para a versão guardada em %LOCALAPPDATA%\MacSkin-backup (dock, barra e botões em PowerShell).
rem Fecha a versão atual, copia o backup por cima e liga de novo. O código dessa versão está no git: tag backup-powershell.
chcp 65001 >nul
setlocal
set "DEST=%LOCALAPPDATA%\MacSkin"
set "BK=%LOCALAPPDATA%\MacSkin-backup"
if not exist "%BK%\macskin.ps1" (echo Não há backup em %BK%. & pause & exit /b 1)

echo Fechando a versão atual...
if exist "%DEST%\macskin.ps1" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%DEST%\macskin.ps1" -Acao desligar
taskkill /im MacSkin.exe /f >nul 2>&1
del /q "%DEST%\MacSkin.antigo-*.exe" >nul 2>&1

echo Restaurando o backup...
rem mantém o que é seu: backup do Windows original, tema escolhido e os apps da dock
robocopy "%BK%" "%DEST%" /e /xf backup.json tema.txt /xd dock /njh /njs /nfl /ndl >nul
del /q "%DEST%\MacSkin.exe" 2>nul

rem o login volta a abrir o vigia da versão PowerShell
powershell.exe -NoProfile -Command "$l=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Startup')+'\Mac Skin.lnk'); $l.TargetPath='powershell.exe'; $l.WindowStyle=7; $l.Arguments='-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%DEST%\vigia.ps1\"'; $l.Save()"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%DEST%\macskin.ps1" -Acao ligar
echo Pronto: versão do backup ligada.
pause
