# Revisão do plano — US-009 Avaliação comparativa dos cérebros

Data: 2026-10-23
Escopo revisado: `tasks/US-009/plan.md` (§1 a §12), `specs/sprints/SPRINT_1.md` (seção US-009, linhas 516–545), `docs/adr/ADR-011-avaliacao-comparativa-logica-no-core-e-criterio-de-correcao.md`, `ARCHITECTURE.md`, `specs/PRODUCT.md`, `specs/BUSINESS_RULES.md` (RN-BRAIN04, RN-BRAIN07, RN-FEED01, RN-FEED02), `ConversationService`, `EnglishTutorApp`, `BrainProviders`, `AppLog`, `FakeTutorBrain`, `Directory.Packages.props`. Branch conferida com `git branch`: `feature/US-009_avaliacao-comparativa-cerebros`. Não há código de produção da US na branch.
Resultado: APROVADO (0 bloqueantes, 7 sugestões)

## Verificações (seção "Revisão de plano" do checklist)
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD no mapa da §4 (projeto, classe, método `Metodo_Cenario_Resultado`) | ✅ S1 a S5 mapeados (S3 a S5 são os cenários novos do commit de spec §2.8 (d)); ver S1 (sugestão) sobre ramos G8 |
| [O] Cada P* com efeito observável tem teste (§4) e tarefa (§5) | ✅ P1 a P12 mapeadas. P7 e P11 e P13 sem efeito observável, declaradas assim |
| [O] DoD da §10 cobre a DoD da US, com comandos e resultado esperado, inclui cobertura ≥ 80% | ✅ D5 a D7 da spec = D5 a D7 do plano; D8 a D10 espelham os acrescentados na spec; D3 pede o line-rate mesclado do Core |
| [O] R1 a R4, RN-* e regras rápidas; decisões com ADR Proposto | ✅ Lógica no Core (R4), BCL + `System.Text.Json` + abstrações de logging/options, Infrastructure inalterada (D12). ADR-011 existe e está versionado |
| [O] Escopo, fora de escopo, adiamentos (AP-022, AP-040) | ✅ Fora de escopo explícito; L1 a L6 listam consumidores e efeito; nenhum adiamento para outra US. Conferi os textos "antigo" da §2.8 contra `SPRINT_1.md`: batem. Linhas 73, 78–79 e 273 do `ARCHITECTURE.md` e a linha 58 e a última linha da §6.5 (234) existem como o plano cita |
| [O] Consistência §3 × §4 × §5 × §6, contagens, nomes (AP-025, AP-046) | ✅ Reconferi as somas: E12, M17, P33, X12, R12, B13, F1 = 100; C18, A11 = 29; total 129. Nomes da §5 iguais aos da §4. Pacote `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 já está em `Directory.Packages.props` e só o projeto Infrastructure.Tests o referencia hoje, como o plano diz |
| Transformações das tabelas da §2 com teste nos dois lados (AP-015, AP-016) | ✅ Percentual (B3), ms (B4, 1000,5 e 1000,33), p95 (X2 em n=1, 2, 20, 21; conferi `(95n+99)/100`), limites 0/1/2/3 (P8), G1 só confere trecho do projeto (P3) |
| Condicionais da §2 com teste (AP-047) | ⚠️ Quase todas ok (M5 com os três papéis, R6 × R7, R2 × R3). Duas sem teste: ver S2 |
| [O] Ordem das tarefas preserva o Red | ✅ Conferi caso a caso contra os esqueletos de T1 a T8: os testes que nascem verdes estão listados (T1, T3, T6, T7) com mutação prescrita e conferida (ex.: X4 (79,100,90,100), C10 `[]`) |
| Relaxamento de validação (AP-051) | N/A (a §4 declara que nada é relaxado) |
| Renomeação de cenário (AP-050) | N/A (nenhum cenário renomeado; o inventário por Grep está na §2.8) |
| Critério `LiveBrain` (AP-032, AP-052) | ✅ D6: camada = comando real, tolerância = limiares do PRODUCT, 3 rodadas, retentativa não exigida, dono da decisão = usuário (§7, T9). Métricas só registradas entram como risco |
| `ValidateOnStart` em várias Options (AP-039) | N/A (sem Options novo) |
| Justificativas que citam outras US (AP-036) | ⚠️ Uma afirmação quantitativa mal atribuída: ver S3 |
| Verificação de "nenhuma linha" na DoD (AP-035) | ⚠️ D7 (`git grep -F '"reviewedBy": ""'`): falta demonstrar o lado positivo e fixar a grafia do literal. Ver S4 |
| Alteração na `ARCHITECTURE.md` lista `CLAUDE.md` e skills | ✅ Grep citado na §2.8: nenhuma linha repete a regra |
| Riscos e ambiguidades que travariam o Executor | ✅ Tratados na §7. Ver S5 sobre T9/D6 |
| Viabilidade técnica | ✅ `ConversationService(brain, promptSource, options, logger)` aceita o contador de chamadas em volta do cérebro. A retentativa e `RawFallback` batem com o plano. `AppLog` vai até 1005, então 1006/1007 estão livres. `AddCommandLine` ignora `eval` e valores soltos (já previsto no risco da §7 e provado no D5) |

## Itens da rodada anterior
N/A (primeira rodada).

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tasks/US-009/plan.md` §2.2 (G8) × §4 (`GoldenSetParserTests`). O plano diz "um caso de teste por ramo", mas `original` ausente ou em branco (G8, `found: missing`/`blank`) não tem caso. P5 cobre `id`, P7 cobre `text`, P11 cobre `accepted`. P13 cobre só os ramos de tipo de `original` (`null`, array). Esperado: um caso para cada um dos dois ramos, ou a §2.2 deixa de afirmar cobertura por ramo. Atualizar também o parágrafo "Placeholders" e os totais (AP-049).
- **S2** — §2.3/§2.2 × §4 (AP-047). Duas afirmações condicionais sem teste:
  - `ContainsWords` com fragmento sem palavras devolve falso. E4 só testa `Tokens`, e M1 a M8 não têm fragmento vazio. Esperado: um caso em `CorrectionMatcherTests` ou `EvalTextTests`. Como o esqueleto devolve `false`, ele nasce verde e precisa de mutação em T1.
  - "Nomes de propriedade em camelCase e sensíveis a maiúsculas; propriedade desconhecida é ignorada". Nenhum teste em P1 a P13 (por exemplo `Sentences` com maiúscula vira "missing"). Ou entra teste, ou a frase sai do plano.
  - Em M8, o texto "as duas → 2" não diz que uma única `Correction` da resposta satisfaz as duas esperadas (§2.3). Explicitar o caso.
