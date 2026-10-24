# Plano — US-009 Avaliação comparativa dos cérebros

Status: Aprovado em 2026-10-23
Branch: feature/US-009_avaliacao-comparativa-cerebros
Spec: specs/sprints/SPRINT_1.md#us-009
Regras: PRODUCT (métricas de sucesso); RN-BRAIN04, RN-BRAIN07 (tocadas, sem mudança); RN-FEED01, RN-FEED02 (consumidas); ARCHITECTURE §2 (R1, R4), §3, §6.5, §10; ADR-006, ADR-010 (consumidos); ADR-011 (novo, Aceito)

## 1. Objetivo

Um comando `EnglishTutor.Console eval --providers ClaudeCode,Ollama` que roda um golden set de ~30 frases com erros conhecidos por provedor, mede correções esperadas encontradas, formato válido (sem e com a retentativa), latência média e p95, marca provedor fora do ar como `unavailable` e grava um relatório markdown em `eval/reports/`, com a nota mínima (`met`/`not met`) que as US de cérebro seguintes usam na DoD.

## 2. Abordagem

Decisão técnica relevante nova: **ADR-011** (`docs/adr/ADR-011-avaliacao-comparativa-logica-no-core-e-criterio-de-correcao.md`, Status: Aceito). Resumo do que ele fixa e os pontos de detalhe que o plano acrescenta.

### 2.1 Onde mora cada coisa

- `EnglishTutor.Core/Evaluation` (novo, BCL apenas, `System.Text.Json` incluído): golden set e seu leitor, normalização de palavras, comparação de correções, `EvalRunner`, métricas, nota mínima, montagem do relatório. É regra de negócio (R4), testada com fakes, e entra na meta de cobertura do Core.
- `EnglishTutor.Console/Commands` (novo): `EvalCommandLine` + `EvalCommandLineParser` (argumentos; o `EvalCommandLineParseResult` mora no mesmo arquivo do parser) e `EvalCommand` (lê o arquivo, resolve os cérebros por chave, grava o relatório, escreve na tela). `EnglishTutorApp` passa a escolher entre o chat e o `eval` depois de a partida validar a configuração.
- `EnglishTutor.Infrastructure`: **não muda** (as chaves de `ITutorBrain` por provedor já existem, ADR-010). Guarda: D12.
- `eval/golden-set.json` (dados, rascunho em §2.9, revisado pelo usuário) e `eval/README.md` (formato do arquivo, como o critério casa, como ampliar `accepted`, como rodar).

Alternativas descartadas (detalhe no ADR-011): juiz por outro modelo; casar pelo `original` da resposta; igualdade exata do `Corrected`; lógica no Console; projeto `EnglishTutor.Eval`; `BrainFactory.Create(provider)` público.

### 2.2 Golden set (formato e validação)

```json
{
  "reviewedBy": "",
  "sentences": [
    { "id": "gs-001", "text": "I have worked with C# since five years",
      "expected": [ { "original": "since five years", "accepted": ["for five years"] } ] }
  ]
}
```

Tipos do Core: `ExpectedCorrection(string Original, IReadOnlyList<string> Accepted)`, `GoldenSentence(string Id, string Text, IReadOnlyList<ExpectedCorrection> Expected)`, `GoldenSet(string ReviewedBy, IReadOnlyList<GoldenSentence> Sentences)` (com `ExpectedTotal`), `GoldenSetParseResult(GoldenSet? Set, IReadOnlyList<string> Errors)` (`IsValid` = sem erros). `GoldenSetParser.Parse(string json)` coleta **todos** os erros (não só o primeiro), em inglês, cada um dizendo o que está errado e o que se espera. Nomes de propriedade em camelCase e sensíveis a maiúsculas (`Sentences` não vale como `sentences`: o erro aparece como "missing"); propriedade desconhecida é ignorada (P15). `reviewedBy` ausente ou `null` vale `""` (revisão pendente).

| # | Condição | Mensagem (`{x}` é derivado) | Teste (§4), um por ramo |
|---|---|---|---|
| G1 | JSON malformado | `The golden set is not valid JSON: {detail}. Expected an object like {"reviewedBy": "<name>", "sentences": [...]}.` (`{detail}` vem da `JsonException`; o teste confere só o trecho `Expected an object like`, AP-015) | P3 |
| G2 | Raiz não é objeto | `The golden set must be a JSON object (found {kind}). Expected {"reviewedBy": "<name>", "sentences": [...]}.` | P4 (array) |
| G3 | `sentences` ausente, vazio ou de outro tipo | `"sentences" must be a non-empty array (found: {missing \| 0 items \| kind}). Expected at least one sentence.` | P4 (ausente, `[]`, string), P15 (`Sentences`) |
| G4 | `id` ausente, em branco ou de outro tipo | `Sentence {ref}: "id" must be a non-empty string (found: {missing \| blank \| kind}).` com `{ref}` = `'gs-001'` quando há id, ou `#3` (posição, base 1) quando não há | P5 (ausente, em branco), P13 (número) |
| G5 | `id` repetido (sem diferenciar maiúsculas) | `Sentence '{id}': "id" is repeated (first used by sentence #{n}). Expected unique ids.` | P6 |
| G6 | `text` ausente, em branco ou de outro tipo | `Sentence '{id}': "text" must be a non-empty string (found: {missing \| blank \| kind}).` | P7 (ausente, em branco), P13 (booleano) |
| G7 | `expected` fora de 1 a 2 itens, ou de outro tipo | `Sentence '{id}': "expected" must be an array of 1 or 2 items (found: {kind \| n items}). Expected one entry per correction, at most 2 (RN-FEED02).` | P8 (0 e 3 itens), P13 (objeto) |
| G8 | `original` ausente, em branco ou de outro tipo | `Sentence '{id}': expected[{i}].original must be a non-empty string (found: {missing \| blank \| kind}).` | P14 (ausente, em branco), P13 (`null`, array) |
| G9 | `original` não está no texto | `Sentence '{id}': expected[{i}].original "{original}" is not in the sentence text. Expected a fragment of the text.` | P9 |
| G10 | `accepted` ausente, vazio ou de outro tipo | `Sentence '{id}': expected[{i}].accepted must be an array of non-empty strings (found: {missing \| 0 items \| kind}).` | P11 (ausente, `[]`), P13 (texto) |
| G11 | item de `accepted` em branco ou sem palavras | `Sentence '{id}': expected[{i}].accepted[{j}] "{item}" has no words. Expected a corrected fragment.` | P11 (`"  "`, `"..."`) |
| G12 | item de `accepted` já está no texto | `Sentence '{id}': expected[{i}].accepted[{j}] "{item}" is already in the sentence text. Expected a fix that the student did not say.` | P10 |

`{kind}` ∈ `a string`, `a number`, `a boolean`, `null`, `an object`, `an array`. "Está no texto" = sequência contígua de palavras normalizadas (§2.3). O parser não exige 30 frases (testes e usuários podem ter arquivos menores); o mínimo de 30 é guardado por `GoldenSetFileTests` (D7).

### 2.3 Critério de "correção esperada encontrada" (ADR-011 §3)

Normalização de palavras (`EvalText.Tokens`): (1) `‘` e `’` viram `'`; `“` e `”` viram `"`; (2) divide em qualquer espaço em branco (espaço, tab, quebra de linha); (3) tira de cada borda do pedaço os caracteres `. , ; : ! ? " ' ( ) [ ] { }`; (4) descarta pedaços que ficaram vazios; (5) `ToLowerInvariant`. Símbolos internos (`c#`, `don't`, `e-mail`) ficam. `EvalText.ContainsWords(text, fragment)` = as palavras do fragmento (ao menos uma) aparecem na mesma ordem e **lado a lado** nas palavras do texto; fragmento sem palavras devolve falso (E6).

`CorrectionMatcher.IsFound(expected, actual)`: verdadeiro se alguma `Correction` de `actual` tem `Corrected` que contém (`ContainsWords`) alguma alternativa de `expected.Accepted`. `Original` e `Explanation` **não** entram. `CountFound(expectedList, actual)` conta cada esperada de forma independente: uma única `Correction` da resposta pode satisfazer as duas esperadas (M8, caso 4).

Caracteres especiais (`’` etc.) aparecem sempre como escape no código e nos testes (AP-007).

### 2.4 Execução por provedor (`EvalRunner`)

`EvalRunner(ITutorPromptSource promptSource, IOptions<ConversationOptions> options, ILoggerFactory loggerFactory, TimeProvider timeProvider)`; `Task<ProviderEvaluation> EvaluateAsync(string provider, ITutorBrain brain, GoldenSet set, CancellationToken ct)`. Registrado em `AddEnglishTutorCore` (`TryAddSingleton`).

Para cada frase, na ordem do arquivo:

1. Cria um contador de chamadas em volta do cérebro (`Name` repassa o nome do cérebro; guarda quantas chamadas houve e se a **primeira** devolveu um `TutorReply`) e um `ConversationService` novo sobre ele (janela vazia; modo padrão `free-conversation`).
2. `t0 = timeProvider.GetTimestamp()`; `result = await service.SendAsync(sentence.Text, ct)`; latência = `GetElapsedTime(t0)` (inclui a retentativa).
3. `SentenceResult(Id, ExpectedCount, FoundCount = CountFound(...), Attempts, ValidFirstAttempt, ValidAfterRetry = !reply.IsRawFallback, Latency, Failure: null)`. Resposta crua (`IsRawFallback`) tem `Corrections` vazias, logo `FoundCount = 0`.
4. `BrainException` em qualquer ponto do passo 2: se for a **primeira** frase, devolve `ProviderEvaluation(provider, Unavailable, motivo = ex.Message, [])` e **para**; se for posterior, registra `SentenceResult(..., FoundCount: 0, ValidFirstAttempt: false, ValidAfterRetry: false, Latency: null, Failure: ex.Message)` e continua. `InvalidOutput` nunca escapa do `ConversationService` (vira resposta crua), então a condição só vê os outros cinco tipos. `OperationCanceledException` propaga sempre.

Tipos: `ProviderStatus { Evaluated, Unavailable }`; `SentenceResult(string Id, int ExpectedCount, int FoundCount, int Attempts, bool ValidFirstAttempt, bool ValidAfterRetry, TimeSpan? Latency, string? Failure)`; `ProviderEvaluation(string Provider, ProviderStatus Status, string? UnavailableReason, IReadOnlyList<SentenceResult> Results)`.

### 2.5 Métricas e nota mínima

`EvalMetrics.From(results)`: `SentenceCount`, `ExpectedTotal`, `FoundTotal`, `ValidFirstAttemptCount`, `ValidAfterRetryCount`, `LatencyCount`, `MeanLatency`, `P95Latency`. Latência: só frases com `Latency` não nula (frases com erro ficam fora da latência e **dentro** das taxas). Média = soma de ticks ÷ quantidade (divisão inteira). p95 pelo posto mais próximo, com inteiros: `posto = (95 × n + 99) / 100`, valor = `ordenados[posto − 1]`; sem latências, zeros.

`EvalThresholds`: `MinimumFoundPercent = 80`, `MinimumValidFormatPercent = 90` (PRODUCT e D6). `IsMet(found, expected, validFirstAttempt, sentences)` = `expected > 0 && sentences > 0 && found × 100 ≥ 80 × expected && validFirstAttempt × 100 ≥ 90 × sentences`. Só o formato **sem** retentativa entra (PRODUCT: "sem retentativa"); a taxa com retentativa é informativa. Com 32 frases: 26 de 32 correções (81,3%; 25 = 78,1% reprova) e 29 de 32 no formato (90,6%; 28 = 87,5% reprova).

### 2.6 Relatório, tela e catálogo de textos

`EvalReportBuilder.Build(DateTimeOffset generatedAtUtc, string goldenSetPath, GoldenSet set, IReadOnlyList<ProviderEvaluation> providers)` devolve o markdown (linhas separadas por `\n`, terminando em `\n`); `EvalReportBuilder.SummaryTable(providers)` devolve as linhas da tabela de resumo (a tela reaproveita). Layout:

```
# EnglishTutor eval report

- Generated (UTC): 2026-10-23 14:30:05
- Golden set: <path> (<n> sentences, <m> expected corrections)
- Golden set review: reviewed by <name>        | pending (reviewedBy is empty)
- Minimum grade: corrections found >= 80% and valid format without retry >= 90%

## Summary

| Provider | Status | Corrections found | Valid format (no retry) | Valid format (with retry) | Mean latency | p95 latency | Minimum grade |
|---|---|---|---|---|---|---|---|
| ClaudeCode | evaluated | 66.7% (2/3) | 100.0% (3/3) | 100.0% (3/3) | 2000 ms | 2000 ms | not met |
| Ollama | unavailable | - | - | - | - | - | - |

## Unavailable providers          (só quando houver)

- Ollama: <motivo>

## Details: ClaudeCode            (um por provedor avaliado, na ordem pedida)

| Id | Corrections found | Valid format (no retry) | Valid format (with retry) | Latency | Note |
|---|---|---|---|---|---|
| gs-001 | 1/1 | yes | yes | 2000 ms |  |
```

| Transformação | Regra | Teste (§4) |
|---|---|---|
| Percentual | `100,0 × parte ÷ total`, uma casa, `CultureInfo.InvariantCulture`, sufixo `%`; seguido de `(parte/total)` | B2 (cultura pt-BR), B3 (1/3 → `33.3%`, 2/3 → `66.7%`) |
| Milissegundos | arredonda para inteiro com `MidpointRounding.AwayFromZero`; `<n> ms`; sem latência: `-` | B4 (1000,5 ms → `1001 ms`; 1000,33 ms → `1000 ms`) |
| Revisão | `reviewed by <nome>` ou `pending (reviewedBy is empty)` | B5 |
| Nota | `met` / `not met` por `EvalThresholds.IsMet`; `-` para `unavailable` | B7 |
| Nota da linha de detalhe | `Note` = `retry used` (primeira inválida, final válida), `raw fallback after retry` (crua) ou a mensagem da falha; células escapam `\|` e trocam quebras de linha por um espaço; o motivo da lista de indisponíveis troca quebras de linha por um espaço | B6, B8, B1 |
| Provedor `unavailable` | linha do resumo com `-`; item na lista; **sem** seção `Details` | B1, B9 |

Tela (saída padrão), em ordem: para cada provedor, `Evaluating <provedor> (<n> sentences)...` antes de rodar; depois as linhas de `SummaryTable`; por último `Report saved to <caminho absoluto>`. Relatório: `<reports-dir>/eval-<yyyyMMdd-HHmmss>.md` (UTC, `TimeProvider.GetUtcNow()`), UTF-8 sem BOM; a pasta é criada se não existir. Duas rodadas no mesmo segundo se sobrescreveriam (limite L5).

Mensagens do console (saída de erro, em inglês), em `ConsoleText`: `Invalid eval arguments:` + `  <erro>` + `Usage: eval [--providers <name[,name...]>] [--golden-set <file>] [--reports-dir <folder>]`; `Golden set not found: <caminho>. Expected a JSON file with the sentences to evaluate (default: eval/golden-set.json).`; `Invalid golden set (<caminho>):` + uma linha `  <erro>` por problema; `Could not save the report to <pasta>: <motivo>`. Os critérios observáveis são os mesmos da US-007 (código 2 e saída de erro para configuração inválida, AP-024).

### 2.7 Linha de comando e despacho

`EvalCommandLineParser.IsEvalCommand(args)`: o **primeiro** argumento é `eval` (sem diferenciar maiúsculas). `Parse(args)` devolve `EvalCommandLineParseResult(EvalCommandLine? Value, string? Error)` (declarado em `EvalCommandLineParser.cs`) e para no **primeiro** erro:

- `--providers`: consome os tokens seguintes até o próximo que comece com `--`; cada token é dividido por `,`; vazios descartados; nomes normalizados por `BrainProviders.TryGetCanonical`; duplicados mantidos uma vez, na ordem; sem nenhum nome: erro. Aceita as duas formas porque o **Windows PowerShell passa `ClaudeCode,Ollama` sem aspas como dois argumentos** (`ClaudeCode` e `Ollama`), enquanto o Bash passa um só.
- `--golden-set <arquivo>` e `--reports-dir <pasta>`: exatamente um valor (que não comece com `--`).
- Padrões: todos os provedores de `BrainProviders.All`, `eval/golden-set.json`, `eval/reports` (relativos ao diretório atual; `dotnet run` roda a partir de onde foi chamado).
- Erros: `Unknown provider '<x>'. Accepted values: ClaudeCode, Ollama.`; `Unknown option '<x>'. Valid options: --providers, --golden-set, --reports-dir.`; `Option <x> needs a value.`; `Option <x> was given more than once.`; `Unexpected argument '<x>'. Valid options: --providers, --golden-set, --reports-dir.`

`EnglishTutorApp`: depois de `host.StartAsync` (que valida a configuração: configuração inválida continua saindo com 2 antes de qualquer coisa), `RunSessionAsync` vira um despacho: `IsEvalCommand(options.Args)` → `EvalCommand.RunAsync`; senão o chat de hoje, sem mudança. `EvalCommand.RunAsync` na ordem: argumentos (erro → saída de erro, retorna `ExitCodes.InvalidConfiguration`); arquivo (ausente ou inválido → idem, nenhum relatório); para cada provedor, `GetRequiredKeyedService<ITutorBrain>(provedor)` e `EvalRunner`; relatório gravado (falha de E/S → mensagem acima e `ExitCodes.UnexpectedError`); tela; retorna `ExitCodes.Success`. Ctrl+C durante o `eval` cai no tratamento de cancelamento que já existe (saída 0, sem relatório). A `Brain:FallbackProvider` configurada **não** interfere: o `eval` não usa o `ITutorBrain` sem chave.

Logs (EventIds do console, `AppLog`; os atuais vão de 1000 a 1005): `1006 EvalProviderEvaluated` (Information; `Provider`, `Status`, `Sentences`, `FoundTotal`, `ExpectedTotal`, `ElapsedMs`) e `1007 EvalReportSaved` (Information; `Path`). Texto das frases do aluno não é logado.

`AddCommandLine` do host continua lendo os mesmos argumentos como configuração: `eval` e valores soltos (sem prefixo e sem `=`) são ignorados pelo provedor e `--providers`, `--golden-set`, `--reports-dir` viram chaves sem efeito. Os testes de console usam `Configuration` em memória (que dispensa a linha de comando), então quem prova isso é o D5 real.

### 2.8 Alterações em outros documentos (dois commits do orquestrador antes da T1)

Cada trecho é identificado pelo texto antigo (AP-023). Conferido por Grep antes de fechar (AP-035): `respostas no formato|formato válido|golden set|golden-set|Avaliação comparativa` fora de `tasks/` e de `docs/aprendizados.md` acha, para texto que muda, só `specs/sprints/SPRINT_1.md` (linhas da US-009: 522–543) e `ARCHITECTURE.md` (73, 78–79, 273); os demais achados (`specs/PRODUCT.md` 33–34, `specs/ROADMAP.md` 53, `README.md` 72, `SPRINT_1.md` 3, 16 e 31, `docs/us/US-004.md`, `docs/us/US-006.md`, ADR-002) citam a meta ou a US sem repetir o texto que muda. `Grep "eval|golden|Persistence/|Speech/|BrainFallbackPolicy, BrainSwitchNotice"` em `CLAUDE.md` e `.claude/` acha só `.claude/agents/planejador.md` (modelo de DoD, sem mudança) e `.claude/agents/executor.md` (o escopo de commit `eval` já existe): **nenhuma linha do `CLAUDE.md` nem das skills repete a regra**, então nada a sincronizar além do `ARCHITECTURE.md`. Nenhum cenário é renomeado; nenhuma regra `RN-*` muda.

**Commit 1 — `docs(ai): update spec for US-009`** (`specs/sprints/SPRINT_1.md`, seção `## US-009`):

(a) Regras. Antigo: `**Regras:** PRODUCT (métricas de sucesso).` Novo: `**Regras:** PRODUCT (métricas de sucesso), RN-BRAIN04, RN-BRAIN07; ARCHITECTURE §6.6; ADR-010, ADR-011.`

(b) Cenário "rodar o golden set". Antigo: `  Então para cada provedor vejo: % de correções esperadas encontradas, % de respostas no formato, latência média e p95`. Novo: `  Então para cada provedor vejo: % de correções esperadas encontradas, % de respostas no formato sem retentativa (e, à parte, com a retentativa única), latência média e p95`.

(c) Cenário "provedor indisponível na avaliação". Depois da linha antiga `  Então o relatório marca o Ollama como "unavailable" e os resultados do Claude Code continuam válidos`, acrescentar: `  E o código de saída é 0`.

(d) Três cenários novos, depois do cenário (c) e antes do fecho do bloco gherkin:

```gherkin
Cenário: nota mínima no relatório
  Dado o resultado de um provedor avaliado
  Quando o relatório é gerado
  Então a linha do provedor diz "met" se as correções esperadas encontradas são ≥ 80% e as respostas no formato sem retentativa são ≥ 90%
  E diz "not met" se qualquer um dos dois ficar abaixo

Cenário: argumentos inválidos no eval
  Dado o comando "eval --providers Inexistente"
  Quando executo o comando
  Então a aplicação termina com código 2 e a saída de erro lista os provedores aceitos
  E nenhum relatório é gravado

Cenário: golden set inválido
  Dado um eval/golden-set.json ausente, ou com uma frase sem correção esperada
  Quando executo "EnglishTutor.Console eval"
  Então a aplicação termina com código 2 e a saída de erro diz o que está errado e o que se espera
  E nenhum relatório é gravado
```

(e) DoD extra. Antigo D6: `| D6 | Nota do provedor principal (ClaudeCode) no relatório | correções esperadas encontradas ≥ 80%; formato válido ≥ 90% |`. Novo: `| D6 | Nota do provedor principal (ClaudeCode) nos relatórios de 3 execuções seguidas do comando completo do D5 (com os dois provedores) | em cada uma: a linha do ClaudeCode diz `met` (correções esperadas encontradas ≥ 80%; formato válido sem retentativa ≥ 90%) |`.

Antigo D7: `| D7 | `eval/golden-set.json` | ≥ 30 frases, cada uma com erro e correção esperada revisados pelo usuário |`. Novo: `| D7 | `dotnet test --filter "FullyQualifiedName~GoldenSetFileTests"` e `git grep -n -F '"reviewedBy": ""' -- eval/golden-set.json` | verde (o arquivo tem ≥ 30 frases, cada uma com erro e correção esperada); enquanto o usuário não revisa, o `git grep` acha a linha `"reviewedBy": ""` (código de saída 0); depois que o usuário preenche `reviewedBy` ao revisar as frases, não acha linha (código de saída 1) |`.

Acrescentar depois do D7 (critérios reutilizados da US-007: `exit=` e mensagem na saída de erro):

