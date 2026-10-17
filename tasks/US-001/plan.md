# Plano — US-001 Fundação da solução e configuração

Status: Em revisão (aprovado em 2026-10-17; reaprovação pendente após S1–S14)
Branch: feature/US-001_fundacao-solucao
Spec: specs/sprints/SPRINT_1.md#us-001
Regras: RN-CONV01 (modo padrão), RN-CONV06, ARCHITECTURE §2 (R1–R4), §3, §9, §10; ADR-003, ADR-004 (Proposto)

## 1. Objetivo

Criar a solução .NET 10 em camadas (Core, Infrastructure, Console + projetos de teste) com DI via Generic Host, `Brain:Provider` validado na inicialização, logs estruturados em arquivo e uma casca de console que mostra o estado e trata os comandos locais `/help`, `/quit` e desconhecidos, sem falar com nenhum cérebro.

## 2. Abordagem

Decisões detalhadas no **ADR-004 (Proposto)**: `docs/adr/ADR-004-fundacao-solucao-host-config-logs-testes.md`. Resumo:

1. **Build**: `global.json` (SDK 10.0.100, `rollForward: latestFeature`), `Directory.Build.props` (conforme skill `padroes-codigo`), Central Package Management em `Directory.Packages.props`, solução `EnglishTutor.slnx`.
2. **Projetos**:
   - `EnglishTutor.Core` — referencia apenas `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`.
   - `EnglishTutor.Infrastructure` — criado vazio (só `.csproj` referenciando Core), para fixar a camada; adaptadores entram a partir da US-002.
   - `EnglishTutor.Console` — composition root (Hosting, Serilog, `appsettings.json`) e UI.
   - `tests/EnglishTutor.Core.Tests` e `tests/EnglishTutor.Console.Tests` (novo; ver §9 P1). `EnglishTutor.Infrastructure.Tests` fica para a US-002, quando houver o que testar.
3. **Configuração validada no Core**: `BrainOptions` (classe mutável para o binder, `SectionName = "Brain"`, `Provider` string) + `BrainProviders` (constantes `ClaudeCode`, `Ollama` e `All`) + `BrainOptionsValidator : IValidateOptions<BrainOptions>`. Mensagens (o que está errado + o que se espera):
   - vazio: `Brain:Provider is required. Accepted values: ClaudeCode, Ollama.`
   - inválido: `Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.`
   - comparação sem diferenciar maiúsculas (§9 P4). O Core expõe `services.AddEnglishTutorCore()` que registra o validador. O Console faz `AddOptions<BrainOptions>().Bind(...).ValidateOnStart()`.
4. **Modo padrão**: `TutorMode` (`sealed record TutorMode(string Name)`) em `Core/Conversation` com `FreeConversation`, `JobInterview`, `ListeningPractice`, `All` e `Default = FreeConversation` (RN-CONV01). Sem chave de configuração (§9 P3). A troca de modo é US-003.
5. **Comandos locais no Core** (RN-CONV06 é regra de negócio, R4): `LocalCommandParser.TryParse(string input, out LocalCommand? command)` (estático, puro). Entrada que, após `Trim`, começa com `/` é sempre comando; o nome é o primeiro token, sem diferenciar maiúsculas; o resto vai em `Arguments`. `LocalCommandKind { Help, Quit, Unknown }`. `LocalCommands.Available` (`IReadOnlyList<LocalCommandInfo>`) é a lista exibida em `/help` e no erro de comando desconhecido — na US-001 só `/help` e `/quit` (§9 P2).
   ```csharp
   public enum LocalCommandKind { Help, Quit, Unknown }
   public sealed record LocalCommand(LocalCommandKind Kind, string Name, string Arguments);
   public sealed record LocalCommandInfo(string Name, string Description);
   ```
