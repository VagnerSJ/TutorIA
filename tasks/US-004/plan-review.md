# Revisão do plano — US-004 Conversar por texto com o professor via Claude Code

## Rodada 1

Data: 2026-10-17
Escopo revisado: `tasks/US-004/plan.md` e `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md` (commit 75a726d, único commit da branch `feature/US-004_conversa-texto-claude-code`). Ainda não há código na branch. Também conferi o plano contra o código atual de `develop` (`EnglishTutorApp`, `ChatSession`, `ConversationService`, `LocalCommands`, `TutorReplyParser`, testes do Console), contra `specs/sprints/SPRINT_1.md` (US-004, US-005, US-006), `BUSINESS_RULES.md`, `ARCHITECTURE.md`, `specs/ROADMAP.md` §2, ADR-006 e as skills. A linha de base da suíte foi conferida com `dotnet test --filter "Category!=LiveBrain"`: Core 102, Infrastructure 24, Console 25, todos verdes, que é a base que o plano usa.
Resultado: REPROVADO (4 bloqueantes)

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD da US está no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ 9/9 cenários mapeados (o cenário 1 também tem caracterização offline com fixture) |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ❌ P5 (`is_error` com código 0) e P13 (normalização de `OutputMode` no adaptador) sem teste (B3, B4) |
| [O] A DoD da §10 cobre a DoD da US, com comandos verificáveis e resultado esperado (inclui Core ≥ 80%) | ✅ D5–D10 da sprint + D1–D4 comuns + D11–D15. O D12 tem um falso positivo (S7) |
| [O] Conformidade com R1–R4, `RN-*`, regras rápidas do `CLAUDE.md`; ADR Proposto para decisão relevante | ❌ ADR-007 Proposto e commitado (ok), mas os binds novos quebram a explicação de configuração inválida quando há mais de uma seção errada (B1; ARCHITECTURE §10) |
| [O] Escopo: nada além nem aquém; adiamentos registrados na spec de destino (AP-022) | ❌ US-006: texto exato conferido (ok). A P11 adia para a US-005 "mensagem clara com a sessão continuando", e a spec da US-005 não tem isso (B2) |
| [O] Consistência §3 × §4 × §5 × §6, contagens e nomes idênticos (AP-025) | ✅ 46 métodos / 54 casos; 205 = 103 + 39 + 63; `ClaudeCodeTutorBrainTests` 18/22 batem por tarefa; nomes da §5 iguais aos da §4. Há uma contagem intermediária errada na T15 (S8) |
| Transformações da §2 testadas nos dois lados do limite (AP-016); asserções com trecho do projeto (AP-015) | ⚠️ AP-015 ok. Os ramos de fallback do `<detalhe>` e o `session_id` vazio não têm teste (S5) |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ Os motivos de Red de T5–T8 e T13–T16 conferem com os estados descritos dos ramos. Os testes de caracterização (T2, T10, Ollama na T16) usam mutação temporária |
| Alteração na `ARCHITECTURE.md` lista também `CLAUDE.md` e skills que repetem a regra | ✅ Os textos antigos citados conferem, letra por letra, com `ARCHITECTURE.md` §3/§5/§6.3, `padroes-codigo` e `tdd-dotnet` |
| Riscos e ambiguidades que travariam o Executor tratados | ⚠️ B1, S4, S9, S11 |
| Viabilidade técnica no SDK e nas bibliotecas | ⚠️ B1 (comportamento do `ValidateOnStart` com várias Options inválidas) |

### Bloqueantes

- [ ] **B1** — Plano §2.6, "Composition root (`EnglishTutorApp`)", segundo item de `BuildHost` (binds de `ClaudeCodeOptions` e `ConversationOptions` com `ValidateOnStart`), e a frase "cai no `catch (OptionsValidationException)` que já existe". Hoje só `BrainOptions` é validada na partida. Com três classes de Options validadas na partida, a validação do `Microsoft.Extensions.Options` junta as falhas de **mais de um** tipo de Options numa `AggregateException`, não numa `OptionsValidationException`. Nesse caso, `EnglishTutorApp.RunAsync` cai no `catch (Exception)`: o aluno vê `An unexpected error occurred...`, o código de saída é 1 e nenhuma mensagem de validação aparece. Exemplo: `Brain:Provider = "Inexistente"` junto com `Conversation:MaxTurnsInWindow = 0`, ou com `Brain:ClaudeCode:OutputMode = "Json"`. Isso regride o comportamento fixado na US-001 (cenário "configuração do cérebro é validada na inicialização") e fere ARCHITECTURE §10 ("Erros de validação (... Options ...) dizem o que está errado e o que se espera"). **Esperado:** com qualquer combinação de chaves inválidas entre `Brain`, `Brain:ClaudeCode` e `Conversation`, a saída de erro mostra `Invalid configuration:` seguido da mensagem de **cada** validador que falhou, e o código é `ExitCodes.InvalidConfiguration`, sem cabeçalho nem prompt. O plano precisa decidir isso (P* com resposta), mapear um teste com duas seções inválidas ao mesmo tempo na §4, criar a tarefa na §5 e atualizar contagens, D2 e a linha de risco na §7. Cabe ao Executor confirmar no Red que a falha acontece pelo motivo descrito.

