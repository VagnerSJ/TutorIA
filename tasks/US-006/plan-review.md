# Revisão do plano — US-006 Professor local via Ollama

Data: 2026-10-21
Escopo revisado: `tasks/US-006/plan.md` (commit 90e4741), `docs/adr/ADR-009-...md` (Proposto), `specs/sprints/SPRINT_1.md` (US-006, US-007), `specs/BUSINESS_RULES.md` (RN-BRAIN07/08/09) e `ARCHITECTURE.md` (§3, §6.1, §6.4, §7). Também li `appsettings.json`, `EnglishTutorApp.cs` e os testes existentes, só para checar contradição com o plano.
Resultado: REPROVADO (1 bloqueante)

Conferi no código e rodando `dotnet test --filter "Category!=LiveBrain"`:
- A linha de base do plano bate: Core 134, Infrastructure 101, Console 51, tudo verde.
- Os padrões do D12 dão 5 linhas no `appsettings.json` atual mais o bloco novo.
- Os textos antigos que o D15c e a §2.9 citam existem em `ARCHITECTURE.md`: linha 62 (§3), linha 176 (§6.1) e linha 218 (§6.4).
- `PromptFiles.FormatJsonPath`, `PromptFiles.FormatTagsPath` e `BrainOutputModes.TryGetCanonical` existem.
- A faixa de EventId 2000–2999 está livre para o 2002 (usados: 2000 e 2001).

## Verificações (checklist §0)
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD no mapa da §4 com projeto, classe e método no padrão | ✅ 5 cenários da spec mais o cenário novo "tempo esgotado" |
| [O] Resposta P* com efeito observável tem teste (§4) e tarefa (§5) | ❌ P9 (veja B1) |
| [O] DoD da §10 cobre a DoD da spec (D5, D6, cobertura do Core ≥ 80%) | ✅ D5, D6, D3/D3b e D4 presentes. D7 e D8–D10 vêm do plano |
| [O] Conformidade com R1–R4, RN-*, CLAUDE.md e ADR Proposto | ✅ ADR-009 Proposto e pacotes só na Infrastructure. O desvio da RN-BRAIN07/08 (`Unknown`) está na P2 e no ADR para o usuário decidir |
| [O] Escopo e adiamentos com texto exato (AP-022, AP-040) | ✅ Seletor provisório → D7 da US-007 com texto exato e prova D16. Limites aceitos (P2, P12) listam consumidores |
| [O] Consistência interna §3 × §4 × §5 × §6, contagens (AP-025, AP-046) | ✅ Conferi: Infrastructure +61 (1+24+25+7+4), Console 51→55, total 351, validador 24 casos/12 métodos, `OllamaTutorBrainTests` 25 casos/18 métodos, D11 = 64. Nomes da §5 iguais aos da §4 |
| Transformações das mensagens com teste dos dois lados (AP-016, AP-015) | ⚠️ veja B1 e S2 |
| Condicionais da §2 com teste (AP-047) | ⚠️ veja S3 |
| [O] Ordem das tarefas preserva o Red | ✅ Os testes que nascem verdes estão declarados, com mutação depois do Green ou no Red (T1, T3, T4, T5, T6, T8) |
| LiveBrain: camada, tolerância, rodadas, risco (AP-032) | ✅ Adaptador sem retentativa, tolerância zero, 3 rodadas, risco de oscilação e quem decide registrados |
| `ValidateOnStart` em várias classes e normalização no consumidor (AP-039) | ✅ Caso com duas seções inválidas; `OutputMode` com caixa diferente no consumidor; `Provider` `Ollama` e `ollama` |
| Justificativas que citam outras US (AP-036) | ✅ O cenário "fallback por limite de uso" tem Claude Code como Provider e Ollama como FallbackProvider, conforme `SPRINT_1.md`. A RN-BRAIN08 não lista `Unknown`, como o plano afirma |
| "Nenhuma linha" na DoD com o outro lado demonstrado (AP-035) | ✅ D15c sobre `864b98d` e D16 |
| Alteração na `ARCHITECTURE.md` lista repetições em CLAUDE.md e skills | ✅ §2.9 varreu CLAUDE.md, README, skills e specs; nada a alterar |
| Riscos e ambiguidades do Executor | ✅ Tratados na §7 e nas paradas explícitas (tipo da exceção do 404, `format`, cerca markdown) |
| Viabilidade no SDK e nas bibliotecas | ✅ `CancellationTokenSource(TimeSpan, TimeProvider)` e `HttpRequestError` existem no .NET 10. A versão do `OllamaSharp` fica a confirmar na T2, com parada prevista |