6. **Casca do console** (testável com `TextReader`/`TextWriter` falsos):
   - `EnglishTutorApp.RunAsync(AppRunOptions options, CancellationToken ct) : Task<int>` monta o host (`HostApplicationBuilder` com `DisableDefaults`, `ContentRootPath = AppContext.BaseDirectory`; fontes: `appsettings.json`, `appsettings.{Environment}.json`, env vars, args — ou só a `IConfiguration` de teste quando `options.Configuration` vier preenchida), cria o logger Serilog, chama `StartAsync` (dispara `ValidateOnStart`), roda o `ChatSession` e para o host.
     ```csharp
     public sealed record AppRunOptions(string[] Args, TextReader Input, TextWriter Output, TextWriter Error,
                                        IConfiguration? Configuration = null);
     ```
   - `OptionsValidationException` na partida → escreve em `Error` `Invalid configuration:` + uma linha por falha, loga `StartupFailed` e retorna `ExitCodes.InvalidConfiguration` (2). Exceção inesperada → log `Fatal`, mensagem curta em `Error`, retorna `ExitCodes.UnexpectedError` (1). `OperationCanceledException` (Ctrl+C) → encerra limpo com 0.
   - `ChatSession.RunAsync(ct) : Task<int>` (em `Console/Ui`): escreve o cabeçalho, lê linhas em laço com prompt `> `:
     - `/quit` → retorna `ExitCodes.Success` (0);
     - `/help` → lista `LocalCommands.Available`;
     - desconhecido → `Unknown command "/xyz".` + `Available commands: /help, /quit` e continua;
     - texto livre → `IChatInputHandler.HandleAsync(text, ct)`;
     - linha vazia → ignora e mostra o prompt de novo (o aviso da RN-CONV05 é US-003);
     - fim da entrada (EOF, ex.: pipe sem `/quit`) → retorna 0.
   - Cabeçalho (texto exato, testado):
     ```
     EnglishTutor
     Brain: ClaudeCode | Mode: free-conversation
     Type /help for commands or /quit to exit.
     ```
   - `IChatInputHandler` (porta da UI, em `Console/Ui`) com `Task HandleAsync(string text, CancellationToken ct)`. Implementação da US-001: `TutorNotConnectedInputHandler`, que só escreve `The tutor is not connected yet.` Na US-003/004 ela é trocada por uma que delega ao `ConversationService` (R2). Nos testes, um `RecordingChatInputHandler` prova que `/quit` e comandos não chegam ao "cérebro" (§9 P5).
7. **Logs**: Serilog com `ReadFrom.Configuration`, apenas sink de arquivo (`logs/englishtutor-.log`, `rollingInterval: Day`, `CompactJsonFormatter`). Caminho relativo ao diretório atual (no `dotnet run` a partir da raiz, `./logs`). Eventos com `[LoggerMessage]` em `Console/Logging/AppLog.cs`: `ApplicationStarted` (1000, `{Provider}`, `{Mode}`), `ApplicationStopped` (1001, `{ExitCode}`, `{ElapsedMs}` via `TimeProvider`), `StartupFailed` (1002, `{Failures}`), `UnexpectedError` (1003). Logger por instância, `services.AddSerilog(logger, dispose: true)`; o descarte do host faz o flush antes de `RunAsync` retornar.
8. **CI (cenário opcional — incluído)**: `.github/workflows/ci.yml` em push/PR para `develop` e `main`: `setup-dotnet` lendo `global.json`, `dotnet build`, `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults`, ReportGenerator (ferramenta local em `.config/dotnet-tools.json`) gerando `MarkdownSummaryGithub` em `$GITHUB_STEP_SUMMARY` e `upload-artifact` dos cobertura XML.
9. **Cobertura consolidada**: os dois projetos de teste geram cobertura do Core; a DoD usa ReportGenerator (ferramenta local) para mesclar e ler a linha do `EnglishTutor.Core` (D3b).

Alternativas descartadas (detalhes no ADR-004):
- `.sln` clássico — verboso; `.slnx` já é suportado pelo SDK 10 e pelos hooks.
- `Provider` como `enum` — o binder falha sem listar os valores aceitos.
- Sink de console do Serilog — mistura log com a conversa do aluno.
- Testes do console executando `dotnet run` — lentos e dependentes de ambiente; ficam só na DoD (D5–D7, D11).
- Comandos locais tratados só no Console — RN-CONV06 é regra de negócio e precisa sobreviver à troca de UI (R2/R4).
- FluentAssertions 8.x — licença comercial.

### Alterações propostas na ARCHITECTURE.md (aplicar antes do código, com aprovação do plano)

