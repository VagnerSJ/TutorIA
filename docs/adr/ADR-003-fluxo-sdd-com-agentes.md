# ADR-003 — Desenvolvimento orientado a especificação com 4 agentes nativos do Claude Code

- **Status:** Aceito
- **Data:** 2026-10-17 (revisado no mesmo dia para incorporar os hábitos de trabalho com IA)
- **US relacionadas:** todas

## Contexto

O projeto é um laboratório para o mesmo fluxo usado no trabalho: Planejador → Executor → Revisor → Documentador, com specs versionadas e aprovação humana. Queremos papéis com responsabilidades e permissões distintas, rastreabilidade de spec até teste, documentação que não gere conflitos de merge, e cinco hábitos de trabalho com IA embutidos na estrutura (não só na intenção): contexto escrito e podado, começo devagar, critério de validação junto com a tarefa, intenção no documento e feedback rápido.

## Decisão

- Subagentes em `.claude/agents/` com **ferramentas mínimas por papel**:

  | Agente | Ferramentas | Escreve em |
  |---|---|---|
  | planejador | Read, Grep, Glob, Write | `tasks/US-XXX/plan.md` e ADRs novos com status Proposto |
  | executor | Read, Grep, Glob, Edit, Write, Bash, PowerShell | `src/`, `tests/`, Evidências no `plan.md`, commits na branch da US |
  | revisor | Read, Grep, Glob, Bash, PowerShell (só comandos de leitura, build, teste, `dotnet format --verify-no-changes` e `reportgenerator` fora do repositório) | nada — o relatório é salvo pelo orquestrador |
  | documentador | Read, Grep, Glob, Write, Edit, Bash | `docs/` (inclui `aprendizados.md`), `README.md`, `CHANGELOG.md`, status na sprint |

- Conhecimento compartilhado em skills pré-carregadas (`tdd-dotnet`, `bdd-para-testes`, `padroes-codigo`, `checklist-revisao`, `revisar-contexto`).
- Orquestração pela skill `/us US-XXX` na conversa principal, com **portão humano** obrigatório entre Planejador e Executor.
- **Revisão do plano** (acrescentada em 2026-10-17, AP-004): antes do portão humano, o revisor avalia o `plan.md` (rastreabilidade BDD → teste, P* com efeito observável testado, DoD verificável, conformidade com `RN-*`/`ARCHITECTURE.md`); relatório em `tasks/US-XXX/plan-review.md`, até 3 rodadas automáticas com o planejador. Na US-001 a etapa gerou 14 sugestões incorporadas antes do código.
- Specs em `specs/` (concluídas → `specs/archive/`), planos e revisões em `tasks/US-XXX/`, documentação modular em `docs/us/US-XXX.md`.

### Como cada hábito virou estrutura

| Hábito | Mecanismo |
|---|---|
| 1. Contexto escrito e podado | `CLAUDE.md` ≤ ~100 linhas apontando para documentos; skill `/revisar-contexto`; poda proposta pelo documentador no fim de cada sprint, aprovada item a item |
| 2. Devagar no começo | Fase 0 e US iniciais pequenas; `docs/aprendizados.md` (erro → regra/skill), alimentado pelo revisor, executor e documentador |
| 3. Validação junto com a tarefa | DoD com comandos em cada US e no `plan.md`; executor cola as Evidências; revisor confere rodando os mesmos comandos |
| 4. Intenção no documento | Spec/plano antes do código; ADR "Proposto" criado pelo planejador antes da implementação; ajustes do usuário na seção "Comentários do usuário" do `plan.md` |
| 5. Feedback rápido | Suíte unitária em segundos com fakes/fixtures; `LiveBrain` separado; hooks `PostToolUse` (format + build + testes afetados) e `SubagentStop` do executor (portão de DoD) |

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Um único agente com instruções longas | Simples | Sem separação de papéis nem de permissões; revisão "de si mesmo" |
| Orquestração por script externo (`claude -p` encadeado) | Automatizável em CI | Perde o portão humano interativo; mais código para manter |
| Subagentes + skill orquestradora (escolhida) | Nativo, versionado, permissões por papel | Restrições de "só escrever em X" para planejador/documentador são por instrução, não por ferramenta |

## Consequências

- Rastreabilidade: cenário BDD → linha do plano → item do plan-review → teste → evidência → item do review.
- Custo: o hook por edição adiciona alguns segundos a cada alteração em `.cs` (desligável com `ENGLISHTUTOR_HOOK_TESTS=0`).
- Limitação: Write/Edit não podem ser restritos por caminho no frontmatter; o revisor confere violações. Evolução possível: hook `PreToolUse` por agente bloqueando escrita fora das pastas permitidas.
- Hooks de projeto só rodam depois que a pasta é marcada como confiável no Claude Code; agentes novos só carregam numa sessão nova (AP-002).
