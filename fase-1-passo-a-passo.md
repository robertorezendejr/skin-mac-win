# Skin macOS no Windows: Fase 1 (levantamento e passo a passo)

**Restrições:** sem senha de admin, o mais leve possível, o mais parecido possível com o macOS (dock, barra de menus, cores, modo claro e escuro).

**Regra que vale para o guia todo:** só uso coisas que o Windows já deixa um usuário comum fazer:
- configurações gravadas em `HKCU` (o seu perfil);
- apps **portáteis** ou instalados **por usuário** em `%LOCALAPPDATA%` ou `%USERPROFILE%`;
- fontes instaladas "só para mim";
- inicialização automática pela pasta `shell:startup`.

Nada disso pede UAC.

---

## 0. Diagnóstico (5 min, faça antes de tudo)

Abra o **PowerShell normal** (sem "Executar como administrador") e cole:

```powershell
# 1. Versão do Windows (Win10 ou Win11? qual build?)
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion') | Select ProductName, DisplayVersion, CurrentBuild

# 2. Posso rodar script no meu usuário?
Get-ExecutionPolicy -List

# 3. Existe AppLocker bloqueando .exe fora de Program Files?
Get-AppLockerPolicy -Effective -ErrorAction SilentlyContinue | Select -Expand RuleCollections | Select Name, Action, PathConditions

# 4. Tenho winget e Microsoft Store?
winget --version
Get-AppxPackage Microsoft.WindowsStore | Select Name, Version

# 5. Consigo executar um .exe a partir da minha pasta? (teste real)
New-Item -ItemType Directory "$env:USERPROFILE\Apps" -Force | Out-Null
Copy-Item "$env:WINDIR\System32\notepad.exe" "$env:USERPROFILE\Apps\teste.exe"
Start-Process "$env:USERPROFILE\Apps\teste.exe"
```

**Como ler o resultado:**

| Resultado | Caminho |
|---|---|
| Win11, o `teste.exe` abriu | **Caminho A** (completo, o mais leve e o mais parecido) |
| Win10, o `teste.exe` abriu | **Caminho B** (completo, com outras ferramentas para a dock) |
| O `teste.exe` foi bloqueado (AppLocker ou WDAC) | **Caminho C** (só recursos nativos, ainda dá uns 60% do visual) |

> A diferença entre os caminhos depende do que a máquina permite. Se o `.exe` for bloqueado por política, não vale tentar driblar: isso vira problema de compliance para você. O Caminho C existe justamente para esse caso.

**Me mande a saída desses 5 comandos.** A Fase 2 começa por ela.

---

## 1. Pré-requisitos (prepare no Mac agora)

Monte um **kit** num pendrive ou no Google Drive. Assim, na máquina Windows é só copiar.

| # | Item | Para quê | Admin? |
|---|---|---|---|
| 1 | **Wallpapers oficiais do macOS** (Sequoia, Sonoma, nas versões claro e escuro) | Fundo da tela | Não |
| 2 | **Cursores macOS**: projeto `apple_cursor` (ful1e5, GitHub), pacote **Windows** | Ponteiro idêntico ao do Mac | Não (aplicação manual, passo 2.4) |
| 3 | **Fonte Inter** (Google Fonts) | Substituta da SF Pro | Não ("Instalar para mim") |
| 4 | **Windhawk**: instalador oficial (windhawk.net), opção *Portable* | Transforma a taskbar em dock (Win11) | Não no modo portátil (veja o aviso abaixo) |
| 5 | **Rainmeter**: instalador oficial, opção *Portable* | Barra de menus no topo | Não |
| 6 | **Skin de menu bar macOS para Rainmeter** (procure "macOS menu bar" em visualskins.com ou no DeviantArt) | Visual da barra superior | Não |
| 7 | **Auto Dark Mode** (GitHub AutoDarkMode, instalador por usuário) | Alterna claro e escuro sozinho, troca o wallpaper e roda scripts | Não |
| 8 | **QuickLook** (GitHub QL-Win, versão zip portátil) | Espaço para pré-visualizar, como no Finder | Não |
| 9 | *(Opcional)* **Flow Launcher**, versão portátil | Spotlight (Alt+Espaço) | Não |
| 10 | *(Opcional)* **AutoHotkey v2**, zip portátil | Atalhos no estilo Cmd (Cmd+Q, Cmd+W…) | Não |
| 11 | *(Só Win10 ou fallback)* **TaskbarX** portátil | Ícones centralizados e taskbar transparente | Não |
| 12 | *(Fallback da dock e da barra)* **Seelen UI** (Microsoft Store ou winget) | Dock e barra num só app (mais pesado) | Normalmente não (pacote MSIX) |

