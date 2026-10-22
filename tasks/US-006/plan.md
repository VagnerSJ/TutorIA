# Plano — US-006 Professor local via Ollama

Status: Aprovado em 2026-10-21
Branch: feature/US-006_ollama-brain
Spec: specs/sprints/SPRINT_1.md#us-006
Regras: RN-BRAIN01–05 (da US), RN-BRAIN07 e RN-BRAIN09 (acrescentadas à US, ver §2.9 e P3), RN-BRAIN04 e RN-BRAIN08 (sem mudança), RN-CONV03; ARCHITECTURE §2 (R1–R4), §3, §6.1, §6.2, §6.4, §7, §9, §10; ADR-002, ADR-005, ADR-006, ADR-008, ADR-009 (Aceito, novo)

## 1. Objetivo

Ligar o professor local: com `Brain:Provider = Ollama`, o console conversa pelo `ConversationService` com um modelo servido pelo Ollama (`/api/chat`), com o mesmo contrato de resposta, o mesmo formato de tela e o mesmo tratamento de falha do Claude Code, incluindo as mensagens "Ollama is not running" e "ollama pull <modelo>".

## 2. Abordagem

A decisão central está no **ADR-009 (Aceito)**: `docs/adr/ADR-009-provedor-ollama-ichatclient-pedido-falhas-e-tempo-maximo.md`. Em resumo:

- `OllamaTutorBrain` (Infrastructure) implementa `ITutorBrain` sobre `IChatClient` (`Microsoft.Extensions.AI`), com o `OllamaApiClient` do pacote `OllamaSharp` criado por `OllamaChatClients.Create(OllamaOptions, HttpMessageHandler?)`. A mesma fábrica serve ao DI e aos testes, que trocam só o `HttpMessageHandler` e por isso conferem o fio HTTP real (`/api/chat`, `messages`, `format`).
- O adaptador não guarda estado (RN-BRAIN01): recebe a janela completa e a repassa. Resposta inválida vira `BrainException.InvalidOutput` (ADR-006). `SessionHint` é ignorado.
- Falhas viram `BrainException` classificada pelo adaptador, com a mensagem ao aluno (ADR-008, mesmo padrão): "Ollama is not running", "ollama pull", tempo esgotado e "Ollama failed (...)". Nenhum valor novo em `BrainErrorKind` (P2).
- Tempo máximo por chamada igual ao do Claude Code: `Brain:Ollama:TimeoutSeconds` (P3).
- O console passa a usar o `ConversationInputHandler` para qualquer provedor; o `TutorNotConnectedInputHandler` e o texto "The tutor is not connected yet." saem do código (P8).
- Nada muda no Core: `BrainErrorKind`, `BrainException`, `BrainProviders` e `ConversationService` já atendem.

### 2.1 Configuração (`OllamaOptions`, `OllamaOptionsValidator`, `appsettings.json`)

```csharp
public sealed class OllamaOptions
{
    public const string SectionName = "Brain:Ollama";
    public const string DefaultEndpoint = "http://localhost:11434";
    public const string DefaultModel = "qwen2.5:14b-instruct-q4_K_M";
    public const double DefaultTemperature = 0.6;
    public const int DefaultTimeoutSeconds = 60;   // RN-BRAIN09
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 3600;
    public const double MinTemperature = 0;
    public const double MaxTemperature = 2;
    public string Endpoint { get; set; } = DefaultEndpoint;
    public string Model { get; set; } = DefaultModel;
    public double Temperature { get; set; } = DefaultTemperature;
    public string OutputMode { get; set; } = BrainOutputModes.JsonSchema;   // o adaptador normaliza, como no ClaudeCode
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}
```

Um validador, `IValidateOptions<OllamaOptions>`, uma falha por chave inválida (mesma forma do `ClaudeCodeOptionsValidator`). Mensagens (inglês, dizem o que está errado e o que se espera):

| Condição | Mensagem |
|---|---|
| `Endpoint` em branco | `Brain:Ollama:Endpoint is required. Expected the URL of the Ollama server (default: http://localhost:11434).` |
| `Endpoint` que não é URL absoluta `http` ou `https` | `Brain:Ollama:Endpoint '<valor>' is invalid. Expected an absolute http or https URL (default: http://localhost:11434).` |
| `Model` em branco | `Brain:Ollama:Model is required. Expected the name of a model downloaded in Ollama (default: qwen2.5:14b-instruct-q4_K_M).` |
| `Temperature` fora de 0 a 2 (inclui `NaN`) | `Brain:Ollama:Temperature must be between 0 and 2 (found <valor>). Expected the sampling temperature (default: 0.6).` (valor em cultura invariante) |
| `OutputMode` que não é `JsonSchema` nem `Tags`, sem diferenciar maiúsculas | `Brain:Ollama:OutputMode '<valor>' is invalid. Accepted values: JsonSchema, Tags.` |
| `TimeoutSeconds` fora de 1 a 3600 | `Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found <valor>). Expected the maximum number of seconds to wait for each Ollama call (default: 60).` |

`appsettings.json` ganha, dentro de `Brain`, depois do bloco `ClaudeCode`, o bloco `"Ollama"` com `Endpoint`, `Model`, `Temperature` (`0.6`), `TimeoutSeconds` (`60`) e `OutputMode` (`JsonSchema`), nessa ordem (ARCHITECTURE §6.1, com `TimeoutSeconds` acrescentado, §2.9).

**`ValidateOnStart` e várias seções (AP-039).** `Brain:Ollama` entra no `BuildHost` com `AddOptions<OllamaOptions>().Bind(...).ValidateOnStart()` e o validador é registrado em `AddEnglishTutorInfrastructure`. Teste de **duas seções inválidas ao mesmo tempo** com `Brain:Ollama` (a seção do Ollama e a `Conversation`): um caso novo no teste existente `RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (§4, T1), mais o caso de uma só seção. **Normalização (AP-039):** o validador aceita `OutputMode` sem diferenciar maiúsculas; o dono do valor canônico é `BrainOutputModes.TryGetCanonical`, usado pelo adaptador, com teste no consumidor com caixa diferente (`SendAsync_WhenOutputModeCaseDiffers_UsesCanonicalMode`). O mesmo vale para `Brain:Provider`, normalizado por `BrainProviders.TryGetCanonical` no seletor do DI (`..._WhenProviderIsOllama_ResolvesOllamaBrain` com `Ollama` e `ollama`).

**Consumidores dos limites (AP-040).** Faixa de `TimeoutSeconds` 1..3600: o validador (recusa fora da faixa, teste nos dois lados); o adaptador (`TimeSpan.FromSeconds`, dentro do máximo do `CancellationTokenSource`); `OllamaTutorBrainLiveTests` (usa o padrão 60; chamada real com o modelo aquecido leva poucos segundos); o D10 (usa 1). Faixa de `Temperature` 0..2: o validador (teste nos dois lados) e o adaptador (repassa `(float)Temperature` no `ChatOptions`); a LiveBrain usa o padrão 0,6. **Limite aceito (P12):** `Brain:Ollama` é validado na partida mesmo com `Brain:Provider = ClaudeCode`; consumidores: a partida do console (uma seção do Ollama inválida impede o uso do Claude Code, com a chave e o valor esperado na saída de erro); o fallback da US-007 (precisa das duas seções válidas de qualquer forma); os testes de console (a configuração de teste não define `Brain:Ollama`, então valem os padrões válidos).

### 2.2 Pedido ao Ollama (`OllamaTutorBrain.SendAsync`)

```csharp
public sealed class OllamaTutorBrain(
    [FromKeyedServices(BrainProviders.Ollama)] IChatClient chatClient,
    TutorReplyParser parser, PromptFiles promptFiles, IOptions<OllamaOptions> options,
    TimeProvider timeProvider, ILogger<OllamaTutorBrain> logger) : ITutorBrain
{
    public string Name => BrainProviders.Ollama;
    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct);
}
```

Passos de `SendAsync`:

1. Normaliza `OutputMode` com `BrainOutputModes.TryGetCanonical`; valor inválido lança `InvalidOperationException` (o validador já barrou na partida; mesma guarda do Claude Code).
2. Lê o arquivo de formato do modo (`PromptFiles.FormatJsonPath` com `JsonSchema`; `FormatTagsPath` com `Tags`) com `File.ReadAllTextAsync(path, ct)`.
3. Mensagens: uma `System` com `request.SystemPrompt + "\n\n" + <conteúdo do arquivo de formato>`, depois **todas** as `request.Messages`, na ordem, com `Role` e `Content` iguais (`User`/`Assistant`), sem filtrar nenhuma (P5). Os 2 turnos de histórico, a nova mensagem do aluno e, na retentativa do ADR-006, a resposta rejeitada e a correção chegam como o `ConversationService` os montou.
4. `ChatOptions`: `ModelId = options.Model`, `Temperature = (float)options.Temperature`; com `JsonSchema`, `ResponseFormat = ChatResponseFormat.ForJsonSchema(<TutorReplySchema.Json como JsonElement>)`; com `Tags`, `ResponseFormat` nulo (nenhum `format` no fio).
5. Um relógio por chamada, ligado ao token do chamador (§2.4); `await _chatClient.GetResponseAsync(messages, chatOptions, call.Token)`.
6. `ChatResponse.Text` vai para `TutorReplyParser.Parse`. Inválido: `BrainException.InvalidOutput(parsed.Rejection, text)` (ADR-006). Válido: loga `OllamaLog.Replied` (EventId 2002: `Provider`, `ElapsedMs`, `Model`, `OutputMode`; nunca o texto do aluno nem a resposta) e devolve `parsed.Reply`.

Sem estado entre chamadas (nenhum campo mutável): duas chamadas seguidas com históricos diferentes enviam cada uma só o próprio (`SendAsync_OnSecondCall_DoesNotCarryMessagesFromTheFirst`). O `OllamaChatClients.Create` devolve `new OllamaApiClient(new HttpClient(handler) { BaseAddress = new Uri(Endpoint), Timeout = Timeout.InfiniteTimeSpan }, Model)`: o limite é o `CancellationTokenSource` do adaptador, não o do `HttpClient` (ADR-009). O Executor ajusta os nomes dos tipos da biblioteca ao que a versão fixada expõe, mantendo o comportamento acima.

**JSON em cerca markdown ou com texto em volta (adiamento da US-002, P5).** Os planos antigos deixaram "decidir na US-006 com a saída real do Ollama". Decisão (P6): **continua rejeitado**; o parser não muda. A T3 grava a resposta real com `format`, e o teste `SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections` prova que ela é JSON puro e passa. Se a gravada vier com cerca ou texto em volta, o Executor para e registra na §12: o usuário decide (os testes `Parse_WhenJsonHasTrailingText_...` e `Parse_WhenJsonIsInFenceOrAfterText_...` mudariam junto).

### 2.3 Falhas do servidor (`OllamaFailures`, `internal static class`)

O adaptador captura só `HttpRequestException` e o tipo próprio de exceção da biblioteca que a T3 registrar para a resposta 404 (`catch (Exception exception) when (OllamaFailures.IsServerFailure(exception))`), e lança `OllamaFailures.Translate(exception, options)`. Qualquer outra exceção propaga (erro inesperado no console, sem mudança). Avalia nesta ordem; a primeira linha que casar decide:

| Ordem | Condição (sobre a exceção capturada) | `BrainErrorKind` | Mensagem |
|---|---|---|---|
| a | `StatusCode` é 404, **ou** a mensagem contém `model` e `not found` (sem diferenciar maiúsculas) | `Unknown` | modelo ausente (§2.5) |
| b | não casou a, e não há `StatusCode` (nenhuma resposta HTTP: conexão recusada, nome do servidor não resolvido) | `Unknown` | servidor fora do ar (§2.5) |
| c | qualquer outra falha do servidor (ex.: `StatusCode` 500) | `Unknown` | falha genérica com detalhe (§2.5) |

`Timeout` não passa por aqui (§2.4). A linha **a antes da b** (precedência) tem caso próprio; o status 404 e o texto, cada um sozinho, têm casos próprios; a linha b tem um caso com `SocketException` interna e outro sem; a linha c tem um caso com status (§4, caso por linha, AP-041/AP-047). A T3 registra na §12 o tipo de exceção e o status que a biblioteca produz de verdade para o modelo ausente e para a conexão recusada; se o 404 chegar sem `StatusCode` e sem o texto `model`/`not found`, o Executor ajusta a linha a ao observado e registra (como a US-005 fez com os padrões de texto).

### 2.4 Tempo máximo por chamada

Igual ao ADR-008, com `Brain:Ollama:TimeoutSeconds`:

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds), _timeProvider);
using var call = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
try { response = await _chatClient.GetResponseAsync(messages, chatOptions, call.Token); }
catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
{ throw OllamaFailures.Timeout(options.TimeoutSeconds, exception); }
catch (Exception exception) when (OllamaFailures.IsServerFailure(exception))
{ throw OllamaFailures.Translate(exception, options); }
```

