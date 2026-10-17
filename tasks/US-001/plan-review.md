# Revisão do plano: US-001 Fundação da solução e configuração

Data: 2026-10-17
Escopo revisado: `C:\Crescimento\TutorIA\tasks\US-001\plan.md` (commits b8b6471, f903b78) e `ARCHITECTURE.md` (798050a). O esqueleto da T1 (648f510) foi usado só para checar se contradiz o plano. Não contradiz.
Resultado: APROVADO

O plano está apto para execução. Encontrei 14 sugestões: S1, S2 e S3 valem resolver antes do Executor seguir para a T5, porque mexem em comportamento já decidido.

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings (esqueleto T1, SDK 10.0.402) | OK: `dotnet build` com 0 avisos e 0 erros |
| Testes (exceto LiveBrain) | OK, ainda sem testes: `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` roda nos 2 projetos ("Nenhum teste corresponde ao filtro") e gera os 2 `coverage.cobertura.xml`. A combinação xUnit v3 + VSTest + coverlet funciona. |
| Cobertura de linhas do Core | N/A (ainda não há código); o plano a garante com D3 + D3b (ReportGenerator) e meta de 80% |
| Cenários BDD com teste | 8/8: os 7 obrigatórios têm teste; "compila sem avisos" tem teste de guarda + D1; o cenário de CI (opcional) é coberto por inspeção D10, como admite a skill `bdd-para-testes` |
| Teste antes do código (ordem planejada) | OK: T3–T9 em pares `test(...)` → `feat(...)`. A exceção da T2 (testes de guarda) está justificada e tem Red demonstrado e registrado. |
| Evidências da DoD conferem | N/A (fase de plano). A DoD tem comandos verificáveis e cobre D1–D8 da spec, mais D3b e D9–D12. |
| Intenção antes do código (spec/ADR → código) | OK: ADR-004 em b8b6471 e aceito em f903b78; ARCHITECTURE em 798050a, antes do esqueleto 648f510 |
| R1–R4, CancellationToken, TreatWarningsAsErrors, Core sem pacotes de terceiros | OK (ver S2 sobre o `CLAUDE.md`) |
| Escopo | OK: nada além do que a US pede; o fora de escopo está explícito no §8 |
| Contagem interna do plano (§3 × §4 × §5 × §6) | OK: 14 + 10 = 24 métodos, conferido método a método; os arquivos do §3 batem com as tarefas. Divergência pequena em S9. |

## Bloqueantes
Nenhum.

## Sugestões
- **S1 (resolver antes da T5)**: a P4 diz que "o cabeçalho mostra o nome canônico (`ClaudeCode`)" quando a configuração traz `claudecode`. Esse comportamento não tem dono nem teste.
  - Problema: o §2.3 só lista as constantes de `BrainProviders`. Não há API de normalização, nem quem a chama (validador? `ChatSession`? `PostConfigure`?), nem teste no §4. O `[Theory]` `Validate_WhenProviderIsAccepted_Succeeds` só prova que o valor é aceito, não que vira `ClaudeCode` no cabeçalho.
  - Esperado: definir um método (ex.: `BrainProviders.TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical)`), mapear `TryGetCanonical_WhenCaseDiffers_ReturnsCanonicalName` em `BrainProvidersTests` e um caso de cabeçalho com `claudecode` em `EnglishTutorAppTests`. Também colocar `BrainProvidersTests.cs` no §3. Sem isso, o Executor vai escrever código sem teste ou ignorar uma decisão aprovada.
- **S2 (resolver antes da T5)**: `CLAUDE.md` contradiz o plano e a ARCHITECTURE.
  - Problema: a linha "Regras rápidas" diz "`EnglishTutor.Core` não referencia pacotes externos (só BCL e `Microsoft.Extensions.*.Abstractions`)". O plano, a ARCHITECTURE §2 (798050a) e o próprio cenário BDD permitem `Microsoft.Extensions.Options`. A ARCHITECTURE prevalece, mas um Revisor ou Executor que leia só o `CLAUDE.md` vai acusar violação de R1 na T5.
  - Esperado: incluir no §2 do plano a alteração do `CLAUDE.md`, a ser aplicada pelo orquestrador junto com a ARCHITECTURE, com o texto "só BCL, `Microsoft.Extensions.*.Abstractions` e `Microsoft.Extensions.Options`".