- [ ] **B2** — Plano §8, primeiro item ("Nada novo a registrar"), e P11 ("A US-005 troca isso por mensagem clara com a sessão continuando e atualiza este teste"). A spec da US-005 (`specs/sprints/SPRINT_1.md`, `## US-005`) tem cenários só para `CliNotFound`, `NotAuthenticated`, `UsageLimitReached` e `Timeout`. "Sessão continua" aparece apenas no D6, e só para `CliNotFound`. Nada na US-005 diz que uma falha `Unknown` (código ≠ 0 não classificado, saída que não é JSON, `is_error`), nem as falhas em geral, mostra mensagem clara **e mantém a sessão aberta**, nem que `EnglishTutorAppTests.RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError` muda. É o adiamento sem dono do AP-022. **Esperado:** a §2.8 traz o texto exato a acrescentar na US-005 (cenário ou DoD extra: qualquer falha do cérebro num turno do console, inclusive `Unknown`, mostra mensagem clara e a sessão continua), aplicado no commit `docs(ai): update spec for US-004` antes da T1, e a §8 deixa de dizer "Nada novo a registrar" (checklist §0, AP-022).

- [ ] **B3** — Plano §2.5, tabela de `ClaudeCodeOutput.Read`, linha `is_error: true`, e o teste `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail`. O caso `"is-error"` usa código de saída 1, então cai na primeira linha (`ExitCode != 0`), e a linha `is_error: true` com código 0 nunca é exercitada. Uma implementação que só olha o código de saída passaria em todos os testes e mandaria o `result` de erro ao parser. Efeito para o aluno: `InvalidOutput` → retentativa → texto do erro da CLI exibido como fala do professor (fallback cru), em vez de `BrainException(Unknown)`. O caso é plausível com `--json-schema` quando a saída estruturada falha na CLI. **Esperado:** a P5 tem teste para "código 0 e `is_error: true`" resultando em `BrainException` `Unknown` com o detalhe, sem chamar o parser. O texto da mensagem para esse caso está definido na tabela da §2.5 (hoje ela diria `failed (exit code 0)`). Contagens, §5 e D11 ficam atualizados (checklist §0, P* com efeito observável).

- [ ] **B4** — Plano §2.2 (`BrainOutputModes.TryGetCanonical`, "sem diferenciar maiúsculas") e §2.4 (`ClaudeCodeTutorBrain.SendAsync`, passo 6: "se `OutputMode = JsonSchema`"). O validador aceita `"tags"`/`"jsonschema"` em qualquer caixa (teste `Validate_WithAcceptedOutputMode_Succeeds` com `"tags"`), mas nada no plano diz que o adaptador usa o valor **canônico**, e nenhum teste do adaptador usa caixa diferente. Com `"OutputMode": "jsonschema"` no `appsettings.json`, a configuração passa na validação, e uma comparação exata no adaptador rodaria em silêncio no modo Tags (sem `--json-schema`, com `format.tags.md`) e logaria o valor cru no evento 2001. É o mesmo padrão da S1 da US-001 (normalização sem dono nem teste). **Esperado:** a P13 diz quem normaliza `OutputMode` para uso (adaptador ou Options) e a §4/§5 mapeiam um teste em que um valor aceito com outra caixa produz os argumentos e o arquivo de formato do modo correspondente (checklist §0, P* com normalização).

### Sugestões

