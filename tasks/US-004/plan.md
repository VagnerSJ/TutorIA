# Plano — US-004 Conversar por texto com o professor via Claude Code

Status: Aprovado em 2026-10-17
Branch: feature/US-004_conversa-texto-claude-code
Spec: specs/sprints/SPRINT_1.md#us-004
Regras: RN-BRAIN01, RN-BRAIN02, RN-BRAIN05, RN-BRAIN06, RN-CONV01, RN-CONV03, RN-CONV05, RN-CONV06, RN-FEED01–05 (RN-BRAIN07/09 só no limite descrito em §8); ARCHITECTURE §2 (R1–R4), §3, §5, §6.1, §6.3, §9, §10; ADR-001, ADR-002, ADR-004, ADR-005, ADR-006, ADR-007 (Aceito, novo)

## 1. Objetivo

Ligar o console ao `ConversationService` com o primeiro cérebro real, o `ClaudeCodeTutorBrain`: um processo `claude -p` isolado por turno, com histórico controlado pela aplicação, `--resume` como otimização, leitura da saída JSON pelo parser único e medição de latência JsonSchema × Tags. No console entram `/mode`, a exibição de reply + feedback e o bind de `Conversation:MaxTurnsInWindow`.

## 2. Abordagem

A decisão central está no **ADR-007 (Aceito)**: `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md`. Em resumo:

- processo por turno atrás de `IProcessRunner`;
- histórico renderizado num texto único no stdin;
- `session_id` guardado no adaptador e reaproveitado só quando o request estende exatamente a sessão conhecida (mesmo system prompt, mesmas mensagens, mais uma mensagem do aluno);
- system prompt num arquivo temporário fora da pasta de trabalho;
- `structured_output`/`result` → `TutorReplyParser`.

O adaptador, como o `ConversationService`, atende uma chamada por vez e não é thread-safe. O console é sequencial, então isso basta.

### 2.1 Porta de processo (`src/EnglishTutor.Infrastructure/Processes/`)

```csharp
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, string StandardInput);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
}

public sealed class ProcessRunner : IProcessRunner   // único lugar que usa System.Diagnostics.Process
```

O `ProcessRunner` usa `UseShellExecute = false`, `CreateNoWindow = true` e `ArgumentList`, nunca string concatenada. stdin, stdout e stderr ficam redirecionados em UTF-8 sem BOM (`new UTF8Encoding(false)`). Ordem, nesta sequência:

1. inicia o processo;
2. **começa** a leitura assíncrona de stdout e de stderr (as duas tarefas ficam em andamento);
3. escreve o stdin e o fecha. Se o processo já tiver terminado (argumento recusado, versão antiga da CLI), a `IOException` de pipe fechado na escrita é ignorada, para que o código de saída e o stderr ainda cheguem ao adaptador (review S4);
4. espera com `WaitForExitAsync(ct)` e depois aguarda as duas leituras.

Se o `ct` for cancelado, chama `Kill(entireProcessTree: true)` e relança a `OperationCanceledException`. Ele não traduz exceções: `Win32Exception` (executável inexistente) propaga, e o console a trata como erro inesperado até a US-005, que a traduz para `CliNotFound` (texto alinhado na §2.8 e no ADR-007, item 5; review S3). Não há teste unitário, porque ele lança um processo real (ARCHITECTURE §10). Quem o exercita são os testes `LiveBrain` (T9).

### 2.2 Configuração (`src/EnglishTutor.Infrastructure/Brain/`)

As Options do adaptador ficam na Infrastructure: são detalhe do provedor (R3). Os validadores são registrados pela própria Infrastructure, e o bind fica no composition root, como no ADR-004.

```csharp
public static class BrainOutputModes            // mesma forma de BrainProviders (Core)
{
    public const string JsonSchema = "JsonSchema";
    public const string Tags = "Tags";
    public static IReadOnlyList<string> All { get; }            // [JsonSchema, Tags]
    public static bool TryGetCanonical(string? value, [NotNullWhen(true)] out string? canonical); // sem diferenciar maiúsculas
}

public sealed class ClaudeCodeOptions
{
    public const string SectionName = "Brain:ClaudeCode";
    public const string DefaultWorkingDirectory = "%LOCALAPPDATA%/EnglishTutor/brain";
    public string ExecutablePath { get; set; } = "claude";
    public string Model { get; set; } = "haiku";
    public string WorkingDirectory { get; set; } = DefaultWorkingDirectory;
    public bool UseResume { get; set; } = true;
    public string OutputMode { get; set; } = BrainOutputModes.JsonSchema;
}

public sealed class ClaudeCodeOptionsValidator : IValidateOptions<ClaudeCodeOptions>;
```

**Normalização de `OutputMode` (B4, P13).** As Options guardam o valor como foi configurado. O **adaptador** é o dono da normalização para uso: a cada chamada, obtém o modo canônico com `BrainOutputModes.TryGetCanonical(options.OutputMode, out var mode)` e usa **só** o canônico na decisão de `--json-schema`, na escolha do arquivo de formato e no log 2001. É o mesmo padrão de `Brain:Provider` (US-001, P4: `BrainProviders.TryGetCanonical` no consumidor). Com `"jsonschema"` no `appsettings.json`, o processo roda no modo JsonSchema e o log diz `JsonSchema`.

`TimeoutSeconds` (ARCHITECTURE §6.1) **não** entra nesta US: o tempo máximo e a mensagem de tempo esgotado são da US-005, cenário "tempo esgotado" (P6).

Mensagens do validador (uma falha por chave inválida; o texto diz o que está errado e o que se espera):

| Condição | Mensagem |
|---|---|
| `ExecutablePath` em branco | `Brain:ClaudeCode:ExecutablePath is required. Expected the name or path of the Claude Code CLI (default: claude).` |
| `Model` em branco | `Brain:ClaudeCode:Model is required. Expected a Claude model name or alias (default: haiku).` |
| `WorkingDirectory` em branco | `Brain:ClaudeCode:WorkingDirectory is required. Expected an empty folder dedicated to the tutor (default: %LOCALAPPDATA%/EnglishTutor/brain).` |
| `OutputMode` fora da lista | `Brain:ClaudeCode:OutputMode '<valor>' is invalid. Accepted values: JsonSchema, Tags.` |

### 2.3 Prompts e schema

- `prompts/**` é copiado para a pasta `prompts/` da saída do Console e da `Infrastructure.Tests` (`<None Include="..\..\prompts\**" Link="prompts\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`), para os testes `LiveBrain` (P12).
- `public sealed record PromptFiles(string Directory)` com `TutorPath` (`tutor.md`), `FormatJsonPath` (`format.json.md`) e `FormatTagsPath` (`format.tags.md`). O composition root cria com `Path.Combine(builder.Environment.ContentRootPath, "prompts")`. Não há chave de configuração.
- `FileTutorPromptSource(PromptFiles files) : ITutorPromptSource` lê `tutor.md` (UTF-8) a cada chamada (ADR-006, item 5). Se o arquivo faltar, lança `FileNotFoundException` com a mensagem `System prompt file not found at '<caminho>'. Expected prompts/tutor.md next to the application (copied from the repository's prompts folder).`
- `public static class TutorReplySchema { public const string Json = "..."; }`, numa linha só, porque vai como argumento de `--json-schema`:

  ```json
  {"type":"object","properties":{"reply":{"type":"string"},"corrections":{"type":"array","maxItems":2,"items":{"type":"object","properties":{"original":{"type":"string"},"corrected":{"type":"string"},"explanation":{"type":"string"}},"required":["original","corrected","explanation"],"additionalProperties":false}},"pronunciation":{"type":["string","null"]},"vocabulary":{"type":["string","null"]},"praise":{"type":["string","null"]}},"required":["reply","corrections","pronunciation","vocabulary","praise"],"additionalProperties":false}
  ```

  O US-006 (Ollama `format`) reutiliza o mesmo texto (ADR-002).

### 2.4 Adaptador `ClaudeCodeTutorBrain` (`src/EnglishTutor.Infrastructure/Brain/`)

```csharp
public sealed class ClaudeCodeTutorBrain(
    IProcessRunner processRunner,
    TutorReplyParser parser,
    PromptFiles promptFiles,
    IOptions<ClaudeCodeOptions> options,
    TimeProvider timeProvider,
    ILogger<ClaudeCodeTutorBrain> logger) : ITutorBrain
{
    public string Name => BrainProviders.ClaudeCode;          // "ClaudeCode"
    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct);
}

// Estado privado do adaptador (campo _session); a igualdade de mensagens é feita com SequenceEqual.
private sealed record ClaudeCodeSession(string SessionId, string SystemPrompt, IReadOnlyList<BrainMessage> Messages);

public static class ClaudeCodePromptRenderer
{
    public static string Render(IReadOnlyList<BrainMessage> messages);
}

internal sealed record ClaudeCodeResult(string? SessionId, string RawText);
internal static class ClaudeCodeOutput
{
    public static ClaudeCodeResult Read(ProcessResult result);   // BrainException(Unknown) nas falhas da §2.5
}

internal static partial class ClaudeCodeLog                       // faixa 2000–2999 (cérebro e parser)
{
    // 2001 Information: "Brain {Provider} replied in {ElapsedMs} ms (output mode {OutputMode}, resumed session {Resumed})"
}
```

**`SendAsync(request, ct)`**, nesta ordem:

1. `ArgumentNullException.ThrowIfNull(request)`.
2. Pasta de trabalho: `Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.WorkingDirectory))` e depois `Directory.CreateDirectory` (RN-BRAIN06). O adaptador não escreve nada nela. Modo: `mode` = canônico de `options.OutputMode` via `BrainOutputModes.TryGetCanonical` (§2.2, P13); se falhar, `InvalidOperationException` (defensiva, §2.9).
3. Retomada: `resumeId = options.UseResume && _session is { } s && request.SystemPrompt == s.SystemPrompt && request.Messages.Count == s.Messages.Count + 1 && request.Messages[^1].Role == BrainRole.User && request.Messages.Take(s.Messages.Count).SequenceEqual(s.Messages) ? s.SessionId : null`. A igualdade é a de `record` de `BrainMessage` (`Role` + `Content`, só strings; AP-028 não se aplica) e a ordinal de `string` para o system prompt (review S2). Antes de rodar, `_session = null`, para que falha ou cancelamento nunca deixem uma sessão "conhecida" inconsistente.
4. stdin: com `resumeId`, `request.Messages[^1].Content`; senão, `ClaudeCodePromptRenderer.Render(request.Messages)`.
5. System prompt: grava `request.SystemPrompt` (UTF-8 sem BOM) em `Path.Combine(Path.GetTempPath(), $"englishtutor-system-prompt-{Guid.NewGuid():N}.md")`. O arquivo é apagado num `finally` (`File.Delete`) depois do processo, inclusive quando o processo falha ou a chamada é cancelada.
6. Argumentos, nesta ordem, em `ArgumentList`:
   `-p`, `--output-format`, `json`, [`--json-schema`, `TutorReplySchema.Json` se `mode == BrainOutputModes.JsonSchema`], `--system-prompt-file`, `<arquivo temporário>`, `--append-system-prompt-file`, `<FormatJsonPath | FormatTagsPath conforme mode>`, `--tools`, `""` (string vazia), `--disallowedTools`, `mcp__*`, `--strict-mcp-config`, `--permission-mode`, `dontAsk`, `--model`, `<Model>`, [`--resume`, `<resumeId>`]. Nunca `--bare` (RN-BRAIN05, RN-BRAIN06).
7. `result = await processRunner.RunAsync(new(ExecutablePath, args, workingDirectory, stdin), ct)`, com o `ct` do chamador sem troca. O tempo é medido com `timeProvider.GetTimestamp()`/`GetElapsedTime`.
8. `ClaudeCodeOutput.Read(result)` (§2.5) devolve `SessionId` e `RawText`.
9. `_session = SessionId is null ? null : new(SessionId, request.SystemPrompt, [.. request.Messages, new(BrainRole.Assistant, RawText)])`, tanto para resposta válida quanto inválida. Assim, a retentativa do ADR-006 estende a sessão.
10. `parser.Parse(RawText)`. Se for válido, loga 2001 (com `mode`, o canônico) e devolve `Reply`. Se for inválido, lança `BrainException.InvalidOutput(rejection, RawText)` (ADR-006).

Os logs não levam o texto do aluno, o system prompt nem o `RawText` (padroes-codigo, Logs).

**Formato do `ClaudeCodePromptRenderer.Render`** (linhas unidas com `"\n"`):

- 1 mensagem → o `Content` sem mudança.
- 2 ou mais mensagens:

  ```
  Previous messages in this conversation, oldest first:

  [student]
  <Content da 1ª mensagem User>

  [tutor]
  <Content da 1ª mensagem Assistant>

  Current message:
  <Content da última mensagem>
  ```

  `User` → `[student]` e `Assistant` → `[tutor]`. Cada mensagem anterior é seguida de uma linha em branco. A última mensagem vem depois de `Current message:`. Com lista vazia, lança `ArgumentException` (defensiva, §2.9).

### 2.5 Leitura da saída e falhas (`ClaudeCodeOutput.Read`)

As linhas são avaliadas nesta ordem; a primeira que casar decide. Cada linha de falha tem, na §4, um caso de teste que só ela satisfaz (B3). A condição da linha 2 é composta (texto que não é JSON; JSON válido que não é objeto com `"type": "result"`), e cada parte tem caso próprio (review S2 da rodada 2).

| # | Situação | Resultado |
|---|---|---|
| 1 | `ExitCode != 0` | `BrainException(Unknown, "Claude Code failed (exit code <n>): <detalhe>")` |
| 2 | `ExitCode == 0` e o stdout não é JSON válido, **ou** é JSON válido mas não é um objeto com `"type": "result"` (array, objeto com outro `type` ou sem `type`) | `BrainException(Unknown, "Claude Code returned output that is not a JSON result. Expected the JSON object printed by \"claude -p --output-format json\".")` |
| 3 | `ExitCode == 0` e `is_error: true` | `BrainException(Unknown, "Claude Code reported an error: <detalhe>")`, **sem** chamar o parser, mesmo que haja `structured_output` válido (B3) |
| 4 | `structured_output` é objeto | `RawText = structured_output.GetRawText()` |
| 5 | senão | `RawText = result` (string; ausente → `""`) |
| — | `session_id` (linhas 4 e 5) | string não vazia → `SessionId`; senão `null` (o turno seguinte não usa `--resume`) |

