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

## Rodada 2

Data: 2026-10-17
Escopo revisado: `tasks/US-002/plan.md` na revisão do commit 3b5a8ac (`docs(ai): revise plan for US-002`), branch `feature/US-002_contrato-parser-resposta`. O commit mexe só no `plan.md` (+34/−17). Ainda não há código na branch: o `git diff develop...HEAD` tem só o ADR-005, o `plan-review.md` e o `plan.md`. Conferi também a US-002 em `specs/sprints/SPRINT_1.md`, `prompts/format.tags.md` (layout `- Pronunciation:` / `- Vocabulary:` / `- Praise:`, base do novo teste da P9) e a busca por caracteres invisíveis no diff da branch (resultado 0).
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD aparece no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ Os 6 cenários continuam com teste no parser (Infrastructure.Tests) e no Core |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P4 (T3), P5 (T6 e T7), P7 (T6) e P9 (T7) agora têm teste nomeado. P2, P3, P6 e P8 já tinham teste na rodada 1. P1 não tem efeito observável (é decisão de ADR) |
| [O] A DoD da §10 cobre a DoD da US, com comandos verificáveis e Core ≥ 80% | ✅ D2 (93 casos) e D5 (18 métodos / 24 casos) atualizados. A ressalva sobre o D11 continua na S1 |
| [O] Conformidade com R1–R4, RN-* citadas e regras rápidas do `CLAUDE.md`; ADR Proposto | ✅ Sem mudança desde a rodada 1. O `throw` temporário da T6 fica só na Infrastructure e some na T7 |
| [O] Escopo: nada além nem aquém; fora de escopo explícito | ✅ Os testes da P5 fixam a rejeição atual e não criam extração de JSON. A §8 continua coerente (ver S12 sobre o texto) |
| [O] Consistência interna §3 × §4 × §5 × §6 com contagens que batem | ✅ Recontei método a método. Core.Tests: validador com 8 métodos (6 `[Fact]`, mais `[Theory]` de 3 e de 2 casos = 11 casos), formatador com 4 métodos / 5 casos, rejeições com 1 método / 4 casos, total 13 métodos / 20 casos (27 + 20 = 47). Infrastructure.Tests: 18 métodos, 14 `[Fact]` mais `[Theory]` de 2, 3, 3 e 2 casos = 24 casos. T3 + T4 = 5 + 3 = 8 métodos e T6 + T7 = 10 + 8 = 18 métodos. Continuam 7 fixtures, e os casos de borda são strings inline (§2.3) |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ A T6 deixa `ParseTags` lançando `NotImplementedException`, então os 8 testes da T7 falham no Red. `JsonHasLeadingWhitespace` só fica verde com o `Trim()` na detecção. O teste da P4 entra na T3, que é onde nasceria vermelho, e na T4 vira regressão declarada. `OptionalFieldsAreBlank` e `CorrectionLacksOriginalOrCorrected` continuam vermelhos depois do Green da T3 (as regras 3 a 5 ainda não existem) |
| Alteração na `ARCHITECTURE.md` lista as linhas do `CLAUDE.md` e das skills que repetem a regra | ✅ (o desvio das "fixtures reais" segue na S5) |
| Riscos e ambiguidades que travariam o Executor | ⚠️ Continuam pendentes a S2 (`MemberNotNullWhen`) e a S3 (ramo de raiz que não é objeto). Há uma pequena lacuna nova na entrada de `KeepsRawTextAndIsNotRawFallback` (S11) |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ `JsonDocument.Parse` lança `JsonException` quando há texto depois do valor e aceita espaço em branco antes dele. Isso confirma o comportamento fixado na P5 e na P7 |

