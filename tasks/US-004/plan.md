# Plano — US-004 Conversar por texto com o professor via Claude Code

Status: Aprovado em 2026-10-20 (revisão da RN-FEED01, §2.10 e T18–T23; a versão anterior foi aprovada em 2026-10-17)
Branch: feature/US-004_conversa-texto-claude-code
Spec: specs/sprints/SPRINT_1.md#us-004
Regras: RN-BRAIN01, RN-BRAIN02, RN-BRAIN05, RN-BRAIN06, RN-CONV01, RN-CONV03, RN-CONV05, RN-CONV06, RN-FEED01 (alterada em 2026-10-19, §2.10), RN-FEED02–05 (RN-BRAIN07/09 só no limite descrito em §8); ARCHITECTURE §2 (R1–R4), §3, §5, §6.1, §6.2, §6.3, §9, §10; ADR-001, ADR-002 (texto revisado na §2.10), ADR-004, ADR-005 (texto revisado na §2.10), ADR-006, ADR-007 (Aceito, novo)

## 1. Objetivo

Ligar o console ao `ConversationService` com o primeiro cérebro real, o `ClaudeCodeTutorBrain`: um processo `claude -p` isolado por turno, com histórico controlado pela aplicação, `--resume` como otimização, leitura da saída JSON pelo parser único e medição de latência JsonSchema × Tags. No console entram `/mode`, a exibição de reply + feedback e o bind de `Conversation:MaxTurnsInWindow`.

Revisão de 2026-10-19: o validador do Core passa a exigir **exatamente uma** pergunta em qualquer posição do `reply` (RN-FEED01 alterada), com um separador de frases próprio do Core, só com BCL (§2.10).

Revisão de 2026-10-20: ajustes de coerência e de verificação pedidos na Rodada 4 do `plan-review.md` (S1–S6) e o risco de privacidade do ambiente herdado aceito de forma explícita (S7, §7 e §8). Nenhuma contagem de testes muda.

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
- no `EnglishTutorApp`, a `AggregateException` com alguma interna que não é `OptionsValidationException` continuar como erro inesperado (o `ValidateOnStart` só junta `OptionsValidationException`; um validador que lançasse em vez de devolver `Fail` seria bug, e o caminho é o `catch (Exception)` já testado);
- (revisão de 2026-10-19) os `ArgumentNullException.ThrowIfNull` de `ReplySentences.Split`, `ReplySentences.IsQuestion` e `TutorReplyRejections.ReplyMustAskExactlyOneQuestion`, e a `ArgumentOutOfRangeException` de `ReplyMustAskExactlyOneQuestion` com `questionCount` negativo ou igual a 1 (o único chamador, o `TutorReplyValidator`, só a chama com contagem diferente de 1, vinda de `Count`).

Só o próprio código os chama, sempre com valores válidos. A US que criar outro chamador acrescenta o teste.

### 2.10 Revisão de 2026-10-19 — RN-FEED01: exatamente uma pergunta (resolve o B1 de `review.md`)

A spec já foi alterada no commit `ea8744d` (`docs(ai): update spec for US-004`): RN-FEED01 e o histórico em `specs/BUSINESS_RULES.md`; em `specs/sprints/SPRINT_1.md`, o cenário "reply sem pergunta" da US-002 (motivo `reply must ask the student exactly one question`), o cenário 1 da US-004 ("no máximo 3 frases e exatamente uma pergunta"), os cenários novos "pergunta seguida de incentivo é aceita" e "reply com duas perguntas é inválido" e o D6 da US-004. O critério do D5 ("5/5 no formato") não muda: passa a medir a regra nova.

Notação: neste plano, caracteres fora do ASCII aparecem como `U+XXXX`; no código e nos testes, eles vão como escape C# (barra invertida + `u` + os 4 dígitos), como manda o `padroes-codigo` (AP-007).

#### 2.10.1 Separador de frases no Core (`src/EnglishTutor.Core/Brain/ReplySentences.cs`)

```csharp
namespace EnglishTutor.Core.Brain;

// Splits a tutor reply into sentences for RN-FEED01. BCL only (R1).
public static class ReplySentences
{
    public static IReadOnlyList<string> Split(string text);
    public static bool IsQuestion(string sentence);
}
```

Fica no Core porque a regra de negócio (RN-FEED01) mora no `TutorReplyValidator` (ADR-005, R4). É `public` porque os testes `LiveBrain` da Infrastructure contam frases e perguntas com o mesmo separador (P18, P24), em vez de manter um regex próprio. Usa só `string`/`char` da BCL (a R1 proíbe pacotes de terceiros, não a BCL). A forma (laço sobre os caracteres ou `Regex` da BCL) é livre para o Executor, desde que o comportamento seja o da tabela de bordas da §4. Uma restrição de forma, porque o D16 a confere por texto: o código novo não usa a chamada `EndsWith('?')` (a regra é "a pontuação final contém `?`", não "o último caractere é `?`", P21).

Definições:

- **Terminadores:** `.`, `!`, `?` e reticências U+2026.
- **Fechos:** `"`, `'`, `)`, `]`, U+201D (aspas duplas de fecho) e U+2019 (aspas simples de fecho).
- **Limite de frase (`Split`):** logo depois de uma sequência de um ou mais terminadores, seguida de zero ou mais fechos (que ficam na frase que termina), seguida de:
  - (a) fim do texto; ou
  - (b) um ou mais caracteres de espaço em branco (`char.IsWhiteSpace`, inclui `\n`) cujo próximo caractere não branco **não** é letra minúscula (`char.IsLower`).

  Terminador seguido de caractere que não é branco nem fecho (ex.: `2.0`, `example.com?q=1`) não é limite. Terminador seguido de espaço e de letra minúscula (ex.: `"why not?" to`, `e.g. xUnit`, `Well... what`) não é limite.
- Cada frase é o trecho entre limites, com `Trim()`; trechos vazios são descartados. Texto vazio ou só com espaços → lista vazia. O último trecho é uma frase mesmo sem terminador (ex.: `Tell me more`).
- **Pergunta (`IsQuestion`, P21):** depois de `Trim()` e de remover os fechos do fim, a sequência final de terminadores contém `?`. Assim, `Really?!`, `Why?"` e `Why?` seguido de U+201D são perguntas; `He asked "why?" twice.` e `Tell me more` não são.

Limites conhecidos (P22), aceitos: abreviação seguida de maiúscula (`Mr. Smith`) vira limite, mas isso só muda a contagem de frases, que o Core não valida (P18), e nunca a de perguntas; frase nova que começa com minúscula depois de `?` não é separada (duas perguntas assim contam como uma); pergunta citada seguida de maiúscula (`said "Really?" Then`) conta como pergunta. A contagem de frases a mais por abreviação atinge o teste `LiveBrain` do cenário 1 (máximo de 3 frases), que roda no D5 com tolerância zero (efeito e tratamento na P22, no §7 e na T23).

#### 2.10.2 Motivo e mensagem de rejeição (`TutorReplyRejections`)

```csharp
public const string ReplyMustAskExactlyOneQuestionReason = "reply must ask the student exactly one question";
public static TutorReplyRejection ReplyMustAskExactlyOneQuestion(string reply, int questionCount);
```

Substituem `ReplyMustEndWithQuestionReason` e `ReplyMustEndWithQuestion(string)`, que saem no Refactor da T20 (não sobra chamador). A citação do reply continua cortada em 80 caracteres + `...` (US-002, review S3). Mensagem (P23), no formato do catálogo (`<Reason>: <o que está errado>. Expected <o que se espera>.`):

| `questionCount` | Mensagem |
|---|---|
| 0 | `reply must ask the student exactly one question: the reply "<citado>" has no question. Expected 1 to 3 short sentences with exactly one question to the student (one sentence ending with "?", in any position); the other sentences are comments or encouragement.` |
| ≥ 2 | `reply must ask the student exactly one question: the reply "<citado>" has <n> questions. Expected 1 to 3 short sentences with exactly one question to the student (one sentence ending with "?", in any position); the other sentences are comments or encouragement.` |

A instrução da retentativa (RN-BRAIN04) não muda de código: `TurnMessageBuilder.BuildFormatCorrection` já embute `rejection.Message`, então passa a pedir "exatamente uma pergunta" sozinha. Nenhum teste do `ConversationService` confere o texto do motivo antigo (Grep por `must end with a question` em `tests/`: só `TutorReplyValidatorTests`, `TutorReplyRejectionsTests` e `TutorReplyParserTests`, tratados na T19 e na T20).

#### 2.10.3 Validador (`TutorReplyValidator.Validate`)

Ordem das regras, com a 2ª trocada:

1. `reply` com `Trim()` vazio → `MissingReply()` (sem mudança).
2. `questionCount = ReplySentences.Split(reply).Count(ReplySentences.IsQuestion)`; `questionCount != 1` → `ReplyMustAskExactlyOneQuestion(reply, questionCount)`. A verificação atual `reply.EndsWith('?')` sai.
3. Correções, opcionais, `IsRawFallback` e `RawText`: sem mudança.

A contagem de frases (1 a 3) continua fora do validador (P18).

#### 2.10.4 Testes existentes que mudam (sem enfraquecer; a regra mudou)

| Teste atual | Mudança | Tarefa |
|---|---|---|
| `TutorReplyValidatorTests.Validate_WhenReplyDoesNotEndWithQuestion_RejectsWithQuestionReason` (2 casos) | Renomeado para `Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason`; motivo novo; mensagem contém `has no question`. O caso `"Is it good? I like that."` sai, porque passou a ser válido (o equivalente válido está em `Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply`); entram `"Great job! Tell me more."` e `"Hmm... what a day."` (3 casos) | T20 |
| `TutorReplyRejectionsTests.Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation` | A linha de `ReplyMustEndWithQuestion` vira `ReplyMustAskExactlyOneQuestion("I like that.", 0)`, com o trecho esperado `the reply "I like that." has no question` (mesma contagem de casos) | T19 |
| `TutorReplyRejectionsTests.ReplyMustEndWithQuestion_WhenReplyExceeds80Characters_QuotesFirst80WithEllipsis` | Renomeado para `ReplyMustAskExactlyOneQuestion_WhenReplyExceeds80Characters_QuotesFirst80WithEllipsis`; confere `the reply "<80 caracteres>..." has`; o comentário do teste deixa de citar o motivo antigo | T19 |
| `TutorReplyRejectionsTests.ReplyMustEndWithQuestion_WhenReplyHas80Characters_QuotesItWhole` | Renomeado para `ReplyMustAskExactlyOneQuestion_WhenReplyHas80Characters_QuotesItWhole`; confere `the reply "<80 caracteres>" has` | T19 |
| `TutorReplyParserTests.Parse_WhenJsonReplyHasNoFinalQuestion_RejectsWithQuestionReason` | Renomeado para `Parse_WhenJsonReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason`; motivo novo (fixture `json-reply-without-question.json` sem mudança) | T20 |
| `TutorReplyParserTests.Parse_WhenTagsReplyHasNoFinalQuestion_RejectsWithQuestionReason` | Renomeado para `Parse_WhenTagsReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason`; motivo novo | T20 |
| `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion` | Renomeado para `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion`; `EndWith("?")` e o regex `SentenceEnd` saem; `ReplySentences.Split(reply.Reply)` com no máximo 3 itens e exatamente 1 com `IsQuestion` | T21 |
| `ClaudeCodeTutorBrainLiveTests.SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` | Última asserção `reply2.Reply.Should().EndWith("?")` → exatamente uma pergunta (`ReplySentences`) | T21 |
| `FixtureRecorderLiveTests.SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes` | `OnlyContain(reply => reply.EndsWith('?'))` → cada reply com exatamente uma pergunta | T21 |
| `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId` e `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedTagsOutput_ReturnsTutorReply` | `reply.Reply.Should().EndWith("?")` → exatamente uma pergunta. As fixtures não são regravadas: as duas têm uma pergunta, no fim | T21 |
| (2026-10-20, review S2 da Rodada 4) Comentários `// US-002 — Cenário: reply sem pergunta final ...` em `TutorReplyValidatorTests` (acima de `Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` e de `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed`) e em `TutorReplyParserTests` (acima dos dois testes de reply sem pergunta, `Parse_WhenJsonReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` e `Parse_WhenTagsReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason`) | Só o comentário: o texto antigo `Cenário: reply sem pergunta final` vira `Cenário: reply sem pergunta` (o nome do cenário na spec desde `ea8744d`); o resto do comentário (`(apoio, P6)`, `(mesma regra nos dois formatos)`, regras e ADR) não muda. O método `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed` não muda de nome nem de corpo | T20 |

Os outros testes do validador, do parser, do `ConversationService` e do console seguem sem mudança: todos os replies válidos deles têm uma única pergunta (`Reply <label>?`, fixtures `json-valid.json`, `tags-valid.txt` etc.), e o rejeitado `"I like that."` continua rejeitado.

#### 2.10.5 Prompts (tarefa do Executor, T22; commit `feat(brain)`, review S1)

O reforço de `2cb0f7a` ("a pergunta é a última frase") passa a contradizer a regra. Texto novo, identificado pelo texto antigo (AP-023):

- `prompts/tutor.md`, texto antigo:
  `- End with exactly ONE question to keep the conversation going. The question is always the LAST sentence of the reply: nothing comes after it (no comment, invitation, or "Tell me more" sentence), and the reply ends with "?".`
  Texto novo:
  `- Ask exactly ONE question to keep the conversation going: exactly one sentence of the reply ends with "?". The question can come anywhere in the reply, and the other sentences are a short comment or encouragement (for example: "Was it busy in the morning? Tell me about your commute."). Never ask a second question, not even a short one like "Right?" or "And you?".`
- `prompts/format.json.md`, texto antigo:
  `- `reply`: the spoken part (string): 2–3 short sentences whose LAST sentence is the one question. The string ends with "?"; never add a sentence after the question.`
  Texto novo:
  `- `reply`: the spoken part (string): 2–3 short sentences with exactly one question (exactly one sentence ends with "?", in any position); the other sentences are comments or encouragement, never a second question.`
- `prompts/format.tags.md`, texto antigo:
  `The spoken part: 2–3 short sentences. The LAST sentence is the one question, and the text ends with "?"; never add a sentence after the question.`
  Texto novo:
  `The spoken part: 2–3 short sentences with exactly one question (exactly one sentence ends with "?", in any position); the other sentences are comments or encouragement, never a second question.`

As demais linhas dos prompts não mudam (os modos `job-interview` e `listening-practice` já dizem "one question at a time").

#### 2.10.6 Documentos (propostas; aplicadas pelo orquestrador depois da aprovação e antes da T18, em dois commits, AP-030)

**`docs(ai): update spec for US-004`** (o `ea8744d` não alterou o `PRODUCT.md`, que ainda repete a regra antiga; P25):

- `specs/PRODUCT.md`, linha do OA1, texto antigo:
  `| OA1 | Conversação do dia a dia | Modo `free-conversation`: respostas curtas que sempre terminam em pergunta, mantendo o aluno falando |`
  Texto novo:
  `| OA1 | Conversação do dia a dia | Modo `free-conversation`: respostas curtas que sempre fazem exatamente uma pergunta, mantendo o aluno falando |`

**`docs(ai): update architecture for US-004`:**

- `ARCHITECTURE.md` §3, texto antigo:
  `                       TutorReplyRejection, TutorReplyRejections, TutorReplyValidator, FeedbackFormatter,`
  Texto novo:
  `                       TutorReplyRejection, TutorReplyRejections, TutorReplyValidator, ReplySentences, FeedbackFormatter,`