- **S3 (resolver antes da T7)**: Ctrl+C e qual token o `ChatSession` recebe.
  - Problema: a P8 promete "Ctrl+C → 0" e a ARCHITECTURE §9 exige que "Ctrl+C encerre a sessão de forma limpa". Mas o `HostApplicationBuilder` registra o `ConsoleLifetime` mesmo com `DisableDefaults`, e ele intercepta o Ctrl+C (`e.Cancel = true`) e só chama `StopApplication()`. Se o `ChatSession` receber o `ct` do `Program` (sem ligação com `IHostApplicationLifetime.ApplicationStopping`), ninguém cancela nada. Além disso, `Console.In.ReadLineAsync(ct)` não é interrompível. No Windows o `ReadLine` costuma devolver `null` depois de um Ctrl+C (aí cai no caminho de EOF), mas isso é comportamento da plataforma, não do projeto.
  - Esperado: o §2.6 deve dizer de onde vem o token (ex.: `CreateLinkedTokenSource(ct, lifetime.ApplicationStopping)`). O `catch (OperationCanceledException)` deve ficar restrito a `when (token.IsCancellationRequested)`, para não ser lido como "catch que engole" pelo checklist. E a verificação manual do Ctrl+C deve virar item obrigatório da DoD (D13), em vez de "opcional" (§7).
- **S4**: a ordem de T7 e T8 pode estragar o Red da T8. A T7 já faz "DI" e precisa de `BrainOptions` com `Bind` para o cabeçalho. Se o Executor colocar `ValidateOnStart` ali, o teste da T8 nasce verde. Deixar escrito que a T7 faz só `Bind` e que `ValidateOnStart` + `AddEnglishTutorCore` entram na T8.
- **S5**: a T2 roda com o Core vazio, então não há tipo para `typeof(...).Assembly`. Indicar `Assembly.Load("EnglishTutor.Core")`, ou mover a T2 para depois da T3.
  - O Red de demonstração com "referência proibida (ex.: `Serilog`)" também tem dois furos. Com CPM, `Serilog` não tem `PackageVersion` no `Directory.Packages.props`, e o restore falha com NU1010 antes do teste. E referência não usada não aparece em `GetReferencedAssemblies`, então só o teste de `.csproj` vai falhar. Sugerir `Serilog.Sinks.File` (já versionado) e registrar qual teste falhou.
- **S6**: a allowlist de `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions` não está definida. Explicitar o critério: assemblies do shared framework `Microsoft.NETCore.App` (não só o prefixo `System.*`, que deixaria de fora `Microsoft.Win32.*`/`netstandard`) mais `Microsoft.Extensions.Options` e `Microsoft.Extensions.*.Abstractions`. Dizer também se `Microsoft.Extensions.Primitives` (dependência transitiva do Options) é aceito.
- **S7**: três comportamentos do console não têm teste mapeado: `TutorNotConnectedInputHandler` (só D11, manual), o caminho `UnexpectedError` → código 1 com evento 1003 (§2.6/§2.7), e linha só com espaços (o plano fala apenas em "linha vazia"; dizer se é `IsNullOrWhiteSpace` e se vai ou não ao handler, porque a RN-CONV05 exige que nada vá ao cérebro). Sugerir 1 teste para cada; o primeiro cabe em `EnglishTutorAppTests` com entrada `"hello"`.
- **S8**: `AppRunOptions(string[] Args, ...)` expõe array numa propriedade pública. A skill `padroes-codigo` pede `IReadOnlyList<T>`, e o CA1819 pode virar erro com `TreatWarningsAsErrors`. Trocar por `IReadOnlyList<string>`.
- **S9**: o §6 e o esqueleto divergem. O `Directory.Packages.props` (648f510) declara `Microsoft.Extensions.DependencyInjection` 10.0.13, que não está no §6 e nenhum projeto usa. Ou incluir no §6 (Core.Tests, se `CoreServiceCollectionExtensionsTests` for chamar `BuildServiceProvider`), ou remover e testar só os `ServiceDescriptor`.
- **S10**: o plano ainda chama o ADR-004 de "Proposto" (linha 6 "Regras", §2 linha 14 e §3 linha 117), mas ele está Aceito desde f903b78. Corrigir para não confundir o Revisor de código.
- **S11**: no §2.6, explicitar como o nome do ambiente é obtido para `appsettings.{Environment}.json`. Com `DisableDefaults`, `DOTNET_ENVIRONMENT` não é lido automaticamente e o valor fica `Production`.
- **S12**: na T10, especificar o runner do CI (`windows-latest` ou `ubuntu-latest`). Também confirmar que `setup-dotnet` com `global-json-file` e `rollForward: latestFeature` instala um SDK 10.0.x compatível; o 10.0.100 basta para o `global.json`.
- **S13**: `TryParse(string input, out LocalCommand? command)` deveria levar `[NotNullWhen(true)]` no `out`, para os chamadores não precisarem de `!` com `Nullable` ligado. Incluir `"/"` sozinho como caso no `[Theory]` do parser (vira `Unknown` com nome vazio?).
- **S14**: `Program.cs` aparece como "Criar" no §3 e na T7, mas já existe desde a T1 (`return 0;`). O certo é "Alterar".