## Bloqueantes
- [ ] **B1** — `plan.md` §2.5 (`<detalhe>`) e P9 × §4 (`SendAsync_WhenServerCallFails_ClassifiesByStatusAndText`, casos 5 e `SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage`) × T4. A P9 define o texto exibido ao aluno na falha genérica: `<detalhe>` é a primeira linha não vazia da mensagem da exceção, sem espaços nas pontas, ou `no error details were provided` quando não há. Os únicos casos testados (`boom t5`, `boom t7`) têm mensagem de uma linha, não vazia e sem espaços nas pontas. Nenhum teste prova nem a escolha da primeira linha não vazia (mensagem com várias linhas, ou começando por linha vazia), nem o texto de reserva (mensagem vazia ou só espaços). Qualquer implementação que embuta a mensagem inteira, ou que nunca use o texto de reserva, passa em toda a suíte. Esperado: cada parte da transformação da P9 tem teste mapeado na §4 e tarefa na §5 (T4, com Red e mutação conforme o caso). A mensagem exibida não pode embutir linhas extras de stack ou de corpo HTTP, nem ficar com parênteses vazios (checklist §0 [O] "P* com efeito observável"; AP-016).

## Sugestões
- **S1** — `plan.md` cabeçalho, linha `Branch:`. O plano diz `feature/US-006_ollama-brain`, mas a branch real é `feature/US-006_ollama-tutor-brain`. Alinhar o texto do plano com a branch (ou o contrário) evita confusão no merge `--no-ff` e no `docs/us/US-006.md`.
- **S2** — `plan.md` §2.1 (mensagem de `Temperature`) e §4 (`Validate_WhenTemperatureIsOutOfRange_FailsWithRangeAndFoundValue`). O plano exige o valor "em cultura invariante", mas nenhum caso descreve a mensagem sob uma cultura com vírgula decimal (ex.: `pt-BR`). Um `ToString()` sem cultura passaria na máquina de CI em `en-US` e quebraria na do usuário. Esperado: a asserção do teste fixa o separador decimal exibido (`found -0.1`) mesmo com a cultura da thread em `pt-BR`.
- **S3** — `plan.md` §2.3, linha a (`mensagem contém model e not found`). O plano testa o status 404 sozinho (caso 3), o texto completo sozinho (caso 4) e a precedência (caso 6). Não há caso com só uma das duas palavras, por exemplo `not found t8` sem `model`, sem status. Esse caso deveria cair na linha b ou c. Sem ele, trocar o "e" por "ou" no código passa na suíte. Esperado: um caso em que só uma das palavras casa e a classificação não vai para a linha a (AP-047).
- **S4** — `plan.md` §2.3 e T4. O catch do adaptador cobre `HttpRequestException` e o tipo próprio de exceção do `OllamaSharp`, ainda desconhecido. A cobertura dessa segunda exceção depende só do teste com a resposta gravada (`SendAsync_WithRecordedModelNotFoundResponse_...`). Se a T3 mostrar que o 404 sai com esse tipo próprio, vale um caso de unidade com o tipo real na `[Theory]` da §4, para o ramo não depender só da fixture. O Executor pode decidir na T3 e registrar na §12.

