# Sprint 2 — Voz de saída e voz de entrada

**Objetivo:** conversar por voz com o professor: falar a frase (push-to-talk), ver a transcrição, ouvir só o `reply` e ver o feedback na tela. Ao fim da sprint, uma sessão de 30 minutos por voz roda do começo ao fim com o Claude Code ou o Ollama.
**Fases do roadmap:** 3 e 4.
**Decisões do usuário (2026-10-26):** a tecla de fala é **Ctrl** (segurar); CUDA é o runtime preferido do Whisper, sujeito à medição da US-014; a Kokoro (antiga US-018) sai da sprint e vai para o backlog até o Piper provar que não agrada; uma interface gráfica inspirada no ChatGPT Voice (com microfone ligado/desligado) é um passo seguinte, não desta sprint (ver "Fora desta sprint"). O restante (ordem, limiares) segue como proposta; o Planejador de cada US refina os nomes e registra a mudança em `ARCHITECTURE.md`.

| US | Título | Fase | Depende de | Status |
|---|---|---|---|---|
| US-010 | Portas de voz e áudio no Core, com configuração validada | 3 | — | Concluída |
| US-011 | O professor fala o `reply` (Piper) | 3 | US-010 | Concluída |
| US-012 | Velocidade da fala configurável e ajustável na sessão | 3 | US-011 | Concluída |
| US-013 | Gravar a fala com push-to-talk | 4 | US-010 | A fazer |
| US-014 | Transcrever com Whisper e confiança por palavra | 4 | US-010 | A fazer |
| US-015 | Palavras de baixa confiança e gravações inválidas no turno | 4 | US-013, US-014 | A fazer |
| US-016 | Ciclo de voz completo no console, com latência por etapa | 4 | US-011, US-012, US-015 | A fazer |
| US-017 | Sessão de 30 minutos: roteiro, medição e ajustes | 4 | US-016 | A fazer |

**Caminho mínimo para a sessão de 30 minutos:** US-010 → US-011 → US-013 → US-014 → US-015 → US-016 → US-017. A US-012 pode entrar antes ou depois da US-016 (a velocidade padrão 1,0× basta para o primeiro teste).

**Fora desta sprint (backlog):**
- **Kokoro como voz alternativa** (era a US-018): volta só se a voz do Piper não agradar na US-011. Ao voltar, a US precisa comparar servidor HTTP local × ONNX no processo e ser escrita de novo.
- **Interface gráfica estilo ChatGPT Voice** (inspiração, não cópia): orbe animado que reage a "ouvindo", "pensando" e "falando", transcrição e feedback ao lado, controle de microfone ligado/desligado e velocidade. Exige ADR de tecnologia (WPF × Blazor, ARCHITECTURE §2) e deve reaproveitar o Core sem mudança (R2). Entra no ROADMAP como fase própria depois da US-017.

**Ponto de corte sugerido:** depois da US-011 já dá para ouvir o professor enquanto você digita (sessão de 30 minutos por texto, com áudio de saída). É um marco útil se a voz de entrada atrasar por causa do CUDA.

## Definition of Done comum (todas as US)

Comandos que o Executor roda e cola em "Evidências" do `plan.md`:

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice"` | todos verdes, em < 30 s, sem rede, sem GPU, sem microfone e sem alto-falante |
| D3 | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice" --collect:"XPlat Code Coverage"` | linhas do `EnglishTutor.Core` ≥ 80% (relatório mesclado, AP-056) |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |

E também: todo cenário BDD com teste escrito antes do código; `review.md` sem bloqueantes; `docs/us/US-XXX.md` criado; merge em `develop`.

**Testes com hardware real** (microfone, alto-falante, modelo de voz, GPU) ficam em `[Trait("Category","LiveVoice")]`, fora do D2. A US-010 atualiza o `CLAUDE.md` (seção Comandos) e o `ARCHITECTURE.md` §10 com o novo filtro.