```
| D8 | No Windows PowerShell 5.1: `$env:Brain__Ollama__Endpoint="http://localhost:1"; dotnet run --project src/EnglishTutor.Console -- eval --providers Ollama; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Ollama__Endpoint` | a linha do Ollama no resumo diz `unavailable` e o relatório lista o motivo; `exit=0` |
| D9 | No Windows PowerShell 5.1: `"/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | o cabeçalho do chat (`Brain: ... \| Mode: free-conversation`); `exit=0` (o despacho do `eval` não quebrou o chat) |
| D10 | No Windows PowerShell 5.1: `dotnet run --project src/EnglishTutor.Console -- eval --providers Inexistente 2>&1; "exit=$LASTEXITCODE"` | a mensagem `Unknown provider 'Inexistente'. Accepted values: ClaudeCode, Ollama.`; `exit=2`; nenhum arquivo novo em `eval/reports/` |
```

(f) Frase depois da tabela. Antigo: `Esses limiares (D6) passam a ser a **nota mínima do eval** na DoD das US de cérebro das sprints seguintes.` Novo: `Esses limiares (D6) passam a ser a **nota mínima do eval** na DoD das US de cérebro das sprints seguintes: a DoD roda `eval --providers <provedor>` e confere que a linha do provedor diz `met`.`

**Commit 2 — `docs(ai): update architecture for US-009`** (`ARCHITECTURE.md`):

1. §3, árvore do Core. Depois da linha `    Audio/             IAudioRecorder, IAudioPlayer, AudioClip`, acrescentar:
   ```
       Evaluation/        GoldenSet, GoldenSentence, ExpectedCorrection, GoldenSetParser, GoldenSetParseResult, EvalText,
                          CorrectionMatcher, EvalRunner, SentenceResult, ProviderEvaluation, ProviderStatus, EvalMetrics,
                          EvalThresholds, EvalReportBuilder
   ```
2. §3, árvore do Console. Antigo: `Commands/ (chat, eval), Ui/`. Novo: `Commands/ (EvalCommand, EvalCommandLine, EvalCommandLineParser com EvalCommandLineParseResult; o chat continua em Ui/), Ui/`.
3. §3, pasta `eval/`. Antigo: `eval/` seguido de `  golden-set.json`. Novo: depois de `  golden-set.json` acrescentar `  README.md` e `  reports/             relatórios gerados (ignorados pelo Git)`.
4. §6, depois da última linha da §6.5 (`- O console implementa o aviso (`Switched to <reserva> (<motivo>)`, saída padrão, antes da linha `Tutor:`) (ADR-010).`), nova seção:
   ```
   ### 6.6 Avaliação comparativa (eval)

   - `EnglishTutor.Console eval [--providers <lista>] [--golden-set <arquivo>] [--reports-dir <pasta>]` (padrões: todos os provedores de `BrainProviders`, `eval/golden-set.json`, `eval/reports`). A lógica mora em `Core/Evaluation` (ADR-011): para cada provedor, cada frase do golden set vai sozinha (janela vazia, modo `free-conversation`) por um `ConversationService` novo sobre o cérebro do provedor **sem reserva** (`ITutorBrain` com chave, ADR-010). Mede correções esperadas encontradas, formato válido sem retentativa e com a retentativa única (RN-BRAIN04), e latência média e p95 do turno.
   - Correção esperada encontrada: algum `Corrected` da resposta contém, como palavras lado a lado (minúsculas, sem pontuação nas bordas), uma das formas aceitas da frase. Só mede cobertura: correções a mais não são penalizadas.
   - Provedor `unavailable`: a primeira frase falha com `BrainException`; as demais não rodam e o relatório traz o motivo. Falha em frase posterior conta como erro daquela frase.
   - Relatório markdown `eval/reports/eval-<aaaaMMdd-HHmmss>.md` (UTC) com resumo, nota mínima (`met` quando correções ≥ 80% e formato sem retentativa ≥ 90%, senão `not met`) e uma linha por frase. Saída 0 quando o relatório é gravado; 2 para argumentos ou golden set inválidos.
   ```
5. §10, linha de avaliação. Antigo: `| Avaliação | comando `eval` + `eval/golden-set.json` | relatório em `eval/reports/` |`. Novo: `| Avaliação | comando `eval` + `eval/golden-set.json` (lógica em `Core/Evaluation`, testada com fakes e relógio falso; `GoldenSetFileTests` guarda o arquivo) | relatório em `eval/reports/`; nota mínima: ≥ 80% das correções esperadas e ≥ 90% de formato sem retentativa (§6.6) |`.

Na aprovação do plano, o ADR-011 passou de Proposto a Aceito em 2026-10-23 (decisão do usuário, registrada na §12).

### 2.9 Rascunho do golden set (para o usuário revisar na aprovação do plano)

32 frases, uma correção esperada cada (nível B1 de um desenvolvedor brasileiro; as 5 primeiras são as frases de fumaça das US-004 e US-006). O Executor copia esta tabela para `eval/golden-set.json` na T8, sem alterar texto; o usuário corrige aqui, no plano, ou depois no arquivo e preenche `reviewedBy`. Conferido contra as regras G9 e G12: todo `original` é trecho do texto e nenhuma forma aceita aparece no texto.

| Id | Frase | Original (no texto) | Aceitas |
|---|---|---|---|
| gs-001 | I have worked with C# since five years | since five years | for five years |
| gs-002 | Yesterday I go to the office by bus | go | went |
| gs-003 | She don't like debugging on Fridays | don't | doesn't; does not |
| gs-004 | I am working here since 2020 | am working | have been working; have worked |
| gs-005 | We discussed about the new architecture | discussed about | discussed the new architecture; discussed the |
| gs-006 | He have two years of experience with Docker | have | has |
| gs-007 | I didn't understood the error message | didn't understood | didn't understand; did not understand |
| gs-008 | My team have five developers | team have | team has |
| gs-009 | I am agree with your solution | am agree | I agree |
| gs-010 | I have 35 years old | have 35 years old | am 35 years old; am 35 |
| gs-011 | She said me that the deploy failed | said me | told me |
| gs-012 | I will call you when I will arrive at the office | when I will arrive | when I arrive; when I get |
| gs-013 | If I will have time, I will study tonight | If I will have | If I have |
| gs-014 | The informations in the documentation are outdated | informations | the information |
| gs-015 | I need to improve my knowledges about databases | knowledges | my knowledge |
| gs-016 | He is more taller than me | more taller | is taller; is much taller |
| gs-017 | I did a mistake in the production database | did a mistake | made a mistake |
| gs-018 | Can you explain me how this algorithm works? | explain me | explain to me |
| gs-019 | I live here since 2015 | live here since | have lived here; have been living here |
| gs-020 | I pretend to travel to Canada next year | pretend to travel | intend to travel; plan to travel |
| gs-021 | I lost the bus this morning | lost the bus | missed the bus |
| gs-022 | He suggested me to use Redis | suggested me to use | suggested that I use; suggested I use; suggested using |
| gs-023 | I have many experience with microservices | many experience | a lot of experience; much experience; lots of experience |
| gs-024 | There is three bugs in the login page | There is three | There are three |
| gs-025 | I like very much play video games | like very much play | like playing; love playing; like to play; love to play; enjoy playing |
| gs-026 | She asked me where was the meeting room | where was the meeting room | where the meeting room was |
| gs-027 | I don't know what means this error | what means this error | what this error means |
| gs-028 | The meeting was canceled because the lack of time | because the lack of time | because of the lack of time; because of the lack |
| gs-029 | I am used to wake up early | used to wake up | used to waking up; used to waking |
| gs-030 | My English is better than the last year | than the last year | than last year |
| gs-031 | He gave me an advice about the interview | an advice | some advice; a piece of advice; a tip |
| gs-032 | I enjoyed to work on that project | enjoyed to work | enjoyed working |

Com 32 frases e 32 correções esperadas, a nota mínima pede 26 (D6). Limite conhecido (L2): uma correção certa com palavras fora da lista de aceitas conta como não encontrada; o relatório detalha cada frase para o usuário ampliar `accepted`.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `docs/adr/ADR-011-avaliacao-comparativa-logica-no-core-e-criterio-de-correcao.md` | Decisão (Status: Aceito em 2026-10-23, criado pelo Planejador) |
| Criar | `src/EnglishTutor.Core/Evaluation/EvalText.cs` | Normalização de palavras e `ContainsWords` |
| Criar | `src/EnglishTutor.Core/Evaluation/ExpectedCorrection.cs`, `GoldenSentence.cs`, `GoldenSet.cs`, `GoldenSetParseResult.cs` | Modelo do golden set |
| Criar | `src/EnglishTutor.Core/Evaluation/CorrectionMatcher.cs` | `IsFound`, `CountFound` |
| Criar | `src/EnglishTutor.Core/Evaluation/GoldenSetParser.cs` | Leitura e validação (G1–G12) |
| Criar | `src/EnglishTutor.Core/Evaluation/SentenceResult.cs`, `ProviderEvaluation.cs` (com `ProviderStatus`), `EvalMetrics.cs`, `EvalThresholds.cs` | Resultados, métricas, nota mínima |
| Criar | `src/EnglishTutor.Core/Evaluation/EvalRunner.cs` (com o contador de chamadas privado) | Execução por provedor |
| Criar | `src/EnglishTutor.Core/Evaluation/EvalReportBuilder.cs` | Relatório markdown e tabela de resumo |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | `TryAddSingleton<EvalRunner>()` |
| Criar | `src/EnglishTutor.Console/Commands/EvalCommandLine.cs` | Argumentos já lidos do `eval` (`Providers`, `GoldenSetPath`, `ReportsDirectory`) |
| Criar | `src/EnglishTutor.Console/Commands/EvalCommandLineParser.cs` (com `EvalCommandLineParseResult`) | `IsEvalCommand`, `Parse` e o resultado do parse |
| Criar | `src/EnglishTutor.Console/Commands/EvalCommand.cs` | Orquestra arquivo, cérebros, relatório e tela |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | Despacho chat × `eval` |
| Alterar | `src/EnglishTutor.Console/Ui/ConsoleText.cs`, `Logging/AppLog.cs` | Textos do `eval`; eventos 1006 e 1007 |
| Criar | `eval/golden-set.json`, `eval/README.md` | Dados (§2.9) e guia |
| Criar | `tests/EnglishTutor.Core.Tests/Evaluation/` (`EvalTextTests`, `CorrectionMatcherTests`, `GoldenSetParserTests`, `EvalMetricsTests`, `EvalRunnerTests`, `EvalReportBuilderTests`, `GoldenSetFileTests`) | Testes do Core |
| Alterar | `tests/EnglishTutor.Core.Tests/Fakes/FakeTutorBrain.cs`, `EnglishTutor.Core.Tests.csproj` | Gancho de relógio por chamada; referência ao pacote de relógio falso |
| Criar | `tests/EnglishTutor.Console.Tests/Commands/EvalCommandLineParserTests.cs`, `EvalCommandTests.cs`; `Support/EvalScriptedBrain.cs` | Testes do console |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutor.Console.Tests.csproj` | Referência ao pacote de relógio falso |
| Alterar | `specs/sprints/SPRINT_1.md`, `ARCHITECTURE.md` | Só pelo orquestrador, §2.8, antes da T1 |

Nenhum arquivo de `src/EnglishTutor.Infrastructure`. `docs/us/US-009.md` e o `CHANGELOG` são do Documentador.

## 4. Mapa cenário BDD → testes

Cenários (spec após §2.8): **S1** rodar o golden set; **S2** provedor indisponível na avaliação; **S3** nota mínima no relatório; **S4** argumentos inválidos no eval; **S5** golden set inválido. Comentário de cada teste: `// US-009 — Cenário: <nome exato> — <RN, se houver>`; testes de apoio dizem `— apoio` depois do cenário a que servem. Textos únicos por teste e por frase (`t1`, `t2`, …); em teste de retentativa, a resposta rejeitada (`rejected raw t1`) difere da aceita (AP-017, AP-026).

**EnglishTutor.Core.Tests/Evaluation**

