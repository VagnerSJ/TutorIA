# Revisão — US-008 Suíte de contrato do ITutorBrain

Data: 2026-10-23
Commits revisados: 94bf924..36c0cd7 (os 10 commits `test(brain)` e `docs(ai)` do código; os de plano e spec começam em bfd902d)
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain) | ✅ 487 passaram (Core 155, Infrastructure 261, Console 71) |
| Cobertura de linhas do Core | 100% (mesclado, reportgenerator); solução 90,5% |
| Cenários BDD com teste | 2/2 (mesma suíte: K1–K15, G1, G2; live opcionais: G3–G5, L1, L2) |
| Teste antes do código (ordem de commits) | ✅ A US só adiciona testes e `src/` não mudou. Os commits são de teste, e o plano prevê o Red por mutação. |
| Evidências da DoD conferem | ✅ |
| Intenção antes do código (spec/ADR → código) | ✅ Spec (0d0193d) e ARCHITECTURE (b860fcd) foram commitados antes dos testes. Não há ADR novo, e o plano justifica. |

Detalhes que reproduzi:
- **D5 e D6:** 46 testes de contrato passam, 23 por provedor. A classe base tem 16 métodos de teste, com os mesmos nomes da §4 do plano.
- **D7:** `--list-tests | grep -c ContractLiveTests` dá 0 com `Category!=LiveBrain` e 2 com `Category=LiveBrain`.
- **D11:** a busca por BOM, zero-width space e espaço sem quebra dá 0.
- **D12:** nenhum arquivo de `src` alterado.
- **D13:** `git status` está limpo.
- **D4:** `dotnet format --verify-no-changes` sai com código 0.
- Não achei `Skip` nem `Thread.Sleep` nos testes de contrato.
- Todas as tarefas do plano estão marcadas `[x]`.

Evidência do Executor, não reproduzida:
- **D8:** os testes live (3 rodadas com `claude` autenticado e Ollama) dependem do ambiente. Só conferi que L1 e L2 existem e carregam o trait `LiveBrain`.
- **Mutações do Red (T1–T4):** as mutações que provam o Red dos testes não foram reproduzidas.

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tasks/US-008/plan.md` §11 — As evidências são resumos de uma linha por comando. Isso basta, mas não registra a saída das mutações do Red nem a contagem por classe do D9.

## Pontos positivos
- A suíte é abstrata, com bancadas por provedor (`ClaudeCodeContractHarness`, `OllamaContractHarness`). A base concentra todos os testes.
- G1 e G2 impedem que um provedor novo fique sem subclasse, e que as subclasses declarem testes próprios.
- G3, G4 e G5 garantem que o trait `LiveBrain` fica só nas classes `*LiveTests`.
- A mudança de técnica do K10 (o chamador cancela e o timeout também já venceu) está registrada na §12 do plano antes do commit que a implementa.
- Zero mudanças em `src/`, e a documentação (ARCHITECTURE, SPRINT_1, skill `tdd-dotnet`) foi atualizada de forma consistente.

## Aprendizados para o processo
- A mutação do K10 prescrita no plano não falhava no Claude Code. Quando o plano prescreve uma mutação para provar o Red, o Planejador deveria checar se a mutação realmente falha no código existente. O Executor registrou o desvio (AP-010), então não há ação além de manter essa conferência.
