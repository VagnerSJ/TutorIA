# BUSINESS_RULES.md — Regras de negócio

Regras numeradas e estáveis. Testes e planos devem citar o ID (ex.: `// RN-FEED02`). Ao mudar uma regra, mantenha o ID e registre a alteração no histórico no fim do arquivo.

## Tolerância de verificação

Quando a regra pedida ao modelo é restrita (ex.: exatamente 1 `?`), a regra **continua escrita e pedida assim no prompt**, mas o **verificador** aceita um nível acima do pedido (ex.: 1 ou 2 `?`) e recusa o que passar disso. Motivo: modelos locais (ex.: 14B) erram o formato de vez em quando, e recusar um erro pequeno e inofensivo custa uma retentativa e deixa o `eval` e o D5 oscilando. Ao criar ou rever uma validação restrita de frase, aplique a folga e registre-a na regra `RN-*` ("Regra pedida" × "Tolerância de verificação") e no histórico. Não vale para segurança, formato estrutural (JSON, tags) nem limites que o usuário vê como erro de conteúdo grave.

## Conversa (RN-CONV)

- **RN-CONV01** — Uma sessão tem exatamente um modo ativo: `free-conversation`, `job-interview` ou `listening-practice`. O modo padrão é `free-conversation`. Trocar de modo durante a sessão é permitido e vale a partir do turno seguinte.
- **RN-CONV02** — A mensagem enviada ao cérebro a cada turno segue este formato:
  ```
  Student said (transcribed): "<texto>"
  Low-confidence words (possible pronunciation issues): ["w1", "w2"]
  Mode: <modo>
  ```
  Se não houver palavras de baixa confiança, a lista é `[]`. No modo texto (sem voz), a lista é sempre `[]`.
- **RN-CONV03** — A janela de contexto contém o system prompt, o resumo de erros recorrentes (se houver) e os últimos **N** turnos completos (aluno + professor), com N configurável (`Conversation:MaxTurnsInWindow`, padrão 10). Turnos mais antigos saem da janela, mas continuam persistidos.
- **RN-CONV04** — No início de cada sessão, os **5** erros mais frequentes do aluno (em todas as sessões anteriores) são enviados ao professor como contexto, para que ele os revise naturalmente na conversa.
- **RN-CONV05** — Entrada vazia ou só com espaços não é enviada ao cérebro; o aluno recebe um aviso local.
- **RN-CONV06** — Comandos locais começam com `/` e nunca são enviados ao cérebro (`/quit`, `/mode <modo>`, `/speed <valor>`, `/help`).

## Feedback (RN-FEED)

- **RN-FEED01** — O `reply` tem de 1 a 3 frases curtas em inglês, nível B1–B2, e faz **exatamente uma** pergunta ao aluno: uma única frase termina com `?`, em qualquer posição; as outras frases são comentário ou incentivo (ex.: "Was it busy in the morning? Tell me about your commute."). Nunca contém correções. **Regra pedida ao modelo:** exatamente uma pergunta (o prompt mantém isto). **Tolerância de verificação (2026-10-22):** o validador aceita 1 ou 2 frases com `?`; 0 ou 3 ou mais é inválido (ver "Tolerância de verificação" no início deste arquivo).
- **RN-FEED02** — O feedback traz **no máximo 2** correções gramaticais/de vocabulário, cada uma com `original → corrected` e uma explicação curta.
- **RN-FEED03** — Se não houver erros, o feedback traz um elogio (`praise`) a algo bem feito.
- **RN-FEED04** — Para cada palavra de baixa confiança informada, o feedback pode trazer uma dica de pronúncia simples (ex.: `beach /biːtʃ/ — long 'ee' sound`).
- **RN-FEED05** — No máximo **1** sugestão de vocabulário (palavra ou expressão mais natural) por turno.
- **RN-FEED06** — Se o aluno escrever em português ou parecer travado, o professor ajuda brevemente em inglês, com uma reformulação mais simples.

## Cérebro (RN-BRAIN)

- **RN-BRAIN01** — O histórico pertence à aplicação. Todo provedor recebe a janela completa (RN-CONV03) e precisa funcionar sem estado próprio. Estado do provedor (ex.: `--resume`) é apenas otimização.
- **RN-BRAIN02** — Todos os provedores usam o mesmo `prompts/tutor.md`. Ajustes por provedor ou por formato ficam em arquivos separados em `prompts/`.
- **RN-BRAIN03** — Todos os provedores devolvem o mesmo contrato (`TutorReply`), validado por um parser único.
- **RN-BRAIN04** — Resposta fora do formato: **uma** nova tentativa com instrução de correção. Se falhar de novo, exibir a resposta crua, sem feedback, e marcar o turno como `IsRawFallback`.
- **RN-BRAIN05** — O professor não usa ferramentas: não lê arquivos, não executa comandos e não acessa a rede além do próprio modelo.
- **RN-BRAIN06** — O provedor Claude Code roda em uma pasta de trabalho vazia e dedicada, isolada de `CLAUDE.md`, hooks e MCP de outros projetos, e nunca usa `--bare`.
- **RN-BRAIN07** — Falhas do provedor são classificadas (`CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout`, `InvalidOutput`, `Unknown`) e mostradas ao aluno com mensagem clara e ação sugerida.
- **RN-BRAIN08** — Se `Brain:FallbackProvider` estiver configurado, `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated` acionam o provedor de fallback para aquele turno, e o aluno é avisado de que o professor mudou.
- **RN-BRAIN09** — O tempo máximo de resposta do cérebro é configurável (padrão 60 s); ao estourar, a chamada é cancelada e o processo filho é encerrado.

## Voz (RN-STT / RN-TTS)

- **RN-STT01** — Uma palavra é de "baixa confiança" quando sua confiança < `Stt:LowConfidenceThreshold` (padrão 0,6). No máximo 5 palavras por turno são enviadas, as de menor confiança primeiro.
- **RN-STT02** — Gravações com menos de 0,5 s ou sem fala detectada são descartadas com aviso local.
- **RN-TTS01** — O TTS fala **somente** o `reply`. Feedback aparece apenas na tela.
- **RN-TTS02** — A velocidade da fala é configurável de 0,5× a 1,5× (padrão 1,0×) e pode ser alterada durante a sessão.

## Persistência (RN-PERS)

- **RN-PERS01** — Todo turno é salvo: texto do aluno, palavras de baixa confiança, reply, feedback, provedor usado, latências.
- **RN-PERS02** — Cada correção recebida incrementa a contagem do erro recorrente correspondente (chave normalizada `original → corrected`).
- **RN-PERS03** — Áudios gravados não são persistidos por padrão (`Audio:KeepRecordings = false`).

## Histórico de alterações

| Data | Regra | Alteração |
|---|---|---|
| 2026-10-17 | todas | Versão inicial |
| 2026-10-19 | RN-FEED01 | "termina com uma pergunta" → "faz exatamente uma pergunta" (uma única frase termina com `?`, em qualquer posição). Motivo: o modelo fecha o `reply` com uma frase de incentivo depois da pergunta, o que é pedagogicamente válido (US-004, D5, `tasks/US-004/plan.md` §12 E3/E4). |
| 2026-10-22 | RN-FEED01 | O validador passa a aceitar 1 ou 2 perguntas (0 e 3 ou mais continuam inválidas); o prompt continua pedindo exatamente 1. Motivo: o D5 da US-006 oscilou com o modelo local 14B por reply com 2 perguntas (`tasks/US-006/plan.md` §12). Criada a convenção "Tolerância de verificação" para validações restritas futuras. |