**Cérebros:** a regra da Sprint 1 continua: US que mexe em cérebro, prompt ou parser inclui na DoD `eval --providers <provedor>` com a linha do provedor em `met`.

**Arquivos pesados:** modelos de voz (Piper, Whisper) e áudios nunca são commitados. Vão para `models/` (ignorado pelo Git) e a US que os baixa registra tamanho e origem.

Cada US abaixo lista apenas os itens **extras** da DoD (D5 em diante).

---

## US-010 — Portas de voz e áudio no Core, com configuração validada

Como desenvolvedor do projeto,
quero as portas de voz e áudio no Core, com opções validadas e fakes de teste,
para que as US de voz só acrescentem adaptadores, sem mexer no `ConversationService`.

**Regras:** ARCHITECTURE §2 (R1, R4), §4, §7; RN-TTS01, RN-TTS02, RN-STT01.

```gherkin
Cenário: portas de voz existem no Core sem pacote externo
  Dado o assembly EnglishTutor.Core
  Quando inspeciono suas referências
  Então ele expõe ISpeechToText, ITextToSpeech, IAudioRecorder, IAudioPlayer, AudioClip e Transcript
  E continua sem referenciar pacotes fora da lista permitida

Cenário: opções de voz inválidas falham na inicialização
  Dado um appsettings com "Tts:Speed" igual a 3.0
  Quando inicio a aplicação
  Então ela termina com código 2
  E a saída de erro diz que Tts:Speed deve estar entre 0.5 e 1.5

Cenário: valores padrão
  Dado um appsettings sem a seção "Tts" nem "Stt"
  Quando inicio a aplicação
  Então Tts:Speed vale 1.0 e Stt:LowConfidenceThreshold vale 0.6
  E a voz fica desligada (Voice:Enabled = false) e o chat por texto continua como hoje

Cenário: clipe de áudio inválido
  Dado um AudioClip com taxa de amostragem zero ou sem amostras
  Quando ele é criado
  Então uma exceção diz o que está errado e o que se espera
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"` | verde com as portas novas |
| D6 | No Windows PowerShell 5.1: `$env:Tts__Speed="3.0"; dotnet run --project src/EnglishTutor.Console 2>&1; "exit=$LASTEXITCODE"; Remove-Item Env:Tts__Speed` | mensagem de faixa válida; `exit=2` |
| D7 | `"/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | cabeçalho do chat; `exit=0` (voz desligada não muda o chat) |

---

## US-011 — O professor fala o `reply` (Piper)

Como aluno,
quero ouvir a resposta do professor,
para treinar a escuta mesmo antes de poder falar.

**Regras:** RN-TTS01, RN-CONV (histórico), PRODUCT princípio 1.

```gherkin
Cenário: falar somente o reply
  Dado Voice:Enabled igual a true e um TutorReply com reply, correção e vocabulário
  Quando o turno termina
  Então o texto enviado ao TTS é exatamente o reply
  E correções, vocabulário e elogio aparecem só na tela

Cenário: o texto aparece antes do áudio terminar
  Dado uma resposta de 3 frases
  Quando o professor responde
  Então o reply e o feedback são escritos na tela antes de o áudio começar
  E a próxima entrada de texto só é lida depois do fim do áudio

Cenário: resposta crua não é falada
  Dado um turno em que a resposta caiu no fallback cru (IsRawFallback)
  Quando o turno termina
  Então nada é enviado ao TTS e a tela mostra a resposta crua como hoje

Cenário: Piper ou modelo de voz ausente
  Dado Voice:Enabled igual a true e o executável do Piper (ou o modelo de voz) inexistente
  Quando a aplicação inicia
  Então a saída de erro contém o caminho esperado e como baixar
  E o chat segue por texto, sem voz, com código de saída 0 ao digitar "/quit"

Cenário: falha da síntese não derruba a sessão
  Dado um TTS que lança exceção num turno
  Quando o professor responde
  Então o texto aparece normalmente, a saída de erro contém um aviso de que o áudio falhou e a sessão continua