**Avisos:**
- **SF Pro:** a licença da Apple proíbe usá-la fora de aparelhos Apple. A Inter é praticamente idêntica e não tem esse problema.
- **Windhawk portátil:** não achei confirmação oficial de que funciona 100% sem admin. Há relatos de que alguns mods de taskbar falham no modo portátil. Por isso existe o fallback (Seelen UI). É o primeiro item que testamos na Fase 2.

---

## 2. Passo a passo de instalação

Pasta-base de tudo: `%USERPROFILE%\Apps\` (um `.exe` aqui não pede admin).

### 2.1 Configurações nativas (todos os caminhos, custo zero de RAM)

1. **Configurações › Personalização › Barra de tarefas › Comportamentos**: alinhamento **Centro**. Desligue Widgets, Visão de tarefas e Chat/Copilot. Deixe a pesquisa como **Ocultar**.
2. **Personalização › Cores**:
   - Modo **Escuro** ou **Claro**; o Auto Dark Mode vai alternar.
   - Efeitos de transparência **ligados**.
   - Cor de destaque **manual** `#007AFF` (azul do macOS). Se a opção existir, desligue "mostrar destaque na barra".
3. **Personalização › Temas › Configurações de ícones da área de trabalho**: desmarque tudo. Depois, clique direito na área de trabalho › Exibir › desmarque "Mostrar ícones". O desktop do Mac fica limpo.
4. **Sistema › Multitarefa**: mantenha o Snap. Ele faz o papel do Split View.

### 2.2 Fonte

Clique direito em `Inter-*.ttf` › **Instalar** (não "para todos os usuários").

> **Limite real:** a fonte do sistema inteiro (menus, títulos de janela) fica em `HKLM` e não muda sem admin. A Inter aparece na **barra de menus** (Rainmeter), na **dock** (o Taskbar Styler aceita fonte) e no navegador. É onde ela mais aparece.

### 2.3 Wallpaper e modo claro/escuro automático

1. Instale o **Auto Dark Mode** (instalador por usuário).
2. Em Tempo, escolha **Do pôr ao nascer do sol**.
3. Em Wallpaper, defina o wallpaper claro e o escuro do macOS.
4. Em Scripts (na Fase 2 escrevemos o script), troque também a skin do Rainmeter entre claro e escuro.

Resultado: o mesmo comportamento do "Automático" do macOS.

### 2.4 Cursores macOS (sem o `install.inf`, que pediria admin)

1. Copie a pasta de cursores para `%USERPROFILE%\Apps\Cursores\macOS`.
2. Vá em **Painel de controle › Mouse › Ponteiros**.
3. Para cada função (Seleção normal, Ocupado, Texto…), clique em **Procurar** e escolha o `.cur` ou `.ani` correspondente.
4. **Salvar como** › "macOS". Isso grava em `HKCU`, sem admin.

### 2.5 Dock

**Caminho A (Win11), o mais leve, sem nenhum processo extra de dock:**

1. Rode o instalador do Windhawk › **Portable** › destino `%USERPROFILE%\Apps\Windhawk`.
2. Em **Explorar**, instale estes mods:
   - **Windows 11 Taskbar Styler**: em Settings › Theme, escolha **DockLike**.
   - **Taskbar height and icon size**: ícones de 40–48 px, altura de 56–64 px.
   - *(Opcional)* **Start menu styler**, para um menu Iniciar mais limpo, parecido com o Launchpad.
3. A taskbar vira uma dock flutuante, arredondada e centralizada.

**Caminho B (Win10), ou se o Windhawk portátil falhar:**

- **Opção leve:** TaskbarX portátil, com ícones centralizados e taskbar transparente.
- **Opção mais fiel:** Seelen UI (Store ou `winget install Seelen.SeelenUI`), com a dock e o tema "Apple Dock".

### 2.6 Barra de menus no topo

1. Rode o instalador do Rainmeter › **Portable** › `%USERPROFILE%\Apps\Rainmeter`.
2. Arraste a skin `.rmskin` para dentro da pasta `Skins` (no modo portátil ela fica dentro da pasta do Rainmeter).
3. Ative a skin e fixe-a no topo com Posição › "Ficar no topo".

> **Limite:** a skin do Rainmeter **não reserva espaço**, então uma janela maximizada passa por baixo dela. A solução vem na Fase 2: um ajuste de margem via AutoHotkey, ou trocar pela barra do Seelen UI, que reserva espaço.

### 2.7 Extras do macOS

