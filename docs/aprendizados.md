# Diário de aprendizados do processo

Cada vez que um agente (ou o orquestrador) erra algo de forma recorrente, ou o usuário corrige o fluxo, registramos aqui o erro e a regra/skill criada para evitá-lo. É o histórico de evolução do processo SDD.

**Status:** Aberta (ainda sem correção) · Proposta (correção escrita, aguardando aprovação) · Aplicada (correção commitada) · Descartada

## Formato

```markdown
### AP-NNN — <título curto>
- **Data / US:** AAAA-MM-DD / US-XXX (ou Fase 0)
- **Quem errou:** planejador | executor | revisor | documentador | orquestrador
- **O que aconteceu:** ...
- **Por que importa:** ...
- **Correção:** regra/skill/hook alterado (arquivo) — ou proposta de alteração
- **Status:** Aberta | Proposta | Aplicada (commit abc1234) | Descartada
```

---

### AP-001 — Plano anunciado e não exibido

- **Data / US:** 2026-10-17 / Fase 0
- **Quem errou:** orquestrador
- **O que aconteceu:** a mensagem de fechamento disse "o plano em fases está abaixo", mas o texto do plano não foi escrito no chat; o usuário aprovou sem ver o plano.
- **Por que importa:** a aprovação humana é o portão do fluxo; aprovar sem conteúdo anula o portão.
- **Correção:** o plano passou a viver em arquivo (`specs/ROADMAP.md`, `tasks/US-XXX/plan.md`) e a skill `/us` (passo 2.4) exige escrever o resumo do plano **na mensagem** antes de pedir aprovação.
- **Status:** Aplicada (Fase 0)

### AP-002 — Subagentes criados na sessão não ficam disponíveis

- **Data / US:** 2026-10-17 / Fase 0 (piloto US-001)
- **Quem errou:** orquestrador
- **O que aconteceu:** ao delegar ao `planejador` na mesma sessão em que `.claude/agents/` foi criado, o Claude Code respondeu `Agent type 'planejador' not found`. Os agentes são carregados no início da sessão.
- **Por que importa:** simular o agente com um agente genérico perderia as restrições de ferramentas do papel.
- **Correção:** a skill `/us` manda parar e avisar quando o agente não existe (não fazer o papel dele) e explica que é preciso abrir uma sessão nova.
- **Status:** Aplicada (Fase 0)

### AP-003 — Requisitos do prompt mudaram depois da Fase 0

- **Data / US:** 2026-10-17 / Fase 0
- **Quem errou:** — (mudança de intenção do usuário)
- **O que aconteceu:** o prompt inicial ganhou a Seção 4.5 (cinco hábitos de trabalho com IA) depois que a governança já tinha sido criada.
- **Por que importa:** confirma o hábito 4: a intenção foi ajustada no documento (prompt → `CLAUDE.md`, agentes, skills, sprint) antes de qualquer código.
- **Correção:** hábitos embutidos em `CLAUDE.md`, nos 4 agentes, no `/us`, no `checklist-revisao`, na nova skill `revisar-contexto`, em DoD por US no Sprint 1 e em hooks de build/teste.
- **Status:** Aplicada (Fase 0)

### AP-004 — Plano grande aprovado sem revisão independente e sem resumo no chat

- **Data / US:** 2026-10-17 / US-001
- **Quem errou:** orquestrador (fluxo sem a etapa) e orquestrador (resumo omitido)
- **O que aconteceu:** no `/us US-001`, o plano do Planejador (11 tarefas, 9 perguntas em aberto, ADR-004) foi direto para a aprovação humana e, aprovado, o orquestrador delegou ao Executor. O usuário interrompeu e perguntou se o plano não deveria passar pelo Revisor antes. O fluxo atual (skill `/us`, ADR-003, `CLAUDE.md` "Fluxo obrigatório") só usa o Revisor depois da execução, sobre o diff. O Executor chegou a commitar a T1 (648f510) antes da interrupção. Além disso, no passo 2.4 o orquestrador não escreveu no chat o resumo do plano antes de pedir aprovação, contrariando a skill `/us` (reincidência do AP-001).
- **Por que importa:** um plano grande revisado só pelo humano não tem checagem independente de rastreabilidade BDD→teste, conformidade com `RN-*`/`ARCHITECTURE.md` e verificabilidade da DoD antes de existir código; erros de plano custam mais caro depois da execução.
- **Correção (proposta):** inserir a etapa "Revisão do plano" no fluxo: Planejador → Revisor (revisa o `plan.md`, somente leitura, relatório salvo em `tasks/US-XXX/plan-review.md`; se REPROVADO, o Planejador corrige, máximo 3 rodadas) → aprovação do usuário → Executor → Revisor (código) → Documentador. Arquivos a alterar se aprovada: `.claude/skills/us/SKILL.md` (nova etapa e linha na tabela de detecção), `.claude/agents/revisor.md` (modo "revisão de plano" com checklist próprio), possivelmente a skill `checklist-revisao`, `CLAUDE.md` (linha do fluxo obrigatório) e ADR-003 (ou um ADR novo que o complemente).
- **Status:** Aplicada (commit 38bdccd)

**Atualização 2026-10-17 (US-001, documentação):** a etapa foi usada de fato nesta US. O `plan-review.md` (APROVADO, 14 sugestões S1–S14, todas incorporadas ao plano) evitou lacunas que o código teria: S1 (normalização do provedor sem dono nem teste), S3 (Ctrl+C sem token ligado ao `ApplicationStopping`) e S7 (comportamentos do console sem teste). O Revisor de código, na Rodada 1, cita isso como evidência a favor de aprovar o AP-004. Falta o usuário aprovar e o orquestrador aplicar nos arquivos listados na Correção. O checklist de revisão de plano deve incluir "cada P* com efeito observável tem teste mapeado".

### AP-005 — Documentos fora de sincronia com o plano e com o código

- **Data / US:** 2026-10-17 / US-001
- **Quem errou:** planejador (S2 e S10 do `plan-review.md`) e executor (S6 da Rodada 1 do `review.md`)
- **O que aconteceu:** três casos do mesmo padrão. (1) O plano propôs mudar a ARCHITECTURE §2 (Core pode usar `Microsoft.Extensions.Options`), mas não o `CLAUDE.md`, que repete a regra; quem lesse só o `CLAUDE.md` acusaria violação de R1. (2) Depois da aprovação, o ADR-004 passou a "Aceito", mas o `plan.md` continuou chamando de "Proposto". (3) Depois da execução, o §3 do `plan.md` ainda dizia que `RepositoryRoot.cs` estava "em andamento, não commitado" depois de commitado (1ca6aed).
- **Por que importa:** o Revisor e o Executor decidem com base nos documentos; texto velho gera falso bloqueante ou decisão errada.
- **Correção (proposta):** (a) checklist do Planejador e skill `/us`: toda alteração proposta na ARCHITECTURE lista também as linhas do `CLAUDE.md` e das skills que repetem a regra (também cabe no `/revisar-contexto`, C2); (b) skill `/us`, passo que registra a aprovação: trocar "(Proposto)" por "(Aceito)" no `plan.md`; (c) agente `executor`, tarefa de DoD: revisar §3 e §5 do `plan.md` e trocar estados "em andamento"/"Criar" pelo commit correspondente. Arquivos: `.claude/skills/us/SKILL.md`, `.claude/agents/planejador.md`, `.claude/agents/executor.md`.
- **Status:** Aplicada (commit 0f886f7)

### AP-006 — BOM UTF-8 na entrada redirecionada do Windows PowerShell 5.1

- **Data / US:** 2026-10-17 / US-001 (P12)
- **Quem errou:** planejador (não previu) e executor (achou na DoD manual D5)
- **O que aconteceu:** `"/quit" | dotnet run ...` no Windows PowerShell 5.1 entrega `"\uFEFF/quit"` ao processo. `Trim()` não remove U+FEFF, então o `/quit` virou texto livre. A partir da US-003/004 isso mandaria um comando local ao cérebro, violando RN-CONV06. Só a DoD manual pegou; nenhum teste automatizado previa a plataforma. Foi tratado pelo fluxo (decisão P12 no plano, teste 388afee, correção e71b316).
- **Por que importa:** defeito de plataforma só aparece na execução real; sem regra, a próxima US com console repete.
- **Correção (proposta):** na skill `padroes-codigo`, seção de entrada/console: "normalizar a entrada padrão redirecionada (remover BOM inicial) e ter um teste para isso". Na DoD de toda US com console, manter ao menos um comando com pipe no Windows PowerShell 5.1. Arquivo: `.claude/skills/padroes-codigo/SKILL.md` (e o modelo de DoD em `specs/sprints/`, a critério do usuário).
- **Status:** Aplicada (commit 7727f50)

### AP-007 — Caracteres invisíveis literais e escapes corrompidos por ferramentas de edição

