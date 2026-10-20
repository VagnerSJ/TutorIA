# Revisão do plano — US-005 Erros do Claude Code com mensagens claras

## Rodada 1

Data: 2026-10-20
Escopo revisado: `tasks/US-005/plan.md` e `docs/adr/ADR-008-classificacao-de-falhas-e-tempo-maximo-do-claude-code.md` (commit `d1166ba`, único commit da branch `feature/US-005_erros-claude-code` sobre `develop` `f033915`). Conferidos contra `specs/sprints/SPRINT_1.md` (US-005, US-007, DoD comum), `specs/BUSINESS_RULES.md` (RN-BRAIN07/08/09), `ARCHITECTURE.md` §3/§6.1/§6.3/§9/§10, ADR-007, `CLAUDE.md`, as skills e o código atual (ainda não há código de produção da US na branch).
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| Todo cenário BDD no mapa da §4 (projeto, classe, método `Metodo_Cenario_Resultado`) | ✅ 5/5 cenários, com testes de apoio |
| P* com efeito observável têm teste na §4 e tarefa na §5 | ✅ (P14 e P15 declaradas sem efeito observável) |
| DoD da §10 cobre a DoD da US (D5–D7 da spec, comum D1–D4, cobertura ≥ 80%) | ✅ (D5 reescrito via §2.8, decisão P4/P15 para o usuário) |
| Conformidade R1–R4, RN-BRAIN07/09, regras rápidas; ADR Proposto | ✅ (ADR-008 Proposto, Core sem mudança) |
| Escopo e adiamentos (§8, P*, comentários de teste; consumidores de "limite aceito") | ✅ (ver S6) |
| Consistência §3 × §4 × §5 × §6 e contagens | ✅ |
| Transformações com teste nos dois lados; asserções com texto só do projeto | ✅ (1/3600 e 0/-1/3601; 999/1000 ms; detalhe da 1ª linha × padrão na 2ª) |
| Ordem das tarefas preserva o Red | ✅ (ver S3 sobre o momento das mutações) |
| Critério `LiveBrain` (camada, tolerância, rodadas, quem decide) | ✅ D13 e P16 |
| `ValidateOnStart` em várias Options / caixa diferente | N/A (nenhuma classe nova; `TimeoutSeconds` inteiro; caso 4 cobre caixa no `Classify`) |
| Justificativas que citam outra US conferidas no `SPRINT_1.md` | ✅ (US-007 "sem fallback configurado" e "fallback por limite de uso"; US-006 mensagens próprias) |
| Verificação de "nenhuma linha" com o outro lado (AP-035) | ✅ |
| Alteração na `ARCHITECTURE.md` lista `CLAUDE.md` e skills | ✅ |
| Riscos e ambiguidades tratados | ✅ |
| Viabilidade técnica | ✅ |

Conferências feitas por mim:
- Linha de base (`dotnet build` 0 erros; `dotnet test --filter "Category!=LiveBrain"`): Core.Tests 134, Infrastructure.Tests 74, Console.Tests 41; `ClaudeCodeTutorBrainTests` 31 casos / 20 métodos. Batem com o ponto de partida da §4. A soma do plano fecha: Console 41 − 1 + 11 = 51; `ClaudeCodeTutorBrainTests` 31 + 19 = 50 casos (28 métodos); Infrastructure 74 + 19 + 5 = 98; total 283.
- Textos antigos da §2.8/§2.9 existem exatamente como citados: `ARCHITECTURE.md` linhas 60 e 212; ADR-007 linha 5; `SPRINT_1.md` linha 354; comentários em `ProcessRunner.cs`:7, `ClaudeCodeOutput.cs`:10, `ConversationInputHandler.cs`:6 e `EnglishTutorAppTests.cs`:296–297. `ARCHITECTURE.md` §6.1 já tem `"TimeoutSeconds": 60` depois de `WorkingDirectory`.
- Rodei o bloco D16 (Select-String, só leitura) na árvore atual. Ele acha as 10 ocorrências esperadas, inclusive as linhas não ASCII (`EnglishTutorAppTests.cs`:296/297) e `ARCHITECTURE.md`:212, com `named=1`. Ou seja, o comando do D16 enxerga o texto antigo, e o D16c complementa isso a partir do `f033915`.
- O que o plano supõe que já existe está no código: `BrainException(kind, message, innerException)` (Core); `ScriptedTutorBrain.Throws`; `AppRunOptions.ConfigureServices`, que roda depois do `AddEnglishTutorInfrastructure` com `TryAdd`, então o teste consegue trocar o `IProcessRunner`; `FixedTutorPromptSource`; `ClaudeOutputs.Error` com `type: result` e `is_error: true`; `ExitCodes.InvalidConfiguration = 2`. O `ManualTimeProvider` só aparece em `ClaudeCodeTutorBrainTests.cs`:29 e no próprio arquivo, então o Refactor da T2 é viável. O único uso de `Advance` (`OnRun` com 1500 ms) fica abaixo dos 60 s padrão.

