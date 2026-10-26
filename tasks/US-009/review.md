# Revisão — US-009 Avaliação comparativa dos cérebros

Data: 2026-10-23
Commits revisados: 58b00d6..26f4217 (`git log develop..HEAD`: 33 commits)
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain) | ✅ 624 passaram (Core 263, Infrastructure 261, Console 100), cada projeto em menos de 1 s |
| Cobertura de linhas do Core | 98,3% (total do relatório mesclado: 91,7%) |
| Cenários BDD com teste | 5/5 (conferidos pelos arquivos de teste do diff, sem rodar cada um: `EvalCommandTests`, `EvalCommandLineParserTests`, `GoldenSetParserTests`, `EvalReportBuilderTests`, `EvalRunnerTests`, `GoldenSetFileTests`) |
| Teste antes do código (ordem de commits) | ✅ Cada `test(eval)` vem antes do `feat(eval)` correspondente |
| Evidências da DoD conferem | ✅ D1 a D4 e D13 reproduzidos e iguais (624 testes, 98,3%, format sem diff, contagem de caracteres invisíveis 0); D5 a D12 são evidência do Executor, não reproduzida |
| Intenção antes do código (spec/ADR → código) | ✅ ADR-011, `SPRINT_1.md` e `ARCHITECTURE.md` foram commitados (`58b00d6`, `b31db78`, `8774762`) antes do primeiro `feat`; as escolhas de forma do Executor (§12) vêm em `docs(ai)` antes de cada `feat` |

## Itens não reproduzidos (evidência do Executor)
- D5 e D6: três rodadas completas com os dois provedores. O ClaudeCode ficou `met` nas três (96,9%, 93,8% e 100% de correções; 100% de formato sem retentativa). A coerência com o código está ok.
- D7: `GoldenSetFileTests` passa, e o `git grep` de `"reviewedBy": ""` acha a linha 2 (código 0). A revisão do usuário continua pendente, então o D6 vale como provisório.
- D8 a D11: dependem de `dotnet run`. Os cenários correspondentes têm teste automatizado no `EvalCommandTests`.

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `GoldenSetParser` (`src/EnglishTutor.Core/Evaluation/GoldenSetParser.cs`) — Os ramos "elemento de `sentences` ou `expected` que não é objeto" e "item de `accepted` não textual" ficam sem teste (93,4% de cobertura na classe). A §11 e a §12 do plano já registram isso. Esperado: um teste por ramo, ou a mensagem fica confirmada como comportamento aceito na US de destino.
- **S2** — Resultado do Ollama no D5 — O Ollama ficou em 56 a 63% das correções, abaixo dos 80% do PRODUCT. Isso não bloqueia esta US, porque a spec só exige a nota mínima para o ClaudeCode. A US que ajustar o modelo ou o `accepted` precisa registrar a decisão na spec antes, como a §11 já alerta.
- **S3** — `eval/golden-set.json` — `reviewedBy` continua vazio. Antes do merge em `develop`, o usuário precisa revisar as 32 frases e preencher o campo. Até lá, o D6 e o D7 ficam provisórios.

## Pontos positivos
- Regra de negócio (métricas, nota mínima, correspondência de correções) no Core, sem pacote externo. A pasta `Infrastructure` não foi tocada (D12).
- O fluxo SDD foi seguido: revisão do plano, aprovação, depois pares test e feat por etapa.
- `EvalRunner` só trata `BrainException`, e o `OperationCanceledException` propaga. O `catch` de I/O em `EvalCommand.SaveReportAsync` é estreito (`IOException` e `UnauthorizedAccessException`), mostra a mensagem ao usuário e passa o `ct`.
- O relógio é injetado (`TimeProvider`), sem `Sleep` nos testes.
- Nenhum teste foi apagado ou ignorado.
- Evidências detalhadas, com hash e data, e desvios do plano explicados na §11.

## Aprendizados para o processo
- Nenhum erro novo nesta US. Ponto menor: o `grep "Passed!"` do fluxo de revisão não casa com a saída do `dotnet test` em pt-BR ("Aprovado!"). Vale uma nota na skill de revisão para usar um padrão que aceite os dois idiomas.
