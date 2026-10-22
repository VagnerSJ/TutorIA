# Plano — US-007 Troca de cérebro por configuração e fallback

Status: Aguardando aprovação
Branch: feature/US-007_troca-cerebro-fallback
Spec: specs/sprints/SPRINT_1.md#us-007
Regras: RN-BRAIN08 (da US), RN-BRAIN07 e RN-BRAIN04 (sem mudança), RN-BRAIN01; ARCHITECTURE §2 (R1–R4), §3, §4, §6.1, §6.3, §9; ADR-006, ADR-008, ADR-009, ADR-010 (Proposto, novo)

## 1. Objetivo

Trocar o seletor provisório de `ITutorBrain` pela `BrainFactory` (escolha por `Brain:Provider`) e ligar o cérebro reserva: com `Brain:FallbackProvider` configurado, falhas `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated` do primário mandam o turno para o reserva e o console mostra `Switched to <reserva> (<motivo>)` antes da resposta; sem reserva, a falha aparece como hoje e a sessão continua.

## 2. Abordagem

A decisão central está no **ADR-010 (Proposto)**: `docs/adr/ADR-010-cerebro-reserva-decorator-politica-no-core-e-aviso-por-porta.md`. Resumo:

- A **política** (quais falhas trocam) é regra de negócio: mora no Core (`BrainFallbackPolicy`, R4). O **decorator** `FallbackTutorBrain` (Infrastructure) só a consulta.
- O **aviso** chega à tela por uma porta do Core, `IBrainSwitchNotifier`, que o console implementa (R2). `ConversationService` e `TutorReply` não mudam.
- A `BrainFactory` (Infrastructure) monta o primário ou o decorator; os dois cérebros concretos também ficam registrados com chave (útil à US-009).
- Alternativas descartadas (detalhe no ADR): fallback dentro do `ConversationService`; aviso como campo de `TutorReply`; evento do decorator assinado pelo console (viola R2); política dentro do decorator (viola R4); reserva "pegajoso" na sessão (contraria "para aquele turno"); qualquer `BrainException` aciona o reserva.

### 2.1 Configuração e validação (Core)

`BrainOptions` ganha `public string FallbackProvider { get; set; } = string.Empty;` (seção `Brain`, chave `FallbackProvider`).

`BrainOptionsValidator` passa a coletar falhas (uma por chave inválida) em vez de devolver na primeira. As mensagens existentes de `Provider` não mudam (os testes atuais continuam como estão). Novas regras para `FallbackProvider`, avaliadas depois de `Provider`:

| Condição | Mensagem |
|---|---|
| vazio, `null` ou só espaços | sem falha (sem reserva; P9) |
| não é um provedor conhecido (`BrainProviders.TryGetCanonical`) | `Brain:FallbackProvider '<valor>' is invalid. Accepted values: ClaudeCode, Ollama. Leave it empty to disable the fallback.` |
| `Provider` é válido e o canônico de `FallbackProvider` é igual ao canônico de `Provider` | `Brain:FallbackProvider '<valor>' is the same as Brain:Provider '<Provider>'. Expected a different provider, or leave it empty to disable the fallback.` |

`<valor>` e `<Provider>` são os textos configurados. Se `Provider` for inválido ou vazio, só a falha de `Provider` aparece quando o reserva é válido (a comparação não é feita); se os dois forem inválidos, as duas falhas aparecem. **AP-039:** não nasce classe de Options nova, mas `Brain:FallbackProvider` é mais uma chave validada na partida; o teste de duas seções inválidas ao mesmo tempo ganha uma linha com ela (§4, C7). O dono da normalização é `BrainProviders.TryGetCanonical` (já existente); o consumidor `BrainFactory` usa o nome canônico, com teste de caixa diferente (B1, B3).

`appsettings.json` ganha `"FallbackProvider": ""` logo depois de `"Provider": "ClaudeCode"` (P1). Motivo: a DoD D6 da spec (`Brain__Provider=Ollama`) quebraria se o arquivo entregasse `"Ollama"` (reserva igual ao primário é configuração inválida). Quem quer o reserva escreve `"Ollama"` no `appsettings` ou usa `Brain__FallbackProvider`.

### 2.2 Política, aviso e porta (Core)

```csharp
public static class BrainFallbackPolicy
{
    public static bool ShouldFallBack(BrainErrorKind kind);   // UsageLimitReached, Timeout, CliNotFound, NotAuthenticated => true
}
public sealed record BrainSwitchNotice(string FromProvider, string ToProvider, BrainErrorKind Reason);
public interface IBrainSwitchNotifier
{
    Task NotifyAsync(BrainSwitchNotice notice, CancellationToken ct);
}
```

`Unknown` e `InvalidOutput` retornam falso. A RN-BRAIN08 vence a prosa da ARCHITECTURE §6.3 (que só cita `UsageLimitReached`, `CliNotFound` e `NotAuthenticated`); o texto novo está na §2.8 (P2).

### 2.3 `FallbackTutorBrain` (Infrastructure)

```csharp
public sealed class FallbackTutorBrain(
    ITutorBrain primary, ITutorBrain secondary, IBrainSwitchNotifier notifier, ILogger<FallbackTutorBrain> logger) : ITutorBrain
{
    public string Name => primary.Name;   // P8: a configuração escolhida; o aviso diz quando o reserva respondeu
    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct);
}
```

Comportamento de `SendAsync`:

1. Chama `primary.SendAsync(request, ct)`; resposta normal volta como está (sem aviso, sem chamar o reserva).
2. `catch (BrainException exception) when (!exception.IsInvalidOutput && BrainFallbackPolicy.ShouldFallBack(exception.Kind))`: loga `FallbackLog.SwitchedToSecondary` (EventId 2003, Warning, `Primary`, `Secondary`, `ErrorKind`; nunca texto do aluno, prompt ou resposta), chama `notifier.NotifyAsync(new BrainSwitchNotice(primary.Name, secondary.Name, exception.Kind), ct)` e então `secondary.SendAsync(request, ct)` com o **mesmo** `BrainRequest` (RN-BRAIN01: o histórico é da aplicação, o reserva recebe a janela completa).
3. Qualquer outra exceção (`BrainException` fora da política, `InvalidOutput`, `OperationCanceledException`, erro inesperado) propaga sem troca e sem aviso.
4. Falha do notificador propaga (o reserva não é chamado). Falha do reserva propaga como está, inclusive `InvalidOutput` (o `ConversationService` faz a retentativa da RN-BRAIN04) (P7).
5. Sem estado: a chamada seguinte tenta o primário de novo ("para aquele turno", RN-BRAIN08; `SendAsync_OnNextCall_TriesPrimaryFirstAgain`).

### 2.4 `BrainFactory` e DI (Infrastructure)

```csharp
public sealed class BrainFactory(
    IOptions<BrainOptions> options, Func<string, ITutorBrain> resolve,
    IBrainSwitchNotifier notifier, ILogger<FallbackTutorBrain> logger)
{
    public ITutorBrain Create();
}
```

`Create()`: valida `options.Value` com `new BrainOptionsValidator()` (mesmas mensagens do Core); se falhar, lança `InvalidOperationException` com as falhas juntas (depois do `ValidateOnStart` isso não acontece no app, mas o DI sem a partida do host, como nos testes, ganha mensagem clara; P10). Normaliza `Provider` e `FallbackProvider` com `BrainProviders.TryGetCanonical`; chama `resolve(<canônico>)`; sem reserva (vazio ou espaços) devolve o primário **como está** (mesma instância); com reserva devolve `new FallbackTutorBrain(primary, resolve(<reserva canônico>), notifier, logger)`. Efeito: um `Brain:Provider` vazio deixa de cair em silêncio no Claude Code (P10).