| Cenário | Classe | Método (casos) | Tipo |
|---|---|---|---|
| S1 (apoio) | `EvalTextTests` | E1 `Tokens_WithMixedCaseAndEdgePunctuation_ReturnsLowercaseWordsWithoutEdgePunctuation` (3: `Hello, World!`; `(for five years).`; `"Quoted" text;`) | Unidade |
| S1 (apoio) | `EvalTextTests` | E2 `Tokens_WithTypographicApostropheOrQuotes_NormalizesToStraightCharacters` (2: `didn’t`; `“go”`) | Unidade |
| S1 (apoio) | `EvalTextTests` | E3 `Tokens_WithInnerSymbols_KeepsThemInsideTheWord` (3: `C#`, `don't`, `e-mail`) | Unidade |
| S1 (apoio) | `EvalTextTests` | E4 `Tokens_WithBlankOrPunctuationOnlyText_ReturnsNoWords` (3: `""`, `"   "`, `"..."`) | Unidade |
| S1 (apoio) | `EvalTextTests` | E5 `Tokens_WithTabsAndLineBreaks_SplitsOnAnyWhitespace` (1) | Unidade |
| S1 (apoio) | `EvalTextTests` | E6 `ContainsWords_WithFragmentWithoutWords_ReturnsFalse` (3: fragmento `""`, `"   "`, `"..."` sobre o texto `for five years`) | Unidade |
| S1 | `CorrectionMatcherTests` | M1 `IsFound_WhenCorrectedContainsAcceptedFragment_ReturnsTrue` (3: igual; dentro de frase maior; maiúsculas e pontuação diferentes) | Unidade |
| S1 | `CorrectionMatcherTests` | M2 `IsFound_WhenFragmentWordsAreNotContiguous_ReturnsFalse` (2: palavra no meio `for the five years`; ordem trocada `five years for`) | Unidade |
| S1 | `CorrectionMatcherTests` | M3 `IsFound_WhenFragmentIsOnlyPartOfAWord_ReturnsFalse` (2: aceita `has` × corrigido `hash`; aceita `for five years` × corrigido `for five yearsold`) | Unidade |
| S1 | `CorrectionMatcherTests` | M4 `IsFound_WhenSecondAcceptedAlternativeMatches_ReturnsTrue` (1) | Unidade |
| S1 | `CorrectionMatcherTests` | M5 `IsFound_WithFragmentInDifferentFields_CountsOnlyTheCorrectedField` (3: só em `Corrected` → verdadeiro; só em `Original` → falso; só em `Explanation` → falso; as três fontes sempre examinadas, AP-047) | Unidade |
| S1 | `CorrectionMatcherTests` | M6 `IsFound_WhenAnyOfSeveralCorrectionsMatches_ReturnsTrue` (2: casa a primeira; casa a segunda) | Unidade |
| S1 | `CorrectionMatcherTests` | M7 `IsFound_WithNoCorrections_ReturnsFalse` (1) | Unidade |
| S1 | `CorrectionMatcherTests` | M8 `CountFound_WithTwoExpectedCorrections_CountsEachOneMatchedIndependently` (4: nenhuma → 0; só a segunda → 1; as duas, cada uma em sua própria `Correction` → 2; uma única `Correction` cujo `Corrected` contém as formas aceitas das duas → 2) | Unidade |
| S5 | `GoldenSetParserTests` | P1 `Parse_WithValidJson_ReturnsSentencesAndExpectedCorrections` (1: 2 frases; campos e alternativas) | Unidade |
| S5 | `GoldenSetParserTests` | P2 `Parse_WithReviewedByAbsentNullOrFilled_ReturnsTheReviewerOrEmpty` (3; cada caso também exige 1 frase lida) | Unidade |
| S5 | `GoldenSetParserTests` | P3 `Parse_WithMalformedJson_ReturnsErrorWithExpectedShape` (1; G1, confere `Expected an object like`) | Unidade |
| S5 | `GoldenSetParserTests` | P4 `Parse_WithoutNonEmptySentencesArray_ReturnsError` (4: raiz é array (G2, `found an array`); `sentences` ausente (`found: missing`); `[]` (`found: 0 items`); `"x"` (`found: a string`)) | Unidade |
| S5 | `GoldenSetParserTests` | P5 `Parse_WithMissingOrBlankId_ReturnsErrorWithPosition` (2: ausente → `Sentence #2` e `found: missing`; `"  "` → `found: blank`) | Unidade |
| S5 | `GoldenSetParserTests` | P6 `Parse_WithRepeatedIdIgnoringCase_ReturnsErrorNamingTheId` (1: `gs-1` e `GS-1`) | Unidade |
| S5 | `GoldenSetParserTests` | P7 `Parse_WithMissingOrBlankText_ReturnsErrorNamingTheSentence` (2) | Unidade |
| S5 | `GoldenSetParserTests` | P8 `Parse_WithZeroOneTwoOrThreeExpectedCorrections_AcceptsOnlyOneOrTwo` (4: 0 e 3 → erro `found: 0 items`/`found: 3 items`; 1 e 2 → válido, nas duas bordas do limite, AP-016) | Unidade |
| S5 | `GoldenSetParserTests` | P9 `Parse_WhenOriginalIsNotInTheSentence_ReturnsErrorNamingTheFragment` (1) | Unidade |
| S5 | `GoldenSetParserTests` | P10 `Parse_WhenAcceptedFragmentIsAlreadyInTheSentence_ReturnsError` (3 sobre `I am agree with your solution`: aceita `agree with` → erro; aceita `I AM AGREE,` (maiúsculas e pontuação) → erro; aceita `i agree` (palavras não contíguas) → válido) | Unidade |
| S5 | `GoldenSetParserTests` | P11 `Parse_WithMissingEmptyOrWordlessAcceptedItems_ReturnsError` (4: `accepted` ausente; `[]`; item `"  "`; item `"..."`) | Unidade |
| S5 | `GoldenSetParserTests` | P12 `Parse_WithSeveralProblems_ReturnsEveryError` (1: 2 frases, 3 problemas, 3 mensagens) | Unidade |
| S5 | `GoldenSetParserTests` | P13 `Parse_WithWrongJsonTypes_ReturnsTypeError` (6, um por ramo de `{kind}`: `id` número → `a number`; `text` booleano → `a boolean`; `original` `null` → `null`; `original` array → `an array`; `expected` objeto → `an object`; `accepted` texto → `a string`) | Unidade |
| S5 | `GoldenSetParserTests` | P14 `Parse_WithMissingOrBlankOriginal_ReturnsErrorNamingTheExpectedItem` (2: `original` ausente → `expected[0].original` e `found: missing`; `original` `"  "` → `found: blank`) | Unidade |
| S5 | `GoldenSetParserTests` | P15 `Parse_WithPropertyNamesInOtherCaseOrUnknownProperties_ReadsOnlyTheExactNames` (2: `Sentences` com maiúscula no lugar de `sentences` → erro `"sentences" ... found: missing`; propriedade desconhecida `extra` na raiz e na frase, com o resto válido → válido e 1 frase lida) | Unidade |
| S3 | `EvalMetricsTests` | X1 `From_WithMixedResults_CountsFoundAndValidFormats` (1: 4 frases, uma com retentativa, uma crua) | Unidade |
| S1 | `EvalMetricsTests` | X2 `From_WithLatencies_ComputesMeanAndNearestRankP95` (4: n=1 → p95 = o valor; n=2 [1 s, 3 s] → 3 s; n=20 [1 s a 20 s] → 19 s; n=21 [1 s a 21 s] → 20 s; médias 5 s, 2 s, 10,5 s, 11 s) | Unidade |
| S2 | `EvalMetricsTests` | X3 `From_WithFailedSentences_ExcludesThemFromLatencyButNotFromRates` (1) | Unidade |
| S3 | `EvalMetricsTests` | X4 `IsMet_AtAndBelowEachThreshold_FollowsBothMinimums` (6 `(encontradas, esperadas, formato, frases)`: (80,100,90,100) sim; (79,100,90,100) não; (80,100,89,100) não; (4,5,9,10) sim; (0,5,10,10) não; (0,0,0,0) não) | Unidade |
| S1 (RN-BRAIN04) | `EvalRunnerTests` | R1 `EvaluateAsync_WithValidReplies_CountsFoundCorrectionsAndFirstAttemptFormat` (1: 3 frases, 2 achadas) | Unidade |
| S1 (RN-BRAIN04) | `EvalRunnerTests` | R2 `EvaluateAsync_WhenFirstReplyIsInvalidAndSecondIsValid_CountsValidAfterRetryButNotFirstAttempt` (1: `Attempts` 2; correções contadas da segunda resposta; texto cru rejeitado ≠ resposta aceita) | Unidade |
| S1 (RN-BRAIN04) | `EvalRunnerTests` | R3 `EvaluateAsync_WhenBothRepliesAreInvalid_CountsInvalidFormatAndNoCorrections` (1: primeira frase, ainda `Evaluated`) | Unidade |
| S1 (RN-BRAIN01) | `EvalRunnerTests` | R4 `EvaluateAsync_Always_SendsEachSentenceAloneWithTheSystemPrompt` (1: cada pedido tem 1 mensagem com o texto da própria frase e `Mode: free-conversation`, sem texto das anteriores; `SystemPrompt` da fonte; `SessionHint` nulo) | Unidade |
| S1 | `EvalRunnerTests` | R5 `EvaluateAsync_Always_MeasuresTheLatencyOfEachSentenceIncludingTheRetry` (1: relógio falso; frase 1 leva 2 s; frase 2 leva 1 s + 3 s com retentativa → 4 s) | Unidade |
| S2 (RN-BRAIN07) | `EvalRunnerTests` | R6 `EvaluateAsync_WhenFirstSentenceFailsWithBrainException_ReturnsUnavailableWithReasonAndStops` (5: `CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`, `Unknown`; motivo = mensagem; `Results` vazio; o cérebro foi chamado 1 vez) | Unidade |
| S2 (RN-BRAIN07) | `EvalRunnerTests` | R7 `EvaluateAsync_WhenLaterSentenceFailsWithBrainException_RecordsTheFailureAndContinues` (1: falha na 2ª de 3; `Evaluated`; 3 resultados; a 2ª com `Failure`; a 3ª avaliada) | Unidade |
| S1 | `EvalRunnerTests` | R8 `EvaluateAsync_WhenCancelled_PropagatesOperationCanceledException` (1: o cérebro lança o cancelamento na 1ª frase; não vira `unavailable`) | Unidade |
| S1 | `EvalReportBuilderTests` | B1 `Build_WithEvaluatedAndUnavailableProviders_ListsBothInSummaryAndExplainsTheUnavailableOne` (1; motivo com quebra de linha sai numa linha só) | Unidade |
| S1 | `EvalReportBuilderTests` | B2 `Build_UnderPortugueseCulture_FormatsNumbersWithInvariantCulture` (1: `CultureInfo.CurrentCulture` = `pt-BR`; `66.7%`, sem `66,7%`) | Unidade |
| S1 | `EvalReportBuilderTests` | B3 `Build_WithRatiosThatNeedRounding_RoundsPercentagesToOneDecimal` (2: 1/3 → `33.3%`; 2/3 → `66.7%`) | Unidade |
| S1 | `EvalReportBuilderTests` | B4 `Build_WithLatenciesThatNeedRounding_RoundsMillisecondsAwayFromZero` (2: 1000 ms e 1001 ms → `1001 ms`; 1000, 1000 e 1001 ms → `1000 ms`) | Unidade |
| S1 | `EvalReportBuilderTests` | B5 `Build_WithReviewerFilledOrEmpty_ShowsReviewerOrPending` (2) | Unidade |
| S1 | `EvalReportBuilderTests` | B6 `Build_WithFailedSentence_ShowsTheFailureEscapingPipesAndLineBreaks` (1: mensagem `boom \| t1` com quebra de linha → uma célula, `\|` escapado, latência `-`) | Unidade |
| S3 | `EvalReportBuilderTests` | B7 `Build_WithMinimumGradeMetOrNot_SaysMetOrNotMet` (2) | Unidade |
| S1 | `EvalReportBuilderTests` | B8 `Build_WithEvaluatedProvider_ListsOneDetailRowPerSentenceInGoldenSetOrder` (1: ordem do arquivo; notas `retry used` e `raw fallback after retry`) | Unidade |
| S2 | `EvalReportBuilderTests` | B9 `Build_WithUnavailableProvider_HasNoDetailSectionForIt` (1: com a linha `unavailable` presente no resumo) | Unidade |
| S5 (D7) | `GoldenSetFileTests` | F1 `GoldenSetFile_InRepository_IsValidWithAtLeastThirtySentences` (1: lê `eval/golden-set.json` pela raiz do repositório; sem erro; ≥ 30 frases) | Unidade (arquivo do repositório) |