| Mac | Windows sem admin |
|---|---|
| Quick Look (espaço) | **QuickLook** portátil: selecione o arquivo e aperte Espaço |
| Spotlight (Cmd+Espaço) | **Flow Launcher** portátil (Alt+Espaço), ou o Win+S nativo se quiser zero RAM |
| Cmd+Q / Cmd+W / Cmd+C | **AutoHotkey v2** portátil com um script de remapeamento (Fase 2) |
| Mission Control | **Win+Tab** nativo |
| Hot corners | AutoHotkey (Fase 2, opcional) |

### 2.8 Ícones da Apple (tirados do seu Mac)

**Já extraídos.** Estão em `icones/ico/`: 92 ícones `.ico` com 7 tamanhos cada (16 a 256 px), renderizados pelo próprio macOS 27 com o visual atual. Os PNG de 1024 px ficam em `icones/png/`. Copie a pasta `icones/ico` para o kit, e no Windows para `%USERPROFILE%\Apps\Icones`.

> Os ícones são da Apple: use só no seu desktop, sem redistribuir.

**Onde trocar, sem admin:**

| Onde | Como | Admin? |
|---|---|---|
| **Dock** (apps fixados na taskbar) | Atalhos em `%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar` › Propriedades › Alterar ícone. Depois reinicie o Explorer | Não |
| **Menu Iniciar** | Atalhos em `%APPDATA%\Microsoft\Windows\Start Menu\Programs`. Os que ficam em `ProgramData` (deste não dá para mexer) você copia para a pasta do usuário e troca o ícone | Não |
| **Lixeira** (vazia e cheia), **Este Computador**, **Usuário** | Configurações › Temas › Ícones da área de trabalho › Alterar ícone (grava em HKCU) | Não |
| **Pastas** (Documentos, Downloads…) | Propriedades › Personalizar › Alterar ícone (cria um `desktop.ini` na pasta) | Não |
| **Tipos de arquivo** (.pdf, .docx…) | Override em `HKCU\Software\Classes` | Não |

Na Fase 2, um script PowerShell faz tudo isso de uma vez.

**De → Para:**

| Windows | Ícone (`icones/ico/`) |
|---|---|
| Explorador de Arquivos | `Finder.ico` |
| Edge ou Chrome | `Safari.ico` (ou mantenha `Google Chrome.ico` / `Microsoft Edge.ico`, que são as versões Mac) |
| Outlook (e-mail) | `Mail.ico` |
| Outlook (calendário) / Calendário | `Calendar.ico` |
| Configurações | `System Settings.ico` |
| Windows Terminal / PowerShell | `Terminal.ico` |
| Bloco de Notas | `TextEdit.ico` ou `Notes.ico` |
| Sticky Notes | `Stickies.ico` |
| Calculadora | `Calculator.ico` |
| Fotos | `Photos.ico` |
| Visualizador de PDF | `Preview.ico` |
| Gerenciador de Tarefas | `Activity Monitor.ico` |
| Ferramenta de Captura | `Screenshot.ico` |
| Microsoft Store | `App Store.ico` |
| Word / Excel / PowerPoint / VS Code / OneDrive / Google Drive / Claude / VLC | Versões Mac com o mesmo nome |
| Lixeira | `TrashIcon.ico` (vazia) e `FullTrashIcon.ico` (cheia) |
| Pastas | `GenericFolderIcon`, `DocumentsFolderIcon`, `DownloadsFolder`, `DesktopFolderIcon`, `PicturesFolderIcon`, `MusicFolderIcon`, `MovieFolderIcon`, `HomeFolderIcon` |
| Este Computador | `DiscoInterno.ico` ou `Finder.ico` |

> **Limite:** o ícone pequeno no **canto da janela** do app continua o original, porque fica dentro do `.exe`. Na dock, no Iniciar e nas pastas aparece o da Apple.

### 2.9 Apps abrindo com "cara de Apple"

**Não existe skin global de janela sem admin.** Ela exige patch do `uxtheme`. O que dá é deixar app por app próximo do macOS:

| App | Ajuste | Admin? |
|---|---|---|
| **Todos os apps modernos** | Modo claro/escuro automático, Mica/transparência e destaque `#007AFF` (já feitos no 2.1). É o que dá a sensação de macOS | Não |
| **Explorador de Arquivos** | Mod do Windhawk **Windows 11 File Explorer Styler**, se existir no catálogo (confirmar na Fase 2): barra lateral mais limpa e cantos arredondados, no estilo do Finder | Não (Windhawk portátil) |
| **Menu Iniciar** | Mod **Windows 11 Start Menu Styler**, com tema minimalista, mais perto do Launchpad | Não |
| **Chrome / Edge** | Tema seguindo o sistema e fonte Inter nas configurações | Não |
| **Windows Terminal** | Esquema de cores do Terminal do macOS (o "Basic"), opacidade e fonte mono | Não |
| **VS Code** | Tema no estilo macOS, fonte Inter na interface | Não |
| **Office** | Tema "Usar configuração do sistema", para seguir claro/escuro | Não |