- **Data / US:** 2026-10-17 / US-001
- **Quem errou:** executor (S1 da Rodada 1) e revisor (não havia item no checklist)
- **O que aconteceu:** o código e os testes de P12 traziam o caractere U+FEFF literal, invisível no editor e no diff (`ChatSession.cs:8`, `ChatSessionTests.cs:110,122,127`). Ao corrigir, o Executor viu que `Edit`/`Write` e `sed` transformaram o escape em caractere literal ou o corromperam.
- **Por que importa:** o leitor e o revisor não veem o que o teste fixa; e a ferramenta pode reintroduzir o problema sem aviso.
- **Correção (proposta):** (a) skill `padroes-codigo`: exigir escape (`\uXXXX`) para caracteres não imprimíveis em código e testes; (b) skill `checklist-revisao`: item "nenhum caractere invisível literal no diff"; (c) agente `executor`: para gravar escapes `\uXXXX` usar `perl` com `\x5cuFEFF` e conferir com `grep` pelos bytes EF BB BF (`$'\xef\xbb\xbf'`), que não deve achar nada. Arquivos: `.claude/skills/padroes-codigo/SKILL.md`, `.claude/skills/checklist-revisao/SKILL.md`, `.claude/agents/executor.md`.
- **Status:** Aplicada (commit c858312)

**Atualização 2026-10-17 (US-002, AP-019):** a conferência por bytes passa a ter forma única, com `LC_ALL=C`: `git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'` (fonte: `checklist-revisao` §4).

### AP-008 — Revisor não consegue reproduzir parte da DoD

- **Data / US:** 2026-10-17 / US-001 (Rodadas 1 e 2)
- **Quem errou:** orquestrador (permissões do papel)
- **O que aconteceu:** os comandos permitidos ao Revisor não incluem `dotnet format --verify-no-changes`, `dotnet tool restore`/`dotnet reportgenerator` nem `dotnet run`. D3b, D4, D5, D7, D11 e D12 foram conferidos só por leitura nas duas rodadas.
- **Por que importa:** o Revisor aceita evidência que não reproduziu; enfraquece o hábito 3 (critério verificável).
- **Correção (proposta):** permitir ao Revisor `dotnet format --verify-no-changes` e `dotnet reportgenerator` com `-targetdir` no scratchpad (não alteram o repositório). Itens que dependem de `dotnet run` ficam no relatório como "evidência do Executor, não reproduzida". Arquivo: `.claude/agents/revisor.md` (ferramentas/permissões).
- **Status:** Aplicada (commit d524714)

### AP-009 — Decisão de forma tomada durante o Green e documentada só depois

- **Data / US:** 2026-10-17 / US-001 (Rodada 2, S2)
- **Quem errou:** executor
- **O que aconteceu:** o pedido do usuário (§12 do plano, R1-S2/S4) deixava a forma em aberto. O Executor escolheu o texto da mensagem (`The application could not start: ...`) e o evento `ApplicationAborted` (1004) no código (c173601, db60928) e só os registrou no §2.6/§2.7 em 500a004, depois do código.
- **Por que importa:** o hábito 4 pede intenção no documento antes do código; o usuário só pôde aceitar os desvios depois de implementados.
- **Correção (proposta):** quando o pedido deixar uma escolha em aberto, o Executor registra a escolha no §12 do `plan.md`, num commit `docs(ai)` antes do `fix`/`feat`. Arquivos: `.claude/agents/executor.md` e/ou skill `tdd-dotnet`.
- **Status:** Aplicada (commit 5f4c9c4)

### AP-010 — Sugestão do Revisor prescreveu a técnica do teste, não o efeito

- **Data / US:** 2026-10-17 / US-001 (Rodada 2, desvio da S3)
- **Quem errou:** revisor
- **O que aconteceu:** a S3 da Rodada 1 pedia um teste com "token já cancelado". Esse cenário nunca chega ao caminho de configuração inválida (`host.StartAsync(ct)` lança antes do `ValidateOnStart`), então o teste passaria por acaso. O Executor investigou, cancelou o token durante a escrita (`CancellingTextWriter`) e registrou o desvio, aceito depois. Funcionou porque ele confirmou o Red pelo motivo certo.
- **Por que importa:** sugestão que descreve a técnica pode gerar teste que não exercita o risco.
- **Correção (proposta):** o Revisor descreve o efeito esperado ("a mensagem não se perde se o cancelamento chegar durante a escrita"), não a técnica; o Executor confirma que o Red falha pelo motivo certo. Arquivo: `.claude/skills/checklist-revisao/SKILL.md` (orientação para escrever sugestões).
- **Status:** Aplicada (commit b8df95e)

### AP-011 — Executor editou o relatório do Revisor

- **Data / US:** 2026-10-17 / US-001 (Rodada 2, S4)
- **Quem errou:** executor
- **O que aconteceu:** o Executor marcou S1–S4 como "Resolvido" dentro de `tasks/US-001/review.md`, artefato do Revisor. O conteúdo estava correto, mas a S6 da Rodada 1, também corrigida, ficou sem marca, e o status das sugestões passou a ter duas fontes.
- **Por que importa:** o relatório deixa de ser registro independente do Revisor.
- **Correção (proposta):** o agente Executor não altera `tasks/US-XXX/review.md`; o status das sugestões fica no `plan.md` (§12/tarefa de correção) ou na rodada seguinte, escrita pelo Revisor. Arquivo: `.claude/agents/executor.md`.
- **Status:** Aplicada (commit f87b632)

### AP-012 — Build e hook não aplicam regras de estilo do .editorconfig

- **Data / US:** 2026-10-17 / US-001 (relato do Executor)
- **Quem errou:** executor (descobriu tarde) e infraestrutura de hooks
- **O que aconteceu:** `dotnet build` com 0 avisos e o hook pós-edição passaram, mas `dotnet format --verify-no-changes` (D4) falhou com IDE1006 (constantes privadas sem `_`). Corrigido em 03d87d5 e d21deae. As regras de estilo do `.editorconfig` só são aplicadas pelo `dotnet format`, não pelo build.
- **Por que importa:** o feedback rápido (hábito 5) não pega o erro; ele aparece só no fim, na DoD.
- **Correção (proposta):** ativar `EnforceCodeStyleInBuild` em `Directory.Build.props` (pode exigir ajustar severidades no `.editorconfig`) ou rodar `dotnet format --verify-no-changes` no hook `post-edit-dotnet`. Arquivos: `Directory.Build.props` ou `.claude/hooks/`; a escolha cabe ao usuário. Se for pelo build, atualizar a seção "Projeto e build" da skill `padroes-codigo`.
- **Status:** Aplicada (commit d07d1b9; opção escolhida pelo usuário: `dotnet format style --verify-no-changes` no hook. Ao aplicar, descobriu-se que o hook passava `--include` com caminho absoluto, ignorado em silêncio pelo `dotnet format`: o `format whitespace` nunca tinha rodado. Corrigido no mesmo commit)

### AP-013 — Teste escrito antes dos stubs gera erros intermediários no hook

- **Data / US:** 2026-10-17 / US-001 (relato do Executor)
- **Quem errou:** executor
- **O que aconteceu:** ao escrever o teste antes de criar os tipos e `using`s mínimos, o hook de compilação devolveu erros de compilação intermediários, que não são o Red desejado (falha de asserção).
- **Por que importa:** ruído no feedback e risco de confundir erro de compilação com Red.
- **Correção (proposta):** no ciclo Red, criar primeiro os esqueletos mínimos (tipos que lançam `NotImplementedException`, `using`s e arquivos) e só então o teste, para o Red falhar por asserção ou exceção prevista. Os commits de teste desta US já seguiram isso. Arquivo: `.claude/skills/tdd-dotnet/SKILL.md`.
- **Status:** Aplicada (commit 1bcb6ee)

### AP-014 — AP-009 recorreu mesmo com a regra escrita no agente

- **Data / US:** 2026-10-17 / US-002 (Rodada 1, S2; Rodada 2)
- **Quem errou:** executor
- **O que aconteceu:** as escolhas de forma (nome do tipo em `invalid corrections`, ponto final do detalhe em `invalid JSON`, marcador e seta nas tags) foram para a §12 do `plan.md` só no commit de evidências, depois dos `feat`, apesar da regra em `.claude/agents/executor.md:58` (aplicada em 5f4c9c4). Na rodada de correções (T9) a regra foi respeitada: o registro saiu em 053779d, antes do primeiro teste. Lá, o pedido do usuário já listava as mudanças.
- **Por que importa:** uma regra escrita não bastou; o momento de registrar não tem ponto de parada no fluxo. Quando a escolha surge no meio do Green, sem pedido prévio, o registro tardio se repete (hábito 4: intenção no documento antes do código).
- **Correção (proposta):** (a) `.claude/skills/tdd-dotnet/SKILL.md` (ciclo Green) ou `.claude/agents/executor.md`: passo explícito antes de cada commit `feat`/`fix`: "o Green decidiu algo que o plano deixava em aberto (texto, nome, formato)? Se sim, commit `docs(ai)` na §12 primeiro"; (b) `.claude/skills/checklist-revisao/SKILL.md` §1: item não obrigatório "escolhas de forma registradas na §12 têm commit anterior ao `feat` que as implementa".
- **Status:** Aplicada (2026-10-17; opção (a) em `.claude/agents/executor.md`, passo Green, e (b) em `checklist-revisao` §1)

### AP-015 — Asserção satisfeita pelo texto de uma biblioteca externa

