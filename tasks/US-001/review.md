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