Cenário: Ctrl+C durante o áudio
  Dado um áudio em síntese ou em reprodução
  Quando pressiono Ctrl+C
  Então a síntese ou a reprodução para e o processo do Piper, se ainda vivo, é encerrado
  E o código de saída é 0
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~Tts&Category!=LiveBrain&Category!=LiveVoice"` | verde, com fake de processo do Piper (nenhum executável real); nenhum teste `LiveVoice` roda: `dotnet test --filter "FullyQualifiedName~Tts&Category!=LiveBrain&Category!=LiveVoice" --list-tests` não lista `PiperLiveVoiceTests` |
| D6 | `dotnet test --filter "Category=LiveVoice&FullyQualifiedName~Piper"`, com o Piper e a voz baixados | verde: sintetiza uma frase de teste e toca sem erro |
| D7 | Manual no console com `Voice:Enabled=true` e o Claude Code: 3 turnos | o usuário confirma que ouve só o `reply`, que o feedback só aparece na tela, e registra a impressão da voz em "Evidências" |
| D8 | `git status --short` depois de baixar a voz | nenhum arquivo de `models/` ou `*.wav` no Git |

---

## US-012 — Velocidade da fala configurável e ajustável na sessão

Como aluno,
quero ouvir mais devagar ou mais rápido,
para treinar a compreensão auditiva no meu ritmo.

**Regras:** RN-TTS02.

```gherkin
Cenário: velocidade configurada
  Dado Tts:Speed igual a 0.8
  Quando o professor fala
  Então o TTS recebe velocidade 0.8

Cenário: mudar a velocidade durante a sessão
  Dado uma sessão com voz
  Quando digito "/speed 1.2"
  Então a saída contém "Speed set to 1.2x" e a próxima resposta é falada a 1.2

Cenário: velocidade fora da faixa
  Dado uma sessão com voz
  Quando digito "/speed 2"
  Então a saída contém uma mensagem dizendo que a faixa é 0.5 a 1.5
  E a velocidade anterior continua valendo

Cenário: velocidade que não é número
  Dado uma sessão com voz e a velocidade 1.2 em vigor
  Quando digito "/speed fast"
  Então a saída contém uma mensagem dizendo que o valor deve ser um número de 0.5 a 1.5
  E a velocidade anterior continua valendo

Cenário: velocidade sem argumento
  Quando digito "/speed"
  Então a saída contém a velocidade atual e a faixa aceita

Cenário: velocidade sem voz
  Dado Voice:Enabled igual a false
  Quando digito "/speed 1.2"
  Então a saída contém "Speed set to 1.2x" e nada é falado
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~Speed&Category!=LiveBrain&Category!=LiveVoice"` | verde, incluindo os limites 0.5, 1.5, 0.49 e 1.51; `--list-tests` com o mesmo filtro não lista nenhuma classe `Live*` |
| D6 | Manual: `/speed 0.7` e `/speed 1.4` em dois turnos | o usuário confirma a diferença audível e registra |
| D7 | `dotnet test --filter "FullyQualifiedName~ReplySpeakerTests&Category!=LiveBrain&Category!=LiveVoice"` | verde: `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` (US-011) continua verde depois que a velocidade passa a ser valor mutável da sessão; a US-012 só troca a fonte da velocidade, não o contrato `ReplySpeaker` → `ITextToSpeech.SynthesizeAsync(texto, velocidade, ct)` |

---

## US-013 — Gravar a fala com push-to-talk

Como aluno,
quero apertar uma tecla para falar e soltar para enviar,
para controlar quando o professor me ouve.

**Regras:** RN-STT02, ARCHITECTURE §7 (áudio 16 kHz mono).

