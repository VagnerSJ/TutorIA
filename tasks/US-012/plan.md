# Plano — US-012 Velocidade da fala configurável e ajustável na sessão

Status: Aguardando aprovação
Branch: feature/US-012_velocidade-da-fala
Spec: specs/sprints/SPRINT_2.md#us-012--velocidade-da-fala-configurável-e-ajustável-na-sessão
Regras: RN-TTS02, RN-CONV06 (comando local nunca vai ao cérebro); ARCHITECTURE §3, §7; ADR-012 (consequência "a velocidade vira valor mutável da sessão na US-012")

## 1. Objetivo

`Tts:Speed` passa a ser só o valor inicial da velocidade da sessão. O aluno digita `/speed <0.5 a 1.5>` para mudá-la (a próxima resposta já sai na nova velocidade), `/speed` sozinho mostra a atual e a faixa, e valor fora da faixa ou que não é número é recusado sem perder a velocidade anterior.

## 2. Abordagem

### 2.1 Onde mora a velocidade da sessão: `SpeechSpeed` no Core

A regra "faixa 0.5 a 1.5, pode mudar na sessão" é de negócio (RN-TTS02), então mora no Core (R4), não no `ChatSession` nem no adaptador. Tipos novos em `Core/Speech`:

```csharp
public enum SpeedChangeStatus { Applied, NotANumber, OutOfRange }
public sealed record SpeedChange(SpeedChangeStatus Status, float Current);   // Current = velocidade em vigor depois da chamada
public sealed class SpeechSpeed(IOptions<TtsOptions> options)
{
    public float Current { get; private set; } = options.Value.Speed;        // valor inicial = Tts:Speed (já validado na partida)
    public SpeedChange Change(string text);                                   // parse + faixa; só muda Current em Applied
    public static string Format(float speed);                                 // "0.0##", cultura invariável: 1.0 -> "1.0", 0.85 -> "0.85"
}
```

- `Change`: `float.TryParse` com `NumberStyles.AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign | AllowDecimalPoint` e `CultureInfo.InvariantCulture` (sem expoente, sem milhar). Falhou ou não finito (`NaN`, `Infinity`; o analisador de .NET pode aceitá-los, por isso `float.IsFinite` é conferido explicitamente) → `NotANumber`. Número fora de `TtsOptions.MinSpeed`..`MaxSpeed` (conferidos no código: 0.5f, 1.5f, limites inclusivos) → `OutOfRange`. Senão `Applied` e `Current` muda. Em `NotANumber` e `OutOfRange` `Current` não muda.
- Só o ponto é separador decimal (`1,2` é `NotANumber`, P2). A cultura do processo não influencia (teste em pt-BR, AP-054).
- Registro: `AddEnglishTutorCore` faz `TryAddSingleton<SpeechSpeed>()` (um por processo, como o `ConversationService`).
- Acesso: o laço do console lê e escreve em sequência (o `ChatSession` só lê a próxima linha depois do áudio, ADR-012); `float` é atômico no .NET. Uma interface futura com threads diferentes precisa revisar (limite da §7).
- Sem ADR novo: é a consequência já anotada no ADR-012, sem alternativa de peso. Alternativas descartadas: (a) estado mutável dentro do `ReplySpeaker` (o `ChatSession` precisaria do speaker, que não existe com voz desligada e deixaria `/speed` sem dono); (b) mutar `IOptions<TtsOptions>.Value.Speed` (esconde estado de sessão em configuração e pula a faixa); (c) o `ChatSession` guardar o número (a regra da faixa ficaria na casca, contra R2/R4).

### 2.2 `ReplySpeaker` lê a velocidade da sessão

O construtor troca `IOptions<TtsOptions> options` por `SpeechSpeed speed`; `SpeakAsync` chama `tts.SynthesizeAsync(reply.Reply, speed.Current, ct)` a cada fala (nunca guarda o valor). O contrato `ReplySpeaker` → `ITextToSpeech.SynthesizeAsync(texto, velocidade, ct)` e todo o resto do `SpeakAsync` não mudam. Só o construtor muda, então os dois pontos que o constroem em teste (`ReplySpeakerTests.CreateSut` e `SpokenReplyTests`) passam a construir um `SpeechSpeed`; o corpo de `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` (D7) não muda.

### 2.3 Comando `/speed` no console

- Core: `LocalCommands.Speed = "/speed"`, `LocalCommandKind.Speed`, `LocalCommandParser` reconhece sem diferenciar maiúsculas (como `/mode`). `LocalCommands.Available` ganha `/speed` entre `/mode` e `/quit`, com a descrição `Show or change the speaking speed (0.5 to 1.5)` (os limites vêm de `SpeechSpeed.Format(TtsOptions.MinSpeed/MaxSpeed)`).
- `ChatSession` ganha o último parâmetro obrigatório `SpeechSpeed speed` (só dois pontos o constroem: `EnglishTutorApp` e o helper de `ChatSessionTests`). `case LocalCommandKind.Speed` → `ChangeSpeedAsync(command.Arguments, ct)`: argumento em branco mostra a velocidade atual; senão `speed.Change(argumento)` e a mensagem do resultado. Nada vai ao `inputHandler` (RN-CONV06).
- Funciona igual com `Voice:Enabled=false` e com Piper ausente (P1): o `SpeechSpeed` sempre existe; só não há fala para ajustar. A US-016 diz "como no chat por texto".
- `EnglishTutorApp.RunSessionAsync` passa o `SpeechSpeed` do DI ao `ChatSession`; é o mesmo singleton do `ReplySpeaker`.
- O cabeçalho e as chaves de configuração não mudam. Sem log novo na mudança de velocidade (P5).