- **Data / US:** 2026-10-17 / US-002 (Rodada 1, S1)
- **Quem errou:** planejador (mapeou o teste com `Contain("Expected ")`) e executor (reportou a fraqueza, mas só depois da revisão)
- **O que aconteceu:** `Parse_WhenJsonIsMalformed_RejectsWithInvalidJson` e `Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson` conferiam `Contain("Expected ")`, mas a `JsonException` do System.Text.Json também diz "Expected ...". O parser poderia montar `"invalid JSON: " + ex.Message`, sem o sufixo do catálogo, e os dois testes continuariam verdes. Corrigido na T9 (f3c8b36) com um trecho que só o catálogo produz (`Expected a single JSON object {"reply": string`) e o final `with no text around it.`.
- **Por que importa:** o teste passa por causa de um texto que o código do projeto não controla; a instrução de correção da RN-BRAIN04 perderia a descrição do objeto esperado sem aviso.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md` e checklist do Planejador/Revisor de plano (`.claude/agents/revisor.md`, modo revisão de plano): "quando a mensagem embute texto de biblioteca externa, o teste confere um trecho que só o código do projeto produz, e o plano já mapeia o teste assim na §4".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-016 — Comportamento de linha única escapa da cobertura de linhas

- **Data / US:** 2026-10-17 / US-002 (Rodada 1, S3)
- **Quem errou:** planejador (tabela de mensagens da §2 citava o corte e a normalização sem teste na §4)
- **O que aconteceu:** o corte do reply em 80 caracteres + `...` (ternário) e o `TrimEnd('.')` do detalhe da `JsonException` estavam a 100% de cobertura sem nenhum teste que os fixasse. Fixados na T9 (e8f5f00).
- **Por que importa:** a meta de cobertura de linhas não vê transformações de uma linha; a US-003 e a US-009 poderiam mexer no catálogo com regressão silenciosa.
- **Correção (proposta):** `.claude/agents/revisor.md` (checklist de revisão de plano) e `.claude/agents/planejador.md`: "cada transformação citada nas tabelas de mensagens da §2 (corte, normalização, limite) tem teste na §4, inclusive nos dois lados do limite, como já se exige para as respostas P*".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")
- **Ver também:** AP-054 (aplicada em 2026-10-22: um caso por ramo de `<placeholder>`)

### AP-017 — Red por mutação temporária para testes de caracterização

- **Data / US:** 2026-10-17 / US-002 (T9, S1 e S3; S6 da Rodada 2)
- **Quem errou:** — (procedimento que funcionou e não está escrito)
- **O que aconteceu:** a revisão pediu testes que fixam comportamento que o código já tem, então não existe Red natural. O Executor mostrou o Red por mutação temporária, não commitada, descrita na T9 com a mensagem de falha obtida (ex.: sem corte e sem `TrimEnd` → 2 falhas; `>=` no lugar de `>` → falha o teste dos 80 caracteres). O 80º caractere do reply de teste é `b`, o que distingue corte em 79 de corte em 80. Ligado ao AP-010 (o Red falha pelo motivo certo). A S6 acrescenta: o valor usado para conferir que um campo é preservado não deve coincidir com o de outro campo que o código poderia copiar por engano (no teste da S4, `RawText` igual ao `Reply`).
- **Por que importa:** sem o procedimento, o teste de caracterização pode nascer verde sem provar que discrimina.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`, seção Red: "teste de caracterização pedido em revisão: aplique uma mutação temporária que quebre o comportamento, mostre a falha pelo motivo certo, reverta (não commite) e descreva a mutação e a falha na tarefa do `plan.md`. Em testes de preservação de campo, use valores distintos entre campos".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-018 — P* sem "Teste: ..." mapeado no plano (recorrência do AP-004)

- **Data / US:** 2026-10-17 / US-002 (`plan-review.md`, rodadas 1 a 3: B1, B2, S11, S13)
- **Quem errou:** planejador
- **O que aconteceu:** a primeira versão do plano deixou P4, P5, P7 e P9 (normalização e comportamento de borda) sem teste na §4 (B1). Na revisão, só as P* citadas no bloqueante receberam "Teste: ..." (P2 e P3 ficaram de fora, S13). Padrões vizinhos da mesma revisão: (1) uma tarefa que cria um desvio (detecção JSON/tags) fazia o ramo da tarefa seguinte existir e o teste da T7 nascer verde (B2); resolvido dizendo que o ramo lança `NotImplementedException` até lá, e os testes da tarefa atual declaram a entrada que os mantém no ramo já implementado (S11); (2) a resposta da §12 no formato "sugestão → seções tocadas" permitiu conferir tudo numa rodada.
- **Por que importa:** o AP-004 já tinha motivado o item [O] do checklist; a regra existe só no revisor e o planejador reincide.
- **Correção (proposta):** `.claude/agents/planejador.md`: (a) na §9, **toda** P* termina com "Teste: `<Classe>.<Método>`" ou "Sem efeito observável", conferido antes de entregar; (b) na §5, quando uma tarefa cria um desvio cujo outro ramo pertence à tarefa seguinte, o plano diz o estado desse ramo; (c) respostas a revisão de plano na §12 seguem o formato "sugestão → seções tocadas". Opcional: espelhar (a) e (b) em `.claude/skills/tdd-dotnet/SKILL.md`.
- **Status:** Aplicada (2026-10-17; (a), (b) e (c) em `.claude/agents/planejador.md`; o espelho opcional na `tdd-dotnet` não foi feito)

### AP-019 — Forma única de checar caracteres invisíveis (LC_ALL=C)

- **Data / US:** 2026-10-17 / US-002 (`plan-review.md`, rodadas 1 a 3: S1 e aprendizado da Rodada 3)
- **Quem errou:** planejador (D11 inicial usava `grep -P '\xEF\xBB\xBF'`, que depende do locale) e skill `checklist-revisao` (forma diferente)
- **O que aconteceu:** o D11 inicial não detectava os bytes (no Git Bash, `\xEF` vira o código U+00EF, não o byte). Foi corrigido para `LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'`. O `checklist-revisao` §4 e o AP-007 usam o mesmo comando sem `LC_ALL=C`: hoje dão o mesmo resultado, mas são duas formas do mesmo comando.
- **Por que importa:** cada variante pode falhar em silêncio e dar `0` falso (AP-007).
- **Correção (proposta):** adotar uma forma só, com `LC_ALL=C`, em `.claude/skills/checklist-revisao/SKILL.md` §4, no AP-007 (nota de atualização) e em `.claude/agents/executor.md`; o Planejador copia dali na DoD, sem reescrever. Lembrar que `grep -c` sai com código 1 quando conta 0, resultado esperado.
- **Status:** Aplicada (2026-10-17; `checklist-revisao` §4, `executor.md`, `planejador.md` (DoD) e nota no AP-007)

### AP-020 — FluentAssertions 7: `Should().BeTrue()` não informa a análise de nulidade

