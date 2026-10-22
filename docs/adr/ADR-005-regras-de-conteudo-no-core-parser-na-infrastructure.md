# ADR-005 — Regras de conteúdo da resposta no Core; parser de formato na Infrastructure

- **Status:** Aceito (2026-10-17)
- **Data:** 2026-10-17
- **Revisado:** 2026-10-19 (RN-FEED01 passou a "exatamente uma pergunta"; US-004)
- **Revisado:** 2026-10-22 (RN-FEED01: o validador aceita 1 ou 2 perguntas; o prompt continua pedindo exatamente uma; US-006)
- **US relacionadas:** US-002 (e, como consumidoras, US-003, US-004, US-006)

## Contexto

A US-002 cria o `TutorReplyParser`, parser único da resposta do professor (RN-BRAIN03, ADR-002). A `ARCHITECTURE.md` diz duas coisas que puxam para lados opostos:

- §3 e §6.2: o `TutorReplyParser` fica em `Infrastructure/Brain` e "também valida as regras de conteúdo verificáveis (reply não vazio, máximo de 2 correções, etc. — RN-FEED*)".
- §2, R4: "Nenhuma classe de `Infrastructure` contém regra de negócio".

"Reply com exatamente uma pergunta" (RN-FEED01) e "no máximo 2 correções" (RN-FEED02) são regras de negócio. Além disso, o `ConversationService` (Core, US-003) precisa do motivo da rejeição para montar a instrução de correção da retentativa (RN-BRAIN04), e esse motivo precisa ser o mesmo para todos os provedores.

## Decisão

1. As regras de conteúdo verificáveis moram no Core, em `EnglishTutor.Core.Brain.TutorReplyValidator` (estático e puro): reply não vazio, reply com 1 ou 2 perguntas (uma ou duas frases terminadas em `?`, em qualquer posição; o prompt pede exatamente uma, tolerância de verificação da RN-FEED01; as frases são separadas por `ReplySentences`, também no Core), no máximo 2 correções (as excedentes são descartadas com aviso), correção sem `original` ou `corrected` descartada com aviso, campos opcionais em branco viram `null`.
2. O resultado (`TutorReplyParseResult`), a rejeição (`TutorReplyRejection(Reason, Message)`) e o catálogo de motivos e mensagens (`TutorReplyRejections`) também ficam no Core. Cada mensagem diz o que está errado e o que se espera.
3. O `TutorReplyParser` continua em `Infrastructure/Brain` e cuida **só do formato**: detecta JSON canônico ou tags, extrai os campos (System.Text.Json / regex), monta um `TutorReply` candidato, delega ao `TutorReplyValidator` e registra no log os avisos devolvidos. O Core não loga nem lê JSON.
4. O texto de feedback mostrado ao aluno é montado no Core por `FeedbackFormatter` (texto puro, uma linha por item), para que toda UI mostre o mesmo conteúdo sem repetir a regra.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Parser inteiro na Infrastructure, com as regras dentro (texto atual da §6.2) | Uma classe só; segue a §3 ao pé da letra | Viola R4; motivos de rejeição ficam presos a um adaptador; o Core (US-003) teria de conhecer strings da Infrastructure |
| Parser inteiro no Core (System.Text.Json é BCL) | Regras e formato juntos, cobertos pela meta de 80% do Core | Formato de saída de provedor é detalhe de adaptador (§3, ADR-002); o Core passaria a depender de detalhes de `<reply>`/JSON |
| Regras no Core, formato na Infrastructure (escolhida) | Respeita R4 e §3; motivos únicos para todos os provedores; Core testável sem JSON | Duas classes e um tipo de resultado a mais; a §6.2 precisa de ajuste de texto |

## Consequências

- Positivas: a retentativa da RN-BRAIN04 (US-003) usa `TutorReplyRejection.Message` sem depender da Infrastructure; os adaptadores (US-004, US-006) só traduzem o resultado inválido para `BrainException(InvalidOutput)`.
- Negativas / riscos: o adaptador precisa lembrar de chamar o parser (e não o validador direto); mitigado porque o parser é o único ponto de entrada público para texto cru, e a suíte de contrato (US-008) cobre isso.
- O que precisa ser medido ou revisitado: se o `eval` (US-009) mostrar muitas rejeições por "reply must ask the student exactly one question", rever a regra ou o separador de frases (`ReplySentences`; limites conhecidos em `tasks/US-004/plan.md` §9 P22).
- `ARCHITECTURE.md` §3, §4 e §6.2 atualizadas no mesmo PR (texto proposto em `tasks/US-002/plan.md` §2).
