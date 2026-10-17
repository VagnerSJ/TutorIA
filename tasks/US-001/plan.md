# Plano — US-001 Fundação da solução e configuração

Status: Aprovado em 2026-10-17 (reaprovado após revisão do plano S1–S14)
Branch: feature/US-001_fundacao-solucao
Spec: specs/sprints/SPRINT_1.md#us-001
Regras: RN-CONV01 (modo padrão), RN-CONV05 (entrada vazia não vai ao cérebro), RN-CONV06, ARCHITECTURE §2 (R1–R4), §3, §9, §10; ADR-003, ADR-004 (Aceito)

## 1. Objetivo

Criar a solução .NET 10 em camadas (Core, Infrastructure, Console + projetos de teste) com DI via Generic Host, `Brain:Provider` validado na inicialização, logs estruturados em arquivo e uma casca de console que mostra o estado e trata os comandos locais `/help`, `/quit` e desconhecidos, sem falar com nenhum cérebro.

## 2. Abordagem

Decisões detalhadas no **ADR-004 (Aceito em 2026-10-17)**: `docs/adr/ADR-004-fundacao-solucao-host-config-logs-testes.md`. Nenhuma sugestão da revisão (S1–S14) exige mudar o ADR-004: as definições novas abaixo detalham a decisão sem contrariá-la. Resumo:

1. **Build** (já feito na T1, 648f510): `global.json` (SDK 10.0.100, `rollForward: latestFeature`), `Directory.Build.props` (conforme skill `padroes-codigo`), Central Package Management em `Directory.Packages.props`, solução `EnglishTutor.slnx`, ferramenta local ReportGenerator em `.config/dotnet-tools.json`.
2. **Projetos** (já criados na T1):
   - `EnglishTutor.Core` — referencia apenas `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`.
   - `EnglishTutor.Infrastructure` — vazio (só `.csproj` referenciando Core), para fixar a camada; adaptadores entram a partir da US-002.
   - `EnglishTutor.Console` — composition root (Hosting, Serilog, `appsettings.json`) e UI.
   - `tests/EnglishTutor.Core.Tests` e `tests/EnglishTutor.Console.Tests` (P1). `EnglishTutor.Infrastructure.Tests` fica para a US-002.
3. **Configuração validada no Core**:
   - `BrainOptions` (classe mutável para o binder, `SectionName = "Brain"`, `Provider` string).
   - `BrainProviders` (estática, sem estado mutável): constantes `ClaudeCode`, `Ollama`, `All` (`IReadOnlyList<string>`) e o **único dono da normalização** (P4, S1):
     ```csharp
     public static bool TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical);
     ```
     Compara com `StringComparison.OrdinalIgnoreCase`, sem `Trim` (`" ClaudeCode "` é inválido); `null`, vazio ou só espaços → `false`.
   - `BrainOptionsValidator : IValidateOptions<BrainOptions>` usa `TryGetCanonical`. Mensagens (o que está errado + o que se espera), montadas a partir de `BrainProviders.All`:
     - vazio ou só espaços: `Brain:Provider is required. Accepted values: ClaudeCode, Ollama.`
     - inválido: `Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.`
   - Quem mostra o nome canônico: `EnglishTutorApp`, ao montar o cabeçalho, chama `BrainProviders.TryGetCanonical(options.Provider, out var name)` (com `claudecode` o cabeçalho mostra `ClaudeCode`). As US seguintes que escolherem o adaptador pelo provedor (US-004) usam o mesmo método. Descartado: `IPostConfigureOptions` que reescreve `Provider` (mais um tipo e esconde do leitor que o valor configurado foi alterado).
   - `services.AddEnglishTutorCore()` (Core) registra o validador. O Console faz `AddOptions<BrainOptions>().Bind(...)` e, a partir da T9, `.ValidateOnStart()` + `AddEnglishTutorCore()` (S4).
4. **Modo padrão**: `TutorMode` (`sealed record TutorMode(string Name)`) em `Core/Conversation` com `FreeConversation`, `JobInterview`, `ListeningPractice`, `All` e `Default = FreeConversation` (RN-CONV01). Sem chave de configuração (P3). A troca de modo é US-003.
5. **Comandos locais no Core** (RN-CONV06 é regra de negócio, R4): parser estático e puro (S13):
   ```csharp
   public static bool TryParse(string input, [NotNullWhen(true)] out LocalCommand? command);
   public enum LocalCommandKind { Help, Quit, Unknown }
   public sealed record LocalCommand(LocalCommandKind Kind, string Name, string Arguments);
   public sealed record LocalCommandInfo(string Name, string Description);
   ```
   - Entrada que, após `Trim`, começa com `/` é sempre comando (`true`); senão `false` e `command` nulo.
   - `Name` é o primeiro token como digitado, **com** a barra (`"/quit"`, `"/XYZ"`); `Kind` é resolvido sem diferenciar maiúsculas; o resto (após `Trim`) vai em `Arguments`.
   - `"/"` sozinho → `true`, `Kind = Unknown`, `Name = "/"`, `Arguments = ""` (o console mostra `Unknown command "/".`).
   - `/mode` e `/speed` ainda não existem → `Unknown` (ex.: `"/mode interview"` → `Name = "/mode"`, `Arguments = "interview"`).
   - `LocalCommands.Available` (`IReadOnlyList<LocalCommandInfo>`) é a lista exibida em `/help` e no erro de comando desconhecido — na US-001 só `/help` e `/quit` (P2).
