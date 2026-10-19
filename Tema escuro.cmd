@echo off
rem Mac Skin: Deixa o Windows no modo escuro (e lembra a escolha).
start "" powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\MacSkin\macskin.ps1" -Acao escuro