## Pontos positivos
- Boa disciplina de prova: testes de fio com o `OllamaApiClient` real e `HttpMessageHandler` falso (`/api/chat`, `format`, `options.temperature`), fixtures gravadas pelo próprio adaptador (AP-048) e recorder que registra a cadeia de exceções para decidir o que o `catch` captura.
- Tratamento rigoroso do tempo máximo: os dois cancelamentos (do chamador e do tempo) com ordem determinística pelo `onCancelled` do fake, `WaitAsync(10 s)` para o Red não travar a suíte, e um relógio por chamada provado com 900 + 999 ms.
- Planejamento completo do Red: testes que nascem verdes listados um a um, com mutações pelo efeito. Controle D15c contra `864b98d` e D16 como prova do comando que a US-007 vai usar.
- Intenção antes do código: spec da US-006 e da US-007, `ARCHITECTURE.md` e ADR-009 num commit separado antes da T1; desvio da RN-BRAIN07/08 exposto como pergunta (P2) com consumidores e efeito.
- LiveBrain com camada, tolerância, rodadas e risco de oscilação declarados.

## Aprendizados para o processo
- Transformação de texto exibida ao aluno (primeira linha, texto de reserva, formatação de número) tende a entrar no plano só pelo caminho feliz. Os casos de uma linha e de mensagem presente passam sem provar a regra. Já aparece nos AP-015 e AP-016. Sugestão para a skill de planejamento: para cada `<placeholder>` derivado de texto de biblioteca ou de valor numérico, listar na §4 um caso por ramo da derivação (normal, múltiplas linhas, vazio) e, para números, a cultura.
- Condicionais com conjunção ("A e B") precisam de caso em que só um dos termos casa (AP-047). O plano já cobre a precedência e cada fonte isolada, mas esqueceu o termo isolado da conjunção textual.

---

## Rodada 2

# Revisão do plano — US-006 Professor local via Ollama (rodada 2)

Data: 2026-10-21
Escopo revisado: `tasks/US-006/plan.md` no commit aebee1a. Conferência do texto e da aritmética das contagens, sem build nem testes (só o `plan.md` mudou).
Resultado: APROVADO (0 bloqueantes)

## Itens da rodada anterior
| Item | Status |
|---|---|
| **B1** (derivação do `<detalhe>`, P9) | Resolvido em aebee1a. §4 ganhou `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback`, `[Theory]` de 5 casos comparando a mensagem inteira. Cada ramo tem caso próprio (várias linhas; linha inicial vazia ou em branco; espaços e `\r` nas pontas; mensagem vazia; só espaços e quebras). T4 lista o teste no Red e uma mutação por ramo depois do Green. §2.5, P9, D2 e D11 atualizados. |
| S1 (nome da branch) | Sem efeito. A branch atual é `feature/US-006_ollama-brain`, igual ao cabeçalho do plano. O `plan-review.md` da rodada 1 citava outro nome (`ollama-tutor-brain`), que não confere com `git branch`. |
| S2 (cultura invariante em `Temperature`) | Pendente, não pedida. |
| S3 (só uma das palavras `model`/`not found`) | Pendente, não pedida. |
| S4 (tipo próprio de exceção do OllamaSharp) | Pendente, não pedida. |

## Contagens (conferidas na §4)
- Validador: 12 métodos, 24 casos. `OllamaTutorBrainTests`: 19 métodos, 30 casos. `OllamaTutorBrainHttpTests`: 7/7. DI: +4 casos, +3 métodos. `OllamaOptionsTests`: 1.
- Infrastructure.Tests: 1+24+30+7+4 = 66 → 101+66 = 167. Console.Tests: 55. Total 134+55+167 = 356 (D2 bate).
- D11: 1+24+30+7 = 62, mais 4 de console e 3 de DI = 69. Bate.
- Nomes do teste novo idênticos na §4, na T4 e na P9 (AP-025).

## Verificações (checklist §0)
Todos os itens ✅. Ressalvas: transformações (AP-016/015) só com a observação de cultura (S2 pendente); condicionais da §2 (AP-047) só com S3 pendente.