- **Data / US:** 2026-10-17 / US-002 (relato do Executor)
- **Quem errou:** executor (descobriu pelo compilador)
- **O que aconteceu:** com `TreatWarningsAsErrors`, `result.IsValid.Should().BeTrue();` seguido de `result.Reply.Reply` gera CS8602, porque o `[MemberNotNullWhen]` de `IsValid` só vale em `if (result.IsValid)` ou `Debug.Assert`. Foi preciso `!` depois de verificar `IsValid` (ex.: `result.Reply!.IsRawFallback`).
- **Por que importa:** cada teste novo sobre `TutorReplyParseResult` (US-003 em diante) repete o tropeço e o Red vira erro de compilação (AP-013).
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`: "FluentAssertions 7 não propaga nulidade; depois de `result.IsValid.Should().BeTrue()`, acesse `result.Reply!` (o `!` é seguro porque a asserção anterior falha o teste). Não usar `if` no teste para contornar".
- **Status:** Aplicada (2026-10-17; `.claude/skills/tdd-dotnet/SKILL.md`, seção Organização)

### AP-021 — PowerShell 5.1: `git commit -m @'...'@` quebra os argumentos

- **Data / US:** 2026-10-17 / US-002 (relato do Executor)
- **Quem errou:** executor
- **O que aconteceu:** no Windows PowerShell 5.1, `git commit -m @'...'@` com aspas duplas dentro da mensagem divide os argumentos do `git`. O que funcionou foi o Bash com `git commit -F -` e heredoc.
- **Por que importa:** o commit sai com mensagem truncada ou falha, e o corpo/atribuição se perdem.
- **Correção (proposta):** `.claude/agents/executor.md` (seção Git) e skill `padroes-codigo` (seção Git): "mensagem de commit com várias linhas ou aspas: `git commit -F -` com heredoc no Bash; não usar here-string do PowerShell 5.1".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-022 — Adiamento em cadeia sem dono na spec

- **Data / US:** 2026-10-17 / US-003 (`plan-review.md`, Rodada 1 B1; Rodadas 2 e 3)
- **Quem errou:** planejador
- **O que aconteceu:** a US-001 adiou `/mode`, o aviso de linha em branco e `Conversation:MaxTurnsInWindow` para a US-003. A primeira versão do plano da US-003 entregou parte e adiou o resto para a US-004, cuja spec não citava nada disso (segundo adiamento sem dono). O Revisor do plano pegou como B1; o usuário escolheu registrar os itens na US-004 (4 cenários e D8 a D10), em commit anterior à T1.
- **Por que importa:** item adiado sem registro na spec de destino se perde ou vira escopo implícito; é mudança de spec disfarçada.
- **Correção (proposta):** (a) `.claude/agents/planejador.md`: todo item da §8 que diz "fica para a US-YYY" é registrado na spec da US-YYY (cenário ou DoD extra) num commit `docs(ai)` anterior à T1, com o texto exato no plano; (b) checklist de revisão de plano (`.claude/agents/revisor.md` ou `checklist-revisao`): o Revisor confere a spec da US de destino.
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-023 — Trecho de documento citado só pelo número da linha

- **Data / US:** 2026-10-17 / US-003 (`plan-review.md`, Rodada 1 S1, ligado ao AP-005; `review.md` Rodada 2 S6)
- **Quem errou:** planejador (plano) e executor (copiou linhas do Revisor sem conferir)
- **O que aconteceu:** o plano citou a faixa "55–58" da skill `bdd-para-testes` para substituição, deslocada em uma linha; a correção foi citar o texto antigo exato. O espelho ocorreu na revisão de código: o Executor copiou para a §12 números de linha da Rodada 1 do Revisor que estavam uma linha acima do real (S6).
- **Por que importa:** o número de linha envelhece a cada edição; quem aplica a mudança altera o trecho errado ou não o encontra.
- **Correção (proposta):** (a) `.claude/agents/planejador.md`, regra das alterações nos documentos-raiz: identificar o trecho pelo texto antigo (citação), com a linha só como apoio; (b) `checklist-revisao`, seção "Como escrever bloqueantes e sugestões": o Revisor identifica o trecho pelo símbolo (classe/método) além da linha; (c) `.claude/agents/executor.md`: conferir a linha no arquivo antes de copiá-la.
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-024 — Cenário ou DoD escrito para outra US sem os critérios já fixados

- **Data / US:** 2026-10-17 / US-003 (`plan-review.md`, Rodada 2 S9)
- **Quem errou:** planejador
- **O que aconteceu:** o cenário novo da US-004 "janela de turnos configurável" descrevia o encerramento por configuração inválida sem o código de saída, embora a US-001 já tivesse fixado o padrão (`ExitCodes.InvalidConfiguration`, diferente de 0). Corrigido a pedido do usuário.
- **Por que importa:** o cenário escrito para outra US vira a spec dela; sem os critérios observáveis, o teste dessa US pode deixar de conferir o que o projeto já decidiu.
- **Correção (proposta):** `.claude/agents/planejador.md`: ao escrever cenário ou DoD para outra US, reutilizar os critérios observáveis que o projeto já fixou para o mesmo tipo de comportamento (código de saída, texto da mensagem, canal de saída).
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-025 — Nome abreviado de teste nas tarefas da §5

- **Data / US:** 2026-10-17 / US-003 (`plan-review.md`, Rodada 2 S10)
- **Quem errou:** planejador
- **O que aconteceu:** as tarefas T7 e T8 citavam testes com prefixo omitido ou `...`, diferentes dos nomes da §4. O usuário pediu os nomes completos, idênticos aos da §4.
- **Por que importa:** Executor e Revisor não conseguem conferir por busca textual se a tarefa e o mapa da §4 falam do mesmo teste.
- **Correção (proposta):** (a) `.claude/agents/planejador.md`: nas tarefas da §5, os testes aparecem com o nome completo idêntico ao da §4, sem prefixo omitido nem `...`; (b) checklist de revisão de plano, item "Consistência interna": "nomes de teste da §5 idênticos aos da §4".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-026 — Reforço do AP-017: valores distintos por turno e por campo

- **Data / US:** 2026-10-17 / US-003 (`plan-review.md`, Rodada 1 S2)
- **Quem errou:** planejador (o plano inicial mapeava testes de janela e histórico com respostas iguais em todos os turnos)
- **O que aconteceu:** recorrência do AP-017. Em testes de janela e histórico, a asserção passa por coincidência se todo turno usa o mesmo texto, ou se a resposta rejeitada é igual à aceita. O plano passou a exigir `TutorReplies.Valid(label)` com `Reply`, `RawText` e `Correction` únicos por rótulo e `raw1` diferente da resposta que fica. A revisão de código confirmou que nenhuma asserção passou por coincidência.
- **Por que importa:** é a segunda US em que o problema aparece no plano, e a regra do AP-017 continua só como proposta.
- **Correção (proposta):** acrescentar ao texto já proposto no AP-017 (`.claude/skills/tdd-dotnet/SKILL.md`): "em testes de janela, histórico ou retentativa, cada turno tem aluno e professor com textos únicos, e a resposta rejeitada difere da aceita". Sem arquivo novo; aprovar junto com o AP-017.
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-027 — AP-014 recorre pela terceira vez

- **Data / US:** 2026-10-17 / US-003 (`review.md`, Rodada 1 S1 e Aprendizados; Rodada 2)
- **Quem errou:** executor
- **O que aconteceu:** as escolhas de forma E1 a E7 (nomes `ReplyRejectedRetrying`/`ReplyRejectedAgainUsingRawFallback`, guardas `ArgumentNullException`, `paramName: "kind"`, mensagem do `FakeTutorBrain` sem resposta) foram decididas no Red/Green e registradas na §12 só no commit de evidências (3156357), depois de todos os `feat`. O Executor reconheceu o desvio em E5. A regra do AP-014 (em `executor.md` e no `checklist-revisao` §1 como item não obrigatório) não bastou. Na rodada de correção (S2/S3), com o escopo definido no pedido do usuário, o registro saiu antes do código (3a09c34): a etapa funciona quando há ponto de parada.
- **Por que importa:** a intenção chega ao documento depois do código (hábito 4); o usuário só aceita os desvios depois de implementados.
- **Correção (proposta):** (a) `.claude/agents/executor.md` e o modelo da §5 do `.claude/agents/planejador.md`: cada tarefa fecha com a linha "Escolhas de forma: nenhuma | §12 em `<hash>`", preenchida antes do commit `feat`; (b) `.claude/skills/checklist-revisao/SKILL.md` §1: promover o item do AP-014 a [O] quando a escolha muda texto exibido, mensagem de exceção ou nome público; nos demais casos continua sugestão.
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-028 — Igualdade de `record` com coleção compara por referência

- **Data / US:** 2026-10-17 / US-003 (Executor E3, `c5cbc1a`; `review.md` Rodada 1)
- **Quem errou:** executor (descobriu pelo teste vermelho)
- **O que aconteceu:** `Add_WhenMoreTurnsThanMax_KeepsMostRecentInOrder` comparava `ConversationTurn` inteiros com `Equal`. Um `record` com membro `IReadOnlyList<T>` (aqui `Corrections`) compara a lista por referência, e cada `TutorReplies.Valid` cria uma lista nova; o teste falhava com a janela correta. Passou a comparar a sequência de `StudentMessage`.
- **Por que importa:** o Red vira falso e custa uma volta; vale para qualquer teste sobre `TutorReply`, `ConversationTurn` e outros DTOs com coleção.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`, seção Organização: "para comparar records com coleções, compare uma projeção (ex.: chave) ou use `BeEquivalentTo`; não use `Equal` sobre o record inteiro".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-029 — Mutação de Red que não compila (CA1805)

