# ADR-009 — Provedor Ollama: IChatClient, montagem do pedido, classificação das falhas e tempo máximo por chamada

- **Status:** Aceito
- **Data:** 2026-10-21
- **US relacionadas:** US-006 (e, como consumidoras, US-007, que escolhe e encadeia os cérebros, US-008, que roda a suíte de contrato sobre este adaptador, e US-009, que o mede no `eval`)
- **Complementa:** ADR-002 (item 4: o adaptador anexa a instrução de formato), ADR-006 (o adaptador só traduz resposta inválida em `InvalidOutput`), ADR-008 (mesmo padrão de falha, tempo e mensagem ao aluno)

## Contexto

A US-006 liga o professor local: o aluno conversa com um modelo rodando no Ollama (`/api/chat`), com o mesmo `ConversationService`, o mesmo `prompts/tutor.md` e o mesmo contrato de resposta do Claude Code (RN-BRAIN01 a RN-BRAIN04). A `ARCHITECTURE.md` §6.4 diz "via `Microsoft.Extensions.AI` (`IChatClient`) com o pacote do Ollama" e deixa três pontos em aberto, que esta decisão fecha:

- **Qual cliente.** O pacote `Microsoft.Extensions.AI.Ollama` foi marcado como obsoleto em favor do `OllamaSharp`, cujo `OllamaApiClient` implementa `IChatClient` (conferir no NuGet na T2 da US-006; se o estado mudou, o Executor para e registra na §12).
- **Como o pedido é montado.** O Ollama não tem `--system-prompt-file` nem `--append-system-prompt-file`: o adaptador lê `prompts/format.json.md` ou `prompts/format.tags.md` e anexa ao system prompt (ADR-002, item 4). Com `OutputMode = JsonSchema`, o campo `format` leva o mesmo JSON Schema do Claude Code (`TutorReplySchema`).
- **Como as falhas são classificadas.** Os cenários "Ollama indisponível" e "modelo não baixado" pedem mensagens com a instrução de correção. A `BrainErrorKind` (RN-BRAIN07) não tem valores para "servidor fora do ar" nem "modelo ausente". O tempo máximo (RN-BRAIN09) vale para "o cérebro", não só para o Claude Code.

Fatos que restringem a solução:

- o `ConversationService` já monta o histórico, a retentativa de formato e o `RawText` do professor (ADR-006); o adaptador não guarda estado (RN-BRAIN01);
- o Core não referencia pacotes externos (R1); o `IChatClient` e o `OllamaSharp` ficam na Infrastructure;
- o Ollama responde 404 com um corpo `{"error": ...}` quando o modelo não existe, e recusa a conexão quando o servidor não está no ar. O que a biblioteca lança nesses casos só se confirma com resposta gravada de verdade (T3 da US-006);
- `HttpClient.Timeout` (padrão 100 s) dispara `TaskCanceledException` e confundiria o limite configurável;
- até a `BrainFactory` (US-007), o `ITutorBrain` precisa ser escolhido por `Brain:Provider` de alguma forma, ou o console não consegue usar o Ollama.

## Decisão