### 2.4 Mensagens e canais

Todas na **saída padrão**, como `Mode changed ...` e `Unknown mode ...` (AP-024: mesmo canal do `/mode`; o código de saída segue 0). Critério de saída: "a saída contém", nunca "a linha" (AP-057). `<atual>` = `SpeechSpeed.Format(Current)`; `<digitado>` = o argumento como digitado (já sem espaços nas pontas), sem transformação.

| Situação | Texto | Teste (§4) |
|---|---|---|
| Aplicada | `Speed set to <atual>x` (ex.: `Speed set to 1.2x`, `Speed set to 1.0x`) | `SpeedCommandTests.RunAsync_WhenUserTypesSpeedWithValidValue_ConfirmsAndChangesSpeed` |
| Fora da faixa | `Speed must be between 0.5 and 1.5 (found <digitado>). The speed stays at <atual>x.` | `SpeedCommandTests.RunAsync_WhenUserTypesSpeedOutOfRange_...` |
| Não é número | `Speed must be a number between 0.5 and 1.5 (found "<digitado>"). Example: /speed 1.2. The speed stays at <atual>x.` | `SpeedCommandTests.RunAsync_WhenUserTypesSpeedThatIsNotANumber_...` |
| Sem argumento | `Current speed: <atual>x (accepted range: 0.5 to 1.5). Example: /speed 1.2.` | `SpeedCommandTests.RunAsync_WhenUserTypesSpeedWithoutArgument_...` |
| `/help` | linha `  /speed - Show or change the speaking speed (0.5 to 1.5)` | `ChatSessionTests.RunAsync_WhenUserTypesHelp_ListsSpeedCommandWithRange` |

Transformações citadas: `Format` (mínimo 1 casa, máximo 3: `1`→`1.0`, `0.8333`→`0.833`, cultura invariável) tem teste nos dois lados do limite e em pt-BR (`SpeechSpeedTests.Format_*`); a faixa tem teste nos dois lados dos dois limites (0.5 e 1.5 aceitos; 0.49 e 1.51 recusados); o texto digitado é ecoado sem corte (a linha `"1,2"` na tabela de não-número prova). O texto da faixa vem de `TtsOptions` por `Format`; os testes comparam com o literal `0.5` e `1.5`.

### 2.5 Mudanças em `ARCHITECTURE.md` (commit `docs(ai): update architecture for US-012`, antes da T1; texto antigo citado)

1. §3, linha `Speech/` do Core. Antigo (final da linha): `ReplySpeaker, SpeakResult, IVoiceOutputCheck, VoiceOutputStatus`. Novo: o mesmo, seguido de `, SpeechSpeed, SpeedChange, SpeedChangeStatus`.
2. §7, linha TTS. Antigo: ``Velocidade configurável (`Tts:Speed`).`` Novo: ``Velocidade configurável: `Tts:Speed` é o valor inicial e `/speed <0.5 a 1.5>` o altera durante a sessão (RN-TTS02); o valor vigente mora em `SpeechSpeed` (Core) e o `ReplySpeaker` o lê a cada fala.`` O resto da linha ("Fala apenas o `Reply` ...") não muda.
3. §7, linha de configuração. Antigo (trecho): `Tts:Speed (0.5 a 1.5, padrão 1.0)`. Novo: `Tts:Speed (0.5 a 1.5, padrão 1.0; valor inicial da velocidade da sessão)`.

Sincronia (Grep em `CLAUDE.md`, `README.md` e `.claude/` por `ChatSession`, `/speed`, `comandos locais`, `Tts:Speed`, `Speech/`): nenhuma ocorrência em `CLAUDE.md` nem em skill; o `README.md` só lista `/help`, `/quit` e `/mode` num histórico por US, que o Documentador atualiza no fim (não é regra repetida). Nenhuma linha de `CLAUDE.md` ou de skill muda.

### 2.6 Mudanças em specs (commit `docs(ai): update spec for US-012`, antes da T1; texto antigo → novo)

`specs/sprints/SPRINT_2.md`, US-012:
1. Cenário "mudar a velocidade durante a sessão". Antigo: `Então vejo "Speed set to 1.2x" e a próxima resposta é falada a 1.2`. Novo: `Então a saída contém "Speed set to 1.2x" e a próxima resposta é falada a 1.2`.
2. Cenário "velocidade fora da faixa". Antigo: `Então vejo uma mensagem dizendo que a faixa é 0.5 a 1.5`. Novo: `Então a saída contém uma mensagem dizendo que a faixa é 0.5 a 1.5`.
3. Cenário "velocidade sem argumento". Antigo: `Então vejo a velocidade atual e a faixa aceita`. Novo: `Então a saída contém a velocidade atual e a faixa aceita`.
4. Acrescentar depois de "velocidade fora da faixa" (P2):
   ```
   Cenário: velocidade que não é número
     Dado uma sessão com voz e a velocidade 1.2 em vigor
     Quando digito "/speed fast"
     Então a saída contém uma mensagem dizendo que o valor deve ser um número de 0.5 a 1.5
     E a velocidade anterior continua valendo
   ```
