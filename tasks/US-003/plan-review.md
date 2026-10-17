# Revisão do plano — US-003 ConversationService dono do histórico

## Rodada 1

Data: 2026-10-17
Escopo revisado: `tasks/US-003/plan.md` e `docs/adr/ADR-006-retentativa-de-formato-e-historico-no-conversation-service.md`, no commit d88a842 da branch `feature/US-003_conversation-service`, sobre `develop`. Ainda não há código na branch. Também conferi:
- a US-003 em `specs/sprints/SPRINT_1.md` e a US-004, que vai receber o que foi adiado;
- as regras RN-CONV01–06, RN-BRAIN01/04/07, RN-STT01 e RN-TTS01;
- `ARCHITECTURE.md` (§2–§5, §6.3, §9, §10), `CLAUDE.md` e `docs/aprendizados.md`;
- as skills `padroes-codigo`, `tdd-dotnet`, `bdd-para-testes` e `checklist-revisao`;
- os arquivos de projeto: `Directory.Packages.props`, `Directory.Build.props` e os csproj;
- o código atual que o plano altera: `ChatSession`, `ConsoleText`, `LocalCommandParser`, `LocalCommand(s)`, `TutorMode`, `TutorReply`, `FeedbackFormatter`, `TutorReplyRejections`, `CoreServiceCollectionExtensions`, `CoreDependencyTests`, `RecordingLogger`;
- os testes existentes: `ChatSessionTests`, `LocalCommandParserTests`, `TutorModeTests`, `CoreServiceCollectionExtensionsTests`;
- os planos da US-001 e da US-002, para saber o que foi adiado para a US-003.

Base atual reproduzida: `dotnet build` com 0 avisos e `dotnet test --filter "Category!=LiveBrain"` com 97 casos verdes (Core.Tests 51, Infrastructure.Tests 24, Console.Tests 22). Esses números conferem com a T1 e com a base usada na §4.
Resultado: REPROVADO (1 bloqueante)

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD aparece no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ 7/7 cenários. Cada um tem teste no `ConversationServiceTests`; "entrada vazia" também tem teste no `ChatSessionTests` |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P1–P13 terminam com "Teste: ..." e todos os testes estão na §4 e em alguma tarefa (T2–T10). P14 está marcada "Sem efeito observável" |
| [O] DoD da §10 cobre a DoD da US (D1–D4 comuns, D5 < 10 s, D6 ≥ 90% em `Conversation`), com comandos e resultado esperado, incluindo Core ≥ 80% | ✅ D3/D3b cobrem o Core ≥ 80% e D6 o namespace `Conversation`. D9 roda no Windows PowerShell 5.1 com BOM (AP-006). D11 está na forma única (AP-019). A ressalva do D5 está na S5 |
| [O] Conformidade com R1–R4, RN-* citadas e regras rápidas do `CLAUDE.md`; decisões técnicas relevantes com ADR Proposto | ✅ ADR-006 está Proposto, criado no mesmo commit do plano. A retentativa fica no Core (R4). O Core passa a usar `Logging.Abstractions`, que R1 e §2 permitem, e o `CoreDependencyTests` é atualizado na mesma T1. O `CancellationToken` aparece em toda assinatura assíncrona |
| [O] Escopo: nada além nem aquém; fora de escopo explícito | ❌ A §8 é explícita, mas o que vai para a US-004 não tem dono na spec. É a segunda vez que esses itens são adiados (US-001 → US-003 → US-004). Ver B1 |
| [O] Consistência interna §3 × §4 × §5 × §6 com contagens que batem | ✅ 31 métodos / 46 casos. Core.Tests 30/43: 16+5+5+6+3+2+1+4+1. Console.Tests 1/3. Por tarefa: T7 7/9, T8 4, T9 3, o que dá os 14/16 de `ConversationServiceTests`. 97 + 46 = 143. Core 94 e Console 25 conferem com D2/D5/D8. A §3 lista todos os arquivos dos testes e tarefas e a §6 tem 1 pacote |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ O estado do ramo `InvalidOutput` está escrito no fim da T7 e da T8 (AP-018 b). O `TutorReply.RawFallback` só nasce como esqueleto na T9. O teste de padrão 10 é de caracterização, com Red por mutação (AP-017). A troca do caso `"/mode interview"` na T2 mantém o teste de desconhecido verde |
| Alteração na `ARCHITECTURE.md` lista as linhas do `CLAUDE.md` e das skills que repetem a regra | ⚠️ A lista existe, mas a faixa de linhas da `bdd-para-testes` está deslocada em uma linha (S1) |
| Riscos e ambiguidades que travariam o Executor | ✅ CS8775 e CA1032 já têm a forma decidida no plano (AP-009). A lista de mensagens é nova a cada request. As ressalvas sobre valores distintos e token estão nas S2 e S3 |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ `[LoggerMessage]` vem do `Microsoft.Extensions.Logging.Abstractions` 10.0.13, que já está no CPM. `IValidateOptions`/`Options.Create` vêm do `Microsoft.Extensions.Options`, que o Core já usa. A guarda do construtor primário pode ser feita no inicializador do campo. Acrescentar `LocalCommandKind.Mode` não quebra o `switch` do `ChatSession`, que tem `default` |