- **S1** — ADR-007, "Consequências". Com a regra de extensão exata, a janela desliza a partir do turno `MaxTurnsInWindow + 1` (padrão 10), e daí em diante **nenhum** turno volta a usar `--resume`: cada turno abre sessão nova com a janela completa. A otimização só vale nos primeiros N turnos. Vale registrar isso de forma explícita, porque afeta a leitura da latência (D7) e do `eval` (US-009). Registrar também que cada sessão nova fica gravada pela CLI no perfil do usuário (fora da pasta de trabalho), com o texto do aluno.
- **S2** — ADR-007 item 3 e plano §2.4, passo 3 (`resumeId`). A identidade da sessão compara só `Messages` e ignora `SystemPrompt`. O ADR-006 item 5 promete ler `tutor.md` a cada turno, e o ROADMAP §2 diz que, com `--resume`, a CLI reutiliza o system prompt gravado no primeiro turno. Se `tutor.md` mudar no meio da sessão, o adaptador retoma com o prompt antigo, o que deixa de ser equivalente ao `BrainRequest` (RN-BRAIN01: estado do provedor é só otimização). Esperado: a sessão só é reaproveitada se o system prompt também for o mesmo, ou então a consequência fica registrada no ADR.
- **S3** — Plano §2.1 ("`Win32Exception` ... propaga") × texto proposto para ARCHITECTURE §6.3 na §2.8 ("Até a US-005, toda falha do processo vira `BrainException` com `Unknown`") × `padroes-codigo` "Erros" ("o Core não conhece `Win32Exception`"). O texto que vai para a fonte da verdade contradiz o §2.1. Esperado: alinhar os dois. Ou o executável inexistente também vira `Unknown` nesta US, ou o texto da ARCHITECTURE e o item 5 do ADR-007 dizem a exceção.
- **S4** — Plano §2.1, `ProcessRunner`. Se o processo terminar logo (argumento recusado, versão antiga da CLI), a escrita do stdin pode falhar com erro de pipe fechado antes de se ler o código de saída e o stderr. O aluno e o log perdem o detalhe que a §2.5 promete. A ordem "escreve o stdin e o fecha, e lê stdout e stderr ao mesmo tempo" também é ambígua. Esperado: o plano diz que a leitura de stdout/stderr já está em andamento durante a escrita, e que uma saída antecipada do processo ainda chega ao adaptador com código de saída e stderr.
- **S5** — Plano §2.5, regra do `<detalhe>` e linha `session_id` (AP-016). Estão sem teste: (a) `is_error` com `result` em branco → primeira linha do stderr; (b) nada no `result` nem no stderr → `no error details were printed`; (c) `session_id` vazio ou ausente → o turno seguinte não usa `--resume`. Cada um é uma transformação de uma linha que a cobertura não vê.
- **S6** — Plano §2.4, passo 5 ("apagado num `finally`"). O teste `SendAsync_Always_PassesSystemPromptInTemporaryFileOutsideWorkingDirectory` só confere a remoção no caminho feliz. Efeito esperado: nenhum arquivo `englishtutor-system-prompt-*.md` fica na pasta temporária depois de uma falha do processo ou de um cancelamento.
- **S7** — D12 (`Get-ChildItem -Force -Recurse ... | Measure-Object`). Se a pasta não existir, o comando também dá `0`, porque o erro do `Get-ChildItem` não conta. O esperado "a pasta existe e está vazia" não fica provado. Esperado: o comando confere também a existência da pasta (ex.: `Test-Path` → `True`).
- **S8** — T15, "Os 25 testes do Console continuam verdes". Nesse ponto já existem 30 (25 + T1 1 + T14 4). As contagens finais (§4, D2) estão certas, mas o número intermediário confunde o Executor no passo de Refactor (AP-031).
- **S9** — T9, `LiveClaudeCode`. Os testes `LiveBrain` precisam de `BrainRequest.SystemPrompt`, mas `FileTutorPromptSource` só nasce na T12. O plano não diz de onde vem o system prompt nesses testes. Esperado: definir (ler `prompts/tutor.md` da saída ou antecipar a T12), para os testes ao vivo usarem o mesmo prompt que o app usa.
- **S10** — Tipos e escopos de commit. Na T11, `feat(infra): measure claude code latency per output mode` só mexe em código do projeto de teste (`LatencySummary`, teste `LiveBrain`): o tipo esperado é `test(infra)`. Na T15, `feat(console)` altera `LocalCommands.Available`, que fica no Core: o escopo esperado é `core`, ou um commit separado.
- **S11** — §7, riscos de isolamento (RN-BRAIN06). A pasta de trabalho dedicada isola o `CLAUDE.md` e os hooks de **projeto**, mas não as configurações de nível de usuário (`~/.claude/CLAUDE.md`, hooks em `~/.claude/settings.json`), que podem entrar no contexto ou rodar a cada turno. Esperado: o risco aparece na §7, com a mitigação decidida (ex.: conferir na documentação da CLI se há flag para restringir as fontes de configuração) ou aceito de forma explícita.
- **S12** — P6 / §8. O `checklist-revisao` §4 tem o item [O] "Processos externos com `ArgumentList`, timeout e encerramento da árvore". Esta US não tem timeout, por decisão da spec (US-005, cenário "tempo esgotado"). Registrar isso de forma explícita evita um bloqueante falso na revisão de código: o item vale como N/A nesta US, com a referência à P6.
- **S13** — T10, `SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId`. Falta dizer se a correção `since five years` → `for five years` é conferida por igualdade ou por "contém" (o teste `LiveBrain` usa "contém", sem diferenciar maiúsculas). O texto real do modelo pode trazer a frase inteira em `Original`.