- **Data / US:** 2026-10-17 / US-003 (Executor E1, T4; `review.md` Rodada 1)
- **Quem errou:** planejador (prescreveu a mutação `= 0`) e executor (adaptou)
- **O que aconteceu:** para provar que o teste de caracterização do padrão 10 discrimina, o plano pediu a mutação temporária `= 0`, que não compila (CA1805: inicialização explícita com o valor padrão). O Executor removeu o inicializador, que dá o mesmo valor 0, e registrou a falha obtida.
- **Por que importa:** com `TreatWarningsAsErrors`, mutações "óbvias" podem ser barradas por analisadores; sem regra, o Executor perde tempo ou desiste da prova de Red. Complementa o AP-017.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`, junto do AP-017: "a mutação temporária pode ser qualquer forma equivalente que compile (ex.: remover o inicializador), registrada na tarefa com a falha obtida".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-030 — Ponto único de aplicação da §2.6 virou dois commits

- **Data / US:** 2026-10-17 / US-003 (`review.md` Rodada 1; `plan.md` §12)
- **Quem errou:** planejador e fluxo do `/us` (instruções divergentes)
- **O que aconteceu:** o plano e a pré-condição da §5 prometiam um único commit `docs(ai): update architecture and spec for US-003`. O orquestrador, seguindo o fluxo do `/us`, usou dois: `4b82f0a` (spec) e `f522128` (arquitetura e skill). Não houve dano, mas plano e fluxo dizem coisas diferentes.
- **Por que importa:** quando o plano promete algo que o fluxo não faz, o Revisor pode apontar falso desvio.
- **Correção (proposta):** alinhar `.claude/agents/planejador.md` ao fluxo do `/us`: o plano prevê dois commits (`docs(ai): update spec for US-XXX` e `docs(ai): update architecture for US-XXX`) antes da T1, em vez de um único. Alternativa: mudar o `/us` para um commit só; a escolha cabe ao usuário.
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31"; opção padrão: `planejador.md` alinhado ao `/us`, com dois commits antes da T1)

### AP-031 — Contagens atualizadas só no cabeçalho do plano

- **Data / US:** 2026-10-17 / US-003 (`review.md` Rodada 2, S4 e S5)
- **Quem errou:** executor
- **O que aconteceu:** depois do review S3 (+2 métodos, +3 casos), o Executor atualizou o cabeçalho da §4 e a DoD (D2/D5), mas não o detalhamento por classe: ele ainda dizia `Core.Tests: 34 métodos / 48 casos` e `BrainExceptionTests: 2 / 2`, sem `TutorReplyTests`, e a frase da suíte total misturava 148 e 151. Os números finais são 151 casos (Core 102, Console 25, Infrastructure 24). Também ficaram T12 e T13 marcadas `[x]` sem o "feito: `<hash>`" que T1 a T11 têm (S5).
- **Por que importa:** quem lê o detalhamento (o Documentador, o próximo Planejador) copia o número antigo.
- **Correção (proposta):** `.claude/agents/executor.md`, etapa de evidências: "mudou uma contagem → atualize o cabeçalho, o detalhamento por classe da §4 e D2/D5/D8, e confira se a soma bate"; e "tarefa marcada `[x]` leva o hash na própria linha".
- **Status:** Aplicada (2026-10-17, ver commit "docs(ai): apply learnings 15-17 and 21-31")

### AP-032 — Critério `LiveBrain` de "100%" sobre saída de LLM gera DoD intermitente

- **Data / US:** 2026-10-20 / US-004 (`review.md` Rodada 1 B1 e Rodada 2 S5; `plan.md` §11, §12 E3 a E6)
- **Quem errou:** planejador (spec e DoD) e revisor do plano (aprovou o critério)
- **O que aconteceu:** o D5 da US-004 exigia "5/5 no formato" medido no adaptador, sem a retentativa do ADR-006 que o produto tem. Com o modelo `haiku`, 5 a 20% das chamadas saíam fora do formato, e o D5 ficou vermelho de forma intermitente (2 verdes em 5 rodadas depois do primeiro reforço de prompt). O que resolveu foi mudar a regra (RN-FEED01, "exatamente uma pergunta") e reforçar o prompt, não o critério: 5 rodadas de 8 chamadas verdes, o que é compatível com uma taxa baixa, mas não a prova (risco residual registrado em `docs/us/US-004.md`).
- **Por que importa:** critério de 100% sobre saída de modelo mede sorte; a DoD vira intermitente e o fluxo trava no fim, depois de todo o código pronto.
- **Correção (proposta):** (a) `.claude/agents/planejador.md` e `.claude/skills/checklist-revisao/SKILL.md` §0 (revisão de plano): todo critério `LiveBrain` diz em que camada mede (adaptador ou serviço com retentativa), qual tolerância aceita e quantas rodadas seguidas precisa sobreviver (ex.: 3 a 5) antes da aprovação do plano; (b) se a tolerância for zero, o plano registra o risco de oscilação e quem decide o que fazer quando ficar vermelho (usuário, com registro prévio na spec).
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 32")

### AP-033 — Tipo de commit diferente do que o usuário decidiu

- **Data / US:** 2026-10-19 / US-004 (`review.md` Rodada 1 S1 e Rodada 2; `plan.md` §12 E3/E4)
- **Quem errou:** executor
- **O que aconteceu:** a decisão do usuário sobre o E3 pedia um commit `feat(brain)` ou `fix(brain)` para o reforço de prompt, mas a mudança entrou como `docs(ai)` (2cb0f7a). O Executor registrou o desvio no E4. Nas duas mudanças de prompt seguintes (`11514ab` e `f237c82`) o tipo fixado foi seguido, então o desvio não se repetiu. Parente do AP-014/AP-027 (forma decidida e não seguida), agora no tipo do commit.
- **Por que importa:** prompts mudam o comportamento do professor; o histórico com `docs(ai)` esconde mudança de comportamento e atrapalha changelog e bisect.
- **Correção (proposta):** `.claude/agents/executor.md`: "quando a §12 fixa tipo ou escopo de commit, use exatamente esse; para divergir, registre o motivo na §12 antes do commit". Arquivos em `prompts/` que mudam o comportamento do professor são `feat(brain)` ou `fix(brain)`, não `docs(ai)`.
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 33")

### AP-034 — Ambiente herdado pelo processo filho afeta privacidade e reprodutibilidade

- **Data / US:** 2026-10-19 / US-004 (`review.md` Rodada 1 S2/S3 e Rodada 2; `plan.md` §7, §8, §12 E3)
- **Quem errou:** planejador e executor (o ambiente do processo não foi tratado como decisão)
- **O que aconteceu:** o `ProcessRunner` deixa o processo `claude` herdar todo o ambiente de quem abre o console (`CLAUDECODE`, `CLAUDE_CODE_*`, `ANTHROPIC_BASE_URL`). Numa gravação feita a partir do app desktop, o nome do titular da conta chegou ao contexto do professor (a fixture foi descartada e gravada de novo). A evidência do D5/D7 também dependia do ambiente de quem rodava. O comando do D5 passou a limpar as variáveis. O usuário aceitou de forma explícita o risco de privacidade (plano §7 e §8), sem US de destino e sem ADR, com a condição de revisitar caso o app seja distribuído ou aberto em ambiente de terceiros.
- **Por que importa:** o mesmo código dá resultados e vazamentos diferentes conforme quem o abre; DoD com processo real sem ambiente descrito não é reproduzível.
- **Correção (proposta):** `.claude/skills/padroes-codigo/SKILL.md`, seção "Processos externos": "o `IProcessRunner` deixa explícito quais variáveis de ambiente o filho recebe; a DoD que usa processo real diz em que ambiente roda (o comando limpa o que for preciso)". Adotar a limpeza como regra do runner é decisão de ADR do usuário, não desta proposta.
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 34")

### AP-035 — Verificação de "nenhuma linha" sem o outro lado, e busca só do texto exato

- **Data / US:** 2026-10-19 / US-004 (`plan-review.md` Rodada 4 S2/S3 e Rodada 5)
- **Quem errou:** planejador
- **O que aconteceu:** ao mudar a RN-FEED01, o D16 (varredura do texto antigo) esperava "nenhuma linha", e só esse lado estava previsto: se o comando deixasse de varrer algum caminho ou tivesse aspas erradas, daria verde falso. A lista de ocorrências também cobria só o texto exato (`must end with a question`, `EndWith("?")`) e deixava de fora o nome antigo do cenário em comentário de teste e a forma `EndsWith('?')`. Corrigido com o controle D16c (o mesmo comando acha as linhas antigas antes da mudança, registrado em `c482f1e`) e com a busca das variações. Variação do AP-005 e do AP-016.
- **Por que importa:** verde sem o outro lado do limite não prova nada; texto antigo sobrevivente gera falso bloqueante.
- **Correção (proposta):** (a) `.claude/agents/planejador.md`: quando a US renomeia cenário ou regra, o Grep de "texto antigo" inclui o nome antigo do cenário, as variações sintáticas da asserção e uma evidência de que o comando de busca da DoD acha as ocorrências antes da mudança; (b) `.claude/skills/checklist-revisao/SKILL.md` §0: "verificação de 'nenhuma linha' na DoD tem o outro lado demonstrado".
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 35")

### AP-036 — Afirmação sobre outra US ou número da revisão usado sem conferir a fonte

- **Data / US:** 2026-10-17 / US-004 (`plan-review.md` Rodada 2 S1 e Rodada 3 S1)
- **Quem errou:** revisor do plano (um número, "a partir do turno N + 1", foi copiado para o ADR-007 sem conferir o código) e planejador (a justificativa da P17 citou o cenário de fallback da US-007 com os papéis invertidos: na spec, `ClaudeCode` é o provedor principal e `Ollama` o fallback)
- **O que aconteceu:** duas afirmações sem conferência da fonte. A primeira foi corrigida contra `ConversationWindow.Add`. A segunda a Rodada 3 apontou, e o Planejador manteve a decisão da P17, que não depende do argumento (resposta na §12 de 2026-10-17).
- **Por que importa:** o argumento errado entra em ADR e plano e leva a conclusões falsas para quem decide depois.
- **Correção (proposta):** (a) `.claude/agents/planejador.md`: "número vindo da revisão é conferido no código antes de ir para ADR ou spec"; "argumento que cita cenário ou regra de outra US cita o nome do cenário e confere o `Dado` no `SPRINT_N.md` antes de escrever"; (b) `.claude/skills/checklist-revisao/SKILL.md`: afirmação quantitativa sobre comportamento existente cita o símbolo que a sustenta (ex.: `ConversationWindow.Add`); §0 confere também as justificativas que citam outras US.
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 36")

### AP-037 — Mutação temporária revertida com `git checkout` apagou o Green não commitado

- **Data / US:** 2026-10-19 / US-004 (relato do usuário; o Executor aplicou mutações temporárias nas tarefas T2, T10, T16 e outras para dar Red a testes de caracterização)
- **Quem errou:** executor
- **O que aconteceu:** segundo o relato do usuário, ao desfazer uma mutação temporária com `git checkout -- <arquivo>`, o Executor apagou também o Green que ainda não estava commitado naquele arquivo e teve de refazê-lo. Os detalhes do incidente não estão em `plan.md`; a entrada registra só o que o usuário relatou.
- **Por que importa:** a técnica do AP-017 (Red por mutação temporária) pressupõe poder voltar ao estado anterior; desfazer pelo repositório destrói trabalho que ainda não existe nele.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`, junto do AP-017/AP-029: "antes de aplicar a mutação temporária, commite o Green (ou o par test/feat) ou desfaça a mutação com a edição inversa; nunca use `git checkout -- <arquivo>` ou `git restore` num arquivo com alterações não commitadas".
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 37")

