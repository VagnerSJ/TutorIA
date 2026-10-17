# ADR-007 — Provedor Claude Code: processo por turno, histórico no stdin e `--resume` só quando a janela é estendida

- **Status:** Proposto
- **Data:** 2026-10-17
- **US relacionadas:** US-004 (e, como consumidoras, US-005, US-007, US-008)

## Contexto

A US-004 cria o `ClaudeCodeTutorBrain`, primeiro `ITutorBrain` real (ADR-001). Ele precisa:

- receber sempre o `BrainRequest` completo (system prompt + janela + mensagem atual) e funcionar sem estado próprio (RN-BRAIN01, ARCHITECTURE §5);
- aproveitar `--resume <session_id>` como otimização, sem duplicar contexto. O ADR-006 deixou isso em aberto: "se o `RawText` do Claude Code com `--resume` duplicar contexto, rever o item 4";
- passar o system prompt por `--system-prompt-file` (cenário "processo isolado e sem ferramentas") e rodar numa pasta de trabalho **vazia** e dedicada (RN-BRAIN06);
- ser testável sem processo real (ARCHITECTURE §10).

Fatos que restringem a solução:

- `claude -p` recebe **um** prompt de texto (stdin). Não há como enviar uma lista de mensagens `User`/`Assistant` numa sessão nova.
- O `BrainRequest.SystemPrompt` é texto (vem do `ITutorPromptSource`, ADR-006), e a CLI quer um caminho de arquivo.
- A saída de `--output-format json` é um objeto com `session_id`, `result` (texto) e, com `--json-schema`, `structured_output` (objeto já validado pela CLI).
- Com `--resume`, o system prompt é gravado no primeiro turno da sessão (ROADMAP §2).

## Decisão

1. **Processo por turno atrás de uma porta interna.** `IProcessRunner` (Infrastructure, `Processes/`) envolve `System.Diagnostics.Process`: `ArgumentList`, stdin/stdout/stderr em UTF-8 sem BOM, leitura assíncrona e `Kill(entireProcessTree: true)` quando o `CancellationToken` é cancelado. O adaptador só conhece `ProcessRequest`/`ProcessResult`; os testes usam um fake.
2. **Histórico no stdin.** Sem `--resume`, o adaptador renderiza todas as mensagens do `BrainRequest` num texto único (`ClaudeCodePromptRenderer`): mensagens anteriores rotuladas (`[student]`/`[tutor]`, mais antiga primeiro) e, por último, a mensagem atual. Com uma mensagem só, o texto vai sem rótulos.
3. **Continuidade de sessão no adaptador.** O `session_id` devolvido pela CLI fica guardado **no adaptador**, junto com as mensagens que aquela sessão já conhece (as do request + `Assistant` com o `RawText` devolvido). Com `UseResume = true`, o próximo request usa `--resume <id>` e envia só a mensagem nova **se e somente se** ele repetir exatamente essas mensagens e acrescentar uma única mensagem `User`. Em qualquer outro caso (janela deslizou, resposta diferente, turno depois de uma retentativa, `UseResume = false`, falha anterior), o adaptador abre sessão nova com a janela completa. `BrainRequest.SessionHint` fica reservado e o `ConversationService` continua enviando `null`.
4. **System prompt em arquivo temporário fora da pasta de trabalho.** A cada chamada o adaptador grava `BrainRequest.SystemPrompt` num arquivo temporário (pasta temporária do sistema, nome único), passa o caminho em `--system-prompt-file` e apaga o arquivo ao final. A pasta de trabalho fica vazia. A instrução de formato (`prompts/format.json.md` ou `prompts/format.tags.md`) vai em `--append-system-prompt-file`, com o caminho da cópia em `prompts/` ao lado do executável (ADR-002, item 4).
5. **Leitura da saída.** `structured_output` (texto JSON bruto) quando existir como objeto; senão, `result`. Esse texto é o `RawText` e vai para o `TutorReplyParser` (ADR-005). Resultado inválido vira `BrainException.InvalidOutput(rejection, rawText)` (ADR-006). Código de saída diferente de zero, `is_error: true` ou saída que não é o objeto JSON da CLI viram `BrainException(Unknown, ...)` com o detalhe; a classificação fina (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`) é da US-005.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| `ConversationService` preenche `SessionHint` | Estado visível no Core | O Core não conhece o `session_id` da CLI; exigiria devolver o id no `TutorReply` (muda o contrato da §4) e o serviço passaria a saber de detalhe de provedor |
| Sempre `--resume` e enviar só a última mensagem | Menos tokens | A sessão da CLI passa a ter mais turnos que a janela (contra RN-CONV03) e guarda a tentativa rejeitada e a correção (contra ADR-006, item 4); quebra se o provedor mudar no meio da sessão |
| Sempre `--resume` e enviar a janela completa | Simples | Duplica o contexto a cada turno (o risco citado no ADR-006) |
| Nunca `--resume` | Sem estado no adaptador | Perde a otimização prevista na ARCHITECTURE §5 e no ADR-001 |
| `--input-format stream-json` com várias mensagens | Mensagens estruturadas | Não aceita mensagens `Assistant` de histórico; fica para o `ClaudeCodeStreamingBrain` (Fase 6) |
| System prompt gravado dentro da pasta de trabalho | Sem pasta temporária | A pasta deixa de ser vazia (RN-BRAIN06) |
| `--system-prompt <texto>` | Sem arquivo | Contraria o cenário da US-004 e a ARCHITECTURE §6.3; texto grande na linha de comando do Windows |

## Consequências

- Positivas: o adaptador funciona com o `BrainRequest` completo (RN-BRAIN01) e só usa `--resume` quando ele é equivalente à janela, sem duplicar contexto. A retentativa do ADR-006 também aproveita a sessão (a correção estende exatamente o que a sessão conhece). Tudo testável com um `IProcessRunner` falso.
- Negativas / riscos: o adaptador tem estado e não é thread-safe (uma chamada por vez, como o `ConversationService`). Depois de uma retentativa, o turno seguinte abre sessão nova (a sessão da CLI contém a resposta rejeitada, que a janela não contém). Um arquivo temporário é criado e apagado por turno. Se `claude` for um shim `.cmd` (instalação via npm), o `cmd.exe` reinterpreta os argumentos e o JSON Schema pode quebrar; o instalador nativo (`claude.exe`) é o suportado.
- O que precisa ser medido ou revisitado: latência JsonSchema × Tags (US-004, D7, tabela no ADR-002); taxa de sessões novas por retentativa no `eval` (US-009); o `ClaudeCodeStreamingBrain` (Fase 6) pode substituir os itens 2 e 3.
- `ARCHITECTURE.md` §3, §5 e §6.3 atualizadas antes do código (texto proposto em `tasks/US-004/plan.md` §2.8).