```gherkin
Cenário: gravar enquanto a tecla está pressionada
  Dado Voice:Enabled igual a true
  Quando pressiono a tecla de fala, falo e solto
  Então o gravador devolve um AudioClip de 16 kHz mono com a duração da fala

Cenário: gravação curta demais
  Dado uma gravação com menos de 0,5 s
  Quando solto a tecla
  Então vejo "Recording too short, hold the key while you speak" e nada é enviado ao cérebro

Cenário: microfone indisponível
  Dado nenhum dispositivo de entrada
  Quando a aplicação inicia com voz ligada
  Então vejo uma mensagem dizendo que o microfone não foi encontrado
  E o chat segue por texto

Cenário: cancelar a gravação
  Dado uma gravação em andamento
  Quando pressiono Ctrl+C
  Então a captura para, o áudio é descartado e o código de saída é 0

Cenário: áudio não é guardado
  Dado Audio:KeepRecordings igual a false (padrão)
  Quando uma gravação termina
  Então nenhum arquivo de áudio é criado em disco
```

**Decisão do usuário:** a tecla de fala é **Ctrl**, segurada. `Console.ReadKey` não gera evento para Ctrl sozinho, então o plano propõe consultar o estado da tecla no Windows (por exemplo `GetAsyncKeyState` por P/Invoke, dentro da Infrastructure, atrás de uma porta de teclado) e mede se funciona com o terminal em foco. Pontos a tratar no plano: Ctrl+C continua cancelando (Ctrl pressionado junto com C não inicia gravação), a tecla configurável (`Voice:PushToTalkKey`, padrão `Ctrl`) e a fallback "alternar" se a consulta de estado falhar no terminal do aluno. Resultado e alternativas descartadas vão num ADR Proposto.

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~Recording"` | verde, com fake de dispositivo e de teclado |
| D6 | `dotnet test --filter "Category=LiveVoice&FullyQualifiedName~Recorder"`, com microfone | verde: grava 2 s e a duração medida fica entre 1,8 s e 2,5 s |
| D7 | `Get-ChildItem -Recurse -Include *.wav,*.mp3 \| Where-Object { $_.FullName -notmatch "bin\|obj\|node_modules" }` depois de uma gravação real | nenhuma linha |

---

## US-014 — Transcrever com Whisper e confiança por palavra

Como aluno,
quero que minha fala vire texto com a confiança de cada palavra,
para o professor saber o que eu disse e onde posso ter errado a pronúncia.

**Regras:** RN-STT01, ARCHITECTURE §7 e orçamento de VRAM.

```gherkin
Cenário: transcrever uma fala
  Dado um AudioClip com a frase "I have worked with C# for five years"
  Quando transcrevo
  Então o texto da transcrição contém essa frase, ignorando maiúsculas e pontuação
  E cada palavra tem uma confiança entre 0 e 1

Cenário: modelo baixado na primeira execução
  Dado que o modelo configurado não existe em models/
  Quando a aplicação inicia com voz ligada
  Então o download mostra o tamanho e o progresso
  E o arquivo é salvo em models/ (ignorado pelo Git)
  E uma segunda inicialização não baixa de novo

Cenário: runtime de aceleração indisponível
  Dado que o runtime CUDA não está disponível
  Quando a aplicação inicia
  Então ela usa Vulkan e depois CPU, nessa ordem
  E o log e o cabeçalho dizem qual runtime está ativo

Cenário: áudio sem fala
  Dado um AudioClip de silêncio
  Quando transcrevo
  Então o resultado indica "sem fala detectada" e o texto vem vazio

Cenário: cancelamento
  Dado uma transcrição em andamento
  Quando o token de cancelamento é acionado
  Então a transcrição é interrompida sem lançar exceção não tratada
```

