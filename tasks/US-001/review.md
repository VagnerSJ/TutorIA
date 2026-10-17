# Revisão — US-001 Fundação da solução e configuração

## Rodada 1

Data: 2026-10-17
Commits revisados: b8b6471..055853c (36 commits, `git log develop..HEAD`)
Resultado: APROVADO (com pendências: D13 manual, do usuário, e a primeira execução do CI depois do push)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ `dotnet build`: 0 Aviso(s), 0 Erro(s) |
| Testes (exceto LiveBrain) | ✅ 46 passaram (Core.Tests 27/27, 64 ms; Console.Tests 19/19, 316 ms) |
| Cobertura de linhas do Core | 100% somando os dois projetos (Core.Tests sozinho dá 89,5%: faltam só `LocalCommands`/`LocalCommandInfo`, que o Console.Tests cobre; Console.Tests sozinho dá 96,5%: faltam só `BrainOptionsValidator.cs:15-16`, que o Core.Tests cobre) |
| Cenários BDD com teste | 8/8 (7 com teste automatizado; o cenário de CI, que é opcional, é conferido por inspeção no D10, como o plano prevê) |
| Teste antes do código (ordem de commits) | ✅ T3–T11 e P12 em pares `test(...)` → `feat/fix(...)`. Os commits de teste só trazem esqueletos que lançam `NotImplementedException` (conferi com `git show` em 2c180cc, 0a480d8, 2f1da88, 95138d9 e 624307b). Os testes de guarda da T2 têm o Red demonstrado e registrado no §11. |
| Evidências da DoD conferem | ✅ D1, D2 e D3/D3b conferem com o que obtive. D4–D7, D9 e D11 não foram refeitos por mim, porque `dotnet format`, `dotnet run` e `reportgenerator` estão fora dos comandos que posso usar. O registro está coerente com o código. D13 ⏳ pendente do usuário. |
| Intenção antes do código (spec/ADR → código) | ✅ ADR-004 em b8b6471 (aceito em f903b78). ARCHITECTURE em 798050a e CLAUDE.md em fa2f0a6, antes do código. Decisão P12 em ab6d9dc, antes de 388afee e e71b316. |

Checklist (resumo): R1–R4 ✅ (o Core só tem `Microsoft.Extensions.Options` e `DependencyInjection.Abstractions`, sem `ProjectReference`). `CancellationToken` repassado em toda a cadeia, sem `.Result` nem `.Wait()` ✅. `OperationCanceledException` com filtro `when` nos tokens da aplicação, os demais propagam ✅. Nenhum `catch` engole exceção ✅. Logs com `[LoggerMessage]` e templates ✅. `.gitignore` cobre `logs/`, `TestResults/` e `coverage/` ✅. Nenhum teste com `Skip` ✅. Suíte unitária abaixo de 1 s, sem rede e sem processo externo ✅. Commits convencionais com escopo e branch `feature/US-001_fundacao-solucao` ✅. Os 33 métodos do mapa §4 existem com os nomes previstos (16 + 17) ✅.

## Bloqueantes
Nenhum.

## Pendências (não bloqueiam esta revisão; o usuário decidiu fazê-las antes do merge)
- **D13** — Ctrl+C real no executável, em terminal interativo. Confirmar: `$LASTEXITCODE` = 0, nenhum stack trace e última linha do log = `ApplicationStopped` com `ExitCode` 0. Depois disso, repetir o D12 e marcar a T13. Hoje o cancelamento é coberto só pelo teste automatizado `RunAsync_WhenCancelledWhileWaitingForInput_ReturnsZero`, que cancela o `ct` do chamador. O caminho `ConsoleLifetime` → `ApplicationStopping` só o D13 exercita.
- **CI** — a primeira execução no GitHub depende do push do usuário. Conferir no log o SDK instalado e se o passo de cobertura publica o resumo.
- Fluxo restante (cabe ao Documentador): `docs/us/US-001.md`, status da US no `specs/sprints/SPRINT_1.md` (ainda "Planejamento") e merge em `develop`.

