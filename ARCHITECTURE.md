# ARCHITECTURE.md — EnglishTutor

> Fonte única da verdade da arquitetura. Qualquer mudança estrutural exige um ADR em `docs/adr/` **e** atualização deste arquivo no mesmo PR.

## 1. Visão geral

```mermaid
flowchart LR
    Mic[Microfone] -->|push-to-talk| Rec[IAudioRecorder]
    Rec --> STT[ISpeechToText<br/>Whisper.net]
    STT -->|Transcript<br/>texto + confiança| CS[ConversationService]
    CS -->|BrainRequest<br/>system prompt + janela + resumo de erros| Brain[ITutorBrain]
    Brain -->|TutorReply<br/>reply + feedback| CS
    CS -->|somente reply| TTS[ITextToSpeech<br/>Piper / Kokoro]
    TTS --> Player[IAudioPlayer] --> Spk[Alto-falante]
    CS -->|feedback| UI[Interface<br/>Console → WPF/Blazor]
    CS --> Store[IConversationStore<br/>SQLite]
```

Tudo roda localmente, exceto o provedor `ClaudeCode`, que chama o processo `claude` usando a assinatura do usuário (sem API key).

## 2. Camadas e regra de dependência

```mermaid
flowchart TB
    Console[EnglishTutor.Console<br/>UI, DI, configuração, comando eval] --> Infra[EnglishTutor.Infrastructure<br/>adaptadores]
    Console --> Core
    Infra --> Core[EnglishTutor.Core<br/>domínio + portas + ConversationService]
```

| Projeto | Responsabilidade | Pode depender de |
|---|---|---|
| `EnglishTutor.Core` | Modelos de domínio, interfaces (portas), `ConversationService`, montagem de mensagens, regras de negócio | BCL, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options` |
| `EnglishTutor.Infrastructure` | Implementações das portas: STT, cérebros, parser, TTS, áudio, persistência | Core + pacotes externos (Whisper.net, NAudio, EF Core/Dapper, Microsoft.Extensions.AI, ...) |
| `EnglishTutor.Console` | Composition root: DI, Options, Serilog, loop de interação, comando `eval` | Core + Infrastructure |
| (futuro) `EnglishTutor.Wpf` / `EnglishTutor.Web` | Outra interface sobre o mesmo Core | Core + Infrastructure |

Regras:

- **R1** — `Core` nunca referencia `Infrastructure`, `Console` nem pacotes de terceiros.
- **R2** — A interface (console hoje, WPF/Blazor amanhã) só conversa com o `ConversationService` e com eventos/DTOs do Core. Trocar a UI não pode exigir mudança no Core.
- **R3** — Cada adaptador implementa exatamente uma porta e é registrado no DI por configuração.
- **R4** — Nenhuma classe de `Infrastructure` contém regra de negócio (ex.: quantos turnos manter, quando refazer a pergunta). Isso mora no Core.

## 3. Estrutura de pastas

```
src/
  EnglishTutor.Core/
    Conversation/      ConversationService, ConversationWindow, TurnMessageBuilder, TutorMode
    Brain/             ITutorBrain, BrainRequest, BrainMessage, TutorReply, Correction, BrainException
    Speech/            ISpeechToText, Transcript, TranscribedWord, ITextToSpeech
    Audio/             IAudioRecorder, IAudioPlayer, AudioClip
    Persistence/       IConversationStore, SessionRecord, TurnRecord, RecurringError
  EnglishTutor.Infrastructure/
    Brain/             ClaudeCodeTutorBrain, ClaudeCodeStreamingBrain, OllamaTutorBrain,
                       TutorReplyParser, BrainFactory, FallbackTutorBrain
    Stt/               WhisperNetSpeechToText, WhisperModelDownloader
    Tts/               PiperTextToSpeech, KokoroHttpTextToSpeech
    Audio/             NAudioRecorder, NAudioPlayer
    Persistence/       SqliteConversationStore
  EnglishTutor.Console/
    Program.cs, appsettings.json, Commands/ (chat, eval), Ui/
prompts/
  tutor.md             system prompt único (regras de conteúdo)
  format.json.md       instrução de formato quando o provedor usa saída estruturada
  format.tags.md       instrução de formato com <reply>/<feedback>
eval/
  golden-set.json
tests/
  EnglishTutor.Core.Tests/
  EnglishTutor.Infrastructure.Tests/   (inclui Fixtures/ e Contract/)