6. **Casca do console** (testável com `TextReader`/`TextWriter` falsos):
   - Parâmetros de execução (S8, S11):
     ```csharp
     public sealed record AppRunOptions(IReadOnlyList<string> Args, TextReader Input, TextWriter Output, TextWriter Error,
                                        IConfiguration? Configuration = null, string? EnvironmentName = null);
     ```
     `Program.cs` é o único lugar que lê o ambiente do processo: passa `args`, `Console.In/Out/Error` e `EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")`. `AddCommandLine` recebe `[.. options.Args]`.
   - `EnglishTutorApp.RunAsync(AppRunOptions options, CancellationToken ct) : Task<int>`:
     1. Monta o host com `new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, ContentRootPath = AppContext.BaseDirectory, EnvironmentName = options.EnvironmentName ?? Environments.Production })`. Com `DisableDefaults`, `DOTNET_ENVIRONMENT` não é lido sozinho; por isso o nome vem explícito (S11). Fontes: `appsettings.json`, `appsettings.{EnvironmentName}.json` (opcional), variáveis de ambiente, args — ou **só** `options.Configuration` quando preenchida (testes).
     2. `ConsoleLifetimeOptions.SuppressStatusMessages = true` (o `ConsoleLifetime` continua registrado e trata o Ctrl+C chamando `StopApplication()`).
     3. Cria o logger Serilog, chama `host.StartAsync(ct)` (dispara `ValidateOnStart` a partir da T9).
     4. **Token da sessão** (S3, a partir da T11): `using var session = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);` com `lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>()`. `ChatSession.RunAsync(session.Token)`. Assim o Ctrl+C (via `ConsoleLifetime` → `ApplicationStopping`) e o `ct` do chamador cancelam a sessão.
     5. Para o host (`StopAsync`) e o descarta em `finally` (flush do log antes de retornar).
   - Tratamento de saída (P8):
     - `OptionsValidationException` na partida → escreve em `Error` `Invalid configuration:` + uma linha por falha, loga `StartupFailed` e retorna `ExitCodes.InvalidConfiguration` (2).
     - `catch (OperationCanceledException) when (session.Token.IsCancellationRequested)` (ou `ct.IsCancellationRequested` se o cancelamento chegar antes da sessão) → encerra limpo com 0. Cancelamento que não veio desses tokens propaga (S3).
     - Qualquer outra exceção → log `Fatal` `UnexpectedError` (1003) com a exceção, `Error` recebe `An unexpected error occurred. See the log file for details.` e retorna `ExitCodes.UnexpectedError` (1) (S7).
     - `ApplicationStopped` é logado em `finally` com o código de saída, sempre que o logger existir.
   - `ChatSession.RunAsync(CancellationToken ct) : Task<int>` (em `Console/Ui`): recebe `TextReader`, `TextWriter`, `IChatInputHandler` e o texto do estado (provedor canônico e modo); escreve o cabeçalho e lê linhas em laço com prompt `> `. Leitura com `await input.ReadLineAsync(ct).AsTask().WaitAsync(ct)`, para honrar o cancelamento mesmo quando o leitor ignora o token (caso do `Console.In`, que bloqueia) (S3):
     - `/quit` → retorna `ExitCodes.Success` (0);
     - `/help` → lista `LocalCommands.Available`;
     - desconhecido → `Unknown command "<Name>".` + `Available commands: /help, /quit` e continua;
     - linha `string.IsNullOrWhiteSpace` (vazia, só espaços ou tabs) → ignora, não chama o handler e mostra o prompt de novo (RN-CONV05: nada vai ao cérebro; o aviso é US-003) (S7);
     - texto livre → `IChatInputHandler.HandleAsync(text, ct)`;
     - fim da entrada (EOF, ex.: pipe sem `/quit`; no Windows o Ctrl+C também costuma fazer o `ReadLine` devolver `null`) → retorna 0.
   - Cabeçalho (texto exato, testado):
     ```
     EnglishTutor
     Brain: ClaudeCode | Mode: free-conversation
     Type /help for commands or /quit to exit.
     ```
   - `IChatInputHandler` (porta da UI, em `Console/Ui`) com `Task HandleAsync(string text, CancellationToken ct)`. Implementação da US-001: `TutorNotConnectedInputHandler`, que só escreve `The tutor is not connected yet.` (testada por `EnglishTutorAppTests`, S7). Na US-003/004 ela é trocada por uma que delega ao `ConversationService` (R2). Nos testes, `RecordingChatInputHandler` prova que `/quit`, comandos e linhas em branco não chegam ao "cérebro" (P5).
7. **Logs**: Serilog com `ReadFrom.Configuration`, apenas sink de arquivo (`logs/englishtutor-.log`, `rollingInterval: Day`, `CompactJsonFormatter`). Caminho relativo ao diretório atual (no `dotnet run` a partir da raiz, `./logs`). Eventos com `[LoggerMessage]` em `Console/Logging/AppLog.cs`: `ApplicationStarted` (1000, `{Provider}`, `{Mode}`), `ApplicationStopped` (1001, `{ExitCode}`, `{ElapsedMs}` via `TimeProvider`), `StartupFailed` (1002, `{Failures}`), `UnexpectedError` (1003, nível `Critical`/Fatal, com a exceção). Logger por instância, `services.AddSerilog(logger, dispose: true)`; o descarte do host faz o flush antes de `RunAsync` retornar.
8. **CI (cenário opcional — incluído)** (S12): `.github/workflows/ci.yml` em push/PR para `develop` e `main`, `runs-on: ubuntu-latest` (P11):
   - `actions/checkout@v4`; `actions/setup-dotnet@v4` com `global-json-file: global.json`; passo `dotnet --version` para registrar o SDK instalado.
   - Compatibilidade: o `global.json` pede 10.0.100 com `latestFeature`, ou seja, qualquer SDK 10.0.x ≥ 10.0.100 serve. Se o `setup-dotnet` instalar exatamente o 10.0.100 ou um 10.0.x mais novo, os dois satisfazem o `global.json`. O Executor confere no log da primeira execução (D10 só inspeciona o arquivo).
   - `dotnet tool restore`, `dotnet build`, `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults`, ReportGenerator gerando `MarkdownSummaryGithub` em `$GITHUB_STEP_SUMMARY` e `upload-artifact` dos XML de cobertura.
9. **Cobertura consolidada**: os dois projetos de teste geram cobertura do Core; a DoD usa ReportGenerator (ferramenta local) para mesclar e ler a linha do `EnglishTutor.Core` (D3b).
10. **Testes de arquitetura** (S5, S6):
    - O assembly do Core é obtido por `Assembly.Load("EnglishTutor.Core")` (o Core ainda não tem tipos quando a T2 roda; o assembly vazio é copiado para a saída pela `ProjectReference`). Mantido assim depois, para o teste não depender de um tipo específico.
    - Allowlist de `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions` — um nome de assembly referenciado é aceito se:
      1. pertence ao shared framework `Microsoft.NETCore.App` em execução: nomes dos `*.dll` em `Path.GetDirectoryName(typeof(object).Assembly.Location)` (cobre `System.*`, `Microsoft.Win32.*`, `netstandard`, `mscorlib`);
      2. é `Microsoft.Extensions.Options`;
      3. casa com `^Microsoft\.Extensions\.[A-Za-z0-9.]+\.Abstractions$`;
      4. é `Microsoft.Extensions.Primitives` — aceito **só** como dependência transitiva do Options (tipos como `IChangeToken` aparecem em APIs do Options); não pode ser `PackageReference` direto, o que o teste do `.csproj` e o D9 garantem (P10).
    - A mensagem de falha lista os assemblies proibidos e a regra (R1 + ARCHITECTURE §2).
    - `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` lê o `.csproj` (XML) e exige: `PackageReference` ⊆ {`Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions`}, nenhuma `ProjectReference`.

Alternativas descartadas (detalhes no ADR-004):
- `.sln` clássico — verboso; `.slnx` já é suportado pelo SDK 10 e pelos hooks.
- `Provider` como `enum` — o binder falha sem listar os valores aceitos.
- Sink de console do Serilog — mistura log com a conversa do aluno.
- Testes do console executando `dotnet run` — lentos e dependentes de ambiente; ficam só na DoD (D5–D7, D11, D13).
- Comandos locais tratados só no Console — RN-CONV06 é regra de negócio e precisa sobreviver à troca de UI (R2/R4).
- FluentAssertions 8.x — licença comercial.
- Allowlist só por prefixo `System.*` — deixaria de fora `Microsoft.Win32.*`/`netstandard` (falso positivo).
- Ler `DOTNET_ENVIRONMENT` dentro de `EnglishTutorApp` — acopla a classe ao ambiente da máquina; os testes passam o nome via `AppRunOptions`.

### Alterações nos documentos-raiz (já aplicadas)

