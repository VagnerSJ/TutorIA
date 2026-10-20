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
