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

## Rodada 4

Data: 2026-10-19
Escopo revisado: `tasks/US-004/plan.md` no commit 72a952b (`docs(ai): revise plan for US-004`), comparado com 037f577 (`git diff 037f577 72a952b`), e a spec em ea8744d (`specs/BUSINESS_RULES.md` RN-FEED01 e histórico; `specs/sprints/SPRINT_1.md` US-002 "reply sem pergunta", cenário 1 da US-004, os dois cenários novos e D6). Só revisei o que é novo ou mudou: §2.9 (guardas novas), §2.10.1–§2.10.6, as alternativas novas, §3, §4 (mapa, tabela "Bordas do separador", contagens), §5 T18–T23, §6, §7, §8, P18 e P20–P26, §10 (D2, D5, D6, D7, D16–D18 e a DoD comum) e §11/§12. Conferi tudo contra o código atual da branch: `TutorReplyRejections`, `TutorReplyValidator.Validate`, `TutorReplyRejectionsTests`, `TutorReplyValidatorTests`, `TutorReplyParserTests`, `ClaudeCodeTutorBrainLiveTests`, `FixtureRecorderLiveTests`, `ClaudeCodeTutorBrainTests` (asserções `EndWith("?")`) e as fixtures `TutorReply/*` e `ClaudeCode/*`. Também conferi o texto exato dos trechos "antigos" citados na §2.10.5 e na §2.10.6 (prompts, ADR-002, ADR-005, `ARCHITECTURE.md` §3/§6.2, `docs/us/US-002.md`, `specs/PRODUCT.md`). Rodei `dotnet test --filter "Category!=LiveBrain"` para confirmar a linha de base: 216 verdes (Core 103, Infrastructure 72, Console 41). A verificação de caracteres invisíveis (forma do AP-019) deu 0 no diff da rodada e no ea8744d.
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD da US está no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ "pergunta seguida de incentivo é aceita" e "reply com duas perguntas é inválido" estão no parser (cenário literal) e no validador (bordas). O cenário 1 foi renomeado para `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion`. O "reply sem pergunta" da US-002 está mapeado com o motivo novo no validador, no parser (JSON e tags) e no catálogo |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P20 (`Split_*`, T18), P21 (`IsQuestion_*` e o caso `Really?!` do validador, T18/T20), P22 (casos `Mr. Smith` e `said "Really?" Then`, T20), P23 (`ReplyMustAskExactlyOneQuestion_WithQuestionCount_*`, T19), P24 (T21), P26 (D5/T23). P25 não tem efeito observável e é coberta pelo D16 |
| [O] A DoD da §10 cobre a DoD da US, com comandos verificáveis e resultado esperado (inclui Core ≥ 80%) | ✅ O D5 está mais rigoroso que a spec (5 rodadas seguidas, camada e ambiente explícitos, o que atende ao pedido do usuário e à S3 do `review.md`). O D6 reflete o texto novo da spec. O D18 reaproveita o D5 da US-002. O D3b (Core ≥ 80%) continua. Há um detalhe de coerência entre a T23 e o D7 (S1) |
| [O] Conformidade com R1–R4, `RN-*` citadas, regras rápidas do `CLAUDE.md`; ADR Proposto para decisão relevante | ✅ O `ReplySentences` fica no Core, só com BCL (R1), e a regra fica no `TutorReplyValidator` (R4, ADR-005). Não há ADR novo. A revisão do ADR-005 (Aceito) só muda a lista de regras, não a decisão de onde elas moram. Isso é aceitável e já tem precedente (ADR-003 revisado). O texto da regra segue a RN-FEED01 de ea8744d, com a leitura da P21 (`?!` conta como pergunta) submetida à aprovação do usuário |
| [O] Escopo: nada além nem aquém; adiamentos registrados na spec de destino (AP-022) | ✅ A §8 não cria adiamento com US de destino. A S2 e a S4 do `review.md` ficam explicitamente fora desta revisão, "para decisão do usuário" (ver Pontos de atenção em S7) |
| [O] Consistência §3 × §4 × §5 × §6, contagens e nomes idênticos (AP-025) | ✅ Recontei. Core: `ReplySentencesTests` 3/19 (10+2+7), `TutorReplyRejectionsTests` +1/+3, `TutorReplyValidatorTests` +2/+9 (4+4+1) = +6/+31. Infrastructure: +2/+2. Total: +8/+33. O resultado é 57/98, e a suíte vai a 249 (134 + 41 + 74), conferido com a linha de base que rodei (103/41/72). A conferência por tarefa (Core 7/32, Infrastructure 36/50) bate. Os nomes da §5 (T18–T21) são idênticos aos da §4. A §3 lista todos os arquivos de código, teste, prompt e documento tocados |
| Transformações da §2 testadas nos dois lados do limite (AP-016); asserções com trecho do projeto (AP-015) | ✅ Testado nos dois lados: separador com `?`+fecho+minúscula × maiúscula (casos 3/4), reticências+minúscula × maiúscula (5/6), terminador+não branco × espaço (8), corte em 80 (80/81) e contagem 0 × 2 × 3. As mensagens conferidas usam trechos próprios (`has no question`, `has 2 questions`, `Expected 1 to 3 short sentences with exactly one question`). O D16 só verifica um lado (S3) |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ Simulei cada caso contra o código atual. T18 e T19 falham com `NotImplementedException` (o esqueleto da T19 mantém a fábrica antiga, que o validador ainda usa). Na T20, os 4 casos "uma pergunta em qualquer posição" terminam em `.` e a regra atual os recusa. Os 4 de "mais de uma pergunta" terminam em `?` e a regra atual os aceita. Os 3 de "sem pergunta" e os 2 renomeados do parser falham no `Reason`. Os 2 novos do parser falham pelo motivo inverso. A T18 não faz nada da T20 nascer verde, porque o validador só muda na T20. A T21 não tem Red declarado e é ajuste de asserção pela regra nova, com a justificativa na §4 |
| Alteração na `ARCHITECTURE.md` lista também `CLAUDE.md` e skills que repetem a regra | ✅ Nenhuma linha do `CLAUDE.md` nem das skills repete a RN-FEED01 ou a §6.2. Só um nome de exemplo no `tdd-dotnet` ainda usa a ideia antiga (S5) |
| Riscos e ambiguidades que travariam o Executor tratados | ✅ A §7 traz 5 riscos novos. A T23 manda parar na primeira rodada vermelha do D5. O impacto do separador na contagem de frases do D5 está subestimado na P22 (S4) |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ `char.IsWhiteSpace`/`char.IsLower` e a BCL bastam. Todas as fixtures válidas atuais (`json-valid`, `json-no-errors-with-praise`, `json-three-corrections`, `tags-valid`, `tags-labeled-lines`, `claude-json-structured`, `claude-json-tags`) têm exatamente uma pergunta pelo separador proposto. O único reply de teste com pergunta no meio é `"Is it good? I like that."`, que a §2.10.4 já trata. Nenhum teste do `ConversationService` ou do console confere o texto do motivo antigo |

