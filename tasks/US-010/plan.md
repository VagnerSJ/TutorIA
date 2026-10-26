# Plano — US-010 Portas de voz e áudio no Core, com configuração validada

Status: Aguardando aprovação
Branch: feature/US-010_portas-voz-audio-config
Spec: specs/sprints/SPRINT_2.md#us-010--portas-de-voz-e-áudio-no-core-com-configuração-validada
Regras: RN-TTS01, RN-TTS02, RN-STT01 (só os valores e a faixa; nenhuma regra muda); ARCHITECTURE §2 (R1, R4), §3, §4, §7, §10; ADR-004 (Options no Core, bind no composition root)

## 1. Objetivo

Criar no Core as portas e tipos de voz e áudio (`ISpeechToText`, `ITextToSpeech`, `IAudioRecorder`, `IAudioPlayer`, `AudioClip`, `Transcript`, `TranscribedWord`) e as opções `Tts`, `Stt` e `Voice` com validação na partida, mais fakes de teste, para as US de voz só acrescentarem adaptadores. Nenhum comportamento do chat muda.

## 2. Abordagem

Sem ADR novo: os contratos já estão em ARCHITECTURE §4 e a validação segue o padrão do ADR-004 (um `IValidateOptions<T>` por classe, registrado por `AddEnglishTutorCore`, bind no composition root). Nenhuma decisão técnica nova.

### 2.1 Tipos (todos no Core, BCL apenas)

`Core/Audio`:
```csharp
public sealed record AudioClip            // PCM mono, float32 normalizado em [-1, 1]
{
    public AudioClip(int sampleRate, ReadOnlyMemory<float> samples);   // valida (2.2)
    public int SampleRate { get; }                                     // get-only: sem init, `with` não altera
    public ReadOnlyMemory<float> Samples { get; }                      // get-only; o chamador não altera o buffer depois de criar
    public TimeSpan Duration { get; }                                  // Samples.Length / SampleRate
}
public interface IAudioRecorder { Task<AudioClip> RecordUntilReleasedAsync(CancellationToken ct); }
public interface IAudioPlayer   { Task PlayAsync(AudioClip clip, CancellationToken ct); }
```
`Core/Speech`: `ISpeechToText`, `Transcript(string Text, IReadOnlyList<TranscribedWord> Words)`, `TranscribedWord(string Word, float Confidence)` e `ITextToSpeech` exatamente como ARCHITECTURE §4 (sem mudança de assinatura). `Transcript` e `TranscribedWord` ficam sem validação nesta US (a spec só pede validação do `AudioClip`; P7).

Decisão S1 (`with` e igualdade): `SampleRate` e `Samples` são propriedades somente de leitura (sem `init`, atribuídas só no construtor), então `clip with { SampleRate = 0 }` não compila e nenhum caminho contorna a validação. A igualdade gerada pelo `record` compara `SampleRate` e a referência do `ReadOnlyMemory` (não o conteúdo): testes e fakes comparam por `BeSameAs` ou por `Samples.ToArray()` e `SampleRate`, nunca `Be(clip)` esperando igualdade por valor. `FakeAudioPlayer.Played` e `FakeSpeechToText` guardam a mesma instância recebida.

Opções (`Core/Speech`, seções `Tts`, `Stt`, `Voice`, `SectionName` constante como `ConversationOptions`):

| Classe | Propriedade | Padrão | Validador |
|---|---|---|---|
| `TtsOptions` | `float Speed` (`MinSpeed` 0.5, `MaxSpeed` 1.5, `DefaultSpeed` 1.0) | 1.0 | `TtsOptionsValidator`: falha se `Speed` for NaN, < 0.5 ou > 1.5 (limites inclusivos) |
| `SttOptions` | `float LowConfidenceThreshold` | 0.6 | `SttOptionsValidator`: falha se NaN, < 0 ou > 1 (P1) |
| `VoiceOptions` | `bool Enabled` | `false` | nenhum (booleano não tem faixa) |

Esqueleto dos validadores (S2; o Executor cria antes do Red, nas T3 e T4): `sealed class TtsOptionsValidator : IValidateOptions<TtsOptions>` e `SttOptionsValidator : IValidateOptions<SttOptions>`, com `Validate(string? name, T options)` devolvendo sempre `ValidateOptionsResult.Success`. Assim os testes de faixa falham pelo comportamento (o valor inválido passa) e não por erro de compilação.

Mensagens (inglês, dizem o que está errado e o que se espera; números sempre com `CultureInfo.InvariantCulture` e formato `0.0##`, para "0,5" nunca aparecer com cultura pt-BR):

| Condição | Mensagem | Teste (§4) |
|---|---|---|
| `Tts:Speed` fora da faixa ou NaN | `Tts:Speed must be between 0.5 and 1.5 (found {x}). Expected a playback speed multiplier; the default is 1.0.` | `Validate_WhenSpeedIsOutOfRange_FailsWithRangeAndFoundValue` (0.49, 1.51, 3.0, NaN) |
| `Stt:LowConfidenceThreshold` fora de 0..1 ou NaN | `Stt:LowConfidenceThreshold must be between 0 and 1 (found {x}). Expected the confidence below which a word counts as low confidence; the default is 0.6.` | `Validate_WhenThresholdIsOutOfRange_FailsWithRangeAndFoundValue` (-0.01, 1.01, 1.5, NaN) |