- `ARCHITECTURE.md` — §2 (dependências do Core com `Microsoft.Extensions.Options`), §3 (`LocalCommand*`, `BrainOptions*`, `BrainProviders`, `EnglishTutorApp`, `Logging/`, `tests/EnglishTutor.Console.Tests`) e §9 (logs só em arquivo): aplicadas pelo orquestrador em 798050a, antes do esqueleto (648f510).
- `CLAUDE.md` — "Regras rápidas": Core pode referenciar "só BCL, `Microsoft.Extensions.*.Abstractions` e `Microsoft.Extensions.Options`": aplicada pelo orquestrador em fa2f0a6 (S2).
- Nenhuma alteração nova é proposta por esta revisão. A aceitação transitiva de `Microsoft.Extensions.Primitives` (P10) não muda a regra de dependências diretas da ARCHITECTURE §2; fica registrada no comentário do teste citando P10.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criado (T1, 648f510) | `global.json` | Fixa o SDK .NET 10 |
| Criado (T1) | `Directory.Build.props` | TFM `net10.0`, Nullable, ImplicitUsings, TreatWarningsAsErrors, AnalysisLevel, LangVersion |
| Criado (T1) | `Directory.Packages.props` | Versões centralizadas dos pacotes (§6) |
| Criado (T1) | `EnglishTutor.slnx` | Solução com os 5 projetos |
| Criado (T1) | `.config/dotnet-tools.json` | Ferramenta local `dotnet-reportgenerator-globaltool` |
| Criado (T1) | `src/EnglishTutor.Core/EnglishTutor.Core.csproj` | Projeto do domínio |
| Criado (T1) | `src/EnglishTutor.Infrastructure/EnglishTutor.Infrastructure.csproj` | Camada de adaptadores (vazia nesta US) |
| Criado (T1) | `src/EnglishTutor.Console/EnglishTutor.Console.csproj` | Executável; já copia `appsettings.json` para a saída |
| Criado (T1) | `tests/EnglishTutor.Console.Tests/EnglishTutor.Console.Tests.csproj` | Testes da casca do console |
| Alterar (T6) | `tests/EnglishTutor.Core.Tests/EnglishTutor.Core.Tests.csproj` | Criado na T1; ganha `PackageReference` a `Microsoft.Extensions.DependencyInjection` (S9) |
| Alterar (T8) | `src/EnglishTutor.Console/Program.cs` | Existe desde a T1 (`return 0;`); passa a repassar `args`, `Console.In/Out/Error` e `DOTNET_ENVIRONMENT` para `EnglishTutorApp` (S14) |
| Criar | `src/EnglishTutor.Core/Brain/BrainOptions.cs` | Options da seção `Brain` |
| Criar | `src/EnglishTutor.Core/Brain/BrainProviders.cs` | Nomes canônicos e `TryGetCanonical` |
| Criar | `src/EnglishTutor.Core/Brain/BrainOptionsValidator.cs` | `IValidateOptions<BrainOptions>` com mensagens claras |
| Criar | `src/EnglishTutor.Core/Conversation/TutorMode.cs` | Modos e modo padrão (RN-CONV01) |
| Criar | `src/EnglishTutor.Core/Conversation/LocalCommand.cs` | `LocalCommand`, `LocalCommandKind`, `LocalCommandInfo` |
| Criar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs` | Lista de comandos disponíveis |
| Criar | `src/EnglishTutor.Core/Conversation/LocalCommandParser.cs` | Reconhecimento de comandos locais (RN-CONV06) |
| Criar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | `AddEnglishTutorCore()` |
| Criar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Host, configuração, Serilog, token da sessão, partida/parada, códigos de saída |
| Criar | `src/EnglishTutor.Console/AppRunOptions.cs` | Parâmetros de execução (testáveis) |
| Criar | `src/EnglishTutor.Console/ExitCodes.cs` | `Success = 0`, `UnexpectedError = 1`, `InvalidConfiguration = 2` |
| Criar | `src/EnglishTutor.Console/Logging/AppLog.cs` | `[LoggerMessage]` com EventIds 1000–1003 |
| Criar | `src/EnglishTutor.Console/Ui/ChatSession.cs` | Laço de entrada, cabeçalho, despacho de comandos |
| Criar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | Textos exibidos (cabeçalho, ajuda, desconhecido, não conectado, erro inesperado) |
| Criar | `src/EnglishTutor.Console/Ui/IChatInputHandler.cs` | Porta da UI para texto livre |
| Criar | `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` | Implementação provisória (sem cérebro) |
| Criar | `src/EnglishTutor.Console/appsettings.json` | `Brain:Provider = "ClaudeCode"` e seção `Serilog` |
| Criar (em andamento, não commitado) | `tests/EnglishTutor.Core.Tests/Support/RepositoryRoot.cs` | Localiza a raiz (pasta com `EnglishTutor.slnx`); entra no commit da T2 |
| Criar | `tests/EnglishTutor.Core.Tests/Architecture/CoreDependencyTests.cs` | Regra R1 por reflexão e por `.csproj` |
| Criar | `tests/EnglishTutor.Core.Tests/Architecture/BuildSettingsTests.cs` | Guarda de `TreatWarningsAsErrors`/`Nullable` |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/BrainProvidersTests.cs` | Normalização para o nome canônico (S1) |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/BrainOptionsValidatorTests.cs` | Validação do provedor |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/TutorModeTests.cs` | Modo padrão |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/LocalCommandParserTests.cs` | Comandos locais |
| Criar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | Registro do validador |
| Criar | `tests/EnglishTutor.Console.Tests/Support/TestConfiguration.cs` | `IConfiguration` em memória (provider, caminho de log em pasta temporária) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/RecordingChatInputHandler.cs` | Fake que registra o texto encaminhado |
| Criar | `tests/EnglishTutor.Console.Tests/Support/PendingTextReader.cs` | Leitor cujo `ReadLineAsync` nunca completa e sinaliza quando foi chamado (teste de cancelamento) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/ThrowingTextWriter.cs` | Escritor que lança `IOException` (teste de erro inesperado) |
| Criar | `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` | Comandos, linhas em branco e encaminhamento |
| Criar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | Partida, cabeçalho, códigos de saída, configuração inválida, erro inesperado, cancelamento, log |
| Criar | `.github/workflows/ci.yml` | Build, testes sem LiveBrain, cobertura |
| Existente (Aceito) | `docs/adr/ADR-004-fundacao-solucao-host-config-logs-testes.md` | Sem alteração nesta revisão |
| Alterado (798050a) | `ARCHITECTURE.md` | §2, §3, §9 |
| Alterado (fa2f0a6) | `CLAUDE.md` | Regra de dependências do Core alinhada (S2) |

## 4. Mapa cenário BDD → testes

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| solução compila sem avisos | Core.Tests | `BuildSettingsTests` | `DirectoryBuildProps_Always_EnablesTreatWarningsAsErrorsAndNullable` | Arquitetura (leitura de arquivo) |
| solução compila sem avisos | — | — | DoD D1 (`dotnet build` 0 warnings) | Verificação de comando |
| Core não depende de pacotes externos | Core.Tests | `CoreDependencyTests` | `CoreAssembly_References_DoNotIncludeInfrastructureOrConsole` | Arquitetura (reflexão) |
| Core não depende de pacotes externos | Core.Tests | `CoreDependencyTests` | `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions` (allowlist do §2.10) | Arquitetura (reflexão) |
| Core não depende de pacotes externos | Core.Tests | `CoreDependencyTests` | `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` | Arquitetura (`.csproj`) |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainProvidersTests` | `TryGetCanonical_WhenCaseDiffers_ReturnsCanonicalName` (`[Theory]`: `claudecode`→`ClaudeCode`, `OLLAMA`→`Ollama`, `ClaudeCode`→`ClaudeCode`) | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainProvidersTests` | `TryGetCanonical_WhenValueIsUnknownOrBlank_ReturnsFalse` (`[Theory]`: `Inexistente`, `""`, `"   "`, `null`) | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainOptionsValidatorTests` | `Validate_WhenProviderIsUnknown_FailsNamingValueAndAcceptedProviders` | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainOptionsValidatorTests` | `Validate_WhenProviderIsEmpty_FailsListingAcceptedProviders` | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `BrainOptionsValidatorTests` | `Validate_WhenProviderIsAccepted_Succeeds` (`[Theory]`: `ClaudeCode`, `Ollama`, `claudecode`) | Unidade |
| configuração do cérebro é validada na inicialização | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_Always_RegistersBrainOptionsValidator` (via `BuildServiceProvider`: resolve `IValidateOptions<BrainOptions>` e `IOptions<BrainOptions>.Value` com provedor inválido lança `OptionsValidationException`) | Unidade |
| configuração do cérebro é validada na inicialização | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenProviderIsInvalid_ReturnsNonZeroAndExplainsAcceptedProviders` | Componente (em processo) |
| aplicação inicia e mostra o estado | Core.Tests | `TutorModeTests` | `Default_Always_IsFreeConversation` | Unidade |
| aplicação inicia e mostra o estado | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WithValidConfiguration_ShowsHeaderWithProviderModeAndCommands` | Componente |
| aplicação inicia e mostra o estado (P4) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenProviderCaseDiffers_ShowsCanonicalProviderInHeader` (`claudecode` → `Brain: ClaudeCode`) | Componente |
| comando local de saída | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputIsQuit_ReturnsQuitCommand` | Unidade |
| comando local de saída | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenCommandHasDifferentCaseOrSurroundingSpaces_RecognizesIt` (`[Theory]`: `/QUIT`, `  /quit  `, `/Help`) | Unidade |
| comando local de saída | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesQuit_ReturnsZeroAndForwardsNothing` | Componente |
| comando local de saída | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenUserTypesQuit_ExitsWithZero` | Componente |
| comando local desconhecido | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenSlashCommandIsUnknown_ReturnsUnknownCommand` (`[Theory]`: `/xyz`→`Name "/xyz"`; `/`→`Name "/"`; `/mode interview`→`Name "/mode"`, `Arguments "interview"`) | Unidade |
| comando local desconhecido | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputDoesNotStartWithSlash_ReturnsFalse` | Unidade |
| comando local desconhecido | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands` | Componente |
| comando local desconhecido | Console.Tests | `ChatSessionTests` | `RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` | Componente |
| (apoio: `/help` citado no cabeçalho) | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputIsHelp_ReturnsHelpCommand` | Unidade |
| (apoio: `/help` citado no cabeçalho) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesHelp_ListsAvailableCommands` | Componente |
| (apoio: texto livre não é comando) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesText_ForwardsItToInputHandler` | Componente |
| (apoio: linha em branco não vai ao cérebro, RN-CONV05) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenLineIsBlank_IgnoresItAndForwardsNothing` (`[Theory]`: `""`, `"   "`, `"\t"`) | Componente |
| (apoio: pipe sem `/quit`, D5/D11) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenInputEnds_ReturnsZero` | Componente |
| (apoio: implementação provisória sem cérebro, D11) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenUserTypesText_ShowsTutorNotConnected` (entrada `"hello"`) | Componente |
| (apoio: erro inesperado, P8) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenUnexpectedErrorOccurs_ReturnsOneAndLogsUnexpectedError` (`ThrowingTextWriter` na saída; código 1, mensagem curta em `Error`, evento `UnexpectedError` no arquivo) | Componente |
| (apoio: Ctrl+C encerra limpo, P8 e ARCHITECTURE §9) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenCancelledWhileWaitingForInput_ReturnsZero` (`PendingTextReader`; cancela o `ct` depois que a leitura começou) | Componente |
| (apoio: Ctrl+C encerra limpo) | — | — | DoD D13 (Ctrl+C real no executável) | Verificação manual obrigatória |
| logs estruturados | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenAppStops_WritesStructuredStartAndStopEventsToLogFile` | Componente (arquivo em pasta temporária) |
| integração contínua (opcional) | — | — | DoD D10 (inspeção do `ci.yml`) + primeira execução no GitHub após push feito pelo usuário | Verificação manual registrada no review |

Total previsto: **31 métodos de teste** — 16 no Core.Tests e 15 no Console.Tests. Seis são `[Theory]` (19 casos de dados) e 25 são `[Fact]`, totalizando **44 casos executados** (Core.Tests: 11 `[Fact]` + 16 casos = 27; Console.Tests: 14 `[Fact]` + 3 casos = 17).

Conferência por classe: `CoreDependencyTests` 3, `BuildSettingsTests` 1, `BrainProvidersTests` 2 (7 casos), `BrainOptionsValidatorTests` 3 (1 Theory com 3 casos), `CoreServiceCollectionExtensionsTests` 1, `TutorModeTests` 1, `LocalCommandParserTests` 5 (2 Theory com 3 + 3 casos); `ChatSessionTests` 7 (1 Theory com 3 casos), `EnglishTutorAppTests` 8.

## 5. Tarefas (ordem de execução TDD)

Pré-condição do Executor: `dotnet --list-sdks` mostra um SDK 10.0.x. Satisfeita: o orquestrador registrou o SDK 10.0.402.

- [x] T1 — Esqueleto: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `EnglishTutor.slnx`, os 3 projetos de `src/` e os 2 de `tests/` (vazios, com referências entre si e pacotes da §6), `.config/dotnet-tools.json`, `Program.cs` com `return 0;`. `dotnet build` com 0 warnings e `dotnet test` executando (0 testes). (commit 648f510: `chore(infra): scaffold solution, build props and test projects`)
- [x] T2 — Testes de guarda de arquitetura e build: `RepositoryRoot` (já existe, não commitado), `CoreDependencyTests` (Core via `Assembly.Load("EnglishTutor.Core")`, allowlist do §2.10), `BuildSettingsTests`. Passam com o Core vazio. **Red de demonstração** (reverter sem commitar e registrar nas Evidências qual teste falhou e a mensagem):
  - (a) `<PackageReference Include="Serilog.Sinks.File" />` no Core (já versionado no CPM, sem NU1010) → falha esperada **só** em `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` (referência não usada não aparece em `GetReferencedAssemblies`);
  - (b) mantendo (a), um arquivo temporário no Core que usa `typeof(Serilog.ILogger)` → falha também em `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions`;
  - (c) `TreatWarningsAsErrors=false` no `Directory.Build.props` → falha em `BuildSettingsTests`;
  - `CoreAssembly_References_DoNotIncludeInfrastructureOrConsole` não tem Red demonstrável: Core → Infrastructure cria referência circular e o build falha antes. Registrar isso; o teste fica como guarda explícita de R1 com mensagem própria.
  (commit: `test(core): guard core dependency rule and build settings`)
- [x] T3 — Red: `TutorModeTests`. Green: `TutorMode`. (commits: `test(core): default tutor mode` + `feat(core): add tutor modes with free-conversation default`)
- [x] T4 — Red: `LocalCommandParserTests` (quit, help, desconhecido incluindo `"/"` e `"/mode interview"`, sem barra, caixa/espaços). Green: `LocalCommand*`, `LocalCommands`, `LocalCommandParser` com `[NotNullWhen(true)]` no `out`. (commits: `test(core): local command parsing` + `feat(core): parse local slash commands`)
- [x] T5 — Red: `BrainProvidersTests`. Green: `BrainProviders` (constantes, `All`, `TryGetCanonical`). (commits: `test(core): canonical brain provider names` + `feat(core): add brain provider names with canonical lookup`)
- [x] T6 — Red: `BrainOptionsValidatorTests` + `CoreServiceCollectionExtensionsTests` (adiciona `Microsoft.Extensions.DependencyInjection` ao Core.Tests). Green: `BrainOptions`, `BrainOptionsValidator` (usa `TryGetCanonical`), `AddEnglishTutorCore`. Refactor: mensagens montadas a partir de `BrainProviders.All`. (commits: `test(core): brain provider validation` + `feat(core): validate brain provider options`)
- [ ] T7 — Red: `ChatSessionTests` (quit, desconhecido, continuidade, help, texto livre, linhas em branco, EOF) com `RecordingChatInputHandler` e `StringReader`/`StringWriter`. Green: `ChatSession` (leitura com `.AsTask().WaitAsync(ct)`), `ConsoleText`, `IChatInputHandler`, `ExitCodes`. (commits: `test(console): chat session local commands` + `feat(console): add chat session shell`)
- [ ] T8 — Red: `EnglishTutorAppTests` de cabeçalho, cabeçalho com `claudecode`, `/quit` → 0 e `"hello"` → `The tutor is not connected yet.`. Green: `AppRunOptions`, `EnglishTutorApp` (host com `EnvironmentName` explícito, fontes de configuração, `TutorNotConnectedInputHandler`, cabeçalho com `TryGetCanonical`), alteração do `Program.cs`, `appsettings.json`. **Restrições (S4, S3):** nesta tarefa só `AddOptions<BrainOptions>().Bind(...)`; **sem** `ValidateOnStart` e **sem** `AddEnglishTutorCore` (entram na T9); o `ct` é repassado direto ao `ChatSession`, sem token ligado e sem `catch` de cancelamento ou genérico (entram na T11). (commits: `test(console): startup header and quit exit code` + `feat(console): compose host and run chat session`)
- [ ] T9 — Red: `RunAsync_WhenProviderIsInvalid_...` (deve falhar: sem `ValidateOnStart`, a sessão abre). Green: `AddEnglishTutorCore()` + `.ValidateOnStart()`, captura de `OptionsValidationException`, `ExitCodes.InvalidConfiguration`, mensagem em `Error`. (commits: `test(console): invalid brain provider at startup` + `feat(console): fail fast on invalid configuration`)
- [ ] T10 — Red: `RunAsync_WhenAppStops_WritesStructuredStartAndStopEventsToLogFile` (lê o arquivo CLEF, faz parse JSON por linha e procura `EventId.Name` = `ApplicationStarted` com `Provider`/`Mode` e `ApplicationStopped` com `ExitCode`). Green: Serilog (`ReadFrom.Configuration`, sink de arquivo, `CompactJsonFormatter`), `AppLog` (1000–1002), `TimeProvider` para `ElapsedMs`. (commits: `test(console): structured start and stop log events` + `feat(console): add serilog file logging`)
- [ ] T11 — Red: `RunAsync_WhenUnexpectedErrorOccurs_ReturnsOneAndLogsUnexpectedError` (`ThrowingTextWriter`) e `RunAsync_WhenCancelledWhileWaitingForInput_ReturnsZero` (`PendingTextReader`; o teste espera o sinal de leitura iniciada e então cancela o `CancellationTokenSource` passado a `RunAsync`). Ambos falham antes: a exceção e o `OperationCanceledException` escapam de `RunAsync`. Green: token da sessão ligado a `ApplicationStopping`, `catch (OperationCanceledException) when (...IsCancellationRequested)` → 0, `catch` genérico → log `UnexpectedError` (1003) + mensagem curta + `ExitCodes.UnexpectedError`, `ApplicationStopped` em `finally`. (commits: `test(console): unexpected error and cancellation exit codes` + `feat(console): handle cancellation and unexpected errors on exit`)
- [ ] T12 — CI: `.github/workflows/ci.yml` (`ubuntu-latest`, `setup-dotnet@v4` com `global-json-file`, `dotnet --version`, build, testes sem LiveBrain com cobertura, ReportGenerator no step summary, upload do artefato). (commit: `ci(infra): add build and test workflow with coverage`)
- [ ] T13 — DoD: rodar D1–D12, pedir ao usuário o D13 (Ctrl+C interativo) e registrar o resultado, colar tudo em §11 e marcar as tarefas. (commit: `docs(ai): add DoD evidence for US-001`)

## 6. Pacotes NuGet

Versões já fixadas em `Directory.Packages.props` (T1, 648f510); o Executor só registra nas Evidências se alguma mudar.

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Options (10.0.13) | Core | `IValidateOptions<T>`, `ValidateOptionsResult` (permitido por R1) |
| Microsoft.Extensions.DependencyInjection.Abstractions (10.0.13) | Core | `AddEnglishTutorCore(IServiceCollection)` |
| Microsoft.Extensions.Hosting (10.0.13) | Console | Generic Host, configuração, DI, `ConsoleLifetime` (Ctrl+C) |
| Microsoft.Extensions.Options.ConfigurationExtensions (10.0.13) | Console | `Bind` da seção `Brain` |
| Serilog.Extensions.Hosting (10.0.0) | Console | `services.AddSerilog(logger, dispose: true)` |
| Serilog.Settings.Configuration (10.0.1) | Console | `ReadFrom.Configuration` (caminho do log substituível nos testes) |
| Serilog.Sinks.File (7.0.0) | Console | Arquivo em `logs/` |
| Serilog.Formatting.Compact (3.0.0) | Console | Formato estruturado CLEF |
| xunit.v3 (3.2.2) | Core.Tests, Console.Tests | Framework de testes |
| xunit.runner.visualstudio (3.1.5) | Core.Tests, Console.Tests | Execução via `dotnet test` (VSTest) |
| Microsoft.NET.Test.Sdk (18.11.0) | Core.Tests, Console.Tests | Host de testes VSTest |
| FluentAssertions (7.2.2) | Core.Tests, Console.Tests | Asserções; 7.x por licença Apache 2.0 (P6) |
| NSubstitute (6.2.0) | Core.Tests, Console.Tests | Dublês simples (padrão do projeto; pode não ser usado nesta US) |
| coverlet.collector (10.1.0) | Core.Tests, Console.Tests | `--collect:"XPlat Code Coverage"` |
| Microsoft.Extensions.DependencyInjection (10.0.13) | Core.Tests (referência explícita, T6) | `BuildServiceProvider` em `CoreServiceCollectionExtensionsTests` (S9). No Console.Tests chega de forma transitiva via Hosting |
| Microsoft.Extensions.Configuration (Memory) | Console.Tests | `IConfiguration` em memória (transitivo via Hosting pela referência ao Console; sem `PackageVersion` próprio) |
| dotnet-reportgenerator-globaltool (ferramenta local) | `.config/dotnet-tools.json` | Mesclar cobertura (D3b) e resumo no CI |

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Variáveis de ambiente da máquina (ex.: `Brain__Provider` deixado pelo D6) contaminando testes | Testes passam `IConfiguration` própria e o host usa `DisableDefaults` (sem env vars nesse caminho); D6 termina com `Remove-Item Env:Brain__Provider` |
| `appsettings.{Environment}.json` nunca carregado porque `DisableDefaults` ignora `DOTNET_ENVIRONMENT` (S11) | `Program.cs` lê `DOTNET_ENVIRONMENT` e passa em `AppRunOptions.EnvironmentName`; padrão `Production` |
| Arquivo de log travado/não descarregado ao ler no teste | Logger descartado com o host antes de `RunAsync` retornar; cada teste usa pasta temporária própria |
| xUnit v3: analisador xUnit1051 vira erro com `TreatWarningsAsErrors` | Passar `TestContext.Current.CancellationToken` em todas as chamadas assíncronas |
| Ctrl+C não cancela a sessão (o `ConsoleLifetime` só chama `StopApplication()`; `Console.In.ReadLineAsync` bloqueia) (S3) | Token da sessão ligado a `ApplicationStopping`; leitura com `.AsTask().WaitAsync(ct)`; teste automatizado do cancelamento (T11) e **D13 obrigatório** com Ctrl+C real |
| `catch (OperationCanceledException)` lido como "catch que engole" | Filtro `when (token.IsCancellationRequested)` nos tokens da aplicação; outros cancelamentos propagam |
| Vários hosts em paralelo nos testes registrando `ConsoleLifetime` (`Console.CancelKeyPress`) | Cada host é descartado ao fim de `RunAsync`, o que desfaz o registro; `SuppressStatusMessages = true` |
| Allowlist do Core falhar em falso positivo por assembly do framework | Lista derivada da pasta do shared framework em execução, não de prefixo; mensagem lista o que foi rejeitado |
| Cobertura do Core dividida entre dois projetos de teste | ReportGenerator mescla (D3b); Core coberto principalmente por Core.Tests |
| Testes de arquitetura dependentes do caminho do repositório | `RepositoryRoot` sobe a partir de `AppContext.BaseDirectory` até achar `EnglishTutor.slnx`, com mensagem clara se não achar |
| CI não verificável localmente (sem `gh`, sem push autorizado); `setup-dotnet` pode resolver o `global.json` de forma diferente da máquina local (S12) | Validação por inspeção (D10); passo `dotnet --version` no workflow; qualquer 10.0.x ≥ 10.0.100 satisfaz o `global.json`; primeira execução após push feito pelo usuário |
| Runner Linux no CI esconder problema exclusivo do Windows | Nesta US não há código específico de plataforma; rever o runner quando entrar adaptador só para Windows (P11) |
| `dotnet format --verify-no-changes` falhar por fim de linha (`.editorconfig` exige LF no Windows) | `.gitattributes` existe; rodar `dotnet format` antes do D4 |

## 8. Fora de escopo

- Conversa com qualquer cérebro, `ITutorBrain`, `BrainRequest`, `TutorReply` (US-002/US-004).
- Aviso de entrada vazia (RN-CONV05) e `/mode` (US-003); `/speed` (Sprint 2). Nesta US, `/mode` e `/speed` respondem como comando desconhecido e linhas em branco são ignoradas em silêncio.
- `Brain:FallbackProvider` e subseções `Brain:ClaudeCode` / `Brain:Ollama` e sua validação (US-004, US-006, US-007).
- `Conversation:MaxTurnsInWindow` (US-003).
- `EnglishTutor.Infrastructure.Tests` e extensão `AddEnglishTutorInfrastructure` (US-002/US-004).
- `appsettings.Local.json` / segredos locais; teste automatizado do carregamento de `appsettings.{Environment}.json`.
- Sink de console do Serilog (P7).

## 9. Perguntas em aberto

P1–P9 foram respondidas em 2026-10-17 (ver "Respostas do usuário" abaixo). Novas, surgidas da revisão:

- **P10 — `Microsoft.Extensions.Primitives` no Core** (S6). É dependência transitiva do `Microsoft.Extensions.Options` e pode aparecer em `GetReferencedAssemblies` se o Core usar tipos como `IChangeToken`. **Padrão proposto:** aceitar no teste de reflexão só como transitiva; proibir como `PackageReference` direto (teste do `.csproj` e D9); sem mudança na ARCHITECTURE §2, que trata de dependências diretas.
- **P11 — Runner do CI** (S12). **Padrão proposto:** `ubuntu-latest` (mais rápido; nada específico de Windows nesta US). Rever quando entrar um adaptador só para Windows (ex.: TTS do sistema). Alternativa: `windows-latest`, igual à máquina de desenvolvimento.

Perguntas anteriores (respondidas):

- **P1 — Novo projeto `tests/EnglishTutor.Console.Tests`.** Criar e atualizar ARCHITECTURE §3 (feito em 798050a).
- **P2 — Lista exibida em `/help` e no "Unknown command".** Listar só os comandos implementados.
- **P3 — "modo padrão" vem de configuração?** Não; fixo pela RN-CONV01.
- **P4 — `Brain:Provider` diferencia maiúsculas?** Não; o cabeçalho mostra o nome canônico (agora com dono e teste: `BrainProviders.TryGetCanonical`, §2.3).
- **P5 — "nada é enviado a nenhum cérebro" sem cérebro existente.** Porta `IChatInputHandler` com fake nos testes.
- **P6 — FluentAssertions 7.x.**
- **P7 — Logs só em arquivo** (ARCHITECTURE §9 atualizada em 798050a).
- **P8 — Códigos de saída.** `0` sucesso (inclui `/quit`, EOF e Ctrl+C), `1` erro inesperado, `2` configuração inválida (agora todos com teste: §4).
- **P9 — Cenário de CI (opcional).** Incluir agora (T12).

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell.

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes (31 métodos, 44 casos: 27 no Core.Tests e 17 no Console.Tests), em < 30 s, sem rede nem GPU |
| D3 | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` (apagar `TestResults/` antes) | gera `coverage.cobertura.xml` para os dois projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `"/quit" \| dotnet run --project src/EnglishTutor.Console; $LASTEXITCODE` | cabeçalho com `Brain: ClaudeCode \| Mode: free-conversation` e as dicas `/help` e `/quit`; `0` |
| D6 | `$env:Brain__Provider="Inexistente"; dotnet run --project src/EnglishTutor.Console; $LASTEXITCODE; Remove-Item Env:Brain__Provider` | código `2`; mensagem cita `'Inexistente'` e `Accepted values: ClaudeCode, Ollama` |
| D7 | `Get-ChildItem logs; Get-Content (Get-ChildItem logs/*.log \| Sort-Object LastWriteTime \| Select-Object -Last 1)` após D5 | arquivo `englishtutor-AAAAMMDD.log` com linhas JSON contendo `ApplicationStarted` e `ApplicationStopped` |
| D8 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"` (incluído em D2) | 3 testes verdes |
| D9 | `dotnet list src/EnglishTutor.Core package` e `dotnet list src/EnglishTutor.Core reference` | só `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`; nenhuma referência de projeto |
| D10 | `Get-Content .github/workflows/ci.yml` | gatilhos push/PR em `develop` e `main`; `runs-on: ubuntu-latest`; `setup-dotnet` com `global-json-file: global.json`; passo `dotnet --version`; build, `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` e publicação da cobertura |
| D11 | `"hello" \| dotnet run --project src/EnglishTutor.Console; $LASTEXITCODE` | mostra `The tutor is not connected yet.`; termina com `0` ao fim da entrada |
| D12 | `git status --short` após D5–D7, D11 e D13 | nenhum arquivo de `logs/`, `TestResults/` ou `coverage/` aparece (ignorados) |
| D13 | **Manual, obrigatório** (exige terminal interativo; o usuário executa e o Executor registra): `dotnet build src/EnglishTutor.Console; & ./src/EnglishTutor.Console/bin/Debug/net10.0/EnglishTutor.Console.exe`, esperar o prompt `> ` e pressionar Ctrl+C; depois `$LASTEXITCODE` e a última linha do log mais recente | processo termina em até ~2 s, sem stack trace no console; `$LASTEXITCODE` = `0`; última linha do log é `ApplicationStopped` com `ExitCode` 0 |