### AP-038 — Comando `perl` para editar strings C# corrompe escapes e aspas

- **Data / US:** 2026-10-19 / US-004 (relato do usuário)
- **Quem errou:** executor
- **O que aconteceu:** segundo o relato do usuário, o Executor usou `perl` em linha de comando para alterar literais de string de código C#. Barras invertidas, `\uXXXX` e aspas passam por camadas de interpretação do shell e do perl e saem alterados. Os detalhes do incidente não estão em `plan.md`. Parente do AP-007 (escapes corrompidos por ferramentas de edição).
- **Por que importa:** a corrupção é silenciosa em literais e só aparece em teste ou no D15; o hook de formatação não a pega.
- **Correção (proposta):** `.claude/agents/executor.md` e `.claude/skills/tdd-dotnet/SKILL.md`: "literais de string com `\`, `\u`, aspas ou `%` são editados com a ferramenta de edição de arquivo, não com `perl`/`sed`/`awk` em linha de comando; se for inevitável, confira o `git diff` do trecho e rode o D15 antes de seguir".
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 38")

### AP-039 — `ValidateOnStart` com várias Options muda a exceção de partida; normalização sem teste no consumidor

- **Data / US:** 2026-10-17 / US-004 (`plan-review.md` Rodada 1 B1 e B4)
- **Quem errou:** planejador
- **O que aconteceu:** (B1) acrescentar `ClaudeCodeOptions` e `ConversationOptions` com `ValidateOnStart` faz duas seções inválidas ao mesmo tempo virarem uma `AggregateException`, que caía no erro inesperado (código 1) e regredia a US-001; nenhum teste existente invalidava duas seções. Corrigido com a P19 e a T17. (B4) o validador aceitava `OutputMode` em outra caixa e o consumidor usaria o valor cru, rodando no modo errado em silêncio; corrigido com o valor canônico no adaptador e teste com `"jsonschema"` e `"TAGS"`. Recorrência da S1 do `plan-review` da US-001.
- **Por que importa:** cada falha só aparece quando a combinação ou a caixa errada acontece, em produção.
- **Correção (proposta):** (a) `.claude/skills/padroes-codigo/SKILL.md`, "DI e configuração": "com mais de uma classe de Options validada na partida, a mensagem de configuração inválida cobre falhas simultâneas de seções diferentes (com teste)", e a lista do Planejador: "US que adiciona `ValidateOnStart` mapeia um teste com duas seções inválidas"; (b) checklist de revisão de plano: "valor aceito sem diferenciar maiúsculas pelo validador tem teste no consumidor com caixa diferente".
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 39")

### AP-040 — Adiamento escrito em P* e "limite aceito" sem listar os consumidores

- **Data / US:** 2026-10-17 / US-004 (`plan-review.md` Rodada 1 B2 e Rodada 4 S4)
- **Quem errou:** planejador
- **O que aconteceu:** (B2, recorrência do AP-022) o adiamento "a US-005 troca isso" ficou numa P*, e a conferência da §8 não o pegou; resolvido com um cenário novo na spec da US-005 antes da T1. (S4) uma limitação foi aceita com "o Core não valida isso", mas o mesmo componente (o separador de frases) passou a ser usado num critério da DoD com tolerância zero; resolvido com a lista de consumidores e o efeito em cada um (P22).
- **Por que importa:** adiamento sem dono some; limite aceito para um consumidor pode ser inaceitável para outro.
- **Correção (proposta):** `.claude/agents/planejador.md` e `.claude/skills/checklist-revisao/SKILL.md` §0: procurar adiamentos também nas P* e nos comentários de teste ("a US-YYY troca/atualiza"), não só na §8; todo "limite aceito" lista os consumidores do componente (validador, testes `LiveBrain`, DoD) e o efeito em cada um.
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 40")

### AP-041 — Tabela de decisão: cada linha precisa de um caso que só ela satisfaz

- **Data / US:** 2026-10-17 / US-004 (`plan-review.md` Rodada 1 B3/S5 e Rodada 2 S2/S3)
- **Quem errou:** planejador
- **O que aconteceu:** na tabela de `ClaudeCodeOutput.Read` (§2.5) as linhas se sobrepõem (código ≠ 0 e `is_error`), e um caso de teste podia cair numa linha anterior e deixar a seguinte sem cobertura. Depois, a linha 2 tinha condição composta ("não é JSON" ou "não é `type: result`") e o auxiliar de teste decidia em silêncio qual linha casava. Corrigido com casos como `other-type` e `json-array`, que usam um `structured_output` válido, e auxiliares que declaram `"type": "result"`. Variação do AP-016.
- **Por que importa:** o teste passa pela linha errada e a regra certa fica sem prova.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`: "em tabela de decisão, cada linha tem um caso que só ela satisfaz; condição composta tem um caso por parte; auxiliares de teste que montam a entrada declaram os campos que decidem a linha".
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 41")

### AP-042 — Rótulo de comentário alterado por substituição mecânica deixa de descrever o teste

- **Data / US:** 2026-10-20 / US-004 (`review.md` Rodada 2 S6; corrigido em `e220427`)
- **Quem errou:** executor
- **O que aconteceu:** ao trocar o nome antigo do cenário pelo novo (S2 da Rodada 4), o comentário de `Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed` ficou `Cenário: reply sem pergunta (apoio, P6)`, que não descreve o que o teste cobre. Sem efeito observável, só rótulo.
- **Por que importa:** comentário que mente confunde quem lê o mapa cenário → teste.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`: "ao trocar o nome de um cenário, releia cada comentário alterado contra o corpo do teste, não só o texto antigo".
- **Status:** Aplicada (2026-10-20, ver commit "docs(ai): apply learning 42")

### AP-043 — Versão de pacote deixada em aberto no plano só é registrada depois do commit

- **Data / US:** 2026-10-21 / US-005 (`review.md` Rodada 1 S1; relato do Executor, `plan.md` §12)
- **Quem errou:** planejador (deixou "última estável 10.x") e executor (registrou a versão `10.11.0` na §12 depois do commit `c545c9f`)
- **O que aconteceu:** o plano disse "última estável 10.x" para `Microsoft.Extensions.TimeProvider.Testing`. O Executor fixou `10.11.0` em `Directory.Packages.props` no commit do teste e só escreveu a escolha na §12 no commit de evidências. Desvio pequeno (pacote só de testes, Core intocado), mas é o padrão do AP-014/AP-027 aplicado a versão de pacote.
- **Por que importa:** escolha que o plano deixou em aberto chega ao documento depois do código (hábito 4); a versão muda o que o build restaura.
- **Correção (proposta):** (a) `.claude/agents/planejador.md`: a §6 (pacotes) fixa a versão, ou diz "o Executor registra a versão na §12 no mesmo commit"; (b) `.claude/agents/executor.md`: mudança de `Directory.Packages.props` com versão não prevista no plano entra na §12 antes ou no mesmo commit. Vale como regra só se o padrão se repetir (primeira ocorrência).
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 43")

### AP-044 — Hook de edição acusa build vermelho transitório quando um tipo novo é usado antes do arquivo que o declara

- **Data / US:** 2026-10-21 / US-005 (relato do Executor)
- **Quem errou:** executor (ordem de edição) e infraestrutura de hooks (feedback sem distinguir o transitório)
- **O que aconteceu:** ao editar primeiro o arquivo que usa um tipo novo (ex.: `ClaudeCodeFailures` ou um método novo do `FakeProcessRunner`) e só depois o que o declara, o hook `post-edit-dotnet` devolveu erro de compilação enquanto o par ainda estava incompleto. O erro some quando o segundo arquivo é salvo. Parente do AP-013 (erros de compilação intermediários no Red).
- **Por que importa:** ruído no feedback rápido; o agente pode tentar "corrigir" um erro que desaparece sozinho ou confundi-lo com o Red.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md` (ciclo Red, junto do AP-013): "crie ou edite primeiro o arquivo que declara o tipo ou membro novo (esqueleto), depois o que o usa; se um erro de símbolo inexistente aparecer logo após a primeira edição de um par, edite o outro arquivo antes de investigar". Alternativa a critério do usuário: o hook não bloqueia em erros de tipo ainda não declarado (CS0103, CS0246, CS1061).
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 44")

### AP-045 — Mutação do Red sem o que quebrar (esqueleto) ou escolhida antes da fixture existir

- **Data / US:** 2026-10-20 / US-005 (`plan-review.md` Rodada 1 S3 e Rodada 2 S2)
- **Quem errou:** planejador
- **O que aconteceu:** (S3) as mutações descritas para T1 e T4b só têm efeito quando a regra real já existe; no Red, o validador sem a faixa e o `Classify` que devolve sempre `Unknown` não têm o que quebrar. O plano passou a ter um parágrafo "Mutações depois do Green" na §5, com procedimento (commit do `feat`, mutação, falha, edição inversa, `git status --short`). (R2-S2) uma mutação escolhida antes de gravar a fixture `unknown-model` poderia não atingir a linha da tabela por onde a fixture realmente passa; resolvido com mutação que vale para as linhas 1 e 3 e com o registro da linha na T4a. Ambos funcionaram na execução (Evidências da §11). Complementa o AP-017, AP-029 e AP-037.
- **Por que importa:** o teste de caracterização pode nascer verde sem provar que discrimina.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`, junto do AP-017: "se o teste nasce verde porque o esqueleto já devolve o valor esperado, a mutação é aplicada depois do Green commitado, e a tarefa diz em que momento; a mutação sobre teste de fixture gravada é escolhida (ou confirmada) depois da gravação, para o ramo que a fixture realmente percorre".
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 45")