5. Acrescentar depois de "velocidade sem argumento" (P1):
   ```
   Cenário: velocidade sem voz
     Dado Voice:Enabled igual a false
     Quando digito "/speed 1.2"
     Então a saída contém "Speed set to 1.2x" e nada é falado
   ```
6. DoD, linha D5 (AP-064). Antigo (linha inteira): ``| D5 | `dotnet test --filter "FullyQualifiedName~Speed"` | verde, incluindo os limites 0.5, 1.5, 0.49 e 1.51 |``. Novo: ``| D5 | `dotnet test --filter "FullyQualifiedName~Speed&Category!=LiveBrain&Category!=LiveVoice"` | verde, incluindo os limites 0.5, 1.5, 0.49 e 1.51; `--list-tests` com o mesmo filtro não lista nenhuma classe `Live*` |``.
7. DoD, linha D7 (AP-064). Antigo (trecho): ``dotnet test --filter "FullyQualifiedName~ReplySpeakerTests"``. Novo: ``dotnet test --filter "FullyQualifiedName~ReplySpeakerTests&Category!=LiveBrain&Category!=LiveVoice"``. O resto da linha não muda.

Nenhuma `RN-*` muda (RN-TTS02 e RN-CONV06 já dizem `/speed`). O cenário "comandos continuam funcionando" da US-016 (`/speed 1.1`) passa a ter dono real aqui; nenhum item é adiado para outra US.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `src/EnglishTutor.Core/Speech/SpeechSpeed.cs`, `SpeedChange.cs` (record + `SpeedChangeStatus`) | velocidade da sessão, parse e faixa |
| Alterar | `src/EnglishTutor.Core/Speech/ReplySpeaker.cs` | lê `SpeechSpeed.Current`; sai o `IOptions<TtsOptions>` |
| Alterar | `src/EnglishTutor.Core/Conversation/LocalCommands.cs`, `LocalCommand.cs`, `LocalCommandParser.cs` | `/speed`, `LocalCommandKind.Speed`, entrada em `Available` |
| Alterar | `src/EnglishTutor.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | registra `SpeechSpeed` |
| Alterar | `src/EnglishTutor.Console/Ui/ChatSession.cs`, `ConsoleText.cs`, `src/EnglishTutor.Console/EnglishTutorApp.cs` | comando, mensagens, ligação do singleton |
| Criar | `tests/EnglishTutor.Core.Tests/Speech/SpeechSpeedTests.cs` | parse, faixa, formato, cultura |
| Alterar | `tests/EnglishTutor.Core.Tests/Speech/ReplySpeakerTests.cs` (só `CreateSut` e um teste novo), `Conversation/LocalCommandParserTests.cs`, `DependencyInjection/CoreServiceCollectionExtensionsTests.cs` | fonte da velocidade, parser, registro |
| Criar | `tests/EnglishTutor.Console.Tests/Ui/SpeedCommandTests.cs`, `SpeedAppTests.cs` | comando no `ChatSession`; de ponta a ponta no composition root |
| Alterar | `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` (helper `CreateSession`, a asserção do comando desconhecido, um teste novo de `/help`), `Ui/SpokenReplyTests.cs` (construção do `ReplySpeaker`) | construtores novos; texto antigo → novo |
| Alterar (orquestrador, docs) | `ARCHITECTURE.md` (§2.5), `specs/sprints/SPRINT_2.md` (§2.6) | |

Texto antigo → novo em `ChatSessionTests.RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands`: `Contain("Available commands: /help, /mode, /quit")` → `Contain("Available commands: /help, /mode, /speed, /quit")`. Inventário nominal pelo Grep de `Available commands: /help, /mode, /quit` em `src`, `tests`, `prompts`, `docs` (rodado antes de fechar a tabela; `tasks/` e `specs/archive/` são histórico e ficam fora): um único arquivo, `tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs` (linha 33). `ConsoleText` monta o texto dinamicamente a partir de `LocalCommands.Available`, então não há outro literal. A variação sintática (`Contain(...)` com `Split(Environment.NewLine)`) é a única forma existente; o D9 abaixo prova que o comando acha o antigo antes da mudança.

## 4. Mapa cenário BDD → testes

Nenhum teste usa rede, GPU, processo real, microfone ou alto-falante. Não há janela, histórico nem retentativa (AP-017/AP-026 não se aplicam), mas cada turno falado tem texto único (`sentence t1`, `sentence t2`) e as velocidades de uma sequência são todas distintas entre si (0.8 configurada, 1.2 depois), para a asserção não passar por coincidência.

| Cenário | Projeto | Classe | Método | Tipo |
|---|---|---|---|---|
| Velocidade configurada | Core.Tests | `SpeechSpeedTests` | `Current_WhenCreated_IsTheConfiguredSpeed` (Theory 0.5, 0.8, 1.0, 1.5) | Unidade |
| (idem, o TTS recebe) | Core.Tests | `ReplySpeakerTests` | `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` (existente da US-011, Theory 1.0, 0.8, 1.5; D7; corpo inalterado) | Unidade |
| (idem, de ponta a ponta) | Console.Tests | `SpeedAppTests` | `RunAsync_WhenTtsSpeedIsConfigured_SpeaksAtThatSpeed` (`Tts:Speed`=0.8; o TTS falso recebe 0.8; nasce verde, ver T5) | Componente |
| (idem, registro) | Core.Tests | `CoreServiceCollectionExtensionsTests` | `AddEnglishTutorCore_Always_RegistersSpeechSpeedAsSingleton` (duas resoluções dão a mesma instância; com `Tts:Speed`=0.8 o `Current` é 0.8) | Unidade |
| Mudar a velocidade durante a sessão | Core.Tests | `LocalCommandParserTests` | `TryParse_WhenInputIsSpeedCommand_ReturnsSpeedWithArguments` (Theory `/speed 1.2`; `  /SPEED   1.2 ` → nome `/SPEED`, argumento `1.2`; `/speed` → argumento vazio) | Unidade |
| (idem, aplica e confirma) | Core.Tests | `SpeechSpeedTests` | `Change_WhenValueIsInRange_AppliesItAndReportsApplied` (Theory `1.2`, `0.5`, `1.5`, `1`, ` 0.8 `; os dois limites aceitos) | Unidade |
| (idem, mensagem e nada ao cérebro) | Console.Tests | `SpeedCommandTests` | `RunAsync_WhenUserTypesSpeedWithValidValue_ConfirmsAndChangesSpeed` (Theory `1.2`→`Speed set to 1.2x`, `0.5`→`0.5x`, `1.5`→`1.5x`, `1`→`1.0x`; `Current` mudou; `RecordingChatInputHandler.Inputs` vazio) | Componente |
| (idem, a próxima fala usa o novo valor) | Core.Tests | `ReplySpeakerTests` | `SpeakAsync_WhenSessionSpeedChangesBetweenCalls_UsesTheCurrentSpeedEachTime` (`SpeechSpeed` compartilhado: fala a 0.8, `Change("1.2")`, fala a 1.2; o TTS recebe `[0.8, 1.2]`) | Unidade |
| (idem, de ponta a ponta) | Console.Tests | `SpeedAppTests` | `RunAsync_WhenSpeedIsChangedInSession_SpeaksNextReplyAtNewSpeed` (`Tts:Speed`=0.8; linhas `sentence t1`, `/speed 1.2`, `sentence t2`; o TTS recebe `[0.8, 1.2]`; a saída contém `Speed set to 1.2x`; o cérebro recebeu 2 pedidos, não 3) | Componente |
| Velocidade fora da faixa | Core.Tests | `SpeechSpeedTests` | `Change_WhenValueIsOutOfRange_KeepsPreviousSpeedAndReportsOutOfRange` (Theory `0.49`, `1.51`, `2`, `0`, `-1`; parte de 0.8, aplica `1.2`, depois o valor ruim: `Current` fica 1.2, nem a inicial nem o valor ruim) | Unidade |
| (idem, na tela) | Console.Tests | `SpeedCommandTests` | `RunAsync_WhenUserTypesSpeedOutOfRange_ShowsRangeAndKeepsPreviousSpeed` (mesmos valores; a saída contém `Speed must be between 0.5 and 1.5 (found <valor>)` e `The speed stays at 1.2x.`; `Current` 1.2; `Inputs` vazio) | Componente |
| (idem, a velocidade anterior continua valendo ao falar) | Console.Tests | `SpeedAppTests` | `RunAsync_WhenSpeedIsOutOfRange_KeepsPreviousSpeedForNextReply` (`Tts:Speed`=0.8; `/speed 1.2`, `/speed 2`, `sentence t1`; o TTS recebe `[1.2]`; a saída contém a faixa) | Componente |
| Velocidade que não é número (cenário novo, §2.6) | Core.Tests | `SpeechSpeedTests` | `Change_WhenTextIsNotANumber_KeepsPreviousSpeedAndReportsNotANumber` (Theory ``, `   `, `fast`, `1,2`, `1.2 fast`, `1e0`, `0x1`, `NaN`, `Infinity`, `-Infinity`; `Current` fica 1.2) | Unidade |
| (idem, na tela) | Console.Tests | `SpeedCommandTests` | `RunAsync_WhenUserTypesSpeedThatIsNotANumber_ExplainsAndKeepsPreviousSpeed` (Theory `fast`, `1,2`, `NaN`, `1.2 fast`; a saída contém `Speed must be a number between 0.5 and 1.5 (found "<valor>")` e `The speed stays at 1.2x.`) | Componente |
| Velocidade sem argumento | Console.Tests | `SpeedCommandTests` | `RunAsync_WhenUserTypesSpeedWithoutArgument_ShowsCurrentSpeedAndRange` (Theory `/speed`, `/speed   `; `Tts:Speed`=0.8: a saída contém `Current speed: 0.8x (accepted range: 0.5 to 1.5)`; `Current` continua 0.8) | Componente |
| Velocidade sem voz (cenário novo, §2.6) | Console.Tests | `SpeedAppTests` | `RunAsync_WhenVoiceIsOff_SpeedCommandStillAnswersAndNothingIsSpoken` (`Voice:Enabled` ausente; `/speed 1.2`, `/speed`, `sentence t1`; a saída contém `Speed set to 1.2x` e `Current speed: 1.2x`; TTS e player não chamados; nasce verde, ver T5) | Componente |
| (apoio: `/help`) | Console.Tests | `ChatSessionTests` | `RunAsync_WhenUserTypesHelp_ListsSpeedCommandWithRange` (contém a linha exata `  /speed - Show or change the speaking speed (0.5 to 1.5)`); `RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands` (existente da US-001, expectativa trocada, §3) | Componente |
| (apoio: formato) | Core.Tests | `SpeechSpeedTests` | `Format_Always_ShowsAtLeastOneAndAtMostThreeDecimals` (Theory `1.0f`→`1.0`, `0.5f`→`0.5`, `1.2f`→`1.2`, `0.85f`→`0.85`, `1.125f`→`1.125`, `0.8333f`→`0.833`) | Unidade |
| (apoio: cultura, AP-054) | Core.Tests | `SpeechSpeedTests` | `Change_WhenCurrentCultureUsesComma_ParsesDotAndFormatsWithDot` (`CurrentCulture` pt-BR, restaurada em `finally`: `Change("1.2")` aplica 1.2; `Change("1,2")` é `NotANumber`; `Format(1.2f)` é `1.2`) | Unidade |
| (apoio: cultura na tela) | Console.Tests | `SpeedCommandTests` | `RunAsync_WhenCurrentCultureUsesComma_ShowsSpeedWithDot` (pt-BR: `/speed 1.2` mostra `Speed set to 1.2x` e a saída não contém `1,2`) | Componente |
| (apoio: cabeçalho não muda, P5) | Console.Tests | `VoiceOutputAppTests` | `RunAsync_WhenVoiceIsOnAndPiperIsAvailable_WritesNoVoiceWarningAndKeepsHeader` (existente da US-011; continua verde sem mudança) | Componente |

Cobertura dos ramos de derivação (AP-054): o único `<placeholder>` numérico é `<atual>`/`<digitado>`; os ramos de `Format` (1, 2 e 3 casas, corte em 3) e da cultura têm caso; os ramos de `Change` (`Applied`, `OutOfRange` dos dois lados, `NotANumber` por texto, por não finito e por separador) têm caso. Cada afirmação condicional da §2 tem teste: "só o ponto é separador" (`1,2`), "não finito" (`NaN`, `Infinity`), "limites inclusivos" (0.5/1.5 aceitos, 0.49/1.51 recusados), "igual com voz desligada" (`...WhenVoiceIsOff_...`), "nunca guarda o valor" (`...SessionSpeedChangesBetweenCalls_...`).

Conferência de Red (AP-054/AP-046), cada asserção contra o `src` atual: `/speed` hoje é `Unknown` no parser e no console, `SpeechSpeed` não existe e `ReplySpeaker` lê `IOptions<TtsOptions>`. Nasce verde só o que o código atual já faz: `SpeedAppTests.RunAsync_WhenTtsSpeedIsConfigured_SpeaksAtThatSpeed` (o `ReplySpeaker` já usa `Tts:Speed`; mutação na T5), `SpeedAppTests.RunAsync_WhenVoiceIsOff_...` (depois da T4; mutação na T5), a linha `1.0` do Theory `Current_WhenCreated_IsTheConfiguredSpeed` (a mesma linha do método que falha nas demais) e `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` (D7, verde antes e depois). Os demais nascem em Red.

## 5. Tarefas (ordem de execução TDD)

Antes da T1 o orquestrador aplica dois commits: `docs(ai): update spec for US-012` (§2.6) e `docs(ai): update architecture for US-012` (§2.5).

- [ ] T1 — `SpeechSpeed` no Core. Esqueleto: `SpeedChange`/`SpeedChangeStatus`; `SpeechSpeed` com `Current` que devolve `TtsOptions.DefaultSpeed`, `Change` e `Format` que lançam `NotImplementedException` (sem registro no DI). Red: `SpeechSpeedTests.Current_WhenCreated_IsTheConfiguredSpeed` (linhas 0.5, 0.8, 1.5 falham; a de 1.0 passa, mesmo método), `Change_WhenValueIsInRange_AppliesItAndReportsApplied`, `Change_WhenValueIsOutOfRange_KeepsPreviousSpeedAndReportsOutOfRange`, `Change_WhenTextIsNotANumber_KeepsPreviousSpeedAndReportsNotANumber`, `Change_WhenCurrentCultureUsesComma_ParsesDotAndFormatsWithDot`, `Format_Always_ShowsAtLeastOneAndAtMostThreeDecimals`, `CoreServiceCollectionExtensionsTests.AddEnglishTutorCore_Always_RegistersSpeechSpeedAsSingleton`. / Green: regras de 2.1 e `TryAddSingleton<SpeechSpeed>()`. Mutações depois do Green: limites exclusivos (`>`/`<`) derrubam as linhas `0.5` e `1.5` de `...InRange...`; remover `float.IsFinite` faz `NaN` e `Infinity` virarem `OutOfRange` e derruba `...NotANumber...`; parse com `CurrentCulture` derruba o teste de pt-BR (a linha `1.2` vira `NotANumber`); `Change` mudar `Current` antes de validar derruba o teste de faixa (`Current` fica com o valor ruim). / Refactor: constantes dos limites reaproveitadas de `TtsOptions`. (commit: `test(core): add SpeechSpeed tests` + `feat(core): hold the session speech speed`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T2 — Parser reconhece `/speed`. Esqueleto: `LocalCommandKind.Speed` e `LocalCommands.Speed` existem; o parser ainda devolve `Unknown`. Red: `LocalCommandParserTests.TryParse_WhenInputIsSpeedCommand_ReturnsSpeedWithArguments` (3 linhas). `Available` **não** muda nesta tarefa (o `/help` só lista o que funciona; precedente da US-003 com `/mode`). / Green: `KindOf` com `LocalCommands.Speed` sem diferenciar maiúsculas. Estado do console até a T4: `/speed` cai no `default` do `switch` e mostra `Unknown command "/speed".` / Refactor: nenhum. (commit: `test(core): add /speed parser tests` + `feat(core): recognize the /speed command`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T3 — `ReplySpeaker` lê a velocidade da sessão. Esqueleto: o construtor ganha `SpeechSpeed speed` **sem** tirar `IOptions<TtsOptions>`; `SpeakAsync` ainda usa `options`; os dois pontos de construção em teste passam os dois (o `SpeechSpeed` criado com a mesma velocidade do `options`). Red: `ReplySpeakerTests.SpeakAsync_WhenSessionSpeedChangesBetweenCalls_UsesTheCurrentSpeedEachTime` (a segunda chamada ainda sai a 0.8). `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` fica verde antes e depois (D7). / Green: `speed.Current` a cada chamada e remoção do parâmetro `options` e do uso; `CreateSut` (helper) e `SpokenReplyTests` passam só o `SpeechSpeed`. Mutações depois do Green: ler `TtsOptions.DefaultSpeed` derruba as linhas 0.8 e 1.5 do D7; guardar `speed.Current` no construtor derruba o teste novo. / Refactor: nenhum. (commit: `test(core): add session speed source test for ReplySpeaker` + `feat(core): speak at the session speed`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T4 — `/speed` no `ChatSession`. Esqueleto: `ChatSession` ganha `SpeechSpeed speed` (ignorado); `EnglishTutorApp` passa uma **instância própria provisória** (`new SpeechSpeed(Options.Create(new TtsOptions()))`, não compartilhada com o `ReplySpeaker`; a T5 troca pelo singleton do DI); o helper `CreateSession` de `ChatSessionTests` passa um `SpeechSpeed`. Red: `SpeedCommandTests.RunAsync_WhenUserTypesSpeedWithValidValue_ConfirmsAndChangesSpeed`, `RunAsync_WhenUserTypesSpeedOutOfRange_ShowsRangeAndKeepsPreviousSpeed`, `RunAsync_WhenUserTypesSpeedThatIsNotANumber_ExplainsAndKeepsPreviousSpeed`, `RunAsync_WhenUserTypesSpeedWithoutArgument_ShowsCurrentSpeedAndRange`, `RunAsync_WhenCurrentCultureUsesComma_ShowsSpeedWithDot`, `ChatSessionTests.RunAsync_WhenUserTypesHelp_ListsSpeedCommandWithRange`, `RunAsync_WhenUserTypesUnknownCommand_ShowsUnknownCommandAndAvailableCommands` (expectativa trocada). / Green: `ChangeSpeedAsync`, `ConsoleText.SpeedChanged/SpeedOutOfRange/SpeedNotANumber/SpeedCurrent`, entrada `/speed` em `LocalCommands.Available`. Mutações depois do Green: o ramo `OutOfRange` aplicar o valor mesmo assim derruba os testes de faixa (`Current` ≠ 1.2); `Available` sem `/speed` derruba os dois testes de `/help`/desconhecido; encaminhar a linha ao `inputHandler` derruba a asserção `Inputs` vazio. / Refactor: nenhum. (commit: `test(console): add /speed command tests` + `feat(console): change the speech speed with /speed`)
  Escolhas de forma: nenhuma | §12 em <hash>
- [ ] T5 — Ligação do singleton e testes de ponta a ponta. Red: `SpeedAppTests.RunAsync_WhenSpeedIsChangedInSession_SpeaksNextReplyAtNewSpeed` (com a instância provisória o TTS recebe `[0.8, 0.8]`) e `RunAsync_WhenSpeedIsOutOfRange_KeepsPreviousSpeedForNextReply` (recebe `[0.8]` em vez de `[1.2]`). Nascem verdes: `RunAsync_WhenTtsSpeedIsConfigured_SpeaksAtThatSpeed` (o `ReplySpeaker` da T3 já usa `Tts:Speed`; Red por mutação "o `SpeechSpeed` ignora `Tts:Speed` e começa em 1.0": o TTS recebe 1.0 e o teste falha) e `RunAsync_WhenVoiceIsOff_SpeedCommandStillAnswersAndNothingIsSpoken` (Red por mutação "`RunSessionAsync` só resolve o `SpeechSpeed` com `Voice:Enabled=true`, senão passa `null!`": `/speed` lança `NullReferenceException`, o código de saída vira 1 e o teste falha). / Green: `RunSessionAsync` passa `services.GetRequiredService<SpeechSpeed>()` ao `ChatSession`. / Refactor: se o helper `PiperFilesPresent`/`CreateOptions` de `VoiceOutputAppTests` for copiado, extrair para `Support` (decisão do Executor, registrada na §12). (commit: `test(console): add session speed tests` + `feat(console): share the session speed with the speaker`)
  Escolhas de forma: nenhuma | §12 em <hash>

## 6. Pacotes NuGet

Nenhum pacote novo.

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Mudar o construtor do `ReplySpeaker` quebrar teste da US-011 | Só dois pontos o constroem em teste (`CreateSut`, `SpokenReplyTests`); a T3 troca em passos (esqueleto com os dois parâmetros, depois remove `options`) e o D7 prova que `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` segue verde |
| `float.TryParse` aceitar `NaN`/`Infinity`, ou a cultura do processo mudar o resultado | `float.IsFinite` explícito, cultura invariável, linhas `NaN`/`Infinity`/`-Infinity` e o teste em pt-BR (nos dois níveis) |
| Filtro `FullyQualifiedName~Speed` casar teste `Live*` | O D5 do spec ganha a exclusão de categorias e a prova com `--list-tests` (§2.6, item 6). Hoje o filtro casa também testes de outras US que têm `Speed` no nome (`TtsOptionsValidatorTests`, `PiperTextToSpeechTests.SynthesizeAsync_WithSpeed_...`, `EnglishTutorAppTests.RunAsync_WhenTtsSpeedIsOutOfRange_...`), todos sem hardware; o Executor registra a contagem por projeto |
| Limite aceito: a velocidade da sessão não é persistida | Consumidores: `ReplySpeaker` (usa o valor da sessão), `ChatSession` (mostra e muda), `Tts:Speed` (valor inicial a cada partida), DoD (D6 manual cobre uma sessão). Efeito: ao reiniciar, volta ao `Tts:Speed`; persistência é da Sprint 3 (RN-PERS) |
| Limite aceito: acesso sem sincronização a `SpeechSpeed.Current` | Consumidores: `ReplySpeaker` e `ChatSession` no mesmo laço sequencial, testes (sequenciais), DoD. Efeito: nenhum hoje; uma interface com várias threads (backlog da sprint) precisa revisar. `float` é atômico, nunca lê valor rasgado |
| Limite aceito: mais de 3 casas são aceitas e mostradas arredondadas (`/speed 0.8333` mostra `0.833x`) | Consumidores: `Format` (só a mensagem), Piper (`length_scale` com `0.####`, calculado do valor exato), validador (não usa). Efeito: só a mensagem arredonda; o áudio usa o valor digitado |
| Limite aceito: nenhum teste prova a diferença audível | Os unitários provam a velocidade chegando ao TTS e o `length_scale` (`PiperTextToSpeechTests`, US-011); só a D6 manual prova que `0.7` e `1.4` soam diferentes. Sem teste `LiveVoice` novo; consumidores: DoD (D6) |
| Quem não sabe da faixa digitar `/speed 2` sem voz e achar que mudou | Mensagem da faixa diz a faixa e a velocidade que continua; `/help` mostra `(0.5 to 1.5)` |