```

## 4. Portas do Core (contratos)

Todas as operações assíncronas recebem `CancellationToken`.

```csharp
public interface ISpeechToText
{
    Task<Transcript> TranscribeAsync(AudioClip audio, CancellationToken ct);
}
public sealed record Transcript(string Text, IReadOnlyList<TranscribedWord> Words);
public sealed record TranscribedWord(string Word, float Confidence);

public interface ITutorBrain
{
    string Name { get; }
    Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct);
}
public sealed record BrainRequest(string SystemPrompt, IReadOnlyList<BrainMessage> Messages, string? SessionHint);
public sealed record BrainMessage(BrainRole Role, string Content);
public sealed record TutorReply(string Reply, IReadOnlyList<Correction> Corrections,
                                string? Pronunciation, string? Vocabulary, string? Praise,
                                bool IsRawFallback, string RawText);

public interface ITextToSpeech
{
    Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct);
}
public interface IAudioRecorder { Task<AudioClip> RecordUntilReleasedAsync(CancellationToken ct); }
public interface IAudioPlayer   { Task PlayAsync(AudioClip clip, CancellationToken ct); }

public interface IConversationStore { /* sessões, turnos, erros recorrentes, palavras de baixa confiança */ }
```

Os nomes acima são o alvo; o Planejador de cada US pode refiná-los, desde que registre a mudança aqui.

## 5. ConversationService — dono do histórico

O histórico **pertence à aplicação**, não ao provedor (RN-BRAIN01).

A cada turno o `ConversationService`:

1. Recebe a fala do aluno (`Transcript`, ou texto digitado na fase de texto).
2. Monta a mensagem do turno (RN-CONV02):
   ```
   Student said (transcribed): "<texto>"
   Low-confidence words (possible pronunciation issues): ["word1", "word2"]
   Mode: <free-conversation | job-interview | listening-practice>
   ```
3. Monta o `BrainRequest`: system prompt (`tutor.md` + instrução de formato do provedor) + resumo dos erros recorrentes + últimos N turnos (`Conversation:MaxTurnsInWindow`) + mensagem atual.
4. Chama `ITutorBrain.SendAsync`. O cérebro devolve um `TutorReply` já validado pelo parser.
5. Se o parser rejeitar, o serviço faz **uma** nova tentativa com instrução de correção; se falhar de novo, usa `TutorReply.IsRawFallback = true` (RN-BRAIN04).
6. Envia **apenas** `Reply` ao TTS (RN-TTS01); o feedback vai só para a tela.
7. Persiste o turno e atualiza erros recorrentes / palavras de baixa confiança.

Provedores com estado (Claude Code via `--resume`) podem usar `SessionHint` como otimização, mas devem funcionar corretamente recebendo apenas o `BrainRequest` completo.

## 6. Cérebro plugável

```mermaid
classDiagram
    class ITutorBrain { <<interface>> +Name +SendAsync(BrainRequest, ct) TutorReply }
    ITutorBrain <|.. ClaudeCodeTutorBrain : processo por chamada
    ITutorBrain <|.. ClaudeCodeStreamingBrain : processo persistente stream-json
    ITutorBrain <|.. OllamaTutorBrain : HTTP via IChatClient
    ITutorBrain <|.. FallbackTutorBrain : decorator primário → secundário
    ClaudeCodeTutorBrain --> TutorReplyParser
    OllamaTutorBrain --> TutorReplyParser
    BrainFactory --> ITutorBrain : cria conforme Brain:Provider
```

### 6.1 Configuração

```json
"Brain": {
  "Provider": "ClaudeCode",
  "FallbackProvider": "Ollama",
  "ClaudeCode": {
    "ExecutablePath": "claude",
    "Model": "haiku",
    "WorkingDirectory": "%LOCALAPPDATA%/EnglishTutor/brain",
    "TimeoutSeconds": 60,
    "UseResume": true,
    "OutputMode": "JsonSchema"
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "Model": "qwen2.5:14b-instruct-q4_K_M",
    "Temperature": 0.6,
    "OutputMode": "JsonSchema"
  }
}
```

Validação das Options na inicialização (`ValidateOnStart`).

### 6.2 Contrato de saída (ver ADR-002)

- Forma canônica: objeto JSON `{ reply, corrections[{original, corrected, explanation}], pronunciation, vocabulary, praise }`.
- `ClaudeCode` usa `--json-schema` e lê `structured_output`; `Ollama` usa `format` com o mesmo JSON Schema.
- `TutorReplyParser` é **único**: aceita o JSON canônico e, como alternativa, o formato com tags `<reply>…</reply><feedback>…</feedback>` (texto cru, modelos menores, `OutputMode: Tags`).
- O parser também valida as regras de conteúdo verificáveis (reply não vazio, máximo de 2 correções, etc. — RN-FEED*).

### 6.3 Provedor Claude Code (processo)

Invocação (por turno, Fase 2):

```
claude -p
  --output-format json
  --json-schema <schema>                  (OutputMode=JsonSchema)
  --system-prompt-file prompts/tutor.md
  --append-system-prompt-file prompts/format.json.md
  --tools ""                              (nenhuma ferramenta embutida)
  --disallowedTools "mcp__*"
  --strict-mcp-config
  --permission-mode dontAsk
  --model <Brain:ClaudeCode:Model>
  [--resume <session_id>]
