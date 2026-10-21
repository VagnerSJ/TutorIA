# Revisão do plano — US-006 Professor local via Ollama

Data: 2026-10-21
Escopo revisado: `tasks/US-006/plan.md` (commit 90e4741), `docs/adr/ADR-009-...md` (Proposto), `specs/sprints/SPRINT_1.md` (US-006, US-007), `specs/BUSINESS_RULES.md` (RN-BRAIN07/08/09) e `ARCHITECTURE.md` (§3, §6.1, §6.4, §7). Também li `appsettings.json`, `EnglishTutorApp.cs` e os testes existentes, só para checar contradição com o plano.
Resultado: REPROVADO (1 bloqueante)

Conferi no código e rodando `dotnet test --filter "Category!=LiveBrain"`:
- A linha de base do plano bate: Core 134, Infrastructure 101, Console 51, tudo verde.
- Os padrões do D12 dão 5 linhas no `appsettings.json` atual mais o bloco novo.
- Os textos antigos que o D15c e a §2.9 citam existem em `ARCHITECTURE.md`: linha 62 (§3), linha 176 (§6.1) e linha 218 (§6.4).
- `PromptFiles.FormatJsonPath`, `PromptFiles.FormatTagsPath` e `BrainOutputModes.TryGetCanonical` existem.
- A faixa de EventId 2000–2999 está livre para o 2002 (usados: 2000 e 2001).

## Verificações (checklist §0)
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD no mapa da §4 com projeto, classe e método no padrão | ✅ 5 cenários da spec mais o cenário novo "tempo esgotado" |
| [O] Resposta P* com efeito observável tem teste (§4) e tarefa (§5) | ❌ P9 (veja B1) |
| [O] DoD da §10 cobre a DoD da spec (D5, D6, cobertura do Core ≥ 80%) | ✅ D5, D6, D3/D3b e D4 presentes. D7 e D8–D10 vêm do plano |
| [O] Conformidade com R1–R4, RN-*, CLAUDE.md e ADR Proposto | ✅ ADR-009 Proposto e pacotes só na Infrastructure. O desvio da RN-BRAIN07/08 (`Unknown`) está na P2 e no ADR para o usuário decidir |
| [O] Escopo e adiamentos com texto exato (AP-022, AP-040) | ✅ Seletor provisório → D7 da US-007 com texto exato e prova D16. Limites aceitos (P2, P12) listam consumidores |
| [O] Consistência interna §3 × §4 × §5 × §6, contagens (AP-025, AP-046) | ✅ Conferi: Infrastructure +61 (1+24+25+7+4), Console 51→55, total 351, validador 24 casos/12 métodos, `OllamaTutorBrainTests` 25 casos/18 métodos, D11 = 64. Nomes da §5 iguais aos da §4 |
| Transformações das mensagens com teste dos dois lados (AP-016, AP-015) | ⚠️ veja B1 e S2 |
| Condicionais da §2 com teste (AP-047) | ⚠️ veja S3 |
| [O] Ordem das tarefas preserva o Red | ✅ Os testes que nascem verdes estão declarados, com mutação depois do Green ou no Red (T1, T3, T4, T5, T6, T8) |
| LiveBrain: camada, tolerância, rodadas, risco (AP-032) | ✅ Adaptador sem retentativa, tolerância zero, 3 rodadas, risco de oscilação e quem decide registrados |
| `ValidateOnStart` em várias classes e normalização no consumidor (AP-039) | ✅ Caso com duas seções inválidas; `OutputMode` com caixa diferente no consumidor; `Provider` `Ollama` e `ollama` |
| Justificativas que citam outras US (AP-036) | ✅ O cenário "fallback por limite de uso" tem Claude Code como Provider e Ollama como FallbackProvider, conforme `SPRINT_1.md`. A RN-BRAIN08 não lista `Unknown`, como o plano afirma |
| "Nenhuma linha" na DoD com o outro lado demonstrado (AP-035) | ✅ D15c sobre `864b98d` e D16 |
| Alteração na `ARCHITECTURE.md` lista repetições em CLAUDE.md e skills | ✅ §2.9 varreu CLAUDE.md, README, skills e specs; nada a alterar |
| Riscos e ambiguidades do Executor | ✅ Tratados na §7 e nas paradas explícitas (tipo da exceção do 404, `format`, cerca markdown) |
| Viabilidade no SDK e nas bibliotecas | ✅ `CancellationTokenSource(TimeSpan, TimeProvider)` e `HttpRequestError` existem no .NET 10. A versão do `OllamaSharp` fica a confirmar na T2, com parada prevista |

