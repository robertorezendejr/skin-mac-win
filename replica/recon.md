# Mapa da recon: Klack (macOS) → "Som de teclado" do Mac Skin (Windows)

Escopo: o motor de som de teclado mecânico e os controles dele. Nada de licença, loja, AppleScript.
Para: usuários do Mac Skin (recurso novo dentro do MacSkin.exe, branch `nativo`).
Data: 2026-11-10

Regra clean room: recriamos o que o Klack FAZ. Não usamos código, marca, nomes de perfil nem os áudios dele.
Nenhum binário do Klack foi baixado ou aberto; só páginas públicas.

## Fontes

| # | fonte | URL | observações |
| --- | --- | --- | --- |
| 1 | site / marketing | https://tryklack.com/ | 8 recursos-chave, 7 conjuntos de switch, maquete do menu da barra |
| 2 | FAQ (ago/2026) | https://tryklack.com/faqs | latência, volume preso ao do sistema, permissão de acessibilidade, "mais de 100 arquivos de áudio" por conjunto |
| 3 | changelog (jul/2026) | https://tryklack.com/changelog | 1.0 (mar/2023) a 2.2.0 (21/07/2026) |
| — | não lidas | /privacy, /recover, Stripe, App Store, vídeos | preço não aparece nas páginas lidas |

## Loop principal

Você digita e, a cada tecla, escuta um clique de teclado mecânico realista, sem atraso perceptível e sem repetição robótica.

## O que o Klack faz (síntese das fontes)

- **Motor**: um som ao descer e outro ao subir a tecla; tom aleatório; "áudio espacial"; latência "a mais baixa possível"; leve em CPU (1.5).
- **Cobertura**: teclas comuns, teclado numérico (1.4), teclas de função (1.6), modificadores como opção (1.5), clique do mouse (1.7), rolagem com "haptic ticks" (2.2).
- **Conjuntos de som**: 7 perfis com nomes de switches reais e prévia no site. Cada um tem mais de 100 arquivos (FAQ), o que indica amostras por tecla ou por grupo, com variações.
- **Controles**: menu na barra (Som, Switches, Ajustes, Sair); atalho para ligar/desligar (1.2); saída de áudio escolhível (1.3); ícone configurável ou oculto; aviso ao trocar de switch (1.6).
- **Pausa automática** ("sleep triggers", 2.0–2.2): compartilhamento de tela, Foco, mídia, calendário, jogos.
- **Extras**: estatísticas (2.0), AppleScript/Raycast.
- **Volume**: limitado pelo volume do sistema (FAQ).
- **Permissão**: precisa de Acessibilidade no macOS (FAQ). No Windows, o gancho WH_KEYBOARD_LL dispensa permissão e admin.

## Telas (no Mac Skin)

| ID | tela | como chegar | finalidade | componentes | estados |
| --- | --- | --- | --- | --- | --- |
| S01 | Bloco "Som de teclado" na Central de Controle | ícone da Central na barra | ligar/desligar, volume, perfil atual | chave, controle deslizante, linha com seta | ligado, desligado, pausado automaticamente |
| S02 | Lista de perfis | S01 → seta do perfil | escolher e ouvir prévia | linhas com ▶ prévia e ✓ | escolhido, tocando prévia |
| S03 | Ajustes do som de teclado | S02 → "Ajustes…" | modificadores, mouse, espacial, saída, pausas, atalho | chaves, seletor de saída | padrão, saída indisponível |
| S04 | Cartão de aviso | atalho ou troca de perfil | mostra "Som de teclado: ligado / Perfil X" | cartão de vidro (o mesmo do Volume.cs) | aparece e some sozinho |

## Fluxos

```
F01 Ligar o som de teclado
    barra → Central (S01) → chave
    cliques no caminho feliz: 2   (original: 2, menu da barra → Sound)
    variação: atalho de teclado = 0 cliques, mostra S04

F02 Trocar de perfil
    S01 → S02 → clicar no perfil (prévia toca na hora)
    cliques: 3
    casos: perfil com arquivos faltando → cai para o padrão e registra no log

F03 Pausa automática
    tela cheia de jogo / compartilhamento no Teams ou Zoom / Não Incomodar → para de tocar → volta sozinho
    casos: o usuário desligou a pausa em S03

F04 Digitar
    tecla desce → som de descer (por grupo) com pan e tom variados → tecla sobe → som de subir
    casos: auto-repetição (segurar a tecla) não repete o som de descer; teclas da Substituição de texto (Marca) não tocam;
           campos de senha? (decidir: o original não diz)
```

## Componentes

Todos já existem no Mac Skin: chave (`Popover.Chave`), linha clicável (`Popover.Linha`), cartão de vidro (`Volume`) e controle deslizante.
Novo: só o motor de áudio, que não tem tela.

## Modelo de dados inferido

```
PerfilDeSom   id, nome, pasta, grupos { comum, espaco, enter, backspace, modificador, mouse, rolagem }
              cada grupo: amostras_descer[], amostras_subir[]   (variações para sortear)
              evidência: FAQ "over 100 audio files" por conjunto; "Up/down keystrokes"   confiança: média
Ajustes       ligado, volume, perfil_id, modificadores, mouse, rolagem, espacial, saida_id,
              pausas { tela_cheia, compartilhando, nao_incomodar, midia }, atalho
              evidência: changelog 1.2–2.2   confiança: alta
Estatistica   dia, teclas, cliques    (opcional)   confiança: palpite
```

Armazenamento: ajustes num `teclado.txt` (mesmo esquema chave=valor do `holofote.txt`) ou no SQLite em `%APPDATA%\Mac Skin Dados`.
Os sons ficam em `%LOCALAPPDATA%\MacSkin\sons\<perfil>\`.

## Matriz de recursos

Veja `features.csv`: must 9, should 7, could 5, skip 3.

## Fora do escopo

- Os áudios do Klack e os nomes de switch que ele usa como marca nos perfis. Fazemos perfis próprios.
- Licença, versão App Store, recuperação de licença.
- AppleScript/Raycast (só existem no macOS). Se fizer falta, a alternativa é a linha de comando.
- "Haptic ticks": não há equivalente de háptico no Windows.

## Tamanho

Telas 4 (todas sobre componentes que já existem), fluxos 4, entidades 2–3.

Partes difíceis:
1. **Latência e polifonia em C# puro com .NET 4.8**, sem dependências: `SoundPlayer` e `MediaPlayer` não servem.
   Precisa de um mixer próprio sobre WASAPI (P/Invoke, com o mesmo estilo do Volume.cs) ou `waveOut` com buffer curto.
2. **Os sons em si**: a qualidade percebida é 80% amostra. Vamos gravar, usar CC0 ou sintetizar?
3. **Não pesar no gancho de teclado**: o callback do LL hook não pode bloquear. Ele só enfileira o evento, e uma thread de áudio toca.

Tamanho: **S/M**. Um protótipo com um perfil cabe em 1–2 dias; o recurso completo com pausas, espacial e ajustes leva algumas semanas.