## Sugestões
- **S1** — `src/EnglishTutor.Console/Ui/ChatSession.cs:8` — `private const char _byteOrderMark = '\uFEFF';` (escrito no código com o caractere U+FEFF literal) é invisível no editor e no diff. O mesmo acontece em `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs:110,122,127`. Esperado: usar o escape `'\uFEFF'` / `"\uFEFF/quit"`, para que o leitor e o revisor vejam o que está sendo testado. **Resolvido** em 6d68c9e (código e testes) e 9c35276 (documentos).
- **S2** — `src/EnglishTutor.Console/EnglishTutorApp.cs:25` — `BuildHost(options)` roda fora do `try`. Se o `appsettings.json` tiver JSON inválido ou a seção `Serilog` estiver mal configurada, a exceção escapa de `RunAsync` com stack trace e código de saída do CLR, e não com `ExitCodes.UnexpectedError` (1) e a mensagem curta prevista na P8. Esperado: tratar a falha de montagem do host, pelo menos com a mensagem curta em `Error` e o código 1 (nesse ponto ainda não há logger). **Resolvido** em 4303f24 (teste) + c173601.
- **S3** — `src/EnglishTutor.Console/EnglishTutorApp.cs:47` — `WriteInvalidConfigurationAsync(options.Error, exception, ct)` usa o `ct` do chamador. Se ele já estiver cancelado, a escrita lança `OperationCanceledException` de dentro do `catch` e a mensagem de configuração inválida se perde. O caminho de erro inesperado (linha 58) já usa `CancellationToken.None`. Esperado: fazer igual aqui. **Resolvido** em 4303f24 (teste) + 54778b9.
- **S4** — `src/EnglishTutor.Console/EnglishTutorApp.cs:30,71` — quando um `OperationCanceledException` que não veio dos tokens da aplicação propaga, o `finally` registra `ApplicationStopped` com `ExitCode` 1 (o valor inicial), mas o processo não sai com 1. Isso engana quem lê o log. Esperado: registrar o código real ou indicar que a saída foi anormal. **Resolvido** em 4303f24 (teste) + db60928 (evento `ApplicationAborted` 1004).
- **S5** — `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs:120` — o teste de guarda `RunAsync_WhenLaterLineStartsWithByteOrderMark_ForwardsItUnchanged` fixa que `"\uFEFF/quit"`, numa linha que não é a primeira, vai para o handler. A partir da US-003/004 o handler leva ao cérebro. É o comportamento aprovado na P12, mas vale revisitar quando o handler real entrar, por causa da RN-CONV06.
- **S6** — `tasks/US-001/plan.md:143` — o §3 ainda diz que `RepositoryRoot.cs` está "em andamento, não commitado", mas ele foi commitado em 1ca6aed. Atualizar o texto.
- **S7** — o `NSubstitute` é referenciado nos dois projetos de teste sem ser usado. O plano já admite isso; só registro para a poda, caso continue sem uso nas próximas US.

## Pontos positivos
- TDD disciplinado e visível no histórico: cada Red tem só esqueletos que lançam `NotImplementedException`, e o Green vem no commit seguinte. A P12 seguiu o mesmo rito: decisão no plano, depois teste, depois correção, depois evidência.
- R4 bem aplicada: `LocalCommandParser`, `BrainProviders.TryGetCanonical` e `BrainOptionsValidator` estão no Core. O console só reage. A normalização de `Brain:Provider` tem um único dono.
- Os testes de arquitetura comparam com o shared framework em execução, não com um prefixo de nome, e têm mensagens que citam a R1. O Red de demonstração dos três casos foi registrado.
- Os testes do console rodam em processo com `TextReader`/`TextWriter` falsos, `PendingTextReader` e `ThrowingTextWriter`, sem gancho de teste no código de produção. Cada teste de log usa uma pasta temporária própria e faz parse do CLEF.
- Mensagens de erro dizem o que está errado e o que se espera (`Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.`).
- O Executor usou a DoD manual para achar um defeito real (BOM do PowerShell 5.1 escondendo o `/quit`) e o tratou pelo fluxo, sem correção solta.
- As evidências separam a rodada anterior da rodada pós-P12 e justificam quais itens não precisaram ser refeitos.

## Aprendizados para o processo
- **Defeito de plataforma descoberto só na DoD manual (P12).** Entrada redirecionada no Windows PowerShell 5.1 chega com BOM UTF-8. Proposta: registrar em `docs/aprendizados.md` e incluir na skill `padroes-codigo` (seção de entrada/console) a regra "normalizar a entrada padrão redirecionada (BOM) e ter um teste para isso". Manter na DoD de toda US com console ao menos um comando com pipe no PowerShell 5.1.
- **Caracteres invisíveis literais no código (S1).** Proposta: a skill `padroes-codigo` passa a exigir escape (`\uXXXX`) para caracteres não imprimíveis em código e testes. O checklist de revisão ganha esse item.
- **Lacuna de verificação do Revisor.** Os comandos permitidos ao Revisor não incluem `dotnet format --verify-no-changes`, `dotnet tool restore`/`dotnet reportgenerator` nem `dotnet run`. Por isso D3b, D4, D5–D7 e D11 só podem ser conferidos por leitura. Proposta: permitir ao Revisor `dotnet format --verify-no-changes` e `dotnet reportgenerator` com `-targetdir` no scratchpad, que não alteram o repositório. Os itens que dependem de `dotnet run` ficam marcados como "evidência do Executor, não reproduzida" no relatório.
- **Plano desatualizado depois da execução (S6).** Reincide o padrão já visto na revisão do plano (S10, status de ADR). Proposta: na tarefa de DoD, o Executor revisa o §3 e o §5 do `plan.md` e troca os estados "em andamento"/"Criar" pelo commit correspondente.
- **AP-004 continua "Proposta"**, mas a etapa de revisão do plano já foi usada nesta US e gerou 14 sugestões incorporadas (S1, S3 e S7 evitaram lacunas de teste que este diff teria). É evidência a favor de aprovar o AP-004 e aplicá-lo na skill `/us` e no agente Revisor.