```

- `WorkingDirectory` é uma pasta vazia dedicada, para não carregar `CLAUDE.md`, hooks ou MCP de outros projetos.
- **Nunca** `--bare` (exige `ANTHROPIC_API_KEY`, não usa a assinatura).
- Prompt enviado via stdin (evita limites e escaping de linha de comando no Windows).
- Erros mapeados para `BrainException` com `BrainErrorKind`: `CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`, `InvalidOutput`, `Unknown`.
- `UsageLimitReached` (e, se configurado, `CliNotFound`/`NotAuthenticated`) aciona o `FallbackTutorBrain`.

### 6.4 Provedor Ollama (HTTP)

- Via `Microsoft.Extensions.AI` (`IChatClient`) com o pacote do Ollama; histórico montado pela aplicação a cada chamada (sem estado).
- O mesmo `IChatClient` servirá de base para o futuro provedor `AnthropicApi`.

## 7. Voz

- STT: Whisper.net; runtime **CUDA** (GPU NVIDIA RTX 4070 Ti SUPER, 16 GB), fallback Vulkan e depois CPU. Modelo padrão `large-v3-turbo`, baixado na primeira execução para `models/` (ignorado pelo Git). Confiança por palavra a partir das probabilidades de token.
- TTS: Piper (executável, opção inicial — sem Python) e Kokoro (alternativa). Velocidade configurável (`Tts:Speed`). Fala apenas o `Reply`.
- Áudio: NAudio (captura 16 kHz mono para o Whisper; reprodução).

### Orçamento de VRAM (alvo, a medir)

| Componente | Estimativa |
|---|---|
| Whisper large-v3-turbo (CUDA) | ~2–3 GB |
| Whisper small/base (modo "LLM local") | <1 GB |
| Piper / Kokoro | <0,5 GB |
| LLM local 14B Q4 | ~9–10 GB |
| LLM local 7–8B Q4 | ~5–6 GB |

## 8. Persistência

SQLite em `%LOCALAPPDATA%/EnglishTutor/tutor.db`: `Sessions`, `Turns` (fala transcrita, resposta, feedback, provedor, latência), `RecurringErrors` (padrão → contagem → último exemplo), `LowConfidenceWords`. No início de cada sessão o resumo dos erros mais frequentes entra no `BrainRequest` (RN-CONV04).

## 9. Transversais

- DI com `Microsoft.Extensions.Hosting`; Options Pattern com validação.
- Logs estruturados com Serilog (console + arquivo em `logs/`), incluindo latência por etapa (STT, cérebro, TTS).
- `CancellationToken` em todo fluxo assíncrono; Ctrl+C encerra a sessão de forma limpa.
- `TreatWarningsAsErrors`, `Nullable` habilitado, analisadores .NET.

## 10. Estratégia de testes

| Tipo | Onde | Ferramentas |
|---|---|---|
| Unidade (TDD) | `EnglishTutor.Core.Tests` | xUnit, FluentAssertions, NSubstitute; fakes de todas as portas |
| Parser / fixtures | `EnglishTutor.Infrastructure.Tests` | saídas reais gravadas em `Fixtures/` (Claude Code JSON, Ollama JSON) |
| Contrato | `Infrastructure.Tests/Contract/TutorBrainContractTests` (classe abstrata) | uma subclasse por provedor; fake de processo/HTTP |
| Live | mesmas classes com `[Trait("Category","LiveBrain")]` | excluídas por padrão: `dotnet test --filter "Category!=LiveBrain"` |
| Avaliação | comando `eval` + `eval/golden-set.json` | relatório em `eval/reports/` |

Meta: cobertura de linhas do Core ≥ 80% (coverlet).