### Bloqueantes
- [ ] **B1** — `tasks/US-003/plan.md` §2.4, §2.5, §8 e §2.6 ("`specs/` não muda"). O plano adia para a US-004 cinco itens:
  - a ligação do `ConversationService` ao console (handler que delega ao serviço);
  - `/mode` no console e em `/help`;
  - o bind de `Conversation:MaxTurnsInWindow` com `ValidateOnStart` e a chave no `appsettings.json`;
  - o registro do serviço no DI;
  - a decisão sobre `SessionHint`.

  A US-004 em `specs/sprints/SPRINT_1.md` não cita nenhum deles. Parte já tinha sido adiada da US-001 para a US-003 (`tasks/US-001/plan.md:283,285`, `docs/us/US-001.md:12-13`).

  Efeito: ao fim desta US, ao digitar `/mode job-interview` o aluno continua vendo `Unknown command "/mode".`. Mesmo assim, o cenário "troca de modo" diz "Quando digito", e a RN-CONV03 diz que N é configurável. Nada na spec garante que a US-004 entregue isso.

  Esperado: mover escopo entre US é mudança de spec (hábito 4; checklist §1 "mudanças de comportamento estão primeiro na spec"). Há dois caminhos, à escolha do usuário em P1/P12:
  - os itens adiados passam a constar na US-004 do `SPRINT_1.md` (cenário ou DoD extra), no mesmo commit `docs(ai)` anterior à T1 descrito na §2.6, e a §2.6 deixa de dizer "`specs/` não muda";
  - ou eles entram nesta US.

### Sugestões
- **S1** — `plan.md` §2.6 e §3 (linha do `bdd-para-testes`). Hoje o exemplo da skill tem `{` na linha 55 e o trecho a trocar (`var brain = ...` até `ConversationServiceBuilder.With(brain).Build();`) nas linhas 56–59. O plano manda trocar as "linhas 55–58". Aplicado ao pé da letra, isso apaga a chave e deixa `ConversationServiceBuilder` no exemplo, que é o falso bloqueante que a §2.6 quer evitar (AP-005). Corrija a faixa ou identifique o trecho pelo texto antigo, não pelo número de linha.
- **S2** — `plan.md` §4. Algumas asserções precisam distinguir qual turno ou qual resposta ficou no histórico (AP-017, valores distintos):
  - em `SendAsync_WhenWindowIsFull_...`, a resposta do professor de cada turno precisa ser distinguível. Se os 3 turnos usarem o mesmo `TutorReplies.Valid()`, uma janela que pareasse aluno e professor de turnos diferentes passaria;
  - em `SendAsync_AfterRawFallbackTurn_...` e `SendAsync_AfterRetriedTurn_...`, a resposta rejeitada da primeira chamada (raw1) precisa ser diferente da que fica (raw2 / `RawText` da 2ª resposta), para provar que a janela guarda a segunda;
  - o `TutorReplies.Valid()` usado em `SendAsync_WhenFirstReplyIsInvalid_ReturnsSecondReplyWithFeedback` precisa ter feedback (correção ou elogio), senão "`FeedbackFormatter.Format` não vazio" não se sustenta.