**EnglishTutor.Console.Tests/Commands** (em processo, `EnglishTutorApp.RunAsync` com `AppRunOptions`; golden set e pasta de relatórios em diretório temporário; cérebros por chave trocados em `ConfigureServices`; `FakeTimeProvider` no lugar do relógio)

| Cenário | Classe | Método (casos) | Tipo |
|---|---|---|---|
| S4 | `EvalCommandLineParserTests` | C1 `Parse_WithOnlyEval_UsesAllProvidersAndDefaultPaths` (1: `ClaudeCode`, `Ollama`; `eval/golden-set.json`; `eval/reports`) | Unidade |
| S4 | `EvalCommandLineParserTests` | C2 `Parse_WithProvidersAsOneCommaSeparatedToken_ReturnsCanonicalProviders` (2: `ClaudeCode,Ollama`; `claudecode,OLLAMA`) | Unidade |
| S4 | `EvalCommandLineParserTests` | C3 `Parse_WithProvidersAsSeparateTokens_ReturnsTheSameProviders` (1: `--providers ClaudeCode Ollama --golden-set x`, o valor de `--golden-set` não vira provedor) | Unidade |
| S4 | `EvalCommandLineParserTests` | C4 `Parse_WithRepeatedProviderNames_KeepsEachOnce` (1: `Ollama,ollama,ClaudeCode` → `Ollama`, `ClaudeCode`) | Unidade |
| S4 | `EvalCommandLineParserTests` | C5 `Parse_WithUnknownProvider_ReturnsErrorListingAcceptedProviders` (1: `Unknown provider 'Inexistente'. Accepted values: ClaudeCode, Ollama.`) | Unidade |
| S4 | `EvalCommandLineParserTests` | C6 `Parse_WithUnknownOption_ReturnsErrorListingValidOptions` (1) | Unidade |
| S4 | `EvalCommandLineParserTests` | C7 `Parse_WithGoldenSetAndReportsDir_UsesTheGivenPaths` (1) | Unidade |
| S4 | `EvalCommandLineParserTests` | C8 `Parse_WithOptionWithoutValue_ReturnsError` (3: `--providers` no fim; `--providers --golden-set x`; `--golden-set` no fim) | Unidade |
| S4 | `EvalCommandLineParserTests` | C9 `Parse_WithUnexpectedOrRepeatedArguments_ReturnsError` (3: `eval foo`; `eval --golden-set a.json extra`; `eval --providers Ollama --providers ClaudeCode`) | Unidade |
| S1 | `EvalCommandLineParserTests` | C10 `IsEvalCommand_WithDifferentFirstArguments_OnlyAcceptsEval` (4: `eval` e `EVAL --providers Ollama` → verdadeiro; lista vazia e `--Brain:Provider Ollama` → falso) | Unidade |
| S1 | `EvalCommandTests` | A1 `RunAsync_WithEvalCommand_PrintsMetricsPerProviderAndSavesTheMarkdownReport` (1: 2 provedores, 3 frases, `Evaluating ClaudeCode (3 sentences)...` antes da tabela; linha do ClaudeCode com `evaluated`, `(3/3)` nas três taxas, `2000 ms` e `met`; linha do Ollama com `(2/3)` nas correções e `not met`; `Report saved to <caminho>`; arquivo `eval-20261023-143005.md` com o mesmo resumo; saída 0; saída de erro vazia; sem cabeçalho do chat) | Integração (console em processo) |
| S2 | `EvalCommandTests` | A2 `RunAsync_WhenOneProviderIsUnavailable_MarksItUnavailableAndKeepsTheOtherResults` (1: o Ollama lança `BrainException(Unknown, "Ollama is not running at t1")`; linha `unavailable`, motivo no relatório, resultados do ClaudeCode intactos; saída 0) | Integração |
| S4 | `EvalCommandTests` | A3 `RunAsync_WithUnknownProvider_ExitsWithInvalidConfigurationListingAcceptedProviders` (1: saída 2; erro com `Invalid eval arguments:`, `Unknown provider 'Inexistente'`, `Accepted values: ClaudeCode, Ollama.`, `Usage:`; nenhuma chamada aos cérebros; pasta de relatórios sem arquivo) | Integração |
| S5 | `EvalCommandTests` | A4 `RunAsync_WithMissingGoldenSet_ExitsWithInvalidConfigurationNamingThePath` (1: saída 2; `Golden set not found: <caminho>`; sem relatório) | Integração |
| S5 | `EvalCommandTests` | A5 `RunAsync_WithInvalidGoldenSet_ExitsWithInvalidConfigurationListingEveryProblem` (1: arquivo com 2 problemas; saída 2; `Invalid golden set (<caminho>):` e as 2 mensagens; nenhuma chamada aos cérebros; sem relatório) | Integração |
| S3 | `EvalCommandTests` | A6 `RunAsync_WhenCorrectionsAreMissed_ShowsNotMetInSummaryAndReport` (1: respostas válidas sem a correção esperada → 0/3; `not met` na tela e no arquivo; saída 0: a nota não muda o código) | Integração |
| S1 | `EvalCommandTests` | A7 `RunAsync_WhenReportsDirectoryCannotBeCreated_ReturnsUnexpectedErrorAndExplains` (1: `--reports-dir` aponta para um arquivo existente; saída 1; `Could not save the report to`) | Integração |
| S1 | `EvalCommandTests` | A8 `RunAsync_WithEvalCommand_LogsOneProviderEvaluatedEventPerProvider` (1: arquivo de log do Serilog; 2 eventos 1006 com `Provider`, `Status`, `Sentences`, `FoundTotal`, `ExpectedTotal`, `ElapsedMs`; 1 evento 1007 com `Path`; nenhum texto de frase no log) | Integração |
| S2 (ADR-010) | `EvalCommandTests` | A9 `RunAsync_WithFallbackConfigured_EvaluatesEachProviderBrainWithoutFallback` (1: `Brain:FallbackProvider=Ollama`; o ClaudeCode lança `CliNotFound`; marcado `unavailable`; o cérebro do Ollama só é chamado nas 3 frases da avaliação dele, não na do ClaudeCode; nenhuma linha `Switched to`) | Integração |
| S1 (apoio, despacho) | `EvalCommandTests` | A10 `RunAsync_WhenFirstArgumentIsNotEval_StartsTheChatSession` (1: `Args` = `--unrelated value`, entrada `/quit`; cabeçalho do chat; saída 0; nenhum relatório) | Integração |
| S1 (apoio, RN-BRAIN09) | `EvalCommandTests` | A11 `RunAsync_WhenCancelledDuringEval_ReturnsZeroWithoutSavingTheReport` (1: o cérebro cancela o token e lança `OperationCanceledException` na 1ª frase; saída 0; 1 chamada ao cérebro; sem relatório) | Integração |

**Totais (AP-049, conferidos contra as tabelas acima).** Core.Tests: E 3+2+3+3+1+3 = 15; M 3+2+2+1+3+2+1+4 = 18; P 1+3+1+4+2+1+2+4+1+3+4+1+6+2+2 = 37; X 1+4+1+6 = 12; R 1+1+1+1+1+5+1+1 = 12; B 1+1+2+2+2+1+2+1+1 = 13; F 1 → **108 casos** (15+18+37+12+12+13+1). Console.Tests: C 1+2+1+1+1+1+1+3+3+4 = 18; A 11 → **29 casos**. Total **137 casos** novos na suíte rápida. Nenhum teste `LiveBrain` novo (as medições reais são os comandos D5–D8).

**Condicionais da §2 (AP-047).** "Só `Corrected` conta; as três fontes são examinadas": M5 (só `Corrected`, só `Original`, só `Explanation`). "Palavras lado a lado": M2 (buraco e ordem trocada) e M1; "palavra inteira": M3. "Fragmento sem palavras devolve falso": E6. "Uma `Correction` pode satisfazer as duas esperadas": M8 (caso 4, contra o caso 3 com uma correção para cada). "Primeira frase falha → `unavailable`; frase posterior → erro da frase": R6 e R7 (papéis trocados). "Cancelamento propaga": R8 e A11. "Despacho só com `eval` primeiro": C10 e A10. "`--providers` aceita um token ou vários": C2 e C3. "Eval sem reserva": A9. "Código de saída independe da nota": A6 (`not met` com saída 0) e A1 (`met` com saída 0). "Sem latência: `-`": B6 e B9. "Retentativa conta no formato final, não no primeiro": R2 (primeira inválida, final válida) e R3 (as duas inválidas). "Rejeita `original` fora do texto e aceita já no texto": P9 e P10 (com o caso válido de palavras não contíguas). "Nomes de propriedade sensíveis a maiúsculas; propriedade desconhecida ignorada": P15 (um caso para cada metade).

**Placeholders (AP-054).** `{kind}` (seis ramos): P13 e P4; `{missing | blank | kind}`: P5 (`id`), P7 (`text`), P14 (`original`) e P13 (`kind`); `{missing | 0 items | kind}`: P4 (`sentences`) e P11 (`accepted`); `found: n items` (0 e 3): P8; `{ref}` com id e com posição: P5 (posição) e P6 (id); `Unknown provider '<x>'` e a lista de provedores: C5 e A3; percentuais e milissegundos: B3 e B4; cultura: B2. A coluna "Teste" da tabela G1–G12 (§2.2) repete essa conferência por condição. Nenhuma mensagem embute texto de biblioteca além do `{detail}` da G1 (P3 confere o trecho do projeto).

**Tolerância de verificação (AP-051).** Esta US não relaxa nenhuma validação. O "formato válido" do eval é o que o parser e o `TutorReplyValidator` aceitam, inclusive 1 ou 2 perguntas (RN-FEED01). Nenhum teste `LiveBrain` existente muda, e nenhum novo asserta a regra original.

## 5. Tarefas (ordem de execução TDD)

Convenções desta lista: o "Red" usa um esqueleto que compila e devolve o valor neutro indicado; depois do commit do Green, cada teste que **nasce verde** recebe a mutação prescrita, aplicada no `src/`, rodada, vista falhar pelo motivo certo e desfeita pela edição inversa, nunca por `git checkout`/`git restore` (AP-037). Cada mutação foi conferida mentalmente contra a asserção. Crie primeiro o arquivo que declara e depois o que usa (AP-044). Antes de cada commit `feat`, o Executor preenche `Escolhas de forma` (decisão de forma vai à §12 num commit `docs(ai)` anterior; AP-055).

- [x] **T1 — Palavras e comparação de correções (E1–E6, M1–M8)** — feito: d0e0f33 (Red 463c31e). Mutações aplicadas e desfeitas (E4: só o caso `"..."` falha, pois `RemoveEmptyEntries` já cobre `""` e `"   "`; E6, M2, M3, M5, M7, M8 caso 1: falharam pelos motivos previstos).
  Red: `EvalText.Tokens` devolve lista vazia; `EvalText.ContainsWords` devolve `false`; `CorrectionMatcher.IsFound` devolve `false`; `CountFound` devolve `0`; `ExpectedCorrection` só como tipo. Falham: E1 (3), E2 (2), E3 (3), E5, M1 (3), M4, M5 caso "só em `Corrected`", M6 (2), M8 casos 2, 3 e 4 (o esqueleto devolve 0, e eles esperam 1, 2 e 2). **Nascem verdes** (conferido asserção por asserção contra o esqueleto): E4 (3), E6 (3), M2 (2), M3 (2), M5 casos "só em `Original`" e "só em `Explanation`", M7, M8 caso 1 ("nenhuma → 0"). Mutações depois do Green: E4 — não descartar pedaços vazios (falha os 3 casos: `""` vira `[""]`, `"   "` vira lista de vazios, `"..."` vira `[""]`); E6 — `ContainsWords` perde a guarda "ao menos uma palavra" e trata a lista vazia de palavras do fragmento como contida (falha os 3 casos, que passam a devolver `true`); M2 — `ContainsWords` testa só se todas as palavras do fragmento existem em qualquer posição (falha os 2 casos: `for the five years` e `five years for` passam a casar); M3 — `ContainsWords` compara como texto (`corrected.Contains(fragment)` sem diferenciar maiúsculas; `hash` contém `has` e `for five yearsold` contém `for five years`: falha os 2 casos); M5 — `IsFound` examina também `Original` e `Explanation` (falha os 2 casos de fonte errada); M7 — `IsFound` devolve verdadeiro com lista vazia; M8 caso 1 — `CountFound` devolve `expected.Count`.
  Green: §2.3.
  Refactor: constantes dos caracteres de borda agrupadas.
  Commit: `test(eval): cover word normalization and correction matching` + `feat(eval): add word normalization and correction matching`.
  Escolhas de forma: nenhuma