## Bloqueantes
- [ ] **B1** — `plan.md` §2.5 (`<detalhe>`) e P9 × §4 (`SendAsync_WhenServerCallFails_ClassifiesByStatusAndText`, casos 5 e `SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage`) × T4. A P9 define o texto exibido ao aluno na falha genérica: `<detalhe>` é a primeira linha não vazia da mensagem da exceção, sem espaços nas pontas, ou `no error details were provided` quando não há. Os únicos casos testados (`boom t5`, `boom t7`) têm mensagem de uma linha, não vazia e sem espaços nas pontas. Nenhum teste prova nem a escolha da primeira linha não vazia (mensagem com várias linhas, ou começando por linha vazia), nem o texto de reserva (mensagem vazia ou só espaços). Qualquer implementação que embuta a mensagem inteira, ou que nunca use o texto de reserva, passa em toda a suíte. Esperado: cada parte da transformação da P9 tem teste mapeado na §4 e tarefa na §5 (T4, com Red e mutação conforme o caso). A mensagem exibida não pode embutir linhas extras de stack ou de corpo HTTP, nem ficar com parênteses vazios (checklist §0 [O] "P* com efeito observável"; AP-016).

## Sugestões
- **S1** — `plan.md` cabeçalho, linha `Branch:`. O plano diz `feature/US-006_ollama-brain`, mas a branch real é `feature/US-006_ollama-tutor-brain`. Alinhar o texto do plano com a branch (ou o contrário) evita confusão no merge `--no-ff` e no `docs/us/US-006.md`.
- **S2** — `plan.md` §2.1 (mensagem de `Temperature`) e §4 (`Validate_WhenTemperatureIsOutOfRange_FailsWithRangeAndFoundValue`). O plano exige o valor "em cultura invariante", mas nenhum caso descreve a mensagem sob uma cultura com vírgula decimal (ex.: `pt-BR`). Um `ToString()` sem cultura passaria na máquina de CI em `en-US` e quebraria na do usuário. Esperado: a asserção do teste fixa o separador decimal exibido (`found -0.1`) mesmo com a cultura da thread em `pt-BR`.
- **S3** — `plan.md` §2.3, linha a (`mensagem contém model e not found`). O plano testa o status 404 sozinho (caso 3), o texto completo sozinho (caso 4) e a precedência (caso 6). Não há caso com só uma das duas palavras, por exemplo `not found t8` sem `model`, sem status. Esse caso deveria cair na linha b ou c. Sem ele, trocar o "e" por "ou" no código passa na suíte. Esperado: um caso em que só uma das palavras casa e a classificação não vai para a linha a (AP-047).
- **S4** — `plan.md` §2.3 e T4. O catch do adaptador cobre `HttpRequestException` e o tipo próprio de exceção do `OllamaSharp`, ainda desconhecido. A cobertura dessa segunda exceção depende só do teste com a resposta gravada (`SendAsync_WithRecordedModelNotFoundResponse_...`). Se a T3 mostrar que o 404 sai com esse tipo próprio, vale um caso de unidade com o tipo real na `[Theory]` da §4, para o ramo não depender só da fixture. O Executor pode decidir na T3 e registrar na §12.

## Pontos positivos
- Boa disciplina de prova: testes de fio com o `OllamaApiClient` real e `HttpMessageHandler` falso (`/api/chat`, `format`, `options.temperature`), fixtures gravadas pelo próprio adaptador (AP-048) e recorder que registra a cadeia de exceções para decidir o que o `catch` captura.
- Tratamento rigoroso do tempo máximo: os dois cancelamentos (do chamador e do tempo) com ordem determinística pelo `onCancelled` do fake, `WaitAsync(10 s)` para o Red não travar a suíte, e um relógio por chamada provado com 900 + 999 ms.
- Planejamento completo do Red: testes que nascem verdes listados um a um, com mutações pelo efeito. Controle D15c contra `864b98d` e D16 como prova do comando que a US-007 vai usar.
- Intenção antes do código: spec da US-006 e da US-007, `ARCHITECTURE.md` e ADR-009 num commit separado antes da T1; desvio da RN-BRAIN07/08 exposto como pergunta (P2) com consumidores e efeito.
- LiveBrain com camada, tolerância, rodadas e risco de oscilação declarados.