### Bloqueantes
Nenhum.

### Sugestões
- **S1** — §4, `SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` / `ClaudeCodeFailures.Classify` (§2.4). O ADR-008 e a §2.4 dizem que a classificação examina o `result` de erro **e** o stderr inteiro. Mas todos os casos em que o padrão está no stderr (2, 4, 7, 10) têm stdout vazio, ou seja, `errorResult` nulo. Uma implementação que olhasse o stderr só quando não há `result` de erro passaria nos 10 casos. Esperado: um caso em que o `result` de erro não casa com nenhum padrão e o stderr casa, para que o comportamento "as duas fontes" fique provado (AP-041).
- **S2** — §2.2, `ClaudeCodeTutorBrain.SendAsync` (filtro `when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)`). O plano afirma que o cancelamento do chamador propaga "mesmo que o tempo também tenha estourado". Nenhum teste da §4 cobre esse caso: `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` cancela só o chamador, com o tempo ainda dentro do limite. Esperado: um teste para os dois cancelamentos juntos, ou a afirmação retirada do plano. Sem uma das duas coisas, a parte `!ct.IsCancellationRequested` do filtro fica sem prova.
- **S3** — §5, T1 (`Validate_WithTimeoutSecondsAtLimits_Succeeds`) e T4b (casos 9 e 10). As mutações descritas ("o limite inferior passa a ser 2"; "a linha 2 passa a ser classificada"; "`rate_limit` passa a contar como limite de uso") só têm efeito quando a regra real já existe. No Red, o validador ainda não confere a faixa e o `Classify` devolve sempre `Unknown`, então a mutação não derruba nada. Esperado: a tarefa dizer que essas mutações são aplicadas depois do Green commitado e desfeitas pela edição inversa (AP-017, AP-037).
- **S4** — §2.7, bloco de gravação (`record`). O comando grava sem os mesmos argumentos de isolamento do adaptador (`--strict-mcp-config`, `--disallowedTools mcp__*`, `--system-prompt-file`). No caso `unknown-model`, que usa a configuração real do usuário, a CLI pode carregar MCP e configuração do usuário e pôr no stderr avisos que o adaptador nunca veria. Esperado: as fixtures gravadas representarem a chamada que o adaptador faz (mesmos argumentos, RN-BRAIN06), ou o `README.md` registrar a diferença.
- **S5** — §10, D6, coluna "Esperado": "a primeira linha chega com BOM e é tratada (AP-006)" não aparece na saída desse comando. Com a CLI ausente, a mensagem é a mesma com ou sem BOM. Esperado: dizer como o efeito é observado ou retirar o item desse esperado (o pipe no PowerShell 5.1 continua valendo como cobertura do `padroes-codigo`).
- **S6** — P9 / §2.3 (limite aceito, AP-040). Falhas de início com código diferente de 2 e 3 (5 = acesso negado, 193 = não é executável) viram `Unknown`. O plano não lista o efeito disso na US-007: a RN-BRAIN08 aciona o fallback para `CliNotFound`, mas não para `Unknown`, então um `ExecutablePath` que aponta para algo que não é executável não aciona o reserva. Esperado: incluir esse consumidor e o efeito na P9 (ou no ADR-008, "Consequências").
- **S7** — §5, T5 Red. O construtor novo do `ConversationInputHandler` faz `EnglishTutorApp.RunSessionAsync` deixar de compilar. A troca da chamada só aparece no Green (§2.6). Esperado: a lista do esqueleto do Red incluir a nova construção no `EnglishTutorApp` (sem `catch` ainda, sem mudar comportamento), para que o Red compile e falhe pelo motivo certo.
- **S8** — §2.5, mensagem `NotAuthenticated`. O padrão `invalid api key` (caso 3) pode vir de uma `ANTHROPIC_API_KEY` inválida no ambiente. Nesse caso "Run "claude auth login"" pode não resolver, porque a variável continua tendo precedência. Esperado: o Planejador decidir se a mensagem também menciona a variável. O cenário só exige `claude auth login`, então isso não muda a spec.