### Itens da rodada anterior

- **S1 (Rodada 3)**: não aplicada, por decisão registrada pelo usuário (§12, "A sugestão S1 da rodada 3 ... não foi aplicada; a decisão da P17 não muda"). Fora do escopo desta rodada. A P17 segue com a mesma justificativa.
- **B1 do `review.md`** (D5 intermitente), contexto desta revisão: o plano trata a causa pela decisão do usuário (RN-FEED01 nova, ea8744d), com as tarefas T18–T22, e prevê nova evidência em 5 rodadas (T23). A situação do B1 só pode ser registrada na próxima rodada da revisão de código, com a evidência da T23.

### Bloqueantes

Nenhum.

### Sugestões

- **S1**: cabeçalho do plano (linha `Status:`) e §5 T23 × §10 D7. O status diz "§2.10 e T18–T22", mas as tarefas novas vão até a T23. A T23 manda "rodar D1–D18", e a linha do D7 diz "não precisa ser repetido na T23". **Esperado:** o status cita T18–T23, e a T23 e o D7 dizem a mesma coisa sobre a medição de latência, para o Revisor não cobrar uma evidência de D7 que o plano dispensa nem aceitar a ausência de uma que ele exige.
- **S2**: comentários de testes existentes que citam o nome antigo do cenário da US-002, "reply sem pergunta final" (renomeado para "reply sem pergunta" em ea8744d). São eles: `TutorReplyValidatorTests`, acima de `Validate_WhenReplyDoesNotEndWithQuestion_RejectsWithQuestionReason` e de `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed`; e `TutorReplyParserTests`, acima de `Parse_WhenJsonReplyHasNoFinalQuestion_RejectsWithQuestionReason` e de `Parse_WhenTagsReplyHasNoFinalQuestion_RejectsWithQuestionReason`. A §2.10.4 só manda atualizar o comentário dos testes de `TutorReplyRejectionsTests`, e o D16 não procura `pergunta final`. **Esperado:** depois da T20, os comentários dos testes citam o nome atual do cenário, e o D16 (ou a lista da §2.10.4) cobre esse trecho, para não sobrar referência a cenário inexistente (AP-005).
- **S3**: §10 D16. É uma verificação de "nenhuma linha", e só esse lado está previsto. Se o `Get-ChildItem -Recurse ... -Include` deixar de varrer algum caminho (os caminhos de arquivo `docs\us\US-002.md`, `ARCHITECTURE.md` e `specs\PRODUCT.md` misturados com pastas no Windows PowerShell), ou se um padrão estiver com aspas erradas, o resultado é um verde falso. Além disso, o D16 procura `EndWith("?")`, mas não a forma `EndsWith('?')`, que é a usada hoje em `TutorReplyValidator.Validate` e em `FixtureRecorderLiveTests.SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes`. **Esperado:** a evidência mostra que o mesmo comando lista as linhas antigas antes da mudança (o outro lado do limite, como no AP-016), e o comando também detecta a forma `EndsWith('?')`.
- **S4**: §9 P22 e §7 (linha "O separador erra em casos raros"). A P22 diz que o limite em abreviação (`Mr. Smith`) "só muda a contagem de frases, que o Core não valida". Só que o teste `LiveBrain` do cenário 1, que roda no D5 com tolerância zero em 5 rodadas, conta as frases com `ReplySentences.Split` e espera no máximo 3. Uma resposta de 3 frases reais com `Mr.` ou `Dr.` contaria 4 e deixaria a rodada vermelha. Isso não é pior que o regex `SentenceEnd` atual, que também separa ali e ainda separa `...` e `e.g.`. **Esperado:** a P22 e a §7 reconhecem esse efeito no D5. Assim, uma rodada vermelha por esse motivo é registrada na T23 como limite do separador, e não como falha do modelo ou da regra.
- **S5**: `.claude/skills/tdd-dotnet/SKILL.md` (exemplo de teste de adaptador, `SendAsync_WithValidOutput_ReturnsReplyEndingWithQuestion`). É só um nome de exemplo, não repete a regra, mas sugere a regra antiga a quem copia o padrão. A §2.10.6 afirma que "nenhuma linha" das skills é afetada. **Esperado:** decidir explicitamente se o exemplo acompanha a regra nova (por exemplo, `...ReturnsReplyWithOneQuestion`) ou se fica como está, com o motivo.
- **S6**: §4 e §5 T18, nome `ReplySentencesTests.IsQuestion_WithSentence_ReturnsWhetherItEndsWithQuestionMark`. Um dos casos é `"Really?!"` → `true`, e essa frase não termina com `?`. O nome descreve a regra que a P21 descartou. **Esperado:** um nome que descreva a regra da P21 (a pontuação final contém `?`), mantendo os nomes idênticos na §4 e na §5 (AP-025).
- **S7**: §8, último item. A S2 do `review.md` (privacidade do ambiente herdado pelo processo `claude`) continua sem dono, e por isso deve reaparecer como sugestão em aberto na próxima rodada da revisão de código (AP-022). Não impede esta revisão. **Esperado:** a decisão do usuário sobre a S2 fica registrada na §12 antes da próxima revisão de código, com o texto a registrar na spec de destino, se houver.