- **S3** — `plan.md` §2.2 e §4. Nenhum teste fixa que o cancelamento pedido pelo chamador de `SendAsync` chega ao `ITutorPromptSource` e ao cérebro, nas duas chamadas da retentativa. Hoje, uma implementação que passasse `CancellationToken.None` adiante deixaria todos os testes verdes. O checklist exige o `CancellationToken` repassado em toda a cadeia assíncrona.
- **S4** — `plan.md` §9 P11 e §4. A P11 só é testada na primeira chamada. Se a segunda chamada (a retentativa) falhar com outro `BrainErrorKind` ou for cancelada, a falha deve propagar e o turno não pode entrar no histórico. Nada fixa isso.
- **S5** — `plan.md` §10 D5. `Measure-Command { dotnet test ... } ; dotnet test ...` roda a suíte duas vezes, e o tempo medido inclui o build. O critério usa a linha "Duração" da segunda execução. Deixe um comando só e diga qual número é comparado com os 10 s, para a evidência não ficar ambígua.
- **S6** — `plan.md` §2.2 (`ChangeMode` com `ArgumentNullException` e `ThrowIfNull(text)` em `SendAsync`). São comportamentos descritos sem teste. Hoje é só cobertura de linha; um teste curto de cada guarda evita regressão silenciosa.
- **S7** — `Directory.Packages.props`. `Microsoft.Extensions.Logging.Abstractions` está no `ItemGroup Label="Infrastructure"`. Com a T1 o Core passa a usá-lo, e a §6 diz "já no CPM". Não é obrigatório, mas movê-lo para o grupo "Core" na T1 deixa a fronteira do R1 legível no arquivo de versões.

### Pontos positivos
- A intenção vem antes do código: ADR-006 Proposto no commit do plano, com o texto das alterações da `ARCHITECTURE.md` (§3, §4, §5) pronto e um ponto único de aplicação antes da T1 (aprendizado da S7 da US-002).
- O estado do ramo `InvalidOutput` está declarado no fim da T7 e da T8, de modo que cada teste da T8 e da T9 falha no Red pelo motivo certo (AP-018 b).
- As formas sensíveis ao compilador já estão decididas no plano: auto-propriedade armazenada para `[MemberNotNullWhen]` e os construtores do CA1032. O plano também lembra do `!` depois de `Should().BeTrue()` (AP-009, AP-020).
- As contagens batem em todos os níveis (método, caso, tarefa, projeto e suíte) e conferem com a base que reproduzi (97 casos).
- As mensagens conferidas são todas do projeto (AP-015). Os logs não levam o texto do aluno nem o texto cru. O limite da janela e do validador é testado nos dois lados (AP-016).
- O D9 exercita o aviso de linha em branco na primeira linha com BOM, no Windows PowerShell 5.1 (AP-006).
- As alternativas descartadas têm motivo claro, como não ligar ao console um cérebro provisório que seria jogado fora.

### Aprendizados para o processo
- **Adiamento em cadeia sem dono na spec (novo).** A US-001 adiou `/mode`, o aviso de linha em branco e `Conversation:MaxTurnsInWindow` para a US-003. A US-003 entrega parte e adia o resto para a US-004, cuja spec não cita nada disso. Proposta para `.claude/agents/planejador.md` e o checklist de revisão de plano: "todo item da §8 que diz 'fica para a US-YYY' é registrado na spec da US-YYY (cenário ou DoD extra) no commit `docs(ai)` anterior à T1; o Revisor de plano confere a spec da US de destino".
- **Referência a documento por número de linha (novo, ligado ao AP-005).** A faixa "55–58" da `bdd-para-testes` está deslocada em uma linha. Proposta: na §2.6 (alterações nos documentos-raiz), o Planejador identifica o trecho a substituir pelo texto antigo (citação), não só pelo número da linha.
- **AP-017 recorrente (valores distintos entre campos ou turnos).** Apareceu de novo na S2. Reforça a proposta do AP-017 (Status: Proposta) para a `tdd-dotnet`: em testes de janela/histórico, cada turno tem aluno e professor com textos únicos, e a resposta rejeitada difere da aceita.