### Pontos positivos

- O ADR-007 trata de frente a dúvida que o ADR-006 deixou aberta (duplicação de contexto com `--resume`). A regra de extensão exata casa bem com a retentativa: a correção de formato reaproveita a sessão, e o turno seguinte abre sessão nova.
- O §4 aplica com rigor o AP-017/026: `session_id`, `Model`, `ExecutablePath`, system prompt e respostas distintos por turno e por campo, e a resposta rejeitada difere da aceita.
- As contagens batem por tarefa, por classe e no total (46/54; 205 = 103 + 39 + 63), conferidas contra a suíte atual.
- Os textos antigos da §2.8 conferem, letra por letra, com `ARCHITECTURE.md`, `padroes-codigo` e `tdd-dotnet`. A divergência antiga sobre `ValidateDataAnnotations` foi notada e corrigida.
- O cenário novo da US-006 reaproveita os critérios já fixados (`Tutor: <reply>`, sem `The tutor is not connected yet.`), como pede o AP-024.
- Cada tarefa diz o estado dos ramos que ainda não existem (T5: `NotImplementedException("US-004 T7")`, `JsonException`, stdin só com a última mensagem), seguindo o AP-018(b). Os motivos de Red da T7 e da T8 decorrem desse estado.
- O D15 usa a forma única do AP-019. O D6 mantém o pipe no Windows PowerShell 5.1 (AP-006).
- O cuidado com o `.cmd` do npm e o JSON Schema na linha de comando está registrado no ADR e no §7.

### Aprendizados para o processo

- **B1 (padrão novo):** acrescentar uma Options com `ValidateOnStart` muda o tipo da exceção de partida quando mais de uma falha ao mesmo tempo. Nenhum teste existente pega isso, porque cada um invalida uma seção só. Proposta: na skill `padroes-codigo`, seção "DI e configuração", incluir "com mais de uma classe de Options validada na partida, a mensagem de configuração inválida cobre falhas simultâneas de seções diferentes (com teste)". Na lista do Planejador: "US que adiciona `ValidateOnStart` mapeia um teste com duas seções inválidas".
- **B2 (recorrência do AP-022):** o adiamento ficou escrito numa P* ("a US-005 troca isso..."), e não só na §8. Por isso a conferência da §8 ("Nada novo a registrar") não o pegou. Proposta: no `.claude/agents/planejador.md` e no `checklist-revisao` §0, procurar adiamentos também nas P* e nos comentários de teste ("a US-YYY troca/atualiza"), não só na §8.
- **B4 (recorrência da S1 do `plan-review` da US-001):** normalização aceita pelo validador sem dono no consumidor. Proposta: no checklist de revisão de plano, "valor aceito sem diferenciar maiúsculas pelo validador tem teste no consumidor com caixa diferente".
- **B3/S5 (variação do AP-016):** em tabelas de decisão com linhas sobrepostas (código ≠ 0 e `is_error`), um caso de teste pode cair numa linha anterior e deixar a seguinte sem cobertura. Proposta: na skill `tdd-dotnet`, "em tabela de decisão, cada linha tem um caso que só ela satisfaz".

## Rodada 2

