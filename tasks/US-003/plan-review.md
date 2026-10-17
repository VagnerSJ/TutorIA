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
