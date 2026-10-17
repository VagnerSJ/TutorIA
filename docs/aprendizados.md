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
- **Status:** Proposta

### AP-015 — Asserção satisfeita pelo texto de uma biblioteca externa

- **Data / US:** 2026-10-17 / US-002 (Rodada 1, S1)
- **Quem errou:** planejador (mapeou o teste com `Contain("Expected ")`) e executor (reportou a fraqueza, mas só depois da revisão)
- **O que aconteceu:** `Parse_WhenJsonIsMalformed_RejectsWithInvalidJson` e `Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson` conferiam `Contain("Expected ")`, mas a `JsonException` do System.Text.Json também diz "Expected ...". O parser poderia montar `"invalid JSON: " + ex.Message`, sem o sufixo do catálogo, e os dois testes continuariam verdes. Corrigido na T9 (f3c8b36) com um trecho que só o catálogo produz (`Expected a single JSON object {"reply": string`) e o final `with no text around it.`.
- **Por que importa:** o teste passa por causa de um texto que o código do projeto não controla; a instrução de correção da RN-BRAIN04 perderia a descrição do objeto esperado sem aviso.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md` e checklist do Planejador/Revisor de plano (`.claude/agents/revisor.md`, modo revisão de plano): "quando a mensagem embute texto de biblioteca externa, o teste confere um trecho que só o código do projeto produz, e o plano já mapeia o teste assim na §4".
- **Status:** Proposta

### AP-016 — Comportamento de linha única escapa da cobertura de linhas

- **Data / US:** 2026-10-17 / US-002 (Rodada 1, S3)
- **Quem errou:** planejador (tabela de mensagens da §2 citava o corte e a normalização sem teste na §4)
- **O que aconteceu:** o corte do reply em 80 caracteres + `...` (ternário) e o `TrimEnd('.')` do detalhe da `JsonException` estavam a 100% de cobertura sem nenhum teste que os fixasse. Fixados na T9 (e8f5f00).
- **Por que importa:** a meta de cobertura de linhas não vê transformações de uma linha; a US-003 e a US-009 poderiam mexer no catálogo com regressão silenciosa.
- **Correção (proposta):** `.claude/agents/revisor.md` (checklist de revisão de plano) e `.claude/agents/planejador.md`: "cada transformação citada nas tabelas de mensagens da §2 (corte, normalização, limite) tem teste na §4, inclusive nos dois lados do limite, como já se exige para as respostas P*".
- **Status:** Proposta

### AP-017 — Red por mutação temporária para testes de caracterização

- **Data / US:** 2026-10-17 / US-002 (T9, S1 e S3; S6 da Rodada 2)
- **Quem errou:** — (procedimento que funcionou e não está escrito)
- **O que aconteceu:** a revisão pediu testes que fixam comportamento que o código já tem, então não existe Red natural. O Executor mostrou o Red por mutação temporária, não commitada, descrita na T9 com a mensagem de falha obtida (ex.: sem corte e sem `TrimEnd` → 2 falhas; `>=` no lugar de `>` → falha o teste dos 80 caracteres). O 80º caractere do reply de teste é `b`, o que distingue corte em 79 de corte em 80. Ligado ao AP-010 (o Red falha pelo motivo certo). A S6 acrescenta: o valor usado para conferir que um campo é preservado não deve coincidir com o de outro campo que o código poderia copiar por engano (no teste da S4, `RawText` igual ao `Reply`).
- **Por que importa:** sem o procedimento, o teste de caracterização pode nascer verde sem provar que discrimina.
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`, seção Red: "teste de caracterização pedido em revisão: aplique uma mutação temporária que quebre o comportamento, mostre a falha pelo motivo certo, reverta (não commite) e descreva a mutação e a falha na tarefa do `plan.md`. Em testes de preservação de campo, use valores distintos entre campos".
- **Status:** Proposta

### AP-018 — P* sem "Teste: ..." mapeado no plano (recorrência do AP-004)

