# Mac Skin para Windows

Deixa o Windows com cara de macOS **sem precisar de admin**. Tudo é aplicado só no usuário atual (HKCU e `%LOCALAPPDATA%`), e o desinstalador devolve exatamente o que havia antes.

## O que faz

- Barra de tarefas centralizada, destaque azul `#007AFF`, transparência, desktop limpo
- Modo claro às 07h e escuro às 19h, com o papel de parede Sonoma claro/escuro
- Cursores do macOS ([apple_cursor](https://github.com/ful1e5/apple_cursor), GPL-3.0)
- Ícones da Apple na Lixeira, Este Computador, pastas do usuário e atalhos da barra e do Iniciar
- Fonte [Inter](https://rsms.me/inter/) (OFL)

## Gerar o instalador (num Mac)

```bash
brew install makensis
pip3 install Pillow
./build.sh            # gera MacSkin-Setup.exe
```

Os ícones e o papel de parede são **extraídos do macOS de quem compila** e não fazem parte deste repositório (são material da Apple). Por isso não há `.exe` pronto para baixar.

## Usar no Windows

1. Rode `MacSkin-Setup.exe` (não pede admin) e escolha os componentes.
2. Para desfazer: Iniciar › Mac Skin › Desinstalar, ou Configurações › Aplicativos.
3. Se algo falhar, o log fica em `%LOCALAPPDATA%\MacSkin\macskin.log`.

O planejamento completo, incluindo o que dá e o que não dá para fazer sem admin, está em [fase-1-passo-a-passo.md](fase-1-passo-a-passo.md).

## Arquivos

| Arquivo | Função |
|---|---|
| `instalador/macskin.ps1` | Aplica, remove e troca o tema (claro/escuro/auto), com backup de tudo |
| `instalador/macskin.nsi` | Instalador NSIS por usuário, com desinstalador |
| `instalador/scripts/*.swift` | Extraem ícones e wallpaper do macOS |
| `instalador/teste-desktopini.ps1` | Teste da lógica de `desktop.ini` (aplicar, ser idempotente, restaurar) |
| `build.sh` | Monta tudo e compila o `.exe` |