Data: 2026-10-17
Escopo revisado: `tasks/US-004/plan.md` e `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md` no commit c09f820 (`docs(ai): revise plan for US-004`). Comparei com a rodada 1 (`git diff 4e7b224 HEAD`) e conferi o plano contra o código atual de `develop`: `EnglishTutorApp`, `ConversationService`, `ConversationWindow`, `CoreServiceCollectionExtensions`, `BrainOptionsValidator`, `ConversationOptionsValidator`, `TutorReplyParser` e `ChatSessionTests`. Também conferi contra `specs/sprints/SPRINT_1.md` (US-005 e US-006) e `BUSINESS_RULES.md` (RN-CONV03). Ainda não há código na branch. Não rodei a suíte de novo: a linha de base da rodada 1 (Core 102, Infrastructure 24, Console 25) continua valendo, porque o c09f820 só mexe em documentos.
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD da US está no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ 9/9 |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P5 (linha 3 da §2.5), P13 (normalização no adaptador) e P19 (falhas em várias seções) agora têm teste e tarefa |
| [O] A DoD da §10 cobre a DoD da US, com comandos verificáveis e resultado esperado (inclui Core ≥ 80%) | ✅ D2 = 214 e D11 = 29/20 batem com a §4. O D12 agora confere que a pasta existe |
| [O] Conformidade com R1–R4, `RN-*`, regras rápidas do `CLAUDE.md`; ADR Proposto para decisão relevante | ✅ O ADR-007 foi revisado e continua Proposto. A P19 mantém as mensagens de validação na partida (ARCHITECTURE §10) |
| [O] Escopo: nada além nem aquém; adiamentos registrados na spec de destino (AP-022) | ✅ Conferi os textos da US-005 (cenário novo + D7) e da US-006 contra o `SPRINT_1.md`. As âncoras existem: a linha 327 está dentro do bloco `gherkin`, a linha D6 é a 335, e não havia D7 na US-005 |
| [O] Consistência §3 × §4 × §5 × §6, contagens e nomes idênticos (AP-025) | ✅ Recontei método a método. `ClaudeCodeTutorBrainTests` 20/29 (T5 5/6 + T6 4/5 + T7 4/7 + T8 5/9 + T10 2/2). Infrastructure 34/46, Console 14/16, Core 1/1, total 49/63, suíte 214. O número intermediário da T15 (30 casos) está certo |
| Transformações da §2 testadas nos dois lados do limite (AP-016); asserções com trecho do projeto (AP-015) | ⚠️ Os três ramos do `<detalhe>` e a primeira linha do stderr estão cobertos. A condição "JSON válido que não é um resultado da CLI" da linha 2 continua sem caso (S2) |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ T5 deixa `OutputMode` com comparação exata e `is_error` lançando `NotImplementedException("US-004 T7")`, então a T6 (outra caixa) e a T7 (código 0) nascem vermelhas. T16 → T17: com os binds da T16, os dois casos da T17 falham com código 1, e o Executor confirma o motivo no log 1003 |
| Alteração na `ARCHITECTURE.md` lista também `CLAUDE.md` e skills que repetem a regra | ✅ Sem mudança desde a rodada 1. O texto novo da §6.3 agora diz o que acontece com o executável inexistente, alinhado com a §2.1 |
| Riscos e ambiguidades que travariam o Executor tratados | ✅ S4 da rodada 1 (pipe fechado), S9 (system prompt nos testes `LiveBrain`) e S11 (configuração de usuário da CLI) foram tratados |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ Com mais de uma Options inválida, o validador de partida do `Microsoft.Extensions.Options` lança `AggregateException` só com `OptionsValidationException`. Com uma só, relança a própria `OptionsValidationException`. A T17 manda confirmar isso no Red |

### Itens da rodada anterior

Bloqueantes:
- [x] **B1** — Resolvido em c09f820. A P19 decide o tratamento e a §2.6 (composition root) aceita `OptionsValidationException` ou `AggregateException` só com `OptionsValidationException`: escreve `Invalid configuration:` + uma linha por falha, loga `StartupFailed` com todas e devolve `ExitCodes.InvalidConfiguration`. A §2.9 cobre a `AggregateException` mista. O teste é `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration`, com 2 casos (Provider + janela, OutputMode + janela). As mensagens esperadas batem com as de `BrainOptionsValidator` e `ConversationOptionsValidator`. Também foram atualizados a T17, a §7, o D2 e as contagens.
- [x] **B2** — Resolvido em c09f820. A §2.8 traz o cenário "falha do cérebro no console não encerra a sessão" e a linha D7 da US-005, com texto antigo de referência e critérios reaproveitados (saída de erro, `/quit` → 0, `Claude Code failed (exit code <n>): ...`). A §8 deixou de dizer "Nada novo a registrar", e a P11 e o comentário do teste apontam para o cenário.
- [x] **B3** — Resolvido em c09f820. A tabela da §2.5 agora é numerada e ordenada. A linha 3 tem a mensagem própria `Claude Code reported an error: <detalhe>` e não chama o parser. O teste `SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing` usa código 0 e um `structured_output` **válido**, então só passa se o `is_error` for respeitado. Também foram atualizados D11, T7 e o ADR-007, item 5.
- [x] **B4** — Resolvido em c09f820. A §2.2 e a P13 tornam o adaptador o dono da normalização. Os passos 2, 6 e 10 da §2.4 usam o modo canônico, e o teste `SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog` (`"jsonschema"`, `"TAGS"`) confere argumentos, arquivo de formato e log 2001. O estado da T5 garante o Red.