## Bloqueantes
Nenhum.

## Sugestões
- **S2, S3, S4** da rodada 1 continuam pendentes e não bloqueiam. A S3 ainda deixa um caso sem prova: com só `not found` (sem `model`) e sem status, trocar o "e" por "ou" na linha a passaria na suíte. Fechar exige 1 caso na `[Theory]` de classificação e atualizar as contagens (AP-031).

## Pontos positivos
- Correção do B1 cirúrgica e completa: caso por ramo, mensagem inteira comparada, mutação por ramo, contagens recalculadas e conferidas.
- O Red de cada teste novo está declarado; o `<detalhe>` não nasce verde.

## Aprendizados para o processo
- Confirma o AP-016: `<placeholder>` derivado de texto de biblioteca precisa de um caso por ramo da derivação (e a cultura, para números).
- O Revisor deve conferir nomes de branch com `git branch` antes de citá-los.

---

## Rodada 3

# Revisão do plano — US-006 Professor local via Ollama (rodada 3, revisão de escopo)

Data: 2026-10-22
Escopo revisado: `tasks/US-006/plan.md` — T10–T14, §2.12, §4 (linhas novas e contagens), §5, §7, §9 (P13–P17) e §10 (D5, D19, D20, D20c). Branch conferida: `feature/US-006_ollama-brain`. Commit de spec conferido: `15b4291`. Não repeti o que as rodadas 1 e 2 já aprovaram.
Resultado: REPROVADO (2 bloqueantes)

## Itens do checklist de plano (só o que a revisão de escopo mexe)
| Item | Resultado |
|---|---|
| Cenários BDD novos no mapa da §4 (duas perguntas aceitas, três inválidas; Core e parser) | ✅ nomes `Metodo_Cenario_Resultado`, nomes dos cenários iguais aos de `SPRINT_1.md` (linhas 241 e 246) |
| P* com efeito observável têm teste (§4) e tarefa (§5) | ✅ P13 (mensagem), P14 (sem aviso), P15 (guarda de prompts), P16 e P17 mapeadas |
| DoD da §10 cobre a DoD da US (D5 com retentativa, 3 rodadas; cobertura do Core) | ✅ |
| Conformidade R1–R4, RN, regras rápidas | ✅ regra no Core (R4); `padroes-codigo` já traz a convenção "Tolerância de verificação" |
| Escopo e adiamentos (AP-022/AP-040) | ✅ consumidores da tolerância listados na §7 e na §2.12.5; sem adiamento novo |
| Consistência interna §3 × §4 × §5 e contagens | ❌ ver B1 e B2. As contagens 356 → 364 batem: Core 134+4=138; Console 55; Infrastructure 167+1+3=171 |
| Ordem das tarefas preserva o Red | ❌ ver B1 |
| LiveBrain sobre saída de modelo (AP-032) | ✅ D5 diz a camada (serviço com retentativa), tolerância zero, 3 rodadas, risco de oscilação e quem decide o vermelho; D19 diz camada, tolerância e 1 rodada |
| Verificação de "nenhuma linha" com o outro lado (AP-035, D20/D20c) | ❌ ver B2 |
| Alteração na `ARCHITECTURE.md` lista as repetições (§2.12.5) | ✅ o texto antigo existe em `ARCHITECTURE.md:191` (§6.2), em ADR-005 (linhas 5 e 19) e em ADR-002 (linha 30); `padroes-codigo` já foi atualizado |
| Viabilidade e riscos | ✅ `ConversationService` retenta só em `InvalidOutput` e cai em `TutorReply.RawFallback` (`ConversationService.cs:62-72`); `ConversationTurnResult.Reply` e `LiveClaudeCode.KnownErrors` existem; os prompts atuais já têm "exactly one question" e "second question", então o guarda nasce verde como o plano diz |

