# Revisão do plano — US-008 Suíte de contrato do ITutorBrain

Data: 2026-10-22
Escopo revisado: `tasks/US-008/plan.md` (branch `feature/US-008_contrato-itutorbrain`, sem código ainda), `specs/sprints/SPRINT_1.md` (US-008), `ARCHITECTURE.md` §10, `.claude/skills/tdd-dotnet/SKILL.md`, ADR-009 e os adaptadores em `src/`.
Resultado: REPROVADO (1 bloqueante)

## Verificações (checklist, seção "Revisão de plano")
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ Os 2 cenários têm testes (K1–K15, G1–G5, L1, L2). |
| [O] Cada P* com efeito observável tem teste (§4) e tarefa (§5) | ✅ P1 a P8 apontam para um teste. |
| [O] DoD da §10 cobre a DoD da US, com comandos e resultado esperado (cobertura ≥ 80%) | ❌ Ver B1: o comando D7 não dá 0. |
| [O] Conformidade com R1–R4, `RN-*` e `CLAUDE.md`; ADR onde couber | ✅ Só código de teste. Os ADR-005, 006, 007 e 009 são consumidos, sem decisão nova. |
| [O] Escopo e adiamentos (AP-022, AP-040) | ✅ Fora de escopo explícito. Os 3 limites aceitos (L1, L2, L3) listam consumidores e efeito. O adiamento para a US-009 diz que não há texto novo a registrar, e isso confere com a spec. |
| [O] Consistência interna §3 × §4 × §5 × §6 | ✅ Eu refiz as somas: K1–K15 dão 23 casos em 16 métodos. T1 tem 12, T2 tem 5 e T3 tem 6, e 12+5+6 = 23. G1–G5 dão 6 casos. Os nomes de teste batem entre a §4, a §5 e as P*. |
| Transformações com teste nos dois lados do limite (AP-016) | ✅ K4a e K4b cobrem 0, 1, 2 e 3 perguntas. K11 cobre 999 ms e 1000 ms. |
| Condicionais da §2 com teste (AP-047) | ✅ O parágrafo "Condicionais da §2" na §4 cobre structured × texto e os papéis trocados em K12/K13. |
| [O] Ordem das tarefas preserva o Red | ✅ Os testes nascem verdes por serem de caracterização, e a mutação por tarefa é prescrita. O G1 vermelho para o Ollama vem num commit separado antes da bancada. |
| Tolerância e `LiveBrain` existentes (AP-051) | ✅ A §4 diz que não há relaxamento e cita os dois testes existentes. |
| Inventário de renomeação (AP-050) | N/A. Não há renomeação. A busca da §2.6 foi conferida: `ARCHITECTURE.md` 271–272, `tdd-dotnet` 26–27 e 87–98, e `SPRINT_1.md` 494 existem. |
| Critério `LiveBrain` (AP-032, AP-052) | ✅ A §2.4 e a §7 dizem a camada, a tolerância zero, as 3 rodadas, o dono da decisão e que a retentativa não é exigida. |
| `ValidateOnStart` em várias Options (AP-039) | N/A. |
| Justificativas que citam outra US (AP-036) | ✅ Conferi o ADR-009 (Ollama indisponível é `Unknown`, `CliNotFound` só no Claude Code) e `OllamaFailures` (a mensagem cita `Brain:Ollama:Endpoint` e `Brain:Ollama:TimeoutSeconds`). Os mínimos de timeout são 1 s nos dois Options. |
| Busca de "nenhuma linha" com controle positivo (AP-035) | ❌ Ver B1: o controle positivo do D7 existe, mas o negativo está errado. |
| Alteração na `ARCHITECTURE.md` lista as linhas repetidas no `CLAUDE.md` e nas skills | ✅ O `CLAUDE.md` não repete a regra. As linhas 26–27 e 87–98 do `tdd-dotnet` entram na §2.6. |
| Riscos e ambiguidades tratados | ✅ Resume do Claude Code (K8/K9), formato do `--list-tests`, hook (AP-044). |
| Viabilidade técnica | ✅ `FakeProcessRunner`, `FakeChatClient` e `OllamaTestKit` existem. `_minQuestions` e `_maxQuestions` existem em `TutorReplyValidator`. `OllamaFailures.IsServerFailure` existe. `ConversationService` já é montado em `OllamaConversationLiveTests`. |

