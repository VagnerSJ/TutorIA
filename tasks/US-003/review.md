# Revisão — US-003 ConversationService dono do histórico

Data: 2026-10-17
Commits revisados: d88a842..3156357 (código: 70a9e5a..232693a; evidências: 3156357)
Resultado: APROVADO

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ (0 avisos, 0 erros) |
| Testes (exceto LiveBrain) | ✅ 148 passaram (Core 99, Console 25, Infrastructure 24) |
| Cobertura de linhas do Core | 100% (namespace `Conversation`: 100% de linhas, 169/169; ramos 85%) |
| Cenários BDD com teste | 7/7 |
| Teste antes do código (ordem de commits) | ✅ (pares `test` → `feat` em T2–T10; esqueletos no commit `test`, AP-013) |
| Evidências da DoD conferem | ✅ (D1–D8, D10, D11 reproduzidos; D9 é evidência do Executor, não reproduzida) |
| Intenção antes do código (spec/ADR → código) | ✅ (ADR-006 em d88a842, Aceito em 3e3d711; spec em 4b82f0a; ARCHITECTURE/skill em f522128; tudo antes de 70a9e5a) |

### Checklist (`checklist-revisao`)

| # | Item | Resultado |
|---|---|---|
| 1 | [O] Todo cenário BDD com teste (§4 confere com o código) | ✅ todos os 35 métodos da §4 existem com os nomes previstos; `ConversationServiceTests` 18 métodos / 21 casos |
| 1 | [O] Comportamento bate com os cenários | ✅ mensagem do turno exata (`\n`), janela 2 + nova mensagem sem o 1º turno, retentativa com `Assistant(raw1)` + `User(correção)`, fallback com `Trim` no `Reply` e `RawText` original, aviso local, `Mode: job-interview` |
| 1 | [O] `RN-*` respeitadas e citadas nos testes | ✅ RN-CONV01/02/03/05/06, RN-BRAIN01/04 citadas; RN-CONV04 e RN-TTS01 (fala) fora de escopo pela §8 |
| 1 | [O] Nada fora do escopo sem registro | ✅ guardas de nulo extras registradas em E5 (§12) |
| 1 | [O] Spec/ADR antes do código | ✅ |
| 1 | [O] DoD rodada e evidência confere | ✅ (ver tabela abaixo) |
| 1 | Comentários do usuário respondidos e respeitados | ✅ S8 (`Name => "Fake"`, `FakeTutorBrain.cs:10`), S9 (cenário da US-004 com `ExitCodes.InvalidConfiguration`), S10 (nomes completos na §5) |
| 1 | Escolhas de forma na §12 antes do `feat` (AP-014) | ⚠️ E1–E7 registrados só em 3156357, depois de todos os `feat` (ver S1); aceitos pelo usuário |
| 2 | [O] R1: Core sem Infrastructure/Console/terceiros | ✅ só `DependencyInjection.Abstractions`, `Logging.Abstractions` e `Options`; `CoreDependencyTests` verde |
| 2 | [O] R4: regra de negócio no Core | ✅ retentativa, janela e aviso no Core; o console só usa `ConversationMessages.EmptyInput` |
| 2 | [O] R3: novos adaptadores | N/A (nenhum adaptador nesta US) |
| 2 | [O] Mudança arquitetural com ADR e `ARCHITECTURE.md` | ✅ ADR-006 + §3/§4/§5 |
| 2 | Nomes e pastas da §3 | ✅ |
| 3 | [O] Build sem warnings / testes verdes / Core ≥ 80% | ✅ / ✅ / ✅ |
| 3 | [O] Nenhum teste apagado, `Skip` ou enfraquecido sem justificativa | ✅ caso `"/mode interview"` → `"/foo bar"` previsto na §4; E3 (comparação por `StudentMessage`) justificado na §12 |
| 3 | [O] Sem rede, GPU ou processo real; suíte em segundos | ✅ Core.Tests 135 ms |
| 3 | Determinísticos, nomes `Metodo_Cenario_Resultado` | ✅ |
| 4 | [O] `CancellationToken` repassado; sem `.Result`/`.Wait()` | ✅ mesmo `ct` no prompt source e nas duas chamadas (`ConversationService.cs:41`, `:55`, `:69`), conferido por teste |
| 4 | [O] Sem `catch` que engole; OCE propaga | ✅ só `catch ... when (IsInvalidOutput)`, com log e conversão em fallback |
| 4 | [O] Processos externos | N/A |
| 4 | Logs estruturados, sem dado sensível | ✅ `[LoggerMessage]` 3000/3001 só com `Provider` e `Reason`; testes conferem a ausência do texto do aluno e do texto cru |
| 4 | `sealed`/`record`, sem código morto | ✅ (ver S2) |
| 4 | Caracteres invisíveis (forma única, AP-019) | ✅ contagem 0, código de saída 1 |
| 5 | [O] Segredos, `.gitignore`, provedor Claude Code | ✅ / N/A (nenhum arquivo local novo) / N/A |
| 6 | [O] Commits convencionais, branch `feature/US-003_conversation-service` | ✅ |
| 6 | Tarefas marcadas `[x]` | ✅ T1–T11 |
| 6 | Novas chaves no `appsettings.json` | N/A: o bind de `Conversation:MaxTurnsInWindow` foi adiado para a US-004 (§8) e lá ficou registrado como D8 |

