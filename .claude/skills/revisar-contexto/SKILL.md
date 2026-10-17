---
name: revisar-contexto
description: Verifica a saúde do contexto do projeto (CLAUDE.md, ARCHITECTURE.md, specs, agentes, skills, aprendizados) e propõe uma poda — regras obsoletas, duplicações, specs concluídas para arquivar, links quebrados. Só propõe; nada é alterado sem aprovação. Use no fim de cada sprint ou quando o contexto parecer inchado.
argument-hint: [sprint-N]
allowed-tools: Read Grep Glob Bash(git log *) Bash(git status *) Bash(wc *)
---

# Revisão e poda de contexto $ARGUMENTS

Objetivo: manter o contexto que os agentes leem **curto, atual e sem duplicação**. Este procedimento **não altera arquivos**; ele produz uma proposta.

## Verificações

| # | Verificação | Como |
|---|---|---|
| C1 | `CLAUDE.md` com até ~100 linhas | `wc -l CLAUDE.md` |
| C2 | `CLAUDE.md` aponta em vez de repetir | Trechos com mais de ~3 linhas iguais ou parafraseados de `ARCHITECTURE.md`, `specs/` ou skills |
| C3 | Links e caminhos válidos | Todo caminho citado em `CLAUDE.md`, `ARCHITECTURE.md`, agentes e skills existe (Glob) |
| C4 | `ARCHITECTURE.md` bate com o código | Pastas, projetos e interfaces citados existem em `src/`; tipos novos relevantes em `src/` aparecem no documento |
| C5 | Regras obsoletas | `RN-*` que nenhum teste, plano ou US cita mais; regras contraditas por ADR aceito |
| C6 | Specs concluídas | Sprints com todas as US "Concluída" → propor mover para `specs/archive/` (a documentação permanente fica em `docs/us/`) |
| C7 | ADRs | "Proposto" há mais de uma sprint sem decisão; ADRs substituídos sem o link "Substituído por" |
| C8 | Instruções duplicadas entre agentes e skills | Mesma regra escrita em vários agentes → mover para a skill compartilhada |
| C9 | Aprendizados pendentes | Entradas de `docs/aprendizados.md` com status "Proposta" ou "Aberta" há mais de uma sprint |
| C10 | Tamanho das skills | `SKILL.md` com mais de ~250 linhas → propor dividir em arquivos de referência |

## Saída

```markdown
# Proposta de poda de contexto — <sprint ou data>

| # | Verificação | Resultado |
|---|---|---|
| C1 | CLAUDE.md: NN linhas | ✅/⚠️ |
...

## Itens propostos
- [ ] **P1** — <arquivo> — <o que remover/mover/fundir> — motivo
- [ ] **P2** — ...

## Sem mudança
- ...
```

Cada item precisa ser pequeno e independente, para o usuário aprovar ou recusar um por um.
