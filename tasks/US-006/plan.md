# Plano — US-006 Professor local via Ollama

Status: Aguardando aprovação (revisão de escopo de 2026-10-22: acrescenta T10–T14 para o D5 vermelho; T1–T9 já commitadas)
Branch: feature/US-006_ollama-brain
Spec: specs/sprints/SPRINT_1.md#us-006
Regras: RN-BRAIN01–05 (da US), RN-BRAIN07 e RN-BRAIN09 (acrescentadas à US, ver §2.9 e P3), RN-BRAIN04 e RN-BRAIN08 (sem mudança), RN-CONV03, RN-FEED01 (alterada em 2026-10-22, §2.12); ARCHITECTURE §2 (R1–R4), §3, §6.1, §6.2, §6.4, §7, §9, §10; ADR-002, ADR-005, ADR-006, ADR-008, ADR-009 (Aceito, novo)

## 1. Objetivo

Ligar o professor local: com `Brain:Provider = Ollama`, o console conversa pelo `ConversationService` com um modelo servido pelo Ollama (`/api/chat`), com o mesmo contrato de resposta, o mesmo formato de tela e o mesmo tratamento de falha do Claude Code, incluindo as mensagens "Ollama is not running" e "ollama pull <modelo>".

Revisão de escopo (2026-10-22): o D5 (5 frases de fumaça, 5/5 no formato) ficou vermelho com o modelo 14B por regra de conteúdo (2 perguntas no `reply`). O objetivo acrescenta fechar o D5 com a decisão do usuário: validador do Core com a tolerância de 1 ou 2 perguntas, prompts reforçados (regra de exatamente uma pergunta mantida) e D5 medido com a retentativa única do ADR-006 (§2.12).

## 2. Abordagem

A decisão central está no **ADR-009 (Aceito)**: `docs/adr/ADR-009-provedor-ollama-ichatclient-pedido-falhas-e-tempo-maximo.md`. Em resumo:

- `OllamaTutorBrain` (Infrastructure) implementa `ITutorBrain` sobre `IChatClient` (`Microsoft.Extensions.AI`), com o `OllamaApiClient` do pacote `OllamaSharp` criado por `OllamaChatClients.Create(OllamaOptions, HttpMessageHandler?)`. A mesma fábrica serve ao DI e aos testes, que trocam só o `HttpMessageHandler` e por isso conferem o fio HTTP real (`/api/chat`, `messages`, `format`).
- O adaptador não guarda estado (RN-BRAIN01): recebe a janela completa e a repassa. Resposta inválida vira `BrainException.InvalidOutput` (ADR-006). `SessionHint` é ignorado.
- Falhas viram `BrainException` classificada pelo adaptador, com a mensagem ao aluno (ADR-008, mesmo padrão): "Ollama is not running", "ollama pull", tempo esgotado e "Ollama failed (...)". Nenhum valor novo em `BrainErrorKind` (P2).
- Tempo máximo por chamada igual ao do Claude Code: `Brain:Ollama:TimeoutSeconds` (P3).
- O console passa a usar o `ConversationInputHandler` para qualquer provedor; o `TutorNotConnectedInputHandler` e o texto "The tutor is not connected yet." saem do código (P8).
- Nada muda no Core **até a T9**: `BrainErrorKind`, `BrainException`, `BrainProviders` e `ConversationService` já atendiam. A revisão de 2026-10-22 muda só o `TutorReplyValidator` e o catálogo de rejeições (T10, §2.12).

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

**Depois da T11 (reforço dos prompts).** O `chat-request.json` foi gravado com os prompts anteriores ao reforço; como nenhum teste o lê, ele não é regravado. A T11 acrescenta ao `README.md` desta pasta uma nota: "gravado antes do reforço dos prompts da US-006 (T11); a mensagem `system` atual difere". O `chat-structured.json` é resposta gravada, não depende do prompt para ser lido pelo teste.

### 2.9 Alterações nos documentos-raiz e na spec (propostas; aplicadas pelo orquestrador antes da T1, em dois commits, AP-030)

O orquestrador aplica, depois da aprovação e **antes da T1**:

- `docs(ai): update spec for US-006`: os blocos de `specs/sprints/SPRINT_1.md` abaixo (US-006 e US-007);
- `docs(ai): update architecture for US-006`: `ARCHITECTURE.md` (§3, §6.1, §6.4) e o ADR-009 em "Aceito", se aprovado.

Nenhuma tarefa da §5 altera esses documentos, **exceto** a T8, que acrescenta a medição de VRAM à `ARCHITECTURE.md` §7 (pedida pela spec, D6). Cada trecho é identificado pelo **texto antigo** (AP-023).

**Estado (2026-10-22):** os dois commits de antes da T1 e o da T8 já foram feitos. A revisão de escopo tem o seu próprio par, descrito na §2.12.5: o commit de spec já existe (`15b4291`, `docs(ai): update spec for US-006`); falta o commit de arquitetura da revisão (`docs(ai): update architecture for US-006`), que o orquestrador aplica **antes da T10**.

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

Nenhuma regra `RN-*` muda **nesta parte original do plano** (a RN-FEED01 muda na revisão, §2.12). O efeito de "Ollama fora do ar" e "modelo ausente" serem `Unknown` na RN-BRAIN08 está no ADR-009 ("Consequências") e na P2; a spec da US-007 não muda além do D7 (o cenário "fallback por limite de uso" tem Claude Code como primário e Ollama como reserva, conferido em `SPRINT_1.md`, AP-036).

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

### 2.12 Revisão de escopo (2026-10-22): D5 vermelho, tolerância de 1 ou 2 perguntas, prompts reforçados e retentativa no D5

**Contexto.** O D5 ficou vermelho (§11/§12 do Executor): o adaptador sempre devolveu JSON que obedece ao schema; todas as rejeições foram de regra de conteúdo ("has 2 questions"); frases fora do formato por execução: 2, 0, 1, 0 e 1 de 5. O usuário escolheu (§12, 2026-10-22) as opções **c + a + tolerância**. A spec já foi atualizada em `15b4291` (`RN-FEED01` e a convenção "Tolerância de verificação" em `specs/BUSINESS_RULES.md`; cenários de 2 e 3 perguntas na US-004; D5 da US-006 com a retentativa contada). O que segue é o desenho do código e dos testes. Nenhum ADR novo (§2.12.5, item 1).

**Conferência do código e dos testes existentes (pedido 4 do usuário; Grep por `exactly one question`, `has 2 questions`, `ReplySentences`, `IsQuestion` e `ReplyMustAskExactlyOneQuestion` em `src/`, `tests/`, `prompts/`; na rodada 3 refeita também pelo **nome antigo do cenário** `reply com duas perguntas é inválido`, que aparece em 5 arquivos de teste, não só nos que mudam de comportamento):**

| Onde a regra de "exatamente 1" vive ou é testada hoje | Efeito da tolerância | Tarefa |
|---|---|---|
| `src/EnglishTutor.Core/Brain/TutorReplyValidator.cs`: `questionCount != 1` e o comentário `// RN-FEED01: exactly one question, in any position; ...` | Passa a `questionCount < 1 \|\| questionCount > 2`; comentário novo | T10 |
| `src/EnglishTutor.Core/Brain/TutorReplyRejections.cs`: `ReplyMustAskExactlyOneQuestion(reply, count)`, texto `Expected 1 to 3 short sentences with exactly one question ...` | Mensagem ganha a frase da tolerância (§2.12.1); `Reason` e nome da fábrica não mudam | T10 |
| `TutorReplyValidatorTests`: `Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason` (4 entradas de 2 perguntas, `has 2 questions`) | Vira `Validate_WhenReplyAsksTwoQuestions_ReturnsValidReplyWithoutWarnings` (mesmas 4 entradas, agora válidas); novo teste de 3 ou mais, que confere a frase da tolerância na `Message` e por isso **nasce vermelho** no Red (a `Message` atual não a tem) | T10 |
| `TutorReplyValidatorTests`: `Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason`, `Validate_WhenReplyAsksOneQuestionInAnyPosition_ReturnsValidReply`, `Validate_WhenReplyEndsWithQuestion_...`, `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_...` | Sem mudança (lados 0 e 1 do limite) | — |
| `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` (casos 0, 2 e 3) | O caso 2 vira 4 (a fábrica nunca mais recebe 2 do validador); todos conferem a frase da tolerância | T10 |
| `TutorReplyParserTests.Parse_WhenJsonReplyAsksTwoQuestions_RejectsWithExactlyOneQuestionReason` | Vira `Parse_WhenJsonReplyAsksTwoQuestions_ReturnsReply`; novo `Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason` | T10 |
| `TutorReplyParserTests.Parse_WhenJsonReplyHasNoQuestion_...` e `Parse_WhenTagsReplyHasNoQuestion_...` | Sem mudança (lado 0) | — |
| `tests/EnglishTutor.Core.Tests/Brain/ReplySentencesTests.cs:28`, comentário do teste `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` (`// US-004 — Cenário: reply com duas perguntas é inválido (apoio, P21: o que é pergunta) — RN-FEED01`; achado do Revisor, rodada 3, B2) | **Só o comentário muda**, o corpo e os 7 casos não (o teste é de `IsQuestion`, que a tolerância não altera): o cenário antigo não existe mais na spec, então o comentário passa a citar o cenário que o teste apoia, `// US-004 — Cenário: reply com duas perguntas é aceito pela tolerância (apoio, P21: o que é pergunta) — RN-FEED01` | T10 |
| `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion` (`Count(IsQuestion).Should().Be(1)`) e `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` (idem) | Com a tolerância, uma resposta de 2 perguntas passa pelo adaptador e a asserção `Be(1)` ficaria vermelha por regra que o validador deixou de impor: passam a `BeInRange(1, 2)`; o primeiro é renomeado para `...WithOneOrTwoQuestions` | T12 |
| `ClaudeCodeTutorBrainTests` (linhas com `Count(ReplySentences.IsQuestion).Should().Be(1)`), `OllamaTutorBrainHttpTests.SendAsync_WithRecordedStructuredResponse_...`, `FixtureRecorderLiveTests`, `OllamaFixtureRecorderLiveTests` | **Sem mudança** (decisão P17): conferem **fixtures gravadas** (determinísticas) ou gravam fixtures; a fixture documenta o caso pedido (1 pergunta), não o tolerado | — |
| Console.Tests e Core.Tests de `ConversationService` | Nenhum teste usa resposta de 2 perguntas contra o validador real (Grep: `has 2 questions` só em Core e Infrastructure); sem mudança | — |
| `prompts/tutor.md`, `format.json.md`, `format.tags.md` | A regra "exatamente uma pergunta" **fica no texto** (a tolerância é só do verificador); reforço na T11 | T11 |

Inventário do Grep do D20 sobre `139adee` (rodada 3, B2), arquivo por arquivo, todos cobertos pela tabela acima: `TutorReplyValidatorTests.cs` (linhas 68, 74, 80), `TutorReplyRejectionsTests.cs` (58, 61), `TutorReplyParserTests.cs` (122, 124, 132), `ReplySentencesTests.cs` (28), `TutorReplyValidator.cs` (19) e `ClaudeCodeTutorBrainLiveTests.cs` (44). Nenhum outro arquivo de `src` ou `tests` casa. O inventário vale como conferência: o D20c reroda o mesmo Grep antes da T10 e o resultado tem de bater com esta lista.

**2.12.1 Validador do Core (`TutorReplyValidator`) e mensagem (`TutorReplyRejections`).**

```csharp
private const int _minQuestions = 1;   // RN-FEED01 tolerance: asked 1, verified 1..2
private const int _maxQuestions = 2;
// if (questionCount < _minQuestions || questionCount > _maxQuestions) return Invalid(ReplyMustAskExactlyOneQuestion(reply, questionCount));
```

- 0 e 3 ou mais perguntas continuam inválidas, com o mesmo `Reason` (`reply must ask the student exactly one question`, texto da spec do cenário "reply com três perguntas é inválido"). Duas perguntas: válido, **sem aviso** (P14); o `Reply` volta aparado como hoje.
- `ReplyMustAskExactlyOneQuestion(string reply, int questionCount)` mantém assinatura e nome (renomear arrastaria 35 ocorrências de `tasks/US-004` e do catálogo sem ganho). A `Message` passa a ser (P13): `reply must ask the student exactly one question: the reply "<quoted>" <has no question | has N questions>. Expected 1 to 3 short sentences with exactly one question to the student (one sentence ending with "?", in any position); the other sentences are comments or encouragement. Replies with 1 or 2 questions are accepted.` O trecho `Expected 1 to 3 short sentences with exactly one question` continua o mesmo (teste existente); a frase final é nova e só o projeto a produz (AP-015). **Como a frase é nova, todo teste que a confere nasce vermelho no Red da T10** (rodada 3, B1): os 4 casos de 3 ou mais perguntas do validador e os 3 casos de `TutorReplyRejectionsTests`; o teste de 3 perguntas do parser **não** a confere (só `has 3 questions`) e nasce verde.
- A mensagem é também a instrução de correção da retentativa (ADR-006, `TurnMessageBuilder.BuildFormatCorrection`). Como a retentativa só dispara com 0 ou 3 ou mais, dizer que 1 ou 2 são aceitas é inofensivo e é o que o usuário pediu; o risco de o modelo ficar com "2" na retentativa é o efeito pretendido da tolerância.

**2.12.2 Prompts reforçados (T11).** Arquivos: `prompts/format.json.md` e `prompts/format.tags.md` (o texto de formato vai por último na mensagem `system`, onde o 14B dá mais peso). `prompts/tutor.md` só muda se o D5 continuar vermelho depois da T11 (P15). Conteúdo mínimo do reforço, **mantendo a regra de exatamente uma pergunta** nos dois arquivos: (i) uma checagem final em uma linha, "Before answering, count the "?" in the reply: there must be exactly one"; (ii) um par de exemplos curto, um bom e um ruim (o ruim com duas perguntas, marcado como proibido: "Never write a second question, not even a short follow-up like "And you?""); (iii) nenhuma frase que diga que duas perguntas são aceitas (a tolerância não vai para o prompt). Os textos exatos são do Executor; o teste de guarda (§4, `PromptFilesContentTests`) impede que a regra saia ou que a tolerância entre. A prova do efeito do reforço é o D5 (sem teste de comportamento do modelo na suíte rápida).

