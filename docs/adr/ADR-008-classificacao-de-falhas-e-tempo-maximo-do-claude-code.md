# ADR-008 — Provedor Claude Code: classificação das falhas, tempo máximo por chamada e mensagem ao aluno

- **Status:** Aceito (2026-10-20)
- **Data:** 2026-10-20
- **US relacionadas:** US-005 (e, como consumidoras, US-006, que segue o mesmo padrão de mensagem, e US-007, que decide o fallback pelo `BrainErrorKind`)
- **Complementa:** ADR-007, item 5 (que deixava a classificação fina e o executável inexistente para a US-005)

## Contexto

Até a US-004, toda falha do `claude -p` vira `BrainException(Unknown)` e o executável inexistente propaga `Win32Exception`, que o console trata como erro inesperado (código 1, fim da sessão). A US-005 precisa:

- classificar as falhas em `CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout` e `Unknown` (RN-BRAIN07; `InvalidOutput` já existe, ADR-006);
- limitar o tempo de resposta (`Brain:ClaudeCode:TimeoutSeconds`, padrão 60 s) e encerrar o processo filho ao estourar (RN-BRAIN09);
- mostrar ao aluno o que deu errado e o que fazer, sem encerrar a sessão (cenário "falha do cérebro no console não encerra a sessão");
- servir de base para o fallback da US-007 (RN-BRAIN08), que decide pelo `BrainErrorKind`.

Fatos que restringem a solução:

- a CLI não documenta códigos de saída por tipo de falha: autenticação, limite de uso e erro genérico saem, até onde se sabe, com o mesmo código (1) ou com `is_error: true`. O que distingue as falhas é o **texto** (`result` do objeto de erro ou stderr);
- o limite de uso da assinatura não é reproduzível sob demanda; a falta de login é (pasta de configuração vazia);
- `Process.Start` com executável inexistente lança `Win32Exception` com `NativeErrorCode` 2 (arquivo não encontrado; também `ENOENT` fora do Windows) ou 3 (caminho não encontrado);
- o `ProcessRunner` já encerra a árvore do processo quando o `CancellationToken` é cancelado (ADR-007, item 1);
- o processo filho herda o ambiente de quem abre o app (US-004, AP-034); uma `ANTHROPIC_API_KEY` inválida no ambiente também produz erro de autenticação, que `claude auth login` sozinho pode não resolver;
- o Core não conhece `Win32Exception` nem detalhes da CLI (R1, R4; `padroes-codigo`, "Erros").

## Decisão

1. **A classificação é do adaptador** (`ClaudeCodeTutorBrain` + `ClaudeCodeOutput`, com as regras de texto e as mensagens num `internal static class ClaudeCodeFailures`), nesta ordem:
   - `Win32Exception` vinda de `IProcessRunner.RunAsync` com `NativeErrorCode` 2 ou 3 → `CliNotFound`; com outro código → `Unknown` ("could not be started"). O `IProcessRunner` continua sem traduzir exceções: lançar `Win32Exception` quando o executável não pode ser iniciado passa a ser parte explícita do contrato da porta.
   - Tempo esgotado → `Timeout` (item 2).
   - Linhas 1 (código ≠ 0) e 3 (código 0 com `is_error: true`) da tabela de leitura da saída (US-004 §2.5): o texto examinado é o `result` do objeto de erro (quando há) mais o stderr inteiro, sempre as duas fontes, sem diferenciar maiúsculas. Contém um padrão de autenticação → `NotAuthenticated`; senão, um padrão de limite de uso → `UsageLimitReached`; senão → `Unknown`, com as mensagens da US-004. A linha 2 (código 0 e saída que não é o JSON `type: result`) continua `Unknown`.
   - Os padrões ficam numa lista no código, conferida contra uma saída gravada de verdade (falta de login, gravada com os mesmos argumentos de isolamento do adaptador, RN-BRAIN06) e contra fixtures sintéticas marcadas (limite de uso). `rate_limit` (limite momentâneo da API) **não** é limite de uso: fica `Unknown`.