### AP-046 — Espera limitada só nos testes novos, e propriedade atribuída a um grupo sem conferir cada membro

- **Data / US:** 2026-10-20 / US-005 (`plan-review.md` Rodada 2 S3 e Rodada 3 S2)
- **Quem errou:** planejador
- **O que aconteceu:** a proteção contra travar a suíte (`WaitAsync(TimeSpan.FromSeconds(10))`) foi aplicada aos testes de tempo novos, mas não ao teste de caracterização `SendAsync_WhenCallerCancels_PropagatesCancellationInsteadOfTimeout`, cujo Red por mutação também trava (R2-S3). Depois, a §7 passou a dizer "os quatro testes que usam `HangsUntilCancelled`", mas a descrição de um deles na §4 não declarava a espera limitada (R3-S2). Ambos corrigidos antes da execução.
- **Por que importa:** fake que só termina por cancelamento trava a suíte inteira quando o Red ou a mutação falha; a §7 ou a §5 pode afirmar mais do que o mapa da §4 mostra.
- **Correção (proposta):** (a) `.claude/skills/tdd-dotnet/SKILL.md`: "todo teste com fake que só termina por cancelamento usa espera limitada, inclusive os de caracterização"; (b) `.claude/skills/checklist-revisao/SKILL.md` §0: "quando a §7 ou a §5 atribui uma propriedade a um grupo de testes ('os N testes que...'), a descrição de cada um na §4 a declara".
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 46")
- **Ver também:** AP-054 (aplicada em 2026-10-22: Red conferido asserção por asserção contra o `src`)

### AP-047 — Afirmação condicional da §2 sem teste; "as duas fontes" provadas só numa direção

- **Data / US:** 2026-10-20 / US-005 (`plan-review.md` Rodada 1 S1 e S2; Rodada 2 S1)
- **Quem errou:** planejador
- **O que aconteceu:** (R1-S2) a §2.2 dizia que o cancelamento do chamador propaga "mesmo que o tempo também tenha estourado", sem teste na §4; virou o teste `SendAsync_WhenCallerCancelsAfterTimeoutExpires_PropagatesCancellation`, com callback determinístico no fake. (R1-S1) todos os casos com padrão no stderr tinham `errorResult` nulo, então "examina as duas fontes" não estava provado; entrou o caso 11. (R2-S1) o caso 11 cobriu só uma direção; entrou o caso 12 (`result` casa, stderr presente não casa). Variação do AP-004/AP-018/AP-041.
- **Por que importa:** a implementação "olha o stderr só quando não há result de erro" (ou o inverso) passaria em todos os casos e contradiria a spec.
- **Correção (proposta):** `.claude/skills/checklist-revisao/SKILL.md` §0 e `.claude/agents/planejador.md`: "cada afirmação condicional da §2 ('mesmo que', 'exceto quando', 'só se') tem um teste na §4 ou sai do plano"; "quando o plano diz que duas fontes são sempre examinadas, há um caso em que só a fonte A casa com a B presente e sem casar, e outro com os papéis trocados". Pode ser fundida ao AP-041 se o usuário preferir.
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 47")

### AP-048 — Fixture gravada com argumentos diferentes dos do adaptador

- **Data / US:** 2026-10-20 / US-005 (`plan-review.md` Rodada 1 S4)
- **Quem errou:** planejador
- **O que aconteceu:** o comando de gravação das fixtures de erro não usava os argumentos de isolamento do adaptador (`--strict-mcp-config`, `--disallowedTools "mcp__*"`, `--system-prompt-file`). Com a configuração real do usuário, o stderr gravado poderia trazer avisos de MCP que o adaptador nunca veria. Corrigido: o comando usa os mesmos argumentos (conferidos em `BuildArguments`) e o `README.md` das fixtures registra as diferenças que sobram (modo Tags, prompt-modelo sem renderizar, sem `--resume`).
- **Por que importa:** fixture que não representa a chamada real dá teste verde com comportamento diferente em produção.
- **Correção (proposta):** `.claude/agents/planejador.md`: "comando de gravação de fixture usa os mesmos argumentos de isolamento do adaptador, ou o `README.md` da pasta de fixtures registra a diferença".
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 48")

### AP-049 — Justificativa de cobertura na §12 mais forte do que o mapa da §4

- **Data / US:** 2026-10-20 / US-005 (`plan-review.md` Rodada 3 S1)
- **Quem errou:** planejador
- **O que aconteceu:** a resposta a R2-S1 na §12 afirmou "cada padrão da linha b aparece nas duas fontes". O padrão `usage limit` só aparece no `result` de erro. O AP-041 não exige o cruzamento, então nada faltava no mapa, mas a frase podia levar uma revisão futura a dar como coberto o que não está. Corrigida na R3-S1. Variação do AP-036 aplicada ao próprio plano.
- **Por que importa:** afirmação de cobertura sem conferência vira falsa garantia.
- **Correção (proposta):** `.claude/agents/planejador.md`: "afirmação de cobertura na §12 ('cada X tem Y') é conferida contra a tabela da §4 antes do commit; se não fechar, é restringida ao que os casos provam".
- **Status:** Aplicada (2026-10-21, ver commit "docs(ai): apply learning 49")

### AP-050 — Inventário de renomeação feito pelos testes que mudam, não pelo nome antigo do cenário

- **Data / US:** 2026-10-22 / US-006 (`plan-review.md` Rodadas 3 e 4, B2 e S6)
- **Quem errou:** planejador
- **O que aconteceu:** ao relaxar a RN-FEED01 (cenário "reply com duas perguntas é inválido" deixou de existir), o inventário da tabela de conferência listou os testes que mudam de comportamento e esqueceu `ReplySentencesTests.cs`, cujo comentário só citava o nome antigo do cenário; o D20 ("nenhuma linha") ficaria vermelho. Corrigido refazendo o Grep pelo nome antigo (6 arquivos). Na rodada 4 a prosa dizia "aparece em 5 arquivos", número que envelheceu; foi trocada pela lista nominal.
- **Por que importa:** busca de "texto antigo" com inventário incompleto dá falsa garantia (variação de AP-035 e AP-040); contagem em prosa envelhece.
- **Correção (proposta):** `.claude/agents/planejador.md` e `.claude/skills/checklist-revisao/SKILL.md` §0: "ao renomear, remover ou substituir um cenário ou texto, o inventário vem do Grep pelo nome antigo (`src`, `tests`, `prompts`, `docs`), rodado antes de fechar a tabela de conferência, e é uma lista nominal de arquivos conferida pelo comando da DoD, não um número em prosa".
- **Status:** Aplicada (2026-10-22, ver commit "docs(ai): apply learning 050")

### AP-051 — Tolerância relaxada no verificador, mas não no teste `LiveBrain` que ainda assertava a regra original

- **Data / US:** 2026-10-22 / US-006 (`review.md` S1; risco apontado pelo Executor)
- **Quem errou:** planejador (P17 manteve `Be(1)` no gravador) e revisor do plano (não pediu a lista)
- **O que aconteceu:** a tolerância de 1 ou 2 perguntas entrou no validador do Core, mas o gravador `OllamaFixtureRecorderLiveTests` continuou exigindo exatamente 1 pergunta e roda no filtro do D5 (`FullyQualifiedName~Ollama`), chamando o adaptador sem a retentativa. Uma resposta tolerada de 2 perguntas deixaria o D5 vermelho sem regressão. O D5 ficou verde nas 3 rodadas (0 primeiras tentativas inválidas), então foi sorte, não garantia. Resolvido pelo usuário em `d614a4e`: a asserção saiu do gravador e fica só na fixture gravada.
- **Por que importa:** um teste que oscila por regra que o verificador já tolera torna o critério da DoD instável, fora da camada medida (AP-032, AP-040).
- **Correção (proposta):** `.claude/skills/checklist-revisao/SKILL.md` §0 e `.claude/skills/bdd-para-testes/SKILL.md`: "ao relaxar uma validação por tolerância, listar todos os testes `LiveBrain` que o filtro da DoD executa e que ainda assertam a regra original (inclusive gravadores de fixture); cada um tem decisão explícita no plano: manter e aceitar o risco, ou mover a asserção para a fixture gravada (determinística)".
- **Status:** Aplicada (2026-10-22, ver commit "docs(ai): apply learning 051")

### AP-052 — D5 `LiveBrain` verde sem exercitar o mecanismo que o critério diz medir