`{x}` com `Speed = 3.0` vira `3.0`; com NaN vira `NaN`. Teste de cultura: `Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot` (pt-BR) nos dois validadores. Nos dois lados do limite (AP-016): 0.49 falha / 0.5 passa; 1.5 passa / 1.51 falha; -0.01 falha / 0 passa; 1 passa / 1.01 falha. Não há valor textual aceito sem diferenciar maiúsculas nestas opções, então a normalização de AP-039 não se aplica.

### 2.2 `AudioClip` inválido

Construtor lança (mensagens dizem o que está errado e o que se espera, sem texto de biblioteca):
- `sampleRate <= 0`: `ArgumentOutOfRangeException` com "sampleRate must be greater than zero (found {n}). Expected the sample rate in Hz, for example 16000." (0 e negativo).
- `samples.IsEmpty` (inclui `default`): `ArgumentException` com "samples must not be empty. Expected at least one sample of mono PCM audio."

O teste confere os trechos `must be greater than zero`, `found 0`, `Expected` e `must not be empty` (texto escrito pelo projeto, AP-015; o `ArgumentException.Message` acrescenta "(Parameter ...)", por isso a asserção é `Contain`, não igualdade).

### 2.3 Registro e composition root

- `AddEnglishTutorCore` registra `TryAddEnumerable` de `TtsOptionsValidator` e `SttOptionsValidator` (cada projeto registra os validadores das próprias Options, `padroes-codigo`).
- `EnglishTutorApp.BuildHost` faz o bind de `Tts`, `Stt` (ambos com `ValidateOnStart`) e `Voice` (com `ValidateOnStart`, sem validador). Duas seções inválidas juntas chegam como `AggregateException`, que `ValidationFailures` já trata (AP-039): o console lista todas as falhas e sai com 2. Nada mais muda em `EnglishTutorApp`.
- `appsettings.json` ganha `"Voice": { "Enabled": false }`, `"Tts": { "Speed": 1.0 }`, `"Stt": { "LowConfidenceThreshold": 0.6 }` (P5), e um teste garante que o arquivo distribuído coincide com os padrões das classes.
- `Voice:Enabled` não é lido por ninguém nesta US (P2): voz ligada ou desligada, o chat é o mesmo.

### 2.4 Fakes de teste

`Core.Tests/Fakes`: `FakeSpeechToText` (devolve `Transcript` programado, guarda os clipes recebidos), `FakeTextToSpeech` (guarda texto e velocidade, devolve clipe programado), `FakeAudioRecorder` (devolve clipe programado), `FakeAudioPlayer` (guarda os clipes tocados). Seguem o estilo de `FakeTutorBrain` (`Returns(...)` encadeável, lista `Requests`/`Played`). Sem `Throws`: nenhuma US atual precisa (YAGNI; a US-011 acrescenta se precisar).

### 2.5 Sincronia do filtro de teste (spec: "atualiza CLAUDE.md e ARCHITECTURE §10")

`CLAUDE.md` (seção Comandos, linhas com `Category!=LiveBrain&Category!=LiveVoice`) e `ARCHITECTURE.md` §10 já estão no filtro novo (commit `9f08858`); a US só confere (D9). Ainda no filtro antigo, e por isso corrigidos na T6 e no commit de docs (texto antigo citado, AP-023):