E também (DoD comum): todo cenário BDD com teste escrito antes do código (exceto os testes de guarda da T2, com Red demonstrado e registrado); `review.md` sem bloqueantes; `docs/us/US-001.md` criado; ADR-004 aceito (feito) e `ARCHITECTURE.md`/`CLAUDE.md` atualizados (feito em 798050a e fa2f0a6); merge em `develop`.

### Respostas do usuário (2026-10-17)

Aprovadas no chat as respostas padrão de P1–P9 e o ADR-004 (Aceito): P1 criar `tests/EnglishTutor.Console.Tests`; P2 listar só os comandos implementados; P3 modo padrão fixo (RN-CONV01), sem chave de configuração; P4 `Brain:Provider` sem diferenciar maiúsculas; P5 porta `IChatInputHandler` com fake nos testes; P6 FluentAssertions 7.x; P7 logs só em arquivo; P8 códigos de saída 0/1/2; P9 incluir o CI agora.

Nota do orquestrador: o SDK 10.0.402 já está instalado (`dotnet --list-sdks`); a pré-condição do §5 está satisfeita.

Reaprovação (2026-10-17), no chat, do plano revisado com S1–S14 (commit 250b065) e das respostas padrão: P10 aceitar `Microsoft.Extensions.Primitives` no Core só como dependência transitiva do Options (proibido como `PackageReference` direto); P11 CI em `ubuntu-latest`, revendo quando entrar adaptador só para Windows.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

