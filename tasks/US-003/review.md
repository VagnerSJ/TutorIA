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
