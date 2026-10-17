# Plano — US-003 ConversationService dono do histórico

Status: Aprovado em 2026-10-17
Branch: feature/US-003_conversation-service
Spec: specs/sprints/SPRINT_1.md#us-003
Regras: RN-CONV01–06, RN-BRAIN01, RN-BRAIN04, RN-TTS01 (RN-BRAIN07 só pelo enum `BrainErrorKind`); ARCHITECTURE §2 (R1, R2, R4), §3, §4, §5, §9, §10; ADR-002, ADR-005 (Aceito), ADR-006 (Aceito, novo)

## 1. Objetivo

Criar no Core o `ConversationService`, que guarda o histórico da sessão e o modo ativo, monta a mensagem padrão de cada turno e a janela dos últimos N turnos, chama o `ITutorBrain` sem depender de estado do provedor e aplica a retentativa de formato com fallback para texto cru (RN-BRAIN04). No console, a linha em branco passa a mostrar um aviso local (RN-CONV05).

## 2. Abordagem

Decisão central no **ADR-006 (Aceito)**: `docs/adr/ADR-006-retentativa-de-formato-e-historico-no-conversation-service.md`. Resumo: o adaptador sinaliza formato inválido com `BrainException.InvalidOutput(rejection, rawText)`. O serviço refaz a chamada **uma** vez, acrescentando a resposta rejeitada (`Assistant`) e a instrução de correção (`User`). Na segunda falha, o turno vira `TutorReply.RawFallback`. O histórico guarda a mensagem formatada do aluno e o `RawText` final do professor. O system prompt vem da porta `ITutorPromptSource`.

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
3. `systemPrompt = await promptSource.GetSystemPromptAsync(ct)` (uma vez por turno; a retentativa reaproveita o mesmo texto).
4. `messages = [.. window.ToMessages(), User(studentMessage)]` (lista nova a cada chamada, nunca reaproveitada). Request: `new BrainRequest(systemPrompt, messages, SessionHint: null)` (P8).
5. `reply = await brain.SendAsync(request, ct)`.
   - `BrainException { IsInvalidOutput: true } first`: log 3000 com `brain.Name` e `first.Rejection.Reason`. Nova chamada, com o **mesmo `ct`**, com `[.. messages, Assistant(first.RawText), User(BuildFormatCorrection(first.Rejection))]`.
     - `BrainException { IsInvalidOutput: true } second`: log 3001 e `reply = TutorReply.RawFallback(second.RawText)`.
     - Outros `BrainException` e `OperationCanceledException` na segunda chamada propagam, e o turno não é registrado (P11).
   - Outros `BrainException` e `OperationCanceledException` na primeira chamada propagam. Nesse caso o turno não é registrado (P11).
6. `window.Add(new ConversationTurn(studentMessage, reply))` e `return Replied(reply)`.

O `ct` recebido é repassado sem troca ao `promptSource` e às duas chamadas do cérebro (nunca `CancellationToken.None`).

Os logs não levam o texto do aluno nem o texto cru (padroes-codigo, Logs). Só `Provider` e `Reason`.

**RN-TTS01:** o `ConversationTurnResult` entrega o `TutorReply`. Quem fala é a UI/voz das fases seguintes, só com `Reply`. Nesta US não há TTS (§8). O fallback já sai sem feedback (`FeedbackFormatter.Format` devolve vazio para `IsRawFallback`, US-002).

### 2.3 Comando `/mode` (Core)

- `LocalCommandKind.Mode` e `LocalCommands.Mode = "/mode"`. `LocalCommandParser` reconhece `/mode` sem diferenciar maiúsculas, e `Arguments` traz o resto (`"job-interview"`).
- `TutorMode.TryParse(string? name, [NotNullWhen(true)] out TutorMode? mode)` aceita os 3 nomes de `TutorMode.All` sem diferenciar maiúsculas e com `Trim()`. Nome vazio ou desconhecido → `false`.
- `/mode` **não** entra em `LocalCommands.Available` nesta US, porque o console só lista comandos que já funcionam (US-001, P2). Até a US-004, o `ChatSession` trata `Mode` no ramo `default` e mostra `Unknown command "/mode".`, como hoje (P12). A entrega do `/mode` no console passa a constar na spec da US-004 (§2.6).

### 2.4 Console (`src/EnglishTutor.Console/Ui/ChatSession.cs`)

Única mudança: a linha em branco (depois da remoção do BOM da primeira linha) mostra `ConversationMessages.EmptyInput` antes de voltar ao prompt. Assim o comentário "the warning arrives with US-003" é cumprido. O handler continua `TutorNotConnectedInputHandler`. Ligar o `ConversationService` ao console, tratar `/mode` no console e fazer o bind de `Conversation:MaxTurnsInWindow` no `appsettings.json` ficam para a US-004, quando existir um `ITutorBrain` real (P1). Esses itens passam a constar na spec da US-004 antes da T1 (§2.6, B1).

### 2.5 DI

`AddEnglishTutorCore()` passa a registrar também `IValidateOptions<ConversationOptions>` → `ConversationOptionsValidator` (`TryAddEnumerable`, como o de `BrainOptions`). O `ConversationService` não é registrado nesta US, porque ainda não há `ITutorBrain` nem `ITutorPromptSource` para resolver (§8). O registro passa a constar na spec da US-004 (§2.6).

O Core passa a referenciar `Microsoft.Extensions.Logging.Abstractions` (já no CPM, 10.0.13). Isso é permitido por R1 e pela ARCHITECTURE §2 (`Microsoft.Extensions.*.Abstractions (Logging, ...)`). `CoreDependencyTests._allowedPackageReferences` ganha esse nome (T1). A regra não muda, só a lista de pacotes diretos usados. Na mesma T1, a `PackageVersion` desse pacote sai do `ItemGroup Label="Infrastructure"` e vai para o `ItemGroup Label="Core"` do `Directory.Packages.props` (mesma versão), para a fronteira do R1 ficar legível (S7).

### 2.6 Alterações nos documentos-raiz e na spec (propostas; aplicadas pelo orquestrador na aprovação, antes da T1)

Ponto único de aplicação: depois da aprovação e **antes da T1**, o orquestrador aplica tudo o que está nesta seção num commit `docs(ai): update architecture and spec for US-003`, com o ADR-006 em "Aceito" se aprovado. Nenhuma tarefa da §5 altera esses documentos. Os trechos a substituir são identificados pelo **texto antigo**, não só pelo número de linha (S1).

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
- `.claude/skills/bdd-para-testes/SKILL.md`, exemplo do "Exemplo completo" (hoje nas linhas 56–59, dentro do corpo do método; a `{` da linha 55 **fica**). O exemplo usa `.ReturnsRaw("no tags here")` e `ConversationServiceBuilder.With(brain).Build()`, nomes que não vão existir. É ilustrativo, mas para não gerar falso bloqueante (AP-005), trocar exatamente este texto antigo:
  ```csharp
      var brain = new FakeTutorBrain()
          .ReturnsRaw("no tags here")
          .Returns(TutorReplies.Valid());
      var sut = ConversationServiceBuilder.With(brain).Build();
  ```
  por este texto novo:
  ```csharp
      var brain = new FakeTutorBrain()
          .ReturnsInvalid("no tags here", TutorReplyRejections.MissingReply())
          .Returns(TutorReplies.Valid("t1"));
      var sut = CreateService(brain);
  ```

`specs/sprints/SPRINT_1.md`, seção `## US-004 — Conversar por texto com o professor via Claude Code` (B1: os itens que esta US adia passam a ter dono na spec):

- Texto antigo da linha de regras da US-004:
  `**Regras:** RN-BRAIN02, RN-BRAIN05, RN-BRAIN06, RN-FEED01–05; ARCHITECTURE §6.3; ADR-001, ADR-002.`
  Texto novo:
  `**Regras:** RN-BRAIN01, RN-BRAIN02, RN-BRAIN05, RN-BRAIN06, RN-CONV01, RN-CONV03, RN-CONV05, RN-CONV06, RN-FEED01–05; ARCHITECTURE §5, §6.3; ADR-001, ADR-002, ADR-006.`
