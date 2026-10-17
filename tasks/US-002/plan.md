# Plano — US-002 Contrato do professor e parser único da resposta

Status: Aprovado em 2026-10-17
Branch: feature/US-002_contrato-parser-resposta
Spec: specs/sprints/SPRINT_1.md#us-002
Regras: RN-BRAIN03, RN-FEED01–05 (RN-BRAIN04 como consumidora das mensagens de rejeição), ARCHITECTURE §2 (R1, R4), §3, §4, §6.2, §10; ADR-002 (Proposto), ADR-005 (Aceito, novo)

## 1. Objetivo

Criar o contrato `TutorReply`/`Correction` no Core e o `TutorReplyParser` único na Infrastructure, que aceita o JSON canônico e o formato com tags, valida as regras de conteúdo verificáveis (no Core) e devolve um resultado válido ou uma rejeição com motivo curto e mensagem que diz o que faltou e o que se espera.

## 2. Abordagem

Decisão central no **ADR-005 (Aceito)**: `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md`. A ARCHITECTURE §6.2 diz que o parser "valida as regras de conteúdo", mas a R4 proíbe regra de negócio na Infrastructure. Solução: **formato na Infrastructure, regras de conteúdo no Core**.

### 2.1 Core (`src/EnglishTutor.Core/Brain/`) — só BCL

```csharp
public sealed record Correction(string Original, string Corrected, string Explanation);

public sealed record TutorReply(string Reply, IReadOnlyList<Correction> Corrections,
                                string? Pronunciation, string? Vocabulary, string? Praise,
                                bool IsRawFallback, string RawText);   // igual à ARCHITECTURE §4

public sealed record TutorReplyRejection(string Reason, string Message);

public sealed record TutorReplyParseResult
{
    private TutorReplyParseResult(TutorReply? reply, TutorReplyRejection? rejection,
                                  IReadOnlyList<string> warnings)
    {
        Reply = reply;
        Rejection = rejection;
        Warnings = warnings;
        IsValid = reply is not null;                   // atribuída aqui, uma única vez
    }

    public TutorReply? Reply { get; }
    public TutorReplyRejection? Rejection { get; }
    public IReadOnlyList<string> Warnings { get; }    // vazio quando inválido

    [MemberNotNullWhen(true, nameof(Reply))]
    [MemberNotNullWhen(false, nameof(Rejection))]
    public bool IsValid { get; }                       // auto-propriedade só com get (armazenada)

    public static TutorReplyParseResult Valid(TutorReply reply, IReadOnlyList<string> warnings);
    public static TutorReplyParseResult Invalid(TutorReplyRejection rejection);
}

public static class TutorReplyRejections               // catálogo único de motivos e mensagens
{
    public const string MissingReplyReason = "missing reply";
    public const string ReplyMustEndWithQuestionReason = "reply must end with a question";
    public const string InvalidJsonReason = "invalid JSON";
    public const string InvalidCorrectionsReason = "invalid corrections";
    public static TutorReplyRejection MissingReply();
    public static TutorReplyRejection ReplyMustEndWithQuestion(string reply);
    public static TutorReplyRejection InvalidJson(string detail);
    public static TutorReplyRejection InvalidCorrections(string foundKind);
}

public static class TutorReplyValidator
{
    public static TutorReplyParseResult Validate(TutorReply candidate);
}

public static class FeedbackFormatter
{
    public static IReadOnlyList<string> Format(TutorReply reply);
}
```

**Forma de `TutorReplyParseResult` (decidida aqui, S2):** `IsValid` é uma **auto-propriedade só com `get`, atribuída no construtor privado**, e não uma propriedade calculada (`=> Reply is not null`). Com a propriedade calculada, o compilador analisa o corpo do getter e não consegue provar `[MemberNotNullWhen(false, nameof(Rejection))]`, o que gera CS8775 e quebra o build com `TreatWarningsAsErrors`. Na auto-propriedade não há corpo para analisar: os atributos valem como contrato para quem chama, e a coerência é garantida pelas duas fábricas (únicos caminhos de criação, já que o construtor é privado e as propriedades não têm `init`, então nem um `with` consegue deixar o objeto incoerente). `Valid` exige `reply` não nulo e passa `rejection: null`; `Invalid` passa `reply: null`, `rejection` não nulo e `warnings: []`; as duas lançam `ArgumentNullException` para argumento nulo. A forma `TryGetReply(out ...)`/`TryGetRejection(out ...)` foi descartada: deixa os testes e o consumidor da US-003 mais verbosos e não ganha nada sobre a auto-propriedade. O build da T2 (esqueleto já com essa forma) confirma que não há CS8775; se houver, o Executor para e registra no §12 antes de mudar a forma (AP-009).

**Mensagens de rejeição** (inglês, porque vão na instrução de correção ao modelo — RN-BRAIN04; formato `"<Reason>: <o que está errado>. Expected <o que se espera>."`; D6):

| Reason | Message (texto-alvo; o teste confere prefixo, o trecho do problema e `Expected`) |
|---|---|
| `missing reply` | `missing reply: the answer has no reply text. Expected a JSON object with a non-empty "reply" string, or the spoken text inside <reply>...</reply> followed by <feedback>...</feedback>.` |
| `reply must end with a question` | `reply must end with a question: the reply "<reply, até 80 caracteres + ...>" does not end with "?". Expected 1 to 3 short sentences that end with one question to the student.` |
| `invalid JSON` | `invalid JSON: <detalhe da JsonException>. Expected a single JSON object {"reply": string, "corrections": [{"original", "corrected", "explanation"}], "pronunciation": string or null, "vocabulary": string or null, "praise": string or null} with no text around it.` |
| `invalid corrections` | `invalid corrections: "corrections" is a <string/number/object/...>. Expected an array of objects {"original", "corrected", "explanation"} (an empty array when there are no errors).` |

**Regras do `TutorReplyValidator.Validate`** (nesta ordem):

1. `Reply` após `Trim()` vazio → `Invalid(MissingReply())` (RN-FEED01).
2. `Reply` após `Trim()` não termina com `'?'` → `Invalid(ReplyMustEndWithQuestion(reply))` (RN-FEED01, ADR-002 item 5).
3. Correções com `Original` ou `Corrected` em branco são descartadas; aviso `Dropped {n} correction(s) without "original" or "corrected".` Campos das correções restantes passam por `Trim()`.
4. Mais de 2 correções → mantém as 2 primeiras; aviso `Tutor reply had {n} corrections; kept the first 2 (RN-FEED02).`
5. `Pronunciation`, `Vocabulary`, `Praise` em branco → `null`; senão `Trim()`.
6. Resultado: `Valid(candidate with { Reply = trimmed, Corrections = ..., ... }, warnings)`. `RawText` e `IsRawFallback` (sempre `false`) são preservados.