## Rodada 2

Data: 2026-10-17
Escopo revisado: `tasks/US-003/plan.md` no commit 8efac37 (`docs(ai): revise plan for US-003`) da branch `feature/US-003_conversation-service`, comparado com a Rodada 1 (d88a842). O commit só altera o `plan.md` (+138/−49) e ainda não há código na branch. Também conferi o que o plano agora cita pelo texto antigo:
- a US-004 em `specs/sprints/SPRINT_1.md` (linhas 221–266: linha de regras, último cenário "medição de latência" e linha D7 da DoD extra);
- `.claude/skills/bdd-para-testes/SKILL.md` (linhas 50–59) e `.claude/skills/tdd-dotnet/SKILL.md` (linhas 18–59);
- `ChatSession.cs`, `ChatSessionTests.cs` (linhas 36–92), `LocalCommandParserTests.cs` (caso `"/mode interview"`), `CoreDependencyTests.cs` (3 `[Fact]`), `TutorReplyRejections.cs`, `ExitCodes.cs`, o ADR-006 e `docs/aprendizados.md`.

Base reproduzida de novo: `dotnet test --filter "Category!=LiveBrain"` dá 97 casos verdes (Core.Tests 51, Infrastructure.Tests 24, Console.Tests 22).
Resultado: APROVADO

