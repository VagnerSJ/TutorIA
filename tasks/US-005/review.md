# Revisão — US-005 Erros do Claude Code com mensagens claras

## Rodada 1

Data: 2026-10-21
Commits revisados: 4470b74..c2a83fa (branch `feature/US-005_erros-claude-code` contra `develop`; 28 commits, dos quais 17 de código e testes e o restante de documentos e plano)
Resultado: APROVADO (0 bloqueantes)

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros (`dotnet build`) |
| Testes (exceto LiveBrain) | ✅ 286 passaram (Core 134, Infrastructure 101, Console 51), 0 falhas, 0 ignorados, cerca de 1 s por projeto |
| Cobertura de linhas do Core | 100% (solução: 89,2%; reportgenerator com saída em `$TEMP`) |
| `dotnet format --verify-no-changes` | ✅ exit 0 |
| Caracteres invisíveis no diff (D15) | ✅ contagem 0 |
| Cenários BDD com teste | 5/5 (CLI não encontrada, não autenticado, limite de uso, tempo esgotado, falha do cérebro no console não encerra a sessão), mais os testes de apoio do mapa da §4 |
| Teste antes do código (ordem de commits) | ✅ cada `feat` tem o `test` correspondente antes: 187a7dd→f4b785f, c545c9f→0d782f9, 17ef943→f973492, 6269814→d048663, 615804c→9d678a7; o `refactor` 25ca6c7 tira o `ManualTimeProvider`; 66e37c6 traz os testes `LiveBrain` do runner |
| Evidências da DoD conferem | ✅ D1, D2, D3b, D4 e D15 reproduzidos, com os mesmos números das Evidências (286 casos; Core 100%; 89,2%; format limpo; 0). Os demais itens não foram reproduzidos; veja a nota abaixo |
| Intenção antes do código (spec/ADR → código) | ✅ ADR-008 (Aceito), D5 da spec, `ARCHITECTURE.md` §3 e §6.3 e o ponteiro no ADR-007 estão em `e763689` e `64265b4`, antes do primeiro commit de código (`187a7dd`) |

Não reproduzidos, por dependerem de `dotnet run`, de CLI real, de rodadas `LiveBrain` ou de PowerShell 5.1: D6, D10, D11, D12, D13 e a gravação das fixtures (T4a). São "evidência do Executor, não reproduzida". Conferi só a coerência com o código:
- A mensagem de `CliNotFound` em `ClaudeCodeFailures.StartFailed` é a do D6.
- A mensagem de `Timeout` é a do D11.
- A fixture `not-authenticated` tem `result` "Not logged in · Please run /login" e `is_error: true`, o que bate com o D12 e casa com os padrões `not logged in` e `/login`.
- A fixture `unknown-model` cai na linha 1 e nenhum padrão da lista casa, como diz a T4a.

### Itens da rodada anterior
Não se aplica (primeira revisão de código). Os bloqueantes e as sugestões das três rodadas de revisão do plano estão em `tasks/US-005/plan-review.md` e foram respondidos na §12 do plano.

### Bloqueantes
Nenhum.

### Sugestões
- [ ] **S1** — `tasks/US-005/plan.md` §12 (entrada do Executor, 2026-10-21) e `Directory.Packages.props`. A versão do pacote `Microsoft.Extensions.TimeProvider.Testing` (10.11.0) foi fixada no commit `c545c9f` e só registrada na §12 depois, no commit de evidências. É um desvio pequeno: não muda texto exibido nem nome público, então não é bloqueante. Esperado: escolhas de versão não previstas no plano entram na §12 antes ou no mesmo commit da mudança (padrão de AP-014/AP-027). O desvio conhecido foi conferido: o pacote está só no grupo de testes, o Core não ganhou referência nova e o build fica limpo.
- [ ] **S2** — `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeFailures.cs`, símbolos `_notAuthenticatedPatterns` e `_usageLimitPatterns`, `Timeout`, `NotAuthenticated`, `UsageLimitReached`. Os campos privados e os métodos públicos estão misturados (campos no meio da classe, `Matches` entre métodos públicos). Reordenar (constantes e campos no topo, métodos públicos, depois os privados) deixaria a leitura mais fácil. É só estilo, sem efeito de comportamento.
- [ ] **S3** — `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOutput.cs`, método `ClassifiedOrDefault`. O ramo `_` cria `BrainException(Unknown, ...)` sem `inner`, o que está correto porque não há exceção de origem. Se a US-007 precisar de diagnóstico de `Unknown`, vale lembrar que o texto bruto só está na mensagem. Nenhuma ação agora.

### Pontos positivos
- Classificação isolada em `ClaudeCodeFailures`, com a regra "duas fontes, nas duas direções" (`Classify`), ordem a antes de b e comparação `OrdinalIgnoreCase`. Bate com a §2.4 do plano e com o ADR-008.
- O filtro `when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)` preserva a propagação do cancelamento do chamador (`padroes-codigo`, "Erros"). Tem teste próprio e mutação registrada nas Evidências.
- `Win32Exception` é traduzida só no adaptador e o Core não mudou (R1, R4). Nenhuma mensagem embute o texto do sistema operacional (AP-015).
- O relógio é por chamada ao processo (`CancellationTokenSource(TimeSpan, TimeProvider)` ligado ao token do chamador) e foi testado com `FakeTimeProvider`. O arquivo temporário do system prompt é apagado no `finally`.
- O console captura só `BrainException`, escreve na saída de erro, registra o evento 1005 com template estruturado e sem texto do aluno, e deixa `OperationCanceledException` e demais exceções propagarem. O `catch` não engole nada.
- Os testes unitários não usam `Thread.Sleep` nem processo real. Os que usam `Task.Delay` e processo real (`ProcessRunnerLiveTests`) estão em `Category=LiveBrain`.
- As fixtures de erro têm origem rastreada no `README.md` (gravadas e sintéticas, com as diferenças em relação à chamada do adaptador). Não há dado pessoal nelas.
- `appsettings.json` ganhou `TimeoutSeconds: 60`. O validador tem teste nos dois lados do limite e a mensagem diz o que está errado e o que se espera.
- Os comentários de adiamento "até a US-005" foram removidos (D16 e D16c do plano).

### Aprendizados para o processo
- Escolha de versão de pacote que o plano deixa em aberto ("última estável 10.x") costuma só ser registrada depois do commit → sugerir que o Planejador fixe a versão no plano (ou diga "o Executor registra na §12 no mesmo commit"). Candidato a regra na skill `tdd-dotnet` ou no `plan.md` modelo. Vale só se o padrão se repetir.
- Nenhum erro recorrente de `docs/aprendizados.md` apareceu nesta US (BOM, AP-019 e AP-034 foram cobertos pelo D15, pelo ambiente limpo e pelas fixtures).
