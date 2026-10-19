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

### Sem o `.exe` e sem Mac

Pré-requisitos: Windows 10/11, internet na primeira vez e Python 3 (Microsoft Store, sem admin). O `instalar.cmd` instala sozinho o resto (`pip install --user Pillow resvg-py`).

Dê dois cliques em `instalar.cmd`. Se `instalador/payload` não existir, ele gera com [payload-windows.py](instalador/scripts/payload-windows.py), usando projetos livres no lugar do material da Apple:

| Item | Fonte | Licença |
|---|---|---|
| Ícones | [WhiteSur icon theme](https://github.com/vinceliuice/WhiteSur-icon-theme) | GPL-3.0 |
| Papel de parede | [WhiteSur wallpapers](https://github.com/vinceliuice/WhiteSur-wallpapers) (Monterey claro/escuro) | MIT |
| Cursores | [apple_cursor](https://github.com/ful1e5/apple_cursor) | GPL-3.0 |
| Fonte | [Inter](https://rsms.me/inter/) | OFL |

Depois copia tudo para `%LOCALAPPDATA%\MacSkin`, cria os atalhos no Iniciar e aplica. Sem Python, aplica só a base. Para desfazer, use `desinstalar.cmd` ou Iniciar › Mac Skin › Desinstalar Mac Skin.

### Dock

[dock.ps1](instalador/dock.ps1) é uma dock própria em WPF, sem instalar nada: vidro com desfoque (estilo Liquid Glass do macOS Golden Gate), ícones que crescem sob o mouse, pulo ao abrir, ponto nos apps abertos, Lixeira e some em tela cheia. A barra do Windows passa a se esconder sozinha e a dock reserva o espaço dela. Os itens são atalhos em `%LOCALAPPDATA%\MacSkin\dock` (botão direito na dock › Editar itens da Dock).

### Tema

O padrão é o modo escuro fixo. Iniciar › Mac Skin › Modo claro / Modo escuro trocam e lembram a escolha (`tema.txt`); `macskin.ps1 -Acao automatico` volta ao 07h claro / 19h escuro. O Office passa a seguir o tema do Windows.

Papel de parede definido por política da empresa (`Policies\System\Wallpaper`) prevalece e não é alterado.

O planejamento completo, incluindo o que dá e o que não dá para fazer sem admin, está em [fase-1-passo-a-passo.md](fase-1-passo-a-passo.md).

## Arquivos

| Arquivo | Função |
|---|---|
| `instalador/macskin.ps1` | Aplica, remove e troca o tema (claro/escuro/auto), com backup de tudo |
| `instalador/macskin.nsi` | Instalador NSIS por usuário, com desinstalador |
| `instalador/dock.ps1` | Dock no estilo macOS com Liquid Glass |
| `instalador/scripts/*.swift` | Extraem ícones e wallpaper do macOS |
| `instalador/scripts/payload-windows.py` | Monta o payload no Windows com ícones e wallpaper livres |
| `instalador/teste-desktopini.ps1` | Teste da lógica de `desktop.ini` (aplicar, ser idempotente, restaurar) |
| `build.sh` | Monta tudo e compila o `.exe` |
| `instalar.cmd` / `desinstalar.cmd` | Instalam e removem sem o `.exe`, direto pelo `macskin.ps1` |