O Planejador não edita a raiz; texto proposto para o orquestrador aplicar em commit `docs(ai): update architecture for US-001`:
- §2, tabela, linha `EnglishTutor.Core` → "Pode depender de": `BCL, Microsoft.Extensions.*.Abstractions (Logging, DependencyInjection), Microsoft.Extensions.Options`.
- §3: em `Conversation/` acrescentar `LocalCommandParser, LocalCommand, LocalCommands`; em `Brain/` acrescentar `BrainOptions, BrainOptionsValidator, BrainProviders`; em `EnglishTutor.Console/` acrescentar `EnglishTutorApp, Logging/`; em `tests/` acrescentar `EnglishTutor.Console.Tests/   (casca do console, em processo com TextReader/TextWriter)`.
- §9, linha de logs → "Logs estruturados com Serilog em arquivo (`logs/`, formato JSON compacto), incluindo latência por etapa (STT, cérebro, TTS). O console é reservado à interface do aluno."

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `global.json` | Fixa o SDK .NET 10 |
| Criar | `Directory.Build.props` | TFM `net10.0`, Nullable, ImplicitUsings, TreatWarningsAsErrors, AnalysisLevel, LangVersion |
| Criar | `Directory.Packages.props` | Versões centralizadas dos pacotes (§6) |
| Criar | `EnglishTutor.slnx` | Solução com os 5 projetos |
| Criar | `.config/dotnet-tools.json` | Ferramenta local `dotnet-reportgenerator-globaltool` |
| Criar | `src/EnglishTutor.Core/EnglishTutor.Core.csproj` | Projeto do domínio |
| Criar | `src/EnglishTutor.Core/Brain/BrainOptions.cs` | Options da seção `Brain` |
| Criar | `src/EnglishTutor.Core/Brain/BrainProviders.cs` | Nomes canônicos dos provedores aceitos |
| Criar | `src/EnglishTutor.Core/Brain/BrainOptionsValidator.cs` | `IValidateOptions<BrainOptions>` com mensagens claras |
| Criar | `src/EnglishTutor.Core/Conversation/TutorMode.cs` | Modos e modo padrão (RN-CONV01) |
| Criar | `src/EnglishTutor.Core/Conversation/LocalCommand.cs` | `LocalCommand`, `LocalCommandKind`, `LocalCommandInfo` |
| Criar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs` | Lista de comandos disponíveis |
| Criar | `src/EnglishTutor.Core/Conversation/LocalCommandParser.cs` | Reconhecimento de comandos locais (RN-CONV06) |
| Criar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | `AddEnglishTutorCore()` |
| Criar | `src/EnglishTutor.Infrastructure/EnglishTutor.Infrastructure.csproj` | Camada de adaptadores (vazia nesta US) |
| Criar | `src/EnglishTutor.Console/EnglishTutor.Console.csproj` | Executável; copia `appsettings.json` para a saída |
| Criar | `src/EnglishTutor.Console/Program.cs` | Repassa `Console.In/Out/Error` para `EnglishTutorApp` |
| Criar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Host, configuração, Serilog, partida/parada, códigos de saída |
| Criar | `src/EnglishTutor.Console/AppRunOptions.cs` | Parâmetros de execução (testáveis) |
| Criar | `src/EnglishTutor.Console/ExitCodes.cs` | `Success = 0`, `UnexpectedError = 1`, `InvalidConfiguration = 2` |
| Criar | `src/EnglishTutor.Console/Logging/AppLog.cs` | `[LoggerMessage]` com EventIds |
| Criar | `src/EnglishTutor.Console/Ui/ChatSession.cs` | Laço de entrada, cabeçalho, despacho de comandos |
| Criar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | Textos exibidos (cabeçalho, ajuda, desconhecido) |
| Criar | `src/EnglishTutor.Console/Ui/IChatInputHandler.cs` | Porta da UI para texto livre |
| Criar | `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` | Implementação provisória (sem cérebro) |
| Criar | `src/EnglishTutor.Console/appsettings.json` | `Brain:Provider = "ClaudeCode"` e seção `Serilog` |
| Criar | `tests/EnglishTutor.Core.Tests/EnglishTutor.Core.Tests.csproj` | Testes do Core |
| Criar | `tests/EnglishTutor.Core.Tests/Support/RepositoryRoot.cs` | Localiza a raiz (pasta com `EnglishTutor.slnx`) |
| Criar | `tests/EnglishTutor.Core.Tests/Architecture/CoreDependencyTests.cs` | Regra R1 por reflexão e por `.csproj` |
| Criar | `tests/EnglishTutor.Core.Tests/Architecture/BuildSettingsTests.cs` | Guarda de `TreatWarningsAsErrors`/`Nullable` |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/BrainOptionsValidatorTests.cs` | Validação do provedor |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/TutorModeTests.cs` | Modo padrão |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/LocalCommandParserTests.cs` | Comandos locais |
| Criar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | Registro do validador |
| Criar | `tests/EnglishTutor.Console.Tests/EnglishTutor.Console.Tests.csproj` | Testes da casca do console |
| Criar | `tests/EnglishTutor.Console.Tests/Support/TestConfiguration.cs` | `IConfiguration` em memória (provider, caminho de log em pasta temporária) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/RecordingChatInputHandler.cs` | Fake que registra o texto encaminhado |
| Criar | `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` | Comandos e encaminhamento |
| Criar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | Partida, cabeçalho, código de saída, configuração inválida, log |
| Criar | `.github/workflows/ci.yml` | Build, testes sem LiveBrain, cobertura |
| Criar | `docs/adr/ADR-004-fundacao-solucao-host-config-logs-testes.md` | (já criado pelo Planejador, Status: Proposto) |
| Alterar | `ARCHITECTURE.md` | Ajustes de §2, §3, §9 (texto acima) — pelo orquestrador, antes do código |

## 4. Mapa cenário BDD → testes

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| solução compila sem avisos | Core.Tests | `BuildSettingsTests` | `DirectoryBuildProps_Always_EnablesTreatWarningsAsErrorsAndNullable` | Arquitetura (leitura de arquivo) |
| solução compila sem avisos | — | — | DoD D1 (`dotnet build` 0 warnings) | Verificação de comando |
| Core não depende de pacotes externos | Core.Tests | `CoreDependencyTests` | `CoreAssembly_References_DoNotIncludeInfrastructureOrConsole` | Arquitetura (reflexão) |
| Core não depende de pacotes externos | Core.Tests | `CoreDependencyTests` | `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions` | Arquitetura (reflexão) |
| Core não depende de pacotes externos | Core.Tests | `CoreDependencyTests` | `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` | Arquitetura (`.csproj`) |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainOptionsValidatorTests` | `Validate_WhenProviderIsUnknown_FailsNamingValueAndAcceptedProviders` | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainOptionsValidatorTests` | `Validate_WhenProviderIsEmpty_FailsListingAcceptedProviders` | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainOptionsValidatorTests` | `Validate_WhenProviderIsAccepted_Succeeds` (`[Theory]`: `ClaudeCode`, `Ollama`, `claudecode`) | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_Always_RegistersBrainOptionsValidator` | Unidade |
| configuração do cérebro é validada na inicialização | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenProviderIsInvalid_ReturnsNonZeroAndExplainsAcceptedProviders` | Componente (em processo) |
| aplicação inicia e mostra o estado | Core.Tests | `TutorModeTests` | `Default_Always_IsFreeConversation` | Unidade |
| aplicação inicia e mostra o estado | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WithValidConfiguration_ShowsHeaderWithProviderModeAndCommands` | Componente |
| comando local de saída | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputIsQuit_ReturnsQuitCommand` | Unidade |
| comando local de saída | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenCommandHasDifferentCaseOrSurroundingSpaces_RecognizesIt` (`[Theory]`) | Unidade |
| comando local de saída | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesQuit_ReturnsZeroAndForwardsNothing` | Componente |
| comando local de saída | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenUserTypesQuit_ExitsWithZero` | Componente |
| comando local desconhecido | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenSlashCommandIsUnknown_ReturnsUnknownCommand` | Unidade |
| comando local desconhecido | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputDoesNotStartWithSlash_ReturnsFalse` | Unidade |
| comando local desconhecido | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands` | Componente |
| comando local desconhecido | Console.Tests | `ChatSessionTests` | `RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` | Componente |
| (apoio: `/help` citado no cabeçalho) | Core.Tests / Console.Tests | `LocalCommandParserTests` / `ChatSessionTests` | `TryParse_WhenInputIsHelp_ReturnsHelpCommand` / `RunAsync_WhenUserTypesHelp_ListsAvailableCommands` | Unidade / Componente |
| (apoio: texto livre não é comando) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesText_ForwardsItToInputHandler` | Componente |
| (apoio: pipe sem `/quit`, D5/D11) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenInputEnds_ReturnsZero` | Componente |
| logs estruturados | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenAppStops_WritesStructuredStartAndStopEventsToLogFile` | Componente (arquivo em pasta temporária) |
| integração contínua (opcional) | — | — | DoD D10 (inspeção do `ci.yml`) + primeira execução no GitHub após push feito pelo usuário | Verificação manual registrada no review |

Total previsto: 24 métodos de teste (2 deles `[Theory]`, 6 casos de dados) — 14 no Core.Tests e 10 no Console.Tests; ≈ 28 casos executados.

## 5. Tarefas (ordem de execução TDD)

Pré-condição do Executor: `dotnet --list-sdks` mostra um SDK 10.0.x (ROADMAP §1: instalar com `winget install Microsoft.DotNet.SDK.10`). Se não houver, **parar** e avisar.

- [x] T1 — Esqueleto: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `EnglishTutor.slnx`, os 3 projetos de `src/` e os 2 de `tests/` (vazios, com referências entre si e pacotes da §6), `.config/dotnet-tools.json`. Verificar `dotnet build` com 0 warnings e `dotnet test` executando (0 testes). Sem Red possível (infraestrutura). (commit: `chore(infra): scaffold solution, build props and test projects`)
- [ ] T2 — Testes de arquitetura e build (`CoreDependencyTests`, `BuildSettingsTests`, `RepositoryRoot`). São testes de **guarda**: passam com o Core vazio. Red demonstrado localmente adicionando temporariamente uma referência proibida ao Core (ex.: `Serilog`) e `TreatWarningsAsErrors=false`, confirmando a falha com mensagem clara; reverter sem commitar e registrar nas Evidências. (commit: `test(core): guard core dependency rule and build settings`)
- [ ] T3 — Red: `TutorModeTests`. Green: `TutorMode`. (commits: `test(core): default tutor mode` + `feat(core): add tutor modes with free-conversation default`)
- [ ] T4 — Red: `LocalCommandParserTests` (quit, help, desconhecido, sem barra, caixa/espaços). Green: `LocalCommand*`, `LocalCommands`, `LocalCommandParser`. (commits: `test(core): local command parsing` + `feat(core): parse local slash commands`)
- [ ] T5 — Red: `BrainOptionsValidatorTests` + `CoreServiceCollectionExtensionsTests`. Green: `BrainOptions`, `BrainProviders`, `BrainOptionsValidator`, `AddEnglishTutorCore`. Refactor: mensagens montadas a partir de `BrainProviders.All`. (commits: `test(core): brain provider validation` + `feat(core): validate brain provider options`)
- [ ] T6 — Red: `ChatSessionTests` (quit, desconhecido, continuidade, help, texto livre, EOF) com `RecordingChatInputHandler` e `StringReader`/`StringWriter`. Green: `ChatSession`, `ConsoleText`, `IChatInputHandler`, `ExitCodes`. (commits: `test(console): chat session local commands` + `feat(console): add chat session shell`)
- [ ] T7 — Red: `EnglishTutorAppTests` de cabeçalho e `/quit` → 0. Green: `AppRunOptions`, `EnglishTutorApp` (host, fontes de configuração, DI, `TutorNotConnectedInputHandler`), `Program.cs`, `appsettings.json`. (commits: `test(console): startup header and quit exit code` + `feat(console): compose host and run chat session`)
- [ ] T8 — Red: `RunAsync_WhenProviderIsInvalid_...`. Green: bind + `ValidateOnStart`, captura de `OptionsValidationException`, `ExitCodes.InvalidConfiguration`, mensagem em `Error`. (commits: `test(console): invalid brain provider at startup` + `feat(console): fail fast on invalid configuration`)
- [ ] T9 — Red: `RunAsync_WhenAppStops_WritesStructuredStartAndStopEventsToLogFile` (lê o arquivo CLEF, faz parse JSON por linha e procura `EventId.Name` = `ApplicationStarted` com `Provider`/`Mode` e `ApplicationStopped` com `ExitCode`). Green: Serilog (`ReadFrom.Configuration`, sink de arquivo, `CompactJsonFormatter`), `AppLog`, `TimeProvider` para `ElapsedMs`. (commits: `test(console): structured start and stop log events` + `feat(console): add serilog file logging`)
- [ ] T10 — CI: `.github/workflows/ci.yml` (build, testes sem LiveBrain com cobertura, ReportGenerator no step summary, upload do artefato). (commit: `ci(infra): add build and test workflow with coverage`)
- [ ] T11 — DoD: rodar D1–D12, colar em §11 e marcar as tarefas. (commit: `docs(ai): add DoD evidence for US-001`)

## 6. Pacotes NuGet

Versões fixadas em `Directory.Packages.props` (última estável compatível com .NET 10 no momento da execução; o Executor registra a versão escolhida nas Evidências).

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Options | Core | `IValidateOptions<T>`, `ValidateOptionsResult` (permitido por R1) |
| Microsoft.Extensions.DependencyInjection.Abstractions | Core | `AddEnglishTutorCore(IServiceCollection)` |
| Microsoft.Extensions.Hosting | Console | Generic Host, configuração, DI, Ctrl+C |
| Microsoft.Extensions.Options.ConfigurationExtensions | Console | `Bind` da seção `Brain` (vem com Hosting; explicitar se necessário) |
| Serilog.Extensions.Hosting | Console | `services.AddSerilog(logger, dispose: true)` |
| Serilog.Settings.Configuration | Console | `ReadFrom.Configuration` (caminho do log substituível nos testes) |
| Serilog.Sinks.File | Console | Arquivo em `logs/` |
| Serilog.Formatting.Compact | Console | Formato estruturado CLEF |
| xunit.v3 | Core.Tests, Console.Tests | Framework de testes |
| xunit.runner.visualstudio | Core.Tests, Console.Tests | Execução via `dotnet test` (VSTest) |
| Microsoft.NET.Test.Sdk | Core.Tests, Console.Tests | Host de testes VSTest |
| FluentAssertions (7.x) | Core.Tests, Console.Tests | Asserções; 7.x por licença Apache 2.0 (§9 P6) |
| NSubstitute | Core.Tests, Console.Tests | Dublês simples (padrão do projeto; pode não ser usado nesta US) |
| coverlet.collector | Core.Tests, Console.Tests | `--collect:"XPlat Code Coverage"` |
| Microsoft.Extensions.Configuration (Memory) | Console.Tests | `IConfiguration` em memória (vem via Hosting pela referência ao Console) |
| dotnet-reportgenerator-globaltool (ferramenta local) | `.config/dotnet-tools.json` | Mesclar cobertura (D3b) e resumo no CI |

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| SDK .NET 10 não instalado (ROADMAP §1 só encontrou 9.0.200) | Pré-condição explícita no §5; Executor para e avisa |
| Variáveis de ambiente da máquina (ex.: `Brain__Provider` deixado pelo D6) contaminando testes | Testes passam `IConfiguration` própria e o host usa `DisableDefaults` (sem env vars nesse caminho); D6 termina com `Remove-Item Env:Brain__Provider` |
| Arquivo de log travado/não descarregado ao ler no teste | Logger descartado com o host antes de `RunAsync` retornar; cada teste usa pasta temporária própria |
| xUnit v3: analisador xUnit1051 vira erro com `TreatWarningsAsErrors` | Passar `TestContext.Current.CancellationToken` em todas as chamadas assíncronas |
| `ReadLineAsync(ct)` do `Console.In` pode não respeitar cancelamento no Ctrl+C | Ctrl+C tratado pelo `ConsoleLifetime`; fora da suíte automatizada; verificação manual opcional registrada nas Evidências |
| Cobertura do Core dividida entre dois projetos de teste | ReportGenerator mescla (D3b); Core coberto principalmente por Core.Tests |
| Testes de arquitetura dependentes do caminho do repositório | `RepositoryRoot` sobe a partir de `AppContext.BaseDirectory` até achar `EnglishTutor.slnx`, com mensagem clara se não achar |
| CI não verificável localmente (sem `gh`, sem push autorizado) | Cenário opcional; validação por inspeção (D10) e primeira execução após push feito pelo usuário |
| `dotnet format --verify-no-changes` falhar por fim de linha (`.editorconfig` exige LF no Windows) | Conferir `.gitattributes`; rodar `dotnet format` antes do D4 |

## 8. Fora de escopo

- Conversa com qualquer cérebro, `ITutorBrain`, `BrainRequest`, `TutorReply` (US-002/US-004).
- Aviso de entrada vazia (RN-CONV05) e `/mode` (US-003); `/speed` (Sprint 2). Nesta US, `/mode` e `/speed` respondem como comando desconhecido.
- `Brain:FallbackProvider` e subseções `Brain:ClaudeCode` / `Brain:Ollama` e sua validação (US-004, US-006, US-007).
- `Conversation:MaxTurnsInWindow` (US-003).
- `EnglishTutor.Infrastructure.Tests` e extensão `AddEnglishTutorInfrastructure` (US-002/US-004).
- `appsettings.Local.json` / segredos locais.
- Sink de console do Serilog (ver §9 P7).

## 9. Perguntas em aberto

- **P1 — Novo projeto `tests/EnglishTutor.Console.Tests`** (não listado em ARCHITECTURE §3). Necessário para testar a casca do console em processo, e casa com o hook `src/X → tests/X.Tests`. **Padrão proposto:** criar e atualizar §3 (texto em §2 deste plano).
- **P2 — Lista exibida em `/help` e no "Unknown command"**: RN-CONV06 cita `/quit`, `/mode`, `/speed`, `/help`, mas só `/help` e `/quit` existem na US-001. **Padrão proposto:** listar apenas os comandos implementados; cada US que adicionar um comando acrescenta à `LocalCommands.Available`. Sem mudança no texto da RN-CONV06.
- **P3 — "modo padrão" vem de configuração?** **Padrão proposto:** não; o modo padrão é fixo pela RN-CONV01 (`free-conversation`), sem chave `Conversation:DefaultMode`.
- **P4 — `Brain:Provider` diferencia maiúsculas?** **Padrão proposto:** não (`claudecode` aceito); o cabeçalho mostra o nome canônico (`ClaudeCode`).
- **P5 — "nada é enviado a nenhum cérebro" sem cérebro existente.** **Padrão proposto:** a UI encaminha texto livre por `IChatInputHandler` (porta da UI); o teste prova com um fake que `/quit` e comandos não são encaminhados. Na US-003/004 a implementação passa a delegar ao `ConversationService`.
- **P6 — FluentAssertions 7.x (Apache 2.0) em vez de 8.x (comercial).** **Padrão proposto:** 7.x, como orienta a skill `tdd-dotnet`.
- **P7 — Logs só em arquivo**, divergindo de ARCHITECTURE §9 ("console + arquivo"). **Padrão proposto:** só arquivo; o console fica para a UI; atualizar §9 (texto em §2 deste plano).
- **P8 — Códigos de saída.** **Padrão proposto:** `0` sucesso (inclui `/quit`, EOF e Ctrl+C), `1` erro inesperado, `2` configuração inválida.
- **P9 — Cenário de CI (opcional).** **Padrão proposto:** incluir agora (T10), validado por inspeção; a primeira execução real acontece quando o usuário fizer push.

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell.

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes (≥ 24 métodos, ≈ 28 casos), em < 30 s, sem rede nem GPU |
| D3 | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` (apagar `TestResults/` antes) | gera `coverage.cobertura.xml` para os dois projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `"/quit" \| dotnet run --project src/EnglishTutor.Console; $LASTEXITCODE` | cabeçalho com `Brain: ClaudeCode \| Mode: free-conversation` e as dicas `/help` e `/quit`; `0` |
| D6 | `$env:Brain__Provider="Inexistente"; dotnet run --project src/EnglishTutor.Console; $LASTEXITCODE; Remove-Item Env:Brain__Provider` | código ≠ 0 (`2`); mensagem cita `'Inexistente'` e `Accepted values: ClaudeCode, Ollama` |
| D7 | `Get-ChildItem logs; Get-Content (Get-ChildItem logs/*.log \| Sort-Object LastWriteTime \| Select-Object -Last 1)` após D5 | arquivo `englishtutor-AAAAMMDD.log` com linhas JSON contendo `ApplicationStarted` e `ApplicationStopped` |
| D8 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"` (incluído em D2) | verde |
| D9 | `dotnet list src/EnglishTutor.Core package` e `dotnet list src/EnglishTutor.Core reference` | só `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`; nenhuma referência de projeto |
| D10 | `Get-Content .github/workflows/ci.yml` | gatilhos push/PR em `develop` e `main`; passos de build, `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` e publicação da cobertura |
| D11 | `"hello" \| dotnet run --project src/EnglishTutor.Console; $LASTEXITCODE` | mostra `The tutor is not connected yet.`; termina com `0` ao fim da entrada |
| D12 | `git status --short` após D5–D7 e D11 | nenhum arquivo de `logs/`, `TestResults/` ou `coverage/` aparece (ignorados) |