2. **Tempo máximo por chamada ao processo.** A cada `SendAsync`, o adaptador cria `new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), timeProvider)` ligado ao token do chamador e passa o token ligado ao `IProcessRunner`. Se o tempo estourar (o token do tempo cancelado e o do chamador não), o runner encerra a árvore do processo e o adaptador converte a `OperationCanceledException` em `BrainException(Timeout)`. Cancelamento pelo chamador (Ctrl+C) continua propagando `OperationCanceledException`, inclusive quando o tempo também estourou. A retentativa de formato do ADR-006 é outra chamada, com o próprio limite.
3. **A mensagem da `BrainException` é o texto mostrado ao aluno**: diz o que aconteceu e o que fazer, com as instruções do provedor (instalar a CLI e a chave `Brain:ClaudeCode:ExecutablePath`; `claude auth login` e, sempre na mesma mensagem, conferir ou remover a variável `ANTHROPIC_API_KEY`; esperar o limite; `Brain:ClaudeCode:TimeoutSeconds`). Cada adaptador escreve as próprias (a US-006 fará o mesmo para o Ollama, cenários "Ollama indisponível" e "modelo não baixado").
4. **O console não encerra a sessão por falha do cérebro.** O `ConversationInputHandler` captura `BrainException` (e só ela), escreve na saída de erro `The tutor could not answer: <Message>` e uma linha de próximo passo, registra um aviso estruturado (`BrainFailed`, 1005, com o `BrainErrorKind`) e volta ao prompt. `OperationCanceledException` e qualquer outra exceção seguem os caminhos que já existem (saída limpa; erro inesperado).

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Console traduz `BrainErrorKind` em texto | Um lugar só para os textos | O console não sabe a instrução do provedor (instalar `claude`, `ollama pull`); viraria um `switch` por provedor na UI (contra R2) |
| Classificar pelo código de saída | Simples e estável | A CLI não diferencia as falhas por código |
| Classificar pelo campo `subtype` do JSON | Estruturado | Valores não documentados como estáveis e ausentes quando a falha sai só no stderr |
| `ProcessRunner` traduz `Win32Exception` num tipo próprio (`ProcessStartException`) | Porta sem exceção do sistema | Um tipo a mais sem ganho de teste: o fake já lança a exceção que o runner real lança; o runner não tem teste unitário |
| Tempo máximo dentro do `ProcessRunner` (parâmetro na `ProcessRequest`) | Runner completo | O runner não sabe que o estouro é `Timeout` de cérebro; a conversão ficaria dividida entre runner e adaptador |
| Tempo máximo por turno no `ConversationService` (incluindo a retentativa) | Limite único por turno | A regra fala do processo filho, que é do adaptador; exigiria `TimeoutSeconds` no Core e cancelar duas chamadas |
| Console continua encerrando, só com mensagem melhor | Menos código | Contraria o cenário da US-005 e a US-007 ("continuo na sessão") |
| Mencionar `ANTHROPIC_API_KEY` só quando a variável existe no ambiente | Mensagem mais curta | O adaptador teria de ler o ambiente do processo atual, que o `IProcessRunner` não expõe (AP-034); um ramo a mais sem ganho para o aluno |

## Consequências

- Positivas: o aluno vê a causa e a ação sem abrir logs; a sessão continua; a US-007 recebe o `BrainErrorKind` pronto para decidir o fallback; o tempo esgotado é testável sem relógio real (`TimeProvider` falso) e sem processo real (fake que só termina com o cancelamento).
- Negativas / riscos:
  - a classificação por texto depende das mensagens da CLI, que podem mudar entre versões. Falha não reconhecida cai em `Unknown`, que mostra o detalhe da CLI e mantém a sessão: degrada para "mensagem genérica com detalhe", nunca para erro inesperado. Mas, na US-007, um limite de uso não reconhecido **não aciona o fallback**;
  - falhas de início com `NativeErrorCode` diferente de 2 e 3 (ex.: 5, acesso negado; 193, não é um executável válido) viram `Unknown`. A RN-BRAIN08 aciona o fallback para `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated`, não para `Unknown`; então, na US-007, um `Brain:ClaudeCode:ExecutablePath` que aponta para algo que existe mas não pode ser executado **não aciona o fallback**. O aluno vê "could not be started" com o código e a chave a corrigir, e a sessão continua. Limite aceito; a RN-BRAIN08 não muda;
  - a mensagem de `NotAuthenticated` sempre menciona `ANTHROPIC_API_KEY`, mesmo quando a variável não existe (o aluno sem a variável ignora a frase);
  - a fixture de limite de uso é sintética até a primeira ocorrência real; o texto real pode não casar com os padrões;
  - o detalhe mostrado nas mensagens segue a regra da US-004 (o `result` do erro, senão a primeira linha não vazia do stderr), enquanto a classificação examina o stderr inteiro; se a primeira linha for um aviso sem relação, o aluno vê esse aviso como detalhe;
  - um turno com retentativa pode levar até 2 × `TimeoutSeconds`.
- O que precisa ser medido ou revisitado: na primeira ocorrência real de limite de uso, gravar a saída e trocar a fixture sintética (registro em `tests/EnglishTutor.Infrastructure.Tests/Fixtures/ClaudeCode/errors/README.md`; decisão do usuário, sem US de destino); ao atualizar a CLI, regravar a fixture de falta de login com os mesmos argumentos de isolamento do adaptador.
- `ARCHITECTURE.md` §3 e §6.3 atualizadas antes do código (texto proposto em `tasks/US-005/plan.md` §2.8).