### T2 — Red de demonstração (2026-10-17, revertido sem commit)

Com o Core vazio, os 4 testes de guarda passam. Cada passo foi aplicado, executado com `dotnet test tests/EnglishTutor.Core.Tests` e revertido:

- (a) `<PackageReference Include="Serilog.Sinks.File" />` no Core → 1 falha, só `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes`: `Expected packages to be a subset of {"Microsoft.Extensions.Options", "Microsoft.Extensions.DependencyInjection.Abstractions"} ... but items {"Serilog.Sinks.File"} are not part of the superset.`
- (b) (a) + `src/EnglishTutor.Core/RedDemo.cs` com `typeof(Serilog.ILogger)` → 2 falhas: a de (a) e `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions`: `Expected forbidden to be empty because these assemblies are not allowed in the Core: [Serilog]. R1 (ARCHITECTURE §2): ...`
- (c) `TreatWarningsAsErrors=false` no `Directory.Build.props` → 1 falha, `DirectoryBuildProps_Always_EnablesTreatWarningsAsErrorsAndNullable`: `Expected PropertyValue(props, "TreatWarningsAsErrors") to be "true" ... but "false" ...`
- `CoreAssembly_References_DoNotIncludeInfrastructureOrConsole`: sem Red demonstrável (Core → Infrastructure/Console gera referência circular e o build falha antes do teste); fica como guarda explícita de R1.

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-17 — Incorporar as sugestões S1–S14 da revisão do plano