E também (DoD comum): todo cenário BDD com teste escrito antes do código (exceto os testes de guarda de T2, com Red demonstrado e registrado); `review.md` sem bloqueantes; `docs/us/US-001.md` criado; ADR-004 aceito pelo usuário e `ARCHITECTURE.md` atualizado; merge em `develop`.

### Respostas do usuário (2026-10-17)

Aprovadas no chat as respostas padrão de P1–P9 e o ADR-004 (Aceito): P1 criar `tests/EnglishTutor.Console.Tests`; P2 listar só os comandos implementados; P3 modo padrão fixo (RN-CONV01), sem chave de configuração; P4 `Brain:Provider` sem diferenciar maiúsculas; P5 porta `IChatInputHandler` com fake nos testes; P6 FluentAssertions 7.x; P7 logs só em arquivo; P8 códigos de saída 0/1/2; P9 incluir o CI agora.

Nota do orquestrador: o SDK 10.0.402 já está instalado (`dotnet --list-sdks`); a pré-condição do §5 está satisfeita.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-17 — Incorporar as sugestões S1–S14 da revisão do plano

Pedido do usuário no chat (transcrito pelo orquestrador): incorporar ao plano **todas** as sugestões S1 a S14 de [plan-review.md](plan-review.md); o plano volta para reaprovação. Contexto: a T1 já está commitada (648f510) e há `tests/EnglishTutor.Core.Tests/Support/RepositoryRoot.cs` não commitado (início da T2); o plano revisado deve refletir esse estado.

