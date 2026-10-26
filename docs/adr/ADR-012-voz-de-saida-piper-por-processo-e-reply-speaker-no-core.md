# ADR-012 — Voz de saída: Piper por processo com arquivo temporário e `ReplySpeaker` no Core

- **Status:** Proposto
- **Data:** 2026-10-26
- **US relacionadas:** US-011 (US-012, US-016 e US-017 reutilizam)

## Contexto

A US-011 faz o professor falar o `reply`. Há quatro decisões acopladas:

1. Onde mora a regra "só o `reply` é falado; a resposta crua não" (RN-TTS01) e quem a executa. O `ConversationService.SendAsync` devolve o turno só no fim; se ele falasse, o texto só apareceria depois do áudio, contra o cenário "o texto aparece antes do áudio terminar". A regra é de negócio, então não pode ficar no adaptador (R4) nem só na casca do console (R2: outra UI precisa da mesma regra).
2. Como o Piper entrega o áudio. O `IProcessRunner` devolve `StandardOutput` como `string` UTF-8; áudio binário no stdout não cabe nele sem mudar a porta.
3. Como tocar o áudio no Windows.
4. O que fazer quando `Voice:Enabled=true` mas o Piper ou a voz não estão em disco.

## Decisão

1. Nova classe `ReplySpeaker` no Core (`Speech/`), chamada pela interface **depois** de mostrar o texto do turno. Ela decide o que falar (só `Reply`; nada se `IsRawFallback` ou se o texto for em branco), lê a velocidade de `Tts:Speed`, sintetiza com `ITextToSpeech`, toca com `IAudioPlayer`, mede e loga cada etapa e converte falha de síntese ou de reprodução em `SpeakResult.Failed` (a sessão continua). `OperationCanceledException` sempre propaga. O `ConversationService` não muda.
2. `PiperTextToSpeech` (Infrastructure) executa `piper --model <onnx> --output_file <wav temporário> --length_scale <1/velocidade>` pelo `IProcessRunner` (texto no stdin, em uma linha), lê o WAV PCM16 mono com um leitor próprio (`WavPcmReader`), converte para `AudioClip` e apaga o arquivo em `finally` (sucesso, falha, cancelamento). Cancelamento e tempo máximo (`Tts:Piper:TimeoutSeconds`) encerram a árvore do processo pelo runner.
3. `NAudioPlayer` (Infrastructure) toca o `AudioClip` com `WaveOutEvent` (pacote `NAudio.WinMM`), atrás de `IAudioPlayer`; cancelar para a reprodução.
4. Na partida, com `Voice:Enabled=true`, o console pergunta a uma porta do Core (`IVoiceOutputCheck`, implementada por `PiperVoiceOutputCheck`) se executável, modelo e `<modelo>.json` existem. Se faltar algo, escreve na saída de erro os caminhos completos esperados e como baixar, não cria o `ReplySpeaker` e o chat segue por texto.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Falar dentro do `ConversationService.SendAsync` | Um só lugar | O texto só aparece depois do áudio; a US-016 não consegue medir "fim da fala até o início do áudio" separado do cérebro |
| Regra de "só o reply" no console | Menos código no Core | Outra interface (WPF/Blazor) repetiria a regra (R2); sem teste do Core |
| Piper com `--output-raw` no stdout | Sem arquivo temporário; permite streaming | `IProcessRunner` devolve `string`; exigiria mudar a porta; streaming é da Fase 6 |
| Servidor HTTP do Piper | Modelo fica carregado (menos latência) | Exige Python (`piper1-gpl`); fora da decisão "sem Python" |
| Síntese do Windows (SAPI) | Nada para baixar | Voz pior; foge de RN-TTS e do plano local-offline com Piper |
| ONNX do Piper dentro do processo | Sem processo filho | Fonemização (espeak-ng) pesada de reproduzir; muito código |
| Kokoro agora | Voz melhor | Backlog da sprint: só volta se a voz do Piper não agradar |

## Consequências

- Positivas: regra de RN-TTS01 testada no Core com fakes; trocar de TTS (Kokoro) só cria um adaptador; texto sempre antes do áudio; Ctrl+C para tudo.
- Negativas / riscos: cada turno inicia um processo do Piper e recarrega a voz (custo de dezenas a centenas de ms, a medir); arquivo temporário em disco por alguns instantes (apagado sempre); `NAudio.WinMM` só toca no Windows; `rhasspy/piper` está arquivado, então os binários vêm da release `2023.11.14-2`, que pode sumir.
- O que precisa ser medido ou revisitado: tempo de síntese por turno (log `ReplySpoken`, US-016/US-017); se passar do orçamento, Fase 6 (processo persistente ou streaming); a velocidade vira valor mutável da sessão na US-012.