Registro em `AddEnglishTutorInfrastructure` (substitui o bloco "Provisional selector"):

```csharp
services.TryAddKeyedSingleton<ITutorBrain>(BrainProviders.ClaudeCode, (sp, _) => sp.GetRequiredService<ClaudeCodeTutorBrain>());
services.TryAddKeyedSingleton<ITutorBrain>(BrainProviders.Ollama, (sp, _) => sp.GetRequiredService<OllamaTutorBrain>());
services.TryAddSingleton<IBrainSwitchNotifier, SilentBrainSwitchNotifier>();   // internal; o console registra o dele antes
services.TryAddSingleton(sp => new BrainFactory(sp.GetRequiredService<IOptions<BrainOptions>>(),
    name => sp.GetRequiredKeyedService<ITutorBrain>(name), sp.GetRequiredService<IBrainSwitchNotifier>(),
    sp.GetRequiredService<ILogger<FallbackTutorBrain>>()));
services.TryAddSingleton<ITutorBrain>(sp => sp.GetRequiredService<BrainFactory>().Create());
```

O comentário do cabeçalho do arquivo (`ITutorBrain is chosen by Brain:Provider until the BrainFactory (US-007).`) passa a dizer que o `ITutorBrain` vem da `BrainFactory`. O texto `Provisional selector` desaparece de `src` e `tests` (D7 da spec). Os testes de console que registram um `ITutorBrain` em `ConfigureServices` continuam valendo (o último registro vence; a fábrica nem é resolvida).

### 2.5 Console

- `ConsoleText.BrainSwitched(BrainSwitchNotice notice)` devolve `Switched to <ToProvider> (<motivo>)`; motivos por tipo: `UsageLimitReached` → `usage limit reached`; `Timeout` → `timed out`; `CliNotFound` → `CLI not found`; `NotAuthenticated` → `not authenticated`; outro tipo → `ArgumentOutOfRangeException` (a política nunca o produz) (P5).
- `ConsoleBrainSwitchNotifier(TextWriter output) : IBrainSwitchNotifier` escreve essa linha na **saída padrão** com `WriteLineAsync(line.AsMemory(), ct)`, igual aos avisos locais da RN-CONV05 (P4). Como a escrita acontece durante `SendAsync`, a linha sai antes de `Tutor: <reply>`.
- `EnglishTutorApp.BuildHost`: `builder.Services.TryAddSingleton<IBrainSwitchNotifier>(new ConsoleBrainSwitchNotifier(options.Output));` **antes** de `AddEnglishTutorInfrastructure` (o `TryAdd` da Infrastructure não substitui; P12).
- `RunSessionAsync`, `ChatSession`, `ConversationInputHandler` e o cabeçalho não mudam: a falha sem reserva chega como `BrainException` e aparece na saída de erro com as duas linhas da US-005, e a sessão continua (cenário "sem fallback configurado"; mesmo canal e mesmo código de saída, AP-024). O cabeçalho já usa o nome canônico de `Brain:Provider` (cenário "escolha por configuração").

### 2.6 Efeito sobre o Claude Code com `--resume` (P13)

Depois de um turno respondido pelo reserva, o pedido seguinte ao Claude Code traz duas mensagens a mais do que a sessão conhece; o ADR-007 manda abrir sessão nova com a janela completa. Nada a implementar; coberto por `ClaudeCodeTutorBrainTests.SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript`.

### 2.7 Alterações na spec (aplicadas pelo orquestrador no commit `docs(ai): update spec for US-007`, antes da T1)

Em `specs/sprints/SPRINT_1.md`, seção `## US-007 — Troca de cérebro por configuração e fallback`. Cada trecho é identificado pelo texto antigo (AP-023).

(a) Regras. Texto antigo: `**Regras:** RN-BRAIN08.` Texto novo: `**Regras:** RN-BRAIN07, RN-BRAIN08; ARCHITECTURE §6.1, §6.3.`

(b) Cenários novos (reutilizam os critérios observáveis do projeto: saída de erro, `ExitCodes.InvalidConfiguration`, sessão continua, AP-024). Texto antigo (última linha do cenário "sem fallback configurado"):

```
  Então vejo a mensagem de limite de uso e continuo na sessão
```

Texto novo (a linha antiga seguida dos cenários novos, antes de `**DoD extra**`):

```
  Então vejo a mensagem de limite de uso e continuo na sessão

Esquema do Cenário: fallback por outras falhas do primário
  Dado Provider "ClaudeCode" e FallbackProvider "Ollama"
  E o Claude Code retorna <falha>
  Quando envio uma frase
  Então a resposta vem do Ollama
  E vejo o aviso "Switched to Ollama (<motivo>)"

  Exemplos:
    | falha            | motivo            |
    | Timeout          | timed out         |
    | CliNotFound      | CLI not found     |
    | NotAuthenticated | not authenticated |

Cenário: falha que não aciona o reserva
  Dado Provider "ClaudeCode" e FallbackProvider "Ollama"
  E o Claude Code retorna uma falha desconhecida (Unknown)
  Quando envio uma frase
  Então o Ollama não é chamado
  E vejo a mensagem da falha na saída de erro e continuo na sessão

Cenário: reserva igual ao primário
  Dado Provider "ClaudeCode" e FallbackProvider "claudecode"
  Quando a aplicação inicia
  Então a aplicação termina com código 2 e a saída de erro explica que o reserva deve ser outro provedor
  E nenhuma sessão é aberta
```

(c) DoD extra. Depois da linha antiga `| D7 | `git grep -n -F "Provisional selector" -- src tests` | nenhuma linha: o seletor provisório de `ITutorBrain` por `Brain:Provider` (US-006, em `InfrastructureServiceCollectionExtensions`) foi substituído pela `BrainFactory` |`, acrescentar:

```
| D8 | No Windows PowerShell 5.1, com o Ollama rodando e o modelo baixado: `$env:Brain__ClaudeCode__ExecutablePath="claude-missing-us007"; $env:Brain__FallbackProvider="Ollama"; "I have worked with C# since five years", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__ClaudeCode__ExecutablePath, Env:Brain__FallbackProvider` | a linha `Switched to Ollama (CLI not found)` seguida de uma linha `Tutor: <reply>`; `exit=0` |
| D9 | No Windows PowerShell 5.1: `$env:Brain__ClaudeCode__ExecutablePath="claude-missing-us007"; "I have worked with C# since five years", "/quit" \| dotnet run --project src/EnglishTutor.Console 2>&1; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__ClaudeCode__ExecutablePath` | a mensagem `The tutor could not answer: Claude Code CLI not found`; nenhuma linha `Switched to`; `exit=0` |
```

Nenhuma regra `RN-*` muda (a RN-BRAIN08 já lista os quatro tipos e o aviso). Conferência do `Dado` do cenário "fallback por limite de uso" no `SPRINT_1.md` (AP-036): Claude Code como primário, Ollama como reserva e o Claude Code retornando `UsageLimitReached`; é o que os testes C2 e F3 montam.

### 2.8 Alterações em `ARCHITECTURE.md` (commit `docs(ai): update architecture for US-007`, antes da T1, junto com o ADR-010 em "Aceito")

Cada trecho identificado pelo texto antigo.

