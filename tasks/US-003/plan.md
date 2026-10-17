# Plano — US-003 ConversationService dono do histórico

Status: Aguardando aprovação
Branch: feature/US-003_conversation-service
Spec: specs/sprints/SPRINT_1.md#us-003
Regras: RN-CONV01–06, RN-BRAIN01, RN-BRAIN04, RN-TTS01 (RN-BRAIN07 só pelo enum `BrainErrorKind`); ARCHITECTURE §2 (R1, R2, R4), §3, §4, §5, §9, §10; ADR-002, ADR-005 (Aceito), ADR-006 (Proposto, novo)

## 1. Objetivo

Criar no Core o `ConversationService`, que guarda o histórico da sessão e o modo ativo, monta a mensagem padrão de cada turno e a janela dos últimos N turnos, chama o `ITutorBrain` sem depender de estado do provedor e aplica a retentativa de formato com fallback para texto cru (RN-BRAIN04). No console, a linha em branco passa a mostrar um aviso local (RN-CONV05).

## 2. Abordagem

Decisão central no **ADR-006 (Proposto)**: `docs/adr/ADR-006-retentativa-de-formato-e-historico-no-conversation-service.md`. Resumo: o adaptador sinaliza formato inválido com `BrainException.InvalidOutput(rejection, rawText)`. O serviço refaz a chamada **uma** vez, acrescentando a resposta rejeitada (`Assistant`) e a instrução de correção (`User`). Na segunda falha, o turno vira `TutorReply.RawFallback`. O histórico guarda a mensagem formatada do aluno e o `RawText` final do professor. O system prompt vem da porta `ITutorPromptSource`.

O serviço é por sessão e não é thread-safe: uma chamada de `SendAsync` por vez. O console é sequencial, então isso basta.

### 2.1 Contrato do cérebro (`src/EnglishTutor.Core/Brain/`)

```csharp
public enum BrainRole { User, Assistant }
public sealed record BrainMessage(BrainRole Role, string Content);
public sealed record BrainRequest(string SystemPrompt, IReadOnlyList<BrainMessage> Messages, string? SessionHint);

public interface ITutorBrain
{
    string Name { get; }
    Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct);
}

public enum BrainErrorKind { CliNotFound, NotAuthenticated, UsageLimitReached, Timeout, InvalidOutput, Unknown } // RN-BRAIN07

public sealed class BrainException : Exception
{
    public BrainException(BrainErrorKind kind, string message, Exception? innerException = null);
    // kind == InvalidOutput -> ArgumentException("Use BrainException.InvalidOutput(rejection, rawText) for InvalidOutput.")
    public BrainErrorKind Kind { get; }
    public TutorReplyRejection? Rejection { get; }   // não nulo só em InvalidOutput
    public string? RawText { get; }                  // não nulo só em InvalidOutput
    [MemberNotNullWhen(true, nameof(Rejection), nameof(RawText))]
    public bool IsInvalidOutput { get; }             // auto-propriedade armazenada, atribuída no construtor (mesma forma do US-002 §2.1)
    public static BrainException InvalidOutput(TutorReplyRejection rejection, string rawText);
    // Message: "The tutor answer is not in the expected format. {rejection.Message}"
}
```

Em `TutorReply.cs` (US-002), acrescentar a fábrica:

```csharp
public static TutorReply RawFallback(string rawText)
    => new(rawText.Trim(), [], null, null, null, IsRawFallback: true, RawText: rawText);
```

**Análise CA1032 / CA1064:** se `AnalysisLevel=latest-recommended` exigir os construtores padrão de exceção (CA1032), o Executor acrescenta `BrainException()`, `BrainException(string)` e `BrainException(string, Exception)` com `Kind = Unknown`, sem perguntar. Essa forma já fica decidida aqui (AP-009). Nenhuma outra supressão está autorizada.

`BrainErrorKind` nasce com os 6 valores da RN-BRAIN07 e da ARCHITECTURE §6.3, porque é o contrato publicado. Nesta US só `InvalidOutput` (retentativa) e `Timeout` (teste de propagação) são usados.

### 2.2 Conversa (`src/EnglishTutor.Core/Conversation/`)

```csharp
public interface ITutorPromptSource                       // porta; arquivo prompts/tutor.md na US-004
{
    Task<string> GetSystemPromptAsync(CancellationToken ct);
}

public sealed class ConversationOptions
{
    public const string SectionName = "Conversation";
    public const int DefaultMaxTurnsInWindow = 10;
    public int MaxTurnsInWindow { get; set; } = DefaultMaxTurnsInWindow;   // RN-CONV03
}
public sealed class ConversationOptionsValidator : IValidateOptions<ConversationOptions>;
// < 1 -> "Conversation:MaxTurnsInWindow must be at least 1 (found {n}). Expected the number of previous turns sent to the tutor; the default is 10."

public static class TurnMessageBuilder
{
    public static string Build(string studentText, IReadOnlyList<string> lowConfidenceWords, TutorMode mode);
    public static string BuildFormatCorrection(TutorReplyRejection rejection);
}

public sealed record ConversationTurn(string StudentMessage, TutorReply Reply);

public sealed class ConversationWindow(int maxTurns)       // maxTurns < 1 -> ArgumentOutOfRangeException
{
    public IReadOnlyList<ConversationTurn> Turns { get; }
    public void Add(ConversationTurn turn);                // descarta o mais antigo além de maxTurns
    public IReadOnlyList<BrainMessage> ToMessages();       // User(StudentMessage), Assistant(Reply.RawText), ... em ordem
}

public sealed record ConversationTurnResult              // mesma forma do TutorReplyParseResult (US-002 §2.1)
{
    public TutorReply? Reply { get; }
    public string? Warning { get; }
    [MemberNotNullWhen(true, nameof(Reply))]
    [MemberNotNullWhen(false, nameof(Warning))]
    public bool IsReplied { get; }                         // armazenada, atribuída no construtor privado
    public static ConversationTurnResult Replied(TutorReply reply);
    public static ConversationTurnResult Ignored(string warning);
}

public static class ConversationMessages
{
    public const string EmptyInput = "Nothing was sent: type a sentence in English and press Enter.";
}

public sealed class ConversationService(
    ITutorBrain brain, ITutorPromptSource promptSource,
    IOptions<ConversationOptions> options, ILogger<ConversationService> logger)
{
    public TutorMode Mode { get; }                         // começa em TutorMode.Default (RN-CONV01)
    public void ChangeMode(TutorMode mode);                // ArgumentNullException se null; vale no turno seguinte
    public Task<ConversationTurnResult> SendAsync(string text, CancellationToken ct);
}

internal static partial class ConversationLog            // [LoggerMessage], faixa 3000–3999 (Conversa)
{
    // 3000 Warning: "Brain {Provider} reply rejected ({Reason}); retrying once with a format correction"
    // 3001 Warning: "Brain {Provider} reply rejected again ({Reason}); showing the raw answer without feedback"
}
```