`<detalhe>`, nesta ordem: o `result` do JSON, quando o stdout é legível, `is_error` é `true` e o `result` não está em branco (com `Trim()`); senão, a **primeira linha não vazia** do stderr (com `Trim()`); senão, `no error details were printed`. Os três ramos e a "primeira linha" têm teste (§4, AP-016; review S5). A classificação por `BrainErrorKind` (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`) é da US-005 (P5).

### 2.6 Console

**`IChatInputHandler`** ganha o modo, para o `ChatSession` tratar `/mode` sem conhecer o serviço:

```csharp
public interface IChatInputHandler
{
    TutorMode Mode { get; }
    void ChangeMode(TutorMode mode);
    Task HandleAsync(string text, CancellationToken ct);
}
```

- `ConversationInputHandler(ConversationService conversation, TextWriter output)` (novo). `Mode` e `ChangeMode` delegam ao serviço. `HandleAsync` chama `SendAsync`: no `Ignored`, escreve o `Warning`; no `Replied`, escreve as linhas da tabela abaixo. Exceções propagam (P11).
- `TutorNotConnectedInputHandler` guarda o modo num campo (`TutorMode.Default` no início) e continua escrevendo `The tutor is not connected yet.`. Ele passa a ser usado só com `Brain:Provider = Ollama`, até a US-006 (P7).
- `ChatSession(TextReader input, TextWriter output, IChatInputHandler inputHandler, string provider)`: o parâmetro `TutorMode mode` sai, e o cabeçalho usa `inputHandler.Mode`. Novo `case LocalCommandKind.Mode`: `TutorMode.TryParse(command.Arguments, out var mode)`. Se der certo, chama `inputHandler.ChangeMode(mode)` e escreve `ConsoleText.ModeChanged(mode)`. Senão, escreve `ConsoleText.ModeNotRecognized(command.Arguments)`. Nos dois casos nada vai ao handler (RN-CONV06). A checagem de linha em branco continua no `ChatSession` (P10).
- `LocalCommands.Available` passa a ser `[Help, Mode, Quit]`, com `Mode` descrito como `Change the mode: free-conversation, job-interview, listening-practice` (montado de `TutorMode.All`). O comentário "not listed in Available until the console handles it" sai.

Textos exibidos ao aluno (em `ConsoleText`, em inglês):

| Situação | Linhas |
|---|---|
| Resposta do professor | `Tutor: <reply.Reply>` |
| Feedback (só se `FeedbackFormatter.Format(reply)` não estiver vazio) | `Feedback:` e, para cada linha, `  - <linha>` |
| Fallback cru (`IsRawFallback`) | só `Tutor: <reply.Reply>` (o feedback é vazio, US-002) |
| `/mode` com nome válido | `Mode changed to "<mode.Name>". It applies from your next sentence.` |
| `/mode` com nome desconhecido | `Unknown mode "<argumento>".` + `Available modes: free-conversation, job-interview, listening-practice` |
| `/mode` sem nome | `Choose a mode: /mode <mode>.` + `Available modes: free-conversation, job-interview, listening-practice` |
| `/help` | `Available commands:`, `  /help - Show the available commands`, `  /mode - Change the mode: free-conversation, job-interview, listening-practice`, `  /quit - Exit the tutor` |
| Comando desconhecido | `Unknown command "<nome>".` + `Available commands: /help, /mode, /quit` |

**Composition root (`EnglishTutorApp`):**

- `AppRunOptions` ganha `Action<IServiceCollection>? ConfigureServices = null` (último parâmetro). Ele é aplicado no fim do `BuildHost`, depois de todos os registros, para os testes trocarem `ITutorBrain`/`ITutorPromptSource` sem processo real. `Program.cs` não o usa.
- `BuildHost` passa a ter:
  - `AddEnglishTutorInfrastructure(Path.Combine(ContentRootPath, "prompts"))`;
  - `AddOptions<ClaudeCodeOptions>().Bind(GetSection(ClaudeCodeOptions.SectionName)).ValidateOnStart()`;
  - `AddOptions<ConversationOptions>().Bind(GetSection(ConversationOptions.SectionName)).ValidateOnStart()`.

  Com **uma** classe de Options inválida (ex.: só `MaxTurnsInWindow = 0`), o `StartAsync` lança `OptionsValidationException`, que cai no `catch` que já existe: `Invalid configuration:` + a mensagem do validador em `Error` e `ExitCodes.InvalidConfiguration` (2), sem cabeçalho nem prompt (AP-024, mesmo padrão da US-001).

  Com **mais de uma** classe inválida ao mesmo tempo (ex.: `Brain:Provider = "Inexistente"` e `Conversation:MaxTurnsInWindow = 0`), o `ValidateOnStart` lança uma `AggregateException` com uma `OptionsValidationException` por classe, que hoje cairia no `catch (Exception)` (erro inesperado, código 1). Decisão (B1, P19): o tratamento de configuração inválida passa a reconhecer as duas formas.
  - Falhas de validação = `exception.Failures` de uma `OptionsValidationException`, ou a concatenação dos `Failures` de cada interna de uma `AggregateException` cujas internas são **todas** `OptionsValidationException`, na ordem em que vêm.
  - Com falhas de validação: `logger.StartupFailed(string.Join(" ", failures))`, `Invalid configuration:` e uma linha `  <falha>` por falha em `Error` (com `CancellationToken.None`, como hoje), e `ExitCodes.InvalidConfiguration`.
  - `AggregateException` com qualquer interna de outro tipo continua erro inesperado (defensiva, §2.9).
  - Forma sugerida, livre para o Executor: um `private static IReadOnlyList<string>? ValidationFailures(Exception)` usado num filtro `catch (Exception exception) when (ValidationFailures(exception) is { } failures)`, no lugar do `catch (OptionsValidationException)`; `WriteInvalidConfigurationAsync` passa a receber a lista.
- `RunSessionAsync`: com o provedor canônico `ClaudeCode`, usa `new ConversationInputHandler(services.GetRequiredService<ConversationService>(), options.Output)`; com qualquer outro, `new TutorNotConnectedInputHandler(options.Output)` (P7). `ApplicationStarted` loga `handler.Mode.Name`.
- `appsettings.json`:

  ```json
  "Brain": {
    "Provider": "ClaudeCode",
    "ClaudeCode": {
      "ExecutablePath": "claude",
      "Model": "haiku",
      "WorkingDirectory": "%LOCALAPPDATA%/EnglishTutor/brain",
      "UseResume": true,
      "OutputMode": "JsonSchema"
    }
  },
  "Conversation": {
    "MaxTurnsInWindow": 10
  },
  ```

- `Program.cs`: `System.Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);` antes de criar as opções, para a seta `→` do feedback sair certa no console do Windows (pendência citada em `docs/us/US-002.md`; P14).

### 2.7 DI

- `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`: `AddEnglishTutorInfrastructure(this IServiceCollection services, string promptsDirectory)` registra:
  - `AddOptions()` e `TryAddSingleton(TimeProvider.System)`;
  - `TryAddSingleton(new PromptFiles(promptsDirectory))`;
  - `TryAddSingleton<IProcessRunner, ProcessRunner>()` e `TryAddSingleton<TutorReplyParser>()`;
  - `TryAddSingleton<ITutorPromptSource, FileTutorPromptSource>()` e `TryAddSingleton<ITutorBrain, ClaudeCodeTutorBrain>()`;
  - `TryAddEnumerable(IValidateOptions<ClaudeCodeOptions> → ClaudeCodeOptionsValidator)`.

  O `ITutorBrain` fica fixo em ClaudeCode até a `BrainFactory` da US-007 (P17). Com `Ollama`, o console não resolve o `ConversationService`, então o processo nunca é chamado.
- `AddEnglishTutorCore()` passa a registrar `TryAddSingleton<ConversationService>()` (uma sessão por processo).

### 2.8 Alterações nos documentos-raiz e na spec (propostas; aplicadas pelo orquestrador antes da T1, em dois commits)

O orquestrador aplica, depois da aprovação e **antes da T1**:

- `docs(ai): update spec for US-004`: os dois blocos de `specs/sprints/SPRINT_1.md` abaixo (US-005 e US-006);
- `docs(ai): update architecture for US-004`: `ARCHITECTURE.md` e skills abaixo, com o ADR-007 em "Aceito", se aprovado.

Nenhuma tarefa da §5 altera esses documentos. Cada trecho é identificado pelo **texto antigo** (AP-023).

**`specs/sprints/SPRINT_1.md`, seção `## US-005 — Erros do Claude Code com mensagens claras`** (AP-022: esta US deixa a falha do cérebro encerrar o app com erro inesperado, P11; B2). Os critérios reaproveitam os já fixados no projeto (AP-024): mensagens de falha na saída de erro (`Error`), como `Invalid configuration:` e `An unexpected error occurred...`; código 0 ao sair com `/quit`; o texto `Claude Code failed (exit code <n>): <detalhe>` desta US.

1. No bloco `gherkin` da US-005, depois do cenário "tempo esgotado" (última linha antiga: `  E vejo uma mensagem de tempo esgotado`), acrescentar:

```gherkin

Cenário: falha do cérebro no console não encerra a sessão
  Dado o console com o provedor ClaudeCode
  E um cérebro que falha num turno com BrainException de qualquer BrainErrorKind que chegue ao console, inclusive Unknown
  Quando envio uma frase
  Então vejo na saída de erro uma mensagem que diz o que deu errado e o que fazer
  E, para Unknown, a mensagem inclui o detalhe da BrainException (ex.: "Claude Code failed (exit code 1): ...")
  E não vejo "An unexpected error occurred. See the log file for details."
  E a sessão continua: a frase seguinte é enviada ao cérebro
  E ao digitar "/quit" o código de saída é 0
```

2. Na tabela "DoD extra" da US-005, depois da linha antiga que começa com `| D6 | `$env:Brain__ClaudeCode__ExecutablePath="nao-existe"`, acrescentar:

```
| D7 | `dotnet test --filter "FullyQualifiedName~EnglishTutorAppTests"` | verde; o teste `RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError` (US-004, P11) foi substituído pelo teste do cenário "falha do cérebro no console não encerra a sessão" |
```

(O `InvalidOutput` não chega ao console: o `ConversationService` o trata com retentativa e fallback, ADR-006. Por isso o texto diz "que chegue ao console".)

**`specs/sprints/SPRINT_1.md`, seção `## US-006 — Professor local via Ollama`** (AP-022: esta US mantém o console "não conectado" para `Ollama`, P7). No bloco `gherkin` da US-006, depois do cenário "modelo não baixado" (última linha antiga: `  Então vejo o comando "ollama pull <modelo>"`), acrescentar:

```gherkin

Cenário: console conversa com o Ollama
  Dado "Brain:Provider" igual a "Ollama"
  Quando envio uma frase pelo console
  Então o BrainRequest é enviado ao OllamaTutorBrain
  E vejo a linha "Tutor: <reply>" e o feedback do turno, no mesmo formato do provedor ClaudeCode
  E não vejo "The tutor is not connected yet."
```

**`ARCHITECTURE.md`:**

- §3, texto antigo:
  ```
    EnglishTutor.Infrastructure/
      Brain/             ClaudeCodeTutorBrain, ClaudeCodeStreamingBrain, OllamaTutorBrain,
                         TutorReplyParser, BrainFactory, FallbackTutorBrain
  ```
  Texto novo:
  ```
    EnglishTutor.Infrastructure/
      Brain/             ClaudeCodeTutorBrain, ClaudeCodeOptions, ClaudeCodeOptionsValidator, ClaudeCodeOutput,
                         ClaudeCodePromptRenderer, ClaudeCodeLog, BrainOutputModes, TutorReplySchema,
                         FileTutorPromptSource, PromptFiles, ClaudeCodeStreamingBrain, OllamaTutorBrain,
                         TutorReplyParser, BrainFactory, FallbackTutorBrain
      Processes/         IProcessRunner, ProcessRequest, ProcessResult, ProcessRunner
      DependencyInjection/ InfrastructureServiceCollectionExtensions
  ```
- §3, texto antigo: `    Program.cs, EnglishTutorApp, appsettings.json, Commands/ (chat, eval), Ui/, Logging/`
  Texto novo: `    Program.cs, EnglishTutorApp, appsettings.json, Commands/ (chat, eval), Ui/ (ChatSession, ConversationInputHandler), Logging/; prompts/ copiado para a saída`
- §5, texto antigo: `Provedores com estado (Claude Code via `--resume`) podem usar `SessionHint` como otimização, mas devem funcionar corretamente recebendo apenas o `BrainRequest` completo.`
  Texto novo: `Provedores com estado (Claude Code via `--resume`) guardam o identificador da sessão no próprio adaptador e só o reaproveitam quando o `BrainRequest` tem o mesmo system prompt, repete exatamente as mensagens que a sessão já conhece e acrescenta uma única mensagem do aluno; em qualquer outro caso enviam a janela completa numa sessão nova (ADR-007). `BrainRequest.SessionHint` fica reservado, e o `ConversationService` o envia `null`.`
- §6.3, no bloco da invocação, texto antigo (3 linhas):
  ```
    --json-schema <schema>                  (OutputMode=JsonSchema)
    --system-prompt-file prompts/tutor.md
    --append-system-prompt-file prompts/format.json.md
  ```
  Texto novo:
  ```
    --json-schema <schema>                  (OutputMode=JsonSchema; TutorReplySchema)
    --system-prompt-file <arquivo temporário com BrainRequest.SystemPrompt = prompts/tutor.md>
    --append-system-prompt-file prompts/format.json.md   (prompts/format.tags.md com OutputMode=Tags)
  ```
- §6.3, texto antigo: `- Prompt enviado via stdin (evita limites e escaping de linha de comando no Windows).`
  Texto novo (2 itens):
  `- Prompt enviado via stdin (evita limites e escaping de linha de comando no Windows): sem `--resume`, as mensagens do `BrainRequest` num texto único (`ClaudeCodePromptRenderer`); com `--resume`, só a mensagem nova (ADR-007). O arquivo temporário do system prompt fica fora da pasta de trabalho e é apagado ao fim da chamada.`
  `- Saída: `structured_output` (JsonSchema) ou `result` (Tags) vai para o `TutorReplyParser`; o `session_id` fica no adaptador (ADR-007). Até a US-005, código de saída diferente de zero, `is_error` ou saída que não é JSON viram `BrainException` com `Unknown`; executável inexistente propaga a exceção do sistema e o console mostra erro inesperado (a US-005 a classifica como `CliNotFound`).`

**Repetições em `CLAUDE.md` e skills** (Grep por `SessionHint|--resume|system-prompt-file|AddEnglishTutorInfrastructure|ValidateDataAnnotations|IProcessRunner|claude-json` em `CLAUDE.md` e `.claude/**`):

- `CLAUDE.md`: nenhuma linha repete §3/§5/§6.3. **Sem alteração.**
- `.claude/skills/padroes-codigo/SKILL.md`, seção "DI e configuração", texto antigo:
  `- Cada projeto expõe uma extensão de registro: `services.AddEnglishTutorCore()`, `services.AddEnglishTutorInfrastructure(configuration)`.`
  Texto novo:
  `- Cada projeto expõe uma extensão de registro: `services.AddEnglishTutorCore()`, `services.AddEnglishTutorInfrastructure(promptsDirectory)`. Cada uma registra os validadores das próprias Options; o bind das seções fica no composition root (ADR-004).`
- Mesma seção, texto antigo:
  `- Options: `services.AddOptions<BrainOptions>().Bind(config.GetSection("Brain")).ValidateDataAnnotations().ValidateOnStart();` + `IValidateOptions<T>` quando a validação for condicional.`
  Texto novo:
  `- Options: `services.AddOptions<BrainOptions>().Bind(config.GetSection(BrainOptions.SectionName)).ValidateOnStart();` no composition root + um `IValidateOptions<T>` por classe de Options, com mensagens que dizem o que está errado e o que se espera.`
  (O código nunca usou `ValidateDataAnnotations`. A linha antiga já divergia do ADR-004.)
- `.claude/skills/padroes-codigo/SKILL.md`, seção "Processos externos" (`IProcessRunner`, `ArgumentList`, stdin, leitura assíncrona, `Kill(entireProcessTree: true)`): continua correta. **Sem alteração.**
- `.claude/skills/tdd-dotnet/SKILL.md`, bloco "Organização", texto antigo:
  `  Fixtures/claude-json-structured.json         # saídas reais gravadas a partir da US-004 (CopyToOutputDirectory)`
  Texto novo:
  `  Fixtures/ClaudeCode/claude-json-structured.json  # saídas reais gravadas a partir da US-004 (CopyToOutputDirectory)`
- `.claude/skills/tdd-dotnet/SKILL.md`, linha sobre `IProcessRunner` em "Fakes x mocks": continua correta. **Sem alteração.**

(As propostas de skill dos "Aprendizados para o processo" do `plan-review.md`, como a regra de falhas simultâneas de Options, ficam para o Documentador, como no fluxo do `/us`.)

### 2.9 Guardas defensivas (sem teste, decisão registrada; review S3 da US-003)

São defensivas e ficam sem teste:

