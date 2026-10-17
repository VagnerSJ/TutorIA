# Revisão do plano — US-002 Contrato do professor e parser único da resposta

## Rodada 1

Data: 2026-10-17
Escopo revisado: `tasks/US-002/plan.md` e `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md` (commit 8559a18, branch `feature/US-002_contrato-parser-resposta` sobre `develop`). Ainda não há código na branch. Também conferi a US-002 em `specs/sprints/SPRINT_1.md`, as regras RN-BRAIN03/04 e RN-FEED01–05, `ARCHITECTURE.md` (§2, §3, §4, §5, §6.2, §10), ADR-002, `CLAUDE.md`, as skills `padroes-codigo`, `tdd-dotnet` e `bdd-para-testes`, `docs/aprendizados.md`, `Directory.Packages.props`, `EnglishTutor.slnx`, os csproj e o hook `post-edit-dotnet.mjs`.
Resultado: REPROVADO (2 bloqueantes)

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD aparece no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ 6/6 cenários, cada um com teste no parser (Infrastructure.Tests) e no Core |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ❌ P9, P5 e P7 (espaços antes do `{`) sem teste; P4 só implícito (B1) |
| [O] DoD da §10 cobre a DoD da US (D1–D4 comuns, D5, D6), com comandos e resultado esperado, incluindo Core ≥ 80% | ✅ (D3/D3b para cobertura; D6 da US vira teste mais evidência colada). Ressalva sobre o D11 na S1 |
| [O] Conformidade com R1–R4, RN-* citadas e regras rápidas do `CLAUDE.md`; decisões técnicas relevantes com ADR Proposto | ✅ ADR-005 resolve o conflito entre §6.2 e R4. Core continua só com BCL. `Parse` síncrono sem `CancellationToken` tem justificativa |
| [O] Escopo: nada além nem aquém; fora de escopo explícito | ✅ §8 claro. `FeedbackFormatter` tem justificativa no cenário "sem erros" (P2) |
| [O] Consistência interna §3 × §4 × §5 × §6 com contagens que batem | ✅ 26 métodos / 37 casos; Core.Tests 27 hoje + 19 = 46 (conferido com `dotnet test`: 27 e 22 verdes); T3+T4 = 7, T6+T7 = 14; 7 fixtures; pacotes coerentes |
| [O] Ordem das tarefas preserva o Red de cada teste | ❌ T6 → T7: o teste de texto fora do formato pode já nascer verde (B2) |
| Alteração na `ARCHITECTURE.md` lista as linhas do `CLAUDE.md` e das skills que repetem a regra | ✅ §2.4 (`CLAUDE.md:31`, `checklist-revisao`, `tdd-dotnet:25`). O desvio das "fixtures reais" (§10) não está listado (S5) |
| Riscos e ambiguidades que travariam o Executor | ⚠️ `MemberNotNullWhen(false, …)` quase certamente gera aviso (S2); ramo "raiz não-objeto" inalcançável (S3) |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ `JsonDocument`, `[GeneratedRegex]` (exige classe `partial`, S8), `[LoggerMessage]` do Logging.Abstractions 10.0.13, `dotnet sln … .slnx list` |