**Formato da mensagem do turno (RN-CONV02)**, linhas unidas com `"\n"` (não `Environment.NewLine`, para ser determinístico):

```
Student said (transcribed): "<studentText.Trim()>"
Low-confidence words (possible pronunciation issues): []            (lista vazia)
Low-confidence words (possible pronunciation issues): ["beach", "sheet"]   (com palavras: entre aspas, separadas por ", ")
Mode: <mode.Name>
```

Aspas dentro do texto do aluno não são escapadas (P7). No modo texto o serviço sempre passa `[]` (RN-CONV02). Limite e ordenação das palavras (RN-STT01) são da fase de voz (§8).

**Instrução de correção** (`BuildFormatCorrection`), duas linhas unidas com `"\n"`:

```
Your previous answer could not be used: <rejection.Message>
Answer the student's last message again, following the required response format exactly.
```

**`SendAsync(text, ct)`**, nesta ordem:

1. `ArgumentNullException.ThrowIfNull(text)`. Texto em branco → `Ignored(ConversationMessages.EmptyInput)`. O cérebro não é chamado e nada entra no histórico (RN-CONV05).
2. `studentMessage = TurnMessageBuilder.Build(text, [], Mode)`.
3. `systemPrompt = await promptSource.GetSystemPromptAsync(ct)`.
4. `messages = [.. window.ToMessages(), User(studentMessage)]` (lista nova a cada chamada, nunca reaproveitada). Request: `new BrainRequest(systemPrompt, messages, SessionHint: null)` (P8).
5. `reply = await brain.SendAsync(request, ct)`.
   - `BrainException { IsInvalidOutput: true } first`: log 3000 com `brain.Name` e `first.Rejection.Reason`. Nova chamada com `[.. messages, Assistant(first.RawText), User(BuildFormatCorrection(first.Rejection))]`.
     - `BrainException { IsInvalidOutput: true } second`: log 3001 e `reply = TutorReply.RawFallback(second.RawText)`.
   - Outros `BrainException` e `OperationCanceledException` propagam. Nesse caso o turno não é registrado (P11).
6. `window.Add(new ConversationTurn(studentMessage, reply))` e `return Replied(reply)`.

Os logs não levam o texto do aluno nem o texto cru (padroes-codigo, Logs). Só `Provider` e `Reason`.

**RN-TTS01:** o `ConversationTurnResult` entrega o `TutorReply`. Quem fala é a UI/voz das fases seguintes, só com `Reply`. Nesta US não há TTS (§8). O fallback já sai sem feedback (`FeedbackFormatter.Format` devolve vazio para `IsRawFallback`, US-002).

### 2.3 Comando `/mode` (Core)

- `LocalCommandKind.Mode` e `LocalCommands.Mode = "/mode"`. `LocalCommandParser` reconhece `/mode` sem diferenciar maiúsculas, e `Arguments` traz o resto (`"job-interview"`).
- `TutorMode.TryParse(string? name, [NotNullWhen(true)] out TutorMode? mode)` aceita os 3 nomes de `TutorMode.All` sem diferenciar maiúsculas e com `Trim()`. Nome vazio ou desconhecido → `false`.
- `/mode` **não** entra em `LocalCommands.Available` nesta US, porque o console só lista comandos que já funcionam (US-001, P2). Até a US-004, o `ChatSession` trata `Mode` no ramo `default` e mostra `Unknown command "/mode".`, como hoje (P12).

### 2.4 Console (`src/EnglishTutor.Console/Ui/ChatSession.cs`)

Única mudança: a linha em branco (depois da remoção do BOM da primeira linha) mostra `ConversationMessages.EmptyInput` antes de voltar ao prompt. Assim o comentário "the warning arrives with US-003" é cumprido. O handler continua `TutorNotConnectedInputHandler`. Ligar o `ConversationService` ao console, tratar `/mode` no console e fazer o bind de `Conversation:MaxTurnsInWindow` no `appsettings.json` ficam para a US-004, quando existir um `ITutorBrain` real (P1).

### 2.5 DI

`AddEnglishTutorCore()` passa a registrar também `IValidateOptions<ConversationOptions>` → `ConversationOptionsValidator` (`TryAddEnumerable`, como o de `BrainOptions`). O `ConversationService` não é registrado nesta US, porque ainda não há `ITutorBrain` nem `ITutorPromptSource` para resolver (§8).

O Core passa a referenciar `Microsoft.Extensions.Logging.Abstractions` (já no CPM, 10.0.13). Isso é permitido por R1 e pela ARCHITECTURE §2 (`Microsoft.Extensions.*.Abstractions (Logging, ...)`). `CoreDependencyTests._allowedPackageReferences` ganha esse nome (T1). A regra não muda, só a lista de pacotes diretos usados.

### 2.6 Alterações nos documentos-raiz (propostas; aplicadas pelo orquestrador na aprovação, antes da T1)

Ponto único de aplicação: depois da aprovação e **antes da T1**, o orquestrador aplica tudo o que está nesta seção num commit `docs(ai): update architecture for US-003`, com o ADR-006 em "Aceito" se aprovado. Nenhuma tarefa da §5 altera esses documentos.

`ARCHITECTURE.md`:

- §3, linhas 50–51 (`Conversation/` do Core). Texto novo:
  `Conversation/      ConversationService, ConversationWindow, ConversationTurn, ConversationTurnResult,`
  `                   ConversationMessages, ConversationOptions, ConversationOptionsValidator, ConversationLog,`
  `                   ITutorPromptSource, TurnMessageBuilder, TutorMode, LocalCommandParser, LocalCommand, LocalCommands`
- §3, linha `Brain/` do Core: depois de `BrainMessage`, acrescentar `BrainRole`; depois de `BrainException`, acrescentar `BrainErrorKind`.
- §4, depois de `BrainMessage`, acrescentar:
  ```csharp
  public enum BrainRole { User, Assistant }   // o system prompt vai em BrainRequest.SystemPrompt
  public enum BrainErrorKind { CliNotFound, NotAuthenticated, UsageLimitReached, Timeout, InvalidOutput, Unknown }
  public sealed class BrainException : Exception { /* Kind; Rejection e RawText só via BrainException.InvalidOutput(rejection, rawText) (ADR-006) */ }
  public interface ITutorPromptSource { Task<string> GetSystemPromptAsync(CancellationToken ct); }   // conteúdo de prompts/tutor.md
  ```
- §5, item 1. Texto novo: "Recebe a fala do aluno (`Transcript`, ou texto digitado na fase de texto). Texto em branco não chega ao cérebro: o resultado é um aviso local (RN-CONV05)."
- §5, item 3. Texto novo: "Monta o `BrainRequest`: system prompt (`tutor.md`, via `ITutorPromptSource`; a instrução de formato do provedor é anexada pelo adaptador, ADR-002) + resumo dos erros recorrentes + últimos N turnos (`Conversation:MaxTurnsInWindow`; o professor entra com o `RawText` da resposta) + mensagem atual."
- §5, item 4. Texto novo: "Chama `ITutorBrain.SendAsync`. O cérebro devolve um `TutorReply` já validado pelo parser. Se o parser rejeitar, o adaptador lança `BrainException.InvalidOutput(rejection, rawText)` (ADR-005, ADR-006)."
- §5, item 5. Texto novo: "Em `InvalidOutput`, o serviço faz **uma** nova tentativa: as mesmas mensagens + a resposta rejeitada (`Assistant`) + a instrução de correção com `TutorReplyRejection.Message` (`User`). Se falhar de novo, usa `TutorReply.RawFallback` (`IsRawFallback = true`, sem feedback) (RN-BRAIN04). A tentativa rejeitada e a correção não entram no histórico (ADR-006)."

Repetições em `CLAUDE.md` e skills (Grep por `ConversationService|InvalidOutput|BrainException|FakeTutorBrain|SendAsync|MaxTurnsInWindow` em `CLAUDE.md` e `.claude/**`):

- `CLAUDE.md`: nenhuma linha repete §3/§4/§5. **Sem alteração.**
- `.claude/skills/tdd-dotnet/SKILL.md:22-23` (`Conversation/ConversationServiceTests.cs`, `Fakes/FakeTutorBrain.cs`) e `:43-59` (exemplo de `FakeTutorBrain` com `Returns`/`Throws`) continuam corretos. **Sem alteração.**
- `.claude/skills/padroes-codigo/SKILL.md` (`BrainException` com `BrainErrorKind`, `SendAsync(BrainRequest request, CancellationToken ct)`) continua correto. **Sem alteração.**
- `.claude/skills/bdd-para-testes/SKILL.md:53-60`: o exemplo usa `.ReturnsRaw("no tags here")` e `ConversationServiceBuilder.With(brain).Build()`, nomes que não vão existir. É um exemplo ilustrativo, mas para não gerar falso bloqueante (AP-005), o texto novo das linhas 55–58 fica:
  ```csharp
      var brain = new FakeTutorBrain()
          .ReturnsInvalid("no tags here", TutorReplyRejections.MissingReply())
          .Returns(TutorReplies.Valid());
      var sut = CreateService(brain);
  ```

`specs/` não muda. Os cenários e as RN citadas cobrem o comportamento, e as interpretações estão em §9.

### Alternativas descartadas