## Bloqueantes
- [ ] **B1** — §4 (asserções principais de `Validate_WhenReplyAsksMoreThanTwoQuestions_RejectsWithExactlyOneQuestionReason`) × §5 T10 — A §4 diz que o teste confere `Message` com `Replies with 1 or 2 questions are accepted`. A T10 diz que os 4 casos "nascem verdes" e que, na mutação "limite superior aceita até 3", "o caso de 4 continua verde". Hoje a mensagem não tem essa frase (`TutorReplyRejections.cs:30`), então os 4 casos nascem vermelhos no Red, não verdes. Esperado: o plano diz uma coisa só. Ou a frase da tolerância sai das asserções desse teste e fica só no `TutorReplyRejectionsTests` (3 casos), ou a T10 passa a listar esses 4 casos entre os que falham pela asserção. Reescrever a lista de Red e a de mutações de acordo, para o Executor saber que o Red falha pelo motivo certo (checklist: ordem das tarefas preserva o Red; AP-025/AP-046).
- [ ] **B2** — `tests/EnglishTutor.Core.Tests/Brain/ReplySentencesTests.cs:28` (comentário do teste `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark`) — O comentário `// US-004 — Cenário: reply com duas perguntas é inválido (apoio, P21...)` casa com o padrão `reply com duas perguntas é inválido` do D20. O D20 exige "nenhuma linha" depois da T10 e da T12, mas nenhuma tarefa, nem a §3, nem a tabela de conferência da §2.12 toca esse arquivo. O D20 ficaria vermelho por um arquivo que o plano não prevê. O D20c ("pelo menos uma linha para cada padrão") também não o lista. Esperado: o arquivo entra na §2.12, na §3 e na T10, com o novo texto do comentário (o cenário antigo não existe mais na spec). Conferi com Grep em `src` e `tests`: além dos arquivos já listados, só esse comentário casa.

## Sugestões
- **S1** — `specs/sprints/SPRINT_1.md:233` (US-004, cenário "frase com erro gramatical": `Então recebo um reply com no máximo 3 frases e exatamente uma pergunta`) e linha 306 (D6 da US-004: `reply com exatamente uma pergunta`). O commit `15b4291` não os alterou. A T12 relaxa o `LiveBrain` do Claude Code para 1..2, e o teste continua citando esse cenário. A justificativa está na P17, mas a spec não diz que o cenário descreve a regra pedida. Esperado: o plano registra que esses dois textos ficam como regra pedida, com o comentário do teste dizendo "pedida 1, verificada 1..2", ou propõe uma nota na spec.
- **S2** — §2.12.1, P13 e §2.12.2(iii) — A `Message` da rejeição é a instrução de correção enviada ao modelo na retentativa (`TurnMessageBuilder.BuildFormatCorrection`). Com a frase `Replies with 1 or 2 questions are accepted.`, a tolerância chega ao modelo, e o plano e o guarda `PromptFilesContentTests` dizem que ela não vai para o prompt. O plano reconhece isso e registra que foi pedido pelo usuário (P13), então não bloqueia. Peço só que a §2.12.2 diga que a exceção é a mensagem de correção da retentativa, para o guarda não parecer contradito. Se o D5 oscilar, essa frase é a primeira candidata a sair.
- **S3** — §4 (`PromptFilesContentTests`) e T11 — O guarda confere só texto que os prompts já têm (`exactly one question`, `second question`). Ele nasce verde e continua verde sem o reforço, então a T11 não tem teste que falhe antes e passe depois do que ela entrega (a checagem final por contagem de `?` e o par bom/ruim). A prova é o D5, `LiveBrain`, como o plano diz. Esperado: incluir no guarda um trecho que só o reforço produz (ex.: a linha de contagem de `?`), ou registrar na T11 que o reforço só tem prova por `LiveBrain`.
- **S4** — T13 — A frase "Outra mutação, 'o decorator conta mas não altera' é o estado final" confunde. Esperado: dizer só que o estado final é o decorator que conta sem alterar a resposta.
- **S5** — `CHANGELOG.md:28` registra a RN-FEED01 antiga (US-004). O Documentador acrescenta a entrada da tolerância. Vale uma linha na §2.12.5.