- **Data / US:** 2026-10-22 / US-006 (`review.md` S2 e S3)
- **Quem errou:** planejador (critério) e executor (leitura da evidência)
- **O que aconteceu:** o D5 foi redefinido para medir "o adaptador mais a retentativa única do ADR-006". Nas 3 rodadas nenhuma primeira tentativa foi inválida (`retries used: 0`), então o D5 verde mediu o adaptador com os prompts reforçados, não a retentativa. Do mesmo modo, a fumaça registrou 4/5 correções esperadas nas 3 rodadas sem margem, e o critério só exige o formato. Ambos ficaram registrados como limites em `docs/us/US-006.md`.
- **Por que importa:** a US seguinte pode herdar uma garantia que nunca foi medida ao vivo (a retentativa) e o `eval` da US-009 parte de um valor no limite (AP-032).
- **Correção (proposta):** `.claude/agents/planejador.md` e `.claude/skills/checklist-revisao/SKILL.md`: "o critério de um teste `LiveBrain` diz se o mecanismo medido precisa ter sido exercitado (ex.: pelo menos uma retentativa usada) ou se basta o teste unitário; quando só o unitário prova, a US registra isso como limite, e métricas só registradas (ex.: correções esperadas encontradas) entram como risco para a US que as usará como critério".
- **Status:** Aplicada (2026-10-22, ver commit "docs(ai): apply learning 052")

### AP-053 — Remover um tipo apagando a declaração antes dos usos gera erro de compilação transitório no hook

- **Data / US:** 2026-10-22 / US-006 (relato do Executor, remoção de `TutorNotConnectedInputHandler` e `ConsoleText.TutorNotConnected`)
- **Quem errou:** executor (ordem de edição)
- **O que aconteceu:** variação inversa do AP-044: ao apagar a classe e a constante antes de editar os usos, o hook `post-edit-dotnet` devolveu erro de símbolo inexistente até o último uso ser removido. Removendo os usos primeiro e a declaração por último, o hook não acusou erro intermediário.
- **Por que importa:** ruído no feedback rápido e risco de "corrigir" um erro que desaparece sozinho.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md` (junto do AP-044): "para remover um tipo ou membro, remova primeiro os usos e por último a declaração; para criar, declare primeiro e use depois".
- **Status:** Aplicada (2026-10-22, ver commit "docs(ai): apply learning 053")

### AP-054 — Plano cobre o caminho feliz de `<placeholder>` e de condicionais; Revisor cita nome de branch sem conferir

- **Data / US:** 2026-10-21 e 2026-10-22 / US-006 (`plan-review.md` Rodadas 1 a 4)
- **Quem errou:** planejador (casos por ramo; asserção acrescentada a teste "que nasce verde") e revisor (nome de branch)
- **O que aconteceu:** (a) a derivação de `<detalhe>` da mensagem de falha (primeira linha, texto de reserva) entrou no plano só com mensagens de uma linha; o B1 da rodada 1 exigiu um caso por ramo (confirma AP-015, AP-016 e AP-047); (b) o termo isolado de uma conjunção textual ("404 ou texto") ficou sem caso próprio; (c) na rodada 3, uma asserção foi acrescentada a um teste "que nasce verde" depois de desenhar a mensagem nova, e a lista de Red da T10 estava errada (variação de AP-046); (d) o Revisor citou nome de branch sem `git branch`. Todos corrigidos antes da execução.
- **Por que importa:** os casos felizes passam sem provar a regra; a lista de Red errada leva o Executor a achar que o teste está certo quando nasce verde.
- **Correção (proposta):** `.claude/agents/planejador.md`: "para cada `<placeholder>` derivado de texto de biblioteca ou de número, um caso por ramo da derivação (e a cultura, para números); ao fechar a lista de Red de uma tarefa, rodar mentalmente cada asserção contra o `src` atual"; `.claude/agents/revisor.md`: "confira nomes de branch com `git branch` antes de citá-los". Pode ser fundida ao AP-016 e AP-046 se o usuário preferir.
- **Status:** Aplicada (2026-10-22, fundida com AP-016 e AP-046, ver commit "docs(ai): apply learning 54")

### AP-055 — AP-014/AP-027 recorrem pela quarta vez: escolhas de forma registradas depois do `feat`

- **Data / US:** 2026-10-22 / US-007 (`review.md` S1 e Aprendizados)
- **Quem errou:** executor
- **O que aconteceu:** as escolhas de forma das T3, T4 e T5 (texto do log 2003, mensagem de `InvalidOperationException` da fábrica, mensagem de `ArgumentOutOfRangeException` de `ConsoleText.BrainSwitched`, posição do aviso depois do prompt `> `) foram registradas na §12 em `6594ede`, depois dos três `feat` (`d08b7c6`, `e673750`, `a712b33`). As linhas "Escolhas de forma" das tarefas foram preenchidas com "§12 em 6594ede (registrado depois do feat)": o campo criado pelo AP-027 virou registro tardio, e não ponto de parada. O Executor reconheceu o desvio na própria §12. Nenhuma escolha mudou comportamento da spec, então o Revisor não bloqueou.
- **Por que importa:** é a quarta ocorrência (AP-009, AP-014, AP-027). A regra escrita (no `executor.md`, no modelo da §5 e no `checklist-revisao` §1) não bastou, porque nada impede o commit `feat` com o campo preenchido depois. A intenção chega ao documento depois do código (hábito 4).
- **Correção (proposta, não aplicada; o usuário decidiu não aplicar as sugestões da revisão agora):** (a) `.claude/agents/executor.md`, passo Green: "antes do commit `feat`, rode `git log -1 --format=%H -- tasks/US-XXX/plan.md` e confirme que o commit `docs(ai)` da §12 com o texto de log, mensagem de exceção ou aviso novo é **anterior** ao `feat`; a linha 'Escolhas de forma' nunca cita um hash posterior ao `feat` da tarefa"; (b) `.claude/skills/checklist-revisao/SKILL.md` §1: tornar [O] (obrigatório) o item "escolha de forma que cria texto exibido ou mensagem de exceção tem commit na §12 anterior ao `feat`", em vez de sugestão; (c) avaliar um lembrete no hook `executor-stop-gate` quando o diff de uma tarefa cria literais de texto sem commit prévio em `tasks/US-XXX/plan.md`.
- **Status:** Proposta

### AP-056 — Evidência de cobertura do Core com número de um único projeto de teste

- **Data / US:** 2026-10-22 / US-007 (`review.md` S2 e Aprendizados)
- **Quem errou:** executor (evidência) e planejador (D3 não diz qual relatório vale)
- **O que aconteceu:** o D3 do `plan.md` registrou `line-rate` 0,9822 do Core no relatório do `Core.Tests`, anotando que os relatórios do `Infrastructure.Tests` e do `Console.Tests` mostram o Core parcial (0,7494 e 0,8181). O Revisor mesclou os três com `reportgenerator` e obteve 100% para o assembly Core (solução 90,5%). Os dois números passam de 80%, mas são grandezas diferentes, e a comparação entre US fica ambígua (a US-006 registrou 100%, a US-007 98,22%).
- **Por que importa:** cobertura do Core é critério do projeto (CLAUDE.md); número de fonte variável impede ver tendência e esconde queda real.
- **Correção (proposta, não aplicada):** `.claude/skills/tdd-dotnet/SKILL.md` e o modelo do D3 em `.claude/agents/planejador.md`: "a evidência do D3 é o relatório **mesclado** (`reportgenerator` sobre todos os `coverage.cobertura.xml`), com o line-rate do assembly `EnglishTutor.Core` e o total da solução; relatório de um único projeto de teste só entra como nota". Padronizar também no `docs/us/US-XXX.md` (seção Testes e evidências).
- **Status:** Proposta

### AP-057 — Teste procura "linha" exata de saída, mas o prompt `> ` não quebra linha

- **Data / US:** 2026-10-22 / US-007 (relato do Executor, `plan.md` §12 e DoD D8)
- **Quem errou:** planejador (redação da spec) e executor (descoberta tardia)
- **O que aconteceu:** a spec (D8/D9 da US-007) e os testes C2/C3 falam da "linha `Switched to ...`". Na saída padrão do console o prompt `> ` não é seguido de quebra de linha, então o aviso sai como `> Switched to Ollama (CLI not found)`, igual a `> Tutor: ...`. Os testes C2/C3 passaram a usar `EndsWith` e a DoD confere o trecho. O prefixo é do prompt, não do aviso, e a spec ficou com redação mais estreita do que o comportamento real.
- **Por que importa:** qualquer teste de componente ou comando de DoD que procure uma linha exata na saída do console falha por causa do prefixo; o erro aparece só na execução real (D8).
- **Correção (proposta, não aplicada):** (a) `.claude/skills/bdd-para-testes/SKILL.md`: "no console, o prompt `> ` não quebra linha: testes que procuram uma linha de saída consideram o prefixo (`EndsWith`/`Contains`), e cenários e DoD dizem 'a saída contém', não 'a linha'"; (b) `.claude/agents/planejador.md`: ao escrever critério de saída do console, usar 'a saída contém'. Para a US-007, a redação de D8 e D9 em `specs/sprints/SPRINT_1.md` ("a linha `Switched to`") pode ser ajustada para "a saída contém"; isso é spec e depende de decisão do usuário (o Documentador não altera a spec).
- **Status:** Proposta
