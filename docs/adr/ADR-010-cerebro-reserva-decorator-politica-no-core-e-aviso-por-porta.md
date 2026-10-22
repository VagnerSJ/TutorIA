# ADR-010 — Cérebro reserva: decorator na Infrastructure, política no Core e aviso por porta

- **Status:** Aceito
- **Data:** 2026-10-22
- **US relacionadas:** US-007 (e, como consumidoras, US-008, que roda a suíte de contrato sobre os cérebros, e US-009, que cria cada cérebro sem reserva no `eval`)

## Contexto

A US-007 troca o cérebro por configuração e liga um reserva: com `Brain:FallbackProvider` configurado, `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated` do primário mandam aquele turno para o reserva, e o aluno é avisado de que o professor mudou (RN-BRAIN08). Forças:

- a ARCHITECTURE §3 e §6 já fixam `BrainFactory` e `FallbackTutorBrain` na Infrastructure, como decorator de `ITutorBrain`;
- a ARCHITECTURE R4 proíbe regra de negócio na Infrastructure: "quais falhas mandam trocar" é regra (RN-BRAIN08) e precisa morar no Core;
- o aviso "Switched to Ollama (usage limit reached)" precisa chegar à tela, mas `ITutorBrain.SendAsync` devolve só `TutorReply` e a interface só conversa com o Core (R2): o console não pode conhecer o `FallbackTutorBrain`;
- a ARCHITECTURE §6.3 diz que só `UsageLimitReached` aciona o reserva ("e, se configurado, `CliNotFound`/`NotAuthenticated`") e não cita `Timeout`; a RN-BRAIN08 lista os quatro. A regra vence a prosa de arquitetura;
- a retentativa de formato (RN-BRAIN04, ADR-006) vive acima do cérebro, no `ConversationService`, e chama `ITutorBrain.SendAsync` duas vezes no pior caso;
- o ADR-009 deixou o seletor provisório de `ITutorBrain` para a `BrainFactory` desta US.

## Decisão

1. **Política no Core.** `BrainFallbackPolicy.ShouldFallBack(BrainErrorKind)` devolve verdadeiro para `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated` e falso para `Unknown` e `InvalidOutput`. O `FallbackTutorBrain` só consulta a política.
2. **Decorator na Infrastructure.** `FallbackTutorBrain(primary, secondary, notifier, logger)`: chama o primário; se lançar `BrainException` com `Kind` que a política manda trocar, loga, avisa o notificador e envia o **mesmo** `BrainRequest` ao reserva. A troca vale só para aquela chamada (o primário é tentado de novo na chamada seguinte). Qualquer outra exceção (incluindo cancelamento e `InvalidOutput`) propaga sem troca. A exceção do reserva propaga como está. `Name` é o do primário (a configuração escolhida).
3. **Aviso por porta do Core.** `IBrainSwitchNotifier.NotifyAsync(BrainSwitchNotice, CancellationToken)` com `BrainSwitchNotice(FromProvider, ToProvider, Reason)`. O console implementa (`ConsoleBrainSwitchNotifier`, texto `Switched to <reserva> (<motivo>)` na saída padrão, antes da linha `Tutor:`). A Infrastructure registra um notificador silencioso por `TryAdd`; o console registra o dele antes. O `ConversationService` não muda.
4. **`BrainFactory`.** Valida `Brain:Provider` e `Brain:FallbackProvider` com o `BrainOptionsValidator` do Core (mesmas mensagens), normaliza pelos nomes canônicos de `BrainProviders` e monta o primário, ou o decorator quando há reserva. Cada cérebro concreto também é registrado com chave (`ITutorBrain` com o nome do provedor), para o `eval` da US-009 usar cada um sem reserva. O seletor provisório do ADR-009 sai.
5. **Configuração.** `Brain:FallbackProvider` (vazio ou em branco = sem reserva); diferente de `Brain:Provider` (sem diferenciar maiúsculas), senão a partida falha com `ExitCodes.InvalidConfiguration`. O `appsettings.json` entrega o valor vazio (opt-in); `ARCHITECTURE.md` §6.1 mostra o exemplo com `"Ollama"`.
6. **`ARCHITECTURE.md` §6.3** passa a dizer os quatro tipos da RN-BRAIN08; texto exato no `tasks/US-007/plan.md` §2.8.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Fallback dentro do `ConversationService` | Aviso vira dado do `ConversationTurnResult`, sem porta nova | O serviço passaria a conhecer dois cérebros e a política de troca; contraria a ARCHITECTURE §3/§6 (decorator) e mistura com a retentativa de formato |
| Aviso como campo novo de `TutorReply` | Sem porta nova; chega por onde a resposta já passa | O DTO do professor passa a carregar estado da infraestrutura; persistência e TTS veriam um campo que não é do professor |
| Console assina evento do `FallbackTutorBrain` | Simples | A UI passaria a depender de um tipo da Infrastructure (viola R2) |
| Política (quais falhas) dentro do `FallbackTutorBrain` | Menos tipos | Regra de negócio na Infrastructure (viola R4) |
| Reserva "pegajoso" (depois da primeira troca, fica no reserva até o fim da sessão) | Evita a chamada que falha a cada turno | Contraria "para aquele turno" da RN-BRAIN08; o limite do plano volta sozinho e o aluno não saberia |
| Qualquer `BrainException` aciona o reserva | Mais cobertura | `Unknown` esconde erro de configuração; `InvalidOutput` já tem a retentativa da RN-BRAIN04 |
| `FallbackProvider` igual ao primário vira "sem reserva" em silêncio | `appsettings.json` poderia entregar `"Ollama"` | Configuração enganosa; preferimos erro claro na partida |

## Consequências

- Positivas: a regra de troca é testável no Core sem rede nem processo; o decorator é testável com cérebros falsos; a UI continua só com o Core; o `eval` (US-009) recebe cérebros por chave, sem reserva; o console mostra o aviso antes da resposta.
- Negativas / riscos:
  - **Retentativa depois da troca.** Se a resposta do reserva for `InvalidOutput`, o `ConversationService` chama `ITutorBrain.SendAsync` de novo; o decorator tenta o primário outra vez e, se ele ainda estiver no limite, o aviso aparece de novo no mesmo turno. Aceito: o texto é verdadeiro nas duas vezes; efeito em cada consumidor no `tasks/US-007/plan.md` §7 (L1);
  - **Custo por turno.** Com o primário no limite, cada turno gasta uma chamada que falha antes de ir ao reserva (um processo curto do `claude`, ou até `TimeoutSeconds` no caso de `Timeout`);
  - **Falhas do Ollama como primário são `Unknown`** (ADR-009): "Ollama fora do ar" e "modelo ausente" não acionam o reserva. Limite aceito; consumidor: o fallback só ajuda quem tem o Claude Code como primário. Novos valores de `BrainErrorKind` ficam sem dono;
  - **Limite de uso não reconhecido** pelos padrões de texto do ADR-008 cai em `Unknown` e não aciona o reserva;
  - `Brain:Ollama` e `Brain:ClaudeCode` continuam validados na partida mesmo quando só um é usado (ADR-009); o reserva exige as duas válidas de qualquer forma;
  - o `session_id` do Claude Code não conhece o turno respondido pelo reserva; o ADR-007 já manda abrir sessão nova com a janela completa quando o pedido não estende a sessão conhecida, então não há estado a limpar.
- O que precisa ser medido ou revisitado: frequência real de troca por limite de uso (ADR-001); se o aviso em todo turno incomoda (considerar aviso só na primeira troca da sessão); se o `eval` da US-009 precisa de um `Create(provider)` público na fábrica.