## Pontos positivos
- A tabela de conferência da §2.12 (onde "exatamente 1" vive ou é testado) está completa e distingue testes de fixture gravada (mantidos, P17) de `LiveBrain` ao vivo (relaxados, T12).
- Os dois lados do limite superior (2 válido, 3 inválido) e do inferior (0 inválido, 1 válido) têm teste. A mensagem da rejeição é conferida por trecho que só o projeto produz (AP-015, AP-016).
- A T13 troca o teste de adaptador puro pelo de serviço com retentativa. O filtro `FullyQualifiedName~Ollama` continua com 5 testes `LiveBrain` por rodada (3 + 1 + 1), como o D5 diz.
- O risco de oscilação do D5 e o limite aceito da tolerância estão na §7 (AP-032, AP-040).
- Os textos antigo e novo das alterações em `ARCHITECTURE.md`, ADR-005 e ADR-002 estão identificados pelo texto antigo, e confere com os arquivos (AP-023). O commit de arquitetura da revisão está antes da T10 (AP-030).

## Aprendizados para o processo
- O D20 (busca de texto antigo) deixou um arquivo de fora porque o inventário foi feito pelos testes que mudam de comportamento, não pelo texto do comentário. → Regra sugerida: ao renomear um cenário, o inventário da tabela de conferência inclui todo arquivo que cita o nome antigo, rodando o Grep do D20c antes de fechar a §2.12 (variação de AP-035/AP-040).
- Uma asserção acrescentada a um teste "que nasce verde" depois de desenhar a mensagem nova. → Ao fechar a lista de Red de uma tarefa, rodar mentalmente cada asserção contra o `src` atual (variação de AP-046).

---

## Rodada 4

# Revisão do plano — US-006 Professor local via Ollama (rodada 4, revisão de escopo)

Data: 2026-10-22
Escopo revisado: `tasks/US-006/plan.md`, diff `293ca32..aa68b5b` (só o `plan.md` mudou). Li a §2.12, a §3, a §4, a §5 T10 e T12, a §7, a §10 (D20 e D20c) e o registro da rodada 3 no fim do arquivo. Conferi nomes de arquivo e linhas com `git grep` sobre `139adee` e com Grep em `src` e `tests`. Não rodei build nem testes, porque nenhum código mudou. Não repeti o que as rodadas anteriores aprovaram.
Resultado: APROVADO (0 bloqueantes)

## Itens da rodada anterior
| Item | Status |
|---|---|
| **B1** (Red de T10 incoerente com `TutorReplyRejections.cs`) | Resolvido em `aa68b5b`. A T10 agora lista os 4 casos de 3 ou mais perguntas do validador entre os que falham pela asserção, junto com os 3 casos de `TutorReplyRejectionsTests`. `TutorReplyRejections.cs:30` não tem a frase `Replies with 1 or 2 questions are accepted`, então essa lista está certa. Só `Parse_WhenJsonReplyAsksThreeQuestions_...` nasce verde, porque confere apenas `has 3 questions`. A mutação "limite superior até 3" derruba os 3 casos de 3 perguntas e deixa o caso de 4 verde, o que está coerente. Entrou a mutação "frase sai da Message". A §2.12.1, a §4, a P13 e as alternativas descartadas dizem a mesma coisa. |
| **B2** (`ReplySentencesTests.cs` fora do inventário) | Resolvido em `aa68b5b`. O arquivo entrou na tabela da §2.12, no parágrafo de inventário, na §3 (`Alterar`, só o comentário), na linha de apoio da §4, na T10 (o comentário novo faz parte do Red), na T12, no risco da §7, no D20 e no D20c. O D20c lista os 6 arquivos. Conferi o Grep dos padrões do D20 sobre `139adee`. Ele devolve exatamente os 6 arquivos, nas linhas que o plano cita: `TutorReplyValidator.cs:19`, `ReplySentencesTests.cs:28`, `TutorReplyRejectionsTests.cs:58` e `:61`, `TutorReplyValidatorTests.cs:68`, `:74` e `:80`, `ClaudeCodeTutorBrainLiveTests.cs:44`, `TutorReplyParserTests.cs:122`, `:124` e `:132`. Nenhum outro arquivo casa. |
| S1–S5 (rodada 3) | Não incorporadas, como pedido. O diff não mexe em nenhum dos pontos das S1–S5. A T13, `SPRINT_1.md`, `PromptFilesContentTests` e a §2.12.2 seguem como estavam. O plano registra as cinco como pendentes para o usuário decidir. |