## 8. Fora de escopo

Persistir a velocidade entre sessões; linha de velocidade no cabeçalho; log da mudança de velocidade (nenhuma US prevista, só se o usuário pedir); atalho de teclado para velocidade; `/mic` e o ciclo de voz (US-013 a US-016; a US-016 só confere que `/speed 1.1` segue funcionando); controle gráfico de velocidade (interface do backlog); mudança no `PiperTextToSpeech` (já converte `1/velocidade` em `length_scale`) ou em `TtsOptionsValidator` (a faixa da configuração continua a mesma); cérebro, prompt e parser (sem `eval`).

## 9. Perguntas em aberto

- **P1** — `/speed` com `Voice:Enabled=false` ou Piper ausente. Resposta padrão: funciona igual (mesmas mensagens), pois o `SpeechSpeed` sempre existe e a US-016 espera `/speed` "como no chat por texto"; vira cenário novo "velocidade sem voz" na spec (§2.6, item 5). Teste: `SpeedAppTests.RunAsync_WhenVoiceIsOff_SpeedCommandStillAnswersAndNothingIsSpoken`.
- **P2** — Valor que não é número, e vírgula decimal (`1,2`). Resposta padrão: só o ponto vale; qualquer outra coisa é `NotANumber` com mensagem de exemplo, sem mudar a velocidade; cenário novo na spec (§2.6, item 4). Teste: `SpeechSpeedTests.Change_WhenTextIsNotANumber_KeepsPreviousSpeedAndReportsNotANumber` (linha `1,2`) e `SpeedCommandTests.RunAsync_WhenUserTypesSpeedThatIsNotANumber_ExplainsAndKeepsPreviousSpeed`.
- **P3** — Persistência da velocidade. Resposta padrão: não; `Tts:Speed` é o valor inicial a cada partida. Teste: `SpeechSpeedTests.Current_WhenCreated_IsTheConfiguredSpeed`.
- **P4** — ADR para o `SpeechSpeed`. Resposta padrão: não há; é a consequência registrada no ADR-012 e as alternativas estão na §2.1. Sem efeito observável.
- **P5** — Log da mudança e velocidade no cabeçalho. Resposta padrão: nenhum dos dois; o cabeçalho segue igual. Teste: `VoiceOutputAppTests.RunAsync_WhenVoiceIsOnAndPiperIsAvailable_WritesNoVoiceWarningAndKeepsHeader`.
- **P6** — Mensagens e canal. Resposta padrão: textos da §2.4, na saída padrão como `/mode`, código de saída 0. Teste: `SpeedCommandTests.RunAsync_WhenUserTypesSpeedOutOfRange_ShowsRangeAndKeepsPreviousSpeed`.
- **P7** — Mudar o construtor do `ReplySpeaker` (sai `IOptions<TtsOptions>`, entra `SpeechSpeed`) é compatível com o D7 da spec ("só troca a fonte da velocidade, não o contrato")? Resposta padrão: sim; o contrato é `ReplySpeaker` → `ITextToSpeech.SynthesizeAsync(texto, velocidade, ct)`, e o corpo do teste D7 não muda; só a montagem do `CreateSut`. Teste: `ReplySpeakerTests.SpeakAsync_WhenSpeedIsConfigured_PassesItToTts`.