- os `ArgumentNullException.ThrowIfNull` dos métodos públicos novos (`ClaudeCodeTutorBrain.SendAsync`, `ClaudeCodePromptRenderer.Render`, `FileTutorPromptSource` (construtor), `ConversationInputHandler` (construtor e `HandleAsync`), `ClaudeCodeOptionsValidator.Validate`, `AddEnglishTutorInfrastructure`);
- a `ArgumentException` de `Render` com lista vazia;
- a `InvalidOperationException` do adaptador quando `TryGetCanonical` falha para `OutputMode` (só acontece se o validador for contornado; o validador recusa o valor antes, com teste);
- no `EnglishTutorApp`, a `AggregateException` com alguma interna que não é `OptionsValidationException` continuar como erro inesperado (o `ValidateOnStart` só junta `OptionsValidationException`; um validador que lançasse em vez de devolver `Fail` seria bug, e o caminho é o `catch (Exception)` já testado).

Só o próprio código os chama, sempre com valores válidos. A US que criar outro chamador acrescenta o teste.

### Alternativas descartadas

- `SessionHint` preenchido pelo serviço: o Core não conhece o `session_id` da CLI, e seria preciso mudar o `TutorReply` (ADR-007).
- `--resume` sempre, enviando só a última mensagem: a sessão da CLI passa a guardar mais turnos que a janela e a tentativa rejeitada (ADR-007).
- System prompt gravado dentro da pasta de trabalho, ou `--system-prompt <texto>`: viola RN-BRAIN06 ou o cenário da US-004 (ADR-007).
- `AddEnglishTutorInfrastructure(configuration)` com bind na Infrastructure: exige `Options.ConfigurationExtensions` na Infrastructure e contraria o ADR-004.
- Normalizar `OutputMode` nas Options (`PostConfigure`): esconderia o valor configurado das mensagens do validador e divergiria do padrão de `Brain:Provider` (P13).
- Um validador único para as três seções, para evitar a `AggregateException`: acoplaria Core e Infrastructure num só tipo (P19).
- Comando `measure-latency` no console: o `AddCommandLine` atual trataria o argumento como configuração. O teste `LiveBrain` de medição basta para a D7, e a US-009 cria o comando `eval` (P15).
- `ITutorBrain` "não conectado" para Ollama: provisório descartável. O handler não conectado já existe (P7).
- Gravar fixtures à mão no PowerShell: o JSON Schema na linha de comando do PowerShell 5.1 quebra as aspas. O teste gravador (`LiveBrain`) usa o mesmo adaptador (P16).

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md` | Decisão (Aceito em 2026-10-17), criada junto com este plano; itens 3 e 5 e "Consequências" revisados na rodada 1; item 5 e contagem de turnos em "Consequências" revisados na rodada 2 |
| Alterar | `ARCHITECTURE.md` | §3, §5, §6.3 (texto em §2.8), pelo orquestrador antes da T1 |
| Alterar | `.claude/skills/padroes-codigo/SKILL.md` | 2 linhas de "DI e configuração" (§2.8), pelo orquestrador antes da T1 |
| Alterar | `.claude/skills/tdd-dotnet/SKILL.md` | Caminho da fixture (§2.8), pelo orquestrador antes da T1 |
| Alterar | `specs/sprints/SPRINT_1.md` | Cenário "falha do cérebro no console não encerra a sessão" e D7 na US-005; cenário "console conversa com o Ollama" na US-006 (§2.8), pelo orquestrador antes da T1 |
| Alterar | `docs/adr/ADR-002-contrato-saida-estruturada.md` | Tabela de latência JsonSchema × Tags (D7), pelo Executor na T11 |
| Alterar | `src/EnglishTutor.Console/EnglishTutor.Console.csproj` | Cópia de `prompts/**` para a saída |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/EnglishTutor.Infrastructure.Tests.csproj` | Cópia de `prompts/**`; `PackageReference` a `Microsoft.Extensions.DependencyInjection` |
| Alterar | `src/EnglishTutor.Infrastructure/EnglishTutor.Infrastructure.csproj` | `PackageReference` a `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Criar | `src/EnglishTutor.Infrastructure/Brain/BrainOutputModes.cs` | `JsonSchema`/`Tags` canônicos |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOptions.cs` | Seção `Brain:ClaudeCode` |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOptionsValidator.cs` | Mensagens da §2.2 |
| Criar | `src/EnglishTutor.Infrastructure/Brain/TutorReplySchema.cs` | JSON Schema do contrato (ADR-002) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodePromptRenderer.cs` | Mensagens → texto único (§2.4) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/PromptFiles.cs` | Caminhos de `tutor.md` e dos arquivos de formato |
| Criar | `src/EnglishTutor.Infrastructure/Brain/FileTutorPromptSource.cs` | `ITutorPromptSource` lendo `tutor.md` |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeTutorBrain.cs` | Adaptador (§2.4), com `ClaudeCodeSession` privado e normalização de `OutputMode` |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOutput.cs` | `ClaudeCodeResult` + leitura e falhas (§2.5) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeLog.cs` | `[LoggerMessage]` 2001 |
| Criar | `src/EnglishTutor.Infrastructure/Processes/IProcessRunner.cs` | Porta interna + `ProcessRequest`/`ProcessResult` |
| Criar | `src/EnglishTutor.Infrastructure/Processes/ProcessRunner.cs` | `System.Diagnostics.Process` (§2.1) |
| Criar | `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | `AddEnglishTutorInfrastructure(promptsDirectory)` |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | Registrar `ConversationService` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs` | `/mode` em `Available` |
| Alterar | `src/EnglishTutor.Console/Ui/IChatInputHandler.cs` | `Mode` e `ChangeMode` |
| Alterar | `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` | Modo local; uso só com Ollama |
| Criar | `src/EnglishTutor.Console/Ui/ConversationInputHandler.cs` | Delegação ao `ConversationService` + exibição |
| Alterar | `src/EnglishTutor.Console/Ui/ChatSession.cs` | Modo pelo handler; `case Mode` |
| Alterar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | Textos da §2.6 |
| Alterar | `src/EnglishTutor.Console/AppRunOptions.cs` | `ConfigureServices` |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Infrastructure, binds, handler por provedor; configuração inválida em várias seções (P19) |
| Alterar | `src/EnglishTutor.Console/Program.cs` | `OutputEncoding` UTF-8 sem BOM |
| Alterar | `src/EnglishTutor.Console/appsettings.json` | `Brain:ClaudeCode` e `Conversation:MaxTurnsInWindow` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeProcessRunner.cs` | Fila de `ProcessResult`; registra `Requests`, `Tokens`; callback `OnRun(ProcessRequest)` para inspecionar arquivos durante a execução |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/ClaudeOutputs.cs` | Monta stdout da CLI. Os três auxiliares produzem o **objeto de resultado** da CLI, no mesmo formato das fixtures gravadas, sempre com `"type": "result"` e com os campos que decidem a linha da §2.5 declarados (review S3 da rodada 2): `Structured(sessionId, replyJson)` → `"type": "result"`, `"subtype": "success"`, `"is_error": false`, `"session_id"`, `"result": ""`, `"structured_output": <replyJson>`; `Text(sessionId, resultText)` → `"type": "result"`, `"subtype": "success"`, `"is_error": false`, `"session_id"`, `"result": <resultText>`, sem `structured_output`; `Error(resultText, structuredReplyJson = null)` → `"type": "result"`, `"subtype": "error"`, `"is_error": true`, `"session_id": "s-error"`, `"result": <resultText>` e `structured_output` só quando informado. Mais `ValidReplyJson(label)` (`reply` = `Reply <label>?`, `praise` = `Praise <label>`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/ManualTimeProvider.cs` | `TimeProvider` com `Advance(TimeSpan)` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/RecordingProcessRunner.cs` | (LiveBrain) envolve o `ProcessRunner` real e guarda requests/resultados |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LiveClaudeCode.cs` | (LiveBrain) monta o adaptador real com pasta temporária, `prompts/` da saída e `ENGLISHTUTOR_CLAUDE_PATH` (padrão `claude`); o system prompt dos requests é o conteúdo de `AppContext.BaseDirectory/prompts/tutor.md` (o mesmo que o app usa; review S9) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LatencySummary.cs` | Média e p95 por posto mais próximo |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/claude-json-structured.json` | Saída real, `OutputMode = JsonSchema`, frase "I have worked with C# since five years" |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/claude-json-tags.json` | Saída real, `OutputMode = Tags`, mesma frase |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainTests.cs` | Adaptador com fake de processo |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodePromptRendererTests.cs` | Formato do texto único |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/TutorReplySchemaTests.cs` | Schema canônico |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeOptionsTests.cs` | Padrões |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeOptionsValidatorTests.cs` | Validação |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/FileTutorPromptSourceTests.cs` | Leitura de `tutor.md` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs` | Registros |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LatencySummaryTests.cs` | p95 nos dois lados do posto |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainLiveTests.cs` | `LiveBrain`: fumaça, cenário 1, retomada |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/LatencyMeasurementLiveTests.cs` | `LiveBrain`: medição D7 (nome sem "ClaudeCode", fora do filtro do D5) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/FixtureRecorderLiveTests.cs` | `LiveBrain`: grava as fixtures em `bin/.../recorded-fixtures/` (fora do filtro do D5) |
| Alterar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | `ConversationService` registrado |
| Criar | `tests/EnglishTutor.Console.Tests/Support/ScriptedTutorBrain.cs` | `ITutorBrain` com fila (`Returns`, `Throws`) e `Requests`; sem resposta na fila lança `InvalidOperationException` (nenhum teste do console chama o processo real) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/FixedTutorPromptSource.cs` | System prompt fixo |
| Criar | `tests/EnglishTutor.Console.Tests/Support/ConsoleTutorReplies.cs` | `Valid(label)`: `Reply <label>?`, `raw answer <label>`, `Correction("orig <label>", "fixed <label>", "why <label>")` (AP-026) |
| Alterar | `tests/EnglishTutor.Console.Tests/Support/RecordingChatInputHandler.cs` | `Mode`, `ChangeMode`, `ModeChanges` |
| Alterar | `tests/EnglishTutor.Console.Tests/Support/TestConfiguration.cs` | Parâmetro opcional com valores extras (ex.: `Conversation:MaxTurnsInWindow`, `Brain:ClaudeCode:OutputMode`) |
| Criar | `tests/EnglishTutor.Console.Tests/Ui/ConversationInputHandlerTests.cs` | Exibição e modo |
| Alterar | `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` | `/mode`, `/help`; 2 testes existentes ajustados |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | Ligação ao serviço, binds, várias seções inválidas; 1 teste existente renomeado |
| Criar | `tests/EnglishTutor.Console.Tests/PromptFilesDeploymentTests.cs` | `prompts/` na saída |

## 4. Mapa cenário BDD → testes

**Valores distintos (AP-017, AP-026).** Em todo teste com mais de uma chamada, cada turno tem frase do aluno, `reply` e `RawText` próprios:

- `t1`/`t2`/`t3`, com `ClaudeOutputs.ValidReplyJson(label)` na Infrastructure e `ConsoleTutorReplies.Valid(label)` no Console;
- os `session_id` são distintos (`s-1`, `s-2`, ...);
- a resposta rejeitada (`"I like that."` sem `?`) difere da aceita;
- o system prompt de teste é `SYSTEM PROMPT for test`, diferente de qualquer mensagem (o caso `"system-prompt-changed"` usa `SYSTEM PROMPT v2 for test`);
- o `Model` de teste é `model-under-test` e o `ExecutablePath` é `claude-under-test`, diferentes dos padrões, para a asserção não passar por coincidência com o default;
- nos testes de falha, o texto de cada fonte do detalhe é único (`boom result detail`, `first stderr t2`, `ignored stderr`), para a asserção provar de onde ele veio.

Nos testes do adaptador, a pasta de trabalho é uma pasta temporária única por teste, apagada no `Dispose`.

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| frase com erro gramatical | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion` (`"I have worked with C# since five years"` via `TurnMessageBuilder.Build(..., [], FreeConversation)`; `Reply` termina em `?`; no máximo 3 frases, contando terminadores `.`, `!`, `?` seguidos de espaço ou fim; alguma `Correction` com `Original` contendo `since five years` e `Corrected` contendo `for five years`, sem diferenciar maiúsculas) | LiveBrain |
| frase com erro gramatical (offline, com a saída gravada) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId` (ver "leitura da saída gravada") | Unidade (caracterização) |
| frase com erro gramatical (D5) | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat` (5 frases da tabela abaixo, cada uma num request de 1 mensagem; conta `InvalidOutput` como fora do formato; espera 5/5 no formato e ≥ 4 com `Corrected` contendo o trecho esperado, sem diferenciar maiúsculas) | LiveBrain |
| processo isolado e sem ferramentas | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_Always_RunsIsolatedClaudeWithoutTools` (`FileName == "claude-under-test"`; `Arguments` contém `-p`; valor depois de `--output-format` = `json`; depois de `--tools` = `""` (string vazia); depois de `--disallowedTools` = `mcp__*`; contém `--strict-mcp-config`; depois de `--permission-mode` = `dontAsk`; depois de `--model` = `model-under-test`; contém `--system-prompt-file`; **não** contém `--bare`) | Unidade |
| processo isolado e sem ferramentas | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenWorkingDirectoryDoesNotExist_CreatesItAndKeepsItEmpty` (pasta configurada ainda inexistente; `OnRun` registra `Directory.Exists` = `true` e nenhuma entrada na pasta; `ProcessRequest.WorkingDirectory` = caminho completo dela) | Unidade |
| processo isolado (apoio, AP-016: expansão de variável) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithEnvironmentVariableInWorkingDirectory_ExpandsIt` (variável de ambiente com nome único por teste, ex.: `ENGLISHTUTOR_TEST_<guid>` = pasta temporária, criada e removida no teste; `WorkingDirectory = "%ENGLISHTUTOR_TEST_<guid>%/brain"` → `ProcessRequest.WorkingDirectory` = `<temp>/brain` em caminho completo, sem `%`) | Unidade |
| processo isolado (apoio, P3) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_Always_PassesSystemPromptInTemporaryFileOutsideWorkingDirectory` (`OnRun` lê o arquivo depois de `--system-prompt-file`: conteúdo = `SYSTEM PROMPT for test`; o caminho não fica dentro da pasta de trabalho; depois da chamada, o arquivo não existe) | Unidade |
| processo isolado (apoio, ADR-002) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithOutputMode_AppendsMatchingFormatFileAndSchema` (`[Theory]`: `JsonSchema` → depois de `--append-system-prompt-file` = `PromptFiles.FormatJsonPath` e depois de `--json-schema` = `TutorReplySchema.Json`; `Tags` → `FormatTagsPath` e sem `--json-schema`; saída `Structured`/`Text` conforme o caso) | Unidade |
| processo isolado (apoio, P13, B4: normalização) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog` (`[Theory]`: `"jsonschema"` com saída `Structured` → depois de `--json-schema` = `TutorReplySchema.Json`, depois de `--append-system-prompt-file` = `FormatJsonPath`, entrada 2001 contém `output mode JsonSchema`; `"TAGS"` com saída `Text` → sem `--json-schema`, `FormatTagsPath`, entrada 2001 contém `output mode Tags`; nos dois, a entrada 2001 não contém o valor configurado cru) | Unidade |
| processo isolado (apoio: stdin) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithSingleMessage_SendsItUnchangedAsStandardInput` (stdin = `Content` exato da mensagem RN-CONV02, sem rótulos) | Unidade |
| processo isolado (apoio: cancelamento) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_Always_PassesCallerTokenToProcessRunner` (token de um `CancellationTokenSource` não cancelado; `FakeProcessRunner.Tokens` = `[token]`) | Unidade |
| continuidade de sessão | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_OnSecondTurnWithUseResume_ResumesSessionAndSendsOnlyNewMessage` (1ª saída `Structured("abc", ValidReplyJson("t1"))`; 2º request = `[User(m1), Assistant(reply1.RawText), User(m2)]`, mesmo system prompt; 2ª chamada: valor depois de `--resume` = `abc`; stdin = `m2`) | Unidade |
| continuidade de sessão (apoio, P2: sem estado) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithHistoryAndNoKnownSession_SendsRenderedTranscriptWithoutResume` (adaptador novo; request de 3 mensagens; stdin = `ClaudeCodePromptRenderer.Render(messages)`; sem `--resume`) | Unidade |
| continuidade de sessão (apoio) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenUseResumeIsDisabled_SendsFullTranscriptWithoutResume` (`UseResume = false`; mesmos 2 turnos do teste de retomada; 2ª chamada sem `--resume` e stdin = texto renderizado das 3 mensagens) | Unidade |
| continuidade de sessão (apoio, P1, ADR-007) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript` (`[Theory]`, 5 casos: `"tutor-differs"`, 2º request com `Assistant("another raw answer")`; `"window-slid"`, depois de 2 turnos, 3º request sem o 1º turno; `"after-failure"`, 1ª chamada ok com `s-1`, 2ª falha com exit 1, 3ª repete o request da 2ª; `"system-prompt-changed"` (review S2), 2º request estende exatamente o 1º mas com `SystemPrompt = "SYSTEM PROMPT v2 for test"`; `"session-id-missing"` (review S5c), 1ª saída `Structured("", ValidReplyJson("t1"))` e 2º request que estende exatamente o 1º; em todos, a última chamada não tem `--resume` e o stdin é o texto renderizado) | Unidade |
| continuidade de sessão (apoio, ADR-006 × ADR-007) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenRetryingRejectedAnswer_ResumesWithOnlyFormatCorrection` (1ª saída `Structured("s-1", {"reply":"I like that.",...})` → `BrainException` com `IsInvalidOutput`; 2º request = `[User(m1), Assistant(ex.RawText), User(TurnMessageBuilder.BuildFormatCorrection(ex.Rejection))]`; 2ª chamada: valor depois de `--resume` = `s-1`, stdin = a instrução de correção) | Unidade |
| leitura da saída gravada | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId` (stdout = `Fixtures/ClaudeCode/claude-json-structured.json`; `Reply` termina em `?`; `Corrections` tem um item com `Original` **contendo** `since five years` e `Corrected` **contendo** `for five years`, sem diferenciar maiúsculas, porque o texto real pode trazer a frase inteira (review S13); o 2º turno que estende o 1º tem, depois de `--resume`, o `session_id` lido da fixture com `JsonDocument` no próprio teste) | Unidade (caracterização) |
| leitura da saída gravada (apoio, OutputMode Tags) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedTagsOutput_ReturnsTutorReply` (`OutputMode = Tags`; stdout = `claude-json-tags.json`; `Reply` termina em `?`; `RawText` = campo `result` da fixture) | Unidade (caracterização) |
| leitura da saída (apoio, ADR-006) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenAnswerIsOutOfFormat_ThrowsInvalidOutputWithRawText` (`Tags`; `Text("s-1", "Sure! Here is my answer without tags.")`; `BrainException` com `IsInvalidOutput`, `Rejection!.Reason == TutorReplyRejections.MissingReplyReason`, `RawText` = o texto) | Unidade |
| leitura da saída (apoio, P5, AP-015/016: linha 1 da §2.5) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail` (`[Theory]`: `"exit-code"` com exit 1, stdout vazio e stderr `"\n  boom stderr detail  \nsecond stderr line"` → `Message` contém `Claude Code failed (exit code 1): boom stderr detail` e não contém `second stderr line`; `"is-error"` com exit 1, stdout `Error("boom result detail")` e stderr `ignored stderr` → `Message` contém `Claude Code failed (exit code 1): boom result detail` e não contém `ignored stderr`; nos dois, `Kind == Unknown` e o arquivo temporário do system prompt, lido no `OnRun`, não existe depois da exceção (review S6)) | Unidade |
| leitura da saída (apoio, P5, B3: linha 3 da §2.5, só ela satisfaz) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing` (`[Theory]`, `OutputMode = JsonSchema`, exit **0**, stdout `Error(<result>, ValidReplyJson("t1"))` (um `structured_output` **válido**, para que ignorar `is_error` devolva uma resposta e o teste falhe): `"result-detail"`, `result` = `boom result detail` e stderr `ignored stderr` → `Message` contém `Claude Code reported an error: boom result detail` e não contém `ignored stderr`; `"blank-result-uses-stderr"` (review S5a), `result` = `"   "` e stderr `"\nfirst stderr t2\nsecond stderr t2"` → contém `Claude Code reported an error: first stderr t2` e não contém `second stderr t2`; `"no-details"` (review S5b), `result` = `""` e stderr `""` → contém `Claude Code reported an error: no error details were printed`; nos três, `Kind == Unknown`, `IsInvalidOutput == false` e não contém `Reply t1?`) | Unidade |
| leitura da saída (apoio, P5: linha 2 da §2.5, as duas partes da condição; review S2 da rodada 2) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation` (`[Theory]`, `OutputMode = JsonSchema`, exit 0, stdout por caso: `"not-json"`, `not json at all`; `"other-type"`, `{"type":"system","subtype":"init","is_error":false,"session_id":"s-other","structured_output":<ValidReplyJson("t1")>}` (JSON válido com `structured_output` **válido**, para que ignorar o `type` devolva uma resposta e o teste falhe); `"json-array"`, `[<ClaudeOutputs.Structured("s-array", ValidReplyJson("t1"))>]` (o objeto de resultado válido dentro de um array); nos três, `Kind == Unknown`, `IsInvalidOutput == false`, `Message` contém `is not a JSON result` e `Expected`, e não contém `Reply t1?`) | Unidade |
| medição de latência | Infrastructure.Tests | `LatencyMeasurementLiveTests` | `SendAsync_FiveSentencesInEachOutputMode_ReportsMeanAndP95Latency` (as 5 frases da tabela, primeiro com `JsonSchema` e depois com `Tags`, cada uma num request de 1 mensagem; `Stopwatch` em volta de `SendAsync`; escreve no `TestContext.Current.TestOutputHelper` uma tabela markdown `Modo \| n \| média (ms) \| p95 (ms) \| fora do formato`; falha se algum modo tiver menos de 5 amostras medidas) | LiveBrain |
| medição de latência (apoio, P15, AP-016) | Infrastructure.Tests | `LatencySummaryTests` | `Compute_WithFiveSamples_ReturnsMeanAndMaxAsP95` (100, 200, 300, 400, 1000 ms → média 400, p95 1000) | Unidade |
| medição de latência (apoio, AP-016: outro lado do posto) | Infrastructure.Tests | `LatencySummaryTests` | `Compute_WithTwentySamples_ReturnsNineteenthSmallestAsP95` (1..20 × 10 ms embaralhados → p95 = 190 ms, posto ⌈0,95·20⌉ = 19, não 200) | Unidade |
| console conversa pelo ConversationService | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenUserSendsTwoSentences_SecondRequestCarriesFirstTurnAndShowsBothReplies` (Provider `ClaudeCode`; `ScriptedTutorBrain` via `ConfigureServices`, `Valid("t1")` e `Valid("t2")`; entrada `first sentence t1`, `second sentence t2`, `/quit`; `Requests[1].Messages` com 3 itens: `User` contendo `first sentence t1`, `Assistant("raw answer t1")`, `User` contendo `second sentence t2`; saída contém `Tutor: Reply t1?`, `  - orig t1 → fixed t1 (why t1)`, `Tutor: Reply t2?`, `  - orig t2 → fixed t2 (why t2)`; não contém `The tutor is not connected yet.`) | Componente UI |
| console conversa (apoio, P8) | Console.Tests | `ConversationInputHandlerTests` | `HandleAsync_WhenTutorReplies_ShowsReplyAndFeedbackLines` (`ConversationService` real com `ScriptedTutorBrain`; saída = exatamente `Tutor: Reply t1?`, `Feedback:`, `  - orig t1 → fixed t1 (why t1)`) | Componente UI |
| console conversa (apoio, P8: fallback) | Console.Tests | `ConversationInputHandlerTests` | `HandleAsync_WhenReplyIsRawFallback_ShowsOnlyReply` (cérebro lança `InvalidOutput` 2 vezes, com raw1 ≠ raw2; saída = `Tutor: <raw2 com Trim>`, sem `Feedback:`) | Componente UI |
| console conversa (apoio, P10) | Console.Tests | `ConversationInputHandlerTests` | `HandleAsync_WhenInputIsBlank_ShowsLocalWarning` (`"   "` → saída = `ConversationMessages.EmptyInput`; `Requests` vazio) | Componente UI |
| troca de modo pelo console | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesModeWithKnownName_ChangesModeAndConfirms` (`/mode job-interview`, `/quit`; `ModeChanges` = `[JobInterview]`; saída contém `Mode changed to "job-interview". It applies from your next sentence.`; `Inputs` vazio) | Componente UI |
| troca de modo pelo console | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesHelp_ListsModeCommandWithAllModes` (saída contém `  /mode - Change the mode: free-conversation, job-interview, listening-practice`) | Componente UI |
| troca de modo pelo console | Console.Tests | `EnglishTutorAppTests` | `RunAsync_AfterModeCommand_NextBrainMessageCarriesNewMode` (`/mode job-interview`, `sentence t1`, `/quit`; `Requests[0].Messages[^1].Content` termina com `Mode: job-interview`; saída contém a confirmação) | Componente UI |
| troca de modo (apoio) | Console.Tests | `ConversationInputHandlerTests` | `ChangeMode_WithMode_ChangesConversationServiceMode` (`ChangeMode(ListeningPractice)` → `handler.Mode` e `service.Mode` = `ListeningPractice`) | Componente UI |
| modo desconhecido pelo console | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesModeWithUnknownOrMissingName_ShowsAvailableModesAndKeepsMode` (`[Theory]`: `"/mode interview"` → 1ª linha `Unknown mode "interview".`; `"/mode"` → `Choose a mode: /mode <mode>.`; nos dois, `Available modes: free-conversation, job-interview, listening-practice`; `ModeChanges` vazio; `handler.Mode == FreeConversation`; `Inputs` vazio) | Componente UI |
| modo desconhecido pelo console | Console.Tests | `EnglishTutorAppTests` | `RunAsync_AfterUnknownMode_SendsNothingAndKeepsFreeConversation` (`/mode interview`, `sentence t1`, `/quit`; `Requests` com 1 item, só da frase; `Messages[^1].Content` termina com `Mode: free-conversation`) | Componente UI |
| janela de turnos configurável | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenMaxTurnsInWindowIsZero_ExitsWithInvalidConfigurationBeforePrompt` (`Conversation:MaxTurnsInWindow = 0`; código = `ExitCodes.InvalidConfiguration`; `Error` contém `Invalid configuration:`, `Conversation:MaxTurnsInWindow must be at least 1 (found 0)` e `Expected`; `Output` vazio) | Componente UI |
| janela de turnos configurável (apoio, P19, B1: várias seções inválidas) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (`[Theory]`, `Conversation:MaxTurnsInWindow = 0` nos dois casos: `"provider-and-window"`, `Brain:Provider = "Inexistente"` → `Error` contém `Brain:Provider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.`; `"output-mode-and-window"`, Provider `ClaudeCode` e `Brain:ClaudeCode:OutputMode = "Json"` → `Error` contém `Brain:ClaudeCode:OutputMode 'Json' is invalid. Accepted values: JsonSchema, Tags.`; nos dois: código = `ExitCodes.InvalidConfiguration`, `Error` contém `Invalid configuration:` e `Conversation:MaxTurnsInWindow must be at least 1 (found 0)`, não contém `An unexpected error occurred`; `Output` vazio. A ordem das mensagens não é conferida) | Componente UI |
| (apoio, P7: Ollama ainda não conectado) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected` (renomeado de `RunAsync_WhenUserTypesText_ShowsTutorNotConnected`; Provider `Ollama`, entrada `hello`; saída contém `The tutor is not connected yet.`; `ScriptedTutorBrain.Requests` vazio) | Componente UI (caracterização) |
| (apoio, P11: falha do cérebro até a US-005) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError` (`ScriptedTutorBrain.Throws(new BrainException(BrainErrorKind.Unknown, "Claude Code failed (exit code 1): test"))`; código = `ExitCodes.UnexpectedError`; `Error` contém `An unexpected error occurred. See the log file for details.`. Comentário do teste: substituído na US-005, cenário "falha do cérebro no console não encerra a sessão") | Componente UI |
| (apoio, P12: prompts na saída) | Console.Tests | `PromptFilesDeploymentTests` | `ApplicationFolder_Always_ContainsTutorAndFormatPrompts` (`AppContext.BaseDirectory/prompts/` tem `tutor.md`, `format.json.md` e `format.tags.md`, todos não vazios) | Unidade |
| (apoio, ADR-002) | Infrastructure.Tests | `TutorReplySchemaTests` | `Json_Always_DescribesCanonicalTutorReplyContract` (`JsonDocument.Parse`; `type` = `object`; `required` = os 5 campos; `additionalProperties` = `false`; `corrections.maxItems` = 2; `required` do item = `original`, `corrected`, `explanation`) | Unidade |
| (apoio, P2) | Infrastructure.Tests | `ClaudeCodePromptRendererTests` | `Render_WithSingleMessage_ReturnsItsContentUnchanged` | Unidade |
| (apoio, P2) | Infrastructure.Tests | `ClaudeCodePromptRendererTests` | `Render_WithHistory_LabelsEachMessageAndEndsWithCurrentMessage` (`User("student t1")`, `Assistant("tutor t1")`, `User("student t2")` → texto exato da §2.4) | Unidade |
| (apoio, P13) | Infrastructure.Tests | `ClaudeCodeOptionsTests` | `Constructor_Always_UsesDocumentedDefaults` (`claude`, `haiku`, `%LOCALAPPDATA%/EnglishTutor/brain`, `true`, `JsonSchema`) | Unidade (caracterização) |
| (apoio, P13) | Infrastructure.Tests | `ClaudeCodeOptionsValidatorTests` | `Validate_WhenRequiredValueIsBlank_FailsNamingTheKey` (`[Theory]` `ExecutablePath`, `Model`, `WorkingDirectory` com `"  "`; falha contém `Brain:ClaudeCode:<chave> is required.` e `Expected`) | Unidade |
| (apoio, P13) | Infrastructure.Tests | `ClaudeCodeOptionsValidatorTests` | `Validate_WhenOutputModeIsUnknown_FailsWithAcceptedValues` (`"Json"` → `Brain:ClaudeCode:OutputMode 'Json' is invalid. Accepted values: JsonSchema, Tags.`) | Unidade |
| (apoio, P13) | Infrastructure.Tests | `ClaudeCodeOptionsValidatorTests` | `Validate_WithAcceptedOutputMode_Succeeds` (`[Theory]` `"JsonSchema"`, `"tags"`) | Unidade |
| (apoio, P12) | Infrastructure.Tests | `FileTutorPromptSourceTests` | `GetSystemPromptAsync_WhenTutorFileExists_ReturnsItsContent` (pasta temporária com `tutor.md` = `tutor prompt v1`) | Unidade |
| (apoio, P12) | Infrastructure.Tests | `FileTutorPromptSourceTests` | `GetSystemPromptAsync_WhenTutorFileIsMissing_ThrowsWithPathAndExpectation` (`FileNotFoundException`; `Message` contém o caminho e `Expected prompts/tutor.md`) | Unidade |
| (apoio, ADR-006 item 5) | Infrastructure.Tests | `FileTutorPromptSourceTests` | `GetSystemPromptAsync_AfterFileChanges_ReturnsNewContent` (`v1` e depois `v2` no mesmo arquivo → 2ª leitura = `tutor prompt v2`) | Unidade |
| (apoio: latência em log) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_AfterReply_LogsLatencyWithoutConversationText` (`ManualTimeProvider` avançado 1500 ms no `OnRun`; 1 entrada `Information`, EventId 2001, contém `ClaudeCode`, `1500 ms` e `JsonSchema`; nenhuma entrada contém a frase do aluno, `SYSTEM PROMPT for test` nem o `RawText`) | Unidade |
| (apoio, P17: registro) | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | `AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource` (com `ILogger<>` → `NullLogger<>` e `ClaudeCodeOptions` padrão: `ITutorBrain` é `ClaudeCodeTutorBrain` com `Name == "ClaudeCode"`; `ITutorPromptSource` é `FileTutorPromptSource`) | Unidade |
| (apoio, P13) | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | `AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeOptionsValidator` (`OutputMode = "Json"` → ler `IOptions<ClaudeCodeOptions>.Value` lança `OptionsValidationException` citando `'Json'`) | Unidade |
| console conversa (apoio: DI do serviço) | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_WithBrainAndPromptSource_RegistersConversationServiceAsSingleton` (com `FakeTutorBrain`, `FakeTutorPromptSource` e `NullLogger<>`: duas resoluções devolvem a mesma instância) | Unidade |
| continuidade de sessão (apoio, P1: `SessionHint`) | Core.Tests | `ConversationServiceTests` | `SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` (existente, sem mudança: confere `SessionHint` nulo) | Unidade (regressão) |
| continuidade de sessão (apoio, LiveBrain) | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` (`RecordingProcessRunner`; 2 turnos como no teste unitário de retomada; a 2ª chamada tem `--resume` com o `session_id` da 1ª; a 2ª resposta é válida) | LiveBrain |
| leitura da saída gravada (gravação, P16) | Infrastructure.Tests | `FixtureRecorderLiveTests` | `SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes` (`RecordingProcessRunner`; a frase do cenário 1 em `JsonSchema` e em `Tags`; grava os stdouts em `AppContext.BaseDirectory/recorded-fixtures/claude-json-structured.json` e `claude-json-tags.json`; as duas respostas são válidas) | LiveBrain |

**Frases de fumaça e da medição** (D5, D7):

| # | Frase | `Corrected` deve conter |
|---|---|---|
| 1 | `I have worked with C# since five years` | `for five years` |
| 2 | `Yesterday I go to the office by bus` | `went` |
| 3 | `She don't like debugging on Fridays` | `doesn't` |
| 4 | `I am working here since 2020` | `have been working` |
| 5 | `We discussed about the new architecture` | `discussed the new architecture` |

**Testes existentes alterados** (com o motivo; nenhum é enfraquecido):

- `ChatSessionTests.RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands`: a expectativa passa de `Available commands: /help, /quit` para a linha exata `Available commands: /help, /mode, /quit`, porque `/mode` entra em `Available`.
- `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing`: a entrada `"/mode interview"` vira `"/foo bar"`, e a expectativa vira `Unknown command "/foo".`, porque `/mode` deixa de ser desconhecido (efeito colateral previsto no plano da US-003).
- `EnglishTutorAppTests.RunAsync_WhenUserTypesText_ShowsTutorNotConnected` → renomeado para `RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected`, com Provider `Ollama` (P7). Com `ClaudeCode`, o texto livre agora vai ao cérebro.
- `ChatSessionTests.CreateSession` e `EnglishTutorAppTests.CreateOptions` (helpers): o primeiro deixa de passar `TutorMode`; o segundo passa a registrar sempre um `ScriptedTutorBrain` sem respostas e um `FixedTutorPromptSource` via `ConfigureServices`, para nenhum teste do console lançar o processo real.

**Contagens previstas (fora do `LiveBrain`): 49 métodos novos, 65 casos novos.**

- Infrastructure.Tests: 34 métodos / 48 casos.
  - `ClaudeCodeTutorBrainTests`: 20 / 31 (14 `[Fact]` + 6 `[Theory]`: `WithOutputMode` 2, `WithOutputModeInOtherCase` 2, `WhenRequestDoesNotExtendKnownSession` 5, `WhenClaudeReportsFailure` 2, `WhenClaudeReportsErrorWithExitCodeZero` 3, `WhenOutputIsNotJsonResult` 3).
  - `ClaudeCodePromptRendererTests`: 2 / 2.
  - `TutorReplySchemaTests`: 1 / 1.
  - `ClaudeCodeOptionsTests`: 1 / 1.
  - `ClaudeCodeOptionsValidatorTests`: 3 / 6.
  - `FileTutorPromptSourceTests`: 3 / 3.
  - `InfrastructureServiceCollectionExtensionsTests`: 2 / 2.
  - `LatencySummaryTests`: 2 / 2.
- Console.Tests: 14 métodos / 16 casos.
  - `ConversationInputHandlerTests`: 4 / 4.
  - `ChatSessionTests`: +3 / +4 (2 `[Fact]` + 1 `[Theory]` com 2 casos).
  - `EnglishTutorAppTests`: +6 / +7 (5 `[Fact]` + 1 `[Theory]` com 2 casos; o renomeado não muda a contagem).
  - `PromptFilesDeploymentTests`: 1 / 1.
- Core.Tests: +1 / +1 (`CoreServiceCollectionExtensionsTests`).

`LiveBrain` (fora do D2): 5 métodos / 5 casos (`ClaudeCodeTutorBrainLiveTests` 3, `LatencyMeasurementLiveTests` 1, `FixtureRecorderLiveTests` 1).

Suíte após a US (D2): **216 casos** = Core.Tests 102 + 1 = 103; Console.Tests 25 + 16 = 41; Infrastructure.Tests 24 + 48 = 72. Se o Executor mudar uma contagem, atualiza o cabeçalho, este detalhamento, D2 e D11, e confere a soma (AP-031).

## 5. Tarefas (ordem de execução TDD)

**Pré-condição** (antes da T1): o orquestrador aplica a §2.8 em dois commits, `docs(ai): update spec for US-004` (US-005 e US-006 no `SPRINT_1.md`) e `docs(ai): update architecture for US-004` (`ARCHITECTURE.md` §3/§5/§6.3, `padroes-codigo`, `tdd-dotnet`). Junto vai o ADR-007 em "Aceito", se aprovado.

Regras que valem para todas as tarefas:

- Os esqueletos vêm antes dos testes (AP-013).
- Depois de `x.Should().BeTrue()` em propriedades com `[MemberNotNullWhen]`, use `!` (AP-020).
- Escolha de forma não prevista aqui vai para a §12 num commit `docs(ai)` **antes** do `feat` (AP-014, AP-027).
- Os nomes de teste são os da §4, por extenso (AP-025).
- Os testes `LiveBrain` exigem o Claude Code CLI instalado (`claude.exe` do instalador nativo) e autenticado.

- [ ] T1 — Red: `PromptFilesDeploymentTests.ApplicationFolder_Always_ContainsTutorAndFormatPrompts`, que falha porque `prompts/` não está na saída. Green: `<None Include="..\..\prompts\**" Link=...>` no `EnglishTutor.Console.csproj` e no `EnglishTutor.Infrastructure.Tests.csproj`. No mesmo commit `chore`, entram os `PackageReference` da §6 (Infrastructure: `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`; Infrastructure.Tests: `Microsoft.Extensions.DependencyInjection`), com build verde. (commits: `test(console): prompt files deployed with the app` + `chore(infra): copy prompts to outputs and add options packages`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T2 — Red: esqueletos `BrainOutputModes` (completo: é dado, como `BrainProviders`), `ClaudeCodeOptions` (com os padrões, porque são dado) e `ClaudeCodeOptionsValidator.Validate` (lança `NotImplementedException`). Testes:
  - `ClaudeCodeOptionsTests.Constructor_Always_UsesDocumentedDefaults`: nasce verde (caracterização). Red por mutação temporária não commitada, pelo efeito: "o padrão de `Model` deixa de ser `haiku`". Registrar a falha na tarefa (AP-017, AP-029).
  - `ClaudeCodeOptionsValidatorTests.Validate_WhenRequiredValueIsBlank_FailsNamingTheKey`
  - `ClaudeCodeOptionsValidatorTests.Validate_WhenOutputModeIsUnknown_FailsWithAcceptedValues`
  - `ClaudeCodeOptionsValidatorTests.Validate_WithAcceptedOutputMode_Succeeds`

  Os 6 casos do validador falham com `NotImplementedException`. Green: mensagens da §2.2. (commits: `test(infra): claude code options validation` + `feat(infra): validate claude code options`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T3 — Red: esqueleto `TutorReplySchema.Json` (constante `"{}"`) + `TutorReplySchemaTests.Json_Always_DescribesCanonicalTutorReplyContract`, que falha na asserção (`type` ausente). Green: o schema da §2.3. (commits: `test(infra): tutor reply json schema` + `feat(infra): add tutor reply json schema`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T4 — Red: esqueleto `ClaudeCodePromptRenderer.Render` (lança `NotImplementedException`) + `ClaudeCodePromptRendererTests.Render_WithSingleMessage_ReturnsItsContentUnchanged` e `ClaudeCodePromptRendererTests.Render_WithHistory_LabelsEachMessageAndEndsWithCurrentMessage`. Green: formato da §2.4. (commits: `test(infra): claude code prompt rendering` + `feat(infra): render brain messages for claude code stdin`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T5 — Red: esqueletos `IProcessRunner`/`ProcessRequest`/`ProcessResult`, `PromptFiles`, `ClaudeCodeResult`/`ClaudeCodeOutput` (`Read` lança `NotImplementedException`), `ClaudeCodeLog` (2001 declarado, sem uso) e `ClaudeCodeTutorBrain` (construtor completo da §2.4; `SendAsync` lança `NotImplementedException`). Fakes e apoio: `FakeProcessRunner`, `ClaudeOutputs` (objeto de resultado com `"type": "result"`, §3), `ManualTimeProvider`. Testes, todos com pasta de trabalho **já existente**, `OutputMode` na caixa canônica (`JsonSchema` ou `Tags`), request de 1 mensagem e saída válida (`Structured` ou `Text`):
  - `ClaudeCodeTutorBrainTests.SendAsync_Always_RunsIsolatedClaudeWithoutTools`
  - `ClaudeCodeTutorBrainTests.SendAsync_Always_PassesSystemPromptInTemporaryFileOutsideWorkingDirectory`
  - `ClaudeCodeTutorBrainTests.SendAsync_WithOutputMode_AppendsMatchingFormatFileAndSchema` (2 casos)
  - `ClaudeCodeTutorBrainTests.SendAsync_WithSingleMessage_SendsItUnchangedAsStandardInput`
  - `ClaudeCodeTutorBrainTests.SendAsync_Always_PassesCallerTokenToProcessRunner`

  São 5 métodos / 6 casos, que falham com `NotImplementedException`. Green: passos 1 e 4–8 e 10 da §2.4 (com o `finally` do arquivo temporário) e o caminho feliz do `ClaudeCodeOutput.Read` (linhas 4 e 5 da §2.5). **Estado dos ramos até as tarefas seguintes:**
  - pasta de trabalho: usada como configurada, sem expansão nem criação (T6);
  - `OutputMode` comparado de forma exata: `JsonSchema` → modo schema, `Tags` → modo tags, qualquer outro valor lança `NotImplementedException("US-004 T6")` (T6);
  - sem medição nem log (T6);
  - `ExitCode != 0`, `is_error` (com qualquer código) e resultado do parser inválido lançam `NotImplementedException("US-004 T7")`, e stdout que não é JSON deixa a `JsonException` propagar (T7);
  - o `type` do JSON não é conferido: um objeto com outro `type` segue o caminho feliz das linhas 4 e 5, e um array faz o `JsonElement` lançar `InvalidOperationException` (T7);
  - stdin com mais de uma mensagem leva só `request.Messages[^1].Content`;
  - sem `_session` nem `--resume` (T8).

  (commits: `test(infra): claude code invocation` + `feat(infra): run claude code headless per turn`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T6 — Red (4 métodos / 5 casos):
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenWorkingDirectoryDoesNotExist_CreatesItAndKeepsItEmpty`: falha porque a pasta não é criada.
  - `ClaudeCodeTutorBrainTests.SendAsync_WithEnvironmentVariableInWorkingDirectory_ExpandsIt`: falha porque o caminho mantém `%...%`.
  - `ClaudeCodeTutorBrainTests.SendAsync_AfterReply_LogsLatencyWithoutConversationText`: falha porque não há entrada 2001.
  - `ClaudeCodeTutorBrainTests.SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog` (2 casos): falham com `NotImplementedException("US-004 T6")`, porque `"jsonschema"` e `"TAGS"` não são a caixa canônica.

  Green: passo 2 da §2.4 (expansão, caminho completo, criação, modo canônico via `BrainOutputModes.TryGetCanonical`), medição com `TimeProvider` e log 2001 com o modo canônico. (commits: `test(infra): claude code working directory, output mode and latency log` + `feat(infra): isolate claude code working directory, normalize output mode and log latency`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T7 — Red (4 métodos / 9 casos):
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenAnswerIsOutOfFormat_ThrowsInvalidOutputWithRawText`: recebe `NotImplementedException`.
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail` (2 casos): recebe `NotImplementedException`.
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing` (3 casos): recebe `NotImplementedException` (`is_error` com código 0, estado da T5).
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation` (3 casos, estado da T5): `"not-json"` recebe `JsonException`; `"other-type"` não recebe exceção nenhuma (devolve `Reply t1?`); `"json-array"` recebe `InvalidOperationException`.

  O Executor confere esses motivos antes do Green. Green: a tabela da §2.5 em `ClaudeCodeOutput.Read`, na ordem das linhas 1–5 (a linha 2 com as duas partes da condição), e `BrainException.InvalidOutput` no passo 10. (commits: `test(infra): claude code failures and invalid output` + `feat(infra): translate claude code failures into brain exceptions`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T8 — Red (5 métodos / 9 casos):
  - `ClaudeCodeTutorBrainTests.SendAsync_WithHistoryAndNoKnownSession_SendsRenderedTranscriptWithoutResume`
  - `ClaudeCodeTutorBrainTests.SendAsync_OnSecondTurnWithUseResume_ResumesSessionAndSendsOnlyNewMessage`
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenUseResumeIsDisabled_SendsFullTranscriptWithoutResume`
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript` (5 casos)
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenRetryingRejectedAnswer_ResumesWithOnlyFormatCorrection`

  Motivos do Red (estado da T5): nos testes de texto renderizado, o stdin traz só a última mensagem; nos de retomada, falta `--resume`. Green: passos 3, 4 e 9 da §2.4 (`_session` com system prompt, regra de extensão exata, renderer). `SendAsync_WithSingleMessage_SendsItUnchangedAsStandardInput` segue verde. (commits: `test(infra): claude code session continuity` + `feat(infra): resume claude code session only when the window is extended`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T9 — Red: esqueleto `ProcessRunner` (`RunAsync` lança `NotImplementedException`), `RecordingProcessRunner`, `LiveClaudeCode` (system prompt lido de `AppContext.BaseDirectory/prompts/tutor.md`, review S9) e os testes `LiveBrain` (todos com `[Trait("Category", "LiveBrain")]`):
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat`
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion`
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply`
  - `FixtureRecorderLiveTests.SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes`

  `dotnet test --filter "Category=LiveBrain"` → os 4 falham com `NotImplementedException`. O D2 continua verde, sem `LiveBrain`. Green: `ProcessRunner` da §2.1 (leituras iniciadas antes da escrita do stdin; `IOException` de pipe fechado ignorada). Os 4 ficam verdes com o CLI real. Se a fumaça ficar abaixo de 4/5, parar e registrar na §12 (risco de prompt, não de código). (commits: `test(infra): live claude code smoke tests` + `feat(infra): run external processes with tree kill on cancellation`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T10 — Fixtures: copiar `recorded-fixtures/claude-json-structured.json` e `claude-json-tags.json` (gravados na T9) para `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/`. Depois, conferir que a resposta estruturada tem a correção `since five years` → `for five years` (com "contém", sem diferenciar maiúsculas; se não tiver, rodar o gravador de novo) e que não há dado pessoal (D13). Conferir também que as fixtures têm `"type": "result"`, como os auxiliares de `ClaudeOutputs` (§3); se o formato real divergir dos auxiliares, parar e registrar na §12. Testes:
  - `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId`
  - `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedTagsOutput_ReturnsTutorReply`

  Os dois nascem verdes (caracterização do formato real). Red por mutação temporária não commitada, pelo efeito, uma por vez: (a) "o `session_id` lido deixa de ser guardado" → o primeiro falha na asserção de `--resume`; (b) "o texto do campo `result` deixa de ser usado no modo Tags" → o segundo falha. Registrar as falhas na tarefa. (commit: `test(infra): recorded claude code output fixtures`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T11 — Red: esqueleto `LatencySummary.Compute(IReadOnlyList<TimeSpan>)` (lança `NotImplementedException`; devolve `record LatencyStats(int Count, TimeSpan Mean, TimeSpan P95)`) + `LatencySummaryTests.Compute_WithFiveSamples_ReturnsMeanAndMaxAsP95` e `LatencySummaryTests.Compute_WithTwentySamples_ReturnsNineteenthSmallestAsP95`. Green: média e posto mais próximo `⌈0,95·n⌉`. Depois, `LatencyMeasurementLiveTests.SendAsync_FiveSentencesInEachOutputMode_ReportsMeanAndP95Latency` (LiveBrain), rodado como D7. A tabela vai para §11 e para o ADR-002, numa seção "Medição (US-004)" com data, modelo e máquina. Todo o código desta tarefa fica no projeto de teste, por isso os commits são `test` (review S10). (commits: `test(infra): latency summary statistics` + `test(infra): measure claude code latency per output mode` + `docs(ai): record claude code latency in ADR-002`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T12 — Red: esqueleto `FileTutorPromptSource.GetSystemPromptAsync` (lança `NotImplementedException`) + os 3 testes, que falham com essa exceção:
  - `FileTutorPromptSourceTests.GetSystemPromptAsync_WhenTutorFileExists_ReturnsItsContent`
  - `FileTutorPromptSourceTests.GetSystemPromptAsync_WhenTutorFileIsMissing_ThrowsWithPathAndExpectation`
  - `FileTutorPromptSourceTests.GetSystemPromptAsync_AfterFileChanges_ReturnsNewContent`

  Green: leitura a cada chamada, com `ct`, e mensagem da §2.3. (commits: `test(infra): system prompt from prompts folder` + `feat(infra): read tutor system prompt from file`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T13 — Red: esqueleto `AddEnglishTutorInfrastructure` (só a guarda e `return services`) e os testes:
  - `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource`: falha porque `ITutorBrain` não está registrado.
  - `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeOptionsValidator`: falha porque nada é lançado.
  - `CoreServiceCollectionExtensionsTests.AddEnglishTutorCore_WithBrainAndPromptSource_RegistersConversationServiceAsSingleton`: falha porque o `ConversationService` não está registrado.

  Green: registros da §2.7. (commits: `test(infra): infrastructure and conversation service registration` + `feat(infra): register claude code brain and conversation service`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T14 — Esqueleto (compila, comportamento igual): `IChatInputHandler` com `Mode`/`ChangeMode`; `TutorNotConnectedInputHandler` com o modo num campo; `RecordingChatInputHandler` com `Mode`/`ChangeMode`/`ModeChanges`; `ConversationInputHandler` com `HandleAsync`, `Mode` e `ChangeMode` lançando `NotImplementedException`; `ScriptedTutorBrain`, `FixedTutorPromptSource` e `ConsoleTutorReplies` em `Console.Tests/Support`. Red, os 4 falhando com `NotImplementedException`:
  - `ConversationInputHandlerTests.HandleAsync_WhenTutorReplies_ShowsReplyAndFeedbackLines`
  - `ConversationInputHandlerTests.HandleAsync_WhenReplyIsRawFallback_ShowsOnlyReply`
  - `ConversationInputHandlerTests.HandleAsync_WhenInputIsBlank_ShowsLocalWarning`
  - `ConversationInputHandlerTests.ChangeMode_WithMode_ChangesConversationServiceMode`

  Green: delegação e textos da §2.6 (`ConsoleText.TutorReply`, `FeedbackHeader`, prefixo `  - `). (commits: `test(console): show tutor reply and feedback` + `feat(console): delegate free text to the conversation service`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T15 — Refactor primeiro: o `ChatSession` lê o modo do handler e o construtor perde `TutorMode mode`; `CreateSession` e `EnglishTutorApp.RunSessionAsync` acompanham. Os 30 casos do Console existentes nesse ponto (25 da base + 1 da T1 + 4 da T14) continuam verdes (commit `refactor(console): read session mode from input handler`; review S8). Red:
  - `ChatSessionTests.RunAsync_WhenUserTypesModeWithKnownName_ChangesModeAndConfirms`
  - `ChatSessionTests.RunAsync_WhenUserTypesHelp_ListsModeCommandWithAllModes`
  - `ChatSessionTests.RunAsync_WhenUserTypesModeWithUnknownOrMissingName_ShowsAvailableModesAndKeepsMode` (2 casos)
  - e os 2 testes existentes ajustados (§4).

  Os 3 novos e `RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands` falham na asserção (hoje a saída é `Unknown command "/mode".` e a lista é `/help, /quit`). `RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` com `"/foo bar"` já passa: é regressão. Green, em dois commits por escopo (review S10): primeiro `LocalCommands.Available` com `Mode` (Core); depois `case LocalCommandKind.Mode`, `ConsoleText.ModeChanged` e `ConsoleText.ModeNotRecognized` (Console). (commits: `refactor(console): read session mode from input handler` + `test(console): mode command in the console` + `feat(core): list mode among available local commands` + `feat(console): change tutor mode with /mode`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T16 — Red: esqueleto `AppRunOptions.ConfigureServices`, já aplicado no fim do `BuildHost` (encanamento de teste; sem ele o arranjo não compila). `TestConfiguration.Create` passa a aceitar valores extras. `CreateOptions` registra `ScriptedTutorBrain` e `FixedTutorPromptSource`. O teste existente é renomeado para Ollama (§4). Testes:
  - `EnglishTutorAppTests.RunAsync_WhenUserSendsTwoSentences_SecondRequestCarriesFirstTurnAndShowsBothReplies`: falha porque a saída é `The tutor is not connected yet.` e `Requests` está vazio.
  - `EnglishTutorAppTests.RunAsync_AfterModeCommand_NextBrainMessageCarriesNewMode`: falha porque `Requests` está vazio.
  - `EnglishTutorAppTests.RunAsync_AfterUnknownMode_SendsNothingAndKeepsFreeConversation`: falha porque `Requests` tem 0 itens, não 1.
  - `EnglishTutorAppTests.RunAsync_WhenMaxTurnsInWindowIsZero_ExitsWithInvalidConfigurationBeforePrompt`: falha com código 0 ≠ 2.
  - `EnglishTutorAppTests.RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError`: falha com código 0 ≠ 1.

  `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected` nasce verde (caracterização). Red por mutação temporária, pelo efeito: "o console passa a usar o `ConversationService` para qualquer provedor" → o `ScriptedTutorBrain` sem resposta lança, e o código vira 1. Green:
  - `BuildHost`: `AddEnglishTutorInfrastructure`, binds de `ClaudeCodeOptions` e `ConversationOptions` com `ValidateOnStart`;
  - `RunSessionAsync`: handler por provedor;
  - `appsettings.json` da §2.6;
  - `Program.cs` com `OutputEncoding` (P14, sem teste automatizado; conferido no D6).

  (commits: `test(console): console talks to the conversation service` + `feat(console): connect the console to the claude code tutor`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T17 — Red (B1, P19): `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (2 casos). Com os binds da T16, os dois casos falham com código 1 ≠ 2 e `An unexpected error occurred. See the log file for details.` em `Error`. O Executor confirma no Red que o motivo é o descrito: o evento `UnexpectedError` (1003) do log tem `AggregateException` com `OptionsValidationException` internas. Se o Red não acontecer por esse motivo, parar e registrar na §12. Green: P19 / §2.6 (falhas de validação de `OptionsValidationException` ou de `AggregateException` só com `OptionsValidationException`; `Invalid configuration:` + uma linha por falha; `StartupFailed` com todas; `ExitCodes.InvalidConfiguration`). Os testes de configuração inválida da US-001 e o da T16 seguem verdes. (commits: `test(console): invalid configuration in several sections` + `feat(console): explain every options validation failure at startup`)
  Escolhas de forma: nenhuma | §12 em `<hash>`
- [ ] T18 — DoD: rodar D1–D15, colar em §11 e marcar a §5 com os hashes. Cada `[x]` leva o hash na própria linha (AP-031). (commit: `docs(ai): add DoD evidence for US-004`)

Conferência das contagens por tarefa: `ClaudeCodeTutorBrainTests` = T5 5/6 + T6 4/5 + T7 4/9 + T8 5/9 + T10 2/2 = 20 métodos / 31 casos. Console.Tests = T1 1/1 + T14 4/4 + T15 3/4 + T16 5/5 + T17 1/2 = 14 / 16. Infrastructure.Tests = T2 4/7 + T3 1/1 + T4 2/2 + `ClaudeCodeTutorBrainTests` 20/31 + T11 2/2 + T12 3/3 + T13 2/2 = 34 / 48. Core.Tests = T13 1/1. Total: 49 / 65.

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Options (10.0.13, já no CPM, grupo "Core") | Infrastructure | `IOptions<ClaudeCodeOptions>`, `IValidateOptions<T>` |
| Microsoft.Extensions.DependencyInjection.Abstractions (10.0.13, já no CPM, grupo "Core") | Infrastructure | `TryAddSingleton`/`TryAddEnumerable` na extensão de registro (hoje vem só de forma transitiva) |
| Microsoft.Extensions.DependencyInjection (10.0.13, já no CPM, grupo "Tests") | Infrastructure.Tests | `ServiceCollection.BuildServiceProvider()` nos testes de registro |

Nenhuma versão nova no `Directory.Packages.props` e nenhum pacote novo no Core (`CoreDependencyTests` não muda). `NullLogger<>` vem de `Microsoft.Extensions.Logging.Abstractions`, que já está nos projetos. Sem `Microsoft.Extensions.TimeProvider.Testing`: o `ManualTimeProvider` de ~15 linhas basta.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Algum teste do console lança o `claude` real (lento, depende de rede) | `CreateOptions` registra sempre um `ScriptedTutorBrain` sem respostas, que lança se for chamado. O teste do Ollama não resolve o serviço. `ProcessRunner` só nos testes `LiveBrain` |
| Duas ou mais seções de configuração inválidas ao mesmo tempo viram `AggregateException` e caem no erro inesperado (código 1, sem explicação), regredindo a US-001 (B1) | P19: o `EnglishTutorApp` reconhece a `AggregateException` só com `OptionsValidationException`. T17 com teste de duas seções inválidas, em dois pares; o Executor confirma o motivo do Red no log |
| `Brain:ClaudeCode` inválido impede a partida também com `Brain:Provider = Ollama`, que não usa a seção (review S4 da rodada 2) | Aceito, sem pendência para outra US. O bind com `ValidateOnStart` vale para qualquer provedor, coerente com o registro fixo do `ClaudeCodeTutorBrain` (P17). Também é coerente com a US-007, em que o ClaudeCode pode ser o `FallbackProvider` de um Ollama principal. A saída é a de configuração inválida já testada (`Invalid configuration:` + a mensagem com a chave e os valores aceitos, código 2), então o aluno sabe o que corrigir |
| `OutputMode` aceito em outra caixa roda em silêncio no modo errado (B4) | O adaptador usa só o valor canônico (P13). `SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog` com `"jsonschema"` e `"TAGS"` |
| `is_error: true` com código 0 mandaria o texto do erro ao parser e à tela como fala do professor (B3) | Linha 3 da §2.5, sem chamar o parser; teste com `structured_output` válido, que só passa se o `is_error` for respeitado |
| JSON válido que não é o resultado da CLI (outro `type`, array) cairia na linha 5 com `RawText` vazio: `InvalidOutput` → retentativa → fallback cru vazio na tela (review S2 da rodada 2) | Linha 2 da §2.5 com as duas partes da condição; casos `"other-type"` (com `structured_output` válido, que só passa se o `type` for conferido) e `"json-array"`. Os auxiliares de `ClaudeOutputs` declaram `"type": "result"` (§3), para os testes das linhas 1 e 3 não caírem na linha 2 por acaso |
| `ProcessRunner` sem teste unitário (processo real é proibido na suíte rápida) | Classe pequena e sem regra; exercitada pelos 4 testes `LiveBrain` da T9 e pelo D6. O encerramento por tempo esgotado ganha teste na US-005 |
| Processo que termina antes de ler o stdin derruba a escrita com pipe fechado e esconde o stderr | Leituras iniciadas antes da escrita; `IOException` da escrita ignorada; código de saída e stderr chegam ao adaptador (§2.1, review S4) |
| `claude` instalado como shim `.cmd` (npm): o `cmd.exe` reinterpreta as aspas do JSON Schema | ADR-007 registra o instalador nativo (`claude.exe`) como suportado. `ExecutablePath` aceita caminho completo |
| Configuração de **usuário** da CLI (`~/.claude/CLAUDE.md`, hooks em `~/.claude/settings.json`) entra no contexto ou roda a cada turno; a pasta dedicada só isola a de projeto (review S11) | Aceito de forma explícita nesta US: RN-BRAIN06 trata da pasta de trabalho, e `--tools ""`, `--strict-mcp-config` e `--permission-mode dontAsk` limitam o que roda. Registrado no ADR-007. O Executor anota na §11 se o `claude --help` oferece flag para restringir fontes de configuração; adotá-la é mudança de ADR em outra US, não decisão do Executor |
| Formato JSON da CLI muda (ADR-001) | Fixtures reais gravadas pelo `FixtureRecorderLiveTests`; regravar é rodar um teste |
| Fixture com dado pessoal | D13 procura `Users`, `@` e `sk-ant`. O Executor revisa os campos antes do commit (tdd-dotnet, Fixtures) |
| `--resume` com `--system-prompt-file` reenviado gera erro ou ignora o arquivo | `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` (LiveBrain) confere com o CLI real. Se falhar, parar e registrar na §12 (alternativa: omitir `--system-prompt-file` ao retomar) |
| `tutor.md` alterado no meio da sessão e retomada com o prompt antigo (review S2) | O system prompt faz parte da identidade da sessão (ADR-007, item 3); caso `"system-prompt-changed"` |
| Fumaça abaixo de 4/5 (qualidade do modelo `haiku`) | Parar na T9 e registrar. A correção é de prompt (`tutor.md`) ou de modelo, decidida pelo usuário, não pelo Executor |
| Sessão da CLI com conteúdo diferente da janela (tentativa rejeitada) | Regra de extensão exata (ADR-007). Depois de uma retentativa, o turno seguinte abre sessão nova |
| `--resume` só ajuda enquanto a janela cresce. Pelo `ConversationWindow.Add` (guarda os últimos `MaxTurnsInWindow` turnos já respondidos; o request leva essa janela mais a frase nova), sem retentativas, os turnos 2 a `MaxTurnsInWindow + 1` (padrão: 2 a 11) retomam a sessão. A partir do turno `MaxTurnsInWindow + 2` (padrão: 12), a janela desliza e todo turno abre sessão nova (review S1 das rodadas 1 e 2) | Registrado no ADR-007 ("Consequências"); a US-009 mede a taxa de sessões novas no `eval` |
| `RawText` vazio gera `[tutor]` vazio no texto renderizado (ADR-006) | Aceito: o texto único não é recusado pela CLI como uma mensagem `Assistant` vazia seria numa API de chat |
| Seta `→` ilegível no console do Windows | `OutputEncoding` UTF-8 sem BOM no `Program.cs` (P14); o D6 confere a seta. Efeito colateral conhecido: o code page do console fica UTF-8 depois da execução |
| Variável de ambiente de teste vaza entre testes | Nome único por teste (`ENGLISHTUTOR_TEST_<guid>`), removido no fim |
| Asserção satisfeita por texto externo (AP-015) | As mensagens conferidas têm trechos do projeto (`Claude Code failed (exit code`, `Claude Code reported an error:`, `is not a JSON result`, `Expected prompts/tutor.md`, `Invalid configuration:`) |
| Contagens divergentes no fim (AP-031) | Conferência por tarefa no fim da §5; o Executor atualiza cabeçalho, detalhamento, D2 e D11 juntos |

## 8. Fora de escopo

- Classificação de falhas (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`), `TimeoutSeconds` e mensagens claras ao aluno com a sessão continuando: **US-005**. A spec já cobre a classificação (cenários "CLI não encontrada", "não autenticado", "limite de uso atingido", "tempo esgotado" e D5). A sessão continuando depois de **qualquer** falha do cérebro, inclusive `Unknown`, e a troca do teste da P11 entram na spec da US-005 antes da T1: cenário "falha do cérebro no console não encerra a sessão" e D7 (§2.8, AP-022; B2). Até lá, falha do cérebro encerra com `ExitCodes.UnexpectedError` (P11).
- Timeout: o item [O] "Processos externos com `ArgumentList`, timeout e encerramento da árvore" do `checklist-revisao` §4 vale como **N/A para o timeout** nesta US, por decisão da spec (US-005, cenário "tempo esgotado"; P6). `ArgumentList` e encerramento da árvore no cancelamento valem e estão na §2.1 (review S12).
- Ollama no console: **US-006**, com o cenário "console conversa com o Ollama" acrescentado à spec antes da T1 (§2.8, AP-022).
- `BrainFactory`, troca de cérebro por configuração e fallback: US-007. Suíte de contrato: US-008, que vai consumir o `FakeProcessRunner` e as fixtures desta US.
- Comando `eval`, golden set e relatório: US-009. O `LatencySummary` desta US mora no projeto de teste, e a US-009 decide se o promove.
- `ClaudeCodeStreamingBrain` (processo persistente, `stream-json`): Fase 6.
- Persistência de turnos e resumo de erros recorrentes (RN-PERS*, RN-CONV04), voz e TTS.
- Validação de "no máximo 3 frases" (RN-FEED01) no Core: fica no prompt e no teste `LiveBrain` (P18). Mudar o `TutorReplyValidator` seria decisão do ADR-005, revisitável na US-009.
- Remover `BrainRequest.SessionHint` do contrato: fica reservado (P1).
- Restringir as fontes de configuração de usuário da CLI: risco aceito (§7); sem US de destino até haver decisão de ADR.

## 9. Perguntas em aberto

- **P1 — Como a continuidade de sessão chega ao adaptador (D10).** **Padrão proposto:** o `session_id` fica guardado no adaptador, junto com o system prompt e as mensagens que a sessão conhece. `--resume` só vale quando o request tem o mesmo system prompt, repete exatamente essas mensagens e acrescenta uma única mensagem `User`. Em qualquer outro caso (inclusive `session_id` ausente ou vazio), abre sessão nova com a janela completa (ADR-007). `SessionHint` continua `null` e reservado. Alternativa: o serviço preenche `SessionHint`, o que exige o id no `TutorReply` e muda o contrato da §4. Testes: `ClaudeCodeTutorBrainTests.SendAsync_OnSecondTurnWithUseResume_ResumesSessionAndSendsOnlyNewMessage`, `ClaudeCodeTutorBrainTests.SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript`, `ClaudeCodeTutorBrainTests.SendAsync_WhenRetryingRejectedAnswer_ResumesWithOnlyFormatCorrection` e `ConversationServiceTests.SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast`.
- **P2 — Como o histórico vai ao `claude -p` sem `--resume`.** **Padrão proposto:** texto único no stdin, com mensagens anteriores rotuladas `[student]`/`[tutor]` e a atual depois de `Current message:`. Uma mensagem só vai sem rótulos. Alternativa: `--input-format stream-json`, que não aceita histórico de `Assistant`. Testes: `ClaudeCodePromptRendererTests.Render_WithHistory_LabelsEachMessageAndEndsWithCurrentMessage`, `ClaudeCodePromptRendererTests.Render_WithSingleMessage_ReturnsItsContentUnchanged` e `ClaudeCodeTutorBrainTests.SendAsync_WithHistoryAndNoKnownSession_SendsRenderedTranscriptWithoutResume`.
- **P3 — Onde fica o arquivo do system prompt.** **Padrão proposto:** arquivo temporário com nome único na pasta temporária do sistema, apagado ao fim da chamada, inclusive em falha; a pasta de trabalho fica vazia (RN-BRAIN06). Alternativas: arquivo dentro da pasta de trabalho, ou `--system-prompt <texto>`. Testes: `ClaudeCodeTutorBrainTests.SendAsync_Always_PassesSystemPromptInTemporaryFileOutsideWorkingDirectory`, `ClaudeCodeTutorBrainTests.SendAsync_WhenWorkingDirectoryDoesNotExist_CreatesItAndKeepsItEmpty` e `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail`.
- **P4 — Qual campo da saída é o `RawText`.** **Padrão proposto:** `structured_output` (texto JSON bruto) quando for objeto; senão, `result`. Testes: `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId` e `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedTagsOutput_ReturnsTutorReply`.
- **P5 — Falhas do processo nesta US.** **Padrão proposto:** as linhas 1–3 da §2.5, nessa ordem: código de saída ≠ 0 → `Claude Code failed (exit code <n>): <detalhe>`; saída que não é o objeto JSON da CLI (texto que não é JSON, ou JSON válido que não é um objeto com `"type": "result"`, como array ou outro `type`) → `... is not a JSON result. Expected ...`; código 0 com `is_error: true` → `Claude Code reported an error: <detalhe>`, sem chamar o parser (B3). Todas viram `BrainException(Unknown)`. O detalhe é o `result` do erro, senão a primeira linha não vazia do stderr, senão `no error details were printed`. Executável inexistente propaga `Win32Exception` (§2.1). A classificação fina é da US-005. Testes: `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail`, `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing` e `ClaudeCodeTutorBrainTests.SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation`.
- **P6 — Tempo máximo nesta US.** **Padrão proposto:** sem `TimeoutSeconds`; o tempo esgotado é da US-005 (cenário "tempo esgotado"). O item de timeout do `checklist-revisao` §4 é N/A nesta US (§8). O `ProcessRunner` já encerra a árvore do processo quando o `CancellationToken` é cancelado (Ctrl+C), e o adaptador repassa o token do chamador sem troca. Teste: `ClaudeCodeTutorBrainTests.SendAsync_Always_PassesCallerTokenToProcessRunner`.
- **P7 — Console com `Brain:Provider = Ollama` antes da US-006.** **Padrão proposto:** o console mantém o `TutorNotConnectedInputHandler` (`The tutor is not connected yet.`) e não resolve o `ConversationService`. A ligação ao Ollama vai para a spec da US-006 (§2.8). Alternativa: registrar um cérebro "não conectado" (provisório descartável). Teste: `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected`.
- **P8 — Formato da resposta na tela.** **Padrão proposto:** `Tutor: <reply>`; depois, se houver feedback, `Feedback:` e uma linha `  - <item>` por item do `FeedbackFormatter`. No fallback cru, só a linha `Tutor:`. Testes: `ConversationInputHandlerTests.HandleAsync_WhenTutorReplies_ShowsReplyAndFeedbackLines` e `ConversationInputHandlerTests.HandleAsync_WhenReplyIsRawFallback_ShowsOnlyReply`.
- **P9 — Textos do `/mode` e do `/help`.** **Padrão proposto:** os da tabela da §2.6 (confirmação, nome desconhecido, nome ausente, linha do `/help`, ordem `/help, /mode, /quit`). Testes: `ChatSessionTests.RunAsync_WhenUserTypesModeWithKnownName_ChangesModeAndConfirms`, `ChatSessionTests.RunAsync_WhenUserTypesModeWithUnknownOrMissingName_ShowsAvailableModesAndKeepsMode` e `ChatSessionTests.RunAsync_WhenUserTypesHelp_ListsModeCommandWithAllModes`.
- **P10 — Quem trata a linha em branco no console.** **Padrão proposto:** o `ChatSession` continua tratando (vale também para o handler não conectado). O `ConversationInputHandler` mostra o `Warning` do serviço se um texto em branco chegar até ele. Testes: `ChatSessionTests.RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning` (existente, regressão) e `ConversationInputHandlerTests.HandleAsync_WhenInputIsBlank_ShowsLocalWarning`.
- **P11 — Falha do cérebro durante a sessão, até a US-005.** **Padrão proposto:** a `BrainException` propaga e o app encerra com `ExitCodes.UnexpectedError` e `An unexpected error occurred. See the log file for details.`, como qualquer erro inesperado (US-001). A troca por mensagem clara com a sessão continuando, para qualquer falha que chegue ao console (inclusive `Unknown`), e a substituição deste teste ficam registradas na spec da US-005 (cenário "falha do cérebro no console não encerra a sessão" e D7, §2.8; B2). Teste: `EnglishTutorAppTests.RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError`.
- **P12 — Onde ficam os prompts em tempo de execução.** **Padrão proposto:** cópia de `prompts/**` na pasta `prompts/` ao lado do executável, resolvida pelo content root, sem chave de configuração. O `tutor.md` é lido a cada turno. Os testes `LiveBrain` usam o mesmo `prompts/tutor.md` da saída. Testes: `PromptFilesDeploymentTests.ApplicationFolder_Always_ContainsTutorAndFormatPrompts`, `FileTutorPromptSourceTests.GetSystemPromptAsync_WhenTutorFileIsMissing_ThrowsWithPathAndExpectation` e `FileTutorPromptSourceTests.GetSystemPromptAsync_AfterFileChanges_ReturnsNewContent`.
- **P13 — Onde ficam as Options do Claude Code, o que elas validam e quem normaliza `OutputMode`.** **Padrão proposto:** `ClaudeCodeOptions` e o validador na Infrastructure (registrado por ela), bind no composition root (ADR-004). Valores obrigatórios: `ExecutablePath`, `Model`, `WorkingDirectory`; `OutputMode` em `JsonSchema`/`Tags`, sem diferenciar maiúsculas. As Options guardam o valor configurado; o **adaptador** normaliza a cada chamada com `BrainOutputModes.TryGetCanonical` e usa só o canônico nos argumentos, no arquivo de formato e no log 2001 (B4; mesmo padrão de `Brain:Provider`, US-001 P4). Alternativa: `PostConfigure` nas Options (descartada na §2). Testes: `ClaudeCodeOptionsTests.Constructor_Always_UsesDocumentedDefaults`, `ClaudeCodeOptionsValidatorTests.Validate_WhenRequiredValueIsBlank_FailsNamingTheKey`, `ClaudeCodeOptionsValidatorTests.Validate_WhenOutputModeIsUnknown_FailsWithAcceptedValues`, `ClaudeCodeOptionsValidatorTests.Validate_WithAcceptedOutputMode_Succeeds`, `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeOptionsValidator` e `ClaudeCodeTutorBrainTests.SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog`.
- **P14 — Codificação da saída do console.** **Padrão proposto:** `Console.OutputEncoding = new UTF8Encoding(false)` no `Program.cs`, para a seta `→` do feedback (pendência do `docs/us/US-002.md`). O `Program.cs` não roda nos testes em processo (ADR-004), e a conferência é o D6. Sem efeito observável nos testes automatizados.
- **P15 — Forma do "comando de medição" de latência.** **Padrão proposto:** um teste `LiveBrain` (`LatencyMeasurementLiveTests`) rodado com `--logger "console;verbosity=detailed"`, que imprime a tabela. O p95 é por posto mais próximo, `⌈0,95·n⌉`; com n = 5, ele é o maior valor. Alternativa: subcomando no console (fica para o `eval` da US-009). Testes: `LatencySummaryTests.Compute_WithFiveSamples_ReturnsMeanAndMaxAsP95` e `LatencySummaryTests.Compute_WithTwentySamples_ReturnsNineteenthSmallestAsP95`.
- **P16 — Como as fixtures reais são gravadas.** **Padrão proposto:** pelo `FixtureRecorderLiveTests`, com o mesmo adaptador e um `RecordingProcessRunner`. Ele grava em `bin/.../recorded-fixtures/`, e o Executor copia para `Fixtures/ClaudeCode/` depois de revisar (D13). Alternativa: rodar o `claude` à mão no PowerShell, onde as aspas do schema quebram no 5.1. Teste: `FixtureRecorderLiveTests.SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes`.
- **P17 — Registro do `ITutorBrain` antes da `BrainFactory`.** **Padrão proposto:** `ClaudeCodeTutorBrain` registrado sempre como `ITutorBrain` pela Infrastructure. O console só o usa com Provider `ClaudeCode` (P7). A US-007 troca pelo registro por configuração. Consequência aceita (review S4 da rodada 2): a seção `Brain:ClaudeCode` é validada na partida com qualquer provedor, então um `Brain:ClaudeCode` inválido impede a partida também com `Brain:Provider = Ollama` (`Invalid configuration:` + a mensagem do validador, código 2). Não fica pendência para a US-007, porque lá o ClaudeCode pode ser o `FallbackProvider` de um Ollama principal (§7). Teste: `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource`.
- **P18 — "No máximo 3 frases" (RN-FEED01).** **Padrão proposto:** conferido só no teste `LiveBrain` do cenário 1 (contando terminadores de frase); o `TutorReplyValidator` (ADR-005) não muda. Teste: `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion`.
- **P19 — Configuração inválida em mais de uma seção ao mesmo tempo (B1).** **Padrão proposto:** com `BrainOptions`, `ClaudeCodeOptions` e `ConversationOptions` validadas na partida, o `ValidateOnStart` lança `OptionsValidationException` quando uma só classe falha e `AggregateException` (uma `OptionsValidationException` por classe) quando mais de uma falha. O `EnglishTutorApp` trata as duas formas como configuração inválida: escreve `Invalid configuration:` e uma linha `  <falha>` para cada falha de cada classe, na ordem em que vêm; loga `StartupFailed` com todas; devolve `ExitCodes.InvalidConfiguration`, sem cabeçalho nem prompt (mesmos critérios da US-001, AP-024). `AggregateException` com interna de outro tipo continua erro inesperado (§2.9). Alternativa: um validador único para as três seções (acopla Core e Infrastructure). Teste: `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration`.

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell. As exceções são o D15 (Git Bash) e o D6/D9 (Windows PowerShell 5.1). D5, D6 e D7 exigem o Claude Code CLI instalado e autenticado.

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: 216 casos (Core.Tests 103, Console.Tests 41, Infrastructure.Tests 72), em < 30 s, sem rede, sem GPU e sem processo real |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ClaudeCode"` | verde: 5 frases de fumaça com erros conhecidos, ≥ 4 com a correção esperada e 5/5 no formato (os 3 testes de `ClaudeCodeTutorBrainLiveTests`; medição e gravador ficam fora do filtro) |
| D6 | No Windows PowerShell 5.1: `"I have worked with C# since five years" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | cabeçalho `Brain: ClaudeCode \| Mode: free-conversation`; `Tutor: ...` terminando em pergunta; linha de feedback com `since five years → for five years` (seta legível); `exit=0` (a entrada termina depois da frase); a primeira linha chega com BOM (AP-006) |
| D7 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~LatencyMeasurementLiveTests" --logger "console;verbosity=detailed"` | tabela com média e p95 de `JsonSchema` e de `Tags` (5 frases cada), colada nas Evidências e no ADR-002 |
| D8 | `Select-String -Path src/EnglishTutor.Console/appsettings.json -Pattern '"MaxTurnsInWindow": 10'` | uma linha, dentro da seção `Conversation` (RN-CONV03) |
| D9 | No Windows PowerShell 5.1: `"/mode job-interview", "/help", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | aviso `Mode changed to "job-interview". It applies from your next sentence.`; `/help` lista `/mode`; nenhum `Unknown command`; `exit=0` |
| D10 | Inspeção de `tasks/US-004/plan.md` | a P1 decide como a continuidade de sessão chega ao adaptador (`session_id` guardado no adaptador, RN-BRAIN01), com testes |
| D11 | `dotnet test --filter "FullyQualifiedName~ClaudeCodeTutorBrainTests"` | 31 casos verdes (20 métodos); os testes `LiveBrain` têm outra classe (`ClaudeCodeTutorBrainLiveTests`) e não entram |
| D12 | `Test-Path "$env:LOCALAPPDATA\EnglishTutor\brain"; (Get-ChildItem -Force -Recurse "$env:LOCALAPPDATA\EnglishTutor\brain").Count` (após D6) | duas linhas: `True` e `0` (a pasta existe e está vazia, RN-BRAIN06; review S7) |
| D13 | `Select-String -Path tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/*.json -Pattern 'Users','@','sk-ant'` | nenhuma linha (fixtures sem caminho pessoal, e-mail ou chave) |
| D14 | `git status --short` após D2–D13 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |
| D15 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |

D15 (fora da tabela por causa do `|`; forma única do `checklist-revisao` §4, AP-019):

```bash
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
```

Também entram, da DoD comum:

- todo cenário BDD com teste escrito antes do código;
- `review.md` sem bloqueantes;
- `docs/us/US-004.md` criado;
- ADR-007 aceito;
- `ARCHITECTURE.md` §3/§5/§6.3, `padroes-codigo`, `tdd-dotnet` e a US-005 e a US-006 do `SPRINT_1.md` atualizados antes da T1 (§2.8);
- tabela de latência no ADR-002 (D7);
- merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-17 — pedido do Revisor do plano (rodada 1, `plan-review.md`)

Corrigir os bloqueantes abaixo (texto completo e "Esperado" em [plan-review.md](plan-review.md), Rodada 1):

- **B1** — Com `BrainOptions`, `ClaudeCodeOptions` e `ConversationOptions` validadas na partida, falhas simultâneas de mais de uma seção viram `AggregateException` e caem no `catch (Exception)` (erro inesperado, código 1). Decidir numa P* como a saída mostra `Invalid configuration:` com a mensagem de cada validador e `ExitCodes.InvalidConfiguration`; mapear teste com duas seções inválidas (§4), tarefa (§5), contagens, D2 e risco (§7).

  → Incorporado. B1 → §2.6 (composition root: as duas formas de exceção), §2.9 (`AggregateException` mista), §2 "Alternativas descartadas", §3 (`EnglishTutorApp.cs`, `EnglishTutorAppTests.cs`, `TestConfiguration.cs`), §4 (`EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration`, 2 casos: Provider + janela e OutputMode + janela), §4 contagens, §5 T17 (nova; Red confirmado pelo log com `AggregateException`; a antiga T17 virou T18), §7 (linha nova), §9 P19 (nova), §10 D2 (214).

- **B2** — A P11 adia para a US-005 "mensagem clara com a sessão continuando" para qualquer falha (inclusive `Unknown`), mas a spec da US-005 não tem isso. Trazer na §2.8 o texto exato a acrescentar na US-005 (commit `docs(ai): update spec for US-004` antes da T1) e corrigir a §8.

  → Incorporado. B2 → §2.8 (cenário "falha do cérebro no console não encerra a sessão" e linha D7 na DoD extra da US-005, com o texto antigo de referência; critérios reaproveitados: saída de erro, código 0 no `/quit`, texto `Claude Code failed (exit code <n>): ...`), §3 (`SPRINT_1.md`), §4 (comentário do teste da P11), §5 pré-condição, §8 (primeiro item reescrito, sem "Nada novo a registrar"), §9 P11, §10 DoD comum.

- **B3** — A linha `is_error: true` com código de saída 0 da tabela de `ClaudeCodeOutput.Read` (§2.5) não é exercitada. Teste para "código 0 e `is_error: true`" → `BrainException(Unknown)` com detalhe, sem chamar o parser; definir o texto da mensagem; atualizar contagens, §5 e D11.

  → Incorporado. B3 → §2.5 (tabela numerada e ordenada; linha 3 com `Claude Code reported an error: <detalhe>`, sem chamar o parser), §3 (`ClaudeOutputs.Error(resultText, structuredReplyJson = null)`), §4 (`ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing`, 3 casos, com `structured_output` válido para provar que o parser não é chamado), §4 contagens, §5 T5 (estado: `is_error` com qualquer código) e T7 (4/7), §7 (linha nova), §9 P5, §10 D11 (29 casos / 20 métodos), ADR-007 item 5.

- **B4** — `OutputMode` aceito sem diferenciar maiúsculas pelo validador, mas sem dono da normalização no adaptador. A P13 decide quem normaliza; §4/§5 mapeiam teste com valor aceito em outra caixa produzindo os argumentos e o arquivo de formato do modo correspondente.

  → Incorporado. B4 → §2.2 (parágrafo "Normalização de `OutputMode`": o adaptador é o dono), §2.4 (passos 2, 6 e 10 usam `mode` canônico), §2.9 (falha de `TryGetCanonical`), §2 "Alternativas descartadas" (`PostConfigure`), §4 (`ClaudeCodeTutorBrainTests.SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog`, `"jsonschema"` e `"TAGS"`, com argumentos, arquivo de formato e log 2001), §5 T5 (estado: valor fora da caixa canônica lança `NotImplementedException("US-004 T6")`, para os dois casos nascerem vermelhos) e T6 (4/5), §7 (linha nova), §9 P13.

**Sugestões do `plan-review.md` (rodada 1), não obrigatórias.** Incorporadas todas, por serem baratas e coerentes:

- S1 → ADR-007 "Consequências" (otimização só nos primeiros `MaxTurnsInWindow` turnos; sessão gravada no perfil do usuário), §7.
- S2 → §2.4 passos 3 e 9 (`ClaudeCodeSession` com `SystemPrompt`), §4 caso `"system-prompt-changed"`, §5 T8 (5/9), §7, §9 P1, ADR-007 item 3; §2.8 texto novo da ARCHITECTURE §5.
- S3 → §2.1 (último parágrafo), §2.8 (texto novo da ARCHITECTURE §6.3 diz a exceção do executável inexistente), §9 P5, ADR-007 item 5.
- S4 → §2.1 (ordem numerada: leituras antes da escrita; `IOException` de pipe fechado ignorada), §5 T9, §7, ADR-007 item 1.
- S5 → (a) e (b) nos casos `"blank-result-uses-stderr"` e `"no-details"` do teste de B3; (c) no caso `"session-id-missing"` de `SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript`; §2.5, §4, §5 T7/T8.
- S6 → §2.4 passo 5, §4 (`SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail` confere que o arquivo temporário não existe depois da exceção), §9 P3, ADR-007 item 4. O caso de cancelamento não ganha teste próprio: o `finally` é o mesmo.
- S7 → §10 D12 (`Test-Path` → `True` e contagem `0`).
- S8 → §5 T15 (30 casos no ponto do Refactor).
- S9 → §3 (`LiveClaudeCode` lê `prompts/tutor.md` da saída), §5 T9, §9 P12.
- S10 → §5 T11 (commits `test(infra)`) e T15 (commit `feat(core)` separado para `LocalCommands.Available`).
- S11 → §7 (risco aceito de forma explícita, com anotação na §11), §8, ADR-007 "Consequências".
- S12 → §8 (item de timeout do `checklist-revisao` §4 = N/A nesta US), §9 P6.
- S13 → §4 (correção conferida com "contém", sem diferenciar maiúsculas), §5 T10.

### 2026-10-17 — pedido do usuário (sugestões da rodada 2, `plan-review.md`)

Incorporar as sugestões S1–S4 da Rodada 2 (texto completo e "Esperado" em [plan-review.md](plan-review.md), Rodada 2):

- **S1** — Corrigir a contagem no ADR-007 ("Consequências") e na §7: com `ConversationWindow.Add`, o turno `MaxTurnsInWindow + 1` ainda estende a sessão; a janela só desliza a partir do turno `MaxTurnsInWindow + 2` (padrão 12). `--resume` ajuda nos primeiros `MaxTurnsInWindow + 1` turnos. Conferir no código antes de escrever.

  → Incorporado. S1 → ADR-007 "Consequências" (terceiro item de "Negativas / riscos") e cabeçalho "Data", §3 (linha do ADR-007), §7 (linha "`--resume` só ajuda enquanto a janela cresce"). Conferido no código antes de escrever: `ConversationWindow.Add` só remove o turno mais antigo quando `_turns.Count > _maxTurns`, e `ConversationService.SendAsync` monta o request com `_window.ToMessages()` + a frase nova e só depois chama `_window.Add`. Com `MaxTurnsInWindow = 10` e sem retentativas: depois do turno 10, a sessão conhece os turnos 1–10, e o request do turno 11 leva os turnos 1–10 + a frase nova, então retoma. Depois do turno 11, a janela fica com os turnos 2–11, e o request do turno 12 não repete o que a sessão conhece (turnos 1–11), então abre sessão nova. Daí em diante, a sessão nova sempre conhece um turno a mais no início do que a janela seguinte (ex.: turnos 2–12 contra a janela 3–12 no turno 13), então todo turno abre sessão nova. O turno 1 nunca retoma, porque não há sessão. Por isso o texto diz "turnos 2 a `MaxTurnsInWindow + 1` (padrão: 2 a 11)" e "a partir do turno `MaxTurnsInWindow + 2` (padrão: 12)". A resposta da S1 da rodada 1, acima, fica como histórico, com a contagem antiga.

- **S2** — Linha 2 da §2.5: um JSON válido sem `"type": "result"` (outro `type`, array) também resulta na `BrainException(Unknown)` da linha 2. Caso de teste próprio; atualizar contagens, §5, D2 e D11.

  → Incorporado. S2 → §2.5 (parágrafo de abertura: a condição composta tem um caso por parte; linha 2 da tabela reescrita com as duas partes), §4 (`ClaudeCodeTutorBrainTests.SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation` passa de `[Fact]` a `[Theory]` com 3 casos: `"not-json"`, `"other-type"` com `structured_output` válido, para que ignorar o `type` devolva uma resposta e o teste falhe, e `"json-array"`), §4 contagens (`ClaudeCodeTutorBrainTests` 20/31, Infrastructure.Tests 34/48, total 49/65, suíte 216), §5 T5 (estado: o `type` não é conferido; objeto de outro `type` segue o caminho feliz, array lança `InvalidOperationException`), §5 T7 (4/9, motivo do Red por caso), §5 conferência das contagens, §7 (linha nova), §9 P5, §10 D2 (216; Infrastructure.Tests 72) e D11 (31 casos / 20 métodos), ADR-007 item 5. O nome do método não muda, então nenhuma P* nem tarefa precisa trocar de nome.

- **S3** — §3, `Support/ClaudeOutputs.cs`: os auxiliares `Structured`, `Text` e `Error` produzem o objeto de resultado da CLI com `"type": "result"`, no formato das fixtures gravadas.

  → Incorporado. S3 → §3 (`ClaudeOutputs.cs`: os três auxiliares declaram `"type": "result"`, `subtype`, `is_error`, `session_id`, `result` e, quando houver, `structured_output`), §5 T5 (menção aos auxiliares), §5 T10 (o Executor confere que as fixtures reais têm `"type": "result"` e para, registrando na §12, se o formato divergir dos auxiliares), §7 (linha da S2, parte da mitigação).

- **S4** — Registrar na §7 ou na P17 que um `Brain:ClaudeCode` inválido impede a partida mesmo com `Brain:Provider = Ollama`, comportamento aceito até a `BrainFactory` (US-007).

  → Incorporado com um ajuste. S4 → §7 (linha nova), §9 P17. O ajuste: o plano registra o comportamento como aceito **sem** "até a US-007". Se o texto prometesse uma mudança na US-007, seria um adiamento que precisaria entrar na spec da US-007 (AP-022). E a US-007 tem um bom motivo para manter a validação: lá o ClaudeCode pode ser o `FallbackProvider` de um Ollama principal (cenário "fallback por limite de uso"). Se a US-007 quiser validar a seção só quando o ClaudeCode estiver em uso, decide isso na própria spec. A saída nesse caso é a de configuração inválida que já tem teste (`Invalid configuration:` + a mensagem com a chave e os valores aceitos, código 2). Sem teste novo: o mecanismo é o mesmo de `RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` e dos testes de configuração inválida da US-001.

Depois desses ajustes e de uma revisão do plano sem bloqueantes, o plano está aprovado pelo usuário.

### 2026-10-17 — aprovação do usuário

Plano aprovado pelo usuário no chat, com as sugestões S1–S4 da rodada 2 incorporadas e a rodada 3 da revisão do plano sem bloqueantes.

- Perguntas em aberto P1–P19: valem as respostas padrão propostas na §9.
- ADR-007: Aceito (2026-10-17).
- §2.8 aplicada antes da T1: `docs(ai): update spec for US-004` (f42d0d7) e `docs(ai): update architecture for US-004` (5917a1d).
- A sugestão S1 da rodada 3 (justificativa da P17/§7 cita o cenário de fallback da US-007 com papéis invertidos) não foi aplicada; a decisão da P17 não muda.

### 2026-10-17 — escolhas de forma do Executor (AP-009, AP-014)

- **E1 (T6)** — Mensagem da `InvalidOperationException` defensiva do adaptador quando `BrainOutputModes.TryGetCanonical` falha (§2.9, sem teste): `Brain:ClaudeCode:OutputMode '<valor>' is invalid. Expected one of: JsonSchema, Tags.`
- **E2 (T9–T11, bloqueio)** — O Claude Code CLI **não está instalado** nesta máquina (2026-10-17 e de novo em 2026-10-19: `Get-Command claude` vazio, `where.exe claude` sem resultado, sem `%USERPROFILE%\.local\bin\claude.exe`). Conforme a instrução do orquestrador, o Executor parou nesse ponto sem improvisar:
  - T9: só o Red foi feito (`d6ed73e`; os 4 testes `LiveBrain` falham com `NotImplementedException`). O Green (`ProcessRunner` da §2.1) **não** foi implementado: sem o CLI, não há como ver os testes verdes, e um `feat` sem Green viola o TDD. Até lá, o `ProcessRunner` registrado no DI lança `NotImplementedException`, então o app real com `Brain:Provider = ClaudeCode` encerra com erro inesperado no primeiro turno.
  - T10: pendente (depende das fixtures gravadas na T9).
  - T11: feita só a parte unitária (`LatencySummary` + 2 testes) e o código do `LatencyMeasurementLiveTests`, que não foi executado; a tabela no ADR-002 fica pendente.
  - D5, D6, D7 e D12 (depende do D6): não executados. D2 e D11 ficam com 2 casos a menos que o previsto (sem os 2 testes da T10).
  - As tarefas T12–T17 não dependem do CLI e foram feitas normalmente.
  - Para retomar: instalar o CLI pelo instalador nativo (`claude.exe`, ADR-007), autenticar e rodar o Executor a partir do Green da T9.