## Itens por área
- Todas as classes de teste existentes com o trait `LiveBrain` terminam em `LiveTests` (7 classes conferidas). Por isso G3 e G4 nascem verdes, como o plano afirma.
- A contagem do plano é consistente: 23 casos × 2 provedores = 46 no D5, e 23 em cada filtro do D6.
- Os nomes `*ContractLiveTests` e `ContractSuiteStructureTests` não contêm a subcadeia `ContractTests`. O D5 da spec não arrasta testes live nem as guardas. Os métodos G1/G2 também não contêm a subcadeia.

## Bloqueantes
- [ ] **B1** — `tasks/US-008/plan.md` §10 D7 (e o texto idêntico do D7 que a §2.6, Commit 1, manda registrar em `SPRINT_1.md`) × §4 G3 e G4 — o comando `dotnet test --no-build --filter "Category!=LiveBrain" --list-tests | grep -c "LiveTests"` não vai dar `0`. Os testes G3 `LiveTests_Always_CarryTheLiveBrainTrait` e G4 `LiveBrainTrait_OnlyOnClassesNamedLiveTests` rodam em `Category!=LiveBrain` (a classe `LiveBrainTraitTests` não tem o trait). Os dois têm "LiveTests" no nome do método, e a listagem mostra o nome completo. A contagem sai ≥ 2, e a verificação de que os testes live ficam fora do filtro padrão falharia por causa das próprias guardas. Esperado: a busca do D7 só pode casar o que identifica os testes live (por exemplo, o nome de classe `...LiveTests.` com o ponto, ou a busca de `ContractLiveTests` nos dois filtros), ou os métodos G3/G4 são renomeados sem a subcadeia `LiveTests`. O comando precisa ter o controle positivo e o negativo demonstrados (AP-035), com o texto idêntico na spec e no plano. Regra: checklist §0 (DoD verificável e AP-035).

## Sugestões
- **S1** — `plan.md` §5 T4: as mutações do Ollama cobrem K1, K2, K5–K8, K10–K12 e K15. Faltam K3, K4a/K4b, K9, K13 e K14. Vale prescrever pelo menos K13 (adaptador que guarda a falha) e K14 (`ThrowIfNull`) para provar que a suíte do Ollama não nasce verde por acaso. Os mesmos pontos já são prescritos para o Claude Code nas T1–T3.
- **S2** — `plan.md` §4 K13: o comentário cita `RN-BRAIN01` (janela sem estado), mas o teste é de recuperação após falha. `RN-BRAIN07` descreve melhor o que ele prova.
- **S3** — `plan.md` §5 T4: a mutação "`Skip(1)` na conversão de mensagens" é aplicada em `OllamaTutorBrain`, onde as mensagens vêm de `messages.AddRange(request.Messages.Select(ToChatMessage))`. Diga o ponto exato, para o Executor não improvisar.

## Pontos positivos
- Não há mudança em `src/`, e o D12 garante isso depois das mutações. A regra de desfazer mutação por edição inversa, e não por `git checkout`, está explícita (AP-037).
- A bancada por provedor resolve a diferença entre `IProcessRunner` e `IChatClient`. O plano trata o `--resume` do Claude Code com K6/K7 (instância nova), K8 (janelas sem relação) e K9 (só o resultado).
- A §2.6 identifica cada trecho a alterar pelo texto antigo e pelo novo, e trata o `ARCHITECTURE.md` e a skill juntos. Os adiamentos dos ADR-005, 006, 007 e 009 foram todos fechados.
- A guarda por reflexão (G1–G5) impede um provedor novo sem subclasse e a contaminação do filtro `Category!=LiveBrain`.

## Aprendizados para o processo
- Comando de DoD que conta linhas de listagem por substring pode casar nomes de método das próprias guardas, não só o nome de classe. A regra seria: antes de fixar o padrão de um `grep -c` sobre `--list-tests`, o Planejador confere se algum método planejado na §4 contém a subcadeia. Pode virar um complemento do AP-035.