## Pontos positivos
- Rastreabilidade completa: os 8 cenários estão mapeados para classe e método no padrão `Metodo_Cenario_Resultado`, com contagem que confere (14 + 10 = 24).
- Boa leitura de R4: `LocalCommandParser` no Core, com a RN-CONV06 testada por unidade, e o console só reage ao resultado.
- `DisableDefaults` evita o logger de console padrão, que poluiria a tela do aluno, e isola os testes das variáveis de ambiente (risco do D6 tratado).
- Testes do console em processo com `TextReader`/`TextWriter`, rápidos e sem processo real (hábito 5).
- A DoD vai além da spec: D3b mescla a cobertura dos dois projetos, D9 lista os pacotes, D12 confere o `.gitignore`. O `.gitignore` atual já cobre `logs/`, `TestResults/` e `coverage/`.
- Riscos reais antecipados: xUnit1051 com `TreatWarningsAsErrors`, fim de linha LF no `dotnet format`, FluentAssertions 7.x por licença.
- A pilha escolhida funciona no SDK 10.0.402. Conferi rodando build, testes e coleta de cobertura sobre o esqueleto.

## Aprendizados para o processo
- **Documentos-raiz fora de sincronia (S2)**: o plano propôs mudanças na ARCHITECTURE §2, mas não no `CLAUDE.md`, que repete a mesma regra de forma resumida. Proposta: o checklist do Planejador e da skill `/us` deve exigir "toda alteração proposta na ARCHITECTURE lista também as linhas do `CLAUDE.md` e das skills que repetem a regra". Também cabe na poda do `/revisar-contexto`.
- **Status de ADR desatualizado no plano (S10)**: o passo de aprovação mudou o ADR para Aceito, mas não as referências no `plan.md`. Proposta: o passo 2.x do `/us` que registra a aprovação também troca "(Proposto)" por "(Aceito)" no plano.
- **Decisão aprovada sem teste nem tarefa (S1)**: respostas P* que mudam comportamento observável precisam aparecer no mapa da §4. Proposta: o checklist de revisão de plano (AP-004, ainda "Proposta") deve conferir "cada P* com efeito observável tem teste mapeado".
- Este relatório é o primeiro caso concreto da etapa "Revisão do plano" proposta no AP-004 (`docs/aprendizados.md`, mudança ainda não commitada). Ele dá base para decidir sobre essa proposta.

Observação, sem efeito no resultado: a árvore de trabalho tem `tests/EnglishTutor.Core.Tests/Support/RepositoryRoot.cs` não rastreado (T2 em andamento) e `docs/aprendizados.md` modificado (AP-004). Rodar `dotnet test` criou pastas `tests/*/TestResults/`, que o `.gitignore` já cobre. Não alterei nenhum arquivo do projeto.