## Aprendizados para o processo
- Transformação de texto exibida ao aluno (primeira linha, texto de reserva, formatação de número) tende a entrar no plano só pelo caminho feliz. Os casos de uma linha e de mensagem presente passam sem provar a regra. Já aparece nos AP-015 e AP-016. Sugestão para a skill de planejamento: para cada `<placeholder>` derivado de texto de biblioteca ou de valor numérico, listar na §4 um caso por ramo da derivação (normal, múltiplas linhas, vazio) e, para números, a cultura.
- Condicionais com conjunção ("A e B") precisam de caso em que só um dos termos casa (AP-047). O plano já cobre a precedência e cada fonte isolada, mas esqueceu o termo isolado da conjunção textual.

---

## Rodada 2

# Revisão do plano — US-006 Professor local via Ollama (rodada 2)

Data: 2026-10-21
Escopo revisado: `tasks/US-006/plan.md` no commit aebee1a. Conferência do texto e da aritmética das contagens, sem build nem testes (só o `plan.md` mudou).
Resultado: APROVADO (0 bloqueantes)

## Itens da rodada anterior
| Item | Status |
|---|---|
| **B1** (derivação do `<detalhe>`, P9) | Resolvido em aebee1a. §4 ganhou `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback`, `[Theory]` de 5 casos comparando a mensagem inteira. Cada ramo tem caso próprio (várias linhas; linha inicial vazia ou em branco; espaços e `\r` nas pontas; mensagem vazia; só espaços e quebras). T4 lista o teste no Red e uma mutação por ramo depois do Green. §2.5, P9, D2 e D11 atualizados. |
| S1 (nome da branch) | Sem efeito. A branch atual é `feature/US-006_ollama-brain`, igual ao cabeçalho do plano. O `plan-review.md` da rodada 1 citava outro nome (`ollama-tutor-brain`), que não confere com `git branch`. |
| S2 (cultura invariante em `Temperature`) | Pendente, não pedida. |
| S3 (só uma das palavras `model`/`not found`) | Pendente, não pedida. |
| S4 (tipo próprio de exceção do OllamaSharp) | Pendente, não pedida. |

## Contagens (conferidas na §4)
- Validador: 12 métodos, 24 casos. `OllamaTutorBrainTests`: 19 métodos, 30 casos. `OllamaTutorBrainHttpTests`: 7/7. DI: +4 casos, +3 métodos. `OllamaOptionsTests`: 1.
- Infrastructure.Tests: 1+24+30+7+4 = 66 → 101+66 = 167. Console.Tests: 55. Total 134+55+167 = 356 (D2 bate).
- D11: 1+24+30+7 = 62, mais 4 de console e 3 de DI = 69. Bate.
- Nomes do teste novo idênticos na §4, na T4 e na P9 (AP-025).

## Verificações (checklist §0)
Todos os itens ✅. Ressalvas: transformações (AP-016/015) só com a observação de cultura (S2 pendente); condicionais da §2 (AP-047) só com S3 pendente.

## Bloqueantes
Nenhum.

## Sugestões
- **S2, S3, S4** da rodada 1 continuam pendentes e não bloqueiam. A S3 ainda deixa um caso sem prova: com só `not found` (sem `model`) e sem status, trocar o "e" por "ou" na linha a passaria na suíte. Fechar exige 1 caso na `[Theory]` de classificação e atualizar as contagens (AP-031).

## Pontos positivos
- Correção do B1 cirúrgica e completa: caso por ramo, mensagem inteira comparada, mutação por ramo, contagens recalculadas e conferidas.
- O Red de cada teste novo está declarado; o `<detalhe>` não nasce verde.

## Aprendizados para o processo
- Confirma o AP-016: `<placeholder>` derivado de texto de biblioteca precisa de um caso por ramo da derivação (e a cultura, para números).
- O Revisor deve conferir nomes de branch com `git branch` antes de citá-los.
