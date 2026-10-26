# EnglishTutor — regras do projeto

Professor de inglês por voz, local (Whisper + TTS), com "cérebro" plugável (Claude Code headless ou Ollama).
Também é um laboratório de **Spec-Driven Development (SDD)** com 4 agentes.

> Meta: este arquivo fica com até ~100 linhas. Aponte para documentos; não repita conteúdo deles.

## Fontes da verdade (leia antes de agir)

- Arquitetura: [ARCHITECTURE.md](ARCHITECTURE.md). Em caso de conflito, ela vence.
- Produto e regras: [specs/PRODUCT.md](specs/PRODUCT.md), [specs/BUSINESS_RULES.md](specs/BUSINESS_RULES.md) (IDs `RN-*`).
- Histórias: `specs/sprints/SPRINT_N.md` (sprints concluídas ficam em `specs/archive/`; IDs `US-XXX`, cenários BDD + Definition of Done).
- Fases e ambiente: [specs/ROADMAP.md](specs/ROADMAP.md). Decisões: `docs/adr/`.
- Diário do processo: [docs/aprendizados.md](docs/aprendizados.md).

## Fluxo obrigatório (SDD)

Toda funcionalidade é conduzida por `/us US-XXX`:
**Planejador** → **Revisor** (revisão do plano) → aprovação do usuário → **Executor** (TDD) → **Revisor** → correção dos bloqueantes → **Documentador**.

- Nada de código de produção sem US e sem `tasks/US-XXX/plan.md` aprovado.
- Cada cenário BDD vira pelo menos um teste escrito **antes** do código (Red → Green → Refactor).
- Cobertura de linhas do `EnglishTutor.Core` ≥ 80%.

## Hábitos de trabalho com IA

1. **Contexto escrito e podado.** Contexto mora em arquivos versionados, não em conversa. Ao fim de cada sprint o Documentador roda `/revisar-contexto` e propõe uma poda (regras obsoletas, specs concluídas → `specs/archive/`, duplicações). Nada é podado sem aprovação do usuário.
2. **Devagar no começo.** Fase 0 e primeiras US são pequenas de propósito. Erro recorrente de agente → registro em `docs/aprendizados.md` + regra ou skill nova que o evita.
3. **Critério de validação junto com a tarefa.** Toda US tem cenários BDD **e** Definition of Done com comandos verificáveis. O Executor roda os comandos ele mesmo, cola o resultado na seção "Evidências" do `plan.md` e só declara a tarefa concluída com tudo verde.
4. **Intenção no documento, não no código.** Mudança de comportamento começa na spec (US / `BUSINESS_RULES.md`) e no `plan.md`. Decisão técnica relevante vira ADR (status Proposto) **antes** da implementação. Comentários do usuário são feitos no `plan.md` (seção "Comentários do usuário"), não como correções soltas no código.
5. **Feedback rápido.** Testes unitários em segundos, sem rede e sem GPU (fakes e fixtures); testes com modelo real só em `Category=LiveBrain`. Hooks em `.claude/settings.json` formatam, compilam e rodam os testes afetados após cada edição e devolvem o erro ao agente. Mensagens de erro do parser e das validações dizem o que está errado e o que se espera.

## Regras rápidas

- .NET 10 / C# com `Nullable` e `TreatWarningsAsErrors`; testes com xUnit + FluentAssertions + NSubstitute.
- `EnglishTutor.Core` não referencia pacotes externos (só BCL, `Microsoft.Extensions.*.Abstractions` e `Microsoft.Extensions.Options`).
- `CancellationToken` em todo método assíncrono público.
- Commits convencionais com escopo: `feat(core):`, `feat(brain):`, `test(core):`, `docs(ai):`, `chore(infra):`.
- Branches: `main` (só o que já foi testado) ← `develop` (tudo o que já foi desenvolvido) ← `feature/US-XXX_slug` (ex.: `feature/US-001_fundacao-solucao`), criada a partir de `develop`. US pronta → merge `--no-ff` da feature em `develop`. `develop` → `main` só quando o usuário pedir.
- Código, nomes e mensagens de commit em inglês; documentação e specs em português.
- Nunca commitar modelos, bancos SQLite, áudios ou `appsettings.*.local.json`.
- **Economia de contexto (vale para todos os agentes e para o orquestrador):** nunca cole saída bruta de build, testes, cobertura ou execução. Mostre só contagens (aprovados/falhos/ignorados), o nome e a mensagem curta de cada falha e o número de avisos. Em listas longas, mostre as 10 primeiras linhas e a contagem total. Subagentes devolvem resumo curto (tabela da DoD, commits, desvios), sem repetir o que já está em `plan.md`/`review.md`. Releia arquivo só se ele mudou. O resultado completo fica na seção "Evidências" do `plan.md`, resumido.

## Comandos

```bash
dotnet build
dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice"
dotnet test --filter "Category!=LiveBrain&Category!=LiveVoice" --collect:"XPlat Code Coverage"
dotnet run --project src/EnglishTutor.Console
```

Hooks: `.claude/hooks/` (desligar os testes por edição com `ENGLISHTUTOR_HOOK_TESTS=0`).