- No bloco `gherkin` da US-004, depois do cenário "medição de latência" (última linha antiga: `  Então vejo a latência média e p95 de cada modo`), acrescentar:
  ```gherkin

  Cenário: console conversa pelo ConversationService
    Dado o console com o ConversationService registrado no DI e ligado ao provedor configurado
    Quando envio duas frases seguidas
    Então o BrainRequest da segunda frase contém o primeiro turno completo (aluno e professor)
    E vejo o reply e o feedback de cada turno

  Cenário: troca de modo pelo console
    Dado que estou no modo "free-conversation"
    Quando digito "/mode job-interview"
    Então vejo um aviso local confirmando o modo "job-interview"
    E a próxima mensagem enviada ao cérebro tem "Mode: job-interview"
    E "/help" lista "/mode" e os 3 modos

  Cenário: modo desconhecido pelo console
    Dado que estou no modo "free-conversation"
    Quando digito "/mode interview"
    Então vejo um aviso local com os modos disponíveis
    E nada é enviado ao cérebro
    E continuo no modo "free-conversation"

  Cenário: janela de turnos configurável
    Dado "Conversation:MaxTurnsInWindow" igual a 0 na configuração
    Quando a aplicação inicia
    Então ela encerra antes do primeiro prompt com a mensagem do validador, que cita "Conversation:MaxTurnsInWindow" e o valor esperado
    E o código de saída é o de configuração inválida (ExitCodes.InvalidConfiguration, diferente de 0)
  ```
- Na tabela "DoD extra" da US-004, depois da linha `D7`, acrescentar:
  ```markdown
  | D8 | `Select-String -Path src/EnglishTutor.Console/appsettings.json -Pattern '"MaxTurnsInWindow": 10'` | uma linha, dentro da seção `Conversation` (RN-CONV03) |
  | D9 | No Windows PowerShell 5.1: `"/mode job-interview", "/help", "/quit" \| dotnet run --project src/EnglishTutor.Console` | aviso de troca para `job-interview`; `/help` lista `/mode`; nenhum `Unknown command`; `exit=0` |
  | D10 | Inspeção de `tasks/US-004/plan.md` | uma P* decide como a continuidade de sessão chega ao adaptador (`BrainRequest.SessionHint` preenchido pelo serviço ou `session_id` guardado no adaptador, RN-BRAIN01), com teste |
  ```

Efeito colateral registrado: com o cenário "troca de modo pelo console", a US-004 atualiza o teste `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing`, que nesta US fixa `Unknown command "/mode".` (P12).

Se o usuário escolher o caminho (b) do B1 (trazer os itens para esta US), este bloco da `SPRINT_1.md` não é aplicado e o plano volta ao Planejador para incluir os testes e tarefas correspondentes.

### Alternativas descartadas

- `ITutorBrain` devolvendo `TutorReplyParseResult`: muda a §4 e contraria o ADR-005 (ADR-006).
- Retentativa dentro do adaptador: viola R4 (ADR-006).
- Correção como uma mensagem `User` só, sem o `Assistant` rejeitado: o modelo não vê o próprio erro (ADR-006).
- System prompt em `ConversationOptions`: texto grande em configuração e I/O no Core (ADR-006).
- Ligar o `ConversationService` ao console já nesta US: não há `ITutorBrain` real. Exigiria um cérebro "desconectado" provisório, descartado na US-004 (P1).
- Adiar para a US-004 sem tocar na spec: segundo adiamento sem dono (US-001 → US-003 → US-004), B1.
- `ConversationServiceBuilder` nos testes: um método privado `CreateService(...)` na classe de teste basta.
- `FakeLogger` (`Microsoft.Extensions.Diagnostics.Testing`): pacote a mais. Copia-se o `RecordingLogger<T>` de ~20 linhas da US-002 para `Core.Tests/Support`.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade | Estado |
|---|---|---|---|
| Criar | `docs/adr/ADR-006-retentativa-de-formato-e-historico-no-conversation-service.md` | Decisão (Aceito em 2026-10-17), criada junto com este plano | criado em `d88a842`; Aceito em `3e3d711` |
| Alterar | `ARCHITECTURE.md` | §3, §4, §5 (texto em §2.6), pelo orquestrador antes da T1 | feito em `f522128` |
| Alterar | `.claude/skills/bdd-para-testes/SKILL.md` | Trecho do "Exemplo completo" identificado pelo texto antigo (§2.6), pelo orquestrador antes da T1 | feito em `f522128` |
| Alterar | `specs/sprints/SPRINT_1.md` | US-004: regras, 4 cenários e D8–D10 (texto em §2.6, B1), pelo orquestrador antes da T1 | feito em `4b82f0a` |
| Alterar | `Directory.Packages.props` | `Microsoft.Extensions.Logging.Abstractions` do grupo "Infrastructure" para o grupo "Core" (S7) | feito em `70a9e5a` |
| Alterar | `src/EnglishTutor.Core/EnglishTutor.Core.csproj` | `PackageReference` a `Microsoft.Extensions.Logging.Abstractions` | feito em `70a9e5a` |
| Alterar | `tests/EnglishTutor.Core.Tests/Architecture/CoreDependencyTests.cs` | `Microsoft.Extensions.Logging.Abstractions` em `_allowedPackageReferences` | feito em `70a9e5a` |
| Alterar | `src/EnglishTutor.Core/Conversation/TutorMode.cs` | `TryParse` | feito em `89dd1f6`/`ecd41e7` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommand.cs` | `LocalCommandKind.Mode` | feito em `89dd1f6` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs` | Constante `Mode` (fora de `Available`) | feito em `89dd1f6` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommandParser.cs` | Reconhecer `/mode` | feito em `ecd41e7` |
| Criar | `src/EnglishTutor.Core/Brain/BrainRole.cs` | Papel da mensagem | criado em `3bb384f` |
| Criar | `src/EnglishTutor.Core/Brain/BrainMessage.cs` | Mensagem do histórico | criado em `3bb384f` |
| Criar | `src/EnglishTutor.Core/Brain/BrainRequest.cs` | Requisição completa ao cérebro | criado em `3bb384f` |
| Criar | `src/EnglishTutor.Core/Brain/ITutorBrain.cs` | Porta do cérebro | criado em `3bb384f` |
| Criar | `src/EnglishTutor.Core/Brain/BrainErrorKind.cs` | Classificação de falhas (RN-BRAIN07) | criado em `3bb384f` |
| Criar | `src/EnglishTutor.Core/Brain/BrainException.cs` | Exceção de domínio + fábrica `InvalidOutput` | criado em `3bb384f`/`cec3bd1` |
| Alterar | `src/EnglishTutor.Core/Brain/TutorReply.cs` | Fábrica `RawFallback` | feito em `613b98d`/`88aa8eb` |
| Criar | `src/EnglishTutor.Core/Conversation/ITutorPromptSource.cs` | Porta do system prompt | criado em `f75cc4b` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationOptions.cs` | `MaxTurnsInWindow` (padrão 10) | criado em `513066e` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationOptionsValidator.cs` | ≥ 1, com mensagem que diz o que se espera | criado em `513066e`/`e15fade` |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | Registrar o validador de `ConversationOptions` | feito em `e15fade` |
| Criar | `src/EnglishTutor.Core/Conversation/TurnMessageBuilder.cs` | Mensagem RN-CONV02 e instrução de correção | criado em `5edc1c0`/`c5db19f` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationTurn.cs` | Turno (aluno + professor) | criado em `f7dafc8` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationWindow.cs` | Últimos N turnos → mensagens | criado em `f7dafc8`/`9858f6d` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationTurnResult.cs` | Resposta ou aviso local | criado em `f75cc4b` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationMessages.cs` | Texto do aviso de entrada vazia | criado em `f75cc4b` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationService.cs` | Orquestração do turno, modo, retentativa e fallback | criado em `f75cc4b`; `24106f2`, `a401b33`, `88aa8eb`, `3206913` |
| Criar | `src/EnglishTutor.Core/Conversation/ConversationLog.cs` | `[LoggerMessage]` 3000 e 3001 | criado em `f75cc4b` |
| Alterar | `src/EnglishTutor.Console/Ui/ChatSession.cs` | Aviso local na linha em branco | feito em `232693a` |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/FakeTutorBrain.cs` | `Name => "Fake"` (fixo; a asserção de `SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason` depende dele, S8) + fila de respostas (`Returns`, `ReturnsInvalid`, `Throws`) + `Requests` + `Tokens` | criado em `f75cc4b` |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/FakeTutorPromptSource.cs` | Devolve um system prompt fixo; registra `Tokens` | criado em `f75cc4b` |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/TutorReplies.cs` | `TutorReplies.Valid(string label)`: `Reply`, `RawText` e uma `Correction` únicos por rótulo (AP-017) | criado em `f7dafc8` (T6, antes do previsto; §12) |
| Criar | `tests/EnglishTutor.Core.Tests/Support/RecordingLogger.cs` | Logger fake (cópia do da Infrastructure.Tests) | criado em `f75cc4b` |
| Alterar | `tests/EnglishTutor.Core.Tests/Conversation/TutorModeTests.cs` | `TryParse` | feito em `89dd1f6` |
| Alterar | `tests/EnglishTutor.Core.Tests/Conversation/LocalCommandParserTests.cs` | `/mode`; caso `"/mode interview"` do desconhecido trocado por `"/foo bar"` | feito em `89dd1f6` |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/BrainExceptionTests.cs` | Fábrica e guarda do construtor | criado em `3bb384f` |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationOptionsTests.cs` | Padrão 10 | criado em `513066e` |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationOptionsValidatorTests.cs` | Limite ≥ 1 nos dois lados | criado em `513066e` |
| Alterar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | Validador de `ConversationOptions` registrado | feito em `513066e` |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/TurnMessageBuilderTests.cs` | Formato RN-CONV02 e correção | criado em `5edc1c0` |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationWindowTests.cs` | Janela | criado em `f7dafc8`/`c5cbc1a` |
| Criar | `tests/EnglishTutor.Core.Tests/Conversation/ConversationServiceTests.cs` | Cenários da US | criado em `f75cc4b`; `89a1524`, `613b98d` |
| Alterar | `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` | Aviso na linha em branco | feito em `3efb049` |