### Itens da rodada anterior
- [x] **B1** — resolvido em 3b5a8ac. Cada efeito agora tem teste nomeado na §4 e tarefa na §5:
  - P9: `Parse_WhenTagsRepeatLabeledLine_JoinsValuesWithSemicolon` (T7). Também fixa a comparação de rótulo sem diferenciar maiúsculas.
  - P5: `Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson` (T6) e `Parse_WhenJsonIsInFenceOrAfterText_RejectsWithMissingReply` (T7). Os dois conferem `Reason` e a presença de `Expected` na mensagem.
  - P7: `Parse_WhenJsonHasLeadingWhitespace_DetectsJson` (T6), com os prefixos `"  "` e `"\r\n\t"`, e `RawText` preservado.
  - P4: `Validate_WhenNoCorrectionsAndNoPraise_IsValidWithoutWarnings` (T3). O candidato de `ReturnsValidReplyWithoutWarnings` também ficou explícito.
  - As contagens da §4, do D2 e do D5 foram atualizadas e batem.
- [x] **B2** — resolvido em 3b5a8ac. A §5 da T6 diz o estado do ramo de tags (`ParseTags` só lança `NotImplementedException`, sem montar candidato nem chamar o validador). A T7 diz que os 8 testes falham no Red por essa exceção, inclusive `TextHasNoJsonNorTags` e `JsonIsInFenceOrAfterText`, e que o Executor confere o motivo da falha antes do Green.
- **S1–S10** — pendentes, dependem de decisão do usuário. Nenhuma foi aplicada nem precisava ser nesta rodada. Elas continuam valendo como estão na Rodada 1. A S1 (o D11 não detecta de fato os bytes EF BB BF / E2 80 8B / C2 A0) segue sendo a mais relevante para a T8.

### Bloqueantes
Nenhum.

### Sugestões
- **S11** — `plan.md` §4, linha de `Parse_WhenValid_KeepsRawTextAndIsNotRawFallback`, e §5 T6. O teste está na T6, mas o plano não diz que a entrada é JSON. Se for uma resposta com tags, ele não fica verde no Green da T6 (o ramo de tags lança até a T7). O efeito esperado é que o teste da T6 só dependa do caminho JSON. A preservação do texto cru com tags pode ficar para a T7, se for desejada.
- **S12** — `plan.md` §8, item "JSON dentro de cerca markdown ou com texto em volta (P5)". Agora existe teste que fixa a rejeição desses casos, e o item, lido isolado, sugere que eles não têm comportamento definido. O que fica fora de escopo é *extrair* o JSON. A rejeição com motivo e `Expected` está no escopo e tem teste (P5).
- **S13** — `plan.md` §9, P2 e P3. O planejador adotou o padrão "cada P* termina com `Teste: ...`" para P4, P5, P7 e P9 (aprendizado da rodada 1), mas P2 e P3 não o seguem, embora tenham teste na §4 (`Parse_WhenJsonHasNoCorrectionsAndPraise_FeedbackContainsPraise` / `Format_WhenNoCorrectionsAndPraise_ContainsPraise` e `Validate_WhenCorrectionLacksOriginalOrCorrected_DropsItAndWarns`). Fechar o padrão em todas as P* deixa a conferência mecânica.

### Pontos positivos
- A resposta da §12 trata cada bloqueante, aponta as seções alteradas e explica por que a P5 virou dois métodos (cada caso passa por um ramo diferente).
- O teste da P4 foi posto na tarefa onde ele nasce vermelho (T3), com a justificativa escrita, e não onde "seria natural" (T4). É a aplicação direta do item de Red do checklist.
- O estado do ramo de tags ao fim da T6 virou uma instrução verificável, e o teste da P7 ficou com um Red que depende do `Trim()`. É o motivo certo de falha (AP-010).
- As contagens foram refeitas de forma consistente nos quatro lugares (§4, §5, D2, D5) e batem com a minha recontagem.