### Pontos positivos

- Todos os trechos "antigos" da §2.10.5 e da §2.10.6 foram conferidos por mim no repositório e batem caractere a caractere, inclusive a indentação da linha do ADR-002 e a linha do §3 da `ARCHITECTURE.md` (AP-023). O planejador também achou o `PRODUCT.md` (OA1), que a spec de ea8744d tinha deixado de fora.
- A tabela "Bordas do separador" cobre os dois lados de cada condição de limite. Os casos do validador foram escolhidos para que a regra antiga dê o resultado oposto (todos os "válidos" terminam em `.` e todos os "inválidos" terminam em `?`), o que garante o Red da T20 pelo motivo certo.
- A §2.10.4 lista cada teste existente que muda, com o motivo e sem enfraquecer nenhum. O caso `"Is it good? I like that."` passa explicitamente de rejeição para aceitação, em vez de sumir.
- A P26 define a camada medida (adaptador, sem retentativa), o ambiente e a regra "não repetir a rodada para pegar uma verde". Isso atende de uma vez ao aprendizado do `review.md` e à S3 de lá.
- As contagens foram atualizadas em todos os lugares (cabeçalho da §4, detalhamento por classe, conferência por tarefa, D2, D11, D17) e somam corretamente (AP-031).
- A T22 fixa o commit `feat(brain)` para os prompts, e há uma regra nova na §5 para divergir só com registro prévio na §12, o que responde à S1 do `review.md`.

