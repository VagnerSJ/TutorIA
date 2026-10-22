# Revisão — US-007 Troca de cérebro por configuração e fallback

Data: 2026-10-22
Commits revisados: 0badc52..3d9baf5 (20 commits em `develop..HEAD`, branch `feature/US-007_troca-cerebro-fallback`)
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain) | ✅ 435 passaram (Core 155, Infrastructure 209, Console 71), 0 falhas, menos de 1 s por projeto |
| Cobertura de linhas do Core | 100% (relatório mesclado com `reportgenerator`; total da solução 90,5%; Infrastructure 86,6%; Console 95,4%) |
| Cenários BDD com teste | 6/6 (os 3 exemplos do Esquema ficam em F3 e C3). Conferi por Grep que os métodos C1–C6, B5, F5, F14, F15, DI3, P2, P3 e C12 existem nos testes. |
| Teste antes do código (ordem de commits) | ✅ pares `test` e `feat` em todas as tarefas: `aba35d3`→`06424f1`, `bd42857`→`3a7645a`, `014d1ee`→`d08b7c6`, `e138b09`→`e673750`, `7cd48a1`→`a712b33` |
| Evidências da DoD conferem | ✅ D1–D4, D12–D14 e D15 batem com o que obtive: build limpo, 435 testes, format com código de saída 0, contagem de caracteres invisíveis 0, árvore limpa. D6, D8 e D9 são evidência do Executor, não reproduzida; conferi só a coerência com o código. |
| Intenção antes do código (spec/ADR → código) | ⚠️ ADR-010, `ARCHITECTURE.md` e `SPRINT_1.md` foram commitados antes do código. As escolhas de forma do §12 vieram depois dos `feat` (ver S1). |

## Itens da rodada anterior
N/A (primeira revisão de código).

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tasks/US-007/plan.md` §12, commit `6594ede`. As escolhas de forma foram registradas depois dos `feat`:
  - O texto do log 2003 (`d08b7c6`).
  - A mensagem da fábrica (`e673750`).
  - A mensagem de `ConsoleText.BrainSwitched` (`a712b33`).
  - O aviso com o prefixo `> ` do prompt.

  O checklist (§1, AP-014 e AP-027) pede o §12 em commit anterior ao `feat` quando a escolha muda texto exibido ou mensagem de exceção. O Executor reconheceu o desvio no próprio §12 e nenhuma escolha muda comportamento da spec, então não bloqueio. Esperado nas próximas US: registrar a escolha de forma antes do `feat`.
- **S2** — `tasks/US-007/plan.md` §11, D3. A evidência cita 0,9822 de linha do Core no relatório só do `Core.Tests`. Meu relatório mesclado dá 100% para o assembly Core. Ambos passam de 80%. Para as próximas US, convém padronizar a evidência como "relatório mesclado com `reportgenerator`".
- **S3** — mutações de V6, C4, C5 e C1 não aplicadas (§12). A justificativa é plausível, pois B2, P2 e o cabeçalho cobrem esses casos. Sem ação.

## Pontos positivos
- `FallbackTutorBrain` é pequeno e sem estado. O filtro `when (!IsInvalidOutput && ShouldFallBack)` deixa `OperationCanceledException` e exceções que não são `BrainException` propagarem, e o `ct` é repassado ao notificador e ao reserva.
- A política mora no Core (`BrainFallbackPolicy`, R4) e o aviso passa pela porta `IBrainSwitchNotifier` implementada no console (R2). O Core continua sem pacotes externos.
- O teste P3, que conta os valores de `BrainErrorKind`, obriga quem criar um tipo novo de falha a decidir se ele aciona o reserva.
- Os testes de componente rodam os cérebros reais com fakes de processo e de cliente de chat. O D7 e o D12 foram usados para provar que as buscas dão zero depois da mudança.
- Nenhum teste foi ignorado ou removido. O teste renomeado (DI1) foi justificado no plano.

## Aprendizados para o processo
- Padrão já visto (AP-014/AP-027): a escolha de forma é registrada no §12 depois do `feat`. Regra sugerida: o Executor grava um commit `docs(ai)` com o texto de logs, mensagens de exceção e avisos antes do `feat` que os implementa, e o hook ou a checklist do Executor passa a lembrar disso quando uma tarefa cria texto novo.
- Cobertura do Core: padronizar nas evidências o número do relatório mesclado, e não o de um único projeto de teste.
