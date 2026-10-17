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