- **S1** — Dar dono e teste à normalização de `Brain:Provider` para o nome canônico (P4): ex. `BrainProviders.TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical)`, `BrainProvidersTests.TryGetCanonical_WhenCaseDiffers_ReturnsCanonicalName`, caso de cabeçalho com `claudecode` em `EnglishTutorAppTests`; incluir `BrainProvidersTests.cs` no §3.
- **S2** — O `CLAUDE.md` foi alinhado pelo orquestrador (Core: só BCL, `Microsoft.Extensions.*.Abstractions` e `Microsoft.Extensions.Options`). Registrar no §2 do plano, junto das alterações da ARCHITECTURE, como já aplicado.
- **S3** — Definir no §2.6 de onde vem o token do `ChatSession` (ex.: `CreateLinkedTokenSource(ct, lifetime.ApplicationStopping)`); restringir o `catch (OperationCanceledException)` a `when (token.IsCancellationRequested)`; tornar a verificação manual do Ctrl+C item obrigatório da DoD (D13).
- **S4** — Deixar explícito que a T7 faz só `Bind` e que `ValidateOnStart` + `AddEnglishTutorCore` entram na T8, para preservar o Red da T8.
- **S5** — T2 com Core vazio: usar `Assembly.Load("EnglishTutor.Core")` (ou mover a T2 para depois da T3). Red de demonstração com pacote já versionado no CPM (ex.: `Serilog.Sinks.File`) e registrar qual teste falhou.
- **S6** — Definir a allowlist de `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions`: shared framework `Microsoft.NETCore.App` + `Microsoft.Extensions.Options` + `Microsoft.Extensions.*.Abstractions`; decidir sobre `Microsoft.Extensions.Primitives`.
- **S7** — Mapear testes para: `TutorNotConnectedInputHandler` (ex.: entrada `"hello"` em `EnglishTutorAppTests`), caminho `UnexpectedError` → código 1 com evento 1003, e linha só com espaços (`IsNullOrWhiteSpace`, não vai ao handler).
- **S8** — `AppRunOptions.Args` como `IReadOnlyList<string>` em vez de `string[]`.
- **S9** — Resolver a divergência de `Microsoft.Extensions.DependencyInjection` no `Directory.Packages.props`: incluir no §6 com uso justificado ou remover.
- **S10** — Atualizar as referências ao ADR-004 de "Proposto" para "Aceito".
- **S11** — Explicitar no §2.6 como o nome do ambiente é obtido para `appsettings.{Environment}.json` com `DisableDefaults`.
- **S12** — Especificar o runner do CI e confirmar a compatibilidade de `setup-dotnet` com o `global.json`.
- **S13** — `[NotNullWhen(true)]` no `out` de `TryParse`; incluir `"/"` sozinho como caso do parser, com resultado definido.
- **S14** — `Program.cs` como "Alterar" (já existe desde a T1) no §3 e na T7.