Sugestões (o planejador incorporou todas, §12):
- S1 resolvido (ADR-007 "Consequências", §7), mas com um erro de contagem herdado da própria S1. Ver S1 abaixo.
- S2, S3, S4, S6, S7, S8, S9, S10, S11, S12 e S13 resolvidos em c09f820.
- S5 resolvido em c09f820: (a) e (b) nos casos de B3; (c) com `session_id` vazio. O `session_id` **ausente** não tem caso, o que é aceitável, porque é o mesmo ramo "senão `null`".

### Bloqueantes

Nenhum.

### Sugestões

- **S1** — ADR-007, "Consequências", terceiro item ("a partir do turno `MaxTurnsInWindow + 1` (padrão 11)"), e plano §7, linha "`--resume` só ajuda nos primeiros `MaxTurnsInWindow` turnos". O erro veio da S1 da rodada 1, que dizia "padrão 10". Pelo `ConversationWindow.Add` (guarda os últimos N turnos **anteriores**), o request do turno 11 ainda leva os turnos 1–10 mais a frase nova. Ele estende exatamente a sessão e retoma. A janela só desliza no turno 12. **Esperado:** "a partir do turno `MaxTurnsInWindow + 2` (padrão 12)" e "nos primeiros `MaxTurnsInWindow + 1` turnos", ou outra formulação equivalente que bata com `ConversationWindow`. Isso afeta a leitura da taxa de sessões novas no `eval` (US-009).
- **S2** — Plano §2.5, linha 2 ("stdout não é objeto JSON com `"type": "result"`"), e o teste `SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation`. O único caso é `not json at all`. Uma implementação que só trate `JsonException` passaria. Com um JSON válido que não é o resultado da CLI (outro `type`, array), o fluxo cairia na linha 5 com `RawText = ""`: `InvalidOutput` → retentativa → fallback cru vazio na tela. **Esperado:** um JSON válido sem `"type": "result"` também resulta na `BrainException(Unknown)` da linha 2 (AP-016, a condição tem dois lados).
- **S3** — Plano §3, `Support/ClaudeOutputs.cs`. A descrição de `Structured`, `Text` e `Error` não diz que o stdout montado tem `"type": "result"`. Se `Error(...)` omitir esse campo, a linha 2 absorve os casos de `is_error` e os testes de B3 falham pelo motivo errado (ou passam por acaso no caso de código ≠ 0). **Esperado:** a §3 diz que os três auxiliares produzem o objeto de resultado da CLI com `"type": "result"`, no mesmo formato das fixtures gravadas.
- **S4** — Plano §2.6, `BuildHost`, bind de `ClaudeCodeOptions` com `ValidateOnStart`. A seção é validada mesmo com `Brain:Provider = Ollama`. Até a US-007, um `Brain:ClaudeCode` inválido impede o uso do Ollama, que nem o usa. É coerente com o registro fixo da P17, mas não está escrito. **Esperado:** registrar na §7 ou na P17 como comportamento aceito até a `BrainFactory`.

### Pontos positivos

- Cada bloqueante tem resposta rastreável na §12, com a lista exata das seções tocadas. Facilitou a conferência item por item.
- O teste de B3 usa um `structured_output` válido junto com `is_error`, de modo que ignorar o erro devolva uma resposta e o teste falhe. É o uso certo de "um caso que só a linha satisfaz", aplicado ao próprio plano (§2.5: "Cada linha de falha tem ... um caso de teste que só ela satisfaz").
- A T5 descreve o estado provisório dos ramos novos (comparação exata de `OutputMode`, `is_error` com qualquer código). Assim, os testes de B3 e B4 têm Red garantido nas tarefas seguintes (AP-018(b)).
- A T17 manda o Executor confirmar no log que o Red vem da `AggregateException` e parar se não vier. A dúvida de viabilidade virou verificação, não suposição.
- Os textos para a US-005 reaproveitam critérios já fixados (saída de erro, código 0 no `/quit`, mensagem `Claude Code failed ...`), seguindo o AP-024. Delimitar "que chegue ao console" exclui o `InvalidOutput` de forma correta (ADR-006).
- O system prompt entrou na identidade da sessão (S2 da rodada 1) com um caso próprio, `"system-prompt-changed"`, de valores distintos.

### Aprendizados para o processo