| Arquivo | Texto antigo (citado) | Texto novo |
|---|---|---|
| `.claude/hooks/post-edit-dotnet.mjs` | `"--filter", "Category!=LiveBrain"` | `"--filter", "Category!=LiveBrain&Category!=LiveVoice"` |
| `.claude/hooks/executor-stop-gate.mjs` | `"--filter", "Category!=LiveBrain"` | `"--filter", "Category!=LiveBrain&Category!=LiveVoice"` |
| `.github/workflows/ci.yml` | `run: dotnet test --no-build --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage" --results-directory TestResults` (e o nome do passo `Test (without LiveBrain) with coverage`) | mesmo comando com `--filter "Category!=LiveBrain&Category!=LiveVoice"`; passo `Test (without LiveBrain and LiveVoice) with coverage` |
| `README.md` | `dotnet test --filter "Category!=LiveBrain"` | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice"` |
| `.claude/agents/revisor.md` | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` | idem com `"Category!=LiveBrain&Category!=LiveVoice"` |
| `.claude/agents/executor.md` | `dotnet test --filter "Category!=LiveBrain"` (duas linhas: o exemplo `D2 — ...` e a linha de fim de trabalho) | idem com o filtro novo |
| `.claude/agents/planejador.md` | linhas D2 e D3 do modelo da DoD com `Category!=LiveBrain` | idem com o filtro novo |
| `.claude/skills/checklist-revisao/SKILL.md` | `` `dotnet test --filter "Category!=LiveBrain"` verde `` | idem com o filtro novo |
| `.claude/skills/tdd-dotnet/SKILL.md` | `excluídas no CI com --filter "Category!=LiveBrain"` | `excluídas no CI com --filter "Category!=LiveBrain&Category!=LiveVoice"` |
| `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs` (comentário do cabeçalho, linha 5) | `live tests stay out of Category!=LiveBrain, and nothing else` | `live tests stay out of the default run (Category!=LiveBrain&Category!=LiveVoice), and nothing else` |
| idem (`because:` de `LiveBrainTrait_OnlyOnClassesNamedLiveTests`, linha 30) | `"the trait would hide a fast test from Category!=LiveBrain"` | `"the trait would hide a fast test from the default run (Category!=LiveBrain&Category!=LiveVoice)"` |

As duas últimas linhas vieram do `git grep` do D8 rodado na solução inteira, incluindo `tests/` (B1). São só texto de comentário e de mensagem de asserção, sem mudança de comportamento; a T6 as altera num commit próprio `test(brain): update default test filter in trait guard text`. Os testes `LiveTests_Always_CarryTheLiveBrainTrait` e `LiveBrainTrait_OnlyOnClassesNamedLiveTests` não são `LiveBrain` e seguem verdes.

Nota S4: a linha da tabela "Live" em `ARCHITECTURE.md` §10 abre com `excluídas por padrão: dotnet test --filter "Category!=LiveBrain"` e traz o filtro novo na mesma linha. Foi vista e mantida de propósito (descreve o que `LiveBrain` exclui e acrescenta o `LiveVoice`); o D8 a ignora porque a linha contém `Category!=LiveVoice`.

Arquivos de histórico não mudam: `specs/archive/SPRINT_1.md`, `docs/us/US-004.md`, `docs/us/US-008.md`, `CHANGELOG.md`, `docs/adr/ADR-004-...md` (descrevem o que valia na época). Os arquivos de `.claude/` e `README.md` entram num commit `docs(ai): update test filter for US-010` do orquestrador antes da T1 (só texto); `ci.yml` e os dois hooks entram na T6 como `chore(infra)` porque são código/infra executável e o Executor valida o hook; o arquivo de teste entra na T6 como `test(brain)`. Os hooks passam argumentos como lista (sem shell); se `&` for reinterpretado, o Executor registra em §12 e ajusta as aspas conforme o spawn.

Nota S5 (quando o D8 reproduz as 13): os commits de docs entram antes da T1 e a T6 só depois, então o D8 "antes" só reproduz as 13 ocorrências contra o commit anterior ao de docs (ex.: `git grep ... 9e35333`, no lugar do working tree). No estado da branch logo após os commits de docs, restam 5 ocorrências (`ci.yml`, os 2 hooks e as 2 de `LiveBrainTraitTests.cs`) até a T6. A revisão de código e o Executor conferem assim.

### 2.6 Mudança em `ARCHITECTURE.md` (commit `docs(ai): update architecture for US-010`, antes da T1)

1. §3, linha `Speech/` do Core. Texto antigo: `Speech/            ISpeechToText, Transcript, TranscribedWord, ITextToSpeech`. Texto novo: `Speech/            ISpeechToText, Transcript, TranscribedWord, ITextToSpeech, VoiceOptions, TtsOptions, TtsOptionsValidator, SttOptions, SttOptionsValidator`.
2. §4, depois da linha `public interface IAudioPlayer   { Task PlayAsync(AudioClip clip, CancellationToken ct); }`, acrescentar: `// AudioClip: PCM mono float32 em [-1, 1] com SampleRate; o construtor rejeita taxa <= 0 e clipe sem amostras (US-010).`
3. §7, depois do item `- Áudio: NAudio (captura 16 kHz mono para o Whisper; reprodução).`, acrescentar: `- Configuração (Core, validada na partida): Voice:Enabled (padrão false), Tts:Speed (0.5 a 1.5, padrão 1.0), Stt:LowConfidenceThreshold (0 a 1, padrão 0.6). Valor fora da faixa encerra com código 2 e a mensagem diz a faixa.`

Sincronia (Grep em `CLAUDE.md` e `.claude/` por `Speech/`, `Audio/`, `TranscribedWord`, `Tts:Speed`, `LowConfidenceThreshold`): nenhuma ocorrência fora do `ARCHITECTURE.md` e das specs, então não há linha do `CLAUDE.md` nem de skill a mudar por esses três itens. A sincronia do filtro está em §2.5.

Spec: nenhuma mudança de cenário ou regra. `Audio:KeepRecordings` e `Voice:PushToTalkKey` já estão na spec da US-013 e ficam lá.