1. §3, Core/Brain. Antigo: `BrainException, BrainErrorKind, BrainOptions, BrainOptionsValidator, BrainProviders`. Novo: `BrainException, BrainErrorKind, BrainOptions, BrainOptionsValidator, BrainProviders,` e, na linha seguinte, alinhada: `BrainFallbackPolicy, BrainSwitchNotice, IBrainSwitchNotifier`.
2. §3, Infrastructure/Brain. Antigo: `TutorReplyParser, BrainFactory, FallbackTutorBrain`. Novo: `TutorReplyParser, BrainFactory, FallbackTutorBrain, FallbackLog, SilentBrainSwitchNotifier`.
3. §3, Console. Antigo: `Ui/ (ChatSession, ConversationInputHandler)`. Novo: `Ui/ (ChatSession, ConversationInputHandler, ConsoleBrainSwitchNotifier)`.
4. §4, depois da linha antiga `public interface ITutorPromptSource { Task<string> GetSystemPromptAsync(CancellationToken ct); }   // conteúdo de prompts/tutor.md`, acrescentar:
   ```
   public interface IBrainSwitchNotifier { Task NotifyAsync(BrainSwitchNotice notice, CancellationToken ct); }   // aviso de troca para o reserva (RN-BRAIN08, ADR-010)
   public sealed record BrainSwitchNotice(string FromProvider, string ToProvider, BrainErrorKind Reason);
   ```
5. §6.1, depois da linha antiga `Validação das Options na inicialização (`ValidateOnStart`).`, acrescentar: `` `Brain:FallbackProvider` vazio (padrão do `appsettings.json`) desliga o reserva; preenchido, deve ser um provedor conhecido e diferente de `Brain:Provider` (o exemplo acima mostra a configuração com reserva). ``
6. §6.3. Antigo: ``- `UsageLimitReached` (e, se configurado, `CliNotFound`/`NotAuthenticated`) aciona o `FallbackTutorBrain`.`` Novo: ``- Com `Brain:FallbackProvider` configurado, `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated` acionam o `FallbackTutorBrain` (RN-BRAIN08; política em `BrainFallbackPolicy`, no Core). `Unknown` e `InvalidOutput` não acionam (ADR-010).``
7. Seção nova `### 6.5 Reserva (fallback)`, inserida antes da linha `## 7. Voz`:
   ```
   ### 6.5 Reserva (fallback)

   - A `BrainFactory` (Infrastructure) lê `Brain:Provider` e `Brain:FallbackProvider` e entrega o `ITutorBrain`: o cérebro primário, ou o `FallbackTutorBrain` que o envolve quando há reserva. Cada cérebro concreto também é registrado com chave (o nome do provedor).
   - O `FallbackTutorBrain` chama o primário; se ele lançar `BrainException` cujo `Kind` a `BrainFallbackPolicy` manda trocar (RN-BRAIN08), avisa o `IBrainSwitchNotifier` e envia o mesmo `BrainRequest` ao reserva, só naquele turno. Outras falhas, `InvalidOutput` (retentativa do `ConversationService`, ADR-006) e cancelamento passam adiante sem troca.
   - O console implementa o aviso (`Switched to <reserva> (<motivo>)`, saída padrão, antes da linha `Tutor:`) (ADR-010).
   ```