- `ITutorBrain` devolvendo `TutorReplyParseResult`: muda a §4 e contraria o ADR-005 (ADR-006).
- Retentativa dentro do adaptador: viola R4 (ADR-006).
- Correção como uma mensagem `User` só, sem o `Assistant` rejeitado: o modelo não vê o próprio erro (ADR-006).
- System prompt em `ConversationOptions`: texto grande em configuração e I/O no Core (ADR-006).
- Ligar o `ConversationService` ao console já nesta US: não há `ITutorBrain` real. Exigiria um cérebro "desconectado" provisório, descartado na US-004 (P1).
- `ConversationServiceBuilder` nos testes: um método privado `CreateService(...)` na classe de teste basta.
- `FakeLogger` (`Microsoft.Extensions.Diagnostics.Testing`): pacote a mais. Copia-se o `RecordingLogger<T>` de ~20 linhas da US-002 para `Core.Tests/Support`.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-006-retentativa-de-formato-e-historico-no-conversation-service.md` | Decisão (Proposto), criada junto com este plano |
| Alterar | `ARCHITECTURE.md` | §3, §4, §5 (texto em §2.6), pelo orquestrador antes da T1 |
| Alterar | `.claude/skills/bdd-para-testes/SKILL.md` | Linhas 55–58 do exemplo (texto em §2.6), pelo orquestrador antes da T1 |
| Alterar | `src/EnglishTutor.Core/EnglishTutor.Core.csproj` | `PackageReference` a `Microsoft.Extensions.Logging.Abstractions` |
| Alterar | `tests/EnglishTutor.Core.Tests/Architecture/CoreDependencyTests.cs` | `Microsoft.Extensions.Logging.Abstractions` em `_allowedPackageReferences` |
| Alterar | `src/EnglishTutor.Core/Conversation/TutorMode.cs` | `TryParse` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommand.cs` | `LocalCommandKind.Mode` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs` | Constante `Mode` (fora de `Available`) |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommandParser.cs` | Reconhecer `/mode` |
| Criar | `src/EnglishTutor.Core/Brain/BrainRole.cs` | Papel da mensagem |
| Criar | `src/EnglishTutor.Core/Brain/BrainMessage.cs` | Mensagem do histórico |
| Criar | `src/EnglishTutor.Core/Brain/BrainRequest.cs` | Requisição completa ao cérebro |
| Criar | `src/EnglishTutor.Core/Brain/ITutorBrain.cs` | Porta do cérebro |
| Criar | `src/EnglishTutor.Core/Brain/BrainErrorKind.cs` | Classificação de falhas (RN-BRAIN07) |
| Criar | `src/EnglishTutor.Core/Brain/BrainException.cs` | Exceção de domínio + fábrica `InvalidOutput` |
| Alterar | `src/EnglishTutor.Core/Brain/TutorReply.cs` | Fábrica `RawFallback` |
| Criar | `src/EnglishTutor.Core/Conversation/ITutorPromptSource.cs` | Porta do system prompt |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationOptions.cs` | `MaxTurnsInWindow` (padrão 10) |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationOptionsValidator.cs` | ≥ 1, com mensagem que diz o que se espera |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | Registrar o validador de `ConversationOptions` |
| Criar | `src/EnglishTutor.Core/Conversation/TurnMessageBuilder.cs` | Mensagem RN-CONV02 e instrução de correção |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationTurn.cs` | Turno (aluno + professor) |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationWindow.cs` | Últimos N turnos → mensagens |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationTurnResult.cs` | Resposta ou aviso local |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationMessages.cs` | Texto do aviso de entrada vazia |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationService.cs` | Orquestração do turno, modo, retentativa e fallback |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationLog.cs` | `[LoggerMessage]` 3000 e 3001 |
| Alterar | `src/EnglishTutor.Console/Ui/ChatSession.cs` | Aviso local na linha em branco |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/FakeTutorBrain.cs` | Fila de respostas (`Returns`, `ReturnsInvalid`, `Throws`) + `Requests` |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/FakeTutorPromptSource.cs` | Devolve um system prompt fixo |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/TutorReplies.cs` | `TutorReply` válidos de teste (com `RawText` diferente do `Reply`, AP-017) |
| Criar | `tests/EnglishTutor.Core.Tests/Support/RecordingLogger.cs` | Logger fake (cópia do da Infrastructure.Tests) |
| Alterar | `tests/EnglishTutor.Core.Tests/Conversation/TutorModeTests.cs` | `TryParse` |
| Alterar | `tests/EnglishTutor.Core.Tests/Conversation/LocalCommandParserTests.cs` | `/mode`; caso `"/mode interview"` do desconhecido trocado por `"/foo bar"` |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/BrainExceptionTests.cs` | Fábrica e guarda do construtor |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationOptionsTests.cs` | Padrão 10 |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationOptionsValidatorTests.cs` | Limite ≥ 1 nos dois lados |
| Alterar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | Validador de `ConversationOptions` registrado |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/TurnMessageBuilderTests.cs` | Formato RN-CONV02 e correção |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationWindowTests.cs` | Janela |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationServiceTests.cs` | Cenários da US |
| Alterar | `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` | Aviso na linha em branco |

## 4. Mapa cenário BDD → testes

Todos os testes do Core usam `FakeTutorBrain` (sem estado: só enxerga o `BrainRequest`), `FakeTutorPromptSource("SYSTEM PROMPT")` e `RecordingLogger<ConversationService>`. A mensagem esperada do cenário 1 é exatamente `"Student said (transcribed): \"I have worked with C# since five years\"\nLow-confidence words (possible pronunciation issues): []\nMode: job-interview"`.

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| mensagem do turno no formato padrão | Core.Tests | `ConversationServiceTests` | `SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` (`ChangeMode(JobInterview)`; `Requests[0].Messages[^1]` = `User` + texto exato; `SystemPrompt == "SYSTEM PROMPT"`; `SessionHint` nulo, P8/P9) | Unidade |
| mensagem do turno no formato padrão | Core.Tests | `TurnMessageBuilderTests` | `Build_InJobInterviewMode_ProducesStandardThreeLineMessage` (texto exato, separador `\n`) | Unidade |
| mensagem do turno (apoio, RN-CONV02) | Core.Tests | `TurnMessageBuilderTests` | `Build_WithLowConfidenceWords_ListsThemQuotedAndCommaSeparated` (`["beach", "sheet"]`) | Unidade |
| mensagem do turno (apoio, P7) | Core.Tests | `TurnMessageBuilderTests` | `Build_WithStudentText_TrimsSpacesAndKeepsQuotes` (`[Theory]`: `"  I went home  "` → `"I went home"`; `He said "hi"` mantido sem escape) | Unidade |
| janela de turnos | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenWindowIsFull_SendsSystemPromptLastTurnsAndNewMessage` (`MaxTurnsInWindow = 2`, 3 turnos válidos, 4º envio: `SystemPrompt == "SYSTEM PROMPT"`; `Messages` com 5 itens = turno 2 (User, Assistant), turno 3 (User, Assistant), nova mensagem; nenhuma mensagem contém o texto do turno 1) | Unidade |
| janela de turnos (apoio: limite nos dois lados, AP-016) | Core.Tests | `ConversationWindowTests` | `Add_WhenMoreTurnsThanMax_KeepsMostRecentInOrder` (`[Theory]`: max 2 com 2 turnos → os 2; max 2 com 3 → turnos 2 e 3; max 1 com 3 → turno 3) | Unidade |
| janela de turnos (apoio) | Core.Tests | `ConversationWindowTests` | `ToMessages_WithTurns_AlternatesStudentAndTutorMessages` (`User` = `StudentMessage`, `Assistant` = `Reply.RawText`, diferente de `Reply.Reply`) | Unidade |
| janela de turnos (apoio) | Core.Tests | `ConversationWindowTests` | `Constructor_WithMaxTurnsBelowOne_Throws` (`0` → `ArgumentOutOfRangeException`) | Unidade |
| janela de turnos (apoio, RN-CONV03, P10) | Core.Tests | `ConversationOptionsTests` | `Constructor_Always_DefaultsMaxTurnsInWindowToTen` | Unidade |
| janela de turnos (apoio, P10) | Core.Tests | `ConversationOptionsValidatorTests` | `Validate_WhenMaxTurnsInWindowIsBelowOne_FailsWithExpectation` (`[Theory]` 0 e -1; mensagem contém `Conversation:MaxTurnsInWindow`, `(found 0)`/`(found -1)` e `Expected`) | Unidade |
| janela de turnos (apoio, P10) | Core.Tests | `ConversationOptionsValidatorTests` | `Validate_WhenMaxTurnsInWindowIsAtLeastOne_Succeeds` (`[Theory]` 1 e 10) | Unidade |
| janela de turnos (apoio: DI) | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_Always_RegistersConversationOptionsValidator` (com `MaxTurnsInWindow = 0`, ler `IOptions<ConversationOptions>.Value` lança `OptionsValidationException` com a mensagem do validador) | Unidade |
| histórico independe do provedor | Core.Tests | `ConversationServiceTests` | `SendAsync_OnSecondTurn_IncludesFirstTurnStudentAndTutorMessages` (`Requests[1].Messages` = `User`(mensagem formatada do 1º turno), `Assistant`(`RawText` da 1ª resposta), `User`(2ª mensagem), P4) | Unidade |
| nova tentativa em resposta inválida | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection` (`Requests` com 2 itens; `Requests[1].Messages` = `[User(turno), Assistant(raw1), User(correção)]`; a última contém `format` e a `Message` da rejeição; `Requests[0].Messages` continua com 1 item, sem lista reaproveitada; P2, P3) | Unidade |
| nova tentativa em resposta inválida | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenFirstReplyIsInvalid_ReturnsSecondReplyWithFeedback` (`IsReplied`; `Reply` igual à 2ª resposta; `IsRawFallback == false`; `FeedbackFormatter.Format(reply)` não vazio) | Unidade |
| nova tentativa em resposta inválida (apoio: correção) | Core.Tests | `TurnMessageBuilderTests` | `BuildFormatCorrection_WithRejection_IncludesRejectionMessageAndFormatInstruction` (texto exato das 2 linhas da §2.2 com `TutorReplyRejections.MissingReply()`) | Unidade |
| nova tentativa em resposta inválida (apoio: log) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason` (1 entrada `Warning`, EventId 3000, mensagem contém `Fake` e `missing reply` e não contém o texto cru nem o texto do aluno) | Unidade |
| nova tentativa em resposta inválida (apoio, P6) | Core.Tests | `ConversationServiceTests` | `SendAsync_AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow` (turno 1 com retentativa bem-sucedida; no turno 2 `Messages` = `[User(t1), Assistant(RawText da 2ª resposta), User(t2)]`, 3 itens) | Unidade |
| resposta crua após duas falhas | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback` (raw2 = `"  Plain answer without tags.\n"`; `Reply.Reply == "Plain answer without tags."`; `Corrections` vazio e `FeedbackFormatter.Format` vazio; `IsRawFallback == true`; `RawText == raw2`, P13) | Unidade |
| resposta crua após duas falhas (apoio: log) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenReplyIsInvalidTwice_LogsRawFallbackWarning` (entradas 3000 e 3001, nessa ordem, `Warning`; nenhuma contém raw1/raw2) | Unidade |
| resposta crua após duas falhas (apoio, P5) | Core.Tests | `ConversationServiceTests` | `SendAsync_AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage` (no turno seguinte, `Messages[1]` = `Assistant(raw2)` sem `Trim`) | Unidade |
| entrada vazia | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenInputIsBlank_DoesNotCallBrainAndReturnsWarning` (`[Theory]` `""`, `"   "`, `"\t"`; `Requests` vazio; `IsReplied == false`; `Warning == ConversationMessages.EmptyInput`) | Unidade |
| entrada vazia (console, P1) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning` (`[Theory]` `""`, `"   "`, `"\t"`; saída contém `ConversationMessages.EmptyInput` uma vez; handler sem entradas) | Componente UI |
| troca de modo | Core.Tests | `ConversationServiceTests` | `SendAsync_AfterModeCommand_SendsNewModeInTurnMessage` (`Mode == FreeConversation` no início; `LocalCommandParser.TryParse("/mode job-interview")` → `TutorMode.TryParse(command.Arguments)` → `ChangeMode`; `Messages[^1]` termina com `Mode: job-interview`) | Unidade |
| troca de modo (apoio, P12) | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments` (`[Theory]`: `"/mode job-interview"` → `"job-interview"`; `"/MODE  free-conversation "` → `"free-conversation"`; `"/mode"` → `""`) | Unidade |
| troca de modo (apoio) | Core.Tests | `TutorModeTests` | `TryParse_WithKnownName_ReturnsMode` (`[Theory]`: `job-interview`, `JOB-INTERVIEW`, `"  listening-practice "`) | Unidade |
| troca de modo (apoio) | Core.Tests | `TutorModeTests` | `TryParse_WithUnknownOrBlankName_ReturnsFalse` (`[Theory]`: `interview`, `""`, `"   "`) | Unidade |
| (apoio: P2, contrato da rejeição) | Core.Tests | `BrainExceptionTests` | `InvalidOutput_Always_CarriesKindRejectionAndRawText` (`Kind == InvalidOutput`, `IsInvalidOutput`, mesma rejeição, mesmo raw, `Message` contém `rejection.Message`) | Unidade |
| (apoio: P2) | Core.Tests | `BrainExceptionTests` | `Constructor_WithInvalidOutputKind_ThrowsArgumentException` | Unidade |
| (apoio, P11: só InvalidOutput é refeito) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry` (`BrainException(Timeout, ...)` propaga; `Requests` com 1 item) | Unidade |
| (apoio, P11: cancelamento) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenCancelled_PropagatesAndDoesNotRecordTurn` (cérebro lança `OperationCanceledException`, que propaga; o envio seguinte tem `Messages` com 1 item) | Unidade |

Teste existente alterado: em `LocalCommandParserTests.TryParse_WhenSlashCommandIsUnknown_ReturnsUnknownCommand`, o caso `"/mode interview"` vira `"/foo bar"` (`"/foo"`, `"bar"`), porque `/mode` deixa de ser desconhecido. A contagem de casos não muda. `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` (`"/mode interview"` → `Unknown command "/mode".`) **não muda** e continua verde como regressão de P12. A US-004 o atualiza ao tratar `/mode` no console.

**Contagens previstas: 31 métodos novos, 46 casos novos.**

- Core.Tests: 30 métodos / 43 casos.
  - `ConversationServiceTests`: 14 / 16 (13 `[Fact]` + 1 `[Theory]` com 3).
  - `TurnMessageBuilderTests`: 4 / 5.
  - `ConversationWindowTests`: 3 / 5.
  - `TutorModeTests`: +2 / +6.
  - `LocalCommandParserTests`: +1 / +3.
  - `BrainExceptionTests`: 2 / 2.
  - `ConversationOptionsTests`: 1 / 1.
  - `ConversationOptionsValidatorTests`: 2 / 4.
  - `CoreServiceCollectionExtensionsTests`: +1 / +1.
- Console.Tests: 1 / 3.

Suíte inteira após a US: **143 casos** (Core.Tests 51 + 43 = 94; Console.Tests 22 + 3 = 25; Infrastructure.Tests 24). Se o Executor mudar uma contagem, atualiza §4 e D2/D5/D8 com o motivo.

## 5. Tarefas (ordem de execução TDD)

Pré-condição (ponto único): na aprovação e **antes da T1**, o orquestrador aplica a §2.6 (`ARCHITECTURE.md` §3/§4/§5 e `bdd-para-testes` linhas 55–58) num commit `docs(ai)`, com o ADR-006 em "Aceito" se aprovado.

Em toda tarefa, os esqueletos vêm antes dos testes (AP-013). Depois de `x.Should().BeTrue()` em propriedades com `[MemberNotNullWhen]`, use `!` (AP-020). Escolha de forma não prevista aqui → registro na §12 em commit `docs(ai)` **antes** do `feat` (AP-009/AP-014).

- [ ] T1 — Pacote: `PackageReference` a `Microsoft.Extensions.Logging.Abstractions` no Core e o nome em `CoreDependencyTests._allowedPackageReferences`, no mesmo commit (sem o segundo, `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` fica vermelho). `dotnet build` com 0 warnings e suíte verde (97 casos). (commit: `chore(core): allow logging abstractions in core`)
- [ ] T2 — Red: esqueletos `LocalCommandKind.Mode`, `LocalCommands.Mode`, `TutorMode.TryParse` (lança `NotImplementedException`). Testes `TryParse_WithKnownName_ReturnsMode`, `TryParse_WithUnknownOrBlankName_ReturnsFalse` e `TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments`, que falha por asserção: `Unknown` ≠ `Mode`. Troca de `"/mode interview"` por `"/foo bar"` no teste de desconhecido, que continua verde. Green: `KindOf` reconhece `/mode`; `TryParse` sobre `TutorMode.All`. O `ChatSession` não muda, e `Mode` cai no `default` (P12). (commits: `test(core): mode command and mode names` + `feat(core): parse mode command and mode names`)
- [ ] T3 — Red: esqueletos `BrainRole`, `BrainMessage`, `BrainRequest`, `ITutorBrain`, `BrainErrorKind`, `BrainException` (construtor sem a guarda; `InvalidOutput` lança `NotImplementedException`) + `BrainExceptionTests`. Os 2 falham: um com `NotImplementedException`, o outro porque nada é lançado. Confirmar no build do esqueleto que não há CS8775 em `IsInvalidOutput`. Se aparecer CA1032, aplicar a forma da §2.1. Green: guarda e fábrica. (commits: `test(core): brain exception for invalid output` + `feat(core): add brain contract and brain exception`)
- [ ] T4 — Red: esqueletos `ConversationOptions` (padrão já 10, porque é dado e não regra), `ConversationOptionsValidator.Validate` (lança `NotImplementedException`) + `ConversationOptionsTests`, `ConversationOptionsValidatorTests`, `AddEnglishTutorCore_Always_RegistersConversationOptionsValidator`. `Constructor_Always_DefaultsMaxTurnsInWindowToTen` nasce verde com o esqueleto: é teste de caracterização de dado. Para provar que discrimina, mostrar o Red por mutação temporária não commitada (padrão `= 0`), registrando a falha na tarefa (AP-017). Os demais falham com `NotImplementedException` ou porque o validador não está registrado. Green: validador e registro no DI. (commits: `test(core): conversation options validation` + `feat(core): validate conversation window size`)
- [ ] T5 — Red: esqueleto `TurnMessageBuilder` (`Build` e `BuildFormatCorrection` lançam `NotImplementedException`) + `TurnMessageBuilderTests` (4 métodos / 5 casos). Green: formato da §2.2. (commits: `test(core): turn message format` + `feat(core): build standard turn message`)
- [ ] T6 — Red: esqueletos `ConversationTurn`, `ConversationWindow` (`Add` e `ToMessages` lançam `NotImplementedException`; construtor sem guarda) + `ConversationWindowTests` (3 / 5). Green: guarda, descarte do mais antigo e mensagens. (commits: `test(core): conversation window` + `feat(core): keep last turns in conversation window`)
- [ ] T7 — Red: esqueletos `ITutorPromptSource`, `ConversationTurnResult` (fábricas), `ConversationMessages`, `ConversationLog` (3000 e 3001 já declarados, sem uso), `ConversationService` (`Mode`/`ChangeMode` prontos; `SendAsync` lança `NotImplementedException`) e os fakes (`FakeTutorBrain`, `FakeTutorPromptSource`, `TutorReplies`, `RecordingLogger`). Testes, todos com respostas válidas ou com `BrainException(Timeout)`/`OperationCanceledException`, nunca `InvalidOutput`:
  - `InJobInterviewMode_SendsStandardTurnMessageLast`
  - `WhenWindowIsFull_...`
  - `OnSecondTurn_...`
  - `WhenInputIsBlank_...` (3 casos)
  - `AfterModeCommand_...`
  - `WhenBrainFailsWithOtherKind_PropagatesWithoutRetry`
  - `WhenCancelled_PropagatesAndDoesNotRecordTurn`

  São 7 métodos / 9 casos, e todos falham com a `NotImplementedException` de `SendAsync`. Green: passos 1–4 e 6 da §2.2, com o `await brain.SendAsync` direto. **Estado do ramo `InvalidOutput` ao fim da T7:** não há `catch`, então `BrainException.InvalidOutput` propaga ao chamador como qualquer outra falha. Por isso `WhenBrainFailsWithOtherKind` já passa aqui e segue como regressão na T8/T9. (commits: `test(core): conversation service turn and window` + `feat(core): send turns with application-owned history`)
- [ ] T8 — Red: `WhenFirstReplyIsInvalid_RetriesWithFormatCorrection`, `WhenFirstReplyIsInvalid_ReturnsSecondReplyWithFeedback`, `WhenFirstReplyIsInvalid_LogsRetryWarningWithReason`, `AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow` (4). Todos usam um `InvalidOutput` seguido de resposta válida e falham porque a `BrainException` propaga (estado da T7). O Executor confere esse motivo antes do Green. Green: `catch` de `IsInvalidOutput`, log 3000 e nova chamada com as mensagens extras. **Estado ao fim da T8:** se a segunda chamada também lançar `InvalidOutput`, a exceção propaga, sem `catch` interno. (commits: `test(core): retry once on invalid tutor reply` + `feat(core): retry with format correction on invalid reply`)
- [ ] T9 — Red: esqueleto `TutorReply.RawFallback` (lança `NotImplementedException`) + `WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback`, `WhenReplyIsInvalidTwice_LogsRawFallbackWarning`, `AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage` (3). Todos falham porque a segunda `BrainException` propaga (estado da T8). Green: `catch` interno, log 3001 e `RawFallback`. Refactor: extrair a montagem do `BrainRequest` num método privado. (commits: `test(core): raw fallback after second invalid reply` + `feat(core): fall back to raw reply after second failure` + `refactor(core): ...` se houver)
- [ ] T10 — Red: `ChatSessionTests.RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning` (3 casos), que falha porque hoje nada é escrito. Green: `ChatSession` escreve `ConversationMessages.EmptyInput` na linha em branco e atualiza o comentário da RN-CONV05. `RunAsync_WhenLineIsBlank_IgnoresItAndForwardsNothing` continua verde (2 prompts). (commits: `test(console): warn on blank input` + `feat(console): show local warning for blank input`)
- [ ] T11 — DoD: rodar D1–D11, colar em §11, marcar §5 com os hashes. (commit: `docs(ai): add DoD evidence for US-003`)

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Logging.Abstractions (10.0.13, já no CPM) | Core | `ILogger<T>` e `[LoggerMessage]` no `ConversationService` (permitido por R1) |

Nenhum pacote novo em Infrastructure, Console ou nos projetos de teste. `Microsoft.Extensions.Options` (já no Core) cobre `IOptions<T>`/`IValidateOptions<T>`. Os testes usam `Options.Create(...)`.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| CS8775 em `IsInvalidOutput`/`IsReplied` com `[MemberNotNullWhen]` | Auto-propriedade armazenada, atribuída no construtor (forma já validada no US-002). O build do esqueleto nas T3/T7 confirma. Se mesmo assim falhar, parar e registrar na §12 |
| CA1032 (construtores padrão de exceção) quebra o build | Forma decidida na §2.1 (3 construtores com `Kind = Unknown`) |
| Lista de mensagens reaproveitada entre a 1ª e a 2ª chamada faz o `FakeTutorBrain` registrar um request mutado | Lista nova a cada request (§2.2, passo 4); o teste de retentativa confere que `Requests[0].Messages` continua com 1 item |
| `RawText` vazio gera `Assistant` vazio na retentativa, e algumas APIs recusam | Fica com o adaptador (US-004/US-006), registrado no ADR-006 |
| `/mode` reconhecido no Core mas mostrado como "Unknown command" no console até a US-004 | Fica fora de `/help` (US-001, P2), e o teste de console existente fixa o comportamento atual (P12) |
| Duas checagens de linha em branco (`ChatSession` e `ConversationService`) | As duas usam o mesmo texto do Core (`ConversationMessages.EmptyInput`). Na US-004 o console delega ao serviço e decide se mantém a checagem |
| Asserção satisfeita por texto externo (AP-015) | Todas as mensagens conferidas (aviso, correção, logs, validador) são do projeto |
| Cobertura do namespace `Conversation` < 90% (D6) | Cada ramo do serviço tem teste (§4). `ConversationLog` é código gerado e exercitado pelos testes de log |

## 8. Fora de escopo

- Implementações de `ITutorBrain` e de `ITutorPromptSource` (leitura de `prompts/tutor.md`): US-004/US-006.
- Ligar o `ConversationService` ao console: handler que delega ao serviço, exibição do reply e do feedback, `/mode` no console e em `/help`, bind de `Conversation:MaxTurnsInWindow` com `ValidateOnStart` e chave no `appsettings.json`, registro do serviço no DI. Tudo na US-004 (P1).
- RN-CONV04 (resumo dos 5 erros mais frequentes) e persistência de turnos (RN-PERS*): dependem do `IConversationStore` (fases futuras).
- Entrada por voz (`Transcript`), palavras de baixa confiança reais, limite de 5 e ordenação (RN-STT01) e TTS (RN-TTS01, além de o `Reply` estar separado do feedback no resultado).
- Mensagens ao aluno por `BrainErrorKind` (US-005) e fallback entre provedores (US-007).
- Medição de latência do cérebro em log: entra com os adaptadores (US-004).
- `SessionHint` preenchido: US-004 decide (P8).

## 9. Perguntas em aberto

- **P1 — Quanto do console entra nesta US?** **Padrão proposto:** só o aviso local da linha em branco, usando o texto do Core. A ligação ao `ConversationService`, o `/mode` no console, o bind de `Conversation:MaxTurnsInWindow` e o `appsettings.json` ficam para a US-004, quando existir um cérebro real. Alternativa: tudo agora, com um `ITutorBrain` provisório "não conectado". Teste: `ChatSessionTests.RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning`.
- **P2 — Como a rejeição do parser chega ao Core?** **Padrão proposto:** `BrainException.InvalidOutput(rejection, rawText)` (ADR-006). Alternativa: mudar `ITutorBrain` para devolver `TutorReplyParseResult`. Testes: `BrainExceptionTests.InvalidOutput_Always_CarriesKindRejectionAndRawText` e `ConversationServiceTests.SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection`.
- **P3 — Forma da segunda chamada.** **Padrão proposto:** mesmas mensagens + `Assistant(raw1)` + `User(instrução de correção com a Message da rejeição)`, com o mesmo system prompt. Alternativa: só um `User` com a correção. Teste: `ConversationServiceTests.SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection`.
- **P4 — Conteúdo do professor no histórico.** **Padrão proposto:** `TutorReply.RawText` (o que o modelo de fato respondeu). Alternativa: só o `Reply`. Teste: `ConversationServiceTests.SendAsync_OnSecondTurn_IncludesFirstTurnStudentAndTutorMessages`.
- **P5 — Turno com fallback entra na janela?** **Padrão proposto:** sim, com o texto cru (`RawText`, sem `Trim`). Alternativa: excluir o turno da janela. Teste: `ConversationServiceTests.SendAsync_AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage`.
- **P6 — Tentativa rejeitada e instrução de correção ficam no histórico?** **Padrão proposto:** não. Só o turno final (mensagem do aluno + resposta aceita). Teste: `ConversationServiceTests.SendAsync_AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow`.
- **P7 — Normalização do texto do aluno na mensagem do turno.** **Padrão proposto:** `Trim()` e nenhum escape de aspas internas. Teste: `TurnMessageBuilderTests.Build_WithStudentText_TrimsSpacesAndKeepsQuotes`.
- **P8 — `SessionHint` nesta US.** **Padrão proposto:** sempre `null`. A US-004 decide se o serviço gera um id de sessão ou se o adaptador guarda o `session_id`. Teste: `ConversationServiceTests.SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` (confere `SessionHint` nulo).
- **P9 — De onde vem o system prompt e o que ele contém?** **Padrão proposto:** porta `ITutorPromptSource`, lida a cada turno, só com o conteúdo de `tutor.md`. A instrução de formato é anexada pelo adaptador (ADR-002). Testes: `ConversationServiceTests.SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` e `SendAsync_WhenWindowIsFull_SendsSystemPromptLastTurnsAndNewMessage` (`SystemPrompt == "SYSTEM PROMPT"`).
- **P10 — Limites de `Conversation:MaxTurnsInWindow`.** **Padrão proposto:** mínimo 1, sem máximo, padrão 10 (RN-CONV03). O valor 0 é rejeitado porque deixaria o professor sem histórico (contra a RN-BRAIN01). Testes: `ConversationOptionsValidatorTests.Validate_WhenMaxTurnsInWindowIsBelowOne_FailsWithExpectation`, `ConversationOptionsValidatorTests.Validate_WhenMaxTurnsInWindowIsAtLeastOne_Succeeds` e `ConversationOptionsTests.Constructor_Always_DefaultsMaxTurnsInWindowToTen`.
- **P11 — Falhas que não são de formato.** **Padrão proposto:** outros `BrainErrorKind` e `OperationCanceledException` propagam sem retentativa e sem registrar o turno. As mensagens ao aluno ficam para a US-005 e o fallback de provedor para a US-007. Testes: `ConversationServiceTests.SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry` e `ConversationServiceTests.SendAsync_WhenCancelled_PropagatesAndDoesNotRecordTurn`.
- **P12 — `/mode` no console antes da US-004.** **Padrão proposto:** o Core já reconhece `/mode`, mas o comando não aparece em `/help`, e o console continua mostrando `Unknown command "/mode".` até a US-004. Alternativa: deixar todo o `/mode` (parser incluído) para a US-004 e testar o cenário só com `ChangeMode`. Testes: `LocalCommandParserTests.TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments` e o existente `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` (regressão).
- **P13 — Texto exibido no fallback.** **Padrão proposto:** `Reply` = texto cru da segunda resposta com `Trim()`, e `RawText` = texto original. Teste: `ConversationServiceTests.SendAsync_WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback`.
- **P14 — RN-CONV04 (resumo de erros recorrentes) nesta US.** **Padrão proposto:** fora (§8). Sem persistência não há erros recorrentes, então nada é acrescentado ao system prompt. Sem efeito observável.

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell (exceto D11, em Git Bash, e D9, no Windows PowerShell 5.1).

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros (sem CS8775 nem CA1032) |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: 143 casos (Core.Tests 94, Console.Tests 25, Infrastructure.Tests 24), em < 30 s, sem rede nem GPU |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `Measure-Command { dotnet test tests/EnglishTutor.Core.Tests } ; dotnet test tests/EnglishTutor.Core.Tests` | 94 casos verdes; tempo de execução dos testes (linha "Duração"/"Duration" do resumo) < 10 s; só fakes |
| D6 | `dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage/conversation -reporttypes:TextSummary -assemblyfilters:"+EnglishTutor.Core" -classfilters:"+EnglishTutor.Core.Conversation.*"; Get-Content coverage/conversation/Summary.txt` (após D3) | linha `EnglishTutor.Core` (restrita ao namespace `Conversation`) ≥ 90% de linhas |
| D7 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"`; `dotnet list src/EnglishTutor.Core package` | 3 testes verdes; pacotes: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options` |
| D8 | `dotnet test --filter "FullyQualifiedName~ConversationServiceTests"` | 16 casos verdes (14 métodos), cobrindo os 7 cenários da US |
| D9 | No **Windows PowerShell 5.1** (`powershell.exe`): `"   ", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | cabeçalho com provedor e modo; a linha `Nothing was sent: type a sentence in English and press Enter.` aparece uma vez (a primeira linha chega com BOM, AP-006); `exit=0` |
| D10 | `git status --short` após D2–D9 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/` ou `obj/` |
| D11 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |

D11 (fora da tabela por causa do `|`; forma única do `checklist-revisao` §4, AP-019):

```bash
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
```

Também entram, da DoD comum: todo cenário BDD com teste escrito antes do código; `review.md` sem bloqueantes; `docs/us/US-003.md` criado; ADR-006 aceito e `ARCHITECTURE.md` §3/§4/§5 + `bdd-para-testes` atualizados antes da T1 (§2.6); merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._
