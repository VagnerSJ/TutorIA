# Sprint 1 — Fundação e cérebro por texto

**Objetivo:** conversar por texto com o professor (Claude Code ou Ollama), com o histórico controlado pela aplicação, formato de resposta validado, fallback entre provedores e uma avaliação comparativa dos cérebros.
**Fases do roadmap:** 1, 2 e 2b.

| US | Título | Fase | Depende de | Status |
|---|---|---|---|---|
| US-001 | Fundação da solução e configuração | 1 | — | Concluída |
| US-002 | Contrato do professor e parser único da resposta | 1 | US-001 | Concluída |
| US-003 | ConversationService dono do histórico | 1 | US-002 | A fazer |
| US-004 | Conversar por texto com o professor via Claude Code | 2 | US-003 | A fazer |
| US-005 | Erros do Claude Code com mensagens claras | 2 | US-004 | A fazer |
| US-006 | Professor local via Ollama | 2b | US-003 | A fazer |
| US-007 | Troca de cérebro por configuração e fallback | 2b | US-005, US-006 | A fazer |
| US-008 | Suíte de contrato do ITutorBrain | 2b | US-004, US-006 | A fazer |
| US-009 | Avaliação comparativa dos cérebros (golden set + `eval`) | 2b | US-007 | A fazer |

## Definition of Done comum (todas as US)

Comandos que o Executor roda e cola em "Evidências" do `plan.md`:

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes, em < 30 s, sem rede nem GPU |
| D3 | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` | linhas do `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |

E também: todo cenário BDD com teste escrito antes do código; `review.md` sem bloqueantes; `docs/us/US-XXX.md` criado; merge em `develop`.

**Cérebros:** a partir da US-009, toda US que mexe em cérebro, prompt ou parser inclui na DoD o `eval` do golden set com nota mínima (ver US-009). Antes disso, as US de cérebro usam um teste `LiveBrain` de fumaça.

Cada US abaixo lista apenas os itens **extras** da DoD (D5 em diante).

---

## US-001 — Fundação da solução e configuração

Como desenvolvedor do projeto,
quero a solução .NET estruturada em camadas com DI, configuração validada, logs e uma casca de console,
para que as próximas US só acrescentem comportamento, sem retrabalho de infraestrutura.

**Regras:** ARCHITECTURE §2 (R1–R4), §9; RN-CONV06.

```gherkin
Cenário: solução compila sem avisos
  Dado que clonei o repositório e tenho o SDK .NET definido em global.json
  Quando executo "dotnet build"
  Então a build termina com sucesso
  E não há nenhum warning (TreatWarningsAsErrors habilitado)

Cenário: Core não depende de pacotes externos
  Dado o assembly EnglishTutor.Core
  Quando inspeciono suas referências
  Então ele não referencia EnglishTutor.Infrastructure nem EnglishTutor.Console
  E não referencia pacotes fora de System.* e Microsoft.Extensions.*.Abstractions / Microsoft.Extensions.Options

Cenário: configuração do cérebro é validada na inicialização
  Dado um appsettings.json com "Brain:Provider" igual a "Inexistente"
  Quando inicio a aplicação
  Então ela encerra com código diferente de zero
  E mostra uma mensagem dizendo que o provedor "Inexistente" é inválido e quais são os aceitos

Cenário: aplicação inicia e mostra o estado
  Dado uma configuração válida com Provider "ClaudeCode" e modo padrão
  Quando inicio a aplicação
  Então vejo um cabeçalho com o provedor configurado e o modo "free-conversation"
  E vejo como pedir ajuda ("/help") e sair ("/quit")

Cenário: comando local de saída
  Dado que a aplicação está aguardando minha entrada
  Quando digito "/quit"
  Então a aplicação encerra com código zero
  E nada é enviado a nenhum cérebro

Cenário: comando local desconhecido
  Dado que a aplicação está aguardando minha entrada
  Quando digito "/xyz"
  Então vejo "Unknown command" e a lista de comandos disponíveis
  E continuo na sessão

Cenário: logs estruturados
  Dado que a aplicação foi iniciada
  Quando ela encerra
  Então existe um arquivo de log em "logs/" com o evento de início e de fim em formato estruturado

Cenário: integração contínua (opcional)
  Dado um push ou pull request para develop ou main
  Quando o GitHub Actions executa
  Então ele roda build, testes (exceto Category=LiveBrain) e publica o relatório de cobertura
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `"/quit" \| dotnet run --project src/EnglishTutor.Console` | cabeçalho com provedor e modo; código de saída 0 |
| D6 | `$env:Brain__Provider="Inexistente"; dotnet run --project src/EnglishTutor.Console` | código de saída ≠ 0; mensagem cita "Inexistente" e os provedores aceitos |
| D7 | `Get-ChildItem logs` após D5 | arquivo de log com eventos de início e fim |
| D8 | Teste de arquitetura do Core (incluído em D2) | verde |

---

## US-002 — Contrato do professor e parser único da resposta

Como aluno,
quero que toda resposta do professor seja separada em fala e feedback de forma confiável,
para que só a parte conversacional seja falada e o feedback apareça organizado na tela.

**Regras:** RN-BRAIN03, RN-FEED01–05; ADR-002.

```gherkin
Cenário: resposta JSON válida
  Dado o JSON {"reply":"Nice! How long have you worked with C#?","corrections":[{"original":"since five years","corrected":"for five years","explanation":"use 'for' with a period of time"}],"pronunciation":null,"vocabulary":null,"praise":null}
  Quando o parser processa a resposta
  Então obtenho um TutorReply com Reply "Nice! How long have you worked with C#?"
  E 1 correção "since five years → for five years"