### Pontos positivos
- ADR-008 Proposto já no commit do plano, com alternativas, consequências e o efeito na US-007 (limite de uso não reconhecido não aciona o fallback) registrados antes do código.
- Mapa §4 detalhado: cada padrão, linha e fonte da tabela de classificação tem um caso que só ele satisfaz, com rótulos únicos (AP-041, AP-026). Os casos 9 e 10 cobrem o "outro lado" (linha 2 e `rate_limit`).
- Tempo máximo testável sem relógio real (`FakeTimeProvider`, 999/1000 ms, um relógio por chamada) e `WaitAsync(10 s)` para que o Red não trave a suíte.
- O encerramento da árvore real fica coberto por um teste `LiveBrain` com processo neto (`cmd` → `powershell`), fora do D2.
- Asserções conferem só texto do projeto e nunca `Win32Exception.Message` (AP-015).
- §2.8 com textos antigos exatos para a spec, a `ARCHITECTURE.md` e o ADR-007, aplicados antes da T1 (AP-023, AP-030). Repetições em `CLAUDE.md` e nas skills foram procuradas e justificadas.
- Higiene das fixtures: D14, anonimização no `README.md`, ambiente limpo explícito (AP-034). O sintético está marcado, com a condição de troca registrada.
- D16 com controle D16c contra o `develop` de partida, cobrindo o nome antigo do teste, o teste do token e as variações dos comentários de adiamento (AP-035).

### Aprendizados para o processo
- **Red por mutação em teste que nasce verde porque a regra ainda é um stub** (S3). A mutação "pelo efeito" pressupõe que a regra real já exista. Quando o Red da tarefa é um esqueleto (validador sem a regra, `Classify` devolvendo um valor fixo), a mutação não tem o que quebrar. Regra sugerida em `.claude/skills/tdd-dotnet/SKILL.md`, junto do AP-017/AP-037: "se o teste nasce verde porque o esqueleto já devolve o valor esperado, a mutação é aplicada depois do Green commitado; a tarefa diz em que momento ela é feita".
- **Afirmação de comportamento no §2 sem teste no §4** (S2). Variação do AP-004/AP-018: frases como "mesmo que X também aconteça" na abordagem descrevem um ramo que nenhum teste fixa. Regra sugerida no checklist §0: "cada afirmação condicional da §2 ('mesmo que', 'exceto quando', 'só se') tem um teste na §4 ou sai do plano".
- **Fixture gravada com argumentos diferentes dos do adaptador** (S4). Regra sugerida no `planejador.md`: "comando de gravação de fixture usa os mesmos argumentos de isolamento do adaptador, ou o `README.md` da pasta registra a diferença".

## Rodada 2

Data: 2026-10-20
Escopo revisado: `tasks/US-005/plan.md` e `docs/adr/ADR-008-classificacao-de-falhas-e-tempo-maximo-do-claude-code.md` no commit `83a7f19` ("docs(ai): revise plan for US-005"), comparados com `1c8f492`. A branch tem 4 commits sobre `develop`, todos de documentação (`git diff develop...HEAD --stat`: só o ADR-008, `plan.md` e `plan-review.md`). Ainda não há código da US. Conferi contra `specs/BUSINESS_RULES.md` (RN-BRAIN08/09), `ClaudeCodeTutorBrain.BuildArguments`, `ClaudeCodeOutput.Read`/`Detail`, `FakeProcessRunner`, `prompts/` e o uso de `ManualTimeProvider`.
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| Todo cenário BDD no mapa da §4 (projeto, classe, método `Metodo_Cenario_Resultado`) | ✅ 5/5 cenários. Entra o apoio novo `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` |
| P* com efeito observável têm teste na §4 e tarefa na §5 | ✅ P17 tem teste (asserção de `ANTHROPIC_API_KEY`) e entra na T4b. P18 é declarada sem efeito observável |
| DoD da §10 cobre a DoD da US (D5–D7 da spec, comum D1–D4, cobertura ≥ 80%) | ✅ D2/D8 atualizados (285; 52/29), D6 sem a alegação do BOM, D12 com a frase de `ANTHROPIC_API_KEY` |
| Conformidade R1–R4, RN-*, regras rápidas; ADR Proposto | ✅ ADR-008 continua Proposto e foi revisado com S2, S4, S6 e S8. O cabeçalho "Regras" agora cita RN-BRAIN06/08 |
| Escopo e adiamentos (§8, P*, comentários; consumidores de "limite aceito") | ✅ P9 lista os consumidores (mensagem, log, fallback da US-007, D6). O texto da RN-BRAIN08 citado bate com `BUSINESS_RULES.md`:38 |
| Consistência §3 × §4 × §5 × §6 e contagens | ✅ (conta abaixo) |
| Transformações com teste nos dois lados; asserções com texto só do projeto | ✅ (ver S1 sobre a simetria das fontes) |
| Ordem das tarefas preserva o Red | ✅ As mutações depois do Green estão corretas na T1, na T2 (filtro S2) e na T4b. O esqueleto da T5 agora compila (S7). Ver S2 e S3 |
| Critério `LiveBrain` (camada, tolerância, rodadas, quem decide) | ✅ Sem mudança desde a Rodada 1 (D10, D13, P16) |
| `ValidateOnStart` em várias Options / caixa diferente | N/A (sem mudança) |
| Justificativas que citam outra US conferidas | ✅ RN-BRAIN08 conferida no texto. O efeito na US-007 está na P9, na §2.3, na §8 e no ADR-008 "Consequências" |
| Verificação de "nenhuma linha" com o outro lado (AP-035) | ✅ D16/D16c sem mudança. `ManualTimeProvider` continua aparecendo só em `ClaudeCodeTutorBrainTests.cs`:29 e no próprio arquivo |
| Alteração na `ARCHITECTURE.md` lista `CLAUDE.md` e skills | ✅ (sem mudança) |
| Riscos e ambiguidades tratados | ✅ Dois riscos novos na §7 (fixture × chamada real; ordem dos dois cancelamentos) |
| Viabilidade técnica | ✅ Os argumentos de gravação da §2.7 batem um a um com `ClaudeCodeTutorBrain.BuildArguments` (linhas 107–122). A única ausência é `--json-schema`, registrada no README (P18). `prompts/tutor.md` e `prompts/format.tags.md` existem |