- **S3** — §7, linha "Oscilação do D6". "A fumaça da US-006 achou 4/5 (80%) no Claude Code" não procede. `tasks/US-006/plan.md` registra 4/5 no **Ollama** (D5, 3 rodadas). Para o Claude Code, `tasks/US-004/plan.md` só fixa o **mínimo** de 4/5 (risco "Fumaça abaixo de 4/5"). Esperado: corrigir a atribuição. O risco continua válido (sem margem, mas em outro provedor), e o D6 é só do ClaudeCode. Relevante também: no D5 da US-006 o Ollama teve respostas fora do formato em 2 de 5 execuções antes do reforço do prompt. Esse dado ajuda o usuário a interpretar a taxa "sem retentativa" do Ollama.
- **S4** — §10 D7 e §2.8 (e) (AP-035). O `git grep -F '"reviewedBy": ""'` só acha o arquivo se T8 gravar exatamente essa grafia (com espaço após os dois-pontos). Esperado: T8 fixa a grafia do literal no Green, e o Executor cola na §11 a linha achada pelo mesmo comando (código 0) antes de o usuário preencher `reviewedBy`. Assim a verificação "nenhuma linha" tem o outro lado demonstrado.
- **S5** — §5 T9 × §7 × §10 D6. T9 diz "roda o D5 uma vez e o D6 em 3 execuções seguidas (as mesmas do D5)", e a §7 diz "o D5 conta como a 1ª". A redação sugere que o D5 roda uma vez ou três. Além disso, o texto do D6 no plano admite `eval --providers ClaudeCode`, mas a spec (§2.8 (e)) diz "3 execuções seguidas do D5". Esperado: dizer em uma frase quantas vezes o comando completo (com Ollama) roda e se as rodadas 2 e 3 podem ser só do ClaudeCode, e alinhar o texto do plano com o da spec.
- **S6** — §5 T1 Red. "M8 casos 1 e 2" falham, mas os casos de M8 são (nenhuma → 0; só a segunda → 1; as duas → 2). Os que falham são o 2º e o 3º, e o 1º é o que nasce verde (a lista de verdes e a mutação já tratam o "nenhuma → 0"). Esperado: corrigir a numeração, para o Executor não confundir o motivo do Red.
- **S7** — §3 × `ARCHITECTURE.md` (§2.8 item 2). `EvalCommandLineParseResult` aparece na T6 (e `EvalCommandLine` com `IReadOnlyList<string> Providers`), mas não está em nenhuma linha da §3 nem na árvore proposta para o Console. Esperado: dizer em qual arquivo ele mora (por exemplo, junto de `EvalCommandLineParser.cs`) e refletir na árvore.