- `ARCHITECTURE.md` §6.2, texto antigo:
  `- As regras de conteúdo verificáveis (reply não vazio e terminando em `?`, no máximo 2 correções, correção com `original` e `corrected`) moram no Core (`TutorReplyValidator`, R4); o `TutorReplyParser` cuida só do formato e delega a elas (ADR-005). Toda rejeição traz um `Reason` curto e uma `Message` que diz o que faltou e o que se espera, usada na retentativa da RN-BRAIN04.`
  Texto novo:
  `- As regras de conteúdo verificáveis (reply não vazio e com exatamente uma pergunta, isto é, uma única frase terminada em `?`, em qualquer posição, com as frases separadas por `ReplySentences`; no máximo 2 correções; correção com `original` e `corrected`) moram no Core (`TutorReplyValidator`, R4); o `TutorReplyParser` cuida só do formato e delega a elas (ADR-005). Toda rejeição traz um `Reason` curto e uma `Message` que diz o que faltou e o que se espera, usada na retentativa da RN-BRAIN04.`
- `docs/adr/ADR-002-contrato-saida-estruturada.md` (Proposto), item 1 da Decisão, texto antigo:
  `     "reply": "string (1–3 frases, termina com '?')",`
  Texto novo:
  `     "reply": "string (1–3 frases, com exatamente uma pergunta, em qualquer posição)",`
- ADR-002, item 5 da Decisão, texto antigo:
  `5. Regras que o schema não garante (reply termina com pergunta, no máximo 2 correções) continuam validadas pelo parser, com a retentativa da RN-BRAIN04.`
  Texto novo:
  `5. Regras que o schema não garante (reply com exatamente uma pergunta, RN-FEED01; no máximo 2 correções) continuam validadas pelo parser, com a retentativa da RN-BRAIN04.`
- `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md` (Aceito; a decisão de onde as regras moram não muda, só a lista de regras): depois da linha antiga `- **Data:** 2026-10-17`, acrescentar:
  `- **Revisado:** 2026-10-19 (RN-FEED01 passou a "exatamente uma pergunta"; US-004)`
- ADR-005, Contexto, texto antigo: `"Reply termina com pergunta" (RN-FEED01)`
  Texto novo: `"Reply com exatamente uma pergunta" (RN-FEED01)`
- ADR-005, item 1 da Decisão, texto antigo: `reply não vazio, reply terminando em `?`, no máximo 2 correções`
  Texto novo: `reply não vazio, reply com exatamente uma pergunta (uma única frase terminada em `?`, em qualquer posição; as frases são separadas por `ReplySentences`, também no Core), no máximo 2 correções`
- ADR-005, Consequências, texto antigo:
  `- O que precisa ser medido ou revisitado: se o `eval` (US-009) mostrar muitas rejeições por "reply must end with a question", rever a rigidez da regra (ex.: aceitar `?"` ou `?)` no fim).`
  Texto novo:
  `- O que precisa ser medido ou revisitado: se o `eval` (US-009) mostrar muitas rejeições por "reply must ask the student exactly one question", rever a regra ou o separador de frases (`ReplySentences`; limites conhecidos em `tasks/US-004/plan.md` §9 P22).`
- `docs/us/US-002.md` (resumo da US-002, que repete a regra antiga), texto antigo:
  `reply que não termina em `?` rejeita (`reply must end with a question`);`
  Texto novo:
  `reply sem exatamente uma pergunta rejeita (`reply must ask the student exactly one question`; regra alterada em 2026-10-19 pela US-004);`
- (2026-10-20, review S5 da Rodada 4) `.claude/skills/tdd-dotnet/SKILL.md`, exemplo de teste de adaptador, texto antigo:
  `    public async Task SendAsync_WithValidOutput_ReturnsReplyEndingWithQuestion() { ... }`
  Texto novo:
  `    public async Task SendAsync_WithValidOutput_ReturnsReplyWithOneQuestion() { ... }`
  Decisão: o exemplo acompanha a regra nova. É só um nome de exemplo, mas sugeriria "termina em pergunta" a quem copiar o padrão, e o D16 passa a conferir `.claude/skills`.

**Repetições em `CLAUDE.md` e skills** (Grep por `pergunta|question|termina em|terminando|EndingWith|pergunta final` em `CLAUDE.md` e `.claude/**`): `CLAUDE.md` e as demais skills não repetem a RN-FEED01 nem a §6.2. **Única linha afetada:** o exemplo do `tdd-dotnet` acima. `CHANGELOG.md` diz "reply com pergunta final" na entrada da US-002: é o registro histórico do que aquela US entregou e **não é alterado**; o Documentador acrescenta a entrada da US-004 (o D16 não varre o `CHANGELOG.md`).

Nenhum ADR novo: a decisão técnica (separador no Core, regra de limite) é pequena e fica registrada no ADR-005 revisado e nas P20–P22.

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
- (2026-10-19) Contar os `?` do texto inteiro: um `"why not?"` citado no meio da frase contaria como pergunta; a regra fala em frases (P20).
- (2026-10-19) Separador com lista de abreviações (`Mr.`, `e.g.`): mais código para um efeito que não muda a contagem de perguntas (P22).
- (2026-10-19) Pacote de tokenização de frases: viola a R1.
- (2026-10-19) Considerar pergunta só a frase cujo último caractere é `?`: `Really?! What happened next?` passaria como uma pergunta (P21).

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md` | Decisão (Aceito em 2026-10-17), criada junto com este plano; itens 3 e 5 e "Consequências" revisados na rodada 1; item 5 e contagem de turnos em "Consequências" revisados na rodada 2 |
| Alterar | `ARCHITECTURE.md` | §3, §5, §6.3 (texto em §2.8), pelo orquestrador antes da T1; §3 e §6.2 (texto em §2.10.6), pelo orquestrador antes da T18 |
| Alterar | `.claude/skills/padroes-codigo/SKILL.md` | 2 linhas de "DI e configuração" (§2.8), pelo orquestrador antes da T1 |
| Alterar | `.claude/skills/tdd-dotnet/SKILL.md` | Caminho da fixture (§2.8), pelo orquestrador antes da T1; (2026-10-20) nome do exemplo de teste de adaptador (§2.10.6), pelo orquestrador antes da T18 |
| Alterar | `specs/sprints/SPRINT_1.md` | Cenário "falha do cérebro no console não encerra a sessão" e D7 na US-005; cenário "console conversa com o Ollama" na US-006 (§2.8), pelo orquestrador antes da T1. RN-FEED01: US-002 "reply sem pergunta", US-004 cenário 1, dois cenários novos e D6 — já alterado em `ea8744d` |
| Alterar | `specs/BUSINESS_RULES.md` | RN-FEED01 + histórico — já alterado em `ea8744d` |
| Alterar | `specs/PRODUCT.md` | OA1 (§2.10.6), pelo orquestrador antes da T18 |
| Alterar | `docs/adr/ADR-002-contrato-saida-estruturada.md` | Tabela de latência JsonSchema × Tags (D7), pelo Executor na T11 — alterado em 2fd8419 (seção "Medição (US-004)"); itens 1 e 5 da Decisão (§2.10.6), pelo orquestrador antes da T18 |
| Alterar | `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md` | "Revisado", Contexto, item 1 e Consequências (§2.10.6), pelo orquestrador antes da T18 |
| Alterar | `docs/us/US-002.md` | Regra do validador (§2.10.6), pelo orquestrador antes da T18 |
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
| Criar | `src/EnglishTutor.Infrastructure/Processes/ProcessRunner.cs` | `System.Diagnostics.Process` (§2.1) — criado: esqueleto em d6ed73e, implementação em 014746f |
| Criar | `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | `AddEnglishTutorInfrastructure(promptsDirectory)` |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | Registrar `ConversationService` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs` | `/mode` em `Available` |
| Criar | `src/EnglishTutor.Core/Brain/ReplySentences.cs` | (2026-10-19) Separador de frases e detecção de pergunta (§2.10.1) |
| Alterar | `src/EnglishTutor.Core/Brain/TutorReplyRejections.cs` | (2026-10-19) Motivo e mensagem `reply must ask the student exactly one question`; saem o motivo e a fábrica antigos (§2.10.2) |
| Alterar | `src/EnglishTutor.Core/Brain/TutorReplyValidator.cs` | (2026-10-19) Regra de exatamente uma pergunta (§2.10.3) |
| Alterar | `src/EnglishTutor.Console/Ui/IChatInputHandler.cs` | `Mode` e `ChangeMode` |
| Alterar | `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` | Modo local; uso só com Ollama |
| Criar | `src/EnglishTutor.Console/Ui/ConversationInputHandler.cs` | Delegação ao `ConversationService` + exibição |
| Alterar | `src/EnglishTutor.Console/Ui/ChatSession.cs` | Modo pelo handler; `case Mode` |
| Alterar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | Textos da §2.6 |
| Alterar | `src/EnglishTutor.Console/AppRunOptions.cs` | `ConfigureServices` |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Infrastructure, binds, handler por provedor; configuração inválida em várias seções (P19) |
| Alterar | `src/EnglishTutor.Console/Program.cs` | `OutputEncoding` UTF-8 sem BOM |
| Alterar | `src/EnglishTutor.Console/appsettings.json` | `Brain:ClaudeCode` e `Conversation:MaxTurnsInWindow` |
| Alterar | `prompts/tutor.md`, `prompts/format.json.md`, `prompts/format.tags.md` | Reforço de `2cb0f7a` (pergunta como última frase); (2026-10-19) texto da regra nova (§2.10.5), pelo Executor na T22 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeProcessRunner.cs` | Fila de `ProcessResult`; registra `Requests`, `Tokens`; callback `OnRun(ProcessRequest)` para inspecionar arquivos durante a execução |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/ClaudeOutputs.cs` | Monta stdout da CLI. Os três auxiliares produzem o **objeto de resultado** da CLI, no mesmo formato das fixtures gravadas, sempre com `"type": "result"` e com os campos que decidem a linha da §2.5 declarados (review S3 da rodada 2): `Structured(sessionId, replyJson)` → `"type": "result"`, `"subtype": "success"`, `"is_error": false`, `"session_id"`, `"result": ""`, `"structured_output": <replyJson>`; `Text(sessionId, resultText)` → `"type": "result"`, `"subtype": "success"`, `"is_error": false`, `"session_id"`, `"result": <resultText>`, sem `structured_output`; `Error(resultText, structuredReplyJson = null)` → `"type": "result"`, `"subtype": "error"`, `"is_error": true`, `"session_id": "s-error"`, `"result": <resultText>` e `structured_output` só quando informado. Mais `ValidReplyJson(label)` (`reply` = `Reply <label>?`, `praise` = `Praise <label>`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/ManualTimeProvider.cs` | `TimeProvider` com `Advance(TimeSpan)` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/RecordingProcessRunner.cs` | (LiveBrain) envolve o `ProcessRunner` real e guarda requests/resultados |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LiveClaudeCode.cs` | (LiveBrain) monta o adaptador real com pasta temporária, `prompts/` da saída e `ENGLISHTUTOR_CLAUDE_PATH` (padrão `claude`); o system prompt dos requests é o conteúdo de `AppContext.BaseDirectory/prompts/tutor.md` (o mesmo que o app usa; review S9) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LatencySummary.cs` | Média e p95 por posto mais próximo |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/claude-json-structured.json` | Saída real, `OutputMode = JsonSchema`, frase "I have worked with C# since five years" — criado em f3f153c (gravado sem as variáveis herdadas, §12 E3) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/claude-json-tags.json` | Saída real, `OutputMode = Tags`, mesma frase — criado em f3f153c (gravado sem as variáveis herdadas, §12 E3) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainTests.cs` | Adaptador com fake de processo; (2026-10-19) 2 asserções de pergunta na T21 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodePromptRendererTests.cs` | Formato do texto único |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/TutorReplySchemaTests.cs` | Schema canônico |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeOptionsTests.cs` | Padrões |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeOptionsValidatorTests.cs` | Validação |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/FileTutorPromptSourceTests.cs` | Leitura de `tutor.md` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs` | Registros |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LatencySummaryTests.cs` | p95 nos dois lados do posto |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainLiveTests.cs` | `LiveBrain`: fumaça, cenário 1, retomada; (2026-10-19) regra de uma pergunta via `ReplySentences` na T21 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/LatencyMeasurementLiveTests.cs` | `LiveBrain`: medição D7 (nome sem "ClaudeCode", fora do filtro do D5) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/FixtureRecorderLiveTests.cs` | `LiveBrain`: grava as fixtures em `bin/.../recorded-fixtures/` (fora do filtro do D5); (2026-10-19) asserção de pergunta na T21 |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Brain/TutorReplyParserTests.cs` | (2026-10-19) 2 testes novos (cenários novos da US-004) e 2 renomeados (motivo novo), na T20; (2026-10-20) comentários dos 2 testes de reply sem pergunta citam o cenário `reply sem pergunta` (§2.10.4) |
| Alterar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | `ConversationService` registrado |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/ReplySentencesTests.cs` | (2026-10-19) Bordas do separador (T18) |
| Alterar | `tests/EnglishTutor.Core.Tests/Brain/TutorReplyRejectionsTests.cs` | (2026-10-19) Mensagem nova, 2 renomeados e 1 novo (T19) |
| Alterar | `tests/EnglishTutor.Core.Tests/Brain/TutorReplyValidatorTests.cs` | (2026-10-19) 2 testes novos e 1 renomeado (T20); (2026-10-20) comentários dos testes de reply sem pergunta e de `...FollowedBySpaces...` citam o cenário `reply sem pergunta` (§2.10.4) |
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
| frase com erro gramatical | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion` (renomeado na T21; antes `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion`. `"I have worked with C# since five years"` via `TurnMessageBuilder.Build(..., [], FreeConversation)`; `ReplySentences.Split(Reply)` com no máximo 3 itens e exatamente 1 com `ReplySentences.IsQuestion`; alguma `Correction` com `Original` contendo `since five years` e `Corrected` contendo `for five years`, sem diferenciar maiúsculas) | LiveBrain |
| frase com erro gramatical (offline, com a saída gravada) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId` (ver "leitura da saída gravada") | Unidade (caracterização) |
| frase com erro gramatical (D5) | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat` (5 frases da tabela abaixo, cada uma num request de 1 mensagem; conta `InvalidOutput` como fora do formato; espera 5/5 no formato e ≥ 4 com `Corrected` contendo o trecho esperado, sem diferenciar maiúsculas. Código sem mudança; a partir da T20, "no formato" usa a RN-FEED01 nova) | LiveBrain |
| pergunta seguida de incentivo é aceita | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyAsksQuestionThenEncourages_ReturnsReply` (`{"reply":"Was it busy in the morning? Tell me about your commute.","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Nice sentence."}` → `IsValid`; `Reply` = `Was it busy in the morning? Tell me about your commute.`) | Unidade |
| pergunta seguida de incentivo é aceita (regra no Core, bordas) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply` (`[Theory]`, 4 casos, todos terminados em `.`: `"Was it busy in the morning? Tell me about your commute."`; `"Did you really say \"why not?\" to your boss? Tell me more."` (pergunta citada no meio); `"I met Mr. Smith there. Do you know him? He is nice."` (abreviação); `"Hmm... do you mean the bus? Tell me more."` (reticências); nos 4, `IsValid` e `Reply` = o texto) | Unidade |
| pergunta seguida de incentivo é aceita (apoio, P20: separador) | Core.Tests | `ReplySentencesTests` | `Split_WithReplyText_ReturnsSentencesAtBoundaries` (`[Theory]`, 10 casos da tabela "Bordas do separador" abaixo) | Unidade |
| pergunta seguida de incentivo é aceita (apoio, P20: texto vazio) | Core.Tests | `ReplySentencesTests` | `Split_WhenTextIsBlank_ReturnsNoSentences` (`[Theory]`: `""` e `" \n "` → lista vazia) | Unidade |
| reply com duas perguntas é inválido | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyAsksTwoQuestions_RejectsWithExactlyOneQuestionReason` (`{"reply":"Was it busy? How long was the trip?","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Nice sentence."}` → inválido; `Reason` = `reply must ask the student exactly one question`; `Message` contém `has 2 questions`) | Unidade |
| reply com duas perguntas é inválido (regra no Core, bordas) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason` (`[Theory]`, 4 casos, todos terminados em `?`: `"Was it busy? How long was the trip?"`; `"Really?! What happened next?"` (P21); `"Your boss said \"Really?\" Then what did you say?"` (citação seguida de maiúscula, P22); `"Was it busy?\nHow long was the trip?"` (quebra de linha); nos 4, inválido, `Reason` = `reply must ask the student exactly one question` e `Message` contém `has 2 questions`) | Unidade |
| reply com duas perguntas é inválido (apoio, P21: o que é pergunta) | Core.Tests | `ReplySentencesTests` | `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` (renomeado em 2026-10-20, review S6 da Rodada 4: o nome descreve a regra da P21, a pontuação final contém `?`, e não "termina em `?`". `[Theory]`, 7 casos: `"How are you?"` → true; `"Really?!"` → true; `Why?` seguido de U+201D → true; `"He asked \"why?\" twice."` → false; `"Tell me more."` → false; `"Tell me more"` → false; `"Wow!"` → false) | Unidade |
| reply sem pergunta (US-002, motivo novo) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` (renomeado na T20; antes `Validate_WhenReplyDoesNotEndWithQuestion_RejectsWithQuestionReason`. `[Theory]`: `"I like that."`, `"Great job! Tell me more."`, `"Hmm... what a day."`; inválido; `Reason` = `reply must ask the student exactly one question`; `Message` contém `"<reply>"` e `has no question`) | Unidade |
| reply sem pergunta (US-002, motivo novo) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` (renomeado na T20; antes `Parse_WhenJsonReplyHasNoFinalQuestion_RejectsWithQuestionReason`; fixture `json-reply-without-question.json`; `Reason` = `reply must ask the student exactly one question`) | Unidade |
| reply sem pergunta (US-002, mesma regra nos dois formatos) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` (renomeado na T20; antes `Parse_WhenTagsReplyHasNoFinalQuestion_RejectsWithQuestionReason`; `<reply>I like that.</reply><feedback>- Praise: Nice sentence.</feedback>`; motivo novo) | Unidade |
| reply sem pergunta (US-002 D6: mensagem) | Core.Tests | `TutorReplyRejectionsTests` | `Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation` (linha alterada na T19: `ReplyMustAskExactlyOneQuestion("I like that.", 0)`, motivo novo, trecho `the reply "I like that." has no question`, `Expected `) | Unidade |
| reply sem pergunta (apoio, AP-016: corte em 80) | Core.Tests | `TutorReplyRejectionsTests` | `ReplyMustAskExactlyOneQuestion_WhenReplyExceeds80Characters_QuotesFirst80WithEllipsis` (renomeado na T19; 81 caracteres → `the reply "<80>..." has`; não contém o reply inteiro) | Unidade |
| reply sem pergunta (apoio, AP-016: corte em 80, outro lado) | Core.Tests | `TutorReplyRejectionsTests` | `ReplyMustAskExactlyOneQuestion_WhenReplyHas80Characters_QuotesItWhole` (renomeado na T19; 80 caracteres → `the reply "<80>" has`; sem `...`) | Unidade |
| reply sem pergunta / duas perguntas (apoio, P23, AP-016: contagem) | Core.Tests | `TutorReplyRejectionsTests` | `ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` (`[Theory]`: (`"I like that."`, 0) → contém `the reply "I like that." has no question`; (`"Was it busy? How long was the trip?"`, 2) → contém `has 2 questions` e não contém `no question`; (`"Why? When? Where?"`, 3) → contém `has 3 questions`; nos 3, `Expected 1 to 3 short sentences with exactly one question`) | Unidade |
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
| leitura da saída gravada | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId` (stdout = `Fixtures/ClaudeCode/claude-json-structured.json`; `Reply` com exatamente uma pergunta (`ReplySentences`; até a T21, termina em `?`); `Corrections` tem um item com `Original` **contendo** `since five years` e `Corrected` **contendo** `for five years`, sem diferenciar maiúsculas, porque o texto real pode trazer a frase inteira (review S13); o 2º turno que estende o 1º tem, depois de `--resume`, o `session_id` lido da fixture com `JsonDocument` no próprio teste) | Unidade (caracterização) |
| leitura da saída gravada (apoio, OutputMode Tags) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedTagsOutput_ReturnsTutorReply` (`OutputMode = Tags`; stdout = `claude-json-tags.json`; `Reply` com exatamente uma pergunta (`ReplySentences`; até a T21, termina em `?`); `RawText` = campo `result` da fixture) | Unidade (caracterização) |
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
| continuidade de sessão (apoio, LiveBrain) | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` (`RecordingProcessRunner`; 2 turnos como no teste unitário de retomada; a 2ª chamada tem `--resume` com o `session_id` da 1ª; a 2ª resposta é válida e tem exatamente uma pergunta (`ReplySentences`, a partir da T21)) | LiveBrain |
| leitura da saída gravada (gravação, P16) | Infrastructure.Tests | `FixtureRecorderLiveTests` | `SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes` (`RecordingProcessRunner`; a frase do cenário 1 em `JsonSchema` e em `Tags`; grava os stdouts em `AppContext.BaseDirectory/recorded-fixtures/claude-json-structured.json` e `claude-json-tags.json`; as duas respostas são válidas e têm exatamente uma pergunta (`ReplySentences`, a partir da T21)) | LiveBrain |

**Frases de fumaça e da medição** (D5, D7):

| # | Frase | `Corrected` deve conter |
|---|---|---|
| 1 | `I have worked with C# since five years` | `for five years` |
| 2 | `Yesterday I go to the office by bus` | `went` |
| 3 | `She don't like debugging on Fridays` | `doesn't` |
| 4 | `I am working here since 2020` | `have been working` |
| 5 | `We discussed about the new architecture` | `discussed the new architecture` |