Cenário: resposta com tags válida
  Dado o texto "<reply>Great question. What do you do at work?</reply><feedback>- I am work → I work (simple present)</feedback>"
  Quando o parser processa a resposta
  Então obtenho Reply "Great question. What do you do at work?"
  E 1 correção "I am work → I work"

Cenário: resposta fora do formato
  Dado o texto "Sure! Here is my answer without tags."
  Quando o parser processa a resposta
  Então o resultado é inválido com o motivo "missing reply"

Cenário: reply sem pergunta final
  Dado um JSON cujo reply é "I like that."
  Quando o parser processa a resposta
  Então o resultado é inválido com o motivo "reply must end with a question"

Cenário: mais de duas correções
  Dado um JSON com 3 correções
  Quando o parser processa a resposta
  Então mantenho apenas as 2 primeiras
  E registro um aviso no log

Cenário: sem erros
  Dado um JSON sem correções e com praise "Great use of the present perfect!"
  Quando o parser processa a resposta
  Então o feedback exibido contém o elogio
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~TutorReplyParser"` | verde, incluindo fixtures JSON e tags |
| D6 | Inspeção dos testes de formato inválido | cada motivo de rejeição tem mensagem que diz o que faltou e o que se espera (usada na instrução de correção da RN-BRAIN04) |

---

## US-003 — ConversationService dono do histórico

Como aluno,
quero que o professor lembre do que conversamos, independentemente do provedor,
para manter uma conversa coerente mesmo se o cérebro mudar no meio da sessão.

**Regras:** RN-CONV01–06, RN-BRAIN01, RN-BRAIN04, RN-TTS01.

```gherkin
Cenário: mensagem do turno no formato padrão
  Dado que estou no modo "job-interview"
  Quando envio o texto "I have worked with C# since five years"
  Então o cérebro recebe como última mensagem:
    """
    Student said (transcribed): "I have worked with C# since five years"
    Low-confidence words (possible pronunciation issues): []
    Mode: job-interview
    """

Cenário: janela de turnos
  Dado que MaxTurnsInWindow é 2 e já houve 3 turnos completos
  Quando envio uma nova frase
  Então o cérebro recebe o system prompt, os 2 últimos turnos e a nova mensagem
  E não recebe o primeiro turno

Cenário: histórico independe do provedor
  Dado um cérebro sem estado (fake que só enxerga o BrainRequest)
  Quando envio a segunda frase da sessão
  Então o BrainRequest contém o primeiro turno completo (aluno e professor)

Cenário: nova tentativa em resposta inválida
  Dado um cérebro que responde fora do formato na primeira vez e no formato correto na segunda
  Quando envio uma frase
  Então o cérebro é chamado 2 vezes
  E a segunda chamada inclui a instrução de correção de formato
  E recebo o reply e o feedback normalmente

Cenário: resposta crua após duas falhas
  Dado um cérebro que responde fora do formato duas vezes
  Quando envio uma frase
  Então vejo o texto cru da segunda resposta
  E não vejo feedback
  E o turno fica marcado como IsRawFallback

Cenário: entrada vazia
  Dado que a aplicação aguarda minha entrada
  Quando envio "   "
  Então nada é enviado ao cérebro
  E vejo um aviso local

Cenário: troca de modo
  Dado que estou no modo "free-conversation"
  Quando digito "/mode job-interview"
  Então a próxima mensagem enviada ao cérebro tem "Mode: job-interview"
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test tests/EnglishTutor.Core.Tests` | verde em < 10 s, só com fakes |
| D6 | Cobertura do namespace `EnglishTutor.Core.Conversation` (relatório de D3) | ≥ 90% de linhas |