Conferências de contagem que eu mesmo fiz:
- `ClaudeCodeTutorBrainTests`: 31 − 1 + 1 (substituto) + 1 (S2) + 2 + 1 + 1 + 2 + 1 + 11 + 1 + 1 = **52 casos**. Métodos: 20 − 1 + 10 = **29**. Batem com a §4, o D8 e a §12.
- Infrastructure.Tests: 74 + 21 + 5 (validador 3 + 2) = **100**.
- Console.Tests: 41 − 1 + 5 + 1 + 1 + 1 + 1 + 2 = **51**, sem mudança desde a Rodada 1.
- Total: 134 + 51 + 100 = **285**, igual ao D2. `LiveBrain`: +2.
- A linha de base é a da Rodada 1. Desde `f033915` não houve mudança de código: os commits da branch só tocam documentação, então não rodei de novo o build nem os testes.

### Itens da rodada anterior
- **S1** (caso em que só o stderr casa): **resolvido em `83a7f19`**. Caso 11 da §4 (`Server error t11` no erro-json e `Not logged in t11` no stderr → `NotAuthenticated`, detalhe vindo do `result`). A §2.4 diz "sempre as duas fontes", e a T4b diz que o caso 11 falha no Red. O detalhe esperado bate com `ClaudeCodeOutput.Detail` (o `result` de erro não vazio vence o stderr). O ADR-008 item 1 foi atualizado.
- **S2** (os dois cancelamentos juntos): **resolvido em `83a7f19`**. Teste `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` com `HangsUntilCancelled(onCancelled)`; o `onCancelled` roda antes de a exceção sair do fake, então a ordem é determinística (§2.9, §7). Ele falha no Red pelo `WaitAsync`. A mutação depois do Green, "a condição do chamador deixa de contar no filtro", prova a parte `!ct.IsCancellationRequested`. Estão coerentes a §2.2, a §3, a §4, a T2 e o ADR-008 item 2.
- **S3** (mutações depois do Green): **resolvido em `83a7f19`**. Há um parágrafo geral na §5 (procedimento + `git status --short` + Evidências). A T1 e a T4b ganharam o passo "Mutação depois do Green". A T2, a T5 e a T6 dizem por que a mutação delas continua no próprio Red. Ver a nova S2 sobre uma das mutações da T4b.
- **S4** (argumentos da gravação): **resolvido em `83a7f19`**, nas duas formas. O bloco da §2.7 usa os mesmos argumentos de isolamento do adaptador, e o README tem a seção "Diferenças em relação à chamada do adaptador". Há P18 nova, a alternativa JsonSchema foi descartada e há risco novo na §7. Também estão no ADR-008 item 1 e em "O que precisa ser medido".
- **S5** (BOM no D6): **resolvido em `83a7f19`**. O esperado do D6 deixou de afirmar o BOM e explica a cobertura do pipe no PowerShell 5.1.
- **S6** (efeito na US-007 dos códigos 5/193): **resolvido em `83a7f19`**. Está na §2.3, na §2.8 ("A spec da US-007 não muda"), na §8, na P9 com os consumidores e no ADR-008 "Consequências" (bullet novo). O texto da RN-BRAIN08 confere.
- **S7** (Red da T5 compila): **resolvido em `83a7f19`**. A T5 Red constrói o handler no `EnglishTutorApp.RunSessionAsync` sem `catch`. Também estão na §2.6 e na §3.
- **S8** (`ANTHROPIC_API_KEY`): **resolvido em `83a7f19`**. A frase fixa está na mensagem `NotAuthenticated` (§2.5), há P17 nova, a asserção em `SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin` e no D12, e o ADR-008 foi atualizado (contexto, item 3, alternativa e consequência). A spec não muda, o que está certo: o cenário só exige `claude auth login`.