### Itens da rodada anterior
| Item | Status |
|---|---|
| **B1** — itens adiados para a US-004 sem dono na spec | Resolvido em 8efac37. A §2.6 agora traz o texto exato a aplicar na US-004 do `SPRINT_1.md`: a linha de regras (texto antigo e novo), 4 cenários e D8–D10. Os 5 itens adiados têm dono: ligação ao console e DI pelo cenário "console conversa pelo ConversationService"; `/mode` e `/help` pelos cenários "troca de modo pelo console" e "modo desconhecido" mais o D9; bind e `appsettings.json` pelo cenário "janela de turnos configurável" mais o D8; `SessionHint` pelo D10. A frase "`specs/` não muda" saiu. O commit é um só, antes da T1 (§2.6 e pré-condição da §5). O caminho (b) ficou como alternativa na P1. Conferi o texto antigo citado: a linha de regras confere com `SPRINT_1.md:227`, a última linha do gherkin com `:257` e o D7 com `:266` |
| **S1** — faixa de linhas da `bdd-para-testes` | Resolvido em 8efac37. Agora são as linhas 56–59 e a `{` da 55 fica, o que confere com o arquivo. O trecho também é identificado pelo texto antigo citado, com a mesma indentação. O texto novo usa `ReturnsInvalid`, `TutorReplies.Valid("t1")` e `CreateService`, que batem com a §3 e a §2.6 (Alternativas descartadas) |
| **S2** — valores distintos (AP-017) | Resolvido em 8efac37. O parágrafo "Valores distintos" da §4 define `TutorReplies.Valid(label)`, com `Reply` ≠ `RawText` e uma `Correction`. `WhenWindowIsFull` usa t1/t2/t3 e exclui `turn one` e `t1`. raw1 (`"rejected answer one"`) é diferente do texto aceito em `AfterRetriedTurn` e em `AfterRawFallbackTurn`. `ReturnsSecondReplyWithFeedback` tem feedback garantido |
| **S3** — `ct` repassado | Resolvido em 8efac37. A §2.2 traz o parágrafo do `ct`. Os fakes têm `Tokens` (§3). O teste `SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls` fica na T8, e há a linha correspondente na §7 |
| **S4** — falha ou cancelamento na retentativa | Resolvido em 8efac37. A §2.2 (passo 5) e a P11 foram ampliadas. O `[Theory]` `SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn` tem 2 casos, e o motivo do Red está na T8 |
| **S5** — D5 ambíguo | Resolvido em 8efac37. Um comando só, com `--no-build`, e o número comparado é a duração da linha de resumo |
| **S6** — guardas sem teste | Resolvido em 8efac37. Entraram `SendAsync_WithNullText_ThrowsArgumentNullException` e `ChangeMode_WithNull_ThrowsArgumentNullException`. O esqueleto do `ChangeMode` nasce sem a guarda, o que garante o Red |
| **S7** — grupo do pacote no CPM | Resolvido em 8efac37. A mudança está na §2.5, na §3, na T1 e na §6 |

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD aparece no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ 7/7. Cada cenário tem teste no `ConversationServiceTests`. "Entrada vazia" também tem teste no `ChatSessionTests` |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P1–P13 terminam com "Teste: ...". Os testes novos citados (P3 e P11) estão na §4 e na T8. P14 continua "Sem efeito observável" |
| [O] DoD da §10 cobre a DoD da US, com Core ≥ 80% | ✅ D3/D3b cobrem o Core ≥ 80%. D5 tem um comando só e diz qual número é comparado com os 10 s. D6 cobre `Conversation` ≥ 90%. D2, D5 e D8 foram atualizados para 148, 99 e 21/18. D9 roda no PS 5.1 com BOM. D11 está na forma única (AP-019). A DoD comum inclui a US-004 do `SPRINT_1.md` antes da T1 |
| [O] Conformidade com R1–R4, RN-* e `CLAUDE.md`; ADR Proposto | ✅ Sem mudança em relação à Rodada 1. O ADR-006 (Proposto) já dizia que outros erros e o cancelamento propagam, o que cobre a ampliação da P11 sem precisar editar o ADR |
| [O] Escopo: nada além nem aquém; fora de escopo explícito | ✅ O que vai para a US-004 agora tem dono na spec (B1). A US-003 não ganhou comportamento novo, só testes de guarda e de propagação do que a §2.2 já descrevia |
| [O] Consistência §3 × §4 × §5 × §6, com contagens que batem | ✅ Recontei método a método. `ConversationServiceTests` tem 18 métodos e 21 casos (16 Fact + Theory de 3 + Theory de 2). TurnMessageBuilder 4/5, Window 3/5, TutorMode +2/+6, LocalCommandParser +1/+3, BrainException 2/2, Options 1/1, Validator 2/4, DI +1/+1: o Core.Tests soma 34/48. Com o Console.Tests (1/3), o total é 35/51. A suíte fica com 97 + 51 = 148 (Core 99, Console 25, Infra 24). Por tarefa: T7 9/11 + T8 6/7 + T9 3/3 = 18/21. A §3 inclui `Tokens`, `TutorReplies.Valid(label)`, `Directory.Packages.props` e `SPRINT_1.md` |
| [O] Ordem das tarefas preserva o Red | ✅ Conferi teste a teste. T7: os 8 testes de `SendAsync` falham com `NotImplementedException`, e o de `WithNullText` também, por ser a exceção errada. `ChangeMode_WithNull` falha porque o esqueleto não tem guarda. T8: com o estado da T7, o `InvalidOutput` propaga. Os 5 primeiros falham por exceção inesperada. O `[Theory]` S4 falha por `Kind`/tipo diferente e por `Requests` ter 1 item; isso exige que o teste confira o `Kind`, o que o plano declara. T9: a segunda `InvalidOutput` propaga (estado da T8). T4: o teste de caracterização tem Red por mutação (AP-017) |
| Alteração na `ARCHITECTURE.md` lista as linhas do `CLAUDE.md` e das skills que repetem a regra | ✅ A faixa da `bdd-para-testes` confere com o arquivo e o trecho é citado pelo texto antigo. Na `tdd-dotnet`, as linhas 22–23 e 43–59 continuam válidas: o exemplo de `FakeTutorBrain` é um subconjunto do fake planejado e já usa `Name => "Fake"` |
| Riscos e ambiguidades que travariam o Executor | ✅ A §7 ganhou as linhas do `ct`, dos valores distintos e do adiamento em cadeia. Ver S9 (código de saída no cenário novo da US-004) |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ Sem mudança em relação à Rodada 1. `CancellationToken` tem igualdade por fonte, então `brain.Tokens` com o mesmo `token` se compara bem. Mesmo quando o fake lança de forma síncrona em `SendAsync`, a exceção propaga pelo `await` do serviço |