## Rodada 2

Data: 2026-10-17
Commits revisados: f5d52b8..500a004 (`a6b016f..HEAD`, 9 commits: f5d52b8, 18ea9ea, 6d68c9e, 9c35276, 4303f24, c173601, 54778b9, db60928, 500a004). Build e testes rodados na branch inteira.
Resultado: APROVADO (pendências que não bloqueiam: D13 manual do usuário e a primeira execução do CI depois do push)

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ `dotnet build`: 0 Aviso(s), 0 Erro(s) |
| Testes (exceto LiveBrain) | ✅ 49 passaram (Core.Tests 27/27 em 67 ms; Console.Tests 22/22 em 356 ms) |
| Cobertura de linhas do Core | Core.Tests sozinho: 89,5%. Console.Tests sozinho: 96,5%. Somando os dois: 100% (o ReportGenerator do Executor deu o mesmo; eu só li os dois `coverage.cobertura.xml`). |
| Cenários BDD com teste | 8/8. São 7 com teste automatizado; o cenário de CI, que é opcional, é conferido por inspeção (D10). O mapa da §4 tem 36 métodos e confere com o código. |
| Teste antes do código (ordem de commits) | ✅ 4303f24 (os 3 testes do Red, S2–S4) vem antes de c173601, 54778b9 e db60928. O Red compila sem esqueleto: os testes só usam strings e o nome do evento lido do log. As mensagens do Red registradas no §11 batem com o código anterior. A S1 é refactor e não precisava de Red. |
| Evidências da DoD conferem | ✅ D1 e D2 batem com o que obtive: 0 avisos e 27 + 22 = 49 casos. D3 e D3b batem: 2 arquivos de cobertura e Core em 100%. D4, D5, D11 e D12 eu não reproduzi, porque `dotnet format`, `dotnet run` e `reportgenerator` estão fora dos comandos que posso usar. O registro do Executor está coerente com o código. D13 continua pendente do usuário. |
| Intenção antes do código (spec/ADR → código) | ⚠️ O pedido S1–S4 foi registrado no §12 (f5d52b8) antes do código. Já os detalhes das decisões (texto da mensagem da S2 e evento 1004 da S4) só entraram no §2.6/§2.7 em 500a004, depois do código. Ver S2 abaixo. Nenhum ADR era necessário. |

Checklist (resumo da rodada):
- R1–R4 ✅ (o Core não mudou).
- `OperationCanceledException` continua propagando ✅. O novo `catch (Exception) when (exception is not OperationCanceledException)` em volta do `BuildHost` não engole nada: escreve em `Error` e retorna 1, porque nesse ponto ainda não existe logger (P8).
- `CancellationToken.None` só aparece nas escritas de erro e no `StopAsync`, com justificativa ✅.
- Log com `[LoggerMessage]` e template ✅.
- Nenhum teste removido, enfraquecido ou com `Skip` ✅.
- Nenhum caractere U+FEFF literal sobrou no diff ✅.
- Commits convencionais com escopo ✅. T14 marcada `[x]` ✅.
- O texto do §3 sobre o `RepositoryRoot.cs` foi corrigido (`plan.md:144`) ✅.

#### Os desvios do Executor (§12 do plan.md)
- **S3: o teste cancela o token durante a escrita, com `CancellingTextWriter`. Aceitável, e é melhor que o teste pedido.** O Executor está certo: com o token já cancelado antes, `host.StartAsync(ct)` lança antes do `ValidateOnStart`. O app sai com 0 e nunca chega ao caminho de configuração inválida, o que também é correto, porque Ctrl+C antes da partida é saída limpa. O `CancellingTextWriter` respeita o token que recebe. Por isso o teste falharia com o `ct` antigo (o Red registrado no §11 confirma) e passa com `CancellationToken.None`. É exatamente o risco que a S3 descrevia.
- **S2: texto `The application could not start: <mensagem>`. Aceitável.** Não reaproveitar "See the log file for details" está correto, porque ainda não existe arquivo de log. O teste exige que não haja stack trace (`NotContain("   at ")`) e que a sessão não abra.
- **S4: evento novo `ApplicationAborted` (1004, Warning) no lugar de `ApplicationStopped`. Aceitável.** Assim o log não mostra mais um código de saída falso. A numeração e o nível são coerentes com 1000–1003, e o teste confere Id 1004, `ElapsedMs` e a ausência de `ApplicationStopped`. Há uma ressalva sobre o texto do evento, que pode enganar em outros caminhos: ver S1.