### Aprendizados para o processo
- O padrão "P* termina com `Teste: ...` ou `Sem efeito observável`", sugerido na rodada 1, resolveu o B1 assim que foi aplicado. Mas o planejador só o aplicou às P* citadas no bloqueante (P2 e P3 ficaram de fora, S13). Isso reforça a regra sugerida para `.claude/agents/planejador.md`: valer para todas as P* desde a primeira versão do plano, e não só como resposta à revisão.
- O B2 foi resolvido com "o outro ramo lança `NotImplementedException` até a tarefa seguinte". Isso confirma a sugestão da rodada 1 para a skill `tdd-dotnet`: quando uma tarefa cria um desvio cujo outro ramo pertence à tarefa seguinte, o plano diz o estado desse ramo. A S11 mostra o complemento: os testes da tarefa atual precisam declarar a entrada que os mantém no ramo já implementado.

## Rodada 3

Data: 2026-10-17
Escopo revisado: `tasks/US-002/plan.md` na revisão do commit e702c74 (`docs(ai): revise plan for US-002`), branch `feature/US-002_contrato-parser-resposta`. O commit mexe só no `plan.md` (+81/−24). Ainda não há código na branch: o `git diff develop...HEAD` tem só o ADR-005, o `plan-review.md` e o `plan.md`. Também conferi `ARCHITECTURE.md:241`, `.claude/skills/tdd-dotnet/SKILL.md:28,90`, uma busca por `saídas reais|gravad` em `CLAUDE.md`, `ARCHITECTURE.md`, `.claude/`, `docs/` e `specs/`, o AP-007 em `docs/aprendizados.md` e o item de caracteres invisíveis do `checklist-revisao` §4. Rodei a D11a no diff atual da branch e o resultado foi `0`.
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD aparece no mapa da §4 com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ Os 6 cenários continuam com teste no parser (Infrastructure.Tests) e no Core. O mapa não mudou, só a descrição da entrada de `KeepsRawTextAndIsNotRawFallback` (S11) |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ Nada mudou desde a rodada 2. A S3 tirou um ramo que não tinha teste e não criou efeito novo sem teste |
| [O] A DoD da §10 cobre a DoD da US, com comandos verificáveis e Core ≥ 80% | ✅ D1 agora também confere a ausência de CS8775. O D11 passou a dois comandos que de fato detectam os bytes (ver S1 abaixo) |
| [O] Conformidade com R1–R4, RN-* citadas e regras rápidas do `CLAUDE.md`; ADR Proposto | ✅ Nada mudou. O desvio das "fixtures reais" agora está declarado e tem texto proposto para a `ARCHITECTURE.md` e a `tdd-dotnet` (S5) |
| [O] Escopo: nada além nem aquém; fora de escopo explícito | ✅ A §8 separa extrair o JSON (fora) de rejeitar o caso (dentro, com teste) (S12) |
| [O] Consistência interna §3 × §4 × §5 × §6 com contagens que batem | ✅ Recontei. Core.Tests tem 13 métodos / 20 casos: validador 8/11, formatador 4/5, rejeições 1/4. Infrastructure.Tests tem 18 métodos / 24 casos: 14 `[Fact]` e `[Theory]` de 2, 3, 3 e 2 casos. T3+T4 = 5+3 e T6+T7 = 10+8. D2 = 93 (47+22+24) e D5 = 18/24. A §3 tem a linha nova da `tdd-dotnet` e as notas `partial` e "`IsValid` armazenada". Não sobrou nenhuma menção a "antes da T2" nem ao ramo "the root is …" |
| [O] Ordem das tarefas preserva o Red de cada teste | ✅ Os 10 testes da T6 têm entrada que começa com `{` depois do `Trim()`, inclusive `KeepsRawTextAndIsNotRawFallback`, que agora lê `json-valid.json`. Nenhum depende do ramo de tags, que lança até a T7. Na T6, `JsonHasNoCorrectionsAndPraise` usa o `FeedbackFormatter` já pronto (T5), mas falha no Red porque o `Parse` ainda é esqueleto. A T2 só compila o esqueleto de `TutorReplyParseResult`, sem antecipar teste da T3 |
| Alteração na `ARCHITECTURE.md` lista as linhas do `CLAUDE.md` e das skills que repetem a regra | ✅ A busca confirma que a regra das "saídas reais gravadas" só aparece em `ARCHITECTURE.md:241` e em `tdd-dotnet/SKILL.md:28,90`. As menções em `SPRINT_1.md:249-250` (US-004) e no ADR-001 batem com a ressalva "a partir da US-004" |
| Riscos e ambiguidades que travariam o Executor | ✅ A S2 e a S3 foram resolvidas. O caso de CS8775 aparecer mesmo assim tem caminho definido: parar e registrar no §12 (AP-009) |
| Viabilidade técnica no SDK e nas bibliotecas | ✅ Ver a análise abaixo (CS8775 e D11 no Git Bash) |