---

## US-004 — Conversar por texto com o professor via Claude Code

Como aluno,
quero digitar uma frase em inglês e receber uma resposta curta com feedback,
para praticar antes de ter o fluxo de voz.

**Regras:** RN-BRAIN02, RN-BRAIN05, RN-BRAIN06, RN-FEED01–05; ARCHITECTURE §6.3; ADR-001, ADR-002.

```gherkin
Cenário: frase com erro gramatical
  Dado que estou no modo free-conversation
  Quando eu envio "I have worked with C# since five years"
  Então recebo um reply com no máximo 3 frases terminando em pergunta
  E recebo um feedback com a correção "since five years → for five years"

Cenário: processo isolado e sem ferramentas
  Dado o provedor ClaudeCode configurado
  Quando a aplicação monta a chamada ao processo "claude"
  Então os argumentos incluem "-p", "--output-format json", "--system-prompt-file", "--tools" com valor vazio e "--permission-mode dontAsk"
  E não incluem "--bare"
  E o diretório de trabalho é a pasta dedicada do cérebro, criada se não existir

Cenário: continuidade de sessão
  Dado que o primeiro turno retornou session_id "abc"
  E UseResume está habilitado
  Quando envio o segundo turno
  Então a chamada inclui "--resume abc"

Cenário: leitura da saída gravada
  Dado a fixture "claude-json-structured.json" gravada de uma execução real
  Quando o cérebro interpreta a saída
  Então obtenho o TutorReply e o session_id

Cenário: medição de latência
  Dado o comando de medição com 5 frases
  Quando rodo com OutputMode JsonSchema e depois com Tags
  Então vejo a latência média e p95 de cada modo
```

**DoD extra** (requer Claude Code CLI instalado e autenticado)

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ClaudeCode"` | verde: 5 frases de fumaça com erros conhecidos, ≥ 4 com a correção esperada e 5/5 no formato |
| D6 | `"I have worked with C# since five years" \| dotnet run --project src/EnglishTutor.Console` | reply terminando em pergunta + correção "since five years → for five years" |
| D7 | Medição de latência JsonSchema × Tags | tabela com média e p95 colada nas Evidências e no ADR-002 |

---

## US-005 — Erros do Claude Code com mensagens claras

Como aluno,
quero entender o que deu errado quando o professor não responde,
para saber como resolver sem abrir logs.

**Regras:** RN-BRAIN07, RN-BRAIN09.

```gherkin
Cenário: CLI não encontrada
  Dado que o executável "claude" não existe no caminho configurado
  Quando envio uma frase
  Então vejo "Claude Code CLI not found" com a instrução de instalação e a chave ExecutablePath

Cenário: não autenticado
  Dado que o processo retorna erro de autenticação
  Quando envio uma frase
  Então vejo uma mensagem pedindo para rodar "claude auth login"

Cenário: limite de uso atingido
  Dado que o processo retorna erro de limite de uso
  Quando envio uma frase
  Então o erro é classificado como UsageLimitReached

Cenário: tempo esgotado
  Dado TimeoutSeconds igual a 1 e um processo que não responde
  Quando envio uma frase
  Então o processo filho é encerrado
  E vejo uma mensagem de tempo esgotado
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~ClaudeCode"` | um teste por `BrainErrorKind`, com fixtures de stdout/stderr gravadas |
| D6 | `$env:Brain__ClaudeCode__ExecutablePath="nao-existe"; "hi" \| dotnet run --project src/EnglishTutor.Console` | mensagem "Claude Code CLI not found" com instrução; sessão continua |

---

## US-006 — Professor local via Ollama

Como aluno,
quero conversar com um modelo rodando na minha GPU,
para praticar sem consumir minha assinatura e sem internet.

**Regras:** RN-BRAIN01–05; ARCHITECTURE §6.4.

