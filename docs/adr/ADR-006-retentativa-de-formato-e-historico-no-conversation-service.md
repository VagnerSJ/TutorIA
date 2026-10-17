# ADR-006 — Retentativa de formato e histórico no ConversationService

- **Status:** Aceito (2026-10-17)
- **Data:** 2026-10-17
- **US relacionadas:** US-003 (e, como consumidoras, US-004, US-006, US-007, US-008)

## Contexto

A US-003 cria o `ConversationService`, dono do histórico (RN-BRAIN01, ARCHITECTURE §5). Ele precisa:

- refazer a pergunta **uma** vez quando a resposta vem fora do formato e, se falhar de novo, mostrar o texto cru com `IsRawFallback` (RN-BRAIN04);
- montar a janela dos últimos N turnos (RN-CONV03) com o mesmo conteúdo para qualquer provedor.

O contrato `ITutorBrain.SendAsync` devolve `Task<TutorReply>` (ARCHITECTURE §4), já validado pelo parser. O parser fica na Infrastructure (ADR-005), e o Core não pode chamá-lo. O ADR-005 já previa que os adaptadores traduzem o resultado inválido em `BrainException(InvalidOutput)`, mas não dizia como o Core recebe o motivo da rejeição e o texto cru (necessários para a instrução de correção e para o fallback), nem como a retentativa e o fallback aparecem no histórico.

## Decisão

1. **Sinal de formato inválido:** o adaptador lança `BrainException.InvalidOutput(TutorReplyRejection rejection, string rawText)`. `BrainException` (Core) tem `Kind` (`BrainErrorKind`, RN-BRAIN07) e, só para `InvalidOutput`, `Rejection` e `RawText` não nulos. O construtor geral recusa `InvalidOutput`, para que esses dois campos existam sempre que o `Kind` for esse.
2. **Retentativa (RN-BRAIN04):** ao receber `InvalidOutput`, o `ConversationService` chama o cérebro mais uma vez, com o mesmo system prompt e as mesmas mensagens, acrescidas de duas: `Assistant` com o `RawText` rejeitado e `User` com a instrução de correção, que inclui `TutorReplyRejection.Message` (o que faltou e o que se espera). Outros `BrainErrorKind` e `OperationCanceledException` propagam sem retentativa.
3. **Fallback:** se a segunda chamada também lançar `InvalidOutput`, o turno vira `TutorReply.RawFallback(rawText)`: `Reply` = texto cru da segunda resposta sem espaços nas pontas, sem correções nem feedback, `IsRawFallback = true`, `RawText` = texto original.
4. **Histórico:** cada turno guarda a mensagem do aluno já formatada (RN-CONV02) e o `TutorReply` final. Nas chamadas seguintes, o professor aparece como `Assistant` com o `RawText` do turno. A resposta rejeitada e a instrução de correção **não** entram no histórico. Turnos com fallback entram (com o texto cru).
5. **System prompt:** vem de uma porta do Core, `ITutorPromptSource.GetSystemPromptAsync(ct)`, lida a cada turno. Ela devolve só o conteúdo de `prompts/tutor.md`. A instrução de formato continua sendo anexada pelo adaptador (ADR-002, item 4). A implementação que lê o arquivo entra na US-004.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| `ITutorBrain` devolve `TutorReplyParseResult` (ou `BrainResponse` com texto cru + resultado) | Sem exceção para fluxo esperado | Muda o contrato da ARCHITECTURE §4 e obriga todo consumidor (fallback, eval) a tratar dois caminhos; contraria o ADR-005 |
| Retentativa dentro de cada adaptador | O Core não precisa saber de formato | Viola R4 (regra de negócio na Infrastructure) e repete a regra em cada provedor |
| Correção como **uma** mensagem `User` só (sem o `Assistant` rejeitado) | Lista mais curta; nunca há `Assistant` vazio | O modelo não vê o que respondeu de errado; duas mensagens `User` seguidas confundem APIs de chat |
| Guardar no histórico também a resposta rejeitada e a correção | Rastro completo | Polui a janela com turnos que não são conversa e gasta contexto; a persistência (RN-PERS01) cuida do rastro |
| Professor no histórico como texto do `Reply` (sem feedback) | Janela menor | O modelo perde o exemplo do próprio formato e as correções já feitas; o `RawText` é o que ele de fato respondeu |
| System prompt em `ConversationOptions` (string) | Sem porta nova | Texto grande em configuração; a leitura de arquivo é I/O e cabe a um adaptador |

## Consequências

- Positivas: a regra da RN-BRAIN04 mora só no Core (R4) e vale para todos os provedores. Os adaptadores (US-004, US-006) só traduzem `TutorReplyParseResult` inválido em `BrainException.InvalidOutput`. A suíte de contrato (US-008) confere essa tradução.
- Negativas / riscos: exceção como fluxo para um caso esperado (custo irrelevante perto da latência do modelo). Se o `RawText` vier vazio, a mensagem `Assistant` da retentativa fica vazia. Algumas APIs recusam isso, e cabe ao adaptador (US-004/US-006) tratar.
- O que precisa ser medido ou revisitado: taxa de retentativas e de fallback no `eval` (US-009). Se o `RawText` do Claude Code com `--resume` (US-004) duplicar contexto, rever o item 4 para provedores com estado.
- `ARCHITECTURE.md` §3, §4 e §5 atualizadas no mesmo PR (texto proposto em `tasks/US-003/plan.md` §2.6).