## 10. Definition of Done (comandos verificáveis)

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice"` | todos verdes, em < 30 s, sem rede, GPU, microfone nem alto-falante |
| D3 | `dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice" --collect:"XPlat Code Coverage"` | Core ≥ 80% de linhas; evidência = relatório mesclado (`reportgenerator`), com `line-rate` do assembly Core e total da solução; relatório de um projeto só como nota (AP-056); `SpeechSpeed` registrado à parte |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test --filter "FullyQualifiedName~Speed&Category!=LiveBrain&Category!=LiveVoice"` e o mesmo com `--list-tests`, por projeto de teste (Core, Infrastructure, Console) | verde, incluindo os limites 0.5, 1.5, 0.49 e 1.51 (o Executor confere no arquivo `SpeechSpeedTests.cs` as quatro linhas); a lista não contém `Live` (nenhuma classe `*LiveVoiceTests`/`*LiveTests`); o Executor registra a contagem por projeto e que os métodos de `SpeechSpeedTests`, `SpeedCommandTests` e `SpeedAppTests` aparecem |
| D6 | Manual (usuário), `Voice:Enabled=true`, Claude Code: `/speed 0.7`, um turno, `/speed 1.4`, outro turno | o usuário confirma a diferença audível e registra em "Evidências"; o Executor anota que o ambiente do Piper é herdado sem limpeza (ADR-012) |
| D7 | `dotnet test --filter "FullyQualifiedName~ReplySpeakerTests&Category!=LiveBrain&Category!=LiveVoice"` e o mesmo com `--list-tests` | verde: `SpeakAsync_WhenSpeedIsConfigured_PassesItToTts` (US-011) continua verde com a velocidade vinda do `SpeechSpeed`; a lista contém esse método e nenhuma classe `Live*`; `git diff develop...HEAD -- tests/EnglishTutor.Core.Tests/Speech/ReplySpeakerTests.cs` mostra o corpo desse teste sem mudança (só `CreateSut` e o teste novo) |
| D8 | No Windows PowerShell 5.1 (voz desligada, nenhum Piper necessário): `"/speed", "/speed 1.2", "/speed 2", "/speed fast", "/speed", "/help", "/quit" \| dotnet run --project src/EnglishTutor.Console; "exit=$LASTEXITCODE"` | a saída contém, nesta ordem, `Current speed: 1.0x (accepted range: 0.5 to 1.5)`, `Speed set to 1.2x`, `Speed must be between 0.5 and 1.5 (found 2). The speed stays at 1.2x.`, `Speed must be a number between 0.5 and 1.5 (found "fast")`, `Current speed: 1.2x` e `/speed - Show or change the speaking speed (0.5 to 1.5)`; nenhuma ocorrência de `Unknown command`; `exit=0` (o primeiro `/speed` chega com BOM, AP-006) |
| D9 | Inventário e texto antigo (Git Bash): (a) `grep -rn -F "Available commands: /help, /mode, /quit" src tests prompts docs`; (b) `grep -n -F -e 'Então vejo "Speed set to' -e 'Então vejo uma mensagem dizendo que a faixa' -e 'Então vejo a velocidade atual' -e 'filter "FullyQualifiedName~Speed"' -e 'filter "FullyQualifiedName~ReplySpeakerTests"' specs/sprints/SPRINT_2.md` | nenhuma linha em (a) e (b) depois dos commits. Evidência de que os comandos acham o antigo: antes da T1, (a) rodado sobre o `develop` lista 1 linha (`tests/EnglishTutor.Console.Tests/Ui/ChatSessionTests.cs`) e (b) com `git show develop:specs/sprints/SPRINT_2.md \| grep ...` lista 5 linhas; o Executor registra as duas contagens na §11 |
| D10 | `git diff develop...HEAD \| LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'` (Git Bash) | contagem 0 (código de saída 1 é o esperado) |
| D11 | `git status --short` depois da D6 | nenhum arquivo de `models/`, `*.wav` nem `appsettings.*.local.json` |

Critério `LiveVoice`/`LiveBrain` (AP-052, AP-032): nenhum teste `Live*` novo e nenhuma mudança em cérebro, prompt ou parser (sem `eval`). Só a D6, manual, mede a diferença audível, com o Piper e o alto-falante reais; o unitário prova a velocidade chegando ao TTS e o `length_scale`; o que só a D6 prova é o efeito no ouvido (limite da §7). Sem tolerância numérica nem rodadas repetidas; se a D6 ficar ruim, quem decide é o usuário.

## 11. Evidências
_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário
_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._