Não validados (fora de escopo, §8): número de frases (1–3), "exatamente uma" pergunta, ausência de correção dentro do reply, RN-FEED03 (praise obrigatório sem erros), RN-FEED05 (o JSON só tem um campo `vocabulary`; nas tags, linhas repetidas são unidas).

**`FeedbackFormatter.Format`** (texto exibido; uma linha por item, nesta ordem; `IsRawFallback = true` → lista vazia, RN-BRAIN04):

- correção: `{Original} → {Corrected} ({Explanation})`; sem explicação: `{Original} → {Corrected}`
- `Pronunciation: {Pronunciation}`
- `Vocabulary: {Vocabulary}`
- `Praise: {Praise}`

### 2.2 Infrastructure (`src/EnglishTutor.Infrastructure/Brain/`)

```csharp
public sealed partial class TutorReplyParser(ILogger<TutorReplyParser> logger)   // partial: [GeneratedRegex]
{
    public TutorReplyParseResult Parse(string rawText);   // ArgumentNullException se null
    [GeneratedRegex(@"<reply>(.*?)</reply>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReplyBlock();
    // idem para <feedback> e para "corrected (explanation)"
}
internal static partial class TutorReplyParserLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "Tutor reply adjusted: {Adjustment}")]
    public static partial void TutorReplyAdjusted(ILogger logger, string adjustment);
}
```

`TutorReplyParser` é `sealed partial class` porque os métodos `[GeneratedRegex]` são `static partial` dentro dela (S8). Os regex ficam na própria classe, não numa classe auxiliar.

Método síncrono (CPU puro, sem I/O), por isso sem `CancellationToken`. Faixa de EventIds: Console 1000–1999 (US-001), cérebro/parser 2000–2999.

**Detecção de formato** (sem receber `OutputMode`; P7): texto após `Trim()` começa com `{` → JSON; senão → tags. Espaços e quebras de linha antes do `{` não mudam a detecção (teste `Parse_WhenJsonHasLeadingWhitespace_DetectsJson`).

**Caminho JSON** (`System.Text.Json.JsonDocument`, BCL; leitura manual para mensagens claras):

- `JsonException` → `Invalid(InvalidJson(ex.Message))`. Texto depois do objeto (ex.: `{...} Hope this helps!`) faz o `JsonDocument.Parse` lançar `JsonException` → `invalid JSON` (P5).
- Não há ramo para "raiz que não é objeto" (S3): só chega aqui texto que começa com `{`, então a raiz é sempre objeto ou o `JsonDocument.Parse` lança `JsonException`. Um texto que começa com `[` vai para o caminho de tags e é rejeitado com `missing reply`; aceitar `[` na detecção foi descartado porque o schema nunca tem array na raiz.
- `reply`: string → valor; ausente, `null` ou outro tipo → `""` (o validador rejeita com `missing reply`).
- `corrections`: ausente ou `null` → `[]`; array → cada elemento objeto vira `Correction` (campos não-string → `""`); elemento não-objeto vira `Correction("", "", "")` (o validador descarta com aviso); outro tipo → `Invalid(InvalidCorrections(kind))`.
- `pronunciation`, `vocabulary`, `praise`: string → valor; senão `null`. Propriedades extras são ignoradas; nomes diferenciam maiúsculas (o schema é minúsculo).

**Caminho tags** (`[GeneratedRegex]`, `Singleline | IgnoreCase`):

