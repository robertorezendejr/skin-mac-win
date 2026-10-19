@echo off
rem Mac Skin: Abre de novo a dock e a barra de menus (e volta a abrir sozinho no login).
start "" powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\MacSkin\macskin.ps1" -Acao ligar