### Bloqueantes
Nenhum.

### Sugestões
- **S1** — §4, `SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` / `ClaudeCodeFailures.Classify` (§2.4). O caso 11 prova uma direção: o `result` não casa e o stderr casa. A direção contrária não tem prova. Em todos os casos em que o padrão está no `result` (1, 3, 5, 6, 8), o stderr está vazio. Por isso uma implementação que olha o stderr e só recorre ao `result` quando o stderr está vazio passa nos 11 casos e contradiz "sempre as duas fontes". Esperado: um caso em que o stderr tem texto sem relação com nenhum padrão (ex.: um aviso da CLI) e o `result` de erro casa, com o tipo vindo do `result` (AP-041).
- **S2** — §5 T4b, terceira mutação depois do Green ("toda falha da linha 1 passa a ser `NotAuthenticated`"), alvo `SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail`. A §4 admite que a fixture `unknown-model` saia pela linha 1 (código ≠ 0) ou pela linha 3 (código 0 e `is_error`), "conforme o código gravado". Se a gravação cair na linha 3, a mutação não afeta o teste e não prova nada. Esperado: a mutação valer para a linha em que a fixture gravada de fato cai (ou para as linhas 1 e 3), e o Executor registrar qual foi depois da T4a.
- **S3** — §4/§5 T2, `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`. A mutação do Red ("o cancelamento do chamador deixa de chegar ao runner") faz o fake nunca terminar. Diferente dos outros três testes de tempo, a asserção desse teste não diz que a espera é limitada. Sob a mutação, o teste pode travar a suíte em vez de falhar. Esperado: o teste falhar em tempo limitado sob essa mutação, como os demais testes de tempo (§7, risco "trava a suíte no Red"). Isso já existia na Rodada 1.

### Pontos positivos
- A §12 responde item a item no formato "item → seções tocadas" (AP-018), e cada seção citada foi de fato alterada. As contagens novas aparecem no mesmo lugar e batem em todas as seções (§4, §10 D2/D8, §12).
- O S2 foi resolvido com um teste determinístico (callback antes da exceção, sem corrida), não retirando a afirmação. Ele tem uma mutação específica para a parte do filtro que estava sem prova.
- O S3 virou um procedimento geral na §5, com conferência por `git status --short` e Evidências. Cada tarefa diz quando a mutação ocorre e por quê.
- O S4 foi conferido contra o código (`BuildArguments`), e as diferenças que sobram estão registradas no README e numa P nova.
- O S6 confere o texto da RN-BRAIN08 antes de escrever (AP-036) e registra o efeito no ADR-008 sem mexer na regra nem na spec da US-007.
- O ADR-008 continua coerente com o plano depois da revisão: item 1 com as duas fontes, item 2 com os dois cancelamentos, item 3 com `ANTHROPIC_API_KEY`, e as consequências com os códigos 5/193.

### Aprendizados para o processo
- **Prova de "as duas fontes" só numa direção** (S1). Ao incorporar a S1 da Rodada 1, o caso pedido cobriu exatamente a direção citada, e a simétrica ficou de fora. Regra sugerida no checklist §0, junto do AP-041: "quando o plano diz que duas fontes são sempre examinadas, há um caso em que só a fonte A casa com a B presente e sem casar, e outro com os papéis trocados".
- **Mutação que depende de um dado ainda não gravado** (S2). Uma mutação escolhida antes de a fixture existir pode não atingir o caminho que ela exercita. Regra sugerida na `tdd-dotnet`: "mutação sobre teste de fixture gravada é escolhida (ou confirmada) depois da gravação, para o ramo que a fixture realmente percorre".
- **Espera limitada só nos testes novos** (S3). A proteção contra travar a suíte foi aplicada aos testes de tempo, mas não ao teste de caracterização cujo Red por mutação também trava. Regra sugerida na `tdd-dotnet`: "todo teste com fake que só termina por cancelamento usa espera limitada, inclusive os de caracterização".
