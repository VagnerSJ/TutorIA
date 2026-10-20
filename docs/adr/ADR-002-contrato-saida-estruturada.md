# ADR-002 — Contrato de saída do professor: JSON Schema como caminho principal, tags como alternativa

- **Status:** Proposto (confirmar com medições na US-004 e no `eval` da US-009)
- **Data:** 2026-10-17
- **US relacionadas:** US-002, US-004, US-006, US-009

## Contexto

A resposta do professor tem duas partes com destinos diferentes: o `reply` vai para o TTS e o feedback vai só para a tela (RN-TTS01). Precisamos separar as duas partes de forma confiável em todos os provedores (RN-BRAIN03). O formato inicial proposto usa tags `<reply>…</reply><feedback>…</feedback>`.

Fatos verificados na documentação do Claude Code v2.1.299:
- `--json-schema '<schema>'` com `--output-format json` devolve o resultado **validado** no campo `structured_output`.
- O Ollama aceita `format` com um JSON Schema em `/api/chat` (saída restrita pela gramática).

## Decisão

1. A forma canônica do contrato é um objeto JSON:
   ```json
   {
     "reply": "string (1–3 frases, com exatamente uma pergunta, em qualquer posição)",
     "corrections": [{ "original": "string", "corrected": "string", "explanation": "string" }],
     "pronunciation": "string | null",
     "vocabulary": "string | null",
     "praise": "string | null"
   }
   ```
2. `ClaudeCode` e `Ollama` usam saída estruturada com esse schema (`OutputMode = JsonSchema`, padrão).
3. O `TutorReplyParser` é único e aceita **também** o formato com tags (`OutputMode = Tags`), usado como alternativa de compatibilidade e para comparação no `eval`.
4. As regras de conteúdo ficam em `prompts/tutor.md`; a instrução de formato fica em `prompts/format.json.md` ou `prompts/format.tags.md`, anexada pelo adaptador (RN-BRAIN02).
5. Regras que o schema não garante (reply com exatamente uma pergunta, RN-FEED01; no máximo 2 correções) continuam validadas pelo parser, com a retentativa da RN-BRAIN04.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Só tags | Simples; funciona com qualquer modelo e com streaming de texto | Parsing frágil; feedback em texto livre dificulta medir correções no `eval` e persistir erros recorrentes |
| Só JSON Schema | Validado na origem; campos tipados para persistência e `eval` | Pode adicionar latência no Claude Code; streaming do `reply` para o TTS fica mais difícil |
| JSON principal + tags alternativa (escolhida) | Robusto e mensurável, com uma saída alternativa | Dois caminhos no parser (mitigado com testes de fixture para ambos) |

## Consequências

- Correções chegam estruturadas → `RecurringErrors` (RN-PERS02) e o `eval` (US-009) ficam triviais.
- Risco: latência extra do `--json-schema` no Claude Code e dificuldade de streaming do TTS na Fase 6. A US-004 mede JsonSchema × Tags; se a diferença for grande, este ADR será revisto (por exemplo, tags no modo streaming).

## Medição (US-004)

2026-10-19 — `LatencyMeasurementLiveTests` (D7), 5 frases de fumaça da US-004 por modo, cada uma num processo `claude -p` novo (sem `--resume`), modelo `haiku` (`claude-haiku-5-5`), Claude Code CLI 2.1.304 (instalador nativo), AMD Ryzen 9 5900X, 32 GB, Windows 11. Tempo medido em volta de `ClaudeCodeTutorBrain.SendAsync` (inclui o arranque do processo).

| Modo | n | média (ms) | p95 (ms) | fora do formato |
|---|---|---|---|---|
| JsonSchema | 5 | 4982 | 5554 | 0 |
| Tags | 5 | 3642 | 4183 | 1 |

Leitura: o `--json-schema` custou cerca de 1,3 s a mais por turno nesta amostra, mas deu 5/5 no formato, contra 4/5 no modo Tags. Amostra pequena; a decisão (JsonSchema como padrão) não muda nesta US, e a US-009 repete a medição com o golden set.
