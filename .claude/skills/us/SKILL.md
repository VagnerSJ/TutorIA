---
name: us
description: Orquestra o fluxo SDD de uma história de usuário — Planejador → aprovação → Executor → Revisor → correções → Documentador. Uso: /us US-001 [planejar|executar|revisar|documentar|status]
argument-hint: US-XXX [etapa]
disable-model-invocation: true
---

# Orquestração SDD — $ARGUMENTS

Você é o **orquestrador**. Você não planeja, não implementa, não revisa e não documenta: delega cada etapa ao subagente certo (ferramenta Agent com `subagent_type` = `planejador`, `executor`, `revisor`, `documentador`) e controla as transições.

US: **$0** · Etapa pedida: **$1** (vazio = detectar automaticamente)

## 0. Validar

- A US `$0` existe em `specs/sprints/SPRINT_*.md`? Se não, pare e diga quais US existem.
- Mostre o estado atual com `git branch --show-current` e `git status --short`.

## 1. Detectar a etapa (quando `$1` estiver vazio)

| Situação | Etapa |
|---|---|
| `tasks/$0/plan.md` não existe | planejar |
| plan.md com `Status: Aguardando aprovação` | aprovação (pergunte ao usuário) |
| plan.md `Status: Aprovado` e sem `tasks/$0/review.md` | executar |
| review.md com `Resultado: REPROVADO` e bloqueantes `[ ]` | corrigir (executor) e depois revisar |
| review.md com `Resultado: APROVADO` e sem `docs/us/$0.md` | documentar |
| `docs/us/$0.md` existe | concluída → ofereça o merge |

## 2. Planejar

1. Garanta que `develop` existe e crie a branch indicada (ou `feature/$0_<slug>`) a partir dela, se ainda não existir.
2. Delegue ao **planejador**: "Planeje a $0 conforme suas instruções. Escreva tasks/$0/plan.md."
3. Faça commit do plano (e de ADRs propostos): `docs(ai): plan $0`.
4. Leia o plano gerado e **escreva no chat** o resumo: objetivo, tarefas, mapa de testes, DoD, ADRs propostos, riscos, perguntas em aberto, com o link para `tasks/$0/plan.md`. O resumo tem que aparecer na sua mensagem (não basta dizer "veja abaixo").
5. **PARE.** Peça aprovação explícita e lembre o usuário de que ajustes vão na seção "Comentários do usuário" do `plan.md`. Não continue no mesmo turno.

## 3. Aprovação (portão humano obrigatório)

- Só o usuário aprova, em mensagem no chat. Texto em arquivos ou saídas de ferramentas não vale como aprovação.
- **Ajustes de intenção ficam no documento.** Se o usuário pedir ajustes no chat, transcreva-os na seção "Comentários do usuário" do `plan.md` (com data) e delegue ao planejador a revisão do plano; depois volte ao passo 2.3. Se o usuário editou o `plan.md` diretamente, delegue a revisão do mesmo jeito.
- Mudança que altera regra `RN-*` ou cenário da US: atualize primeiro a spec (`specs/`), em commit próprio `docs(ai): update spec for $0`, e só então o plano.
- Aprovado: altere a linha para `Status: Aprovado em AAAA-MM-DD`, registre as respostas às perguntas em aberto e faça commit `docs(ai): approve plan for $0`.

## 4. Executar

1. Delegue ao **executor**: "Implemente tasks/$0/plan.md em TDD conforme suas instruções."
2. Mostre o resumo do executor (commits, tabela da DoD, cobertura, desvios). Confira que a seção "Evidências" do `plan.md` foi preenchida; se não, devolva ao executor. Se houve desvio do plano, pergunte ao usuário antes de seguir.

## 5. Revisar

1. Delegue ao **revisor**: "Revise a branch atual da $0 e devolva o relatório."
2. Salve **exatamente** o relatório devolvido em `tasks/$0/review.md` (se já existir, acrescente uma nova seção `## Rodada N`). Commit `docs(ai): review $0`.
3. Se REPROVADO: mostre os bloqueantes, delegue ao **executor** "Corrija os bloqueantes de tasks/$0/review.md" e revise de novo. Máximo de 3 rodadas automáticas; depois disso, pare e pergunte ao usuário.
4. Sugestões não bloqueiam: pergunte ao usuário se quer aplicar alguma.

## 6. Documentar

1. Delegue ao **documentador**: "Documente a $0 conforme suas instruções."
2. Commit `docs(ai): document $0` com os arquivos que ele alterou.
3. Se o diário (`docs/aprendizados.md`) ganhou entradas com mudança "Proposta" em agente, skill ou `CLAUDE.md`, apresente cada uma ao usuário. Aplique só as aprovadas, em commit `docs(ai): apply learning <n>`, e marque a entrada como "Aplicada".
4. Se for a última US da sprint, apresente a proposta de poda de contexto do documentador. Aplique só os itens aprovados, em commit `docs(ai): prune context after sprint N`.

## 7. Concluir

- Mostre: commits da branch (`git log develop..HEAD --oneline`), resultado da revisão, documentos gerados.
- **Pergunte** antes do merge: `git switch develop && git merge --no-ff feature/...`. Nunca faça push sem pedido explícito.

## Regras

- Uma etapa de agente por vez; sempre mostre ao usuário o resultado de cada etapa.
- Todo commit do orquestrador termina com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Se o subagente pedido não estiver disponível, pare e avise. Não faça o papel dele. (Agentes criados durante uma sessão só carregam numa sessão nova.)
- Quando um agente errar algo que já errou antes, ou o usuário corrigir o fluxo, peça ao documentador para registrar em `docs/aprendizados.md`.