## Verificações (só o que mudou)
| Item | Resultado |
|---|---|
| Contagem D2 | ✅ 364 = Core 138 + Console 55 + Infrastructure 171. A rodada 4 não altera contagem: B1 mudou só a declaração do Red e B2 só um comentário. Core +4: o teste de 3 ou mais perguntas tem 4 casos, o de 2 perguntas substitui o de rejeição com os mesmos 4 casos, e `TutorReplyRejectionsTests` fica com 3. Parser +1. |
| Contagem D11 | ✅ 69, sem alteração. As contagens de Ollama (1+24+30+7, mais 4 de console e 3 de DI) seguem as da rodada 2. |
| Ordem das tarefas e Red da T10 | ✅ Cada asserção do Red bate com o `src` atual. Os testes que nascem verdes estão declarados, com mutação depois do Green. |
| AP-035 (D20 e D20c) | ✅ O D20c sobre `139adee` acha todos os padrões, incluindo `ReplySentencesTests.cs`. O D20 fica sem linha depois da T10 e da T12. |
| Consistência §3, §4 e §5 | ✅ Os nomes do método de `ReplySentencesTests` são iguais nas três seções. |

## Bloqueantes
Nenhum.

## Sugestões
- **S6** — `plan.md` §2.12, cabeçalho da tabela de conferência. O texto diz que o nome antigo do cenário "aparece em 5 arquivos de teste". O Grep dá 4 arquivos de teste com `reply com duas perguntas é inválido`: `ReplySentencesTests`, `TutorReplyRejectionsTests`, `TutorReplyValidatorTests` e `TutorReplyParserTests`. O inventário de 6 arquivos está certo: soma `TutorReplyValidator.cs` e `ClaudeCodeTutorBrainLiveTests.cs`, que casam outros padrões. Corrigir o número, ou dizer "5 arquivos de teste no inventário". Hoje o Executor, ao conferir o D20c, pode estranhar a diferença. Não bloqueia, porque o D20c já exige a lista dos 6 arquivos.
- As S1–S5 da rodada 3 seguem pendentes de decisão do usuário e não bloqueiam. Se o usuário quiser fechar alguma, a S3 é a mais útil: a T11 hoje só tem prova por `LiveBrain`.

## Pontos positivos
- A correção do B1 declarou o Red assertiva por assertiva. Escolheu a opção que mantém a frase da tolerância nas duas camadas e registrou o motivo em "Alternativas descartadas". Entrou também a mutação "frase sai da Message".
- O inventário do B2 foi refeito pelo Grep do D20c sobre `139adee`, arquivo por arquivo e com linhas. Há um risco novo na §7 e uma instrução para registrar na §12 se o resultado divergir da lista.
- O plano transcreveu os bloqueantes e deixou as S1–S5 como pendentes, sem alterar o plano.

## Aprendizados para o processo
- Confirma a regra sugerida na rodada 3: ao renomear um cenário, o inventário deve vir do Grep pelo nome antigo antes de fechar a tabela de conferência. A correção só funcionou porque o Grep foi refeito. Falta transformar isso numa regra ou skill do Planejador (variação de AP-035 e AP-040).
- Contagens citadas em prosa ("aparece em N arquivos") envelhecem. Preferir a lista nominal, que o D20c confere, a um número solto (S6).