Pedido do usuário no chat (transcrito pelo orquestrador): incorporar ao plano **todas** as sugestões S1 a S14 de [plan-review.md](plan-review.md); o plano volta para reaprovação. Contexto: a T1 já está commitada (648f510) e há `tests/EnglishTutor.Core.Tests/Support/RepositoryRoot.cs` não commitado (início da T2); o plano revisado deve refletir esse estado.

→ Incorporado. Status voltou para "Aguardando aprovação". O §3 e o §5 mostram a T1 como feita e o `RepositoryRoot.cs` como em andamento na T2. As tarefas passaram de 11 para 13; os métodos de teste, de 24 para 31 (44 casos); a DoD, de 13 para 14 itens (D13 novo). Nenhum item exigiu editar o ADR-004. Duas decisões novas aguardam o usuário: P10 e P11 (§9).

- **S1** — Dar dono e teste à normalização de `Brain:Provider` para o nome canônico (P4): ex. `BrainProviders.TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical)`, `BrainProvidersTests.TryGetCanonical_WhenCaseDiffers_ReturnsCanonicalName`, caso de cabeçalho com `claudecode` em `EnglishTutorAppTests`; incluir `BrainProvidersTests.cs` no §3.
  → Incorporado em §2.3: `BrainProviders.TryGetCanonical` é o único dono (`OrdinalIgnoreCase`, sem `Trim`). O validador e o cabeçalho do `EnglishTutorApp` o chamam; `IPostConfigureOptions` foi descartado. §4: `BrainProvidersTests` com 2 `[Theory]` (`TryGetCanonical_WhenCaseDiffers_ReturnsCanonicalName` e `TryGetCanonical_WhenValueIsUnknownOrBlank_ReturnsFalse`) e `EnglishTutorAppTests.RunAsync_WhenProviderCaseDiffers_ShowsCanonicalProviderInHeader`. §3: `BrainProvidersTests.cs`. §5: nova T5 só para `BrainProviders`; o caso de cabeçalho entra na T8.