**Sincronia (AP-005).** Grep em `CLAUDE.md` e `.claude/` por `FallbackTutorBrain`, `BrainFactory`, `FallbackProvider`, `RN-BRAIN08`, `Provisional selector` e `aciona`: nenhuma ocorrência. Nenhuma linha de `CLAUDE.md` nem de skill repete as regras acima; nada a atualizar fora de `ARCHITECTURE.md`.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `src/EnglishTutor.Core/Brain/BrainFallbackPolicy.cs` | Quais `BrainErrorKind` mandam trocar (RN-BRAIN08) |
| Criar | `src/EnglishTutor.Core/Brain/BrainSwitchNotice.cs` | Registro do aviso de troca |
| Criar | `src/EnglishTutor.Core/Brain/IBrainSwitchNotifier.cs` | Porta do aviso |
| Alterar | `src/EnglishTutor.Core/Brain/BrainOptions.cs` | `FallbackProvider` |
| Alterar | `src/EnglishTutor.Core/Brain/BrainOptionsValidator.cs` | Valida `FallbackProvider`, coleta falhas |
| Criar | `src/EnglishTutor.Infrastructure/Brain/FallbackTutorBrain.cs` | Decorator primário → reserva |
| Criar | `src/EnglishTutor.Infrastructure/Brain/FallbackLog.cs` | `[LoggerMessage]` EventId 2003 |
| Criar | `src/EnglishTutor.Infrastructure/Brain/BrainFactory.cs` | Monta primário ou decorator |
| Criar | `src/EnglishTutor.Infrastructure/Brain/SilentBrainSwitchNotifier.cs` | `internal`; notificador padrão sem saída |
| Alterar | `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | Remove o seletor provisório; registra chaves, fábrica, notificador padrão |
| Criar | `src/EnglishTutor.Console/Ui/ConsoleBrainSwitchNotifier.cs` | Escreve o aviso na saída padrão |
| Alterar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | `BrainSwitched` |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Registra o notificador do console antes da Infrastructure |
| Alterar | `src/EnglishTutor.Console/appsettings.json` | `"FallbackProvider": ""` |
| Alterar | `tests/EnglishTutor.Core.Tests/Brain/BrainOptionsValidatorTests.cs` | V1–V6 |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/BrainFallbackPolicyTests.cs` | P1–P3 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/FallbackTutorBrainTests.cs` | F1–F14 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/FallbackTutorBrainConversationTests.cs` | F15 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/BrainFactoryTests.cs` | B1–B6 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/StubTutorBrain.cs`, `RecordingBrainSwitchNotifier.cs` | Cérebro falso (fila de respostas, nome fixo por teste) e notificador que registra avisos e a ordem das chamadas |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs` | DI1–DI5 (um teste renomeado, §4) |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | C1–C7 |
| Criar | `tests/EnglishTutor.Console.Tests/Ui/ConsoleTextTests.cs`, `ConsoleBrainSwitchNotifierTests.cs` | C8–C11 |
| Criar | `tests/EnglishTutor.Console.Tests/AppSettingsTests.cs` | C12 |
| Criar | `tests/EnglishTutor.Console.Tests/Support/FixedResultProcessRunner.cs` | `IProcessRunner` falso que devolve um `ProcessResult` fixo e conta chamadas |
| Criar | `docs/adr/ADR-010-cerebro-reserva-decorator-politica-no-core-e-aviso-por-porta.md` | Status Proposto (já criado com este plano) |
| Alterar | `specs/sprints/SPRINT_1.md`, `ARCHITECTURE.md` | Só pelo orquestrador, §2.7 e §2.8, antes da T1 |

Nenhuma tarefa altera `specs/`, `ARCHITECTURE.md` ou ADR; `CHANGELOG` e `docs/us/US-007.md` são do Documentador.

## 4. Mapa cenário BDD → testes

Fakes: o Core.Tests usa `FakeTutorBrain`; o Infrastructure.Tests usa `StubTutorBrain` e `RecordingBrainSwitchNotifier` (novos); o Console.Tests usa `FixedResultProcessRunner` (novo) com o `ClaudeCodeTutorBrain` **real** e o `ScriptedChatClient` (já existente) com o `OllamaTutorBrain` **real**, sem substituir `ITutorBrain`; o processo do Claude Code usa `WorkingDirectory` numa pasta temporária do teste, como `RunAsync_WhenClaudeExecutableIsNotFound_ShowsCliNotFoundInstructionAndContinues`. O `ProcessResult` do limite de uso copia a forma de `Fixtures/ClaudeCode/errors/usage-limit-synthetic-1.stdout.txt` (`is_error: true`, `result` com `usage limit reached`, exit code do `.exit-code.txt`). Textos únicos por teste (AP-017): rótulos `t1`, `t2`; a resposta rejeitada difere da aceita onde há retentativa (F15).

| Cenário | Projeto de teste | Classe | Método | Tipo |
|---|---|---|---|---|
| escolha por configuração | Console.Tests | `EnglishTutorAppTests` | C1 `RunAsync_WhenProviderIsOllama_ShowsOllamaInHeader` (`Brain: Ollama \| Mode: free-conversation`; **nasce verde**: o cabeçalho já usa o nome canônico, US-006) | Componente |
| escolha por configuração | Infrastructure.Tests | `BrainFactoryTests` | B1 `Create_WithoutFallbackProvider_ReturnsThePrimaryBrain` (Theory: `ClaudeCode`, `claudecode`, `ollama`; o `resolve` recebe o nome canônico e a mesma instância volta; AP-039) | Unidade |
| escolha por configuração | Infrastructure.Tests | `BrainFactoryTests` | B2 `Create_WithBlankFallbackProvider_ReturnsThePrimaryBrain` (Theory: `""`, `"   "`; `resolve` chamado uma vez) | Unidade |
| escolha por configuração | Infrastructure.Tests | `BrainFactoryTests` | B4 `Create_WhenProviderIsInvalid_ThrowsExplainingAcceptedProviders` (Theory: `""`, `"Inexistente"`; a mensagem tem `Accepted values: ClaudeCode, Ollama.`) | Unidade |
| (apoio) validação de `FallbackProvider` na fábrica (P10) | Infrastructure.Tests | `BrainFactoryTests` | B5 `Create_WhenFallbackProviderIsUnknown_ThrowsNamingValueAndAcceptedProviders` (`Provider` `ClaudeCode`, reserva `Inexistente`; `InvalidOperationException` com `Brain:FallbackProvider 'Inexistente' is invalid` e `Accepted values: ClaudeCode, Ollama.`; papéis trocados em relação a B4, que invalida só o `Provider`) | Unidade |
| escolha por configuração | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | DI1 `AddEnglishTutorInfrastructure_Always_RegistersFilePromptSource` (renomeado; ver abaixo) | Unidade |
| escolha por configuração | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | `AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` e `AddEnglishTutorInfrastructure_WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain` (existentes, sem mudança; **continuam verdes** com a fábrica) | Unidade |
| escolha por configuração | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | DI5 `AddEnglishTutorInfrastructure_Always_RegistersEachBrainUnderItsProviderKey` (`GetRequiredKeyedService<ITutorBrain>("ClaudeCode")` é `ClaudeCodeTutorBrain`, `"Ollama"` é `OllamaTutorBrain`) | Unidade |
| fallback por limite de uso | Console.Tests | `EnglishTutorAppTests` | C2 `RunAsync_WhenPrimaryHitsUsageLimit_AnswersFromFallbackAfterShowingSwitchNotice` (`FixedResultProcessRunner` devolve o `ProcessResult` do limite de uso; `ScriptedChatClient` responde `t1`; a saída tem a linha `Switched to Ollama (usage limit reached)` **antes** da linha `Tutor: Reply t1?`; saída de erro vazia; `exit 0`; vermelho na T5, depois que a T4 liga o decorator) | Componente |
| fallback por limite de uso | Infrastructure.Tests | `FallbackTutorBrainTests` | F3 `SendAsync_WhenPrimaryFailsWithFallbackKind_ReturnsSecondaryReply` (Theory: os 4 tipos) | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `FallbackTutorBrainTests` | F4 `SendAsync_WhenPrimaryFailsWithFallbackKind_SendsTheSameRequestToSecondary` (mesma instância e mesmas mensagens do pedido) | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `FallbackTutorBrainTests` | F5 `SendAsync_WhenPrimaryFailsWithFallbackKind_NotifiesProvidersAndKindBeforeCallingSecondary` (o notificador registra `ClaudeCode t1`/`Ollama t1`/`UsageLimitReached` e, no instante do aviso, o reserva tem 0 chamadas) | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `FallbackTutorBrainTests` | F12 `SendAsync_WhenFallingBack_PassesCallerTokenToNotifierAndSecondary` | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `FallbackTutorBrainTests` | F14 `SendAsync_WhenFallingBack_LogsWarningWithProvidersAndKind` (nível Warning, EventId 2003, a mensagem tem os dois nomes e `UsageLimitReached`; não tem o texto do aluno) | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `BrainFactoryTests` | B3 `Create_WithFallbackProvider_ReturnsBrainThatFallsBackToTheSecond` (Theory: `ClaudeCode`+`Ollama`, `Ollama`+`ClaudeCode`, `claudecode`+`OLLAMA`; o primário lança `UsageLimitReached`, a resposta vem do reserva, o aviso traz os nomes canônicos; pega papéis trocados e caixa) | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | DI2 `AddEnglishTutorInfrastructure_WhenFallbackProviderIsSet_ResolvesFallbackBrainNamedAfterPrimary` | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | DI3 `AddEnglishTutorInfrastructure_WhenNotifierIsAlreadyRegistered_KeepsIt` (observa o efeito, não o tipo resolvido: antes de `AddEnglishTutorInfrastructure` o teste registra um `RecordingBrainSwitchNotifier` e dois `ITutorBrain` com chave, um `StubTutorBrain` `ClaudeCode` que lança `UsageLimitReached` e um `StubTutorBrain` `Ollama` que responde `t1`, com `Brain:Provider`=`ClaudeCode`, `Brain:FallbackProvider`=`Ollama` e `Brain:ClaudeCode:ExecutablePath` inexistente para nenhum processo real rodar; resolve `ITutorBrain`, chama `SendAsync` e espera a resposta `t1` e exatamente **um** aviso no notificador registrado. Antes da T4 falha: o seletor provisório ignora as chaves e o reserva, e o notificador não recebe aviso. Mutação depois do Green: trocar o `TryAddSingleton` do notificador silencioso por `AddSingleton` leva o aviso ao silencioso e quebra o DI3) | Unidade |
| fallback por limite de uso | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | DI4 `AddEnglishTutorInfrastructure_WhenNoNotifierIsRegistered_ProvidesSilentDefault` | Unidade |
| fallback por limite de uso | Core.Tests | `BrainFallbackPolicyTests` | P1 `ShouldFallBack_ForKindThatRn08Lists_ReturnsTrue` (Theory: `UsageLimitReached`, `Timeout`, `CliNotFound`, `NotAuthenticated`) | Unidade |
| fallback por limite de uso | Console.Tests | `ConsoleTextTests` | C8 `BrainSwitched_ForEachFallbackKind_NamesTargetAndReason` (Theory: 4 tipos → `Switched to Ollama (usage limit reached)`, `(timed out)`, `(CLI not found)`, `(not authenticated)`) | Unidade |
| fallback por limite de uso | Console.Tests | `ConsoleTextTests` | C9 `BrainSwitched_WhenTargetIsClaudeCode_NamesTheTarget` (`Switched to ClaudeCode (usage limit reached)`) | Unidade |
| fallback por limite de uso | Console.Tests | `ConsoleTextTests` | C10 `BrainSwitched_ForKindThatNeverFallsBack_ThrowsArgumentOutOfRange` (Theory: `Unknown`, `InvalidOutput`) | Unidade |
| fallback por limite de uso | Console.Tests | `ConsoleBrainSwitchNotifierTests` | C11 `NotifyAsync_Always_WritesSwitchLineToOutput` (a linha é exatamente a de `ConsoleText.BrainSwitched`) | Unidade |
| fallback por outras falhas do primário (novo) | Console.Tests | `EnglishTutorAppTests` | C3 `RunAsync_WhenClaudeExecutableIsNotFoundAndFallbackIsSet_AnswersFromFallbackWithCliNotFoundNotice` (`MissingExecutableProcessRunner`; `Switched to Ollama (CLI not found)`; `Tutor: Reply t1?`; vermelho na T5) | Componente |
| fallback por outras falhas do primário (novo) | Infrastructure.Tests | `FallbackTutorBrainTests` | F3 (Theory dos 4 tipos, acima: `Timeout`, `CliNotFound` e `NotAuthenticated` são as linhas do Esquema) | Unidade |
| sem fallback configurado | Console.Tests | `EnglishTutorAppTests` | C4 `RunAsync_WhenFallbackProviderIsEmptyAndPrimaryHitsUsageLimit_ShowsLimitMessageAndContinuesSession` (duas frases; saída de erro: `The tutor could not answer: Claude Code usage limit reached` e `You can send a sentence again or type /quit to exit.`; a saída não tem `Switched to`; `FixedResultProcessRunner.Calls == 2` prova a sessão continuando; `ScriptedChatClient` com 0 chamadas; `exit 0`; **nasce verde** na T4) | Componente |
| sem fallback configurado | Infrastructure.Tests | `BrainFactoryTests` | B2 (acima: reserva vazio devolve o primário, sem decorator) | Unidade |
| sem fallback configurado | Core.Tests | `BrainOptionsValidatorTests` | V1 `Validate_WhenFallbackProviderIsEmptyOrBlank_Succeeds` (Theory: `""`, `"   "`; nasce verde) | Unidade |
| falha que não aciona o reserva (novo) | Console.Tests | `EnglishTutorAppTests` | C5 `RunAsync_WhenPrimaryFailsWithUnknown_DoesNotSwitchAndShowsTheFailure` (reserva `Ollama` configurado; o runner devolve `exit 1` com `result` `boom t1`; erro `The tutor could not answer: Claude Code failed (exit code 1): boom t1`; `ScriptedChatClient` 0 chamadas; sem `Switched to`; **nasce verde** na T4) | Componente |
| falha que não aciona o reserva (novo) | Core.Tests | `BrainFallbackPolicyTests` | P2 `ShouldFallBack_ForKindOutsideRn08_ReturnsFalse` (Theory: `Unknown`, `InvalidOutput`) | Unidade |
| falha que não aciona o reserva (novo) | Core.Tests | `BrainFallbackPolicyTests` | P3 `BrainErrorKind_HasOnlyTheKindsTheTableDecides` (`Enum.GetValues<BrainErrorKind>()` tem 6 valores; um tipo novo obriga a decidir uma linha de P1/P2) | Unidade |
| falha que não aciona o reserva (novo) | Infrastructure.Tests | `FallbackTutorBrainTests` | F6 `SendAsync_WhenPrimaryFailsWithKindOutsidePolicy_PropagatesWithoutSecondaryCallOrNotice` (Theory: `Unknown`, `InvalidOutput`) | Unidade |
| falha que não aciona o reserva (novo) | Infrastructure.Tests | `FallbackTutorBrainTests` | F7 `SendAsync_WhenPrimaryThrowsNonBrainException_PropagatesWithoutFallback` | Unidade |
| falha que não aciona o reserva (novo) | Infrastructure.Tests | `FallbackTutorBrainTests` | F8 `SendAsync_WhenCallerCancels_PropagatesCancellationWithoutFallback` | Unidade |
| reserva igual ao primário (novo) | Console.Tests | `EnglishTutorAppTests` | C6 `RunAsync_WhenFallbackProviderEqualsProvider_ExitsWithInvalidConfigurationBeforePrompt` (`Provider` `ClaudeCode`, reserva `claudecode`; `ExitCodes.InvalidConfiguration`; o erro tem `Invalid configuration:` e `is the same as Brain:Provider 'ClaudeCode'`; saída vazia; vermelho até o Green da T1) | Componente |
| reserva igual ao primário (novo) | Core.Tests | `BrainOptionsValidatorTests` | V4 `Validate_WhenFallbackProviderEqualsProviderIgnoringCase_FailsAskingForADifferentProvider` (Theory: `ClaudeCode`/`claudecode`, `Ollama`/`OLLAMA`) | Unidade |
| reserva igual ao primário (novo) | Infrastructure.Tests | `BrainFactoryTests` | B6 `Create_WhenFallbackProviderEqualsProvider_ThrowsExplainingDifferentProviderIsExpected` | Unidade |
| (apoio) validação de `FallbackProvider` na partida (AP-039) | Core.Tests | `BrainOptionsValidatorTests` | V2 `Validate_WhenFallbackProviderIsAccepted_Succeeds` (Theory: `ClaudeCode`+`ollama`, `Ollama`+`CLAUDECODE`; nasce verde) | Unidade |
| (apoio) validação de `FallbackProvider` na partida (AP-039) | Core.Tests | `BrainOptionsValidatorTests` | V3 `Validate_WhenFallbackProviderIsUnknown_FailsNamingValueAndAcceptedProviders` | Unidade |
| (apoio) validação de `FallbackProvider` na partida (AP-039) | Core.Tests | `BrainOptionsValidatorTests` | V5 `Validate_WhenProviderAndFallbackProviderAreBothInvalid_ReportsBothFailures` | Unidade |
| (apoio) validação de `FallbackProvider` na partida (AP-039) | Core.Tests | `BrainOptionsValidatorTests` | V6 `Validate_WhenProviderIsInvalidAndFallbackProviderIsValid_ReportsOnlyTheProviderFailure` (Theory: `Inexistente`+`Ollama`, `""`+`ClaudeCode`; papéis trocados em relação a V3; nasce verde) | Unidade |
| (apoio) validação de `FallbackProvider` na partida (AP-039) | Console.Tests | `EnglishTutorAppTests` | C7 `RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (**linha nova** no Theory existente: `ClaudeCode`, `Brain:FallbackProvider`, `Inexistente`, falha `Brain:FallbackProvider 'Inexistente' is invalid. Accepted values: ClaudeCode, Ollama.`, junto com `Conversation:MaxTurnsInWindow` = 0; vermelho até o Green da T1) | Componente |
| (apoio) padrão do `appsettings.json` (P1) | Console.Tests | `AppSettingsTests` | C12 `AppSettings_WhenShipped_KeepsFallbackProviderEmpty` | Unidade |
| (apoio) decorator sem estado e papéis | Infrastructure.Tests | `FallbackTutorBrainTests` | F1 `Name_Always_IsThePrimaryName`; F2 `SendAsync_WhenPrimaryReplies_ReturnsItsReplyWithoutNoticeOrSecondaryCall`; F9 `SendAsync_WhenSecondaryFails_PropagatesTheSecondaryException` (Theory: `Unknown`, `Timeout`; a exceção é a do reserva, depois do aviso); F10 `SendAsync_WhenSecondaryAnswerIsInvalid_PropagatesInvalidOutputForTheServiceRetry` (rejeição e `RawText` preservados); F11 `SendAsync_OnNextCall_TriesPrimaryFirstAgain`; F13 `SendAsync_WhenNotifierFails_PropagatesWithoutCallingSecondary` | Unidade |
| (apoio) retentativa depois da troca (limite L1) | Infrastructure.Tests | `FallbackTutorBrainConversationTests` | F15 `SendAsync_WhenFallbackAnswerIsInvalid_ServiceRetryStartsAgainFromPrimary` (`ConversationService` real sobre o decorator: o primário está no limite nas duas chamadas; o reserva responde `InvalidOutput` com `bad t1` e depois uma resposta válida; 2 avisos, 2 chamadas ao primário, 2 ao reserva; a segunda chamada ao reserva carrega a resposta rejeitada `bad t1` e a correção) | Unidade |

