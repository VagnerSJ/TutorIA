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
- **Status:** Proposta (aguardando aprovação do usuário)
