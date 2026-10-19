@echo off
rem Mac Skin: Fecha a dock e a barra de menus e devolve a barra do Windows. Continua desligado depois de reiniciar.
start "" powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\MacSkin\macskin.ps1" -Acao desligar
