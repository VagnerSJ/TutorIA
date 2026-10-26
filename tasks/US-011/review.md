# Revisão — US-011 O professor fala o `reply` (Piper)

Data: 2026-10-26
Commits revisados: eb6d9f4..2f801e3 (36 commits em `develop..HEAD`)
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain/LiveVoice) | ✅ 762 passaram (Core 322, Infrastructure 320, Console 120), 0 falhas, 0 ignorados, cada projeto em menos de 1 s |
| `dotnet format --verify-no-changes` | ✅ código de saída 0 |
| Cobertura de linhas do Core | 98,2% (total da solução 91,3%), relatório mesclado com reportgenerator em diretório temporário |
| Cenários BDD com teste | 6/6 (falar só o reply; texto antes do áudio; resposta crua; Piper ausente; falha da síntese; Ctrl+C), cada um com teste de unidade e de console conforme o mapa da §4 |
| Teste antes do código (ordem de commits) | ✅ cada tarefa T1 a T10 tem commit `test` antes do `feat` |
| Evidências da DoD conferem | ✅ D1 a D4 reproduzidas (contagens e cobertura iguais às do plano). D12: 0 ocorrências de caractere invisível. D13: nenhum `.wav`, `.onnx` nem `appsettings.*.local` rastreado. D5, D6, D8 a D11 e D14 são evidência do Executor, não reproduzida. D7 (ouvir a voz) está pendente do usuário e o plano diz isso. |
| Intenção antes do código (spec/ADR → código) | ✅ ADR-012 (Aceito) entrou em eb6d9f4. `ARCHITECTURE.md` (a9e9c79) e a spec da sprint (0d86b50) vieram antes do primeiro `test`. Todos os registros de "Escolhas de forma" cabem em commits anteriores ao `feat` correspondente (b8fcb12, 23c1870, d234b85, 59cf110, 3197ed8, 072e6cd, c294410). |

## Itens da DoD que não reproduzi
- Evidência do Executor, não reproduzida: D5, D6, D8, D9, D10, D11 e D14. D5, D6, D10 e D11 dependem de `dotnet run` ou de hardware, ou rodam filtros que não repeti.
- Conferi a coerência dessas evidências com o código: `PiperVoiceOutputCheck` e `ResolveReplySpeakerAsync` escrevem na saída de erro, e o cabeçalho não muda.
- D7 ficou pendente do usuário (a impressão da voz). Isso não bloqueia o merge, mas precisa ser preenchido antes de fechar a US.

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `src/EnglishTutor.Infrastructure/Audio/NAudioPlayer.cs`: a classe fica com 0% de cobertura unitária. Isso é um limite aceito e registrado na §7, coberto só pela D6. Acompanhar na US-016, onde a latência e o áudio entram nos critérios.
- **S2** — `tasks/US-011/plan.md` §11: preencher a D7 (impressão da voz) antes de arquivar a US. Se a voz `en_US-lessac-medium` desagradar, o backlog do Kokoro volta ao sprint (SPRINT_2, "Fora de escopo").

## Pontos positivos
- `ReplySpeaker` no Core (R4) fala só o `Reply`, ignora `IsRawFallback` e texto em branco, e deixa `OperationCanceledException` propagar. O `catch (Exception)` loga e converte em `Failed`, sem engolir silenciosamente. O log não traz o texto.
- `PiperTextToSpeech` usa `ArgumentList` (lista de argumentos) e `length_scale` com cultura invariante. O tempo máximo é separado do cancelamento do chamador (`TimeoutException` só com o `ct` vivo). O WAV temporário é apagado no `finally`. As mensagens dizem o que foi achado e o que se espera.
- A fala acontece depois de o texto ir para a tela e é aguardada pelo handler, então a próxima entrada só é lida depois do áudio. Falha de áudio vai para a saída de erro. Com `Voice:Enabled=false` nada de voz é resolvido.
- Validação de `Tts:Piper` na partida tem teste com duas seções inválidas ao mesmo tempo (AP-039). O guarda do trait `LiveVoice` evita rodar hardware no filtro da DoD.
- Processo disciplinado: ADR e specs primeiro, TDD em ciclos pequenos, provas de Red por mutação registradas e `models/` fora do Git.