### Bloqueantes
Nenhum.

### Sugestões
- **S8** — `plan.md` §4, `SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason` ("mensagem contém `Fake`"). O texto `Fake` vem do `Name` do `FakeTutorBrain`, mas a §3 não diz o valor de `Name`. O exemplo da `tdd-dotnet` usa `"Fake"`, então o risco é baixo. Mesmo assim, deixe na §3 que o `Name` do fake é `"Fake"`. Assim a asserção não depende do Executor copiar o exemplo da skill.
- **S9** — `plan.md` §2.6, cenário novo da US-004 "janela de turnos configurável". O cenário diz que a aplicação "encerra antes do primeiro prompt com a mensagem do validador", mas não diz o código de saída. Hoje a configuração inválida tem código próprio (`ExitCodes.InvalidConfiguration = 2`, `src/EnglishTutor.Console/ExitCodes.cs:10`), e a US-001 D6 exige "código de saída ≠ 0". Diga no cenário (ou num D da US-004) que o código é diferente de 0, ou que é o de configuração inválida. Sem isso, a US-004 pode entregar o encerramento sem um critério de saída verificável.
- **S10** — `plan.md` §5 T7 e T8. As listas de testes usam nomes abreviados, sem o prefixo `SendAsync_` e às vezes com `...` (ex.: `WhenWindowIsFull_...`). Os nomes completos estão na §4 e a conferência é possível, mas nomes completos nas tarefas evitam que o Executor crie um método com sufixo diferente do mapeado. Isso faria o D8 e a revisão de código conferirem nomes divergentes.

### Pontos positivos
- O B1 foi resolvido do jeito que o processo pede: o texto antigo e o novo da spec da US-004 estão prontos para aplicar, vão num commit só antes da T1 e o caminho alternativo ficou registrado. O efeito colateral também foi previsto: a US-004 vai precisar atualizar `RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing`.
- A §12 responde a cada item da Rodada 1 no formato "item → seções tocadas", o que tornou a reconferência direta.
- As contagens foram atualizadas em todos os lugares (§4, §5, D2, D5, D8) e batem entre si e com a base reproduzida (97 → 148).
- O motivo do Red do `[Theory]` S4 está escrito, inclusive a sutileza de que `ThrowAsync<BrainException>` passaria pelo tipo e só o `Kind` discrimina (AP-018 b).
- O esqueleto de `ChangeMode` sem guarda, criado de propósito para a guarda ter Red, mostra o cuidado com a ordem TDD.

### Aprendizados para o processo
- **Os aprendizados da Rodada 1 ainda não estão em `docs/aprendizados.md`.** São o adiamento em cadeia sem dono na spec e a referência a documento só por número de linha. O plano já aplica os dois na prática: a §2.6 registra na spec de destino e cita o trecho pelo texto antigo. Mesmo assim, o registro como AP e a regra no `planejador.md` e no checklist de revisão de plano ("todo item da §8 que vai para a US-YYY é registrado na spec da US-YYY antes da T1; o trecho a substituir é citado pelo texto antigo") continuam pendentes para o Documentador.
- **Cenário de spec criado para outra US herda critérios implícitos.** Na S9, o cenário novo da US-004 descreve o encerramento sem o código de saída, embora a US-001 já tenha fixado esse padrão. Proposta para o `planejador.md`: ao escrever cenário ou DoD para outra US, reutilizar os critérios observáveis que o projeto já fixou para o mesmo tipo de comportamento (código de saída, texto da mensagem, canal de saída).