- [x] **T2 — Leitura e validação do golden set (P1–P15)** — feito: a12a9d5 (Red c5b1935, ajuste P15 a51f3d0)
  Red: `GoldenSetParser.Parse` devolve `GoldenSetParseResult` válido com `GoldenSet("", [])`; tipos `GoldenSet`, `GoldenSentence`, `GoldenSetParseResult`. Falham todos os 37 casos (cada caso de valor aceito também exige a frase lida, e cada caso de erro exige a mensagem; P15 caso "propriedade desconhecida" exige 1 frase lida e o esqueleto devolve 0). Nenhum nasce verde.
  Green: §2.2 (G1–G12), usando `EvalText.ContainsWords` para G9 e G12; coleta todos os erros; `JsonDocument` com checagem explícita de tipo e nomes de propriedade comparados com diferenciação de maiúsculas.
  Refactor: modelos de mensagem agrupados; `{kind}` num único método.
  Commit: `test(eval): cover golden set parsing and validation` + `feat(eval): parse and validate the golden set`.
  Escolhas de forma: §12 em 7ca0eac

- [x] **T3 — Resultados, métricas e nota mínima (X1–X4)** — feito: 1a88ebf (Red 390d07a); as 3 mutações prescritas falharam pelos casos previstos e foram desfeitas
  Red: tipos `SentenceResult`, `ProviderEvaluation`, `ProviderStatus`, `EvalMetrics`, `EvalThresholds`; `EvalMetrics.From` devolve zeros; `EvalThresholds.IsMet` devolve `false`. Falham: X1, X2 (4), X3, X4 casos (80,100,90,100) e (4,5,9,10). **Nascem verdes**: X4 casos (79,100,90,100), (80,100,89,100), (0,5,10,10), (0,0,0,0). Mutações depois do Green: (79,100,90,100) e (0,5,10,10) — `IsMet` compara só o formato (os dois passam a `true`); (80,100,89,100) — `IsMet` compara só as correções (passa a `true`); (0,0,0,0) — remover a guarda `expected > 0 && sentences > 0` (0 ≥ 0 passa a `true`).
  Green: §2.5, com inteiros.
  Refactor: nenhum previsto.
  Commit: `test(eval): cover eval metrics and the minimum grade` + `feat(eval): add eval metrics and the minimum grade`.
  Escolhas de forma: nenhuma

- [x] **T4 — `EvalRunner` (R1–R8)** — feito: 3866fcf (Red f8da682)
  Esqueleto de teste: `Microsoft.Extensions.TimeProvider.Testing` no `EnglishTutor.Core.Tests.csproj` (versão já em `Directory.Packages.props`, §6) e um gancho no `FakeTutorBrain` para uma ação por chamada (avançar o `FakeTimeProvider`); a forma exata (overload de `Returns`/`ReturnsInvalid`) é escolha do Executor, registrada na §12 num commit `docs(ai)` antes do `feat`. Red: `EvalRunner.EvaluateAsync` devolve `ProviderEvaluation(provider, Evaluated, null, [])` sem chamar o cérebro. Falham todos os 12 casos (R1 a R5 e R7 esperam resultados; R6 espera `Unavailable`; R8 espera a exceção). Nenhum nasce verde.
  Green: §2.4, registro em `AddEnglishTutorCore`.
  Refactor: o contador de chamadas fica `private sealed` dentro do `EvalRunner`.
  Commit: `test(eval): cover the eval runner` + `feat(eval): add the eval runner`.
  Escolhas de forma: §12 em 0cc48d6

- [x] **T5 — Relatório (B1–B9)** — feito: a4fd584 (Red 98ea737)
  Red: `EvalReportBuilder.Build` devolve `""` e `SummaryTable` devolve lista vazia. Falham os 13 casos (todo teste exige conteúdo presente; B9 exige também a linha `unavailable` no resumo). Nenhum nasce verde.
  Green: §2.6 (layout e transformações).
  Refactor: formatadores de percentual e de milissegundos em métodos privados únicos.
  Commit: `test(eval): cover the eval report` + `feat(eval): build the markdown eval report`.
  Escolhas de forma: §12 em f587058

- [x] **T6 — Argumentos do `eval` (C1–C10)** — feito: 839067d (Red 80303c1); mutações de C10 aplicadas e desfeitas
  Red: `EvalCommandLine(IReadOnlyList<string> Providers, string GoldenSetPath, string ReportsDirectory)` (em `EvalCommandLine.cs`), `EvalCommandLineParseResult` (em `EvalCommandLineParser.cs`, criado primeiro o `EvalCommandLine.cs`); `IsEvalCommand` devolve `false`; `Parse` devolve resultado válido com `EvalCommandLine([], "", "")`. Falham: C1 a C9 (14 casos) e os 2 casos verdadeiros de C10 (16 dos 18). **Nascem verdes**: C10 `[]` e `--Brain:Provider Ollama` (devolvem `false`). Mutações depois do Green: C10 `[]` — `IsEvalCommand` devolve `true` para lista vazia; C10 `--Brain:Provider Ollama` — `IsEvalCommand` devolve `true` para qualquer lista não vazia.
  Green: §2.7 (parser).
  Refactor: nenhum previsto.
  Commit: `test(eval): cover eval command-line parsing` + `feat(eval): parse the eval command line`.
  Escolhas de forma: nenhuma

- [ ] **T7 — Comando `eval` e despacho (A1–A11)**
  Esqueleto de teste: `EvalScriptedBrain` em `Console.Tests/Support` (fila de respostas; cada resposta avança um `FakeTimeProvider` por um tempo dado; conta chamadas), referência ao pacote de relógio falso no `Console.Tests.csproj`. Red: nenhum código de produção novo (`EnglishTutorApp` ainda sem despacho: com `eval` nos argumentos abre o chat com a entrada vazia, imprime o cabeçalho e sai com 0). Falham: A1, A2, A3, A4, A5, A6, A7, A8, A9, A11 (o cérebro nunca é chamado, não há relatório, o código de saída esperado é 1 ou 2, ou faltam as linhas). **Nasce verde**: A10 (sem `eval` o chat abre e sai com 0). Mutações depois do Green para A10: o despacho trata qualquer lista de argumentos não vazia como `eval` (com `--unrelated value` o parser acusa opção desconhecida, a saída é 2 e o cabeçalho não aparece: A10 falha); o despacho sempre abre o `eval` (idem).
  Green: `EvalCommand`, `ConsoleText` (§2.6), `AppLog` 1006 e 1007, despacho em `EnglishTutorApp` (§2.7).
  Refactor: o relatório é gravado por um método único que traduz `IOException` e `UnauthorizedAccessException` na mensagem de A7.
  Commit: `test(eval): cover the eval command end to end` + `feat(eval): add the eval command`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] **T8 — Golden set e guia (F1)**
  Red: `GoldenSetFileTests.GoldenSetFile_InRepository_IsValidWithAtLeastThirtySentences`, que falha (`eval/` só tem `.gitkeep`; o teste reporta "arquivo não encontrado").
  Green: `eval/golden-set.json` com as 32 frases da §2.9 (sem alterar texto) e `reviewedBy` gravado **exatamente** como a linha `  "reviewedBy": "",` (aspas, dois-pontos, **um** espaço, duas aspas, em linha própria), a grafia que o `git grep -n -F '"reviewedBy": ""'` do D7 procura; e `eval/README.md` (formato, como o critério casa, como ampliar `accepted`, comando de execução em Windows PowerShell 5.1 e Bash, aviso de que `reviewedBy` é do usuário). Antes do commit, o Executor roda o `git grep` do D7 e confere que acha essa linha (código 0); a saída vai à §11 na T9.
  Refactor: nenhum previsto.
  Commit: `test(eval): guard the golden set file` + `feat(eval): add the golden set draft and the eval guide`.
  Escolhas de forma: nenhuma | §12 em <hash>

- [ ] **T9 — Execuções reais e evidências (D5–D14)**
  Sem código. O Executor aquece o modelo do Ollama (uma chamada antes, para a 1ª frase não estourar `TimeoutSeconds`, L3) e roda o **comando completo do D5 (com `ClaudeCode,Ollama`) 3 vezes seguidas**: as três rodadas são, ao mesmo tempo, o D5 e o D6 (a 1ª prova o D5; as 3 provam o D6; nenhuma rodada é só do ClaudeCode). Depois roda o D8 a D11 e cola na §11 a saída resumida, as linhas dos provedores de cada relatório e, para o D7, a linha que o `git grep -n -F '"reviewedBy": ""' -- eval/golden-set.json` acha **antes** de o usuário preencher `reviewedBy` (código 0, lado positivo da verificação, AP-035). O `reviewedBy` é do usuário: enquanto vazio, o Executor registra "D7: pendente de revisão do usuário" e o D6 vale como provisório. Se qualquer rodada do D6 ficar `not met`, o Executor **não** altera o golden set, o critério nem os limiares: registra os números e para; quem decide é o usuário, com registro prévio na spec (AP-032).
  Commit: `docs(ai): add DoD evidence for US-009` (feito pelo Executor ao fim).
  Escolhas de forma: nenhuma | §12 em <hash>

## 6. Pacotes NuGet