Análise dos dois pontos técnicos pedidos:

- **CS8775 na forma escolhida.** O CS8775 é emitido quando o compilador analisa o corpo de um getter ou método que tem `MemberNotNullWhen`. Uma auto-propriedade só com `get` tem acessor gerado pelo compilador, sem corpo do usuário para analisar, então a forma da §2.1 não deve gerar o aviso. O construtor privado só atribui um `TutorReply?` e um `TutorReplyRejection?` a propriedades anuláveis e `Warnings` a uma propriedade não anulável, o que também não gera aviso de nulidade. Como a classe é `sealed record`, o compilador gera um construtor de cópia privado, usado pelo `with`. Ele copia os campos de apoio como estão, e sem `init` não há como mudar uma propriedade sozinha. A coerência entre `IsValid`, `Reply` e `Rejection` fica garantida pelas duas fábricas. O build do esqueleto na T2 é o lugar certo para confirmar. Não reproduzi esse build porque não posso criar arquivos.
- **D11 no Git Bash do Windows.** `$'\xef\xbb\xbf'` (ANSI-C quoting) vira bytes crus no Bash, e o `grep` do Git Bash é um binário MSYS que recebe os argumentos sem conversão. `LC_ALL=C` antes do `grep` na pipeline é sintaxe válida. `--include`/`--exclude-dir` depois dos caminhos funcionam no GNU grep. O `-c` conta linhas e imprime `0` com código de saída 1, como o plano avisa. Rodei a D11a no diff atual e o resultado foi `0`.

### Itens da rodada anterior
- [x] **S1** — resolvido em e702c74. O D11 virou D11a (mesma forma do `checklist-revisao` §4, com `LC_ALL=C`, esperado `0`) e D11b (busca recursiva em `src`/`tests`, esperado não listar nada). O `grep -P '\xEF…'` saiu, e a linha do BOM na §7 foi atualizada. A forma é viável no Git Bash (ver a análise acima).
- [x] **S2** — resolvido em e702c74. A §2.1 traz o código e o parágrafo "Forma de `TutorReplyParseResult`": auto-propriedade armazenada atribuída no construtor privado, fábricas como único caminho de criação, `ArgumentNullException` para argumento nulo e `TryGet…` descartado com o motivo. A forma também aparece nas alternativas descartadas, na §3, na §5 T2, na §7 e no D1, e há caminho definido se o aviso aparecer (AP-009).
- [x] **S3** — resolvido em e702c74. O ramo "raiz que não é objeto" saiu da §2.2, com a justificativa e o destino de um texto que começa com `[` (caminho de tags → `missing reply`). A mensagem de `invalid JSON` na tabela da §2.1 não tem mais o exemplo "the root is an array", e "Detectar `[` como JSON" entrou nas alternativas descartadas.
- [x] **S5** — resolvido em e702c74. A §2.3 declara o desvio e a §2.4 traz o texto novo para `ARCHITECTURE.md:241` e `tdd-dotnet/SKILL.md:28,90`, com os números de linha conferidos. A busca de repetições foi registrada, e a §3, a pré-condição da §5, a §7 e a DoD comum estão coerentes.
- [x] **S7** — resolvido em e702c74. Agora há um ponto único de aplicação, antes da T1, num commit `docs(ai)` do orquestrador, e ele é o mesmo no título da §2.4, na pré-condição da §5, na §3 e na DoD comum. Não sobrou "antes da T2".
- [x] **S8** — resolvido em e702c74. `public sealed partial class TutorReplyParser`, com exemplo de `[GeneratedRegex]` `static partial` na própria classe e uma nota explicando. Coerente na §3 e na §5 (T6 e T7).
- [x] **S11** — resolvido em e702c74. A entrada é o conteúdo de `json-valid.json`, com `RawText` igual byte a byte e `IsRawFallback == false`. A T6 diz que as 10 entradas começam com `{` depois do `Trim()`. O plano diz por que não entrou um teste de texto cru com tags (opcional e mudaria as contagens).
- [x] **S12** — resolvido em e702c74. A §8 separa **extrair** (fora de escopo) de **rejeitar** (no escopo, com os dois testes citados).
- **S4, S6, S9, S10, S13** — recusadas pelo usuário. Não foram reabertas.

