@echo off
rem Desfaz o que o instalar.cmd aplicou e devolve o Windows como estava (usa o backup.json).
chcp 65001 >nul
setlocal
set "DEST=%LOCALAPPDATA%\MacSkin"
if not exist "%DEST%\macskin.ps1" (echo Mac Skin não está instalado em %DEST%. & pause & exit /b 1)

echo Restaurando o Windows original...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%DEST%\macskin.ps1" -Acao remover
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Mac Skin tema.lnk" 2>nul
rmdir /s /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Mac Skin" 2>nul
echo Pronto.
pause
rem apaga a pasta por último (este .cmd pode estar dentro dela)
cd /d "%TEMP%" & rmdir /s /q "%DEST%" 2>nul