**Bordas do separador** (`ReplySentencesTests.Split_WithReplyText_ReturnsSentencesAtBoundaries`, P20, AP-016; caracteres fora do ASCII como escape no código, AP-007):

| # | Entrada | Frases esperadas | Lado do limite conferido |
|---|---|---|---|
| 1 | `Was it busy in the morning? Tell me about your commute.` | `Was it busy in the morning?` · `Tell me about your commute.` | `?` + espaço + maiúscula → limite |
| 2 | `Great job!\nWhat did you do next?` | `Great job!` · `What did you do next?` | quebra de linha conta como espaço |
| 3 | `Did you really say "why not?" to your boss?` | a entrada inteira (1 frase) | `?` + fecho + espaço + minúscula → não é limite |
| 4 | `Your boss said "Really?" Then what did you say?` | `Your boss said "Really?"` · `Then what did you say?` | `?` + fecho + espaço + maiúscula → limite; o fecho fica na frase |
| 5 | `Hmm... do you mean the bus?` | a entrada inteira (1 frase) | reticências + espaço + minúscula → não é limite |
| 6 | `Hmm` + U+2026 + ` Do you mean the bus?` | `Hmm` + U+2026 · `Do you mean the bus?` | reticências Unicode + espaço + maiúscula → limite |
| 7 | `We use tools, e.g. xUnit. Do you test your code?` | `We use tools, e.g. xUnit.` · `Do you test your code?` | abreviação + espaço + minúscula → não é limite |
| 8 | `Version 2.0 is out. Have you tried it?` | `Version 2.0 is out.` · `Have you tried it?` | terminador seguido de não branco → não é limite |
| 9 | `Really?! Tell me more` | `Really?!` · `Tell me more` | sequência de terminadores; última frase sem terminador |
| 10 | `  What do you do at work?  ` | `What do you do at work?` | `Trim()`; fim do texto → limite |

**Testes existentes alterados** (com o motivo; nenhum é enfraquecido):

- `ChatSessionTests.RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands`: a expectativa passa de `Available commands: /help, /quit` para a linha exata `Available commands: /help, /mode, /quit`, porque `/mode` entra em `Available`.
- `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing`: a entrada `"/mode interview"` vira `"/foo bar"`, e a expectativa vira `Unknown command "/foo".`, porque `/mode` deixa de ser desconhecido (efeito colateral previsto no plano da US-003).
- `EnglishTutorAppTests.RunAsync_WhenUserTypesText_ShowsTutorNotConnected` → renomeado para `RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected`, com Provider `Ollama` (P7). Com `ClaudeCode`, o texto livre agora vai ao cérebro.
- `ChatSessionTests.CreateSession` e `EnglishTutorAppTests.CreateOptions` (helpers): o primeiro deixa de passar `TutorMode`; o segundo passa a registrar sempre um `ScriptedTutorBrain` sem respostas e um `FixedTutorPromptSource` via `ConfigureServices`, para nenhum teste do console lançar o processo real.
- (2026-10-19, RN-FEED01) os 11 itens da tabela da §2.10.4 (os 10 de 2026-10-19 mais o dos comentários de cenário, 2026-10-20). Eles mudam porque a regra mudou na spec (`ea8744d`), não para fazer teste passar: os casos que viraram válidos saem dos testes de rejeição e entram nos de aceitação, e as asserções `EndWith("?")` viram "exatamente uma pergunta".

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

**Estado final (2026-10-19):** os números previstos acima se confirmaram: 49 métodos novos / 65 casos novos; suíte com 216 casos (Core.Tests 103, Console.Tests 41, Infrastructure.Tests 72); `ClaudeCodeTutorBrainTests` 20 / 31; `LiveBrain` 5 / 5.

**Contagens da revisão de 2026-10-19 (RN-FEED01, T18–T20): +8 métodos novos, +33 casos novos** (renomeações não contam; a linha alterada da `Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation` não muda a contagem; a revisão de 2026-10-20 só renomeia um método e altera comentários, então nenhuma contagem muda):

- Core.Tests: +6 métodos / +31 casos.
  - `ReplySentencesTests` (nova): 3 / 19 (`Split_WithReplyText_ReturnsSentencesAtBoundaries` 10, `Split_WhenTextIsBlank_ReturnsNoSentences` 2, `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` 7).
  - `TutorReplyRejectionsTests`: +1 / +3 (`ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` 3; 2 renomeados).
  - `TutorReplyValidatorTests`: +2 / +9 (`Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply` 4, `Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason` 4; `Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` passa de 2 para 3 casos, +1).
- Infrastructure.Tests: +2 métodos / +2 casos (`TutorReplyParserTests`: `Parse_WhenJsonReplyAsksQuestionThenEncourages_ReturnsReply` e `Parse_WhenJsonReplyAsksTwoQuestions_RejectsWithExactlyOneQuestionReason`; 2 renomeados). `ClaudeCodeTutorBrainTests` continua 20 / 31 (D11).
- Console.Tests: sem mudança (41).
- `LiveBrain`: continua 5 métodos / 5 casos (1 renomeado).

Total da US com a revisão: 57 métodos novos / 98 casos novos. Suíte após a revisão (D2): **249 casos** = Core.Tests 103 + 31 = 134; Console.Tests 41; Infrastructure.Tests 72 + 2 = 74.

## 5. Tarefas (ordem de execução TDD)

**Pré-condição** (antes da T1): o orquestrador aplica a §2.8 em dois commits, `docs(ai): update spec for US-004` (US-005 e US-006 no `SPRINT_1.md`) e `docs(ai): update architecture for US-004` (`ARCHITECTURE.md` §3/§5/§6.3, `padroes-codigo`, `tdd-dotnet`). Junto vai o ADR-007 em "Aceito", se aprovado.

**Pré-condição da revisão de 2026-10-19** (antes da T18): a spec da RN-FEED01 já está em `ea8744d`. Depois da nova aprovação, o orquestrador aplica a §2.10.6 em dois commits: `docs(ai): update spec for US-004` (`specs/PRODUCT.md`) e `docs(ai): update architecture for US-004` (`ARCHITECTURE.md` §3/§6.2, ADR-002, ADR-005, `docs/us/US-002.md` e, desde a revisão de 2026-10-20, o exemplo de teste de `.claude/skills/tdd-dotnet/SKILL.md`). Nenhuma tarefa da §5 altera esses documentos.

Regras que valem para todas as tarefas:

- Os esqueletos vêm antes dos testes (AP-013).
- Depois de `x.Should().BeTrue()` em propriedades com `[MemberNotNullWhen]`, use `!` (AP-020).
- Escolha de forma não prevista aqui vai para a §12 num commit `docs(ai)` **antes** do `feat` (AP-014, AP-027).
- Os nomes de teste são os da §4, por extenso (AP-025).
- Os testes `LiveBrain` exigem o Claude Code CLI instalado (`claude.exe` do instalador nativo) e autenticado.
- (2026-10-19) Quando a §5 fixa o tipo e o escopo do commit, use esse; para divergir, registre na §12 **antes** do commit (review S1).

- [x] T1 — Red: `PromptFilesDeploymentTests.ApplicationFolder_Always_ContainsTutorAndFormatPrompts`, que falha porque `prompts/` não está na saída. Green: `<None Include="..\..\prompts\**" Link=...>` no `EnglishTutor.Console.csproj` e no `EnglishTutor.Infrastructure.Tests.csproj`. No mesmo commit `chore`, entram os `PackageReference` da §6 (Infrastructure: `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`; Infrastructure.Tests: `Microsoft.Extensions.DependencyInjection`), com build verde. (commits: `test(console): prompt files deployed with the app` + `chore(infra): copy prompts to outputs and add options packages`) — feito: fc57ab3, fe52dc9
  Escolhas de forma: nenhuma
- [x] T2 — Red: esqueletos `BrainOutputModes` (completo: é dado, como `BrainProviders`), `ClaudeCodeOptions` (com os padrões, porque são dado) e `ClaudeCodeOptionsValidator.Validate` (lança `NotImplementedException`). Testes:
  - `ClaudeCodeOptionsTests.Constructor_Always_UsesDocumentedDefaults`: nasce verde (caracterização). Red por mutação temporária não commitada, pelo efeito: "o padrão de `Model` deixa de ser `haiku`". Registrar a falha na tarefa (AP-017, AP-029).
  - `ClaudeCodeOptionsValidatorTests.Validate_WhenRequiredValueIsBlank_FailsNamingTheKey`
  - `ClaudeCodeOptionsValidatorTests.Validate_WhenOutputModeIsUnknown_FailsWithAcceptedValues`
  - `ClaudeCodeOptionsValidatorTests.Validate_WithAcceptedOutputMode_Succeeds`

  Os 6 casos do validador falham com `NotImplementedException`. Green: mensagens da §2.2. (commits: `test(infra): claude code options validation` + `feat(infra): validate claude code options`) — feito: 2b527ba, f27ab18; mutação temporária não commitada `Model = "sonnet"` → `Constructor_Always_UsesDocumentedDefaults` falhou com `Expected options.Model to be "haiku" ... but "sonnet"`; revertida
  Escolhas de forma: nenhuma
- [x] T3 — Red: esqueleto `TutorReplySchema.Json` (constante `"{}"`) + `TutorReplySchemaTests.Json_Always_DescribesCanonicalTutorReplyContract`, que falha na asserção (`type` ausente). Green: o schema da §2.3. (commits: `test(infra): tutor reply json schema` + `feat(infra): add tutor reply json schema`) — feito: fdca8ba, b023f95
  Escolhas de forma: nenhuma
- [x] T4 — Red: esqueleto `ClaudeCodePromptRenderer.Render` (lança `NotImplementedException`) + `ClaudeCodePromptRendererTests.Render_WithSingleMessage_ReturnsItsContentUnchanged` e `ClaudeCodePromptRendererTests.Render_WithHistory_LabelsEachMessageAndEndsWithCurrentMessage`. Green: formato da §2.4. (commits: `test(infra): claude code prompt rendering` + `feat(infra): render brain messages for claude code stdin`) — feito: 8c28249, a553986
  Escolhas de forma: nenhuma