**2.12.3 D5 com a retentativa do ADR-006 (T13).** Ponto de montagem: um teste `LiveBrain` novo, `OllamaConversationLiveTests.SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry`, que monta o `ConversationService` **real** (Core) sobre o `OllamaTutorBrain` real (`LiveOllama`), com `FileTutorPromptSource` sobre a pasta `prompts` copiada para a saída (o mesmo `tutor.md` do app), `ConversationOptions` padrão e `NullLogger`. Um `CountingTutorBrain` (decorator de `ITutorBrain`, em `Support/`) fica entre os dois e conta chamadas, rejeições `InvalidOutput` e motivos, só para registro. Cada uma das 5 frases usa um `ConversationService` novo (janela vazia: amostras independentes, como o teste antigo). A saída do teste é `ConversationTurnResult.Reply`; "fora do formato" = o serviço caiu no `TutorReply.RawFallback` (as duas tentativas inválidas, `IsRawFallback`). O teste antigo de adaptador puro, `OllamaTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_AllInFormat`, **sai** (a camada do D5 mudou; a medição "primeira tentativa inválida" passa para o contador do decorator). `LiveOllama` ganha a propriedade `PromptFiles`. Cada frase tem um limite de 2 chamadas (RN-BRAIN04); o tempo de uma frase pode chegar a 2 × `TimeoutSeconds`.

**2.12.4 Contagem do `ConversationService` não muda.** Nenhum código do `ConversationService` muda: a retentativa já existe (US-003). O D5 apenas a passa a contar.

**2.12.5 Documentos (pedido 3 do usuário).**

1. **ADR novo: não.** A convenção "Tolerância de verificação" já vive em `specs/BUSINESS_RULES.md` e em `.claude/skills/padroes-codigo/SKILL.md` (linha `- Validação restrita de frase do modelo (ex.: exatamente 1 `?`): ...`); a mudança é uma revisão do ADR-005 (já tem o campo "Revisado:"), não uma decisão nova. A camada do D5 está na spec (D5 da US-006) e aqui (§2.12.3).
2. **`ARCHITECTURE.md` muda (1 trecho)**, §6.2 ("Contrato de saída", a linha que descreve o `TutorReplyValidator`). Texto antigo:
   `(reply não vazio e com exatamente uma pergunta, isto é, uma única frase terminada em `?`, em qualquer posição, com as frases separadas por `ReplySentences`;`
   Texto novo:
   `(reply não vazio e com 1 ou 2 perguntas, isto é, uma ou duas frases terminadas em `?`, em qualquer posição, com as frases separadas por `ReplySentences` (tolerância de verificação da RN-FEED01: o prompt continua pedindo exatamente uma; 0 e 3 ou mais são rejeitadas);`
   Repetições dessa regra (Grep por `uma pergunta|one question|exatamente 1|exactly one|1 ou 2|Tolerância` em `ARCHITECTURE.md`, `CLAUDE.md`, `README.md`, `.claude/**/*.md`, `docs/adr/*.md`, `specs/PRODUCT.md`, `specs/ROADMAP.md`):
   - `CLAUDE.md` e `README.md`: não repetem a regra. **Sem alteração.**
   - `.claude/skills/padroes-codigo/SKILL.md`: já traz a convenção nova (a linha acima). **Sem alteração.** Nenhuma outra skill repete a regra.
   - `specs/PRODUCT.md` (OA1 "sempre fazem exatamente uma pergunta" e OA3): descrevem a regra **pedida**, que não muda. **Sem alteração.**
   - `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md`: texto antigo `- **Revisado:** 2026-10-19 (RN-FEED01 passou a "exatamente uma pergunta"; US-004)`; texto novo, a linha antiga seguida de `- **Revisado:** 2026-10-22 (RN-FEED01: o validador aceita 1 ou 2 perguntas; o prompt continua pedindo exatamente uma; US-006)`. Texto antigo: `reply com exatamente uma pergunta (uma única frase terminada em `?`, em qualquer posição;`; texto novo: `reply com 1 ou 2 perguntas (uma ou duas frases terminadas em `?`, em qualquer posição; o prompt pede exatamente uma, tolerância de verificação da RN-FEED01;`. A linha da seção "o que precisa ser medido" (`se o `eval` (US-009) mostrar muitas rejeições por "reply must ask the student exactly one question", rever a regra ou o separador de frases`) continua correta (o motivo ainda existe para 0 e 3 ou mais). **Sem alteração.**
   - `docs/adr/ADR-002-contrato-saida-estruturada.md`, item 5, texto antigo: `(reply com exatamente uma pergunta, RN-FEED01;`; texto novo: `(reply com exatamente uma pergunta pedida, 1 ou 2 verificadas, RN-FEED01;`. O exemplo JSON do ADR (`"reply": "string (1–3 frases, com exatamente uma pergunta, em qualquer posição)"`) é o contrato **pedido** ao modelo: **sem alteração**.
3. **Commits do orquestrador antes da T10 (AP-030):** o de spec já existe (`15b4291`); falta `docs(ai): update architecture for US-006` com os três trechos acima (ARCHITECTURE, ADR-005, ADR-002), um commit só.
4. **Adiamentos (AP-022, AP-040).** Nada fica para outra US. Efeito da tolerância em consumidores futuros: o `eval` da US-009 (conta rejeições; respostas de 2 perguntas deixam de contar como rejeição, e a spec da US-009 não cita contagem de perguntas, conferido por Grep em `SPRINT_1.md`: nenhuma ocorrência de `pergunta` na US-009); a US-008 (suíte de contrato testa `TutorReply`, não a contagem). Nenhum registro novo em spec.

### Alternativas descartadas