Alternativas descartadas: `AudioClip` com `byte[]` PCM16 (a conversão para float do Whisper viraria trabalho de todo adaptador); `AudioClip` com canais (Piper e Whisper são mono, YAGNI); validar a faixa de `Speed` no adaptador Piper (violaria R4); `IOptionsMonitor` para `/speed` em sessão (a US-012 decide, aqui só leitura na partida).

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `src/EnglishTutor.Core/Audio/AudioClip.cs` | clipe PCM mono com validação |
| Criar | `src/EnglishTutor.Core/Audio/IAudioRecorder.cs`, `IAudioPlayer.cs` | portas de áudio |
| Criar | `src/EnglishTutor.Core/Speech/ISpeechToText.cs`, `Transcript.cs`, `TranscribedWord.cs`, `ITextToSpeech.cs` | portas e tipos de voz |
| Criar | `src/EnglishTutor.Core/Speech/TtsOptions.cs`, `TtsOptionsValidator.cs`, `SttOptions.cs`, `SttOptionsValidator.cs`, `VoiceOptions.cs` | opções e validação |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | registra os dois validadores |
| Alterar | `src/EnglishTutor.Console/EnglishTutorApp.cs` | bind de `Tts`, `Stt`, `Voice` |
| Alterar | `src/EnglishTutor.Console/appsettings.json` | seções com os padrões |
| Criar | `tests/EnglishTutor.Core.Tests/Audio/AudioClipTests.cs` | cenário "clipe inválido" |
| Criar | `tests/EnglishTutor.Core.Tests/Architecture/VoicePortsTests.cs` | cenário "portas existem" |
| Criar | `tests/EnglishTutor.Core.Tests/Speech/TtsOptionsTests.cs`, `TtsOptionsValidatorTests.cs`, `SttOptionsTests.cs`, `SttOptionsValidatorTests.cs`, `VoiceOptionsTests.cs` | padrões e faixas |
| Criar | `tests/EnglishTutor.Core.Tests/Fakes/FakeSpeechToText.cs`, `FakeTextToSpeech.cs`, `FakeAudioRecorder.cs`, `FakeAudioPlayer.cs`, `tests/EnglishTutor.Core.Tests/Fakes/FakeVoicePortsTests.cs` | fakes e seu teste |
| Alterar | `tests/EnglishTutor.Core.Tests/DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | registro dos validadores |
| Alterar | `tests/EnglishTutor.Console.Tests/EnglishTutorAppTests.cs`, `AppSettingsTests.cs` | partida com voz |
| Criar | `tests/EnglishTutor.Console.Tests/Support/OptionsProbe.cs` | `IHostedService` de teste que captura as Options resolvidas (via `AppRunOptions.ConfigureServices`) |
| Alterar | `.claude/hooks/post-edit-dotnet.mjs`, `.claude/hooks/executor-stop-gate.mjs`, `.github/workflows/ci.yml` | filtro novo (T6) |
| Alterar | `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs` | só comentário e `because:` com o filtro novo (T6) |
| Alterar (orquestrador, docs) | `ARCHITECTURE.md`, `README.md`, `.claude/agents/{revisor,executor,planejador}.md`, `.claude/skills/{checklist-revisao,tdd-dotnet}/SKILL.md` | §2.5 e §2.6 |

## 4. Mapa cenário BDD → testes

Todos em xUnit; nenhum teste usa rede, GPU, microfone, alto-falante ou processo real.

| Cenário | Projeto de teste | Classe | Método | Tipo |
|---|---|---|---|---|
| Portas de voz existem no Core sem pacote externo | Core.Tests | `VoicePortsTests` | `CoreAssembly_Always_ExposesVoiceType` (Theory: `ISpeechToText`, `ITextToSpeech`, `IAudioRecorder`, `IAudioPlayer` como interface; `AudioClip`, `Transcript` como tipo selado; confere o namespace) | Unidade (reflexão) |
| (idem, apoio: regra `CancellationToken` último) | Core.Tests | `VoicePortsTests` | `VoicePorts_AsyncMethods_TakeCancellationTokenAsLastParameter` (Theory sobre as 4 interfaces) | Unidade (reflexão) |
| (idem) E continua sem pacotes fora da lista | Core.Tests | `CoreDependencyTests` | `CoreAssembly_References_DoNotIncludeInfrastructureOrConsole`, `CoreAssembly_References_OnlyBclAndAllowedMicrosoftExtensions`, `CoreProject_PackageAndProjectReferences_OnlyAllowedOnes` (existentes, sem mudança; nascem verdes, D5 prova com as portas novas) | Arquitetura |
| Opções de voz inválidas falham na inicialização | Core.Tests | `TtsOptionsValidatorTests` | `Validate_WhenSpeedIsOutOfRange_FailsWithRangeAndFoundValue` (Theory 0.49, 1.51, 3.0, NaN) | Unidade |
| (idem, nos dois lados do limite) | Core.Tests | `TtsOptionsValidatorTests` | `Validate_WhenSpeedIsInRange_Succeeds` (Theory 0.5, 1.0, 1.5) | Unidade |
| (idem, cultura) | Core.Tests | `TtsOptionsValidatorTests` | `Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot` | Unidade |
| (idem, Stt) | Core.Tests | `SttOptionsValidatorTests` | `Validate_WhenThresholdIsOutOfRange_FailsWithRangeAndFoundValue` (Theory -0.01, 1.01, 1.5, NaN) | Unidade |
| (idem, Stt, dois lados) | Core.Tests | `SttOptionsValidatorTests` | `Validate_WhenThresholdIsInRange_Succeeds` (Theory 0, 0.6, 1) | Unidade |
| (idem, Stt, cultura) | Core.Tests | `SttOptionsValidatorTests` | `Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot` | Unidade |
| (idem, registro) | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_Always_RegistersTtsOptionsValidator`, `AddEnglishTutorCore_Always_RegistersSttOptionsValidator` | Unidade |
| (idem, de ponta a ponta: código 2 e mensagem na saída de erro) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenTtsSpeedIsOutOfRange_ExitsWithInvalidConfigurationBeforePrompt` (`Tts:Speed` = 3.0; `ExitCodes.InvalidConfiguration`; erro contém `Tts:Speed must be between 0.5 and 1.5 (found 3.0)`; saída padrão vazia) | Componente (em processo) |
| (idem, duas seções inválidas ao mesmo tempo, AP-039) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WhenTtsAndSttAreBothInvalid_ShowsBothFailures` (`Tts:Speed` 3.0 e `Stt:LowConfidenceThreshold` 1.5; código 2; as duas mensagens; sem "An unexpected error occurred") | Componente |
| Valores padrão | Core.Tests | `TtsOptionsTests`, `SttOptionsTests`, `VoiceOptionsTests` | `TtsOptions_WhenCreated_DefaultsSpeedToOne`, `SttOptions_WhenCreated_DefaultsThresholdToPointSix`, `VoiceOptions_WhenCreated_IsDisabled` (nascem verdes ao criar a classe; Red por mutação do efeito, ver T3 e T4) | Unidade |
| (idem, de ponta a ponta) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WithoutVoiceSections_BindsDefaultsAndKeepsTextChat` (config sem `Tts`/`Stt`/`Voice`; `OptionsProbe` vê 1.0, 0.6, false; cabeçalho começa com as 3 linhas do US-001; exit 0 com `/quit`) | Componente |
| (idem, o bind lê a configuração) | Console.Tests | `EnglishTutorAppTests` | `RunAsync_WithVoiceValues_BindsThemFromConfiguration` (`Tts:Speed` 0.8, `Stt:LowConfidenceThreshold` 0.7, `Voice:Enabled` true; o probe vê esses valores; exit 0; cabeçalho igual ao padrão) | Componente |
| (idem, arquivo distribuído) | Console.Tests | `AppSettingsTests` | `AppSettings_WhenShipped_DeclaresVoiceDefaultsEqualToOptionDefaults` | Componente |
| Clipe de áudio inválido | Core.Tests | `AudioClipTests` | `Constructor_WhenSampleRateIsNotPositive_ThrowsWithExpectation` (Theory 0, -1) | Unidade |
| (idem) | Core.Tests | `AudioClipTests` | `Constructor_WhenThereAreNoSamples_ThrowsWithExpectation` (Theory: array vazio, `default`) | Unidade |
| (idem, lado válido do limite) | Core.Tests | `AudioClipTests` | `Constructor_WithSamples_ExposesSampleRateSamplesAndDuration` (Theory 16000/16000 → 1 s; 16000/8000 → 0,5 s; 22050/22050 → 1 s; 16000 com 1 amostra) | Unidade |
| Apoio: fakes (a spec diz "com fakes de teste") | Core.Tests | `FakeVoicePortsTests` | `FakeSpeechToText_WhenCalled_RecordsClipAndReturnsScriptedTranscript`, `FakeTextToSpeech_WhenCalled_RecordsTextAndSpeedAndReturnsClip`, `FakeAudioRecorder_WhenCalled_ReturnsScriptedClip`, `FakeAudioPlayer_WhenCalled_RecordsClip` | Unidade |

Cada afirmação condicional da §2 tem teste: faixa (dois lados), NaN, cultura, duas seções inválidas, padrões. Não há `<placeholder>` derivado de texto de biblioteca; os números das mensagens usam a cultura invariável, com teste em pt-BR. Os cenários não usam janela, histórico nem retentativa (AP-017/AP-026 não se aplicam). Não existe renomeação de cenário nem de texto de comportamento nesta US; a troca de filtro (§2.5) é troca de texto de comando, conferida por D8 (inventário nominal de 13 arquivos:linha, incluindo `tests/`).

Ao fechar a lista de Red (AP-054/AP-046): nascem verdes `CoreDependencyTests` (3, existentes), `TtsOptions_WhenCreated_DefaultsSpeedToOne`, `SttOptions_WhenCreated_DefaultsThresholdToPointSix`, `VoiceOptions_WhenCreated_IsDisabled`, `RunAsync_WithoutVoiceSections_BindsDefaultsAndKeepsTextChat` (as Options sem configuração já têm os padrões) e `FakeVoicePortsTests` (o fake nasce junto do teste). Os demais falham de verdade: `RunAsync_WhenTtsSpeedIsOutOfRange_...`, `RunAsync_WhenTtsAndSttAreBothInvalid_...` e `RunAsync_WithVoiceValues_...` falham até o bind existir (o 3.0 não chega ao validador, o probe vê 1.0).

## 5. Tarefas (ordem de execução TDD)

Antes da T1 o orquestrador aplica os dois commits de documentação (`docs(ai): update architecture for US-010` com §2.6 e `docs(ai): update test filter for US-010` com as linhas de `.claude/` e `README.md` da §2.5); não há commit de spec (sem mudança de cenário ou regra).

- [ ] T1 — `AudioClip`. Red: `AudioClipTests.Constructor_WhenSampleRateIsNotPositive_ThrowsWithExpectation`, `AudioClipTests.Constructor_WhenThereAreNoSamples_ThrowsWithExpectation`, `AudioClipTests.Constructor_WithSamples_ExposesSampleRateSamplesAndDuration` (o tipo ainda não existe: o Executor cria primeiro o esqueleto sem validação e com `Duration` zerada, para o Red falhar por comportamento e não por compilação) / Green: validação e `Duration` / Refactor: constantes de mensagem. (commit: `test(core): add AudioClip validation tests` + `feat(core): add AudioClip with validation`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T2 — Portas, tipos e fakes. Red: `VoicePortsTests.CoreAssembly_Always_ExposesVoiceType`, `VoicePortsTests.VoicePorts_AsyncMethods_TakeCancellationTokenAsLastParameter`, `FakeVoicePortsTests.FakeSpeechToText_WhenCalled_RecordsClipAndReturnsScriptedTranscript`, `FakeVoicePortsTests.FakeTextToSpeech_WhenCalled_RecordsTextAndSpeedAndReturnsClip`, `FakeVoicePortsTests.FakeAudioRecorder_WhenCalled_ReturnsScriptedClip`, `FakeVoicePortsTests.FakeAudioPlayer_WhenCalled_RecordsClip`; os 3 `CoreDependencyTests` seguem verdes (nascem verdes) / Green: `ISpeechToText`, `ITextToSpeech`, `IAudioRecorder`, `IAudioPlayer`, `Transcript`, `TranscribedWord` e os 4 fakes / Refactor: nenhum. (commit: `test(core): add voice port architecture tests` + `feat(core): add voice and audio ports with fakes`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T3 — `TtsOptions` e validador. O Executor cria antes o esqueleto (S2, §2.1): `TtsOptions` com `Speed = 1.0f` e `TtsOptionsValidator` que sempre devolve `Success`, sem registro no `AddEnglishTutorCore`, para o Red falhar por comportamento e não por compilação (AP-010). Red: `TtsOptionsValidatorTests.Validate_WhenSpeedIsOutOfRange_FailsWithRangeAndFoundValue`, `TtsOptionsValidatorTests.Validate_WhenSpeedIsInRange_Succeeds`, `TtsOptionsValidatorTests.Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot`, `CoreServiceCollectionExtensionsTests.AddEnglishTutorCore_Always_RegistersTtsOptionsValidator`; `TtsOptionsTests.TtsOptions_WhenCreated_DefaultsSpeedToOne` nasce verde, Red por mutação temporária pelo efeito ("o padrão deixa de ser 1.0": o teste falha; depois reverte); `Validate_WhenSpeedIsInRange_Succeeds` também nasce verde com o esqueleto (ele sempre devolve `Success`) e o Red dele é a mutação "o limite máximo passa a 1.4" / Green: classes e registro / Refactor: formatador de número compartilhado com a T4 se surgir duplicação. (commit: `test(core): add Tts options validation tests` + `feat(core): validate Tts options`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T4 — `SttOptions`, `VoiceOptions` e validador do Stt. Esqueleto antes do Red, como na T3: `SttOptions` (0.6), `VoiceOptions` (`false`) e `SttOptionsValidator` que sempre devolve `Success`, sem registro. Red: `SttOptionsValidatorTests.Validate_WhenThresholdIsOutOfRange_FailsWithRangeAndFoundValue`, `SttOptionsValidatorTests.Validate_WhenThresholdIsInRange_Succeeds`, `SttOptionsValidatorTests.Validate_WhenCurrentCultureUsesComma_WritesNumbersWithDot`, `CoreServiceCollectionExtensionsTests.AddEnglishTutorCore_Always_RegistersSttOptionsValidator`; `SttOptionsTests.SttOptions_WhenCreated_DefaultsThresholdToPointSix` e `VoiceOptionsTests.VoiceOptions_WhenCreated_IsDisabled` nascem verdes, Red por mutação pelo efeito ("o padrão deixa de ser 0.6", "o padrão deixa de ser false"); `Validate_WhenThresholdIsInRange_Succeeds` também nasce verde com o esqueleto, Red por mutação ("o limite máximo passa a 0.9") / Green: classes e registro / Refactor: extrair o helper de faixa, se houver. (commit: `test(core): add Stt and Voice options tests` + `feat(core): validate Stt options and add Voice options`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T5 — Composition root e `appsettings.json`. Red: `OptionsProbe` (suporte) + `EnglishTutorAppTests.RunAsync_WhenTtsSpeedIsOutOfRange_ExitsWithInvalidConfigurationBeforePrompt`, `EnglishTutorAppTests.RunAsync_WhenTtsAndSttAreBothInvalid_ShowsBothFailures`, `EnglishTutorAppTests.RunAsync_WithVoiceValues_BindsThemFromConfiguration`, `AppSettingsTests.AppSettings_WhenShipped_DeclaresVoiceDefaultsEqualToOptionDefaults`; `EnglishTutorAppTests.RunAsync_WithoutVoiceSections_BindsDefaultsAndKeepsTextChat` nasce verde, Red por mutação pelo efeito ("o padrão de `Voice:Enabled` passa a true" na classe: o probe vê true e o teste falha; depois reverte) / Green: bind no `BuildHost` e seções no appsettings / Refactor: agrupar os binds de voz num método privado de `EnglishTutorApp` se a leitura ficar melhor. (commit: `test(console): add voice configuration startup tests` + `feat(console): bind and validate voice options at startup`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T6 — Filtro de testes na infra e no texto do guarda do trait. Sem teste novo (configuração executável e texto). Edita `ci.yml` e os dois hooks (§2.5) e, em commit separado, o comentário e o `because:` de `LiveBrainTraitTests.cs` (§2.5); verifica que o hook ainda roda os testes depois de uma edição trivial de `.cs` (evidência em §11), que `LiveBrainTraitTests` segue verde e que o D8 devolve nenhuma linha. (commits: `chore(infra): exclude LiveVoice tests from default runs` + `test(brain): update default test filter in trait guard text`)
  Escolhas de forma: nenhuma | §12 em <hash>

## 6. Pacotes NuGet

Nenhum. O Core usa só BCL e `Microsoft.Extensions.Options` (já referenciado). `System.Globalization` é BCL.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| `&` no filtro dos hooks ser reinterpretado | T6 testa uma edição real; ajuste registrado em §12 |
| `float` na configuração: "3.0" lido com a cultura da máquina | o binder usa cultura invariável; `RunAsync_WhenTtsSpeedIsOutOfRange_...` usa "3.0" e a mensagem sai com ponto |
| `Voice:Enabled` com texto inválido (ex.: `abc`) | o binder lança fora de `OptionsValidationException`, o console responde "unexpected error" com código 1, como as demais seções; limite aceito. Consumidores do limite: validador (não vê o valor), testes `LiveBrain` (não tocados), DoD (D6 e D7 usam valores válidos). Sem teste (P6) |
| `ReadOnlyMemory<float>` compartilhado: o dono do buffer altera depois | documentado no tipo; os adaptadores (US-011 e US-013) criam um buffer por clipe |
| Teste de cultura mudar `CurrentCulture` e afetar outros testes | testes síncronos; a cultura é por thread e restaurada em `finally` |

## 8. Fora de escopo

Adaptadores (Piper, Whisper, NAudio), uso de `Voice:Enabled` pelo console, `Audio:KeepRecordings`, `Voice:PushToTalkKey`, `/speed`, `/mic`, mudanças no `ConversationService`, `TurnMessageBuilder` e prompts. Nenhum item depende de texto novo em outra US (`Audio:KeepRecordings` e `Voice:PushToTalkKey` já constam da spec da US-013). Sem mudança de cérebro, prompt ou parser: o `eval` não entra na DoD.

## 9. Perguntas em aberto

- **P1** — Faixa de `Stt:LowConfidenceThreshold`. A spec só fixa o padrão 0,6. Resposta padrão: 0 a 1, inclusivos (confiança é probabilidade). Teste: `SttOptionsValidatorTests.Validate_WhenThresholdIsOutOfRange_FailsWithRangeAndFoundValue`.
- **P2** — `Voice:Enabled=true` antes da US-011. Resposta padrão: não é lido por nada; o chat não muda (o cenário "valores padrão" só cobre `false`). Teste: `EnglishTutorAppTests.RunAsync_WithVoiceValues_BindsThemFromConfiguration` (cabeçalho igual ao padrão com `true`).
- **P3** — Formato do `AudioClip`. Resposta padrão: mono, float32 em [-1, 1], `ReadOnlyMemory<float>`; o adaptador converte de/para PCM16. Teste: `AudioClipTests.Constructor_WithSamples_ExposesSampleRateSamplesAndDuration`.
- **P4** — `Tts:Speed` NaN. Resposta padrão: rejeitado com a mesma mensagem (`found NaN`). Teste: `TtsOptionsValidatorTests.Validate_WhenSpeedIsOutOfRange_FailsWithRangeAndFoundValue`.
- **P5** — O `appsettings.json` distribuído declara as três seções com os padrões. Resposta padrão: sim, para o aluno ver as chaves. Teste: `AppSettingsTests.AppSettings_WhenShipped_DeclaresVoiceDefaultsEqualToOptionDefaults`.
- **P6** — `Voice:Enabled` com valor não booleano. Resposta padrão: herda o comportamento do binder (código 1), sem tratamento novo. Sem efeito observável (nenhum teste novo; é o comportamento das demais seções).
- **P7** — Validar `Transcript`/`TranscribedWord` (confiança em [0, 1]). Resposta padrão: não validar nesta US, a spec não pede; os tipos ficam como no ARCHITECTURE §4. Sem efeito observável.

## 10. Definition of Done (comandos verificáveis)

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice"` | todos verdes, em < 30 s, sem rede, sem GPU, sem microfone e sem alto-falante |
| D3 | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice" --collect:"XPlat Code Coverage"` | Core ≥ 80% de linhas; evidência = relatório mesclado (`reportgenerator`), com `line-rate` do assembly Core e total da solução; relatório de um projeto só como nota (AP-056) |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test --filter "FullyQualifiedName~CoreDependencyTests"` | verde com as portas novas |
| D6 | No Windows PowerShell 5.1: `$env:Tts__Speed="3.0"; dotnet run --project src/EnglishTutor.Console 2>&1; "exit=$LASTEXITCODE"; Remove-Item Env:Tts__Speed` | a saída contém `Tts:Speed must be between 0.5 and 1.5`; `exit=2` |
| D7 | No Windows PowerShell 5.1: `"/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | a saída contém o cabeçalho do chat (`EnglishTutor`, `Brain: ...`); `exit=0` |
| D8 | (Git Bash) `git grep -n 'Category!=LiveBrain' -- . ':!specs/archive' ':!docs/us' ':!docs/adr' ':!CHANGELOG.md' ':!tasks' \| grep -v 'Category!=LiveVoice'` | antes do commit de docs e da T6, lista exatamente estas 13 ocorrências (conferidas pelo Planejador com `git grep` na solução inteira, incluindo `tests/`; evidência de que o comando acha o texto antigo): `.claude/hooks/post-edit-dotnet.mjs:49`, `.claude/hooks/executor-stop-gate.mjs:24`, `.github/workflows/ci.yml:37`, `README.md:59`, `.claude/agents/revisor.md:35`, `.claude/agents/executor.md:51`, `.claude/agents/executor.md:67`, `.claude/agents/planejador.md:97`, `.claude/agents/planejador.md:98`, `.claude/skills/checklist-revisao/SKILL.md:56`, `.claude/skills/tdd-dotnet/SKILL.md:98`, `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs:5`, `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs:30`; depois, nenhuma linha (código de saída 1 com contagem 0 é o esperado); os dois trechos de `tests/` saem na T6. Linhas que já trazem os dois filtros (`CLAUDE.md`, `ARCHITECTURE.md` §10, `specs/sprints/SPRINT_2.md`) não aparecem. Nota S4: a linha de `ARCHITECTURE.md` §10 abre com o filtro antigo, mas contém o novo, e foi mantida de propósito (§2.5). Nota S5: os commits de docs entram antes da T1 e a T6 só depois, então o "antes" só reproduz as 13 ocorrências contra o commit anterior ao de docs (ex.: `git grep -n 'Category!=LiveBrain' 9e35333 -- . ':!specs/archive' ...`); no estado da branch logo após os commits de docs, restam 5 (`ci.yml`, os 2 hooks e as 2 de `LiveBrainTraitTests.cs`) até a T6, e só depois da T6 o resultado é nenhuma linha. Revisão de código e Executor conferem assim |
| D9 | `git grep -c 'Category!=LiveBrain&Category!=LiveVoice' -- CLAUDE.md ARCHITECTURE.md` | contagem ≥ 1 em cada um dos dois arquivos |
| D10 | `git diff develop...HEAD \| LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'` | contagem 0 (código de saída 1 é o esperado) |
| D11 | `dotnet test --no-build --list-tests --filter "FullyQualifiedName~VoicePortsTests\|FullyQualifiedName~AudioClipTests\|FullyQualifiedName~OptionsValidatorTests"` | lista os métodos da §4 com esses nomes de classe |