### Evidências da DoD × resultado do Revisor

| # | Executor (§11) | Revisor |
|---|---|---|
| D1 | 0 avisos, 0 erros | 0 avisos, 0 erros ✅ |
| D2 | 148 (99/25/24) | 148 (99/25/24) ✅ |
| D3/D3b | Core 100%, geral 87,8% | Core 100%, geral 87,8% ✅ (relatório em `%TEMP%`) |
| D4 | sem diferenças | exit=0 ✅ |
| D5 | 99, 127 ms | 99, 135 ms ✅ |
| D6 | `Conversation` 100% (169/169), ramos 85% (17/20) | igual ✅ |
| D7 | 3 pacotes 10.0.13 | `Directory.Packages.props` e `.csproj` conferem; `CoreDependencyTests` verde ✅ (`dotnet list` não reproduzido, fora da lista de comandos permitidos) |
| D8 | 21 casos / 18 métodos | 18 métodos / 21 casos contados no código ✅ |
| D9 | cabeçalho, aviso uma vez com BOM, `exit=0` | evidência do Executor, não reproduzida (AP-008). Coerente com o código: `ChatSession.cs:35-38` escreve `EmptyInput` depois da remoção do BOM; `RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning` cobre os 3 casos |
| D10 | vazio | `git status` limpo ✅ |
| D11 | 0, exit 1 | 0, exit 1 ✅ |

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tasks/US-003/plan.md:565-575` (§12, E1–E7). Todas as escolhas de forma foram registradas no commit de evidências (3156357), depois dos `feat`. Exemplos: os nomes `ReplyRejectedRetrying`/`ReplyRejectedAgainUsingRawFallback`, as guardas `ArgumentNullException` em `BrainException.InvalidOutput` e `TutorReply.RawFallback`, `paramName: "kind"` e a mensagem do `FakeTutorBrain` sem resposta, todos decididos no Red da T7. Esse é o efeito que o AP-014 queria evitar. O usuário aceitou os desvios, então não bloqueia. Esperado nas próximas US: cada escolha de forma aparece na §12 num commit `docs(ai)` anterior ao `feat` que a implementa.
- **S2** — `src/EnglishTutor.Core/Conversation/ConversationService.cs:18`. O campo `_logger` só copia o parâmetro `logger` do construtor primário, enquanto `brain` e `promptSource` são usados direto. Esperado: uma forma só para as dependências da classe (ou todas capturadas, ou todas em campos), sem cópia redundante.
- **S3** — Guardas de nulo fora do plano: `BrainException.cs:41-42`, `TutorReply.cs:16`, `ConversationTurnResult.cs:28,34`, `ConversationWindow.cs:22` e `TurnMessageBuilder.cs:13-15,29`. Nenhum teste fixa essas guardas, e a cobertura de linhas não as enxerga porque cada uma ocupa uma linha só (AP-016). Esperado: ou um teste por guarda que faz parte do contrato público (no mínimo `BrainException.InvalidOutput` e `TutorReply.RawFallback`, que os adaptadores da US-004/US-006 vão chamar), ou o registro no plano de que elas são defensivas e não têm teste.

## Pontos positivos
- O Red de cada tarefa foi descrito com o motivo real da falha (§5), inclusive o estado intermediário do ramo `InvalidOutput` entre T7, T8 e T9, que manteve o Red dos testes seguintes.
- Os testes usam valores distintos de verdade (AP-017): `raw1` ≠ resposta aceita, `RawText` ≠ `Reply`, rótulos por turno. Assim, nenhuma asserção de janela ou de histórico passa por coincidência.
- O teste `SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls` fecha o risco de um `CancellationToken.None` escondido. E4 (`BeSameAs(retryFailure)`) deixou a asserção mais forte, sem `if` no teste.
- A lista de mensagens é nova a cada request, e o teste confere que `Requests[0]` não foi alterado depois da retentativa.
- Os itens adiados ganharam dono na spec da US-004 (cenários + D8–D10) antes do código, o que fecha o adiamento em cadeia (B1 da revisão do plano).
- Os logs não levam texto do aluno nem texto cru, e há teste negativo para isso.

## Aprendizados para o processo
- **AP-014 recorre pela terceira vez** (US-001 como AP-009, US-002 e agora US-003, com E1–E7 todos no commit de evidências). O próprio Executor reconheceu isso em E5. A regra escrita no `executor.md` e o item não obrigatório do checklist não bastaram. Proposta: (a) em `.claude/agents/executor.md` e no modelo da §5 do `planejador.md`, cada tarefa fecha com a linha "Escolhas de forma: nenhuma | §12 em `<hash>`", preenchida antes do commit `feat`; (b) em `checklist-revisao` §1, promover o item AP-014 a [O] quando a escolha muda texto exibido, mensagem de exceção ou nome público; nos demais casos, continua sugestão.
- **Igualdade de `record` com coleção** (E3, c5cbc1a). Um `record` com membro `IReadOnlyList<T>` compara a lista por referência, então `Equal` sobre records "iguais" falha. Proposta: nota curta em `.claude/skills/tdd-dotnet/SKILL.md` (Organização): "para comparar records com coleções, compare uma projeção (ex.: chave) ou use `BeEquivalentTo`; não use `Equal` sobre o record inteiro".
- **Mutação de Red que não compila** (E1, CA1805 em `= 0`). Proposta: em `.claude/skills/tdd-dotnet/SKILL.md`, junto do AP-017, dizer que a mutação temporária pode ser qualquer forma equivalente que compile (ex.: remover o inicializador), registrada na tarefa.
- **Ponto único de aplicação da §2.6 virou dois commits** (4b82f0a e f522128, registrado na §12). Não causou dano, mas o plano e o fluxo do `/us` dizem coisas diferentes. Proposta: alinhar o `planejador.md` ao fluxo do `/us` (um commit para a spec e outro para a arquitetura/skills), para o plano não prometer um commit único.

Arquivos relevantes:
- C:\Crescimento\TutorIA\tasks\US-003\plan.md
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Conversation\ConversationService.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Core.Tests\Conversation\ConversationServiceTests.cs
- C:\Crescimento\TutorIA\docs\aprendizados.md

## Rodada 2

Data: 2026-10-17
Commits revisados: 1cfa147..6591575 (pedido do usuário: 1cfa147; escolhas de forma: 3a09c34; código: d00a61f, e704924; evidências: 6591575)
Resultado: APROVADO

### Itens da rodada anterior
| Item | Status |
|---|---|
| S1 — escolhas de forma da §12 registradas depois dos `feat` (AP-014) | Não estava no pedido do usuário (§12, 1cfa147). Nesta rodada a regra foi seguida: as escolhas de S2 e S3 estão em 3a09c34, antes de d00a61f e e704924. Segue como aprendizado do processo, sem pendência nesta US. |
| S2 — `ConversationService` com o campo `_logger` que só copiava o parâmetro | Resolvido em d00a61f. O campo saiu e `logger` é usado direto (`ConversationService.cs:55`, `:71`), como `brain` e `promptSource`. `_window` continua campo porque é estado da sessão (justificado na §12). Sem mudança de comportamento: `ConversationServiceTests` 21/21 e nenhum teste alterado. |
| S3 — guardas de nulo sem teste e sem registro | Resolvido em e704924 (testes) + 3a09c34 (registro). `BrainExceptionTests.InvalidOutput_WithNullArgument_ThrowsArgumentNullException` (2 casos, `ParamName` conferido) e `TutorReplyTests.RawFallback_WithNullRawText_ThrowsArgumentNullException` (1 caso). As guardas de `ConversationTurnResult`, `ConversationWindow.Add` e `TurnMessageBuilder` ficaram registradas como defensivas, e a US que criar um novo chamador assume o teste. Red por mutação registrado na T13 com o motivo de cada falha (NRE no construtor privado e em `rawText.Trim()`; "no exception was thrown" sem a guarda de `rawText` em `InvalidOutput`). Os três motivos batem com o código (`BrainException.cs:41-43`, `TutorReply.cs:16-17`). |

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ (0 avisos, 0 erros) |
| Testes (exceto LiveBrain) | ✅ 151 passaram (Core 102, Console 25, Infrastructure 24) |
| Cobertura de linhas do Core | 100% (namespace `Conversation`: 100%, 168/168; ramos 85%, 17/20; geral 87,8%) |
| Cenários BDD com teste | 7/7 (sem mudança; D8 com 21 casos / 18 métodos) |
| Teste antes do código (ordem de commits) | ✅ (S2 é refactor sem teste novo, com os 21 casos verdes antes e depois. S3 é caracterização: os testes nascem verdes e o Red por mutação não commitada está registrado na T13, conforme a §12) |
| Evidências da DoD conferem | ✅ (§11.1: D1, D2, D3/D3b, D4, D5, D6, D8, D10 e D11 reproduzidos. D7 reconferido: `CoreDependencyTests` 3/3. D9 é evidência do Executor da Rodada 1, não reproduzida, e não foi refeita porque o console não mudou, AP-008) |
| Intenção antes do código (spec/ADR → código) | ✅ (pedido do usuário em 1cfa147 → escolhas de forma em 3a09c34 → código em d00a61f/e704924 → evidências em 6591575, AP-014) |

#### Evidências da DoD (§11.1) × resultado do Revisor
| # | Executor | Revisor |
|---|---|---|
| D1 | 0 avisos, 0 erros | 0 avisos, 0 erros ✅ |
| D2 | 151 (102/25/24) | 151 (102/25/24) ✅ |
| D3/D3b | Core 100%, geral 87,8% | Core 100%, geral 87,8% ✅ (relatório em `%TEMP%`) |
| D4 | sem diferenças, exit=0 | exit=0 ✅ |
| D5 | 102, 120 ms | 102, 121 ms ✅ |
| D6 | `Conversation` 100% (168/168), ramos 85% (17/20) | igual ✅ |
| D8 | 21 casos (18 métodos) | 21 ✅ |
| D10 | vazio | `git status --short` vazio ✅ |
| D11 | 0, exit 1 | 0, exit 1 ✅ |

### Bloqueantes
Nenhum.

### Sugestões
- **S4** — `tasks/US-003/plan.md:378-386` (§4, detalhamento das contagens). O cabeçalho foi atualizado ("37 métodos / 54 casos; `BrainExceptionTests` 3 / 4 e `TutorReplyTests` 1 / 1"). A lista abaixo dele não foi: ainda diz `Core.Tests: 34 métodos / 48 casos` e `BrainExceptionTests: 2 / 2`, e não tem a linha de `TutorReplyTests`. A frase "Suíte inteira após a US: **148 casos** (151 após o review S3, ...) (Core.Tests 51 + 48 = 99; ...)" mistura os dois estados. Esperado: o detalhamento soma o mesmo total do cabeçalho, para o Documentador não copiar o número antigo.
- **S5** — `tasks/US-003/plan.md:425-426` (T12, T13). As linhas estão marcadas `[x]`, mas não têm o "feito: `<hash>`" que as tarefas T1–T11 têm. Os hashes só aparecem na §3 e na §11.1. Esperado: T12 → `d00a61f` e T13 → `e704924` na própria linha da tarefa.
- **S6** — §12 (3a09c34), lista das guardas defensivas. As linhas citadas vieram da minha Rodada 1 e estão uma acima do real: `ConversationTurnResult.cs:27,33` (não 28,34) e `TurnMessageBuilder.cs:28` (não 29). O nome dos métodos já identifica cada guarda, então o efeito é pequeno. Esperado: linhas corretas ou só os nomes dos métodos.

### Pontos positivos
- A ordem docs → código do AP-014 foi seguida nesta rodada: as escolhas de forma (nomes dos testes, `ParamName`, quais guardas ficam sem teste e por quê) foram commitadas antes dos dois commits de código.
- O Red de cada caso de caracterização foi mostrado por mutação, uma guarda por vez, e o motivo de cada falha foi registrado. Os motivos são diferentes entre si e coerentes com o código. Assim, cada teste falha pela razão certa (AP-010).
- O `[Theory]` de `InvalidOutput` deixa o outro argumento válido e confere `ParamName`, então cada caso isola uma guarda.
- A decisão sobre as guardas sem teste diz quem vai criar o teste (a US que criar um novo chamador), em vez de deixar o assunto em aberto.
- O efeito do S2 na cobertura (169 → 168 linhas) foi explicado na evidência D6.

### Aprendizados para o processo
- **Contagens atualizadas só no cabeçalho** (S4). Quando uma rodada de correção acrescenta testes, o Executor atualiza o total e a DoD, mas não o detalhamento por classe. Proposta: em `.claude/agents/executor.md`, na etapa de evidências, incluir "mudou uma contagem → atualize o cabeçalho, o detalhamento por classe da §4 e D2/D5/D8, e confira se a soma bate".
- **Linha citada pelo Revisor copiada sem conferir** (S6). O Executor copiou para a §12 números de linha da Rodada 1 que estavam errados. Proposta: em `checklist-revisao`, seção "Como escrever bloqueantes e sugestões", pedir que o Revisor identifique o trecho pelo símbolo (classe/método) além da linha. Em `executor.md`, pedir que o Executor confira a linha antes de copiá-la.
- **AP-014 cumprido na rodada de correção.** É a primeira vez nesta US que o registro da §12 vem antes do código. Isso indica que a etapa "registrar escolhas → commit `docs(ai)` → código" funciona quando o pedido do usuário já define o escopo. Vale manter a proposta (a) da Rodada 1 (linha "Escolhas de forma: nenhuma | §12 em `<hash>`" por tarefa) para que isso aconteça também na primeira passada.

Arquivos relevantes:
- C:\Crescimento\TutorIA\tasks\US-003\plan.md
- C:\Crescimento\TutorIA\tasks\US-003\review.md
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Conversation\ConversationService.cs
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Brain\BrainException.cs
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Brain\TutorReply.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Core.Tests\Brain\BrainExceptionTests.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Core.Tests\Brain\TutorReplyTests.cs