- O relógio começa antes da leitura do arquivo de formato e vale para a chamada inteira (um relógio por chamada: `SendAsync_OnEachCall_StartsTimeoutAgain`). A retentativa do ADR-006 é outra chamada, com o próprio limite (um turno com retentativa pode levar até 2 × `TimeoutSeconds`).
- Cancelamento do chamador (Ctrl+C) propaga `OperationCanceledException` sem conversão, nos dois casos: só o chamador cancelou (`SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`) e o tempo estourou e o chamador também cancelou antes de a exceção chegar ao adaptador (`SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`, que prova a parte `!ct.IsCancellationRequested` do filtro).
- A forma acima é sugestão; o Executor pode reorganizar, mantendo o comportamento e a ordem dos `catch`.

### 2.5 Mensagens ao aluno (texto da `BrainException.Message`; inglês)

| Situação | `Kind` | Mensagem |
|---|---|---|
| Servidor fora do ar (linha b) | `Unknown` | `Ollama is not running at "<Endpoint>". Start it (open the Ollama app, or run "ollama serve" in a terminal), or set Brain:Ollama:Endpoint to the address where it runs, then send your sentence again.` |
| Modelo ausente (linha a) | `Unknown` | `Ollama model "<Model>" is not downloaded. Run "ollama pull <Model>" in a terminal, then send your sentence again. To use another model, set Brain:Ollama:Model.` |
| Tempo esgotado | `Timeout` | `Ollama did not answer in time (limit: <TimeoutSeconds> s) and the request was cancelled. Send your sentence again, or raise Brain:Ollama:TimeoutSeconds.` |
| Falha genérica (linha c) | `Unknown` | `Ollama failed (<detalhe>). Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.` |

`<Endpoint>` e `<Model>` vêm das opções (o modelo da mensagem é o configurado, `ollama pull qwen2.5:14b-instruct-q4_K_M` com o padrão). `<detalhe>` = primeira linha não vazia da mensagem da exceção, sem espaços nas pontas; se não houver, `no error details were provided` (P9). Só a mensagem de falha genérica embute texto de biblioteca; os testes conferem o prefixo `Ollama failed (`, um trecho que vem da exceção que o teste cria (`boom t5`) e o sufixo `Check that the Ollama server is healthy` (AP-015). Nenhuma outra mensagem embute `Message` de exceção. A derivação de `<detalhe>` tem um caso por ramo (B1; §4, `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback`): mensagem de uma linha (caso 5 da classificação da §4), várias linhas (só a primeira entra: nenhuma linha de stack ou de corpo HTTP vaza), começando por linhas vazias ou só com espaços, com espaços e `\r` nas pontas, vazia e só com espaços (texto de reserva; nunca parênteses vazios). As instruções (`ollama serve`, `ollama pull`, as chaves `Brain:Ollama:*`) são conferidas na T3 contra o Ollama instalado (`ollama --help` mostra `serve` e `pull`; mesma conferência que a US-005 fez para `claude auth login`, registrada nas Evidências).

### 2.6 Console

- `EnglishTutorApp.RunSessionAsync` sempre constrói `new ConversationInputHandler(conversation, options.Output, options.Error, logger)`; sai o ramo por provedor e o comentário `// P7: only ClaudeCode is connected; Ollama keeps the "not connected" handler until US-006.`. O cabeçalho (`Brain: Ollama | Mode: ...`) e o `ApplicationStarted` seguem usando o nome canônico.
- Saem `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` e `ConsoleText.TutorNotConnected` (o texto "The tutor is not connected yet." deixa de existir em `src/`). O `ChatSession` e o `ConversationInputHandler` não mudam: a falha do Ollama chega como `BrainException`, e o console a mostra na saída de erro com as duas linhas da US-005 (`The tutor could not answer: <message>` e `You can send a sentence again or type /quit to exit.`) e continua (ADR-008, item 4; cenário "falha do cérebro no console não encerra a sessão" da US-005, mesmo canal e mesmo código de saída, AP-024).
- Os testes de console registram o `IChatClient` com chave `"Ollama"` por `AppRunOptions.ConfigureServices`; o `OllamaTutorBrain` real fica por baixo, então "o `BrainRequest` é enviado ao `OllamaTutorBrain`" é provado (o `ITutorBrain` não é substituído).

### 2.7 Registro no DI (seletor provisório) — `AddEnglishTutorInfrastructure`

```csharp
services.TryAddSingleton<ClaudeCodeTutorBrain>();
services.TryAddSingleton<OllamaTutorBrain>();
services.TryAddKeyedSingleton<IChatClient>(BrainProviders.Ollama,
    (sp, _) => OllamaChatClients.Create(sp.GetRequiredService<IOptions<OllamaOptions>>().Value));
// Provisional selector until the BrainFactory (US-007).
services.TryAddSingleton<ITutorBrain>(sp =>
    BrainProviders.TryGetCanonical(sp.GetRequiredService<IOptions<BrainOptions>>().Value.Provider, out var provider)
    && provider == BrainProviders.Ollama
        ? sp.GetRequiredService<OllamaTutorBrain>()
        : sp.GetRequiredService<ClaudeCodeTutorBrain>());
services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>());
```

Sem `Brain:Provider` ligado (como no teste existente `AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource`), o provedor é vazio e o seletor devolve o Claude Code. O comentário `Provisional selector until the BrainFactory (US-007)` substitui `ITutorBrain is fixed on ClaudeCode until the BrainFactory (US-007, P17).` A US-007 remove o seletor; o dono está registrado na spec da US-007 (§2.9, AP-022).

### 2.8 Fixtures do Ollama (`tests/EnglishTutor.Infrastructure.Tests/Fixtures/Ollama/`)

| Arquivo | Origem |
|---|---|
| `chat-structured.json` | **gravado** (T3): corpo da resposta de `/api/chat` para a frase `I have worked with C# since five years`, com `OutputMode = JsonSchema` |
| `chat-request.json` | **gravado** (T3): corpo da requisição que o adaptador enviou nessa chamada (documenta o fio; não é lido por teste) |
| `model-not-found.status.txt` e `model-not-found.json` | **gravados** (T3): status HTTP e corpo da resposta para `Model = nao-existe-us006` |
| `README.md` | data, `ollama --version`, modelo, comando, o que foi gravado e a seção "Diferenças em relação à chamada do adaptador" |

Gravação (AP-048): a T3 usa o **próprio adaptador** (`OllamaFixtureRecorderLiveTests`, `LiveBrain`), com um `DelegatingHandler` de gravação por baixo do `OllamaApiClient` que copia status e corpo (requisição e resposta). Assim a requisição gravada é a do adaptador, com o mesmo `OllamaChatClients.Create`, o mesmo system prompt (`prompts/tutor.md`, o mesmo que o `FileTutorPromptSource` entrega ao app, como em `LiveClaudeCode`), o mesmo arquivo de formato e a mesma mensagem de aluno (`TurnMessageBuilder`). A diferença que sobra, a registrar no `README.md`: o teste usa uma pasta `prompts` copiada para a saída do teste; a primeira chamada de cada modelo inclui o carregamento dele na VRAM. O recorder também escreve `recorded-fixtures/exceptions.txt` com a cadeia de exceções (tipo, `StatusCode`, primeira linha da mensagem) do modelo ausente e da conexão recusada (porta fechada), para a escolha das exceções capturadas (§2.3); o arquivo não é fixture e vai para as Evidências e para a §12.

Pré-condição da T3 (ROADMAP, fase 2b): Ollama instalado (`winget install Ollama.Ollama`) e o modelo baixado (`ollama pull qwen2.5:14b-instruct-q4_K_M`, cerca de 9 GB). Se o Ollama não puder ser instalado ou o modelo não couber, o Executor para e o usuário decide na §12 (P1 e P10). Higiene: D13 sem linhas; as respostas do Ollama não trazem caminhos pessoais, mas o recorder confere.

### 2.9 Alterações nos documentos-raiz e na spec (propostas; aplicadas pelo orquestrador antes da T1, em dois commits, AP-030)

O orquestrador aplica, depois da aprovação e **antes da T1**:

- `docs(ai): update spec for US-006`: os blocos de `specs/sprints/SPRINT_1.md` abaixo (US-006 e US-007);
- `docs(ai): update architecture for US-006`: `ARCHITECTURE.md` (§3, §6.1, §6.4) e o ADR-009 em "Aceito", se aprovado.

Nenhuma tarefa da §5 altera esses documentos, **exceto** a T8, que acrescenta a medição de VRAM à `ARCHITECTURE.md` §7 (pedida pela spec, D6). Cada trecho é identificado pelo **texto antigo** (AP-023).

**`specs/sprints/SPRINT_1.md`, seção `## US-006 — Professor local via Ollama`:**

(a) Regras (P3). Texto antigo: `**Regras:** RN-BRAIN01–05; ARCHITECTURE §6.4.` Texto novo: `**Regras:** RN-BRAIN01–05, RN-BRAIN07, RN-BRAIN09; ARCHITECTURE §6.4.`

(b) Cenário novo, depois de "modelo não baixado" (P3; mesmo critério observável do cenário "tempo esgotado" da US-005: o trabalho em curso é encerrado e há uma mensagem de tempo esgotado). Texto antigo (última linha do cenário):

```
  Então vejo o comando "ollama pull <modelo>"
```

Texto novo (a linha antiga seguida do cenário novo, antes do cenário "console conversa com o Ollama"):

```
  Então vejo o comando "ollama pull <modelo>"

Cenário: tempo esgotado
  Dado TimeoutSeconds igual a 1 e um Ollama que não responde
  Quando envio uma frase
  Então a requisição é cancelada
  E vejo uma mensagem de tempo esgotado
```

(c) DoD extra, depois da linha antiga `| D6 | `nvidia-smi` durante o teste D5 | VRAM do modelo registrada em ARCHITECTURE §7 |`, acrescentar (critérios observáveis da US-005: saída de erro, `exit=0`; a spec da US-004 já exige a execução real pelo console):

```
| D7 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; "I have worked with C# since five years", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider` | uma linha `Tutor: <reply>`; nenhuma linha "The tutor is not connected yet."; `exit=0` |
```

**`specs/sprints/SPRINT_1.md`, seção `## US-007 — Troca de cérebro por configuração e fallback`** (adiamento do seletor provisório, AP-022). Depois da linha antiga `| D6 | `$env:Brain__Provider="Ollama"; "/quit" \| dotnet run --project src/EnglishTutor.Console` | cabeçalho mostra "Ollama" |`, acrescentar:

```
| D7 | `git grep -n -F "Provisional selector" -- src tests` | nenhuma linha: o seletor provisório de `ITutorBrain` por `Brain:Provider` (US-006, em `InfrastructureServiceCollectionExtensions`) foi substituído pela `BrainFactory` |
```