**Renomeação (AP-035, AP-050).** O teste `AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource` deixa de valer: sem `Brain:Provider`, a fábrica passa a lançar (P10), e o resolve do Claude Code já tem o teste `..._WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain`. Ele vira DI1 `AddEnglishTutorInfrastructure_Always_RegistersFilePromptSource`, só com o `ITutorPromptSource`. Inventário do Grep pelo nome antigo (`src`, `tests`, `prompts`, `docs`), rodado antes de fechar a tabela: uma ocorrência, em `tests/EnglishTutor.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs` (linha 18, a declaração do método). As demais ocorrências estão em `tasks/US-004/plan.md` e `tasks/US-006/plan.md`, histórico que não muda. Variações sintáticas: não há comentário nem asserção com o nome (só o `public void`). Comando da DoD (D12): `git grep -n -F "Always_RegistersClaudeCodeBrainAndFilePromptSource" -- src tests prompts docs` acha a linha 18 **antes** da T4 e nenhuma depois.

**Placeholders (AP-054).** `<motivo>`: um caso por ramo da derivação (C8: 4 linhas; C10: o ramo de erro). `<ToProvider>`: C8 (`Ollama`) e C9 (`ClaudeCode`). Não há número nem cultura.

**Condicionais da §2 (AP-047).** "Sem reserva (vazio ou espaços) devolve o primário": B2. "Reserva igual ao primário, sem diferenciar maiúsculas": V4, B6, C6. "Se `Provider` é inválido, só a falha de `Provider`": V6 (e V5 com os dois inválidos). "Falha do notificador": F13. "`InvalidOutput` do reserva propaga": F10. "Tentar o primário de novo": F11, F15. "Notificador do console antes do padrão": DI3 (a Infrastructure respeita um já registrado) e C2 (o console registra o dele). As duas fontes sempre examinadas (`Provider` e `FallbackProvider` no validador): só `FallbackProvider` inválido (V3, B5), só `Provider` inválido (V6, B4), os dois (V5).