### Aprendizados para o processo

- **S2/S3 (variação do AP-005):** quando um cenário ou motivo é renomeado na spec, o plano tende a listar só as ocorrências do texto exato que mudou (`must end with a question`, `EndWith("?")`). Ficam de fora as variações: nome antigo do cenário em comentários de teste e a forma `EndsWith('?')`. Proposta: no `.claude/agents/planejador.md`, quando a US renomeia cenário ou regra, o Grep de "texto antigo" inclui (a) o nome antigo do cenário, (b) as variações sintáticas da asserção e (c) uma evidência de que o comando de busca da DoD acha as ocorrências antes da mudança. No `checklist-revisao` §0, acrescentar: "verificação de 'nenhuma linha' na DoD tem o outro lado demonstrado (o comando acha as ocorrências antes da mudança)".
- **S4:** uma limitação aceita foi justificada com "o Core não valida isso", mas o mesmo componente passou a ser usado num critério da DoD com tolerância zero. Proposta: no `planejador.md`, todo "limite aceito" lista os consumidores do componente (validador, testes `LiveBrain`, DoD) e diz o efeito em cada um.

## Rodada 5

Data: 2026-10-20
Escopo revisado: `tasks/US-004/plan.md` no HEAD 097f237 (`docs(ai): revise plan for US-004`), comparado com a93c42b (`git diff HEAD~1 HEAD`: 64 inserções e 31 remoções, o arquivo vai de 1255 para 1288 linhas). Só revisei o que mudou. O arquivo foi reescrito inteiro, mas o diff mostra só os trechos abaixo, então nada foi perdido.
Resultado: APROVADO

### Itens da rodada anterior (Rodada 4)