- [x] T5 — Red: esqueletos `IProcessRunner`/`ProcessRequest`/`ProcessResult`, `PromptFiles`, `ClaudeCodeResult`/`ClaudeCodeOutput` (`Read` lança `NotImplementedException`), `ClaudeCodeLog` (2001 declarado, sem uso) e `ClaudeCodeTutorBrain` (construtor completo da §2.4; `SendAsync` lança `NotImplementedException`). Fakes e apoio: `FakeProcessRunner`, `ClaudeOutputs` (objeto de resultado com `"type": "result"`, §3), `ManualTimeProvider`. Testes, todos com pasta de trabalho **já existente**, `OutputMode` na caixa canônica (`JsonSchema` ou `Tags`), request de 1 mensagem e saída válida (`Structured` ou `Text`):
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

  (commits: `test(infra): claude code invocation` + `feat(infra): run claude code headless per turn`) — feito: a848a42, 3b7fd18 (Red: 6 casos com `NotImplementedException`). Desvio de forma no teste: o `[Theory]` `WithOutputMode` recebe o arquivo de formato e se espera schema como parâmetros, em vez de um `if` (AP-020)
  Escolhas de forma: nenhuma
- [x] T6 — Red (4 métodos / 5 casos):
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenWorkingDirectoryDoesNotExist_CreatesItAndKeepsItEmpty`: falha porque a pasta não é criada.
  - `ClaudeCodeTutorBrainTests.SendAsync_WithEnvironmentVariableInWorkingDirectory_ExpandsIt`: falha porque o caminho mantém `%...%`.
  - `ClaudeCodeTutorBrainTests.SendAsync_AfterReply_LogsLatencyWithoutConversationText`: falha porque não há entrada 2001.
  - `ClaudeCodeTutorBrainTests.SendAsync_WithOutputModeInOtherCase_UsesCanonicalModeInArgumentsAndLog` (2 casos): falham com `NotImplementedException("US-004 T6")`, porque `"jsonschema"` e `"TAGS"` não são a caixa canônica.

  Green: passo 2 da §2.4 (expansão, caminho completo, criação, modo canônico via `BrainOutputModes.TryGetCanonical`), medição com `TimeProvider` e log 2001 com o modo canônico. (commits: `test(infra): claude code working directory, output mode and latency log` + `feat(infra): isolate claude code working directory, normalize output mode and log latency`) — feito: 95dc6f7, 369608a (Red pelos motivos previstos: pasta não criada, `%...%` mantido, sem entrada 2001, `NotImplementedException("US-004 T6")`)
  Escolhas de forma: §12 em `9e49cf0` (E1)
- [x] T7 — Red (4 métodos / 9 casos):
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenAnswerIsOutOfFormat_ThrowsInvalidOutputWithRawText`: recebe `NotImplementedException`.
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsFailure_ThrowsUnknownWithDetail` (2 casos): recebe `NotImplementedException`.
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenClaudeReportsErrorWithExitCodeZero_ThrowsUnknownWithoutParsing` (3 casos): recebe `NotImplementedException` (`is_error` com código 0, estado da T5).
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenOutputIsNotJsonResult_ThrowsUnknownWithExpectation` (3 casos, estado da T5): `"not-json"` recebe `JsonException`; `"other-type"` não recebe exceção nenhuma (devolve `Reply t1?`); `"json-array"` recebe `InvalidOperationException`.

  O Executor confere esses motivos antes do Green. Green: a tabela da §2.5 em `ClaudeCodeOutput.Read`, na ordem das linhas 1–5 (a linha 2 com as duas partes da condição), e `BrainException.InvalidOutput` no passo 10. (commits: `test(infra): claude code failures and invalid output` + `feat(infra): translate claude code failures into brain exceptions`) — feito: b71da6c, 91e0ce2 (Red conferido: 6 casos com `NotImplementedException("US-004 T7")`; `not-json` com `JsonReaderException`; `other-type` sem exceção; `json-array` com `InvalidOperationException`)
  Escolhas de forma: nenhuma
- [x] T8 — Red (5 métodos / 9 casos):
  - `ClaudeCodeTutorBrainTests.SendAsync_WithHistoryAndNoKnownSession_SendsRenderedTranscriptWithoutResume`
  - `ClaudeCodeTutorBrainTests.SendAsync_OnSecondTurnWithUseResume_ResumesSessionAndSendsOnlyNewMessage`
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenUseResumeIsDisabled_SendsFullTranscriptWithoutResume`
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript` (5 casos)
  - `ClaudeCodeTutorBrainTests.SendAsync_WhenRetryingRejectedAnswer_ResumesWithOnlyFormatCorrection`

  Motivos do Red (estado da T5): nos testes de texto renderizado, o stdin traz só a última mensagem; nos de retomada, falta `--resume`. Green: passos 3, 4 e 9 da §2.4 (`_session` com system prompt, regra de extensão exata, renderer). `SendAsync_WithSingleMessage_SendsItUnchangedAsStandardInput` segue verde. (commits: `test(infra): claude code session continuity` + `feat(infra): resume claude code session only when the window is extended`) — feito: 0b3dde6, fcdf0a7 (Red: stdin só com a última mensagem; `--resume` ausente)
  Escolhas de forma: nenhuma
- [x] T9 — Red: esqueleto `ProcessRunner` (`RunAsync` lança `NotImplementedException`), `RecordingProcessRunner`, `LiveClaudeCode` (system prompt lido de `AppContext.BaseDirectory/prompts/tutor.md`, review S9) e os testes `LiveBrain` (todos com `[Trait("Category", "LiveBrain")]`):
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat`
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion` (renomeado na T21 para `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion`)
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply`
  - `FixtureRecorderLiveTests.SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes`

  `dotnet test --filter "Category=LiveBrain"` → os 4 falham com `NotImplementedException`. O D2 continua verde, sem `LiveBrain`. Green: `ProcessRunner` da §2.1 (leituras iniciadas antes da escrita do stdin; `IOException` de pipe fechado ignorada). Os 4 ficam verdes com o CLI real. Se a fumaça ficar abaixo de 4/5, parar e registrar na §12 (risco de prompt, não de código). (commits: `test(infra): live claude code smoke tests` + `feat(infra): run external processes with tree kill on cancellation`) — feito: d6ed73e (Red: 4 `LiveBrain` com `NotImplementedException`), 014746f (Green: os 4 verdes juntos com o CLI 2.1.304). Bloqueio inicial em §12 E2 (76c88f7). **Ressalva:** o critério "5/5 no formato" do D5 é intermitente (≈ 20% das respostas fora do formato), registrado em §12 E3 (c8af838) para decisão do usuário
  Escolhas de forma: nenhuma
- [x] T10 — Fixtures: copiar `recorded-fixtures/claude-json-structured.json` e `claude-json-tags.json` (gravados na T9) para `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/`. Depois, conferir que a resposta estruturada tem a correção `since five years` → `for five years` (com "contém", sem diferenciar maiúsculas; se não tiver, rodar o gravador de novo) e que não há dado pessoal (D13). Conferir também que as fixtures têm `"type": "result"`, como os auxiliares de `ClaudeOutputs` (§3); se o formato real divergir dos auxiliares, parar e registrar na §12. Testes:
  - `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId`
  - `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedTagsOutput_ReturnsTutorReply`

  Os dois nascem verdes (caracterização do formato real). Red por mutação temporária não commitada, pelo efeito, uma por vez: (a) "o `session_id` lido deixa de ser guardado" → o primeiro falha na asserção de `--resume`; (b) "o texto do campo `result` deixa de ser usado no modo Tags" → o segundo falha. Registrar as falhas na tarefa. (commit: `test(infra): recorded claude code output fixtures`) — feito: f3f153c. Fixtures gravadas sem as variáveis herdadas do app desktop: a 1ª gravação Tags, com o ambiente herdado, trazia o nome do titular da conta e foi descartada (§12 E3). Ambas têm `"type": "result"`, como os auxiliares de `ClaudeOutputs`; a estruturada tem `since five years` → `for five years`. Mutações temporárias não commitadas, restauradas de cópia: (a) `_session = null;` antes do parser → `WithRecordedStructuredOutput` falhou com `Expected ValueAfter(..., "--resume") to be "4575c852-…", but found <null>`; (b) `result` ignorado (`: string.Empty`) → `WithRecordedTagsOutput` falhou com `BrainException ... missing reply`
  Escolhas de forma: nenhuma
- [x] T11 — Red: esqueleto `LatencySummary.Compute(IReadOnlyList<TimeSpan>)` (lança `NotImplementedException`; devolve `record LatencyStats(int Count, TimeSpan Mean, TimeSpan P95)`) + `LatencySummaryTests.Compute_WithFiveSamples_ReturnsMeanAndMaxAsP95` e `LatencySummaryTests.Compute_WithTwentySamples_ReturnsNineteenthSmallestAsP95`. Green: média e posto mais próximo `⌈0,95·n⌉`. Depois, `LatencyMeasurementLiveTests.SendAsync_FiveSentencesInEachOutputMode_ReportsMeanAndP95Latency` (LiveBrain), rodado como D7. A tabela vai para §11 e para o ADR-002, numa seção "Medição (US-004)" com data, modelo e máquina. Todo o código desta tarefa fica no projeto de teste, por isso os commits são `test` (review S10). (commits: `test(infra): latency summary statistics` + `test(infra): measure claude code latency per output mode` + `docs(ai): record claude code latency in ADR-002`) — feito: `LatencySummary` em e3923db (Red: 2 casos com `NotImplementedException`) e 34e1e31 (Green); `LatencyMeasurementLiveTests` em f604c37, rodado como D7; tabela no ADR-002 em 2fd8419. Desvio: o Green do `LatencySummary` ganhou commit próprio `test(infra): compute mean and nearest-rank p95 latency`
  Escolhas de forma: nenhuma
- [x] T12 — Red: esqueleto `FileTutorPromptSource.GetSystemPromptAsync` (lança `NotImplementedException`) + os 3 testes, que falham com essa exceção:
  - `FileTutorPromptSourceTests.GetSystemPromptAsync_WhenTutorFileExists_ReturnsItsContent`
  - `FileTutorPromptSourceTests.GetSystemPromptAsync_WhenTutorFileIsMissing_ThrowsWithPathAndExpectation`
  - `FileTutorPromptSourceTests.GetSystemPromptAsync_AfterFileChanges_ReturnsNewContent`

  Green: leitura a cada chamada, com `ct`, e mensagem da §2.3. (commits: `test(infra): system prompt from prompts folder` + `feat(infra): read tutor system prompt from file`) — feito: 61c1436, 777b50f
  Escolhas de forma: nenhuma
- [x] T13 — Red: esqueleto `AddEnglishTutorInfrastructure` (só a guarda e `return services`) e os testes:
  - `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource`: falha porque `ITutorBrain` não está registrado.
  - `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeOptionsValidator`: falha porque nada é lançado.
  - `CoreServiceCollectionExtensionsTests.AddEnglishTutorCore_WithBrainAndPromptSource_RegistersConversationServiceAsSingleton`: falha porque o `ConversationService` não está registrado.

  Green: registros da §2.7. (commits: `test(infra): infrastructure and conversation service registration` + `feat(infra): register claude code brain and conversation service`) — feito: bf4d744, 90f23bc
  Escolhas de forma: nenhuma
- [x] T14 — Esqueleto (compila, comportamento igual): `IChatInputHandler` com `Mode`/`ChangeMode`; `TutorNotConnectedInputHandler` com o modo num campo; `RecordingChatInputHandler` com `Mode`/`ChangeMode`/`ModeChanges`; `ConversationInputHandler` com `HandleAsync`, `Mode` e `ChangeMode` lançando `NotImplementedException`; `ScriptedTutorBrain`, `FixedTutorPromptSource` e `ConsoleTutorReplies` em `Console.Tests/Support`. Red, os 4 falhando com `NotImplementedException`:
  - `ConversationInputHandlerTests.HandleAsync_WhenTutorReplies_ShowsReplyAndFeedbackLines`
  - `ConversationInputHandlerTests.HandleAsync_WhenReplyIsRawFallback_ShowsOnlyReply`
  - `ConversationInputHandlerTests.HandleAsync_WhenInputIsBlank_ShowsLocalWarning`
  - `ConversationInputHandlerTests.ChangeMode_WithMode_ChangesConversationServiceMode`

  Green: delegação e textos da §2.6 (`ConsoleText.TutorReply`, `FeedbackHeader`, prefixo `  - `). (commits: `test(console): show tutor reply and feedback` + `feat(console): delegate free text to the conversation service`) — feito: 5ce7e4d, db267b2
  Escolhas de forma: nenhuma
- [x] T15 — Refactor primeiro: o `ChatSession` lê o modo do handler e o construtor perde `TutorMode mode`; `CreateSession` e `EnglishTutorApp.RunSessionAsync` acompanham. Os 30 casos do Console existentes nesse ponto (25 da base + 1 da T1 + 4 da T14) continuam verdes (commit `refactor(console): read session mode from input handler`; review S8). Red:
  - `ChatSessionTests.RunAsync_WhenUserTypesModeWithKnownName_ChangesModeAndConfirms`
  - `ChatSessionTests.RunAsync_WhenUserTypesHelp_ListsModeCommandWithAllModes`
  - `ChatSessionTests.RunAsync_WhenUserTypesModeWithUnknownOrMissingName_ShowsAvailableModesAndKeepsMode` (2 casos)
  - e os 2 testes existentes ajustados (§4).

  Os 3 novos e `RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands` falham na asserção (hoje a saída é `Unknown command "/mode".` e a lista é `/help, /quit`). `RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` com `"/foo bar"` já passa: é regressão. Green, em dois commits por escopo (review S10): primeiro `LocalCommands.Available` com `Mode` (Core); depois `case LocalCommandKind.Mode`, `ConsoleText.ModeChanged` e `ConsoleText.ModeNotRecognized` (Console). (commits: `refactor(console): read session mode from input handler` + `test(console): mode command in the console` + `feat(core): list mode among available local commands` + `feat(console): change tutor mode with /mode`) — feito: 80f5188, 60c05d9, 80d46c0, b233bdc (30 casos verdes no Refactor; Red: 3 novos + `UnknownCommand` na asserção, `/foo bar` já verde)
  Escolhas de forma: nenhuma
- [x] T16 — Red: esqueleto `AppRunOptions.ConfigureServices`, já aplicado no fim do `BuildHost` (encanamento de teste; sem ele o arranjo não compila). `TestConfiguration.Create` passa a aceitar valores extras. `CreateOptions` registra `ScriptedTutorBrain` e `FixedTutorPromptSource`. O teste existente é renomeado para Ollama (§4). Testes:
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

  (commits: `test(console): console talks to the conversation service` + `feat(console): connect the console to the claude code tutor`) — feito: fade2ae, 8669458 (Red: 5 testes pelos motivos previstos). Mutação temporária não commitada (handler de conversa para qualquer provedor, via `provider.Length >= 0`) → `RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected` falhou: a saída não contém `The tutor is not connected yet.` (o `ScriptedTutorBrain` sem resposta lançou e o app saiu com 1); revertida
  Escolhas de forma: nenhuma