- **Data / US:** 2026-10-17 / US-002 (`plan-review.md`, rodadas 1 a 3: B1, B2, S11, S13)
- **Quem errou:** planejador
- **O que aconteceu:** a primeira versão do plano deixou P4, P5, P7 e P9 (normalização e comportamento de borda) sem teste na §4 (B1). Na revisão, só as P* citadas no bloqueante receberam "Teste: ..." (P2 e P3 ficaram de fora, S13). Padrões vizinhos da mesma revisão: (1) uma tarefa que cria um desvio (detecção JSON/tags) fazia o ramo da tarefa seguinte existir e o teste da T7 nascer verde (B2); resolvido dizendo que o ramo lança `NotImplementedException` até lá, e os testes da tarefa atual declaram a entrada que os mantém no ramo já implementado (S11); (2) a resposta da §12 no formato "sugestão → seções tocadas" permitiu conferir tudo numa rodada.
- **Por que importa:** o AP-004 já tinha motivado o item [O] do checklist; a regra existe só no revisor e o planejador reincide.
- **Correção (proposta):** `.claude/agents/planejador.md`: (a) na §9, **toda** P* termina com "Teste: `<Classe>.<Método>`" ou "Sem efeito observável", conferido antes de entregar; (b) na §5, quando uma tarefa cria um desvio cujo outro ramo pertence à tarefa seguinte, o plano diz o estado desse ramo; (c) respostas a revisão de plano na §12 seguem o formato "sugestão → seções tocadas". Opcional: espelhar (a) e (b) em `.claude/skills/tdd-dotnet/SKILL.md`.
- **Status:** Proposta

### AP-019 — Forma única de checar caracteres invisíveis (LC_ALL=C)

- **Data / US:** 2026-10-17 / US-002 (`plan-review.md`, rodadas 1 a 3: S1 e aprendizado da Rodada 3)
- **Quem errou:** planejador (D11 inicial usava `grep -P '\xEF\xBB\xBF'`, que depende do locale) e skill `checklist-revisao` (forma diferente)
- **O que aconteceu:** o D11 inicial não detectava os bytes (no Git Bash, `\xEF` vira o código U+00EF, não o byte). Foi corrigido para `LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'`. O `checklist-revisao` §4 e o AP-007 usam o mesmo comando sem `LC_ALL=C`: hoje dão o mesmo resultado, mas são duas formas do mesmo comando.
- **Por que importa:** cada variante pode falhar em silêncio e dar `0` falso (AP-007).
- **Correção (proposta):** adotar uma forma só, com `LC_ALL=C`, em `.claude/skills/checklist-revisao/SKILL.md` §4, no AP-007 (nota de atualização) e em `.claude/agents/executor.md`; o Planejador copia dali na DoD, sem reescrever. Lembrar que `grep -c` sai com código 1 quando conta 0, resultado esperado.
- **Status:** Proposta

### AP-020 — FluentAssertions 7: `Should().BeTrue()` não informa a análise de nulidade

- **Data / US:** 2026-10-17 / US-002 (relato do Executor)
- **Quem errou:** executor (descobriu pelo compilador)
- **O que aconteceu:** com `TreatWarningsAsErrors`, `result.IsValid.Should().BeTrue();` seguido de `result.Reply.Reply` gera CS8602, porque o `[MemberNotNullWhen]` de `IsValid` só vale em `if (result.IsValid)` ou `Debug.Assert`. Foi preciso `!` depois de verificar `IsValid` (ex.: `result.Reply!.IsRawFallback`).
- **Por que importa:** cada teste novo sobre `TutorReplyParseResult` (US-003 em diante) repete o tropeço e o Red vira erro de compilação (AP-013).
- **Correção (proposta):** `.claude/skills/tdd-dotnet/SKILL.md`: "FluentAssertions 7 não propaga nulidade; depois de `result.IsValid.Should().BeTrue()`, acesse `result.Reply!` (o `!` é seguro porque a asserção anterior falha o teste). Não usar `if` no teste para contornar".
- **Status:** Proposta

### AP-021 — PowerShell 5.1: `git commit -m @'...'@` quebra os argumentos

- **Data / US:** 2026-10-17 / US-002 (relato do Executor)
- **Quem errou:** executor
- **O que aconteceu:** no Windows PowerShell 5.1, `git commit -m @'...'@` com aspas duplas dentro da mensagem divide os argumentos do `git`. O que funcionou foi o Bash com `git commit -F -` e heredoc.
- **Por que importa:** o commit sai com mensagem truncada ou falha, e o corpo/atribuição se perdem.
- **Correção (proposta):** `.claude/agents/executor.md` (seção Git) e skill `padroes-codigo` (seção Git): "mensagem de commit com várias linhas ou aspas: `git commit -F -` com heredoc no Bash; não usar here-string do PowerShell 5.1".
- **Status:** Proposta