**Decisão do usuário:** CUDA é o runtime preferido (a placa é NVIDIA RTX 4070 Ti SUPER), mas "vale uma análise". O CUDA Toolkit ainda não está instalado (ROADMAP §1) e o usuário instala (`winget install Nvidia.CUDA`). O plano mede o tempo de uma frase de 5 s em CUDA, Vulkan e CPU, e com o modelo `large-v3-turbo` e um menor (`small`/`base`), e registra a comparação num ADR Proposto antes de fixar o padrão. Se o CUDA não render ganho claro sobre o Vulkan, o padrão é o Vulkan (sem dependência de instalação).

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~Stt"` | verde, com fake do motor (sem modelo, sem GPU) |
| D6 | `dotnet test --filter "Category=LiveVoice&FullyQualifiedName~Whisper"`, com o modelo baixado e um WAV de fixture (frase em inglês gravada pelo usuário, sem dado pessoal) | verde: texto esperado e confiança em [0,1] |
| D7 | Tempo de transcrever a fixture de 5 s, no log | registrado em "Evidências"; meta de planejamento: < 1,5 s com GPU. Só registrada; a meta fim a fim é medida na US-016 |
| D8 | `git check-ignore models/` | imprime `models/` |

---

## US-015 — Palavras de baixa confiança e gravações inválidas no turno

Como aluno,
quero que o professor receba as palavras que provavelmente pronunciei mal,
para ganhar dicas de pronúncia no feedback.

**Regras:** RN-STT01, RN-STT02, RN-CONV02, RN-FEED (campo `pronunciation`).

```gherkin
Cenário: palavras abaixo do limiar
  Dado uma transcrição com palavras de confiança 0.95, 0.4 e 0.5
  E Stt:LowConfidenceThreshold igual a 0.6
  Quando o turno é montado
  Então a mensagem enviada ao cérebro lista as duas palavras de baixa confiança

Cenário: no máximo 5 palavras, as piores primeiro
  Dado uma transcrição com 8 palavras abaixo do limiar
  Quando o turno é montado
  Então a lista tem 5 palavras, da menor para a maior confiança

Cenário: nenhuma palavra de baixa confiança
  Dado uma transcrição sem palavras abaixo do limiar
  Quando o turno é montado
  Então a mensagem não traz a linha de palavras de baixa confiança

Cenário: sem fala detectada
  Dado uma transcrição vazia
  Quando o turno é processado
  Então vejo um aviso local e nada é enviado ao cérebro

Cenário: dica de pronúncia aparece na tela
  Dado um TutorReply com Pronunciation preenchido
  Quando o turno termina
  Então a dica aparece como feedback e não é falada (RN-TTS01)
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~LowConfidence"` | verde, incluindo limiar exato (0.6 não é baixa), 5, 6 e 0 palavras |
| D6 | `dotnet test --filter "FullyQualifiedName~ConversationService"` | verde: os testes da US-003 continuam passando sem mudança de comportamento para texto digitado |
| D7 | `eval --providers ClaudeCode` | linha do ClaudeCode em `met` (o prompt ganhou contexto de pronúncia; AP-032: a rodada é única, pois o golden set não usa voz) |

---

## US-016 — Ciclo de voz completo no console, com latência por etapa

Como aluno,
quero falar, ver o que o professor entendeu, ler o feedback e ouvir a resposta num só fluxo,
para treinar de verdade, sem digitar.

**Regras:** RN-CONV (turno), RN-TTS01, RN-PERS01 (latências ainda só em log), ARCHITECTURE §1 e §9.