Prova do comando (AP-035): ao fim da US-006, o mesmo comando acha **uma** linha, em `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (D16 deste plano).

Nenhuma regra `RN-*` muda. O efeito de "Ollama fora do ar" e "modelo ausente" serem `Unknown` na RN-BRAIN08 está no ADR-009 ("Consequências") e na P2; a spec da US-007 não muda além do D7 (o cenário "fallback por limite de uso" tem Claude Code como primário e Ollama como reserva, conferido em `SPRINT_1.md`, AP-036).

**`ARCHITECTURE.md`:**

- §3, texto antigo:
  `                       FileTutorPromptSource, PromptFiles, ClaudeCodeStreamingBrain, OllamaTutorBrain,`
  Texto novo:
  `                       FileTutorPromptSource, PromptFiles, ClaudeCodeStreamingBrain, OllamaTutorBrain, OllamaOptions,`
  `                       OllamaOptionsValidator, OllamaFailures, OllamaChatClients, OllamaLog,`
- §6.1, bloco `Ollama`, texto antigo:
  `    "Temperature": 0.6,`
  Texto novo:
  `    "Temperature": 0.6,`
  `    "TimeoutSeconds": 60,`
- §6.4, texto antigo:
  `- Via `Microsoft.Extensions.AI` (`IChatClient`) com o pacote do Ollama; histórico montado pela aplicação a cada chamada (sem estado).`
  Texto novo:
  `- Via `Microsoft.Extensions.AI` (`IChatClient`) com o cliente `OllamaSharp` (`OllamaApiClient`, ADR-009); histórico montado pela aplicação a cada chamada (sem estado, RN-BRAIN01).`
  `- Mensagem `system` = `BrainRequest.SystemPrompt` + instrução de formato do modo (`prompts/format.json.md` ou `format.tags.md`, ADR-002). `OutputMode=JsonSchema` envia `format` com o `TutorReplySchema`; `Tags` não envia `format`. Modelo e temperatura vêm de `Brain:Ollama`.`
  `- Falhas viram `BrainException` classificada pelo adaptador (ADR-009): servidor sem resposta → `Unknown` com "Ollama is not running ..."; modelo ausente → `Unknown` com `ollama pull <modelo>`; `TimeoutSeconds` estourado → `Timeout`, com a requisição cancelada. O console mostra a mensagem como nas falhas do Claude Code.`
- A linha seguinte do §6.4 (`- O mesmo `IChatClient` servirá de base para o futuro provedor `AnthropicApi`.`), o §4 (`BrainErrorKind`) e o §6.2 (`Ollama usa `format` com o mesmo JSON Schema`): **sem alteração**.
- §7 (T8, não pelo orquestrador): depois da linha `| LLM local 14B Q4 | ~9–10 GB |` da tabela "Orçamento de VRAM", acrescentar o parágrafo `Medido (US-006, <data>): <pico> MiB de VRAM em uso durante o teste real com `qwen2.5:14b-instruct-q4_K_M` (linha de base <base> MiB sem o modelo; `nvidia-smi`).`, com os números das Evidências do D6.

**Repetições em `CLAUDE.md`, skills e documentos de apoio** (Grep por `IChatClient|Microsoft\.Extensions\.AI|OllamaSharp|Brain:Ollama|11434|Ollama` em `CLAUDE.md`, `README.md`, `.claude/**`, `specs/PRODUCT.md`, `specs/BUSINESS_RULES.md` e `specs/ROADMAP.md`):

- `CLAUDE.md` (linha 3, "Claude Code headless ou Ollama") e `README.md` (linhas 5, 17, 50): descrevem o produto, não repetem §3, §6.1 nem §6.4. **Sem alteração.**
- `.claude/skills/tdd-dotnet/SKILL.md` (exemplo `OllamaTutorBrainContractTests : TutorBrainContractTests`): continua correto (US-008). **Sem alteração.**
- `specs/ROADMAP.md`, linha de ambiente, texto antigo: `| Ollama | não instalado | Instalar (Fase 2b) |`. Passa a estar instalado depois da T3; **o Documentador** atualiza com a versão (`ollama --version`) registrada nas Evidências e o modelo baixado (não é decisão de arquitetura; não bloqueia a T1).
- `ARCHITECTURE.md` §4/§6.2 e as regras `RN-*`: sem alteração.

Documentos históricos que citam "até a US-006" ou deixam decisões para ela (`docs/us/US-002.md` P5 do JSON com cerca, `docs/us/US-003.md` `RawText` vazio, `docs/us/US-004.md` e `tasks/US-004/*`) não mudam; as decisões estão na §2.2, na P5 e na P6, e o Documentador escreve `docs/us/US-006.md`.

### 2.10 Testes e comentários existentes que mudam (sem enfraquecer)

| Atual | Mudança | Tarefa |
|---|---|---|
| `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected` e o comentário `// US-001 — apoio: implementação provisória sem cérebro (D11); US-004 — apoio, P7: Ollama ainda não conectado` | **Saem.** Substituídos por `RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback` (cenário "console conversa com o Ollama"), que prova o oposto do que o teste antigo caracterizava | T7 |
| `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` | Ganha um `[InlineData]` com `Brain:Ollama:TimeoutSeconds` = `0` (os dois existentes não mudam) | T1 |
| `EnglishTutorAppTests` (helper `CreateOptions`) | Ganha o irmão `CreateOllamaOptions(chatClient, lines)`, que **não** substitui o `ITutorBrain` e registra o `IChatClient` com chave `"Ollama"` e o `FixedTutorPromptSource` | T7 |
| `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource` | Sem mudança de corpo (continua provando o padrão sem `Brain:Provider` ligado) | T6 |

Comentários de código com adiamento ou texto que muda com esta US (AP-040; Grep por `US-006`, `not connected` e `Ollama` em `src/` e `tests/`):

| Arquivo | Texto antigo | Texto novo | Tarefa |
|---|---|---|---|
| `src/EnglishTutor.Console/EnglishTutorApp.cs` | `// P7: only ClaudeCode is connected; Ollama keeps the "not connected" handler until US-006.` | (o comentário sai com o ramo) | T7 |
| `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` | (arquivo inteiro, comentário `// Used only with Brain:Provider = Ollama until US-006 connects it (US-004 P7).`) | arquivo apagado | T7 |
| `src/EnglishTutor.Console/Ui/ConsoleText.cs` | `public const string TutorNotConnected = "The tutor is not connected yet.";` | (a linha sai) | T7 |
| `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | `// Registers the adapters and the validators of their own Options; the sections are bound in the` / `// composition root (ADR-004). ITutorBrain is fixed on ClaudeCode until the BrainFactory (US-007, P17).` | `// composition root (ADR-004). ITutorBrain is chosen by Brain:Provider until the BrainFactory (US-007).` e, junto do registro, o comentário `// Provisional selector until the BrainFactory (US-007).` | T6 |
| `src/EnglishTutor.Infrastructure/Brain/TutorReplySchema.cs` | `// value of --json-schema. Reused by the Ollama "format" (US-006).` | `// value of --json-schema, and reused as the Ollama "format" (ADR-009).` | T2 |

### 2.11 Guardas defensivas (sem teste, decisão registrada)

- `ArgumentNullException.ThrowIfNull` em `OllamaTutorBrain.SendAsync` (`request`), em `OllamaOptionsValidator.Validate`, em `OllamaChatClients.Create` (`options`) e em `OllamaFailures` (exceção e opções).
- A guarda `InvalidOperationException` de `OutputMode` inválido (§2.2, passo 1): o validador já a barra na partida; só o próprio código chega lá, com valor válido (mesma guarda do Claude Code, sem teste).
- Arquivo de formato ausente: `File.ReadAllTextAsync` lança `FileNotFoundException`, que sai como erro inesperado. Falha de implantação, coberta pelo teste de implantação dos prompts que já existe (`PromptFilesDeploymentTests`); sem teste novo.

### Alternativas descartadas

- `HttpClient` direto (ADR-009): contraria a ARCHITECTURE §6.4 e perde a base do futuro `AnthropicApi`.
- Valores novos em `BrainErrorKind` para "fora do ar" e "modelo ausente" (ADR-009; P2): mudaria RN-BRAIN07/08, §4 e o Core por um provedor.
- Testar só com um `IChatClient` falso: não provaria o `/api/chat` nem o `format` do cenário; por isso os testes de fio com o `OllamaApiClient` real e `HttpMessageHandler` falso.
- Gravar as fixtures com `curl`: o corpo da requisição seria diferente do que o adaptador envia (AP-048); o recorder usa o próprio adaptador.
- `BrainFactory` já nesta US: escopo da US-007.
- Sem tempo máximo no Ollama (P3): contraria a RN-BRAIN09.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-009-provedor-ollama-ichatclient-pedido-falhas-e-tempo-maximo.md` | Decisão (Aceito), criada junto com este plano |
| Alterar | `ARCHITECTURE.md` | §3, §6.1, §6.4 (§2.9) pelo orquestrador antes da T1; §7 na T8 |
| Alterar | `specs/sprints/SPRINT_1.md` | US-006 (Regras, cenário "tempo esgotado", D7) e US-007 (D7) (§2.9), pelo orquestrador antes da T1 |
| Alterar | `Directory.Packages.props` | `OllamaSharp` e `Microsoft.Extensions.AI.Abstractions` (grupo novo `Infrastructure`) (§6) |
| Alterar | `src/EnglishTutor.Infrastructure/EnglishTutor.Infrastructure.csproj` | Referências aos dois pacotes |
| Criar | `src/EnglishTutor.Infrastructure/Brain/OllamaOptions.cs` | Seção `Brain:Ollama` (§2.1) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/OllamaOptionsValidator.cs` | Validação com mensagens (§2.1) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/OllamaChatClients.cs` | Fábrica do `IChatClient` do Ollama (§2.2) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/OllamaTutorBrain.cs` | Adaptador (§2.2, §2.4) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/OllamaFailures.cs` | Classificação e mensagens (§2.3, §2.5) |
| Criar | `src/EnglishTutor.Infrastructure/Brain/OllamaLog.cs` | Evento 2002 `Replied` (faixa 2000–2999) |
| Alterar | `src/EnglishTutor.Infrastructure/Brain/TutorReplySchema.cs` | Só o comentário (§2.10) |
| Alterar | `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | Registros, seletor provisório, validador (§2.7) |
| Alterar | `src/EnglishTutor.Console/appsettings.json` | Bloco `Brain:Ollama` (§2.1) |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Bind de `OllamaOptions` com `ValidateOnStart`; handler único (§2.1, §2.6) |
| Alterar | `src/EnglishTutor.Console/Ui/ConsoleText.cs` | Sai `TutorNotConnected` (§2.6) |
| Apagar | `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` | Substituído pela conversa real (P8) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaOptionsTests.cs` | Padrões (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaOptionsValidatorTests.cs` | Validação (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaTutorBrainTests.cs` | Adaptador com `IChatClient` falso (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaTutorBrainHttpTests.cs` | Adaptador com `OllamaApiClient` real e `HttpMessageHandler` falso (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaTutorBrainLiveTests.cs` | `LiveBrain` (D5) (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaFixtureRecorderLiveTests.cs` | Gravação das fixtures (`LiveBrain`, §2.8) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeChatClient.cs` | `IChatClient` com fila, `Throws` e `HangsUntilCancelled(onCancelled)` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeHttpMessageHandler.cs` | Registra requisições (método, URI, corpo) e responde de uma fila (resposta ou exceção) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/RecordingHttpHandler.cs` | `DelegatingHandler` de gravação (recorder) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LiveOllama.cs` | Adaptador real para os testes `LiveBrain` (`ENGLISHTUTOR_OLLAMA_ENDPOINT`, `ENGLISHTUTOR_OLLAMA_MODEL`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/Ollama/*` | 4 arquivos gravados + `README.md` (§2.8) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs` | Seletor e validador do Ollama (§4) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/ScriptedChatClient.cs` | `IChatClient` falso do console (resposta ou exceção; grava as chamadas) |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | Cenário do console, falhas, configuração inválida; teste antigo sai (§2.10, §4) |

## 4. Mapa cenário BDD → testes

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| chamada com histórico completo | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WithTwoPreviousTurns_SendsSystemPromptHistoryAndNewMessageInOrder` | Unidade (`IChatClient` falso) |
| chamada com histórico completo (no fio: `/api/chat`) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WithTwoPreviousTurns_PostsToApiChatWithSystemHistoryAndNewMessage` | Unidade (`OllamaApiClient` real, `HttpMessageHandler` falso) |
| chamada com histórico completo (apoio, RN-BRAIN01: sem estado) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_OnSecondCall_DoesNotCarryMessagesFromTheFirst` | Unidade |
| chamada com histórico completo (apoio, P5: `Assistant` vazio da retentativa) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenHistoryHasEmptyAssistantMessage_KeepsItUnchanged` | Unidade |
| chamada com histórico completo (apoio, P5: o Ollama aceita) | Infrastructure.Tests | `OllamaTutorBrainLiveTests` | `SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply` | `LiveBrain` |
| saída estruturada | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WithJsonSchemaMode_AsksForStructuredOutputWithTutorReplySchema` | Unidade |
| saída estruturada (no fio: campo `format`) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WithJsonSchemaMode_PostsFormatWithTutorReplySchema` | Unidade (fio) |
| saída estruturada (apoio: o outro modo, sem `format`) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WithTagsMode_AppendsTagsInstructionAndAsksForNoStructuredOutput` | Unidade |
| saída estruturada (apoio, no fio: o outro modo) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WithTagsMode_PostsNoFormat` | Unidade (fio) |
| saída estruturada (apoio, AP-039: normalização no consumidor) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenOutputModeCaseDiffers_UsesCanonicalMode` | Unidade, `[Theory]` 3 casos |
| saída estruturada (apoio: modelo e temperatura configurados) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_Always_PassesConfiguredModelAndTemperature` | Unidade |
| saída estruturada (apoio: resposta válida, RN-BRAIN03) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WithValidJson_ReturnsParsedReplyWithRawText` | Unidade |
| saída estruturada (apoio: resposta gravada de verdade; decisão P6) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections` | Fixture gravada (caracterização) |
| saída estruturada (apoio: RN-BRAIN04, ADR-006) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenReplyIsOutOfFormat_ThrowsInvalidOutputWithRejectedText` | Unidade |
| saída estruturada (apoio: log sem texto) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenReplyIsValid_LogsRepliedWithoutStudentTextOrReply` | Unidade |
| saída estruturada (apoio: 5 frases de fumaça, D5) | Infrastructure.Tests | `OllamaTutorBrainLiveTests` | `SendAsync_WithFiveKnownErrors_AllInFormat` | `LiveBrain` |
| Ollama indisponível | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenNothingAnswers_ThrowsNotRunningWithStartInstructionAndKey` | Unidade |
| Ollama indisponível (no fio: conexão recusada) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WhenConnectionIsRefused_ThrowsNotRunningWithEndpoint` | Unidade (fio) |
| Ollama indisponível (de ponta a ponta, console) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenOllamaFails_ShowsInstructionOnErrorAndContinuesSession` | Componente, `[Theory]` 2 casos (servidor fora do ar; modelo ausente) |
| Ollama indisponível (apoio: contrato real da conexão recusada) | Infrastructure.Tests | `OllamaTutorBrainLiveTests` | `SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning` | `LiveBrain` (porta fechada; não precisa do Ollama) |
| modelo não baixado | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommandForConfiguredModel` | Unidade |
| modelo não baixado (resposta gravada de verdade) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WithRecordedModelNotFoundResponse_ThrowsPullCommandForConfiguredModel` | Fixture gravada |
| modelo não baixado (apoio: contrato real) | Infrastructure.Tests | `OllamaTutorBrainLiveTests` | `SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand` | `LiveBrain` (Ollama no ar, modelo inexistente) |
| Ollama indisponível / modelo não baixado (apoio: tabela da §2.3, uma linha e uma condição por caso) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` | Unidade, `[Theory]` 6 casos (abaixo) |
| Ollama indisponível (apoio: `<detalhe>` da falha genérica, P9, B1) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` | Unidade, `[Theory]` 5 casos (abaixo) |
| Ollama indisponível (apoio: só falha do servidor é traduzida) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt` | Unidade (caracterização) |
| Ollama indisponível (apoio: falha genérica no fio) | Infrastructure.Tests | `OllamaTutorBrainHttpTests` | `SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage` | Unidade (fio) |
| tempo esgotado (cenário novo, P3) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenOllamaDoesNotAnswerWithinTimeout_CancelsRequestAndThrowsTimeout` | Unidade (`FakeTimeProvider`) |
| tempo esgotado (apoio: um relógio por chamada) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_OnEachCall_StartsTimeoutAgain` | Unidade |
| tempo esgotado (apoio: Ctrl+C não é `Timeout`) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` | Unidade (caracterização; espera limitada) |
| tempo esgotado (apoio: tempo estourado **e** Ctrl+C ao mesmo tempo não é `Timeout`) | Infrastructure.Tests | `OllamaTutorBrainTests` | `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` | Unidade (`FakeTimeProvider`) |
| tempo esgotado (apoio: padrão, RN-BRAIN09) | Infrastructure.Tests | `OllamaOptionsTests` | `Constructor_Always_UsesDocumentedDefaults` | Unidade |
| tempo esgotado (apoio: faixa) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenTimeoutSecondsIsOutOfRange_FailsWithRangeAndFoundValue` | Unidade, `[Theory]` 3 casos (0, -1, 3601) |
| tempo esgotado (apoio: faixa, outro lado) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WithTimeoutSecondsAtLimits_Succeeds` | Unidade, `[Theory]` 2 casos (1, 3600) |
| tempo esgotado (apoio: chave inválida encerra antes do prompt) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenOllamaTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt` | Componente |
| console conversa com o Ollama | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback` | Componente (`OllamaTutorBrain` real, `IChatClient` falso por baixo) |
| console conversa com o Ollama (apoio: seleção por configuração, AP-039) | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | `AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` | Unidade, `[Theory]` 2 casos (`Ollama`, `ollama`) |
| console conversa com o Ollama (apoio: o outro provedor continua) | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | `AddEnglishTutorInfrastructure_WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain` | Unidade |
| console conversa com o Ollama (apoio: validador registrado) | Infrastructure.Tests | `InfrastructureServiceCollectionExtensionsTests` | `AddEnglishTutorInfrastructure_Always_RegistersOllamaOptionsValidator` | Unidade |
| (apoio: configuração padrão válida) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WithDefaultOptions_Succeeds` | Unidade |
| (apoio: `Endpoint` em branco) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenEndpointIsBlank_FailsWithRequiredAndDefault` | Unidade, `[Theory]` 2 casos (`""`, `" "`) |
| (apoio: `Endpoint` que não é URL `http`/`https`) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenEndpointIsNotAnAbsoluteHttpUrl_FailsWithValueAndExpectation` | Unidade, `[Theory]` 3 casos (`not a url`, `localhost:11434`, `ftp://localhost:11434`) |
| (apoio: `Endpoint` válido, outro lado) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WithHttpAndHttpsEndpoints_Succeeds` | Unidade, `[Theory]` 2 casos |
| (apoio: `Model` em branco) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenModelIsBlank_FailsWithRequiredAndDefault` | Unidade, `[Theory]` 2 casos |
| (apoio: `Temperature` fora da faixa) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenTemperatureIsOutOfRange_FailsWithRangeAndFoundValue` | Unidade, `[Theory]` 3 casos (-0,1; 2,1; `NaN`) |
| (apoio: `Temperature` nos limites, outro lado) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WithTemperatureAtLimits_Succeeds` | Unidade, `[Theory]` 2 casos (0, 2) |
| (apoio: `OutputMode` inválido) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenOutputModeIsInvalid_FailsWithAcceptedValues` | Unidade (`Json`) |
| (apoio: `OutputMode` com outra caixa é aceito) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenOutputModeCaseDiffers_Succeeds` | Unidade, `[Theory]` 2 casos (`jsonschema`, `TAGS`) |
| (apoio: uma falha por chave) | Infrastructure.Tests | `OllamaOptionsValidatorTests` | `Validate_WhenSeveralKeysAreInvalid_ReportsOneFailurePerKey` | Unidade (`Model` em branco e `TimeoutSeconds` 0: 2 falhas) |
| (apoio: várias seções inválidas, AP-039) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (alterado) | Componente, `[Theory]` 2 → 3 casos |

**Asserções principais** (cada uma confere texto que só o projeto produz, AP-015; cada turno e cada mensagem com texto único, AP-017/AP-026):

- `SendAsync_WithTwoPreviousTurns_SendsSystemPromptHistoryAndNewMessageInOrder`: `BrainRequest` com `SystemPrompt` `system t0` e as mensagens `student t1` (User), `tutor t1` (Assistant), `student t2` (User), `tutor t2` (Assistant), `student t3` (User); `PromptFiles` numa pasta temporária do teste com `format.json.md` = `JSON FORMAT t0` e `format.tags.md` = `TAGS FORMAT t0`. O `FakeChatClient` recebeu **uma** chamada com 6 mensagens, nesta ordem: `System` com exatamente `system t0\n\nJSON FORMAT t0`; `User student t1`; `Assistant tutor t1`; `User student t2`; `Assistant tutor t2`; `User student t3`. Os textos de aluno e de professor são todos diferentes.
- `SendAsync_WithJsonSchemaMode_AsksForStructuredOutputWithTutorReplySchema`: `ChatOptions.ResponseFormat` é `ChatResponseFormatJson` com `Schema` igual (comparação estrutural de JSON, não de texto) a `TutorReplySchema.Json`; a mensagem `System` termina com `JSON FORMAT t0`.
- `SendAsync_WithTagsMode_AppendsTagsInstructionAndAsksForNoStructuredOutput`: `OutputMode = Tags`; `ResponseFormat` nulo; a mensagem `System` termina com `TAGS FORMAT t0` e **não** contém `JSON FORMAT t0`.
- `SendAsync_WhenOutputModeCaseDiffers_UsesCanonicalMode`: `jsonschema` e `JSONSCHEMA` → `ResponseFormat` presente e `System` com `JSON FORMAT t0`; `tags` → `ResponseFormat` nulo e `System` com `TAGS FORMAT t0` (o efeito, não só a ausência de exceção).
- `SendAsync_Always_PassesConfiguredModelAndTemperature`: `Model = model-t1`, `Temperature = 0.3`; `ChatOptions.ModelId == "model-t1"` e `ChatOptions.Temperature == 0.3f`.
- `SendAsync_WhenHistoryHasEmptyAssistantMessage_KeepsItUnchanged`: histórico `student t1` (User), `` (Assistant vazio), `correction t1` (User); as 3 mensagens chegam ao `IChatClient` na ordem, com a do meio vazia.
- `SendAsync_OnSecondCall_DoesNotCarryMessagesFromTheFirst`: 1ª chamada com `student a1`; 2ª com `student b2` (outro texto, outro tamanho); a 2ª chamada do cliente tem só `System` + `student b2` (nenhum `a1`).
- `SendAsync_WithValidJson_ReturnsParsedReplyWithRawText`: o cliente devolve um JSON válido com `reply` `Reply t1?`, uma correção `orig t1` → `fixed t1` (`why t1`); o `TutorReply` tem esses campos e `RawText` igual ao texto devolvido pelo cliente, `IsRawFallback` falso.
- `SendAsync_WhenReplyIsOutOfFormat_ThrowsInvalidOutputWithRejectedText`: o cliente devolve `no json here t1`; `BrainException` com `IsInvalidOutput`, `RawText == "no json here t1"` e `Rejection` preenchido.
- `SendAsync_WhenReplyIsValid_LogsRepliedWithoutStudentTextOrReply`: um evento com `EventId.Id == 2002`, nível `Information`, mensagem com `Ollama`, o modelo, `JsonSchema` e `<n> ms` (com `FakeTimeProvider` avançado 250 ms no `OnRun`: `250 ms`); a mensagem não contém o texto do aluno (`student t1`) nem o `Reply`.
- `SendAsync_WhenNothingAnswers_ThrowsNotRunningWithStartInstructionAndKey`: `FakeChatClient.Throws(new HttpRequestException(HttpRequestError.ConnectionError, "refused t1", new SocketException()))` com `Endpoint = http://ollama-test:1234`; `Kind == Unknown`; a mensagem contém `Ollama is not running at "http://ollama-test:1234"`, `ollama serve` e `Brain:Ollama:Endpoint`; `InnerException` é a exceção original; a mensagem **não** contém `refused t1`.
- `SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommandForConfiguredModel`: `Model = model-t2`; `Throws(HttpRequestException com StatusCode 404)`; `Kind == Unknown`; a mensagem contém `Ollama model "model-t2" is not downloaded`, `ollama pull model-t2` e `Brain:Ollama:Model`; não contém `Ollama is not running`.
- `SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` (AP-041, rótulos únicos por caso, AP-026; `Endpoint = http://ollama-test:1234`, `Model = model-t0`):

  | # | Exceção do cliente | Esperado | Trecho conferido |
  |---|---|---|---|
  | 1 | `HttpRequestException(ConnectionError, "refused t1", new SocketException())`, sem status | linha b | `Ollama is not running at "http://ollama-test:1234"` |
  | 2 | `HttpRequestException(NameResolutionError, "dns t2")`, sem status e sem exceção interna | linha b | `Ollama is not running at` (a regra é "sem status", não "tem `SocketException`") |
  | 3 | `HttpRequestException` com status 404 e mensagem `gone t3` (sem as palavras `model` e `not found`) | linha a | `ollama pull model-t0` (o status sozinho decide) |
  | 4 | `HttpRequestException` com status 400 e mensagem `model "m" not found t4` | linha a | `ollama pull model-t0` (o texto sozinho decide; status 400 não é 404) |
  | 5 | `HttpRequestException` com status 500 e mensagem `boom t5` | linha c | começa com `Ollama failed (`, contém `boom t5` e termina com `Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.` (status presente: não é "fora do ar") |
  | 6 | `HttpRequestException(ConnectionError, "model m not found t6")`, sem status | linha a | `ollama pull model-t0` e **não** `Ollama is not running` (a precedência da a sobre a b) |

  Em todos, `Kind == Unknown` (a mensagem é o que distingue) e a exceção original é a `InnerException`.
- `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` (B1, P9; `HttpRequestException` com status 500 em todos os casos, para cair na linha c; `Message` exata da exceção por caso, rótulos únicos, AP-026). A asserção compara a mensagem **inteira**: `Ollama failed (<esperado>). Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.`:

  | # | `Message` da exceção (`\n` e `\r` são os caracteres) | `<esperado>` | Ramo da derivação |
  |---|---|---|---|
  | 1 | `first t8\nsecond t8\nat Stack.Frame t8` | `first t8` | várias linhas: só a primeira; nenhum trecho de `second t8` nem de `Stack.Frame t8` na mensagem |
  | 2 | `\n   \nline t9\nafter t9` | `line t9` | começa por linha vazia e por linha só com espaços: a primeira **não vazia** |
  | 3 | `  padded t10  \r\nafter t10` | `padded t10` | espaços nas pontas e `\r` do fim de linha removidos |
  | 4 | `` (vazia) | `no error details were provided` | texto de reserva; sem `()` |
  | 5 | `  \n \t ` (só espaços e quebras) | `no error details were provided` | só espaços: reserva |

  Em todos, `Kind == Unknown` e a `InnerException` é a exceção original. O caso 5 da `[Theory]` `SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` (`boom t5`) continua provando o texto de biblioteca embutido; este teste prova a derivação do `<detalhe>`.
- `SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt`: o cliente lança `InvalidOperationException("bug t1")`; a mesma exceção sai de `SendAsync` (não é `BrainException`).
- `SendAsync_WhenOllamaDoesNotAnswerWithinTimeout_CancelsRequestAndThrowsTimeout`: `TimeoutSeconds = 1`; `HangsUntilCancelled()`; `OnRun` sinaliza o início; `Advance(999 ms)` → `Tokens[0].IsCancellationRequested` é `false`; `Advance(1 ms)` → `BrainException` com `Kind == Timeout`, mensagem com `Ollama did not answer in time (limit: 1 s)` e `Brain:Ollama:TimeoutSeconds`; `Tokens[0].IsCancellationRequested` é `true`. A espera pela tarefa usa `WaitAsync(TimeSpan.FromSeconds(10))`, para o Red falhar por tempo de teste e não travar a suíte (AP-046).
- `SendAsync_OnEachCall_StartsTimeoutAgain`: `TimeoutSeconds = 1`; a 1ª chamada responde (JSON válido) depois de `Advance(900 ms)` no `OnRun`; a 2ª fica pendente com `HangsUntilCancelled()`; `Advance(999 ms)` → token da 2ª **não** cancelado (com relógio compartilhado, 900 + 999 > 1000 já cancelaria); `Advance(1 ms)` → `Timeout`. Espera com `WaitAsync(TimeSpan.FromSeconds(10))`.
- `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`: `HangsUntilCancelled()`; o teste cancela o próprio `CancellationTokenSource` depois do início, sem avançar o relógio; a chamada lança `OperationCanceledException` (não `BrainException`); `Tokens[0].IsCancellationRequested` é `true`. Espera com `WaitAsync(TimeSpan.FromSeconds(10))`; a asserção do tipo distingue o `TimeoutException` do `WaitAsync` do `OperationCanceledException` esperado.
- `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`: `TimeoutSeconds = 1`; `HangsUntilCancelled(onCancelled: () => callerCts.Cancel())`; `Advance(1000 ms)` → o tempo estoura, o fake cancela o token do chamador e só então lança; a chamada lança `OperationCanceledException` (não `BrainException`/`Timeout`); `callerCts.IsCancellationRequested` e `Tokens[0].IsCancellationRequested` são `true`. Espera com `WaitAsync(TimeSpan.FromSeconds(10))`.
- Testes de fio (`OllamaTutorBrainHttpTests`, `OllamaChatClients.Create(options, handler)` com o `FakeHttpMessageHandler`, que lê o corpo da requisição antes de responder):
  - `SendAsync_WithTwoPreviousTurns_PostsToApiChatWithSystemHistoryAndNewMessage`: a requisição é `POST` para o caminho `/api/chat` do `Endpoint` (`http://ollama-test:1234/api/chat`); o corpo tem `model` = o configurado e `messages` com 6 itens (`role` `system`, `user`, `assistant`, `user`, `assistant`, `user`; `content` iguais aos da asserção do teste de unidade, a `system` com `system t0\n\nJSON FORMAT t0`); a resposta devolvida pelo handler (JSON válido) chega como `TutorReply`.
  - `SendAsync_WithJsonSchemaMode_PostsFormatWithTutorReplySchema`: o corpo tem `format` igual (comparação estrutural, `JsonNode.DeepEquals`) a `TutorReplySchema.Json` e `options.temperature` igual ao configurado.
  - `SendAsync_WithTagsMode_PostsNoFormat`: `OutputMode = Tags`; o corpo **não** tem a propriedade `format` (ou ela é nula); a resposta de tags válida é lida.
  - `SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections`: o handler devolve `Fixtures/Ollama/chat-structured.json`; o `TutorReply` tem `Reply` não vazio com exatamente uma pergunta (`ReplySentences`) e ao menos uma correção; `RawText` é JSON puro (começa por `{`, P6).
  - `SendAsync_WithRecordedModelNotFoundResponse_ThrowsPullCommandForConfiguredModel`: o handler devolve o status e o corpo de `model-not-found.*`; `Kind == Unknown`; a mensagem contém `ollama pull <modelo configurado>`; a `InnerException` é a exceção que a biblioteca produziu (tipo registrado na §12).
  - `SendAsync_WhenConnectionIsRefused_ThrowsNotRunningWithEndpoint`: o handler lança `HttpRequestException(ConnectionError, ..., new SocketException())`; mensagem com `Ollama is not running at "<Endpoint>"`.
  - `SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage`: o handler responde 500 com corpo `{"error":"boom t7"}`; `Kind == Unknown`; a mensagem começa com `Ollama failed (` e termina com `Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.`; não contém `Ollama is not running` nem `ollama pull`.
- `OllamaTutorBrainLiveTests` (Ollama no ar, modelo padrão baixado, `ENGLISHTUTOR_OLLAMA_ENDPOINT`/`ENGLISHTUTOR_OLLAMA_MODEL` opcionais; camada: o adaptador, **sem** a retentativa do ADR-006):
  - `SendAsync_WithFiveKnownErrors_AllInFormat`: as 5 frases de `LiveClaudeCode.KnownErrors`; nenhuma `BrainException` de saída inválida (5/5 no formato; a lista das rejeitadas entra na mensagem de falha); o número de respostas com a correção esperada é registrado nas Evidências, sem critério (a spec só exige o formato).
  - `SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand`: `Model = nao-existe-us006`; `Kind == Unknown`; mensagem com `ollama pull nao-existe-us006`.
  - `SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning`: o teste abre um `TcpListener` na porta 0, lê a porta e o fecha; `Endpoint = http://127.0.0.1:<porta>`; mensagem com `Ollama is not running at`.
  - `SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply`: histórico `student` (User), `` (Assistant), `Please correct your format.` (User); o Ollama responde e a resposta é lida como `TutorReply` válido ou como `InvalidOutput` de conteúdo, mas **não** falha por mensagem vazia (nenhuma `BrainException` de `Unknown`).
- `RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback`: `Brain:Provider = Ollama`; o `ScriptedChatClient` devolve o JSON equivalente ao de `ConsoleTutorReplies` (`Reply t1?`, correção `orig t1` → `fixed t1` (`why t1`)); entrada `sentence t1`, `/quit`. Então: o cliente falso recebeu 1 chamada; a mensagem `System` contém o texto do `FixedTutorPromptSource` e `Output format` (o `prompts/format.json.md` real); a última mensagem é `User` e contém `sentence t1` e `Mode: free-conversation`; a saída tem uma linha que termina com `Tutor: Reply t1?`, a linha `Feedback:` e `  - orig t1 → fixed t1 (why t1)` (mesmo formato do Claude Code); a saída **não** contém `The tutor is not connected yet.`; o erro está vazio; código 0.
- `RunAsync_WhenOllamaFails_ShowsInstructionOnErrorAndContinuesSession` (casos: `HttpRequestException(ConnectionError, ...)` → `Ollama is not running at`; `HttpRequestException` 404 → `ollama pull`): entrada `first sentence t1`, `second sentence t2`, `/quit`; o cliente lança na 1ª chamada e depois responde JSON válido (`Reply t2?`). Então: `Error` contém `The tutor could not answer: ` + o trecho do caso e `You can send a sentence again or type /quit to exit.`; não contém `An unexpected error occurred`; a saída tem `Tutor: Reply t2?`; o cliente recebeu 2 chamadas e a 2ª só tem a mensagem do aluno do 2º turno (`second sentence t2`; o turno que falhou não entrou na janela); código 0.
- `RunAsync_WhenOllamaTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt`: `Brain:Provider = Ollama` e `Brain:Ollama:TimeoutSeconds = 0`; código `ExitCodes.InvalidConfiguration`; `Error` contém `Invalid configuration:` e `Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found 0)`; `Output` vazio.
- `RunAsync_WhenSeveralSectionsAreInvalid_...` (novo `[InlineData]`: provedor `ClaudeCode`, chave `Brain:Ollama:TimeoutSeconds` = `0`, esperado `Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found 0)`): as duas seções inválidas (`Conversation` e `Brain:Ollama`) aparecem na saída de erro; código `InvalidConfiguration`; nada na saída padrão. É também a prova do limite aceito da P12 (Ollama inválido com o Claude Code como provedor).
- `AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` (`Ollama`, `ollama`): com `AddOptions<BrainOptions>().Configure(o => o.Provider = <caso>)`, `GetService<ITutorBrain>()` é `OllamaTutorBrain` e `Name` é `Ollama`. `..._WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain`: `Provider = ClaudeCode` → `ClaudeCodeTutorBrain`.
- `AddEnglishTutorInfrastructure_Always_RegistersOllamaOptionsValidator`: `Configure(o => o.Model = " ")`; ler `IOptions<OllamaOptions>` lança `OptionsValidationException` com uma falha que contém `Brain:Ollama:Model is required`.
- Testes do validador: cada um confere a mensagem exata da §2.1 com o valor do caso (uma falha por chave); `Validate_WhenSeveralKeysAreInvalid_ReportsOneFailurePerKey` confere 2 falhas distintas, cada uma com a própria chave.
- `Constructor_Always_UsesDocumentedDefaults`: `Endpoint == "http://localhost:11434"`, `Model == "qwen2.5:14b-instruct-q4_K_M"`, `Temperature == 0.6`, `OutputMode == "JsonSchema"`, `TimeoutSeconds == 60`.

**Contagem.** Fora de `LiveBrain`: Core.Tests 134 (sem mudança); Console.Tests 51 → **55** (−1 removido; +1 conversa, +2 falhas, +1 `TimeoutSeconds` inválido, +1 caso novo na `[Theory]` de várias seções); Infrastructure.Tests 101 → **167** (+66: `OllamaOptionsTests` 1; `OllamaOptionsValidatorTests` 24 casos / 12 métodos; `OllamaTutorBrainTests` 30 casos / 19 métodos; `OllamaTutorBrainHttpTests` 7 casos / 7 métodos; `InfrastructureServiceCollectionExtensionsTests` +4 casos / +3 métodos). Total **356**. `LiveBrain`: +5 (`OllamaTutorBrainLiveTests` 4, `OllamaFixtureRecorderLiveTests` 1).

## 5. Tarefas (ordem de execução TDD)

**Pré-condição** (antes da T1): o orquestrador aplica a §2.9 em dois commits, `docs(ai): update spec for US-006` e `docs(ai): update architecture for US-006`, com o ADR-009 em "Aceito", se aprovado. Na T1, antes do primeiro teste, o Executor roda o **D15c** e cola o resultado nas Evidências.

**Mutações depois do Green.** Quando um teste nasce verde porque o esqueleto do Red ainda não tem a regra (validador sem as faixas, `IsServerFailure` devolvendo `false`), a mutação não tem o que quebrar no Red. Nesses casos a tarefa diz "mutação depois do Green": o Executor faz o commit `feat`, aplica a mutação pelo efeito descrito, roda o teste e vê falhar, desfaz pela edição inversa (sem commit), confere que `git status --short` não mostra mudança em `src/` e cola o resultado nas Evidências (AP-017, AP-029, AP-037, AP-045). Mutação sobre teste de fixture gravada é escolhida depois da gravação, para o ramo que a fixture percorre.

- [x] T1 — `Brain:Ollama`: opções, validação e configuração. (feito: test 7c8ed13, feat 1acc4a9; D15c e mutações nas Evidências)
  Red: esqueleto `OllamaOptions` com as propriedades **sem** inicializador e as constantes; `OllamaOptionsValidator.Validate` devolvendo `Success`; testes `OllamaOptionsTests.Constructor_Always_UsesDocumentedDefaults`, os 12 métodos de `OllamaOptionsValidatorTests`, `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersOllamaOptionsValidator` (o validador ainda não está registrado), `EnglishTutorAppTests.RunAsync_WhenOllamaTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt` e o novo caso de `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (a seção ainda não é ligada: a sessão abre e sai com 0). Falham pela asserção, exceto os cinco que esperam sucesso do validador, que nascem verdes porque o esqueleto aceita tudo: `Validate_WithDefaultOptions_Succeeds`, `Validate_WithHttpAndHttpsEndpoints_Succeeds`, `Validate_WithTemperatureAtLimits_Succeeds`, `Validate_WhenOutputModeCaseDiffers_Succeeds` e `Validate_WithTimeoutSecondsAtLimits_Succeeds` (mutação depois do Green).
  Green: inicializadores, regras do validador (§2.1), registro do validador em `AddEnglishTutorInfrastructure`, bloco `Brain:Ollama` no `appsettings.json`, bind com `ValidateOnStart` no `EnglishTutorApp.BuildHost`.
  Mutações depois do Green, cada uma desfeita pela edição inversa: "o limite inferior de `TimeoutSeconds` aceito passa a ser 2" (caso 1 de `Validate_WithTimeoutSecondsAtLimits_Succeeds` falha) e "o superior passa a ser 3599" (caso 3600 falha); "a temperatura aceita deixa de incluir 0" e, separadamente, "deixa de incluir 2" (os dois casos de `Validate_WithTemperatureAtLimits_Succeeds`); "o validador do `OutputMode` passa a diferenciar maiúsculas" (`Validate_WhenOutputModeCaseDiffers_Succeeds` falha); "`https` deixa de ser aceito" (`Validate_WithHttpAndHttpsEndpoints_Succeeds` falha); "o `Model` padrão passa a ser vazio" (`Validate_WithDefaultOptions_Succeeds` falha).
  Commits: `test(brain): cover Ollama options and validation` + `feat(brain): add Brain:Ollama options with validation`.
  Escolhas de forma: nenhuma

- [ ] T2 — Adaptador: pedido, modos e leitura da resposta (sem falhas e sem tempo máximo).
  Estado dos ramos até a T4 e a T5: uma `HttpRequestException` ou qualquer outra exceção do cliente propaga sem tradução; não há relógio (`SendAsync` passa o `ct` direto ao `IChatClient`). Os testes desta tarefa usam só respostas e histórico válidos, que ficam nos ramos implementados.
  Red: pacotes `OllamaSharp` e `Microsoft.Extensions.AI.Abstractions` (§6) em `Directory.Packages.props` e no `.csproj` da Infrastructure (o Executor registra as versões na §12 no mesmo commit); esqueletos `OllamaTutorBrain` (`SendAsync` lança `NotImplementedException`), `OllamaChatClients.Create` (lança `NotImplementedException`) e `OllamaLog.Replied` (método parcial do gerador, sem chamada ainda); fakes `FakeChatClient` (`Returns(string)`, `Calls`, `Tokens`, `OnRun`) e `FakeHttpMessageHandler`; testes `OllamaTutorBrainTests.SendAsync_WithTwoPreviousTurns_SendsSystemPromptHistoryAndNewMessageInOrder`, `OllamaTutorBrainTests.SendAsync_WithJsonSchemaMode_AsksForStructuredOutputWithTutorReplySchema`, `OllamaTutorBrainTests.SendAsync_WithTagsMode_AppendsTagsInstructionAndAsksForNoStructuredOutput`, `OllamaTutorBrainTests.SendAsync_WhenOutputModeCaseDiffers_UsesCanonicalMode`, `OllamaTutorBrainTests.SendAsync_Always_PassesConfiguredModelAndTemperature`, `OllamaTutorBrainTests.SendAsync_WhenHistoryHasEmptyAssistantMessage_KeepsItUnchanged`, `OllamaTutorBrainTests.SendAsync_OnSecondCall_DoesNotCarryMessagesFromTheFirst`, `OllamaTutorBrainTests.SendAsync_WithValidJson_ReturnsParsedReplyWithRawText`, `OllamaTutorBrainTests.SendAsync_WhenReplyIsOutOfFormat_ThrowsInvalidOutputWithRejectedText`, `OllamaTutorBrainTests.SendAsync_WhenReplyIsValid_LogsRepliedWithoutStudentTextOrReply`, `OllamaTutorBrainHttpTests.SendAsync_WithTwoPreviousTurns_PostsToApiChatWithSystemHistoryAndNewMessage`, `OllamaTutorBrainHttpTests.SendAsync_WithJsonSchemaMode_PostsFormatWithTutorReplySchema` e `OllamaTutorBrainHttpTests.SendAsync_WithTagsMode_PostsNoFormat`. Todos falham pela `NotImplementedException` do esqueleto.
  Green: §2.2 (leitura do arquivo de formato, mensagens, `ChatOptions`, normalização do modo, `parser`, `InvalidOutput`, log) e `OllamaChatClients.Create` (§2.2); comentário de `TutorReplySchema.cs` (§2.10). O Executor confere nos testes de fio como a versão fixada do `OllamaSharp` mapeia `ChatOptions` para o corpo (`format`, `options.temperature`) e registra na §12 o que diferir do esperado (parar se `format` não sair com o schema: o usuário decide).
  Commits: `test(brain): cover Ollama request composition and reply parsing` + `feat(brain): add OllamaTutorBrain over IChatClient`.
  Escolhas de forma: nenhuma | §12 em <hash> (versões dos pacotes)

- [ ] T3 — Gravação das respostas reais do Ollama (sem código de produção).
  Pré-condição e passos (§2.8): instalar o Ollama e baixar o modelo (`ollama --version` e `ollama list` nas Evidências; `ollama --help` com `serve` e `pull`, §2.5); criar `OllamaFixtureRecorderLiveTests`, `RecordingHttpHandler` e `LiveOllama`; rodar `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~OllamaFixtureRecorderLiveTests"`; copiar de `recorded-fixtures/` para `Fixtures/Ollama/` os 4 arquivos, escrever o `README.md` com a seção "Diferenças em relação à chamada do adaptador" (§2.8); colar `exceptions.txt` nas Evidências e a conclusão na §12 (tipo e status da exceção do modelo ausente e da conexão recusada; se o 404 não casar com a linha a da §2.3, o ajuste); conferir que `chat-structured.json` é JSON puro (P6); rodar o D13. Teste novo: `OllamaTutorBrainHttpTests.SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections` (nasce verde: caracterização). Red por mutação temporária pelo efeito, escolhida depois da gravação e aplicada no próprio Red (o adaptador já existe): "o adaptador deixa de entregar o conteúdo da resposta ao parser" (o teste falha pela asserção, com `InvalidOutput`), desfeita pela edição inversa.
  Commit: `test(brain): record Ollama responses`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T4 — Falhas do servidor: "Ollama is not running" e "ollama pull".
  Red: `OllamaFailures` com `IsServerFailure` devolvendo `false` e `Translate` lançando `NotImplementedException`; testes `OllamaTutorBrainTests.SendAsync_WhenNothingAnswers_ThrowsNotRunningWithStartInstructionAndKey`, `OllamaTutorBrainTests.SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommandForConfiguredModel`, `OllamaTutorBrainTests.SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` (6 casos), `OllamaTutorBrainTests.SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` (5 casos), `OllamaTutorBrainHttpTests.SendAsync_WithRecordedModelNotFoundResponse_ThrowsPullCommandForConfiguredModel`, `OllamaTutorBrainHttpTests.SendAsync_WhenConnectionIsRefused_ThrowsNotRunningWithEndpoint` e `OllamaTutorBrainHttpTests.SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage` (falham: a exceção original propaga em vez de `BrainException`), e `OllamaTutorBrainTests.SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt`, que nasce verde (o esqueleto não traduz nada).
  Green: §2.3 e §2.5 (inclusive a derivação de `<detalhe>`: primeira linha não vazia, sem espaços nas pontas, ou `no error details were provided`); o `catch` do adaptador (`when (OllamaFailures.IsServerFailure(exception))`), com os tipos de exceção que a T3 registrou.
  Mutação depois do Green (para `SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt`): "`IsServerFailure` passa a devolver `true` para qualquer exceção" (o teste recebe `BrainException` em vez de `InvalidOperationException` e falha); depois, para a precedência (caso 6), "a linha b passa a ser avaliada antes da a" (o caso 6 falha). Para o `<detalhe>` (B1; os cinco casos nascem vermelhos no Red pela `NotImplementedException`; as mutações provam que cada ramo é isolado e são aplicadas depois do Green): "o `<detalhe>` passa a ser a mensagem inteira da exceção" (casos 1, 2 e 3 de `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` falham); "o `<detalhe>` deixa de pular linhas vazias e usa a primeira linha mesmo em branco" (caso 2 falha); "o `<detalhe>` deixa de remover espaços e `\r` das pontas" (caso 3 falha); "o texto de reserva deixa de existir e a mensagem fica com `()`" (casos 4 e 5 falham). Cada mutação é desfeita pela edição inversa.
  Commits: `test(brain): cover Ollama server failures` + `feat(brain): classify Ollama failures with instructions`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T5 — Tempo máximo por chamada.
  Red: `FakeChatClient.HangsUntilCancelled(Action? onCancelled = null)` (termina só com `OperationCanceledException` quando o token é cancelado; `onCancelled` roda depois de o token ser cancelado e antes de a exceção sair do fake); `OllamaFailures.Timeout(int, OperationCanceledException)` lançando `NotImplementedException`; testes `OllamaTutorBrainTests.SendAsync_WhenOllamaDoesNotAnswerWithinTimeout_CancelsRequestAndThrowsTimeout`, `OllamaTutorBrainTests.SendAsync_OnEachCall_StartsTimeoutAgain` e `OllamaTutorBrainTests.SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` (falham pelo `WaitAsync` de 10 s: o adaptador ainda não tem relógio) e `OllamaTutorBrainTests.SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`, que nasce verde (o token do chamador já vai direto ao cliente): Red por mutação temporária pelo efeito, aplicada no próprio Red porque o repasse do token já existe, "o cancelamento do chamador deixa de chegar ao cliente" (falha pelo `WaitAsync` de 10 s, com `TimeoutException`, sem travar a suíte), desfeita pela edição inversa.
  Green: §2.4 (relógio por chamada, token ligado, filtro completo do `Timeout`) e a mensagem de tempo esgotado da §2.5.
  Mutação depois do Green, para `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`: "a condição do chamador deixa de contar no filtro do `Timeout`" (o teste recebe `BrainException(Timeout)` e falha), desfeita pela edição inversa.
  Commits: `test(brain): cover Ollama timeout and caller cancellation` + `feat(brain): stop Ollama calls after TimeoutSeconds`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T6 — Seleção do cérebro por `Brain:Provider` (provisória).
  Red: `AddEnglishTutorInfrastructure` sem mudança de seletor (continua fixo no Claude Code); testes `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` (2 casos; falham: resolve `ClaudeCodeTutorBrain`) e `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain` (nasce verde).
  Green: §2.7 (registros concretos, `IChatClient` com chave `"Ollama"`, seletor com o comentário `Provisional selector until the BrainFactory (US-007).`) e o comentário do cabeçalho do arquivo (§2.10).
  Mutação depois do Green, para `..._WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain`: "o seletor devolve sempre o `OllamaTutorBrain`" (o teste falha), desfeita pela edição inversa.
  Commits: `test(brain): cover brain selection by Brain:Provider` + `feat(brain): select the Ollama brain by Brain:Provider`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T7 — Console conversa com o Ollama.
  Red: `ScriptedChatClient` (Console.Tests/Support) e `CreateOllamaOptions`; sai `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected` com o comentário (§2.10); testes `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback` e `EnglishTutorAppTests.RunAsync_WhenOllamaFails_ShowsInstructionOnErrorAndContinuesSession` (2 casos). Falham pela asserção: o console ainda tem o ramo "não conectado" (a saída tem `The tutor is not connected yet.` e o cliente falso não recebe chamada).
  Green: §2.6 (handler único em `RunSessionAsync`; apagar `TutorNotConnectedInputHandler.cs` e `ConsoleText.TutorNotConnected`).
  Commits: `test(console): cover the console talking to Ollama` + `feat(console): connect the Ollama provider to the conversation`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T8 — `LiveBrain` (D5) e medição de VRAM (D6).
  Testes `OllamaTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_AllInFormat`, `OllamaTutorBrainLiveTests.SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand`, `OllamaTutorBrainLiveTests.SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning` e `OllamaTutorBrainLiveTests.SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply`, com `[Trait("Category", "LiveBrain")]`. Nascem verdes (o adaptador existe): Red por mutação temporária pelo efeito, desfeita pela edição inversa: "`IsServerFailure` passa a devolver `false`" (o teste da porta fechada falha com a `HttpRequestException` crua; o do modelo ausente também) e "o adaptador deixa de enviar o `format`" (anotar o efeito na saída da fumaça; sem critério). Rodar o D5 em 3 rodadas, o D6 e atualizar a `ARCHITECTURE.md` §7 com a medição (texto da §2.9).
  Commits: `test(brain): add Ollama LiveBrain smoke tests` + `docs(ai): record Ollama VRAM measurement`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] T9 — DoD e evidências.
  Rodar D1–D18 (D15 depois de todo o código), colar nas Evidências com data e hash, atualizar as contagens da §4 e de D2/D11 se algo mudou (AP-031) e conferir cada afirmação de cobertura da §12 contra a §4 (AP-049).
  Commit: `docs(ai): add US-006 evidence`.
  Escolhas de forma: nenhuma | §12 em <hash>

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| `OllamaSharp` (versão estável mais recente com `OllamaApiClient : IChatClient`; o Executor registra a versão na §12 no mesmo commit da T2 e a fixa em `Directory.Packages.props`, grupo `Infrastructure`) | `EnglishTutor.Infrastructure` | Cliente HTTP do Ollama que implementa `IChatClient` (ADR-009). Pacote externo só na Infrastructure (R1) |
| `Microsoft.Extensions.AI.Abstractions` (versão estável compatível com o `OllamaSharp` escolhido; o Executor registra a versão na §12 no mesmo commit da T2) | `EnglishTutor.Infrastructure` | O tipo `IChatClient`, `ChatMessage`, `ChatOptions` e `ChatResponseFormat` usados pelo adaptador |

Os projetos de teste recebem os tipos de `Microsoft.Extensions.AI` pela referência de projeto (transitivo); nenhum pacote novo neles. `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) já existe. Se o `OllamaSharp` não implementar `IChatClient` na versão estável, o Executor para e registra na §12 (ADR-009, contexto).

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| A exceção que a biblioteca lança para o modelo ausente não é a esperada (sem `StatusCode`, sem o texto `model`/`not found`) | A T3 grava a resposta 404 real e a cadeia de exceções; a linha a da §2.3 é ajustada ao observado, registrada na §12, antes da T4. O teste com a resposta gravada acusa mudança de versão |
| O Ollama não está instalado ou o modelo (cerca de 9 GB) não cabe | Pré-condição da T3 (§2.8); se falhar, o Executor para e o usuário decide na §12 (P1/P10) |
| O `OllamaSharp` não mapeia `ChatOptions.ResponseFormat` para `format` como o esperado | Os testes de fio (T2) provam o corpo; divergência é registrada na §12 e o usuário decide (§5 T2) |
| A resposta real vem com cerca markdown ou texto em volta (P6) | A T3 confere a gravação; o parser continua rejeitando; se a gravada tiver cerca, parar e registrar (§2.2) |
| Teste com `HangsUntilCancelled` trava a suíte no Red ou na mutação | `WaitAsync(TimeSpan.FromSeconds(10))` nos quatro testes de tempo, inclusive o de caracterização `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` (§4, AP-046) |
| O teste dos dois cancelamentos depende da ordem entre o cancelamento do chamador e a exceção | `onCancelled` do fake roda antes de a exceção sair do fake: ordem determinística |
| Primeira chamada do modelo (carga na VRAM) passa do padrão de 60 s | A mensagem de `Timeout` aponta para `Brain:Ollama:TimeoutSeconds`; D5 roda depois de a T3 ter carregado o modelo; registrado no ADR-009 |
| D5 (`LiveBrain`) fica vermelho por formato com um modelo local menor, sem relação com regressão | Camada: o adaptador, sem retentativa; tolerância zero no formato; 3 rodadas seguidas (§10). Risco de oscilação: o schema garante a forma do JSON, mas não a regra de "exatamente uma pergunta" nem o limite de 3 frases; um modelo de 14B pode errar uma delas às vezes. Vermelho: o Executor para, classifica a causa na §12 (JSON inválido × regra de conteúdo) e o usuário decide, com registro prévio na spec, se muda camada (com retentativa) ou tolerância (AP-032) |
| Os testes de console usam o `OllamaApiClient` real por engano e chamam a rede | `CreateOllamaOptions` sempre registra o `IChatClient` falso com chave `"Ollama"`; um `ScriptedChatClient` sem resposta lança (como o `ScriptedTutorBrain`) |
| Teste `LiveBrain` da porta fechada depende de uma porta livre | Abre e fecha um `TcpListener` na porta 0 e usa essa porta; não depende do Ollama |
| `Brain:Ollama` inválido impede o uso do Claude Code (P12) | Limite aceito, consumidores listados na §2.1; o erro nomeia a chave e o valor esperado |
| Um turno com retentativa pode levar 2 × `TimeoutSeconds` | Registrado no ADR-009 e na §2.4 |
| Falhas do Ollama (`Unknown`) não acionam o fallback da US-007 | Registrado no ADR-009 e na P2; nenhum cenário atual depende disso (Claude Code é o primário no cenário de fallback) |

## 8. Fora de escopo

- `BrainFactory`, `FallbackTutorBrain`, `Brain:FallbackProvider` e o aviso de troca de professor: **US-007** (cenários já na spec). O seletor provisório (§2.7) é removido lá; texto exato a registrar na spec da US-007 na §2.9 (D7 com o `git grep`), aplicado no commit de spec antes da T1 (AP-022).
- Suíte de contrato do `ITutorBrain` (US-008): **não é adiamento** desta US. O cenário "mesma suíte para todos os provedores" da US-008 já exige uma subclasse para o Ollama com fakes; o `FakeChatClient` e o `FakeHttpMessageHandler` serão reaproveitados lá. Nada novo a registrar.
- Comparação no `eval` e taxa de resposta fora do formato do modelo local (US-009): cenários da US-009 já na spec (`--providers ClaudeCode,Ollama`, Ollama "unavailable"); nada novo a registrar.
- Streaming da resposta, `keep_alive` e `num_ctx`: nenhum cenário da US; reavaliar se a latência da primeira chamada incomodar (ADR-009, "o que precisa ser medido"). Sem US de destino.
- Novos valores em `BrainErrorKind` para falhas do Ollama (P2): mudaria RN-BRAIN07/08; só com decisão do usuário e spec antes do código.
- `ROADMAP.md` (linha do ambiente do Ollama): o Documentador atualiza (§2.9); não é tarefa do Executor.

Adiamentos procurados também nas P* e nos comentários de teste (AP-040): os únicos são o seletor provisório (US-007, registrado na §2.9), a troca da linha do ROADMAP (Documentador) e as decisões herdadas de planos antigos que esta US fecha (`docs/us/US-002.md` P5 → P6; `docs/us/US-003.md` e ADR-006 `RawText` vazio → P5).

## 9. Perguntas em aberto

(Padrões propostos; o usuário pode mudar qualquer um na §12.)

- **P1 — Cliente do Ollama.** **Padrão proposto:** `IChatClient` com o `OllamaSharp` (`OllamaApiClient`), como a ARCHITECTURE §6.4 pede, em vez de `HttpClient` direto; o Executor confere no NuGet na T2 que o pacote implementa `IChatClient` e registra as versões (§6). Alternativa: `HttpClient` direto, sem pacote novo (exige mudar a §6.4 e o ADR-009). Teste: `OllamaTutorBrainHttpTests.SendAsync_WithTwoPreviousTurns_PostsToApiChatWithSystemHistoryAndNewMessage`.
- **P2 — `BrainErrorKind` das falhas do Ollama.** O cenário pede só a mensagem; o enum não tem "servidor fora do ar" nem "modelo ausente". **Padrão proposto:** `Unknown` para as duas, com a mensagem distinguindo; `Timeout` para o tempo. Limite aceito (AP-040), consumidores e efeito: a mensagem ao aluno (diz o que houve e o que fazer; sem efeito de perda); o log `BrainFailed` do console (registra `Unknown`); o fallback da **US-007**: a RN-BRAIN08 não aciona o fallback para `Unknown`, então com o Ollama como `Provider`, "fora do ar" e "modelo ausente" **não** acionam o reserva, e `Timeout` aciona (nenhum cenário da US-007 usa o Ollama como primário, conferido em `SPRINT_1.md`: o cenário "fallback por limite de uso" tem Claude Code como `Provider` e Ollama como `FallbackProvider`); a suíte de contrato da US-008 (testa `TutorReply`, não os tipos de falha); o D5 (não depende do tipo). Alternativas: valores novos no enum (muda RN-BRAIN07/08 e a §4) ou reaproveitar `CliNotFound` (nome enganoso). Teste: `OllamaTutorBrainTests.SendAsync_WhenServerCallFails_ClassifiesByStatusAndText`.
- **P3 — Tempo máximo do Ollama (RN-BRAIN09) e cenário novo.** A RN-BRAIN09 vale para "o cérebro", mas a US-006 não tem cenário de tempo. **Padrão proposto:** implementar `Brain:Ollama:TimeoutSeconds` (padrão 60, faixa 1 a 3600, como o Claude Code) e acrescentar à spec da US-006 a RN-BRAIN07/09 em "Regras" e o cenário "tempo esgotado" (§2.9). Alternativa: não implementar (a RN-BRAIN09 ficaria sem cumprir no Ollama e a chamada só terminaria pelo `HttpClient.Timeout` de 100 s, com erro inesperado). Teste: `OllamaTutorBrainTests.SendAsync_WhenOllamaDoesNotAnswerWithinTimeout_CancelsRequestAndThrowsTimeout`.
- **P4 — Onde mora a instrução de formato no Ollama.** **Padrão proposto:** o adaptador anexa o conteúdo de `format.json.md` ou `format.tags.md` à mensagem `System`, separado por uma linha em branco (`"\n\n"`), lendo o arquivo a cada chamada (ADR-002, item 4; RN-BRAIN02). Teste: `OllamaTutorBrainTests.SendAsync_WithTwoPreviousTurns_SendsSystemPromptHistoryAndNewMessageInOrder` e `OllamaTutorBrainTests.SendAsync_WithTagsMode_AppendsTagsInstructionAndAsksForNoStructuredOutput`.
- **P5 — Mensagem `Assistant` vazia (ADR-006, `docs/us/US-003.md`: "fica com o adaptador US-004/US-006").** **Padrão proposto:** o adaptador envia as mensagens como estão, sem filtrar nem substituir; a aceitação pelo Ollama é conferida em teste `LiveBrain`. Se o Ollama recusar, o Executor para e o usuário decide (ex.: substituir por um texto fixo). Teste: `OllamaTutorBrainTests.SendAsync_WhenHistoryHasEmptyAssistantMessage_KeepsItUnchanged` e `OllamaTutorBrainLiveTests.SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply`.
- **P6 — JSON com cerca markdown ou texto em volta (`docs/us/US-002.md` P5: "decidir na US-006 com a saída real do Ollama").** **Padrão proposto:** continua rejeitado; o parser não muda. A T3 grava a resposta real com `format`; se for JSON puro (esperado, o `format` restringe a saída), o teste com a gravada prova; se vier com cerca, parar e registrar. Teste: `OllamaTutorBrainHttpTests.SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections`.
- **P7 — Seleção do `ITutorBrain` até a US-007.** **Padrão proposto:** seletor provisório no registro do DI, por `Brain:Provider` normalizado, com o comentário `Provisional selector until the BrainFactory (US-007).` e o D7 acrescentado à spec da US-007 (§2.9). Alternativa: `BrainFactory` mínima agora (escopo da US-007). Teste: `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` e `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain`.
- **P8 — `TutorNotConnectedInputHandler` e o texto "The tutor is not connected yet.".** **Padrão proposto:** apagar a classe e a constante (o cenário da US exige que o texto não apareça; a classe ficaria sem uso). Teste: `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback`.
- **P9 — Mensagem de falha genérica do Ollama.** **Padrão proposto:** `Ollama failed (<detalhe>). Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.`, com `<detalhe>` = primeira linha não vazia da mensagem da exceção, sem espaços nas pontas (ou `no error details were provided` quando a mensagem é vazia ou só tem espaços e quebras de linha). Teste: `OllamaTutorBrainTests.SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` (caso 5), `OllamaTutorBrainTests.SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` (cada ramo da derivação) e `OllamaTutorBrainHttpTests.SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage`.
- **P10 — Critério do D5 e instruções do Ollama.** O D5 da spec pede "5 frases de fumaça, 5/5 no formato". **Padrão proposto:** camada do adaptador (sem a retentativa do ADR-006), tolerância zero no formato, 3 rodadas seguidas; o número de correções esperadas só é registrado. As instruções `ollama serve` e `ollama pull` são conferidas com o Ollama instalado (T3). Teste: `OllamaTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_AllInFormat`.
- **P11 — Quem registra a VRAM na `ARCHITECTURE.md` §7 (spec D6).** **Padrão proposto:** o Executor, na T8, com o texto da §2.9 e os números do D6, em commit `docs(ai)` próprio (a medição só existe depois do código). Sem efeito observável.
- **P12 — `Brain:Ollama` validado na partida com qualquer provedor.** **Padrão proposto:** sim, simétrico ao `Brain:ClaudeCode` (US-004); limite aceito com consumidores na §2.1. Teste: `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (caso com provedor `ClaudeCode` e `Brain:Ollama:TimeoutSeconds` = 0).

## 10. Definition of Done (comandos verificáveis)

Ambiente dos comandos com Ollama real (D5–D10): Windows PowerShell 5.1, Ollama instalado e no ar em `http://localhost:11434` com `qwen2.5:14b-instruct-q4_K_M` baixado, sem `Brain__*` herdadas (`Get-ChildItem Env:Brain__* | Remove-Item`). O Ollama é um servidor local: variáveis do ambiente do cliente não mudam o que o adaptador envia, só as chaves `Brain__Ollama__*` que o comando define (AP-034). Nas células da tabela, `\|` é só o escape do Markdown; no comando é `|`.

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: 356 casos (Core.Tests 134, Console.Tests 55, Infrastructure.Tests 167), em < 30 s, sem rede, sem GPU e sem processo real |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% (o Core não muda nesta US) |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | (spec) `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama"`, em 3 rodadas seguidas: `1..3 \| ForEach-Object { dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama"; "round $_ exit=$LASTEXITCODE" }` | 3 linhas `round <n> exit=0`. O filtro também roda o gravador de fixtures (`OllamaFixtureRecorderLiveTests`, que só escreve em `bin/`) e os outros 3 testes `LiveBrain`; os 5 testes por rodada passam. **Camada:** o adaptador (`OllamaTutorBrain.SendAsync`), sem a retentativa do ADR-006. **Tolerância:** zero no formato (5/5 frases sem `BrainException` de saída inválida), como a spec; o nº de correções esperadas só é registrado nas Evidências. **Rodadas:** 3 seguidas. **Vermelho:** parar, classificar a causa na §12 e o usuário decide, com registro prévio na spec (risco de oscilação, §7; AP-032) |
| D6 | (spec) `$baseline = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits); $job = Start-Job { Set-Location $using:PWD; dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama" }; $samples = @(); while ($job.State -eq 'Running') { $samples += [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits); Start-Sleep -Seconds 2 }; Receive-Job $job; "baseline=$baseline peakMiB=$(($samples \| Measure-Object -Maximum).Maximum)"; ollama ps` | testes verdes; linha `baseline=<n> peakMiB=<m>` com `m` maior que `n`; `ollama ps` lista o modelo com o tamanho em VRAM; os números entram na `ARCHITECTURE.md` §7 (T8) |
| D7 | (spec) No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; "I have worked with C# since five years", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider` | uma linha `Tutor: <reply>`; nenhuma linha "The tutor is not connected yet."; `exit=0`. O pipe no PowerShell 5.1 é a cobertura exigida pelo `padroes-codigo` (AP-006); o BOM continua provado pelo teste da US-001 |
| D8 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; $env:Brain__Ollama__Endpoint="http://localhost:1"; "hi t1", "hi t2", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider, Env:Brain__Ollama__Endpoint` | duas vezes, na saída de erro, `The tutor could not answer: Ollama is not running at "http://localhost:1". Start it ...` com `ollama serve` e `Brain:Ollama:Endpoint`, cada uma seguida de `You can send a sentence again or type /quit to exit.`; nenhum `An unexpected error occurred`; `exit=0` (cenário "Ollama indisponível" de ponta a ponta, sem parar o serviço) |
| D9 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; $env:Brain__Ollama__Model="nao-existe-us006"; "hi t1", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider, Env:Brain__Ollama__Model` | `The tutor could not answer: Ollama model "nao-existe-us006" is not downloaded. Run "ollama pull nao-existe-us006" ...`; nenhum `An unexpected error occurred`; `exit=0` |
| D10 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; $env:Brain__Ollama__TimeoutSeconds="1"; "hi t1", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider, Env:Brain__Ollama__TimeoutSeconds` | `The tutor could not answer: Ollama did not answer in time (limit: 1 s) and the request was cancelled...`; nenhum `An unexpected error occurred`; `exit=0`. Se o modelo responder antes de 1 s (modelo aquecido e frase curta), repetir uma vez e registrar |
| D11 | `dotnet test --filter "Category!=LiveBrain&FullyQualifiedName~Ollama"` | verde; 69 casos (`OllamaOptionsTests` 1, `OllamaOptionsValidatorTests` 24, `OllamaTutorBrainTests` 30, `OllamaTutorBrainHttpTests` 7, testes de console e de DI com `Ollama` no nome da classe ou do método: 4 + 3 = 7); o Executor confere o número real na T9 e atualiza (AP-031) |
| D12 | `Select-String -Path src/EnglishTutor.Console/appsettings.json -Pattern '"Endpoint": "http://localhost:11434"','"Model": "qwen2.5:14b-instruct-q4_K_M"','"Temperature": 0.6','"TimeoutSeconds": 60'` | 5 linhas: `Endpoint`, `Model` e `Temperature` do Ollama (3 linhas) e `TimeoutSeconds` do Claude Code e do Ollama (2 linhas); as do Ollama dentro de `Brain:Ollama` (ARCHITECTURE §6.1) |
| D13 | `Select-String -Path tests/EnglishTutor.Infrastructure.Tests/Fixtures/Ollama/* -Pattern 'Users','@','sk-ant'` | nenhuma linha (sem caminho pessoal, e-mail ou chave); inspeção: sem o nome do titular da conta |
| D14 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D15 | (PowerShell) bloco D15 abaixo, depois da T7 | primeira linha `named=1` (o `ARCHITECTURE.md` foi varrido); depois, **nenhuma linha** |
| D15c | (controle do D15, na T1, antes do primeiro teste) `git grep` do bloco D15c abaixo | pelo menos uma linha para cada padrão, nos arquivos listados abaixo do bloco (prova que padrões e caminhos enxergam o texto antigo; o D15 só vale depois dele, AP-035) |
| D16 | `git grep -n -F "Provisional selector" -- src tests` | **uma** linha, em `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (prova do comando que a spec da US-007 usa no D7, AP-035) |
| D17 | `git status --short` após D2–D16 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |
| D18 | `dotnet list package --vulnerable --include-transitive` | nenhum pacote vulnerável (inclui `OllamaSharp` e `Microsoft.Extensions.AI.Abstractions`) |

D14 (fora da tabela por causa do `|`; forma única do `checklist-revisao` §4, AP-019):

```bash
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
```

D15 (PowerShell). Inclui o nome antigo do teste substituído, o texto do comentário da classe apagada e as variações dos comentários de adiamento (AP-035); o texto "The tutor is not connected yet." não entra aqui porque os testes o citam de propósito nas asserções `NotContain` (cenário "console conversa com o Ollama"); ele tem verificação própria só em `src/`:

```powershell
$patterns = 'TutorNotConnected', 'until US-006', 'Ollama keeps the', 'Ollama ainda não conectado', 'fixed on ClaudeCode until the BrainFactory', 'Reused by the Ollama "format" (US-006)', 'com o pacote do Ollama'
$files = @(Get-ChildItem -Recurse -File -Path src, tests -Include *.cs | Where-Object { $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*' }) + @(Get-Item ARCHITECTURE.md)
"scanned=$($files.Count) named=$(@($files | Where-Object Name -eq 'ARCHITECTURE.md').Count)"
$files | Select-String -SimpleMatch -Pattern $patterns
git grep -n -F "The tutor is not connected yet." -- src
```

D15c (controle; usa o `develop` de partida, `864b98d`, e por isso vale mesmo depois do commit de arquitetura):

```powershell
git grep -n -F -e 'TutorNotConnected' -e 'until US-006' -e 'Ollama keeps the' -e 'Ollama ainda não conectado' -e 'fixed on ClaudeCode until the BrainFactory' -e 'Reused by the Ollama "format" (US-006)' -e 'com o pacote do Ollama' -e 'The tutor is not connected yet.' 864b98d -- src tests ARCHITECTURE.md
```

Esperado no D15c: `TutorNotConnectedInputHandler.cs` (`TutorNotConnected`, `until US-006`, `The tutor is not connected yet.`), `ConsoleText.cs` (`TutorNotConnected`, `The tutor is not connected yet.`), `EnglishTutorApp.cs` (`TutorNotConnected`, `Ollama keeps the`, `until US-006`), `EnglishTutorAppTests.cs` (`ShowsTutorNotConnected`, `Ollama ainda não conectado`, `The tutor is not connected yet.`), `InfrastructureServiceCollectionExtensions.cs` (`fixed on ClaudeCode until the BrainFactory`), `TutorReplySchema.cs` (`Reused by the Ollama "format" (US-006)`) e `ARCHITECTURE.md` (`com o pacote do Ollama`). Os documentos históricos (`docs/us/*`, `tasks/US-00[1-5]/*`) não são varridos (§2.9).

Também entram, da DoD comum:

- todo cenário BDD com teste escrito antes do código;
- `review.md` sem bloqueantes;
- `docs/us/US-006.md` criado;
- ADR-009 aceito;
- `ARCHITECTURE.md` §3/§6.1/§6.4 e `SPRINT_1.md` (US-006 e US-007) atualizados antes da T1 (§2.9); `ARCHITECTURE.md` §7 com a VRAM medida (T8);
- merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

**T1 (2026-10-21)**

- D15c (antes do primeiro teste, em `864b98d`): achou linhas em `ARCHITECTURE.md` (1), `EnglishTutorApp.cs` (2), `ConsoleText.cs` (1), `TutorNotConnectedInputHandler.cs` (3), `TutorReplySchema.cs` (1), `InfrastructureServiceCollectionExtensions.cs` (1) e `EnglishTutorAppTests.cs` (4): cada padrão e arquivo esperado enxerga o texto antigo.
- Red (7c8ed13): 17 falhas em Infrastructure.Tests e 2 em Console.Tests, todas pela asserção (valores vazios do esqueleto, validador que aceita tudo, validador não registrado, seção não ligada). Os cinco testes de sucesso do validador nasceram verdes, como previsto.
- Mutações depois do Green (1acc4a9), cada uma revertida (`git checkout` do arquivo, tudo já commitado; `git status --short` limpo): `MinTimeoutSeconds` 2 → `Validate_WithTimeoutSecondsAtLimits_Succeeds(1)` falha; `MaxTimeoutSeconds` 3599 → caso 3600 falha; `MinTemperature` 0,01 → `Validate_WithTemperatureAtLimits_Succeeds(0)` falha; `MaxTemperature` 1,99 → caso 2 falha; `OutputMode` com caixa → `Validate_WhenOutputModeCaseDiffers_Succeeds` (2 casos) falha; `https` recusado → `Validate_WithHttpAndHttpsEndpoints_Succeeds(https)` falha; `Model` padrão vazio → `Constructor_Always_UsesDocumentedDefaults` e as demais falham.
- Pós-Green: Core 134, Infrastructure 127 (+26), Console 53 (+2), todos verdes.

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-21 — pedido do Revisor do plano (rodada 1, REPROVADO)

- **B1 (bloqueante):** a P9 define `<detalhe>` (primeira linha não vazia da mensagem da exceção, sem espaços nas pontas, ou `no error details were provided`), mas a §4 só testa mensagens de uma linha e não vazias (`boom t5`, `boom t7`). Mapear na §4 (e na T4, com Red e mutação) um caso por ramo da derivação: mensagem com várias linhas, mensagem começando por linha vazia, mensagem vazia ou só espaços. A mensagem exibida não pode embutir linhas extras de stack ou corpo HTTP, nem ficar com parênteses vazios. Atualizar contagens (§4, §6, D11) e a tabela de testes.

  → Incorporado em §2.5 (derivação de `<detalhe>` com um caso por ramo), §4 (nova linha do mapa, tabela de 5 casos nas asserções, contagem), §5 T4 (Red com o teste novo e Green com a derivação; mutações por ramo, com os casos que cada uma derruba), §9 P9 (definição e Teste), §10 D2 e D11. Contagens: `OllamaTutorBrainTests` 25 → 30 casos e 18 → 19 métodos; Infrastructure.Tests 162 → 167 (+66 sobre a base de 101); total 351 → 356; D11 64 → 69. A §6 (pacotes) não tem contagem de testes e não muda. Conferido: a soma da §4 (1 + 24 + 30 + 7 + 4 = 66) fecha com D2 e D11, e cada ramo da derivação (uma linha, várias linhas, linha vazia inicial, espaços e `\r` nas pontas, vazia, só espaços) tem caso próprio, com a mensagem inteira comparada, sem `()` vazio.

- **S1:** a linha `Branch:` do cabeçalho deve ser `feature/US-006_ollama-brain` (a branch real foi renomeada para esse nome; já confere, só verificar).

  → Conferido: o cabeçalho já diz `feature/US-006_ollama-brain`, igual à branch atual (`git status`); sem mudança.

- S2, S3 e S4 do `plan-review.md` (sugestões, fora do pedido deste comentário): **não incorporadas nesta rodada** para não alterar contagens além do B1; ficam para decisão do usuário.

### 2026-10-21 — aprovação do usuário

- Plano aprovado com todos os padrões propostos das perguntas P1 a P12 (nenhuma resposta alterada).
- Sugestões S2, S3 e S4 da revisão do plano: **não incorporadas**, por decisão do usuário.
- ADR-009 passou a "Aceito". `specs/sprints/SPRINT_1.md` (US-006 e US-007) e `ARCHITECTURE.md` (§3, §6.1, §6.4) foram atualizados conforme a §2.9, em commits próprios, antes da T1.

### 2026-10-21 — escolhas do Executor (T2, versões dos pacotes)

- `OllamaSharp` **5.5.0** (versão estável mais recente no nuget.org; o nuspec declara `Microsoft.Extensions.AI.Abstractions` >= 10.8.0 e o `OllamaApiClient` implementa `IChatClient`, condição de parada do ADR-009 afastada).
- `Microsoft.Extensions.AI.Abstractions` **10.11.0** (versão estável mais recente, compatível com a exigência de >= 10.8.0 do `OllamaSharp`). Os dois ficam em `Directory.Packages.props`, grupo `Infrastructure`, e só na `EnglishTutor.Infrastructure.csproj` (R1).

### 2026-10-21 — escolhas do Executor (T2, forma)

- Texto do log `OllamaLog.Replied` (EventId 2002, o plano fixa só as propriedades): `Brain {Provider} replied in {ElapsedMs} ms (model {Model}, output mode {OutputMode})`, no mesmo formato do evento 2001 do Claude Code.
- `ChatResponseFormat.ForJsonSchema(schema)` é chamado sem nome nem descrição de schema (o `format` do Ollama carrega só o JSON Schema).
- Conferência do fio com o `OllamaSharp` 5.5.0 (testes `OllamaTutorBrainHttpTests`, verdes): `POST /api/chat` no `Endpoint`; `model` e `messages` (`system`, `user`, `assistant`) como esperado; `ResponseFormat` com schema sai como `format` igual ao `TutorReplySchema` (comparação estrutural); `ChatOptions.Temperature` sai em `options.temperature`; sem `ResponseFormat` nenhum `format` no corpo. Nada diferiu do esperado; condição de parada da T2 não ocorreu.

### 2026-10-21 — conclusão da gravação (T3)

- Ollama 0.40.7 instalado com `winget install Ollama.Ollama`; `ollama --help` lista `serve` e `pull` (instruções da §2.5 conferidas); modelo `qwen2.5:14b-instruct-q4_K_M` baixado (9,0 GB). Condições de parada de instalação e de tamanho não ocorreram.
- Resposta com `format`: `message.content` é JSON puro (sem cerca nem texto em volta); a decisão P6 (parser continua rejeitando cerca) não precisou ser reaberta. O `format` com o schema foi aceito pelo Ollama.
- Modelo ausente: a biblioteca lança `System.Net.Http.HttpRequestException` com `StatusCode` = `NotFound` (404); o corpo é `{"error":"model 'nao-existe-us006' not found"}` (o texto `model`/`not found` está só no corpo, não na mensagem da exceção, que é `Response status code does not indicate success: 404 (Not Found).`). Logo a linha a da §2.3 casa pelo **status 404**, sem ajuste.
- Conexão recusada: `System.Net.Http.HttpRequestException` sem `StatusCode`, com `System.Net.Sockets.SocketException` interna (mensagem do sistema operacional, em português nesta máquina; por isso nenhuma mensagem ao aluno a embute). A linha b casa por "sem status".
- Tipos capturados pelo adaptador (T4): somente `HttpRequestException`; a biblioteca não usa tipo próprio para o 404.
- Primeira chamada do modelo na máquina: `total_duration` ~54,7 s (`load_duration` ~30,8 s) em `chat-structured.json`, próximo do padrão de 60 s (risco já listado na §7; o D5 roda com o modelo aquecido).