- `<reply>(.*?)</reply>` → `Reply` (com `Trim()`); sem o bloco → `""` (o validador rejeita com `missing reply`). Texto fora das tags é ignorado. JSON em cerca ```` ```json ```` ou precedido de texto cai aqui (não começa com `{`) e, sem `<reply>`, é rejeitado com `missing reply` (P5).
- `<feedback>(.*?)</feedback>` opcional. Cada linha: `Trim()`, remove marcador inicial `-`, `*` ou `•` e espaços; linhas vazias ignoradas.
  - prefixo `Pronunciation:` / `Vocabulary:` / `Praise:` (sem diferenciar maiúsculas) → campo correspondente; linhas repetidas do mesmo rótulo são unidas com `"; "`.
  - linha com `→` ou `->` → corta na primeira seta: esquerda = `Original`; direita casa com `^(.*?)\s*\(([^()]*)\)\s*$` → `Corrected` + `Explanation`, senão `Corrected` = direita e `Explanation = ""`.
  - qualquer outra linha é ignorada.

Depois de montar o candidato (`IsRawFallback = false`, `RawText = rawText` original, sem `Trim`), o parser chama `TutorReplyValidator.Validate` e loga cada aviso com `TutorReplyAdjusted` (2000). Não loga o texto cru nem rejeições (a retentativa da US-003 loga).

### 2.3 Testes e fixtures

- Novo projeto `tests/EnglishTutor.Infrastructure.Tests` (já previsto na ARCHITECTURE §3 e na skill `tdd-dotnet`), com `Fixtures/TutorReply/` copiado para a saída e `Support/RecordingLogger<T>` (fake à mão que guarda nível, EventId e mensagem formatada; P8). O hook `post-edit-dotnet` já mapeia `src/EnglishTutor.Infrastructure` → `tests/EnglishTutor.Infrastructure.Tests`.
- Fixtures desta US são **exemplos da spec** (não gravações reais): as saídas reais do Claude Code e do Ollama entram em US-004/US-006 e passam pelo mesmo parser. Isso é um desvio declarado do texto atual da ARCHITECTURE §10 e da skill `tdd-dotnet`; a ressalva proposta para esses documentos está no §2.4.

| Fixture | Conteúdo |
|---|---|
| `json-valid.json` | JSON exato do cenário "resposta JSON válida" |
| `json-reply-without-question.json` | `{"reply":"I like that.","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Nice sentence."}` |
| `json-three-corrections.json` | reply terminando em `?` e 3 correções distintas (`a1→b1`, `a2→b2`, `a3→b3` com explicações) |
| `json-no-errors-with-praise.json` | `{"reply":"That sounds great! What did you build last week?","corrections":[],"pronunciation":null,"vocabulary":null,"praise":"Great use of the present perfect!"}` |
| `tags-valid.txt` | texto exato do cenário "resposta com tags válida" |
| `tags-labeled-lines.txt` | layout de `prompts/format.tags.md` (multilinha) sem correção, com linhas `- Pronunciation: ...`, `- Vocabulary: ...`, `- Praise: ...` |
| `out-of-format.txt` | `Sure! Here is my answer without tags.` |

Os casos de P5, P7 e P9 (cerca markdown, texto em volta do JSON, espaços antes do `{`, rótulo repetido nas tags) usam strings inline nos testes, não fixtures: são entradas curtas e o que importa é a borda exata.

UTF-8 sem BOM e LF (`.editorconfig`/`.gitattributes` já cobrem `*.json`; `*.txt` cai em `* text=auto eol=lf`). Leitura com `File.ReadAllText` a partir de `AppContext.BaseDirectory` (`Support/Fixture.Read(name)`).

### 2.4 Alterações nos documentos-raiz (propostas; aplicadas pelo orquestrador na aprovação do plano, antes da T1)

Ponto único de aplicação (S7): depois que o usuário aprovar o plano e **antes da T1**, o orquestrador aplica tudo o que está nesta seção num commit `docs(ai): ...` (junto com o ADR-005 em "Aceito", se aprovado). Nenhuma tarefa da §5 altera esses documentos.

`ARCHITECTURE.md`:

- §3, linha `Brain/` do Core — texto novo:
  `Brain/             ITutorBrain, BrainRequest, BrainMessage, TutorReply, Correction, TutorReplyParseResult,`
  `                   TutorReplyRejection, TutorReplyRejections, TutorReplyValidator, FeedbackFormatter,`
  `                   BrainException, BrainOptions, BrainOptionsValidator, BrainProviders`
- §4, logo após `TutorReply`: acrescentar `public sealed record Correction(string Original, string Corrected, string Explanation);`
- §6.2, último bullet — texto novo: "As regras de conteúdo verificáveis (reply não vazio e terminando em `?`, no máximo 2 correções, correção com `original` e `corrected`) moram no Core (`TutorReplyValidator`, R4); o `TutorReplyParser` cuida só do formato e delega a elas (ADR-005). Toda rejeição traz um `Reason` curto e uma `Message` que diz o que faltou e o que se espera, usada na retentativa da RN-BRAIN04."
- §10, linha 241 (`Parser / fixtures`), coluna Ferramentas — texto novo (S5): `` saídas reais gravadas em `Fixtures/` (Claude Code JSON, Ollama JSON) a partir da US-004; até lá (US-002), exemplos da spec em `Fixtures/TutorReply/` ``

Repetições da regra das fixtures (S5; Grep por `saídas reais|gravad` em `CLAUDE.md`, `ARCHITECTURE.md` e `.claude/**`). Só a skill `tdd-dotnet` repete; `CLAUDE.md` e `checklist-revisao` não falam de fixtures reais:

- `.claude/skills/tdd-dotnet/SKILL.md:28` — texto novo: `  Fixtures/claude-json-structured.json         # saídas reais gravadas a partir da US-004 (CopyToOutputDirectory)`
- `.claude/skills/tdd-dotnet/SKILL.md:90` — texto novo: ``Saídas reais de processo/HTTP gravadas em `Fixtures/*.json` (a partir da US-004; a US-002 usa exemplos da spec em `Fixtures/TutorReply/`), com `<None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />`. Nunca dados pessoais nem tokens.``

Repetições das regras do parser em `CLAUDE.md` e skills (Grep por `parser|TutorReply|R4`): `CLAUDE.md:31` ("Mensagens de erro do parser e das validações dizem o que está errado e o que se espera"), `checklist-revisao/SKILL.md:37` ("Regra de negócio no Core, não em adaptadores (R4)"), `checklist-revisao/SKILL.md:50` e `tdd-dotnet/SKILL.md:25` (`Brain/TutorReplyParserTests.cs` em Infrastructure.Tests) **continuam corretas sem alteração**. Fora as duas linhas da `tdd-dotnet` acima, nenhuma linha de `CLAUDE.md` ou de skill precisa mudar (AP-005).

`specs/` não muda: os cenários e as RN citadas cobrem o comportamento; as interpretações estão em §9.

### Alternativas descartadas

- Parser inteiro na Infrastructure com as regras dentro — viola R4 e prende os motivos a um adaptador (ADR-005).
- Parser inteiro no Core — formato de provedor é detalhe de adaptador (§3, ADR-002).
- `JsonSerializer` com DTO — mensagens de erro genéricas e menos controle sobre tipos errados; `JsonDocument` dá mensagens que dizem o que se espera.
- `Parse(string, OutputMode)` — exigiria o adaptador repassar o modo e falharia quando um modelo responde JSON no modo tags; a detecção pelo primeiro caractere é suficiente.
- `IsValid => Reply is not null` com `MemberNotNullWhen` — CS8775 com `TreatWarningsAsErrors` (§2.1).
- `TryGetReply`/`TryGetRejection` — mais verboso para testes e consumidor, sem ganho sobre a auto-propriedade (§2.1).
- Detectar `[` como JSON — o schema nunca tem array na raiz; só criaria um ramo de rejeição a mais (§2.2).
- `FakeLogger` (`Microsoft.Extensions.Diagnostics.Testing`) — pacote a mais para um fake de ~20 linhas.
- `ITutorReplyParser` — não há segunda implementação nem consumidor que precise de dublê nesta US (R3 trata de portas).
- `AddEnglishTutorInfrastructure(configuration)` agora — sem consumidor; entra com o primeiro adaptador (US-004).

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-005-regras-de-conteudo-no-core-parser-na-infrastructure.md` | Decisão (Aceito) — criado junto com este plano |
| Alterar | `ARCHITECTURE.md` | §3, §4, §6.2, §10 (texto em §2.4) — pelo orquestrador, antes da T1 |
| Alterar | `.claude/skills/tdd-dotnet/SKILL.md` | Linhas 28 e 90: ressalva das fixtures reais (texto em §2.4) — pelo orquestrador, antes da T1 |
| Alterar | `Directory.Packages.props` | `PackageVersion` de `Microsoft.Extensions.Logging.Abstractions` (10.0.13) no grupo novo `Infrastructure` |
| Alterar | `EnglishTutor.slnx` | Incluir `tests/EnglishTutor.Infrastructure.Tests` |
| Alterar | `src/EnglishTutor.Infrastructure/EnglishTutor.Infrastructure.csproj` | `PackageReference` a `Microsoft.Extensions.Logging.Abstractions` |
| Criar | `src/EnglishTutor.Core/Brain/Correction.cs` | Correção `original → corrected` |
| Criar | `src/EnglishTutor.Core/Brain/TutorReply.cs` | Contrato da resposta (ARCHITECTURE §4) |
| Criar | `src/EnglishTutor.Core/Brain/TutorReplyRejection.cs` | Motivo + mensagem |
| Criar | `src/EnglishTutor.Core/Brain/TutorReplyRejections.cs` | Catálogo de motivos e mensagens |
| Criar | `src/EnglishTutor.Core/Brain/TutorReplyParseResult.cs` | Resultado válido/inválido + avisos (`IsValid` armazenada, §2.1) |
| Criar | `src/EnglishTutor.Core/Brain/TutorReplyValidator.cs` | Regras RN-FEED01/02 verificáveis |
| Criar | `src/EnglishTutor.Core/Brain/FeedbackFormatter.cs` | Linhas de feedback exibidas |
| Criar | `src/EnglishTutor.Infrastructure/Brain/TutorReplyParser.cs` | `sealed partial class`: detecção JSON/tags, extração, `[GeneratedRegex]`, delegação ao validador, log dos avisos |
| Criar | `src/EnglishTutor.Infrastructure/Brain/TutorReplyParserLog.cs` | `[LoggerMessage]` 2000 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/EnglishTutor.Infrastructure.Tests.csproj` | Projeto de teste (pacotes de teste como o Core.Tests, sem DI; `Fixtures\**` com `CopyToOutputDirectory=PreserveNewest`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/RecordingLogger.cs` | Logger fake que registra entradas |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/Fixture.cs` | Leitura de fixtures |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Fixtures/TutorReply/*` | 7 fixtures (§2.3) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Brain/TutorReplyParserTests.cs` | Cenários da US pelo parser |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/TutorReplyRejectionsTests.cs` | Mensagens dizem o que falta e o que se espera (D6) |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/TutorReplyValidatorTests.cs` | Regras de conteúdo |
| Criar | `tests/EnglishTutor.Core.Tests/Brain/FeedbackFormatterTests.cs` | Texto do feedback |

## 4. Mapa cenário BDD → testes

| Cenário | Projeto de teste | Classe | Método (Metodo_Cenario_Resultado) | Tipo |
|---|---|---|---|---|
| resposta JSON válida | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonIsValid_ReturnsReplyAndOneCorrection` (fixture `json-valid.json`; Reply exato; 1 correção `since five years`/`for five years`/explicação) | Unidade com fixture |
| resposta JSON válida (apoio, P7) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonHasLeadingWhitespace_DetectsJson` (`[Theory]`: prefixos `"  "` e `"\r\n\t"` antes de `{"reply":"What did you do?","corrections":[]}`; `IsValid`, Reply `What did you do?`, `RawText` igual à entrada sem `Trim`) | Unidade |
| resposta JSON válida | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyEndsWithQuestion_ReturnsValidReplyWithoutWarnings` (candidato com 1 correção completa e `Praise = null`) | Unidade |
| resposta JSON válida | Core.Tests | `FeedbackFormatterTests` | `Format_WithCorrection_ShowsOriginalArrowCorrected` (`[Theory]`: com explicação → `since five years → for five years (use 'for' with a period of time)`; sem explicação → `I am work → I work`) | Unidade |
| resposta com tags válida | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsAreValid_ReturnsReplyAndOneCorrection` (fixture `tags-valid.txt`; Reply exato; correção `I am work`/`I work`/`simple present`) | Unidade com fixture |
| resposta com tags válida | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsCorrectionLineVaries_ParsesOriginalCorrectedAndExplanation` (`[Theory]`: `- a → b (x)`; `* a -> b (x)`; `- a → b` → explicação `""`) | Unidade |
| resposta com tags válida (apoio) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsHaveLabeledFeedbackLines_MapsPronunciationVocabularyAndPraise` (fixture `tags-labeled-lines.txt`) | Unidade com fixture |
| resposta com tags válida (apoio, P9) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsRepeatLabeledLine_JoinsValuesWithSemicolon` (feedback com `- Vocabulary: commute` e `- vocabulary: rush hour`; `Vocabulary == "commute; rush hour"`) | Unidade |
| resposta com tags válida (apoio) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsHaveNoFeedbackBlock_ReturnsReplyWithEmptyFeedback` | Unidade |
| resposta fora do formato | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTextHasNoJsonNorTags_RejectsWithMissingReply` (fixture `out-of-format.txt`; `Reason == "missing reply"`) | Unidade com fixture |
| resposta fora do formato | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyIsMissingNullOrEmpty_RejectsWithMissingReply` (`[Theory]`: `{}`, `{"reply":null}`, `{"reply":""}`) | Unidade |
| resposta fora do formato | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyIsBlank_RejectsWithMissingReply` (`[Theory]`: `""`, `"   "`, `"\t\n"`) | Unidade |
| resposta fora do formato (apoio) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonIsMalformed_RejectsWithInvalidJson` (`{"reply": "Hi?"` sem fechar) | Unidade |
| resposta fora do formato (apoio, P5) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson` (`{"reply":"Hi?","corrections":[]} Hope this helps!`; `Reason == "invalid JSON"`, `Message` contém `Expected`) | Unidade |
| resposta fora do formato (apoio, P5) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonIsInFenceOrAfterText_RejectsWithMissingReply` (`[Theory]`: ```` "```json\n{\"reply\":\"Hi?\",\"corrections\":[]}\n```" ````; `"Here is my answer: {\"reply\":\"Hi?\",\"corrections\":[]}"`; `Reason == "missing reply"`, `Message` contém `Expected`) | Unidade |
| resposta fora do formato (apoio) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonCorrectionsIsNotArray_RejectsWithInvalidCorrections` (`"corrections": "none"`) | Unidade |
| reply sem pergunta final | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonReplyHasNoFinalQuestion_RejectsWithQuestionReason` (fixture `json-reply-without-question.json`; `Reason == "reply must end with a question"`) | Unidade com fixture |
| reply sem pergunta final | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenTagsReplyHasNoFinalQuestion_RejectsWithQuestionReason` (mesma regra nos dois formatos, RN-BRAIN03) | Unidade |
| reply sem pergunta final | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyDoesNotEndWithQuestion_RejectsWithQuestionReason` (`[Theory]`: `I like that.`; `Is it good? I like that.`) | Unidade |
| reply sem pergunta final (apoio) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed` | Unidade |
| mais de duas correções | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonHasThreeCorrections_KeepsFirstTwoAndLogsWarning` (fixture `json-three-corrections.json`; correções `a1`, `a2`; `RecordingLogger` tem 1 entrada `Warning`, EventId 2000, mensagem com `3 corrections` e `kept the first 2`) | Unidade com fixture |
| mais de duas correções | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenMoreThanTwoCorrections_KeepsFirstTwoAndWarns` | Unidade |
| mais de duas correções (apoio) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenCorrectionLacksOriginalOrCorrected_DropsItAndWarns` | Unidade |
| sem erros | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenJsonHasNoCorrectionsAndPraise_FeedbackContainsPraise` (fixture `json-no-errors-with-praise.json`; `FeedbackFormatter.Format(reply)` contém `Praise: Great use of the present perfect!`) | Unidade com fixture |
| sem erros | Core.Tests | `FeedbackFormatterTests` | `Format_WhenNoCorrectionsAndPraise_ContainsPraise` | Unidade |
| sem erros (apoio, P4) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenNoCorrectionsAndNoPraise_IsValidWithoutWarnings` (candidato `Corrections = []`, `Praise = null`, demais opcionais `null`; `IsValid`, `Warnings` vazio, `Praise` continua `null`) | Unidade |
| (apoio: ordem e rótulos do feedback) | Core.Tests | `FeedbackFormatterTests` | `Format_WithPronunciationAndVocabulary_ShowsLabeledLinesInOrder` | Unidade |
| (apoio: RN-BRAIN04, resposta crua sem feedback) | Core.Tests | `FeedbackFormatterTests` | `Format_WhenRawFallback_ReturnsNoLines` | Unidade |
| (apoio: campos opcionais) | Core.Tests | `TutorReplyValidatorTests` | `Validate_WhenOptionalFieldsAreBlank_NormalizesThemToNull` | Unidade |
| (apoio: contrato preserva o texto cru) | Infrastructure.Tests | `TutorReplyParserTests` | `Parse_WhenValid_KeepsRawTextAndIsNotRawFallback` (entrada **JSON**: conteúdo da fixture `json-valid.json`, lido com `Fixture.Read`; `IsValid`, `Reply.RawText` igual ao texto lido, byte a byte, e `Reply.IsRawFallback == false`. Só passa pelo caminho JSON, por isso fica verde no Green da T6, S11) | Unidade com fixture |
| (DoD D6: mensagem diz o que falta e o que se espera) | Core.Tests | `TutorReplyRejectionsTests` | `Rejection_ForEachReason_MessageStartsWithReasonAndStatesExpectation` (`[Theory]` com os 4 motivos: `Message` começa com `Reason + ": "`, contém o trecho do problema — ex. `"I like that."`, `"corrections" is a string` — e contém `Expected`) | Unidade |

Total previsto: **31 métodos, 44 casos** — Core.Tests: 13 métodos (`TutorReplyValidatorTests` 8 = 6 `[Fact]` + 2 `[Theory]` com 3 e 2 casos = 11 casos; `FeedbackFormatterTests` 4 = 3 `[Fact]` + 1 `[Theory]` com 2 = 5 casos; `TutorReplyRejectionsTests` 1 `[Theory]` com 4) = 20 casos; Infrastructure.Tests: 18 métodos em `TutorReplyParserTests` (14 `[Fact]` + 4 `[Theory]` com 3, 3, 2 e 2 casos) = 24 casos. Suíte inteira após a US: 93 casos (Core.Tests 47, Console.Tests 22, Infrastructure.Tests 24). Se o Executor mudar a contagem, atualiza §4 e D2 com o motivo.

## 5. Tarefas (ordem de execução TDD)

Pré-condição (ponto único, S7): na aprovação do plano e **antes da T1**, o orquestrador aplica todas as alterações da §2.4 (`ARCHITECTURE.md` §3/§4/§6.2/§10 e `.claude/skills/tdd-dotnet/SKILL.md:28,90`) num commit `docs(ai): ...`, com o ADR-005 em "Aceito" se o usuário aprovar. Nenhuma tarefa abaixo mexe nesses documentos.

- [x] T1 — Esqueleto: `PackageVersion` de `Microsoft.Extensions.Logging.Abstractions` no CPM; `PackageReference` no Infrastructure; projeto `tests/EnglishTutor.Infrastructure.Tests` (csproj, `Fixtures\**` copiado para a saída, `Using` de FluentAssertions e Xunit) no `EnglishTutor.slnx`; `Support/Fixture.cs`, `Support/RecordingLogger.cs`; as 7 fixtures. `dotnet build` com 0 warnings e `dotnet test` verde (testes existentes). (commit: `chore(infra): add infrastructure test project and tutor reply fixtures`)
- [x] T2 — Red: esqueletos de `Correction`, `TutorReply`, `TutorReplyRejection`, `TutorReplyParseResult` (já com a forma da §2.1: construtor privado que atribui `IsValid`; `dotnet build` sem CS8775), `TutorReplyRejections` (métodos com `NotImplementedException`) + `TutorReplyRejectionsTests`. Green: mensagens da §2.1 e fábricas `Valid`/`Invalid`. (commits: `test(core): tutor reply rejection messages` + `feat(core): add tutor reply contract and rejection catalog`)
- [x] T3 — Red: esqueleto de `TutorReplyValidator` + testes de reply (`ReturnsValidReplyWithoutWarnings`, `IsBlank`, `DoesNotEndWithQuestion`, `FollowedBySpaces`, `NoCorrectionsAndNoPraise` — P4, entra aqui porque depois do Green da T3 já passaria e nasceria verde na T4). Green: regras 1, 2 e 6. (commits: `test(core): tutor reply validation of the reply text` + `feat(core): validate tutor reply text`)
- [x] T4 — Red: `MoreThanTwoCorrections`, `CorrectionLacksOriginalOrCorrected`, `OptionalFieldsAreBlank`. Green: regras 3, 4 e 5 com avisos; `NoCorrectionsAndNoPraise` (T3) continua verde como regressão (nenhum aviso novo para lista vazia ou `praise` nulo). (commits: `test(core): tutor reply corrections and optional fields` + `feat(core): limit corrections and normalize optional feedback`)
- [ ] T5 — Red: esqueleto de `FeedbackFormatter` + `FeedbackFormatterTests`. Green: formatação da §2.1. (commits: `test(core): feedback display lines` + `feat(core): format tutor feedback lines`)
- [ ] T6 — Red: esqueleto de `TutorReplyParser` (`sealed partial class`, + `TutorReplyParserLog`) e testes JSON: `JsonIsValid`, `JsonHasLeadingWhitespace` (P7), `JsonReplyIsMissingNullOrEmpty`, `JsonIsMalformed`, `JsonHasTrailingText` (P5), `JsonCorrectionsIsNotArray`, `JsonReplyHasNoFinalQuestion`, `JsonHasThreeCorrections` (log), `JsonHasNoCorrectionsAndPraise`, `KeepsRawTextAndIsNotRawFallback` (entrada JSON, `json-valid.json`, S11) (10 métodos, todos com entrada que começa com `{` após `Trim()`). Green: detecção + caminho JSON + log dos avisos. **Estado do caminho não-JSON ao fim da T6:** o ramo "senão → tags" chama um método privado `ParseTags(string rawText)` cujo corpo é só `throw new NotImplementedException("Tag format: implemented in T7.")`; o Green da T6 não monta candidato nem chama o validador nesse ramo. Assim `JsonHasLeadingWhitespace` só passa se a detecção fizer `Trim()` antes de olhar o `{` (sem o `Trim`, cai no ramo de tags e lança). Os `[GeneratedRegex]` das tags ainda não existem na T6. (commits: `test(infra): parse canonical json tutor reply` + `feat(infra): parse json tutor reply with shared validation`)
- [ ] T7 — Red: testes de tags e fora do formato: `TagsAreValid`, `TagsCorrectionLineVaries`, `TagsHaveLabeledFeedbackLines`, `TagsRepeatLabeledLine` (P9), `TagsHaveNoFeedbackBlock`, `TagsReplyHasNoFinalQuestion`, `TextHasNoJsonNorTags`, `JsonIsInFenceOrAfterText` (P5) (8 métodos). Todos passam pelo ramo de tags, então no Red todos falham com a `NotImplementedException` de `ParseTags` deixada pela T6, inclusive `TextHasNoJsonNorTags` e `JsonIsInFenceOrAfterText` (nenhum é teste de regressão). Antes do Green, o Executor confere no Red que a falha de cada um é essa exceção e não outra. Green: caminho tags com `[GeneratedRegex]` (métodos `static partial` na própria `TutorReplyParser`), substituindo o `throw`. Refactor: extrair a leitura de linhas de feedback em método privado. (commits: `test(infra): parse tagged tutor reply` + `feat(infra): parse tagged tutor reply`)
- [ ] T8 — DoD: rodar D1–D11, colar em §11, atualizar §3/§5 com os commits (AP-005). (commit: `docs(ai): add DoD evidence for US-002`)

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| Microsoft.Extensions.Logging.Abstractions (10.0.13, novo `PackageVersion`) | Infrastructure | `ILogger<T>` e gerador `[LoggerMessage]` no parser |
| xunit.v3, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FluentAssertions 7.2.2, NSubstitute, coverlet.collector (versões já no CPM) | Infrastructure.Tests | Mesma pilha dos outros projetos de teste |

Nenhum pacote novo no Core (continua só `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions`; `System.Text.Json` e `System.Text.RegularExpressions` são do shared framework e ficam na Infrastructure).

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Fixtures sintéticas não refletem saídas reais (texto antes do JSON, cerca markdown) | Escopo explícito (§8, P5); ressalva "a partir da US-004" na ARCHITECTURE §10 e na `tdd-dotnet` (§2.4); US-004/US-006 gravam saídas reais e acrescentam fixtures; o parser rejeita com mensagem clara, e a retentativa (US-003) corrige |
| Regra "termina com `?`" rígida demais (ex.: `?"` no fim) gera retentativas | Medir no `eval` (US-009); revisão prevista no ADR-005 |
| Seta `→` (U+2192) exibida errada no console do Windows | Não afeta esta US (sem console); registrar para a US-004 conferir `Console.OutputEncoding` no D6 dela. A seta é visível, não entra na regra de escape do AP-007 |
| Arquivo novo salvo com BOM, zero-width ou espaço sem quebra por ferramenta de edição | `File.ReadAllText` tolera BOM; o D11 procura os bytes EF BB BF, E2 80 8B e C2 A0 no diff e nos arquivos de `src`/`tests`, independente do locale |
| Fixtures não copiadas para a saída (teste falha com `FileNotFoundException`) | `None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest"` na T1; `Fixture.Read` diz o caminho procurado na mensagem |
| `MemberNotNullWhen` gerar CS8775 | Decidido em §2.1: `IsValid` é auto-propriedade armazenada, sem corpo para o compilador analisar. O build do esqueleto na T2 confirma; se mesmo assim houver aviso, o Executor para e registra no §12 antes de mudar a forma (AP-009) |
| Cobertura do Core: tipos só de dados sem teste direto | Todos são exercitados pelos testes do validador/formatador/rejeições; D3b confere ≥ 80% |

## 8. Fora de escopo

- `ITutorBrain`, `BrainRequest`, `BrainMessage`, `BrainException`/`BrainErrorKind` (US-003/US-004).
- Retentativa com instrução de correção e `IsRawFallback = true` (US-003); esta US só entrega as mensagens que ela usará.
- Leitura de `structured_output` do Claude Code e do `format` do Ollama, fixtures gravadas de execuções reais (US-004/US-006).
- **Extrair** o JSON de dentro de cerca markdown ou de texto em volta (P5). A **rejeição** desses casos, com motivo e `Expected` na mensagem, está no escopo e tem teste (`Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson`, `Parse_WhenJsonIsInFenceOrAfterText_RejectsWithMissingReply`).
- Regras não verificáveis com segurança: 1–3 frases, "exatamente uma" pergunta, reply sem correções, praise obrigatório sem erros (RN-FEED03), uma só sugestão de vocabulário (RN-FEED05), dicas de pronúncia vinculadas às palavras de baixa confiança (RN-FEED04) — continuam no `prompts/tutor.md`.
- Exibição do feedback no console e `AddEnglishTutorInfrastructure` (US-004).
- JSON Schema do `TutorReply` usado por `--json-schema`/`format` (US-004/US-006).

## 9. Perguntas em aberto

Respostas padrão de P1–P9 aceitas pelo usuário em 2026-10-17 (§12).

- **P1 — Onde ficam as regras de conteúdo?** A ARCHITECTURE §6.2 diz "o parser valida"; a R4 proíbe regra na Infrastructure. **Padrão proposto:** regras no Core (`TutorReplyValidator`), formato no parser da Infrastructure (ADR-005, ajuste de texto na §6.2 em §2.4).
- **P2 — O que é "o feedback exibido" do cenário "sem erros"?** **Padrão proposto:** as linhas de `FeedbackFormatter.Format` (Core), que o console da US-004 vai imprimir; o teste confere a linha `Praise: Great use of the present perfect!`. Alternativa: formatar só no console (US-004) e o cenário verificar apenas `TutorReply.Praise`.
- **P3 — Correção sem `original` ou `corrected`.** **Padrão proposto:** descartar com aviso no log, sem rejeitar a resposta (o reply ainda serve). Alternativa: rejeitar com novo motivo `invalid correction`.
- **P4 — Sem correções e sem `praise` (RN-FEED03).** **Padrão proposto:** aceitar sem aviso; a regra fica no prompt. Alternativa: aviso no log. Teste: `TutorReplyValidatorTests.Validate_WhenNoCorrectionsAndNoPraise_IsValidWithoutWarnings` (T3).
- **P5 — JSON com texto em volta ou em cerca ```` ```json ````.** **Padrão proposto:** não tratar agora; decidir na US-006 com saída real do Ollama. Resultado fixado por teste até lá: texto depois do objeto → `invalid JSON`; cerca markdown ou texto antes do `{` → `missing reply`; nos dois casos a `Message` contém `Expected`. Testes: `TutorReplyParserTests.Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson` (T6) e `TutorReplyParserTests.Parse_WhenJsonIsInFenceOrAfterText_RejectsWithMissingReply` (T7). Quando a US-006 mudar a decisão, esses testes mudam junto.
- **P6 — Critério de "termina com pergunta".** **Padrão proposto:** último caractere de `Reply.Trim()` é `?`. Não verificar "exatamente uma" pergunta nem número de frases (o modo listening-practice permite reply mais longo, `prompts/tutor.md`). Testes: `TutorReplyValidatorTests.Validate_WhenReplyDoesNotEndWithQuestion_RejectsWithQuestionReason` e `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed`.
- **P7 — O parser recebe o `OutputMode`?** **Padrão proposto:** não; detecta pelo primeiro caractere não branco (`{` → JSON, senão tags). Teste: `TutorReplyParserTests.Parse_WhenJsonHasLeadingWhitespace_DetectsJson` (T6).
- **P8 — Como verificar o "aviso no log"?** **Padrão proposto:** `RecordingLogger<T>` escrito à mão em `Infrastructure.Tests/Support`, conferindo nível `Warning`, EventId 2000 e a mensagem. Alternativa: pacote `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger`). Teste: `TutorReplyParserTests.Parse_WhenJsonHasThreeCorrections_KeepsFirstTwoAndLogsWarning`.
- **P9 — Linhas repetidas de `Pronunciation:`/`Vocabulary:`/`Praise:` nas tags.** **Padrão proposto:** unir com `"; "` (o limite de 1 vocabulário da RN-FEED05 não é aplicado pelo parser; ver §8). Teste: `TutorReplyParserTests.Parse_WhenTagsRepeatLabeledLine_JoinsValuesWithSemicolon` (T7).

## 10. Definition of Done (comandos verificáveis)

Executar a partir da raiz do repositório, em PowerShell (exceto D11, em Bash). Esta US não altera o console, então não há comando com pipe no Windows PowerShell 5.1 (AP-006 não se aplica).

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros (inclui ausência de CS8775 em `TutorReplyParseResult`) |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes (93 casos: Core.Tests 47, Console.Tests 22, Infrastructure.Tests 24), em < 30 s, sem rede nem GPU |
| D3 | `Remove-Item -Recurse -Force TestResults, coverage -ErrorAction SilentlyContinue; dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` | gera `coverage.cobertura.xml` para os 3 projetos de teste |
| D3b | `dotnet tool restore; dotnet reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:TextSummary; Get-Content coverage/Summary.txt` | linha `EnglishTutor.Core` ≥ 80% (registrar também `EnglishTutor.Infrastructure`, informativo) |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test --filter "FullyQualifiedName~TutorReplyParser"` | verde: 18 métodos / 24 casos, incluindo as fixtures JSON e tags e os casos de borda de P5, P7 e P9 |
| D6 | `dotnet test --filter "FullyQualifiedName~TutorReplyRejectionsTests"` + colar nas Evidências as 4 mensagens geradas (uma por motivo) | verde; cada mensagem começa com o motivo, diz o problema concreto e contém `Expected ...` (base da instrução de correção da RN-BRAIN04) |
| D7 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"`; `dotnet list src/EnglishTutor.Core package` | 3 testes verdes; só `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions` |
| D8 | `dotnet list src/EnglishTutor.Infrastructure package` | só `Microsoft.Extensions.Logging.Abstractions` |
| D9 | `dotnet sln EnglishTutor.slnx list` | inclui `tests/EnglishTutor.Infrastructure.Tests/EnglishTutor.Infrastructure.Tests.csproj` |
| D10 | `git status --short` após D2–D5 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/` ou `obj/` aparece |
| D11 | (Bash, Git Bash) os dois comandos do bloco abaixo, com todo o código da US já commitado (rodar na T8 antes do commit de evidências) | D11a imprime `0`; D11b não imprime nada (nenhum BOM, zero-width space ou espaço sem quebra literal; AP-007) |

D11 (fica fora da tabela por causa do `|`):

```bash
# D11a — mesma verificação do item de caracteres invisíveis do checklist-revisao §4 (diff inteiro da branch)
git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'
# D11b — arquivos de src/ e tests/ (pega também arquivo ainda não rastreado); lista quem tiver os bytes
LC_ALL=C grep -rl -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0' src tests --include=*.cs --include=*.csproj --include=*.json --include=*.txt --exclude-dir=bin --exclude-dir=obj
```

Por que essa forma (S1): `$'\xef\xbb\xbf'` é expandido pelo Bash em bytes crus antes de chegar ao `grep`, como no `checklist-revisao` §4 e no AP-007 (`$'\xef\xbb\xbf'`). O `LC_ALL=C` faz o `grep` comparar byte a byte, independente do locale UTF-8 do Git Bash. Com `grep -P '\xEF'`, o `\xEF` vira o código U+00EF e não o byte. Os três padrões cobrem BOM/U+FEFF (EF BB BF), zero-width space (E2 80 8B) e espaço sem quebra (C2 A0). O `grep -c` do D11a sai com código 1 quando conta 0: isso é o resultado esperado, não falha.

E também (DoD comum): todo cenário BDD com teste escrito antes do código; `review.md` sem bloqueantes; `docs/us/US-002.md` criado; ADR-005 aceito, `ARCHITECTURE.md` §3/§4/§6.2/§10 e `tdd-dotnet` (linhas 28 e 90) atualizadas antes da T1 (§2.4); merge `--no-ff` em `develop`.

## 11. Evidências

_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário

_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-17 — pedido do Revisor do plano (rodada 1, `plan-review.md`)

- **B1** — P4, P5, P7 e P9 definem comportamento observável sem teste na §4: união com `"; "` de linhas repetidas do mesmo rótulo nas tags (P9); JSON em cerca ```` ```json ```` ou com texto em volta rejeitado com motivo e mensagem claros (P5); detecção pelo primeiro caractere não branco, com espaço/quebra antes do `{` (P7); resposta sem correções e sem `praise` aceita sem aviso, de forma explícita (P4). Esperado: cada efeito com teste nomeado na §4 e tarefa na §5, com contagens da §4, D2 e D5 atualizadas.

  → Incorporado em §2.2, §2.3, §4, §5 (T3, T4, T6, T7), §9 (P4, P5, P7, P9, cada um agora termina com "Teste: ...") e §10 (D2, D5). Testes novos (5 métodos, 7 casos):
  - P9: `TutorReplyParserTests.Parse_WhenTagsRepeatLabeledLine_JoinsValuesWithSemicolon` (`[Fact]`, T7). Os rótulos variam de maiúscula (`Vocabulary:`/`vocabulary:`), então o mesmo teste fixa também a comparação sem diferenciar maiúsculas.
  - P5: dividido em dois métodos porque os dois casos vão por ramos diferentes. `Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson` (`[Fact]`, T6; começa com `{`, `JsonDocument` lança) e `Parse_WhenJsonIsInFenceOrAfterText_RejectsWithMissingReply` (`[Theory]` com 2 casos, T7; não começa com `{`, cai nas tags sem `<reply>`). Os dois conferem `Reason` e que a `Message` contém `Expected`.
  - P7: `Parse_WhenJsonHasLeadingWhitespace_DetectsJson` (`[Theory]` com prefixos `"  "` e `"\r\n\t"`, T6). Com o estado do ramo de tags definido em B2, o teste só passa se a detecção fizer `Trim()`.
  - P4: `TutorReplyValidatorTests.Validate_WhenNoCorrectionsAndNoPraise_IsValidWithoutWarnings` (`[Fact]`). Fica na T3 e não na T4: depois do Green da T3 ele já passaria, então na T4 nasceria verde. Na T4 continua como regressão. Também deixei explícito o candidato de `ReturnsValidReplyWithoutWarnings` (1 correção completa, `Praise = null`).
  - Contagens: §4 passa a 31 métodos / 44 casos (Core.Tests 13 / 20; Infrastructure.Tests 18 / 24). D2 passa a 93 casos (47 + 22 + 24) e D5 a 18 métodos / 24 casos. T3+T4 = 8 métodos de validador, T6+T7 = 10+8 = 18 métodos de parser. Nenhuma fixture nova (os casos de borda são strings inline, §2.3).

- **B2** — §5, T6 → T7: dizer o estado do caminho não-JSON (tags) ao fim da T6, de modo que todos os testes da T7 (inclusive `Parse_WhenTextHasNoJsonNorTags_RejectsWithMissingReply`) falhem no Red pelo motivo certo; ou registrar e justificar que aquele teste é de regressão.

  → Incorporado em §5 (T6 e T7). Ao fim da T6, o ramo "senão → tags" chama `ParseTags(string rawText)`, que só lança `NotImplementedException("Tag format: implemented in T7.")`. Nesse ramo não se monta candidato nem se chama o validador. Os 8 testes da T7 passam todos pelo ramo de tags, então falham no Red com essa exceção. Isso inclui `TextHasNoJsonNorTags` e o novo `JsonIsInFenceOrAfterText`. Nenhum deles é de regressão, e o Executor confere o motivo da falha antes do Green. O Green da T7 troca o `throw` pela implementação.

### 2026-10-17 — pedido do usuário (no chat, após a rodada 2 da revisão do plano)

- Incorporar as sugestões S1, S2, S3, S5, S7, S8, S11 e S12 de `plan-review.md` (rodadas 1 e 2). As demais (S4, S6, S9, S10, S13) não foram pedidas.
- Respostas padrão de P1–P9 aceitas como estão.

  → Incorporado. Onde entrou cada sugestão:
  - **S1** (D11 não detectava os bytes) → §10, D11 e bloco logo abaixo da tabela; §7, linha do BOM. O D11 virou dois comandos Bash: D11a é a mesma forma do `checklist-revisao` §4 (`git diff develop...HEAD | grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'`, esperado `0`) com `LC_ALL=C` na frente. D11b procura os mesmos bytes em `src`/`tests` (`*.cs`, `*.csproj`, `*.json`, `*.txt`) e não deve listar nada. `$'...'` gera os bytes crus, igual ao AP-007, e `LC_ALL=C` tira a dependência do locale. O `grep -P '\xEF\xBB\xBF'` antigo saiu.
  - **S2** (CS8775 em `IsValid`) → §2.1 (código e parágrafo "Forma de `TutorReplyParseResult`"), alternativas descartadas, §3, §5 T2, §7 e D1. Decisão: `IsValid` é auto-propriedade só com `get`, atribuída no construtor privado (`IsValid = reply is not null`), com os dois `MemberNotNullWhen`. As fábricas `Valid`/`Invalid` são o único caminho de criação. `TryGet...` foi descartado. O build do esqueleto na T2 confirma que não há CS8775. Se houver, o Executor para e registra no §12 (AP-009), sem escolher outra forma por conta própria.
  - **S3** (ramo "raiz não-objeto" morto) → §2.2, caminho JSON (o ramo saiu, com a justificativa), §2.1 (a tabela de mensagens de `invalid JSON` agora só traz o detalhe da `JsonException`, sem o exemplo "the root is an array") e alternativas descartadas ("Detectar `[` como JSON"). Nenhum teste muda: nenhum cobria esse ramo.
  - **S5** (desvio das "fixtures reais") → §2.3 (desvio declarado) e §2.4. Textos novos propostos para `ARCHITECTURE.md:241` (§10) e para `.claude/skills/tdd-dotnet/SKILL.md:28` e `:90`, com a ressalva "a partir da US-004; a US-002 usa exemplos da spec em `Fixtures/TutorReply/`". O Grep não achou a regra em `CLAUDE.md` nem no `checklist-revisao`. Também atualizei §3 (linha nova da `tdd-dotnet`), §5 (pré-condição), §7 (risco das fixtures sintéticas) e a DoD comum.
  - **S7** (dois pontos de aplicação da ARCHITECTURE) → título e primeiro parágrafo da §2.4, pré-condição da §5, §3 e DoD comum. Agora há um ponto só: o orquestrador aplica tudo na aprovação, antes da T1, num commit `docs(ai)`. O "antes da T2" saiu.
  - **S8** (`partial`) → §2.2 (assinatura `public sealed partial class TutorReplyParser`, exemplo de `[GeneratedRegex]` `static partial` e uma nota), §3 e §5 (T6 e T7).
  - **S11** (entrada de `KeepsRawTextAndIsNotRawFallback`) → §4 (a entrada é o JSON da fixture `json-valid.json`; `RawText` igual ao texto lido e `IsRawFallback == false`) e §5 T6 (os 10 testes da T6 têm entrada que começa com `{`). Não acrescentei teste de texto cru para tags, porque a S11 o deixa como opcional e mudaria as contagens.
  - **S12** (§8 sobre P5) → §8. O item agora separa o que fica fora (**extrair** o JSON da cerca ou do texto em volta) do que fica dentro (a **rejeição** com motivo e `Expected`, com os dois testes citados).
  - Respostas de P1–P9 aceitas → nota no início da §9. O texto das P* não mudou.
  - Contagens sem alteração: §4 continua com 31 métodos / 44 casos, §5 com T3+T4 = 8 e T6+T7 = 10+8 = 18, D2 com 93 casos (47 + 22 + 24) e D5 com 18 métodos / 24 casos. Nenhuma sugestão aplicada acrescentou ou removeu teste. S4, S6, S9, S10 e S13 não foram aplicadas, como pedido.
  - Status continua "Aguardando aprovação".

### 2026-10-17 — aprovação do usuário (no chat)

- Plano aprovado com as sugestões S1, S2, S3, S5, S7, S8, S11 e S12 incorporadas (rodada 3 da revisão do plano: APROVADO, sem bloqueantes).
- Respostas às perguntas em aberto: P1–P9 com as respostas padrão.
- ADR-005 → Aceito. ADR-002 continua Proposto (confirmação prevista para US-004/US-009, fora desta US).
- Alterações da §2.4 aplicadas em `ARCHITECTURE.md` (§3, §4, §6.2, §10) e em `.claude/skills/tdd-dotnet/SKILL.md` (linhas 28 e 90) no commit `docs(ai): update architecture for US-002`, antes da T1.
- Sugestão S14 (rodada 3) não incorporada ao texto; nota para o Executor: na D11b, código de saída 1 do `grep -rl` sem saída é o resultado esperado, não falha.
