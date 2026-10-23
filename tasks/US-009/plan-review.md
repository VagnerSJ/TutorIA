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