```gherkin
Cenário: chamada com histórico completo
  Dado o provedor Ollama e uma sessão com 2 turnos
  Quando envio uma frase
  Então a requisição para /api/chat contém system prompt, os 2 turnos e a nova mensagem

Cenário: saída estruturada
  Dado OutputMode JsonSchema
  Quando a requisição é montada
  Então ela usa "format" com o JSON Schema do TutorReply

Cenário: Ollama indisponível
  Dado que nada responde em http://localhost:11434
  Quando envio uma frase
  Então vejo "Ollama is not running" com a instrução para iniciá-lo

Cenário: modelo não baixado
  Dado que o modelo configurado não existe no Ollama
  Quando envio uma frase
  Então vejo o comando "ollama pull <modelo>"
```

**DoD extra** (requer Ollama com o modelo baixado)

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama"` | verde: 5 frases de fumaça, 5/5 no formato |
| D6 | `nvidia-smi` durante o teste D5 | VRAM do modelo registrada em ARCHITECTURE §7 |

---

## US-007 — Troca de cérebro por configuração e fallback

Como aluno,
quero escolher o cérebro no appsettings e ter um reserva automático,
para não perder a sessão quando a assinatura atingir o limite.

**Regras:** RN-BRAIN08.

```gherkin
Cenário: escolha por configuração
  Dado Brain:Provider igual a "Ollama"
  Quando a aplicação inicia
  Então o cabeçalho mostra o provedor "Ollama"

Cenário: fallback por limite de uso
  Dado Provider "ClaudeCode" e FallbackProvider "Ollama"
  E o Claude Code retorna UsageLimitReached
  Quando envio uma frase
  Então a resposta vem do Ollama
  E vejo o aviso "Switched to Ollama (usage limit reached)"

Cenário: sem fallback configurado
  Dado FallbackProvider vazio
  E o Claude Code retorna UsageLimitReached
  Quando envio uma frase
  Então vejo a mensagem de limite de uso e continuo na sessão
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~BrainFactory\|FullyQualifiedName~FallbackTutorBrain"` | verde |
| D6 | `$env:Brain__Provider="Ollama"; "/quit" \| dotnet run --project src/EnglishTutor.Console` | cabeçalho mostra "Ollama" |

---

## US-008 — Suíte de contrato do ITutorBrain

Como desenvolvedor do projeto,
quero uma única suíte de testes que todo cérebro precisa passar,
para garantir que trocar de provedor não muda o comportamento da aplicação.

**Regras:** RN-BRAIN01, RN-BRAIN03; ARCHITECTURE §10.

```gherkin
Cenário: mesma suíte para todos os provedores
  Dado a classe abstrata TutorBrainContractTests
  Quando existem implementações ClaudeCode e Ollama
  Então cada uma tem uma subclasse que executa todos os testes do contrato com fakes

Cenário: testes live opcionais
  Dado testes marcados com Trait Category=LiveBrain
  Quando executo "dotnet test --filter Category!=LiveBrain"
  Então eles não são executados
  E quando executo "dotnet test --filter Category=LiveBrain" eles rodam contra os provedores reais
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet test --filter "FullyQualifiedName~ContractTests" --logger "console;verbosity=normal"` | os mesmos testes do contrato listados para ClaudeCode e Ollama, todos verdes |

---

## US-009 — Avaliação comparativa dos cérebros

Como aluno,
quero comparar os cérebros com um conjunto fixo de frases com erros conhecidos,
para decidir quando um modelo local é bom o suficiente.

**Regras:** PRODUCT (métricas de sucesso).

```gherkin
Cenário: rodar o golden set
  Dado o arquivo eval/golden-set.json com cerca de 30 frases e correções esperadas
  Quando executo "EnglishTutor.Console eval --providers ClaudeCode,Ollama"
  Então para cada provedor vejo: % de correções esperadas encontradas, % de respostas no formato, latência média e p95
  E um relatório markdown é salvo em eval/reports/

Cenário: provedor indisponível na avaliação
  Dado que o Ollama não está rodando
  Quando executo o eval com os dois provedores
  Então o relatório marca o Ollama como "unavailable" e os resultados do Claude Code continuam válidos
```

**DoD extra**

| # | Comando | Esperado |
|---|---|---|
| D5 | `dotnet run --project src/EnglishTutor.Console -- eval --providers ClaudeCode,Ollama` | relatório em `eval/reports/` |
| D6 | Nota do provedor principal (ClaudeCode) no relatório | correções esperadas encontradas ≥ 80%; formato válido ≥ 90% |
| D7 | `eval/golden-set.json` | ≥ 30 frases, cada uma com erro e correção esperada revisados pelo usuário |

Esses limiares (D6) passam a ser a **nota mínima do eval** na DoD das US de cérebro das sprints seguintes.