## Pontos positivos
- Lógica de avaliação no Core, testada com fakes e `FakeTimeProvider`, sem Infrastructure alterada (D12 guarda), e ADR-011 Proposto antes da implementação.
- Critério de "correção encontrada" explícito e bem delimitado (só `Corrected`, palavras lado a lado, palavra inteira). Os limites L1 e L2 listam consumidores e efeito (AP-040).
- Tarefas com Red/verde-no-nascimento/mutação prescritos teste a teste. Conferi por amostragem e bate.
- Trechos "antigo → novo" da spec e da `ARCHITECTURE.md` todos localizáveis no texto atual.
- Tratamento do Windows PowerShell 5.1 (dois formatos de `--providers`, parser e D11) e do `AddCommandLine`.
- Golden set de rascunho conferido contra G9 e G12: reconferi as 32 linhas, todas com `original` no texto e nenhuma forma aceita contígua no texto.
- Decisão sobre falha do D6 devolvida ao usuário, sem o Executor mexer em golden set, critério ou limiar (AP-032).

## Aprendizados para o processo
- Dado quantitativo de outra US citado sem a fonte certa (S3, AP-036): ao citar medição anterior, nomear o plano/linha e o provedor medido. Reforça a regra existente de conferir a afirmação no registro de origem antes de usá-la como justificativa de risco.
- A frase "um caso de teste por ramo" (S1, S2) foi autoafirmada na §2.2 sem tabela de ramo × teste. Sugestão de regra para o Planejador: quando o plano declara cobertura por ramo, incluir a coluna "ramo → teste" na própria tabela de mensagens (G1 a G12), para o Revisor conferir sem reconstruir.

---

## Rodada 2

# Revisão do plano — US-009 Avaliação comparativa dos cérebros (Rodada 2)

Data: 2026-10-23
Escopo revisado: `C:\Crescimento\TutorIA\tasks\US-009\plan.md` (§1 a §12, inclusive as respostas do Planejador da rodada 2) e `tasks/US-009/plan-review.md` (rodada 1). Conferi também `tasks/US-006/plan.md` (linhas 276 e 891) para a correção do S3. Não reli a spec, o ADR-011 nem o `ARCHITECTURE.md` desta vez, só os trechos "antigo → novo" que o plano cita (conferidos na rodada 1). Branch: `feature/US-009_avaliacao-comparativa-cerebros`, árvore limpa. Não há código de produção da US.
Resultado: APROVADO (0 bloqueantes, 0 sugestões)

## Verificações (seção "Revisão de plano" do checklist)
| Item | Resultado |
|---|---|
| [O] Cenários S1 a S5 no mapa da §4, com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ Sem mudança desde a rodada 1. A tabela G1–G12 ganhou a coluna "Teste", e cada ramo tem caso. G8 `missing`/`blank` está em P14. G8 de tipo (`null`, array) está em P13. |
| [O] P* com efeito observável têm teste (§4) e tarefa (§5) | ✅ P1 a P15 na T2 (37 casos). |
| [O] DoD (§10) cobre a da US, com comandos e resultado esperado | ✅ D1 a D14. D3 pede o line-rate mesclado do Core (≥ 80%). D5 a D7 bate com o texto novo da spec (§2.8 (e)). |
| [O] R1–R4, `RN-*`, regras rápidas e ADR Proposto | ✅ Sem mudança. |
| [O] Escopo, fora de escopo e adiamentos (AP-022, AP-040) | ✅ L1 a L6 listam consumidores e efeito. Não há adiamento para outra US. |
| [O] Consistência §3 × §4 × §5 × §6 (AP-025, AP-046, AP-049) | ✅ Refiz as somas pelas tabelas: E 3+2+3+3+1+3 = 15; M 3+2+2+1+3+2+1+4 = 18; P 1+3+1+4+2+1+2+4+1+3+4+1+6+2+2 = 37; X 12; R 12; B 13; F 1; Core = 108. C 18 e A 11 somam 29 no Console. Total 137. O mesmo número aparece na T2 (37), na T5 (13), no D2 (137, 108 e 29) e no parágrafo de Totais. Não achei "129", "100 casos" nem "33 casos" fora do histórico do §12. |
| Transformações com teste nos dois lados (AP-015, AP-016) | ✅ Percentual (B2, B3), ms (B4), p95 (X2), limites 0/1/2/3 (P8), G1 só confere trecho do projeto (P3). |
| Condicionais da §2 (AP-047) | ✅ Os dois buracos da rodada 1 estão fechados. E6 cobre `ContainsWords` com fragmento sem palavras. P15 cobre nomes sensíveis a maiúsculas e propriedade desconhecida, um caso para cada metade. M8 caso 4 declara que uma única `Correction` satisfaz as duas esperadas. |
| [O] Ordem das tarefas preserva o Red | ✅ T1: E6 e M8 caso 1 nascem verdes, e cada um tem mutação prescrita. Os casos que falham (M8 2, 3 e 4) conferem com o esqueleto. T2: P14 e P15 falham no esqueleto, porque ele devolve resultado válido com 0 frases. T6: 16 dos 18 casos falham (C1–C9 = 14, mais 2 verdadeiros de C10). |
| AP-051, AP-050, AP-039 | N/A, como na rodada 1. |
| Critério `LiveBrain` (AP-032, AP-052) | ✅ D6 diz camada, tolerância, 3 rodadas do comando completo, retentativa não exigida e dono da decisão (o usuário). |
| Justificativas que citam outra US (AP-036) | ✅ Corrigido (ver S3 abaixo). |
| "Nenhuma linha" na DoD (AP-035) | ✅ D7 tem os dois lados. A T8 fixa a grafia `  "reviewedBy": "",`, e o Executor cola na §11 a linha que o `git grep` acha (código 0) antes de o usuário preencher. |
| Alteração na `ARCHITECTURE.md` lista `CLAUDE.md` e skills | ✅ Sem mudança. |
| Riscos e ambiguidades | ✅ A T9 diz que o comando completo do D5 roda 3 vezes seguidas, que essas rodadas são o D5 e o D6 ao mesmo tempo, e que nenhuma é só do ClaudeCode. §7, §9 P7, §10 e a spec (§2.8 (e)) dizem o mesmo. |
| Viabilidade técnica | ✅ Sem mudança. |