### Bloqueantes
Nenhum.

### Sugestões
- **S14** — `plan.md` §10, texto logo abaixo do bloco do D11. O plano avisa que o `grep -c` da D11a sai com código 1 quando conta 0, mas não diz o mesmo da D11b: o `grep -rl` também sai com código 1 quando não acha nada, e esse é justamente o resultado esperado. Ao colar a evidência, o Executor não deve ler esse código 1 como falha. Basta estender a frase final para os dois comandos.

### Pontos positivos
- Cada sugestão aplicada tem na §12 uma entrada que aponta todas as seções tocadas. Isso deixou a conferência mecânica, e não encontrei nenhuma seção esquecida.
- A decisão sobre o CS8775 vem com o porquê técnico, a alternativa descartada e o que fazer se a premissa falhar no build da T2. É a aplicação direta do AP-009.
- O D11 reaproveita a forma do `checklist-revisao` §4 em vez de inventar uma variante, e explica por que o `grep -P '\xNN'` falhava. Isso atende ao aprendizado da rodada 1.
- A S3 foi resolvida removendo o ramo, não criando teste para código morto, e o destino da entrada que começa com `[` ficou declarado.
- As contagens (§4, §5, D2, D5) continuam corretas mesmo com uma revisão grande do texto.

### Aprendizados para o processo
- O D11 agora usa `LC_ALL=C grep …`, e o `checklist-revisao` §4 usa `grep …` sem `LC_ALL=C`. Hoje os dois dão o mesmo resultado, porque os três padrões são sequências UTF-8 válidas, mas são duas formas do mesmo comando, que é o que o aprendizado da rodada 1 queria evitar. Sugestão: adotar uma forma só (com `LC_ALL=C`, que não depende do locale) no `checklist-revisao` §4, no AP-007 e em `.claude/agents/executor.md`, e o Planejador copiar dali.
- Quando o usuário pede para aplicar um conjunto de sugestões, a resposta da §12 no formato "sugestão → seções tocadas" permitiu conferir tudo numa rodada. Vale tornar esse formato padrão em `.claude/agents/planejador.md` para respostas a revisões de plano.

Arquivos relevantes: `C:\Crescimento\TutorIA\tasks\US-002\plan.md`, `C:\Crescimento\TutorIA\tasks\US-002\plan-review.md`, `C:\Crescimento\TutorIA\ARCHITECTURE.md` (linha 241), `C:\Crescimento\TutorIA\.claude\skills\tdd-dotnet\SKILL.md` (linhas 28 e 90), `C:\Crescimento\TutorIA\.claude\skills\checklist-revisao\SKILL.md`.