- **S2** — O `CLAUDE.md` foi alinhado pelo orquestrador (Core: só BCL, `Microsoft.Extensions.*.Abstractions` e `Microsoft.Extensions.Options`). Registrar no §2 do plano, junto das alterações da ARCHITECTURE, como já aplicado.
  → Incorporado em §2, subseção "Alterações nos documentos-raiz (já aplicadas)": ARCHITECTURE (798050a) e CLAUDE.md (fa2f0a6). Também em §3 (linhas "Alterado") e na DoD comum do §10.
- **S3** — Definir no §2.6 de onde vem o token do `ChatSession` (ex.: `CreateLinkedTokenSource(ct, lifetime.ApplicationStopping)`); restringir o `catch (OperationCanceledException)` a `when (token.IsCancellationRequested)`; tornar a verificação manual do Ctrl+C item obrigatório da DoD (D13).
  → Incorporado em §2.6, item 4 (token ligado a `ApplicationStopping`) e "Tratamento de saída" (`catch ... when (...IsCancellationRequested)`). Acrescentei leitura com `.AsTask().WaitAsync(ct)` no `ChatSession`, porque `Console.In.ReadLineAsync` ignora o token. Teste automatizado novo: `RunAsync_WhenCancelledWhileWaitingForInput_ReturnsZero` (§4, T11, com `PendingTextReader`). §10: D13 manual e obrigatório, executado pelo usuário porque exige terminal interativo. §7: risco reescrito.