## Itens da rodada anterior
O usuário mandou aplicar as sugestões S1 a S7. Todas estão resolvidas no plano. Elas não têm hash, porque a revisão do plano ainda não foi commitada. O commit de revisão do plano (`828213c`) é anterior a esta rodada e a rodada 2 ainda não foi commitada.
- **S1 (G8 `original` ausente ou em branco sem caso)** — resolvido. P14 novo (2 casos). A §4 "Placeholders" cita P14 em `{missing | blank | kind}`. Os totais foram atualizados.
- **S2 (condicionais sem teste)** — resolvido. E6 cobre `ContainsWords` com fragmento sem palavras (3 casos, nasce verde, com mutação). P15 cobre maiúsculas e propriedade desconhecida (2 casos). M8 explicita o caso de uma única `Correction`. A lista de condicionais da §4 foi atualizada.
- **S3 (atribuição do 4/5 na §7)** — resolvido. O 4/5 medido é do Ollama (US-006); o Claude Code só tem o mínimo de 4/5 da US-004. Conferi `tasks/US-006/plan.md` (linhas 276 e 891): "frases fora do formato por execução: 2, 0, 1, 0 e 1 de 5". O plano reproduz esse dado do registro de origem e corrige a minha leitura da rodada 1 ("2 de 5 execuções"). O dado do plano é o que está certo.
- **S4 (grafia do literal do D7)** — resolvido, nas formas descritas na linha D7 acima.
- **S5 (D6 em 3 execuções)** — resolvido, nas formas descritas na linha "Riscos e ambiguidades" acima. O consumo da assinatura foi recalculado para 96 a 192 chamadas.
- **S6 (numeração de M8 no Red da T1)** — resolvido. Falham os casos 2, 3 e 4; o caso 1 nasce verde, com a mutação que devolve `expected.Count`.
- **S7 (`EvalCommandLineParseResult` sem arquivo na §3)** — resolvido. O tipo mora em `EvalCommandLineParser.cs`. A §3 tem linhas separadas para `EvalCommandLine.cs` e `EvalCommandLineParser.cs`, o item 2 da árvore do `ARCHITECTURE.md` (§2.8) o cita, e a T6 manda criar primeiro o `EvalCommandLine.cs`.

## Bloqueantes
Nenhum.

## Sugestões
Nenhuma nova.

## Pontos positivos
- O Planejador conferiu a sugestão S3 contra o registro de origem e corrigiu o detalhe que a revisão tinha errado (AP-036).
- As contagens foram conferidas nas tabelas e batem em todos os pontos do plano (AP-049).
- Cada teste que nasce verde tem mutação prescrita, e o Red de cada tarefa confere com o esqueleto.
- D7 e T8/T9 fecham o lado positivo da verificação de "nenhuma linha".

## Aprendizados para o processo
- A regra sugerida na rodada 1 ("coluna ramo → teste" na tabela de mensagens) foi seguida: a coluna "Teste" da G1–G12 deixou a conferência direta. Vale registrar como prática do Planejador para tabelas de mensagens com ramos.
- O Revisor também errou na rodada 1, ao resumir o dado da US-006 sem reler a origem. Ao citar medição de outra US, o relatório da revisão deve copiar o texto da fonte, como o Planejador fez agora (AP-036).
