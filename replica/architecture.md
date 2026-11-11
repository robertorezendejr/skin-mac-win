# Arquitetura: Som de teclado do Mac Skin

Base: `replica/recon.md` e `replica/features.csv`. Data: 2026-11-10.
Decisões já tomadas: sons de licença livre (CC0) ou feitos por nós; **sem som em campo de senha**.

O guia da etapa foi pensado para app web (Postgres, rotas). Aqui o "banco" é um arquivo de ajustes e uma pasta de sons, e a "API" é o
contrato entre as threads e as funções públicas da classe.

## 1. Stack

| camada | escolha | por quê |
| --- | --- | --- |
| linguagem / build | C# do .NET Framework 4.8 via `compilar.cmd` (csc do Windows) | é o que o Mac Skin já usa: sem instalar nada, sem admin |
| captura de teclas | `WH_KEYBOARD_LL` numa thread própria | o padrão do `Holofote.cs`; não pede permissão, ao contrário da Acessibilidade do macOS |
| saída de áudio | **WASAPI compartilhado, orientado a evento**, por COM interop (`IAudioClient3` com período mínimo; se falhar, `IAudioClient` com 10 ms) | é o único caminho nativo com latência na casa de 3–10 ms; `SoundPlayer`/`MediaPlayer` têm 50–150 ms e cortam o som anterior |
| mixer | nosso, em float32 estéreo, no formato do dispositivo (`GetMixFormat`) | polifonia, pan, variação de tom e volume próprio sem depender de nada |
| amostras | WAV PCM (16/24 bits ou float), decodificadas e reamostradas na carga | leitor de WAV tem ~60 linhas; nada de MP3/OGG |
| ajustes | `som-teclado.txt` (chave=valor) em `%APPDATA%\Mac Skin Dados` | não some ao desinstalar (memória do projeto); mesmo formato do `holofote.txt` |
| sons | `%LOCALAPPDATA%\MacSkin\sons\<perfil>\` (vêm no instalador) | ficam com o programa; perfis do usuário podem ir em `Mac Skin Dados\sons\` |
| interface | bloco nas "rápidas" do `PainelBandeja` (Central) + cartão de vidro do `Volume` | componentes que já existem |

Um arquivo novo, `app/SomTeclado.cs`, com uma classe estática `SomTeclado` (como `Holofote`). Talvez um segundo, `app/Wasapi.cs`, só
com as interfaces COM.

## 2. Dados (o "schema")

### som-teclado.txt
```
ligado=1
volume=70            0..100, multiplicado pelo volume do sistema (como o original)
perfil=grafite       nome da pasta em sons\
modificadores=0      Shift Ctrl Alt Win tocam?
mouse=0
espacial=60          0 = mono, 100 = pan total pela coluna da tecla
variacao=50          quanto o tom e o volume variam por toque
pausa_tela_cheia=1   jogo / apresentação em tela cheia (SHQueryUserNotificationState)
pausa_senha=1        fixo em 1 na interface; existe só para teste
atalho=ctrl+alt+k    liga/desliga
saida=               vazio = dispositivo padrão; senão o id do IMMDevice
```
Valores fora da faixa são ignorados e viram o padrão, com uma linha no `som-teclado.log`.

### Pacote de som: `sons\<perfil>\`
```
perfil.txt        nome=Grafite   autor=...   licenca=CC0   fonte=<url>
comum-desce-1.wav  comum-desce-2.wav ...   comum-sobe-1.wav ...
espaco-desce-*.wav espaco-sobe-*.wav
enter-desce-*.wav  enter-sobe-*.wav
apagar-desce-*.wav apagar-sobe-*.wav       (Backspace, Delete)
mod-desce-*.wav    mod-sobe-*.wav          (opcional; se faltar, usa comum)
mouse-desce-*.wav  mouse-sobe-*.wav        (opcional)
```
Regras: um grupo sem arquivo usa `comum`; `comum-desce` é o único obrigatório (sem ele o perfil não aparece). O `perfil.txt`
registra licença e fonte de cada pacote, como prova de que os sons são livres.

## 3. Contrato entre as partes (a "API")

| quem | o que | thread | regra |
| --- | --- | --- | --- |
| `SomTeclado.Iniciar()` / `Parar()` | sobe e desce as duas threads | UI (Programa.Abrir / encerrar) | igual ao Holofote |
| gancho de teclado | `Tecla(vk, desceu, hora)` → põe um `int` numa fila em anel sem trava | thread do gancho | **nunca** aloca, trava ou toca: só enfileira. Filtra `Marca` (Substituição), auto-repetição (`bool[256]` de tecla baixa) e `pausado` |
| thread de áudio | acorda no evento do WASAPI, esvazia a fila, mistura vozes, entrega o buffer | "SomTeclado-Audio", prioridade `Pro Audio` via `AvSetMmThreadCharacteristics` | zero alocação no laço; vozes num vetor fixo de 32, a mais velha cede a vaga |
| vigia de foco | `EVENT_OBJECT_FOCUS` → pergunta ao UI Automation se é senha → `volatile bool emSenha` | thread do gancho (WinEvent fora de contexto) | cache por janela; para controles Win32 basta o estilo `ES_PASSWORD` sem UIA |
| pausa automática | `SHQueryUserNotificationState` no máximo 1×/s, na thread de áudio | áudio | estados 2, 3, 4 (ocupado, D3D em tela cheia, apresentação) pausam |
| troca de saída | `IMMNotificationClient.OnDefaultDeviceChanged` → reabre o cliente | áudio | sem som por ~100 ms na troca; aceitável |
| interface | `SomTeclado.Ligado {get;set}`, `Volume`, `Perfil`, `Perfis()`, `Previa(perfil)` | UI | grava o txt com atraso de 500 ms, como a Substituição |
| linha de comando | `MacSkin.exe --som-teclado` sobe só o recurso, para testar sem reiniciar o resto | — | como `--holofote` |

Voz = { amostra, posição (double, para tom variável), passo (tom), ganho E/D }.
Tom: passo = 2^(±variacao·0,08 semitom aleatório); ganho: ±variacao·2 dB. Pan de potência constante pela coluna da tecla no ABNT2.

## 4. As partes que mordem

- **O Windows remove o gancho LL em silêncio** se o callback passar do `LowLevelHooksTimeout`. O callback é trivial, mas mesmo assim: um vigia que confere a cada 30 s se chegou tecla com o recurso ligado e o teclado em uso é caro. Em vez disso, reinstalar o gancho ao voltar de suspensão/bloqueio (`WM_POWERBROADCAST`, `WTS_SESSION_UNLOCK`).
- **Três ganchos LL no mesmo processo** (Holofote, Substituição e este). Cada um soma no caminho de toda tecla do sistema. Medir; se pesar, unificar num gancho só que distribui para os três (refatoração separada).
- **GC na thread de áudio**: qualquer `new` no laço pode dar estalo. Tudo pré-alocado; buffers do WASAPI copiados por `Marshal.Copy` de um `float[]` fixo.
- **Bluetooth** adiciona 100–200 ms que ninguém resolve (o FAQ do original diz o mesmo). Avisar no ajuste de saída.
- **Formato do dispositivo** varia (44,1/48/96 kHz, 2–8 canais): mixar em estéreo e espalhar para os canais na entrega; reamostrar as amostras na carga.
- **Senha em navegador**: o UIA do Chrome/Edge expõe `IsPassword` direito; apps Electron antigos às vezes não. Na dúvida, toca (não dá para errar para o lado de silenciar tudo).
- **Memória**: um perfil com ~100 WAVs curtos (~80 ms) em float a 48 kHz dá ~3 MB. Carregar só o perfil ativo.
- **Licença dos sons**: guardar a URL e a licença de cada arquivo em `perfil.txt`. Pacotes de outros apps (Mechvibes etc.) costumam não ter licença clara: fora.

## 5. Ordem de construção

**M1, fatia vertical (1–2 dias).** Gancho + fila + WASAPI + mixer com 1 voz por tecla, 1 perfil provisório gerado por síntese
(script nosso que escreve os WAVs; 100% nosso, sem esperar a curadoria CC0). Liga/desliga só pelo txt e `--som-teclado`.
Pronto quando: som ao digitar, várias teclas sem cortar, latência medida por gravação (tecla física × som) abaixo de 30 ms ponta a
ponta, CPU parada < 0,5%.

**M2, essenciais.** Descer/subir, grupos (espaço, Enter, apagar), variação de tom/volume, sem som em senha, 3 perfis (CC0 curados),
bloco na Central (S01) com chave + volume + perfil, lista de perfis com prévia (S02).

**M3, desejáveis.** Atalho liga/desliga com cartão de aviso (S04), modificadores, numérico/função, espacial, escolha de saída,
pausa em tela cheia, tela de ajustes (S03).

**M4, opcionais.** Clique do mouse, rolagem, estatísticas no SQLite de `Mac Skin Dados`, aviso ao trocar de perfil.

Cada marco termina com teste no app instalado (trocar o exe em `%LOCALAPPDATA%\MacSkin`, reiniciar) e prints/gravações.

## 6. Sobre os sons CC0

Os bons acervos CC0 (o Freesound, por exemplo) pedem login para baixar. Por isso: o M1 usa som sintetizado e, em paralelo, eu monto
uma lista de gravações candidatas com link e licença para o Roberto ouvir e baixar. As escolhidas viram os 3 perfis do M2.