```gherkin
Cenário: um turno de voz do começo ao fim
  Dado Voice:Enabled igual a true
  Quando falo uma frase com a tecla de fala
  Então vejo "You said: <transcrição>"
  E depois o feedback na tela e o reply falado
  E o prompt volta a aceitar fala ou comandos

Cenário: comandos continuam funcionando
  Dado uma sessão com voz
  Quando digito "/mode job-interview", "/speed 1.1", "/help" ou "/quit"
  Então cada comando funciona como no chat por texto

Cenário: cérebro cai no meio da sessão por voz
  Dado um cérebro que falha num turno
  Quando falo
  Então vejo a mensagem de erro da US-005, a transcrição não se perde e a sessão continua

Cenário: latência por etapa em log
  Dado um turno de voz concluído
  Quando abro o log da sessão
  Então há uma linha com os tempos de gravação, STT, cérebro, TTS e total (fim da fala até o início do áudio)

Cenário: microfone desligado pelo aluno
  Dado uma sessão com voz
  Quando digito "/mic off"
  Então segurar Ctrl não grava nada, nada vai ao Whisper nem ao cérebro, e vejo "Microphone is off"
  E "/mic on" volta a gravar; "/mic" mostra o estado atual
  E o estado do microfone aparece no cabeçalho

Cenário: voz desligada
  Dado Voice:Enabled igual a false
  Quando a aplicação inicia
  Então o chat por texto funciona exatamente como na Sprint 1
```

**Decisão do usuário:** Ctrl é a tecla de fala (US-013); como Ctrl não é usado ao digitar, comandos e fala coexistem no mesmo laço sem conflito. O plano só precisa definir o laço de entrada (leitura de linha e estado da tecla) e registrar a escolha num ADR Proposto. O `/mic on|off` é a versão do console do controle de microfone da futura interface gráfica (backlog desta sprint), para o Whisper não ouvir nada fora de contexto.

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~VoiceChat"` | verde, com fakes de gravador, STT, cérebro, TTS e player; ordem das etapas conferida |
| D6 | Manual: 5 turnos de voz com o Claude Code e 5 com o Ollama | o usuário confirma que o ciclo fecha; tempos do log colados em "Evidências" |
| D7 | `Select-String -Path logs\*.json -Pattern "VoiceTurnLatency"` depois do D6 | uma linha por turno, com as cinco etapas |
| D8 | `"/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` com `Voice:Enabled=false` | cabeçalho do chat; `exit=0` |
| D9 | `dotnet test --filter "FullyQualifiedName~Mic"` | verde: com `/mic off` nenhuma gravação chega ao STT, e `/mic on` restaura |

---

## US-017 — Sessão de 30 minutos: roteiro, medição e ajustes

Como aluno,
quero fazer uma sessão real de 30 minutos e saber se o produto está pronto para uso diário,
para decidir o que priorizar na Sprint 3 (persistência e latência).

**Regras:** PRODUCT (métricas de sucesso: latência < 4 s com Claude Code e < 3 s com modelo local).

```gherkin
Cenário: roteiro de sessão
  Dado o arquivo docs/sessao-30min.md
  Quando o sigo
  Então ele me guia por: aquecimento, conversa livre, 2 trocas de modo, mudança de velocidade e encerramento
  E diz o que anotar (travadas, erros de transcrição, latências percebidas)

Cenário: sessão de 30 minutos sem queda
  Dado Voice:Enabled igual a true e o Claude Code como cérebro
  Quando converso por 30 minutos
  Então a sessão não termina por erro e o uso de memória não passa de 2 GB

Cenário: relatório da sessão
  Dado o log da sessão
  Quando rodo o comando de resumo do plano
  Então vejo número de turnos, latência média e p95 fim a fim por provedor, e quantos turnos tiveram erro
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | Roteiro `docs/sessao-30min.md` executado pelo usuário, uma vez com o Claude Code | resumo colado em "Evidências": turnos, latência média e p95, erros, impressão do usuário |
| D6 | Latência fim a fim da D5 contra a meta do PRODUCT | registrada. Se média > 4 s com o Claude Code, o plano da Sprint 3 ganha a Fase 6 (streaming) como prioridade; **o Executor não muda limiares** (AP-032): o usuário decide |
| D7 | Medição do consumo de VRAM com Whisper + Piper + cérebro (`nvidia-smi`) | registrada contra o orçamento do ARCHITECTURE §7 |
| D8 | Lista de problemas encontrados | cada um vira item no `ROADMAP.md` ou US da Sprint 3, com prioridade escolhida pelo usuário |