- [x] T17 — Red (B1, P19): `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (2 casos). Com os binds da T16, os dois casos falham com código 1 ≠ 2 e `An unexpected error occurred. See the log file for details.` em `Error`. O Executor confirma no Red que o motivo é o descrito: o evento `UnexpectedError` (1003) do log tem `AggregateException` com `OptionsValidationException` internas. Se o Red não acontecer por esse motivo, parar e registrar na §12. Green: P19 / §2.6 (falhas de validação de `OptionsValidationException` ou de `AggregateException` só com `OptionsValidationException`; `Invalid configuration:` + uma linha por falha; `StartupFailed` com todas; `ExitCodes.InvalidConfiguration`). Os testes de configuração inválida da US-001 e o da T16 seguem verdes. (commits: `test(console): invalid configuration in several sections` + `feat(console): explain every options validation failure at startup`) — feito: ef51f51, bfc45e8 (Red: 2 casos com código 1 ≠ 2; motivo confirmado rodando o app com `Brain__Provider=Inexistente` e `Conversation__MaxTurnsInWindow=0`: evento `UnexpectedError` com `System.AggregateException` contendo duas `OptionsValidationException`)
  Escolhas de forma: nenhuma
- [x] T18 — (2026-10-19, P20/P21) **Controle do D16, antes do Red (review S3 da Rodada 4):** rodar o bloco D16 e o `git grep` do D16c (§10) com o repositório ainda sem as mudanças desta revisão (as mudanças de documentos do orquestrador já estão aplicadas). Os dois têm de listar as linhas antigas esperadas pelo D16c, para provar que os padrões e os caminhos varridos enxergam o texto antigo; colar a saída resumida em §11 (seção nova da revisão). Se algum padrão esperado não aparecer, parar e registrar na §12: o D16 não serve como evidência até ser corrigido. Depois, Red: esqueleto `ReplySentences` (§2.10.1) com `Split` e `IsQuestion` lançando `NotImplementedException`. Testes, todos falhando com `NotImplementedException` (3 métodos / 19 casos):
  - `ReplySentencesTests.Split_WithReplyText_ReturnsSentencesAtBoundaries` (10 casos da tabela "Bordas do separador", §4)
  - `ReplySentencesTests.Split_WhenTextIsBlank_ReturnsNoSentences` (2 casos)
  - `ReplySentencesTests.IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` (7 casos)

  Green: definições da §2.10.1, só com BCL (`CoreDependencyTests` continua verde) e sem a chamada `EndsWith('?')`. Nesta tarefa ninguém chama o `ReplySentences` ainda: o validador muda na T20. (commits: `test(core): split tutor reply into sentences` + `feat(core): split tutor reply into sentences and detect questions`) — feito: c482f1e (controle D16c: `named=3` e todos os padrões esperados na parte 1, os 6 arquivos na parte 2), fcf85b2 (Red: 19 casos com `NotImplementedException`), cebf808 (Green)
  Escolhas de forma: nenhuma
- [x] T19 — (2026-10-19, P23) Red: esqueleto em `TutorReplyRejections` com a constante `ReplyMustAskExactlyOneQuestionReason` completa (é dado) e a fábrica `ReplyMustAskExactlyOneQuestion(string reply, int questionCount)` lançando `NotImplementedException`. `ReplyMustEndWithQuestionReason` e `ReplyMustEndWithQuestion` ficam até a T20, porque o validador ainda os usa. Testes (6 casos falhando com `NotImplementedException`):
  - `TutorReplyRejectionsTests.Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation` (linha alterada para a fábrica nova, §2.10.4; só esse caso falha, os outros 3 seguem verdes)
  - `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WhenReplyExceeds80Characters_QuotesFirst80WithEllipsis` (renomeado)
  - `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WhenReplyHas80Characters_QuotesItWhole` (renomeado)
  - `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` (3 casos)

  Green: mensagens da §2.10.2 (os dois ramos, `has no question` e `has <n> questions`; corte em 80 caracteres reaproveitado). (commits: `test(core): rejection message for exactly one question` + `feat(core): explain the exactly-one-question rejection`) — feito: db3bafd (Red: 6 casos com `NotImplementedException`), 0ad07c0 (Green)
  Escolhas de forma: nenhuma
- [x] T20 — (2026-10-19, RN-FEED01) Red, sem esqueleto novo (tudo já compila):
  - `TutorReplyValidatorTests.Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply` (4 casos): falham porque a regra antiga recusa reply que não termina em `?` (`IsValid` = `false`).
  - `TutorReplyValidatorTests.Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason` (4 casos): falham porque os 4 terminam em `?` e a regra antiga os aceita (`IsValid` = `true`).
  - `TutorReplyValidatorTests.Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` (3 casos; renomeado, §2.10.4): falham no `Reason` (`reply must end with a question` ≠ `reply must ask the student exactly one question`).
  - `TutorReplyParserTests.Parse_WhenJsonReplyAsksQuestionThenEncourages_ReturnsReply`: falha porque o reply é recusado.
  - `TutorReplyParserTests.Parse_WhenJsonReplyAsksTwoQuestions_RejectsWithExactlyOneQuestionReason`: falha porque o reply é aceito.
  - `TutorReplyParserTests.Parse_WhenJsonReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` e `TutorReplyParserTests.Parse_WhenTagsReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` (renomeados): falham no `Reason`.

  São 11 casos do Core e 4 da Infrastructure vermelhos pelos motivos acima; o Executor confere antes do Green. Os demais testes do validador, do parser, do `ConversationService`, do adaptador e do console seguem verdes. Nos mesmos commits `test`, os comentários `// US-002 — Cenário: reply sem pergunta final ...` dos testes de reply sem pergunta e de `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed` passam a citar o cenário `reply sem pergunta` (§2.10.4, review S2 da Rodada 4). Green: §2.10.3 (`ReplySentences.Split` + `Count(ReplySentences.IsQuestion)` + `ReplyMustAskExactlyOneQuestion`; a verificação `reply.EndsWith('?')` sai). Refactor: remover `ReplyMustEndWithQuestionReason` e `ReplyMustEndWithQuestion` (sem chamador). D2 com 249 casos verdes no fim da tarefa. (commits: `test(core): reply asks exactly one question` + `test(infra): parser applies the one-question rule` + `feat(core): accept exactly one question in any position of the reply` + `refactor(core): remove the final-question rejection`) — feito: 31bf6c2 e b82638b (Red: 11 casos do Core e 4 da Infrastructure, pelos motivos previstos; comentários de cenário atualizados), 3f02faa (Green), 99e2e5c (Refactor); D2 = 249 casos
  Escolhas de forma: nenhuma
