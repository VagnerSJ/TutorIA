# ADR-003 — Desenvolvimento orientado a especificação com 4 agentes nativos do Claude Code

- **Status:** Aceito
- **Data:** 2026-10-17
- **US relacionadas:** todas

## Contexto

O projeto é um laboratório para o mesmo fluxo usado no trabalho: Planejador → Executor → Revisor → Documentador, com specs versionadas e aprovação humana. Queremos papéis com responsabilidades e permissões distintas, rastreabilidade de spec até teste, e documentação que não gere conflitos de merge.

## Decisão

- Subagentes em `.claude/agents/` com **ferramentas mínimas por papel**:

  | Agente | Ferramentas | Escreve em |
  |---|---|---|
  | planejador | Read, Grep, Glob, Write | `tasks/US-XXX/plan.md` |
  | executor | Read, Grep, Glob, Edit, Write, Bash, PowerShell | `src/`, `tests/`, commits na branch da US |
  | revisor | Read, Grep, Glob, Bash, PowerShell (só comandos de leitura, build e teste) | nada — o relatório é salvo pelo orquestrador |
  | documentador | Read, Grep, Glob, Write, Edit, Bash | `docs/`, `README.md`, `CHANGELOG.md`, status na sprint |

- Conhecimento compartilhado em skills pré-carregadas (`tdd-dotnet`, `bdd-para-testes`, `padroes-codigo`, `checklist-revisao`).
- Orquestração pela skill `/us US-XXX` na conversa principal, com **portão humano** obrigatório entre Planejador e Executor.
- Specs em `specs/`, planos e revisões em `tasks/US-XXX/`, documentação modular em `docs/us/US-XXX.md`.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Um único agente com instruções longas | Simples | Sem separação de papéis nem de permissões; revisão "de si mesmo" |
| Orquestração por script externo (`claude -p` encadeado) | Automatizável em CI | Perde o portão humano interativo; mais código para manter |
| Subagentes + skill orquestradora (escolhida) | Nativo, versionado, permissões por papel | Restrições de "só escrever em X" para planejador/documentador são por instrução, não por ferramenta |

## Consequências

- Rastreabilidade: cenário BDD → linha do plano → teste → item do review.
- Limitação: Write/Edit não podem ser restritos por caminho no frontmatter; o Revisor confere violações. Evolução possível: hook `PreToolUse` por agente bloqueando escrita fora das pastas permitidas.