### Bloqueantes
- [ ] **B1** — `tasks/US-002/plan.md` §9 (P4, P5, P7, P9) × §4/§5. Algumas perguntas em aberto definem um comportamento observável que nenhum teste da §4 fixa:
  - **P9**: linhas repetidas do mesmo rótulo nas tags (`Pronunciation:`/`Vocabulary:`/`Praise:`) são unidas com `"; "`. A fixture `tags-labeled-lines.txt` tem uma linha por rótulo, então a união nunca é exercitada.
  - **P5**: JSON dentro de cerca ```` ```json ```` ou com texto em volta é rejeitado com motivo e mensagem claros. Nenhum teste fixa esse resultado. Quando a US-006 mudar a decisão, nenhum teste vai acusar a mudança.
  - **P7**: a detecção usa o primeiro caractere **não branco**. Nenhum caso tem espaço ou quebra de linha antes do `{`. Todas as fixtures JSON começam com `{`.
  - **P4**: resposta sem correções e sem `praise` é aceita **sem aviso**. Isso só fica coberto se o candidato de `Validate_WhenReplyEndsWithQuestion_ReturnsValidReplyWithoutWarnings` por acaso não tiver nada disso. O plano não diz.

  Esperado: cada um desses efeitos tem teste nomeado na §4 e tarefa na §5, com as contagens da §4, do D2 e do D5 atualizadas (checklist §0, item "Cada resposta P* com efeito observável…").
- [ ] **B2** — `tasks/US-002/plan.md` §5, T6 e T7. A T6 implementa no Green "detecção + caminho JSON", mas o plano não diz o que o caminho não-JSON faz ao fim da T6. Se a parte que não é JSON já devolver um candidato com `Reply` vazio, `Parse_WhenTextHasNoJsonNorTags_RejectsWithMissingReply` (T7) nasce verde e nunca passa pelo Red. Esperado: o plano diz o estado do caminho de tags ao fim da T6, de modo que todos os testes da T7 falhem no Red pelo motivo certo. Outra saída é registrar e justificar que aquele teste é de regressão (checklist §0, item "Ordem das tarefas preserva o Red").

### Sugestões
- **S1** — `plan.md` §10, D11. `grep -P '\xEF\xBB\xBF'` no Git Bash com locale UTF-8 interpreta `\xEF` como o código U+00EF, não como byte. O comando pode não achar um BOM real e também não procura espaço sem quebra nem zero-width. O D11 precisa detectar de fato os bytes EF BB BF, E2 80 8B e C2 A0 nos arquivos novos, na mesma linha do item de caracteres invisíveis do `checklist-revisao` §4 (AP-007).
- **S2** — `plan.md` §2.1, `TutorReplyParseResult.IsValid`. Com `IsValid => Reply is not null`, o compilador não consegue provar `[MemberNotNullWhen(false, nameof(Rejection))]` e tende a emitir CS8775, que vira erro com `TreatWarningsAsErrors`. O risco já está na §7, mas vale decidir a forma no plano, antes da execução, para não cair no AP-009 durante o Green.
- **S3** — `plan.md` §2.2 (caminho JSON) e §2.1 (mensagem `invalid JSON`). A detecção só leva ao caminho JSON um texto que começa com `{`. Então a raiz é sempre objeto ou dá `JsonException`, e o ramo "the root is a <kind>" (o exemplo "the root is an array" da tabela) é código morto. Ou a detecção passa a aceitar `[`, ou o ramo e o exemplo saem.
- **S4** — `plan.md` §2.1, mensagem `reply must end with a question`. O corte do reply em 80 caracteres + `...` é comportamento visível (vai na instrução de correção ao modelo), mas nenhum teste o fixa. Um reply longo precisa aparecer encurtado na mensagem.
- **S5** — `plan.md` §2.3 × `ARCHITECTURE.md:241` e `.claude/skills/tdd-dotnet/SKILL.md:28,90`. Esses textos dizem que as fixtures do parser são "saídas reais gravadas". O plano usa exemplos sintéticos e justifica no §2.3, mas não lista esse desvio no §2.4. Registre a exceção (ou a ressalva "a partir da US-004") para o Revisor de código não acusar violação da §10 (AP-005).
- **S6** — `plan.md` §5, T2. O Green da T2 implementa as fábricas `Valid`/`Invalid` de `TutorReplyParseResult`, mas nenhum teste da T2 as usa. É código antes do teste. Elas podem ficar como esqueleto até o Green da T3, que é onde os testes as exercitam.
- **S7** — `plan.md` §2.4 × §5. O §2.4 diz que as alterações da `ARCHITECTURE.md` entram "antes da T2"; a pré-condição da §5 diz antes da T1. Escolha um ponto só.
- **S8** — `plan.md` §2.2. `TutorReplyParser` usa `[GeneratedRegex]` e por isso precisa ser `sealed partial class`. A assinatura mostrada não tem `partial`.
- **S9** — `plan.md` §4, `Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation`. "Rejection" não é nome de método. Algo como `Create_ForEachReason_…` ou o nome do catálogo segue melhor o padrão `Metodo_Cenario_Resultado`.
- **S10** — `plan.md` §2.2, caminho JSON. Elemento de `corrections` que não é objeto e campo de correção que não é string viram `""` e são descartados com aviso, mas não há teste no parser. É opcional, porque a meta de cobertura é só do Core, mas esse caminho de tolerância hoje não tem nenhuma verificação.

### Pontos positivos
- O conflito real entre §6.2 ("o parser valida") e R4 foi identificado e resolvido com um ADR curto, com alternativas e consequências.
- O catálogo único de motivos e mensagens no Core prepara a retentativa da RN-BRAIN04 (US-003) sem depender da Infrastructure.
- As mensagens de rejeição já têm texto-alvo no formato "o que está errado / Expected …" (D6 e `CLAUDE.md` hábito 5).
- As contagens da §4, do D2 e do D5 batem entre si e com a suíte atual (27 + 22 casos verdes, conferidos).
- O que fica fora de escopo e as regras não verificáveis (RN-FEED03/04/05, número de frases) estão explícitos e continuam no `prompts/tutor.md`.
- Os esqueletos antes do teste (AP-013), a faixa de EventId documentada e o `RecordingLogger` escrito à mão no lugar de um pacote extra têm justificativa.

### Aprendizados para o processo
- Perguntas em aberto que definem normalização ou comportamento de borda (P9, P5, P7) continuam saindo do planejador sem teste mapeado. É o mesmo padrão que o AP-004 registrou na US-001 (S1/S7 do `plan-review` daquela US) e que motivou o item [O] do checklist. Regra sugerida para `.claude/agents/planejador.md`: na §9, cada P* termina com "Teste: `<Classe>.<Método>`" ou "Sem efeito observável", e o Planejador confere isso antes de entregar.
- Green que adianta um ramo inteiro (aqui, "detecção" fazendo existir o caminho de tags) é a forma mais comum de um teste da tarefa seguinte nascer verde. Sugestão para a skill `tdd-dotnet`: na §5 do plano, quando uma tarefa cria um desvio (if/switch) cujo outro ramo pertence à tarefa seguinte, o plano diz o estado desse ramo (ex.: lança `NotImplementedException`) até lá.
- Comandos de verificação por bytes no Git Bash (`grep -P '\xNN'`) dependem do locale (AP-007). Sugestão para a skill `checklist-revisao` e `.claude/agents/executor.md`: padronizar uma única forma de checar BOM, zero-width e espaço sem quebra, e o planejador copiar essa forma na DoD em vez de escrever uma variante.