### 2.10 Botões das janelas com cores da Apple (sem mudar de lugar)

**Resposta direta: não dá do jeito "oficial" sem admin.**
- Os botões são desenhados pelo `dwm.exe`, um processo do sistema.
- Recolorir exige patch de tema ou um mod injetado no DWM, e os dois pedem admin.
- O Windhawk sem admin só mexe em processos do seu usuário. A barra de tarefas e o Explorador funcionam; o DWM não.

**Truque que funciona sem admin (experimental, para a Fase 2):** um script AutoHotkey (~2–5 MB de RAM) desenha **3 bolinhas coloridas sobrepostas** aos botões da janela ativa:
- As bolinhas são transparentes ao clique: você clica nelas e o clique cai no botão real embaixo.
- Mantêm a ordem do Windows `–  □  ✕`, que fica **amarelo, verde, vermelho**: minimizar, maximizar, fechar.

Efeitos colaterais conhecidos:
- As bolinhas podem "atrasar" um instante ao arrastar uma janela.
- Apps com barra de título própria (Chrome, VS Code, Office) têm os botões em posições ligeiramente diferentes, então cada um precisa de ajuste fino.
- As bolinhas somem em tela cheia.

Se ficar feio no seu uso, desligamos e fica o nativo. No nativo, o ✕ já fica vermelho ao passar o mouse.

### 2.11 Iniciar com o Windows (sem admin)

1. Pressione `Win+R` › `shell:startup`.
2. Crie atalhos para `Windhawk.exe`, `Rainmeter.exe`, `QuickLook.exe` e, se usar, Flow Launcher e AutoHotkey.

O Auto Dark Mode já se registra sozinho.

---

## 3. O que NÃO dá para fazer sem admin (honestidade)

| Item | Por quê | O que fazemos no lugar |
|---|---|---|
| **Botões de janela à esquerda / recoloridos de verdade** | Exige patch do `uxtheme`/msstyles ou mod no DWM, que pede admin | Botões ficam à direita; cores via sobreposição AutoHotkey (2.10, experimental) |
| **Skin global das janelas dos apps** | Mesmo motivo | Ajuste app por app (2.9) |
| **Ícone no canto da janela de cada app** | Fica embutido no `.exe` | Ícone da Apple na dock, no Iniciar e nas pastas (2.8) |
| **SF Pro como fonte do sistema** | `HKLM\...\FontSubstitutes` | Inter na dock, na barra e no navegador |
| **Tema `.msstyles` personalizado** | Precisa de patch do sistema | Modo claro/escuro nativo, transparência e destaque `#007AFF` |
| **Bloqueio por AppLocker ou WDAC** | Política da empresa | Caminho C: só o nativo (2.1 a 2.4 + TaskbarX, se rodar) |

---

## 4. Peso estimado (RAM em uso)

| Componente | RAM aprox. |
|---|---|
| Configurações nativas, cursores, fonte, wallpaper | 0 MB |
| Windhawk + mods da taskbar | ~15–30 MB |
| Rainmeter + 1 skin de barra | ~10–25 MB |
| Auto Dark Mode | ~20–40 MB |
| QuickLook (em repouso) | ~30–60 MB |
| Flow Launcher (opcional) | ~80–120 MB |
| **Total do Caminho A sem os opcionais** | **~80–150 MB** |
| *Seelen UI (fallback), para comparar* | *~150–300 MB* |

---

## 5. Checklist para fechar a Fase 1

- [ ] Rodar o diagnóstico (seção 0) no Windows e me mandar a saída
- [ ] Baixar o kit (seção 1) no pendrive ou no Drive
- [ ] Copiar `icones/ico/` para o kit (já extraído)
- [ ] Me dizer quais apps você mais usa no Windows, para eu montar o "De → Para" dos ícones e os ajustes do 2.9
- [ ] Me dizer se quer os opcionais (Spotlight, atalhos Cmd, hot corners)

**Fase 2:** com o diagnóstico em mãos, eu escrevo um script PowerShell único (sem admin) que extrai tudo em `%USERPROFILE%\Apps`, aplica cores, cursores e atalhos de inicialização, configura o Windhawk e o Rainmeter, e cria o script de troca claro/escuro.