## 5. Tarefas (ordem de execução TDD)

Convenções: o Red de cada tarefa lista o que **nasce verde** (declarado) e o que falha. Antes de cada commit `feat`, o Executor preenche a linha `Escolhas de forma`.

- [ ] **T1 — Configuração do reserva no Core**
  Red: V1–V6 de `BrainOptionsValidatorTests`, C6 e a linha nova C7 de `EnglishTutorAppTests`, mais a propriedade `BrainOptions.FallbackProvider` só para compilar. Falham: V3, V4, V5, C6 (a partida aceita a configuração) e C7 (a falha de `FallbackProvider` não aparece). Nascem verdes: V1, V2 e V6 (o validador de hoje ignora o campo e já devolve só a falha de `Provider`). Mutação depois do Green para V6: avaliar `FallbackProvider` mesmo com `Provider` inválido faz V6 falhar.
  Green: `BrainOptionsValidator` coleta falhas (Provider, depois FallbackProvider, §2.1) com as mensagens exatas.
  Refactor: constantes das mensagens agrupadas, sem mudar os textos de `Provider`.
  Commit: `test(core): cover Brain:FallbackProvider validation` + `feat(core): validate Brain:FallbackProvider`.
  Escolhas de forma: nenhuma | §12 em `<hash>`

- [ ] **T2 — Política e porta do aviso no Core**
  Red: P1, P2, P3 de `BrainFallbackPolicyTests`, com `BrainFallbackPolicy.ShouldFallBack` devolvendo `false` e `BrainSwitchNotice`/`IBrainSwitchNotifier` só como tipos. Falha: P1 (4 linhas). Nascem verdes: P2 (devolve `false` mesmo) e P3 (só conta o enum).
  Green: `ShouldFallBack` verdadeiro para os 4 tipos da RN-BRAIN08.
  Refactor: nenhum previsto.
  Commit: `test(core): cover the fallback policy` + `feat(core): add fallback policy and switch notice port`.
  Escolhas de forma: nenhuma | §12 em `<hash>`

- [ ] **T3 — `FallbackTutorBrain`**
  Red: `StubTutorBrain`, `RecordingBrainSwitchNotifier`, F1–F14 de `FallbackTutorBrainTests` e F15 de `FallbackTutorBrainConversationTests`, com `SendAsync` apenas repassando `primary.SendAsync` (sem catch). Nascem verdes: F1, F2, F6, F7, F8 (repassar já cumpre). Falham: F3, F4, F5, F9, F10, F11, F12, F13, F14, F15. Mutação depois do Green: trocar o filtro do `catch` por `catch (Exception)` quebra F7 e F8; ignorar `ShouldFallBack` quebra F6.
  Green: §2.3 e `FallbackLog` (EventId 2003).
  Refactor: nenhum previsto.
  Commit: `test(brain): cover the fallback decorator` + `feat(brain): add FallbackTutorBrain`.
  Escolhas de forma: nenhuma | §12 em `<hash>`

- [ ] **T4 — `BrainFactory`, DI e fim do seletor provisório**
  Red: B1–B6 de `BrainFactoryTests` (com `Create()` lançando `NotImplementedException`), DI1–DI5 em `InfrastructureServiceCollectionExtensionsTests` (DI1 é o teste renomeado), mais C1, C4 e C5 de `EnglishTutorAppTests` e o `FixedResultProcessRunner`. Falham: B1–B6 (inclui B5), DI2, DI3 (o aviso não chega ao notificador registrado: o seletor provisório ignora as chaves e o reserva, §4), DI4, DI5. Entre os DI, só DI1 nasce verde. Nascem verdes: DI1 (o `ITutorPromptSource` já é registrado), os dois testes de resolução existentes, C1 (o cabeçalho já usa o nome canônico), C4 (sem reserva, o seletor atual devolve o Claude Code puro) e C5 (o `Unknown` do primário propaga; o seletor atual ignora o reserva). Mutações depois do Green: C4 — a fábrica embrulhar o primário num decorator com o Ollama quando `FallbackProvider` é vazio quebra C4 e B2; C5 — `ShouldFallBack` devolver `true` para `Unknown` quebra C5 e P2; C1 — o cabeçalho mostrar sempre `ClaudeCode` quebra C1; DI3 — trocar o `TryAddSingleton` do `SilentBrainSwitchNotifier` por `AddSingleton` quebra DI3.
  Green: §2.4; a fábrica devolve o primário ou o decorator; `SilentBrainSwitchNotifier`; remove o bloco `Provisional selector` e atualiza o comentário do cabeçalho.
  Refactor: nenhum previsto.
  Commit: `test(brain): cover the brain factory` + `feat(brain): replace the provisional selector with BrainFactory`.
  Escolhas de forma: nenhuma | §12 em `<hash>`