### Bloqueantes
Nenhum.

### Sugestões
- **S1** — `src/EnglishTutor.Console/EnglishTutorApp.cs:42,85-92` e `src/EnglishTutor.Console/Logging/AppLog.cs:27-30` — `exitCode` fica `null` sempre que **qualquer** exceção escapa do `try`/`catch`, e não só num cancelamento externo. Exemplos: `options.Error.WriteLineAsync` lança `IOException` dentro do `catch (OptionsValidationException)` ou do `catch` genérico, ou `logger.StartupFailed` falha. Nesses casos o log diz "a cancellation not requested by the app propagated", o que é falso. Esperado: texto neutro (ex.: "Application stopped abnormally after {ElapsedMs} ms; an exception propagated") e comentário da linha 42 ajustado. Outra opção é aceitar e registrar o limite no plano.
- **S2** — `tasks/US-001/plan.md:61,65,80` — as decisões de comportamento novas (mensagem da S2, evento 1004 da S4) só foram para o §2.6/§2.7 no commit de evidências (500a004), depois do código (c173601, db60928). O pedido do usuário no §12 deixava a escolha em aberto ("registrar o código real ou indicar saída anormal"), então não é violação. Mas o hábito 4 pede que a forma escolhida esteja no plano antes do código.
- **S3** — `src/EnglishTutor.Console/EnglishTutorApp.cs:126-138` — se `builder.Build()` (ou algo depois da linha 128) falhar, o `serilogLogger` já criado não é descartado: o `catch` da linha 29 não tem referência a ele. É um impacto pequeno, porque o processo encerra em seguida. Esperado: descartar o logger na falha de montagem, ou registrar que isso é aceito.
- **S4** — `tasks/US-001/review.md:31-36` — o Executor editou o relatório do Revisor e marcou S1–S4 como "**Resolvido**". O conteúdo está correto, mas o `review.md` é artefato do Revisor. Além disso, a S6 da Rodada 1 foi corrigida (`plan.md:144`) e não ficou marcada. Sugiro que o registro de resolução fique no `plan.md` (§12/T14) ou na rodada seguinte do `review.md`, escrita pelo Revisor.

### Pontos positivos
- Cada sugestão virou um commit pequeno e rastreável: um teste Red cobrindo as três e um `fix` por item. As mensagens do Red estão registradas no §11.
- Os dublês de teste novos (`CancellingTextWriter`, `ForeignCancellationTextReader`) são mínimos e não precisam de gancho no código de produção, como os anteriores.
- O Executor investigou antes de seguir o teste prescrito (S3), descobriu que o cenário pedido não chega ao caminho de código e registrou o motivo. Não forçou um teste que passaria por acaso.
- `int? exitCode` deixa explícito que "não há código conhecido" e evita um valor sentinela enganoso.
- Ele também corrigiu a S6 (texto do §3) e trocou o caractere U+FEFF literal pelo escape nos documentos.

### Aprendizados para o processo
- **Decisão de forma tomada durante o Green, documentada só depois (S2 desta rodada).** O caso é parecido com o "plano desatualizado depois da execução" da Rodada 1. Proposta: quando o pedido do usuário deixar uma escolha em aberto, o Executor registra a escolha no §12 do `plan.md`, num commit `docs(ai)` antes do `fix`/`feat`. Incluir isso na skill do Executor / `tdd-dotnet`.
- **Teste prescrito no pedido não exercitava o caminho (desvio da S3).** Proposta: quando o Revisor sugerir um teste, descrever o efeito esperado ("a mensagem não se perde se o cancelamento chegar durante a escrita") e não a técnica ("token já cancelado"). O Executor confirma que o Red falha pelo motivo certo antes de seguir, como fez aqui. Vale entrar no `checklist-revisao` como orientação para escrever sugestões.
- **Executor editando o relatório do Revisor (S4).** Proposta: o agente Executor não altera `tasks/US-XXX/review.md`; o status das sugestões fica no `plan.md`. Registrar no `.claude/agents/executor.md` se o usuário concordar.
- **Lacuna de verificação do Revisor** (já apontada na Rodada 1): D4, D5, D11 e D12 continuam sem reprodução independente. A proposta de liberar `dotnet format --verify-no-changes` e `reportgenerator` com saída no scratchpad continua válida.