| Item | Status |
|---|---|
| S1 (status e T23 × D7) | Resolvida em 097f237. O `Status:` cita T18–T23. A T23 roda D1–D6 e D8–D18 e diz que o D7 não se repete, com o motivo (feito em 2fd8419, latência não é critério da RN-FEED01). O D7 da §10 diz a mesma coisa. A nota de abertura da §11 também. |
| S2 (comentários "reply sem pergunta final") | Resolvida em 097f237. Está na §2.10.4 (linha nova com texto antigo e novo), na §3, na §4 ("Testes existentes alterados": 11 itens), na T20 (mudam nos commits `test`) e no D16 (padrões `pergunta final` e `NoFinalQuestion`). Os 4 comentários existem no código, com o texto citado: `TutorReplyValidatorTests.cs` linhas 40 e 53, `TutorReplyParserTests.cs` linhas 98 e 209. O nome atual do cenário na spec é "reply sem pergunta" (`SPRINT_1.md:130`). |
| S3 (D16 só confere um lado) | Resolvida em 097f237, com a ressalva da S1 desta rodada. O D16 foi reescrito e há o D16c, executado na T18 antes do Red. |
| S4 (P22 e o D5) | Resolvida em 097f237. A P22 lista os consumidores e o efeito de cada limite. A §7 tem a linha nova. A T23 classifica a causa de rodada vermelha em (a) a (d), e "limite do separador" também vai para a §12. |
| S5 (exemplo do `tdd-dotnet`) | Resolvida em 097f237. A decisão é que o exemplo acompanha a regra nova. Texto antigo e novo estão na §2.10.6. A linha antiga confere com `.claude/skills/tdd-dotnet/SKILL.md:86`, com 4 espaços de indentação. A §3, a pré-condição da §5, o D16 e a DoD comum foram atualizados. |
| S6 (nome do teste de `IsQuestion`) | Resolvida em 097f237. O nome é `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark`, igual na §4 (linhas 655 e 778), na T18 (925) e na P21 (1046). O nome antigo só aparece na linha 1287, que registra a própria renomeação (ver S2 abaixo). |
| S7 (privacidade do ambiente herdado) | Registrada como risco aceito, sem criar adiamento sem dono (AP-022). A §7 tem motivo, mitigações em vigor e condição para revisitar. A §8 diz "sem US de destino, sem ADR agora e sem promessa para outra US". A §12 registra o pedido do usuário e a resposta. A condição para revisitar é um gatilho com decisão do usuário, não uma promessa a outra US. |

### Verificações

| Item | Resultado |
|---|---|
| [O] D16 funciona no Windows PowerShell 5.1 | ✅ Rodei o bloco no `powershell.exe` 5.1, no estado atual da árvore, que ainda tem o texto antigo. Saída: `scanned=131 named=3` e 38 linhas. Os 3 nomeados entram por `Get-Item`. `.claude\skills` é varrido, mesmo sendo pasta com ponto. Aspas, crases e `Where-Object Name -in` não deram erro. |
| [O] D16c, parte 1 (código, testes, prompts) | ✅ Todos os padrões esperados pelo plano aparecem. `must end with a question` e `ReplyMustEndWithQuestion` estão em `TutorReplyRejections.cs`, `TutorReplyValidator.cs` e nos testes. `LAST sentence` está nos 3 prompts. `EndWith("?")` está em `ClaudeCodeTutorBrainTests`, `ClaudeCodeTutorBrainLiveTests` e noutros testes. `EndsWith('?')` está em `TutorReplyValidator.cs:19` e `FixtureRecorderLiveTests.cs:36`. `pergunta final` e `NoFinalQuestion` estão em `TutorReplyParserTests.cs` e `TutorReplyValidatorTests.cs`. |
| [O] D16c, parte 2 (`git grep` no `ea8744d`) | ✅ O comando funciona e `ea8744d` existe. Acha uma linha em cada um dos 6 arquivos esperados: `ARCHITECTURE.md:189`, `specs/PRODUCT.md:15`, ADR-002 (linhas 20 e 30), ADR-005 (14, 18 e 35), `docs/us/US-002.md:35` e `.claude/skills/tdd-dotnet/SKILL.md:86`. |
| D16 cobre todas as ocorrências antigas dos documentos | ✅ Cada linha achada nos documentos tem texto antigo e novo na §2.10.6. Os textos novos não contêm nenhum padrão do D16. |
| [O] Contagens | ✅ D2 = 249 = Core 103+31 = 134, Console 41, Infrastructure 72+2 = 74. A §4 e a §5 dão 57 métodos e 98 casos. D17 = 19 casos em 3 métodos (10+2+7). A linha 1267 da §12 bate com essas contagens. A renomeação não muda nada, e a §4 diz isso. |
| [O] Nomes idênticos na §4 e na §5 (AP-025) | ✅ Conferi os 3 testes de `ReplySentencesTests` e os de "reply sem pergunta" do validador e do parser. Os nomes batem. O `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed` (linhas 462 e 945) não muda de nome. |
| [O] Texto da §2.10.6 (trecho antigo do `tdd-dotnet`) | ✅ O texto antigo confere por byte com o arquivo. O novo é `SendAsync_WithValidOutput_ReturnsReplyWithOneQuestion`. O parágrafo "Repetições em `CLAUDE.md` e skills" foi corrigido e a busca inclui `EndingWith\|pergunta final`. O `CHANGELOG.md` fica de fora de propósito (registro histórico). |
| [O] Ordem das tarefas e Red | ✅ A T18 agora começa com o controle do D16 e depois faz o Red. Esse passo só roda comandos e não gera código. Os Reds da T19 a T21 não mudam. |
| [O] Escopo e AP-022 | ✅ A S7 não cria adiamento. O teste do `ChangeMode` (S4 do `review.md`) continua em item próprio, para decisão do usuário. |
| Nada perdido na reescrita | ✅ O diff entre 097f237 e a93c42b só tem os trechos listados acima. Cabeçalhos §1 a §12 e as tarefas T1 a T23 estão presentes. |
| Código e testes do repositório | N/A. Não rodei build nem testes: este é o modo revisão de plano, e o código na branch não mudou desde a Rodada 4. |