- [ ] **T5 — Aviso no console e ligação ponta a ponta**
  Red: C8, C9, C10 de `ConsoleTextTests`, C11 de `ConsoleBrainSwitchNotifierTests`, C12 de `AppSettingsTests`, C2 e C3 de `EnglishTutorAppTests`, com `ConsoleText.BrainSwitched` e `ConsoleBrainSwitchNotifier` só como esqueleto (`BrainSwitched` devolve `string.Empty`, `NotifyAsync` não escreve) e sem o registro no `BuildHost`. Falham: C8, C9, C10 (não lança), C11, C12 (o `appsettings.json` ainda não tem a chave), C2 e C3 (a linha `Switched to` não aparece: o notificador padrão é silencioso). Nenhum teste da tarefa nasce verde. Mutação depois do Green: registrar o notificador do console **depois** de `AddEnglishTutorInfrastructure` (o `TryAdd` da Infrastructure vence) quebra C2 e C3.
  Green: §2.5; `appsettings.json` com `"FallbackProvider": ""`.
  Refactor: nenhum previsto.
  Commit: `test(console): cover the switch notice and fallback end to end` + `feat(console): show the fallback switch notice`.
  Escolhas de forma: nenhuma | §12 em `<hash>`

## 6. Pacotes NuGet

Nenhum pacote novo. (`Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions` e `Microsoft.Extensions.Logging.Abstractions` já estão referenciados na Infrastructure; o `GetRequiredKeyedService` está em `Microsoft.Extensions.DependencyInjection.Abstractions`.)

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| **L1 (limite aceito): retentativa de formato depois da troca.** Se o reserva devolver `InvalidOutput`, o `ConversationService` chama o decorator de novo; o primário é tentado outra vez e, ainda no limite, o aviso se repete no mesmo turno | Consumidores e efeito: (1) console: duas linhas `Switched to ...` no mesmo turno, ambas verdadeiras; (2) validador: não é afetado; (3) testes `LiveBrain`: nenhum cobre o fallback (o limite de uso real não se provoca); (4) `FallbackTutorBrainConversationTests.F15` fixa o comportamento; (5) DoD D8/D9: não exercitam a retentativa. A alternativa "aviso só na primeira troca da sessão" fica no ADR-010 como item a medir |
| **L2 (limite aceito): custo por turno com o primário no limite.** Cada turno gasta uma chamada que falha antes do reserva (um processo curto do `claude`; até `TimeoutSeconds` no `Timeout`) | Consumidores: console (latência do turno no reserva = falha do primário + resposta do reserva); `eval` da US-009 (usa cérebros por chave, sem reserva, não é afetado); testes (fakes, sem relógio real). Medir depois (ADR-001) |
| **L3 (limite aceito): falhas do Ollama como primário são `Unknown`** (ADR-009) e não acionam o reserva | Consumidores: `FallbackTutorBrain` (F6 prova `Unknown` sem troca); console (a mensagem "Ollama is not running" aparece como hoje); validador (aceita `Provider=Ollama` com reserva `ClaudeCode`; o reserva só vale para os 4 tipos); DoD (D8 usa o Claude Code como primário) |
| **L4 (limite aceito): limite de uso não reconhecido pelos padrões do ADR-008** cai em `Unknown` e não aciona o reserva | Consumidores: `ClaudeCodeFailures` (padrões `usage limit`, `hit your limit`); `FallbackTutorBrain` (só olha o `Kind`); `LiveBrain` (nenhum cobre); DoD (usa `CliNotFound`, não o limite real). Se a CLI mudar o texto, o aviso não aparece e o aluno vê a mensagem de falha; o usuário decide se amplia os padrões |
| O padrão sem reserva faz o aluno achar que o fallback automático funciona sozinho | `appsettings.json` entrega vazio (P1) e a ARCHITECTURE §6.1 explica; a decisão é do usuário (P1) |
| `appsettings.json` não é copiado para a pasta do `Console.Tests` | C12 lê o arquivo da saída; se não existir, o Executor lê pela raiz do repositório (como `RepositoryRoot` do Core.Tests) e registra na §12 |
| O aviso entra na saída padrão e mistura com a resposta em quem faz pipe | A saída padrão é a "tela" do aluno hoje (avisos locais já vão para lá); o canal é a P4 |
| `claude-missing-us007` na DoD depende de o PowerShell 5.1 não achar um executável com esse nome | Nome único e improvável; as variáveis `Brain__*` são removidas no mesmo comando (AP-034) |

## 8. Fora de escopo

- Persistir qual provedor respondeu cada turno (a coluna "provedor" da persistência, `ARCHITECTURE.md` §8, é de US futura sem número; o `Name` do decorator é o do primário, P8).
- Novos valores em `BrainErrorKind` (`ProviderUnavailable`, `ModelNotFound`) para "Ollama fora do ar" acionar o reserva: sem US dona; limite L3.
- `Create(provider)` público na fábrica ou qualquer mudança para o `eval`: a US-009 usa os cérebros com chave que esta US registra; nada a registrar na spec dela.
- Reserva "pegajoso" na sessão, aviso só na primeira troca, mais de um reserva em cadeia.
- Teste `LiveBrain` do fallback: o limite de uso real não se provoca; a DoD D8 exercita o decorator com `CliNotFound` e o Ollama real.
- Suíte de contrato (US-008) sobre o `FallbackTutorBrain`: a US-008 cobre `ClaudeCode` e `Ollama`; nenhuma mudança agora.

Adiamentos procurados também nas P*, nos comentários de teste e nos ADRs (AP-040): o único que esta US fecha é o seletor provisório (ADR-009, item 5; spec D7). Nenhum adiamento novo cria dono em outra US.

## 9. Perguntas em aberto