Não há critério `LiveBrain` nem `LiveVoice` nesta US; nenhum teste dessas categorias é criado ou alterado. A US não toca cérebro, prompt ou parser: sem `eval`.

## 11. Evidências
_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário
_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-26 — pedido do Revisor do plano (rodada 1)
- **B1 (bloqueante):** o inventário do filtro antigo `Category!=LiveBrain` (§2.5) e o D8 (§10) estão incompletos: o comando do D8 acha 13 ocorrências, não 11. Faltam as linhas 5 e 30 de `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs` (comentários do cabeçalho e do `because:`). Incluí-las no inventário e numa tarefa com commit de escopo adequado, ou excluí-las explicitamente do D8 (com o motivo) ajustando comando e contagem. Rode o comando do D8 com `git grep` na solução inteira e cole a saída no plano.

→ Incorporado. B1 → §2.5 (duas linhas novas na tabela, com texto antigo e novo citados), §3, §5 T6, §10 D8 (13 ocorrências, nominais), §4 (nota). Escolhi a primeira opção: incluir no inventário e alterar na T6 em commit `test(brain)`, porque o texto do `because:` e do comentário descreve o filtro padrão e ficaria desatualizado.

Saída do comando (busca por `Category!=LiveBrain` na solução inteira, incluindo `tests/`, sem `specs/archive`, `docs/us`, `docs/adr`, `CHANGELOG.md`, `tasks`; filtrada para tirar as linhas que já trazem `Category!=LiveVoice`), 13 linhas, conferidas com o Grep antes de fixar a contagem:

```
.claude/hooks/post-edit-dotnet.mjs:49
.claude/hooks/executor-stop-gate.mjs:24
.github/workflows/ci.yml:37
README.md:59
.claude/agents/revisor.md:35
.claude/agents/executor.md:51
.claude/agents/executor.md:67
.claude/agents/planejador.md:97
.claude/agents/planejador.md:98
.claude/skills/checklist-revisao/SKILL.md:56
.claude/skills/tdd-dotnet/SKILL.md:98
tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs:5
tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs:30
```

(Só números de linha, para economizar contexto; o Executor cola a saída completa em §11.) Observação: as buscas foram feitas com a ferramenta Grep (ripgrep) e não com `git grep` literal; o conjunto é o mesmo porque todos os arquivos são versionados.

### 2026-10-26 — sugestões do plan-review (rodada 1)
- S1 → §2.1 (decisão: `SampleRate` e `Samples` get-only, sem `init`; igualdade por referência do buffer; testes e fakes não usam `Be(clip)`).
- S2 → §2.1 (esqueleto), §5 T3 e T4 (esqueleto que devolve `Success`; como consequência, `Validate_WhenSpeedIsInRange_Succeeds` e `Validate_WhenThresholdIsInRange_Succeeds` nascem verdes e ganham Red por mutação do limite).
- S3 → §7 (última linha: "testes síncronos; a cultura é por thread e restaurada em `finally`").
- S4 → §2.5 (nota) e §10 D8 (nota curta).

### 2026-10-26 — pedido do usuário (S5 do plan-review, rodada 2)
- Incorporar a S5: acrescentar à §2.5 (último parágrafo) e ao D8 uma frase dizendo que os commits de docs entram antes da T1 e a T6 só depois, então o D8 "antes" só reproduz as 13 ocorrências contra o commit anterior ao de docs (ex.: `git grep ... 9e35333`); no estado da branch logo após os commits de docs, restam 5 (`ci.yml`, os 2 hooks e as 2 de `LiveBrainTraitTests.cs`) até a T6. Revisão de código e Executor conferem assim.

→ Incorporado. S5 → §2.5 (nota S5, depois do último parágrafo) e §10 D8 (nota S5 no fim da célula "Esperado"; o comando de exemplo contra `9e35333` e a contagem 13 / 5 / 0 por estado da branch).
