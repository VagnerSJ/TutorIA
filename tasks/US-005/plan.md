# Plano — US-005 Erros do Claude Code com mensagens claras

Status: Aguardando aprovação
Branch: feature/US-005_erros-claude-code
Spec: specs/sprints/SPRINT_1.md#us-005
Regras: RN-BRAIN07, RN-BRAIN09 (e, sem mudança, RN-BRAIN01, RN-BRAIN04, RN-BRAIN06, RN-BRAIN08, RN-CONV05); ARCHITECTURE §2 (R1–R4), §3, §6.1, §6.3, §9, §10; ADR-006, ADR-007 (item 5 complementado), ADR-008 (Proposto, novo)

## 1. Objetivo

Classificar as falhas do `claude -p` (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`, `Unknown`), limitar o tempo de cada chamada com `Brain:ClaudeCode:TimeoutSeconds` (padrão 60 s, encerrando a árvore do processo) e mostrar ao aluno, na saída de erro, o que deu errado e o que fazer, **sem encerrar a sessão**.

## 2. Abordagem

A decisão central está no **ADR-008 (Proposto)**: `docs/adr/ADR-008-classificacao-de-falhas-e-tempo-maximo-do-claude-code.md`. Em resumo:

- a classificação é do adaptador (`ClaudeCodeTutorBrain`, `ClaudeCodeOutput` e um novo `internal static class ClaudeCodeFailures`);
- o tempo máximo é um `CancellationTokenSource(TimeSpan, TimeProvider)` ligado ao token do chamador, por chamada ao processo;
- a `BrainException.Message` é o texto para o aluno (o que houve e o que fazer);
- o console captura só `BrainException`, escreve na saída de erro, registra um aviso e volta ao prompt.

Nada muda no Core: `BrainErrorKind` e `BrainException(kind, message, inner)` já existem (ARCHITECTURE §4).

### 2.1 Configuração (`ClaudeCodeOptions`, `ClaudeCodeOptionsValidator`, `appsettings.json`)

```csharp
public sealed class ClaudeCodeOptions
{
    public const int DefaultTimeoutSeconds = 60;   // RN-BRAIN09
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 3600;     // P6
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
    // demais propriedades sem mudança
}
```

Mensagem nova do validador (mesma forma de `Conversation:MaxTurnsInWindow must be at least 1 (found 0)`, US-004):

| Condição | Mensagem |
|---|---|
| `TimeoutSeconds < 1` ou `> 3600` | `Brain:ClaudeCode:TimeoutSeconds must be between 1 and 3600 (found <valor>). Expected the maximum number of seconds to wait for each Claude Code call (default: 60).` |

`appsettings.json`, na seção `Brain:ClaudeCode`, depois de `"WorkingDirectory"`: `"TimeoutSeconds": 60,` (ARCHITECTURE §6.1 já tem a chave nessa posição).

Nenhuma classe de Options nova entra no `ValidateOnStart`: a regra de "duas seções inválidas ao mesmo tempo" (AP-039) já tem teste na US-004 (`RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration`) e continua valendo. `TimeoutSeconds` é inteiro, então não há normalização de caixa. O caminho da chave nova até `ExitCodes.InvalidConfiguration` ganha um teste de console (§4, T1).

**Consumidores do limite 1..3600 (AP-040):** o validador (recusa fora da faixa, com teste nos dois lados); o adaptador (`TimeSpan.FromSeconds`, sempre dentro do máximo aceito por `CancellationTokenSource`); `LiveClaudeCode` (usa o padrão 60; as chamadas reais levam 3 a 6 s, US-004 D7); o D11 (usa 1, dentro da faixa).

### 2.2 Tempo máximo no adaptador (`ClaudeCodeTutorBrain.SendAsync`)

Mudança nos passos 5 a 7 da US-004 §2.4:

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds), _timeProvider);
using var call = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
try
{
    await File.WriteAllTextAsync(systemPromptPath, request.SystemPrompt, _utf8WithoutBom, call.Token);
    result = await _processRunner.RunAsync(new ProcessRequest(...), call.Token);
}
catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
{
    throw ClaudeCodeFailures.Timeout(options.TimeoutSeconds, exception);
}
catch (Win32Exception exception)
{
    throw ClaudeCodeFailures.StartFailed(options.ExecutablePath, exception);   // §2.3
}
finally
{
    File.Delete(systemPromptPath);
}
```

- O relógio começa antes da escrita do arquivo temporário e vale para a chamada inteira ao processo (P5).
- O `IProcessRunner` recebe o token ligado. No estouro, o `ProcessRunner` real já chama `Kill(entireProcessTree: true)` e relança a `OperationCanceledException` (US-004 §2.1); o adaptador a converte em `BrainException(Timeout)`.
- Cancelamento do chamador (Ctrl+C, `ct` cancelado) propaga `OperationCanceledException` sem conversão (`padroes-codigo`, "Erros"), nos dois casos: só o chamador cancelou (`SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`) e o tempo estourou e o chamador também cancelou antes de a exceção chegar ao adaptador (`SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`, que prova a parte `!ct.IsCancellationRequested` do filtro; S2).
- `_session = null` antes da chamada continua (US-004): depois de um `Timeout`, o turno seguinte abre sessão nova.
- A forma acima é sugestão; o Executor pode reorganizar, mantendo o comportamento e a ordem dos `catch`.

### 2.3 Executável que não pode ser iniciado (`ClaudeCodeFailures.StartFailed`)

O contrato do `IProcessRunner` passa a dizer, num comentário da interface: "lança `Win32Exception` quando o executável não pode ser iniciado; cancelamento encerra a árvore do processo e lança `OperationCanceledException`". O runner não muda de comportamento; só o comentário do `ProcessRunner` (texto antigo na §2.9).

| `Win32Exception.NativeErrorCode` | Resultado |
|---|---|
| 2 (arquivo não encontrado; `ENOENT`) ou 3 (caminho não encontrado) | `BrainException(CliNotFound, <mensagem CliNotFound da §2.5>, inner)` |
| outro (ex.: 5, acesso negado; 193, não é executável) | `BrainException(Unknown, <mensagem "could not be started" da §2.5>, inner)` |

O shim `claude.cmd` do npm não é encontrado com o nome `claude` (o .NET só acrescenta `.exe`), então cai em `CliNotFound`, e a mensagem pede o instalador nativo, que é o suportado (ADR-007, "Consequências").

Efeito da segunda linha na US-007 (limite aceito, S6): a RN-BRAIN08 aciona o fallback para `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated`, não para `Unknown`. Um `ExecutablePath` que aponta para algo que existe mas não pode ser executado (5, 193) **não aciona o fallback**; o aluno vê "could not be started" com o código e a chave, e a sessão continua. Registrado na P9 e no ADR-008 ("Consequências").

### 2.4 Classificação por texto (`ClaudeCodeOutput.Read` + `ClaudeCodeFailures.Classify`)

A tabela da US-004 §2.5 continua valendo; mudam as linhas 1 e 3:

| # | Situação | Resultado |
|---|---|---|
| 1 | `ExitCode != 0` | `kind = Classify(errorResult, stderr)`; `Unknown` → `BrainException(Unknown, "Claude Code failed (exit code <n>): <detalhe>")` (sem mudança); senão a mensagem do `kind` (§2.5) com `<detalhe>` |
| 2 | `ExitCode == 0` e não é o JSON `type: result` | sem mudança: `Unknown`, **sem** classificar (P10) |
| 3 | `ExitCode == 0` e `is_error: true` | `kind = Classify(errorResult, stderr)`; `Unknown` → `"Claude Code reported an error: <detalhe>"` (sem mudança); senão a mensagem do `kind` |
| 4, 5 | sucesso | sem mudança |

- `errorResult`: o `result` do objeto JSON quando o stdout é o JSON `type: result` com `is_error: true`; senão `null` (a mesma condição que o `<detalhe>` da US-004 já usa).
- `<detalhe>`: regra da US-004, sem mudança (o `result` de erro não vazio; senão a primeira linha não vazia do stderr; senão `no error details were printed`). P13.
- `Classify(errorResult, standardError)` examina `errorResult` e o stderr **inteiro**, sempre as duas fontes, sem diferenciar maiúsculas (`StringComparison.OrdinalIgnoreCase`), nas duas direções: um `result` de erro que não casa com nenhum padrão não impede o stderr de casar (caso 11 da §4, S1), e um stderr presente que não casa com nenhum padrão não impede o `result` de erro de casar (caso 12 da §4, R2-S1). Avalia nesta ordem; a primeira que casar decide:

| Ordem | Padrões (qualquer um, como trecho) | `BrainErrorKind` |
|---|---|---|
| a | `not logged in`, `/login`, `invalid api key`, `authentication_error`, `oauth token` | `NotAuthenticated` |
| b | `usage limit`, `hit your limit` | `UsageLimitReached` |
| c | nenhum | `Unknown` |

`rate_limit` (limite momentâneo da API) não está na lista b e fica `Unknown` (P10). Se a saída gravada de falta de login (T4a) não casar com nenhum padrão da linha a, o Executor acrescenta o trecho observado à lista e registra a escolha na §12 antes do commit `feat` (P3). Cada padrão e cada linha tem um caso que só ele satisfaz, e cada fonte tem um caso em que só ela casa com a outra fonte presente e sem casar (§4, casos 11 e 12; AP-041).

### 2.5 Mensagens ao aluno (texto da `BrainException.Message`; inglês, o aluno está praticando)

| `BrainErrorKind` | Mensagem |
|---|---|
| `CliNotFound` | `Claude Code CLI not found ("<ExecutablePath>"). Install the native Claude Code CLI (in PowerShell: irm https://claude.ai/install.ps1 \| iex) or set Brain:ClaudeCode:ExecutablePath to the full path of claude.exe.` |
| `Unknown` (início) | `Claude Code could not be started ("<ExecutablePath>", error <NativeErrorCode>). Expected Brain:ClaudeCode:ExecutablePath to point to the Claude Code CLI (claude.exe).` |
| `NotAuthenticated` | `Claude Code is not logged in (<detalhe>). Run "claude auth login" in a terminal, then send your sentence again. If the ANTHROPIC_API_KEY environment variable is set, check or remove it.` (P17, S8) |
| `UsageLimitReached` | `Claude Code usage limit reached (<detalhe>). Wait until your Claude plan limit resets, then send your sentence again.` |
| `Timeout` | `Claude Code did not answer in time (limit: <TimeoutSeconds> s) and was stopped. Send your sentence again, or raise Brain:ClaudeCode:TimeoutSeconds.` |
| `Unknown` (saída) | sem mudança (US-004 §2.5): `Claude Code failed (exit code <n>): <detalhe>`, `Claude Code reported an error: <detalhe>`, `Claude Code returned output that is not a JSON result. ...` |

(Na tabela, `\|` é só o escape do Markdown; no código é `|`.) Nenhuma mensagem embute `Win32Exception.Message`, que é texto do sistema operacional (AP-015); os testes conferem trechos que só o projeto produz. O texto da instalação e a existência de `claude auth login` são conferidos na T4a (P2). A frase de `ANTHROPIC_API_KEY` é fixa (não depende de a variável existir; P17).

### 2.6 Console

- `ConversationInputHandler(ConversationService conversation, TextWriter output, TextWriter error, ILogger logger)`: `HandleAsync` envolve `_conversation.SendAsync` num `try`/`catch (BrainException exception)`:
  - escreve em `error` as linhas de `ConsoleText.BrainFailed(exception.Message)`;
  - registra `AppLog.BrainFailed(logger, exception.Kind, exception)`;
  - retorna (o `ChatSession` volta ao prompt).
  - Qualquer outra exceção, inclusive `OperationCanceledException`, propaga sem mudança (P11 da US-004: o `ConversationService` não grava o turno que falhou).
- `ConsoleText.BrainFailed(string message)` (novo), em `Error`:

  | Linha | Texto |
  |---|---|
  | 1 | `The tutor could not answer: <message>` |
  | 2 | `You can send a sentence again or type /quit to exit.` |

- `AppLog` (faixa 1000–1099): `[LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Brain call failed with {ErrorKind}; the session continues")] BrainFailed(this ILogger logger, BrainErrorKind errorKind, Exception exception)`. Sem texto do aluno (a mensagem da exceção traz só o texto da CLI ou da configuração).
- `EnglishTutorApp.RunSessionAsync`: `new ConversationInputHandler(services.GetRequiredService<ConversationService>(), options.Output, options.Error, logger)`. O `catch (Exception)` de erro inesperado não muda; ele deixa de ver `BrainException` (P1). Essa construção entra já no esqueleto do Red da T5, para o Red compilar (S7).
- `TutorNotConnectedInputHandler` e `ChatSession` não mudam.

### 2.7 Fixtures de erro (`tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/errors/`)

Cada caso tem três arquivos: `<caso>.stdout.txt`, `<caso>.stderr.txt` e `<caso>.exit-code.txt`. O auxiliar de teste `ClaudeErrorFixtures.Read(string caso) → ProcessResult` (em `Support/`) lê os três (`int.Parse` do código com `Trim()`), com a mesma mensagem de arquivo ausente do `Fixture.Read`. Já são copiados pela regra `Fixtures\**` do `.csproj`.

| Caso | Origem | Tipo esperado |
|---|---|---|
| `not-authenticated` | **gravado** (T4a): `CLAUDE_CONFIG_DIR` apontando para uma pasta vazia | `NotAuthenticated` |
| `unknown-model` | **gravado** (T4a): `--model nao-existe-us005` com login válido | `Unknown` |
| `usage-limit-synthetic-1` | **sintético**: o stdout gravado de `not-authenticated`, com o `result` trocado por `Claude AI usage limit reached\|1760000000 (synthetic)`; mesmo código de saída | `UsageLimitReached` |
| `usage-limit-synthetic-2` | **sintético**: stdout vazio, stderr `You've hit your limit - resets 5pm (synthetic)`, código 1 | `UsageLimitReached` |

`README.md` na pasta: data, `claude --version` da gravação, comando usado, quais casos são sintéticos e por quê (o limite não é reproduzível sob demanda, P4), qualquer anonimização feita (caminhos pessoais trocados por `<temp>`), a instrução "na primeira ocorrência real de limite de uso, gravar a saída e trocar o sintético" e a seção **"Diferenças em relação à chamada do adaptador"** (S4, P18), com o texto:

- modo Tags (`--append-system-prompt-file prompts/format.tags.md`), sem `--json-schema`: o esquema é uma constante do código (`TutorReplySchema.Json`) e as duas falhas gravadas acontecem antes de qualquer resposta do modelo;
- system prompt = o arquivo-modelo `prompts/tutor.md` sem renderizar (o adaptador grava o prompt renderizado num arquivo temporário);
- sem `--resume` (primeira chamada de uma sessão);
- entrada padrão de uma linha (`Student said (transcribed): "hi"`).

Os argumentos de isolamento são os mesmos do adaptador (RN-BRAIN06; `ClaudeCodeTutorBrain.BuildArguments`): pasta de trabalho temporária e vazia, `--system-prompt-file`, `--tools ""`, `--disallowedTools "mcp__*"`, `--strict-mcp-config`, `--permission-mode dontAsk`, `--model`, `--output-format json`. Assim a CLI não carrega MCP nem a configuração de projeto, e o stderr gravado é o que o adaptador veria.

Gravação (T4a, Git Bash, a partir da raiz do repositório; pasta de trabalho temporária e vazia, sem as variáveis herdadas, AP-034). `pwd -W` dá o caminho no formato do Windows (`C:/...`), que o `claude.exe` entende sem conversão:

```bash
root=$(pwd -W); dir="$root/tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/errors"; mkdir -p "$dir"
for v in $(compgen -e | grep '^CLAUDE_CODE_'); do unset "$v"; done
claude --version > "$dir/cli-version.txt"
record() {  # $1 caso, $2 modelo, demais: variáveis extras do filho
  local name=$1 model=$2; shift 2
  ( cd "$(mktemp -d)" && printf 'Student said (transcribed): "hi"\n' \
      | env -u CLAUDECODE -u ANTHROPIC_BASE_URL "$@" timeout 120 claude -p --output-format json \
          --system-prompt-file "$root/prompts/tutor.md" --append-system-prompt-file "$root/prompts/format.tags.md" \
          --tools "" --disallowedTools "mcp__*" --strict-mcp-config --permission-mode dontAsk --model "$model" \
      > "$dir/$name.stdout.txt" 2> "$dir/$name.stderr.txt"; echo $? > "$dir/$name.exit-code.txt" )
}
record not-authenticated haiku CLAUDE_CONFIG_DIR="$(mktemp -d)"
record unknown-model nao-existe-us005
cat "$dir"/*.exit-code.txt
```

- Código `124` = o `timeout` do Bash estourou (a CLI ficou esperando): pare e registre na §12.
- Se `not-authenticated` não sair com erro de autenticação (a CLI achou credenciais fora de `CLAUDE_CONFIG_DIR`), grave de novo acrescentando `ANTHROPIC_API_KEY=invalid-key-for-us005`. Se ainda assim não houver erro de autenticação, pare: o usuário decide na §12 (P3).
- `cli-version.txt` vai para o `README.md` e é apagado (não é fixture).
- Higiene: D14 sem linhas; também sem o nome do titular da conta (US-004, D13).
- Linha de leitura de `unknown-model` (R2-S2): o Executor registra nas Evidências da T4a o código de saída gravado, se o stdout é o JSON `type: result` com `is_error: true`, e a linha da tabela da §2.4 em que a fixture cai (1 ou 3). Se cair na linha 2 (código 0 e saída que não é o JSON `type: result`), pare e registre na §12: a fixture não serviria ao D5 como `Unknown` classificado.

### 2.8 Alterações nos documentos-raiz e na spec (propostas; aplicadas pelo orquestrador antes da T1, em dois commits, AP-030)

O orquestrador aplica, depois da aprovação e **antes da T1**:

- `docs(ai): update spec for US-005`: o bloco de `specs/sprints/SPRINT_1.md` abaixo;
- `docs(ai): update architecture for US-005`: `ARCHITECTURE.md`, o cabeçalho do ADR-007 e o ADR-008 em "Aceito", se aprovado.

Nenhuma tarefa da §5 altera esses documentos. Cada trecho é identificado pelo **texto antigo** (AP-023).

**`specs/sprints/SPRINT_1.md`, seção `## US-005 — Erros do Claude Code com mensagens claras`, tabela "DoD extra"** (P4 e P15). Texto antigo:

```
| D5 | `dotnet test --filter "FullyQualifiedName~ClaudeCode"` | um teste por `BrainErrorKind`, com fixtures de stdout/stderr gravadas |
```

Texto novo:

```
| D5 | `dotnet test --filter "Category!=LiveBrain&FullyQualifiedName~ClaudeCode"` | verde, com ao menos um teste por `BrainErrorKind` que o adaptador produz (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`, `Unknown`; `InvalidOutput` vem da US-004). `NotAuthenticated` e `Unknown` usam stdout/stderr gravados de execuções reais; `UsageLimitReached` usa fixtures sintéticas, marcadas como tal no `README.md` da pasta de fixtures, porque o limite de uso não é reproduzível sob demanda; `CliNotFound` e `Timeout` vêm de exceção e de tempo, sem stdout |
```

Os cenários da US-005 não mudam. Nenhuma regra `RN-*` muda: a leitura "tempo máximo por chamada ao processo" da RN-BRAIN09 fica registrada no ADR-008 e na P5; o efeito de `Unknown` na RN-BRAIN08 (sem fallback) fica registrado no ADR-008 e na P9 (S6). A spec da US-007 não muda: o comportamento segue a RN-BRAIN08 como está escrita.

**`ARCHITECTURE.md`:**

- §3, texto antigo:
  `    Brain/             ClaudeCodeTutorBrain, ClaudeCodeOptions, ClaudeCodeOptionsValidator, ClaudeCodeOutput,`
  Texto novo:
  `    Brain/             ClaudeCodeTutorBrain, ClaudeCodeOptions, ClaudeCodeOptionsValidator, ClaudeCodeOutput, ClaudeCodeFailures,`
- §6.3, texto antigo:
  `- Saída: `structured_output` (JsonSchema) ou `result` (Tags) vai para o `TutorReplyParser`; o `session_id` fica no adaptador (ADR-007). Até a US-005, código de saída diferente de zero, `is_error` ou saída que não é JSON viram `BrainException` com `Unknown`; executável inexistente propaga a exceção do sistema e o console mostra erro inesperado (a US-005 a classifica como `CliNotFound`).`
  Texto novo:
  `- Saída: `structured_output` (JsonSchema) ou `result` (Tags) vai para o `TutorReplyParser`; o `session_id` fica no adaptador (ADR-007). Falhas viram `BrainException` classificada pelo adaptador (ADR-008): executável não encontrado → `CliNotFound`; texto de autenticação ou de limite de uso no `result` de erro ou no stderr → `NotAuthenticated` ou `UsageLimitReached`; `TimeoutSeconds` estourado → `Timeout`, com o processo encerrado junto com a árvore; o resto → `Unknown` com o detalhe. A mensagem da exceção é o texto mostrado ao aluno (o que houve e o que fazer); o console a mostra na saída de erro e a sessão continua.`
- §6.1 (já tem `"TimeoutSeconds": 60`) e §4 (`BrainErrorKind`, `BrainException`): **sem alteração**.

**`docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md`** (Aceito; a decisão não muda, só ganha o ponteiro). Depois da linha antiga `- **US relacionadas:** US-004 (e, como consumidoras, US-005, US-007, US-008)`, acrescentar:
`- **Complementado por:** ADR-008 (item 5: classificação das falhas e executável inexistente, US-005)`

**Repetições em `CLAUDE.md` e skills** (Grep por `Win32Exception|CliNotFound|TimeoutSeconds|BrainErrorKind|erro inesperado|unexpected error|US-005` em `CLAUDE.md` e `.claude/**`):

- `CLAUDE.md`: nenhuma linha repete §3 ou §6.3. **Sem alteração.**
- `.claude/skills/padroes-codigo/SKILL.md`, "Erros": `- Falhas de provedores viram exceções de domínio (`BrainException` com `BrainErrorKind`), traduzidas pelo adaptador; o Core não conhece `Win32Exception` nem `HttpRequestException`.` Continua correta (é exatamente o que esta US faz). **Sem alteração.**
- `.claude/skills/checklist-revisao/SKILL.md`, item `[O] Processos externos com `ArgumentList`, timeout e encerramento da árvore do processo.`: correto; nesta US o timeout **vale** (na US-004 era N/A). **Sem alteração.**

Documentos históricos que citam "até a US-005" (`docs/us/US-004.md`, `tasks/US-004/*`) não mudam; o Documentador escreve `docs/us/US-005.md`.

### 2.9 Testes e comentários existentes que mudam (sem enfraquecer)

| Atual | Mudança | Tarefa |
|---|---|---|
| `ClaudeCodeOptionsTests.Constructor_Always_UsesDocumentedDefaults` | Acrescenta `options.TimeoutSeconds.Should().Be(60)` | T1 |
| `ClaudeCodeTutorBrainTests.SendAsync_Always_PassesCallerTokenToProcessRunner` | **Sai.** O runner passa a receber o token ligado, não o do chamador. Substituído por `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`, que prova o que o teste antigo garantia (o cancelamento do chamador chega ao runner) e mais: não vira `Timeout` | T2 |
| `ClaudeCodeTutorBrainTests` (campo `_timeProvider`) | `ManualTimeProvider` → `FakeTimeProvider` (P8); `ManualTimeProvider.cs` é apagado no Refactor (sem outro uso, conferido por Grep) | T2 |
| `EnglishTutorAppTests.RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError` e os comentários `// US-004 — apoio, P11: falha do cérebro até a US-005.` / `// Substituído na US-005, cenário "falha do cérebro no console não encerra a sessão".` | **Saem.** Substituídos por `RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession` (spec D7) | T5 |
| `ConversationInputHandlerTests` (construtor do `_sut`) | Passa `_error` (`StringWriter` novo) e `NullLogger.Instance`; os 4 testes existentes não mudam de corpo | T5 |
| `FakeProcessRunner` | Ganha `Throws(Exception)` e `HangsUntilCancelled(Action? onCancelled = null)`: termina só com `OperationCanceledException` quando o token é cancelado, como o runner real; se `onCancelled` foi dado, ele roda depois de o token ser cancelado e **antes** de a exceção sair do fake (determinístico, sem corrida; S2). A fila passa a guardar uma função por chamada; `Returns`/`ReturnsOutput` não mudam | T2, T3 |

Comentários de código com adiamento para esta US (AP-040; Grep por `US-005` em `src/` e `tests/`):

| Arquivo | Texto antigo | Texto novo | Tarefa |
|---|---|---|---|
| `src/EnglishTutor.Infrastructure/Processes/ProcessRunner.cs` | `// Exceptions are not translated: a missing executable propagates Win32Exception (CliNotFound is US-005).` | `// Exceptions are not translated: a missing executable throws Win32Exception, which the adapter classifies (ADR-008).` | T3 |
| `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOutput.cs` | `// the fine classification of failures (CliNotFound, NotAuthenticated, ...) belongs to US-005.` | `// rows 1 and 3 are classified by ClaudeCodeFailures (ADR-008).` | T4b |
| `src/EnglishTutor.Console/Ui/ConversationInputHandler.cs` | `// Brain failures propagate (P11, until US-005).` | `// A BrainException is shown on the error output and the session continues (ADR-008); anything else propagates.` | T5 |

### 2.10 Guardas defensivas (sem teste, decisão registrada)

- `ArgumentNullException.ThrowIfNull` do construtor novo do `ConversationInputHandler` (`error`, `logger`) e dos métodos de `ClaudeCodeFailures`.
- Em `Classify`, `errorResult` nulo é tratado como texto vazio (o caminho com `null` é exercitado pelos casos de stderr da §4; não é guarda).

Só o próprio código os chama, sempre com valores válidos.

### Alternativas descartadas

- Textos por `BrainErrorKind` no console: o console não sabe a instrução de cada provedor (ADR-008).
- Classificar pelo código de saída ou pelo `subtype` do JSON: a CLI não diferencia as falhas assim (ADR-008).
- Tipo `ProcessStartException` no runner: um tipo a mais sem ganho de teste (ADR-008).
- Tempo máximo no `ProcessRunner` ou por turno no `ConversationService` (ADR-008; P5).
- Estender o `ManualTimeProvider` com temporizadores: reimplementa o que o `FakeTimeProvider` já faz e testa (P8).
- Classificar também a linha 2 (saída que não é JSON): sem caso real conhecido; manteria o texto "not a JSON result" ambíguo (P10).
- Gravar as fixtures com um teste `LiveBrain` (como o `FixtureRecorderLiveTests`): exigiria mudar o ambiente do processo de teste inteiro (`CLAUDE_CONFIG_DIR`), o que afetaria os outros testes em paralelo; o `IProcessRunner` não tem variáveis de ambiente por chamada (AP-034, fora de escopo).
- Gravar as fixtures também em modo JsonSchema (`--json-schema`): o esquema é uma constante do código e teria de ser extraído para o comando; as falhas gravadas acontecem antes da resposta do modelo (P18).
- Mencionar `ANTHROPIC_API_KEY` só quando a variável existe: o adaptador teria de ler o ambiente, que o `IProcessRunner` não expõe (P17).

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-008-classificacao-de-falhas-e-tempo-maximo-do-claude-code.md` | Decisão (Proposto), criada junto com este plano; revisada com S2, S4, S6 e S8 |
| Alterar | `docs/adr/ADR-007-claude-code-processo-por-turno-historico-e-resume.md` | Linha "Complementado por" (§2.8), pelo orquestrador antes da T1 |
| Alterar | `ARCHITECTURE.md` | §3 e §6.3 (§2.8), pelo orquestrador antes da T1 |
| Alterar | `specs/sprints/SPRINT_1.md` | D5 da US-005 (§2.8), pelo orquestrador antes da T1 |
| Alterar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOptions.cs` | `TimeoutSeconds` e constantes (§2.1) |
| Alterar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOptionsValidator.cs` | Faixa de `TimeoutSeconds` (§2.1) |
| Alterar | `src/EnglishTutor.Console/appsettings.json` | `"TimeoutSeconds": 60` (§2.1) |
| Alterar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeTutorBrain.cs` | Tempo máximo e `Win32Exception` (§2.2, §2.3) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeFailures.cs` | `Classify`, padrões e mensagens (§2.4, §2.5) |
| Alterar | `src/EnglishTutor.Infrastructure/Brain/ClaudeCodeOutput.cs` | Linhas 1 e 3 classificadas; comentário (§2.4, §2.9) |
| Alterar | `src/EnglishTutor.Infrastructure/Processes/IProcessRunner.cs` | Comentário do contrato (§2.3) |
| Alterar | `src/EnglishTutor.Infrastructure/Processes/ProcessRunner.cs` | Só o comentário (§2.9) |
| Alterar | `src/EnglishTutor.Console/Ui/ConversationInputHandler.cs` | Captura de `BrainException`, saída de erro, log (§2.6) |
| Alterar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | `BrainFailed(message)` (§2.6) |
| Alterar | `src/EnglishTutor.Console/Logging/AppLog.cs` | Evento 1005 `BrainFailed` (§2.6) |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Construção do handler com `Error` e logger (§2.6; já no Red da T5) |
| Alterar | `Directory.Packages.props` | `Microsoft.Extensions.TimeProvider.Testing` (grupo Tests) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/EnglishTutor.Infrastructure.Tests.csproj` | Referência ao pacote acima |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeProcessRunner.cs` | `Throws`, `HangsUntilCancelled(onCancelled)` (§2.9) |
| Apagar | `tests/EnglishTutor.Infrastructure.Tests/Support/ManualTimeProvider.cs` | Substituído por `FakeTimeProvider` (P8) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/ClaudeErrorFixtures.cs` | Leitura das fixtures de erro (§2.7) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/errors/*` | 4 casos × 3 arquivos + `README.md` com as diferenças da gravação (§2.7) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainTests.cs` | Testes novos e o substituído (§4) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeOptionsTests.cs` | Padrão 60 (§2.9) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeOptionsValidatorTests.cs` | Faixa de `TimeoutSeconds` (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Processes/ProcessRunnerLiveTests.cs` | Contrato real da porta: executável ausente e encerramento da árvore (`LiveBrain`, P7) |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | Cenário do console, CLI ausente de ponta a ponta, log, `TimeoutSeconds` inválido; teste antigo sai (§4) |
| Alterar | `tests/EnglishTutor.Console.Tests/Ui/ConversationInputHandlerTests.cs` | Falha do cérebro e propagação (§4) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/MissingExecutableProcessRunner.cs` | `IProcessRunner` que lança `Win32Exception(2)` e conta as chamadas |

## 4. Mapa cenário BDD → testes

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| CLI não encontrada | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey` | Unidade, `[Theory]` 2 casos (`NativeErrorCode` 2 e 3) |
| CLI não encontrada (apoio, outro lado, P9) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenExecutableCannotBeStartedForOtherReason_ThrowsUnknownWithErrorCode` | Unidade (`NativeErrorCode` 5) |
| CLI não encontrada (de ponta a ponta) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenClaudeExecutableIsNotFound_ShowsCliNotFoundInstructionAndContinues` | Componente (adaptador real + `MissingExecutableProcessRunner`) |
| CLI não encontrada (apoio: contrato real da porta, P7) | Infrastructure.Tests | `ProcessRunnerLiveTests` | `RunAsync_WhenExecutableDoesNotExist_ThrowsWin32ExceptionWithFileNotFoundCode` | `LiveBrain` (processo real, sem CLI) |
| não autenticado | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin` | Fixture gravada |
| não autenticado / limite de uso (apoio: tabela da §2.4, P3, P10, P13; as duas fontes nas duas direções, S1 e R2-S1) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` | Unidade, `[Theory]` 12 casos (abaixo) |
| limite de uso atingido | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithUsageLimitOutput_ThrowsUsageLimitReached` | Fixture sintética, `[Theory]` 2 casos (P4) |
| (D5: `Unknown` com saída gravada; caracterização) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail` | Fixture gravada |
| tempo esgotado | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenProcessDoesNotAnswerWithinTimeout_CancelsProcessAndThrowsTimeout` | Unidade (`FakeTimeProvider`) |
| tempo esgotado (apoio, P5: um relógio por chamada) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_OnEachCall_StartsTimeoutAgain` | Unidade |
| tempo esgotado (apoio: Ctrl+C não é `Timeout`; substitui `SendAsync_Always_PassesCallerTokenToProcessRunner`) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` | Unidade (caracterização; espera limitada, R2-S3) |
| tempo esgotado (apoio: tempo estourado **e** Ctrl+C ao mesmo tempo não é `Timeout`; S2) | Infrastructure.Tests | `ClaudeCodeTutorBrainTests` | `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` | Unidade (`FakeTimeProvider`) |
| tempo esgotado (apoio: "o processo filho é encerrado", árvore real) | Infrastructure.Tests | `ProcessRunnerLiveTests` | `RunAsync_WhenCancelled_KillsWholeProcessTree` | `LiveBrain` (caracterização) |
| tempo esgotado (apoio: configuração, RN-BRAIN09) | Infrastructure.Tests | `ClaudeCodeOptionsTests` | `Constructor_Always_UsesDocumentedDefaults` (alterado) | Unidade |
| tempo esgotado (apoio: faixa, P6) | Infrastructure.Tests | `ClaudeCodeOptionsValidatorTests` | `Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue` | Unidade, `[Theory]` 3 casos (0, -1, 3601) |
| tempo esgotado (apoio: faixa, outro lado) | Infrastructure.Tests | `ClaudeCodeOptionsValidatorTests` | `Validate_WithTimeoutSecondsAtLimits_Succeeds` | Unidade, `[Theory]` 2 casos (1, 3600) |
| tempo esgotado (apoio: chave inválida encerra antes do prompt) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt` | Componente |
| falha do cérebro no console não encerra a sessão (spec D7) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession` | Componente, `[Theory]` 5 casos (um por `BrainErrorKind` que chega ao console) |
| falha do cérebro no console não encerra a sessão (apoio: log, P11) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenBrainFails_LogsBrainFailedWarningWithKind` | Componente |
| falha do cérebro no console não encerra a sessão (apoio: canal) | Console.Tests | `ConversationInputHandlerTests` | `HandleAsync_WhenBrainFails_WritesMessageAndNextStepToErrorOnly` | Componente |
| falha do cérebro no console não encerra a sessão (apoio: só `BrainException`) | Console.Tests | `ConversationInputHandlerTests` | `HandleAsync_WhenFailureIsNotBrainException_PropagatesIt` | Componente, `[Theory]` 2 casos (`OperationCanceledException`, `InvalidOperationException`; caracterização) |

**Asserções principais** (cada uma confere texto que só o projeto produz, AP-015):

- `SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey`: `FakeProcessRunner.Throws(new Win32Exception(code))`; `Kind == CliNotFound`; a mensagem contém `Claude Code CLI not found ("claude-under-test")`, `irm https://claude.ai/install.ps1 | iex` e `Brain:ClaudeCode:ExecutablePath`; `InnerException` é a `Win32Exception`; o arquivo temporário do system prompt foi apagado.
- `SendAsync_WhenExecutableCannotBeStartedForOtherReason_ThrowsUnknownWithErrorCode`: código 5 → `Kind == Unknown`; a mensagem contém `could not be started ("claude-under-test", error 5)` e **não** contém `not found`.
- `SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin`: `ClaudeErrorFixtures.Read("not-authenticated")`; `Kind == NotAuthenticated`; a mensagem começa com `Claude Code is not logged in (`, contém `Run "claude auth login"` e `If the ANTHROPIC_API_KEY environment variable is set, check or remove it.` (P17); não contém `Claude Code failed (exit code`.
- `SendAsync_WithUsageLimitOutput_ThrowsUsageLimitReached` (casos `usage-limit-synthetic-1` e `usage-limit-synthetic-2`): `Kind == UsageLimitReached`; a mensagem contém `Claude Code usage limit reached (`, `(synthetic)` (o detalhe veio da fixture certa) e `Wait until your Claude plan limit resets`.
- `SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail`: `ClaudeErrorFixtures.Read("unknown-model")`; `Kind == Unknown`; a mensagem começa com `Claude Code failed (exit code` ou `Claude Code reported an error:` (conforme o código gravado, que o teste lê da fixture) e contém `nao-existe-us005` se a CLI o ecoar; senão, o primeiro trecho do detalhe gravado (o Executor fixa o trecho ao gravar, §12). A linha da §2.4 em que a fixture cai (1 ou 3) fica registrada nas Evidências da T4a (§2.7, R2-S2).
- `SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource`, um caso por padrão, linha e fonte (AP-041; rótulos únicos por caso, AP-026). Saída "erro-json" = `ClaudeOutputs.Error(<texto>)`, que declara `"type": "result"` e `"is_error": true`:

  | # | Código | stdout | stderr | Esperado | Trecho conferido |
  |---|---|---|---|---|---|
  | 1 | 1 | erro-json `Not logged in t1` | vazio | `NotAuthenticated` | `is not logged in (Not logged in t1)` |
  | 2 | 1 | vazio | `warning t2` + `\n` + `Run /login to continue t2` | `NotAuthenticated` | `is not logged in (warning t2)` (o padrão está na 2ª linha; o detalhe é a 1ª, P13) |
  | 3 | 0 | erro-json `Invalid API key t3` | vazio | `NotAuthenticated` | `is not logged in (Invalid API key t3)` (linha 3) |
  | 4 | 1 | vazio | `AUTHENTICATION_ERROR t4` | `NotAuthenticated` | `is not logged in (AUTHENTICATION_ERROR t4)` (caixa diferente) |
  | 5 | 1 | erro-json `OAuth token has expired t5` | vazio | `NotAuthenticated` | `is not logged in (OAuth token has expired t5)` |
  | 6 | 0 | erro-json `Claude AI usage limit reached t6` | vazio | `UsageLimitReached` | `usage limit reached (Claude AI usage limit reached t6)` (linha 3) |
  | 7 | 1 | vazio | `You've hit your limit t7` | `UsageLimitReached` | `usage limit reached (You've hit your limit t7)` |
  | 8 | 1 | erro-json `Usage limit reached. Please run /login t8` | vazio | `NotAuthenticated` | `is not logged in (Usage limit reached. Please run /login t8)` (ordem: a antes de b) |
  | 9 | 0 | texto `Not logged in t9` (não é JSON) | vazio | `Unknown` | `is not a JSON result` (linha 2 não é classificada, P10) |
  | 10 | 1 | vazio | `rate_limit_error t10` | `Unknown` | `Claude Code failed (exit code 1): rate_limit_error t10` (P10) |
  | 11 | 1 | erro-json `Server error t11` (não casa com nenhum padrão) | `Not logged in t11` | `NotAuthenticated` | `is not logged in (Server error t11)` (o tipo vem do stderr e o detalhe do `result` de erro: o `result` presente que não casa não impede o stderr, S1) |
  | 12 | 1 | erro-json `You've hit your limit t12` | `Warning: a newer version is available t12` (não casa com nenhum padrão) | `UsageLimitReached` | `usage limit reached (You've hit your limit t12)` (o tipo e o detalhe vêm do `result` de erro: o stderr presente que não casa não impede o `result`, direção simétrica ao caso 11, R2-S1) |

- `SendAsync_WhenProcessDoesNotAnswerWithinTimeout_CancelsProcessAndThrowsTimeout`: `TimeoutSeconds = 1`; `HangsUntilCancelled()`; `OnRun` sinaliza o início; `Advance(999 ms)` → `Tokens[0].IsCancellationRequested` é `false`; `Advance(1 ms)` → `BrainException` com `Kind == Timeout`, mensagem com `did not answer in time (limit: 1 s)` e `Brain:ClaudeCode:TimeoutSeconds`; `Tokens[0].IsCancellationRequested` é `true` (o `ProcessRunner` real encerra a árvore nesse momento, coberto por `RunAsync_WhenCancelled_KillsWholeProcessTree`); o arquivo temporário foi apagado. A espera pela tarefa usa `WaitAsync(TimeSpan.FromSeconds(10))`, para o Red falhar por tempo de teste e não travar a suíte.
- `SendAsync_OnEachCall_StartsTimeoutAgain`: `TimeoutSeconds = 1`; 1ª chamada responde depois de `Advance(900 ms)` (no `OnRun`); 2ª chamada fica pendente; `Advance(999 ms)` → token da 2ª **não** cancelado (com um relógio compartilhado, 900 + 999 > 1000 já teria cancelado); `Advance(1 ms)` → `Timeout`.
- `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`: `HangsUntilCancelled()`; o teste cancela o próprio `CancellationTokenSource` depois do início, sem avançar o relógio; a chamada lança `OperationCanceledException` (não `BrainException`); `Tokens[0].IsCancellationRequested` é `true`. A espera pela tarefa usa `WaitAsync(TimeSpan.FromSeconds(10))`, como os outros testes de tempo: sob a mutação do Red (o cancelamento do chamador não chega ao runner, o fake nunca termina), o teste falha com `TimeoutException` em 10 s em vez de travar a suíte (R2-S3). A asserção do tipo da exceção distingue esse `TimeoutException` do `OperationCanceledException` esperado.
- `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`: `TimeoutSeconds = 1`; `HangsUntilCancelled(onCancelled: () => callerCts.Cancel())`; `OnRun` sinaliza o início; `Advance(1000 ms)` → o tempo estoura, o token ligado é cancelado, o fake cancela o token do chamador e só então lança. Então: a chamada lança `OperationCanceledException` (não `BrainException`, nem com `Kind == Timeout`); `callerCts.IsCancellationRequested` é `true`; `Tokens[0].IsCancellationRequested` é `true`; o arquivo temporário foi apagado. Espera com `WaitAsync(TimeSpan.FromSeconds(10))`. Sem a parte `!ct.IsCancellationRequested` do filtro, o resultado seria `BrainException(Timeout)` e o teste falha.
- `RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession` (casos `CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout` com mensagem `failure <kind> t1`; `Unknown` com `Claude Code failed (exit code 1): boom t1`): entrada `first sentence t1`, `second sentence t2`, `/quit`; o cérebro lança a `BrainException` e depois devolve `ConsoleTutorReplies.Valid("t2")`. Então: `Error` contém `The tutor could not answer: <mensagem>` e `You can send a sentence again or type /quit to exit.`; `Error` não contém `An unexpected error occurred. See the log file for details.`; `_brain.Requests` tem 2 itens; o 2º tem **uma** mensagem só (o turno que falhou não entrou na janela, P11 da US-004) e ela contém `second sentence t2`; `Output` tem uma linha que termina com `Tutor: Reply t2?`; código de saída 0.
- `RunAsync_WhenClaudeExecutableIsNotFound_ShowsCliNotFoundInstructionAndContinues`: sem trocar o `ITutorBrain`; `ConfigureServices` troca o `IProcessRunner` por `MissingExecutableProcessRunner` e o `ITutorPromptSource` por `FixedTutorPromptSource`; configuração com `Brain:ClaudeCode:ExecutablePath = claude-missing-t1` e `Brain:ClaudeCode:WorkingDirectory` numa pasta temporária do teste; entrada `sentence t1`, `sentence t2`, `/quit`. Então: `Error` contém `The tutor could not answer: Claude Code CLI not found ("claude-missing-t1")`, `irm https://claude.ai/install.ps1 | iex` e `Brain:ClaudeCode:ExecutablePath`; o runner foi chamado 2 vezes; código 0; nenhum `An unexpected error occurred`.
- `RunAsync_WhenBrainFails_LogsBrainFailedWarningWithKind`: `BrainException(UsageLimitReached, "limit t1")`, pasta de log do teste; um evento `BrainFailed` com `EventId.Id == 1005`, `@l == "Warning"`, `ErrorKind == "UsageLimitReached"` e `@x` contendo `limit t1`; nenhum evento `UnexpectedError`; `ApplicationStopped` com `ExitCode == 0`.
- `RunAsync_WhenTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt`: `Brain:ClaudeCode:TimeoutSeconds = 0`; código `ExitCodes.InvalidConfiguration`; `Error` contém `Invalid configuration:` e `Brain:ClaudeCode:TimeoutSeconds must be between 1 and 3600 (found 0)`; `Output` vazio.
- `HandleAsync_WhenBrainFails_WritesMessageAndNextStepToErrorOnly`: `BrainException(NotAuthenticated, "not logged in t1")`; as linhas de `Error` são exatamente as 2 da §2.6; `Output` vazio.
- `HandleAsync_WhenFailureIsNotBrainException_PropagatesIt`: a exceção lançada pelo cérebro sai de `HandleAsync` (mesmo tipo); `Error` vazio.
- `Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue`: uma falha, igual à mensagem da §2.1 com o valor do caso.
- `RunAsync_WhenExecutableDoesNotExist_ThrowsWin32ExceptionWithFileNotFoundCode`: `ProcessRunner` real com `FileName` = nome aleatório inexistente; `Win32Exception` com `NativeErrorCode == 2`.
- `RunAsync_WhenCancelled_KillsWholeProcessTree`: `ProcessRunner` real com `cmd.exe /c powershell.exe -NoProfile -NonInteractive -Command "<grava started; Start-Sleep 3; grava finished>"` (o `powershell` é **neto** do runner) numa pasta temporária; o teste espera o arquivo `started` (até 20 s), cancela, recebe `OperationCanceledException`, espera 6 s e confere que `finished` **não** existe.

**Contagem.** Fora de `LiveBrain`: Core.Tests 134 (sem mudança); Console.Tests 41 → **51** (−1 removido; +5, +1, +1, +1, +1, +2); Infrastructure.Tests 74 → **101** (`ClaudeCodeTutorBrainTests` 31 → 53 casos e 20 → 29 métodos: −1 removido, +1 substituto, +1 S2, +2, +1, +1, +2, +1, +12, +1, +1; `ClaudeCodeOptionsValidatorTests` +5). Total **286**. `LiveBrain`: +2 (`ProcessRunnerLiveTests`).

## 5. Tarefas (ordem de execução TDD)

**Pré-condição** (antes da T1): o orquestrador aplica a §2.8 em dois commits, `docs(ai): update spec for US-005` e `docs(ai): update architecture for US-005`, com o ADR-008 em "Aceito", se aprovado. Na T1, antes do primeiro teste, o Executor roda o **D16c** e cola o resultado nas Evidências.

**Mutações depois do Green (S3).** Quando um teste nasce verde porque o esqueleto do Red ainda não tem a regra (validador sem a faixa, `Classify` devolvendo sempre `Unknown`), a mutação não tem o que quebrar no Red. Nesses casos a tarefa diz "mutação depois do Green": o Executor faz o commit `feat`, aplica a mutação pelo efeito descrito, roda o teste e vê falhar, desfaz pela edição inversa (sem commit), confere que `git status --short` não mostra mudança em `src/` e cola o resultado nas Evidências (AP-017, AP-029, AP-037).

- [ ] T1 — `TimeoutSeconds`.
  Red: esqueleto `public int TimeoutSeconds { get; set; }` (sem inicializador) e as três constantes; testes `ClaudeCodeOptionsTests.Constructor_Always_UsesDocumentedDefaults` (alterado), `ClaudeCodeOptionsValidatorTests.Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue`, `ClaudeCodeOptionsValidatorTests.Validate_WithTimeoutSecondsAtLimits_Succeeds` e `EnglishTutorAppTests.RunAsync_WhenTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt`. Falham pela asserção (padrão 0; nenhuma falha de validação; o app abre a sessão e sai com 0), exceto `Validate_WithTimeoutSecondsAtLimits_Succeeds`, que nasce verde porque o validador ainda não confere a faixa.
  Green: inicializador 60, regra do validador, `"TimeoutSeconds": 60` no `appsettings.json`.
  Mutação depois do Green (S3), para `ClaudeCodeOptionsValidatorTests.Validate_WithTimeoutSecondsAtLimits_Succeeds`: "o limite inferior aceito passa a ser 2" (o caso 1 falha) e, separadamente, "o limite superior aceito passa a ser 3599" (o caso 3600 falha); cada uma desfeita pela edição inversa.
  Commits: `test(brain): cover TimeoutSeconds default and range` + `feat(brain): add Brain:ClaudeCode:TimeoutSeconds with validation`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T2 — Tempo máximo por chamada.
  Red: pacote `Microsoft.Extensions.TimeProvider.Testing` (§6); `FakeProcessRunner.HangsUntilCancelled(Action? onCancelled = null)` (§2.9); `_timeProvider` passa a `FakeTimeProvider`; `ClaudeCodeFailures` com `Timeout(int, OperationCanceledException)` lançando `NotImplementedException`; sai `ClaudeCodeTutorBrainTests.SendAsync_Always_PassesCallerTokenToProcessRunner`; testes `ClaudeCodeTutorBrainTests.SendAsync_WhenProcessDoesNotAnswerWithinTimeout_CancelsProcessAndThrowsTimeout`, `ClaudeCodeTutorBrainTests.SendAsync_OnEachCall_StartsTimeoutAgain` e `ClaudeCodeTutorBrainTests.SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` (falham pelo `WaitAsync` de 10 s: o adaptador ainda não tem relógio, o `Advance` não cancela nada e o fake não termina) e `ClaudeCodeTutorBrainTests.SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`, que nasce verde (o token do chamador ainda vai direto ao runner): Red por mutação temporária pelo efeito, aplicada no próprio Red porque o repasse do token já existe, "o cancelamento do chamador deixa de chegar ao runner" (o fake não termina e o teste falha pelo `WaitAsync` de 10 s, com `TimeoutException`, sem travar a suíte; R2-S3), desfeita pela edição inversa.
  Green: §2.2 (só o `catch` de `OperationCanceledException`, com o filtro completo; o de `Win32Exception` é da T3 e, até lá, a `Win32Exception` continua propagando como na US-004) e a mensagem `Timeout` da §2.5.
  Mutação depois do Green (S2, S3), para `ClaudeCodeTutorBrainTests.SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`: "a condição do chamador deixa de contar no filtro do `Timeout`" (o teste recebe `BrainException(Timeout)` e falha), desfeita pela edição inversa.
  Refactor: apagar `ManualTimeProvider.cs` (Grep sem outro uso).
  Commits: `test(brain): cover Claude Code timeout and caller cancellation` + `feat(brain): stop Claude Code after TimeoutSeconds` + `refactor(brain): replace ManualTimeProvider with FakeTimeProvider`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T3 — Executável que não pode ser iniciado.
  Red: `FakeProcessRunner.Throws(Exception)`; `ClaudeCodeFailures.StartFailed(string, Win32Exception)` lançando `NotImplementedException`; testes `ClaudeCodeTutorBrainTests.SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey` e `ClaudeCodeTutorBrainTests.SendAsync_WhenExecutableCannotBeStartedForOtherReason_ThrowsUnknownWithErrorCode` (falham: a `Win32Exception` propaga).
  Green: `catch (Win32Exception)` da §2.2, tabela da §2.3, mensagens da §2.5; comentários de `IProcessRunner.cs` e `ProcessRunner.cs` (§2.3, §2.9).
  Commits: `test(brain): cover missing Claude Code executable` + `feat(brain): classify missing Claude Code CLI as CliNotFound`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T4a — Gravação das fixtures de erro (sem código de produção).
  Rodar `claude auth --help` e `claude --help` e colar nas Evidências o trecho que mostra `auth login` (P2); se `claude auth login` não existir, parar e registrar na §12. Gravar `not-authenticated` e `unknown-model` com o bloco da §2.7 (mesmos argumentos de isolamento do adaptador, S4); registrar nas Evidências o código de saída de `unknown-model` e a linha da §2.4 em que ela cai (1 ou 3; linha 2 = parar, §2.7; R2-S2); montar `usage-limit-synthetic-1` e `usage-limit-synthetic-2`; escrever o `README.md`, com a seção "Diferenças em relação à chamada do adaptador" (§2.7, P18); rodar o D14.
  Commit: `test(brain): record Claude Code error fixtures`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T4b — Classificação por texto.
  Red: `ClaudeErrorFixtures` (Support); `ClaudeCodeFailures.Classify` devolvendo sempre `BrainErrorKind.Unknown` e `NotAuthenticated(string)`/`UsageLimitReached(string)` lançando `NotImplementedException`; testes `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin`, `ClaudeCodeTutorBrainTests.SendAsync_WithUsageLimitOutput_ThrowsUsageLimitReached` e `ClaudeCodeTutorBrainTests.SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` (falham, exceto os casos 9 e 10, que esperam `Unknown` e nascem verdes porque o esqueleto já devolve `Unknown`; os casos 11 e 12 falham), e `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail`, que nasce verde pelo mesmo motivo (caracterização).
  Green: §2.4 e as duas mensagens da §2.5; comentário do `ClaudeCodeOutput.cs` (§2.9). Se o texto gravado não casar com a linha a, ver §2.4 (padrão novo, com §12).
  Mutações depois do Green (S3), cada uma desfeita pela edição inversa: "a linha 2 passa a ser classificada" (caso 9 de `ClaudeCodeTutorBrainTests.SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` falha); "`rate_limit` passa a contar como limite de uso" (caso 10 falha); "quando nenhum padrão casa (ordem c da §2.4), o `Classify` passa a devolver `NotAuthenticated` em vez de `Unknown`" (R2-S2: vale para as linhas 1 e 3 ao mesmo tempo, então atinge `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail` qualquer que seja a linha registrada na T4a; o teste falha pela asserção de `Kind`, não por `NotImplementedException`; o caso 10 também falha, o que é esperado). Nas Evidências, o Executor cita a linha registrada na T4a ao lado do resultado dessa mutação.
  Commits: `test(brain): cover Claude Code failure classification` + `feat(brain): classify not-authenticated and usage-limit failures`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T5 — Console: a falha do cérebro não encerra a sessão.
  Red: `ConsoleText.BrainFailed` e `AppLog.BrainFailed` lançando `NotImplementedException` (o gerador de `LoggerMessage` exige `partial`: o esqueleto do log é o próprio método parcial, sem chamada ainda); construtor novo do `ConversationInputHandler` (guarda `error` e `logger`, ainda sem `catch`); **`EnglishTutorApp.RunSessionAsync` passa a construir o handler com `options.Error` e o logger (§2.6), sem `catch` e sem mudar comportamento, para o Red compilar (S7)**; `MissingExecutableProcessRunner`; atualizar o construtor em `ConversationInputHandlerTests`; sai `EnglishTutorAppTests.RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError` com os dois comentários; testes `EnglishTutorAppTests.RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession`, `EnglishTutorAppTests.RunAsync_WhenClaudeExecutableIsNotFound_ShowsCliNotFoundInstructionAndContinues`, `EnglishTutorAppTests.RunAsync_WhenBrainFails_LogsBrainFailedWarningWithKind`, `ConversationInputHandlerTests.HandleAsync_WhenBrainFails_WritesMessageAndNextStepToErrorOnly` (falham: a exceção propaga e o app sai com 1) e `ConversationInputHandlerTests.HandleAsync_WhenFailureIsNotBrainException_PropagatesIt`, que nasce verde: Red por mutação temporária pelo efeito, aplicada no próprio Red (ela acrescenta comportamento, não depende da regra), "o handler passa a capturar qualquer exceção" (os 2 casos falham), desfeita pela edição inversa.
  Green: §2.6; comentário do `ConversationInputHandler.cs` (§2.9).
  Commits: `test(console): cover brain failures that keep the session` + `feat(console): show brain failures and keep the session`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T6 — Contrato real do `ProcessRunner` (`LiveBrain`, caracterização).
  Testes `ProcessRunnerLiveTests.RunAsync_WhenExecutableDoesNotExist_ThrowsWin32ExceptionWithFileNotFoundCode` e `ProcessRunnerLiveTests.RunAsync_WhenCancelled_KillsWholeProcessTree`, com `[Trait("Category", "LiveBrain")]` (P7). Nascem verdes. Red por mutação temporária pelo efeito (o runner real já existe, então a mutação vale aqui), desfeita pela edição inversa: "a falha de início deixa de chegar ao chamador como `Win32Exception`" (1º teste falha) e "o encerramento deixa de incluir os processos netos" (o arquivo `finished` aparece e o 2º falha). Rodar o D10.
  Commit: `test(infra): cover ProcessRunner start failure and process tree kill`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T7 — DoD e evidências.
  Rodar D1–D17 (D16 depois de todo o código), colar nas Evidências com data e hash, atualizar as contagens da §4 e de D2/D8 se algo mudou (AP-031).
  Commit: `docs(ai): add US-005 evidence`.
  Escolhas de forma: nenhuma | §12 em <hash>

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| `Microsoft.Extensions.TimeProvider.Testing` (última estável 10.x; versão fixada em `Directory.Packages.props`, grupo `Tests`) | `EnglishTutor.Infrastructure.Tests` | `FakeTimeProvider` com temporizadores: o `CancellationTokenSource(TimeSpan, TimeProvider)` dispara no `Advance`, sem relógio real (ARCHITECTURE §10; P8) |

Nenhum pacote novo em `src/`.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Os textos da CLI mudam e a classificação deixa de reconhecer a falha | Cai em `Unknown`, que mostra o detalhe e mantém a sessão. Fixture gravada de falta de login com `claude --version` no `README.md`; regravar ao atualizar a CLI (ADR-008). Na US-007, um limite não reconhecido não aciona o fallback (efeito registrado no ADR-008) |
| A fixture de limite de uso é sintética e o texto real não casa | Dois padrões e duas formas (resultado JSON e stderr); `README.md` manda trocar na primeira ocorrência real (P4, decisão do usuário) |
| A fixture gravada difere da chamada real do adaptador (stderr com avisos de MCP ou de configuração do usuário) | Gravação com os mesmos argumentos de isolamento do adaptador; diferenças restantes (modo Tags, prompt-modelo, sem `--resume`) registradas no `README.md` (§2.7, P18) |
| A fixture `unknown-model` cai numa linha da §2.4 que a mutação escolhida não atinge (R2-S2) | A mutação da T4b muda o resultado de `Classify` sem padrão, que vale para as linhas 1 e 3; a linha real fica registrada na T4a; linha 2 = parar e registrar na §12 (§2.7) |
| `CLAUDE_CONFIG_DIR` vazio não produz erro de autenticação no Windows (credenciais em outro lugar) ou a CLI fica esperando | `timeout 120` no Bash; segunda tentativa com `ANTHROPIC_API_KEY` inválida; se falhar, parar e registrar (§2.7, P3) |
| `claude auth login` não existe na versão instalada | Conferido na T4a; se não existir, o usuário decide o texto na §12 antes da T4b (P2) |
| Teste com `HangsUntilCancelled` trava a suíte no Red (pela falta do relógio ou pela mutação) | `WaitAsync(TimeSpan.FromSeconds(10))` na espera da tarefa nos quatro testes que usam `HangsUntilCancelled`, inclusive o de caracterização `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` (§4, R2-S3) |
| O teste dos dois cancelamentos (S2) depende da ordem entre o cancelamento do chamador e a exceção | O `onCancelled` do fake roda antes de a exceção sair do fake (§2.9): ordem determinística, sem corrida de thread |
| `ProcessRunnerLiveTests` depende de `cmd.exe` e `powershell.exe` e de tempo real | Fica em `LiveBrain` (fora do D2); 3 rodadas seguidas no D10; espera do `started` até 20 s antes de cancelar |
| D11 (1 s) recebe uma resposta antes do limite | Improvável: só o início do processo leva mais de 1 s (US-004 D7: média de 3,6 a 5,0 s). Se acontecer, repetir uma vez e registrar |
| D13 (`LiveBrain` de fumaça) fica vermelho por formato, sem relação com esta US (prompts e parser não mudam) | Mesmo critério da US-004 (camada do adaptador, tolerância zero no formato, ≥ 4/5 correções), 3 rodadas seguidas. Vermelho: o Executor para e classifica a causa na §12; o usuário decide, com registro prévio na spec, se muda camada ou tolerância (AP-032; risco residual da US-004) |
| Um turno com retentativa pode levar 2 × `TimeoutSeconds` | Registrado no ADR-008 e na P5 |

## 8. Fora de escopo

- Fallback por `BrainErrorKind` e o aviso de troca de professor: **US-007** (cenários "fallback por limite de uso" e "sem fallback configurado" já estão na spec; o segundo é atendido pelo console desta US sem mudança). Nada novo a registrar na spec da US-007: falhas de início com código diferente de 2 e 3 viram `Unknown` e, pela RN-BRAIN08 como está escrita, não acionam o fallback (limite aceito, P9, ADR-008).
- Mensagens do Ollama: **US-006** (cenários "Ollama indisponível" e "modelo não baixado" já estão na spec). O console desta US mostra qualquer `BrainException`, então o OllamaTutorBrain só precisa escrever as próprias mensagens. Nada novo a registrar.
- Classificação de falhas na suíte de contrato (US-008): **não prevista**. A suíte cobre o contrato comum (`TutorReply`); as falhas e os textos são de cada provedor. Nenhum adiamento.
- Variáveis de ambiente por chamada no `IProcessRunner` e limpeza do ambiente herdado: risco aceito pelo usuário na US-004 (AP-034), sem mudança. Os comandos de DoD com processo real limpam as variáveis. A mensagem de `NotAuthenticated` menciona `ANTHROPIC_API_KEY` sem ler o ambiente (P17).
- Troca da fixture sintética de limite de uso pela gravada: sem US de destino; condição registrada no `README.md` e no ADR-008 (P4).

Adiamentos procurados também nas P* e nos comentários de teste (AP-040): nenhum "a US-YYY troca/atualiza" nesta US.

## 9. Perguntas em aberto

- **P1 — Onde moram as mensagens ao aluno.** **Padrão proposto:** no adaptador, como `BrainException.Message` (o que houve e o que fazer); o console só acrescenta o prefixo e a linha de próximo passo (ADR-008, itens 3 e 4). Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey` e `EnglishTutorAppTests.RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession`.
- **P2 — Instrução de instalação e `claude auth login`.** O cenário pede "claude auth login". **Padrão proposto:** manter o texto da spec, conferido na T4a com `claude auth --help`; instalação pelo instalador nativo (`irm https://claude.ai/install.ps1 | iex`), o único suportado (ADR-007). Se a CLI instalada não tiver `auth login`, o Executor para e o usuário decide o texto na §12. Teste: `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin`.
- **P3 — Lista de padrões de texto.** **Padrão proposto:** a da §2.4; se a gravação real de falta de login não casar, o trecho observado entra na lista (§12). Se não for possível gravar uma falta de login real, o usuário decide entre fixture sintética (com mudança do D5 na spec) ou outra forma de gravação. Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource`.
- **P4 — "Fixtures gravadas" para limite de uso.** O D5 da spec pede fixtures gravadas, e o limite de uso não é reproduzível sob demanda. **Padrão proposto:** fixtures sintéticas marcadas no `README.md` e o D5 reescrito (§2.8); troca pela gravada na primeira ocorrência real, por decisão do usuário, sem US de destino. Teste: `ClaudeCodeTutorBrainTests.SendAsync_WithUsageLimitOutput_ThrowsUsageLimitReached`.
- **P5 — "Tempo máximo de resposta" (RN-BRAIN09): por chamada ou por turno.** **Padrão proposto:** por chamada ao processo; a retentativa do ADR-006 tem o próprio limite (turno com retentativa: até 2 × 60 s). Sem mudança no texto da regra; registrado no ADR-008. Teste: `ClaudeCodeTutorBrainTests.SendAsync_OnEachCall_StartsTimeoutAgain`.
- **P6 — Faixa de `TimeoutSeconds`.** A spec só fixa o padrão (60). **Padrão proposto:** 1 a 3600 (1 é o valor do cenário; 3600 evita valores sem sentido e fica muito abaixo do máximo do `CancellationTokenSource`). Teste: `ClaudeCodeOptionsValidatorTests.Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue` e `ClaudeCodeOptionsValidatorTests.Validate_WithTimeoutSecondsAtLimits_Succeeds`.
- **P7 — Categoria do teste com processo real que não é cérebro.** ARCHITECTURE §10 proíbe processo real na suíte rápida, e `LiveBrain` é a única categoria excluída do D2 (CLAUDE.md, hooks). **Padrão proposto:** `ProcessRunnerLiveTests` com `[Trait("Category", "LiveBrain")]` e um comentário dizendo que não usa a CLI; nenhuma categoria nova. Teste: `ProcessRunnerLiveTests.RunAsync_WhenCancelled_KillsWholeProcessTree`.
- **P8 — Relógio falso com temporizadores.** **Padrão proposto:** `FakeTimeProvider` (pacote de teste, §6) no lugar do `ManualTimeProvider`, que é apagado. Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenProcessDoesNotAnswerWithinTimeout_CancelsProcessAndThrowsTimeout`.
- **P9 — Quais `NativeErrorCode` são "não encontrado".** **Padrão proposto:** 2 e 3 → `CliNotFound`; os outros (ex.: 5, acesso negado; 193, não é executável) → `Unknown` com "could not be started" e o código. Limite aceito (AP-040), consumidores e efeito: a mensagem ao aluno (diz o código e a chave `Brain:ClaudeCode:ExecutablePath`; a sessão continua); o log `BrainFailed` (registra `Unknown`); o fallback da **US-007**: a RN-BRAIN08 aciona o fallback para `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated`, não para `Unknown`, então um `ExecutablePath` que aponta para algo que existe mas não pode ser executado **não aciona o fallback** (registrado no ADR-008, "Consequências"; sem mudança na RN-BRAIN08 nem na spec da US-007, S6); o D6 (usa um nome inexistente, código 2, sem efeito). Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey` e `ClaudeCodeTutorBrainTests.SendAsync_WhenExecutableCannotBeStartedForOtherReason_ThrowsUnknownWithErrorCode`.
- **P10 — Linha 2 e `rate_limit`.** **Padrão proposto:** a linha 2 (código 0 e saída que não é o JSON `type: result`) não é classificada; `rate_limit` (limite momentâneo da API) fica `Unknown`, para não acionar o fallback da US-007 por um problema passageiro. Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` (casos 9 e 10).
- **P11 — Onde a falha é registrada no log.** **Padrão proposto:** no console (`AppLog.BrainFailed`, 1005, `Warning`, com `ErrorKind` e a exceção), porque é ele que encerra o tratamento; o adaptador não ganha evento de falha. Teste: `EnglishTutorAppTests.RunAsync_WhenBrainFails_LogsBrainFailedWarningWithKind`.
- **P12 — Texto e canal no console.** **Padrão proposto:** as 2 linhas da §2.6 na saída de erro (mesmo canal de `Invalid configuration:` e `An unexpected error occurred...`, AP-024); nada na saída padrão. Teste: `ConversationInputHandlerTests.HandleAsync_WhenBrainFails_WritesMessageAndNextStepToErrorOnly`.
- **P13 — Detalhe mostrado × texto classificado.** A classificação examina o `result` de erro e o stderr inteiro; o detalhe da mensagem segue a regra da US-004 (o `result` de erro; senão a primeira linha não vazia do stderr). **Padrão proposto:** manter as duas regras (o detalhe não muda para `Unknown`, que já tem testes); limite aceito. Consumidores: as mensagens de `NotAuthenticated` e `UsageLimitReached` (podem mostrar uma linha ou um `result` sem relação com o padrão que casou; o tipo e a ação continuam certos); o fallback da US-007 (usa só o tipo, sem efeito); o D12 (confere `claude auth login`, que é texto do projeto, sem efeito). Teste: `ClaudeCodeTutorBrainTests.SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` (casos 2, 11 e 12).
- **P14 — `InvalidOutput` no console.** O `ConversationService` nunca deixa o `InvalidOutput` sair (retentativa e fallback cru, ADR-006). **Padrão proposto:** o console captura qualquer `BrainException`, sem ramo para `InvalidOutput`; nenhum teste de console com esse tipo. Sem efeito observável.
- **P15 — Filtro do D5 da spec.** O comando da spec (`FullyQualifiedName~ClaudeCode`) também roda os testes `LiveBrain` do Claude Code (assinatura, rede). **Padrão proposto:** acrescentar `Category!=LiveBrain&` no texto novo do D5 (§2.8); os testes reais ficam no D10–D13. Sem efeito observável.
- **P16 — Rodadas do `LiveBrain` de fumaça (D13).** **Padrão proposto:** 3 rodadas seguidas dos 3 testes de `ClaudeCodeTutorBrainLiveTests`, mesmo critério da US-004 (a US não muda prompt nem parser; o objetivo é provar que o relógio novo não atrapalha a chamada real). Teste: `ClaudeCodeTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat`.
- **P17 — `ANTHROPIC_API_KEY` na mensagem de `NotAuthenticated` (S8).** O padrão `invalid api key` pode vir de uma `ANTHROPIC_API_KEY` inválida herdada do ambiente (o filho herda o ambiente, AP-034); nesse caso `claude auth login` sozinho pode não resolver. **Padrão proposto (decidido):** a mensagem sempre acrescenta `If the ANTHROPIC_API_KEY environment variable is set, check or remove it.` (§2.5), sem ler o ambiente e sem ramo por padrão: uma frase fixa custa pouco ao aluno sem a variável e evita que o aluno com a variável fique preso. A spec não muda (o cenário exige só `claude auth login`, que continua na mensagem). Descartado: mencionar só quando a variável existe (exigiria ler o ambiente, que o `IProcessRunner` não expõe) ou só quando casou `invalid api key` (um ramo a mais; a variável inválida pode produzir outros textos). Teste: `ClaudeCodeTutorBrainTests.SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin`.
- **P18 — Argumentos da gravação das fixtures (S4).** **Padrão proposto:** o bloco da §2.7 usa os mesmos argumentos de isolamento do adaptador (RN-BRAIN06: pasta vazia, `--system-prompt-file`, `--tools ""`, `--disallowedTools "mcp__*"`, `--strict-mcp-config`, `--permission-mode dontAsk`, `--model`, `--output-format json`); as diferenças que sobram (modo Tags sem `--json-schema`, prompt-modelo sem renderizar, sem `--resume`, entrada de uma linha) ficam registradas no `README.md` das fixtures. Sem efeito observável (muda só como as fixtures são gravadas; os testes que as leem estão na P3).

## 10. Definition of Done (comandos verificáveis)

Ambiente "limpo" (D11–D13): no Windows PowerShell 5.1, antes dos comandos, `Remove-Item Env:CLAUDECODE, Env:ANTHROPIC_BASE_URL -ErrorAction SilentlyContinue; Get-ChildItem Env:CLAUDE_CODE_* | Remove-Item` (US-004, AP-034).

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: 286 casos (Core.Tests 134, Console.Tests 51, Infrastructure.Tests 101), em < 30 s, sem rede, sem GPU e sem processo real |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | (spec, texto novo da §2.8) `dotnet test --filter "Category!=LiveBrain&FullyQualifiedName~ClaudeCode"` | verde; inclui um teste por tipo: `CliNotFound` (`SendAsync_WhenExecutableIsNotFound_ThrowsCliNotFoundWithInstallInstructionAndKey`), `NotAuthenticated` (`SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin`, gravado), `UsageLimitReached` (`SendAsync_WithUsageLimitOutput_ThrowsUsageLimitReached`, sintético), `Timeout` (`SendAsync_WhenProcessDoesNotAnswerWithinTimeout_CancelsProcessAndThrowsTimeout`), `Unknown` (`SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail`, gravado) |
| D6 | (spec) No Windows PowerShell 5.1: `$env:Brain__ClaudeCode__ExecutablePath="nao-existe"; "hi t1", "hi t2", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__ClaudeCode__ExecutablePath` | duas vezes `The tutor could not answer: Claude Code CLI not found ("nao-existe")...` com `irm https://claude.ai/install.ps1 \| iex` e `Brain:ClaudeCode:ExecutablePath`, cada uma seguida de `You can send a sentence again or type /quit to exit.`; nenhum `An unexpected error occurred`; `exit=0` (a sessão continuou até o `/quit`). O pipe no PowerShell 5.1 é a cobertura exigida pelo `padroes-codigo` (AP-006); o tratamento do BOM não é observável nesta saída e continua provado pelo teste da US-001 |
| D7 | (spec) `dotnet test --filter "FullyQualifiedName~EnglishTutorAppTests"` | verde; `RunAsync_WhenBrainFails_ReturnsOneAndShowsUnexpectedError` não existe mais e `RunAsync_WhenBrainFails_ShowsErrorAndContinuesSession` está na lista (5 casos); conferência de nome no D16 |
| D8 | `dotnet test --filter "FullyQualifiedName~ClaudeCodeTutorBrainTests"` | 53 casos verdes (29 métodos); os testes `LiveBrain` têm outra classe e não entram |
| D9 | `Select-String -Path src/EnglishTutor.Console/appsettings.json -Pattern '"TimeoutSeconds": 60'` | uma linha, dentro de `Brain:ClaudeCode` (RN-BRAIN09) |
| D10 | `1..3 \| ForEach-Object { dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ProcessRunnerLiveTests"; "round $_ exit=$LASTEXITCODE" }` | 3 linhas `round <n> exit=0` (2 testes por rodada; não usa a CLI nem a assinatura) |
| D11 | No Windows PowerShell 5.1, ambiente limpo: `$env:Brain__ClaudeCode__TimeoutSeconds="1"; "hi t1", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__ClaudeCode__TimeoutSeconds` | `The tutor could not answer: Claude Code did not answer in time (limit: 1 s) and was stopped...`; nenhum `An unexpected error occurred`; `exit=0` |
| D12 | No Windows PowerShell 5.1, ambiente limpo: `$env:CLAUDE_CONFIG_DIR = (New-Item -ItemType Directory -Force "$env:TEMP\englishtutor-noauth-$(Get-Random)").FullName; "hi t1", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:CLAUDE_CONFIG_DIR` | `The tutor could not answer: Claude Code is not logged in (...). Run "claude auth login"...` e `If the ANTHROPIC_API_KEY environment variable is set, check or remove it.`; `exit=0`. Se a T4a precisou de `ANTHROPIC_API_KEY` inválida, o comando também a define e a remove no fim |
| D13 | Ambiente limpo: `1..3 \| ForEach-Object { dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ClaudeCodeTutorBrainLiveTests"; "round $_ exit=$LASTEXITCODE" }` | 3 linhas `round <n> exit=0`. Camada: o adaptador, sem a retentativa do ADR-006; tolerância zero no formato e ≥ 4/5 correções (critério da US-004); 3 rodadas seguidas. Vermelho: parar, classificar na §12; o usuário decide, com registro prévio na spec (P16, §7) |
| D14 | `Select-String -Path tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/errors/* -Pattern 'Users','@','sk-ant'` | nenhuma linha (sem caminho pessoal, e-mail ou chave); também sem o nome do titular da conta (inspeção) |
| D15 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D16 | (PowerShell) bloco D16 abaixo, depois da T5 | primeira linha `named=1` (o `ARCHITECTURE.md` foi varrido); depois, **nenhuma linha** |
| D16c | (controle do D16, na T1, antes do primeiro teste) `git grep` do bloco D16c abaixo | pelo menos uma linha para cada padrão, nos arquivos listados abaixo do bloco (prova que padrões e caminhos enxergam o texto antigo; o D16 só vale depois dele, AP-035) |
| D17 | `git status --short` após D2–D16 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |

D15 (fora da tabela por causa do `|`; forma única do `checklist-revisao` §4, AP-019):

```bash
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
```

D16 (PowerShell). Inclui o nome antigo do teste substituído, o teste do token e as variações dos comentários de adiamento (AP-035):

```powershell
$patterns = 'CliNotFound is US-005', 'until US-005', 'belongs to US-005', 'falha do cérebro até a US-005', 'Substituído na US-005', 'ReturnsOneAndShowsUnexpectedError', 'PassesCallerTokenToProcessRunner', 'Até a US-005', 'a US-005 a classifica', 'ManualTimeProvider'
$files = @(Get-ChildItem -Recurse -File -Path src, tests -Include *.cs | Where-Object { $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*' }) + @(Get-Item ARCHITECTURE.md)
"scanned=$($files.Count) named=$(@($files | Where-Object Name -eq 'ARCHITECTURE.md').Count)"
$files | Select-String -SimpleMatch -Pattern $patterns
```

D16c (controle; usa o `develop` de partida, `f033915`, e por isso vale mesmo depois do commit de arquitetura):

```powershell
git grep -n -F -e 'CliNotFound is US-005' -e 'until US-005' -e 'belongs to US-005' -e 'falha do cérebro até a US-005' -e 'Substituído na US-005' -e 'ReturnsOneAndShowsUnexpectedError' -e 'PassesCallerTokenToProcessRunner' -e 'Até a US-005' -e 'a US-005 a classifica' -e 'ManualTimeProvider' f033915 -- src tests ARCHITECTURE.md
```

Esperado no D16c: `ProcessRunner.cs` (`CliNotFound is US-005`), `ConversationInputHandler.cs` (`until US-005`), `ClaudeCodeOutput.cs` (`belongs to US-005`), `EnglishTutorAppTests.cs` (`falha do cérebro até a US-005`, `Substituído na US-005`, `ReturnsOneAndShowsUnexpectedError`), `ClaudeCodeTutorBrainTests.cs` (`PassesCallerTokenToProcessRunner`, `ManualTimeProvider`), `ManualTimeProvider.cs` (`ManualTimeProvider`) e `ARCHITECTURE.md` (`Até a US-005`, `a US-005 a classifica`). Os documentos históricos (`docs/us/US-004.md`, `tasks/US-004/*`) não são varridos (§2.8).

Também entram, da DoD comum:

- todo cenário BDD com teste escrito antes do código;
- `review.md` sem bloqueantes;
- `docs/us/US-005.md` criado;
- ADR-008 aceito;
- `ARCHITECTURE.md` §3/§6.3, cabeçalho do ADR-007 e D5 da US-005 no `SPRINT_1.md` atualizados antes da T1 (§2.8);
- merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-20 — Usuário (no chat)

> "Aprovado, incorpore todas as sugestões S1–S8"

Plano aprovado com a condição de incorporar as sugestões S1 a S8 de `tasks/US-005/plan-review.md` (Rodada 1). Os padrões propostos para P1 a P16 foram aceitos, inclusive a mudança do D5 na spec (P4 e P15).

→ Incorporado. Resposta item a item abaixo, no formato "item → seções tocadas" (AP-018). Contagens novas: `ClaudeCodeTutorBrainTests` 52 casos / 29 métodos; Infrastructure.Tests 100; Console.Tests 51 (sem mudança); D2 285. P17 e P18 são novas (S8 e S4); P1–P16 continuam como aceitas (P9 e P13 só ganharam texto de consumidores e casos). (Contagens superadas pela resposta à Rodada 2, abaixo.)

- **S1:** incluir um caso de classificação em que o `result` de erro não casa com nenhum padrão e o stderr casa, para provar as duas fontes.
  → Incorporado. S1 → §2.4 (frase "sempre as duas fontes" e "cada fonte tem um caso em que só ela casa"), §4 (`[Theory]` de 10 para 11 casos; caso 11: código 1, erro-json `Server error t11`, stderr `Not logged in t11` → `NotAuthenticated`, trecho `is not logged in (Server error t11)`; contagem), §5 T4b (o caso 11 falha no Red), §9 P13 (casos 2 e 11), §10 D2/D8, ADR-008 item 1 ("sempre as duas fontes").
- **S2:** incluir um teste com o cancelamento do chamador e o tempo estourado ao mesmo tempo, provando a parte `!ct.IsCancellationRequested` do filtro, ou retirar a afirmação do plano.
  → Incorporado com teste. S2 → §2.2 (bullet do cancelamento cita os dois testes), §2.9 e §3 (`FakeProcessRunner.HangsUntilCancelled(Action? onCancelled = null)`, com o `onCancelled` rodando antes de a exceção sair do fake), §4 (linha e asserção de `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`; contagem), §5 T2 (Red falha pelo `WaitAsync`; mutação depois do Green "a condição do chamador deixa de contar no filtro"), §7 (risco de ordem), §10 D2/D8, ADR-008 item 2 ("inclusive quando o tempo também estourou").
- **S3:** a T1 e a T4b devem dizer que as mutações são aplicadas depois do Green commitado e desfeitas pela edição inversa.
  → Incorporado. S3 → §5 (parágrafo "Mutações depois do Green", com o procedimento e a conferência por `git status --short`), §5 T1 (mutações de `Validate_WithTimeoutSecondsAtLimits_Succeeds` movidas para depois do Green), §5 T4b (casos 9 e 10 e `SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail` movidos para depois do Green; o último falha pela asserção de `Kind`, não por `NotImplementedException`), §5 T2 e T5 (dizem por que a mutação delas continua no próprio Red: a regra mutada já existe ou a mutação acrescenta comportamento), §5 T6 (idem).
- **S4:** o comando de gravação das fixtures usa os mesmos argumentos de isolamento do adaptador (RN-BRAIN06), ou o `README.md` das fixtures registra a diferença.
  → Incorporado nas duas formas: o bloco passa a usar os argumentos de isolamento do adaptador (conferidos em `ClaudeCodeTutorBrain.BuildArguments`), e o `README.md` registra as diferenças que sobram. S4 → §2.7 (bloco com `pwd -W`, `--system-prompt-file`, `--append-system-prompt-file`, `--disallowedTools "mcp__*"`, `--strict-mcp-config`; seção "Diferenças em relação à chamada do adaptador"), §2 "Alternativas descartadas" (modo JsonSchema na gravação), §3 (linha das fixtures), §5 T4a, §7 (risco novo), §9 P18 (nova), ADR-008 item 1 e "O que precisa ser medido".
- **S5:** no esperado do D6, dizer como o tratamento do BOM é observado, ou retirar o item.
  → Incorporado, retirando o item: com a CLI ausente a saída é a mesma com ou sem BOM. S5 → §10 D6 (o esperado diz que o pipe no PowerShell 5.1 é a cobertura do `padroes-codigo` e que o BOM continua provado pelo teste da US-001).
- **S6:** registrar na P9 (e no ADR-008, "Consequências") que os códigos de início diferentes de 2 e 3 (5, 193) viram `Unknown` e não acionam o fallback da US-007.
  → Incorporado. Conferido o texto da RN-BRAIN08 em `specs/BUSINESS_RULES.md` (fallback para `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated`) antes de escrever (AP-036). S6 → §2.3 (parágrafo do efeito na US-007), §2.8 (nenhuma mudança na RN-BRAIN08 nem na spec da US-007), §8 (item da US-007), §9 P9 (consumidores e efeito, AP-040), ADR-008 "Consequências" (bullet novo). Cabeçalho "Regras" passa a citar RN-BRAIN08 e RN-BRAIN06 (sem mudança).
- **S7:** o esqueleto do Red da T5 inclui a nova construção do `ConversationInputHandler` no `EnglishTutorApp`, sem `catch` e sem mudar comportamento, para que o Red compile.
  → Incorporado. S7 → §2.6 (último bullet do `EnglishTutorApp`), §3 (linha do `EnglishTutorApp.cs`: "já no Red da T5"), §5 T5 Red.
- **S8:** o Planejador decide se a mensagem de `NotAuthenticated` também menciona a `ANTHROPIC_API_KEY` do ambiente, e registra a decisão.
  → Decidido: menciona, sempre, com uma frase fixa (`If the ANTHROPIC_API_KEY environment variable is set, check or remove it.`), sem ler o ambiente. S8 → §2.5 (mensagem `NotAuthenticated` e nota), §2 "Alternativas descartadas", §4 (asserção de `SendAsync_WithRecordedNotAuthenticatedOutput_ThrowsNotAuthenticatedAskingForAuthLogin`), §8 (item do ambiente), §9 P17 (nova), §10 D12, ADR-008 (contexto, item 3, alternativa e consequência). A spec não muda: o cenário exige `claude auth login`, que continua na mensagem.

### 2026-10-20 — Usuário (no chat), sobre a Rodada 2 da revisão do plano

> "Sim" (incorporar as sugestões S1–S3 da Rodada 2 de `tasks/US-005/plan-review.md`)

→ Incorporado. Resposta item a item abaixo, no formato "item → seções tocadas" (AP-018). Contagens novas: `SendAsync_WhenFailureTextNamesAKind_ClassifiesByRowAndSource` 11 → 12 casos; `ClaudeCodeTutorBrainTests` 52 → **53 casos** / 29 métodos (sem método novo); Infrastructure.Tests 100 → **101**; Console.Tests **51** (sem mudança); Core.Tests 134; D2 285 → **286**; `LiveBrain` +2 (sem mudança). Nenhuma P nova; ADR-008 sem mudança (o item 1 já diz "sempre as duas fontes", e o caso 12 só prova a direção que faltava; o item 2 e as consequências não dependem de R2-S2 e R2-S3).

- **R2-S1:** incluir o caso simétrico ao caso 11: o stderr tem texto sem relação com nenhum padrão (ex.: um aviso da CLI) e o `result` de erro casa, com o tipo vindo do `result`.
  → Incorporado. Caso 12: código 1, erro-json `You've hit your limit t12`, stderr `Warning: a newer version is available t12` (nenhum padrão da §2.4 é trecho dele) → `UsageLimitReached`, trecho `usage limit reached (You've hit your limit t12)`. Uma implementação que olha o stderr e só recorre ao `result` com o stderr vazio devolve `Unknown` e falha. O caso usa `hit your limit` no `result` (no caso 7 esse padrão está no stderr), então cada padrão da linha b aparece nas duas fontes. R2-S1 → §2.4 (frase das duas direções e "com a outra fonte presente e sem casar"), §4 (linha do mapa: "duas direções, S1 e R2-S1", `[Theory]` 12 casos; caso 12 na tabela; contagem), §5 T4b (os casos 11 e 12 falham no Red), §9 P13 (casos 2, 11 e 12), §10 D2/D8.
- **R2-S2:** a terceira mutação depois do Green da T4b vale para a linha em que a fixture `unknown-model` gravada de fato cai (ou para as linhas 1 e 3), e o Executor registra qual foi depois da T4a.
  → Incorporado nas duas formas. A mutação passa a ser "quando nenhum padrão casa, o `Classify` devolve `NotAuthenticated` em vez de `Unknown`", que vale para as linhas 1 e 3 ao mesmo tempo. A T4a registra nas Evidências o código gravado e a linha (1 ou 3); se cair na linha 2, o Executor para e registra na §12. A T4b cita essa linha ao lado do resultado da mutação. R2-S2 → §2.7 (bullet "Linha de leitura de `unknown-model`"), §4 (asserção de `SendAsync_WithRecordedUnknownFailureOutput_ThrowsUnknownWithDetail`), §5 T4a, §5 T4b (terceira mutação), §7 (risco novo).
- **R2-S3:** `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` falha em tempo limitado sob a mutação do Red, como os demais testes de tempo.
  → Incorporado. A espera pela tarefa usa `WaitAsync(TimeSpan.FromSeconds(10))`. Sob a mutação ("o cancelamento do chamador deixa de chegar ao runner"), o teste falha com `TimeoutException` em 10 s e não trava a suíte. A asserção do tipo `OperationCanceledException` distingue essa falha do resultado esperado. R2-S3 → §4 (linha do mapa: "espera limitada"; asserção do teste), §5 T2 (Red por mutação: falha pelo `WaitAsync`), §7 (risco "trava a suíte" cobre os quatro testes com `HangsUntilCancelled`, inclusive o de caracterização).