- **P1 — Valor de `FallbackProvider` no `appsettings.json` entregue.** Resposta padrão: vazio (opt-in). Motivo: reserva igual ao primário é configuração inválida e o D6 da spec (`Brain__Provider=Ollama`) quebraria com `"Ollama"`. Alternativa: entregar `"Ollama"` e aceitar reserva igual ao primário como "sem reserva". Teste: `AppSettingsTests.AppSettings_WhenShipped_KeepsFallbackProviderEmpty`.
- **P2 — Quais falhas trocam.** Resposta padrão: os quatro tipos da RN-BRAIN08, incluindo `Timeout`; a ARCHITECTURE §6.3 é corrigida (§2.8, item 6). Teste: `BrainFallbackPolicyTests.ShouldFallBack_ForKindThatRn08Lists_ReturnsTrue`.
- **P3 — Reserva igual ao primário.** Resposta padrão: configuração inválida (a partida falha com código 2 e a mensagem da §2.1). Teste: `EnglishTutorAppTests.RunAsync_WhenFallbackProviderEqualsProvider_ExitsWithInvalidConfigurationBeforePrompt`.
- **P4 — Canal e posição do aviso.** Resposta padrão: saída padrão, antes da linha `Tutor:`. Teste: `EnglishTutorAppTests.RunAsync_WhenPrimaryHitsUsageLimit_AnswersFromFallbackAfterShowingSwitchNotice`.
- **P5 — Texto do motivo.** Resposta padrão: `usage limit reached`, `timed out`, `CLI not found`, `not authenticated` (o primeiro vem da spec). Teste: `ConsoleTextTests.BrainSwitched_ForEachFallbackKind_NamesTargetAndReason`.
- **P6 — Aviso em todo turno que troca.** Resposta padrão: sim, sem estado (RN-BRAIN08, "para aquele turno"); a repetição na retentativa é aceita (L1). Teste: `FallbackTutorBrainTests.SendAsync_OnNextCall_TriesPrimaryFirstAgain` e `FallbackTutorBrainConversationTests.SendAsync_WhenFallbackAnswerIsInvalid_ServiceRetryStartsAgainFromPrimary`.
- **P7 — Falha do próprio reserva.** Resposta padrão: propaga a exceção do reserva (o aluno vê a causa do reserva depois do aviso). Teste: `FallbackTutorBrainTests.SendAsync_WhenSecondaryFails_PropagatesTheSecondaryException`.
- **P8 — `Name` do decorator.** Resposta padrão: o nome do primário. Teste: `FallbackTutorBrainTests.Name_Always_IsThePrimaryName`.
- **P9 — `FallbackProvider` só com espaços.** Resposta padrão: igual a vazio (sem reserva), como o `TryGetCanonical` já trata. Teste: `BrainOptionsValidatorTests.Validate_WhenFallbackProviderIsEmptyOrBlank_Succeeds` e `BrainFactoryTests.Create_WithBlankFallbackProvider_ReturnsThePrimaryBrain`.
- **P10 — `Brain:Provider` vazio ou inválido no DI.** Resposta padrão: a `BrainFactory` lança `InvalidOperationException` com a mensagem do validador (antes, o seletor caía em silêncio no Claude Code). No app, o `ValidateOnStart` já barra antes. Teste: `BrainFactoryTests.Create_WhenProviderIsInvalid_ThrowsExplainingAcceptedProviders` (e `Create_WhenFallbackProviderIsUnknown_ThrowsNamingValueAndAcceptedProviders` para o reserva).
- **P11 — Ollama como primário com reserva.** Resposta padrão: permitido na configuração; as falhas do Ollama são `Unknown` e não trocam (L3). Teste: `FallbackTutorBrainTests.SendAsync_WhenPrimaryFailsWithKindOutsidePolicy_PropagatesWithoutSecondaryCallOrNotice`.
- **P12 — Quem registra o notificador.** Resposta padrão: o console, antes da Infrastructure (`TryAdd`); a Infrastructure traz um padrão silencioso. Teste: `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenNotifierIsAlreadyRegistered_KeepsIt`.
- **P13 — `--resume` do Claude Code depois de um turno do reserva.** Resposta padrão: nada a fazer, o ADR-007 já abre sessão nova. Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenRequestDoesNotExtendKnownSession_StartsNewSessionWithFullTranscript` (existente, de outra US).

## 10. Definition of Done (comandos verificáveis)

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes, em < 30 s |
| D3 | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` | Core ≥ 80% de linhas |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test --filter "FullyQualifiedName~BrainFactory\|FullyQualifiedName~FallbackTutorBrain"` | verde (B1–B6, F1–F15 e os testes DI cujo nome tem `BrainFactory`/`FallbackTutorBrain`) |
| D6 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider` | cabeçalho com `Brain: Ollama`; `exit=0` (depende do `FallbackProvider` vazio, P1) |
| D7 | `git grep -n -F "Provisional selector" -- src tests` | nenhuma linha. Prova do comando (AP-035): antes da T4 acha **uma** linha, em `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` |
| D8 | Comando D8 da spec (§2.7 c), Windows PowerShell 5.1, com o Ollama rodando e o modelo `qwen2.5:14b-instruct-q4_K_M` baixado | linha `Switched to Ollama (CLI not found)` seguida de uma linha `Tutor: <reply>`; `exit=0`; as variáveis `Brain__*` removidas ao fim |
| D9 | Comando D9 da spec (§2.7 c), Windows PowerShell 5.1, sem precisar do Ollama | `The tutor could not answer: Claude Code CLI not found`; nenhuma linha `Switched to`; `exit=0` |
| D10 | `dotnet test --filter "FullyQualifiedName~BrainFallbackPolicy\|FullyQualifiedName~BrainOptionsValidator\|FullyQualifiedName~ConsoleBrainSwitchNotifier\|FullyQualifiedName~ConsoleText\|FullyQualifiedName~AppSettings"` | verde (P1–P3, V1–V6, C8–C12) |
| D11 | `dotnet test --filter "FullyQualifiedName~EnglishTutorAppTests"` | verde (C1–C7 e os existentes) |
| D12 | `git grep -n -F "Always_RegistersClaudeCodeBrainAndFilePromptSource" -- src tests prompts docs` | nenhuma linha (antes da T4 acha a linha 18 de `InfrastructureServiceCollectionExtensionsTests.cs`, AP-035/AP-050) |
| D13 | `git grep -n -F "IBrainSwitchNotifier" -- ARCHITECTURE.md` | pelo menos 1 linha (o §4 da ARCHITECTURE foi atualizado antes da T1) |
| D14 | (Git Bash) `git diff develop...HEAD \| LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'`, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D15 | `git status --short` depois de D2–D11 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |

Notas: D8 e D9 rodam no ambiente do desenvolvedor; o `claude` real não é chamado (o executável configurado não existe). Nenhum critério `LiveBrain` nesta US: nenhum teste depende de saída de modelo, então não há tolerância nem rodadas a definir (AP-032, AP-052). Limite: só os testes unitários e de componente provam o aviso com `UsageLimitReached` de verdade; a DoD usa `CliNotFound` porque o limite real de uso não se provoca.

## 11. Evidências
_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário
_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-22 — pedido do Revisor do plano (rodada 1, REPROVADO)
- **B1** — §3 e T4 prometem "B1–B6", mas a §4 só tem B1, B2, B3, B4 e B6 (falta B5). Definir o B5 (projeto, classe, método e cenário) ou renumerar para B1–B5 em §3, §4, §5 T4 e D5.
- **B2** — O DI3 (`AddEnglishTutorInfrastructure_WhenNotifierIsAlreadyRegistered_KeepsIt`) nasce verde após a T2. Descrever na §4 a observação que o faz falhar antes da fábrica (o notificador registrado antes é o que recebe o aviso na troca), ou declará-lo na T4 entre os que nascem verdes, com a mutação que o derruba (`TryAdd` → `Add`). Ajustar D5/T4.
- Sugestões a avaliar junto com o usuário: S1 (remover do D5 o trecho sobre testes DI), S2 (D8: "não reproduzido" se não houver Ollama), S3 (frase sobre `Name` coberto por cérebros/DI5), S4 (justificar `FromProvider` ou remover).

**Respostas do Planejador (rodada 1)**
- B1 → §4 (nova linha B5 `Create_WhenFallbackProviderIsUnknown_ThrowsNamingValueAndAcceptedProviders`, cobre `FallbackProvider` desconhecido na fábrica, lacuna real do mapa; condicionais AP-047 citam B5), §5 T4 (Red diz "B1–B6 (inclui B5)"), §9 P10 (cita B5). A faixa B1–B6 de §3 e D5 fica correta; IDs conferidos na §4: B1, B2, B3, B4, B5, B6.
- B2 → §4 (DI3 descreve o efeito observável: o aviso chega ao notificador registrado antes, com a mutação `TryAdd` → `Add`), §5 T4 (DI3 na lista de falhas, mutação acrescentada, só DI1 nasce verde entre os DI). D5 não muda: o filtro lista B* e F*, e o trecho sobre DI é a S1, pendente.
- S1–S4 → Não incorporado nesta rodada: ficam para decisão do usuário.

### 2026-10-22 — comentário do usuário
Plano aprovado. Incorporar as sugestões S1, S2, S3 e S4 da revisão do plano (rodada 2). Respostas padrão P1–P13 aceitas sem mudança.
- S1: remover do D5 o trecho sobre testes DI (ou dizer que os DI são provados pelo D2).
- S2: D8 — sem Ollama/modelo, o Executor registra "não reproduzido" na §11 e o C3 vale como prova.
- S3: §4 DI1 — frase dizendo que o `Name` de cada provedor continua coberto pelos cérebros e pelo DI5.
- S4: §2.3 — registrar que `BrainSwitchNotice.FromProvider` existe para log e consumidores futuros.
