@echo off
rem Mac Skin sem o .exe: faz o mesmo que o MacSkin-Setup.exe, direto pelo macskin.ps1 (não pede admin).
rem Usa instalador\payload; se não existir, gera aqui (ícones WhiteSur, wallpaper Monterey, cursores, Inter).
chcp 65001 >nul
setlocal
set "SRC=%~dp0instalador"
set "DEST=%LOCALAPPDATA%\MacSkin"
set "PS=powershell.exe -NoProfile -ExecutionPolicy Bypass"

if exist "%SRC%\payload\icones\Finder.ico" goto payload_ok
echo Verificando pré-requisitos (Python, Pillow, resvg-py)...
python -c "import sys" >nul 2>&1 || (echo Sem Python: instale pela Microsoft Store ^(Python 3^) e rode de novo. Aplicando só a base. & goto payload_ok)
python -c "import PIL, resvg_py" >nul 2>&1 || python -m pip install --user -q Pillow resvg-py
echo Baixando ícones, papel de parede, cursores e fonte...
python "%SRC%\scripts\payload-windows.py" "%SRC%\payload" || echo Não deu para gerar tudo; seguindo com o que existir.
:payload_ok

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%\macskin.ps1" "%DEST%\" >nul

set "COMP=base"
rem cursores do macOS ficam de fora: o mouse do usuário continua o dele
if exist "%SRC%\payload\wallpaper\" xcopy /e /i /y /q "%SRC%\payload\wallpaper" "%DEST%\wallpaper" >nul && set "COMP=%COMP%,wallpaper"
if exist "%SRC%\payload\icones\" xcopy /e /i /y /q "%SRC%\payload\icones" "%DEST%\icones" >nul && set "COMP=%COMP%,icones"
if exist "%SRC%\payload\fontes\" xcopy /e /i /y /q "%SRC%\payload\fontes" "%DEST%\fontes" >nul && set "COMP=%COMP%,fonte"
copy /y "%SRC%\dock.ps1" "%DEST%\" >nul && copy /y "%SRC%\barra.ps1" "%DEST%\" >nul && copy /y "%SRC%\botoes.ps1" "%DEST%\" >nul &&copy /y "%SRC%\vigia.ps1" "%DEST%\" >nul && set "COMP=%COMP%,dock"
copy /y "%~dp0desinstalar.cmd" "%DEST%\" >nul

rem atalhos iguais aos do instalador: Iniciar > Mac Skin e o tema certo a cada login
%PS% -Command "$s=New-Object -ComObject WScript.Shell; $m=[Environment]::GetFolderPath('Programs')+'\Mac Skin'; New-Item $m -ItemType Directory -Force | Out-Null; $a='-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%DEST%\macskin.ps1\" -Acao '; foreach($x in @(@(\"$m\Modo claro.lnk\",'claro'),@(\"$m\Modo escuro.lnk\",'escuro'),@(([Environment]::GetFolderPath('Startup')+'\Mac Skin tema.lnk'),'auto'))){$l=$s.CreateShortcut($x[0]); $l.TargetPath='powershell.exe'; $l.Arguments=$a+$x[1]; $l.WindowStyle=7; $l.Save()}; $l=$s.CreateShortcut(\"$m\Desinstalar Mac Skin.lnk\"); $l.TargetPath='%DEST%\desinstalar.cmd'; $l.Save()"

echo Aplicando (componentes: %COMP%)...
%PS% -File "%DEST%\macskin.ps1" -Acao instalar -Componentes "%COMP%"
if errorlevel 1 (echo O script terminou com erro. Veja %DEST%\macskin.log) else (echo Pronto. Para desfazer: Iniciar ^> Mac Skin ^> Desinstalar Mac Skin)
pause
