# Revisão do plano — US-010 Portas de voz e áudio no Core, com configuração validada

Data: 2026-10-26
Escopo revisado: `tasks/US-010/plan.md` (branch `feature/US-010_portas-voz-audio-config`, sem código ainda), `specs/sprints/SPRINT_2.md` (US-010), RN-TTS01/02 e RN-STT01, `ARCHITECTURE.md` §3, §4, §7 e §10, `EnglishTutorApp.cs`, `LiveBrainTraitTests.cs`.
Resultado: REPROVADO (1 bloqueante)

## Verificações (checklist §0)
| Item | Resultado |
|---|---|
| Todo cenário BDD no mapa da §4, com nomes `Metodo_Cenario_Resultado` | ✅ 4/4 |
| Respostas P* com efeito observável têm teste e tarefa | ✅ (P1 a P5 mapeadas; P6 e P7 sem efeito observável, declarado) |
| DoD da §10 cobre a DoD da US (D1 a D7) | ✅ (D8 a D11 são extras do plano) |
| Conformidade com R1 a R4, RN-* e CLAUDE.md; ADR | ✅ (sem ADR novo, e o ADR-004 cobre o padrão de Options) |
| Escopo e fora de escopo; adiamentos | ✅ (nada adiado para outra US) |
| Consistência §3 × §4 × §5 × §6 | ✅ (nomes da §5 idênticos aos da §4) |
| Transformações com teste nos dois lados do limite (AP-016) | ✅ |
| Afirmações condicionais com teste (AP-047) | ✅ |
| Ordem das tarefas preserva o Red | ✅ (mutação documentada para os testes que nascem verdes) |
| LiveBrain (AP-051, AP-032, AP-052) | N/A (a US não cria nem altera testes `LiveBrain`) |
| Inventário nominal de arquivos conferido por comando da DoD (AP-050) | ❌ ver B1 |
| `ValidateOnStart` em mais de uma classe de Options (AP-039) | ✅ (`RunAsync_WhenTtsAndSttAreBothInvalid_ShowsBothFailures`) |
| Alteração na `ARCHITECTURE.md` e linhas repetidas em `CLAUDE.md` e skills | ✅ (§2.6 e §2.5) |
| Viabilidade técnica | ✅ (`BuildHost` aceita `ConfigureServices`; `ValidationFailures` trata o `AggregateException`) |

## Bloqueantes
- [ ] **B1** — `tasks/US-010/plan.md` §2.5 (tabela de arquivos) e §10, D8 — o inventário do filtro antigo `Category!=LiveBrain` está incompleto. Rodei o próprio comando do D8 e ele lista 13 ocorrências, não 11. As duas que faltam estão em `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs`, linhas 5 e 30, nos comentários do cabeçalho da classe e do `because:` do teste `LiveBrainTrait_OnlyOnClassesNamedLiveTests`. Do jeito que está, o D8 não pode dar "exatamente estas 11" antes, e depois da T6 ele continuaria listando essas duas linhas em vez de "nenhuma linha". Esperado: o plano trata esses dois trechos de uma destas formas.
  - Incluí-los no inventário nominal e na tarefa que os altera. Isso precisa de um commit com escopo adequado, já que o arquivo é código de teste.
  - Excluí-los explicitamente do D8, dizendo por quê (a mensagem do `because:` descreve o filtro da US-008) e reduzindo o esperado do D8 para 11 com o comando ajustado.
  Em qualquer das duas formas, o D8 tem de ficar coerente com o Grep (AP-050, AP-035). A conferência por Grep é do planejador, e o inventário da §2.5 não veio dele.

## Sugestões
- **S1** — `plan.md` §2.1, `AudioClip` — um `sealed record` com construtor validado deixa passar `clip with { SampleRate = 0 }`, que não passa pelo construtor. Vale uma decisão explícita no plano: declarar `SampleRate` e `Samples` como `init` privados, ou registrar como limite aceito, listando os consumidores (US-011, US-013 e US-014). A igualdade do record também compara a referência do `ReadOnlyMemory`, não o conteúdo, então os testes e os fakes não devem usar `Be(clip)` esperando comparação por valor.
- **S2** — `plan.md` §5 T3 e T4 — os testes do validador nascem com erro de compilação, porque os tipos ainda não existem. A T1 já prevê esqueleto para o Red falhar por comportamento. Faça o mesmo para `TtsOptionsValidator` e `SttOptionsValidator` (esqueleto que devolve sucesso), para o Red falhar pelo motivo certo (AP-010).
- **S3** — `plan.md` §7, última linha — a mitigação diz "os dois testes de cultura são `async`-free e ficam em classes separadas apenas por validador". Isso não impede interferência entre classes de teste em paralelo, porque `CurrentCulture` é por thread e o xUnit roda classes em paralelo, mas os testes são síncronos. A frase pode ser mais precisa: "testes síncronos; a cultura é por thread, restaurada em `finally`".
- **S4** — `plan.md` §2.5 — a regra `ARCHITECTURE.md` §10, linha 286, ainda abre com o filtro antigo (`excluídas por padrão: --filter "Category!=LiveBrain"`), seguido do novo na mesma linha. Isso é coerente, e o D8 o ignora porque a linha traz os dois filtros, mas vale uma nota dizendo que a linha foi vista e mantida de propósito.

## Pontos positivos
- Todo valor numérico exibido tem cultura invariável e teste em pt-BR, e a faixa tem teste nos dois lados do limite.
- O teste de duas seções inválidas (AP-039) e o de bind lendo a configuração evitam o falso verde do "padrão já vale".
- A lista de testes que nascem verdes está explícita, cada um com a mutação que prova o Red (AP-046).
- A mudança em `ARCHITECTURE.md` e a troca de filtro vêm em commits de documentação anteriores à T1, e o texto antigo e o novo estão citados.
- Fora de escopo e limites aceitos (`Voice:Enabled` não booleano) estão explícitos, com consumidores listados (AP-040).

## Aprendizados para o processo
- Inventário de troca de texto montado de memória ou de um Grep restrito a alguns diretórios (AP-050 de novo). Regra sugerida: o Planejador roda o comando do D8 sobre `git grep` na solução inteira, incluindo `tests/`, antes de fixar a contagem no plano, e cola a saída no plan.md.