- **S4** — Deixar explícito que a T7 faz só `Bind` e que `ValidateOnStart` + `AddEnglishTutorCore` entram na T8, para preservar o Red da T8.
  → Incorporado em §5. Com a renumeração, as tarefas viraram T8 (composição: só `Bind`, sem `ValidateOnStart`, sem `AddEnglishTutorCore`, sem `catch` de cancelamento ou genérico) e T9 (o Red de configuração inválida, que adiciona `ValidateOnStart` + `AddEnglishTutorCore`). Também em §2.3. `AddEnglishTutorCore` continua sendo criado na T6 (Core); o Console só passa a chamá-lo na T9.
- **S5** — T2 com Core vazio: usar `Assembly.Load("EnglishTutor.Core")` (ou mover a T2 para depois da T3). Red de demonstração com pacote já versionado no CPM (ex.: `Serilog.Sinks.File`) e registrar qual teste falhou.
  → Incorporado em §2.10 (`Assembly.Load`, mantido depois) e §5 T2. O Red de demonstração tem 3 passos, cada um com o teste que deve falhar: (a) `Serilog.Sinks.File` no `.csproj` → só o teste de `.csproj`; (b) uso de `typeof(Serilog.ILogger)` → também o teste de reflexão; (c) `TreatWarningsAsErrors=false` → `BuildSettingsTests`. Registrei que `DoNotIncludeInfrastructureOrConsole` não tem Red demonstrável: a referência circular quebra o build antes do teste.
- **S6** — Definir a allowlist de `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions`: shared framework `Microsoft.NETCore.App` + `Microsoft.Extensions.Options` + `Microsoft.Extensions.*.Abstractions`; decidir sobre `Microsoft.Extensions.Primitives`.
  → Incorporado em §2.10. São 4 critérios: DLLs da pasta do shared framework em execução, `Options`, regex `*.Abstractions` e `Primitives` só como transitiva. O prefixo `System.*` foi descartado (§2, alternativas). A decisão sobre `Primitives` está em §9 **P10** (padrão: aceitar como transitiva e proibir como direta) e aguarda o usuário. §7: risco de falso positivo.
- **S7** — Mapear testes para: `TutorNotConnectedInputHandler` (ex.: entrada `"hello"` em `EnglishTutorAppTests`), caminho `UnexpectedError` → código 1 com evento 1003, e linha só com espaços (`IsNullOrWhiteSpace`, não vai ao handler).
  → Incorporado em §4 com 3 métodos novos:
    - `RunAsync_WhenUserTypesText_ShowsTutorNotConnected` (T8);
    - `RunAsync_WhenUnexpectedErrorOccurs_ReturnsOneAndLogsUnexpectedError`, com `ThrowingTextWriter` e sem gancho novo em produção (T11);
    - `RunAsync_WhenLineIsBlank_IgnoresItAndForwardsNothing`, `[Theory]` com `""`, `"   "`, `"\t"` (T7).

    §2.6 define `string.IsNullOrWhiteSpace` → ignora e não chama o handler, além do texto exato da mensagem de erro inesperado. §3 inclui `ThrowingTextWriter.cs`.
- **S8** — `AppRunOptions.Args` como `IReadOnlyList<string>` em vez de `string[]`.
  → Incorporado em §2.6 (assinatura nova; `AddCommandLine([.. options.Args])`).
- **S9** — Resolver a divergência de `Microsoft.Extensions.DependencyInjection` no `Directory.Packages.props`: incluir no §6 com uso justificado ou remover.
  → Incorporado: mantido e justificado em §6, com referência explícita no Core.Tests (T6) para `BuildServiceProvider`. `CoreServiceCollectionExtensionsTests` passa a provar o comportamento (resolve o validador e `IOptions<BrainOptions>.Value` inválido lança `OptionsValidationException`) em vez de só inspecionar `ServiceDescriptor` (§4). §3: `Core.Tests.csproj` como "Alterar (T6)".
- **S10** — Atualizar as referências ao ADR-004 de "Proposto" para "Aceito".
  → Incorporado na linha "Regras", no §2 (cabeçalho), no §3 ("Existente (Aceito)") e no §10.
- **S11** — Explicitar no §2.6 como o nome do ambiente é obtido para `appsettings.{Environment}.json` com `DisableDefaults`.
  → Incorporado em §2.6: `Program.cs` lê `DOTNET_ENVIRONMENT` e passa em `AppRunOptions.EnvironmentName`. `EnglishTutorApp` usa `HostApplicationBuilderSettings.EnvironmentName = options.EnvironmentName ?? Environments.Production`. §7: risco novo. §8: o teste automatizado desse carregamento fica fora de escopo.
- **S12** — Especificar o runner do CI e confirmar a compatibilidade de `setup-dotnet` com o `global.json`.
  → Incorporado em §2.8 e §5 T12: `ubuntu-latest`, `setup-dotnet@v4` com `global-json-file` e passo `dotnet --version`. Qualquer SDK 10.0.x ≥ 10.0.100 satisfaz `latestFeature`, então tanto a versão exata quanto uma 10.0.x mais nova servem. A conferência fica no log da primeira execução. §10 D10 confere esses itens. A escolha do runner está em §9 **P11** e aguarda o usuário.
- **S13** — `[NotNullWhen(true)]` no `out` de `TryParse`; incluir `"/"` sozinho como caso do parser, com resultado definido.
  → Incorporado em §2.5: assinatura com `[NotNullWhen(true)]`; `Name` como digitado, com barra; `"/"` → `true`, `Unknown`, `Name = "/"`, mensagem `Unknown command "/".`. §4: `TryParse_WhenSlashCommandIsUnknown_ReturnsUnknownCommand` virou `[Theory]` com `/xyz`, `/` e `/mode interview`.
- **S14** — `Program.cs` como "Alterar" (já existe desde a T1) no §3 e na T7.
  → Incorporado no §3 ("Alterar (T8)") e no §5. Com a renumeração, a tarefa de composição do host passou a ser a T8.