- **S1 (erro do próprio Revisor, propagado):** uma afirmação numérica da revisão ("a partir do turno N + 1") foi copiada para o ADR sem conferência contra o código. Proposta: no `checklist-revisao`, seção "Como escrever bloqueantes e sugestões", acrescentar "afirmação quantitativa sobre comportamento existente (limites, contagens, a partir de qual turno) cita o símbolo que a sustenta (ex.: `ConversationWindow.Add`)". No `.claude/agents/planejador.md`: "número vindo da revisão é conferido no código antes de ir para ADR ou spec".
- **S2/S3 (variação do AP-016 em tabelas de decisão):** a linha 2 tem condição composta ("não é JSON" ou "não é `type: result`"), e o auxiliar de teste que monta a entrada decide em silêncio qual linha casa. Proposta: na skill `tdd-dotnet`, junto com a regra proposta na rodada 1 ("cada linha tem um caso que só ela satisfaz"), acrescentar: "condição composta de uma linha tem um caso por parte; auxiliares de teste que montam a entrada declaram os campos que decidem a linha".

## Rodada 3

Data: 2026-10-17
Escopo revisado: `tasks/US-004/plan.md` e `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md` no commit 40e2b4a (`docs(ai): revise plan for US-004`), comparados com 2cacfd7 (`git diff 2cacfd7 40e2b4a`). O 137981c só registra o pedido do usuário na §12. Conferi o plano contra o código de `develop` (`ConversationWindow.Add`, `ConversationService.SendAsync`, `ExitCodes`), contra `specs/sprints/SPRINT_1.md` (US-007) e contra `BUSINESS_RULES.md` (RN-BRAIN08). Ainda não há código na branch. Não rodei a suíte de novo, porque o 40e2b4a só mexe em documentos e a linha de base da rodada 1 (Core 102, Infrastructure 24, Console 25) continua valendo. A verificação de caracteres invisíveis no diff da rodada (forma do AP-019) deu 0.
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD da US está no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ 9/9, sem mudança |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P5 agora cobre as duas partes da linha 2. A P17 ganhou uma consequência aceita, que usa o mecanismo de configuração inválida já testado |
| [O] A DoD da §10 cobre a DoD da US, com comandos verificáveis e resultado esperado (inclui Core ≥ 80%) | ✅ D2 = 216 (103 + 41 + 72) e D11 = 31 casos / 20 métodos batem com a §4 |
| [O] Conformidade com R1–R4, `RN-*`, regras rápidas do `CLAUDE.md`; ADR Proposto para decisão relevante | ✅ O ADR-007 continua Proposto. O item 5 e as "Consequências" estão alinhados com a §2.5 e a §7 |
| [O] Escopo: nada além nem aquém; adiamentos registrados na spec de destino (AP-022) | ✅ A S4 foi registrada **sem** promessa para a US-007, então não há adiamento a conferir. A justificativa cita a US-007 de forma inexata (S1) |
| [O] Consistência §3 × §4 × §5 × §6, contagens e nomes idênticos (AP-025) | ✅ Recontei. `ClaudeCodeTutorBrainTests` tem 14 `[Fact]` + 6 `[Theory]` (2+2+5+2+3+3) = 20 métodos / 31 casos. Por tarefa: T5 5/6 + T6 4/5 + T7 4/9 + T8 5/9 + T10 2/2. Infrastructure tem 34/48, Console 14/16, Core 1/1, total 49/65 e suíte 216. Não sobrou nenhuma contagem antiga (214, 63, 46, 70, 29) fora do histórico da §12 |
| Transformações da §2 testadas nos dois lados do limite (AP-016); asserções com trecho do projeto (AP-015) | ✅ A linha 2 tem um caso por parte da condição, e o outro lado (objeto com `"type": "result"`) é exercitado por todos os testes do caminho feliz. O "objeto sem `type`" citado na tabela cai no mesmo ramo de "outro `type`" e não tem caso próprio. É aceitável, pelo mesmo critério do `session_id` ausente na rodada 2 |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ Pelo estado descrito na T5, o caso `"other-type"` devolve `Reply t1?` sem exceção, o `"json-array"` lança `InvalidOperationException` (o `JsonElement` acessado por propriedade num array) e o `"not-json"` lança `JsonException`. Os três ficam vermelhos na T7 pelo motivo listado |
| Alteração na `ARCHITECTURE.md` lista também `CLAUDE.md` e skills que repetem a regra | ✅ Sem mudança desde a rodada 2 |
| Riscos e ambiguidades que travariam o Executor tratados | ✅ A T10 manda parar se as fixtures reais divergirem dos auxiliares de `ClaudeOutputs`. A §7 ganhou as linhas da S2 e da S4 |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ Os argumentos da §2.4 não incluem `--verbose`. Por isso a CLI imprime um objeto único, e tratar array como "não é resultado" não rejeita a saída legítima |