## Aprendizados para o processo
- Nenhum erro novo nesta rodada. Os cuidados dos AP-039, AP-047, AP-052 e AP-057 foram aplicados e aparecem nos testes e na DoD.

---

## Rodada 2

Data: 2026-10-26
Commits revisados: 5da4f77..069a5b0
Resultado: APROVADO (0 bloqueantes)

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ (compilou; TreatWarningsAsErrors) |
| Testes (exceto LiveBrain/LiveVoice) | ✅ 763 passaram (Core 322, Infra 321, Console 120), 0 falhas |
| `dotnet format --verify-no-changes` | ✅ código 0 |
| Cobertura do Core | Não remedida: o Core não mudou desde a rodada 1. Vale o valor da rodada 1. |
| Teste antes do código | ✅ `test(tts)` 5da4f77 precede o fix 5c0c5de |
| Evidências conferem | ✅ as contagens batem com as minhas (322/321/120) |
| Intenção antes do código | ⚠️ ver S2: o §12 entrou no mesmo commit do fix |

### Verificações pedidas
- **O fix cobre o executável, o modelo e o WAV:** sim. `PiperTextToSpeech` aplica `Path.GetFullPath` a `ExecutablePath`, `ModelPath` e ao WAV. O `WorkingDirectory` sai do executável já completo.
- **O teste novo falha sem o fix:** sim, por inspeção. Não rodei mutação local.
  - Sem o fix, `FileName` ficaria `models\piper\piper.exe` e as asserções de `FileName`, `WorkingDirectory` e `Arguments[1]` falhariam.
  - O fix do executável e o do modelo são cobertos em separado (`FileName` e `Arguments[1]`).
  - O fix do WAV não é coberto. O teste usa `TempFolder` absoluto (`_folder/tmp`), então `Path.IsPathRooted(OutputFileOf(request))` passa sem o fix. Ver S1.
- **`PiperLiveVoiceTests`:** usa `new PiperOptions()` com os padrões relativos. O teste faz `Directory.SetCurrentDirectory` para a raiz do repositório (o diretório que contém `EnglishTutor.slnx`). Isso reproduz o cenário do bug. Não reexecutei o teste, porque `LiveVoice` precisa de Piper e áudio. É evidência do Executor (2 aprovados, 91b1c27). O código é coerente com ela.
- **Fix dentro de um commit `docs(ai)` (5c0c5de):** é observação, não bloqueante. O código está correto, vem depois do Red e tem testes. O Executor declarou o engano no plano. Convenção violada: o commit deveria ser `fix(tts):`, com o §12 em `docs(ai)` separado.

### Bloqueantes
Nenhum.

### Sugestões
- **S1** — `tests/EnglishTutor.Infrastructure.Tests/Tts/PiperTextToSpeechTests.cs`, `SynthesizeAsync_WithRelativePaths_PassesFullPathsToPiper`. A asserção final (`Path.IsPathRooted(OutputFileOf(request))`) é vacuosa, porque o WAV só fica relativo se o diretório temporário injetado for relativo. Esperado: o teste passar uma pasta temporária relativa, ou a asserção ser removida. Sem isso, o `GetFullPath` do WAV em `PiperTextToSpeech` fica sem teste.
- **S2** — `tasks/US-011/plan.md` §12 foi escrito junto com o fix (5c0c5de) e não antes. Isso é registro tardio de forma (AP-014/AP-027). Não bloqueia, porque o bug foi achado na D7 e o fix só muda caminhos, não texto nem nome público.

### Pontos positivos
- O Red do teste veio antes do fix, e a causa do bug está documentada com reprodução.
- O teste live agora exercita os padrões relativos, o que fecha a lacuna que deixou o bug escapar.
- A evidência da correção traz contagens exatas, e elas batem com as minhas.

### Aprendizados para o processo
- `git add -A` misturou código de produção num commit `docs(ai)` → regra: usar `git add` por caminho, e commit de fix em `fix(escopo):`.
- Testes de unidade com caminhos só absolutos escondem diferenças de diretório de trabalho → regra: todo adaptador que lança processo com `WorkingDirectory` diferente do diretório atual precisa de teste com caminho relativo.