- `HttpClient` direto (ADR-009): contraria a ARCHITECTURE §6.4 e perde a base do futuro `AnthropicApi`.
- Valores novos em `BrainErrorKind` para "fora do ar" e "modelo ausente" (ADR-009; P2): mudaria RN-BRAIN07/08, §4 e o Core por um provedor.
- Testar só com um `IChatClient` falso: não provaria o `/api/chat` nem o `format` do cenário; por isso os testes de fio com o `OllamaApiClient` real e `HttpMessageHandler` falso.
- Gravar as fixtures com `curl`: o corpo da requisição seria diferente do que o adaptador envia (AP-048); o recorder usa o próprio adaptador.
- `BrainFactory` já nesta US: escopo da US-007.
- Sem tempo máximo no Ollama (P3): contraria a RN-BRAIN09.
- Revisão 2026-10-22: manter o D5 só no adaptador com tolerância de 4/5 (opção b): descartada pelo usuário; a tolerância é do verificador de conteúdo (RN-FEED01), não do número de frases aprovadas. Trocar de modelo (opção d): descartada pelo usuário. Aviso (`Warning`) para a resposta de 2 perguntas aceita (P14): descartado por padrão, para o log não repetir uma regra que a tolerância declarou aceitável.
- Rodada 3, B1: tirar a frase da tolerância das asserções do teste de 3 ou mais perguntas do validador (deixá-la só no `TutorReplyRejectionsTests`): descartado; manter a frase nas duas camadas prova que o validador entrega a mensagem do catálogo, e o custo é só declarar esses 4 casos entre os que falham pela asserção no Red.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-009-provedor-ollama-ichatclient-pedido-falhas-e-tempo-maximo.md` | Decisão (Aceito), criada junto com este plano |
| Alterar | `ARCHITECTURE.md` | §3, §6.1, §6.4 (§2.9) pelo orquestrador antes da T1; §7 na T8; linha do `TutorReplyValidator` (§2.12.5) pelo orquestrador antes da T10 |
| Alterar | `specs/sprints/SPRINT_1.md` | US-006 (Regras, cenário "tempo esgotado", D7) e US-007 (D7) (§2.9), pelo orquestrador antes da T1; US-004 (cenários de 2 e 3 perguntas) e D5 da US-006 já em `15b4291` |
| Alterar | `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md` | Revisão 2026-10-22 (§2.12.5), pelo orquestrador antes da T10 |
| Alterar | `docs/adr/ADR-002-contrato-saida-estruturada.md` | Item 5 (§2.12.5), pelo orquestrador antes da T10 |
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
| Alterar | `src/EnglishTutor.Core/Brain/TutorReplyValidator.cs` | Aceita 1 ou 2 perguntas; comentário (§2.12.1, T10) |
| Alterar | `src/EnglishTutor.Core/Brain/TutorReplyRejections.cs` | Mensagem da rejeição por perguntas diz que 1 ou 2 são aceitas (§2.12.1, T10) |
| Alterar | `prompts/format.json.md` | Reforço da regra de uma pergunta, sem citar tolerância (§2.12.2, T11) |
| Alterar | `prompts/format.tags.md` | Idem (§2.12.2, T11) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaOptionsTests.cs` | Padrões (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaOptionsValidatorTests.cs` | Validação (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaTutorBrainTests.cs` | Adaptador com `IChatClient` falso (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaTutorBrainHttpTests.cs` | Adaptador com `OllamaApiClient` real e `HttpMessageHandler` falso (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaTutorBrainLiveTests.cs` | `LiveBrain` (porta fechada, modelo ausente, `Assistant` vazio); a fumaça de 5 frases sai na T13 (§4) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaFixtureRecorderLiveTests.cs` | Gravação das fixtures (`LiveBrain`, §2.8) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeChatClient.cs` | `IChatClient` com fila, `Throws` e `HangsUntilCancelled(onCancelled)` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fakes/FakeHttpMessageHandler.cs` | Registra requisições (método, URI, corpo) e responde de uma fila (resposta ou exceção) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/RecordingHttpHandler.cs` | `DelegatingHandler` de gravação (recorder) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LiveOllama.cs` | Adaptador real para os testes `LiveBrain` (`ENGLISHTUTOR_OLLAMA_ENDPOINT`, `ENGLISHTUTOR_OLLAMA_MODEL`); T13 expõe `PromptFiles` |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/Ollama/*` | 4 arquivos gravados + `README.md` (§2.8); T11 acrescenta a nota ao `README.md` |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs` | Seletor e validador do Ollama (§4) |
| Criar | `tests/EnglishTutor.Console.Tests/Support/ScriptedChatClient.cs` | `IChatClient` falso do console (resposta ou exceção; grava as chamadas) |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs` | Cenário do console, falhas, configuração inválida; teste antigo sai (§2.10, §4) |
| Alterar | `tests/EnglishTutor.Core.Tests/Brain/TutorReplyValidatorTests.cs` | Duas perguntas válidas; três ou mais inválidas (§4, T10) |
| Alterar | `tests/EnglishTutor.Core.Tests/Brain/TutorReplyRejectionsTests.cs` | Caso 2 vira 4; frase da tolerância (§4, T10) |
| Alterar | `tests/EnglishTutor.Core.Tests/Brain/ReplySentencesTests.cs` | Só o comentário do teste `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` (linha 28): cita o cenário `reply com duas perguntas é aceito pela tolerância` no lugar do cenário antigo; corpo e casos intactos (§2.12, T10) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Brain/TutorReplyParserTests.cs` | Duas perguntas aceitas; três rejeitadas (§4, T10) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/PromptFilesContentTests.cs` | Guarda do texto dos prompts: regra mantida, tolerância ausente (§4, T11) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainLiveTests.cs` | `BeInRange(1, 2)` em duas asserções; um método renomeado (§2.12, T12) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaConversationLiveTests.cs` | D5 com a retentativa (§2.12.3, T13) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/CountingTutorBrain.cs` | Decorator de `ITutorBrain` que conta chamadas e rejeições, só para registro (T13) |

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
| saída estruturada (apoio: 5 frases de fumaça, D5, **com a retentativa do ADR-006**; substitui `OllamaTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_AllInFormat`, que sai na T13) | Infrastructure.Tests | `OllamaConversationLiveTests` | `SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry` | `LiveBrain` |
| reply com duas perguntas é aceito pela tolerância (US-004, RN-FEED01; no Core, bordas) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyAsksTwoQuestions_ReturnsValidReplyWithoutWarnings` | Unidade, `[Theory]` 4 casos (substitui `Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason`) |
| reply com duas perguntas é aceito pela tolerância (no parser) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyAsksTwoQuestions_ReturnsReply` | Unidade (substitui `Parse_WhenJsonReplyAsksTwoQuestions_RejectsWithExactlyOneQuestionReason`) |
| reply com duas perguntas é aceito pela tolerância (apoio, P21: o que é pergunta; **só o comentário muda**, corpo e 7 casos intactos; rodada 3, B2) | Core.Tests | `ReplySentencesTests` | `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` (existente) | Unidade, `[Theory]` 7 casos (sem mudança de contagem; nasce e continua verde) |
| reply com três perguntas é inválido (US-004, RN-FEED01; no Core, bordas) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyAsksMoreThanTwoQuestions_RejectsWithExactlyOneQuestionReason` | Unidade, `[Theory]` 4 casos (3, 3 com aspas, 3 com quebras de linha, 4); **nascem vermelhos** no Red: a `Message` atual não tem a frase da tolerância (B1) |
| reply com três perguntas é inválido (no parser) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason` | Unidade (nasce verde: confere só `has 3 questions`, que a regra atual já produz; mutação depois do Green) |
| reply com três perguntas é inválido (apoio, AP-016: contagem e frase da tolerância na mensagem) | Core.Tests | `TutorReplyRejectionsTests` | `ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` (alterado) | Unidade, `[Theory]` 3 casos (0, 3, 4); nascem vermelhos (falta a frase da tolerância) |
| reply sem pergunta (lado 0 do limite; já existente, sem mudança) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyHasNoQuestion_RejectsWithExactlyOneQuestionReason` | Unidade, `[Theory]` 3 casos |
| frase com erro gramatical (apoio, RN-FEED01: a regra pedida continua nos prompts) | Infrastructure.Tests | `PromptFilesContentTests` | `Prompt_Always_KeepsExactlyOneQuestionRuleAndNoToleranceWording` | Unidade, `[Theory]` 3 casos (`tutor.md`, `format.json.md`, `format.tags.md`); nasce verde (mutação) |
| frase com erro gramatical (US-004; `LiveBrain` do Claude Code com a tolerância) | Infrastructure.Tests | `ClaudeCodeTutorBrainLiveTests` | `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithOneOrTwoQuestions` (renomeado) e `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply` (asserção alterada) | `LiveBrain` |
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
  - `SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections`: o handler devolve `Fixtures/Ollama/chat-structured.json`; o `TutorReply` tem `Reply` não vazio com exatamente uma pergunta (`ReplySentences`) e ao menos uma correção; `RawText` é JSON puro (começa por `{`, P6). Sem mudança com a tolerância (P17: fixture gravada determinística, documenta o caso pedido).
  - `SendAsync_WithRecordedModelNotFoundResponse_ThrowsPullCommandForConfiguredModel`: o handler devolve o status e o corpo de `model-not-found.*`; `Kind == Unknown`; a mensagem contém `ollama pull <modelo configurado>`; a `InnerException` é a exceção que a biblioteca produziu (tipo registrado na §12).
  - `SendAsync_WhenConnectionIsRefused_ThrowsNotRunningWithEndpoint`: o handler lança `HttpRequestException(ConnectionError, ..., new SocketException())`; mensagem com `Ollama is not running at "<Endpoint>"`.
  - `SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage`: o handler responde 500 com corpo `{"error":"boom t7"}`; `Kind == Unknown`; a mensagem começa com `Ollama failed (` e termina com `Check that the Ollama server is healthy and that Brain:Ollama:Endpoint points to it.`; não contém `Ollama is not running` nem `ollama pull`.
- `OllamaTutorBrainLiveTests` (Ollama no ar, modelo padrão baixado, `ENGLISHTUTOR_OLLAMA_ENDPOINT`/`ENGLISHTUTOR_OLLAMA_MODEL` opcionais; camada: o adaptador, **sem** a retentativa do ADR-006; depois da T13 tem 3 testes, sem a fumaça de 5 frases):
  - `SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand`: `Model = nao-existe-us006`; `Kind == Unknown`; mensagem com `ollama pull nao-existe-us006`.
  - `SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning`: o teste abre um `TcpListener` na porta 0, lê a porta e o fecha; `Endpoint = http://127.0.0.1:<porta>`; mensagem com `Ollama is not running at`.
  - `SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply`: histórico `student` (User), `` (Assistant), `Please correct your format.` (User); o Ollama responde e a resposta é lida como `TutorReply` válido ou como `InvalidOutput` de conteúdo, mas **não** falha por mensagem vazia (nenhuma `BrainException` de `Unknown`).
- `OllamaConversationLiveTests.SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry` (T13; camada: `ConversationService` real com a retentativa única do ADR-006 sobre o `OllamaTutorBrain` real; Ollama no ar, modelo padrão): para cada uma das 5 frases de `LiveClaudeCode.KnownErrors`, um `ConversationService` novo; `SendAsync(frase)`; a resposta `Reply` tem `IsRawFallback == false` (5/5 no formato; as frases que caíram no `RawFallback`, com o texto cru e o motivo da segunda rejeição, entram na mensagem de falha). O `CountingTutorBrain` registra, **sem critério**, em `recorded-fixtures/ollama-smoke.txt`: chamadas ao adaptador, quantas frases precisaram da retentativa (primeira tentativa inválida), motivos das rejeições e o número de respostas com a correção esperada (`x/5`). Registro de formato idêntico ao do teste antigo, acrescido de `first attempt invalid: n/5; retries used: n`.
- `Validate_WhenReplyAsksTwoQuestions_ReturnsValidReplyWithoutWarnings` (4 casos, as 4 entradas que eram de rejeição): `Was it busy? How long was the trip?`; `Really?! What happened next?`; `Your boss said "Really?" Then what did you say?`; `Was it busy?\nHow long was the trip?` (com `\n` real). `IsValid` verdadeiro; `Reply.Reply` igual à entrada; `Warnings` vazio (P14).
- `Validate_WhenReplyAsksMoreThanTwoQuestions_RejectsWithExactlyOneQuestionReason` (`[Theory]` com a contagem esperada: `has 3 questions` ou `has 4 questions`): `Was it busy? How long was the trip? Did you sit?` (3); `Your boss said "Really?" Then what did you say? And why?` (3, com o `?` entre aspas contando uma frase); `Was it busy?\nHow long was the trip?\nDid you sit?` (3, quebras de linha); `Why? When? Where? Who?` (4). `IsValid` falso; `Rejection.Reason == "reply must ask the student exactly one question"`; `Message` contém `has <n> questions` e `Replies with 1 or 2 questions are accepted`. **Red (B1):** a `Message` atual não tem a frase final, então os 4 casos falham pela asserção `Contain("Replies with 1 or 2 questions are accepted")` antes do Green, e passam depois dele (o `IsValid` falso e a `Reason` já passam hoje, a falha é só pela frase). Os dois lados do limite superior: 2 (teste acima, válido) e 3 (este, inválido); o lado inferior: 0 (existente) e 1 (existente) (AP-016).
- `Parse_WhenJsonReplyAsksTwoQuestions_ReturnsReply`: o mesmo JSON de hoje (`Was it busy? How long was the trip?`, `praise` `Nice sentence.`); `IsValid` verdadeiro; `Reply.Reply == "Was it busy? How long was the trip?"`. `Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason`: JSON com `reply` `Was it busy? How long was the trip? Did you sit?`; `IsValid` falso; `Reason` igual; `Message` contém `has 3 questions` (só isso: a frase da tolerância fica a cargo dos testes do validador e das rejeições, por isso este teste nasce verde).
- `ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` (casos: `I like that.` 0 → `the reply "I like that." has no question`, sem `has 0`; `Why? When? Where?` 3 → `has 3 questions`; `Why? When? Where? Who?` 4 → `has 4 questions`; todos sem o texto do outro caso): em todos, a mensagem contém `Expected 1 to 3 short sentences with exactly one question` e `Replies with 1 or 2 questions are accepted`.
- `PromptFilesContentTests.Prompt_Always_KeepsExactlyOneQuestionRuleAndNoToleranceWording` (`[Theory]` com `tutor.md`, `format.json.md`, `format.tags.md`, lidos da pasta `prompts` da saída do teste): o texto contém `exactly one question` e `second question` (sem diferenciar maiúsculas: `tutor.md` traz `exactly ONE question`) e **não** contém `1 or 2`, `one or two` nem `two questions are` (a tolerância não vai para o prompt).
- `ClaudeCodeTutorBrainLiveTests` (T12): nos dois métodos, `ReplySentences.Split(reply.Reply).Count(ReplySentences.IsQuestion)` fica `BeInRange(1, 2)` (verificador igual ao do Core).
- `RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback`: `Brain:Provider = Ollama`; o `ScriptedChatClient` devolve o JSON equivalente ao de `ConsoleTutorReplies` (`Reply t1?`, correção `orig t1` → `fixed t1` (`why t1`)); entrada `sentence t1`, `/quit`. Então: o cliente falso recebeu 1 chamada; a mensagem `System` contém o texto do `FixedTutorPromptSource` e `Output format` (o `prompts/format.json.md` real); a última mensagem é `User` e contém `sentence t1` e `Mode: free-conversation`; a saída tem uma linha que termina com `Tutor: Reply t1?`, a linha `Feedback:` e `  - orig t1 → fixed t1 (why t1)` (mesmo formato do Claude Code); a saída **não** contém `The tutor is not connected yet.`; o erro está vazio; código 0.
- `RunAsync_WhenOllamaFails_ShowsInstructionOnErrorAndContinuesSession` (casos: `HttpRequestException(ConnectionError, ...)` → `Ollama is not running at`; `HttpRequestException` 404 → `ollama pull`): entrada `first sentence t1`, `second sentence t2`, `/quit`; o cliente lança na 1ª chamada e depois responde JSON válido (`Reply t2?`). Então: `Error` contém `The tutor could not answer: ` + o trecho do caso e `You can send a sentence again or type /quit to exit.`; não contém `An unexpected error occurred`; a saída tem `Tutor: Reply t2?`; o cliente recebeu 2 chamadas e a 2ª só tem a mensagem do aluno do 2º turno (`second sentence t2`; o turno que falhou não entrou na janela); código 0.
- `RunAsync_WhenOllamaTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt`: `Brain:Provider = Ollama` e `Brain:Ollama:TimeoutSeconds = 0`; código `ExitCodes.InvalidConfiguration`; `Error` contém `Invalid configuration:` e `Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found 0)`; `Output` vazio.
- `RunAsync_WhenSeveralSectionsAreInvalid_...` (novo `[InlineData]`: provedor `ClaudeCode`, chave `Brain:Ollama:TimeoutSeconds` = `0`, esperado `Brain:Ollama:TimeoutSeconds must be between 1 and 3600 (found 0)`): as duas seções inválidas (`Conversation` e `Brain:Ollama`) aparecem na saída de erro; código `InvalidConfiguration`; nada na saída padrão. É também a prova do limite aceito da P12 (Ollama inválido com o Claude Code como provedor).
- `AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` (`Ollama`, `ollama`): com `AddOptions<BrainOptions>().Configure(o => o.Provider = <caso>)`, `GetService<ITutorBrain>()` é `OllamaTutorBrain` e `Name` é `Ollama`. `..._WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain`: `Provider = ClaudeCode` → `ClaudeCodeTutorBrain`.
- `AddEnglishTutorInfrastructure_Always_RegistersOllamaOptionsValidator`: `Configure(o => o.Model = " ")`; ler `IOptions<OllamaOptions>` lança `OptionsValidationException` com uma falha que contém `Brain:Ollama:Model is required`.
- Testes do validador: cada um confere a mensagem exata da §2.1 com o valor do caso (uma falha por chave); `Validate_WhenSeveralKeysAreInvalid_ReportsOneFailurePerKey` confere 2 falhas distintas, cada uma com a própria chave.
- `Constructor_Always_UsesDocumentedDefaults`: `Endpoint == "http://localhost:11434"`, `Model == "qwen2.5:14b-instruct-q4_K_M"`, `Temperature == 0.6`, `OutputMode == "JsonSchema"`, `TimeoutSeconds == 60`.

**Contagem.** Fora de `LiveBrain`: Core.Tests 134 → **138** (+4: o teste novo de 3 ou mais perguntas tem 4 casos; o de 2 perguntas substitui o de rejeição com os mesmos 4 casos; `TutorReplyRejectionsTests` continua com 3 casos; `ReplySentencesTests` não muda de contagem, só o comentário); Console.Tests 51 → **55** (−1 removido; +1 conversa, +2 falhas, +1 `TimeoutSeconds` inválido, +1 caso novo na `[Theory]` de várias seções); Infrastructure.Tests 101 → **171** (+70: `OllamaOptionsTests` 1; `OllamaOptionsValidatorTests` 24 casos / 12 métodos; `OllamaTutorBrainTests` 30 casos / 19 métodos; `OllamaTutorBrainHttpTests` 7 casos / 7 métodos; `InfrastructureServiceCollectionExtensionsTests` +4 casos / +3 métodos; `TutorReplyParserTests` +1 caso (o de 2 perguntas substitui o de rejeição, o de 3 é novo); `PromptFilesContentTests` +3 casos / 1 método). Total **364** (era 356 até a T9). `LiveBrain` do Ollama por rodada do D5: 5 testes antes e depois (`OllamaTutorBrainLiveTests` 4 → 3; `OllamaConversationLiveTests` 0 → 1; `OllamaFixtureRecorderLiveTests` 1). Conferido contra a tabela acima: Core 3+4 (validador: 4 + 4, rejeições 3 já existentes), parser 2 testes (1 alterado + 1 novo), prompts 3 casos. A mudança da rodada 3 (B1: Red declarado de outro jeito; B2: um comentário em `ReplySentencesTests`) não altera nenhuma contagem.

## 5. Tarefas (ordem de execução TDD)

**Pré-condição** (antes da T1): o orquestrador aplica a §2.9 em dois commits, `docs(ai): update spec for US-006` e `docs(ai): update architecture for US-006`, com o ADR-009 em "Aceito", se aprovado. Na T1, antes do primeiro teste, o Executor roda o **D15c** e cola o resultado nas Evidências. **Pré-condição da T10:** o orquestrador aplica o commit `docs(ai): update architecture for US-006` da revisão (§2.12.5; o commit de spec da revisão é o `15b4291`).

**Mutações depois do Green.** Quando um teste nasce verde porque o esqueleto do Red ainda não tem a regra (validador sem as faixas, `IsServerFailure` devolvendo `false`), a mutação não tem o que quebrar no Red. Nesses casos a tarefa diz "mutação depois do Green": o Executor faz o commit `feat`, aplica a mutação pelo efeito descrito, roda o teste e vê falhar, desfaz pela edição inversa (sem commit), confere que `git status --short` não mostra mudança em `src/` e cola o resultado nas Evidências (AP-017, AP-029, AP-037, AP-045). Mutação sobre teste de fixture gravada é escolhida depois da gravação, para o ramo que a fixture percorre.

- [x] T1 — `Brain:Ollama`: opções, validação e configuração. (feito: test 7c8ed13, feat 1acc4a9; D15c e mutações nas Evidências)
  Red: esqueleto `OllamaOptions` com as propriedades **sem** inicializador e as constantes; `OllamaOptionsValidator.Validate` devolvendo `Success`; testes `OllamaOptionsTests.Constructor_Always_UsesDocumentedDefaults`, os 12 métodos de `OllamaOptionsValidatorTests`, `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_Always_RegistersOllamaOptionsValidator` (o validador ainda não está registrado), `EnglishTutorAppTests.RunAsync_WhenOllamaTimeoutSecondsIsZero_ExitsWithInvalidConfigurationBeforePrompt` e o novo caso de `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (a seção ainda não é ligada: a sessão abre e sai com 0). Falham pela asserção, exceto os cinco que esperam sucesso do validador, que nascem verdes porque o esqueleto aceita tudo: `Validate_WithDefaultOptions_Succeeds`, `Validate_WithHttpAndHttpsEndpoints_Succeeds`, `Validate_WithTemperatureAtLimits_Succeeds`, `Validate_WhenOutputModeCaseDiffers_Succeeds` e `Validate_WithTimeoutSecondsAtLimits_Succeeds` (mutação depois do Green).
  Green: inicializadores, regras do validador (§2.1), registro do validador em `AddEnglishTutorInfrastructure`, bloco `Brain:Ollama` no `appsettings.json`, bind com `ValidateOnStart` no `EnglishTutorApp.BuildHost`.
  Mutações depois do Green, cada uma desfeita pela edição inversa: "o limite inferior de `TimeoutSeconds` aceito passa a ser 2" (caso 1 de `Validate_WithTimeoutSecondsAtLimits_Succeeds` falha) e "o superior passa a ser 3599" (caso 3600 falha); "a temperatura aceita deixa de incluir 0" e, separadamente, "deixa de incluir 2" (os dois casos de `Validate_WithTemperatureAtLimits_Succeeds`); "o validador do `OutputMode` passa a diferenciar maiúsculas" (`Validate_WhenOutputModeCaseDiffers_Succeeds` falha); "`https` deixa de ser aceito" (`Validate_WithHttpAndHttpsEndpoints_Succeeds` falha); "o `Model` padrão passa a ser vazio" (`Validate_WithDefaultOptions_Succeeds` falha).
  Commits: `test(brain): cover Ollama options and validation` + `feat(brain): add Brain:Ollama options with validation`.
  Escolhas de forma: nenhuma

- [x] T2 — Adaptador: pedido, modos e leitura da resposta (sem falhas e sem tempo máximo). (feito: test ea4f5a2, feat 5753ead)
  Estado dos ramos até a T4 e a T5: uma `HttpRequestException` ou qualquer outra exceção do cliente propaga sem tradução; não há relógio (`SendAsync` passa o `ct` direto ao `IChatClient`). Os testes desta tarefa usam só respostas e histórico válidos, que ficam nos ramos implementados.
  Red: pacotes `OllamaSharp` e `Microsoft.Extensions.AI.Abstractions` (§6) em `Directory.Packages.props` e no `.csproj` da Infrastructure (o Executor registra as versões na §12 no mesmo commit); esqueletos `OllamaTutorBrain` (`SendAsync` lança `NotImplementedException`), `OllamaChatClients.Create` (lança `NotImplementedException`) e `OllamaLog.Replied` (método parcial do gerador, sem chamada ainda); fakes `FakeChatClient` (`Returns(string)`, `Calls`, `Tokens`, `OnRun`) e `FakeHttpMessageHandler`; testes `OllamaTutorBrainTests.SendAsync_WithTwoPreviousTurns_SendsSystemPromptHistoryAndNewMessageInOrder`, `OllamaTutorBrainTests.SendAsync_WithJsonSchemaMode_AsksForStructuredOutputWithTutorReplySchema`, `OllamaTutorBrainTests.SendAsync_WithTagsMode_AppendsTagsInstructionAndAsksForNoStructuredOutput`, `OllamaTutorBrainTests.SendAsync_WhenOutputModeCaseDiffers_UsesCanonicalMode`, `OllamaTutorBrainTests.SendAsync_Always_PassesConfiguredModelAndTemperature`, `OllamaTutorBrainTests.SendAsync_WhenHistoryHasEmptyAssistantMessage_KeepsItUnchanged`, `OllamaTutorBrainTests.SendAsync_OnSecondCall_DoesNotCarryMessagesFromTheFirst`, `OllamaTutorBrainTests.SendAsync_WithValidJson_ReturnsParsedReplyWithRawText`, `OllamaTutorBrainTests.SendAsync_WhenReplyIsOutOfFormat_ThrowsInvalidOutputWithRejectedText`, `OllamaTutorBrainTests.SendAsync_WhenReplyIsValid_LogsRepliedWithoutStudentTextOrReply`, `OllamaTutorBrainHttpTests.SendAsync_WithTwoPreviousTurns_PostsToApiChatWithSystemHistoryAndNewMessage`, `OllamaTutorBrainHttpTests.SendAsync_WithJsonSchemaMode_PostsFormatWithTutorReplySchema` e `OllamaTutorBrainHttpTests.SendAsync_WithTagsMode_PostsNoFormat`. Todos falham pela `NotImplementedException` do esqueleto.
  Green: §2.2 (leitura do arquivo de formato, mensagens, `ChatOptions`, normalização do modo, `parser`, `InvalidOutput`, log) e `OllamaChatClients.Create` (§2.2); comentário de `TutorReplySchema.cs` (§2.10). O Executor confere nos testes de fio como a versão fixada do `OllamaSharp` mapeia `ChatOptions` para o corpo (`format`, `options.temperature`) e registra na §12 o que diferir do esperado (parar se `format` não sair com o schema: o usuário decide).
  Commits: `test(brain): cover Ollama request composition and reply parsing` + `feat(brain): add OllamaTutorBrain over IChatClient`.
  Escolhas de forma: §12 em 8c05888 (versões dos pacotes) e d7431b0 (texto do log, schema sem nome)

- [x] T3 — Gravação das respostas reais do Ollama (sem código de produção). (feito: c8062aa; mutação do Red aplicada e revertida: o parser deixa de receber o conteúdo, o teste falha com InvalidOutput)
  Pré-condição e passos (§2.8): instalar o Ollama e baixar o modelo (`ollama --version` e `ollama list` nas Evidências; `ollama --help` com `serve` e `pull`, §2.5); criar `OllamaFixtureRecorderLiveTests`, `RecordingHttpHandler` e `LiveOllama`; rodar `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~OllamaFixtureRecorderLiveTests"`; copiar de `recorded-fixtures/` para `Fixtures/Ollama/` os 4 arquivos, escrever o `README.md` com a seção "Diferenças em relação à chamada do adaptador" (§2.8); colar `exceptions.txt` nas Evidências e a conclusão na §12 (tipo e status da exceção do modelo ausente e da conexão recusada; se o 404 não casar com a linha a da §2.3, o ajuste); conferir que `chat-structured.json` é JSON puro (P6); rodar o D13. Teste novo: `OllamaTutorBrainHttpTests.SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections` (nasce verde: caracterização). Red por mutação temporária pelo efeito, escolhida depois da gravação e aplicada no próprio Red (o adaptador já existe): "o adaptador deixa de entregar o conteúdo da resposta ao parser" (o teste falha pela asserção, com `InvalidOutput`), desfeita pela edição inversa.
  Commit: `test(brain): record Ollama responses`.
  Escolhas de forma: nenhuma | §12 em 3264497 (conclusões da gravação; nenhum ajuste na linha a)

- [x] T4 — Falhas do servidor: "Ollama is not running" e "ollama pull". (feito: test b5e5cf6, feat 7c3c18b)
  Red: `OllamaFailures` com `IsServerFailure` devolvendo `false` e `Translate` lançando `NotImplementedException`; testes `OllamaTutorBrainTests.SendAsync_WhenNothingAnswers_ThrowsNotRunningWithStartInstructionAndKey`, `OllamaTutorBrainTests.SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommandForConfiguredModel`, `OllamaTutorBrainTests.SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` (6 casos), `OllamaTutorBrainTests.SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` (5 casos), `OllamaTutorBrainHttpTests.SendAsync_WithRecordedModelNotFoundResponse_ThrowsPullCommandForConfiguredModel`, `OllamaTutorBrainHttpTests.SendAsync_WhenConnectionIsRefused_ThrowsNotRunningWithEndpoint` e `OllamaTutorBrainHttpTests.SendAsync_WhenServerFails_ThrowsUnknownWithGenericMessage` (falham: a exceção original propaga em vez de `BrainException`), e `OllamaTutorBrainTests.SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt`, que nasce verde (o esqueleto não traduz nada).
  Green: §2.3 e §2.5 (inclusive a derivação de `<detalhe>`: primeira linha não vazia, sem espaços nas pontas, ou `no error details were provided`); o `catch` do adaptador (`when (OllamaFailures.IsServerFailure(exception))`), com os tipos de exceção que a T3 registrou.
  Mutação depois do Green (para `SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt`): "`IsServerFailure` passa a devolver `true` para qualquer exceção" (o teste recebe `BrainException` em vez de `InvalidOperationException` e falha); depois, para a precedência (caso 6), "a linha b passa a ser avaliada antes da a" (o caso 6 falha). Para o `<detalhe>` (B1; os cinco casos nascem vermelhos no Red pela `NotImplementedException`; as mutações provam que cada ramo é isolado e são aplicadas depois do Green): "o `<detalhe>` passa a ser a mensagem inteira da exceção" (casos 1, 2 e 3 de `SendAsync_WhenServerFailureDetailVaries_UsesFirstNonBlankLineOrFallback` falham); "o `<detalhe>` deixa de pular linhas vazias e usa a primeira linha mesmo em branco" (caso 2 falha); "o `<detalhe>` deixa de remover espaços e `\r` das pontas" (caso 3 falha); "o texto de reserva deixa de existir e a mensagem fica com `()`" (casos 4 e 5 falham). Cada mutação é desfeita pela edição inversa.
  Commits: `test(brain): cover Ollama server failures` + `feat(brain): classify Ollama failures with instructions`.
  Escolhas de forma: nenhuma

- [x] T5 — Tempo máximo por chamada. (feito: test 6ef78b0, feat ec02909)
  Red: `FakeChatClient.HangsUntilCancelled(Action? onCancelled = null)` (termina só com `OperationCanceledException` quando o token é cancelado; `onCancelled` roda depois de o token ser cancelado e antes de a exceção sair do fake); `OllamaFailures.Timeout(int, OperationCanceledException)` lançando `NotImplementedException`; testes `OllamaTutorBrainTests.SendAsync_WhenOllamaDoesNotAnswerWithinTimeout_CancelsRequestAndThrowsTimeout`, `OllamaTutorBrainTests.SendAsync_OnEachCall_StartsTimeoutAgain` e `OllamaTutorBrainTests.SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` (falham pelo `WaitAsync` de 10 s: o adaptador ainda não tem relógio) e `OllamaTutorBrainTests.SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`, que nasce verde (o token do chamador já vai direto ao cliente): Red por mutação temporária pelo efeito, aplicada no próprio Red porque o repasse do token já existe, "o cancelamento do chamador deixa de chegar ao cliente" (falha pelo `WaitAsync` de 10 s, com `TimeoutException`, sem travar a suíte), desfeita pela edição inversa.
  Green: §2.4 (relógio por chamada, token ligado, filtro completo do `Timeout`) e a mensagem de tempo esgotado da §2.5.
  Mutação depois do Green, para `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`: "a condição do chamador deixa de contar no filtro do `Timeout`" (o teste recebe `BrainException(Timeout)` e falha), desfeita pela edição inversa.
  Commits: `test(brain): cover Ollama timeout and caller cancellation` + `feat(brain): stop Ollama calls after TimeoutSeconds`.
  Escolhas de forma: nenhuma

- [x] T6 — Seleção do cérebro por `Brain:Provider` (provisória). (feito: test 4e44616, feat d8c10fb)
  Red: `AddEnglishTutorInfrastructure` sem mudança de seletor (continua fixo no Claude Code); testes `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenProviderIsOllama_ResolvesOllamaBrain` (2 casos; falham: resolve `ClaudeCodeTutorBrain`) e `InfrastructureServiceCollectionExtensionsTests.AddEnglishTutorInfrastructure_WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain` (nasce verde).
  Green: §2.7 (registros concretos, `IChatClient` com chave `"Ollama"`, seletor com o comentário `Provisional selector until the BrainFactory (US-007).`) e o comentário do cabeçalho do arquivo (§2.10).
  Mutação depois do Green, para `..._WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain`: "o seletor devolve sempre o `OllamaTutorBrain`" (o teste falha), desfeita pela edição inversa.
  Commits: `test(brain): cover brain selection by Brain:Provider` + `feat(brain): select the Ollama brain by Brain:Provider`.
  Escolhas de forma: nenhuma

- [x] T7 — Console conversa com o Ollama. (feito: test 6a24b4f, feat 8cf1a78)
  Red: `ScriptedChatClient` (Console.Tests/Support) e `CreateOllamaOptions`; sai `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected` com o comentário (§2.10); testes `EnglishTutorAppTests.RunAsync_WhenProviderIsOllama_SendsRequestThroughOllamaBrainAndShowsReplyAndFeedback` e `EnglishTutorAppTests.RunAsync_WhenOllamaFails_ShowsInstructionOnErrorAndContinuesSession` (2 casos). Falham pela asserção: o console ainda tem o ramo "não conectado" (a saída tem `The tutor is not connected yet.` e o cliente falso não recebe chamada).
  Green: §2.6 (handler único em `RunSessionAsync`; apagar `TutorNotConnectedInputHandler.cs` e `ConsoleText.TutorNotConnected`).
  Commits: `test(console): cover the console talking to Ollama` + `feat(console): connect the Ollama provider to the conversation`.
  Escolhas de forma: nenhuma

- [x] T8 — `LiveBrain` e medição de VRAM (D6). (feito: testes 8014832; VRAM 3744531. **O D5 vermelho saiu desta tarefa**: foi resolvido pelas T10–T13, decisão de 2026-10-22, §12; o teste `SendAsync_WithFiveKnownErrors_AllInFormat` desta tarefa é substituído na T13)
  Testes `OllamaTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_AllInFormat`, `OllamaTutorBrainLiveTests.SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand`, `OllamaTutorBrainLiveTests.SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning` e `OllamaTutorBrainLiveTests.SendAsync_WhenHistoryHasEmptyAssistantMessage_StillReturnsValidReply`, com `[Trait("Category", "LiveBrain")]`. Nascem verdes (o adaptador existe): Red por mutação temporária pelo efeito, desfeita pela edição inversa: "`IsServerFailure` passa a devolver `false`" (o teste da porta fechada falha com a `HttpRequestException` crua; o do modelo ausente também) e "o adaptador deixa de enviar o `format`" (anotar o efeito na saída da fumaça; sem critério). Rodar o D6 e atualizar a `ARCHITECTURE.md` §7 com a medição (texto da §2.9).
  Commits: `test(brain): add Ollama LiveBrain smoke tests` + `docs(ai): record Ollama VRAM measurement`.
  Escolhas de forma: §12 em 3744531 (posição do parágrafo de VRAM; commit docs único)

- [x] T9 — DoD e evidências (primeira coleta). (feito: 2026-10-21; D1–D4 e D6–D18 verdes, D5 vermelho; a coleta final, com os números novos, é a T14)
  Rodar D1–D18 (D15 depois de todo o código), colar nas Evidências com data e hash, atualizar as contagens da §4 e de D2/D11 se algo mudou (AP-031) e conferir cada afirmação de cobertura da §12 contra a §4 (AP-049).
  Commit: `docs(ai): add US-006 evidence`.
  Escolhas de forma: nenhuma

**Revisão de escopo (2026-10-22): T10 a T14.** Ordem: regra do Core (T10), prompts (T11), `LiveBrain` do Claude Code com a tolerância (T12), D5 com a retentativa (T13), DoD final (T14). Cada uma é independente do resultado das seguintes até a T13, que é a que fecha o D5.

- [ ] T10 — Core: o validador aceita 1 ou 2 perguntas (RN-FEED01). (§2.12.1)
  Antes do primeiro teste: **D20c** (controle do D20, sobre `139adee`), colado nas Evidências; o resultado tem de listar os 6 arquivos do inventário da §2.12 (`TutorReplyValidatorTests.cs`, `TutorReplyRejectionsTests.cs`, `TutorReplyParserTests.cs`, **`ReplySentencesTests.cs`**, `TutorReplyValidator.cs`, `ClaudeCodeTutorBrainLiveTests.cs`).
  Estado dos ramos: o limite inferior (0 inválido) e a razão não mudam; só o limite superior sobe de 1 para 2.
  Red: alterar os testes existentes e acrescentar os novos (sem tocar em `src/`): `TutorReplyValidatorTests.Validate_WhenReplyAsksTwoQuestions_ReturnsValidReplyWithoutWarnings` (4 casos; substitui `Validate_WhenReplyAsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason`), `TutorReplyValidatorTests.Validate_WhenReplyAsksMoreThanTwoQuestions_RejectsWithExactlyOneQuestionReason` (4 casos), `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` (3 casos; o caso 2 vira 4; todos com a frase da tolerância), `TutorReplyParserTests.Parse_WhenJsonReplyAsksTwoQuestions_ReturnsReply` (substitui `Parse_WhenJsonReplyAsksTwoQuestions_RejectsWithExactlyOneQuestionReason`) e `TutorReplyParserTests.Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason`; os comentários de cenário dos testes alterados passam para os nomes novos da spec (`reply com duas perguntas é aceito pela tolerância`, `reply com três perguntas é inválido`); e, **só o comentário** (corpo e 7 casos intactos), o de `ReplySentencesTests.IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark` (`ReplySentencesTests.cs:28`), que passa a `// US-004 — Cenário: reply com duas perguntas é aceito pela tolerância (apoio, P21: o que é pergunta) — RN-FEED01` (B2). **Falham pela asserção (rodada 3, B1; cada asserção conferida contra o `src` atual):** os 4 casos de duas perguntas no validador (a regra atual rejeita); o teste de 2 perguntas no parser (idem); os 3 casos de `ReplyMustAskExactlyOneQuestion_WithQuestionCount_...` (falta a frase da tolerância; o caso novo de 4 também não tem `has 4 questions` na entrada antiga, mas o motivo da falha é a frase); e **os 4 casos de 3 ou mais perguntas no validador** (`IsValid` falso e `Reason` já passam hoje; a `Message` atual não contém `Replies with 1 or 2 questions are accepted`, então a asserção da frase falha). **Nascem verdes** (só estes dois): `Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason` (confere só `has 3 questions`, que a regra atual já produz; mutação depois do Green) e o teste de `ReplySentencesTests` (só mudou um comentário; não é teste novo, sem Red a provar).
  Green: `TutorReplyValidator` com `_minQuestions = 1` e `_maxQuestions = 2` (condição `questionCount < _minQuestions || questionCount > _maxQuestions`) e o comentário `// RN-FEED01: asked exactly one question; verified 1 or 2 (verification tolerance, BUSINESS_RULES), in any position; ...`; `TutorReplyRejections.ReplyMustAskExactlyOneQuestion` com a mensagem da §2.12.1. Depois do Green, os 4 casos de 3 ou mais perguntas do validador passam (agora a frase existe).
  Mutações depois do Green, cada uma desfeita pela edição inversa: "o limite superior passa a aceitar até 3 perguntas" (os 3 casos de 3 perguntas de `Validate_WhenReplyAsksMoreThanTwoQuestions_...` e `Parse_WhenJsonReplyAsksThreeQuestions_...` falham, por `IsValid` verdadeiro; o caso de 4 continua verde, porque 4 ainda é inválido); "a frase da tolerância sai da `Message`" (os 4 casos de `Validate_WhenReplyAsksMoreThanTwoQuestions_...` e os 3 de `ReplyMustAskExactlyOneQuestion_WithQuestionCount_...` falham; o teste de 3 perguntas do parser continua verde, como previsto); "o limite inferior passa a aceitar 0" (`Validate_WhenReplyHasNoQuestion_...` falha); "o limite superior volta a 1" (`Validate_WhenReplyAsksTwoQuestions_...` falha).
  Verificação de saída da tarefa: **D20** (depois da T10 e da T12 o D20 fica sem linha; ao fim da T10 só podem restar as linhas de `ClaudeCodeTutorBrainLiveTests.cs`, que a T12 resolve).
  Commits: `test(core): cover the 1 or 2 questions tolerance` + `feat(core): accept replies with one or two questions`.
  Escolhas de forma: nenhuma

- [ ] T11 — Prompts reforçados para o 14B, regra de exatamente uma pergunta mantida. (§2.12.2)
  Red: `PromptFilesContentTests.Prompt_Always_KeepsExactlyOneQuestionRuleAndNoToleranceWording` (3 casos), lendo `prompts` da saída do teste. **Nasce verde** (os prompts atuais já têm a regra e não citam tolerância): Red por mutação temporária pelo efeito, aplicada no próprio Red e desfeita pela edição inversa: "a regra de exatamente uma pergunta sai do `format.json.md`" (o caso `format.json.md` falha) e, separadamente, "o `tutor.md` ganha a frase `one or two questions are fine`" (o caso `tutor.md` falha pelo `NotContain`).
  Green: reforço de `prompts/format.json.md` e `prompts/format.tags.md` conforme a §2.12.2 (checagem final por contagem de `?`, par bom/ruim, sem tolerância); nota no `tests/EnglishTutor.Infrastructure.Tests/Fixtures/Ollama/README.md` (§2.8); `tutor.md` não muda (P15). O guarda continua verde. A prova do efeito é o D5 (T13); a do outro consumidor dos prompts (Claude Code) é a T12.
  Commits: `test(brain): guard the one-question rule in the prompts` + `feat(brain): reinforce the one-question rule in the format prompts`.
  Escolhas de forma: nenhuma (o Executor registra na §12 o texto final do reforço)

- [ ] T12 — `LiveBrain` do Claude Code com a tolerância (P17).
  Alterar `ClaudeCodeTutorBrainLiveTests`: em `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithExactlyOneQuestion` (renomeado para `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithOneOrTwoQuestions`, com o comentário de cenário atualizado) e em `SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply`, a asserção `Should().Be(1)` sobre `Count(ReplySentences.IsQuestion)` passa a `BeInRange(1, 2)`. Sem Red executável na suíte rápida (os testes são `LiveBrain`): nascem verdes com o Claude Code autenticado; Red por mutação pelo efeito, aplicada antes da mudança e desfeita: "a asserção passa a exigir 0 perguntas" (o teste falha pela asserção); a mudança então é feita e o teste roda **uma vez** (D19).
  Green: as duas asserções e o nome; rodar o D19 (a primeira vez conta como o novo Red/Green do consumidor dos prompts reforçados na T11: o Claude Code continua no formato). Rodar o **D20** (agora sem nenhuma linha).
  Commit: `test(brain): accept one or two questions in the Claude Code LiveBrain tests`.
  Escolhas de forma: nenhuma

- [ ] T13 — D5 com a retentativa do ADR-006 e fechamento do D5. (§2.12.3)
  Red: `CountingTutorBrain` (Support) e a propriedade `LiveOllama.PromptFiles`; `OllamaConversationLiveTests.SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry`; o teste antigo `OllamaTutorBrainLiveTests.SendAsync_WithFiveKnownErrors_AllInFormat` sai no mesmo commit (a camada do D5 mudou). **Nasce verde ou vermelho conforme o modelo** (é `LiveBrain`, o adaptador e o serviço existem): o Red é por mutação temporária pelo efeito, determinística: "o decorator do teste rejeita **toda** resposta como `InvalidOutput`, nas duas tentativas" (as 5 frases caem no `RawFallback` e o teste falha pela asserção `IsRawFallback == false`, com os motivos na mensagem); desfeita pela edição inversa. Outra mutação, "o decorator conta mas não altera" é o estado final.
  Green: o teste final passa; rodar o **D5 em 3 rodadas seguidas** (comando da §10) e o D6 não precisa repetir (a VRAM medida não muda). Colar nas Evidências as 3 linhas `round <n> exit=<c>` e, de cada rodada, as linhas de `recorded-fixtures/ollama-smoke.txt` (`first attempt invalid`, `retries used`, `expected corrections found`). **Se qualquer rodada ficar vermelha:** parar, classificar a causa na §12 (as duas tentativas inválidas, por quantas perguntas e por qual frase; ou erro de outro tipo) e o usuário decide, com registro prévio na spec (§7, AP-032); se só `tutor.md` ficar para mexer, é a decisão P15, que o usuário confirma antes.
  Commits: `test(brain): run the Ollama LiveBrain smoke through the conversation retry`.
  Escolhas de forma: nenhuma

- [ ] T14 — DoD e evidências finais da US.
  Rodar D1–D20 de novo (D15 e D20 depois de todo o código; D19 já rodado na T12, repetir se `prompts/` mudou depois), colar nas Evidências com data e hash (nova subseção; a de 2026-10-21 fica como histórico), atualizar contagens (AP-031: D2 364, Core 138, Console 55, Infrastructure 171; D11 69) e conferir cada afirmação de cobertura da §12 e da §4 contra a tabela (AP-049).
  Commit: `docs(ai): add US-006 final evidence`.
  Escolhas de forma: nenhuma

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| `OllamaSharp` (versão estável mais recente com `OllamaApiClient : IChatClient`; o Executor registra a versão na §12 no mesmo commit da T2 e a fixa em `Directory.Packages.props`, grupo `Infrastructure`) | `EnglishTutor.Infrastructure` | Cliente HTTP do Ollama que implementa `IChatClient` (ADR-009). Pacote externo só na Infrastructure (R1) |
| `Microsoft.Extensions.AI.Abstractions` (versão estável compatível com o `OllamaSharp` escolhido; o Executor registra a versão na §12 no mesmo commit da T2) | `EnglishTutor.Infrastructure` | O tipo `IChatClient`, `ChatMessage`, `ChatOptions` e `ChatResponseFormat` usados pelo adaptador |

Os projetos de teste recebem os tipos de `Microsoft.Extensions.AI` pela referência de projeto (transitivo); nenhum pacote novo neles. `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) já existe. Se o `OllamaSharp` não implementar `IChatClient` na versão estável, o Executor para e registra na §12 (ADR-009, contexto). A revisão de 2026-10-22 (T10–T14) **não adiciona pacote**.

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
| **D5 (`LiveBrain`) oscila com o modelo local (revisado em 2026-10-22).** Observado com a regra "exatamente 1" e **sem** retentativa: 0 a 2 frases de 5 fora do formato, todas por contagem de perguntas, nenhuma por JSON inválido | Três camadas, decisão do usuário: (1) validador aceita 1 ou 2 perguntas (T10); (2) prompts reforçados (T11); (3) D5 mede o adaptador **mais** a retentativa única do `ConversationService` (T13, §2.12.3). **Camada medida:** `OllamaTutorBrain` dentro do `ConversationService` real. **Tolerância:** zero (5/5 sem `RawFallback`, com a retentativa e com a regra 1 ou 2). **Rodadas:** 3 seguidas. **Oscilação residual:** o 14B ainda pode errar a contagem nas duas tentativas (e o schema não impede 3 ou mais perguntas ou uma 4ª frase); a chance cai muito com 1 ou 2 aceitas e a retentativa, mas não é zero. **Quem decide o vermelho:** o Executor para e classifica na §12; o usuário decide, com registro prévio na spec (mais tolerância, mexer em `tutor.md`, outro modelo), AP-032 |
| **Limite aceito (AP-040): a tolerância de 1 ou 2 perguntas é do validador do Core e vale para todos os provedores.** Consumidores e efeito de cada um | `ConversationService`: só retenta com 0 ou 3 ou mais perguntas (a retentativa de 2 deixa de existir; a mensagem de correção diz que 1 ou 2 são aceitas). `TutorReplyParser` (JSON e tags): delega ao validador; testes em T10. `LiveBrain` Claude Code: asserções passam a 1..2 (T12). `LiveBrain` Ollama (D5): mede pelo serviço (T13). Testes de fixture gravada e gravadores: continuam exigindo 1 pergunta (P17), porque a fixture documenta o caso pedido. DoD: D5 da spec já com a retentativa. `eval` da US-009: respostas de 2 perguntas deixam de ser rejeição (sem cenário afetado, §2.12.5 item 4). Prompts: continuam pedindo exatamente uma (guarda em `PromptFilesContentTests`) |
| O reforço dos prompts (`format.*.md`, compartilhados) piora o Claude Code | D19 roda o `LiveBrain` do Claude Code depois da T11 (T12); regressão: reverter o reforço no arquivo afetado e registrar na §12 |
| O `chat-request.json` gravado fica com o prompt antigo depois da T11 | Nenhum teste o lê; nota no `README.md` da pasta (§2.8); sem regravar |
| O D20 fica vermelho por arquivo fora do inventário (comentário com o nome antigo do cenário) | O inventário da §2.12 foi refeito pelo Grep do D20c sobre `139adee` (6 arquivos, inclusive `ReplySentencesTests.cs`); a T10 confere o resultado do D20c contra a lista antes do primeiro teste (rodada 3, B2) |
| Os testes de console usam o `OllamaApiClient` real por engano e chamam a rede | `CreateOllamaOptions` sempre registra o `IChatClient` falso com chave `"Ollama"`; um `ScriptedChatClient` sem resposta lança (como o `ScriptedTutorBrain`) |
| Teste `LiveBrain` da porta fechada depende de uma porta livre | Abre e fecha um `TcpListener` na porta 0 e usa essa porta; não depende do Ollama |
| `Brain:Ollama` inválido impede o uso do Claude Code (P12) | Limite aceito, consumidores listados na §2.1; o erro nomeia a chave e o valor esperado |
| Um turno com retentativa pode levar 2 × `TimeoutSeconds` | Registrado no ADR-009 e na §2.4; o D5 novo (T13) herda: cada frase pode levar até 2 chamadas |
| Falhas do Ollama (`Unknown`) não acionam o fallback da US-007 | Registrado no ADR-009 e na P2; nenhum cenário atual depende disso (Claude Code é o primário no cenário de fallback) |

## 8. Fora de escopo

- `BrainFactory`, `FallbackTutorBrain`, `Brain:FallbackProvider` e o aviso de troca de professor: **US-007** (cenários já na spec). O seletor provisório (§2.7) é removido lá; texto exato a registrar na spec da US-007 na §2.9 (D7 com o `git grep`), aplicado no commit de spec antes da T1 (AP-022).
- Suíte de contrato do `ITutorBrain` (US-008): **não é adiamento** desta US. O cenário "mesma suíte para todos os provedores" da US-008 já exige uma subclasse para o Ollama com fakes; o `FakeChatClient` e o `FakeHttpMessageHandler` serão reaproveitados lá. Nada novo a registrar.
- Comparação no `eval` e taxa de resposta fora do formato do modelo local (US-009): cenários da US-009 já na spec (`--providers ClaudeCode,Ollama`, Ollama "unavailable"); nada novo a registrar.
- Streaming da resposta, `keep_alive` e `num_ctx`: nenhum cenário da US; reavaliar se a latência da primeira chamada incomodar (ADR-009, "o que precisa ser medido"). Sem US de destino.
- Novos valores em `BrainErrorKind` para falhas do Ollama (P2): mudaria RN-BRAIN07/08; só com decisão do usuário e spec antes do código.
- `ROADMAP.md` (linha do ambiente do Ollama): o Documentador atualiza (§2.9); não é tarefa do Executor.
- Revisão de 2026-10-22: validação de **quantidade de frases** (no máximo 3) no Core, e o `ReplySentences` com outros separadores: nenhum cenário pede; o D5 observado não falhou por isso. Sem US de destino. Mexer em `prompts/tutor.md` só se o D5 da T13 continuar vermelho (P15, decisão do usuário).

Adiamentos procurados também nas P* e nos comentários de teste (AP-040): os únicos são o seletor provisório (US-007, registrado na §2.9), a troca da linha do ROADMAP (Documentador) e as decisões herdadas de planos antigos que esta US fecha (`docs/us/US-002.md` P5 → P6; `docs/us/US-003.md` e ADR-006 `RawText` vazio → P5). A revisão de 2026-10-22 não cria adiamento.

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
- **P10 — Critério do D5 e instruções do Ollama (revisada em 2026-10-22; vale a P16 para o D5).** O D5 da spec pede "5 frases de fumaça, 5/5 no formato". **Padrão original (substituído):** camada do adaptador, sem a retentativa, tolerância zero, 3 rodadas. **Padrão vigente:** camada do adaptador **mais a retentativa única do ADR-006** (P16), tolerância zero no formato (5/5 sem `RawFallback`), 3 rodadas seguidas; o número de correções esperadas só é registrado. As instruções `ollama serve` e `ollama pull` são conferidas com o Ollama instalado (T3). Teste: `OllamaConversationLiveTests.SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry`.
- **P11 — Quem registra a VRAM na `ARCHITECTURE.md` §7 (spec D6).** **Padrão proposto:** o Executor, na T8, com o texto da §2.9 e os números do D6, em commit `docs(ai)` próprio (a medição só existe depois do código). Sem efeito observável.
- **P12 — `Brain:Ollama` validado na partida com qualquer provedor.** **Padrão proposto:** sim, simétrico ao `Brain:ClaudeCode` (US-004); limite aceito com consumidores na §2.1. Teste: `EnglishTutorAppTests.RunAsync_WhenSeveralSectionsAreInvalid_ShowsEveryFailureAndReturnsInvalidConfiguration` (caso com provedor `ClaudeCode` e `Brain:Ollama:TimeoutSeconds` = 0).
- **P13 — Texto da mensagem de rejeição por perguntas (revisão 2026-10-22).** O usuário pediu que a mensagem diga quantas perguntas achou e que 1 ou 2 são aceitas. **Padrão proposto:** acrescentar a frase final `Replies with 1 or 2 questions are accepted.` à mensagem atual, que continua pedindo "exactly one question"; como essa mensagem também é a instrução de correção da retentativa, que só dispara com 0 ou 3 ou mais, o modelo pode responder com 1 ou 2 e passa. Alternativa: mensagem de correção da retentativa sem a frase da tolerância (exigiria separar o texto para o aluno/log do texto para o modelo). Teste: `TutorReplyRejectionsTests.ReplyMustAskExactlyOneQuestion_WithQuestionCount_StatesHowManyQuestionsWereFound` e `TutorReplyValidatorTests.Validate_WhenReplyAsksMoreThanTwoQuestions_RejectsWithExactlyOneQuestionReason`.
- **P14 — Aviso para a resposta de 2 perguntas aceita.** **Padrão proposto:** nenhum aviso (o cenário da spec só pede o `TutorReply`; a tolerância declara o caso aceitável; o D5 já registra pelo decorator quantas frases precisaram de retentativa). Alternativa: `Warnings` com "reply has 2 questions (tolerated)", útil para o `eval`, mas ruído no log. Teste: `TutorReplyValidatorTests.Validate_WhenReplyAsksTwoQuestions_ReturnsValidReplyWithoutWarnings`.
- **P15 — Escopo do reforço dos prompts.** **Padrão proposto:** só `format.json.md` e `format.tags.md` (texto de formato vai por último no `system`), sem citar tolerância; `tutor.md` fica como está (já tem a contagem e a regra) e só muda se o D5 da T13 continuar vermelho, com confirmação do usuário (arquivo compartilhado com o Claude Code). Alternativa: reforçar também o `tutor.md` já agora. Teste: `PromptFilesContentTests.Prompt_Always_KeepsExactlyOneQuestionRuleAndNoToleranceWording`.
- **P16 — Ponto de montagem do D5 com retentativa.** **Padrão proposto:** teste novo em classe própria, `OllamaConversationLiveTests`, com o `ConversationService` real (Core) sobre o `OllamaTutorBrain` real, um serviço novo por frase, e o teste antigo de adaptador puro sai (a camada do D5 mudou; o filtro `FullyQualifiedName~Ollama` continua pegando 5 testes por rodada). Alternativa: manter os dois testes (o de adaptador puro seguiria oscilando e o D5 da spec, que roda todo o filtro, ficaria vermelho). Teste: `OllamaConversationLiveTests.SendAsync_WithFiveKnownErrors_AllInFormatCountingTheSingleRetry`.
- **P17 — Testes com asserção de exatamente 1 pergunta, depois da tolerância.** **Padrão proposto:** relaxar para 1..2 só os dois `LiveBrain` do Claude Code que conferem resposta ao vivo (T12); manter `Be(1)` nos testes de fixture gravada (`ClaudeCodeTutorBrainTests`, `OllamaTutorBrainHttpTests`) e nos dois gravadores (`FixtureRecorderLiveTests`, `OllamaFixtureRecorderLiveTests`), porque a fixture é um exemplo do caso pedido. Alternativa: relaxar tudo (a fixture deixaria de documentar a regra pedida). Teste: `ClaudeCodeTutorBrainLiveTests.SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesWithOneOrTwoQuestions` e `ClaudeCodeTutorBrainLiveTests.SendAsync_OnSecondTurnWithUseResume_ResumesAndReturnsValidReply`.

## 10. Definition of Done (comandos verificáveis)

Ambiente dos comandos com Ollama real (D5–D10): Windows PowerShell 5.1, Ollama instalado e no ar em `http://localhost:11434` com `qwen2.5:14b-instruct-q4_K_M` baixado, sem `Brain__*` herdadas (`Get-ChildItem Env:Brain__* | Remove-Item`). O Ollama é um servidor local: variáveis do ambiente do cliente não mudam o que o adaptador envia, só as chaves `Brain__Ollama__*` que o comando define (AP-034). Nas células da tabela, `\|` é só o escape do Markdown; no comando é `|`.

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes: **364** casos (Core.Tests **138**, Console.Tests 55, Infrastructure.Tests **171**), em < 30 s, sem rede, sem GPU e sem processo real (eram 356 até a T9) |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% (a T10 mexe no Core, com testes dos dois lados do limite; a cobertura não deve cair) |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | (spec, alterada em 2026-10-22) `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama"`, em 3 rodadas seguidas: `1..3 \| ForEach-Object { dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama"; "round $_ exit=$LASTEXITCODE" }` | 3 linhas `round <n> exit=0`. O filtro também roda o gravador de fixtures (`OllamaFixtureRecorderLiveTests`, que só escreve em `bin/`) e os outros 3 testes `LiveBrain` do Ollama; os 5 testes por rodada passam. **Camada (alterada):** `OllamaTutorBrain` dentro do `ConversationService` real, **com a retentativa única do ADR-006** (`OllamaConversationLiveTests`). **Tolerância:** zero no formato (5/5 frases sem `RawFallback`), com a regra de 1 ou 2 perguntas (RN-FEED01) e os prompts reforçados; o nº de retentativas e o de correções esperadas só são registrados nas Evidências. **Rodadas:** 3 seguidas. **Vermelho:** parar, classificar a causa na §12 e o usuário decide, com registro prévio na spec (risco de oscilação, §7; AP-032) |
| D6 | (spec) `$baseline = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits); $job = Start-Job { Set-Location $using:PWD; dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama" }; $samples = @(); while ($job.State -eq 'Running') { $samples += [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits); Start-Sleep -Seconds 2 }; Receive-Job $job; "baseline=$baseline peakMiB=$(($samples \| Measure-Object -Maximum).Maximum)"; ollama ps` | testes verdes; linha `baseline=<n> peakMiB=<m>` com `m` maior que `n`; `ollama ps` lista o modelo com o tamanho em VRAM; os números entram na `ARCHITECTURE.md` §7 (T8). **Já coletado e registrado na T8; não precisa repetir** (a retentativa do D5 novo não muda a VRAM do modelo) |
| D7 | (spec) No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; "I have worked with C# since five years", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider` | uma linha `Tutor: <reply>`; nenhuma linha "The tutor is not connected yet."; `exit=0`. O pipe no PowerShell 5.1 é a cobertura exigida pelo `padroes-codigo` (AP-006); o BOM continua provado pelo teste da US-001. **Repetir na T14** (os prompts mudaram) |
| D8 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; $env:Brain__Ollama__Endpoint="http://localhost:1"; "hi t1", "hi t2", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider, Env:Brain__Ollama__Endpoint` | duas vezes, na saída de erro, `The tutor could not answer: Ollama is not running at "http://localhost:1". Start it ...` com `ollama serve` e `Brain:Ollama:Endpoint`, cada uma seguida de `You can send a sentence again or type /quit to exit.`; nenhum `An unexpected error occurred`; `exit=0` (cenário "Ollama indisponível" de ponta a ponta, sem parar o serviço) |
| D9 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; $env:Brain__Ollama__Model="nao-existe-us006"; "hi t1", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider, Env:Brain__Ollama__Model` | `The tutor could not answer: Ollama model "nao-existe-us006" is not downloaded. Run "ollama pull nao-existe-us006" ...`; nenhum `An unexpected error occurred`; `exit=0` |
| D10 | No Windows PowerShell 5.1: `$env:Brain__Provider="Ollama"; $env:Brain__Ollama__TimeoutSeconds="1"; "hi t1", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Provider, Env:Brain__Ollama__TimeoutSeconds` | `The tutor could not answer: Ollama did not answer in time (limit: 1 s) and the request was cancelled...`; nenhum `An unexpected error occurred`; `exit=0`. Se o modelo responder antes de 1 s (modelo aquecido e frase curta), repetir uma vez e registrar |
| D11 | `dotnet test --filter "Category!=LiveBrain&FullyQualifiedName~Ollama"` | verde; 69 casos (`OllamaOptionsTests` 1, `OllamaOptionsValidatorTests` 24, `OllamaTutorBrainTests` 30, `OllamaTutorBrainHttpTests` 7, testes de console e de DI com `Ollama` no nome da classe ou do método: 4 + 3 = 7); **não muda com a revisão** (os testes novos não têm `Ollama` no nome; `OllamaConversationLiveTests` é `LiveBrain`); o Executor confere o número real na T14 e atualiza (AP-031) |
| D12 | `Select-String -Path src/EnglishTutor.Console/appsettings.json -Pattern '"Endpoint": "http://localhost:11434"','"Model": "qwen2.5:14b-instruct-q4_K_M"','"Temperature": 0.6','"TimeoutSeconds": 60'` | 5 linhas: `Endpoint`, `Model` e `Temperature` do Ollama (3 linhas) e `TimeoutSeconds` do Claude Code e do Ollama (2 linhas); as do Ollama dentro de `Brain:Ollama` (ARCHITECTURE §6.1) |
| D13 | `Select-String -Path tests/EnglishTutor.Infrastructure.Tests/Fixtures/Ollama/* -Pattern 'Users','@','sk-ant'` | nenhuma linha (sem caminho pessoal, e-mail ou chave); inspeção: sem o nome do titular da conta (a nota da T11 no `README.md` também entra) |
| D14 | (Git Bash) comando do bloco abaixo, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D15 | (PowerShell) bloco D15 abaixo, depois da T7 | primeira linha `named=1` (o `ARCHITECTURE.md` foi varrido); depois, **nenhuma linha** |
| D15c | (controle do D15, na T1, antes do primeiro teste) `git grep` do bloco D15c abaixo | pelo menos uma linha para cada padrão, nos arquivos listados abaixo do bloco (prova que padrões e caminhos enxergam o texto antigo; o D15 só vale depois dele, AP-035) |
| D16 | `git grep -n -F "Provisional selector" -- src tests` | **uma** linha, em `src/EnglishTutor.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (prova do comando que a spec da US-007 usa no D7, AP-035) |
| D17 | `git status --short` após D2–D20 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |
| D18 | `dotnet list package --vulnerable --include-transitive` | nenhum pacote vulnerável (inclui `OllamaSharp` e `Microsoft.Extensions.AI.Abstractions`) |
| D19 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ClaudeCodeTutorBrainLiveTests"` (Claude Code autenticado; sem `Brain__*` herdadas), na T12 e de novo na T14 se `prompts/` mudou | verde, **1 rodada** (consome cota do Claude Code; os prompts `format.*.md` são compartilhados e o reforço da T11 não pode piorar o professor atual). **Camada:** adaptador `ClaudeCodeTutorBrain`, sem a retentativa. **Tolerância:** a de antes (4 de 5 correções esperadas, 5/5 no formato, da US-004) com a regra 1 ou 2 perguntas. **Vermelho:** parar e registrar na §12; quem decide é o usuário (reverter o reforço no arquivo afetado é a saída padrão) |
| D20 | `git grep -n -F -e "AsksTwoQuestions_RejectsWithExactlyOneQuestionReason" -e "AsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason" -e "reply com duas perguntas é inválido" -e "has 2 questions" -e "RN-FEED01: exactly one question, in any position" -e "RepliesUpToThreeSentencesWithExactlyOneQuestion" -- src tests` | depois da T10 e da T12: **nenhuma linha** (código de saída 1). Cobre o nome antigo dos testes alterados, o nome antigo do cenário (comentários de teste, **inclusive o de `ReplySentencesTests.cs`, tratado na T10**), as duas formas de asserção do texto `Contain("has 2 questions")` e `"has 2 questions"` (um único padrão `-F` cobre as duas, a diferença é só o `Contain(`), o comentário antigo do validador e o nome antigo do teste do Claude Code |
| D20c | (controle do D20, na T10, antes do primeiro teste) o mesmo `git grep` do D20 com o commit `139adee` antes de `-- src tests` | pelo menos uma linha para cada padrão: `TutorReplyValidatorTests.cs` (`AsksMoreThanOneQuestion_RejectsWithExactlyOneQuestionReason`, `reply com duas perguntas é inválido`, `has 2 questions`), `TutorReplyParserTests.cs` (`AsksTwoQuestions_RejectsWithExactlyOneQuestionReason`, `reply com duas perguntas é inválido`, `has 2 questions`), `TutorReplyRejectionsTests.cs` (`reply com duas perguntas é inválido`, `has 2 questions`), **`ReplySentencesTests.cs` (`reply com duas perguntas é inválido`, linha 28)**, `TutorReplyValidator.cs` (`RN-FEED01: exactly one question, in any position`) e `ClaudeCodeTutorBrainLiveTests.cs` (`RepliesUpToThreeSentencesWithExactlyOneQuestion`). São 6 arquivos, o inventário da §2.12; um arquivo a mais ou a menos no resultado é achado a registrar na §12 antes de seguir. Prova que o D20 enxerga o texto antigo antes da mudança (AP-035) |

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
- `ARCHITECTURE.md` §3/§6.1/§6.4 e `SPRINT_1.md` (US-006 e US-007) atualizados antes da T1 (§2.9); `ARCHITECTURE.md` §7 com a VRAM medida (T8); `ARCHITECTURE.md` (linha do `TutorReplyValidator`), ADR-005 e ADR-002 atualizados antes da T10 (§2.12.5);
- merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

**T1 (2026-10-21)**

- D15c (antes do primeiro teste, em `864b98d`): achou linhas em `ARCHITECTURE.md` (1), `EnglishTutorApp.cs` (2), `ConsoleText.cs` (1), `TutorNotConnectedInputHandler.cs` (3), `TutorReplySchema.cs` (1), `InfrastructureServiceCollectionExtensions.cs` (1) e `EnglishTutorAppTests.cs` (4): cada padrão e arquivo esperado enxerga o texto antigo.
- Red (7c8ed13): 17 falhas em Infrastructure.Tests e 2 em Console.Tests, todas pela asserção (valores vazios do esqueleto, validador que aceita tudo, validador não registrado, seção não ligada). Os cinco testes de sucesso do validador nasceram verdes, como previsto.
- Mutações depois do Green (1acc4a9), cada uma revertida (`git checkout` do arquivo, tudo já commitado; `git status --short` limpo): `MinTimeoutSeconds` 2 → `Validate_WithTimeoutSecondsAtLimits_Succeeds(1)` falha; `MaxTimeoutSeconds` 3599 → caso 3600 falha; `MinTemperature` 0,01 → `Validate_WithTemperatureAtLimits_Succeeds(0)` falha; `MaxTemperature` 1,99 → caso 2 falha; `OutputMode` com caixa → `Validate_WhenOutputModeCaseDiffers_Succeeds` (2 casos) falha; `https` recusado → `Validate_WithHttpAndHttpsEndpoints_Succeeds(https)` falha; `Model` padrão vazio → `Constructor_Always_UsesDocumentedDefaults` e as demais falham.
- Pós-Green: Core 134, Infrastructure 127 (+26), Console 53 (+2), todos verdes.

**T2 a T8 (2026-10-21)**

- T3: `ollama --version` → `ollama version is 0.40.7`; `ollama list` → `qwen2.5:14b-instruct-q4_K_M` 9,0 GB; `ollama --help` lista `serve` e `pull`. `exceptions.txt` do gravador: modelo ausente = `System.Net.Http.HttpRequestException | StatusCode=NotFound | Response status code does not indicate success: 404 (Not Found).`; conexão recusada = `HttpRequestException | StatusCode=none | <texto do SO>` com `System.Net.Sockets.SocketException` interna. Mutação do Red da T3: "o adaptador deixa de entregar o conteúdo da resposta ao parser" (`var text = string.Empty;`) → `SendAsync_WithRecordedStructuredResponse_ReturnsReplyAndCorrections` falha com `BrainException` (`missing reply`); revertida.
- T4, mutações depois do Green (revertidas, `git status` limpo): `IsServerFailure` sempre `true` → `SendAsync_WhenFailureIsNotFromTheServer_PropagatesIt` falha; a linha a só vale com status (efeito de "b antes de a") → caso 6 de `SendAsync_WhenServerCallFails_ClassifiesByStatusAndText` falha; `<detalhe>` = mensagem inteira → os 5 casos de `SendAsync_WhenServerFailureDetailVaries_...` falham; primeira linha mesmo em branco (sem pular vazias) → casos 2, 4 e 5 falham; sem `Trim` → caso 3 falha; sem texto de reserva (`First(line => true)`) → casos 2, 4 e 5 falham.
- T5: Red: 3 testes falham pelo `WaitAsync` de 10 s (`TimeoutException`); `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout` nasceu verde e, com a mutação "o token do chamador deixa de chegar ao cliente" (`CancellationToken.None`), falhou com `TimeoutException` do `WaitAsync` (revertida). Mutação depois do Green: o filtro do `Timeout` sem `!ct.IsCancellationRequested` → `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation` recebe `BrainException` e falha (revertida).
- T6: Red: 2 casos falham (resolve `ClaudeCodeTutorBrain`); `..._WhenProviderIsClaudeCode_ResolvesClaudeCodeBrain` nasceu verde. Mutação depois do Green: seletor sempre `OllamaTutorBrain` → esse teste falha (e `AddEnglishTutorInfrastructure_Always_RegistersClaudeCodeBrainAndFilePromptSource` também); revertida.
- T7: Red: 3 falhas pela asserção (saída com "not connected", cliente falso sem chamadas, erro vazio).
- T8, mutações dos testes `LiveBrain` (revertidas): `IsServerFailure` sempre `false` → `SendAsync_WhenNothingListensOnEndpoint_ThrowsNotRunning` e `SendAsync_WhenModelIsNotDownloaded_ThrowsPullCommand` falham com `HttpRequestException` crua; adaptador sem `format` → a fumaça de 5 frases continuou a falhar pelas mesmas regras de conteúdo (2 perguntas), `3/5` correções (sem critério: o `format` não muda a regra de conteúdo, só a forma do JSON).

**D1–D18 (2026-10-21; código em 8cf1a78, testes `LiveBrain` em 8014832, docs em 3744531)**

| # | Comando | Resultado |
|---|---|---|
| D1 | `dotnet build` | ✅ 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | ✅ 356 passed, 0 failed (Core 134, Console 55, Infrastructure 167), < 1 s por projeto |
| D3 | `... --collect:"XPlat Code Coverage" --results-directory TestResults` | ✅ 3 `coverage.cobertura.xml` |
| D3b | `dotnet reportgenerator ...` | ✅ `EnglishTutor.Core` 100% (linha geral 90%; Infrastructure 86%) |
| D4 | `dotnet format --verify-no-changes` | ✅ sem diferenças |
| D5 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~Ollama"` ×3 | ❌ `round 1 exit=0`, `round 2 exit=1`, `round 3 exit=0` (a rodada 2 e outras duas execuções avulsas falharam em `SendAsync_WithFiveKnownErrors_AllInFormat`, só por regra de conteúdo; os outros 4 testes passam sempre); causa e opções na §12 |
| D6 | comando da spec | ✅ `baseline=1568 peakMiB=10828`; `ollama ps`: `qwen2.5:14b-instruct-q4_K_M` 9,5 GB, 100% GPU, contexto 4096 (testes: 4 de 5, o D5 vermelho acima) |
| D7 | PowerShell 5.1 com `Brain__Provider=Ollama` | ✅ `Tutor: That's great. C# is a powerful language. How has your experience been working with it for so long?`, `Feedback:`, nenhuma linha "not connected", `exit=0` |
| D8 | `Brain__Ollama__Endpoint=http://localhost:1` | ✅ 2× `The tutor could not answer: Ollama is not running at "http://localhost:1". Start it ...` com `ollama serve` e `Brain:Ollama:Endpoint`, cada uma seguida de `You can send a sentence again or type /quit to exit.`; `exit=0` |
| D9 | `Brain__Ollama__Model=nao-existe-us006` | ✅ `Ollama model "nao-existe-us006" is not downloaded. Run "ollama pull nao-existe-us006" ...`; `exit=0` |
| D10 | `Brain__Ollama__TimeoutSeconds=1` | ✅ `Ollama did not answer in time (limit: 1 s) and the request was cancelled. ...`; `exit=0` (na primeira tentativa) |
| D11 | `dotnet test --filter "Category!=LiveBrain&FullyQualifiedName~Ollama"` | ✅ 69 casos (Infrastructure 65, Console 4) |
| D12 | `Select-String ... appsettings.json` | ✅ 5 linhas |
| D13 | `Select-String ... Fixtures/Ollama/*` | ✅ 0 linhas (inclui o README) |
| D14 | `git diff develop...HEAD \| grep -c ...` | ✅ `0` (código de saída 1) |
| D15 | bloco PowerShell | ✅ `scanned=134 named=1`; nenhuma linha |
| D16 | `git grep -n -F "Provisional selector" -- src tests` | ✅ 1 linha, em `InfrastructureServiceCollectionExtensions.cs:33` |
| D17 | `git status --short` | ✅ vazio |
| D18 | `dotnet list package --vulnerable --include-transitive` | ✅ nenhum pacote vulnerável nos 6 projetos |
| D15c | (T1) | ✅ ver T1 |

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

### 2026-10-21 — escolha do Executor (T8, forma) e D5 vermelho

- A medição de VRAM entrou na `ARCHITECTURE.md` §7 **depois da tabela** (e não entre as linhas dela, o que quebraria a tabela do Markdown), com os números do D6: linha de base 1568 MiB, pico 10828 MiB, `ollama ps` 9,5 GB 100% GPU, contexto 4096 (RTX 4070 Ti SUPER, 16 GB).
- **D5 VERMELHO (condição de parada da §7 e do D5): o usuário decide.** O adaptador responde sempre JSON que obedece ao schema; todas as rejeições são de **regra de conteúdo** ("reply must ask the student exactly one question ... has 2 questions"), nenhuma por JSON inválido. Execuções com o adaptador correto (primeira da T8, 3 rodadas do D5 e a corrida do D6), frases fora do formato em 5: 2, 0, 1, 0 e 1 (de 5 execuções com o adaptador correto, 2 ficaram 5/5; as frases rejeitadas variam: "Yesterday I go to the office by bus" e "We discussed about the new architecture"). Correções esperadas encontradas (só registro; mesma ordem, exceto a do D6, não registrada): 3/5, 3/5, 3/5 e 4/5. Opções para o usuário, com registro prévio na spec (AP-032): (a) mudar a camada do D5 para incluir a retentativa do ADR-006 (o `ConversationService` já reenvia uma vez com correção de formato); (b) aceitar tolerância (ex.: 4/5) para o modelo local; (c) reforçar o prompt do professor para o modelo 14B (`feat(brain)`, muda o `prompts/` compartilhado com o Claude Code); (d) outro modelo local. Nada foi alterado no código nem na spec por isso.

### 2026-10-21 — desvio de ordem do Executor (T2/T5)

- O fake `FakeChatClient.HangsUntilCancelled` foi criado já na T2 (junto do resto do fake), e não na T5; o Red da T5 usou-o como previsto, sem outra mudança.

### 2026-10-22 — decisão do usuário sobre o D5 vermelho (opções c + a + tolerância)

O usuário escolheu, para resolver o D5 vermelho (§11/§12 do Executor): **(c)** reforçar `prompts/` para o modelo 14B, **mais (a)** contar a retentativa única do ADR-006 no D5, **mais** uma tolerância de verificação: o validador do Core aceita **1 ou 2** perguntas no `reply` (a regra pedida ao modelo continua "exatamente uma", mantida no prompt; 0 e 3 ou mais continuam inválidas). A spec já foi atualizada (commit `docs(ai): update spec for US-006`): `RN-FEED01` e a convenção "Tolerância de verificação" em `specs/BUSINESS_RULES.md`, cenários de 2 e 3 perguntas na US-004 (`SPRINT_1.md`) e o D5 da US-006 (retentativa contada). Pedido ao Planejador:

1. Novas tarefas (T10 em diante, depois da T9) com Red → Green: (i) o `TutorReplyValidator` do Core aceita 1 ou 2 perguntas (0 e ≥ 3 inválidas; manter a razão `reply must ask the student exactly one question` e ajustar a mensagem para dizer quantas perguntas achou e que 1 ou 2 são aceitas; testes dos dois lados do limite 0/1/2/3 e todos os testes existentes de 2 perguntas atualizados); (ii) reforço dos prompts `prompts/format.json.md` e `format.tags.md` (e do prompt do professor, se for o caso) para o 14B, **mantendo a regra de exatamente uma pergunta** no texto; sem efeito observável testável além de teste de arquivo/snapshot do que existir, e com prova pelo D5; (iii) o teste `LiveBrain` do D5 passa a incluir a retentativa do ADR-006 (camada: adaptador mais a retentativa única do `ConversationService`/mecanismo equivalente; o planejador define o ponto de montagem e se a camada fica em um teste `Live` novo ou no existente).
2. Atualizar §4 (mapa e contagens), §5, §10 (D2, D5, D11 e demais que mudem), §7 (risco da oscilação, agora com a tolerância e a retentativa) e §9 (novas perguntas P13+ com padrão proposto), mantendo-as consistentes. Marcar T8/T9 conforme necessário.
3. Dizer se algum ADR novo ou alteração em `ARCHITECTURE.md`/`CLAUDE.md`/skills é necessário (a convenção de tolerância já está em `BUSINESS_RULES.md` e `padroes-codigo`); se sim, listar com texto antigo e novo.
4. Os 5 testes de contagem/estado de testes atuais (356) e os commits já feitos (T1–T9) permanecem; o plano só acrescenta. Antes de acrescentar, conferir no código e nos testes existentes onde a regra de 1 pergunta está testada (Core, Infrastructure, Console).

→ **Respostas (item → seções tocadas):**

- `1(i)` (validador do Core, 0/1/2/3, testes existentes de 2 perguntas) → §2.12.1 (regra, mensagem), §2.12 (tabela de conferência), §3, §4 (linhas do validador, parser e rejeições; asserções; contagem), §5 T10, §9 P13 e P14, §10 D20 e D20c. Resultado: aceita 1 ou 2; 0 e 3 ou mais continuam inválidas com o mesmo `Reason`; a mensagem ganha `Replies with 1 or 2 questions are accepted.` e continua dizendo quantas perguntas achou; os dois lados de cada limite têm teste (0 e 1 existentes, 2 e 3 novos, 4 também); os 3 testes de 2 perguntas (validador com 4 casos, parser, rejeições) foram alterados, e o de 3 ou mais nasce verde com mutação depois do Green.
- `1(ii)` (reforço dos prompts, regra mantida) → §2.12.2, §3, §4 (`PromptFilesContentTests`, 3 casos: regra presente, tolerância ausente), §5 T11, §9 P15, §10 D19. Resultado: só `format.json.md` e `format.tags.md` (o texto de formato vai por último no `system`); `tutor.md` só muda se o D5 continuar vermelho (confirmação do usuário); teste de arquivo como guarda; a prova do efeito é o D5.
- `1(iii)` (D5 com a retentativa; ponto de montagem) → §2.12.3, §3, §4 (nova classe `OllamaConversationLiveTests`, teste antigo sai), §5 T13, §9 P10 (revisada) e P16, §10 D5. Resultado: teste `Live` **novo** com o `ConversationService` real sobre o `OllamaTutorBrain` real, um serviço por frase; "fora do formato" = `RawFallback`; um `CountingTutorBrain` só registra quantas retentativas houve; o teste antigo de adaptador puro sai no mesmo commit; ficam 5 testes por rodada, como o D5 já dizia.
- `2` (§4, §5, §7, §9, §10 consistentes; marcar T8/T9) → §4 (contagem: Core 138, Console 55, Infrastructure 171, total 364), §5 (T8 e T9 marcadas como feitas, com a nota de que o D5 passou para a T13; novas T10–T14), §7 (risco da oscilação refeito com camada, tolerância, rodadas, oscilação residual e quem decide; novo limite aceito da tolerância com consumidores e efeito; riscos do reforço e do `chat-request.json`), §9 (P13 a P17; P10 revisada), §10 (D2 364; D5 com a camada nova; D11 inalterado em 69, conferido; D19, D20, D20c novos; D6 não repete). Conferido: a contagem da §4 fecha com a D2 e as afirmações de cobertura ("os dois lados de cada limite") valem para o que a tabela prova (0, 1, 2, 3 e 4).
- `3` (ADR novo; `ARCHITECTURE.md`, `CLAUDE.md`, skills) → §2.12.5. **ADR novo: não** (revisão do ADR-005, que já tem o campo "Revisado:"). **Muda:** `ARCHITECTURE.md` (1 trecho, linha do `TutorReplyValidator`), ADR-005 (linha "Revisado" e a frase do item 1) e ADR-002 (item 5), com texto antigo e novo, no commit `docs(ai): update architecture for US-006` que o orquestrador aplica **antes da T10** (o commit de spec já existe, `15b4291`). **Não mudam:** `CLAUDE.md`, `README.md`, `specs/PRODUCT.md` (descrevem a regra pedida) e as skills (a `padroes-codigo` já traz a convenção; nenhuma outra repete a regra).
- `4` (356 e T1–T9 permanecem; conferir onde a regra de 1 pergunta está testada) → §2.12 (tabela "Conferência do código e dos testes existentes": validador, rejeições, parser, `LiveBrain` do Claude Code, fixtures e gravadores, Console.Tests, prompts). Nada de T1–T9 foi alterado; as contagens de T1–T9 (356) viram 364 só depois da T10 e da T11. Achados que o pedido não citava: dois `LiveBrain` do Claude Code com `Be(1)` ficariam vermelhos com a tolerância (T12, P17); o `chat-request.json` gravado fica com o prompt antigo (§2.8, nota na T11); nenhum teste do Console nem do `ConversationService` usa resposta de 2 perguntas contra o validador real.
- **Status** → alterado para "Aguardando aprovação (revisão de escopo, 2026-10-22)". Nenhuma pergunta bloqueia a T10; a P13 (texto da mensagem) e a P15 (escopo dos prompts) são as que o usuário pode querer mudar.

### 2026-10-22 — pedido do Revisor do plano (rodada 3, REPROVADO)

- **B1 (bloqueante):** §4 (`Validate_WhenReplyAsksMoreThanTwoQuestions_RejectsWithExactlyOneQuestionReason`) confere `Message` com `Replies with 1 or 2 questions are accepted`, mas a T10 diz que os 4 casos "nascem verdes" e que na mutação "limite superior aceita até 3" o caso de 4 continua verde. Hoje a mensagem não tem a frase (`TutorReplyRejections.cs:30`), então os 4 casos nascem **vermelhos**. Fazer o plano dizer uma coisa só: ou a frase sai das asserções desse teste e fica só no `TutorReplyRejectionsTests` (3 casos), ou a T10 lista os 4 casos entre os que falham pela asserção. Reescrever Red e mutações de acordo.

  → **Incorporado** (opção 2: a T10 lista os 4 casos entre os que falham pela asserção). Seções tocadas: §2.12 (linha do `TutorReplyValidatorTests` na tabela de conferência), §2.12.1 (parágrafo da `Message`: quais testes nascem vermelhos e qual nasce verde), §4 (linhas de "reply com três perguntas é inválido" no validador e nas rejeições; asserção principal do validador com o parágrafo "Red (B1)"; asserção do parser, que continua só em `has 3 questions`), §5 T10 (Red reescrito: lista de falhas e lista dos que nascem verdes; mutações reescritas), §9 P13 (Teste inclui o validador), "Alternativas descartadas" (a opção 1, com o motivo). Efeito no Red da T10, conferido assertiva por assertiva contra o `src` atual: falham pela asserção os 4 casos de 2 perguntas (validador), o teste de 2 perguntas (parser), os 3 casos de `ReplyMustAskExactlyOneQuestion_WithQuestionCount_...` e os 4 casos de 3 ou mais perguntas (validador; só a frase da tolerância falta). Nascem verdes só `Parse_WhenJsonReplyAsksThreeQuestions_RejectsWithExactlyOneQuestionReason` (confere só `has 3 questions`) e o teste de `ReplySentencesTests` (só comentário). Mutações da T10, depois do Green: "limite superior aceita até 3" derruba os 3 casos de 3 perguntas do validador e o teste de 3 do parser (o caso de 4 continua verde, porque 4 é inválido), "frase da tolerância sai da `Message`" (derruba os 4 casos do validador e os 3 de `TutorReplyRejectionsTests`; o parser de 3 continua verde), "limite inferior aceita 0" e "limite superior volta a 1". Contagens inalteradas (364; Core 138, Console 55, Infrastructure 171; D11 69), pois só mudou o estado de Red, não o número de casos.

- **B2 (bloqueante):** `tests/EnglishTutor.Core.Tests/Brain/ReplySentencesTests.cs:28` tem o comentário `// US-004 — Cenário: reply com duas perguntas é inválido (apoio, P21...)`, que casa com o padrão do D20 e quebraria o "nenhuma linha". Incluir o arquivo na §2.12, na §3 e na T10, com o novo texto do comentário, e no D20c.

  → **Incorporado.** Seções tocadas: §2.12 (nova linha na tabela de conferência e parágrafo "Inventário do Grep do D20 sobre `139adee`" com os 6 arquivos e as linhas), §3 (nova linha `Alterar` para `ReplySentencesTests.cs`), §4 (nova linha de apoio do mapa, com o método existente `IsQuestion_WithSentence_ReturnsWhetherFinalPunctuationContainsQuestionMark`, sem mudança de contagem; sentença na "Contagem"), §5 T10 (antes do primeiro teste, o D20c tem de listar os 6 arquivos; o comentário novo entra no Red; verificação de saída pelo D20), §5 T12 (roda o D20 sem linhas), §7 (novo risco "D20 vermelho por arquivo fora do inventário"), §10 D20 e D20c (o arquivo e o padrão `reply com duas perguntas é inválido` na linha 28). Texto novo do comentário: `// US-004 — Cenário: reply com duas perguntas é aceito pela tolerância (apoio, P21: o que é pergunta) — RN-FEED01`. Conferido por Grep em `src` e `tests` (todos os padrões do D20) antes de fechar o inventário: além dos arquivos já listados, só esse comentário casava. Evidência do comando (AP-035): o D20c, sobre `139adee`, é o controle que acha as ocorrências antigas antes da mudança, incluindo esse arquivo; o D20 depois da T10 e da T12 deve ficar sem linha. O comentário não vale como teste novo (corpo e 7 casos intactos), então a contagem não muda.

- **Status** → mantido como "Aguardando aprovação (revisão de escopo de 2026-10-22 ...)" (cabeçalho).

- **Sugestões S1–S5 da rodada 3** (`plan-review.md`, rodada 3): **não pedidas nesta revisão; não incorporadas; registradas como pendentes para decisão do usuário**, sem mudar o plano:
  - **S1** (`SPRINT_1.md` US-004: cenário "frase com erro gramatical" e D6 da US-004 dizem "exatamente uma pergunta", que a T12 relaxa no `LiveBrain`): pendente. Nota de contexto: a P17 já diz que o texto é a regra pedida; falta decidir se o plano registra isso também no comentário do teste ("pedida 1, verificada 1..2") ou propõe nota na spec.
  - **S2** (§2.12.1, P13 e §2.12.2(iii): a frase da tolerância chega ao modelo pela mensagem de correção da retentativa, e o guarda diz que a tolerância não vai ao prompt): pendente. Se o D5 oscilar, a frase `Replies with 1 or 2 questions are accepted.` é a primeira candidata a sair.
  - **S3** (`PromptFilesContentTests` nasce e continua verde sem o reforço da T11, sem teste que falhe antes e passe depois): pendente. A prova do reforço continua sendo o D5 (`LiveBrain`).
  - **S4** (T13: frase "Outra mutação, 'o decorator conta mas não altera' é o estado final" confusa): pendente de decisão do usuário. A frase da T13 foi mantida como estava.
  - **S5** (`CHANGELOG.md:28` registra a RN-FEED01 antiga; o Documentador acrescenta a entrada da tolerância): pendente; o Documentador cuida na fase final.