### Itens da rodada anterior

Sugestões (o usuário pediu a aplicação de S1–S4, §12):
- [x] **S1**: resolvida em 40e2b4a. O ADR-007 ("Consequências") e a §7 dizem agora "turnos 2 a `MaxTurnsInWindow + 1` (padrão: 2 a 11)" retomam e "a partir do turno `MaxTurnsInWindow + 2` (padrão: 12)" abrem sessão nova. Conferi no código. `ConversationService.SendAsync` monta o request com `_window.ToMessages()` + a frase nova e só depois chama `_window.Add`. `ConversationWindow.Add` remove o turno mais antigo quando `_turns.Count > _maxTurns`. A §12 mostra a conta passo a passo e cita os símbolos, como pedia o aprendizado da rodada 2.
- [x] **S2**: resolvida em 40e2b4a. A linha 2 da §2.5 tem a condição composta explícita. `SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation` virou `[Theory]` com `"not-json"`, `"other-type"` (com `structured_output` válido, então só passa se o `type` for conferido) e `"json-array"`. Também foram atualizados as contagens, a T5 (estado), a T7 (motivo do Red por caso), a §7, a P5, o D2, o D11 e o item 5 do ADR-007.
- [x] **S3**: resolvida em 40e2b4a. A §3 declara que `Structured`, `Text` e `Error` produzem o objeto de resultado com `"type": "result"` e lista os campos que decidem a linha. A T10 confere as fixtures reais contra esse formato.
- [x] **S4**: resolvida em 40e2b4a (§7, P17), com o ajuste de não prometer mudança na US-007, que está correto quanto ao AP-022. A justificativa tem um erro de leitura da spec (S1 abaixo).

### Bloqueantes

Nenhum.

### Sugestões

- **S1**: plano §7, linha "`Brain:ClaudeCode` inválido impede a partida também com `Brain:Provider = Ollama`"; §9 P17 ("lá o ClaudeCode pode ser o `FallbackProvider` de um Ollama principal"); e a resposta à S4 na §12 ("cenário 'fallback por limite de uso'"). O cenário citado da US-007 (`SPRINT_1.md`, `## US-007`) é o contrário: `Provider "ClaudeCode" e FallbackProvider "Ollama"`. Os gatilhos da RN-BRAIN08 (`UsageLimitReached`, `Timeout`, `CliNotFound`, `NotAuthenticated`) são falhas do Claude Code, o que indica Ollama como reserva, não como principal. A RN-BRAIN08 não proíbe a combinação inversa, então "pode ser" é defensável, mas atribuir isso ao cenário é inexato, e a US-007 pode herdar esse argumento como se viesse da spec. A decisão (aceito, sem pendência para outra US) não muda. **Esperado:** a justificativa se apoia no que a spec diz (registro fixo da P17; a US-007 decide a validação condicional na própria spec), sem citar o cenário de fallback como exemplo de ClaudeCode reserva de Ollama.

### Pontos positivos

- A resposta à S1 confere a contagem no código, citando `ConversationWindow.Add` e `ConversationService.SendAsync`, antes de alterar o ADR. É o que o aprendizado da rodada 2 propôs.
- O caso `"other-type"` repete o padrão do teste de B3: um `structured_output` válido junto com o defeito, para que ignorar a condição devolva uma resposta e o teste falhe.
- A T5 descreve até o comportamento provisório do array (`InvalidOperationException`), e o Red de cada caso da T7 fica previsível (AP-018(b)).
- O planejador recusou, com motivo, a expressão "até a US-007" da S4, que criaria um adiamento sem registro na spec de destino (AP-022). Contestar a sugestão do Revisor com base nas regras do processo é o comportamento esperado.
- As contagens foram atualizadas em todos os lugares (cabeçalho, detalhamento, T7, conferência por tarefa, D2, D11) e não sobrou número antigo.

### Aprendizados para o processo

- **S1 (variação do aprendizado da S1 da rodada 2):** desta vez a afirmação sem conferência veio do Planejador e cita a spec, não o código: um cenário da US-007 foi usado como argumento com os papéis invertidos. Proposta: no `.claude/agents/planejador.md`, estender a regra proposta na rodada 2 ("número vindo da revisão é conferido no código") para "argumento que cita cenário ou regra de outra US cita o nome do cenário e confere o `Dado` no `SPRINT_N.md` antes de escrever". No `checklist-revisao` §0, conferir também as justificativas que citam outras US, não só os textos a registrar na spec de destino.