1. **O adaptador depende de `IChatClient`** (`Microsoft.Extensions.AI.Abstractions`) e a implementação é o `OllamaApiClient` do `OllamaSharp`. Uma fábrica pública, `OllamaChatClients.Create(OllamaOptions, HttpMessageHandler? = null)`, monta o cliente (`HttpClient` com `BaseAddress = Endpoint` e `Timeout = Timeout.InfiniteTimeSpan`) e é registrada como serviço com chave `"Ollama"`; os testes usam a mesma fábrica com um `HttpMessageHandler` falso, então o fio HTTP real do cliente é conferido (não só o contrato do `IChatClient`).
2. **Montagem do pedido.** Mensagens para o `IChatClient`: uma `System` com `BrainRequest.SystemPrompt` + `"\n\n"` + o conteúdo do arquivo de formato do modo (`JsonSchema` → `format.json.md`; `Tags` → `format.tags.md`), seguida de todas as mensagens do `BrainRequest`, na ordem, sem filtrar nenhuma (inclusive `Assistant` vazio). `ChatOptions`: `ModelId` e `Temperature` de `Brain:Ollama`; com `JsonSchema`, `ResponseFormat = ChatResponseFormat.ForJsonSchema(TutorReplySchema.Json)`; com `Tags`, nenhum formato estruturado. O modo é normalizado por `BrainOutputModes.TryGetCanonical` (dono único, como no Claude Code). `BrainRequest.SessionHint` é ignorado. Resposta inválida → `BrainException.InvalidOutput` (ADR-006).
3. **Falhas, sem tipo novo na `BrainErrorKind`.** O adaptador captura só as exceções do servidor (`HttpRequestException` e o tipo próprio da biblioteca que a T3 registrar) e as traduz em `OllamaFailures`: servidor sem resposta HTTP (sem `StatusCode`) → `Unknown` com "Ollama is not running at ..." e a instrução de iniciá-lo; status 404 ou texto da falha com `not found` → `Unknown` com `ollama pull <modelo>`; as demais → `Unknown` com "Ollama failed (<detalhe>)". Qualquer outra exceção propaga (erro inesperado no console). A `Message` da `BrainException` é o texto ao aluno, como no ADR-008.
4. **Tempo máximo por chamada**, igual ao ADR-008: `Brain:Ollama:TimeoutSeconds` (padrão 60, faixa 1 a 3600), um `CancellationTokenSource(TimeSpan, TimeProvider)` ligado ao token do chamador por `SendAsync`; ao estourar (e o chamador não ter cancelado) → `BrainException(Timeout)`; cancelamento do chamador propaga `OperationCanceledException`, inclusive quando o tempo também estourou. A retentativa do ADR-006 é outra chamada, com o próprio limite.
5. **Seleção provisória do `ITutorBrain`.** Até a `BrainFactory` da US-007, o registro do `ITutorBrain` em `AddEnglishTutorInfrastructure` escolhe `OllamaTutorBrain` quando `Brain:Provider` (normalizado por `BrainProviders.TryGetCanonical`) é `Ollama` e `ClaudeCodeTutorBrain` nos demais casos. O comentário no código traz o texto `Provisional selector` e a US-007 o remove (registrado na DoD da US-007).

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| `HttpClient` direto, sem biblioteca (JSON montado à mão) | Nenhum pacote novo em `src/`; fio HTTP e erros 100% sob controle | Contraria a `ARCHITECTURE.md` §6.4 e perde a base comum do futuro `AnthropicApi`; reimplementa o que a biblioteca já faz |
| `Microsoft.Extensions.AI.Ollama` | Pacote oficial da família `Microsoft.Extensions.AI` | Obsoleto em favor do `OllamaSharp` (a confirmar na T2) |
| Novos valores em `BrainErrorKind` (`ProviderUnavailable`, `ModelNotFound`) | Fallback da US-007 poderia reagir a "Ollama fora do ar" quando ele for o primário | Muda a RN-BRAIN07, a RN-BRAIN08, a `ARCHITECTURE.md` §4 e o enum do Core por dois casos de um só provedor; adiável sem custo (ver Consequências) |
| Reaproveitar `CliNotFound` para "Ollama fora do ar" | Aciona o fallback pela RN-BRAIN08 como está | O nome e a regra falam de executável (CLI); a mensagem e o log ficam enganosos |
| Sem tempo máximo no Ollama (só o `HttpClient.Timeout` de 100 s) | Menos código | Contraria a RN-BRAIN09 ("tempo máximo do cérebro, configurável"); o limite não seria configurável e o erro seria `TaskCanceledException` |
| `BrainFactory` já nesta US | Sem seletor provisório | É o escopo da US-007 (cenários de escolha e fallback); aumentaria a US-006 sem cenário que a justifique |

## Consequências

- Positivas: o Ollama entra no console com o mesmo contrato, as mesmas mensagens de falha (o que houve e o que fazer) e a mesma tolerância a cancelamento do Claude Code; os testes cobrem o fio HTTP real sem rede; a US-008 e a US-009 recebem um adaptador testável por fakes.
- Negativas / riscos:
  - **Efeito na RN-BRAIN08 (US-007):** "Ollama fora do ar" e "modelo ausente" são `Unknown`, e a RN-BRAIN08 não aciona o fallback para `Unknown`. Com o Ollama como `Provider` e o Claude Code como `FallbackProvider`, essas duas falhas **não** acionam o fallback; só `Timeout` aciona. Hoje o fallback previsto é do Claude Code para o Ollama (cenário "fallback por limite de uso" da US-007), então não há cenário afetado. Se o usuário quiser fallback a partir do Ollama, a mudança é nova (valores na `BrainErrorKind` ou nova regra) e passa pela spec antes do código;
  - a classificação do modelo ausente usa o status 404 ou o texto `not found`; um servidor errado que responda 404 em `/api/chat` mostraria a instrução `ollama pull`. A mensagem repete o `Endpoint`/`Model` configurados, e o aluno ainda vê a chave a corrigir;
  - o tipo de exceção que o `OllamaSharp` lança para a resposta 404 só se confirma com a gravação (T3); se a biblioteca mudar entre versões, o teste com a resposta gravada acusa;
  - a primeira chamada de um modelo grande carrega-o na VRAM e pode passar do padrão de 60 s em disco lento; a mensagem de `Timeout` aponta para `Brain:Ollama:TimeoutSeconds`;
  - `Brain:Ollama` é validado na partida (`ValidateOnStart`) mesmo com `Brain:Provider = ClaudeCode` (simétrico ao que a US-004 aceitou para o Claude Code): uma seção do Ollama inválida impede a partida; o fallback da US-007 exige as duas seções válidas de qualquer forma;
  - uma mensagem `Assistant` vazia (retentativa com `RawText` vazio, ADR-006) é enviada como está; a aceitação pelo Ollama é conferida em teste `LiveBrain`.
- O que precisa ser medido ou revisitado: VRAM do modelo padrão durante o teste real (registrada na `ARCHITECTURE.md` §7); taxa de resposta fora do formato do modelo local (US-009); necessidade de `keep_alive`/`num_ctx` se a latência da primeira chamada incomodar (fora desta US).
- `ARCHITECTURE.md` §3, §6.1 e §6.4 atualizadas antes do código (texto proposto em `tasks/US-006/plan.md` §2.9).