- [x] T21 — (2026-10-19, P18/P24) Ajuste de asserções em testes existentes, sem teste novo (§2.10.4):
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion` (renomeado de `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion`; o regex `SentenceEnd` sai);
  - `ClaudeCodeTutorBrainLiveTests.SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply`;
  - `FixtureRecorderLiveTests.SendAsync_WithGrammarErrorSentence_SavesRawOutputOfBothModes`;
  - `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedStructuredOutput_ReturnsCorrectionAndKeepsSessionId`;
  - `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedTagsOutput_ReturnsTutorReply`.

  Estado de Red: nenhum. O comportamento conferido não muda (as fixtures têm uma pergunta, no fim); a asserção passa a seguir a regra da spec. Os 2 testes unitários seguem verdes (D11: 31 casos). Os 3 `LiveBrain` rodam no D5/T23, não aqui, para não gastar a assinatura antes do ajuste de prompt (T22). (commit: `test(infra): check the one-question rule in live and fixture tests`) — feito: ed41b9a (as asserções `EndWith("?")`/`EndsWith` viraram "exatamente uma pergunta"; o regex `SentenceEnd` e o `partial` da classe saíram)
  Escolhas de forma: nenhuma
- [x] T22 — (2026-10-19) Prompts: aplicar os textos novos da §2.10.5 em `prompts/tutor.md`, `prompts/format.json.md` e `prompts/format.tags.md` (só as 3 linhas citadas). Sem teste unitário de conteúdo de prompt: `PromptFilesDeploymentTests.ApplicationFolder_Always_ContainsTutorAndFormatPrompts` continua verde, o D16 confere que o texto antigo saiu, e o efeito no modelo é medido no D5 (T23). (commit: `feat(brain): describe the one-question rule in the tutor prompts`, review S1) — feito: 11514ab (`feat(brain)`, como a §5 fixa; só as 3 linhas citadas)
  Escolhas de forma: nenhuma
- [ ] T23 — DoD (era a T18 antes da revisão de 2026-10-19): rodar **D1–D6 e D8–D18** (o D7, a medição de latência, **não se repete**: foi feito e registrado no ADR-002 em 2fd8419, antes da regra nova, e a latência não é critério da RN-FEED01; review S1 da Rodada 4), colar em §11 (numa seção nova, sem apagar a rodada anterior; o controle do D16, feito na T18, já está nela) e marcar a §5 com os hashes. Cada `[x]` leva o hash na própria linha (AP-031). O D5 roda 5 vezes seguidas, no ambiente descrito na §10. Se alguma rodada do D5 ficar vermelha, registrar a resposta recusada e o motivo na §11 e na §12 e parar: a próxima decisão é do usuário. Ao registrar, o Executor classifica a causa: (a) resposta fora do formato pela regra (zero ou duas perguntas); (b) mais de 3 frases pela contagem do `ReplySentences.Split` com abreviação ou outro limite conhecido do separador (`Mr.`, `Dr.`, P22), que é **limite do separador**, não falha do modelo nem da regra; (c) correção esperada ausente; (d) outra. A classificação (b) também vai para a §12, porque a decisão sobre o separador é do usuário (review S4 da Rodada 4). (commit: `docs(ai): add DoD evidence for US-004`) — rodada anterior (antes da revisão) em §11: todos verdes, menos o D5, intermitente (§12 E3/E4) — **parcial**: D1–D4, D6, D8–D18 verdes e colados na §11 (11514ab); D5 vermelho na rodada 1, classificado como (a) com (d) no diagnóstico (§12 E5); decisão do usuário pendente
  Escolhas de forma: nenhuma

Conferência das contagens por tarefa: `ClaudeCodeTutorBrainTests` = T5 5/6 + T6 4/5 + T7 4/9 + T8 5/9 + T10 2/2 = 20 métodos / 31 casos. Console.Tests = T1 1/1 + T14 4/4 + T15 3/4 + T16 5/5 + T17 1/2 = 14 / 16. Infrastructure.Tests = T2 4/7 + T3 1/1 + T4 2/2 + `ClaudeCodeTutorBrainTests` 20/31 + T11 2/2 + T12 3/3 + T13 2/2 + T20 2/2 = 36 / 50. Core.Tests = T13 1/1 + T18 3/19 + T19 1/3 + T20 2/9 = 7 / 32. Total: 57 / 98 (antes da revisão: 49 / 65; revisão: +8 / +33).

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Options (10.0.13, já no CPM, grupo "Core") | Infrastructure | `IOptions<ClaudeCodeOptions>`, `IValidateOptions<T>` |
| Microsoft.Extensions.DependencyInjection.Abstractions (10.0.13, já no CPM, grupo "Core") | Infrastructure | `TryAddSingleton`/`TryAddEnumerable` na extensão de registro (hoje vem só de forma transitiva) |
| Microsoft.Extensions.DependencyInjection (10.0.13, já no CPM, grupo "Tests") | Infrastructure.Tests | `ServiceCollection.BuildServiceProvider()` nos testes de registro |

Nenhuma versão nova no `Directory.Packages.props` e nenhum pacote novo no Core (`CoreDependencyTests` não muda). `NullLogger<>` vem de `Microsoft.Extensions.Logging.Abstractions`, que já está nos projetos. Sem `Microsoft.Extensions.TimeProvider.Testing`: o `ManualTimeProvider` de ~15 linhas basta. A revisão de 2026-10-19 não acrescenta pacote: o `ReplySentences` usa só a BCL (R1).

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
| (2026-10-20, S7 do pedido do usuário; S2 do `review.md`) O processo `claude` herda o **ambiente** de quem abre o console (`CLAUDECODE`, `CLAUDE_CODE_*`, `ANTHROPIC_BASE_URL`); numa gravação feita com o ambiente herdado do app desktop, o nome do titular da conta chegou ao contexto do professor (§12 E3) | **Risco aceito de forma explícita, por enquanto**, junto ao da linha anterior (S11). Motivo: hoje o app é local e usado por uma pessoa na própria máquina, e o dado só vai ao contexto do professor de quem já é titular da conta; o impacto é baixo. Mitigações já em vigor: as fixtures commitadas foram gravadas sem as variáveis herdadas e passaram no D13; o comando do D5/D6/D7 limpa as variáveis (§10). **Condição para revisitar:** o app ser distribuído a outras pessoas, ou ser aberto por um app desktop ou ambiente com variáveis de conta de terceiros. Nessa hora, o usuário decide a limpeza do ambiente do processo (ex.: no `ProcessRunner`) com um ADR. **Sem US de destino, sem ADR agora e sem promessa para outra US** (AP-022); nada a registrar em spec |
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
| Asserção satisfeita por texto externo (AP-015) | As mensagens conferidas têm trechos do projeto (`Claude Code failed (exit code`, `Claude Code reported an error:`, `is not a JSON result`, `Expected prompts/tutor.md`, `Invalid configuration:`, `reply must ask the student exactly one question`, `has no question`, `has 2 questions`) |
| Contagens divergentes no fim (AP-031) | Conferência por tarefa no fim da §5; o Executor atualiza cabeçalho, detalhamento, D2 e D11 juntos |
| (2026-10-19) Com o prompt mais permissivo, o modelo passa a fazer duas perguntas (ex.: "... Right?", "And you?") e o D5 continua intermitente por outro motivo | O texto novo dos prompts proíbe a segunda pergunta e cita os exemplos (§2.10.5). O D5 roda 5 vezes seguidas (§10); qualquer rodada vermelha → parar, registrar a resposta recusada e o motivo (T23). A decisão seguinte (prompt, critério ou modelo) é do usuário |
| (2026-10-19) O separador erra em casos raros: frase nova começando com minúscula depois de `?` (duas perguntas contadas como uma) ou pergunta citada seguida de maiúscula (contada como pergunta) | Limites aceitos e documentados (P22), com teste do lado conferido (`Your boss said "Really?" Then ...`). Se o `eval` (US-009) mostrar muitas rejeições pelo motivo novo, o ADR-005 revisado manda rever o separador |
| (2026-10-20, S4 da Rodada 4) O separador também conta frases a mais com abreviação seguida de maiúscula (`Mr. Smith`, `Dr. Lee`); o teste `LiveBrain` do cenário 1 conta frases com `ReplySentences.Split` (máximo 3) e roda no D5 com tolerância zero em 5 rodadas | Efeito reconhecido (P22 lista os consumidores). Probabilidade baixa: as 5 frases de fumaça não falam de pessoas nem de títulos. Se uma rodada ficar vermelha só por esse motivo, o Executor a registra na T23 como **limite do separador** (classificação (b)), com a resposta recusada, e para; a decisão (aceitar, lista de abreviações pelo ADR-005 ou mudar o teste) é do usuário. Não é falha do modelo nem da regra |
| (2026-10-19) A regra nova vale para todos os provedores (ADR-005) e para a retentativa (RN-BRAIN04) | Regra só no `TutorReplyValidator`; `BuildFormatCorrection` embute a mensagem nova sem mudança de código; a suíte do Core e do parser roda inteira na T20 (D2 com 249) |
| (2026-10-19) Texto antigo da regra sobrevive em código, testes, prompts ou documentos e gera falso bloqueante (AP-005) | Lista fechada de trechos na §2.10.4–§2.10.6, incluindo comentários com o nome antigo do cenário e o exemplo do `tdd-dotnet` (2026-10-20); o D16 procura os textos antigos e a forma `EndsWith('?')` e espera nenhuma linha; o D16c mostra, antes da T18, que o mesmo comando acha as linhas antigas (outro lado do limite) |
| (2026-10-19) Evidência do D5 depende do ambiente de quem roda (variáveis herdadas do app desktop; review S3) | O comando do D5 limpa `CLAUDECODE`, `CLAUDE_CODE_*` e `ANTHROPIC_BASE_URL` antes das 5 rodadas (§10) |

## 8. Fora de escopo

- Classificação de falhas (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`), `TimeoutSeconds` e mensagens claras ao aluno com a sessão continuando: **US-005**. A spec já cobre a classificação (cenários "CLI não encontrada", "não autenticado", "limite de uso atingido", "tempo esgotado" e D5). A sessão continuando depois de **qualquer** falha do cérebro, inclusive `Unknown`, e a troca do teste da P11 entram na spec da US-005 antes da T1: cenário "falha do cérebro no console não encerra a sessão" e D7 (§2.8, AP-022; B2). Até lá, falha do cérebro encerra com `ExitCodes.UnexpectedError` (P11).
- Timeout: o item [O] "Processos externos com `ArgumentList`, timeout e encerramento da árvore" do `checklist-revisao` §4 vale como **N/A para o timeout** nesta US, por decisão da spec (US-005, cenário "tempo esgotado"; P6). `ArgumentList` e encerramento da árvore no cancelamento valem e estão na §2.1 (review S12).
- Ollama no console: **US-006**, com o cenário "console conversa com o Ollama" acrescentado à spec antes da T1 (§2.8, AP-022).
- `BrainFactory`, troca de cérebro por configuração e fallback: US-007. Suíte de contrato: US-008, que vai consumir o `FakeProcessRunner` e as fixtures desta US.
- Comando `eval`, golden set e relatório: US-009. O `LatencySummary` desta US mora no projeto de teste, e a US-009 decide se o promove.
- `ClaudeCodeStreamingBrain` (processo persistente, `stream-json`): Fase 6.
- Persistência de turnos e resumo de erros recorrentes (RN-PERS*, RN-CONV04), voz e TTS.
- Validação de "no máximo 3 frases" (RN-FEED01) no Core: fica no prompt e no teste `LiveBrain` do cenário 1, que conta as frases com o `ReplySentences` do Core (P18). Mudar o `TutorReplyValidator` para contar frases seria decisão do ADR-005, revisitável na US-009. Nenhuma US de destino: o "fica no prompt" não adia nada.
- Remover `BrainRequest.SessionHint` do contrato: fica reservado (P1).
- Restringir as fontes de configuração de usuário da CLI: risco aceito (§7); sem US de destino até haver decisão de ADR.
- (2026-10-19) Regravar as fixtures `claude-json-*.json`: desnecessário, as duas já têm exatamente uma pergunta (§2.10.4).
- (2026-10-19) Lista de abreviações e casos linguísticos além dos da tabela "Bordas do separador": limites aceitos (P22); qualquer mudança passa pelo ADR-005 revisado, com o `eval` da US-009 como gatilho. Não é adiamento com US de destino.
- (2026-10-20, S7 do pedido do usuário; S2 do `review.md`) **Privacidade do ambiente herdado pelo processo `claude`: risco aceito de forma explícita, por enquanto** (motivo, mitigações em vigor e condição para revisitar na §7, linha de 2026-10-20). Fica fora desta US, **sem US de destino, sem ADR agora e sem promessa para outra US**; por isso não há texto a registrar em spec de destino (AP-022). A decisão fica registrada na §12 antes da próxima revisão de código.
- (2026-10-19) Teste do `TutorNotConnectedInputHandler.ChangeMode` (S4 do `review.md`): não faz parte da decisão sobre a RN-FEED01; segue para decisão do usuário, fora desta revisão.

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
- **P18 — "No máximo 3 frases" (RN-FEED01).** **Padrão proposto (revisto em 2026-10-19):** conferido só no teste `LiveBrain` do cenário 1, contando as frases com `ReplySentences.Split` do Core (o mesmo separador da regra de pergunta), em vez do regex próprio do teste; o `TutorReplyValidator` (ADR-005) não conta frases. Teste: `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion`.
- **P19 — Configuração inválida em mais de uma seção ao mesmo tempo (B1).** **Padrão proposto:** com `BrainOptions`, `ClaudeCodeOptions` e `ConversationOptions` validadas na partida, o `ValidateOnStart` lança `OptionsValidationException` quando uma só classe falha e `AggregateException` (uma `OptionsValidationException` por classe) quando mais de uma falha. O `EnglishTutorApp` trata as duas formas como configuração inválida: escreve `Invalid configuration:` e uma linha `  <falha>` para cada falha de cada classe, na ordem em que vêm; loga `StartupFailed` com todas; devolve `ExitCodes.InvalidConfiguration`, sem cabeçalho nem prompt (mesmos critérios da US-001, AP-024). `AggregateException` com interna de outro tipo continua erro inesperado (§2.9). Alternativa: um validador único para as três seções (acopla Core e Infrastructure). Teste: `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration`.
- **P20 — (2026-10-19) Como o Core separa o reply em frases.** **Padrão proposto:** `ReplySentences.Split` no Core, só com BCL. Há limite depois de uma sequência de terminadores (`.`, `!`, `?`, reticências U+2026), seguida de fechos opcionais, quando vem o fim do texto ou espaço em branco seguido de caractere que não é letra minúscula. As frases saem com `Trim()` e sem vazias (§2.10.1). Alternativas: contar os `?` do texto inteiro (um `?` citado no meio da frase contaria como pergunta) ou usar um pacote de tokenização (viola R1). Teste: `ReplySentencesTests.Split_WithReplyText_ReturnsSentencesAtBoundaries`.
- **P21 — (2026-10-19) O que é "frase que termina com `?`".** **Padrão proposto:** depois de remover os fechos do fim (`"`, `'`, `)`, `]`, U+201D, U+2019), a sequência final de terminadores contém `?`. Assim, `Really?!`, `What??` e `Why?"` são perguntas. Leitura da RN-FEED01: o sinal que encerra a frase inclui `?`. Alternativa: só o último caractere ser `?`, o que deixaria `Really?! What happened next?` passar como uma pergunta só. Teste: `ReplySentencesTests.IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` e `TutorReplyValidatorTests.Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason` (caso `"Really?! What happened next?"`).
- **P22 — (2026-10-19; consumidores e efeito no D5 acrescentados em 2026-10-20, review S4 da Rodada 4) Limites aceitos do separador.** **Padrão proposto:** sem lista de abreviações. `Mr. Smith` vira limite. Frase nova começando com minúscula depois de `?` não é separada. Pergunta citada seguida de maiúscula (`said "Really?" Then`) conta como pergunta. **Consumidores do `ReplySentences` e o efeito de cada limite:** (1) o `TutorReplyValidator`, que só conta perguntas: a abreviação seguida de maiúscula **não** muda a contagem de perguntas, e os outros dois limites mudam (aceitos); (2) o teste `LiveBrain` do cenário 1 (`SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion`), que conta **frases** (máximo 3) e roda no D5 com tolerância zero: uma resposta de 3 frases reais com `Mr.`, `Dr.` e similares conta 4 e deixa a rodada vermelha. Probabilidade baixa (as 5 frases de fumaça não falam de pessoas nem de títulos), e o efeito não é pior que o do regex `SentenceEnd` que o teste usava antes; (3) o D5 (T23): uma rodada vermelha por esse motivo é registrada como **limite do separador** (classificação (b) da T23), não como falha do modelo nem da regra, e a decisão é do usuário; (4) o D6, conferido a olho. O `eval` (US-009) é o gatilho para rever o separador (ADR-005 revisado). Teste: `TutorReplyValidatorTests.Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply` (caso `Mr. Smith`) e `TutorReplyValidatorTests.Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason` (caso da citação seguida de maiúscula).
- **P23 — (2026-10-19) Texto do motivo e da mensagem.** **Padrão proposto:** motivo `reply must ask the student exactly one question` (o da spec), com a mensagem da §2.10.2: `has no question` com 0 e `has <n> questions` com 2 ou mais, citação cortada em 80 caracteres, e `Expected 1 to 3 short sentences with exactly one question to the student (one sentence ending with "?", in any position); the other sentences are comments or encouragement.`. A retentativa usa a mensagem sem mudança no `TurnMessageBuilder`. Teste: `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound`.
- **P24 — (2026-10-19) Testes de fixture e `LiveBrain` que conferem `EndWith("?")`.** **Padrão proposto:** trocar por "exatamente uma pergunta" com `ReplySentences` (§2.10.4), sem regravar as fixtures (as duas têm uma pergunta). Alternativa: manter `EndWith("?")`, o que faria um teste falhar com uma resposta que a spec aceita. Teste: `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion`.
- **P25 — (2026-10-19) `specs/PRODUCT.md` (OA1) ainda diz "respostas curtas que sempre terminam em pergunta".** **Padrão proposto:** trocar por "sempre fazem exatamente uma pergunta" no commit `docs(ai): update spec for US-004` antes da T18 (§2.10.6), porque a spec da RN-FEED01 (`ea8744d`) não o alterou. Sem efeito observável (conferido pelo D16).
- **P26 — (2026-10-19) Quantas rodadas do D5 valem como "verde".** **Padrão proposto:** 5 rodadas completas seguidas, todas verdes, sem as variáveis herdadas do app desktop (comando na §10). O D5 mede no adaptador, **sem** a retentativa do ADR-006, com tolerância zero (5/5 no formato em cada rodada). Qualquer rodada vermelha → parar e registrar (T23); não se repete a rodada para "pegar" uma verde (aprendizado do `review.md`: critério `LiveBrain` precisa dizer a camada e sobreviver a N rodadas seguidas). Teste: `ClaudeCodeTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat`.

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell. As exceções são o D15 (Git Bash) e o D6/D9 (Windows PowerShell 5.1). D5, D6 e D7 exigem o Claude Code CLI instalado e autenticado. **Ambiente dos comandos com o CLI real (D5, D6, D7):** num terminal aberto fora do app desktop ou depois de `Remove-Item Env:CLAUDECODE, Env:ANTHROPIC_BASE_URL -ErrorAction SilentlyContinue; Get-ChildItem Env:CLAUDE_CODE_* | Remove-Item` (review S3; §12 E3).

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: 249 casos (Core.Tests 134, Console.Tests 41, Infrastructure.Tests 74), em < 30 s, sem rede, sem GPU e sem processo real |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `Remove-Item Env:CLAUDECODE, Env:ANTHROPIC_BASE_URL -ErrorAction SilentlyContinue; Get-ChildItem Env:CLAUDE_CODE_* \| Remove-Item; 1..5 \| ForEach-Object { dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ClaudeCode"; "round $_ exit=$LASTEXITCODE" }` | 5 linhas `round <n> exit=0` (5/5 rodadas seguidas verdes, P26). Camada: o adaptador, sem a retentativa do ADR-006; tolerância zero. Em cada rodada: 5 frases de fumaça com erros conhecidos, ≥ 4 com a correção esperada e 5/5 no formato pela RN-FEED01 nova (os 3 testes de `ClaudeCodeTutorBrainLiveTests`; medição e gravador ficam fora do filtro). Rodada vermelha: a T23 classifica a causa, inclusive "limite do separador" (P22) |
| D6 | No Windows PowerShell 5.1, no ambiente acima: `"I have worked with C# since five years" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | cabeçalho `Brain: ClaudeCode \| Mode: free-conversation`; `Tutor: ...` com exatamente uma pergunta (uma frase terminada em `?`, em qualquer posição); linha de feedback com `since five years → for five years` (seta legível); `exit=0` (a entrada termina depois da frase); a primeira linha chega com BOM (AP-006) |
| D7 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~LatencyMeasurementLiveTests" --logger "console;verbosity=detailed"` | tabela com média e p95 de `JsonSchema` e de `Tags` (5 frases cada), colada nas Evidências e no ADR-002 (já feito em 2fd8419; **não se repete na T23**, que roda D1–D6 e D8–D18) |
| D8 | `Select-String -Path src/EnglishTutor.Console/appsettings.json -Pattern '"MaxTurnsInWindow": 10'` | uma linha, dentro da seção `Conversation` (RN-CONV03) |
| D9 | No Windows PowerShell 5.1: `"/mode job-interview", "/help", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | aviso `Mode changed to "job-interview". It applies from your next sentence.`; `/help` lista `/mode`; nenhum `Unknown command`; `exit=0` |
| D10 | Inspeção de `tasks/US-004/plan.md` | a P1 decide como a continuidade de sessão chega ao adaptador (`session_id` guardado no adaptador, RN-BRAIN01), com testes |
| D11 | `dotnet test --filter "FullyQualifiedName~ClaudeCodeTutorBrainTests"` | 31 casos verdes (20 métodos); os testes `LiveBrain` têm outra classe (`ClaudeCodeTutorBrainLiveTests`) e não entram |
| D12 | `Test-Path "$env:LOCALAPPDATA\EnglishTutor\brain"; (Get-ChildItem -Force -Recurse "$env:LOCALAPPDATA\EnglishTutor\brain").Count` (após D6) | duas linhas: `True` e `0` (a pasta existe e está vazia, RN-BRAIN06; review S7) |
| D13 | `Select-String -Path tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/*.json -Pattern 'Users','@','sk-ant'` | nenhuma linha (fixtures sem caminho pessoal, e-mail ou chave) |
| D14 | `git status --short` após D2–D13 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |
| D15 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D16 | (2026-10-19; reforçado em 2026-10-20, S2/S3/S5 da Rodada 4) bloco D16 abaixo, depois da T22 | primeira linha impressa `named=3` (os 3 arquivos nomeados foram varridos); depois, **nenhuma linha** (o texto da regra antiga, os comentários com o nome antigo do cenário, a forma `EndsWith('?')` e o exemplo antigo do `tdd-dotnet` saíram de código, testes, prompts, skills e documentos; AP-005) |
| D16c | (2026-10-20, S3 da Rodada 4; **controle do D16, antes da T18**) bloco D16 e `git grep` do bloco D16c abaixo | o bloco D16 lista as linhas antigas do código, dos testes e dos prompts (padrões esperados abaixo do bloco); o `git grep` no `ea8744d` lista as linhas antigas dos documentos e da skill. Prova que padrões e caminhos enxergam o texto antigo; o D16 só vale como evidência depois dele |
| D17 | (2026-10-19) `dotnet test tests/EnglishTutor.Core.Tests --filter "FullyQualifiedName~ReplySentencesTests"` | 19 casos verdes (3 métodos) |
| D18 | (2026-10-19; DoD D5 da US-002) `dotnet test --filter "FullyQualifiedName~TutorReplyParser"` | verde, incluindo fixtures JSON e tags e os 2 testes novos da RN-FEED01 |

D15 (fora da tabela por causa do `|`; forma única do `checklist-revisao` §4, AP-019):

```bash
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
```

D16 (PowerShell; fora da tabela por causa do `|`). Os caminhos nomeados entram por `Get-Item`, e não misturados com pastas no `-Path` do `Get-ChildItem`, e o comando imprime quantos arquivos varreu e se os 3 nomeados estão entre eles, para um verde falso por caminho não varrido aparecer na evidência:

```powershell
$patterns = 'must end with a question', 'ReplyMustEndWithQuestion', 'LAST sentence', 'EndWith("?")', "EndsWith('?')", 'pergunta final', 'NoFinalQuestion', 'ReturnsReplyEndingWithQuestion', 'terminam em pergunta', 'reply termina com pergunta', 'terminando em `?`', "termina com '?'"
$files = @(Get-ChildItem -Recurse -File -Path src, tests, prompts, docs\adr, .claude\skills -Include *.cs, *.md | Where-Object { $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*' }) + @(Get-Item docs\us\US-002.md, ARCHITECTURE.md, specs\PRODUCT.md)
"scanned=$($files.Count) named=$(@($files | Where-Object Name -in 'US-002.md', 'ARCHITECTURE.md', 'PRODUCT.md').Count)"
$files | Select-String -SimpleMatch -Pattern $patterns
```

D16c (controle, antes da T18). Parte 1: o mesmo bloco D16 acima, com o repositório sem as mudanças desta revisão (as de documentos do orquestrador já aplicadas). Parte 2, para os documentos e a skill, que a parte 1 já não acha por terem sido atualizados:

```powershell
git grep -n -F -e 'terminam em pergunta' -e 'reply termina com pergunta' -e 'terminando em `?`' -e "termina com '?'" -e 'must end with a question' -e 'ReturnsReplyEndingWithQuestion' ea8744d -- ARCHITECTURE.md specs/PRODUCT.md docs/adr docs/us/US-002.md .claude/skills
```

Esperado no D16c: (parte 1) `named=3` e pelo menos uma linha para cada um destes padrões: `must end with a question` e `ReplyMustEndWithQuestion` (`src/EnglishTutor.Core/Brain/TutorReplyRejections.cs`, `TutorReplyValidator.cs` e os testes), `LAST sentence` (`prompts/`), `EndWith("?")` (testes), `EndsWith('?')` (`TutorReplyValidator.cs` e `FixtureRecorderLiveTests.cs`), `pergunta final` e `NoFinalQuestion` (`TutorReplyParserTests.cs` e `TutorReplyValidatorTests.cs`). (parte 2) pelo menos uma linha de cada um destes arquivos: `ARCHITECTURE.md`, `specs/PRODUCT.md`, `docs/adr/ADR-002-contrato-saida-estruturada.md`, `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md`, `docs/us/US-002.md` e `.claude/skills/tdd-dotnet/SKILL.md`. O `CHANGELOG.md` não é varrido (registro histórico, §2.10.6).

Também entram, da DoD comum:

- todo cenário BDD com teste escrito antes do código;
- `review.md` sem bloqueantes;
- `docs/us/US-004.md` criado;
- ADR-007 aceito;
- `ARCHITECTURE.md` §3/§5/§6.3, `padroes-codigo`, `tdd-dotnet` e a US-005 e a US-006 do `SPRINT_1.md` atualizados antes da T1 (§2.8);
- (2026-10-19) `specs/PRODUCT.md`, `ARCHITECTURE.md` §3/§6.2, ADR-002, ADR-005, `docs/us/US-002.md` e (2026-10-20) o exemplo do `tdd-dotnet` atualizados antes da T18 (§2.10.6);
- tabela de latência no ADR-002 (D7);
- merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

**Revisão de 2026-10-19 (RN-FEED01):** a rodada abaixo é anterior à mudança da regra. A T18 registra antes o controle do D16 (D16c), e a T23 roda a DoD de novo (D1–D6 e D8–D18, com o D5 cinco vezes; o D7 não se repete) e acrescenta uma seção nova no fim desta, sem apagar a anterior.

Rodada final de 2026-10-19, HEAD `c8af838` (último código em `f3f153c`), Claude Code CLI 2.1.304 (instalador nativo, `claude-haiku-5-5`). Uma rodada anterior, sem o CLI (HEAD `bfc45e8`), ficou registrada em §12 E2. **A US não está pronta para fechar: o D5 é intermitente (§12 E3) e depende de uma decisão do usuário.**

| # | Resultado | Status |
|---|---|---|
| D1 | `dotnet build` → 0 Aviso(s), 0 Erro(s) (2026-10-19, c8af838) | ✅ |
| D2 | `dotnet test --filter "Category!=LiveBrain"` → Core.Tests 103 + Console.Tests 41 + Infrastructure.Tests 72 = **216 passed, 0 failed**, 4,7 s, sem rede nem processo real (2026-10-19, c8af838) | ✅ |
| D3 | 3 `coverage.cobertura.xml` em `TestResults/` (2026-10-19, c8af838) | ✅ |
| D3b | `EnglishTutor.Core` **100%**; Infrastructure 83,5%; Console 93,4%; total 88,2% (2026-10-19, c8af838) | ✅ |
| D4 | `dotnet format --verify-no-changes` → código de saída 0 (2026-10-19, c8af838) | ✅ |
| D5 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ClaudeCode"` → **intermitente**. Com o ambiente herdado: 1 rodada com os 3 verdes (T9 Green, 014746f) e 1 com `WithFiveKnownErrors` vermelho (`outOfFormat` = `Yesterday I go to the office by bus`). Sem as variáveis herdadas: 3 rodadas, 1 verde e 2 vermelhas (`reply must end with a question`). Correções esperadas sempre ≥ 4/5; o que falha é o "5/5 no formato" (≈ 20% das respostas, 4/20 no diagnóstico). Sem mudança de prompt ou de critério, que cabe ao usuário (§12 E3) | ❌ intermitente |
| D6 | Windows PowerShell 5.1: `"I have worked with C# since five years" \| dotnet run --project src/EnglishTutor.Console` → `Brain: ClaudeCode \| Mode: free-conversation`; `Tutor: That's great, five years is a solid amount of experience with C#! What kind of projects have you been building lately?`; `Feedback:` + `  - I have worked with C# since five years → I have worked with C# for five years (...)` com a seta legível; primeira linha com BOM tratada; `exit=0` (2026-10-19, c8af838) | ✅ |
| D7 | `LatencyMeasurementLiveTests` (sem as variáveis herdadas) → JsonSchema n=5, média 4982 ms, p95 5554 ms, 0 fora do formato; Tags n=5, média 3642 ms, p95 4183 ms, 1 fora do formato. Tabela no ADR-002 (2fd8419) | ✅ |
| D8 | `appsettings.json:13:    "MaxTurnsInWindow": 10`, dentro de `Conversation` (2026-10-19, c8af838) | ✅ |
| D9 | Windows PowerShell 5.1: `"/mode job-interview", "/help", "/quit" \| dotnet run ...` → `Mode changed to "job-interview". It applies from your next sentence.`; `/help` lista `  /mode - Change the mode: free-conversation, job-interview, listening-practice`; nenhum `Unknown command`; `exit=0` (2026-10-19, c8af838) | ✅ |
| D10 | P1 decide que o `session_id` fica no adaptador (RN-BRAIN01, ADR-007), com testes verdes; retomada real confirmada por `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` (`--resume` com `--system-prompt-file` reenviado funcionou) | ✅ |
| D11 | `dotnet test --filter "FullyQualifiedName~ClaudeCodeTutorBrainTests"` → **31 passed** (20 métodos), 0 failed (2026-10-19, c8af838) | ✅ |
| D12 | Após o D6: `Test-Path` → `True`; contagem → `0` (2026-10-19) | ✅ |
| D13 | `Select-String ... -Pattern 'Users','@','sk-ant'` → nenhuma linha; também sem o nome do titular da conta (2026-10-19, f3f153c) | ✅ |
| D14 | `git status --short` após D2–D13 → vazio (2026-10-19, c8af838) | ✅ |
| D15 | Git Bash: `git diff develop...HEAD \| LC_ALL=C grep -c ...` → `0`, código de saída 1 (esperado) (2026-10-19, HEAD do commit de evidências) | ✅ |

Risco S11 (§7), anotação pedida: o `claude --help` (2.1.304) oferece `--setting-sources <sources>` (lista de fontes de configuração a carregar), `--settings <file-or-json>` e `--exclude-dynamic-system-prompt-sections`. Qualquer um deles poderia restringir a configuração de usuário. Adotar é decisão de ADR em outra US, não do Executor. A herança de variáveis do app desktop também levou dados da conta ao contexto (§12 E3).

**Nova medição depois da opção (a) do E3** (2026-10-19; prompts em `2cb0f7a`: `tutor.md`, `format.json.md` e, para os dois modos ficarem coerentes, `format.tags.md` dizem que a pergunta é a última frase do `reply` e que nada vem depois dela). Tudo sem as variáveis herdadas do app desktop:

| Medição | Antes (prompts de `f3f153c`) | Depois (`2cb0f7a`) |
|---|---|---|
| Diagnóstico por chamada (5 frases × 4 rodadas, processo novo por chamada; teste temporário, não commitado) | 4/20 fora do formato (20%) | 0/20 na 1ª rodada + 2/20 na 2ª = **2/40 (5%)** |
| Rodadas completas do D5 (`Category=LiveBrain&FullyQualifiedName~ClaudeCode`) | 1 verde de 3 | **2 verdes de 5** (rodadas 1 e 3; as outras falharam em `WithFiveKnownErrors` ou `WithGrammarErrorSentence` por `reply must end with a question`) |
| Respostas fora do formato dentro das rodadas do D5 (8 chamadas por rodada) | — | 4/40 (10%) |

Exemplos que continuam recusados: `"... What kind of projects do you work on there? I'd love to hear more about your day-to-day work."` e `"... Which kind of projects do you usually build with it? Tell me about your most recent one."`. As correções esperadas seguem ≥ 4/5 em todas as rodadas.

Depois da mudança de prompts: D1 → 0 Aviso(s), 0 Erro(s); D2 → 216 passed (103 + 41 + 72), 0 failed; D4 → código de saída 0 (2026-10-19, 2cb0f7a). **O D5 continua ❌ intermitente**: a taxa caiu de cerca de 20% para 5–10% por chamada, mas "5/5 no formato" ainda falha em 3 de 5 rodadas. A T18 fica aberta (§12 E4).

### Revisão de 2026-10-19 (RN-FEED01): evidências da T18 à T23

**T18 — controle do D16 (D16c), antes de qualquer mudança desta revisão** (2026-10-20, HEAD `d39ffab`; documentos do orquestrador já aplicados):

- Parte 1 (bloco D16 no repositório): `scanned=131 named=3`. Padrões encontrados: `must end with a question` (`TutorReplyRejections.cs:9`, `TutorReplyRejectionsTests.cs:29`, `TutorReplyValidatorTests.cs:49`, `TutorReplyParserTests.cs:107,216`); `ReplyMustEndWithQuestion` (`TutorReplyRejections.cs`, `TutorReplyValidator.cs:21`, `TutorReplyRejectionsTests.cs`); `LAST sentence` (`prompts/tutor.md:19`, `format.json.md:5`, `format.tags.md:6`); `EndWith("?")` (`ClaudeCodeTutorBrainLiveTests.cs:48,74`, `ClaudeCodeTutorBrainTests.cs:410,430`); `EndsWith('?')` (`TutorReplyValidator.cs:19`, `FixtureRecorderLiveTests.cs:36`); `pergunta final` (`TutorReplyValidatorTests.cs:40,53`, `TutorReplyParserTests.cs:98,209`); `NoFinalQuestion` (`TutorReplyParserTests.cs:100,211`). Todos os padrões esperados apareceram.
- Parte 2 (`git grep` no `ea8744d`): linhas em `ARCHITECTURE.md:189`, `specs/PRODUCT.md:15`, `docs/adr/ADR-002-contrato-saida-estruturada.md:20,30`, `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md:18,35`, `docs/us/US-002.md:35` e `.claude/skills/tdd-dotnet/SKILL.md:86`. Os 6 arquivos esperados apareceram.
- Conclusão: o D16 enxerga o texto antigo no código, nos testes e nos prompts, e a parte 2 o enxerga nos documentos e na skill; o D16 vale como evidência depois da T22.

**T23 — DoD da revisão (RN-FEED01)** (2026-10-20, código em `11514ab`; Claude Code CLI 2.1.304, `claude-haiku-5-5`; D7 não se repete). **A T23 parou no D5: a rodada 1 ficou vermelha.** Ambiente do D5 e do D6: `Remove-Item Env:CLAUDECODE, Env:ANTHROPIC_BASE_URL; Get-ChildItem Env:CLAUDE_CODE_* | Remove-Item`, na mesma sessão. Sobraram 4 variáveis que o §10 não manda remover: `CLAUDE_AGENT_SDK_MCP_NO_PREFIX`, `CLAUDE_AGENT_SDK_VERSION`, `CLAUDE_PID` e `CLAUDE_PREVIEW_CLASSIFIER_FLOOR`.

| # | Resultado | Status |
|---|---|---|
| D1 | `dotnet build` → 0 Aviso(s), 0 Erro(s) (11514ab) | ✅ |
| D2 | `dotnet test --filter "Category!=LiveBrain"` → Core.Tests 134 + Console.Tests 41 + Infrastructure.Tests 74 = **249 passed, 0 failed**, 5,2 s (11514ab) | ✅ |
| D3 | 3 `coverage.cobertura.xml` (11514ab) | ✅ |
| D3b | `EnglishTutor.Core` **100%**; Infrastructure 83,5%; Console 93,4%; total 88,7% (11514ab) | ✅ |
| D4 | `dotnet format --verify-no-changes` → código de saída 0 (11514ab) | ✅ |
| D5 | **Rodada 1: `exit=1`** (3 testes: 2 verdes, 1 vermelho). Vermelho: `SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat`, `Expected outOfFormat to be empty because every answer must follow the format (5/5), but found at least one item {"Yesterday I go to the office by bus"}`. Parei, sem rodar as rodadas 2 a 5 (P26). Classificação abaixo | ❌ rodada 1 vermelha |
| D6 | Windows PowerShell 5.1, mesma sessão limpa do D5: cabeçalho `Brain: ClaudeCode \| Mode: free-conversation`; `Tutor: Five years with C# is a great amount of experience! It sounds like you know the language well. What kind of projects do you usually work on at your job?` (3 frases, uma pergunta, no fim); `Feedback:` + `  - I have worked with C# since five years → I have worked with C# for five years (...)` com a seta legível; `exit=0` | ✅ |
| D8 | `appsettings.json:13`, dentro de `Conversation` | ✅ |
| D9 | `Mode changed to "job-interview". It applies from your next sentence.`; `/help` lista `  /mode - Change the mode: free-conversation, job-interview, listening-practice`; nenhum `Unknown command`; `exit=0` | ✅ |
| D10 | Inspeção: a P1 (`session_id` no adaptador, ADR-007) segue no plano, com os testes de continuidade verdes | ✅ |
| D11 | `ClaudeCodeTutorBrainTests` → **31 passed** | ✅ |
| D12 | Depois do D6: `True` e `0` | ✅ |
| D13 | `Select-String ... 'Users','@','sk-ant'` → 0 linhas | ✅ |
| D14 | `git status --short` depois de D2–D13 → só `tasks/US-004/plan.md` (esta evidência); nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` | ✅ |
| D15 | Git Bash → `0`, código de saída 1 (esperado) (11514ab) | ✅ |
| D16 | `scanned=133 named=3` e **nenhuma linha** depois da T22 (o controle D16c da T18 tinha achado o texto antigo em todos os padrões e arquivos esperados) | ✅ |
| D17 | `ReplySentencesTests` → **19 passed** (3 métodos) | ✅ |
| D18 | `FullyQualifiedName~TutorReplyParser` → **26 passed**, incluindo os 2 testes novos da RN-FEED01 | ✅ |

**Cobertura de linhas do Core: 100%.**

**D5, rodada 1: classificação da causa.** A resposta recusada da rodada 1 **não foi capturada**: o teste `WithFiveKnownErrors` só guarda a frase (`catch (BrainException) when (IsInvalidOutput)`), e não o `RawText` nem o motivo. Pela construção do caminho (`OutputMode = JsonSchema`, o schema garante JSON válido e `reply` presente), um `InvalidOutput` aqui só vem da regra da RN-FEED01: zero ou duas ou mais perguntas. Isso é **(a)** resposta fora do formato pela regra. Não dá para dizer se foram zero ou duas perguntas. Não é **(b)**, porque o teste que conta frases (`WithGrammarErrorSentence`) passou nessa rodada. A correção esperada **(c)** não foi o motivo: o teste que a confere nem chegou a ser decidido por ela, já que a falha de formato vem antes. Para a decisão do usuário, e sem fazer parte do veredito do D5, rodei um diagnóstico temporário (não commitado, apagado): 5 frases × 4 rodadas, uma chamada `SendAsync` por frase, no mesmo ambiente limpo. Resultado: **0/20 fora do formato pela RN-FEED01** (antes da regra nova e do ajuste de prompt: 2/40 no melhor caso, §12 E4), mas **1/20 com 4 frases** (`I am working here since 2020` → `Almost six years! That's a solid stretch at one company. Your team must know your work very well. What do you enjoy most about your job there?`). Essas 4 frases são de verdade (não é limite do separador, **(b)**); é o modelo passando de 3 frases, e o teste do cenário 1 (`HaveCountLessThanOrEqualTo(3)`) falharia nesse caso. Isso é **(d)**, outra causa, e é um risco para as próximas rodadas do D5.

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
- **E3 (T9, D5 — parada, decisão do usuário)** — Com o CLI instalado (2.1.304, `claude-haiku-5-5`), o `ProcessRunner` ficou verde (`014746f`: os 4 testes `LiveBrain` da T9 passaram juntos uma vez) e as correções da fumaça vêm certas (≥ 4/5 em todas as rodadas). Mas o critério **"5/5 no formato"** do D5 falha de forma intermitente. O modelo às vezes fecha o `reply` com uma frase depois da pergunta, e o `TutorReplyValidator` recusa (`reply must end with a question`). Medição: diagnóstico temporário, não commitado, com as 5 frases × 4 rodadas, cada uma num processo novo e sem as variáveis herdadas → **4/20 fora do formato (20%)**. Exemplos: `"Oh, you took the bus to the office yesterday! Was it busy in the morning? Tell me a little about your commute."` e `"... Which part of the new architecture did the team discuss the most? I'm curious whether it's about microservices or the database."`. Nas rodadas completas do D5: 1 verde e 3 vermelhas em ambiente limpo, 1 vermelha com o ambiente herdado. O teste do cenário 1 (`SendAsync_WithGrammarErrorSentence_...`) falhou 2 vezes em cerca de 34 chamadas pelo mesmo motivo.
  - **Herança das variáveis do app desktop** (`CLAUDECODE`, `CLAUDE_CODE_*`, `ANTHROPIC_BASE_URL`): **não** explica a falha de formato, que se repete sem elas. Ela influenciou outra coisa: a primeira gravação da fixture Tags, feita com o ambiente herdado, chamou o aluno pelo nome do titular da conta (dado pessoal). As 3 gravações seguintes, sem as variáveis, não trouxeram o nome. As fixtures commitadas (`f3f153c`) foram gravadas sem as variáveis e passaram no D13 e numa busca pelo nome. O app real herda o ambiente de quem o abre; abrir o console pelo app desktop pode levar dados da conta ao contexto do professor (ligado ao risco S11 da §7).
  - O Executor **não** mudou prompt, teste nem código para contornar (o plano diz que a correção é de prompt ou de modelo, decidida pelo usuário). No app, a retentativa do ADR-006 cobre a maior parte desses casos (o D6 saiu correto). Opções para o usuário: (a) reforçar em `prompts/tutor.md`/`format.json.md` que a pergunta é a **última** frase; (b) aceitar a taxa e mudar o critério do D5 (mudança de spec); (c) trocar o modelo.

### 2026-10-19 — decisão do usuário sobre o E3 (D5 intermitente)

Opção (a): reforçar em `prompts/tutor.md` e `prompts/format.json.md` que a pergunta é a **última** frase do `reply` (nada depois dela). Não muda spec, teste, validador nem modelo.

- O Executor faz a alteração dos prompts num commit próprio (`feat(brain)` ou `fix(brain)`), sem tocar em `format.tags.md` além do necessário para manter os dois modos coerentes.
- Depois, roda o D5 várias vezes (pelo menos 5 rodadas completas, sem as variáveis herdadas do app desktop) e registra na §11 a taxa de respostas fora do formato antes × depois e quantas rodadas do D5 ficaram verdes.
- Se o D5 continuar intermitente, o Executor registra o resultado e para; a próxima decisão é do usuário.
- **E4 (D5 depois da opção (a) — parada)** — O reforço nos prompts (`2cb0f7a`) reduziu as respostas fora do formato de cerca de 20% (4/20) para 5–10% (2/40 no diagnóstico, 4/40 nas rodadas do D5), mas o D5 ficou verde em só 2 de 5 rodadas. Detalhes na §11. Conforme a instrução, o Executor registrou o resultado e parou, sem mexer em spec, testes, validador ou modelo. A T18 continua aberta. O que resta é decisão do usuário: outro ajuste de prompt, aceitar a taxa (a retentativa do ADR-006 cobre no app) mudando o critério do D5 na spec, ou trocar o modelo. Escopo do commit: os prompts entraram como `docs(ai)`; eles mudam o comportamento do professor, e `feat(brain)` seria mais preciso.

### 2026-10-19 — decisão do usuário: mudar a RN-FEED01 (resolve o B1 de `review.md`)

O usuário decidiu tratar o D5 mudando a regra, não o critério do D5 nem o modelo. Spec já atualizada no commit `docs(ai): update spec for US-004` desta data (`BUSINESS_RULES.md` RN-FEED01 + histórico; `SPRINT_1.md` US-002 cenário "reply sem pergunta", US-004 cenário 1, dois cenários novos na US-004 e D6).

- **Regra nova (RN-FEED01):** o `reply` faz **exatamente uma** pergunta: separado em frases, uma única frase termina com `?`, em qualquer posição; as outras são comentário ou incentivo. Zero perguntas ou duas ou mais perguntas → inválido.
- **Motivo de rejeição renomeado:** `reply must end with a question` → `reply must ask the student exactly one question` (constante, mensagem detalhada e instrução de retentativa coerentes com a regra nova; ARCHITECTURE §10).
- O critério do D5 ("5/5 no formato") **não muda**; ele passa a medir a regra nova. O reforço de prompt de `2cb0f7a` pode ser ajustado para descrever a regra nova (uma pergunta, em qualquer posição), sem incentivar duas perguntas.
- O plano precisa: tarefas em TDD no Core (`TutorReplyValidator`, `TutorReplyRejections` e testes da US-002 que citam o motivo antigo, com o separador de frases definido e testado, inclusive bordas como `?` no meio de aspas, reticências e abreviações, se relevantes); ajuste do teste `LiveBrain` do cenário 1 (`EndWith("?")`); os dois cenários novos da US-004 no mapa da §4; menções em ADR-002 (linhas 20 e 30) e ADR-005 a atualizar; prompts; contagens, §10 e §11 (nova rodada do D5, pelo menos 5 vezes, e D6).

  → Incorporado. O plano volta para "Aguardando aprovação". Item a item:
  - Regra nova e status → Cabeçalho (Status, Regras, ADRs), §1, §2.10 (abertura).
  - Separador de frases definido e testado, com bordas (`?` entre aspas, reticências, abreviações, decimal, `?!`, quebra de linha, texto vazio) → §2.10.1, §2 "Alternativas descartadas", §4 (tabela "Bordas do separador"; `ReplySentencesTests.Split_WithReplyText_ReturnsSentencesAtBoundaries`, `ReplySentencesTests.Split_WhenTextIsBlank_ReturnsNoSentences`, `ReplySentencesTests.IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark`), §5 T18, §9 P20, P21 e P22, §10 D17. (O nome do último teste foi trocado em 2026-10-20, review S6 da Rodada 4; ver a resposta da rodada 4, abaixo.)
  - Motivo renomeado, mensagem e retentativa → §2.10.2 (a retentativa embute a mensagem sem mudança no `TurnMessageBuilder`), §2.9 (guardas novas), §4 (`TutorReplyRejectionsTests`: 1 linha alterada, 2 renomeados, `ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` novo), §5 T19, §9 P23.
  - `TutorReplyValidator` e testes da US-002 com o motivo antigo → §2.10.3, §2.10.4 (tabela dos 10 testes existentes que mudam, com o motivo), §4 (validador: 2 novos e 1 renomeado; parser: 2 novos e 2 renomeados), §5 T20 (Red por caso, com o motivo esperado; Refactor remove a fábrica antiga).
  - Dois cenários novos da US-004 no mapa → §4 (linhas "pergunta seguida de incentivo é aceita" e "reply com duas perguntas é inválido", no parser e no validador).
  - Teste `LiveBrain` do cenário 1 e demais `EndWith("?")` → §2.10.4, §4 (linha do cenário 1 renomeada; linhas da retomada, do gravador e das fixtures anotadas), §5 T9 (nota do nome novo) e T21, §9 P18 (revista) e P24, §8 (fixtures não são regravadas).
  - ADR-002 (linhas 20 e 30), ADR-005, `ARCHITECTURE.md` §3/§6.2, `docs/us/US-002.md` e `specs/PRODUCT.md` (OA1, que ainda dizia "sempre terminam em pergunta") → §2.10.6 (texto antigo e novo de cada trecho; dois commits do orquestrador antes da T18; `CLAUDE.md` e skills sem alteração, conferido com Grep), §3, §5 (pré-condição da revisão), §9 P25, §10 DoD comum e D16. Nenhum ADR novo. A "ARCHITECTURE §10" citada no pedido corresponde, no texto atual, à §6.2 (onde está a regra do validador e a mensagem de rejeição); a §10 é a estratégia de testes e não muda.
  - Prompts → §2.10.5 (texto exato das 3 linhas; tarefa do Executor), §5 T22 (commit `feat(brain)`, review S1), §7.
  - Contagens → §4 ("Contagens da revisão": +8 métodos / +33 casos; Core.Tests 134, Infrastructure.Tests 74, Console.Tests 41), §5 (conferência por tarefa: 57 / 98), §10 D2 (249) e D11 (sem mudança, 31).
  - §10 e §11 (nova rodada do D5, pelo menos 5 vezes, e D6) → §10 (D5 com 5 rodadas seguidas, limpeza do ambiente e camada medida, review S3 e aprendizado do `review.md`; D6 com "exatamente uma pergunta"; D16–D18), §9 P26, §5 T23 (antiga T18, renumerada para o fim; para na primeira rodada vermelha), §11 (nota: a rodada nova entra numa seção própria).
  - Riscos → §7 (5 linhas novas: duas perguntas com o prompt permissivo, limites do separador, alcance da regra em todos os provedores, texto antigo sobrevivente, ambiente do D5).
  - Não incorporado neste item: S2 (privacidade do ambiente herdado) e S4 (teste do `ChangeMode` com Ollama) do `review.md`, que não fazem parte da decisão sobre a RN-FEED01 (§8). (Atualização de 2026-10-20: a S2 passou a risco aceito, ver a resposta da rodada 4, S7.)

### 2026-10-20 — pedido do usuário (sugestões da rodada 4, `plan-review.md`)

O usuário aprovou o plano revisado (RN-FEED01 nova, T18–T23, P20–P26 com as respostas padrão) e pediu:

- Incorporar as sugestões **S1–S6** da Rodada 4 (texto completo e "Esperado" em [plan-review.md](plan-review.md), Rodada 4).
- **S7 — risco de privacidade do ambiente herdado (S2 do `review.md`): aceito de forma explícita por enquanto.** O processo `claude` herda o ambiente de quem abre o console (`CLAUDECODE`, `CLAUDE_CODE_*`, `ANTHROPIC_BASE_URL`); numa gravação, o nome do titular da conta chegou ao contexto do professor. Sem US de destino e sem ADR agora. Registrar na §7 e na §8 como risco aceito (junto ao S11), com o motivo e a condição para revisitar (ex.: quando o app for distribuído a outras pessoas ou aberto por um app desktop), sem criar promessa para outra US (AP-022). As fixtures commitadas foram gravadas sem as variáveis herdadas.

Depois dos ajustes e de uma revisão do plano sem bloqueantes, o plano está aprovado pelo usuário.

  → Incorporado. Item → seções tocadas (a conferência é mecânica; nenhuma contagem de testes mudou, então a §4 "Contagens" só ganhou o novo nome do teste de S6):
  - **S1** → Cabeçalho (`Status:` cita T18–T23 e a revisão de 2026-10-20), §1 (parágrafo novo), §5 T23 (roda D1–D6 e D8–D18; o D7 não se repete, com o motivo) e §10 D7 (mesma frase) e §11 (nota de abertura, que também cita o D16c). Os dois lados dizem agora a mesma coisa: o D7 foi feito em 2fd8419 e fica dispensado.
  - **S2** → §2.10.4 (linha nova: comentários `Cenário: reply sem pergunta final` de `TutorReplyValidatorTests`, nos dois pontos citados, e de `TutorReplyParserTests`, com o texto antigo e o novo), §4 ("Testes existentes alterados": 11 itens), §3 (linhas de `TutorReplyValidatorTests` e `TutorReplyParserTests`), §5 T20 (os comentários mudam nos commits `test`), §10 D16 (padrão `pergunta final` e `NoFinalQuestion`). O `CHANGELOG.md` (entrada da US-002, "reply com pergunta final") fica de fora de propósito: é registro histórico (§2.10.6).
  - **S3** → §10 D16 (reescrito: `Get-Item` para os 3 caminhos nomeados, contagem `scanned`/`named` impressa, padrão `EndsWith('?')` acrescentado, `.claude\skills` varrido), §10 D16c (novo: controle antes da T18, com o mesmo bloco e um `git grep` no `ea8744d` para os documentos, e a lista do que deve aparecer), §5 T18 (executa o controle antes do Red e cola em §11; para se algum padrão não aparecer) e T23, §2.10.1 (o código novo não usa `EndsWith('?')`), §7 (linha do texto antigo sobrevivente), §11 (nota).
  - **S4** → §9 P22 (consumidores do `ReplySentences` com o efeito de cada limite, incluindo o teste `LiveBrain` do cenário 1 e o D5), §7 (linha nova), §2.10.1 (parágrafo dos limites), §5 T23 (classificação (a)–(d) da causa de rodada vermelha; "limite do separador" vai também para a §12), §10 D5.
  - **S5** → Decisão: o exemplo do `tdd-dotnet` acompanha a regra nova (`SendAsync_WithValidOutput_ReturnsReplyWithOneQuestion`). §2.10.6 (texto antigo e novo; commit `docs(ai): update architecture for US-004` do orquestrador, antes da T18; parágrafo "Repetições em `CLAUDE.md` e skills" corrigido), §3 (linha do `tdd-dotnet`), §5 (pré-condição da revisão), §10 D16 (varre `.claude\skills`, padrão `ReturnsReplyEndingWithQuestion`) e DoD comum.
  - **S6** → `IsQuestion_WithSentence_ReturnsWhetherItEndsWithQuestionMark` virou `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark`, igual na §4 (linha do mapa e contagens), §5 T18 e §9 P21; também no histórico acima (resposta de 2026-10-19), para uma busca pelo nome antigo não achar mais nada. A contagem (7 casos) não muda.
  - **S7** → §7 (linha nova: risco aceito de forma explícita, com motivo, mitigações em vigor e condição para revisitar; sem US de destino, sem ADR agora e sem promessa), §8 (item reescrito; o teste do `TutorNotConnectedInputHandler.ChangeMode`, S4 do `review.md`, ficou em item próprio, ainda para decisão do usuário), §1 e o fecho da resposta de 2026-10-19 acima. Nada a registrar em spec de destino (AP-022), porque não há US de destino. A decisão fica aqui na §12 antes da próxima revisão de código.

### 2026-10-20 — aprovação do usuário (revisão da RN-FEED01)

Plano revisado aprovado pelo usuário no chat, com as sugestões S1–S6 da rodada 4 incorporadas e a rodada 5 da revisão do plano sem bloqueantes.

- Perguntas em aberto P20–P26: valem as respostas padrão propostas na §9.
- S7 (privacidade do ambiente herdado): risco aceito de forma explícita por enquanto (§7, §8).
- §2.10.6 aplicada antes da T18: `docs(ai): update spec for US-004` (`specs/PRODUCT.md`) e `docs(ai): update architecture for US-004` (`ARCHITECTURE.md`, ADR-002, ADR-005, `docs/us/US-002.md`, `tdd-dotnet`).
- As sugestões S1 e S2 da rodada 5 são ajustes de texto (frase do D6 na sessão do D5; a frase "não achar mais nada" da §12) e não bloqueiam; o Executor aplica a S1 ao rodar o D6 na mesma sessão limpa do D5.

### 2026-10-20 — Executor: T23 parou no D5 (rodada 1 vermelha)

- **E5 (D5, parada, decisão do usuário)** — Depois da regra nova (T18 a T22, prompts em `11514ab`), a **rodada 1** do D5 ficou vermelha no ambiente limpo definido na §10: `WithFiveKnownErrors`, frase `Yesterday I go to the office by bus` fora do formato. Parei sem repetir rodadas (P26). Causa: **(a)** resposta fora do formato pela regra (zero ou duas perguntas); a resposta recusada não foi capturada pelo teste, então não sei qual dos dois casos. Não é **(b)** (limite do separador) e não é **(c)**. Um diagnóstico temporário (não commitado) com 20 chamadas deu 0/20 fora da regra de perguntas, mas **1/20 com 4 frases** (o modelo passa do máximo de 3), o que faria o teste do cenário 1 falhar; é **(d)**, falha do modelo, não do separador. O D6 e os demais itens passaram. Decisões possíveis (do usuário): (1) mais um ajuste de prompt, reforçando "no máximo 3 frases" e a contagem de perguntas; (2) fazer o `WithFiveKnownErrors` guardar o `RawText` e o motivo da recusa, para a próxima rodada permitir classificar (mudança de teste, fora do plano aprovado); (3) aceitar a taxa e mudar o critério do D5 na spec (a retentativa do ADR-006 cobre no app); (4) trocar o modelo. A T23 fica `[ ]` até lá.