## 4. Mapa cenário BDD → testes

Todos os testes do Core usam `FakeTutorBrain` (sem estado: só enxerga o `BrainRequest`), `FakeTutorPromptSource("SYSTEM PROMPT")` e `RecordingLogger<ConversationService>`. A mensagem esperada do cenário 1 é exatamente `"Student said (transcribed): \"I have worked with C# since five years\"\nLow-confidence words (possible pronunciation issues): []\nMode: job-interview"`.

**Valores distintos (AP-017, S2).** `TutorReplies.Valid(label)` devolve `Reply = $"Reply {label}?"`, `RawText = $"raw answer {label}"` (diferente do `Reply`) e uma `Correction` (feedback não vazio). Em testes com vários turnos, cada turno tem frase do aluno e rótulo próprios (`"t1"`, `"t2"`, ...). Em testes de retentativa, a resposta rejeitada da 1ª chamada (`raw1 = "rejected answer one"`) é diferente da que fica (`raw answer t1` ou `raw2`).

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| mensagem do turno no formato padrão | Core.Tests | `ConversationServiceTests` | `SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` (`ChangeMode(JobInterview)`; `Requests[0].Messages[^1]` = `User` + texto exato; `SystemPrompt == "SYSTEM PROMPT"`; `SessionHint` nulo, P8/P9) | Unidade |
| mensagem do turno no formato padrão | Core.Tests | `TurnMessageBuilderTests` | `Build_InJobInterviewMode_ProducesStandardThreeLineMessage` (texto exato, separador `\n`) | Unidade |
| mensagem do turno (apoio, RN-CONV02) | Core.Tests | `TurnMessageBuilderTests` | `Build_WithLowConfidenceWords_ListsThemQuotedAndCommaSeparated` (`["beach", "sheet"]`) | Unidade |
| mensagem do turno (apoio, P7) | Core.Tests | `TurnMessageBuilderTests` | `Build_WithStudentText_TrimsSpacesAndKeepsQuotes` (`[Theory]`: `"  I went home  "` → `"I went home"`; `He said "hi"` mantido sem escape) | Unidade |
| janela de turnos | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenWindowIsFull_SendsSystemPromptLastTurnsAndNewMessage` (`MaxTurnsInWindow = 2`; turnos `"turn one"`/`Valid("t1")`, `"turn two"`/`Valid("t2")`, `"turn three"`/`Valid("t3")`; 4º envio: `SystemPrompt == "SYSTEM PROMPT"`; `Messages` com 5 itens = `User`(contém `turn two`), `Assistant("raw answer t2")`, `User`(contém `turn three`), `Assistant("raw answer t3")`, nova mensagem; nenhuma mensagem contém `turn one` nem `t1`) | Unidade |
| janela de turnos (apoio: limite nos dois lados, AP-016) | Core.Tests | `ConversationWindowTests` | `Add_WhenMoreTurnsThanMax_KeepsMostRecentInOrder` (`[Theory]`: max 2 com 2 turnos → os 2; max 2 com 3 → turnos 2 e 3; max 1 com 3 → turno 3) | Unidade |
| janela de turnos (apoio) | Core.Tests | `ConversationWindowTests` | `ToMessages_WithTurns_AlternatesStudentAndTutorMessages` (`User` = `StudentMessage`, `Assistant` = `Reply.RawText`, diferente de `Reply.Reply`) | Unidade |
| janela de turnos (apoio) | Core.Tests | `ConversationWindowTests` | `Constructor_WithMaxTurnsBelowOne_Throws` (`0` → `ArgumentOutOfRangeException`) | Unidade |
| janela de turnos (apoio, RN-CONV03, P10) | Core.Tests | `ConversationOptionsTests` | `Constructor_Always_DefaultsMaxTurnsInWindowToTen` | Unidade |
| janela de turnos (apoio, P10) | Core.Tests | `ConversationOptionsValidatorTests` | `Validate_WhenMaxTurnsInWindowIsBelowOne_FailsWithExpectation` (`[Theory]` 0 e -1; mensagem contém `Conversation:MaxTurnsInWindow`, `(found 0)`/`(found -1)` e `Expected`) | Unidade |
| janela de turnos (apoio, P10) | Core.Tests | `ConversationOptionsValidatorTests` | `Validate_WhenMaxTurnsInWindowIsAtLeastOne_Succeeds` (`[Theory]` 1 e 10) | Unidade |
| janela de turnos (apoio: DI) | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_Always_RegistersConversationOptionsValidator` (com `MaxTurnsInWindow = 0`, ler `IOptions<ConversationOptions>.Value` lança `OptionsValidationException` com a mensagem do validador) | Unidade |
| histórico independe do provedor | Core.Tests | `ConversationServiceTests` | `SendAsync_OnSecondTurn_IncludesFirstTurnStudentAndTutorMessages` (1º turno `Valid("t1")`, 2º `Valid("t2")`; `Requests[1].Messages` = `User`(mensagem formatada do 1º turno), `Assistant("raw answer t1")`, `User`(2ª mensagem), P4) | Unidade |
| nova tentativa em resposta inválida | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection` (`Requests` com 2 itens; `Requests[1].Messages` = `[User(turno), Assistant(raw1), User(correção)]`; a última contém `format` e a `Message` da rejeição; `Requests[0].Messages` continua com 1 item, sem lista reaproveitada; P2, P3) | Unidade |
| nova tentativa em resposta inválida | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenFirstReplyIsInvalid_ReturnsSecondReplyWithFeedback` (2ª resposta `Valid("t1")`, com `Correction`; `IsReplied`; `Reply` igual à 2ª resposta; `IsRawFallback == false`; `FeedbackFormatter.Format(reply)` não vazio) | Unidade |
| nova tentativa em resposta inválida (apoio: correção) | Core.Tests | `TurnMessageBuilderTests` | `BuildFormatCorrection_WithRejection_IncludesRejectionMessageAndFormatInstruction` (texto exato das 2 linhas da §2.2 com `TutorReplyRejections.MissingReply()`) | Unidade |
| nova tentativa em resposta inválida (apoio: log) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason` (1 entrada `Warning`, EventId 3000, mensagem contém `Fake` e `missing reply` e não contém raw1 nem o texto do aluno) | Unidade |
| nova tentativa em resposta inválida (apoio, P6) | Core.Tests | `ConversationServiceTests` | `SendAsync_AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow` (turno 1: raw1 rejeitado e depois `Valid("t1")`; no turno 2 `Messages` = `[User(t1), Assistant("raw answer t1"), User(t2)]`, 3 itens; nenhuma contém raw1 nem a instrução de correção) | Unidade |
| nova tentativa em resposta inválida (apoio, S3: cancelamento repassado) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls` (token de um `CancellationTokenSource` não cancelado; raw1 rejeitado e depois `Valid("t1")`; `promptSource.Tokens` = `[token]`; `brain.Tokens` com 2 itens, ambos iguais a `token`) | Unidade |
| nova tentativa em resposta inválida (apoio, S4/P11: falha na retentativa) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn` (`[Theory]` `"timeout"` → `BrainException(Timeout)` com `Kind == Timeout`, `"cancelled"` → `OperationCanceledException`; 1ª chamada `InvalidOutput(raw1)`, 2ª lança; `Requests` com 2 itens; o envio seguinte (`Valid("t2")`) tem `Messages` com 1 item) | Unidade |
| resposta crua após duas falhas | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback` (raw1 = `"rejected answer one"`, raw2 = `"  Plain answer without tags.\n"`; `Reply.Reply == "Plain answer without tags."`; `Corrections` vazio e `FeedbackFormatter.Format` vazio; `IsRawFallback == true`; `RawText == raw2`, P13) | Unidade |
| resposta crua após duas falhas (apoio: log) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenReplyIsInvalidTwice_LogsRawFallbackWarning` (entradas 3000 e 3001, nessa ordem, `Warning`; nenhuma contém raw1/raw2) | Unidade |
| resposta crua após duas falhas (apoio, P5) | Core.Tests | `ConversationServiceTests` | `SendAsync_AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage` (raw1 ≠ raw2; no turno seguinte, `Messages[1]` = `Assistant(raw2)` sem `Trim`; nenhuma mensagem contém raw1) | Unidade |
| entrada vazia | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenInputIsBlank_DoesNotCallBrainAndReturnsWarning` (`[Theory]` `""`, `"   "`, `"\t"`; `Requests` vazio; `IsReplied == false`; `Warning == ConversationMessages.EmptyInput`) | Unidade |
| entrada vazia (apoio, S6: guarda) | Core.Tests | `ConversationServiceTests` | `SendAsync_WithNullText_ThrowsArgumentNullException` (`Requests` vazio) | Unidade |
| entrada vazia (console, P1) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning` (`[Theory]` `""`, `"   "`, `"\t"`; saída contém `ConversationMessages.EmptyInput` uma vez; handler sem entradas) | Componente UI |
| troca de modo | Core.Tests | `ConversationServiceTests` | `SendAsync_AfterModeCommand_SendsNewModeInTurnMessage` (`Mode == FreeConversation` no início; `LocalCommandParser.TryParse("/mode job-interview")` → `TutorMode.TryParse(command.Arguments)` → `ChangeMode`; `Messages[^1]` termina com `Mode: job-interview`) | Unidade |
| troca de modo (apoio, S6: guarda) | Core.Tests | `ConversationServiceTests` | `ChangeMode_WithNull_ThrowsArgumentNullException` (`Mode` continua `FreeConversation`) | Unidade |
| troca de modo (apoio, P12) | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments` (`[Theory]`: `"/mode job-interview"` → `"job-interview"`; `"/MODE  free-conversation "` → `"free-conversation"`; `"/mode"` → `""`) | Unidade |
| troca de modo (apoio) | Core.Tests | `TutorModeTests` | `TryParse_WithKnownName_ReturnsMode` (`[Theory]`: `job-interview`, `JOB-INTERVIEW`, `"  listening-practice "`) | Unidade |
| troca de modo (apoio) | Core.Tests | `TutorModeTests` | `TryParse_WithUnknownOrBlankName_ReturnsFalse` (`[Theory]`: `interview`, `""`, `"   "`) | Unidade |
| (apoio: P2, contrato da rejeição) | Core.Tests | `BrainExceptionTests` | `InvalidOutput_Always_CarriesKindRejectionAndRawText` (`Kind == InvalidOutput`, `IsInvalidOutput`, mesma rejeição, mesmo raw, `Message` contém `rejection.Message`) | Unidade |
| (apoio: P2) | Core.Tests | `BrainExceptionTests` | `Constructor_WithInvalidOutputKind_ThrowsArgumentException` | Unidade |
| (apoio, P11: só InvalidOutput é refeito) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry` (`BrainException(Timeout, ...)` propaga; `Requests` com 1 item) | Unidade |
| (apoio, P11: cancelamento) | Core.Tests | `ConversationServiceTests` | `SendAsync_WhenCancelled_PropagatesAndDoesNotRecordTurn` (cérebro lança `OperationCanceledException`, que propaga; o envio seguinte tem `Messages` com 1 item) | Unidade |

Teste existente alterado: em `LocalCommandParserTests.TryParse_WhenSlashCommandIsUnknown_ReturnsUnknownCommand`, o caso `"/mode interview"` vira `"/foo bar"` (`"/foo"`, `"bar"`), porque `/mode` deixa de ser desconhecido. A contagem de casos não muda. `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` (`"/mode interview"` → `Unknown command "/mode".`) **não muda** e continua verde como regressão de P12. A US-004 o atualiza ao tratar `/mode` no console (cenário "troca de modo pelo console", §2.6).

**Contagens previstas: 35 métodos novos, 51 casos novos.**

- Core.Tests: 34 métodos / 48 casos.
  - `ConversationServiceTests`: 18 / 21 (16 `[Fact]` + 2 `[Theory]`, com 3 e 2 casos).
  - `TurnMessageBuilderTests`: 4 / 5.
  - `ConversationWindowTests`: 3 / 5.
  - `TutorModeTests`: +2 / +6.
  - `LocalCommandParserTests`: +1 / +3.
  - `BrainExceptionTests`: 2 / 2.
  - `ConversationOptionsTests`: 1 / 1.
  - `ConversationOptionsValidatorTests`: 2 / 4.
  - `CoreServiceCollectionExtensionsTests`: +1 / +1.
- Console.Tests: 1 / 3.

Suíte inteira após a US: **148 casos** (Core.Tests 51 + 48 = 99; Console.Tests 22 + 3 = 25; Infrastructure.Tests 24). Se o Executor mudar uma contagem, atualiza §4 e D2/D5/D8 com o motivo.

## 5. Tarefas (ordem de execução TDD)

Pré-condição (ponto único): na aprovação e **antes da T1**, o orquestrador aplica a §2.6 (`ARCHITECTURE.md` §3/§4/§5, o trecho da `bdd-para-testes` e a US-004 do `specs/sprints/SPRINT_1.md`) num commit `docs(ai): update architecture and spec for US-003`, com o ADR-006 em "Aceito" se aprovado.

Em toda tarefa, os esqueletos vêm antes dos testes (AP-013). Depois de `x.Should().BeTrue()` em propriedades com `[MemberNotNullWhen]`, use `!` (AP-020). Escolha de forma não prevista aqui → registro na §12 em commit `docs(ai)` **antes** do `feat` (AP-009/AP-014). Os nomes de teste nas tarefas são os mesmos da §4, por extenso (S10).

- [x] T1 — Pacote: mover a `PackageVersion` de `Microsoft.Extensions.Logging.Abstractions` para o `ItemGroup Label="Core"` do `Directory.Packages.props` (mesma versão), `PackageReference` a ela no Core e o nome em `CoreDependencyTests._allowedPackageReferences`, no mesmo commit (sem o último, `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` fica vermelho). `dotnet build` com 0 warnings e suíte verde (97 casos). (commit: `chore(core): allow logging abstractions in core`) — feito: `70a9e5a` (suíte 97 verde, 0 warnings).
- [x] T2 — Red: esqueletos `LocalCommandKind.Mode`, `LocalCommands.Mode`, `TutorMode.TryParse` (lança `NotImplementedException`). Testes `TryParse_WithKnownName_ReturnsMode`, `TryParse_WithUnknownOrBlankName_ReturnsFalse` e `TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments`, que falha por asserção: `Unknown` ≠ `Mode`. Troca de `"/mode interview"` por `"/foo bar"` no teste de desconhecido, que continua verde. Green: `KindOf` reconhece `/mode`; `TryParse` sobre `TutorMode.All`. O `ChatSession` não muda, e `Mode` cai no `default` (P12). (commits: `test(core): mode command and mode names` + `feat(core): parse mode command and mode names`) — feito: `89dd1f6` (Red: 6 `NotImplementedException` + 3 `Unknown` ≠ `Mode`) + `ecd41e7`.
- [x] T3 — Red: esqueletos `BrainRole`, `BrainMessage`, `BrainRequest`, `ITutorBrain`, `BrainErrorKind`, `BrainException` (construtor sem a guarda; `InvalidOutput` lança `NotImplementedException`) + `BrainExceptionTests`. Os 2 falham: um com `NotImplementedException`, o outro porque nada é lançado. Confirmar no build do esqueleto que não há CS8775 em `IsInvalidOutput`. Se aparecer CA1032, aplicar a forma da §2.1. Green: guarda e fábrica. (commits: `test(core): brain exception for invalid output` + `feat(core): add brain contract and brain exception`) — feito: `3bb384f` (Red: 1 `NotImplementedException`, 1 "no exception was thrown"; sem CS8775 nem CA1032 no esqueleto) + `cec3bd1`.
- [x] T4 — Red: esqueletos `ConversationOptions` (padrão já 10, porque é dado e não regra), `ConversationOptionsValidator.Validate` (lança `NotImplementedException`) + `ConversationOptionsTests`, `ConversationOptionsValidatorTests`, `AddEnglishTutorCore_Always_RegistersConversationOptionsValidator`. `Constructor_Always_DefaultsMaxTurnsInWindowToTen` nasce verde com o esqueleto: é teste de caracterização de dado. Para provar que discrimina, mostrar o Red por mutação temporária não commitada (padrão `= 0`), registrando a falha na tarefa (AP-017). Os demais falham com `NotImplementedException` ou porque o validador não está registrado. Green: validador e registro no DI. (commits: `test(core): conversation options validation` + `feat(core): validate conversation window size`) — feito: `513066e` + `e15fade`. Red por mutação (não commitada): com o inicializador removido (valor 0), `Constructor_Always_DefaultsMaxTurnsInWindowToTen` falhou com "Expected options.MaxTurnsInWindow to be 10, but found 0"; `= 0` literal não compila (CA1805), ver §12.
- [x] T5 — Red: esqueleto `TurnMessageBuilder` (`Build` e `BuildFormatCorrection` lançam `NotImplementedException`) + `TurnMessageBuilderTests` (4 métodos / 5 casos). Green: formato da §2.2. (commits: `test(core): turn message format` + `feat(core): build standard turn message`) — feito: `5edc1c0` (Red: 5 `NotImplementedException`) + `c5db19f`.
- [x] T6 — Red: esqueletos `ConversationTurn`, `ConversationWindow` (`Add` e `ToMessages` lançam `NotImplementedException`; construtor sem guarda) + `ConversationWindowTests` (3 / 5). Green: guarda, descarte do mais antigo e mensagens. (commits: `test(core): conversation window` + `feat(core): keep last turns in conversation window`) — feito: `f7dafc8` (Red: 4 `NotImplementedException` + 1 "no exception was thrown") + `c5cbc1a` (correção do teste, ver §12) + `9858f6d`.
- [x] T7 — Red: esqueletos `ITutorPromptSource`, `ConversationTurnResult` (fábricas), `ConversationMessages`, `ConversationLog` (3000 e 3001 já declarados, sem uso), `ConversationService` (`Mode` pronto; `ChangeMode` só atribui, **sem** a guarda de nulo; `SendAsync` lança `NotImplementedException`) e os fakes (`FakeTutorBrain` com `Name => "Fake"` e `FakeTutorPromptSource`, ambos com `Tokens`, `TutorReplies.Valid(label)`, `RecordingLogger`). Testes, todos com respostas válidas ou com `BrainException(Timeout)`/`OperationCanceledException`, nunca `InvalidOutput`:
  - `SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast`
  - `SendAsync_WhenWindowIsFull_SendsSystemPromptLastTurnsAndNewMessage`
  - `SendAsync_OnSecondTurn_IncludesFirstTurnStudentAndTutorMessages`
  - `SendAsync_WhenInputIsBlank_DoesNotCallBrainAndReturnsWarning` (3 casos)
  - `SendAsync_WithNullText_ThrowsArgumentNullException`
  - `SendAsync_AfterModeCommand_SendsNewModeInTurnMessage`
  - `ChangeMode_WithNull_ThrowsArgumentNullException`
  - `SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry`
  - `SendAsync_WhenCancelled_PropagatesAndDoesNotRecordTurn`

  São 9 métodos / 11 casos. `ChangeMode_WithNull_ThrowsArgumentNullException` falha porque nada é lançado (esqueleto sem guarda); os 8 de `SendAsync` falham com a `NotImplementedException` (em `SendAsync_WithNullText_ThrowsArgumentNullException`, por ser a exceção errada). Green: guarda do `ChangeMode` e passos 1–4 e 6 da §2.2, com o `await brain.SendAsync` direto e o `ct` repassado. **Estado do ramo `InvalidOutput` ao fim da T7:** não há `catch`, então `BrainException.InvalidOutput` propaga ao chamador como qualquer outra falha. Por isso `SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry` já passa aqui e segue como regressão na T8/T9. (commits: `test(core): conversation service turn and window` + `feat(core): send turns with application-owned history`) — feito: `f75cc4b` (Red: 10 casos com `NotImplementedException`/exceção errada + `ChangeMode` sem exceção) + `24106f2`.
- [x] T8 — Red (6 métodos / 7 casos), todos com um `InvalidOutput(raw1)` na 1ª chamada:
  - `SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection`
  - `SendAsync_WhenFirstReplyIsInvalid_ReturnsSecondReplyWithFeedback`
  - `SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason`
  - `SendAsync_AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow`
  - `SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls`
  - `SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn` (2 casos; a 2ª chamada lança `Timeout` ou `OperationCanceledException`)

  Todos falham porque a `BrainException` de `InvalidOutput` propaga (estado da T7): nos 5 primeiros, por exceção inesperada; no último, porque a exceção recebida não é a esperada (`Kind` ≠ `Timeout` ou tipo ≠ `OperationCanceledException`) e `Requests` tem 1 item, não 2. O Executor confere esse motivo antes do Green. Green: `catch` de `IsInvalidOutput`, log 3000 e nova chamada com as mensagens extras e o mesmo `ct`. **Estado ao fim da T8:** se a segunda chamada também lançar `InvalidOutput`, a exceção propaga, sem `catch` interno; outras falhas da segunda chamada propagam (e continuam propagando na T9). (commits: `test(core): retry once on invalid tutor reply` + `feat(core): retry with format correction on invalid reply`) — feito: `89a1524` (Red: os 7 casos recebem a `BrainException` de `InvalidOutput`; no `[Theory]`, exceção diferente da esperada) + `a401b33`.
- [x] T9 — Red: esqueleto `TutorReply.RawFallback` (lança `NotImplementedException`) + `SendAsync_WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback`, `SendAsync_WhenReplyIsInvalidTwice_LogsRawFallbackWarning`, `SendAsync_AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage` (3). Todos falham porque a segunda `BrainException` propaga (estado da T8). Green: `catch` interno só de `IsInvalidOutput`, log 3001 e `RawFallback`. Refactor: extrair a montagem do `BrainRequest` num método privado. (commits: `test(core): raw fallback after second invalid reply` + `feat(core): fall back to raw reply after second failure` + `refactor(core): ...` se houver) — feito: `613b98d` (Red: a 2ª `BrainException` propaga nos 3) + `88aa8eb` + `3206913` (refactor: `AskWithFormatRetryAsync` e `CreateRequest`).
- [x] T10 — Red: `ChatSessionTests.RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning` (3 casos), que falha porque hoje nada é escrito. Green: `ChatSession` escreve `ConversationMessages.EmptyInput` na linha em branco e atualiza o comentário da RN-CONV05. `RunAsync_WhenLineIsBlank_IgnoresItAndForwardsNothing` continua verde (2 prompts). (commits: `test(console): warn on blank input` + `feat(console): show local warning for blank input`) — feito: `3efb049` (Red: "Expected ... to be 1, but found 0" nos 3 casos) + `232693a`.
- [x] T11 — DoD: rodar D1–D11, colar em §11, marcar §5 com os hashes. (commit: `docs(ai): add DoD evidence for US-003`) — feito: ver §11 (commit desta evidência).

Conferência das contagens por tarefa: `ConversationServiceTests` = T7 9/11 + T8 6/7 + T9 3/3 = 18 métodos / 21 casos.

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Logging.Abstractions (10.0.13, já no CPM; passa ao grupo "Core" do `Directory.Packages.props`) | Core | `ILogger<T>` e `[LoggerMessage]` no `ConversationService` (permitido por R1) |

Nenhum pacote novo em Infrastructure, Console ou nos projetos de teste. `Microsoft.Extensions.Options` (já no Core) cobre `IOptions<T>`/`IValidateOptions<T>`. Os testes usam `Options.Create(...)`.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| CS8775 em `IsInvalidOutput`/`IsReplied` com `[MemberNotNullWhen]` | Auto-propriedade armazenada, atribuída no construtor (forma já validada no US-002). O build do esqueleto nas T3/T7 confirma. Se mesmo assim falhar, parar e registrar na §12 |
| CA1032 (construtores padrão de exceção) quebra o build | Forma decidida na §2.1 (3 construtores com `Kind = Unknown`) |
| Lista de mensagens reaproveitada entre a 1ª e a 2ª chamada faz o `FakeTutorBrain` registrar um request mutado | Lista nova a cada request (§2.2, passo 4); o teste de retentativa confere que `Requests[0].Messages` continua com 1 item |
| `CancellationToken.None` passado adiante no lugar do `ct` do chamador | `SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls` confere o token nas 3 chamadas (S3) |
| Asserção de janela satisfeita por respostas iguais em todos os turnos (AP-017) | `TutorReplies.Valid(label)` com textos únicos por turno; raw1 ≠ resposta aceita (S2) |
| `RawText` vazio gera `Assistant` vazio na retentativa, e algumas APIs recusam | Fica com o adaptador (US-004/US-006), registrado no ADR-006 |
| `/mode` reconhecido no Core mas mostrado como "Unknown command" no console até a US-004 | Fica fora de `/help` (US-001, P2), o teste de console existente fixa o comportamento atual (P12), e a entrega no console passa a constar na spec da US-004 (§2.6) |
| Escopo adiado para a US-004 sem dono na spec (adiamento em cadeia) | Cenários e DoD extra acrescentados à US-004 no commit anterior à T1 (§2.6, B1) |
| Duas checagens de linha em branco (`ChatSession` e `ConversationService`) | As duas usam o mesmo texto do Core (`ConversationMessages.EmptyInput`). Na US-004 o console delega ao serviço e decide se mantém a checagem |
| Asserção satisfeita por texto externo (AP-015) | Todas as mensagens conferidas (aviso, correção, logs, validador) são do projeto |
| Cobertura do namespace `Conversation` < 90% (D6) | Cada ramo do serviço tem teste (§4), incluindo as guardas (S6). `ConversationLog` é código gerado e exercitado pelos testes de log |

## 8. Fora de escopo

- Implementações de `ITutorBrain` e de `ITutorPromptSource` (leitura de `prompts/tutor.md`): US-004/US-006.
- Ligar o `ConversationService` ao console: handler que delega ao serviço, exibição do reply e do feedback, `/mode` no console e em `/help`, bind de `Conversation:MaxTurnsInWindow` com `ValidateOnStart` e chave no `appsettings.json`, registro do serviço no DI. Tudo na US-004 (P1), com cenários e DoD extra registrados na spec da US-004 antes da T1 (§2.6).
- RN-CONV04 (resumo dos 5 erros mais frequentes) e persistência de turnos (RN-PERS*): dependem do `IConversationStore` (fases futuras).
- Entrada por voz (`Transcript`), palavras de baixa confiança reais, limite de 5 e ordenação (RN-STT01) e TTS (RN-TTS01, além de o `Reply` estar separado do feedback no resultado).
- Mensagens ao aluno por `BrainErrorKind` (US-005) e fallback entre provedores (US-007).
- Medição de latência do cérebro em log: entra com os adaptadores (US-004).
- `SessionHint` preenchido: US-004 decide (P8), registrado como D10 da US-004 (§2.6).

## 9. Perguntas em aberto

- **P1 — Quanto do console entra nesta US?** **Padrão proposto:** só o aviso local da linha em branco, usando o texto do Core. A ligação ao `ConversationService`, o `/mode` no console, o bind de `Conversation:MaxTurnsInWindow`, o `appsettings.json` e o registro no DI ficam para a US-004, quando existir um cérebro real, **e passam a constar na spec da US-004** (4 cenários + D8–D10, texto exato na §2.6; caminho (a) do B1). Alternativa (caminho (b) do B1): tudo agora, com um `ITutorBrain` provisório "não conectado"; nesse caso o plano volta ao Planejador. Teste: `ChatSessionTests.RunAsync_WhenLineIsBlank_ShowsEmptyInputWarning`.
- **P2 — Como a rejeição do parser chega ao Core?** **Padrão proposto:** `BrainException.InvalidOutput(rejection, rawText)` (ADR-006). Alternativa: mudar `ITutorBrain` para devolver `TutorReplyParseResult`. Testes: `BrainExceptionTests.InvalidOutput_Always_CarriesKindRejectionAndRawText` e `ConversationServiceTests.SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection`.
- **P3 — Forma da segunda chamada.** **Padrão proposto:** mesmas mensagens + `Assistant(raw1)` + `User(instrução de correção com a Message da rejeição)`, com o mesmo system prompt e o mesmo `CancellationToken`. Alternativa: só um `User` com a correção. Testes: `ConversationServiceTests.SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection` e `ConversationServiceTests.SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls`.
- **P4 — Conteúdo do professor no histórico.** **Padrão proposto:** `TutorReply.RawText` (o que o modelo de fato respondeu). Alternativa: só o `Reply`. Teste: `ConversationServiceTests.SendAsync_OnSecondTurn_IncludesFirstTurnStudentAndTutorMessages`.
- **P5 — Turno com fallback entra na janela?** **Padrão proposto:** sim, com o texto cru (`RawText`, sem `Trim`). Alternativa: excluir o turno da janela. Teste: `ConversationServiceTests.SendAsync_AfterRawFallbackTurn_SendsRawAnswerAsTutorMessage`.
- **P6 — Tentativa rejeitada e instrução de correção ficam no histórico?** **Padrão proposto:** não. Só o turno final (mensagem do aluno + resposta aceita). Teste: `ConversationServiceTests.SendAsync_AfterRetriedTurn_KeepsOnlyFinalAnswerInWindow`.
- **P7 — Normalização do texto do aluno na mensagem do turno.** **Padrão proposto:** `Trim()` e nenhum escape de aspas internas. Teste: `TurnMessageBuilderTests.Build_WithStudentText_TrimsSpacesAndKeepsQuotes`.
- **P8 — `SessionHint` nesta US.** **Padrão proposto:** sempre `null`. A US-004 decide se o serviço gera um id de sessão ou se o adaptador guarda o `session_id` (D10 acrescentado à US-004, §2.6). Teste: `ConversationServiceTests.SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` (confere `SessionHint` nulo).
- **P9 — De onde vem o system prompt e o que ele contém?** **Padrão proposto:** porta `ITutorPromptSource`, lida a cada turno (uma vez, reaproveitada na retentativa), só com o conteúdo de `tutor.md`. A instrução de formato é anexada pelo adaptador (ADR-002). Testes: `ConversationServiceTests.SendAsync_InJobInterviewMode_SendsStandardTurnMessageLast` e `ConversationServiceTests.SendAsync_WhenWindowIsFull_SendsSystemPromptLastTurnsAndNewMessage` (`SystemPrompt == "SYSTEM PROMPT"`).
- **P10 — Limites de `Conversation:MaxTurnsInWindow`.** **Padrão proposto:** mínimo 1, sem máximo, padrão 10 (RN-CONV03). O valor 0 é rejeitado porque deixaria o professor sem histórico (contra a RN-BRAIN01). Testes: `ConversationOptionsValidatorTests.Validate_WhenMaxTurnsInWindowIsBelowOne_FailsWithExpectation`, `ConversationOptionsValidatorTests.Validate_WhenMaxTurnsInWindowIsAtLeastOne_Succeeds` e `ConversationOptionsTests.Constructor_Always_DefaultsMaxTurnsInWindowToTen`.
- **P11 — Falhas que não são de formato.** **Padrão proposto:** outros `BrainErrorKind` e `OperationCanceledException`, na primeira chamada ou na retentativa, propagam sem nova tentativa e sem registrar o turno. As mensagens ao aluno ficam para a US-005 e o fallback de provedor para a US-007. Testes: `ConversationServiceTests.SendAsync_WhenBrainFailsWithOtherKind_PropagatesWithoutRetry`, `ConversationServiceTests.SendAsync_WhenCancelled_PropagatesAndDoesNotRecordTurn` e `ConversationServiceTests.SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn`.
- **P12 — `/mode` no console antes da US-004.** **Padrão proposto:** o Core já reconhece `/mode`, mas o comando não aparece em `/help`, e o console continua mostrando `Unknown command "/mode".` até a US-004, cuja spec passa a ter os cenários "troca de modo pelo console" e "modo desconhecido pelo console" (§2.6). Alternativa: deixar todo o `/mode` (parser incluído) para a US-004 e testar o cenário só com `ChangeMode`. Testes: `LocalCommandParserTests.TryParse_WhenInputIsModeCommand_ReturnsModeWithArguments` e o existente `ChatSessionTests.RunAsync_AfterUnknownCommand_KeepsSessionOpenAndForwardsNothing` (regressão).
- **P13 — Texto exibido no fallback.** **Padrão proposto:** `Reply` = texto cru da segunda resposta com `Trim()`, e `RawText` = texto original. Teste: `ConversationServiceTests.SendAsync_WhenReplyIsInvalidTwice_ReturnsRawSecondAnswerWithoutFeedback`.
- **P14 — RN-CONV04 (resumo de erros recorrentes) nesta US.** **Padrão proposto:** fora (§8). Sem persistência não há erros recorrentes, então nada é acrescentado ao system prompt. Sem efeito observável.

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell (exceto D11, em Git Bash, e D9, no Windows PowerShell 5.1).

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros (sem CS8775 nem CA1032) |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: 148 casos (Core.Tests 99, Console.Tests 25, Infrastructure.Tests 24), em < 30 s, sem rede nem GPU |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test tests/EnglishTutor.Core.Tests --no-build` (depois do D1) | 99 casos verdes; o número comparado com 10 s é a duração da linha de resumo do teste (`Duração`/`Duration`), que não inclui build; só fakes |
| D6 | `dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage/conversation -reporttypes:TextSummary -assemblyfilters:"+EnglishTutor.Core" -classfilters:"+EnglishTutor.Core.Conversation.*"; Get-Content coverage/conversation/Summary.txt` (após D3) | linha `EnglishTutor.Core` (restrita ao namespace `Conversation`) ≥ 90% de linhas |
| D7 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"`; `dotnet list src/EnglishTutor.Core package` | 3 testes verdes; pacotes: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options` |
| D8 | `dotnet test --filter "FullyQualifiedName~ConversationServiceTests"` | 21 casos verdes (18 métodos), cobrindo os 7 cenários da US |
| D9 | No **Windows PowerShell 5.1** (`powershell.exe`): `"   ", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | cabeçalho com provedor e modo; a linha `Nothing was sent: type a sentence in English and press Enter.` aparece uma vez (a primeira linha chega com BOM, AP-006); `exit=0` |
| D10 | `git status --short` após D2–D9 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/` ou `obj/` |
| D11 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |

D11 (fora da tabela por causa do `|`; forma única do `checklist-revisao` §4, AP-019):

```bash
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
```

Também entram, da DoD comum: todo cenário BDD com teste escrito antes do código; `review.md` sem bloqueantes; `docs/us/US-003.md` criado; ADR-006 aceito e `ARCHITECTURE.md` §3/§4/§5 + `bdd-para-testes` + US-004 do `SPRINT_1.md` atualizados antes da T1 (§2.6); merge `--no-ff` em `develop`.

## 11. Evidências

Executado em 2026-10-17 sobre o commit `232693a` (todo o código da US commitado), Windows 11, .NET 10, Windows PowerShell 5.1.26100.

| # | Resultado | |
|---|---|---|
| D1 | `dotnet build` → "Compilação com êxito. 0 Aviso(s) 0 Erro(s)" (sem CS8775 nem CA1032) | ✅ |
| D2 | `dotnet test --filter "Category!=LiveBrain"` → Core.Tests 99/99, Console.Tests 25/25, Infrastructure.Tests 24/24 = **148 passed, 0 failed**; 6,1 s de parede com build incremental; sem rede nem GPU | ✅ |
| D3 | Cobertura com `--results-directory TestResults` → 148 verdes; 3 arquivos `coverage.cobertura.xml` | ✅ |
| D3b | `coverage/Summary.txt` → `EnglishTutor.Core 100%` (geral da solução 87,8%) | ✅ |
| D4 | `dotnet format --verify-no-changes` → sem diferenças, exit=0 | ✅ |
| D5 | `dotnet test tests/EnglishTutor.Core.Tests --no-build` → 99 passed, Duração: 127 ms (< 10 s) | ✅ |
| D6 | `coverage/conversation/Summary.txt` → `EnglishTutor.Core` restrito a `EnglishTutor.Core.Conversation.*`: **100% de linhas** (169/169; ramos 85%, 17/20), 13 classes, incluindo `ConversationService` e `ConversationLog` 100% | ✅ |
| D7 | `CoreDependencyTests` → 3 passed; `dotnet list src/EnglishTutor.Core package` → `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.13, `Microsoft.Extensions.Logging.Abstractions` 10.0.13, `Microsoft.Extensions.Options` 10.0.13 | ✅ |
| D8 | `--filter "FullyQualifiedName~ConversationServiceTests"` → 21 passed (18 métodos) | ✅ |
| D9 | `"   ", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` no PowerShell 5.1 → cabeçalho `Brain: ClaudeCode \| Mode: free-conversation`; `> Nothing was sent: type a sentence in English and press Enter.` uma vez (1ª linha com BOM); `exit=0` | ✅ |
| D10 | `git status --short` após D2–D9 → vazio | ✅ |
| D11 | `git diff develop...HEAD \| LC_ALL=C grep -c ...` (Git Bash) → `0`, código de saída 1 (esperado, AP-019) | ✅ |

DoD comum: cada cenário BDD com teste escrito antes do código (pares `test`/`feat` em §5); §2.6 aplicada antes da T1 (`4b82f0a`, `f522128`); ADR-006 Aceito (`3e3d711`). Pendentes fora do Executor: `review.md`, `docs/us/US-003.md` e merge em `develop`.

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-17 — pedido do Revisor do plano (rodada 1, B1)

**B1** — §2.4, §2.5, §8 e §2.6 ("`specs/` não muda"). O plano adia para a US-004 cinco itens (ligação do `ConversationService` ao console; `/mode` no console e em `/help`; bind de `Conversation:MaxTurnsInWindow` com `ValidateOnStart` e a chave no `appsettings.json`; registro do serviço no DI; decisão sobre `SessionHint`), mas a US-004 em `specs/sprints/SPRINT_1.md` não cita nenhum deles — é o segundo adiamento (US-001 → US-003 → US-004). Mover escopo entre US é mudança de spec. Caminhos: (a) registrar os itens adiados na US-004 do `SPRINT_1.md` (cenário ou DoD extra), em commit `docs(ai): update spec for US-003` anterior à T1, e a §2.6 deixa de dizer "`specs/` não muda"; ou (b) trazê-los para esta US. A escolha final é do usuário na aprovação (P1/P12); até lá, mantenha o padrão do plano (caminho a) e escreva no plano o texto exato a acrescentar na US-004.

→ Incorporado (caminho (a), padrão; (b) fica como alternativa em P1). Respostas no formato "item → seções tocadas":

- B1 → §2.3, §2.4, §2.5, §2.6 (bloco `specs/sprints/SPRINT_1.md`: linha de regras, 4 cenários e D8–D10 da US-004, com texto antigo e novo; "`specs/` não muda" removido), §2.6 Alternativas descartadas, §3 (linha `SPRINT_1.md`), §5 pré-condição (commit `docs(ai): update architecture and spec for US-003`, um só para documentos-raiz e spec), §7, §8, §9 P1/P8/P12, §10 (DoD comum). Cobertura dos 5 itens: ligação ao console e registro no DI → cenário "console conversa pelo ConversationService"; `/mode` e `/help` → cenários "troca de modo pelo console" e "modo desconhecido pelo console" + D9; bind e `appsettings.json` → cenário "janela de turnos configurável" + D8; `SessionHint` → D10.
- S1 → §2.6 (trecho da `bdd-para-testes` identificado pelo texto antigo, linhas 56–59, a `{` fica), §3.
- S2 → §2.2 não muda; §3 (`TutorReplies.Valid(label)`), §4 (parágrafo "Valores distintos" e os métodos `WhenWindowIsFull`, `OnSecondTurn`, `ReturnsSecondReplyWithFeedback`, `AfterRetriedTurn`, `WhenReplyIsInvalidTwice_Returns...`, `AfterRawFallbackTurn`), §7.
- S3 → §2.2 (passos 3 e 5 e parágrafo do `ct`), §3 (`Tokens` nos fakes), §4 (`SendAsync_WhenRetrying_PassesCallerTokenToPromptSourceAndBothBrainCalls`), §5 T7/T8, §7, §9 P3.
- S4 → §2.2 (passo 5), §4 (`SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn`, 2 casos), §5 T8 (motivo do Red e estado ao fim), §9 P11.
- S5 → §10 D5 (um comando só, `--no-build`, número comparado = duração da linha de resumo).
- S6 → §4 (`SendAsync_WithNullText_ThrowsArgumentNullException`, `ChangeMode_WithNull_ThrowsArgumentNullException`), §5 T7 (esqueleto de `ChangeMode` sem guarda, para o Red), §7.
- S7 → §2.5, §3 (`Directory.Packages.props`), §5 T1, §6.
- Contagens → §4 (35 métodos / 51 casos; Core.Tests 34/48; `ConversationServiceTests` 18/21; suíte 148), §5 (T7 9/11, T8 6/7, T9 3), §10 D2 (148; Core 99), D5 (99), D8 (21/18).

### 2026-10-17 — usuário (aprovação condicionada)

Aprovo o plano com as respostas padrão para P1–P14 e o ADR-006. Incorporar as sugestões S8, S9 e S10 da rodada 2 de `plan-review.md`:
- **S8** — §3: deixar explícito que o `Name` do `FakeTutorBrain` é `"Fake"` (a asserção de `SendAsync_WhenFirstReplyIsInvalid_LogsRetryWarningWithReason` depende disso).
- **S9** — §2.6, cenário novo da US-004 "janela de turnos configurável": dizer que a aplicação encerra com o código de saída de configuração inválida (`ExitCodes.InvalidConfiguration`, ≠ 0), no cenário ou num D da US-004.
- **S10** — §5 T7 e T8: usar os nomes completos dos métodos de teste (com o prefixo `SendAsync_`/`ChangeMode_` e sem `...`), idênticos aos da §4.

→ Incorporado. Respostas no formato "item → seções tocadas":

- S8 → §3 (linha `FakeTutorBrain.cs`: `Name => "Fake"`, fixo, com a dependência do teste de log), §5 T7 (lista de esqueletos dos fakes cita `Name => "Fake"`).
- S9 → §2.6 (bloco `gherkin` da US-004, cenário "janela de turnos configurável": nova linha `E o código de saída é o de configuração inválida (ExitCodes.InvalidConfiguration, diferente de 0)`; `ExitCodes.InvalidConfiguration = 2` em `src/EnglishTutor.Console/ExitCodes.cs:10`). Nenhum D novo na US-004; `specs/` só muda pelo orquestrador antes da T1, como já previsto.
- S10 → §5 (frase no parágrafo de abertura das tarefas), §5 T7 (9 nomes completos na lista e nos textos de motivo do Red e do estado do ramo), §5 T8 (6 nomes completos), §5 T9 (3 nomes completos, pela mesma razão). Os nomes conferem um a um com a §4.
- Sem mudança em contagens (35/51; `ConversationServiceTests` 18/21; suíte 148), escopo, tarefas, testes, P* ou DoD.

### 2026-10-17 — aprovação do usuário

Plano aprovado pelo usuário no chat em 2026-10-17, com S8, S9 e S10 incorporados (rodada 3 da revisão do plano: APROVADO).
- Perguntas em aberto P1–P14: valem as respostas padrão propostas na §9 (P1 e B1: caminho (a), itens adiados registrados na US-004).
- ADR-006: Aceito.
- §2.6 aplicada antes da T1: `docs(ai): update spec for US-003` (US-004 do `SPRINT_1.md`) e `docs(ai): update architecture for US-003` (`ARCHITECTURE.md` §3/§4/§5 e exemplo da `bdd-para-testes`). O orquestrador usou dois commits, em vez do commit único citado na §2.6 e na pré-condição da §5, para separar spec e arquitetura conforme o fluxo do `/us`.

### 2026-10-17 — Executor: escolhas de forma e desvios da implementação

Nenhum desvio de comportamento, de arquitetura nem de contagem (148 casos; `ConversationServiceTests` 18/21). Registros:

- **E1 — Mutação da T4.** O plano pedia a mutação temporária `= 0`. Ela não compila (CA1805: inicialização explícita com o valor padrão). Usei a mutação equivalente, sem inicializador (valor 0). O teste falhou com "Expected options.MaxTurnsInWindow to be 10, but found 0". A mutação não foi commitada.
- **E2 — `TutorReplies.Valid(label)` criado na T6** (`f7dafc8`), não na T7, porque `ConversationWindowTests` já precisava de `TutorReply`. Mesmo conteúdo da §3/§4.
- **E3 — Comparação em `Add_WhenMoreTurnsThanMax_KeepsMostRecentInOrder`** (`c5cbc1a`). A 1ª versão comparava `ConversationTurn` inteiros com `Equal`. A igualdade de record compara `Corrections` por referência, e cada `TutorReplies.Valid` cria uma lista nova, então o teste falhava mesmo com a janela correta. Passou a comparar a sequência de `StudentMessage` (`student t1`, ...), o que continua verificando quais turnos ficam e em que ordem. O teste não ficou mais fraco quanto ao risco do cenário.
- **E4 — Asserção da exceção em `SendAsync_WhenRetryFailsWithOtherKindOrIsCancelled_PropagatesAndDoesNotRecordTurn`.** Em vez de um ramo por caso (`Kind == Timeout` / tipo `OperationCanceledException`), o teste confere `BeSameAs(retryFailure)`, a mesma instância lançada pela 2ª chamada (no caso `"timeout"`, a `BrainException` com `Kind == Timeout`). É mais forte e evita `if` no teste.
- **E5 — Nomes que o plano não fixava** (decididos no Red da T7, `f75cc4b`; este registro veio depois do `feat`, ver "Possível aprendizado" no relatório): métodos `ConversationLog.ReplyRejectedRetrying` (3000) e `ConversationLog.ReplyRejectedAgainUsingRawFallback` (3001); métodos privados do refactor `AskWithFormatRetryAsync` e `CreateRequest` (`3206913`); `ArgumentException` da guarda de `BrainException` com `paramName: "kind"`; `BrainException.InvalidOutput` e `TutorReply.RawFallback` com `ArgumentNullException` para argumentos nulos; o `FakeTutorBrain` sem resposta na fila lança `InvalidOperationException` com mensagem que diz o que falta.
- **E6 — Esqueleto da T7.** Para não gerar CS9113 (parâmetro do construtor primário não lido), o `SendAsync` do esqueleto citava os campos na mensagem da `NotImplementedException`. Isso foi substituído no Green (`24106f2`).
- **E7 — `Directory.Packages.props`.** Com a mudança da T1, o `ItemGroup Label="Infrastructure"` ficou vazio e foi removido.

### 2026-10-17 — usuário (sugestões da revisão de código)

Aplicar as sugestões S2 e S3 de `tasks/US-003/review.md` (Rodada 1) antes da documentação:
- **S2** — `ConversationService.cs:18`: usar uma forma só para as dependências da classe (sem o campo `_logger` que só copia o parâmetro do construtor primário).
- **S3** — guardas de nulo fora do plano: um teste por guarda do contrato público (no mínimo `BrainException.InvalidOutput` e `TutorReply.RawFallback`); as guardas que ficarem sem teste são registradas aqui como defensivas.