| Pacote | Projeto | Motivo |
|---|---|---|
| `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 (versão já fixada em `Directory.Packages.props`; nenhuma versão nova) | `EnglishTutor.Core.Tests`, `EnglishTutor.Console.Tests` | `FakeTimeProvider` para medir latência e datar o relatório sem relógio real |

Nenhum pacote novo em `src/`. O Core usa só BCL (`System.Text.Json`) e as abstrações que já referencia; `CoreDependencyTests` continua verde.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| **Oscilação do D6 (AP-032).** Camada: o comando real `eval`, medindo a **primeira** chamada ao adaptador (parser e validador incluídos) e as correções da resposta final. Tolerância: limiares do PRODUCT (≥ 80% das correções, ≥ 90% no formato sem retentativa; com 32 frases, 26 e 29). Rodadas: **3 seguidas do comando completo do D5**. A retentativa **não** precisa ter sido usada (a taxa com retentativa é informativa). Não há medição anterior do ClaudeCode com margem: a US-004 fixou só o **mínimo** de 4/5 correções na fumaça do Claude Code. O 4/5 (80%) **medido** é do **Ollama** (`tasks/US-006/plan.md`, D5, 3 rodadas iguais, `expected corrections found: 4/5`), também sem margem; como o D6 é só do ClaudeCode, serve de referência de ordem de grandeza, não de previsão. Para ler a taxa "sem retentativa" do Ollama: antes do reforço do prompt, a US-006 observou de 0 a 2 frases de 5 fora do formato por execução (sequência 2, 0, 1, 0, 1), todas por contagem de perguntas e nenhuma por JSON inválido; depois do reforço, 0 em 3 rodadas | Se alguma rodada ficar `not met`, o Executor registra os números e para (T9); **quem decide é o usuário**, com registro prévio na spec (ampliar `accepted`, trocar frases, mudar o modelo ou o limiar). O relatório detalhado por frase mostra onde perdeu |
| **Métricas só registradas (AP-052).** Latência média e p95, taxa com retentativa e tudo do Ollama não têm limiar nesta US | Risco para a US que as usar como critério (por exemplo, decidir o modelo local padrão ou fixar a meta de latência do PRODUCT, que é fim a fim e inclui STT e TTS): essa US precisa registrar o limiar na spec antes. Nada a registrar na spec desta US |
| **L1 (limite aceito): o critério mede só cobertura.** Correção errada ou a mais não é penalizada | Consumidores: `CorrectionMatcher` (conta só acertos); relatório (mostra a porcentagem sem falsos positivos); D6 e a nota mínima das US de cérebro futuras (um modelo que "corrige demais" passa). RN-FEED02 limita a 2 correções por resposta, o que limita o efeito |
| **L2 (limite aceito): paráfrase fora de `accepted` conta como não encontrada** | Consumidores: matcher, relatório (a tabela de detalhes mostra cada frase), D6 e as US futuras (a régua é o arquivo, e mudar o arquivo muda a régua). O usuário amplia `accepted` depois de ver o relatório; a mudança é de dados, e o `reviewedBy` registra quem revisou |
| **L3 (limite aceito): a indisponibilidade é decidida pela primeira frase** (um `Timeout` na primeira, por exemplo o Ollama carregando o modelo, marca o provedor inteiro como `unavailable`) | Consumidores: `EvalRunner` (R6 cobre os cinco tipos), relatório (traz o motivo), D5 e D8 (o Executor aquece o modelo; no D8 o `unavailable` é o resultado esperado), D6 (só o ClaudeCode é nota; o Ollama das mesmas rodadas é informativo). Falha em frase posterior não derruba o provedor (R7) |
| **L4 (limite aceito): "formato válido" herda a tolerância de 1 a 2 perguntas (RN-FEED01)** | Consumidores: validador (sem mudança), relatório e D6 (a taxa conta como válida uma resposta com 2 perguntas, que o prompt não pede); o PRODUCT mede "formato válido" e o validador é a definição do projeto |
| **L5 (limite aceito): relatório de duas rodadas no mesmo segundo se sobrescreve** | Consumidores: relatório (nome por segundo) e D6 (rodadas levam minutos). Sem mitigação além do nome por segundo |
| **L6 (limite aceito): o p95 de 32 frases é a 31ª medida** (a segunda pior) | Consumidores: relatório (informativo) e as US futuras que fixarem meta de latência (precisam de amostra maior). Só registrado nesta US |
| Consumo da assinatura: uma rodada do Claude Code faz 32 a 64 chamadas; o D5/D6 faz 3 rodadas completas (as mesmas), ou seja, 96 a 192 chamadas | Rodadas em sequência; se o limite de uso bater no meio, as frases seguintes viram erro de frase e a taxa cai: o Executor espera o limite voltar e repete a rodada inteira, registrando |
| `AddCommandLine` do host lê `eval` e `--providers ...` como configuração (efeito esperado: nenhum) e os testes não passam por ele (`Configuration` em memória) | O D5 real (Windows PowerShell 5.1, `Configuration` nula) prova; se o provedor de linha de comando lançar para algum formato de argumento, o Executor registra e propõe o ajuste na §12 |
| Windows PowerShell passa `ClaudeCode,Ollama` como dois argumentos | O parser aceita os dois formatos (C2, C3); o D5 roda no PowerShell 5.1 e o D11 confere os dois formatos sem chamar modelo |
| Hook de edição acusa símbolo inexistente ao criar tipo e teste juntos (AP-044) | Criar primeiro o arquivo que declara e depois o que usa; se o hook falhar logo após a primeira edição, editar o outro arquivo antes de investigar |
| `FakeTimeProvider` e `GetElapsedTime`: o tempo só anda quando o teste chama `Advance` | O gancho do `FakeTutorBrain` (T4) e o `EvalScriptedBrain` (T7) avançam o relógio dentro da chamada; R5 e B4 conferem os valores exatos |
| Quebra de linha do relatório: o arquivo usa `\n` em Windows | Os testes comparam com `\n`; o editor do usuário abre normalmente. Sem mitigação extra |
| Privacidade | As frases do golden set são de exemplo (sem dado pessoal); os relatórios ficam em `eval/reports/` (ignorado pelo Git); o log não leva o texto das frases |

## 8. Fora de escopo

- Limiar de latência ou nota mínima para o Ollama (só registrado; ver risco "Métricas só registradas").
- Comparação entre rodadas (diferença contra o relatório anterior) e relatório em outros formatos.
- Avaliação da pronúncia, de elogio (`praise`) ou de vocabulário: só as correções (RN-FEED02).
- Frases de modos `job-interview` e `listening-practice`: o golden set é de `free-conversation`.
- Teste `LiveBrain` do `eval` em si: o comando real é a DoD (D5 a D8).
- `BrainFactory.Create(provider)` público (as chaves do ADR-010 bastam).
- Mudança no `appsettings.json` (o `eval` não tem seção de configuração; sem `Options` novo, então não há `ValidateOnStart` novo, AP-039 não se aplica).

Adiamentos procurados nas P*, nos comentários de teste e nos ADRs (AP-040): a pendência "taxa de retentativa fica para a US-009" (US-008 §7 L1 e §8) é fechada por esta US (R2, R3, relatório). A pergunta do ADR-010 "o `eval` precisa de um `Create(provider)` público na fábrica?" é respondida aqui: não (§8). Nenhum adiamento novo para outra US; nenhuma alteração na spec de outra US.

## 9. Perguntas em aberto

- **P1 — Critério de "correção esperada encontrada".** Resposta padrão: algum `Corrected` da resposta contém, como palavras lado a lado, uma das formas aceitas da frase; só cobertura (ADR-011 §3). Teste: `CorrectionMatcherTests.IsFound_WithFragmentInDifferentFields_CountsOnlyTheCorrectedField`
- **P2 — Qual formato conta na nota mínima.** Resposta padrão: o formato **sem** retentativa (PRODUCT: "sem retentativa"; é o que o D6 já pede com "formato válido"); a taxa com a retentativa única aparece à parte, só informativa. Teste: `EvalRunnerTests.EvaluateAsync_WhenFirstReplyIsInvalidAndSecondIsValid_CountsValidAfterRetryButNotFirstAttempt`
- **P3 — O que é provedor `unavailable`.** Resposta padrão: a primeira frase termina em `BrainException`; as outras não rodam e o relatório traz o motivo; falha em frase posterior é erro daquela frase. Teste: `EvalRunnerTests.EvaluateAsync_WhenFirstSentenceFailsWithBrainException_ReturnsUnavailableWithReasonAndStops`
- **P4 — Código de saída e nota.** Resposta padrão: 0 sempre que o relatório é gravado (mesmo com todos `unavailable` ou `not met`); 2 para argumentos ou golden set inválidos; 1 para falha ao gravar o relatório. A nota mínima fica só no relatório. Teste: `EvalCommandTests.RunAsync_WhenCorrectionsAreMissed_ShowsNotMetInSummaryAndReport`
- **P5 — Opções além de `--providers`.** Resposta padrão: `--golden-set` e `--reports-dir`, com os padrões `eval/golden-set.json` e `eval/reports` (também servem aos testes, que usam pastas temporárias). Teste: `EvalCommandLineParserTests.Parse_WithGoldenSetAndReportsDir_UsesTheGivenPaths`
- **P6 — Quem fornece o golden set.** Resposta padrão: o rascunho da §2.9 (32 frases) é revisado pelo usuário na aprovação do plano; o Executor o grava na T8 com `reviewedBy` vazio; o usuário preenche `reviewedBy` depois de revisar o arquivo (D7). Teste: `GoldenSetFileTests.GoldenSetFile_InRepository_IsValidWithAtLeastThirtySentences`
- **P7 — D6 em 3 execuções seguidas.** Resposta padrão: sim, do comando completo do D5 (com os dois provedores); as 3 rodadas são o D5 e o D6 ao mesmo tempo (a spec passa a dizer isso no commit de spec, §2.8 (e)). Sem efeito observável (só o comando da DoD).
- **P8 — Frases independentes, sem histórico.** Resposta padrão: cada frase vai sozinha, com `ConversationService` novo (como no D5 das US-004 e US-006), para uma frase ruim não contaminar a seguinte. Teste: `EvalRunnerTests.EvaluateAsync_Always_SendsEachSentenceAloneWithTheSystemPrompt`
- **P9 — Latência inclui a retentativa; frases com erro ficam fora.** Resposta padrão: sim (é o tempo que o aluno espera; frase sem resposta não tem latência). Teste: `EvalRunnerTests.EvaluateAsync_Always_MeasuresTheLatencyOfEachSentenceIncludingTheRetry`
- **P10 — Despacho pelo primeiro argumento `eval`.** Resposta padrão: só o primeiro argumento decide; qualquer outro caso abre o chat como hoje. Teste: `EvalCommandLineParserTests.IsEvalCommand_WithDifferentFirstArguments_OnlyAcceptsEval`
- **P11 — Lugar da lógica.** Resposta padrão: `Core/Evaluation` (ADR-011 §1), com o Console só orquestrando; a Infrastructure não muda. Sem efeito observável (decisão de lugar; `CoreDependencyTests` e o D12 guardam as fronteiras).
- **P12 — Cérebro de cada provedor no `eval`.** Resposta padrão: o `ITutorBrain` com chave do provedor, sem reserva, mesmo com `Brain:FallbackProvider` configurado (ADR-010). Teste: `EvalCommandTests.RunAsync_WithFallbackConfigured_EvaluatesEachProviderBrainWithoutFallback`
- **P13 — Relatório não é versionado.** Resposta padrão: `eval/reports/` já é ignorado (`.gitignore`); a §11 copia as linhas dos provedores. Sem efeito observável.

## 10. Definition of Done (comandos verificáveis)

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes, em < 30 s; 137 casos novos (108 no Core, 29 no Console) |
| D3 | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` | Core ≥ 80% de linhas; o Executor registra o line-rate **mesclado** do assembly `EnglishTutor.Core` (`reportgenerator` sobre os três `coverage.cobertura.xml`, AP-056) e o de `Evaluation/` |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | (da spec) No Windows PowerShell 5.1, na raiz do repositório, com a CLI `claude` autenticada e o Ollama rodando com `qwen2.5:14b-instruct-q4_K_M` baixado e aquecido: `dotnet run --project src/EnglishTutor.Console -- eval --providers ClaudeCode,Ollama; "exit=$LASTEXITCODE"`, **rodado 3 vezes seguidas** (as 3 rodadas também são o D6) | em cada rodada: `Evaluating ClaudeCode (32 sentences)...`, `Evaluating Ollama (32 sentences)...`, a tabela de resumo com as duas linhas, `Report saved to <caminho>` e `exit=0`; um arquivo `eval-*.md` novo em `eval/reports/` com a seção `Details` de cada provedor |
| D6 | (da spec, §2.8 (e)) Linha do ClaudeCode no resumo dos relatórios das **3 execuções seguidas do comando completo do D5** (com os dois provedores; nenhuma rodada só do ClaudeCode) | em cada uma: `Minimum grade` = `met` (correções ≥ 80%, formato sem retentativa ≥ 90%); o Executor cola as três linhas na §11 (T9, critério e dono da decisão em §7) |
| D7 | (da spec, §2.8 (e)) `dotnet test --filter "FullyQualifiedName~GoldenSetFileTests"` e `git grep -n -F '"reviewedBy": ""' -- eval/golden-set.json` | verde. Lado positivo (T8/T9, antes de o usuário revisar): o `git grep` acha a linha `"reviewedBy": ""` (código 0), colada na §11. Lado final: depois de o usuário preencher `reviewedBy`, não acha linha (código 1); até lá o Executor registra "pendente de revisão do usuário" |
| D8 | (da spec) No Windows PowerShell 5.1: `$env:Brain__Ollama__Endpoint="http://localhost:1"; dotnet run --project src/EnglishTutor.Console -- eval --providers Ollama; "exit=$LASTEXITCODE"; Remove-Item Env:Brain__Ollama__Endpoint` | a linha do Ollama diz `unavailable`, o relatório lista o motivo (`Ollama is not running at ...`) em `Unavailable providers` e não tem `Details: Ollama`; `exit=0` |
| D9 | (da spec) No Windows PowerShell 5.1: `"/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | o cabeçalho do chat (`Brain: ... \| Mode: free-conversation`); `exit=0` |
| D10 | (da spec) No Windows PowerShell 5.1: `dotnet run --project src/EnglishTutor.Console -- eval --providers Inexistente 2>&1; "exit=$LASTEXITCODE"` | `Unknown provider 'Inexistente'. Accepted values: ClaudeCode, Ollama.`; `exit=2`; nenhum arquivo novo em `eval/reports/` |
| D11 | No Windows PowerShell 5.1: `dotnet run --project src/EnglishTutor.Console -- eval --providers ClaudeCode,Ollama --golden-set eval/nao-existe.json 2>&1; "exit=$LASTEXITCODE"` e `dotnet run --project src/EnglishTutor.Console -- eval --providers "ClaudeCode,Ollama" --golden-set eval/nao-existe.json 2>&1; "exit=$LASTEXITCODE"` | nas duas, `Golden set not found: ...nao-existe.json` e `exit=2` (os dois formatos de `--providers` passam pelo parser, sem chamar modelo e sem `Unknown provider`) |
| D12 | `git diff --name-only develop...HEAD -- src/EnglishTutor.Infrastructure` | nenhuma linha (a Infrastructure não muda) |
| D13 | (Git Bash) `git diff develop...HEAD \| LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'`, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D14 | `git status --short` depois de D2–D11 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/`, `logs/` nem de `eval/reports/` |