### Bloqueantes

Nenhum.

### Sugestões

- **S1** — §7, linha de 2026-10-20 (privacidade do ambiente herdado), frase "o comando do D5/D6/D7 limpa as variáveis (§10)". Só o D5 tem o comando de limpeza. O D6 diz "no ambiente acima" e depende de ser a mesma sessão do D5. O D7 não limpa, mas também não é repetido na T23. **Esperado:** a frase diz que o D5 limpa e que o D6 roda na mesma sessão. Assim o Executor não roda o D6 numa sessão nova com o ambiente herdado do app desktop. Se esse ambiente for a causa do problema, o D6 mostraria o nome do titular e o D13 reprovaria, mas a evidência já estaria contaminada.
- **S2** — §12, resposta da rodada 4 (linha 1287), item S6. A frase "para uma busca pelo nome antigo não achar mais nada" não é literal: o nome antigo `...ItEndsWithQuestionMark` ainda aparece nessa própria linha, ao registrar a renomeação. Não afeta o D16, que não varre `tasks/`. **Esperado:** ajustar a frase para "fora desta linha" ou aceitar a ocorrência como histórico.

### Pontos positivos

- O D16 imprime `scanned` e `named` antes dos resultados. Um caminho não varrido aparece na evidência em vez de virar um verde falso. Os 3 arquivos nomeados entram por `Get-Item`, e isso resolve o problema de misturar arquivo e pasta no `-Path`.
- O D16c prova os dois lados da verificação: o mesmo bloco acha as ocorrências no código, e o `git grep` em `ea8744d` acha as dos documentos, que a parte 1 já não vê. A lista esperada é concreta, por padrão e por arquivo. Ela bate com o que obtive, e o plano manda parar se algo faltar.
- A restrição de forma na §2.10.1 (sem `EndsWith('?')` no código novo) é explicada pelo motivo: o D16 confere por texto, e a regra é "pontuação final contém `?`".
- A P22 passa a listar os consumidores do `ReplySentences` e o efeito de cada limite. A T23 diferencia falha do modelo, da regra e do separador, e leva a decisão ao usuário.

### Aprendizados para o processo

- Nada novo. O AP-005 (texto antigo sobrevivente) e o AP-022 já cobrem o ocorrido. As propostas da Rodada 4 (o outro lado da verificação de "nenhuma linha" e os consumidores de todo "limite aceito") foram aplicadas e funcionaram. Ainda vale registrá-las em `.claude/agents/planejador.md` e no `checklist-revisao` §0, se o usuário concordar.