Notas: a US mexe no console, então D8 a D11 rodam no Windows PowerShell 5.1 e o D9 usa pipe (BOM na entrada, AP-006); o `eval` não lê a entrada padrão. Critério `LiveBrain` do D6 (AP-032, AP-052): camada = comando real, primeira chamada ao adaptador (parser e validador) mais as correções da resposta final; tolerância = limiares do PRODUCT; 3 rodadas seguidas do comando completo; a retentativa não precisa ter sido exercitada; risco de oscilação e dono da decisão (o usuário, com registro prévio na spec) em §7. D5 e D6 consomem a assinatura do Claude Code e dependem do Ollama local; sem eles o Executor registra "não reproduzido" na §11 e o usuário decide.

## 11. Evidências
_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário
_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-23 — pedido do usuário

Incorporar as sugestões S1 a S7 da revisão do plano (`tasks/US-009/plan-review.md`, rodada 1):

- S1: caso de teste para `original` ausente e para `original` em branco (G8); atualizar "Placeholders" e totais (AP-049).
- S2: testes para `ContainsWords` com fragmento sem palavras, para nomes de propriedade sensíveis a maiúsculas/propriedade desconhecida (ou remover a frase), e explicitar em M8 que uma única `Correction` satisfaz as duas esperadas.
- S3: corrigir em §7 a atribuição do 4/5 (Ollama na US-006; Claude Code só tem o mínimo de 4/5 da US-004) e citar as 2 de 5 respostas fora do formato do Ollama antes do reforço do prompt.
- S4: T8 fixa a grafia `"reviewedBy": ""`; o Executor cola na §11 a linha achada pelo `git grep` (código 0) antes de o usuário preencher.
- S5: dizer em uma frase quantas vezes o comando completo roda no D6 e alinhar o texto com a spec (§2.8 (e)).
- S6: corrigir a numeração dos casos de M8 no Red de T1 (falham o 2º e o 3º; o 1º nasce verde).
- S7: informar em qual arquivo mora `EvalCommandLineParseResult` e refletir na §3 e na árvore proposta para o `ARCHITECTURE.md`.

Este pedido não é a aprovação do plano; a aprovação continua pendente.

### Respostas do Planejador (rodada 2, 2026-10-23)

- **S1 → §2.2 (coluna "Teste" nova na tabela G1–G12), §4 (P14 novo: `original` ausente e em branco), §4 Totais e Placeholders, §5 T2 (P1–P15, 37 casos).** → Incorporado. P14 cobre os dois ramos `missing`/`blank` da G8; os ramos de tipo de `original` continuam em P13. Totais: P 33 → 37 (P14 +2, P15 +2).
- **S2 → §2.2 e §2.3 (frases ligadas a E6, P15 e M8 caso 4), §4 (E6, P15 e M8 com 4 casos; "Condicionais"), §5 T1 (E1–E6; E6 nasce verde, com mutação) e T2 (P1–P15), §10 D2.** → Incorporado. Foi mantida a frase sobre maiúsculas e propriedade desconhecida, com teste (P15, um caso para cada metade). E6: fragmento `""`, `"   "` e `"..."` devolve falso; mutação: remover a guarda "ao menos uma palavra" (a lista vazia passa a casar). M8 caso 4: uma única `Correction` satisfaz as duas esperadas. Totais: E 12 → 15, M 17 → 18, Core 100 → 108, total 129 → 137.
- **S3 → §7 (linha "Oscilação do D6").** → Incorporado, com uma correção ao texto da revisão (AP-036). Conferi `tasks/US-006/plan.md`: o 4/5 medido é do Ollama (D5, 3 rodadas iguais); o Claude Code só tem o mínimo de 4/5 da US-004. Os dados de formato do Ollama antes do reforço do prompt são "frases fora do formato por execução: 2, 0, 1, 0 e 1 de 5" (0 a 2 frases por execução, todas por contagem de perguntas), e não "2 respostas em 5 execuções" como diz a revisão; o plano cita o dado do registro de origem.
- **S4 → §5 T8 (grafia fixa da linha, conferência pelo `git grep` antes do commit) e T9, §10 D7, §2.8 (e) (novo texto do D7 da spec, com os dois lados).** → Incorporado. A linha é `  "reviewedBy": "",`; o Executor cola na §11 a linha achada (código 0) antes de o usuário preencher.
- **S5 → §5 T9, §10 D5 e D6, §7 (oscilação e consumo da assinatura: 3 rodadas completas, 96 a 192 chamadas), §9 P7, §2.8 (e) (novo texto do D6 da spec).** → Incorporado. O comando completo do D5 (com os dois provedores) roda 3 vezes seguidas; as 3 rodadas são o D5 e o D6; nenhuma rodada é só do ClaudeCode. O texto do plano e o da spec dizem o mesmo.
- **S6 → §5 T1 (Red).** → Incorporado. Falham M8 casos 2, 3 e 4; o caso 1 ("nenhuma → 0") nasce verde, com a mutação que devolve `expected.Count`.
- **S7 → §2.1, §2.7, §3 (linhas de `EvalCommandLine.cs` e `EvalCommandLineParser.cs` separadas), §2.8 Commit 2 item 2 (árvore do Console), §5 T6.** → Incorporado. `EvalCommandLineParseResult` mora em `EvalCommandLineParser.cs`; a árvore do `ARCHITECTURE.md` passa a citá-lo.

Conferência de contagens (AP-025, AP-046, AP-049): E 15, M 18, P 37, X 12, R 12, B 13, F 1 = 108 (Core); C 18, A 11 = 29 (Console); total 137. A §4, a T2 ("37 casos"), a T5 ("13 casos") e o D2 ("137 casos novos (108 no Core, 29 no Console)") batem; não restou "129", "100 casos" nem "33 casos" fora deste histórico.

Este pedido não é a aprovação do plano; a aprovação continua pendente. Status mantido: Aguardando aprovação.

### 2026-10-23 — aprovação do plano

O usuário aprovou o plano no chat. As perguntas em aberto P1 a P13 ficam com as **respostas padrão** descritas na §9 (incluindo P2, P4, P6 e P7). O rascunho de 32 frases da §2.9 segue como está; a revisão do arquivo e o preenchimento de `reviewedBy` continuam sendo do usuário (D7). ADR-011 passou a Aceito. Alterações em `specs/sprints/SPRINT_1.md` e `ARCHITECTURE.md` aplicadas nos commits `b31db78` e `8774762`.

### Escolhas de forma do Executor

- **T2 (parser do golden set), escolhas não fixadas pelo plano:** (1) os erros saem na ordem do arquivo: erro da raiz, depois, por frase, `id`, `id` repetido, `text`, `expected` e, por item, `original`, `original` fora do texto, `accepted` e cada item de `accepted`; (2) quando o `text` da frase é inválido, as checagens G9 e G12 (que dependem do texto) são puladas naquela frase, para não gerar erro derivado; (3) elemento de `sentences` que não é objeto: `Sentence #<n> must be a JSON object (found: <kind>). Expected {"id": ..., "text": ..., "expected": [...]}.`; elemento de `expected` que não é objeto: `Sentence '<id>': expected[<i>] must be a JSON object (found: <kind>). Expected {"original": ..., "accepted": [...]}.`; item de `accepted` que não é texto: `Sentence '<id>': expected[<i>].accepted[<j>] must be a string (found: <kind>). Expected a corrected fragment.`; (4) `reviewedBy` de tipo diferente de texto ou `null` vale `""` (sem erro). Nenhuma dessas escolhas tem teste dedicado no plano (os ramos de (3) e o item de `accepted` não textual ficam sem teste).
- **T4 (`EvalRunner`), forma do gancho de relógio no `FakeTutorBrain`:** overloads `Returns(TutorReply reply, Action onCall)` e `ReturnsInvalid(string rawText, TutorReplyRejection rejection, Action onCall)`; `onCall` roda quando a chamada chega, antes da resposta (nos testes, `_clock.Advance(...)`). `EvalRunner` não escreve logs; `TimeProvider.System` entra no `AddEnglishTutorCore` por `TryAddSingleton`, porque o runner o recebe por injeção e o host do Console ainda não o registra.
- **T5 (relatório), escolhas não fixadas pelo plano:** (1) percentual com total zero (provedor `evaluated` sem frases ou sem correções esperadas) sai como `-` em vez de `0.0% (0/0)`; (2) a ordem das linhas de detalhe é a ordem de `Results` do provedor (o `EvalRunner` já a mantém igual à do arquivo); (3) a `Note` da linha de detalhe segue a precedência falha da frase, `raw fallback after retry`, `retry used`, vazio; (4) o p95 e a média da linha de resumo são as de `EvalMetrics`, arredondadas por `Math.Round(..., MidpointRounding.AwayFromZero)`; (5) o resumo de provedor `evaluated` sem nenhuma latência mostra `-` nas duas colunas de latência.
- **T7 (comando `eval` e despacho), escolhas não fixadas pelo plano:** (1) `EvalCommand` é `internal sealed`, recebe `IServiceProvider`, `AppRunOptions` e o `ILogger` do host e expõe `RunAsync(CancellationToken)`; o despacho em `EnglishTutorApp.RunSessionAsync` chama `EvalCommandLineParser.IsEvalCommand(options.Args)`; (2) o caminho do golden set nas mensagens de erro é o que o usuário passou (sem torná-lo absoluto), e o de `Report saved to` é o caminho absoluto; (3) o evento 1006 loga `Status` pelo nome do enum (`Evaluated` ou `Unavailable`) e `ElapsedMs` é o tempo medido com `TimeProvider` ao redor de `EvaluateAsync`; o 1007 loga `Path` absoluto; (4) o relatório é gravado com `File.WriteAllTextAsync` e `UTF8Encoding(false)`; (5) o teste A10 usa `--unrelated value` como `Args` e `/quit` na entrada; os testes `EvalCommandTests` registram também o `ITutorBrain` sem chave (o do ClaudeCode scriptado) porque o host o resolve na partida.
- **T7, acréscimo (decidido no Green):** `Generated (UTC)` e o nome do arquivo do relatório usam o `GetUtcNow()` lido **no início** do comando (antes de avaliar o primeiro provedor), e não ao fim; assim o nome não depende do tempo que os provedores levam (A1 espera `eval-20261023-143005.md` com o relógio falso andando 12 s).
